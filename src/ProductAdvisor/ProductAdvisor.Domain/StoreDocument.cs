namespace ProductAdvisor.Domain;

/// <summary>
/// Aggregate root for one coherent store reference/policy document — delivery terms, a return
/// policy, and so on (spec.md 002 FR-013, data-model.md). Owns its <see cref="Chunks"/>; a chunk
/// never exists independently of the document that gives it a store, a language, and a type.
/// </summary>
public sealed class StoreDocument
{
    private readonly List<DocumentChunk> _chunks = [];

    public Guid DocumentId { get; private set; }

    /// <summary>
    /// The store this document belongs to — part of every retrieval query's mandatory filter
    /// (FR-020). Resolved for a request from deployment configuration, never from the shopper's
    /// question text (research.md §4).
    /// </summary>
    public string StoreId { get; private set; } = "";

    /// <summary>Human-readable name, shown directly in a citation (FR-008).</summary>
    public string Title { get; private set; } = "";

    public string Language { get; private set; } = "";
    public DocumentType DocumentType { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Active;

    /// <summary>The prior version this document replaces; null for a document's first version.</summary>
    public Guid? SupersedesDocumentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set if and only if <see cref="Status"/> is <see cref="DocumentStatus.Superseded"/>.</summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    public IReadOnlyList<DocumentChunk> Chunks => _chunks;

    public StoreDocument(
        Guid documentId,
        string storeId,
        string title,
        string language,
        DocumentType documentType,
        IReadOnlyList<DocumentChunk> chunks,
        DateTimeOffset createdAt,
        Guid? supersedesDocumentId = null)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException("DocumentId is required.", nameof(documentId));
        if (string.IsNullOrWhiteSpace(storeId))
            throw new ArgumentException("StoreId is required.", nameof(storeId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language is required.", nameof(language));
        ArgumentNullException.ThrowIfNull(chunks);

        // FR-013/data-model.md: an empty document can never be a retrieval source, so it can
        // never come into existence Active — the same "incomplete cannot be searchable" rule
        // ProductCatalog's Product.IsActive already applies to product data.
        if (chunks.Count == 0)
        {
            throw new ArgumentException(
                "A StoreDocument must have at least one chunk before it can be active.", nameof(chunks));
        }

        if (chunks.Select(c => c.Order).Distinct().Count() != chunks.Count)
        {
            throw new ArgumentException("Chunk Order values must be unique within a document.", nameof(chunks));
        }

        DocumentId = documentId;
        StoreId = storeId;
        Title = title;
        Language = language;
        DocumentType = documentType;
        CreatedAt = createdAt;
        SupersedesDocumentId = supersedesDocumentId;

        _chunks.AddRange(chunks);
        SyncChunkMetadata();
    }

    private StoreDocument()
    {
        // EF Core materialization only.
    }

    /// <summary>
    /// Marks this document as replaced (FR-014). One-directional: there is no
    /// <see cref="DocumentStatus.Superseded"/> → <see cref="DocumentStatus.Active"/> transition —
    /// reactivating old content is authored as a new version instead, keeping the audit trail
    /// linear. The status is pushed down onto every chunk so the mandatory
    /// <c>Status = 'Active'</c> retrieval predicate excludes them immediately (FR-012).
    /// </summary>
    public void Supersede(DateTimeOffset supersededAt)
    {
        if (Status == DocumentStatus.Superseded)
        {
            throw new InvalidOperationException(
                $"Document {DocumentId} is already superseded; a superseded document is never reactivated.");
        }

        Status = DocumentStatus.Superseded;
        SupersededAt = supersededAt;
        SyncChunkMetadata();
    }

    private void SyncChunkMetadata()
    {
        foreach (var chunk in _chunks)
        {
            chunk.ApplyDocumentMetadata(DocumentId, StoreId, Language, DocumentType, Status);
        }
    }
}
