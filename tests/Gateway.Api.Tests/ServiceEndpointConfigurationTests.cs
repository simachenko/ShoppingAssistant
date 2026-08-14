using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Gateway.Api.Tests;

/// <summary>
/// How a service's base address is resolved from configuration. Previously untested, and now
/// load-bearing: a missing external host is fatal on Render, because the logical name cannot
/// resolve there and the resulting failure is silent all the way up to the readiness screen.
/// </summary>
public class ServiceEndpointConfigurationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Theory]
    // What Render's own RENDER_EXTERNAL_HOSTNAME supplies: a bare hostname.
    [InlineData("pricing-api-mbjo.onrender.com", "https://pricing-api-mbjo.onrender.com/")]
    // A full URL pasted from the browser is accepted unchanged — both forms are valid input.
    [InlineData("https://pricing-api-mbjo.onrender.com", "https://pricing-api-mbjo.onrender.com/")]
    [InlineData("https://pricing-api-mbjo.onrender.com/", "https://pricing-api-mbjo.onrender.com/")]
    [InlineData("http://pricing-api-mbjo.onrender.com", "http://pricing-api-mbjo.onrender.com/")]
    public void A_configured_host_is_accepted_with_or_without_a_scheme(string configured, string expected)
    {
        var address = Config(("RenderExternalHosts:pricing-api", configured))
            .GetServiceBaseAddress("pricing-api");

        Assert.Equal(expected, address.ToString());
    }

    [Fact]
    public void Outside_render_a_missing_host_falls_back_to_the_logical_service_name()
    {
        // Aspire and Docker Compose resolve this through service discovery.
        var address = Config().GetServiceBaseAddress("pricing-api");

        Assert.Equal("http://pricing-api/", address.ToString());
    }

    [Theory]
    [InlineData("RENDER", "true")]
    [InlineData("RENDER_EXTERNAL_HOSTNAME", "gateway-api-xxxx.onrender.com")]
    public void On_render_a_missing_host_fails_at_startup_naming_the_variable_to_set(string key, string value)
    {
        // The regression this guards: falling back to http://advisor-api on Render meant the
        // dependency was never contacted at all — no logs, no wake-up, and a readiness screen
        // that reported "not ready yet" forever with nothing to diagnose.
        var exception = Assert.Throws<InvalidOperationException>(
            () => Config((key, value)).GetServiceBaseAddress("advisor-api"));

        Assert.Contains("RenderExternalHosts__advisor-api", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://pricing-api-mbjo.onrender.com")]
    [InlineData("not a uri at all")]
    public void A_host_that_is_not_http_or_https_is_rejected(string configured)
    {
        Assert.Throws<InvalidOperationException>(
            () => Config(("RenderExternalHosts:pricing-api", configured)).GetServiceBaseAddress("pricing-api"));
    }
}
