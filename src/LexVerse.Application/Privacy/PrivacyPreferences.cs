namespace LexVerse.Application.Privacy;

public sealed record PrivacyPreferences(
    bool AllowRemoteTextProcessing = false,
    bool AllowLocalDiagnostics = true)
{
    public const int CurrentSchemaVersion = 1;
}
