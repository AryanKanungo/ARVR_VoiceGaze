// Shared diagnostics/lifecycle only; each backend uses the existing controller and logger.
public interface IInteractionSpeechProvider
{
    string RecognizerStatus { get; }
    int DroppedCallbackEvents { get; }
    void StartListening();
    void StopListening();
}
