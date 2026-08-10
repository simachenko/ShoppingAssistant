# Implementation Plan: Store Info RAG (Retrieval-Augmented Store Policy Answers)

**Branch**: `002-store-info-rag` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-store-info-rag/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Store-policy questions (delivery, payment, returns, warranty, loyalty, contacts) become a new,
strictly grounded capability inside the existing `ProductAdvisor` service — a new `store_info`
intent/route added to its already-hardened Turn Processing Cycle (001 spec.md FR-036–FR-137), not
a new microservice. A question is answered only from fragments a hybrid (vector + keyword) search
actually retrieves from a PostgreSQL/`pgvector`-backed knowledge base of `StoreDocument`/
`DocumentChunk` rows, scoped by store, language, and document type; every stated policy fact
carries a structured citation built deterministically by the application from the chunks that were
actually retrieved, never authored freely by the model. When nothing sufficiently relevant is
retrieved, the fixed, non-LLM "I don't have enough information" response is returned directly — no
narration call is even made. This mirrors, for free-text policy claims, exactly the same
"deterministic canonical data + constrained, structurally-incapable-of-fabricating narration"
architecture 001 already built for prices/specs/comparisons (`EvidenceEnvelope` +
`OutputValidationStage`), extended with a citation-token whitelist alongside the existing
numeric-claim whitelist.

Store-document retrieval is never left to the model's own tool-selection judgment: exactly like
`recommend` already calls `get_recommendations` directly through `IRecommendationService` instead
of offering it as an LLM-selectable tool, this feature adds a sibling `IStoreDocumentSearchService`
that the orchestrator calls directly — for the new `store_info` route always, and for the one
mixed-message case the spec calls for (a product-fact question with a store-policy question
attached, e.g. "is it in stock, and what's your return window") alongside `product_fact`'s
existing resolution. `search_store_documents` remains registered on the `/mcp` server's generic
tool catalog for external MCP clients, but is never placed in a turn's `chatOptions.Tools` by the
conversation orchestrator itself — so the reachability boundary (store-document evidence only ever
enters a `recommend`/`compare`/`checkout`/`smalltalk` turn never) is enforced structurally by which
code path runs, not by prompt instruction or per-turn tool-list scoping alone. A pure `store_info`
turn gets the full strict two-step pipeline `recommend` already uses (retrieval → Evidence
Envelope → constrained narration seeing only that Envelope); the mixed `product_fact` case keeps
using the existing legacy tool-calling bridge for product-reference resolution (unrelated,
larger-scoped work this feature doesn't take on) but still validates every citation post-hoc via
the same mechanism `ApplyGroundingIfApplicable` already applies to `comparison`/`checkoutLink`
today — because this project's own prior live smoke test empirically confirmed that path's LLM
output can go unchecked, this feature's own new guarantee (no citation without a real, retrieved
source) is never allowed to inherit that gap, even though the pre-existing gap for a *pure*
`product_fact` turn with no policy topic remains separately tracked and out of scope here.

Ingestion (creating/updating/withdrawing a `StoreDocument`, with synchronous chunking + embedding
generation) is a small internal-only HTTP surface on `ProductAdvisor.Api`, authenticated the same
way every other Advisor endpoint already is, reachable only server-to-server — no new UI, no new
service, no message broker, consistent with 001's own "no message broker in this version" decision
and this project's demo-scale document-count assumption.

## Technical Context

**Language/Version**: C# 13 / .NET 10, ASP.NET Core 10 — unchanged from 001.

