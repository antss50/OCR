using LexVerse.Core.Product;

namespace LexVerse.Application.Product;

/// <summary>
/// Keeps explicitly free capabilities available without weakening paid entitlement checks.
/// </summary>
public sealed class FreeTierFeatureAccessService : IFeatureAccessService
{
    private readonly IFeatureAccessService _paidAccess;
    private readonly HashSet<FeatureKey> _freeFeatures;

    public FreeTierFeatureAccessService(
        IFeatureAccessService paidAccess,
        IEnumerable<FeatureKey> freeFeatures)
    {
        ArgumentNullException.ThrowIfNull(paidAccess);
        ArgumentNullException.ThrowIfNull(freeFeatures);

        _paidAccess = paidAccess;
        _freeFeatures = [.. freeFeatures];
    }

    public ValueTask<FeatureAccessDecision> GetAccessAsync(
        FeatureKey feature,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return _freeFeatures.Contains(feature)
            ? ValueTask.FromResult(FeatureAccessDecision.Allowed(feature, expiresAtUtc: null))
            : _paidAccess.GetAccessAsync(feature, cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        _paidAccess.RefreshAsync(cancellationToken);

    public void Invalidate() => _paidAccess.Invalidate();
}
