using ProductCatalog.Application.Contracts;
using ProductCatalog.Domain;

namespace ProductCatalog.Application;

/// <summary>
/// Pure, deterministic evaluation of structured characteristic filters (FR-020) against a
/// product's specifications — no I/O, no LLM involvement. A product must satisfy every given
/// filter (AND semantics); a filter whose <see cref="CharacteristicFilter.Key"/> doesn't exist
/// for the product is unsatisfied (spec.md edge case: zero matches, not "filter ignored").
/// </summary>
public static class CharacteristicFilterMatcher
{
    public static bool MatchesAll(Product product, IReadOnlyList<CharacteristicFilter> filters) =>
        filters.All(filter => Matches(product, filter));

    private static bool Matches(Product product, CharacteristicFilter filter)
    {
        var spec = product.Specifications.FirstOrDefault(s => s.Key == filter.Key);
        if (spec is null)
        {
            return false;
        }

        return filter.Operator switch
        {
            CharacteristicFilterOperator.Equals => MatchesEquals(spec.Value, filter.Value),
            CharacteristicFilterOperator.GreaterThanOrEqual => TryCompareNumeric(spec.Value, filter.Value, out var cmp) && cmp >= 0,
            CharacteristicFilterOperator.LessThanOrEqual => TryCompareNumeric(spec.Value, filter.Value, out var cmp) && cmp <= 0,
            CharacteristicFilterOperator.Between => MatchesBetween(spec.Value, filter.Value, filter.ValueTo),
            _ => false,
        };
    }

    private static bool MatchesEquals(string specValue, string filterValue)
    {
        if (decimal.TryParse(specValue, out var specNumber) && decimal.TryParse(filterValue, out var filterNumber))
        {
            return specNumber == filterNumber;
        }

        return string.Equals(specValue, filterValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesBetween(string specValue, string filterValue, string? filterValueTo)
    {
        if (filterValueTo is null)
        {
            return false;
        }

        if (!decimal.TryParse(specValue, out var specNumber)
            || !decimal.TryParse(filterValue, out var lower)
            || !decimal.TryParse(filterValueTo, out var upper))
        {
            return false;
        }

        return specNumber >= lower && specNumber <= upper;
    }

    private static bool TryCompareNumeric(string specValue, string filterValue, out int comparison)
    {
        comparison = 0;
        if (!decimal.TryParse(specValue, out var specNumber) || !decimal.TryParse(filterValue, out var filterNumber))
        {
            return false;
        }

        comparison = specNumber.CompareTo(filterNumber);
        return true;
    }
}
