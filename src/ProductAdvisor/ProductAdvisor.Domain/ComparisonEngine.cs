namespace ProductAdvisor.Domain;

/// <summary>
/// Pure, deterministic comparison: shared criteria, per-criterion values, a composite rating,
/// and deltas vs. the best value in the set. No I/O, no LLM call. Called exclusively by the
/// <c>compare_products</c> tool handler — never directly by the conversation orchestration loop
/// (research.md §1, plan.md Summary).
/// </summary>
public static class ComparisonEngine
{
    public static Comparison Compare(IReadOnlyList<ProductCandidate> candidates, IReadOnlyList<string> comparableAttributeKeys)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(comparableAttributeKeys);
        if (candidates.Count < 2)
        {
            throw new ArgumentException("At least two candidates are required to compare.", nameof(candidates));
        }

        var criteria = new List<string> { "price" };
        criteria.AddRange(comparableAttributeKeys);
        criteria.Add("availability");

        var rows = candidates
            .Select(candidate => new ComparisonRow
            {
                Candidate = candidate,
                ValuesByCriterion = criteria.ToDictionary(c => c, c => FormatValue(candidate, c)),
                Rating = ComputeRating(candidate, criteria, candidates),
                DeltasVsBest = ComputeDeltas(candidate, criteria, candidates),
            })
            .ToList();

        return new Comparison { ComparisonId = Guid.NewGuid(), Criteria = criteria, Rows = rows };
    }

    private static string? FormatValue(ProductCandidate candidate, string criterion)
    {
        if (criterion == "price")
        {
            return candidate.PriceVerified && candidate.Price is not null
                ? $"{candidate.Price.Amount} {candidate.Price.Currency}"
                : null;
        }

        if (criterion == "availability")
        {
            return candidate.AvailabilityVerified && candidate.Availability is not null
                ? candidate.Availability.Value.ToString()
                : null;
        }

        return candidate.Specifications.FirstOrDefault(s => s.Key == criterion)?.Value;
    }

    /// <summary>
    /// Averages, over every criterion this candidate has a verified numeric value for, how close
    /// that value is to the best value present in the set (price: cheapest wins; every other
    /// numeric criterion: highest wins), scaled to 0-10. A candidate missing every numeric
    /// criterion rates 0 rather than throwing — it still gets a row, just an uninformative one.
    /// </summary>
    private static decimal ComputeRating(
        ProductCandidate candidate, IReadOnlyList<string> criteria, IReadOnlyList<ProductCandidate> allCandidates)
    {
        var normalized = new List<decimal>();

        foreach (var criterion in criteria)
        {
            if (criterion == "availability")
            {
                continue;
            }

            if (criterion == "price")
            {
                if (candidate.PriceVerified && candidate.Price is { Amount: > 0 })
                {
                    var cheapest = allCandidates
                        .Where(c => c.PriceVerified && c.Price is not null && c.Price.Currency == candidate.Price.Currency)
                        .Select(c => c.Price!.Amount)
                        .DefaultIfEmpty(candidate.Price.Amount)
                        .Min();

                    normalized.Add(cheapest / candidate.Price.Amount);
                }

                continue;
            }

            var value = GetNumericSpecValue(candidate, criterion);
            if (value is null)
            {
                continue;
            }

            var best = allCandidates
                .Select(c => GetNumericSpecValue(c, criterion))
                .Where(v => v is not null)
                .Max();

            if (best is > 0)
            {
                normalized.Add(value.Value / best.Value);
            }
        }

        return normalized.Count == 0 ? 0m : Math.Round(normalized.Average() * 10m, 1);
    }

    private static Dictionary<string, string> ComputeDeltas(
        ProductCandidate candidate, IReadOnlyList<string> criteria, IReadOnlyList<ProductCandidate> allCandidates)
    {
        var deltas = new Dictionary<string, string>();

        foreach (var criterion in criteria)
        {
            if (criterion == "availability")
            {
                continue;
            }

            if (criterion == "price")
            {
                deltas[criterion] = ComputePriceDelta(candidate, allCandidates);
                continue;
            }

            deltas[criterion] = ComputeSpecDelta(candidate, criterion, allCandidates);
        }

        return deltas;
    }

    private static string ComputePriceDelta(ProductCandidate candidate, IReadOnlyList<ProductCandidate> allCandidates)
    {
        if (!candidate.PriceVerified || candidate.Price is null)
        {
            return "not verified";
        }

        var cheapest = allCandidates
            .Where(c => c.PriceVerified && c.Price is not null && c.Price.Currency == candidate.Price.Currency)
            .Select(c => c.Price!.Amount)
            .DefaultIfEmpty(candidate.Price.Amount)
            .Min();

        return candidate.Price.Amount == cheapest
            ? "cheapest in set"
            : $"+{candidate.Price.Amount - cheapest} {candidate.Price.Currency} vs cheapest";
    }

    private static string ComputeSpecDelta(
        ProductCandidate candidate, string criterion, IReadOnlyList<ProductCandidate> allCandidates)
    {
        var spec = candidate.Specifications.FirstOrDefault(s => s.Key == criterion);
        if (spec is null)
        {
            return "not verified";
        }

        if (!decimal.TryParse(spec.Value, out var value))
        {
            return "not comparable";
        }

        var best = allCandidates
            .Select(c => GetNumericSpecValue(c, criterion))
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .DefaultIfEmpty(value)
            .Max();

        if (value >= best)
        {
            return "best in set";
        }

        var unit = string.IsNullOrEmpty(spec.Unit) ? "" : spec.Unit;
        return $"-{best - value}{unit} vs best";
    }

    private static decimal? GetNumericSpecValue(ProductCandidate candidate, string key)
    {
        var spec = candidate.Specifications.FirstOrDefault(s => s.Key == key);
        return spec is not null && decimal.TryParse(spec.Value, out var v) ? v : null;
    }
}
