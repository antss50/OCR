namespace LexVerse.Application.Realtime;

public enum RealtimeCoordinatorStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed record RealtimeCoordinatorState(
    RealtimeCoordinatorStatus Status,
    RealtimeCaptureKind? CaptureKind = null,
    string? ErrorCode = null)
{
    public static RealtimeCoordinatorState Stopped { get; } = new(RealtimeCoordinatorStatus.Stopped);
}
