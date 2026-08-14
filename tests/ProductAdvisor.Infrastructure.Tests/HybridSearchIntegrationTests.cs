using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure;
using ProductAdvisor.Infrastructure.Rag;
using Xunit;

namespace ProductAdvisor.Infrastructure.Tests;

/// <summary>
/// The hybrid-search query against a real Postgres with pgvector (spec.md 002
/// FR-012/FR-019–FR-023, research.md §8). These cover what unit tests structurally cannot: the
/// mandatory store filter, the active-only predicate, and the language/type ranking preferences
/// are all expressed in SQL, so only real SQL execution proves them.
/// </summary>
/// <remarks>
/// Embeddings here are deterministic synthetic vectors, not model output — the point is to verify
/// the query's filtering and fusion behavior, which must hold for any embedding. The keyword leg
/// still exercises real full-text matching against the seeded content.
/// </remarks>
[Collection(PgvectorCollection.Name)]
public class HybridSearchIntegrationTests(PgvectorFixture fixture)
{
    private const string StoreA = "store-a";
    private const string StoreB = "store-b";

    private static readonly StoreInfoOptions Options = new()
    {
        // Zero threshold: these tests assert which rows are *reachable*, so a relevance cutoff
        // would confound "correctly filtered out" with "merely ranked low".
        RelevanceThreshold = 0,
        CandidatesPerLeg = 20,
        MaxMatches = 10,
    };

