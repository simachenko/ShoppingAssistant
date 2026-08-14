using System.ComponentModel;
using ModelContextProtocol.Server;
using ProductAdvisor.Application.Pipeline;

namespace ProductAdvisor.Infrastructure.Tools;

/// <summary>
/// Hosts store-knowledge retrieval as a true MCP tool for external MCP clients connecting to
/// <c>/mcp</c> (contracts/advisor-mcp-tools-additions.md) — the same dual exposure
/// <c>get_recommendations</c> already has.
/// </summary>
/// <remarks>
/// This service's own conversation path does <b>not</b> route through here: the `store_info` route
/// calls <see cref="IStoreInfoRetrievalService"/> directly, so retrieval is never a tool the
/// language model may choose, forget, or call with arguments it invented (002 research.md §2).
/// This type is the external contract surface only.
/// </remarks>
[McpServerToolType]
public sealed class RagTools(IStoreInfoRetrievalService retrievalService)
{
    [McpServerTool(Name = "retrieve_store_info", UseStructuredContent = true)]
    [Description("Search the store's reference documentation (delivery, payment, returns, warranty, loyalty program, contacts, and other store policies) for content relevant to a shopper's question. Returns matched fragments with their source document, or an empty result when nothing in the knowledge base is relevant enough to answer confidently. Never used for product price, availability, specifications, or comparisons — those come only from the product-data tools in this catalog.")]
    public async Task<StoreInfoToolResponse> RetrieveStoreInfoAsync(
        [Description("The shopper's store-policy question, verbatim or lightly normalized")] string query,
        [Description("BCP-47/ISO language tag the answer should prefer, e.g. 'uk', 'en'")] string language,
        CancellationToken cancellationToken = default)
    {
        var answer = await retrievalService.RetrieveAsync(query, language, cancellationToken);

        // Score is deliberately absent from the wire shape: it is an internal ranking signal, and
        // exposing it would invite it being restated as if it were a fact about the document.
        return new StoreInfoToolResponse(
            [.. answer.Matches.Select(m => new StoreInfoMatchDto(
                m.ChunkId, m.DocumentId, m.DocumentTitle, m.DocumentType.ToString(), m.Language, m.Content))]);
    }
}

/// <summary>
/// An empty <see cref="Matches"/> is this tool's own honest "nothing relevant was found" — never
/// an error and never a <c>found: false</c> flag, so a caller cannot mistake "no evidence" for
/// "call failed" (contracts/advisor-mcp-tools-additions.md).
/// </summary>
public sealed record StoreInfoToolResponse(IReadOnlyList<StoreInfoMatchDto> Matches);

public sealed record StoreInfoMatchDto(
    Guid ChunkId, Guid DocumentId, string DocumentTitle, string DocumentType, string Language, string Content);
