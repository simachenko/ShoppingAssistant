using System.Net;
using System.Text;
using System.Text.Json;

namespace TestSupport;

/// <summary>
/// A minimal stub <see cref="HttpMessageHandler"/> that maps request paths to canned JSON
/// responses — used to substitute real Catalog/Pricing services in fast, no-Docker-required
/// tool contract tests.
/// </summary>
public sealed class FakeJsonHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode StatusCode, object? Body)> respond) : HttpMessageHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (statusCode, body) = respond(request);
        var response = new HttpResponseMessage(statusCode);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), SerializerOptions);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return Task.FromResult(response);
    }
}
