namespace TestSupport;

/// <summary>
/// Fixed internal API key value shared by every WebApplicationFactory-based contract test.
/// Each service's test factory sets this as the <c>InternalApiKey</c> configuration value the
/// hosted app expects, and attaches it as a default header on every client it creates — so
/// existing tests keep passing unmodified. A dedicated auth-rejection test builds its own
/// client without this header (or a wrong one) to prove the middleware actually rejects it.
/// </summary>
public static class InternalApiKeyTestDefaults
{
    public const string Key = "test-internal-api-key";
}
