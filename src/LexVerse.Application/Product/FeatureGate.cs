using LexVerse.Core.Product;

namespace LexVerse.Application.Product;

/// <summary>
/// Central execution boundary for product capabilities. UI visibility is only a convenience;
/// invoking a use case must still pass through this gate.
/// </summary>
public sealed class FeatureGate(IFeatureAccessService accessService)
{
    private readonly IFeatureAccessService _accessService =
        accessService ?? throw new ArgumentNullException(nameof(accessService));

    public async Task ExecuteAsync(
        FeatureKey feature,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await DemandAccessAsync(feature, cancellationToken);
        await action(cancellationToken);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        FeatureKey feature,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await DemandAccessAsync(feature, cancellationToken);
        return await action(cancellationToken);
    }

    private async Task<FeatureAccessDecision> DemandAccessAsync(
        FeatureKey feature,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feature);

        var decision = await _accessService.GetAccessAsync(feature, cancellationToken);
        if (!decision.IsAllowed)
        {
            throw new FeatureAccessDeniedException(decision);
        }

        return decision;
    }
}
