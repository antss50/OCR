using LexVerse.Application.Diagnostics;

namespace LexVerse.Application.Privacy;

public sealed class ConsentAwareOperationalTelemetry(
    IOperationalTelemetry inner,
    IPrivacyPreferencesService preferences) : IOperationalTelemetry
{
    public void Record(OperationalEvent operationalEvent)
    {
        if (preferences.Current.AllowLocalDiagnostics)
        {
            inner.Record(operationalEvent);
        }
    }
}
