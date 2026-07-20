namespace LexVerse.Application.Privacy;

public sealed class RemoteProcessingConsentRequiredException : InvalidOperationException
{
    public RemoteProcessingConsentRequiredException()
        : base("External text processing requires the user's consent.")
    {
    }
}
