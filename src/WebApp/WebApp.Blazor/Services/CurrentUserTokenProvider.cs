namespace WebApp.Blazor.Services;

/// <summary>
/// Scoped, per-circuit holder for the signed-in user's Google id_token. Blazor Server has no
/// per-request <c>HttpContext</c> once the SignalR circuit is established, so the token is read
/// once from the auth cookie during the initial static-SSR render (see <c>Routes.razor</c>) and
/// cached here for the rest of the circuit's lifetime, so it can be attached to every Gateway
/// call (FR-030, research.md §17).
/// </summary>
public sealed class CurrentUserTokenProvider
{
    public string? IdToken { get; set; }
}
