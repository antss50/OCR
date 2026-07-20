namespace LexVerse.Infrastructure.Product;

public sealed class EntitlementVerificationException : Exception
{
    public EntitlementVerificationException(string message)
        : base(message)
    {
    }

    public EntitlementVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
