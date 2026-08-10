namespace ProductAdvisor.Domain;

/// <summary>
/// Pure, deterministic budget filtering + requirement matching + ranking. No I/O, no LLM call.
/// Called exclusively by the <c>get_recommendations</c> tool handler — never directly by the
/// conversation orchestration loop (research.md §1, plan.md Summary).
/// </summary>
public static class ScoringPolicy
{
    public static Recommendation Score(UserRequirement requirement, IReadOnlyList<ProductCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(candidates);
        if (requirement.Budget is null)
        {
            throw new InvalidOperationException("Cannot score candidates without a known budget.");
        }

        // Two-phase (FR-080–FR-085): (1) hard-constraint filtering — a candidate confirmed to
        // violate budget, currency, a required feature, or (when explicitly stated) an
        // availability requirement is never eligible for Items, no matter how well it otherwise
        // ranks; (2) soft-preference ranking of the survivors only, which never re-excludes
        // anything. Price/availability that could not be verified at all (e.g. a Pricing-service
        // outage) is a different case from a confirmed disqualification — a candidate whose
        // *price* is unverified skips the budget/currency check (nothing to confirm) rather than
        // being excluded or presented as a confirmed match (constitution Principle V, FR-005);
        // required-feature checks still apply, since they read catalog specification data, not
        // pricing data.
        var qualifying = new List<ProductCandidate>();
        var violators = new List<NearestAlternative>();

        foreach (var candidate in candidates)
        {
            var violated = GetViolatedHardConstraints(candidate, requirement);
            if (violated.Count == 0)
            {
                qualifying.Add(candidate);
            }
            else
            {
                violators.Add(new NearestAlternative { Candidate = candidate, ViolatedConstraints = violated });
            }
        }

        if (qualifying.Count == 0)
        {
            return new Recommendation
            {
                RecommendationId = Guid.NewGuid(),
                Items = [],
                UnmetConstraintExplanation = BuildUnmetConstraintExplanation(requirement),
                // FR-082/Assumptions: surfaced only in this empty-Items case, never alongside a
                // non-empty Items — a violator that happens to coexist with genuine matches is
                // simply left out of the response entirely, not shown as an "alternative".
                NearestAlternatives = violators,
            };
        }

        var items = qualifying
            .Select(candidate => BuildRecommendedItem(candidate, requirement, qualifying))
            .OrderByDescending(i => i.Candidate.PriceVerified)
            .ThenByDescending(i => i.Score)
            .ThenBy(i => i.Candidate.Name, StringComparer.Ordinal)
            .ToList();

        return new Recommendation { RecommendationId = Guid.NewGuid(), Items = items, UnmetConstraintExplanation = null };
    }

    /// <summary>
    /// Every hard constraint (FR-080) this candidate is *confirmed* to violate — empty when the
    /// candidate qualifies. Budget/currency can only be confirmed violated when the candidate's
    /// price is itself verified (FR-005); a required feature or an explicit availability
    /// requirement can be confirmed independently of price verification.
    /// </summary>
    private static List<string> GetViolatedHardConstraints(ProductCandidate candidate, UserRequirement requirement)
    {
        var budget = requirement.Budget!;
        var violated = new List<string>();

        if (candidate.PriceVerified && candidate.Price is not null)
        {
            if (candidate.Price.Currency != budget.Currency)
            {
                violated.Add($"currency: priced in {candidate.Price.Currency}, requirement stated {budget.Currency}");
            }
            else if (candidate.Price.Amount > budget.Amount)
            {
                violated.Add($"budget: {candidate.Price.Amount} {candidate.Price.Currency} exceeds {budget.Amount} {budget.Currency} ceiling");
            }
        }

        foreach (var feature in requirement.RequiredFeatures)
        {
            if (!MatchesSpec(candidate, feature))
            {
                violated.Add($"requiredFeature: does not clearly satisfy \"{feature}\"");
            }
        }

        // FR-085: only a hard constraint when the user explicitly stated one; otherwise
        // availability stays informational (FR-012) and never disqualifies.
        if (requirement.AvailabilityRequirements.Count > 0
            && candidate.AvailabilityVerified
            && candidate.Availability != StockStatus.InStock)
        {
            violated.Add("availability: required availability could not be confirmed");
        }

        return violated;
    }

