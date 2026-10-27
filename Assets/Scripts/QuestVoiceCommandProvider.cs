using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using Meta.WitAi;
using Meta.WitAi.Data.Configuration;
using Meta.WitAi.Json;
using Oculus.Voice;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

[DefaultExecutionOrder(30), DisallowMultipleComponent]
public sealed class QuestVoiceCommandProvider : MonoBehaviour, IInteractionSpeechProvider
{
    public SpatialVoiceIntentController controller;
    public WitConfiguration configuration;
    public AppVoiceExperience voiceService;
    [Tooltip("Optional only: use the existing select_object intent when a final response has no transcription.")]
    public bool allowIntentFallback;
    [Range(0,1)] public float minimumIntentConfidence = .6f;
    public string RecognizerStatus { get; private set; } = "Quest voice: initializing";
    public int DroppedCallbackEvents => dropped;
    public string LastRecognizedCommand { get; private set; } = "None";
    public string LastSafeError { get; private set; } = "None";
    readonly ConcurrentQueue<Response> queue = new ConcurrentQueue<Response>();
    enum ResultKind { Transcription, Intent, Error, Complete }
    struct Response { public ResultKind Kind; public string Intent, Text, Error; public float Confidence; public DateTime Utc; public long Ticks; public int Generation; }
    int pending, dropped, generation;
    bool triggerHeld, suspended, subscribed, requestPending, finalReceived, permissionPending, micWasActive;
    float requestStarted, nextActivation;
#if UNITY_ANDROID
    PermissionCallbacks permissionCallbacks;
#endif
    void Start()
    {
        if (!controller) controller = GetComponent<SpatialVoiceIntentController>();
#if UNITY_ANDROID && !UNITY_EDITOR
        Status(configuration ? "Quest: right trigger to speak a command; internet required" : "Quest voice: existing Wit configuration not assigned");
#else
        Status("Quest preview: Android voice capture is off; transcription routing requires headset testing");
#endif
    }
    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool pressed = device.TryGetFeatureValue(CommonUsages.triggerButton, out bool value) && value;
        if (pressed && !triggerHeld) StartListening();
        triggerHeld = pressed;
#endif
        if (requestPending && voiceService && voiceService.MicActive != micWasActive)
        {
            micWasActive=voiceService.MicActive;
            Status(micWasActive ? "Quest voice: microphone listening — say a command" : "Quest voice: processing transcription");
        }
        if (requestPending && Time.unscaledTime-requestStarted>25)
        { StopNative(); LogSafeError(SafeErrorDetails("timeout","request timeout")); Status("Quest voice: request timed out; check Wi-Fi and trigger to retry"); }
    }
    void LateUpdate()
    {
        while (queue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref pending);
            if (!isActiveAndEnabled || suspended || item.Generation!=generation) continue;
            if (item.Kind==ResultKind.Error)
            { requestPending=false; LogSafeError(item.Error); Status("Quest voice: request failed; check safe Console details and trigger to retry"); continue; }
            if (item.Kind==ResultKind.Complete)
            {
                if(requestPending && !finalReceived) { requestPending=false; Status("Quest voice: no final command heard; trigger to retry"); }
                continue;
            }
            // One SDK request may emit both OnFullTranscription and OnResponse: never dispatch or count it twice.
            if(finalReceived || !requestPending) continue;
            bool mapped;
            string command;
            if(item.Kind==ResultKind.Transcription) mapped=TryMapTranscription(item.Text,out command);
            else { if(!allowIntentFallback) continue; mapped=TryMapIntent(item.Intent,out command); }
            if(!mapped && item.Kind==ResultKind.Intent) command="intent:"+item.Intent;
            if(command.Length>128) command=command.Substring(0,128);
            var context=new InteractionCommandContext(InteractionInputSource.Voice,command,item.Utc,item.Ticks,
                item.Kind==ResultKind.Intent && !float.IsNaN(item.Confidence) ? item.Confidence.ToString("F4",CultureInfo.InvariantCulture) : "");
            if(!controller) { Status("Quest voice: interaction controller missing"); continue; }
            double age=(System.Diagnostics.Stopwatch.GetTimestamp()-item.Ticks)/(double)System.Diagnostics.Stopwatch.Frequency;
            if(age>2) controller.RecordRejectedCommand(context,"expired_speech_result");
            else if(!mapped) controller.RecordRejectedCommand(context,item.Kind==ResultKind.Transcription?"unknown_speech_command":"unsupported_quest_intent");
            else if(item.Kind==ResultKind.Intent && (float.IsNaN(item.Confidence)||item.Confidence<minimumIntentConfidence))
                controller.RecordRejectedCommand(context,"low_intent_confidence");
            else controller.ExecuteCommand(context);
            finalReceived=true; requestPending=false; nextActivation=Time.unscaledTime+.75f;
            LastRecognizedCommand=command;
            Status(mapped ? "Quest voice: command processed; trigger for next request" : "Quest voice: phrase not supported; use a handbook command");
        }
    }
    public static bool TryMapTranscription(string transcript,out string command)
    {
        command=string.Join(" ",(transcript??"").Trim().Trim('.', '!', '?', ',', ';', ':', '"', '\'').ToLowerInvariant()
            .Split(new[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries));
        if(command=="rotate forty five degrees" || command=="rotate forty-five degrees") command="rotate 45 degrees";
        if(command=="rotate ninety degrees") command="rotate 90 degrees";
        foreach(var phrase in VoiceCommandRecognizer.CommandKeywords) if(command==phrase) return true;
        if(command.Length>128) command=command.Substring(0,128);
        return false;
    }
    public static bool TryMapIntent(string intent,out string command)
    { command=intent=="select_object"?"select":""; return command.Length!=0; }
    // Whitelisted output only: never return raw service messages, URLs, headers, bodies, or credentials.
    public static string SafeErrorDetails(string code,string message)
    {
        int status=0;
        if(!int.TryParse(code,out status) || status<100 || status>599)
        {
            var match=Regex.Match((code??"")+" "+(message??""),@"(?i)\b(?:HTTP(?:/\d(?:\.\d)?)?(?:\s+status(?:\s+code)?)?|response\s+code|status(?:\s+code)?)\s*[:=]?\s*(\d{3})\b");
            status=match.Success && int.TryParse(match.Groups[1].Value,out int number) && number>=100 && number<=599 ? number : 0;
        }
        string text=((code??"")+" "+(message??"")).ToLowerInvariant();
        string category=status==401||status==403||text.Contains("unauthorized")||text.Contains("invalid token")||text.Contains("bad auth")||text.Contains("missing token")||text.Contains("token expired")?"authentication_or_access":status==429?"rate_limited":status>=500?"service_error":
            text.Contains("timeout")||text.Contains("timed out")?"timeout":text.Contains("ssl")||text.Contains("tls")||text.Contains("certificate")?"tls":
            text.Contains("permission")?"permission_denied":text.Contains("cancel")?"cancelled":text.Contains("network")||text.Contains("connect")||text.Contains("dns")||text.Contains("resolve")?"network":"request_error";
        return "HTTP="+(status==0?"N/A":status.ToString(CultureInfo.InvariantCulture))+"; category="+category;
    }
    void LogSafeError(string detail) { LastSafeError=detail; Debug.LogWarning("Quest voice: "+detail,this); }
    public void StartListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if(!isActiveAndEnabled||suspended||permissionPending||Time.unscaledTime<nextActivation) return;
        if(!configuration||string.IsNullOrWhiteSpace(configuration.GetClientAccessToken()))
        { Status("Quest voice: existing wit_voice_verse configuration missing or incomplete"); return; }
        if(Application.internetReachability==NetworkReachability.NotReachable)
        { LogSafeError(SafeErrorDetails("network","no network")); Status("Quest voice: no network; connect Wi-Fi and retry"); return; }
        if(!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            permissionPending=true; permissionCallbacks=new PermissionCallbacks();
            permissionCallbacks.PermissionGranted+=_=>{permissionPending=false;if(this&&isActiveAndEnabled&&!suspended)StartListening();};
            permissionCallbacks.PermissionDenied+=_=>{permissionPending=false;if(this)EnqueueError("permission","microphone permission denied");};
            Status("Quest voice: allow microphone permission in the headset");
            Permission.RequestUserPermission(Permission.Microphone,permissionCallbacks);return;
        }
        if(!voiceService||requestPending||voiceService.Active||voiceService.IsRequestActive)
        { Status(voiceService?"Quest voice: wait for the current request":"Quest voice service is not assigned");return; }
        try
        {
            if(!subscribed)
            {
                voiceService.VoiceEvents.OnFullTranscription.AddListener(OnTranscription);
                voiceService.VoiceEvents.OnResponse.AddListener(OnResponse);
                voiceService.VoiceEvents.OnRequestCompleted.AddListener(OnComplete);
                voiceService.VoiceEvents.OnError.AddListener(OnError);subscribed=true;
            }
            voiceService.RuntimeConfiguration.witConfiguration=configuration;
            voiceService.RuntimeConfiguration.transcribeOnly=!allowIntentFallback;
            voiceService.UsePlatformIntegrations=false;
            voiceService.gameObject.SetActive(true);
            finalReceived=false;requestPending=true;requestStarted=Time.unscaledTime;
            voiceService.Activate();Status("Quest voice: starting microphone request");
        }
        catch {StopNative();LogSafeError(SafeErrorDetails("startup","startup failure"));Status("Quest voice: start failed; check microphone/network/configuration");}
#else
        Status("Quest voice: Android headset only; Windows speech is unchanged");
#endif
    }
    Response Capture(ResultKind kind)
    { return new Response{Kind=kind,Utc=DateTime.UtcNow,Ticks=System.Diagnostics.Stopwatch.GetTimestamp(),Generation=generation,Confidence=float.NaN}; }
    void OnTranscription(string text) {if(string.IsNullOrWhiteSpace(text))return;var item=Capture(ResultKind.Transcription);item.Text=text;Enqueue(item);}
    void OnResponse(WitResponseNode response)
    {
        var item=Capture(ResultKind.Transcription);item.Text=response?["text"]?.Value??"";
        if(string.IsNullOrWhiteSpace(item.Text))
        {
            if(!allowIntentFallback)return;
            var intent=response?.GetFirstIntentData();item.Kind=ResultKind.Intent;item.Intent=intent?.name??"";item.Confidence=intent?.confidence??float.NaN;
        }
        Enqueue(item);
    }
    void OnComplete(){Enqueue(Capture(ResultKind.Complete));}
    void OnError(string code,string message){EnqueueError(code,message);}
    void EnqueueError(string code,string message){var item=Capture(ResultKind.Error);item.Error=SafeErrorDetails(code,message);Enqueue(item);}
    void Enqueue(Response item)
    {if(Interlocked.Increment(ref pending)>32){Interlocked.Decrement(ref pending);Interlocked.Increment(ref dropped);return;}queue.Enqueue(item);}
    void Status(string value){RecognizerStatus=value;if(controller)controller.SetVoiceFeedback(value,"Heard: "+LastRecognizedCommand);}
    public void StopListening(){StopNative();Status("Quest voice stopped; trigger to retry");}
    void StopNative()
    {
        Interlocked.Increment(ref generation);requestPending=false;
        if(voiceService)
        {
            if(subscribed)
            {
                voiceService.VoiceEvents.OnFullTranscription.RemoveListener(OnTranscription);
                voiceService.VoiceEvents.OnResponse.RemoveListener(OnResponse);
                voiceService.VoiceEvents.OnRequestCompleted.RemoveListener(OnComplete);
                voiceService.VoiceEvents.OnError.RemoveListener(OnError);subscribed=false;
            }
            try{voiceService.Deactivate();}catch{}
            voiceService.gameObject.SetActive(false);
        }
        while(queue.TryDequeue(out _))Interlocked.Decrement(ref pending);
    }
    void OnApplicationPause(bool paused){suspended=paused;if(paused)StopListening();else Status("Quest voice: press trigger to resume");}
    void OnDisable(){StopNative();}
}
