using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ProductAdvisor.Application;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.Rag;

/// <summary>
/// The `store_info` route's terminal retrieval call (spec.md 002 FR-019, research.md §9): embeds
/// the shopper's question, runs the hybrid search, and returns only the fragments that cleared the
/// configured relevance threshold. Deterministic in the sense that matters here — it neither asks
/// the language model what to retrieve nor lets it widen what was retrieved.
/// </summary>
public sealed class StoreInfoRetrievalService(
    AdvisorDbContext dbContext,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IStoreContext storeContext,
    IOptions<StoreInfoOptions> options) : IStoreInfoRetrievalService
{
    public async Task<StoreInfoAnswer> RetrieveAsync(
        string query, string language, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var embedding = await embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken);

        // Null when the question does not clearly belong to one policy area — retrieval then
        // searches across all types rather than forcing a wrong preference (FR-022).
        var preferredDocumentType = DocumentTypeClassifier.Classify(query);

        var matches = await HybridSearchQuery.ExecuteAsync(
            dbContext,
            query,
            embedding.ToArray(),
            storeContext.CurrentStoreId,
            // FR-030: normalized once here so the query compares like with like — an un-normalized
            // `uk-UA` would silently lose the same-language preference against a `uk` document.
            LanguageTag.Normalize(language),
            preferredDocumentType,
            options.Value,
            cancellationToken);

        // An empty list here means either "nothing matched" or "nothing was relevant enough" —
        // deliberately indistinguishable to the caller, because FR-009's honest answer is the same
        // either way and a caller that could tell them apart would be tempted to treat one of them
        // as license to answer anyway.
        return new StoreInfoAnswer { Matches = matches };
    }
}
