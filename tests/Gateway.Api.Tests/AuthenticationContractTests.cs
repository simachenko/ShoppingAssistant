using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Gateway.Api.Tests;

/// <summary>Proves FR-030: every Gateway endpoint requires a valid Google-issued identity token,
/// validated independently by Gateway itself rather than trusted by network position
/// (research.md §17). The real Google issuer is stood in for by a test-signed token in
/// <see cref="GatewayApiFactory"/> (T103).</summary>
public sealed class AuthenticationContractTests
{
    [Fact]
    public async Task A_request_with_no_bearer_token_is_rejected()
    {
        var factory = new GatewayApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/products/search?category=Smartphones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_a_malformed_bearer_token_is_rejected()
    {
        var factory = new GatewayApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.GetAsync("/api/products/search?category=Smartphones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_an_expired_bearer_token_is_rejected()
    {
        var factory = new GatewayApiFactory();
        var client = factory.CreateClient();
        var expiredToken = GatewayApiFactory.CreateTestToken(expiresInPast: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/api/products/search?category=Smartphones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_a_valid_bearer_token_succeeds()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0 }"""),
            },
        };

        var response = await factory.CreateAuthenticatedClient().GetAsync("/api/products/search?category=Smartphones");

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Proves the addendum to research.md §17: the token's <c>sub</c> claim is forwarded
    /// as <c>X-User-Id</c> on every call to Advisor, so Advisor can enforce FR-031 without itself
    /// validating Google tokens.</summary>
    [Fact]
    public async Task The_tokens_sub_claim_is_forwarded_to_advisor_as_x_user_id()
    {
        string? forwardedUserId = null;
        var factory = new GatewayApiFactory
        {
            AdvisorResponder = request =>
            {
                forwardedUserId = request.Headers.TryGetValues("X-User-Id", out var values) ? values.FirstOrDefault() : null;
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            },
        };

        await factory.CreateAuthenticatedClient("a-specific-google-sub").GetAsync($"/api/chat/{Guid.NewGuid()}");

        Assert.Equal("a-specific-google-sub", forwardedUserId);
    }
}
