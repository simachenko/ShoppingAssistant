using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TestSupport;

/// <summary>
/// Mints bearer tokens accepted by Gateway's test-only <c>E2ETest</c> JwtBearer scheme
/// (<c>src/Gateway/Gateway.Api/Program.cs</c>), which only exists when
/// <c>Authentication:TestSigningKey</c> is configured — set in <c>docker-compose.yml</c> for
/// local/CI runs, never in <c>render.yaml</c> for the real deployment. Stands in for a real,
/// interactive Google sign-in that EndToEnd.Tests/CI cannot perform (FR-030, research.md §17).
/// </summary>
public static class E2EAuthTestDefaults
{
    public const string SigningKey = "e2e-tests-only-signing-key-never-used-in-production";
    public const string DefaultUserId = "e2e-test-user";

    /// <summary>The internal API key the running docker-compose stack expects on every direct
    /// call to Catalog/Pricing/Advisor (FR-029) — matches docker-compose.yml's own
    /// <c>INTERNAL_API_KEY:-dev-internal-api-key</c> default, and honors the same override.</summary>
    public static readonly string InternalApiKey =
        Environment.GetEnvironmentVariable("INTERNAL_API_KEY") is { Length: > 0 } value ? value : "dev-internal-api-key";

    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes(SigningKey));

    public static string CreateBearerToken(string userId = DefaultUserId)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim("sub", userId)],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        return handler.WriteToken(token);
    }
}
