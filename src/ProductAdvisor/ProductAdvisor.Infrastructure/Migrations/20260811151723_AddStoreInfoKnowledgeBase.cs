using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;
using ProductAdvisor.Infrastructure.Configurations;

#nullable disable

// EF-scaffolded migration: the multi-column index calls below use inline array literals because
// that is what `dotnet ef migrations add` emits. Rewriting them to satisfy CA1861 would make the
// file diverge from what regeneration produces, for no runtime benefit in code that runs once.
#pragma warning disable CA1861

namespace ProductAdvisor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreInfoKnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "store_documents",
                schema: "advisor",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SupersedesDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_documents", x => x.DocumentId);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                schema: "advisor",
                columns: table => new
                {
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_chunks", x => x.ChunkId);
                    table.ForeignKey(
                        name: "FK_document_chunks_store_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "advisor",
                        principalTable: "store_documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_DocumentId_Order",
                schema: "advisor",
                table: "document_chunks",
                columns: new[] { "DocumentId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_StoreId_Status",
                schema: "advisor",
                table: "document_chunks",
                columns: new[] { "StoreId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_store_documents_StoreId_Status_DocumentType",
                schema: "advisor",
                table: "store_documents",
                columns: new[] { "StoreId", "Status", "DocumentType" });

            // Hybrid search's keyword leg (002 research.md §8). Deliberately not an EF-mapped
            // property: the search itself is raw SQL, so mapping it would only push an
            // NpgsqlTsVector dependency into the Domain entity. The 'simple' text-search config
            // is chosen over a language-specific one because the knowledge base spans languages
            // (FR-021) and a per-row config cannot be expressed in a generated column.
            migrationBuilder.Sql($"""
                ALTER TABLE advisor.document_chunks
                ADD COLUMN {DocumentChunkConfiguration.ContentTsVectorColumn} tsvector
                GENERATED ALWAYS AS (to_tsvector('simple', "Content")) STORED;
                """);

            migrationBuilder.Sql($"""
                CREATE INDEX "IX_document_chunks_content_tsvector"
                ON advisor.document_chunks
                USING GIN ({DocumentChunkConfiguration.ContentTsVectorColumn});
                """);

            // Hybrid search's vector leg. Cosine distance (vector_cosine_ops) is the standard
            // metric for normalized text embeddings; HNSW gives indexed approximate-nearest-
            // neighbour search rather than a full scan of every chunk (002 research.md §6).
            migrationBuilder.Sql("""
                CREATE INDEX "IX_document_chunks_embedding_hnsw"
                ON advisor.document_chunks
                USING hnsw ("Embedding" vector_cosine_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_chunks",
                schema: "advisor");

            migrationBuilder.DropTable(
                name: "store_documents",
                schema: "advisor");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
