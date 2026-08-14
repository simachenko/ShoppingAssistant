using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.Rag;

/// <summary>
/// The hybrid-search query behind store-knowledge retrieval (spec.md 002 FR-019, research.md §8):
/// a semantic (pgvector cosine) leg and a lexical (PostgreSQL full-text) leg, each ranked
/// independently, then combined by Reciprocal Rank Fusion into one ordered candidate set — never
/// one signal used merely to pre-filter the other.
/// </summary>
/// <remarks>
/// Raw SQL rather than LINQ because RRF (window functions over two CTEs joined by a full outer
/// join) has no LINQ expression. It is executed over the <see cref="DbContext"/>'s own connection,
/// so it participates in whatever transaction/connection EF already has open.
/// <para>
/// The query embedding is bound as text and cast with <c>::vector</c> rather than as a typed
/// pgvector parameter: the typed route would require the ambient <c>NpgsqlDataSource</c> to have
/// been built with pgvector's type mapping, which is an assumption about how the connection was
/// constructed elsewhere. The cast is equivalent and holds regardless.
/// </para>
/// </remarks>
public static class HybridSearchQuery
{
    // The mandatory store predicate (FR-020) and the active-only predicate (FR-012) are applied
    // inside BOTH legs, not after fusion — a chunk from another store or a superseded document
    // must never become a candidate in the first place, not merely lose on ranking.
    private const string Sql = """
        WITH vector_leg AS (
            SELECT "ChunkId",
                   ROW_NUMBER() OVER (ORDER BY "Embedding" <=> @queryEmbedding::vector) AS leg_rank
            FROM advisor.document_chunks
            WHERE "StoreId" = @storeId AND "Status" = 'Active'
            ORDER BY "Embedding" <=> @queryEmbedding::vector
            LIMIT @candidatesPerLeg
        ),
        keyword_leg AS (
            SELECT c."ChunkId",
                   ROW_NUMBER() OVER (ORDER BY ts_rank(c.content_tsvector, q.query) DESC) AS leg_rank
            FROM advisor.document_chunks c, plainto_tsquery('simple', @queryText) AS q(query)
            WHERE c."StoreId" = @storeId AND c."Status" = 'Active'
              AND c.content_tsvector @@ q.query
            ORDER BY ts_rank(c.content_tsvector, q.query) DESC
            LIMIT @candidatesPerLeg
        ),
        fused AS (
            SELECT COALESCE(v."ChunkId", k."ChunkId") AS chunk_id,
                   COALESCE(1.0 / (@rrfK + v.leg_rank), 0) + COALESCE(1.0 / (@rrfK + k.leg_rank), 0) AS rrf_score
            FROM vector_leg v
            FULL OUTER JOIN keyword_leg k ON v."ChunkId" = k."ChunkId"
        ),
        scored AS (
            SELECT c."ChunkId", c."DocumentId", d."Title", c."DocumentType", c."Language", c."Content",
                   -- Language and document type are RANKING PREFERENCES, never filters (FR-021,
                   -- FR-022, FR-023): a mismatch on either only costs position, so a shopper is
                   -- never refused an answer that exists merely because it is in another language
                   -- or a document whose type the classifier could not confirm.
                   -- @documentType is cast explicitly because it is the one parameter that can be
                   -- NULL (an unclassifiable question, FR-022). Postgres cannot infer a bare
                   -- NULL parameter's type and fails the whole statement with 42P08, so without
                   -- this cast every question the classifier could not categorize would error
                   -- instead of searching across all types.
                   -- Both sides are reduced to the lowercase primary subtag so `uk-UA` matches a
                   -- `uk` document (FR-030). The caller normalizes @language; the stored side is
                   -- normalized here because document tags come from ingestion, which this query
                   -- cannot constrain.
                   f.rrf_score
                     + CASE WHEN lower(split_part(c."Language", '-', 1)) = @language
                            THEN @languageBoost ELSE 0 END
                     + CASE WHEN @documentType::text IS NOT NULL AND c."DocumentType" = @documentType::text
                            THEN @documentTypeBoost ELSE 0 END AS score
            FROM fused f
            JOIN advisor.document_chunks c ON c."ChunkId" = f.chunk_id
            JOIN advisor.store_documents d ON d."DocumentId" = c."DocumentId"
        )
        SELECT "ChunkId", "DocumentId", "Title", "DocumentType", "Language", "Content", score
        FROM scored
        WHERE score >= @relevanceThreshold
        ORDER BY score DESC
        LIMIT @maxMatches;
        """;

    public static async Task<IReadOnlyList<StoreInfoMatch>> ExecuteAsync(
        DbContext dbContext,
        string queryText,
        float[] queryEmbedding,
        string storeId,
        string language,
        DocumentType? preferredDocumentType,
        StoreInfoOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandText = Sql;

            AddParameter(command, "queryEmbedding", new Vector(queryEmbedding).ToString());
            AddParameter(command, "queryText", queryText);
            AddParameter(command, "storeId", storeId);
            AddParameter(command, "language", language);
            // DBNull, not null: an unclassifiable question adds no type preference at all, which
            // the SQL's `@documentType IS NOT NULL` guard turns into a no-op rather than a filter.
            AddParameter(command, "documentType", preferredDocumentType?.ToString() ?? (object)DBNull.Value);
            AddParameter(command, "languageBoost", options.LanguageMatchBoost);
            AddParameter(command, "documentTypeBoost", options.DocumentTypeMatchBoost);
            AddParameter(command, "candidatesPerLeg", options.CandidatesPerLeg);
            AddParameter(command, "rrfK", options.RrfK);
            AddParameter(command, "relevanceThreshold", options.RelevanceThreshold);
            AddParameter(command, "maxMatches", options.MaxMatches);

            var matches = new List<StoreInfoMatch>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                matches.Add(new StoreInfoMatch
                {
                    ChunkId = reader.GetGuid(0),
                    DocumentId = reader.GetGuid(1),
                    DocumentTitle = reader.GetString(2),
                    DocumentType = Enum.Parse<DocumentType>(reader.GetString(3)),
                    Language = reader.GetString(4),
                    Content = reader.GetString(5),
                    Score = Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
                });
            }

            return matches;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
