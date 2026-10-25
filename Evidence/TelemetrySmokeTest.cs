using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System.Collections;
using System.IO;

internal class CommandScript : IRunCommand
{
    SpatialVoiceIntentController c; VoiceCommandRecognizer voice; InteractionTelemetry t; Keyboard keyboard;
    InputSettings.EditorInputBehaviorInPlayMode oldInput; InputSettings.BackgroundBehavior oldBackground; bool oldRun;
    int passed, failed;
    [System.Serializable] class PerformanceReport
    {
        public string session_id, csv_path, measurement_started_utc, measurement_ended_utc, unity_version, platform, graphics_api;
        public string context = "Automated development Editor scene; not human trial results or headset performance.";
        public int measurement_id, target_frame_rate, v_sync_count, passed_checks, failed_checks;
        public long sampled_frames;
        public double sampled_duration_s, average_frame_ms, average_fps, max_frame_ms, warmup_s;
        public bool application_focused_at_snapshot;
    }
    void Check(bool ok, string label) { if(ok) { passed++; Debug.Log("TELEMETRY TEST PASS: "+label); } else { failed++; Debug.LogError("TELEMETRY TEST FAIL: "+label); } }
    IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key)); yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
    }
    IEnumerator Aim(Vector3 point) { c.resolver.gazeCamera.transform.LookAt(point); yield return new WaitForSecondsRealtime(.3f); }
    InteractionCommandContext SyntheticVoice(string command)
    {
        return InteractionCommandContext.Capture(InteractionInputSource.Voice,command,true);
    }
    IEnumerator Run()
    {
        var cam=c.resolver.gazeCamera; var cameraPosition=cam.transform.position; var cameraRotation=cam.transform.rotation;
        var red=GameObject.Find("Red Cube").GetComponent<InteractableObject>();
        var blue=GameObject.Find("Blue Cube").GetComponent<InteractableObject>();
        var left=GameObject.Find("Left Pad").GetComponent<InteractableObject>();
        Vector3 redStart=red.transform.position, blueStart=blue.transform.position;
        try
        {
            voice.StopListening();
            t.enabled=false; t.sessionLabel="automated_smoke, \"CSV quote check\"";t.trialId="AUTOMATED-001";t.automatedTestSession=true;t.enabled=true;
            File.WriteAllText("Evidence/TelemetryAutomatedPath.txt",t.CsvPath);
            yield return null;
            Check(Mathf.Abs(c.resolver.stabilizationSeconds-.12f)<.00001f,"120 ms gaze configuration preserved");
            yield return Press(Key.Digit2);
            Check(!c.LastResult.Accepted && c.LastResult.RejectionReason=="no_selection" &&
                c.LastResult.Context.Source==InteractionInputSource.Keyboard,"invalid keyboard Place rejected with source and reason");
            yield return Aim(red.transform.position);
            yield return Press(Key.Digit1);
            Check(c.SelectedObject==red && c.LastResult.Accepted && c.LastResult.TargetObject=="Red Cube","keyboard Select accepted and target captured");
            yield return Aim(left.transform.position);
            Check(c.SelectedObject==red && red.IsSelected && c.resolver.CurrentStableTarget==left,"selection persists while gaze moves");
            yield return Press(Key.Digit2);
            Check(c.LastResult.Accepted && c.LastResult.TargetObject=="Red Cube" && c.LastResult.DestinationObject=="Left Pad" &&
                Vector3.Distance(red.transform.position,left.placementAnchor.position)<.001f,"keyboard Place moves cube and records target/destination before clearing");
            yield return Aim(blue.transform.position);yield return Press(Key.Digit1);
            yield return Aim(left.transform.position+Vector3.right*.32f);yield return Press(Key.Digit2);
            Check(!c.LastResult.Accepted && c.LastResult.RejectionReason=="destination_occupied" && c.SelectedObject==blue,"occupied pad rejected without losing selection");
            yield return Aim(Vector3.up*20);yield return Press(Key.Digit2);
            Check(!c.LastResult.Accepted && c.LastResult.RejectionReason=="no_stable_destination" && c.SelectedObject==blue,"invalid destination rejected safely");
            yield return Press(Key.Digit3);
            Check(c.LastResult.Accepted && c.SelectedObject==null && !blue.IsSelected,"keyboard Release");
            yield return Press(Key.Digit4);
            Check(c.LastResult.Accepted && Vector3.Distance(red.transform.position,redStart)<.001f && !left.IsOccupied,"keyboard Reset restores transforms and occupancy");
            yield return Press(Key.Digit1);
            Check(!c.LastResult.Accepted && c.LastResult.RejectionReason=="no_stable_object","invalid keyboard Select rejected");
            yield return Aim(red.transform.position);yield return Press(Key.Digit1);
            var first=c.ExecuteCommand(SyntheticVoice("release"));var duplicate=c.ExecuteCommand(SyntheticVoice("release"));
            Check(first.Accepted && !duplicate.Accepted && duplicate.RejectionReason=="duplicate_same_frame","same-frame duplicate prevented (explicitly synthetic voice rows)");
            var heldResolver=c.resolver;
            blue.transform.position+=Vector3.up;c.resolver=null;c.ResetObjects();c.resolver=heldResolver;
            Check(c.LastResult.Accepted && Vector3.Distance(blue.transform.position,blueStart)<.001f,"Reset works without gaze resolver");
            yield return null;
            var rejected=c.RecordRejectedCommand(SyntheticVoice("select"),"synthetic_filter_check");
            Check(!rejected.Accepted && rejected.RejectionReason=="synthetic_filter_check","filtered voice result can be logged without manipulation (synthetic)");
            Check(!c.LastResult.RecognitionEventToActionMs.HasValue,"rejected result has no action latency");
            c.ResetObjects();yield return null;
            cam.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            var before=cam.transform.position;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            Check(Vector3.Distance(cam.transform.position,before)>.01f,"keyboard camera movement unchanged");
            cam.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            t.ResetPerformanceMeasurement();
            string start=System.DateTime.UtcNow.ToString("O");
            yield return new WaitForSecondsRealtime(12f);
            t.SetSamplingEnabled(false);t.ExportPerformanceSnapshot();t.RequestFlush();
            var report=new PerformanceReport {
                session_id=t.SessionId,csv_path=t.CsvPath,measurement_started_utc=start,measurement_ended_utc=System.DateTime.UtcNow.ToString("O"),
                unity_version=Application.unityVersion,platform=Application.platform.ToString(),graphics_api=SystemInfo.graphicsDeviceType.ToString(),measurement_id=t.MeasurementId,
                target_frame_rate=Application.targetFrameRate,v_sync_count=QualitySettings.vSyncCount,
                sampled_frames=t.SampleCount,sampled_duration_s=t.MeasuredSeconds,average_frame_ms=t.AverageFrameTimeMs,
                average_fps=t.AverageFps,max_frame_ms=t.MaximumFrameTimeMs,warmup_s=t.warmupSeconds,
                application_focused_at_snapshot=Application.isFocused
            };
            Check(t.SampleCount>10 && t.AverageFps>0 && t.MaximumFrameTimeMs>=t.AverageFrameTimeMs,"frame-time/FPS sample collected");
            Check(System.Math.Abs(t.AverageFrameTimeMs*t.AverageFps-1000d)<.0001,"FPS calculated from total sampled duration");
            yield return new WaitForSecondsRealtime(.7f);
            Check(t.WriterReady && t.WriterError==null && t.WrittenRows>t.RecordedCommands && t.DroppedRows==0,"background CSV writer flushed rows without drops/errors");
            report.passed_checks=passed;report.failed_checks=failed;
            File.WriteAllText("Evidence/TelemetryPerformance.json",JsonUtility.ToJson(report,true));
            Debug.Log("TELEMETRY MEASURED: N="+t.SampleCount+" mean_ms="+t.AverageFrameTimeMs+" FPS="+t.AverageFps+" max_ms="+t.MaximumFrameTimeMs+" csv="+t.CsvPath);
            t.enabled=false;
            t.sessionLabel="development_live";t.trialId="unassigned";t.automatedTestSession=false;t.SetSamplingEnabled(true);t.enabled=true;
            File.WriteAllText("Evidence/TelemetryLivePath.txt",t.CsvPath);
        }
        finally
        {
            cam.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            if(keyboard!=null && keyboard.added)InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.editorInputBehaviorInPlayMode=oldInput;InputSystem.settings.backgroundBehavior=oldBackground;Application.runInBackground=oldRun;
            voice.StartListening();
            Debug.Log("TELEMETRY TEST COMPLETE: passed="+passed+" failed="+failed);
        }
    }
    public void Execute(ExecutionResult result)
    {
        if(!EditorApplication.isPlaying)throw new System.Exception("Play required");
        c=UnityEngine.Object.FindAnyObjectByType<SpatialVoiceIntentController>();voice=c.GetComponent<VoiceCommandRecognizer>();t=c.GetComponent<InteractionTelemetry>();
        oldRun=Application.runInBackground;Application.runInBackground=true;oldInput=InputSystem.settings.editorInputBehaviorInPlayMode;oldBackground=InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard=InputSystem.AddDevice<Keyboard>("IVRAR Telemetry Test");
        c.StartCoroutine(Run());result.Log("Scheduled automated telemetry regression and 12-second frame sampling; synthetic inputs explicitly labeled.");
    }
}
