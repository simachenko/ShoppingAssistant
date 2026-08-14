# Implementation Plan: Store Info RAG (Retrieval-Augmented Store Policy Answers)

**Branch**: `002-store-info-rag` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-store-info-rag/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

This feature adds one new, self-contained capability to the already-existing `ProductAdvisor`
service (`specs/001-smart-product-advisor`): answering a shopper's store-policy questions
(delivery, payment, returns/exchange, warranty, loyalty program, contacts, and other store rules)
grounded in a searchable store knowledge base, with a mandatory source citation on every claim
and an honest "not found" when the knowledge base does not cover the question. It is implemented
as **one new intent/route** (`store_info`) inside the advisor's existing fixed turn-processing
cycle (`specs/001-smart-product-advisor/spec.md` FR-036–FR-047) — not a new service, not a new
UI surface, not a parallel processing flow (spec.md 002 FR-002/FR-003).

The route follows the **same fully-deterministic, single-terminal-call shape the `recommend`
route already uses**, rather than the "legacy" free tool-invocation loop `product_fact`/
`compare`/`checkout` still go through today (research.md §2) — a new
`IStoreInfoRetrievalService.RetrieveAsync` call, invoked directly by
`ProductAdvisor.Application` from the classified query, returns a ranked, threshold-filtered set
of matched document fragments; those are wrapped in the existing `EvidenceEnvelope` (extended
with a new `Citations` field) and narrated through the existing, unmodified `NarrationPrompt` +
`OutputValidationStage` — the same structural, non-prompt-only grounding guarantee `recommend`
already gets, satisfying spec.md 002 FR-007–FR-012 (grounded answers, mandatory citations,
deterministic honesty, deterministic version-conflict resolution) by construction rather than by
instruction alone. Retrieved content is protected from prompt injection (FR-027) for free, since
`NarrationPrompt`'s existing system prompt already instructs the model to treat Evidence content
as data, never instructions — no new mechanism, confirmed reuse (research.md §10).

Store reference content is modeled as two new, EF Core–mapped entities in
`ProductAdvisor.Domain` — `StoreDocument` (one coherent policy document: store, language,
document type, lifecycle `Active`/`Superseded` status) and its child `DocumentChunk` (a bounded,
independently retrievable fragment carrying its own vector embedding) — persisted in the
*existing* `advisor` Postgres schema/database (`AdvisorDbContext`), not a new database or service
(research.md §1). Retrieval combines PostgreSQL full-text search and `pgvector` cosine-similarity
search over the same `DocumentChunk` rows via Reciprocal Rank Fusion (research.md §8), filtered
by a **mandatory** store predicate (spec.md 002 FR-019/FR-020 — resolved from deployment
configuration per the 2026-08-10 clarification, never per-user/session) and **preference**
(non-filtering) boosts for language and document type (FR-021/FR-022). A `Superseded` document's
chunks are excluded from every query, which is this feature's deterministic resolution of what
would otherwise be a same-topic version conflict (FR-012/FR-014).

Product price, availability, specifications, and comparisons are explicitly, structurally
untouched by this feature: the `store_info` route's tool recipe contains no product-data tool,
and no existing route's recipe gains access to the new store-knowledge retrieval capability
(spec.md 002 FR-004/FR-005, `contracts/advisor-mcp-tools-additions.md`) — the same
`ToolRecipe`/per-route tool-scoping mechanism `specs/001-smart-product-advisor` already relies on
for every other exclusion of this kind.

## Technical Context

**Language/Version**: C# 13 / .NET 10, ASP.NET Core 10 — unchanged from
`specs/001-smart-product-advisor/plan.md`; this feature adds files to already-existing projects,
it does not introduce a new runtime or language.

**Primary Dependencies**: Everything `specs/001-smart-product-advisor/plan.md` already lists for
`ProductAdvisor.*`, plus: `Pgvector` + `Pgvector.EntityFrameworkCore` (vector column type/LINQ
operators on top of the already-referenced `Npgsql.EntityFrameworkCore.PostgreSQL`, research.md
§6) and `Microsoft.Extensions.AI`'s `IEmbeddingGenerator<string, Embedding<float>>` abstraction
(same package family already referenced for chat, research.md §7) — no new package family is
introduced, only additional capability from packages/abstractions already in the dependency
graph.

**Storage**: PostgreSQL — the same `advisordb` database and `advisor` schema
`specs/001-smart-product-advisor/plan.md` already establishes for `ConversationSession`, with the
`vector` extension enabled and two new tables (`StoreDocument`, `DocumentChunk`, the latter with
an HNSW index on its embedding column and a generated `tsvector` column with a GIN index) added
by a new EF Core migration (research.md §1/§6). No new database instance, no new schema, no
cross-service data access — `store_info` retrieval never calls Catalog or Pricing, and no other
route ever queries `StoreDocument`/`DocumentChunk`.

