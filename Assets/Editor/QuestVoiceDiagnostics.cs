using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Meta.WitAi;
using Meta.WitAi.Configuration;
using Meta.WitAi.Data.Configuration;
using Meta.WitAi.Requests;
using UnityEditor;
using UnityEngine;

// Isolated, explicit text-only probe of the same SDK factory used by Understanding Viewer.
// It never trains intents, opens the microphone, dispatches commands, or logs response bodies/tokens.
[InitializeOnLoad]
public static class QuestVoiceDiagnostics
{
    static VoiceServiceRequest request;
    static Stopwatch clock;
    static int timeoutMs;
    static volatile bool complete;
    static int status;
    static string safeDetail;
    static QuestVoiceDiagnostics() { AssemblyReloadEvents.beforeAssemblyReload += Cancel; }
    [MenuItem("VOICEVERSE/Quest/Diagnose Wit Text Request (No Microphone)")]
    public static void Run()
    {
        if(request!=null) { UnityEngine.Debug.LogWarning("Wit text diagnostic is already running."); return; }
        var configuration=AssetDatabase.LoadAssetAtPath<WitConfiguration>(QuestProjectSetup.WitPath);
        if(!configuration || string.IsNullOrWhiteSpace(configuration.GetClientAccessToken()))
        { UnityEngine.Debug.LogWarning("Wit text diagnostic: existing client configuration is incomplete; no request sent."); return; }
        timeoutMs=configuration.RequestTimeoutMs; complete=false; status=0; safeDetail="";
        clock=Stopwatch.StartNew();
        try
        {
            request=configuration.CreateMessageRequest(new WitRequestOptions(),new VoiceServiceRequestEvents());
            request.Options.Text="select";
            request.Events.OnComplete.AddListener(OnComplete);
            EditorApplication.update+=Tick;
            UnityEngine.Debug.Log("Wit text diagnostic started; microphone off; configured timeout="+timeoutMs+" ms.");
            request.Send();
        }
        catch { Finish(0,QuestVoiceCommandProvider.SafeErrorDetails("startup","request startup failed")); }
    }
    static void OnComplete(VoiceServiceRequest result)
    {
        status=result.StatusCode;
        safeDetail=status>=200 && status<300 ? "HTTP="+status+"; category=success" :
            QuestVoiceCommandProvider.SafeErrorDetails(status.ToString(CultureInfo.InvariantCulture),result.Results.Message);
        complete=true;
    }
    static void Tick()
    {
        if(complete) { Finish(status,safeDetail); return; }
        if(clock!=null && clock.Elapsed.TotalSeconds>Math.Min(45,Math.Max(15,timeoutMs/1000d+5)))
        { Finish(0,QuestVoiceCommandProvider.SafeErrorDetails("timeout","Editor text request deadline exceeded")); }
    }
    static void Finish(int code,string detail)
    {
        EditorApplication.update-=Tick;
        var elapsed=clock?.Elapsed.TotalSeconds??0;
        string summary="Wit text diagnostic: "+detail+"; elapsed="+elapsed.ToString("F2",CultureInfo.InvariantCulture)+" s; configured timeout="+timeoutMs+" ms. No microphone tested.";
        UnityEngine.Debug.Log(summary);
        Directory.CreateDirectory("Builds/QuestVerification");
        File.WriteAllText("Builds/QuestVerification/wit-text-diagnostic.json",Newtonsoft.Json.JsonConvert.SerializeObject(
            new { sdkStatus=code, httpStatus=code>=100 && code<=599 ? (int?)code : null, safeDetail=detail, elapsedSeconds=elapsed, configuredTimeoutMs=timeoutMs, microphoneTested=false },Newtonsoft.Json.Formatting.Indented));
        Cancel();
    }
    static void Cancel()
    {
        EditorApplication.update-=Tick;
        var active=request; request=null;
        if(active!=null) { active.Events.OnComplete.RemoveListener(OnComplete); try { active.Cancel("Diagnostic finished"); } catch { } }
        clock=null;
    }
}
