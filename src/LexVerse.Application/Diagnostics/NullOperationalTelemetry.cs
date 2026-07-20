namespace LexVerse.Application.Diagnostics;

public sealed class NullOperationalTelemetry : IOperationalTelemetry
{
    public static NullOperationalTelemetry Instance { get; } = new();

    private NullOperationalTelemetry()
    {
    }

    public void Record(OperationalEvent operationalEvent)
    {
        ArgumentNullException.ThrowIfNull(operationalEvent);
    }
}
