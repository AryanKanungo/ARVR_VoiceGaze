using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine.Windows.Speech;
#endif

// Native callbacks only enqueue data. LateUpdate runs after gaze (10) and keyboard (20).
[DefaultExecutionOrder(30), DisallowMultipleComponent]
public sealed class VoiceCommandRecognizer : MonoBehaviour
{
    public enum RecognitionConfidence { High, Medium, Low }
    public SpatialVoiceIntentController controller;
    public bool startOnEnable = true;
    [Tooltip("Restart recognition after changing this threshold.")]
    public RecognitionConfidence minimumConfidence = RecognitionConfidence.Medium;
    [Min(0f)] public float repeatCooldownSeconds = .75f;
    [Min(.1f)] public float maximumEventAgeSeconds = 2f;
    [Tooltip("Bounded, in-memory diagnostic history; no audio is recorded.")]
    [Range(4, 128)] public int historyCapacity = 32;

    public string RecognizerStatus { get; private set; } = "Stopped";
    public string MicrophoneStatus { get; private set; } = "Not checked";
    public string LastRecognizedCommand { get; private set; } = "None";
    public string LastDisposition { get; private set; } = "None";
    public DateTime LastRecognitionReceivedUtc { get; private set; }
    public DateTime LastPhraseStartedUtc { get; private set; }
    public TimeSpan LastPhraseDuration { get; private set; }
    public int DispatchCount { get; private set; }
    public int SuppressedCount { get; private set; }
    public int DroppedCallbackEvents { get; private set; }

    public readonly struct RecognitionRecord
    {
        public readonly string Command, Confidence, Disposition;
        public readonly DateTime ReceivedUtc, PhraseStartedUtc;
        public readonly TimeSpan Duration;
        public RecognitionRecord(string command, string confidence, string disposition,
            DateTime receivedUtc, DateTime startedUtc, TimeSpan duration)
        {
            Command = command; Confidence = confidence; Disposition = disposition;
            ReceivedUtc = receivedUtc; PhraseStartedUtc = startedUtc; Duration = duration;
        }
    }
    public IEnumerable<RecognitionRecord> RecognitionHistory => history;

    struct PendingEvent
    {
        public int Session, Kind, ConfidenceRank;
        public string Text, Confidence;
        public DateTime ReceivedUtc, StartedUtc;
        public TimeSpan Duration;
        public long ReceivedTicks;
    }
    readonly ConcurrentQueue<PendingEvent> pending = new ConcurrentQueue<PendingEvent>();
    readonly Queue<RecognitionRecord> history = new Queue<RecognitionRecord>(32);
    public static readonly string[] CommandKeywords = { "select", "place", "release", "reset",
        "rotate left", "rotate right", "rotate 45 degrees", "rotate 90 degrees",
        "move up", "move down", "move left", "move right" };
    readonly long[] lastPhraseTicks = new long[CommandKeywords.Length];
    readonly long[] lastDispatchTicks = new long[CommandKeywords.Length];
    int session, accepting, queued, dropped;
    bool initialized, faulted;
    float nextHealthCheck;
    string heardSummary = "None";
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    KeywordRecognizer recognizer;
    PhraseRecognizer.PhraseRecognizedDelegate phraseHandler;
    PhraseRecognitionSystem.ErrorDelegate errorHandler;
    PhraseRecognitionSystem.StatusDelegate statusHandler;
#endif

    void OnEnable()
    {
        if (!controller) controller = GetComponent<SpatialVoiceIntentController>();
        if (startOnEnable) StartListening();
        else Publish();
    }

