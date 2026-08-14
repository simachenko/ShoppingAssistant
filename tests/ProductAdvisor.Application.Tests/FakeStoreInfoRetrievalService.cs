using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Stands in for the real hybrid-search retrieval call so orchestrator tests can assert the
/// grounding/citation/honesty contract without a database — mirrors the existing
/// <see cref="FakeRecommendationService"/> pattern for this test project.
/// </summary>
/// <remarks>
/// Defaults to returning no evidence, which is deliberately the honest-failure path (spec.md 002
/// FR-009): a test that does not care about store-policy retrieval gets the behavior that can
/// never fabricate an answer, rather than a canned one that could mask a regression.
/// </remarks>
public sealed class FakeStoreInfoRetrievalService(StoreInfoAnswer? answer = null) : IStoreInfoRetrievalService
{
    private readonly StoreInfoAnswer _answer = answer ?? StoreInfoAnswer.Empty;

    public string? LastQuery { get; private set; }
    public string? LastLanguage { get; private set; }
    public int CallCount { get; private set; }

    public Task<StoreInfoAnswer> RetrieveAsync(string query, string language, CancellationToken cancellationToken)
    {
        LastQuery = query;
        LastLanguage = language;
        CallCount++;
        return Task.FromResult(_answer);
    }

    /// <summary>Convenience builder for a single-fragment result from one named document.</summary>
    public static StoreInfoAnswer AnswerWith(
        string content,
        string documentTitle = "Delivery Terms",
        DocumentType documentType = DocumentType.Delivery,
        string language = "en",
        Guid? documentId = null,
        Guid? chunkId = null) => new()
        {
            Matches =
            [
                new StoreInfoMatch
                {
                    ChunkId = chunkId ?? Guid.NewGuid(),
                    DocumentId = documentId ?? Guid.NewGuid(),
                    DocumentTitle = documentTitle,
                    DocumentType = documentType,
                    Language = language,
                    Content = content,
                    Score = 0.5,
                },
            ],
        };
}
