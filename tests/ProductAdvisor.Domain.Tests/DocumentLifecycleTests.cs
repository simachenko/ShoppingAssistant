using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Domain.Tests;

/// <summary>
/// The knowledge base's construction and lifecycle rules (spec.md 002 FR-013/FR-014,
/// data-model.md). These are what make FR-012's version-conflict resolution deterministic: a
/// superseded document's chunks carry the superseded status themselves, so the retrieval query's
/// active-only predicate excludes them without needing a join or a caller's cooperation.
/// </summary>
public class DocumentLifecycleTests
{
    private static float[] Embedding(float seed = 0.1f) =>
        [.. Enumerable.Repeat(seed, DocumentChunk.EmbeddingDimension)];

    private static DocumentChunk Chunk(int order = 0, string content = "Delivery takes 1-2 business days.") =>
        new(Guid.NewGuid(), order, content, Embedding());

    private static StoreDocument Document(params DocumentChunk[] chunks) =>
        new(Guid.NewGuid(), "store-1", "Delivery Terms", "en", DocumentType.Delivery,
            chunks.Length == 0 ? [Chunk()] : chunks, DateTimeOffset.UtcNow);

    [Fact]
    public void A_document_cannot_be_created_without_at_least_one_chunk()
    {
        // FR-013: an empty document could never ground an answer, so it must never exist as a
        // retrievable source in the first place.
        var exception = Assert.Throws<ArgumentException>(() => new StoreDocument(
            Guid.NewGuid(), "store-1", "Delivery Terms", "en", DocumentType.Delivery, [], DateTimeOffset.UtcNow));

        Assert.Contains("at least one chunk", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_new_document_is_active_and_stamps_its_metadata_onto_every_chunk()
    {
        var document = Document(Chunk(0), Chunk(1, "Free delivery over 2000 UAH."));

        Assert.Equal(DocumentStatus.Active, document.Status);
        Assert.Null(document.SupersededAt);
        Assert.All(document.Chunks, chunk =>
        {
            Assert.Equal(document.DocumentId, chunk.DocumentId);
            Assert.Equal("store-1", chunk.StoreId);
            Assert.Equal("en", chunk.Language);
            Assert.Equal(DocumentType.Delivery, chunk.DocumentType);
            Assert.Equal(DocumentStatus.Active, chunk.Status);
        });
    }

    [Fact]
    public void Superseding_a_document_pushes_the_status_down_onto_its_chunks()
    {
        var document = Document(Chunk(0), Chunk(1, "Free delivery over 2000 UAH."));
        var supersededAt = DateTimeOffset.UtcNow;

        document.Supersede(supersededAt);

        Assert.Equal(DocumentStatus.Superseded, document.Status);
        Assert.Equal(supersededAt, document.SupersededAt);
        // The denormalized copy is what the retrieval query actually filters on — if it did not
        // travel with the status change, a superseded document would keep being cited (FR-012).
        Assert.All(document.Chunks, chunk => Assert.Equal(DocumentStatus.Superseded, chunk.Status));
    }

    [Fact]
    public void A_superseded_document_is_never_reactivated()
    {
        var document = Document();
        document.Supersede(DateTimeOffset.UtcNow);

        // One-directional by design: a returning policy is authored as a new version, so the
        // audit trail stays linear (data-model.md).
        Assert.Throws<InvalidOperationException>(() => document.Supersede(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Chunk_order_must_be_unique_within_a_document()
    {
        var exception = Assert.Throws<ArgumentException>(() => Document(Chunk(0), Chunk(0, "Duplicate position.")));

        Assert.Contains("unique", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_chunk_rejects_an_embedding_of_the_wrong_dimension()
    {
        // pgvector pins a dimension per column: silently truncating or padding here would produce
        // a stored vector that no longer represents the text, corrupting retrieval invisibly.
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentChunk(Guid.NewGuid(), 0, "Some content.", [0.1f, 0.2f]));

        Assert.Contains("dimension", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_chunk_requires_content()
    {
        Assert.Throws<ArgumentException>(() => new DocumentChunk(Guid.NewGuid(), 0, "   ", Embedding()));
    }
}
