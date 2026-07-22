using Polly;
using WebApp.Blazor.Components;
using WebApp.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental
builder.Services.AddHttpClient<GatewayApiClient>(client => client.BaseAddress = new Uri("http://gateway-api"))
    // See the matching comment in Gateway.Api/Program.cs — the SSE streaming call needs a
    // longer, retry-free timeout instead of the standard resilience handler's short-request assumptions.
    .RemoveAllResilienceHandlers()
    .AddResilienceHandler("gateway-streaming", pipeline => pipeline.AddTimeout(TimeSpan.FromMinutes(5)));
#pragma warning restore EXTEXP0001

var app = builder.Build();

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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
