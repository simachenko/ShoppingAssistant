using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.Tests;

public sealed class BearerTokenHandlerTests
{
    [Fact]
    public async Task Reads_the_id_token_from_the_current_server_http_context()
    {
        const string idToken = "google-id-token";
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "id_token", Value = idToken }]);
        var ticket = new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(), properties, "Cookies");
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IAuthenticationService>(new StubAuthenticationService(ticket))
                .BuildServiceProvider(),
        };
        var terminalHandler = new CapturingHandler();
        var handler = new BearerTokenHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = terminalHandler,
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "https://gateway.example/api/chat/messages"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer", terminalHandler.AuthorizationScheme);
        Assert.Equal(idToken, terminalHandler.AuthorizationParameter);
    }

    [Fact]
    public async Task Allows_the_anonymous_startup_status_call_without_an_http_context()
    {
        var terminalHandler = new CapturingHandler();
        var handler = new BearerTokenHandler(new HttpContextAccessor())
        {
            InnerHandler = terminalHandler,
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://gateway.example/api/system-status"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(terminalHandler.AuthorizationScheme);
    }

    [Fact]
    public async Task Fails_clearly_when_the_interactive_request_has_no_http_context()
    {
        var handler = new BearerTokenHandler(new HttpContextAccessor())
        {
            InnerHandler = new CapturingHandler(),
        };

        using var invoker = new HttpMessageInvoker(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://gateway.example/api/chat/messages"),
            CancellationToken.None));

        Assert.Contains("HTTP context", exception.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class StubAuthenticationService(AuthenticationTicket ticket) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(ticket));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignInAsync(
            HttpContext context, string? scheme, System.Security.Claims.ClaimsPrincipal principal,
            AuthenticationProperties? properties) => throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();
    }
}