    private static string BuildUnmetConstraintExplanation(UserRequirement requirement)
    {
        var categoryPart = string.IsNullOrWhiteSpace(requirement.Category) ? "" : $" in {requirement.Category}";
        return $"No product{categoryPart} was found at or under {requirement.Budget!.Amount} {requirement.Budget.Currency}.";
    }

    private static RecommendedItem BuildRecommendedItem(
        ProductCandidate candidate, UserRequirement requirement, IReadOnlyList<ProductCandidate> qualifyingSet)
    {
        // Only a *verified* price can honestly be claimed to meet the budget (FR-005) — an
        // unverified candidate skips this claim entirely rather than implying a confirmed match.
        var matched = candidate.PriceVerified
            ? new List<string> { $"budget ≤ {requirement.Budget!.Amount} {requirement.Budget.Currency}" }
            : [];

        // Every required feature is already confirmed matched: a candidate reaching this method
        // has already survived hard-constraint filtering (FR-081), which excludes any candidate
        // that fails to match a required feature.
        matched.AddRange(requirement.RequiredFeatures);

        var matchedPreferenceCount = 0;
        foreach (var preference in requirement.Preferences)
        {
            if (MatchesSpec(candidate, preference))
            {
                matched.Add(preference);
                matchedPreferenceCount++;
            }
        }

        var tradeOffs = BuildTradeOffs(candidate, qualifyingSet);

        var score = (2m * requirement.RequiredFeatures.Count) + (1m * matchedPreferenceCount) - (0.5m * tradeOffs.Count);

        return new RecommendedItem
        {
            Candidate = candidate,
            MatchedRequirements = matched,
            TradeOffs = tradeOffs,
            Score = score,
        };
    }

    private static readonly char[] TokenSeparators = [' ', '_', '-', '.', ',', '!', '?'];

    /// <summary>
    /// Deterministic word-token-overlap match of a free-form requirement/preference phrase
    /// against a candidate's specification keys/values — no LLM involved in deciding a match.
    /// Token overlap (rather than plain substring containment) is what makes this robust to the
    /// LLM phrasing the same requirement differently across calls (e.g. "camera",
    /// "good camera", and "camera quality" must all match the spec key "camera_mp"); a residual
    /// substring check on the value covers numeric/exact-phrase cases token-splitting would miss.
    /// </summary>
    private static bool MatchesSpec(ProductCandidate candidate, string requirementText)
    {
        var requirementTokens = Tokenize(requirementText);

        return candidate.Specifications.Any(s =>
            Tokenize(s.Key).Overlaps(requirementTokens) ||
            Tokenize(s.Value).Overlaps(requirementTokens) ||
            requirementText.Contains(s.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> Tokenize(string text) =>
        text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

    /// <summary>
    /// Every candidate gets at least one trade-off (FR-009): unmatched requirements first, then
    /// any specification where this candidate is strictly below the best value present in the
    /// within-budget set — a genuine, comparative, deterministic disadvantage.
    /// </summary>
    private static List<string> BuildTradeOffs(
        ProductCandidate candidate, IReadOnlyList<ProductCandidate> qualifyingSet)
    {
        var tradeOffs = new List<string>();
        if (!candidate.PriceVerified)
        {
            tradeOffs.Add("Price and availability could not be verified right now.");
        }

        foreach (var spec in candidate.Specifications)
        {
            if (!decimal.TryParse(spec.Value, out var value))
            {
                continue;
            }

            var bestInSet = qualifyingSet
                .SelectMany(c => c.Specifications.Where(s => s.Key == spec.Key))
                .Select(s => decimal.TryParse(s.Value, out var v) ? (decimal?)v : null)
                .Where(v => v is not null)
                .Max();

            if (bestInSet is not null && value < bestInSet)
            {
                var unit = string.IsNullOrEmpty(spec.Unit) ? "" : spec.Unit;
                tradeOffs.Add($"{spec.Key} ({spec.Value}{unit}) is below the best option in this budget ({bestInSet}{unit})");
            }
        }

        if (tradeOffs.Count == 0)
        {
            tradeOffs.Add("No notable trade-off identified versus the other matching candidates.");
        }

        return tradeOffs;
    }
}
