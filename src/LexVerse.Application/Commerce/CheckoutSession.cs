namespace LexVerse.Application.Commerce;

public sealed record CheckoutSession
{
    public CheckoutSession(Uri checkoutUri, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(checkoutUri);
        if (!checkoutUri.IsAbsoluteUri || checkoutUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(checkoutUri.UserInfo) || checkoutUri.AbsoluteUri.Length > 4096)
        {
            throw new ArgumentException("Checkout must use an absolute HTTPS URI.", nameof(checkoutUri));
        }

        if (expiresAtUtc == default)
        {
            throw new ArgumentException("Checkout expiry is required.", nameof(expiresAtUtc));
        }

        CheckoutUri = checkoutUri;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Uri CheckoutUri { get; }

    public DateTimeOffset ExpiresAtUtc { get; }
}
