using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebApp.Blazor.Models;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.Tests;

public sealed class GatewayApiClientAuthorizationTests
{
    [Fact]
    public async Task Sends_the_id_token_persisted_for_the_interactive_circuit()
    {
        const string idToken = "google-id-token";
        var terminalHandler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(terminalHandler, new CurrentUserTokenProvider { IdToken = idToken });

        var result = await client.GetProductDetailAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal("Bearer", terminalHandler.Authorization?.Scheme);
        Assert.Equal(idToken, terminalHandler.Authorization?.Parameter);
    }

    [Fact]
    public async Task Allows_the_anonymous_startup_status_call_without_a_token()
    {
        var terminalHandler = new CapturingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SystemReadinessStatusDto("ready", [])),
            });
        var client = CreateClient(terminalHandler, new CurrentUserTokenProvider());

        var result = await client.GetSystemStatusAsync(CancellationToken.None);

        Assert.Equal("ready", result.Overall);
        Assert.Null(terminalHandler.Authorization);
    }

    [Fact]
    public async Task Fails_clearly_when_the_interactive_request_has_no_token()
    {
        var client = CreateClient(
            new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new CurrentUserTokenProvider());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetProductDetailAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("id_token", exception.Message, StringComparison.Ordinal);
    }

    private static GatewayApiClient CreateClient(
        HttpMessageHandler handler, CurrentUserTokenProvider tokenProvider) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.example") }, tokenProvider);

    private sealed class CapturingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(respond(request));
        }
    }
}
