namespace Gateway.Api;

/// <summary>
/// Forwards the caller's Google-validated identity (the JWT's <c>sub</c> claim) as
/// <c>X-User-Id</c> on every call to Advisor — Advisor trusts this header only because the
/// internal API key (attached separately by <c>InternalApiKeyHandler</c>) already establishes
/// the caller is Gateway itself, not an arbitrary client (FR-031, research.md §17 addendum).
/// </summary>
public sealed class UserIdForwardingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId) && !request.Headers.Contains("X-User-Id"))
        {
            request.Headers.Add("X-User-Id", userId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