    [Fact]
    public async Task A_query_never_returns_a_chunk_belonging_to_another_store()
    {
        await using var db = await NewDatabaseAsync();
        db.StoreDocuments.Add(Document(StoreA, "Delivery Terms", DocumentType.Delivery, "en",
            "Delivery within Kyiv takes 1-2 business days."));
        db.StoreDocuments.Add(Document(StoreB, "Delivery Terms", DocumentType.Delivery, "en",
            "Delivery within Lviv takes 9 business days."));
        await db.SaveChangesAsync();

        var matches = await Search(db, "How long does delivery take?", StoreA);

        // FR-020: the store predicate is mandatory, so store B's document is not merely ranked
        // lower — it is never a candidate at all.
        Assert.NotEmpty(matches);
        Assert.All(matches, m => Assert.DoesNotContain("Lviv", m.Content, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_superseded_document_is_never_returned_even_when_its_text_matches_best()
    {
        await using var db = await NewDatabaseAsync();
        var outdated = Document(StoreA, "Delivery Terms", DocumentType.Delivery, "en",
            "Delivery takes 3 business days.");
        outdated.Supersede(DateTimeOffset.UtcNow);
        db.StoreDocuments.Add(outdated);
        db.StoreDocuments.Add(Document(StoreA, "Delivery Terms (2026)", DocumentType.Delivery, "en",
            "Delivery takes 5 business days."));
        await db.SaveChangesAsync();

        var matches = await Search(db, "How many business days does delivery take?", StoreA);

        // FR-012/FR-014: version conflict is resolved by status before ranking runs, so the
        // advisor never has to choose between two conflicting answers at narration time.
        Assert.NotEmpty(matches);
        Assert.All(matches, m => Assert.DoesNotContain("3 business days", m.Content, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_matching_language_document_outranks_an_equivalent_one_in_another_language()
    {
        await using var db = await NewDatabaseAsync();
        db.StoreDocuments.Add(Document(StoreA, "Return Policy", DocumentType.Returns, "en",
            "You may return an unused product within 14 calendar days."));
        db.StoreDocuments.Add(Document(StoreA, "Політика повернення", DocumentType.Returns, "uk",
            "Ви можете повернути невикористаний товар протягом 14 календарних днів."));
        await db.SaveChangesAsync();

        var matches = await Search(db, "return policy", StoreA, language: "uk");

        Assert.Equal("uk", matches[0].Language);
    }

    [Fact]
    public async Task A_language_mismatch_still_returns_the_best_available_content()
    {
        await using var db = await NewDatabaseAsync();
        db.StoreDocuments.Add(Document(StoreA, "Warranty Terms", DocumentType.Warranty, "en",
            "All products carry a 12 month manufacturer warranty."));
        await db.SaveChangesAsync();

        // FR-021/FR-023: language is a preference, never a filter — a shopper asking in a
        // language the knowledge base does not have must still get the answer that exists.
        var matches = await Search(db, "warranty", StoreA, language: "uk");

        Assert.NotEmpty(matches);
        Assert.Equal("en", matches[0].Language);
    }

    [Fact]
    public async Task A_question_with_no_relevant_document_returns_nothing_rather_than_a_weak_match()
    {
        await using var db = await NewDatabaseAsync();
        db.StoreDocuments.Add(Document(StoreA, "Delivery Terms", DocumentType.Delivery, "en",
            "Delivery within Kyiv takes 1-2 business days."));
        await db.SaveChangesAsync();

        // With a realistic threshold, an unrelated question must clear nothing — this is the
        // condition FR-009's honest "couldn't find it" answer depends on.
        var options = new StoreInfoOptions { RelevanceThreshold = 0.5, CandidatesPerLeg = 20, MaxMatches = 10 };
        var matches = await HybridSearchQuery.ExecuteAsync(
            db, "do you price match competitors", SyntheticEmbedding("price match"), StoreA, "en", null, options,
            CancellationToken.None);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task A_region_qualified_language_still_matches_a_plain_language_document()
    {
        await using var db = await NewDatabaseAsync();
        db.StoreDocuments.Add(Document(StoreA, "Return Policy", DocumentType.Returns, "en",
            "You may return an unused product within 14 calendar days."));
        db.StoreDocuments.Add(Document(StoreA, "Політика повернення", DocumentType.Returns, "uk",
            "Ви можете повернути невикористаний товар протягом 14 календарних днів."));
        await db.SaveChangesAsync();

        // FR-030: `uk-UA` must be treated as `uk`. Before normalization this compared as a
        // mismatch, so a region-qualified shopper silently lost the same-language preference.
        var matches = await HybridSearchQuery.ExecuteAsync(
            db, "повернення товару", SyntheticEmbedding("повернення товару"), StoreA,
            LanguageTag.Normalize("uk-UA"), DocumentType.Returns, Options, CancellationToken.None);

        Assert.Equal("uk", matches[0].Language);
    }

    [Fact]
    public async Task A_question_with_no_identifiable_document_type_searches_across_all_types()
    {
        await using var db = await NewDatabaseAsync();
        db.StoreDocuments.Add(Document(StoreA, "Loyalty Programme", DocumentType.Loyalty, "en",
            "Members earn 1 bonus point for every 10 UAH spent."));
        await db.SaveChangesAsync();

        // Regression: a null document-type preference used to abort the whole query with
        // Postgres 42P08 ("could not determine data type of parameter"), so every question the
        // classifier could not categorize failed instead of searching across types (FR-022).
        Assert.Null(DocumentTypeClassifier.Classify("tell me how this all works"));

        var matches = await HybridSearchQuery.ExecuteAsync(
            db, "bonus point", SyntheticEmbedding("bonus point"), StoreA, "en",
            preferredDocumentType: null, Options, CancellationToken.None);

        Assert.NotEmpty(matches);
    }

    private static async Task<IReadOnlyList<StoreInfoMatch>> Search(
        AdvisorDbContext db, string query, string storeId, string language = "en") =>
        await HybridSearchQuery.ExecuteAsync(
            db, query, SyntheticEmbedding(query), storeId, language,
            DocumentTypeClassifier.Classify(query), Options, CancellationToken.None);

    private async Task<AdvisorDbContext> NewDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<AdvisorDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseVector())
            .Options;

        var db = new AdvisorDbContext(options);
        // Each test gets the schema rebuilt so one test's documents can never leak into another's
        // ranking, which would make a filtering assertion pass or fail for the wrong reason.
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    private static StoreDocument Document(
        string storeId, string title, DocumentType type, string language, params string[] chunkContents) =>
        new(Guid.NewGuid(), storeId, title, language, type,
            [.. chunkContents.Select((content, i) => new DocumentChunk(
                Guid.NewGuid(), i, content, SyntheticEmbedding(content)))],
            DateTimeOffset.UtcNow);

    /// <summary>
    /// A deterministic pseudo-embedding derived from the text. Similar strings get similar
    /// vectors, which is all these tests need — they assert filtering and ordering behavior, not
    /// semantic quality, which belongs to the real embedding model rather than this query.
    /// </summary>
    private static float[] SyntheticEmbedding(string text)
    {
        var vector = new float[DocumentChunk.EmbeddingDimension];
        foreach (var word in text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var slot = (uint)word.GetHashCode(StringComparison.Ordinal) % DocumentChunk.EmbeddingDimension;
            vector[slot] += 1f;
        }

        return vector;
    }
}
