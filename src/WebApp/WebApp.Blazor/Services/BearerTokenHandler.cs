using Microsoft.AspNetCore.Authentication;
using System.Net.Http.Headers;

namespace WebApp.Blazor.Services;

/// <summary>
/// Attaches the signed-in user's Google id_token as <c>Authorization: Bearer</c> to every call
/// to Gateway, which independently validates it against Google rather than trusting WebApp by
/// network position (FR-030, research.md §17).
/// </summary>
public sealed class BearerTokenHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // This one Gateway endpoint is deliberately anonymous so the startup gate can run before
        // authentication state is available (and can still diagnose an expired sign-in session).
        if (request.RequestUri?.AbsolutePath == "/api/system-status")
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("The authenticated HTTP context is unavailable.");
        var idToken = await httpContext.GetTokenAsync("id_token")
            ?? throw new InvalidOperationException("The signed-in session has no Google id_token.");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
