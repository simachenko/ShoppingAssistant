namespace ProductAdvisor.Domain;

/// <summary>
/// A bounded, independently retrievable fragment of a <see cref="StoreDocument"/> (spec.md 002
/// FR-016/FR-017, data-model.md). Carries its own embedding so semantic search ranks fragments,
/// not whole documents, and stays traceable to its parent so an answer can cite it precisely
/// (FR-008).
/// </summary>
public sealed class DocumentChunk
{
    /// <summary>
    /// The embedding dimension every <see cref="Embedding"/> must have. Fixed here (not
    /// configurable at runtime) because pgvector pins a dimension per column: changing the
    /// configured embedding model to one with a different output dimension requires a new
    /// migration, never merely a configuration change (research.md §7).
    /// </summary>
    public const int EmbeddingDimension = 1536;

    public Guid ChunkId { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>Position within the parent document — part of a citation's traceability.</summary>
    public int Order { get; private set; }

    /// <summary>
    /// The fragment's text. The only material a store-policy answer may draw a claim from
    /// (FR-007) — <c>AllowedClaims</c> is derived from exactly this, never from anything wider.
    /// </summary>
    public string Content { get; private set; } = "";

    /// <summary>
    /// The semantic-search vector derived from <see cref="Content"/>. Held as a plain
    /// <see cref="float"/> array so the Domain layer carries no persistence-technology
    /// dependency (constitution Principle I); Infrastructure converts it to pgvector's own column
    /// type at the mapping boundary (data-model.md, research.md §6).
    /// </summary>
    public float[] Embedding { get; private set; } = [];

    // Denormalized from the parent document so a hybrid-search query can filter and rank without
    // a join (data-model.md). Never independently mutable — kept in sync only by StoreDocument,
    // which owns every write to them.
    public string StoreId { get; private set; } = "";
    public string Language { get; private set; } = "";
    public DocumentType DocumentType { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Active;

    public DocumentChunk(Guid chunkId, int order, string content, float[] embedding)
    {
        if (chunkId == Guid.Empty)
            throw new ArgumentException("ChunkId is required.", nameof(chunkId));
        if (order < 0)
            throw new ArgumentOutOfRangeException(nameof(order), "Order must be non-negative.");
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length != EmbeddingDimension)
        {
            throw new ArgumentException(
                $"Embedding must have exactly {EmbeddingDimension} dimensions, but had {embedding.Length}. " +
                "A dimension mismatch is never silently truncated or padded (data-model.md).",
                nameof(embedding));
        }

        ChunkId = chunkId;
        Order = order;
        Content = content;
        Embedding = embedding;
    }

    private DocumentChunk()
    {
        // EF Core materialization only.
    }

    /// <summary>
    /// Copies the parent document's filterable/rankable metadata onto this chunk. Internal by
    /// design: only <see cref="StoreDocument"/> may call it, so a chunk's denormalized copy can
    /// never drift from the document that owns it (data-model.md).
    /// </summary>
    internal void ApplyDocumentMetadata(
        Guid documentId, string storeId, string language, DocumentType documentType, DocumentStatus status)
    {
        DocumentId = documentId;
        StoreId = storeId;
        Language = language;
        DocumentType = documentType;
        Status = status;
    }
}
