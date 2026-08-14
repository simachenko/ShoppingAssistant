using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.SeedData;

/// <summary>
/// Inserts the demo knowledge base, embedding each chunk as it goes (002 research.md §12).
/// Idempotent: a document already present is left untouched, so a restart never duplicates
/// content or re-spends embedding calls — the same "only when enabled and absent" contract
/// ProductCatalog's demo seeding already follows.
/// </summary>
public static class StoreInfoSeeder
{
    public static async Task SeedAsync(
        AdvisorDbContext dbContext,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string storeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);

        var existingIds = await dbContext.StoreDocuments
            .Where(d => d.StoreId == storeId)
            .Select(d => d.DocumentId)
            .ToListAsync(cancellationToken);

        var missing = StoreInfoSeedData.Documents
            .Where(d => !existingIds.Contains(d.DocumentId))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        var createdAt = DateTimeOffset.UtcNow;

        foreach (var seed in missing)
        {
            // One batched embedding call per document rather than one per chunk — the provider
            // bills and rate-limits per request, and a document's chunks are always needed together.
            var embeddings = await embeddingGenerator.GenerateAsync(seed.Chunks, cancellationToken: cancellationToken);

            var chunks = seed.Chunks
                .Select((content, index) => new DocumentChunk(
                    DeterministicChunkId(seed.DocumentId, index),
                    index,
                    content,
                    embeddings[index].Vector.ToArray()))
                .ToList();

            dbContext.StoreDocuments.Add(new StoreDocument(
                seed.DocumentId, storeId, seed.Title, seed.Language, seed.DocumentType, chunks, createdAt));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Derives a stable chunk id from its document and position, so re-seeding an environment
    /// produces the same ids — a citation recorded in a log or a test fixture stays meaningful
    /// across rebuilds rather than pointing at a regenerated guid.
    /// </summary>
    /// <remarks>
    /// Hashed rather than derived arithmetically from the document id. An earlier version XOR-ed
    /// the order into the guid's last byte, which collided across documents whose ids differ only
    /// in that byte (this seed set's do) — the second document's first chunk reused the first
    /// document's second chunk id. Hashing the (document, order) pair removes that whole class of
    /// collision instead of relying on the seed guids keeping a convenient shape.
    /// </remarks>
    public static Guid DeterministicChunkId(Guid documentId, int order)
    {
        Span<byte> input = stackalloc byte[20];
        documentId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt32LittleEndian(input[16..], order);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
