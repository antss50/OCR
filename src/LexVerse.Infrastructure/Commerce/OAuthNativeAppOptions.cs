namespace LexVerse.Infrastructure.Commerce;

public sealed record OAuthNativeAppOptions
{
    public OAuthNativeAppOptions(
        Uri authorizationEndpoint,
        Uri tokenEndpoint,
        Uri accountEndpoint,
        string clientId,
        IEnumerable<string> scopes)
    {
        RemoteProductCatalogProvider.ValidateHttps(authorizationEndpoint, nameof(authorizationEndpoint));
        RemoteProductCatalogProvider.ValidateHttps(tokenEndpoint, nameof(tokenEndpoint));
        RemoteProductCatalogProvider.ValidateHttps(accountEndpoint, nameof(accountEndpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(scopes);
        var normalizedScopes = scopes
            .Select(scope => scope.Trim())
            .Where(scope => scope.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedScopes.Length == 0 || normalizedScopes.Any(scope => scope.Any(char.IsWhiteSpace)))
        {
            throw new ArgumentException("At least one valid OAuth scope is required.", nameof(scopes));
        }

        AuthorizationEndpoint = authorizationEndpoint;
        TokenEndpoint = tokenEndpoint;
        AccountEndpoint = accountEndpoint;
        ClientId = clientId.Trim();
        Scopes = normalizedScopes;
    }

    public Uri AuthorizationEndpoint { get; }
    public Uri TokenEndpoint { get; }
    public Uri AccountEndpoint { get; }
    public string ClientId { get; }
    public IReadOnlyList<string> Scopes { get; }
}
