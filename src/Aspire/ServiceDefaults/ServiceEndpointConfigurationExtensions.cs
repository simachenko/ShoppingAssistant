using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

public static class ServiceEndpointConfigurationExtensions
{
    /// <summary>
    /// Uses a service's public Render hostname when one is supplied by the Blueprint. Render's
    /// free web services cannot receive private-network traffic, while a request to the public
    /// hostname both reaches and wakes a sleeping free instance. Outside Render, the logical
    /// service name continues to resolve through Aspire service discovery.
    /// </summary>
    public static Uri GetServiceBaseAddress(this IConfiguration configuration, string serviceName)
    {
        var configuredHost = configuration[$"RenderExternalHosts:{serviceName}"];
        if (string.IsNullOrWhiteSpace(configuredHost))
        {
            return new Uri($"http://{serviceName}");
        }

        var candidate = configuredHost.Contains("://", StringComparison.Ordinal)
            ? configuredHost
            : $"https://{configuredHost}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"RenderExternalHosts:{serviceName} must be an HTTP(S) hostname or absolute URI.");
        }

        return endpoint;
    }
}
