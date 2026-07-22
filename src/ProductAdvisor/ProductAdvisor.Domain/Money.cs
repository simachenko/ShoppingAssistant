namespace ProductAdvisor.Domain;

/// <summary>
/// Advisor's own copy of the Money concept — bounded contexts do not share domain model
/// assemblies, so this is intentionally independent of Pricing and Availability's Money type.
/// Plain class (not a record): EF Core's owned-entity constructor binding (for the persisted
/// ConversationSession.CurrentRequirement.Budget) gets confused by a record's synthesized
/// copy constructor.
/// </summary>
public sealed class Money : IEquatable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be non-negative.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public bool Equals(Money? other) =>
        other is not null && Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) => Equals(obj as Money);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);
}
