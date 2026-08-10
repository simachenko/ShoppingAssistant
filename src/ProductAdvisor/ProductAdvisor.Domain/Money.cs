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

    /// <summary>
    /// Non-throwing counterpart of the constructor above, for content the system does not
    /// already trust — specifically, a language model's extracted budget/currency (spec.md
    /// FR-108: "strictly validate currency... budget... a value outside its valid format, range,
    /// or set MUST be rejected rather than passed through"). Unlike the constructor (a 3-letter
    /// sanity check only, used for already-trusted internal data such as a Pricing-service
    /// response), this additionally checks against a known currency set — an invalid amount or
    /// currency returns <c>false</c> instead of throwing, so a fabricated/malformed value from
    /// extraction never crashes the turn; the caller decides what happens next (e.g., treat the
    /// budget as still unknown and route to clarification) rather than an unhandled exception
    /// deciding it via a 500.
    /// </summary>
    public static bool TryCreate(decimal amount, string? currency, out Money? money)
    {
        if (amount >= 0 && IsKnownCurrencyCode(currency))
        {
            money = new Money(amount, currency!);
            return true;
        }

        money = null;
        return false;
    }

    /// <summary>
    /// A representative, commonly-used subset of ISO 4217 codes (spec.md FR-108's "known ISO
    /// 4217 set") — not the full ~180-code standard list, but enough to validate the currencies
    /// this system actually supports; extend as new markets are supported.
    /// </summary>
    public static bool IsKnownCurrencyCode(string? currency) =>
        currency is not null && KnownCurrencyCodes.Contains(currency.ToUpperInvariant());

    private static readonly HashSet<string> KnownCurrencyCodes =
    [
        "USD", "EUR", "GBP", "UAH", "PLN", "CAD", "AUD", "CHF", "JPY", "CNY",
        "CZK", "SEK", "NOK", "DKK", "HUF", "RON", "TRY", "ILS", "INR", "BRL",
    ];
}
