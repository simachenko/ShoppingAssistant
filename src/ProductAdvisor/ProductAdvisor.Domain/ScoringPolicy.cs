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

        var budget = requirement.Budget;

        // Hard exclude: over-budget or unverified/mismatched-currency candidates never appear as
        // a recommendation (FR-007) — no product disqualified this way is ever presented as a match.
        var withinBudget = candidates
            .Where(c => c.PriceVerified && c.Price is not null
                        && c.Price.Currency == budget.Currency
                        && c.Price.Amount <= budget.Amount)
            .ToList();

        if (withinBudget.Count == 0)
        {
            return new Recommendation
            {
                RecommendationId = Guid.NewGuid(),
                Items = [],
                UnmetConstraintExplanation = BuildUnmetConstraintExplanation(requirement),
            };
        }

        var items = withinBudget
            .Select(candidate => BuildRecommendedItem(candidate, requirement, withinBudget))
            .OrderByDescending(i => i.Score)
            .ThenBy(i => i.Candidate.Name, StringComparer.Ordinal)
            .ToList();

        return new Recommendation { RecommendationId = Guid.NewGuid(), Items = items, UnmetConstraintExplanation = null };
    }

    private static string BuildUnmetConstraintExplanation(UserRequirement requirement)
    {
        var categoryPart = string.IsNullOrWhiteSpace(requirement.Category) ? "" : $" in {requirement.Category}";
        return $"No product{categoryPart} was found at or under {requirement.Budget!.Amount} {requirement.Budget.Currency}.";
    }

    private static RecommendedItem BuildRecommendedItem(
        ProductCandidate candidate, UserRequirement requirement, IReadOnlyList<ProductCandidate> withinBudgetSet)
    {
        var matched = new List<string> { $"budget ≤ {requirement.Budget!.Amount} {requirement.Budget.Currency}" };
        var unmatchedRequirements = new List<string>();

        foreach (var feature in requirement.RequiredFeatures)
        {
            if (MatchesSpec(candidate, feature))
            {
                matched.Add(feature);
            }
            else
            {
                unmatchedRequirements.Add(feature);
            }
        }

        var matchedPreferenceCount = 0;
        foreach (var preference in requirement.Preferences)
        {
            if (MatchesSpec(candidate, preference))
            {
                matched.Add(preference);
                matchedPreferenceCount++;
            }
        }

        var tradeOffs = BuildTradeOffs(candidate, withinBudgetSet, unmatchedRequirements);

        var score = (2m * (requirement.RequiredFeatures.Count - unmatchedRequirements.Count))
                    + (1m * matchedPreferenceCount)
                    - (0.5m * tradeOffs.Count);

        return new RecommendedItem
        {
            Candidate = candidate,
            MatchedRequirements = matched,
            TradeOffs = tradeOffs,
            Score = score,
        };
    }

    /// <summary>
    /// Deterministic substring match of a free-form requirement/preference phrase against the
    /// candidate's specification keys/values — no LLM involved in deciding a match.
    /// </summary>
    private static bool MatchesSpec(ProductCandidate candidate, string requirementText) =>
        candidate.Specifications.Any(s =>
            requirementText.Contains(s.Key, StringComparison.OrdinalIgnoreCase) ||
            requirementText.Contains(s.Value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every candidate gets at least one trade-off (FR-009): unmatched requirements first, then
    /// any specification where this candidate is strictly below the best value present in the
    /// within-budget set — a genuine, comparative, deterministic disadvantage.
    /// </summary>
    private static List<string> BuildTradeOffs(
        ProductCandidate candidate, IReadOnlyList<ProductCandidate> withinBudgetSet, List<string> unmatchedRequirements)
    {
        var tradeOffs = unmatchedRequirements.Select(f => $"Does not clearly satisfy: {f}").ToList();

        foreach (var spec in candidate.Specifications)
        {
            if (!decimal.TryParse(spec.Value, out var value))
            {
                continue;
            }

            var bestInSet = withinBudgetSet
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