**Testing**: xUnit, matching `specs/001-smart-product-advisor/plan.md`'s pyramid exactly — pure
unit tests for RRF scoring/threshold logic and entity validation in `ProductAdvisor.Domain.Tests`;
fake-backed (`FakeStoreInfoRetrievalService`, mirroring `FakeRecommendationService`) tests for
`PolicyRouter`'s new branch, `EvidenceEnvelopeBuilder.ForStoreInfo`, and the citation/honesty
contract in `ProductAdvisor.Application.Tests`; Testcontainers-backed Postgres integration tests
for the real hybrid-search query, store isolation, and `Superseded`-exclusion in
`ProductAdvisor.Infrastructure.Tests`(new; see Project Structure); one new agentic eval case
added to the existing eval suite for store-policy honesty (research.md §13).

**Target Platform**: Linux containers, same deployables as `specs/001-smart-product-advisor` — no
new deployable image; `ProductAdvisor.Api`'s existing Dockerfile/service is unchanged in kind, it
now also runs one additional EF Core migration at startup.

**Project Type**: Extension to an existing backend microservice bounded context
(`ProductAdvisor`) within the already-established multi-project .NET solution — not a new
service.

**Performance Goals**: A `store_info` turn's shape matches `recommend`'s existing latency
expectation (`specs/001-smart-product-advisor/plan.md`: "conversation turns that only call
[deterministic tools] (no LLM clarification loop beyond one call): p95 < 3 s end-to-end") — one
extraction LLM call, one deterministic retrieval call (embedding generation + one hybrid-search
SQL query), one narration LLM call. The HNSW vector index and GIN full-text index (research.md
§6/§8) keep the retrieval call's own cost close to Catalog/Pricing's existing indexed-lookup
latency budget rather than scanning the full `DocumentChunk` table.

**Constraints**: Same free/low-cost-tier constraints as `specs/001-smart-product-advisor/plan.md`
(Render free web services, Neon free-tier Postgres, free-tier LLM provider) — this feature adds
one more embedding-generation call per `store_info` turn against the same provider, which MUST
also be treated as retryable-with-backoff on rate-limit responses like every other outbound call
already is (constitution Principle V). No new external service account is introduced (research.md
§6's "reuse Postgres, not a dedicated vector database" decision exists specifically to hold this
constraint).

**Scale/Scope**: Demonstration scale, consistent with `specs/001-smart-product-advisor/plan.md`
— a handful of store documents (delivery, payment, returns, warranty, loyalty, contacts) split
into tens of chunks total, single configured store (research.md §4), not a production
multi-tenant knowledge-base volume.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Design response | Status |
|---|---|---|
| I. Code Quality & Maintainability | New code follows the exact existing layering — `StoreDocument`/`DocumentChunk` (Domain, pure entities), `IStoreInfoRetrievalService`/`IStoreContext` (Application, interfaces only), the actual hybrid-search implementation and EF Core mapping (Infrastructure) — with the same interface-only coupling between layers 001 already established. `LLM_PROVIDER_EMBEDDING_MODEL` and the store-id configuration value are environment-variable-driven, never hard-coded, matching Principle I and 001's existing config conventions. | PASS |
| II. Reliable & Grounded Behavior | Every store-policy claim narration makes is checked against that turn's `EvidenceEnvelope.AllowedClaims`, derived only from actually-retrieved chunk content (research.md §9) — the same `OutputValidationStage` mechanism, unmodified, that already makes fabrication structurally (not just promptly) impossible for `recommend`. An empty/below-threshold retrieval result produces the fixed, honest "not found" statement rather than any narration attempt (FR-009/FR-010). Citations (FR-008) give every claim a checkable source, a stronger grounding guarantee than 001 required for narration text alone. | PASS |
| III. Testing Standards | Domain-level RRF/threshold math and entity rules are pure-unit-tested; the new route's application-layer wiring is tested against a fake retrieval service; real hybrid-search behavior (including store isolation and superseded-document exclusion) is integration-tested against real Postgres via Testcontainers — mirroring 001's pyramid exactly (research.md §13), plus a new honesty-focused eval case added to the existing mandatory eval suite. | PASS |
| IV. Consistent UX | `store_info` answers use the same `answer` result type, the same narration/structured-data split, and the same language-preservation behavior (FR-021, matching 001's "MUST NOT silently switch... language") already governing every other route — citations are additive structured data, not a new inconsistent presentation shape. | PASS |
| V. Performance & Resilience | The embedding-generation call and the hybrid-search query both sit behind the same timeout/retry/backoff posture (constitution Principle V, research.md §11) already required for every other outbound call; a retrieval-dependency outage degrades to the existing `error` result type (FR-028) rather than hanging the turn or silently falling back to ungrounded narration. | PASS |
| VI. Observability & Safe Evolution | Which document(s)/chunk(s) were retrieved and cited is logged per turn (spec.md 002 FR-026), consistent with 001's existing per-turn logging discipline; the new `NarrationPrompt` usage and `EvidenceEnvelopeBuilder.ForStoreInfo` are plain version-controlled C#, not runtime-mutable state, matching Principle VI's "prompts... version-controlled alongside the code that depends on them." | PASS |

No unjustified violations were identified; the **Complexity Tracking** table below is
intentionally empty. Reusing the existing `advisor` schema/database and the existing
Envelope/narration mechanism (rather than introducing a new service or a new prompting
mechanism) is a complexity-*reducing* choice relative to the alternatives research.md considered
and rejected, not a new complexity requiring justification.

## Project Structure

### Documentation (this feature)

```text
specs/002-store-info-rag/
├── plan.md                # This file (/speckit-plan command output)
├── research.md            # Phase 0 output (/speckit-plan command)
├── data-model.md          # Phase 1 output (/speckit-plan command)
├── quickstart.md          # Phase 1 output (/speckit-plan command)
├── contracts/              # Phase 1 output (/speckit-plan command)
│   ├── advisor-mcp-tools-additions.md
│   └── advisor-conversation-api-additions.md
├── checklists/
│   └── requirements.md
└── tasks.md                # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

All paths below are **additions to the existing `specs/001-smart-product-advisor` solution
layout** — no new top-level project group, no new deployable, no new `.sln` entry outside the
`ProductAdvisor.*` set that already exists.

```text
src/
├── ProductAdvisor.sln                                # unchanged — new projects/files added to
│                                                       # already-referenced ProductAdvisor.* projects
│
├── ProductAdvisor/
│   ├── ProductAdvisor.Domain/
│   │   ├── StoreDocument.cs                          # aggregate root: StoreId, Title, Language,
│   │   │                                               # DocumentType, Status, SupersedesDocumentId
│   │   ├── DocumentChunk.cs                           # owned child entity: Content, Embedding,
│   │   │                                               # Order — data-model.md
│   │   ├── DocumentType.cs                            # enum (Delivery/Payment/Returns/Warranty/
│   │   │                                               # Loyalty/Contacts/Other), DocumentStatus.cs
│   │   ├── StoreInfoAnswer.cs                          # value objects: StoreInfoMatch,
│   │   │                                               # StoreInfoAnswer, Citation
│   │   └── StructuredIntent.cs                         # MODIFIED: Intent gains StoreInfo
│   │                                                    # (research.md §3)
│   │
│   ├── ProductAdvisor.Application/
│   │   ├── Pipeline/
│   │   │   ├── PolicyRouter.cs                         # MODIFIED: Route gains StoreInfo;
│   │   │   │                                            # SelectRoute maps Intent.StoreInfo →
│   │   │   │                                            # Route.StoreInfo unconditionally
│   │   │   ├── IStoreInfoRetrievalService.cs            # NEW — mirrors IRecommendationService
│   │   │   ├── IStoreContext.cs                         # NEW — research.md §4
│   │   │   ├── EvidenceEnvelope.cs                      # MODIFIED: + Citations field
│   │   │   └── EvidenceEnvelopeBuilder.cs               # MODIFIED: + ForStoreInfo(StoreInfoAnswer)
│   │   ├── ConversationOrchestrator.cs                  # MODIFIED: new HandleStoreInfoAsync
│   │   │                                                # (mirrors HandleRecommendAsync), wired
│   │   │                                                # into both ProcessMessageAsync and
│   │   │                                                # ProcessMessageStreamAsync's route switch
│   │   ├── AdvisorTurnResult.cs                         # MODIFIED: + Citations field
│   │   └── Contracts/ConversationTurnResponse.cs        # MODIFIED: + Citations field,
│   │                                                     # CitationResponse type
│   │
│   ├── ProductAdvisor.Infrastructure/
│   │   ├── Rag/
│   │   │   ├── StoreInfoRetrievalService.cs             # IStoreInfoRetrievalService impl:
│   │   │   │                                            # embedding call + hybrid-search query +
│   │   │   │                                            # threshold cutoff (research.md §8/§9)
│   │   │   ├── HybridSearchQuery.cs                      # the FromSqlInterpolated RRF query
│   │   │   │                                            # (research.md §8)
│   │   │   ├── DocumentTypeClassifier.cs                 # keyword→DocumentType lookup
│   │   │   │                                            # (research.md §5)
│   │   │   └── ConfiguredStoreContext.cs                 # IStoreContext impl (research.md §4)
│   │   ├── Configurations/
│   │   │   ├── StoreDocumentConfiguration.cs             # EF Core mapping, incl. HasPostgresExtension
│   │   │   │                                            # ("vector"), HNSW index (research.md §6)
│   │   │   └── DocumentChunkConfiguration.cs             # incl. generated tsvector column + GIN index
│   │   ├── Migrations/                                   # NEW migration: vector extension, both
│   │   │                                                 # tables, both indexes
│   │   ├── SeedData/
│   │   │   └── StoreInfoSeedData.cs                      # demo StoreDocument/DocumentChunk fixtures
│   │   │                                                 # (research.md §12), same pattern as
│   │   │                                                 # ProductCatalog/PricingAvailability SeedData
│   │   ├── Tools/AdvisorToolCatalog.cs                   # MODIFIED: + retrieve_store_info wrapper
│   │   ├── Tools/RagTools.cs                             # NEW — [McpServerToolType] hosting
│   │   │                                                 # retrieve_store_info for external MCP
│   │   │                                                 # clients (contracts/advisor-mcp-tools-
│   │   │                                                 # additions.md); internal orchestrator
│   │   │                                                 # calls IStoreInfoRetrievalService
│   │   │                                                 # directly, not through this wrapper
│   │   │                                                 # (research.md §2)
│   │   └── ToolRecipes/ToolRecipe.cs                     # MODIFIED: Route.StoreInfo → EmptyTools
│   │                                                     # (same as Recommend — research.md §3)
│   └── ProductAdvisor.Api/                               # unchanged surface — new intent flows
│                                                          # through the existing conversation
│                                                          # endpoints and /mcp; no new endpoint
│
└── Aspire/AppHost/AppHost.cs                             # unchanged (advisordb already provisioned);
                                                            # LLM_PROVIDER_EMBEDDING_MODEL added to
                                                            # existing ProductAdvisor.Api env wiring

tests/
├── ProductAdvisor.Domain.Tests/
│   ├── DocumentLifecycleTests.cs                         # NEW — Active/Superseded transition rules
│   └── HybridSearchScoringTests.cs                        # NEW — RRF fusion + threshold cutoff, pure
├── ProductAdvisor.Application.Tests/
│   ├── FakeStoreInfoRetrievalService.cs                    # NEW — mirrors FakeRecommendationService
│   ├── StoreInfoRoutingTests.cs                            # NEW — PolicyRouter's new branch
│   └── StoreInfoHonestyTests.cs                            # NEW — citation presence, empty-match
│                                                           # honesty fallback, mirrors
│                                                           # NotFoundHonestyTests.cs's pattern
├── ProductAdvisor.Infrastructure.Tests/                     # NEW test project — Testcontainers-backed
│   └── HybridSearchIntegrationTests.cs                      # real Postgres: store isolation,
│                                                            # superseded-document exclusion,
│                                                            # language/type preference ranking
└── ProductAdvisor.Api.Tests/                                # MODIFIED — retrieve_store_info MCP
                                                              # tool contract test, conversation API
                                                              # citations-field contract test

docker-compose.yml                                          # unchanged (Postgres already present;
                                                              # vector extension enabled by migration,
                                                              # not compose config)
render.yaml                                                  # MODIFIED: + LLM_PROVIDER_EMBEDDING_MODEL
                                                              # env var on the existing ProductAdvisor
                                                              # service entry
```

**Structure Decision**: Every new file lands inside an already-existing `ProductAdvisor.*`
project, following that project's existing internal folder conventions (`Pipeline/` for cycle
stages, `Tools/`/`ToolRecipes/` for MCP surfaces, `Configurations/`/`Migrations/`/`SeedData/` for
persistence) — the one new addition to that convention is a `Rag/` subfolder under
`ProductAdvisor.Infrastructure` and a new `ProductAdvisor.Infrastructure.Tests` project (today,
`ProductAdvisor.Infrastructure` has no dedicated test project of its own; the hybrid-search
query is the first piece of Infrastructure-layer logic in this bounded context that needs a real
Postgres to test meaningfully, matching how `ProductCatalog.Api.Tests`/
`PricingAvailability.Api.Tests` already combine contract and Testcontainers-integration tests for
their own Infrastructure-layer repository code). This directly matches spec.md 002 FR-002/FR-003
(new intent/route inside the existing cycle) and FR-004/FR-005 (product tools and the new
retrieval capability structurally excluded from each other's reach).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations were identified during this design; this table is intentionally
left empty.
