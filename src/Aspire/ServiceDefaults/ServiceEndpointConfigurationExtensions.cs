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
            // On Render the logical name cannot resolve: free web services have no private
            // network, so `http://advisor-api` fails at the connection level the moment it is
            // used. Falling back to it there produced a uniquely undiagnosable failure — the
            // dependency was simply never contacted, so it never woke and logged nothing at all,
            // while the readiness screen kept reporting it as "not ready yet" indefinitely.
            // Fail at startup instead, naming the exact missing variable.
            if (IsRunningOnRender(configuration))
            {
                throw new InvalidOperationException(
                    $"RenderExternalHosts:{serviceName} is not configured, but this service is running on " +
                    $"Render, where the logical name 'http://{serviceName}' cannot resolve. Set the " +
                    $"environment variable RenderExternalHosts__{serviceName} on this service to " +
                    $"{serviceName}'s public hostname (render.yaml wires this via `fromService`; a service " +
                    "added after the Blueprint was last applied will be missing it until the Blueprint is " +
                    "re-synced).");
            }

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

    /// <summary>
    /// Render sets <c>RENDER=true</c> on every service it runs. Used only to decide whether a
    /// missing external host is a fatal misconfiguration (it is, there) or the normal case
    /// (Aspire/Compose, where the logical name resolves through service discovery).
    /// </summary>
    private static bool IsRunningOnRender(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["RENDER"])
        || !string.IsNullOrWhiteSpace(configuration["RENDER_EXTERNAL_HOSTNAME"]);
}
