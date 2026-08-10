---

description: "Task list for the Store Info RAG feature"
---

# Tasks: Store Info RAG (Retrieval-Augmented Store Policy Answers)

**Input**: Design documents from `/specs/002-store-info-rag/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included and required, not optional — the project constitution (`.specify/memory/constitution.md`
Principle III) mandates automated coverage for core business logic and MCP tools, and
`001-smart-product-advisor`'s own `tasks.md` included tests throughout every phase; this feature
follows the same established practice.

**Organization**: Tasks are grouped by user story (spec.md) to enable independent implementation
and testing of each story. All paths are additions inside the existing `src/ProductAdvisor/*` and
`tests/*` projects — no new project or solution entry (plan.md Project Structure).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in every task description

---

## Phase 1: Setup

**Purpose**: Package dependencies this whole feature needs.

- [ ] T001 Add `Pgvector` and `Pgvector.EntityFrameworkCore` NuGet package references to `src/ProductAdvisor/ProductAdvisor.Infrastructure/ProductAdvisor.Infrastructure.csproj` (plan.md Technical Context, research.md §2)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entities, closed-set/routing extensions, response-shape extension, DB schema,
embedding generator, and the pure chunking/ranking algorithms every user story needs to exist
before any of them can be built end-to-end.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 Add `DocumentType` enum (`Delivery`, `Payment`, `Returns`, `Warranty`, `Loyalty`, `Contacts`, `Other`) and `DocumentStatus` enum (`Active`, `Withdrawn`) in `src/ProductAdvisor/ProductAdvisor.Domain/StoreDocument.cs` (data-model.md `StoreDocument`)
- [ ] T003 [P] Create `Store` entity (`Id`, `Code`, `Name`) in `src/ProductAdvisor/ProductAdvisor.Domain/Store.cs` (data-model.md `Store`)
- [ ] T004 Create `StoreDocument` aggregate root in `src/ProductAdvisor/ProductAdvisor.Domain/StoreDocument.cs` (same file as T002; depends on T002) (data-model.md `StoreDocument`)
- [ ] T005 Create `DocumentChunk` entity in `src/ProductAdvisor/ProductAdvisor.Domain/DocumentChunk.cs` (depends on T004) (data-model.md `DocumentChunk`)
- [ ] T006 [P] Create `Citation` value record (`DocumentTitle`, `DocumentType`, `SectionLabel`, `SourceLabel`) in `src/ProductAdvisor/ProductAdvisor.Domain/Citation.cs` (data-model.md `Citation`)
- [ ] T007 Add `Intent.StoreInfo` value (wire literal `store_info`, `JsonStringEnumMemberName`) to the `Intent` enum in `src/ProductAdvisor/ProductAdvisor.Domain/StructuredIntent.cs` (research.md §7)
- [ ] T008 Add `MentionsStorePolicy: bool` field (default `false`) to the `StructuredIntent` record in `src/ProductAdvisor/ProductAdvisor.Domain/StructuredIntent.cs` (same file as T007; depends on T007) (data-model.md `StructuredIntent`)
- [ ] T009 Add `Route.StoreInfo` value and a `PolicyRouter.SelectRoute` mapping (`Intent.StoreInfo` → `Route.StoreInfo` unconditionally once confidence clears `ConfidenceThreshold`, no essential-field gating) in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/PolicyRouter.cs` (depends on T007) (research.md §7)
- [ ] T010 [P] Create the `RetrievalQuery` record (`QueryText`, `StoreId`, `Language`, `DocumentType`, `MaxResults`) in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/StoreInfoRetrievalQuery.cs` (data-model.md `RetrievalQuery`)
- [ ] T011 [P] Create the `IStoreDocumentSearchService` port interface (`Task<IReadOnlyList<RetrievedChunk>> SearchAsync(RetrievalQuery, CancellationToken)`, plus a `RetrievedChunk` record: `ChunkId`, `DocumentTitle`, `DocumentType`, `SectionLabel`, `SourceLabel`, `Text`) in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/IStoreDocumentSearchService.cs` (depends on T010) — mirrors `IRecommendationService`'s existing role for `recommend` (research.md §7)
- [ ] T012 Extend `AdvisorTurnResult` with a `Citations: IReadOnlyList<Citation>?` field and a `ForStoreInfo(string message, IReadOnlyList<Citation> citations)` factory in `src/ProductAdvisor/ProductAdvisor.Application/AdvisorTurnResult.cs` (depends on T006) (data-model.md `TurnResult`)
- [ ] T013 [P] Configure Npgsql vector support (`UseVector()` on the data source builder passed to `AddNpgsqlDbContext<AdvisorDbContext>`) in `src/ProductAdvisor/ProductAdvisor.Api/Program.cs` (research.md §2)
- [ ] T014 Add an EF Core entity configuration for `Store` in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Configurations/StoreConfiguration.cs` (depends on T003)
- [ ] T015 Add an EF Core entity configuration for `StoreDocument` (owns `DocumentChunk` collection) in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Configurations/StoreDocumentConfiguration.cs` (depends on T004)
- [ ] T016 Add an EF Core entity configuration for `DocumentChunk` in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Configurations/DocumentChunkConfiguration.cs` — maps `Embedding` as `vector(1536)` with an HNSW (cosine distance) index, `SearchVector` as a Postgres-generated `tsvector` column with a GIN index, and a B-tree index on `(StoreId, Language, DocumentType)` (depends on T005, T013) (data-model.md `DocumentChunk`, research.md §2/§5/§12)
- [ ] T017 Register `Stores`, `StoreDocuments`, `DocumentChunks` `DbSet`s on `AdvisorDbContext` in `src/ProductAdvisor/ProductAdvisor.Infrastructure/AdvisorDbContext.cs` (depends on T014, T015, T016)
- [ ] T018 Generate the EF Core migration enabling the `pgvector` extension (`CREATE EXTENSION IF NOT EXISTS vector;`) and creating the new tables/indexes, in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Migrations/` (depends on T017) (research.md §2)
- [ ] T019 [P] Register `IEmbeddingGenerator<string, Embedding<float>>` (OpenAI `text-embedding-3-small` via `Microsoft.Extensions.AI.OpenAI`'s `EmbeddingClient.AsIEmbeddingGenerator()`, same provider config as the existing chat client) in `src/ProductAdvisor/ProductAdvisor.Api/Program.cs` (research.md §3)
- [ ] T020 [P] Implement `DocumentChunker` — paragraph/section-aware chunking with section-label inheritance, ~200–400 tokens per chunk — as a pure function in `src/ProductAdvisor/ProductAdvisor.Domain/DocumentChunker.cs` (research.md §4; placed in `Domain` alongside `ScoringPolicy`/`ComparisonEngine` as this project's established location for pure deterministic algorithms, 001 plan.md Project Structure)
- [ ] T021 [P] Implement Reciprocal Rank Fusion (`score = Σ 1/(k + rank_i)`, `k = 60`) as a pure function combining two ranked id lists, in `src/ProductAdvisor/ProductAdvisor.Domain/ReciprocalRankFusion.cs` (research.md §5)
- [ ] T022 [P] Domain unit tests for `DocumentChunker` (structural splitting on headings/paragraphs, section-label inheritance, hard-wrap fallback for an oversized paragraph) in `tests/ProductAdvisor.Domain.Tests/DocumentChunkerTests.cs` (depends on T020)
- [ ] T023 [P] Domain unit tests for `ReciprocalRankFusion` (known input rankings → known combined order) in `tests/ProductAdvisor.Domain.Tests/ReciprocalRankFusionTests.cs` (depends on T021)
- [ ] T024 [P] Create a `StoreInfoSeedData` test-fixture helper (mirrors `tests/TestSupport`'s existing `CatalogSeedData`/`PricingSeedData` pattern) that inserts `Store`/`StoreDocument`/`DocumentChunk` rows directly via `AdvisorDbContext` — bypassing the ingestion API, for deterministic test setup — in `tests/TestSupport/StoreInfoSeedData.cs` (depends on T017)

**Checkpoint**: Entities, routing extension, response-shape extension, DB schema (with `pgvector`),
embedding generator, and the pure chunking/ranking algorithms all exist. `Route.StoreInfo` is
selectable but not yet handled; `MentionsStorePolicy` is parsed but not yet acted on. User story
implementation can now begin.

---

## Phase 3: User Story 1 - Get a Grounded Answer to a Store-Policy Question (Priority: P1) 🎯 MVP

**Goal**: A `store_info`-routed question is answered only from retrieved, hybrid-searched store
document fragments, with a structured citation for every stated fact — or an honest "not enough
information" response when nothing sufficiently relevant is found.

**Independent Test**: Seed one store document (`StoreInfoSeedData`, T024), ask a question it
covers, confirm a `storeInfo` response whose `message` is consistent with the seeded content and
whose `citations` references that document; ask a question nothing covers, confirm `citations: []`
and the fixed FR-006 message (quickstart.md Scenarios 1–2).

### Tests for User Story 1

- [ ] T025 [P] [US1] Application test: `PolicyRouter.SelectRoute` selects `Route.StoreInfo` for `Intent.StoreInfo` (and `Route.Clarify` below the confidence threshold, matching every other intent's existing behavior) in `tests/ProductAdvisor.Application.Tests/StoreInfoRoutingTests.cs`
- [ ] T026 [P] [US1] Application test: `OutputValidationStage`'s citation-checking branch — a narration citing only allowed chunk ids passes unchanged; a narration citing an id outside the envelope's allowed set falls back to the deterministic FR-006 message; zero retrieved chunks short-circuits to that same fallback without an LLM call being made — in `tests/ProductAdvisor.Application.Tests/OutputValidationStageCitationTests.cs`
- [ ] T027 [P] [US1] Infrastructure integration test (Testcontainers, reusing `tests/TestSupport/PostgresFixture.cs`): `StoreDocumentSearchService` returns hybrid-ranked, store/language/[documentType]-filtered results against real seeded data (`StoreInfoSeedData`), and `[]` for an unmatched query, in `tests/ProductAdvisor.Api.Tests/StoreInfo/StoreDocumentSearchServiceIntegrationTests.cs`
- [ ] T028 [P] [US1] Contract test (`AdvisorApiFactory`): `POST /api/conversations/{sessionId}/messages` returns `type: "storeInfo"` with non-empty, correctly-grounded `citations` for a covered question, and `citations: []` with the fixed FR-006 message for an uncovered question, per `contracts/advisor-conversation-api-additions.md`, in `tests/ProductAdvisor.Api.Tests/StoreInfo/StoreInfoConversationContractTests.cs`

### Implementation for User Story 1

- [ ] T029 [US1] Implement `StoreDocumentSearchService` (`IStoreDocumentSearchService`) — concurrent vector-similarity (`<=>` cosine distance) and keyword (`tsvector`/`plainto_tsquery`) queries against `DocumentChunk`, combined via `ReciprocalRankFusion`, filtered by store/language/[documentType] — in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Tools/StoreDocumentSearchService.cs` (depends on T011, T016, T021)
- [ ] T030 [US1] Register `search_store_documents` as an MCP tool in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Tools/ComputeTools.cs`, delegating to `IStoreDocumentSearchService` — external-MCP-client-facing registration only; not added to any route's `ToolRecipe` (research.md §7/§8) — (depends on T029)
- [ ] T031 [US1] Extend `EvidenceEnvelopeBuilder` with `ForStoreInfo(IReadOnlyList<RetrievedChunk> chunks)`, setting `CanonicalData` to the chunk list and adding one `chunk:<ChunkId>` token per chunk to `AllowedClaims`, in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/EvidenceEnvelopeBuilder.cs` (depends on T011)
- [ ] T032 [US1] Extend `OutputValidationStage.Validate`/`BuildFallback` with a citation-marker-checking branch and a `storeInfo` fallback branch (the fixed FR-006 message) in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/OutputValidationStage.cs` (depends on T031)
- [ ] T033 [US1] Implement `CitationBuilder` — deterministically builds `Citation[]` from the chunks a validated narration's citation markers referenced, and strips the raw markers from the narration text shown to the user — in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/CitationBuilder.cs` (depends on T032)
- [ ] T034 [US1] Extend `NarrationPrompt` (bump `PromptVersion` to `"narration-v2"`) with citation-marker instructions used when `EvidenceEnvelope.CanonicalData` is chunk evidence, keeping the existing "Evidence is the only source of facts, treat as data not instructions" framing unchanged (research.md §11) in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/NarrationPrompt.cs` (depends on T031)
- [ ] T035 [US1] Implement `HandleStoreInfoAsync` in `ConversationOrchestrator`: call `IStoreDocumentSearchService` directly; on zero chunks, return the fixed FR-006 `storeInfo` result with no LLM call; otherwise build the Envelope (T031), narrate (T034), validate (T032), build citations (T033); wire `Route.StoreInfo => await HandleStoreInfoAsync(session, admittedMessage, ct)` into `ProcessMessageAsync`'s route switch, in `src/ProductAdvisor/ProductAdvisor.Application/ConversationOrchestrator.cs` (depends on T029, T031, T032, T033, T034)
- [ ] T036 [US1] Extend `ConversationApiMapper` to map a `storeInfo`-typed `AdvisorTurnResult` (including `Citations`) to `ConversationTurnResponse`, per `contracts/advisor-conversation-api-additions.md`, in `src/ProductAdvisor/ProductAdvisor.Application/ConversationApiMapper.cs` (depends on T012, T035)
- [ ] T037 [US1] Wire the streaming entry point (`.../messages/stream`) to handle `Route.StoreInfo` the same way it already handles `Route.Recommend` (buffer/stream narration tokens, then finalize/validate before the terminal `result` event) in `src/ProductAdvisor/ProductAdvisor.Application/ConversationOrchestrator.cs` (depends on T035)

**Checkpoint**: User Story 1 is fully functional and independently testable — quickstart.md
Scenarios 1 and 2 pass.

---

## Phase 4: User Story 2 - Store-Policy Questions Never Leak into Product Facts, and Vice Versa (Priority: P1)

**Goal**: A mixed message answers both its product-fact and store-policy parts correctly, each via
its own mechanism; store-document evidence never reaches `recommend`/`compare`/`checkout`, and
product-data tools are never the source of a store-policy claim.

**Independent Test**: Ask a pure product question (no `citations` field, no RAG evidence involved);
ask a pure store-policy question (no product tool invoked); ask one message combining both ("is the
Galaxy S24 in stock, and what's your return window") and confirm both parts are correctly answered
and grounded in one response (quickstart.md Scenario 3, spec.md US2).

### Tests for User Story 2

- [ ] T038 [P] [US2] Application test: `IStoreDocumentSearchService` is never invoked for `recommend`/`compare`/`checkout`/`smalltalk`/`unsupported` routes; product-data tools (`search_products`/`get_product_details`/`check_price_and_availability`) are never invoked for a pure `store_info` turn — in `tests/ProductAdvisor.Application.Tests/StoreInfoToolScopingTests.cs`
- [ ] T039 [P] [US2] Contract test: a mixed message yields `type: "answer"` with `fact` populated and non-empty, correctly-grounded `citations`; a pure product-fact message yields no `citations` field (byte-for-byte matching 001's existing `answer` contract tests); a pure store-policy message triggers no product-catalog/pricing tool call — in `tests/ProductAdvisor.Api.Tests/StoreInfo/MixedIntentBoundaryTests.cs`
- [ ] T040 [US2] Extend `tests/EndToEnd.Tests/Evals/CriticalEvals.cs` with two new `[Fact]` methods — (1) a store-info answer never states a policy fact without a citation, and never cites a document that was not actually retrieved; (2) a product price/stock/spec/comparison question is never answered via store-document retrieval, and a store-policy question is never answered via the product-data tools — added to the *existing* class (not a new file) so both run under the current `FullyQualifiedName~EndToEnd.Tests.Evals.CriticalEvals` CI filter with no `.github/workflows/ci.yml` change required (research.md §14)

### Implementation for User Story 2

- [ ] T041 [US2] Extend `ApplyGroundingIfApplicable` in `ConversationOrchestrator` to also build a citation-token `EvidenceEnvelope` (via T031) for a `product_fact` turn with `MentionsStorePolicy == true`, validating citations post-hoc the same way `comparison`/`checkoutLink` already are today, in `src/ProductAdvisor/ProductAdvisor.Application/ConversationOrchestrator.cs` (depends on T031, T035)
- [ ] T042 [US2] In `RunLegacyToolContinuationAsync`'s handling for `Route.ProductFact`, when `MentionsStorePolicy == true`, call `IStoreDocumentSearchService` directly (before building that turn's chat history) and inject the retrieved chunk text into the existing system prompt as additional grounded reference material, using the same untrusted-data framing NarrationPrompt already uses (research.md §11) — in `src/ProductAdvisor/ProductAdvisor.Application/ConversationOrchestrator.cs` (depends on T029, T041)
- [ ] T043 [US2] Extend `ConversationApiMapper`'s `answer`-type mapping so `Citations` is attached only when `MentionsStorePolicy == true` produced grounded citations that turn (absent/`null` for every other `answer`, including all `smalltalk` turns), per `contracts/advisor-conversation-api-additions.md`, in `src/ProductAdvisor/ProductAdvisor.Application/ConversationApiMapper.cs` (depends on T012, T042)

**Checkpoint**: User Stories 1 AND 2 both work independently — quickstart.md Scenario 3 passes; the
FR-007/FR-008 boundary is proven structurally (T038/T039) and behaviorally (T040).

---

## Phase 5: User Story 3 - Keep Store Reference Content Up to Date Without a Deployment (Priority: P2)

**Goal**: Adding, updating, or withdrawing a `StoreDocument` through the internal ingestion API is
reflected in subsequently retrieved answers immediately, with no redeploy.

**Independent Test**: `POST` a new/changed document, ask a question only it answers, confirm the
answer reflects it; `DELETE` a document, confirm it's no longer retrieved or cited (quickstart.md
Scenarios 4–5, spec.md US3).

### Tests for User Story 3

- [ ] T044 [P] [US3] Contract test: `POST /api/store-documents` create+update is reflected immediately in a subsequent `search_store_documents`/conversation query (old chunks fully replaced); `DELETE /api/store-documents/{id}` removes a document from all future retrieval; a missing/invalid `X-Internal-Api-Key` is rejected the same way every other Advisor endpoint already rejects one — per `contracts/advisor-mcp-tools-additions.md`, in `tests/ProductAdvisor.Api.Tests/StoreInfo/StoreDocumentIngestionApiTests.cs`

### Implementation for User Story 3

- [ ] T045 [US3] Implement `StoreDocumentIngestionService` — upsert (chunk via `DocumentChunker`, embed each chunk via `IEmbeddingGenerator`, replace all of a document's chunks in one transaction) and withdraw (`Status → Withdrawn`) — in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Ingestion/StoreDocumentIngestionService.cs` (depends on T017, T019, T020)
- [ ] T046 [US3] Add `POST /api/store-documents` and `DELETE /api/store-documents/{id}` endpoints (protected by the same `X-Internal-Api-Key` middleware as every other Advisor endpoint, never routed through `Gateway.Api`) in `src/ProductAdvisor/ProductAdvisor.Api/StoreDocuments/StoreDocumentEndpoints.cs`, registered from `src/ProductAdvisor/ProductAdvisor.Api/Program.cs` (depends on T045)

**Checkpoint**: All three user stories are independently functional — quickstart.md Scenarios 1–5
all pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T047 [P] Add a "Store info (RAG)" section to `README.md` (mirroring the existing "Agentic security and quality evals"/"Privacy & data protection" sections) — how to seed a document, the `storeInfo` response shape, and the FR-007 product-data/store-policy boundary
- [ ] T048 [P] Run `dotnet format --verify-no-changes` and the project's analyzers across every new/modified file in this feature; fix any warnings
- [ ] T049 Run `quickstart.md` end-to-end against a live local stack (Aspire or `docker compose up --build`) and confirm all 5 scenarios pass, mirroring how `001-smart-product-advisor`'s Phase 14 work was smoke-tested live before being considered done
- [ ] T050 Update this file's task checkboxes to `[X]` and record any deliberate scope reductions found during build-out, matching this project's established `tasks.md` documentation practice

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup (T001, for T016's `vector` column type) — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Foundational (Phase 2) completion.
  - US1 and US2 are both P1; US2's tests/implementation build on US1's `HandleStoreInfoAsync`/`EvidenceEnvelopeBuilder.ForStoreInfo`/`OutputValidationStage` citation machinery (T031–T035), so US2 (Phase 4) is sequenced after US1 (Phase 3) despite sharing a priority tier.
  - US3 (Phase 5) only needs `DocumentChunker` (Foundational) and can, in principle, be built in parallel with US1/US2 by a second developer — it does not depend on either's implementation tasks, only on Foundational.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Within Each User Story

- Tests are written first and MUST fail before their corresponding implementation task.
- Domain/port interfaces before services; services before orchestrator wiring; orchestrator wiring before API-shape mapping.

### Parallel Opportunities

- All Setup tasks marked `[P]` (just T001 here).
- Within Foundational: T003/T006/T010/T011/T013/T019/T020/T021/T022/T023/T024 are `[P]` against each other where file/dependency-disjoint (see each task's own `depends on`).
- Once Foundational completes, **US3 (Phase 5) can proceed in parallel with US1/US2** (Phase 3/4) — it shares no implementation files with either.
- All `[P]`-marked tests within a story phase can run in parallel.

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Application test: PolicyRouter routes Intent.StoreInfo to Route.StoreInfo in tests/ProductAdvisor.Application.Tests/StoreInfoRoutingTests.cs"
Task: "Application test: OutputValidationStage citation-checking branch in tests/ProductAdvisor.Application.Tests/OutputValidationStageCitationTests.cs"
Task: "Integration test: StoreDocumentSearchService hybrid search in tests/ProductAdvisor.Api.Tests/StoreInfo/StoreDocumentSearchServiceIntegrationTests.cs"
Task: "Contract test: storeInfo turn shape in tests/ProductAdvisor.Api.Tests/StoreInfo/StoreInfoConversationContractTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenarios 1–2 independently.
5. This alone is a demoable MVP: grounded, cited store-policy answers, honest when uncovered —
   the feature's entire stated value proposition — without yet proving the product-fact boundary
   (US2) or live content updates (US3).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add US1 → validate independently → MVP demoable.
3. Add US2 → validate independently (quickstart.md Scenario 3) → the FR-007/FR-008 boundary is now proven, both structurally and via the eval suite.
4. Add US3 → validate independently (quickstart.md Scenarios 4–5) → the knowledge base is now maintainable without a redeploy.
5. Polish.

### Parallel Team Strategy

With two developers: both complete Setup + Foundational together; then Developer A takes US1 → US2
(sequential, since US2 depends on US1's citation machinery) while Developer B takes US3 in
parallel (independent of US1/US2's implementation files); both converge on Polish.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency.
- `[Story]` labels map every user-story-phase task to spec.md's US1/US2/US3 for traceability.
- Commit after each task or logical group, consistent with this project's established practice.
- `search_store_documents` is registered as an MCP tool (T030) for external MCP-client
  compatibility with the server's general catalog contract, but the conversation orchestrator's
  own flow never places it in a turn's `chatOptions.Tools` — retrieval is always a direct
  `IStoreDocumentSearchService` call (T029, T035, T042), mirroring how `get_recommendations` is
  already called directly through `IRecommendationService` rather than left to the model's tool
  selection (research.md §7/§8). Do not "fix" this by adding it to `ToolRecipe` — that would
  reintroduce exactly the model-tool-selection risk this design deliberately avoids.
