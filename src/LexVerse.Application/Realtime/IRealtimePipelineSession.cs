namespace LexVerse.Application.Realtime;

public interface IRealtimePipelineSession : IAsyncDisposable
{
    Task<RealtimeFrameUpdate> ProcessNextAsync(
        Func<RealtimeFrameUpdate, CancellationToken, Task>? partialResultHandler = null,
        CancellationToken cancellationToken = default);
}
