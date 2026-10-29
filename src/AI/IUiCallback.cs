namespace AIVoxelStudio.AI;

public interface IUiCallback
{
    void OnDelta(string text);
    void OnReasoningDelta(string text);
    void OnAssistantMessage(string text);
    void OnToolStarted(string name, string arguments);
    void OnToolFinished(string name, bool ok, string result);
    void OnSnapshot(string attachment);
    void OnError(string message);
    void OnStopped();
    void OnBusyChanged(bool busy);
}
