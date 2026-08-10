using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// PolicyRouter is a pure, deterministic function of state (spec.md FR-041/SC-026): two turns
/// sharing identical merged state and identical extraction results always select the identical
/// route; missing essential information routes to Clarify rather than guessing.
/// </summary>
public class PolicyRouterTests
{
    private static StructuredIntent Intent(Domain.Intent intent, IReadOnlyList<string>? productReferences = null, double confidence = 0.9) =>
        new()
        {
            Intent = intent,
            Confidence = confidence,
            ProductReferences = productReferences ?? [],
        };

    [Fact]
    public void Two_calls_with_identical_state_select_the_identical_route()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var intent = Intent(Domain.Intent.Recommend);

        var route1 = PolicyRouter.SelectRoute(requirement, intent);
        var route2 = PolicyRouter.SelectRoute(requirement, intent);

        Assert.Equal(route1, route2);
        Assert.Equal(Route.Recommend, route1);
    }

    [Fact]
    public void Recommend_intent_without_essential_information_routes_to_Clarify()
    {
        var requirement = new UserRequirement { Category = "smartphones" }; // no budget yet
        var route = PolicyRouter.SelectRoute(requirement, Intent(Domain.Intent.Recommend));

        Assert.Equal(Route.Clarify, route);
    }

    [Fact]
    public void Compare_intent_with_fewer_than_two_product_references_routes_to_Clarify()
    {
        var requirement = UserRequirement.Empty;
        var route = PolicyRouter.SelectRoute(requirement, Intent(Domain.Intent.Compare, ["Galaxy S24"]));

        Assert.Equal(Route.Clarify, route);
    }

    [Fact]
    public void Compare_intent_with_two_product_references_routes_to_Compare()
    {
        var requirement = UserRequirement.Empty;
        var route = PolicyRouter.SelectRoute(requirement, Intent(Domain.Intent.Compare, ["Galaxy S24", "Pixel 9"]));

        Assert.Equal(Route.Compare, route);
    }

    [Fact]
    public void Checkout_and_ProductFact_intents_need_at_least_one_product_reference()
    {
        var requirement = UserRequirement.Empty;

        Assert.Equal(Route.Clarify, PolicyRouter.SelectRoute(requirement, Intent(Domain.Intent.Checkout)));
        Assert.Equal(Route.Checkout, PolicyRouter.SelectRoute(requirement, Intent(Domain.Intent.Checkout, ["Galaxy S24"])));
        Assert.Equal(Route.Clarify, PolicyRouter.SelectRoute(requirement, Intent(Domain.Intent.ProductFact)));
        Assert.Equal(Route.ProductFact, PolicyRouter.SelectRoute(requirement, Intent(Domain.Intent.ProductFact, ["Galaxy S24"])));
    }

    [Theory]
    [InlineData(Domain.Intent.Smalltalk, Route.Smalltalk)]
    [InlineData(Domain.Intent.Unsupported, Route.Unsupported)]
    public void Smalltalk_and_unsupported_intents_route_directly_with_no_essential_information_needed(
        Domain.Intent intent, Route expectedRoute)
    {
        var route = PolicyRouter.SelectRoute(UserRequirement.Empty, Intent(intent));
        Assert.Equal(expectedRoute, route);
    }

    [Fact]
    public void A_confidence_below_the_threshold_routes_to_Clarify_regardless_of_intent()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var route = PolicyRouter.SelectRoute(requirement, Intent(Domain.Intent.Recommend, confidence: 0.1));

        Assert.Equal(Route.Clarify, route);
    }
}
