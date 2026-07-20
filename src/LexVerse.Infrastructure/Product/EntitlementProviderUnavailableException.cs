namespace LexVerse.Infrastructure.Product;

public sealed class EntitlementProviderUnavailableException : Exception
{
    public EntitlementProviderUnavailableException()
        : base("No current verified entitlement is available.")
    {
    }

    public EntitlementProviderUnavailableException(Exception innerException)
        : base("No current verified entitlement is available.", innerException)
    {
    }
}