**Primary Dependencies** (additive to 001's list): `Pgvector` + `Pgvector.EntityFrameworkCore`
(official pgvector-dotnet EF Core integration, research.md §2) for the new `vector(1536)` column
type on `DocumentChunk`; `Microsoft.Extensions.AI`'s `IEmbeddingGenerator<string, Embedding<float>>`
abstraction, backed by `Microsoft.Extensions.AI.OpenAI`'s `EmbeddingClient.AsIEmbeddingGenerator()`
against `text-embedding-3-small` (research.md §3) — reuses the already-referenced
`Microsoft.Extensions.AI.OpenAI` package, no new provider SDK; PostgreSQL's built-in `tsvector`/
`tsquery` full-text search (no new package — native to Npgsql/EF Core raw SQL, research.md §5) for
the keyword-search leg of hybrid search.

**Storage**: PostgreSQL — the same instance/schema `ProductAdvisor` already owns (001's `advisor`
schema), with the `pgvector` extension enabled (`CREATE EXTENSION IF NOT EXISTS vector;`, an EF
Core migration). No new database, no new managed service. Verified live against Neon's own
documentation (research.md §2): pgvector is available on Neon's free tier with no add-on required,
so this fits inside 001's already-adopted free-tier deployment model unchanged.

**Testing**: xUnit, same layering as 001 (research.md §14) — Domain unit tests for chunking/RRF
math (pure functions), Application-layer tests with faked retrieval results (no real Postgres),
Infrastructure/integration tests against a real `pgvector`-enabled Postgres via Testcontainers
(extending 001's existing Testcontainers pattern), and two new critical/release-blocking classes
appended to 001's existing fifteen-class agentic eval suite (research.md §14).

**Target Platform**: Unchanged — Linux containers, same `ProductAdvisor.Api` Docker image (no new
Dockerfile, no new deployable).

**Project Type**: Unchanged — this feature only adds to `ProductAdvisor`'s existing
Domain/Application/Infrastructure/API projects; the overall backend-microservices +
Gateway/BFF + Blazor topology is untouched.

**Performance Goals**: A `store_info`/RAG-touching turn meets the same p95 < 3 s conversational-
turn target 001 already sets for tool-calling turns — no separate, looser target. Retrieval itself
(two independent, index-backed SQL queries run concurrently, research.md §5) is a small fraction of
that budget; as with every other route, the LLM call(s) dominate latency, which remains outside
this system's direct control (001 plan.md, unchanged). The zero-relevant-chunks case is *faster*
than a typical turn, not slower — it skips the narration LLM call entirely (research.md §9).

**Constraints**: Must continue to run within the same free/low-cost tiers 001 already commits to
(Render free web services, Neon free-tier Postgres, a free-tier LLM provider) — no new paid
dependency introduced (research.md §2 confirms pgvector needs none). No message broker/queue
infrastructure introduced (carried forward from 001's research.md, reaffirmed research.md §10).

**Scale/Scope**: Demonstration scale — a knowledge base of tens to low hundreds of documents,
hundreds to low thousands of chunks, matching 001's own "demonstration scale, not production
e-commerce volumes" scope decision (001 plan.md Scale/Scope).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Design response | Status |
|---|---|---|
| I. Code Quality & Maintainability | New types live in the same layered projects 001 already established (`ProductAdvisor.Domain`/`.Application`/`.Infrastructure`/`.Api`) with the same interface-level coupling; the embedding provider is configured via the same externalized environment-variable mechanism as the chat provider (research.md §3), never hard-coded; the ingestion API's `X-Internal-Api-Key` requirement reuses 001's existing secret-handling, no new secret type introduced. | PASS |
| II. Reliable & Grounded Behavior | This is the principle this feature exists to extend into a new domain (free-text policy claims, not just numeric product facts): every `store_info`/mixed-message answer is built from a deterministically-assembled `EvidenceEnvelope` whose `CanonicalData` is the actually-retrieved chunk set, with a citation-token whitelist `OutputValidationStage` enforces before any response is sent (research.md §9) — the model narrates only what was retrieved and can be checked, never anything else, and zero-relevant-chunks short-circuits straight to a fixed, honest, non-LLM "not enough information" response (spec.md FR-006). RAG-touching turns are explicitly barred from the legacy blended path that this project's own live smoke test already showed can fabricate (research.md §8) — this is a stricter bar than that pre-existing gap, not an inherited weaker one. | PASS |
| III. Testing Standards | Domain unit tests for chunking/RRF (pure), Application tests for route/recipe selection and the new `OutputValidationStage` citation branch (faked I/O), Infrastructure integration tests against a real `pgvector` Postgres via Testcontainers, and two new critical (release-blocking) eval classes extending 001's existing eval suite (research.md §14) — all required green before merge, same gate 001 already established. | PASS |
| IV. Consistent UX | Citations are always returned as a structured field (`citations`), never left for a client to parse out of prose — the same "structured facts rendered by the UI's own markup, not parsed from Markdown" pattern 001 already uses (001 plan.md); a `storeInfo` answer is given in the user's conversation language when available, falling back to the same honest "not enough information" response rather than silently answering from a mismatched-language document (spec.md FR-009). | PASS |
| V. Performance & Resilience | The two retrieval queries (vector + keyword) run concurrently (research.md §5, 001's existing "independent reads run concurrently" rule); the zero-relevant-chunks case actively *avoids* an unnecessary LLM call (research.md §9) rather than adding one; if the knowledge-base retrieval store is unavailable, the turn returns an honest, typed degraded response (spec.md FR-024) rather than crashing — the same partial-failure posture 001 already requires for Catalog/Pricing outages. | PASS |
| VI. Observability & Safe Evolution | No new metric category — a citation-grounding rejection reuses the existing `GroundingFailure` counter (research.md §13), and the closed eleven-field `TurnLogFields` set (001 FR-133) is unchanged, preserving that guarantee rather than growing it per-feature; `StoreDocument`/`DocumentChunk` content changes are version-tracked as ordinary data (created/updated/withdrawn with timestamps), and the ingestion API itself is a plain, reviewable, revertible HTTP surface (no runtime-mutable prompt/rule logic introduced). | PASS |

No unjustified violations were identified; the **Complexity Tracking** table below is
intentionally empty. Adding retrieval/embedding infrastructure to an already-multi-project service
is the smallest change that satisfies the feature's explicit "part of ProductAdvisor, not a new
microservice" requirement — it is not complexity introduced for its own sake.

## Project Structure

### Documentation (this feature)

```text
specs/002-store-info-rag/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   ├── advisor-conversation-api-additions.md
│   └── advisor-mcp-tools-additions.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

All paths are additions inside the existing `src/ProductAdvisor/*` and `tests/*` projects
established by `001-smart-product-advisor`'s plan.md — no new project, no new solution entry
beyond what's listed below.

```text
src/ProductAdvisor/
├── ProductAdvisor.Domain/
│   ├── Store.cs                              # Store entity (data-model.md)
│   ├── StoreDocument.cs                      # StoreDocument aggregate root + DocumentType/DocumentStatus enums
│   ├── DocumentChunk.cs                      # DocumentChunk entity (owned by StoreDocument)
│   ├── Intent.cs                             # (existing file, extended) + StoreInfo value
│   └── StructuredIntent.cs                   # (existing file, extended) + MentionsStorePolicy field
│
├── ProductAdvisor.Application/
│   ├── AdvisorTurnResult.cs                  # (existing file, extended) + ForStoreInfo(...), Citations on answer
│   ├── Pipeline/
│   │   ├── PolicyRouter.cs                   # (existing file, extended) + Route.StoreInfo mapping
│   │   ├── EvidenceEnvelope.cs               # (existing file, extended) + citation-token AllowedClaims
│   │   ├── OutputValidationStage.cs          # (existing file, extended) + citation-checking branch,
│   │   │                                        store_info fallback branch
│   │   ├── IStoreDocumentSearchService.cs    # Port interface (mirrors IRecommendationService) — retrieval
│   │   │                                        is always invoked directly by application code, never
│   │   │                                        left to the LLM's tool selection (research.md §7/§8)
│   │   ├── StoreInfoRetrievalQuery.cs        # RetrievalQuery (data-model.md) — request-scoped, not persisted
│   │   └── CitationBuilder.cs                # Deterministically builds Citation[] from retrieved chunk ids
│   └── ConversationOrchestrator.cs           # (existing file, extended) — HandleStoreInfoAsync (strict,
│                                                # recommend-style pipeline) + ApplyGroundingIfApplicable
│                                                # extended for the mixed product_fact/MentionsStorePolicy
│                                                # case (research.md §8) — pure product_fact/compare/
│                                                # checkout/smalltalk keep using RunLegacyToolContinuationAsync
│                                                # exactly as before, unchanged
│
├── ProductAdvisor.Infrastructure/
│   ├── Tools/
│   │   ├── StoreDocumentSearchService.cs     # IStoreDocumentSearchService impl — hybrid (vector + tsvector/
│   │   │                                        RRF) query, store/language/type filtered (mirrors
│   │   │                                        RecommendationService.cs's role for IRecommendationService)
│   │   └── ComputeTools.cs                   # (existing file, extended) + search_store_documents MCP tool
│   │                                            # registration for external MCP clients, delegating to
│   │                                            # IStoreDocumentSearchService (mirrors get_recommendations'
│   │                                            # existing registration/delegation pattern)
│   ├── ToolRecipes/
│   │   └── ToolRecipe.cs                     # (existing file) — unchanged: Route.StoreInfo has no entry
│   │                                            # (same as Route.Recommend), Route.ProductFact's LLM-
│   │                                            # selectable set is unchanged (research.md §7/§8)
│   ├── Embeddings/
│   │   └── EmbeddingGeneratorExtensions.cs   # IEmbeddingGenerator<string, Embedding<float>> registration
│   ├── Ingestion/
│   │   ├── DocumentChunker.cs                # Paragraph/section-aware chunking (research.md §4)
│   │   └── StoreDocumentIngestionService.cs  # Upsert/withdraw + synchronous chunk+embed (research.md §10)
│   ├── Configurations/
│   │   ├── StoreConfiguration.cs             # EF Core mapping
│   │   ├── StoreDocumentConfiguration.cs     # EF Core mapping
│   │   └── DocumentChunkConfiguration.cs     # EF Core mapping — vector(1536) column, HNSW + GIN + B-tree indexes
│   └── Migrations/
│       └── <timestamp>_AddStoreInfoRag.cs    # CREATE EXTENSION vector; + new tables/indexes
│
└── ProductAdvisor.Api/
    ├── Program.cs                            # (existing file, extended) — MCP tool registration,
    │                                            # /api/store-documents endpoints, embedding-generator DI
    └── StoreDocuments/
        └── StoreDocumentEndpoints.cs         # POST /api/store-documents, DELETE /api/store-documents/{id}

tests/
├── ProductAdvisor.Domain.Tests/
│   ├── DocumentChunkerTests.cs               # structural chunking + section-label inheritance
│   └── ReciprocalRankFusionTests.cs          # pure RRF combination math
├── ProductAdvisor.Application.Tests/
│   ├── StoreInfoRoutingTests.cs              # Route.StoreInfo selection, MentionsStorePolicy attachment
│   └── OutputValidationStageCitationTests.cs # citation-token grounding/rejection, zero-chunk short-circuit
├── ProductAdvisor.Infrastructure.Tests/       # (new: first Infrastructure-layer test project for this
│   │                                            # service — 001 only had Domain/Application/Api test
│   │                                            # projects; hybrid SQL needs a real Postgres, which the
│   │                                            # existing Api.Tests Testcontainers fixture can also host,
│   │                                            # so this may instead land inside ProductAdvisor.Api.Tests
│   │                                            # depending on what's simplest at implementation time —
│   │                                            # see tasks.md)
│   └── StoreDocumentSearchToolIntegrationTests.cs
├── ProductAdvisor.Api.Tests/
│   ├── StoreInfoConversationContractTests.cs  # contracts/advisor-conversation-api-additions.md
│   ├── StoreDocumentIngestionApiTests.cs      # contracts/advisor-mcp-tools-additions.md ingestion endpoints
│   └── StoreInfoToolScopingTests.cs           # search_store_documents reachable only from store_info/
│                                                # MentionsStorePolicy product_fact recipes
└── EndToEnd.Tests/
    └── Evals/
        └── StoreInfoEvals.cs                  # the two new critical eval classes (research.md §14),
                                                 # appended alongside 001's existing CriticalEvals.cs
```

**Structure Decision**: Every addition lands inside `ProductAdvisor`'s existing four-project
layering (Domain → Application → Infrastructure → API) and existing test-project set, per the
feature's explicit "part of ProductAdvisor, not a separate microservice" requirement. The one open
question — whether hybrid-search integration tests get their own new `ProductAdvisor.
Infrastructure.Tests` project or reuse the existing `ProductAdvisor.Api.Tests` Testcontainers
fixture — is left for `tasks.md` to resolve at implementation time rather than decided
prematurely here, since either choice satisfies this plan's requirements equally and 001 already
established a working Testcontainers pattern either project could reuse.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations were identified during this design; this table is intentionally left
empty.
