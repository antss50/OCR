using System.Text.Json;
using System.Text.Json.Serialization;
using LexVerse.Core.Product;

namespace LexVerse.Infrastructure.Commerce;

public sealed class RemoteProductCatalogProvider : IProductCatalogProvider
{
    private const int MaximumResponseBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;

    public RemoteProductCatalogProvider(HttpClient httpClient, Uri endpoint, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ValidateHttps(endpoint, nameof(endpoint));
        _httpClient = httpClient;
        _endpoint = endpoint;
        _timeout = timeout ?? TimeSpan.FromSeconds(8);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<ProductCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await HttpJson.ReadBoundedAsync(
            _httpClient, new HttpRequestMessage(HttpMethod.Get, _endpoint), MaximumResponseBytes, _timeout, cancellationToken);
        var wire = JsonSerializer.Deserialize<CatalogWire>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("The product catalog response is empty.");
        if (wire.Plans.Count > 64 || wire.Offers.Count > 256 || wire.Revision.Length > 128 ||
            wire.Plans.Any(plan =>
                plan.Id.Length > 128 || plan.DisplayName.Length > 256 || plan.Features.Count > 64) ||
            wire.Offers.Any(offer =>
                offer.Id.Length > 128 || offer.PlanId.Length > 128 || offer.CurrencyCode.Length > 3))
        {
            throw new InvalidOperationException("The product catalog exceeds client limits.");
        }

        var plans = wire.Plans.Select(plan => new ProductPlan(
            plan.Id,
            plan.DisplayName,
            plan.Features.Select(feature => new FeatureKey(feature)),
            plan.IsPublic));
        var offers = wire.Offers.Select(offer => new ProductOffer(
            offer.Id,
            offer.PlanId,
            new Money(offer.Amount, offer.CurrencyCode),
            ToPeriod(offer.BillingPeriod),
            ToPeriod(offer.TrialPeriod),
            offer.IsPublic));
        return new ProductCatalog(wire.Revision, wire.PublishedAtUtc, plans, offers);
    }

    private static SubscriptionPeriod? ToPeriod(PeriodWire? period) => period is null
        ? null
        : new SubscriptionPeriod(period.Count, Enum.Parse<SubscriptionPeriodUnit>(period.Unit, ignoreCase: true));

    internal static void ValidateHttps(Uri endpoint, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new ArgumentException("Remote commerce endpoints must use absolute HTTPS URIs without embedded credentials.", parameterName);
        }
    }

    private sealed class CatalogWire
    {
        public required string Revision { get; init; }
        public required DateTimeOffset PublishedAtUtc { get; init; }
        public required List<PlanWire> Plans { get; init; }
        public required List<OfferWire> Offers { get; init; }
    }

    private sealed class PlanWire
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required List<string> Features { get; init; }
        public required bool IsPublic { get; init; }
    }

    private sealed class OfferWire
    {
        public required string Id { get; init; }
        public required string PlanId { get; init; }
        public required decimal Amount { get; init; }
        public required string CurrencyCode { get; init; }
        public PeriodWire? BillingPeriod { get; init; }
        public PeriodWire? TrialPeriod { get; init; }
        public required bool IsPublic { get; init; }
    }

    private sealed class PeriodWire
    {
        public required int Count { get; init; }
        public required string Unit { get; init; }
    }
}
