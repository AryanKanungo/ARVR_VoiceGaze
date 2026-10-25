using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

public enum InteractionInputSource { Voice, Keyboard, System }

public readonly struct InteractionCommandContext
{
    public readonly InteractionInputSource Source;
    public readonly string Command, Confidence;
    public readonly DateTime ReceivedUtc;
    public readonly long ReceivedTicks;
    public readonly DateTime? PhraseStartedUtc;
    public readonly double? PhraseDurationMs;
    public readonly bool Synthetic;
    public InteractionCommandContext(InteractionInputSource source, string command, DateTime utc,
        long ticks, string confidence = "", DateTime? phraseStart = null, double? phraseDurationMs = null, bool synthetic = false)
    {
        Source = source; Command = command; ReceivedUtc = utc; ReceivedTicks = ticks;
        Confidence = confidence; PhraseStartedUtc = phraseStart; PhraseDurationMs = phraseDurationMs; Synthetic = synthetic;
    }
    public static InteractionCommandContext Capture(InteractionInputSource source, string command, bool synthetic = false)
    {
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp();
        return new InteractionCommandContext(source, command, DateTime.UtcNow, ticks, synthetic: synthetic);
    }
}

public readonly struct InteractionCommandResult
{
    public readonly InteractionCommandContext Context;
    public readonly bool Accepted;
    public readonly string RejectionReason, TargetObject, DestinationObject;
    public readonly DateTime CompletedUtc;
    public readonly double ProcessingMs, EventToResultMs;
    public readonly double? RecognitionEventToActionMs;
    public readonly float FrameTimeMs;
    public readonly string SelectedObject;
    public readonly long CompletedTicks;
    public InteractionCommandResult(InteractionCommandContext context, bool accepted, string reason,
        string target, string destination, DateTime utc, long completedTicks, long startedTicks, float frameMs, string selectedObject = "")
    {
        Context = context; Accepted = accepted; RejectionReason = reason;
        TargetObject = target; DestinationObject = destination; CompletedUtc = utc; FrameTimeMs = frameMs;
        SelectedObject = selectedObject; CompletedTicks = completedTicks;
        ProcessingMs = (completedTicks - startedTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        EventToResultMs = Math.Max(0d, (completedTicks - context.ReceivedTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency);
        RecognitionEventToActionMs = context.Source == InteractionInputSource.Voice && accepted ? EventToResultMs : (double?)null;
    }
}

[DefaultExecutionOrder(-50), DisallowMultipleComponent]
public sealed class InteractionTelemetry : MonoBehaviour
{
    public SpatialVoiceIntentController controller;
    public VoiceCommandRecognizer voiceRecognizer;
    [Tooltip("Development data by default. Set explicitly for an approved study protocol.")]
    public string sessionLabel = "development";
    [Tooltip("No implicit human trials are generated. Set with BeginTrial for each planned trial.")]
    public string trialId = "unassigned";
    public bool automatedTestSession;
    [Min(0f)] public float warmupSeconds = 2f;
    [Min(1f)] public float performanceExportIntervalSeconds = 5f;
    public string SessionId { get; private set; }
    public string CsvPath { get; private set; }
    public long SampleCount { get; private set; }
    public int MeasurementId { get; private set; }
    public double MeasuredSeconds { get; private set; }
    public double AverageFrameTimeMs => SampleCount > 0 ? MeasuredSeconds * 1000d / SampleCount : 0d;
    public double AverageFps => MeasuredSeconds > 0d ? SampleCount / MeasuredSeconds : 0d;
    public double MaximumFrameTimeMs { get; private set; }
    public int RecordedCommands { get; private set; }
    public int TotalRecognizedCommands { get; private set; }
    public int InputCommands { get; private set; }
    public int AcceptedInputCommands { get; private set; }
    public int CompletedTasks { get; private set; }
    public int FailedTasks { get; private set; }
    public double? CommandSuccessRate => InputCommands > 0 ? 100d * AcceptedInputCommands / InputCommands : (double?)null;
    public double? TaskFailureRate => CompletedTasks + FailedTasks > 0 ? 100d * FailedTasks / (CompletedTasks + FailedTasks) : (double?)null;
    public double? AverageCompletedTaskSeconds => CompletedTasks > 0 ? completedTaskSeconds / CompletedTasks : (double?)null;
    double completedTaskSeconds;
    int nextTask;
    TaskAttempt activeTask;
    sealed class TaskAttempt
    {
        public string Id, Selected, Destination, Reason;
        public DateTime StartUtc, EndUtc;
        public long StartTicks;
        public int Rejected;
        public bool Success;
        public double Elapsed;
        public InteractionCommandContext Context;
    }
    public int WrittenRows => writer != null ? writer.WrittenRows : lastWrittenRows;
    public int DroppedRows => writer != null ? writer.DroppedRows : lastDroppedRows;
    public int PendingRows => writer != null ? writer.PendingRows : 0;
    public string WriterError => writer != null ? writer.Error : lastWriterError;
    public bool WriterReady => writer != null && writer.Ready;
    public bool SamplingEnabled { get; private set; } = true;
    long sequence;
    double warmupUntil, previousFrameRealtime;
    CsvWorker writer;
    int lastWrittenRows, lastDroppedRows;
    string lastWriterError;
    bool reportedError;

    public const string Header = "schema_version,record_type,session_id,session_label,trial_id,event_id,automated_test_session,synthetic_input,input_source,command,input_received_utc,result_completed_utc,target_object,destination_object,recognition_confidence,accepted,rejection_reason,recognition_event_to_action_ms,input_event_to_result_ms,command_processing_ms,frame_time_ms,avg_frame_time_ms,avg_fps,sampled_frames,sampled_duration_s,max_frame_time_ms,application_focused,unity_version,platform,phrase_started_utc,phrase_duration_ms,dropped_rows,voice_callback_events_dropped,measurement_id,warmup_s,selected_object,callback_to_action_ms,task_id,task_start_utc,task_end_utc,task_completion_s,task_success,task_failure_reason,task_rejected_commands,task_elapsed_s,gaze_distance_m,target_width_m,fitts_id_bits,acquisition_time_s,throughput_bits_s";

    void OnEnable()
    {
        if (!controller) controller = GetComponent<SpatialVoiceIntentController>();
        if (!voiceRecognizer) voiceRecognizer = GetComponent<VoiceCommandRecognizer>();
        SessionId = Guid.NewGuid().ToString("N");
        CsvPath = Path.Combine(Application.persistentDataPath, "Telemetry",
            "interaction_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + SessionId + ".csv");
        sequence = 0; RecordedCommands = 0; reportedError = false; MeasurementId = 0;
        TotalRecognizedCommands = InputCommands = AcceptedInputCommands = CompletedTasks = FailedTasks = nextTask = 0;
        completedTaskSeconds = 0; activeTask = null;
        ResetPerformanceMeasurement();
        writer = new CsvWorker(CsvPath, Header);
        if (controller) controller.CommandCompleted += OnCommand;
        Enqueue(BuildRow("session_start", null));
        StartCoroutine(PeriodicSnapshot());
        Debug.Log("Interaction telemetry CSV: " + CsvPath + " | Fitts: projected BoxCollider geometry only; no explicit acquisition trial, so acquisition time/throughput are N/A.", this);
    }
    // Numeric accumulation only: no formatting, scene searches, collection growth, or file I/O.
    void Update()
    {
        if (activeTask != null && (!controller || !controller.isActiveAndEnabled || !controller.SelectedObject))
            EndTask(false, "abandoned", DateTime.UtcNow, System.Diagnostics.Stopwatch.GetTimestamp(), "");
        double now = Time.realtimeSinceStartupAsDouble;
        double intervalStarted = previousFrameRealtime;
        previousFrameRealtime = now;
        // Exclude an interval that began before warm-up ended (including startup/import stalls).
        if (!SamplingEnabled || intervalStarted < warmupUntil) return;
        double seconds = Time.unscaledDeltaTime;
        if (seconds <= 0d || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
        SampleCount++; MeasuredSeconds += seconds;
        if (seconds * 1000d > MaximumFrameTimeMs) MaximumFrameTimeMs = seconds * 1000d;
    }
    public void ResetPerformanceMeasurement()
    {
        SampleCount = 0; MeasuredSeconds = 0d; MaximumFrameTimeMs = 0d;
        MeasurementId++;
        previousFrameRealtime = Time.realtimeSinceStartupAsDouble;
        warmupUntil = previousFrameRealtime + warmupSeconds;
        if (writer != null) Enqueue(BuildRow("measurement_reset", null));
    }
    public void SetSamplingEnabled(bool value) { SamplingEnabled = value; }
    public void BeginTrial(string id)
    {
        trialId = string.IsNullOrWhiteSpace(id) ? "unassigned" : id;
        if (writer != null) Enqueue(BuildRow("trial_begin", null));
    }
    void OnCommand(InteractionCommandResult result)
    {
        var context = result.Context;
        if (!context.Synthetic && context.Source != InteractionInputSource.System)
        {
            InputCommands++;
            if (result.Accepted) AcceptedInputCommands++;
            if (context.Source == InteractionInputSource.Voice) TotalRecognizedCommands++;
        }
        if (result.Accepted && context.Command == "select")
        {
            if (activeTask != null) EndTask(false, "reselected", result.CompletedUtc, result.CompletedTicks, "");
            activeTask = new TaskAttempt { Id = SessionId + "-" + (++nextTask), Selected = result.TargetObject,
                StartUtc = result.CompletedUtc, StartTicks = result.CompletedTicks, Context = context };
        }
        if (activeTask != null && !result.Accepted) activeTask.Rejected++;
        RecordedCommands++;
        Enqueue(BuildRow("command", result));
        if (result.Accepted && context.Command == "place")
            EndTask(true, "", result.CompletedUtc, result.CompletedTicks, result.DestinationObject);
        else if (result.Accepted && (context.Command == "release" || context.Command == "reset"))
            EndTask(false, context.Command + "_before_place", result.CompletedUtc, result.CompletedTicks, "");
    }
    public void AbandonCurrentTask()
    { EndTask(false, "abandoned", DateTime.UtcNow, System.Diagnostics.Stopwatch.GetTimestamp(), ""); }
    void EndTask(bool success, string reason, DateTime endUtc, long endTicks, string destination)
    {
        if (activeTask == null) return;
        var task = activeTask;
        task.Success = success; task.Reason = reason; task.EndUtc = endUtc; task.Destination = destination;
        task.Elapsed = Math.Max(0d, (endTicks - task.StartTicks) / (double)System.Diagnostics.Stopwatch.Frequency);
        if (success) { CompletedTasks++; completedTaskSeconds += task.Elapsed; } else FailedTasks++;
        Enqueue(BuildRow("task", null, task));
        activeTask = null;
    }
    IEnumerator PeriodicSnapshot()
    {
        var wait = new WaitForSecondsRealtime(1f);
        double lastExport = Time.realtimeSinceStartupAsDouble;
        while (true)
        {
            yield return wait;
            if (Time.realtimeSinceStartupAsDouble - lastExport >= performanceExportIntervalSeconds)
            {
                Enqueue(BuildRow("performance", null));
                lastExport = Time.realtimeSinceStartupAsDouble;
            }
            string error = WriterError;
            if (error != null && !reportedError) { reportedError = true; Debug.LogWarning("Telemetry writer: " + error, this); }
            if (controller)
            {
                string performance = SampleCount == 0 ? "Performance: warming up" :
                    "Frame avg: " + AverageFrameTimeMs.ToString("F2") + " ms | " + AverageFps.ToString("F1") +
                    " FPS | N=" + SampleCount;
                string logging = error != null ? "CSV ERROR: " + error :
                    "CSV: " + (WriterReady ? "recording" : "opening") + " | rows " + WrittenRows +
                    " | dropped " + DroppedRows;
                controller.SetPerformanceFeedback(performance, logging);
                controller.SetTaskFeedback("Recognized: " + TotalRecognizedCommands + " | Command success: " + Metric(CommandSuccessRate, "%") +
                    "\nCompleted tasks: " + CompletedTasks + " | Failed tasks: " + FailedTasks + " | Task failure: " + Metric(TaskFailureRate, "%") +
                    "\nAvg completed task: " + Metric(AverageCompletedTaskSeconds, " s"));
            }
        }
    }
    // Explicit snapshot is event-driven and useful for reproducible development measurements.
    public void ExportPerformanceSnapshot() { if (writer != null) Enqueue(BuildRow("performance", null)); }
    public void RequestFlush() { writer?.RequestFlush(); }

    string BuildRow(string recordType, InteractionCommandResult? optional, TaskAttempt taskRecord = null)
    {
        var culture = CultureInfo.InvariantCulture;
        var b = new StringBuilder(700);
        string source = "", command = "", inputUtc = "", completedUtc = DateTime.UtcNow.ToString("O");
        string target = "", destination = "", confidence = "", accepted = "", reason = "", phraseStart = "";
        double? voiceLatency = null, eventLatency = null, processing = null, frameMs = null, phraseMs = null;
        bool synthetic = false;
        string selected = "";
        double? distance = null, width = null, difficulty = null;
        if (optional.HasValue)
        {
            var r = optional.Value; var c = r.Context;
            source = c.Source.ToString().ToLowerInvariant(); command = c.Command;
            inputUtc = c.ReceivedUtc.ToString("O"); completedUtc = r.CompletedUtc.ToString("O");
            target = r.TargetObject; destination = r.DestinationObject; confidence = c.Confidence;
            accepted = r.Accepted ? "true" : "false"; reason = r.RejectionReason;
            voiceLatency = r.RecognitionEventToActionMs; eventLatency = r.EventToResultMs;
            processing = r.ProcessingMs; frameMs = r.FrameTimeMs;
            phraseStart = c.PhraseStartedUtc.HasValue ? c.PhraseStartedUtc.Value.ToString("O") : "";
            phraseMs = c.PhraseDurationMs; synthetic = c.Synthetic;
            selected = r.SelectedObject;
            if (r.Accepted && c.Command == "select") MeasureGeometry(out distance, out width, out difficulty);
        }
        if (taskRecord != null)
        {
            source = taskRecord.Context.Source.ToString().ToLowerInvariant(); command = "select_place";
            inputUtc = taskRecord.StartUtc.ToString("O"); completedUtc = taskRecord.EndUtc.ToString("O");
            target = selected = taskRecord.Selected; destination = taskRecord.Destination;
            synthetic = taskRecord.Context.Synthetic;
        }
        Cell(b, "2"); Cell(b, recordType); Cell(b, SessionId); Cell(b, sessionLabel); Cell(b, trialId);
        Cell(b, (++sequence).ToString(culture)); Cell(b, automatedTestSession ? "true" : "false");
        Cell(b, synthetic ? "true" : "false"); Cell(b, source); Cell(b, command);
        Cell(b, inputUtc); Cell(b, completedUtc); Cell(b, target); Cell(b, destination); Cell(b, confidence);
        Cell(b, accepted); Cell(b, reason); Cell(b, Number(voiceLatency)); Cell(b, Number(eventLatency));
        Cell(b, Number(processing)); Cell(b, Number(frameMs)); Cell(b, SampleCount > 0 ? Number(AverageFrameTimeMs) : "");
        Cell(b, SampleCount > 0 ? Number(AverageFps) : ""); Cell(b, SampleCount.ToString(culture));
        Cell(b, Number(MeasuredSeconds)); Cell(b, SampleCount > 0 ? Number(MaximumFrameTimeMs) : "");
        Cell(b, Application.isFocused ? "true" : "false"); Cell(b, Application.unityVersion);
        Cell(b, Application.platform.ToString()); Cell(b, phraseStart); Cell(b, Number(phraseMs));
        Cell(b, DroppedRows.ToString(culture));
        Cell(b, voiceRecognizer ? voiceRecognizer.DroppedCallbackEvents.ToString(culture) : "");
        Cell(b, MeasurementId.ToString(culture)); Cell(b, Number(warmupSeconds));
        Cell(b, selected); Cell(b, voiceLatency.HasValue ? Number(voiceLatency) : "N/A");
        var task = taskRecord ?? activeTask;
        Cell(b, task != null ? task.Id : ""); Cell(b, task != null ? task.StartUtc.ToString("O") : "");
        Cell(b, taskRecord != null ? task.EndUtc.ToString("O") : "");
        Cell(b, taskRecord != null && task.Success ? Number(task.Elapsed) : "N/A");
        Cell(b, taskRecord != null ? (task.Success ? "true" : "false") : "");
        Cell(b, taskRecord != null ? task.Reason : ""); Cell(b, task != null ? task.Rejected.ToString(culture) : "");
        Cell(b, taskRecord != null ? Number(task.Elapsed) : "N/A");
        Cell(b, distance.HasValue ? Number(distance) : "N/A"); Cell(b, width.HasValue ? Number(width) : "N/A");
        Cell(b, difficulty.HasValue ? Number(difficulty) : "N/A");
        // A Select->Place task is not a deliberate target-acquisition trial. Never substitute stabilization delay.
        Cell(b, "N/A"); Cell(b, "N/A");
        b.Length--; return b.ToString();
    }
    static string Metric(double? value, string suffix)
    { return value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) + suffix : "N/A"; }
    void MeasureGeometry(out double? distance, out double? width, out double? difficulty)
    {
        distance = width = difficulty = null;
        if (!controller || !controller.resolver || !controller.resolver.gazeCamera || !controller.SelectedObject) return;
        var box = controller.SelectedObject.GetComponent<BoxCollider>();
        if (!box) return; // Non-box silhouettes are not approximated by a misleading world AABB.
        var camera = controller.resolver.gazeCamera.transform;
        var tr = box.transform; Vector3 half = box.size * .5f;
        // W is the horizontal silhouette extent along camera-right on the plane perpendicular to the gaze ray.
        Vector3 right = camera.right;
        double w = 2d * (Math.Abs(Vector3.Dot(tr.TransformVector(Vector3.right * half.x), right)) +
            Math.Abs(Vector3.Dot(tr.TransformVector(Vector3.up * half.y), right)) +
            Math.Abs(Vector3.Dot(tr.TransformVector(Vector3.forward * half.z), right)));
        double d = Vector3.Distance(camera.position, tr.TransformPoint(box.center));
        if (w <= 0d) return;
        distance = d; width = w; difficulty = Math.Log(d / w + 1d, 2d);
    }
    static string Number(double? value) { return value.HasValue ? value.Value.ToString("F6", CultureInfo.InvariantCulture) : ""; }
    static void Cell(StringBuilder b, string value)
    {
        b.Append('"');
        if (value != null) foreach (char c in value) { if (c == '"') b.Append('"'); b.Append(c); }
        b.Append('"').Append(',');
    }
    void Enqueue(string row) { writer?.Enqueue(row); }
    void OnDisable()
    {
        StopAllCoroutines();
        if (controller) controller.CommandCompleted -= OnCommand;
        if (writer == null) return;
        EndTask(false, "session_ended_before_place", DateTime.UtcNow, System.Diagnostics.Stopwatch.GetTimestamp(), "");
        Enqueue(BuildRow("session_end", null));
        var closing = writer;
        if (!closing.Stop()) Debug.LogWarning("Telemetry flush timeout; background writer will finish pending rows.", this);
        lastWrittenRows = closing.WrittenRows; lastDroppedRows = closing.DroppedRows; lastWriterError = closing.Error;
        writer = null;
    }
    void OnApplicationQuit() { OnDisable(); }

    // Worker owns all directory creation, file writes and flushes; no Unity API is called here.
    sealed class CsvWorker
    {
        readonly ConcurrentQueue<string> rows = new ConcurrentQueue<string>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread thread;
        readonly string path, header;
        int stopping, pending, written, dropped, ready;
        string error;
        public int WrittenRows => Volatile.Read(ref written);
        public int DroppedRows => Volatile.Read(ref dropped);
        public int PendingRows => Volatile.Read(ref pending);
        public bool Ready => Volatile.Read(ref ready) != 0;
        public string Error => Volatile.Read(ref error);
        public CsvWorker(string outputPath, string csvHeader)
        {
            path = outputPath; header = csvHeader;
            thread = new Thread(Run) { IsBackground = true, Name = "IVRAR telemetry CSV writer" };
            thread.Start();
        }
        public void Enqueue(string row)
        {
            if (Error != null || Volatile.Read(ref stopping) != 0) { Interlocked.Increment(ref dropped); return; }
            if (Interlocked.Increment(ref pending) > 4096)
            { Interlocked.Decrement(ref pending); Interlocked.Increment(ref dropped); return; }
            rows.Enqueue(row); wake.Set();
        }
        public void RequestFlush() { if (Volatile.Read(ref stopping) == 0) wake.Set(); }
        void Run()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var output = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    output.WriteLine(header); output.Flush(); Volatile.Write(ref ready, 1);
                    do
                    {
                        wake.WaitOne(500);
                        while (rows.TryDequeue(out string row))
                        {
                            Interlocked.Decrement(ref pending);
                            output.WriteLine(row); Interlocked.Increment(ref written);
                        }
                        output.Flush();
                    } while (Volatile.Read(ref stopping) == 0 || !rows.IsEmpty);
                }
            }
            catch (Exception exception) { Volatile.Write(ref error, exception.GetType().Name + ": " + exception.Message); }
        }
        public bool Stop()
        {
            if (Interlocked.Exchange(ref stopping, 1) == 0) wake.Set();
            bool finished = thread.Join(2000);
            if (finished) wake.Dispose();
            return finished;
        }
    }
}

