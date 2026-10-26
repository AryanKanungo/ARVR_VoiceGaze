using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Dormant unless explicitly requested by benchmark/self-test launch flags or an Editor test.
public sealed class DesktopBuildVerification : MonoBehaviour
{
    [Serializable] public sealed class Report
    {
        public string label, utc, unity, platform, graphicsApi, gpu, cpu, outputPath, telemetryPath;
        public int width, height, targetFps, vSync, frames, unfocusedFrames, cpuTimingSamples, gpuTimingSamples;
        public double warmupSeconds, requestedSeconds, sampledSeconds, averageMs, fps, p50Ms, p95Ms, p99Ms, maxMs;
        public double cpuFrameMs, cpuMainMs, cpuRenderMs, presentWaitMs, gpuMs;
        public bool xrActive, timingFeatureEnabled, truncated;
    }
    struct Sample { public double ms, cpu, main, render, wait, gpu; public bool focus, timing; }
    Sample[] samples;
    readonly FrameTiming[] timing = new FrameTiming[1];
    int count, cpuCount, gpuCount, unfocused;
    double warmupEnd, previous, seconds, sumCpu, sumMain, sumRender, sumWait, sumGpu;
    ulong lastTiming;
    bool running, quit;
    int writerDone;
    string label, output;
    float requested, warmup;
    VoiceCommandRecognizer voice;
    bool restartVoice;
    public static string Arg(string key, string fallback)
    {
        var args=Environment.GetCommandLineArgs();
        for(int i=0;i<args.Length-1;i++) if(args[i]==key)return args[i+1];
        return fallback;
    }
    static bool Flag(string key) { return Array.IndexOf(Environment.GetCommandLineArgs(),key)>=0; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        Application.targetFrameRate=60;
        if(Flag("-ivrar-benchmark") || Flag("-ivrar-self-test"))
        {
            var go=new GameObject("Explicit Desktop Verification");
            var test=go.AddComponent<DesktopBuildVerification>();
            string path=Arg("-ivrar-output",Path.Combine(Application.persistentDataPath,"desktop_verification.json"));
            if(Flag("-ivrar-self-test")) test.StartCoroutine(test.Smoke(path,true));
            else test.Begin(Arg("-ivrar-label","standalone"),path,
                float.Parse(Arg("-ivrar-duration","30"),CultureInfo.InvariantCulture),
                int.Parse(Arg("-ivrar-cap","-1"),CultureInfo.InvariantCulture),true);
        }
#endif
    }
    public void Begin(string testLabel,string path,float duration=30,int cap=-1,bool quitAfter=false)
    {
        if(running)return;
        label=testLabel;output=Path.GetFullPath(path);requested=duration;warmup=5;quit=quitAfter;
        Application.runInBackground=true;Application.targetFrameRate=cap;QualitySettings.vSyncCount=0;
        samples=new Sample[100000];count=0;seconds=0;cpuCount=0;gpuCount=0;unfocused=0;
        sumCpu=sumMain=sumRender=sumWait=sumGpu=0;lastTiming=0;
        voice=FindAnyObjectByType<VoiceCommandRecognizer>();
        if(voice){restartVoice=true;voice.StopListening();}
        var telemetry=FindAnyObjectByType<InteractionTelemetry>();
        if(telemetry){telemetry.sessionLabel="automated_benchmark_"+label;telemetry.automatedTestSession=true;telemetry.BeginTrial("BENCHMARK-NOT-HUMAN");}
        previous=Time.realtimeSinceStartupAsDouble;warmupEnd=previous+warmup;running=true;
        Debug.Log("DESKTOP BENCHMARK START "+label+" "+Screen.width+"x"+Screen.height+" "+SystemInfo.graphicsDeviceType);
    }
    void Update()
    {
        if(!running)return;
        FrameTimingManager.CaptureFrameTimings();
        double now=Time.realtimeSinceStartupAsDouble;double started=previous;previous=now;
        if(started<warmupEnd)return;
        double delta=Time.unscaledDeltaTime*1000d;
        var sample=new Sample {ms=delta,focus=Application.isFocused};
        uint n=FrameTimingManager.GetLatestTimings(1,timing);
        if(n>0 && timing[0].frameStartTimestamp!=lastTiming)
        {
            lastTiming=timing[0].frameStartTimestamp;var f=timing[0];sample.timing=true;
            sample.cpu=f.cpuFrameTime;sample.main=f.cpuMainThreadFrameTime;sample.render=f.cpuRenderThreadFrameTime;sample.wait=f.cpuMainThreadPresentWaitTime;sample.gpu=f.gpuFrameTime;
            if(f.cpuFrameTime>0){cpuCount++;sumCpu+=f.cpuFrameTime;sumMain+=f.cpuMainThreadFrameTime;sumRender+=f.cpuRenderThreadFrameTime;sumWait+=f.cpuMainThreadPresentWaitTime;}
            if(f.gpuFrameTime>0){gpuCount++;sumGpu+=f.gpuFrameTime;}
        }
        samples[count++]=sample;seconds+=delta/1000d;if(!sample.focus)unfocused++;
        if(now>=warmupEnd+requested || count==samples.Length)Finish();
    }
    double Percentile(double[] sorted,double q) { return sorted[Math.Max(0,(int)Math.Ceiling(q*sorted.Length)-1)]; }
    void Finish()
    {
        running=false;
        var sorted=new double[count];for(int i=0;i<count;i++)sorted[i]=samples[i].ms;Array.Sort(sorted);
        var telemetry=FindAnyObjectByType<InteractionTelemetry>();
        var report=new Report {label=label,utc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,
            platform=Application.platform.ToString(),graphicsApi=SystemInfo.graphicsDeviceType.ToString(),gpu=SystemInfo.graphicsDeviceName,cpu=SystemInfo.processorType,
            outputPath=output,telemetryPath=telemetry?telemetry.CsvPath:"",width=Screen.width,height=Screen.height,targetFps=Application.targetFrameRate,vSync=QualitySettings.vSyncCount,
            frames=count,unfocusedFrames=unfocused,warmupSeconds=warmup,requestedSeconds=requested,sampledSeconds=seconds,
            averageMs=seconds*1000/count,fps=count/seconds,p50Ms=Percentile(sorted,.5),p95Ms=Percentile(sorted,.95),p99Ms=Percentile(sorted,.99),maxMs=sorted[count-1],
            cpuTimingSamples=cpuCount,gpuTimingSamples=gpuCount,cpuFrameMs=cpuCount>0?sumCpu/cpuCount:0,
            cpuMainMs=cpuCount>0?sumMain/cpuCount:0,cpuRenderMs=cpuCount>0?sumRender/cpuCount:0,presentWaitMs=cpuCount>0?sumWait/cpuCount:0,
            gpuMs=gpuCount>0?sumGpu/gpuCount:0,xrActive=UnityEngine.XR.XRSettings.isDeviceActive,timingFeatureEnabled=FrameTimingManager.IsFeatureEnabled(),truncated=count==samples.Length};
        string json=JsonUtility.ToJson(report,true);var captured=samples;int length=count;string destination=output;
        writerDone=0;
        ThreadPool.QueueUserWorkItem(_=>{
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(destination));File.WriteAllText(destination,json);
                using(var csv=new StreamWriter(destination+".frames.csv",false,new UTF8Encoding(false)))
                {
                    csv.WriteLine("frame,interval_ms,focused,timing_available,cpu_frame_ms,cpu_main_ms,cpu_render_ms,present_wait_ms,gpu_ms");
                    for(int i=0;i<length;i++){var s=captured[i];csv.WriteLine(string.Join(",",i.ToString(),s.ms.ToString("F6",CultureInfo.InvariantCulture),s.focus.ToString(),s.timing.ToString(),s.cpu.ToString("F6",CultureInfo.InvariantCulture),s.main.ToString("F6",CultureInfo.InvariantCulture),s.render.ToString("F6",CultureInfo.InvariantCulture),s.wait.ToString("F6",CultureInfo.InvariantCulture),s.gpu.ToString("F6",CultureInfo.InvariantCulture)));}
                }
            }finally{Interlocked.Exchange(ref writerDone,1);}
        });
        if(telemetry){telemetry.ExportPerformanceSnapshot();telemetry.RequestFlush();}
        Debug.Log("DESKTOP BENCHMARK COMPLETE "+json);
        StartCoroutine(CompleteWrite());
    }
    IEnumerator CompleteWrite()
    {
        while(Volatile.Read(ref writerDone)==0)yield return null;
        if(restartVoice && voice)voice.StartListening();
        if(quit)Application.Quit();
    }
    [Serializable] public sealed class SmokeReport {public string utc,platform,graphicsApi,telemetryPath;public int passed,failed;public string[] results;}
    public IEnumerator Smoke(string path,bool quitAfter=false)
    {
        Application.runInBackground=true;
        var c=FindAnyObjectByType<SpatialVoiceIntentController>();var v=c.GetComponent<VoiceCommandRecognizer>();var t=c.GetComponent<InteractionTelemetry>();
        v.StopListening();t.sessionLabel="automated_desktop_smoke";t.automatedTestSession=true;t.BeginTrial("SMOKE-NOT-HUMAN");
        var keyboard=InputSystem.AddDevice<Keyboard>("Desktop Verification Keyboard");
        var cam=c.resolver.gazeCamera;var cp=cam.transform.position;var cr=cam.transform.rotation;
        var red=GameObject.Find("Red Cube").GetComponent<InteractableObject>();var pad=GameObject.Find("Left Pad").GetComponent<InteractableObject>();var original=red.transform.position;
        var results=new System.Collections.Generic.List<string>();int pass=0,fail=0;
        Action<bool,string> check=(ok,name)=>{results.Add((ok?"PASS ":"FAIL ")+name);if(ok)pass++;else fail++;};
        yield return Press(keyboard,Key.Digit2);check(!c.LastResult.Accepted && c.LastResult.RejectionReason=="no_selection","invalid Place");
        cam.transform.LookAt(red.transform.position);yield return new WaitForSecondsRealtime(.3f);check(c.resolver.CurrentStableTarget==red && red.IsGazed,"stable gaze/highlight");
        var block=new MaterialPropertyBlock();red.GetComponent<Renderer>().GetPropertyBlock(block);check(block.GetColor("_BaseColor")!=red.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor"),"highlight material feedback");
        yield return Press(keyboard,Key.Digit1);check(c.SelectedObject==red && red.IsSelected,"keyboard Select");
        cam.transform.LookAt(pad.transform.position);yield return new WaitForSecondsRealtime(.3f);check(c.SelectedObject==red && c.resolver.CurrentStableTarget==pad,"locked selection while gaze moves");
        yield return Press(keyboard,Key.Digit2);check(Vector3.Distance(red.transform.position,pad.placementAnchor.position)<.001f && c.SelectedObject==null,"keyboard Place");
        yield return Press(keyboard,Key.Digit4);check(Vector3.Distance(red.transform.position,original)<.001f && !pad.IsOccupied,"keyboard Reset");
        cam.transform.LookAt(red.transform.position);yield return new WaitForSecondsRealtime(.3f);yield return Press(keyboard,Key.Digit1);yield return Press(keyboard,Key.Digit3);check(c.SelectedObject==null && !red.IsSelected,"keyboard Release");
        cam.transform.LookAt(Vector3.up*20);yield return new WaitForSecondsRealtime(.3f);yield return Press(keyboard,Key.Digit1);check(!c.LastResult.Accepted,"invalid Select");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));yield return new WaitForSecondsRealtime(.15f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;check(Vector3.Distance(cp,cam.transform.position)>.01f,"camera movement");
        check(c.statusText.text.Contains("Selected:") && c.statusText.text.Contains("CSV:"),"status dashboard");
        cam.transform.SetPositionAndRotation(cp,cr);c.ResetObjects();InputSystem.RemoveDevice(keyboard);
        t.RequestFlush();yield return new WaitForSecondsRealtime(1f);check(t.WriterReady && t.WriterError==null && t.WrittenRows>0 && t.DroppedRows==0,"CSV export");
        var report=new SmokeReport {utc=DateTime.UtcNow.ToString("O"),platform=Application.platform.ToString(),graphicsApi=SystemInfo.graphicsDeviceType.ToString(),telemetryPath=t.CsvPath,passed=pass,failed=fail,results=results.ToArray()};
        string json=JsonUtility.ToJson(report,true);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,json);Debug.Log("DESKTOP SMOKE "+json);
        v.StartListening();if(quitAfter)Application.Quit();
    }
    IEnumerator Press(Keyboard keyboard,Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
    }
}

