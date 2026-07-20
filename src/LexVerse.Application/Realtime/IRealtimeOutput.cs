namespace LexVerse.Application.Realtime;

public interface IRealtimeOutput
{
    Task RenderAsync(
        RealtimeFrameUpdate update,
        bool isPartial,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
