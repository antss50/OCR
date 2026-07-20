namespace LexVerse.Application.Diagnostics;

public interface IOperationalTelemetry
{
    void Record(OperationalEvent operationalEvent);
}
