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
    public InteractionCommandResult(InteractionCommandContext context, bool accepted, string reason,
        string target, string destination, DateTime utc, long completedTicks, long startedTicks, float frameMs)
    {
        Context = context; Accepted = accepted; RejectionReason = reason;
        TargetObject = target; DestinationObject = destination; CompletedUtc = utc; FrameTimeMs = frameMs;
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

    public const string Header = "schema_version,record_type,session_id,session_label,trial_id,event_id,automated_test_session,synthetic_input,input_source,command,input_received_utc,result_completed_utc,target_object,destination_object,recognition_confidence,accepted,rejection_reason,recognition_event_to_action_ms,input_event_to_result_ms,command_processing_ms,frame_time_ms,avg_frame_time_ms,avg_fps,sampled_frames,sampled_duration_s,max_frame_time_ms,application_focused,unity_version,platform,phrase_started_utc,phrase_duration_ms,dropped_rows,voice_callback_events_dropped,measurement_id,warmup_s";

    void OnEnable()
    {
        if (!controller) controller = GetComponent<SpatialVoiceIntentController>();
        if (!voiceRecognizer) voiceRecognizer = GetComponent<VoiceCommandRecognizer>();
        SessionId = Guid.NewGuid().ToString("N");
        CsvPath = Path.Combine(Application.persistentDataPath, "Telemetry",
            "interaction_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + SessionId + ".csv");
        sequence = 0; RecordedCommands = 0; reportedError = false; MeasurementId = 0;
        ResetPerformanceMeasurement();
        writer = new CsvWorker(CsvPath, Header);
        if (controller) controller.CommandCompleted += OnCommand;
        Enqueue(BuildRow("session_start", null));
        StartCoroutine(PeriodicSnapshot());
        Debug.Log("Interaction telemetry CSV: " + CsvPath, this);
    }
    // Numeric accumulation only: no formatting, scene searches, collection growth, or file I/O.
    void Update()
    {
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
        RecordedCommands++;
        Enqueue(BuildRow("command", result));
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
            }
        }
    }
    // Explicit snapshot is event-driven and useful for reproducible development measurements.
    public void ExportPerformanceSnapshot() { if (writer != null) Enqueue(BuildRow("performance", null)); }
    public void RequestFlush() { writer?.RequestFlush(); }

    string BuildRow(string recordType, InteractionCommandResult? optional)
    {
        var culture = CultureInfo.InvariantCulture;
        var b = new StringBuilder(700);
        string source = "", command = "", inputUtc = "", completedUtc = DateTime.UtcNow.ToString("O");
        string target = "", destination = "", confidence = "", accepted = "", reason = "", phraseStart = "";
        double? voiceLatency = null, eventLatency = null, processing = null, frameMs = null, phraseMs = null;
        bool synthetic = false;
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
        }
        Cell(b, "1"); Cell(b, recordType); Cell(b, SessionId); Cell(b, sessionLabel); Cell(b, trialId);
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
        b.Length--; return b.ToString();
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

