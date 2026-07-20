namespace LexVerse.Application.Diagnostics;

public static class OperationalTelemetryExtensions
{
    public static void TryRecord(
        this IOperationalTelemetry telemetry,
        OperationalEvent operationalEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(operationalEvent);
        try
        {
            telemetry.Record(operationalEvent);
        }
        catch
        {
        }
    }
}
