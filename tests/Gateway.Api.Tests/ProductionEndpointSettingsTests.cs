using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Gateway.Api.Tests;

/// <summary>
/// Loads the real committed <c>appsettings.Production.json</c> files and resolves them through the
/// same helper the services use. A misspelled key here would not fail — it would silently leave the
/// dependency unconfigured, which is exactly the failure mode that made the original Render
/// cold-start problem so hard to see.
/// </summary>
public class ProductionEndpointSettingsTests
{
    [Theory]
    [InlineData("src/Gateway/Gateway.Api", new[] { "catalog-api", "pricing-api", "advisor-api" })]
    // The Advisor calls Catalog and Pricing directly rather than through the Gateway, so it needs
    // its own entries — configuring only the Gateway leaves it able to wake but unable to work.
    [InlineData("src/ProductAdvisor/ProductAdvisor.Api", new[] { "catalog-api", "pricing-api" })]
    [InlineData("src/WebApp/WebApp.Blazor", new[] { "gateway-api" })]
    public void Every_service_this_one_calls_resolves_to_an_absolute_https_render_url(
        string projectDirectory, string[] expectedServices)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryRoot(), projectDirectory, "appsettings.Production.json"),
                optional: false)
            .Build();

        foreach (var serviceName in expectedServices)
        {
            var address = configuration.GetServiceBaseAddress(serviceName);

            Assert.Equal(Uri.UriSchemeHttps, address.Scheme);
            Assert.EndsWith(".onrender.com/", address.ToString(), StringComparison.Ordinal);
            // Guards the real hazard: a typo'd key falls back to the logical name instead of
            // throwing, because these files are read outside Render at test time.
            Assert.DoesNotContain(serviceName + "/", address.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>Walks up from the test output directory to the repository root (marked by global.json).</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