    [ContextMenu("Start / Retry Voice Recognition")]
    public void StartListening()
    {
        if (!isActiveAndEnabled) return;
        StopNative();
        faulted = false;
        if (!controller) { SetStatus("No interaction controller"); return; }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            if (!PhraseRecognitionSystem.isSupported)
            {
                MicrophoneStatus = "Unavailable";
                SetStatus("Windows speech unsupported");
                return;
            }
            // Enumeration indicates device presence, not permission or actual capture success.
            MicrophoneStatus = Microphone.devices.Length > 0 ? "Default input detected" : "No input detected";
            if (Microphone.devices.Length == 0) { SetStatus("Connect microphone, then retry"); return; }
            int thisSession = Volatile.Read(ref session);
            recognizer = new KeywordRecognizer(CommandKeywords,
                NativeConfidence(minimumConfidence));
            phraseHandler = args => {
                long receivedTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                DateTime receivedUtc = DateTime.UtcNow;
                Enqueue(new PendingEvent {
                Session = thisSession, Kind = 0, Text = args.text,
                Confidence = args.confidence.ToString(), ConfidenceRank = ConfidenceRank(args.confidence),
                StartedUtc = args.phraseStartTime.ToUniversalTime(), Duration = args.phraseDuration,
                ReceivedUtc = receivedUtc, ReceivedTicks = receivedTicks
                });
            };
            errorHandler = error => Enqueue(new PendingEvent {
                Session = thisSession, Kind = 1, Text = error.ToString()
            });
            statusHandler = status => Enqueue(new PendingEvent {
                Session = thisSession, Kind = 2, Text = status.ToString()
            });
            recognizer.OnPhraseRecognized += phraseHandler;
            PhraseRecognitionSystem.OnError += errorHandler;
            PhraseRecognitionSystem.OnStatusChanged += statusHandler;
            Volatile.Write(ref accepting, 1);
            recognizer.Start();
            initialized = true;
            nextHealthCheck = 0;
            SetStatus(recognizer.IsRunning ? "Listening" : "Starting");
        }
        catch (Exception exception)
        {
            StopNative();
            faulted = true;
            Debug.LogWarning("Voice initialization failed: " + exception.Message, this);
            SetStatus("Start failed; check mic access/language");
        }
#else
        MicrophoneStatus = "Windows only";
        SetStatus("Unsupported platform; keyboard available");
#endif
    }

    // ConcurrentQueue is bounded by reservation; callbacks never call Unity object APIs.
    void Enqueue(PendingEvent item)
    {
        if (Volatile.Read(ref accepting) == 0 || item.Session != Volatile.Read(ref session)) return;
        if (Interlocked.Increment(ref queued) > 32)
        {
            Interlocked.Decrement(ref queued);
            Interlocked.Increment(ref dropped);
            return;
        }
        pending.Enqueue(item);
    }

    void LateUpdate()
    {
        for (int i = 0; i < 8 && pending.TryDequeue(out PendingEvent item); i++)
        {
            Interlocked.Decrement(ref queued);
            if (item.Session != Volatile.Read(ref session) || !initialized || faulted) continue;
            if (item.Kind == 1)
            {
                faulted = true;
                StopNative();
                SetStatus("Error: " + item.Text + "; retry after fixing input");
                Debug.LogWarning("Windows speech error: " + item.Text, this);
                break;
            }
            if (item.Kind == 2) { SetStatus("System " + item.Text); continue; }
            ProcessPhrase(item);
        }
        int droppedNow = Interlocked.Exchange(ref dropped, 0);
        if (droppedNow > 0)
        {
            DroppedCallbackEvents += droppedNow;
            SetStatus("Input backlog dropped; keyboard available");
        }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (initialized && !faulted && Time.unscaledTime >= nextHealthCheck)
        {
            nextHealthCheck = Time.unscaledTime + 1f;
            try
            {
                string value = recognizer.IsRunning && PhraseRecognitionSystem.Status == SpeechSystemStatus.Running
                    ? "Listening" : "Stopped; retry recognition";
                if (RecognizerStatus != value) SetStatus(value);
            }
            catch (Exception exception)
            {
                faulted = true;
                StopNative();
                SetStatus("Recognition unavailable; retry");
                Debug.LogWarning("Voice health check failed: " + exception.Message, this);
            }
        }
#endif
    }

    void ProcessPhrase(PendingEvent item)
    {
        int command = CommandIndex(item.Text);
        LastRecognizedCommand = item.Text;
        LastRecognitionReceivedUtc = item.ReceivedUtc;
        LastPhraseStartedUtc = item.StartedUtc;
        LastPhraseDuration = item.Duration;
        string disposition;
        var context = new InteractionCommandContext(InteractionInputSource.Voice, item.Text,
            item.ReceivedUtc, item.ReceivedTicks, item.Confidence, item.StartedUtc, item.Duration.TotalMilliseconds);
        InteractionCommandResult? actionResult = null;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double age = (now - item.ReceivedTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (command < 0) disposition = "unknown";
        else if (item.ConfidenceRank > (int)minimumConfidence) disposition = "confidence filtered";
        else if (age > maximumEventAgeSeconds) disposition = "expired";
        else if ((lastPhraseTicks[command] != 0 && item.StartedUtc.Ticks <= lastPhraseTicks[command]) ||
            (lastDispatchTicks[command] != 0 && (item.ReceivedTicks - lastDispatchTicks[command]) /
                (double)System.Diagnostics.Stopwatch.Frequency < repeatCooldownSeconds))
            disposition = "repeat ignored";
        else if (!controller || !controller.isActiveAndEnabled) disposition = "controller unavailable";
        else
        {
            lastPhraseTicks[command] = item.StartedUtc.Ticks;
            lastDispatchTicks[command] = item.ReceivedTicks;
            // The shared controller owns validity, actions and the result event used by telemetry.
            actionResult = controller.ExecuteCommand(context);
            DispatchCount++;
            disposition = actionResult.Value.Accepted ? "accepted" : "rejected: " + actionResult.Value.RejectionReason;
        }
        if (!actionResult.HasValue)
        {
            SuppressedCount++;
            if (controller) controller.RecordRejectedCommand(context, disposition);
        }
        LastDisposition = disposition;
        while (history.Count >= Mathf.Max(4, historyCapacity)) history.Dequeue();
        history.Enqueue(new RecognitionRecord(item.Text, item.Confidence, disposition,
            item.ReceivedUtc, item.StartedUtc, item.Duration));
        heardSummary = item.Text + " " + item.ReceivedUtc.ToString("HH:mm:ss.fff") + " UTC (" + disposition + ")";
        Debug.Log("Voice event " + item.ReceivedUtc.ToString("O") + " phraseStart=" +
            item.StartedUtc.ToString("O") + " duration=" + item.Duration.TotalSeconds +
            " command=" + item.Text + " confidence=" + item.Confidence + " " + disposition, this);
        Publish();
    }

    static int CommandIndex(string command)
    {
        for (int i = 0; i < CommandKeywords.Length; i++)
            if (string.Equals(command, CommandKeywords[i], StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    static ConfidenceLevel NativeConfidence(RecognitionConfidence value)
    {
        switch (value)
        {
            case RecognitionConfidence.High: return ConfidenceLevel.High;
            case RecognitionConfidence.Low: return ConfidenceLevel.Low;
            default: return ConfidenceLevel.Medium;
        }
    }
    static int ConfidenceRank(ConfidenceLevel value)
    {
        switch (value)
        {
            case ConfidenceLevel.High: return 0;
            case ConfidenceLevel.Medium: return 1;
            case ConfidenceLevel.Low: return 2;
            default: return 3;
        }
    }
#endif

    [ContextMenu("Stop Voice Recognition")]
    public void StopListening() { StopNative(); SetStatus("Stopped; keyboard available"); }

    void StopNative()
    {
        Volatile.Write(ref accepting, 0);
        Interlocked.Increment(ref session);
        initialized = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        PhraseRecognitionSystem.OnError -= errorHandler;
        PhraseRecognitionSystem.OnStatusChanged -= statusHandler;
        if (recognizer != null)
        {
            recognizer.OnPhraseRecognized -= phraseHandler;
            try { if (recognizer.IsRunning) recognizer.Stop(); }
            catch (Exception exception) { Debug.LogWarning("Voice stop: " + exception.Message, this); }
            try { recognizer.Dispose(); }
            catch (Exception exception) { Debug.LogWarning("Voice dispose: " + exception.Message, this); }
            recognizer = null;
        }
        phraseHandler = null; errorHandler = null; statusHandler = null;
#endif
        while (pending.TryDequeue(out _)) Interlocked.Decrement(ref queued);
        // Never shut down the shared PhraseRecognitionSystem: other recognizers may use it.
    }

    void SetStatus(string value) { RecognizerStatus = value; Publish(); }
    void Publish()
    {
        if (controller) controller.SetVoiceFeedback(
            "Voice: " + RecognizerStatus + " | Mic: " + MicrophoneStatus,
            "Heard: " + heardSummary);
    }
    void OnDisable() { StopListening(); }
    void OnDestroy() { StopNative(); }
    void OnApplicationQuit() { StopNative(); }
}

