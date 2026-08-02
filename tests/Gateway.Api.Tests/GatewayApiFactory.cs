using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using Gateway.Api.Clients;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Api.Tests;

/// <summary>
/// Hosts the real Gateway with its <see cref="AdvisorApiClient"/> HttpClient swapped for a fake
/// handler — lets contract tests assert the Gateway's own composition/merge logic (correlation
/// ID propagation, session-creation-on-null, SSE proxying) without a live Advisor service. The
/// real JwtBearer handler (configured in Program.cs against Google's own OIDC discovery
/// document) is overridden here to validate against a test-only symmetric key instead, standing
/// in for Google as the fake OIDC provider referenced by T103/FR-030.
/// </summary>
public sealed class GatewayApiFactory : WebApplicationFactory<Program>
{
    public const string DefaultTestUserId = "test-user";

    private static readonly SymmetricSecurityKey TestSigningKey =
        new(System.Text.Encoding.UTF8.GetBytes("gateway-tests-only-signing-key-not-used-anywhere-else"));

    public Func<HttpRequestMessage, HttpResponseMessage> AdvisorResponder { get; set; } =
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

    public Func<HttpRequestMessage, HttpResponseMessage> CatalogResponder { get; set; } =
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

    public Func<HttpRequestMessage, HttpResponseMessage> PricingResponder { get; set; } =
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AdvisorApiClient>();
            services.AddHttpClient<AdvisorApiClient>(client => client.BaseAddress = new Uri("http://advisor-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(req => AdvisorResponder(req)));

            services.RemoveAll<Gateway.Api.Clients.CatalogApiClient>();
            services.AddHttpClient<Gateway.Api.Clients.CatalogApiClient>(client => client.BaseAddress = new Uri("http://catalog-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(req => CatalogResponder(req)));

            services.RemoveAll<Gateway.Api.Clients.PricingApiClient>();
            services.AddHttpClient<Gateway.Api.Clients.PricingApiClient>(client => client.BaseAddress = new Uri("http://pricing-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(req => PricingResponder(req)));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.RequireHttpsMetadata = false;
                options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = TestSigningKey,
                };
            });
        });
    }

    /// <summary>A client carrying a valid test-signed bearer token for <see cref="DefaultTestUserId"/>.</summary>
    public HttpClient CreateAuthenticatedClient(string userId = DefaultTestUserId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateTestToken(userId));
        return client;
    }

    public static string CreateTestToken(string userId = DefaultTestUserId, bool expiresInPast = false)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(TestSigningKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim("sub", userId)],
            expires: expiresInPast ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);
        return handler.WriteToken(token);
    }
}

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
