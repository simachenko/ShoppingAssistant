using System.Reflection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 13 (spec.md FR-133–FR-135/FR-137): the allow-list is enforced structurally — pure
/// reflection/logic, no Docker/database needed, unlike the rest of this test project.
/// </summary>
public class ObservabilityFieldShapeTests
{
    /// <summary>The exact eleven fields FR-133 permits — nothing more, nothing less. This test
    /// fails the moment a twelfth property (a denied field in disguise) is added to
    /// <see cref="TurnLogFields"/>, or one of the eleven is accidentally removed.</summary>
    private static readonly HashSet<string> AllowedFieldNames =
    [
        nameof(TurnLogFields.CorrelationId),
        nameof(TurnLogFields.PseudonymousUserId),
        nameof(TurnLogFields.PromptVersion),
        nameof(TurnLogFields.ModelIdentifier),
        nameof(TurnLogFields.Intent),
        nameof(TurnLogFields.ToolName),
        nameof(TurnLogFields.Decision),
        nameof(TurnLogFields.Latency),
        nameof(TurnLogFields.TokenUsage),
        nameof(TurnLogFields.ValidationStatus),
        nameof(TurnLogFields.ErrorCategory),
    ];

    [Fact]
    public void TurnLogFields_exposes_exactly_the_eleven_allowed_fields_no_more_no_less()
    {
        var actualPropertyNames = typeof(TurnLogFields)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract") // record-synthesized, not a data field.
            .ToHashSet();

        Assert.Equal(AllowedFieldNames, actualPropertyNames);
    }

    [Fact]
    public void TurnLogFields_has_no_property_capable_of_holding_a_full_message_or_prompt()
    {
        // FR-134's denied fields (full raw message, full assembled prompt, tool
        // call arguments/results, credentials, connection strings, full LLM response) have no
        // property to be assigned to in the first place — this asserts that structurally, by
        // property-name shape, rather than by content inspection of some populated instance.
        var deniedNameFragments = new[] { "message", "prompt", "response", "credential", "key", "connectionstring", "argument", "result" };
        var propertyNames = typeof(TurnLogFields).GetProperties().Select(p => p.Name.ToLowerInvariant());

        foreach (var propertyName in propertyNames)
        {
            // PromptVersion legitimately contains "prompt" — it's a version identifier (FR-101),
            // not the prompt content itself; excluded from this fragment check by name.
            if (string.Equals(propertyName, nameof(TurnLogFields.PromptVersion), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.DoesNotContain(deniedNameFragments, fragment => propertyName.Contains(fragment, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PseudonymousIdentifier_hash_is_deterministic_for_the_same_input()
    {
        var first = PseudonymousIdentifier.Hash("user-123");
        var second = PseudonymousIdentifier.Hash("user-123");

        Assert.Equal(first, second);
    }

    [Fact]
    public void PseudonymousIdentifier_hash_never_contains_the_raw_identifier_verbatim()
    {
        var rawIdentifier = "sensitive-user-id-42";

        var hash = PseudonymousIdentifier.Hash(rawIdentifier);

        Assert.DoesNotContain(rawIdentifier, hash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PseudonymousIdentifier_hash_differs_for_different_identifiers()
    {
        var hashA = PseudonymousIdentifier.Hash("user-a");
        var hashB = PseudonymousIdentifier.Hash("user-b");

        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void PseudonymousIdentifier_hash_differs_under_a_different_pepper_same_input()
    {
        // FR-137's Assumptions note: pseudonyms under different schemes are never assumed
        // correlatable — a pepper change is exactly this kind of scheme change.
        var defaultPepper = PseudonymousIdentifier.Hash("user-a");
        var customPepper = PseudonymousIdentifier.Hash("user-a", pepper: "a-different-pepper");

        Assert.NotEqual(defaultPepper, customPepper);
    }
}
