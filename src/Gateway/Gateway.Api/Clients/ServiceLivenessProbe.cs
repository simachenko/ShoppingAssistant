namespace Gateway.Api.Clients;

/// <summary>
/// Keeps probing a service after Render has accepted the wake-up request but still returns a
/// transient non-success response while the free instance is starting.
/// </summary>
internal static class ServiceLivenessProbe
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static async Task<bool> WaitUntilAliveAsync(
        HttpClient httpClient, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await httpClient.GetAsync("/alive", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Unlike Render's transient 502/503 startup responses, a connection-level
                // failure has no accepted HTTP request to keep observing.
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }
}
