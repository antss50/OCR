using LexVerse.Core.Product;

namespace LexVerse.Application.Product;

public interface IFeatureAccessService
{
    ValueTask<FeatureAccessDecision> GetAccessAsync(
        FeatureKey feature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a refresh after sign-in, checkout, restore-purchase, or a user retry.
    /// Provider failures are propagated so the caller can present a useful recovery state.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Immediately removes cached paid grants, for example after sign-out.</summary>
    void Invalidate();
}
