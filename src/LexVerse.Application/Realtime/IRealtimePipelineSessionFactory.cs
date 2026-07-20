namespace LexVerse.Application.Realtime;

public interface IRealtimePipelineSessionFactory
{
    Task<IRealtimePipelineSession> CreateAsync(
        RealtimeTranslationRequest request,
        CancellationToken cancellationToken = default);
}
