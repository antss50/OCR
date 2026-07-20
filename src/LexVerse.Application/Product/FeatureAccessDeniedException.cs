using LexVerse.Core.Product;

namespace LexVerse.Application.Product;

public sealed class FeatureAccessDeniedException : InvalidOperationException
{
    public FeatureAccessDeniedException(FeatureAccessDecision decision)
        : base(CreateMessage(decision))
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.IsAllowed)
        {
            throw new ArgumentException("An allowed decision cannot create an access-denied exception.", nameof(decision));
        }

        Decision = decision;
    }

    public FeatureAccessDecision Decision { get; }

    private static string CreateMessage(FeatureAccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return $"Access to feature '{decision.Feature}' was denied: {decision.DenialReason}.";
    }
}
