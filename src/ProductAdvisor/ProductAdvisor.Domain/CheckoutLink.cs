namespace ProductAdvisor.Domain;

/// <summary>The typed result of a <c>generate_checkout_link</c> tool call (FR-025, US4) — a
/// deterministic URL built from already-resolved product ids, never authored by the LLM.</summary>
public sealed record CheckoutLink
{
    public required string Url { get; init; }

    public required IReadOnlyList<Guid> ProductIds { get; init; }
}
