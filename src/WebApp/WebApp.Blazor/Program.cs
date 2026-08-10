using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.HttpOverrides;
using Polly;
using WebApp.Blazor.Components;
using WebApp.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Whole-app Google sign-in gate (FR-030, research.md §17): a cookie identifies the browser
// session locally, but the identity WebApp forwards to Gateway is the Google-issued id_token
// itself — Gateway validates that token independently rather than trusting this cookie.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options => options.LoginPath = "/login")
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "";
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";
    options.SaveTokens = true;
    // SaveTokens only persists the standard OAuth2 fields (access_token/refresh_token/
    // token_type/expires_at) — Google's handler does not add "id_token" to that set on its own,
    // so HttpContext.GetTokenAsync("id_token") (Routes.razor) would otherwise always return
    // null. This is the actual Google-issued identity token Gateway independently validates
    // (research.md §17), so it must be captured explicitly here.
    options.Events.OnCreatingTicket = context =>
    {
        if (context.TokenResponse?.Response?.RootElement.TryGetProperty("id_token", out var idTokenElement) is true)
        {
            var tokens = context.Properties.GetTokens().ToList();
            tokens.Add(new AuthenticationToken { Name = "id_token", Value = idTokenElement.GetString() ?? "" });
            context.Properties.StoreTokens(tokens);
        }

        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<CurrentUserTokenProvider>();
// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .RegisterPersistentService<CurrentUserTokenProvider>(RenderMode.InteractiveServer);

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental
builder.Services.AddHttpClient<GatewayApiClient>(client =>
        client.BaseAddress = builder.Configuration.GetServiceBaseAddress("gateway-api"))
    // See the matching comment in Gateway.Api/Program.cs — the SSE streaming call needs a
    // longer, retry-free timeout instead of the standard resilience handler's short-request assumptions.
    .RemoveAllResilienceHandlers()
    .AddResilienceHandler("gateway-streaming", pipeline => pipeline.AddTimeout(TimeSpan.FromMinutes(5)));
#pragma warning restore EXTEXP0001

var app = builder.Build();

// Render (and most PaaS reverse proxies) terminates TLS at the edge and forwards plain HTTP
// internally — without this, the app thinks every request is http, so the Google OAuth
// redirect_uri it builds is http://... instead of https://..., which doesn't exactly match the
// URI registered in Google Cloud Console and fails with "redirect_uri_mismatch". Must run before
// any middleware that reads Request.Scheme (HTTPS redirection, the OAuth challenge, etc).
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
// The proxy's IP isn't fixed/known in advance on a PaaS like Render, so the default
// known-proxy allowlist (which normally guards against header spoofing) must be cleared.
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseCorrelationId();
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/login", (string? returnUrl) =>
    TypedResults.Challenge(
        // Always show Google's account chooser rather than silently completing against
        // whatever Google session is already active in the browser — otherwise a user who just
        // signed out of this app (their local cookie cleared) gets bounced straight back into a
        // signed-in state the instant anything challenges again, with no visible feedback that
        // sign-out ever happened.
        BuildLoginChallengeProperties(returnUrl),
        [GoogleDefaults.AuthenticationScheme]))
    .AllowAnonymous();

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    // Redirect to a real, anonymous-accessible landing page instead of "/" — every other page
    // requires authentication, so redirecting there would immediately re-challenge and, since
    // the browser's own Google session is untouched by this app's sign-out, silently sign the
    // user right back in with no visible confirmation that sign-out ever took effect.
    return Results.LocalRedirect("/signed-out");
});

app.Run();

static OAuthChallengeProperties BuildLoginChallengeProperties(string? returnUrl)
{
    var properties = new OAuthChallengeProperties
    {
        RedirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl,
    };
    properties.SetParameter("prompt", "select_account");
    return properties;
}
