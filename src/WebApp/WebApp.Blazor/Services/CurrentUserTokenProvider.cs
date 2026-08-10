using Microsoft.AspNetCore.Components;

namespace WebApp.Blazor.Services;

/// <summary>
/// Carries the Google id_token from authenticated server prerendering into the interactive
/// server circuit. Interactive Server protects persisted prerendered state with Data Protection.
/// </summary>
public sealed class CurrentUserTokenProvider
{
    [PersistentState]
    public string? IdToken { get; set; }
}
