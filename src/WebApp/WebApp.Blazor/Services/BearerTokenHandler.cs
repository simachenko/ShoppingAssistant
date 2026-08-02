using System.Net.Http.Headers;

namespace WebApp.Blazor.Services;

/// <summary>
/// Attaches the signed-in user's Google id_token as <c>Authorization: Bearer</c> to every call
/// to Gateway, which independently validates it against Google rather than trusting WebApp by
/// network position (FR-030, research.md §17).
/// </summary>
public sealed class BearerTokenHandler(CurrentUserTokenProvider tokenProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(tokenProvider.IdToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.IdToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
