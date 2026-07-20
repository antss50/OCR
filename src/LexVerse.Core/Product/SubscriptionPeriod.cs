namespace LexVerse.Core.Product;

public enum SubscriptionPeriodUnit
{
    Day,
    Week,
    Month,
    Year
}

public sealed record SubscriptionPeriod
{
    public SubscriptionPeriod(int count, SubscriptionPeriodUnit unit)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Subscription period count must be positive.");
        }

        Count = count;
        Unit = unit;
    }

    public int Count { get; }

    public SubscriptionPeriodUnit Unit { get; }
}
