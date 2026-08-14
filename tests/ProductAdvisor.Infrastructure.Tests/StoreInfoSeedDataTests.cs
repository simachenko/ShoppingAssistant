using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure.SeedData;
using Xunit;

namespace ProductAdvisor.Infrastructure.Tests;

/// <summary>
/// Shape checks on the demo knowledge base (002 research.md §12). The identity test below exists
/// because a real defect shipped past review: chunk ids were derived by XOR-ing the chunk's order
/// into the document id's last byte, which silently collided between documents whose ids differ
/// only in that byte — and every id in this seed set does. Seeding failed with an EF tracking
/// error only when actually run.
/// </summary>
public class StoreInfoSeedDataTests
{
    [Fact]
    public void Every_seed_chunk_gets_a_distinct_id()
    {
        var ids = StoreInfoSeedData.Documents
            .SelectMany(d => Enumerable.Range(0, d.Chunks.Count).Select(order => ChunkId(d.DocumentId, order)))
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Chunk_ids_are_stable_across_calls_so_re_seeding_does_not_duplicate_content()
    {
        var first = ChunkId(StoreInfoSeedData.DeliveryDocumentId, 0);
        var second = ChunkId(StoreInfoSeedData.DeliveryDocumentId, 0);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Every_seed_document_has_a_distinct_id_and_at_least_one_chunk()
    {
        var documentIds = StoreInfoSeedData.Documents.Select(d => d.DocumentId).ToList();

        Assert.Equal(documentIds.Count, documentIds.Distinct().Count());
        Assert.All(StoreInfoSeedData.Documents, d => Assert.NotEmpty(d.Chunks));
        Assert.All(StoreInfoSeedData.Documents, d => Assert.All(d.Chunks, c => Assert.False(string.IsNullOrWhiteSpace(c))));
    }

    [Fact]
    public void The_seed_set_covers_every_policy_area_the_specification_names()
    {
        // spec.md 002 FR-015's minimum taxonomy — quickstart.md's scenarios assume each of these
        // is answerable, so a missing one would make those scenarios silently untestable.
        var covered = StoreInfoSeedData.Documents.Select(d => d.DocumentType).ToHashSet();

        Assert.Contains(DocumentType.Delivery, covered);
        Assert.Contains(DocumentType.Payment, covered);
        Assert.Contains(DocumentType.Returns, covered);
        Assert.Contains(DocumentType.Warranty, covered);
        Assert.Contains(DocumentType.Loyalty, covered);
        Assert.Contains(DocumentType.Contacts, covered);
    }

    // Calls the production derivation directly rather than reimplementing it — a local copy would
    // keep passing even if the real one regressed, which is precisely the failure this test exists
    // to catch.
    private static Guid ChunkId(Guid documentId, int order) =>
        StoreInfoSeeder.DeterministicChunkId(documentId, order);
}
