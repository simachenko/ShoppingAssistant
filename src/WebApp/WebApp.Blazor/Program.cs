using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
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
        new AuthenticationProperties { RedirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl },
        [GoogleDefaults.AuthenticationScheme]))
    .AllowAnonymous();

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/");
});

app.Run();
