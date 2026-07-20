namespace LexVerse.Core.Product;

public sealed record Money
{
    public Money(decimal amount, string currencyCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money amount cannot be negative.");
        }

        var normalizedCurrency = currencyCode.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("Currency code must be a three-letter ISO-style code.", nameof(currencyCode));
        }

        Amount = amount;
        CurrencyCode = normalizedCurrency;
    }

    public decimal Amount { get; }

    public string CurrencyCode { get; }
}
