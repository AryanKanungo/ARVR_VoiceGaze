using System;
using System.Collections.Concurrent;
using System.Threading;
using Meta.WitAi.Configuration;
using Meta.WitAi.Data.Configuration;
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
    public string RecognizerStatus { get; private set; } = "Quest voice: initializing";
    public int DroppedCallbackEvents => dropped;
    public string LastRecognizedCommand { get; private set; } = "None";
    const string ConfigurationResource = "QuestVoiceLocal/WitConfiguration";
    readonly ConcurrentQueue<Callback> callbacks = new ConcurrentQueue<Callback>();
    struct Callback { public string Text; public bool Error; public DateTime Utc; public long Ticks; }
    int pending, dropped;
    AppVoiceExperience voice;
    bool triggerHeld, suspended, finalReceived, subscribed;
    string previousCommand;
    long previousCommandTicks;

    void Start()
    {
        if (!controller) controller = GetComponent<SpatialVoiceIntentController>();
#if UNITY_ANDROID && !UNITY_EDITOR
        Status("Quest voice: press right trigger to speak (internet required)");
#else
        Status("Quest voice: Android headset only; no microphone started");
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
    }
    void LateUpdate()
    {
        while (callbacks.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref pending);
            if (!isActiveAndEnabled || suspended) continue;
            if (item.Error) { Status("Quest voice request failed. Check network/Wit configuration; trigger to retry."); continue; }
            bool known = TryMapCommand(item.Text, out string command);
            var context = new InteractionCommandContext(InteractionInputSource.Voice, command, item.Utc, item.Ticks);
            if (!controller) { Status("Quest voice: interaction controller missing"); continue; }
            double age = (System.Diagnostics.Stopwatch.GetTimestamp() - item.Ticks) / (double)System.Diagnostics.Stopwatch.Frequency;
            bool repeated = command == previousCommand && (item.Ticks - previousCommandTicks) / (double)System.Diagnostics.Stopwatch.Frequency < .75;
            if (finalReceived || repeated) controller.RecordRejectedCommand(context, "duplicate_speech_result");
            else if (age > 2) controller.RecordRejectedCommand(context, "expired_speech_result");
            else if (!known) controller.RecordRejectedCommand(context, "unknown_speech_command");
            else controller.ExecuteCommand(context);
            finalReceived = true; previousCommand = command; previousCommandTicks = item.Ticks;
            LastRecognizedCommand = command;
            Status("Quest voice: press right trigger for next command");
        }
    }
    public static bool TryMapCommand(string transcript, out string command)
    {
        command = string.Join(" ", (transcript ?? "").Trim().Trim('.', '!', '?', ',').ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        if (command == "rotate forty five degrees" || command == "rotate forty-five degrees") command = "rotate 45 degrees";
        if (command == "rotate ninety degrees") command = "rotate 90 degrees";
        foreach (var phrase in VoiceCommandRecognizer.CommandKeywords) if (command == phrase) return true;
        if (command.Length > 128) command = command.Substring(0, 128);
        return false;
    }
    public void StartListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!isActiveAndEnabled || suspended) return;
        if (Application.internetReachability == NetworkReachability.NotReachable)
        { Status("Quest voice: no network; connect Wi-Fi and press trigger to retry"); return; }
        var configuration = Resources.Load<WitConfiguration>(ConfigurationResource);
        if (!configuration || string.IsNullOrWhiteSpace(configuration.GetClientAccessToken()))
        { Status("Quest voice: Wit.ai configuration missing. Complete local setup first."); return; }
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            var permission = new PermissionCallbacks();
            permission.PermissionGranted += _ => { if (isActiveAndEnabled && !suspended) StartListening(); };
            permission.PermissionDenied += _ => Enqueue("Microphone permission denied", true);
            Status("Quest voice: allow microphone permission in the headset");
            Permission.RequestUserPermission(Permission.Microphone, permission); return;
        }
        if (voice && (voice.Active || voice.IsRequestActive))
        { Status("Quest voice: previous request still running; wait before retrying"); return; }
        try
        {
            if (!voice)
            {
                var go = new GameObject("Quest Meta Voice SDK"); go.SetActive(false); go.transform.SetParent(transform, false);
                voice = go.AddComponent<AppVoiceExperience>();
                voice.RuntimeConfiguration = new WitRuntimeConfiguration { witConfiguration = configuration,
                    transcribeOnly = true, sendAudioToWit = true, alwaysRecord = false,
                    maxRecordingTime = 8, minKeepAliveTimeInSeconds = 1, maxConcurrentRequests = 1, overrideTimeoutMs = 15000 };
                voice.UsePlatformIntegrations = false;
                voice.VoiceEvents.OnFullTranscription.AddListener(OnTranscript);
                voice.VoiceEvents.OnError.AddListener(OnError); subscribed = true;
                go.SetActive(true);
            }
            finalReceived = false;
            voice.Activate(); Status("Quest voice: listening — say one command");
        }
        catch { Status("Quest voice: could not start; check permission/network/configuration"); StopNative(); }
#else
        Status("Quest voice: Android headset only; Windows recognizer is preserved");
#endif
    }
    void OnTranscript(string text) { Enqueue(text, false); }
    void OnError(string code, string message) { Enqueue("Voice service error", true); }
    void Enqueue(string text, bool error)
    {
        // Native/SDK callbacks only capture data. All command execution occurs in LateUpdate.
        var item = new Callback { Text = text, Error = error, Utc = DateTime.UtcNow, Ticks = System.Diagnostics.Stopwatch.GetTimestamp() };
        if (Interlocked.Increment(ref pending) > 32) { Interlocked.Decrement(ref pending); Interlocked.Increment(ref dropped); return; }
        callbacks.Enqueue(item);
    }
    void Status(string value)
    { RecognizerStatus = value; if (controller) controller.SetVoiceFeedback(value, "Heard: " + LastRecognizedCommand); }
    public void StopListening() { StopNative(); Status("Quest voice stopped; trigger to retry"); }
    void StopNative()
    {
        if (voice)
        {
            if (subscribed) { voice.VoiceEvents.OnFullTranscription.RemoveListener(OnTranscript); voice.VoiceEvents.OnError.RemoveListener(OnError); }
            subscribed = false;
            try { voice.Deactivate(); } catch { /* teardown must not prevent scene shutdown */ }
            Destroy(voice.gameObject); voice = null;
        }
        while (callbacks.TryDequeue(out _)) Interlocked.Decrement(ref pending);
    }
    void OnApplicationPause(bool paused)
    { suspended = paused; if (paused) StopListening(); else Status("Quest voice: press right trigger to resume"); }
    void OnDisable() { StopNative(); }
}
