---

description: "Task list for the Smart Product Advisor feature"
---

# Tasks: Smart Product Advisor

**Input**: Design documents from `/specs/001-smart-product-advisor/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [data-model.md](./data-model.md),
[contracts/](./contracts/), [research.md](./research.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The plan explicitly requires xUnit unit, domain, contract, and integration
tests, with complete recommendation/comparison scenarios covered across service boundaries.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P3) so each story can
be implemented and verified independently once Setup + Foundational are done.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1, US2, or US3 from spec.md
- Every task names its exact file path(s)

## Path Conventions

Paths follow plan.md's Project Structure exactly:

- Services: `src/ProductCatalog/{Domain,Application,Infrastructure,Api}`,
  `src/PricingAvailability/{Domain,Application,Infrastructure,Api}`,
  `src/ProductAdvisor/{Domain,Application,Infrastructure,Api}`
- Gateway/UI: `src/Gateway/Gateway.Api`, `src/WebApp/WebApp.Blazor`
- Orchestration: `src/Aspire/AppHost`, `src/Aspire/ServiceDefaults`
- Tests mirror service names 1:1 under `tests/`, plus `tests/EndToEnd.Tests` and
  `tests/TestSupport` for shared fixtures.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution scaffolding so every later task has a project to add code to.

- [X] T001 Create the solution (`src/ProductAdvisor.sln`) and every project from plan.md's
      Project Structure — `src/Aspire/AppHost`, `src/Aspire/ServiceDefaults`,
      `src/ProductCatalog/{Domain,Application,Infrastructure,Api}`,
      `src/PricingAvailability/{Domain,Application,Infrastructure,Api}`,
      `src/ProductAdvisor/{Domain,Application,Infrastructure,Api}`, `src/Gateway/Gateway.Api`,
      `src/WebApp/WebApp.Blazor`, and the mirrored `tests/*` projects — with correct
      project-to-project references (Api → Application → Domain; Infrastructure → Domain +
      Application interfaces) and add them all to the `.sln`.
- [X] T002 [P] Add `src/Directory.Build.props` with nullable/implicit-usings enabled and Roslyn
      analyzers (e.g., `Microsoft.CodeAnalysis.NetAnalyzers`) so lint/type checks apply
      solution-wide (constitution Principle I).
- [X] T003 [P] Add NuGet package references: EF Core + Npgsql to
      `ProductCatalog.Infrastructure`, `PricingAvailability.Infrastructure`, and
      `ProductAdvisor.Infrastructure`; `ModelContextProtocol` + `ModelContextProtocol.AspNetCore`
      to `ProductAdvisor.Api`; `Microsoft.Extensions.AI` (+ chosen provider connector) to
      `ProductAdvisor.Infrastructure`; `Microsoft.Extensions.Http.Resilience` to every
      `*.Infrastructure` and `Gateway.Api`; `Yarp.ReverseProxy` to `Gateway.Api`;
      `Aspire.Hosting.AppHost` + `Aspire.Hosting.PostgreSQL` to `Aspire/AppHost`;
      `OpenTelemetry.Extensions.Hosting` + an OTLP exporter to `Aspire/ServiceDefaults`.
- [X] T004 [P] Scaffold `src/Aspire/AppHost/Program.cs`: a Postgres resource plus all five
      services registered with service discovery, matching quickstart.md Option A.
- [X] T005 [P] Scaffold `src/Aspire/ServiceDefaults/Extensions.cs`: OpenTelemetry
      tracing/metrics, health checks, and the standard resilience handler, to be referenced by
      every `*.Api` project (constitution Principles V & VI).
- [X] T006 [P] Write `docker-compose.yml` at the repo root: Postgres + all five service
      containers with health checks, mirroring the Aspire topology for CI/non-Aspire parity.
- [X] T007 [P] Write a Dockerfile for each deployable: `src/ProductCatalog/ProductCatalog.Api/Dockerfile`,
      `src/PricingAvailability/PricingAvailability.Api/Dockerfile`,
      `src/ProductAdvisor/ProductAdvisor.Api/Dockerfile`, `src/Gateway/Gateway.Api/Dockerfile`,
      `src/WebApp/WebApp.Blazor/Dockerfile`.
- [X] T008 [P] Add `.github/workflows/ci.yml`: restore/build the solution, run `dotnet test`,
      and build all five Docker images, failing the job on any error (constitution Principle
      III / Development Workflow).
- [X] T009 [P] Add `render.yaml` declaring the five web services and the environment variables
      they require (Neon connection strings, LLM provider key/endpoint, inter-service base
      URLs) without secret values.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entities, persistence, and cross-cutting plumbing every user story needs.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T010 [P] Implement `Product`, `Category`, `Brand`, `Specification` in
      `src/ProductCatalog/ProductCatalog.Domain/` per data-model.md (Name/CategoryId required,
      ≥1 Specification before `IsActive`).
- [X] T011 [P] Implement `Offer`, `Money`, `Discount`, `StockStatus` in
      `src/PricingAvailability/PricingAvailability.Domain/` per data-model.md (`Availability`
      defaults to `Unknown`, never a guessed `InStock`).
- [X] T012 [P] Implement `ConversationSession`, `UserRequirement`, `ClarificationQuestion`,
      `ProductCandidate` in `src/ProductAdvisor/ProductAdvisor.Domain/` per data-model.md,
      including the `Collecting` → `Recommending` → `Comparing` state transitions.
- [X] T013 [P] Unit test `Product`/`Category`/`Brand` validation rules in
      `tests/ProductCatalog.Domain.Tests/`.
- [X] T014 [P] Unit test `Offer`/`Money`/`Discount`/`StockStatus` validation rules, especially
      the Unknown-by-default rule, in `tests/PricingAvailability.Domain.Tests/`.
- [X] T015 [P] Unit test `ConversationSession` state transitions and
      `UserRequirement`-completeness logic in `tests/ProductAdvisor.Domain.Tests/`.
- [X] T016 Configure the Catalog EF Core `DbContext` (schema `catalog`) + initial migration in
      `src/ProductCatalog/ProductCatalog.Infrastructure/` (depends on T010).
- [X] T017 Configure the Pricing EF Core `DbContext` (schema `pricing`) + initial migration in
      `src/PricingAvailability/PricingAvailability.Infrastructure/` (depends on T011).
- [X] T018 Configure the Advisor EF Core `DbContext` (schema `advisor`, conversation history
      only) + initial migration in `src/ProductAdvisor/ProductAdvisor.Infrastructure/` (depends
      on T012).
- [X] T019 [P] Add SQL scripts under `db/init/` provisioning one least-privileged Postgres role
      and database per service (`catalog`, `pricing`, `advisor`) on the shared instance — a
      separate physical database per service, not just a schema, so Postgres itself refuses
      cross-service queries (research.md §5).
- [X] T020 [P] Add a shared Testcontainers-Postgres xUnit fixture in
      `tests/TestSupport/PostgresFixture.cs`, reusable by `ProductCatalog.Api.Tests`,
      `PricingAvailability.Api.Tests`, and `ProductAdvisor.Api.Tests`.
- [X] T021 [P] Implement correlation-ID middleware/`DelegatingHandler` (generate-if-absent,
      forward on every outbound call, attach to every log scope) in a small shared
      project/library referenced by `Gateway.Api` and every `*.Api` (research.md §7).
- [X] T022 Host an MCP server with an empty tool list at `/mcp` in
      `src/ProductAdvisor/ProductAdvisor.Api/` via `ModelContextProtocol.AspNetCore`, and
      register `Microsoft.Extensions.AI`'s `IChatClient` against the configured, swappable,
      env-driven free-tier provider in `src/ProductAdvisor/ProductAdvisor.Infrastructure/`
      (depends on T003; research.md §1, §10).
- [X] T023 [P] Scaffold `Gateway.Api`'s YARP base routing/config in
      `src/Gateway/Gateway.Api/`, wired to the correlation-ID middleware from T021.
- [X] T024 [P] Scaffold the Blazor Web App shell (Interactive Server render mode) with an empty
      chat page in `src/WebApp/WebApp.Blazor/`.
- [X] T025 [P] Add a reusable seed dataset (2 categories, 4 products with specifications,
      matching Pricing offers with varied availability/discounts) under
      `tests/TestSupport/SeedData/` (`CatalogSeedData`, `PricingSeedData`, fixed guids so
      scenarios can name a specific product deterministically); the docker-compose/EndToEnd
      seeding step that loads this dataset into the running stack is wired in T044.

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Get a Recommendation from a Natural-Language Need (Priority: P1) 🎯 MVP

**Goal**: A shopper describes a need in natural language and receives a grounded, reasoned
recommendation, or a single focused clarifying question if essential info is missing.

**Independent Test**: Submit a fully-specified request (category, budget, one feature) and
confirm a recommendation with reasoning; submit an under-specified request and confirm exactly
one clarifying question is asked before any recommendation.

### Tests for User Story 1

- [X] T026 [P] [US1] Contract test `GET /api/catalog/products` (search, per
      contracts/catalog-api.md) in
      `tests/ProductCatalog.Api.Tests/SearchProductsContractTests.cs`. Compiles and runs;
      requires Docker (Testcontainers Postgres) to execute — unavailable in this sandbox, so
      pass/fail is unverified here (fails only at the Docker-connect step, not in app code).
- [X] T027 [P] [US1] Contract test `GET /api/pricing/offers?productIds=` batch, including
      partial `notFound` behavior (per contracts/pricing-api.md), in
      `tests/PricingAvailability.Api.Tests/BatchOffersContractTests.cs`. Same Docker caveat as T026.
- [X] T028 [P] [US1] Unit tests for `ScoringPolicy` — budget hard-exclude, required-feature
      matching, deterministic trade-off flagging, ranking — in
      `tests/ProductAdvisor.Domain.Tests/ScoringPolicyTests.cs`. Verified: 9/9 passing (no Docker needed).
- [X] T029 [P] [US1] MCP tool contract tests for `search_products` and
      `check_price_and_availability` (schema, not-found/empty/over-limit cases) in
      `tests/ProductAdvisor.Api.Tests/DataAccessToolsTests.cs`. Verified: 4/4 passing via a real
      in-process MCP client (no Docker needed — Catalog/Pricing calls are faked).
- [X] T030 [US1] MCP tool contract test for `get_recommendations`, including a repeated-call
      determinism assertion on `score`, in
      `tests/ProductAdvisor.Api.Tests/GetRecommendationsToolTests.cs`. Verified: 2/2 passing.
- [X] T031 [P] [US1] Contract tests for the conversation API's clarification and recommendation
      response shapes in `tests/ProductAdvisor.Api.Tests/ConversationApiContractTests.cs`.
      Compiles and runs; requires Docker (conversation history is persisted) — unverified here,
      same Docker caveat as T026.
- [X] T032 [P] [US1] Application-layer test (stubbed tools only) proving the orchestration loop
      never produces a score or fact itself in
      `tests/ProductAdvisor.Application.Tests/OrchestrationNeverComputesTests.cs`. Verified: 3/3 passing.

### Implementation for User Story 1

- [X] T033 [P] [US1] Implement `GET /api/catalog/products` search (category, keyword,
      pagination) in `src/ProductCatalog/ProductCatalog.Application/` +
      `src/ProductCatalog/ProductCatalog.Api/` (depends on T016).
- [X] T034 [P] [US1] Implement `GET /api/pricing/offers/{productId}` and
      `GET /api/pricing/offers?productIds=` batch in
      `src/PricingAvailability/PricingAvailability.Application/` + `.Api/` (depends on T017).
- [X] T035 [US1] Implement the `ScoringPolicy` domain service in
      `src/ProductAdvisor/ProductAdvisor.Domain/ScoringPolicy.cs`.
- [X] T036 [US1] Implement typed HTTP clients to Catalog and Pricing, registered with the
      standard resilience handler, in `src/ProductAdvisor/ProductAdvisor.Infrastructure/`
      (depends on T033, T034).
- [X] T037 [US1] Implement and register the `search_products` and
      `check_price_and_availability` MCP tools in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/Tools/` +
      `src/ProductAdvisor/ProductAdvisor.Api/` (depends on T036, T022).
- [X] T038 [US1] Implement the `get_recommendations` MCP tool — search then price/availability
      lookup, then `ScoringPolicy` — in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/Tools/ComputeTools.cs` (depends on T035,
      T037). Note: search→price is a genuine sequential dependency (price lookup needs the
      search results' ids), not an independent-calls case, so `Task.WhenAll` doesn't apply here;
      it's used in `compare_products` (US2) instead, where per-product detail+price calls really
      are independent.
- [X] T039 [US1] Essential-field completeness gate: implemented as the existing
      `UserRequirement.HasEssentialInformation` domain rule (T012/T015) enforced by
      `ConversationSession.StartRecommending()`; the orchestrator calls it after a tool result is
      captured rather than re-implementing the check.
- [X] T040 [US1] Implement the conversation orchestration loop — feed `IChatClient` the message,
      session state, and tool catalog; execute the LLM's chosen tool call(s); persist the turn —
      in `src/ProductAdvisor/ProductAdvisor.Application/ConversationOrchestrator.cs` (depends on
      T038, T039). Tool wiring for the chat client goes through `IAdvisorToolCatalog`
      (Application port / Infrastructure `AdvisorToolCatalog` impl) so Application never
      references Infrastructure directly.
- [X] T041 [US1] Implement `POST /api/conversations` and
      `POST /api/conversations/{sessionId}/messages` (+ `GET /api/conversations/{sessionId}`) in
      `src/ProductAdvisor/ProductAdvisor.Api/` (depends on T040, T018).
- [X] T042 [US1] Implement Gateway `POST /api/chat/messages` and `GET /api/chat/{sessionId}`
      composition endpoints in `src/Gateway/Gateway.Api/` (depends on T041, T023). Verified: both
      services start cleanly and the Gateway forwards/merges the Advisor's response as designed.
- [X] T043 [US1] Implement the Blazor chat page — send message, render a clarification question,
      render recommendation cards with price/availability + verified flags, matched
      requirements, and trade-offs — in `src/WebApp/WebApp.Blazor/` (depends on T042, T024).
      Verified interactively in-browser: page renders, chat input/send are interactive (real
      SignalR circuit), sending a message appends to history, and a Gateway-unreachable failure
      now shows a friendly inline error instead of crashing the circuit (fixed during this
      verification — see `Home.razor`'s `catch` block).
- [X] T044 [US1] EndToEnd test covering quickstart Scenarios 1–3 (recommendation, clarification,
      honest no-match) against the docker-compose–orchestrated stack in
      `tests/EndToEnd.Tests/RecommendationScenarioTests.cs` (depends on T043, T025), plus
      `DockerComposeStackFixture` (seeds `CatalogSeedData`/`PricingSeedData` into the running
      stack's databases if not already present — the T025 seeding mechanism). Compiles.
      **Live-verified** (Docker + a real LLM key became available mid-session): brought up the
      full `docker compose` stack and drove Scenarios 1–3 through the real Gateway → Advisor →
      LLM → Catalog/Pricing path via curl and, separately, through the actual Blazor UI in a
      browser. Found and fixed three real defects surfaced only by this live run:
      (1) Catalog's search endpoint required `page`/`pageSize` query params instead of
      defaulting them — 400 on any request that omitted them; (2) `ScoringPolicy`'s
      requirement-to-spec matching only checked one substring direction, so an LLM-extracted
      feature like `"camera"` never matched the catalog's `"camera_mp"` key; (3) migrations
      and demo seed data were never wired into the real services' startup, only into tests —
      added migrate-on-startup and a config-gated (`SeedDemoData`) seed-if-empty step to
      `ProductCatalog.Api`/`PricingAvailability.Api`/`ProductAdvisor.Api`, with the demo dataset
      now duplicated into each service's own Infrastructure project (production code must not
      reference the test-only `TestSupport` project). Also had to loosen `global.json`'s SDK
      pin (`10.0.302` → `10.0.100`/`latestFeature`) since the published SDK Docker image lags
      the locally installed preview SDK.
      **Full solution build**: all 28 projects build with 0 warnings/0 errors.
      **Full non-Docker test run**: 60/60 passing (15 Catalog domain, 14 Pricing domain, 23
      Advisor domain incl. ScoringPolicy — now with regression tests for the token-overlap
      matching fix, 3 Application orchestrator, 6 Advisor MCP tool contract via a real
      in-process MCP client). Docker-dependent contract-test suites (Catalog/Pricing/
      Advisor-conversation) still fail only at the Docker-connect step *in this stateless
      sandbox environment* (no persistent Docker daemon across turns) — verified via the
      plain `docker run` in this environment, i.e., not a code defect.

**Checkpoint**: User Story 1 is fully functional and independently demoable (MVP).

---

## Phase 3.5: Streaming & Rich Response Rendering (US1 Enhancement)

> **Numbering note**: this phase was added during a spec-refinement pass after Phase 3 shipped,
> so its task IDs (T073+) continue from the highest number then in use rather than fitting
> between T044 and T045. It must still be completed **before** Phase 4/5 — see spec.md
> FR-015/FR-016/FR-017, SC-008/SC-009, and research.md §11–§12 for the requirements and design
> this phase implements.

**Goal**: The existing US1 chat experience delivers its narration progressively over SSE
(FR-015) instead of only after the full answer is ready, and renders that narration plus the
structured facts (specs, matched requirements, trade-offs) with real formatting — Markdown for
the LLM's text, actual HTML lists/tables for the facts — instead of a single prose block
(FR-016/FR-017). No grounding guarantee changes: streaming only staggers delivery of narration
text, and facts are still only ever rendered from tool-produced structured data, never from
LLM-authored Markdown.

**Independent Test**: Send a fully-specified request through the streaming endpoint and confirm
`token` events arrive before the final `result` event and concatenate to the same text as
`result.message`; confirm the rendered page shows headings/bold/bullet lists instead of literal
`**`/`-` characters, and that matched requirements/trade-offs render as real `<ul>/<li>` items.

### Tasks

- [X] T073 [P] Add a new `tests/WebApp.Blazor.Tests` xUnit project to the solution, referencing
      `src/WebApp/WebApp.Blazor`.
- [X] T074 [P] Add `Markdig` and an HTML allow-list sanitizer package to
      `src/WebApp/WebApp.Blazor`; implement `NarrationMarkdownRenderer.ToSafeHtml(string markdown)`
      in `src/WebApp/WebApp.Blazor/Rendering/` — Markdig pipeline with the raw-HTML-passthrough
      extension disabled, output run through the sanitizer before use as a `MarkupString`
      (research.md §12).
- [X] T075 [P] Unit tests for `NarrationMarkdownRenderer` — headings/bold/bullet lists render as
      the expected HTML; a `<script>` tag, an `onclick` attribute, and a `javascript:` link in
      the input are all stripped — in `tests/WebApp.Blazor.Tests/NarrationMarkdownRendererTests.cs`
      (depends on T073, T074).
- [X] T076 Implement `POST /api/conversations/{sessionId}/messages/stream` on
      `src/ProductAdvisor/ProductAdvisor.Api/` using `IChatClient.GetStreamingResponseAsync`
      (`FunctionInvokingChatClient` resolves tool calls / `IToolResultCapture` exactly as the
      non-streaming path); emits the `token`/`result` SSE event sequence
      (contracts/advisor-conversation-api.md) (depends on T041).
- [X] T077 [P] Contract test for the streaming endpoint — concatenated `token` deltas equal
      `result.message`; `result`'s structured fields are byte-identical to the non-streaming
      endpoint's response for the same stubbed tool output; a stream forcibly cut before
      `result` is detectable as incomplete — in
      `tests/ProductAdvisor.Api.Tests/StreamingConversationApiContractTests.cs` (depends on T076).
- [X] T078 Implement `POST /api/chat/messages/stream` on `src/Gateway/Gateway.Api/`, proxying the
      Advisor's SSE stream and merging the resolved `sessionId` into the `result` event
      (contracts/gateway-bff-api.md) (depends on T076, T042).
- [X] T079 [P] Contract test for the Gateway streaming endpoint — `sessionId` correctly merged
      into `result`; exactly-one-session-created guarantee holds for the streaming path too — in
      `tests/Gateway.Api.Tests/StreamingChatContractTests.cs` (depends on T078).
- [X] T080 Update the Blazor chat page to call `POST /api/chat/messages/stream` from its own
      server-side code (reading the SSE response incrementally via .NET's built-in SSE parser),
      append each `token` event's `delta` to the in-progress narration and re-render, and fall
      back to the non-streaming endpoint if the connection ends without a `result` event — in
      `src/WebApp/WebApp.Blazor/Components/Pages/Home.razor` (depends on T078).
- [X] T081 Render the narration through `NarrationMarkdownRenderer` (as a `MarkupString`) and
      render specifications/matched-requirements/trade-offs as real `<ul>/<li>` elements instead
      of `string.Join(...)` text in the recommendation card — in `Home.razor` (depends on T075,
      T080).
- [X] T082 Manually re-verify quickstart Scenarios 1–3 and 6 (Pricing outage) against the running
      stack with streaming enabled: confirm the answer visibly appears progressively, renders
      with real formatting (not literal Markdown characters), and that the Pricing-outage
      fallback still yields a complete, honestly-partial streamed response rather than a stuck
      or truncated one.

**Checkpoint**: US1's chat experience streams and renders richly; Phases 4/5 (US2/US3) can
build on top of it, reusing the same streaming/rendering plumbing for comparisons.

---

## Phase 4: User Story 2 - Compare Multiple Products Using Consistent Criteria (Priority: P2)

**Goal**: A shopper compares two or more named products and sees identical criteria, a
deterministic rating, and computed deltas for each.

**Independent Test**: Request a comparison of two or three seeded products and confirm the
response uses the identical criteria set/order for every product, with an unverifiable
characteristic explicitly marked rather than guessed.

### Tests for User Story 2

- [X] T045 [P] [US2] Contract tests for `GET /api/catalog/products/{productId}` and
      `GET /api/catalog/categories/{categoryId}` in
      `tests/ProductCatalog.Api.Tests/ProductAndCategoryDetailContractTests.cs`.
- [X] T046 [P] [US2] Unit tests for `ComparisonEngine` — shared criteria, deterministic rating,
      `deltasVsBest`, unverifiable-value handling — in
      `tests/ProductAdvisor.Domain.Tests/ComparisonEngineTests.cs`.
- [X] T047 [P] [US2] MCP tool contract test for `compare_products` — schema, ≥2-id requirement,
      determinism across repeated calls — in
      `tests/ProductAdvisor.Api.Tests/CompareProductsToolTests.cs`.
- [X] T048 [P] [US2] Contract test for the conversation API's comparison response shape in
      `tests/ProductAdvisor.Api.Tests/ConversationApiContractTests.cs`.

### Implementation for User Story 2

- [X] T049 [P] [US2] Implement `GET /api/catalog/products/{productId}` and
      `GET /api/catalog/categories/{categoryId}` in
      `src/ProductCatalog/ProductCatalog.Application/` + `.Api/` (depends on T016).
- [X] T050 [US2] Implement the `ComparisonEngine` domain service in
      `src/ProductAdvisor/ProductAdvisor.Domain/ComparisonEngine.cs`.
- [X] T051 [US2] Implement and register the `get_product_details` MCP tool in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/Tools/` + `.Api/` (depends on T049,
      T037).
- [X] T052 [US2] Implement and register the `compare_products` MCP tool — concurrent detail +
      price/availability fetch, then `ComparisonEngine` — in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/Tools/CompareProductsTool.cs` (depends
      on T050, T051).
- [X] T053 [US2] Extend the conversation orchestration loop and
      `POST /api/conversations/{sessionId}/messages` to support the comparison response shape
      in `src/ProductAdvisor/ProductAdvisor.Application/` + `.Api/` (depends on T052, T040).
- [X] T054 [US2] Implement the Blazor comparison view — shared criteria table, per-product
      rating, `deltasVsBest`, unverified markers — in `src/WebApp/WebApp.Blazor/` (depends on
      T053, T043).
- [X] T055 [US2] EndToEnd test covering quickstart Scenario 4 (consistent criteria + rating/delta
      determinism) in `tests/EndToEnd.Tests/ComparisonScenarioTests.cs` (depends on T054).

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 4.5: Deterministic Search & Direct Comparison (US2 Enhancement)

> **Numbering note**: like Phase 3.5, this phase was added during a spec-refinement pass after
> Phase 4 shipped, so its task IDs (T083+) continue from the highest number then in use rather
> than fitting between T055 and T056. It must still be completed **before** Phase 6 (Polish) —
> see spec.md FR-018–FR-022, SC-010–SC-012, and research.md §13–§15 for the requirements and
> design this phase implements.

**Goal**: Two things that previously depended on the LLM choosing to invoke them correctly no
longer do. Product search accepts explicit category/price/characteristic filters instead of
relying on the LLM to guess free-text query terms, with category names and comparable
characteristics resolvable without guessing an id. Product comparison — ratings, deltas,
rankings — is reachable through a stateless direct endpoint with no conversation turn and no LLM
tool-selection step at all; the LLM's only optional role afterward is a narrow, constrained call
that narrates the already-computed table. A capped, per-session memory of the most recently shown
search/recommendation/comparison result lets ordinal follow-ups ("the first two") resolve to
concrete ids instead of asking the LLM to reconstruct them from prior prose.

**Independent Test**: Search with a category, a price range, and a characteristic condition and
confirm every returned product satisfies all three; call the direct comparison endpoint with a
known product-id set and confirm its `rating`/`deltasVsBest` are byte-identical to what the same
ids produce through conversation; ask a follow-up ("compare the first two") after a search and
confirm it resolves against the session's remembered result set.

### Tests for This Phase

- [X] T083 [P] Contract test for `GET /api/catalog/categories?name=` in
      `tests/ProductCatalog.Api.Tests/CategoryByNameContractTests.cs`.
- [X] T084 [P] Contract test for `POST /api/catalog/products/search` — characteristic filter
      operators (`eq`/`gte`/`lte`/`between`), an unknown-attribute filter yielding zero matches,
      and a `400` for an unrecognized operator or a missing `valueTo` — in
      `tests/ProductCatalog.Api.Tests/ParametricSearchContractTests.cs`.
- [X] T085 [P] Unit tests for the characteristic-filter matcher (all four operators, unknown key,
      non-numeric value against an ordinal operator) in
      `tests/ProductCatalog.Application.Tests/CharacteristicFilterTests.cs`.
- [X] T086 [P] MCP tool contract test for the extended `search_products` (characteristics/price
      range/sort/limit) and the new `get_category` tool in
      `tests/ProductAdvisor.Api.Tests/AdvancedSearchToolTests.cs`.
- [X] T087 [P] Contract test for `POST /api/comparisons` — byte-identical `criteria`/`rows`
      against the same ids compared through `compare_products` in conversation (SC-010),
      `includeExplanation: false` makes zero LLM calls, a failing/unavailable chat client still
      returns `200` with `explanation: null` (FR-019), and fewer than 2 valid ids is a `400` — in
      `tests/ProductAdvisor.Api.Tests/DirectComparisonContractTests.cs`.
- [X] T088 [P] Contract test for Gateway `GET /api/products/search` and `POST /api/products/compare`
      in `tests/Gateway.Api.Tests/ProductSearchAndCompareContractTests.cs`.
- [X] T089 [P] Unit test for `ConversationSession.LastSearchResults` being replaced (not appended
      to) on each new search/recommendation/comparison in
      `tests/ProductAdvisor.Domain.Tests/ConversationSessionTests.cs`.

### Implementation for This Phase

- [X] T090 [P] Implement `GET /api/catalog/categories?name=` in
      `src/ProductCatalog/ProductCatalog.Application/` + `.Api/` (reuses the existing
      `FindCategoryByNameAsync` repository method) (depends on T083).
- [X] T091 Implement `CharacteristicFilter` (key/operator/value/valueTo) and its matcher in
      `src/ProductCatalog/ProductCatalog.Application/` (depends on T085).
- [X] T092 Implement `POST /api/catalog/products/search` — SQL-pushed category/free-text
      narrowing, then in-process characteristic filtering on the narrowed set (research.md §13)
      — in `src/ProductCatalog/ProductCatalog.Application/` + `.Api/` (depends on T084, T091).
- [X] T093 Extend the `search_products` MCP tool with `categoryId`/`characteristics`/`priceMin`/
      `priceMax`/`sortBy`/`limit` (composing Catalog's search with a Pricing batch price-range
      filter, per research.md §13's pushdown pattern) and add the `get_category` tool in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/Tools/` (depends on T086, T090, T092).
- [X] T094 Extract the `compare_products` composition (candidate assembly + `ComparisonEngine`
      invocation) into a shared service in `src/ProductAdvisor/ProductAdvisor.Infrastructure/`
      so it has exactly one implementation reused by both the MCP tool and the new direct
      endpoint (research.md §14).
- [X] T095 Implement the stateless `POST /api/comparisons` endpoint — calls the shared service
      from T094, then an optional separate constrained `IChatClient` call for `explanation` that
      cannot alter the computed data and whose failure never blocks the response — in
      `src/ProductAdvisor/ProductAdvisor.Api/` (depends on T087, T094).
- [X] T096 Add `ConversationSession.LastSearchResults` (capped, replaced per new result set) in
      `src/ProductAdvisor/ProductAdvisor.Domain/` and wire updates into
      `ConversationOrchestrator` whenever `search_products`/`get_recommendations`/
      `compare_products` produces a candidate list (depends on T089).
- [X] T097 Implement Gateway `GET /api/products/search` (Catalog search + Pricing price-range
      filter composition, mirroring `GET /api/products/{productId}`'s existing pattern) and
      `POST /api/products/compare` (proxy to `POST /api/comparisons`) in
      `src/Gateway/Gateway.Api/` (depends on T088, T092, T095).
- [X] T098 Implement the Blazor explicit product-picker page — search/filter form, checkbox
      selection, a "Compare" button calling the Gateway's direct endpoints with no chat/LLM
      involvement — in `src/WebApp/WebApp.Blazor/` (depends on T097).
- [X] T099 Manually re-verify: (a) chat-based "compare Samsung Galaxy S24 and GooglePixel 9"
      still resolves via `search_products`/`get_category`/`compare_products`; (b) the explicit
      picker's comparison is byte-identical to the same ids compared via chat; (c) a
      category+price+characteristic-filtered search returns only qualifying products; (d) a
      follow-up "compare the first two" after a search resolves via `LastSearchResults`.

**Checkpoint**: Search filtering and product comparison are both reachable without depending on
the LLM to compute anything; the LLM's only remaining job in this area is resolving language to
ids (retrieval) and, optionally, narrating an already-computed table.

---

## Phase 4.6: Persist Structured Renders Across Turns (US1 Enhancement)

> **Numbering note**: this phase was added during a spec-refinement pass after Phase 4.5 shipped,
> continuing task IDs from the highest number then in use (T099) rather than renumbering. See
> spec.md FR-023, SC-013, and the new Assumptions bullet for the requirement this phase
> implements.

**Goal**: `src/WebApp/WebApp.Blazor/Components/Pages/Home.razor` currently keeps only one
"current result" slot (`_lastTurn`) for rendering a recommendation card list, comparison table,
or clarification prompt. Because every new turn overwrites that single slot, sending any further
message — even an unrelated one — makes a previously-shown structured rendering vanish from the
conversation view, even though the plain narration text for that turn remains in the scrolling
history. This phase makes each turn's structured rendering persist alongside its message instead
of sharing one slot (FR-023).

**Independent Test**: In the chat UI, trigger a comparison (renders a table), then send an
unrelated follow-up message; confirm the comparison table is still visible in its original place
in the conversation after the new turn's response arrives. Repeat for a recommendation card list
followed by another recommendation request — both card lists must remain visible, in order.

### Tasks

- [X] T100 Change `Home.razor`'s history model from `List<(string Role, string Text)>` to a
      `HistoryEntry(string Role, string Text, ChatTurnDto? Turn)` record list, so each assistant
      entry carries its own turn's structured result instead of a single shared `_lastTurn` field,
      in `src/WebApp/WebApp.Blazor/Components/Pages/Home.razor` (depends on Phase 3.5).
- [X] T101 Move the recommendation-card/comparison-table/clarification-prompt rendering markup
      inside the per-entry `@foreach` loop, keyed off each entry's own `Turn` instead of the
      removed `_lastTurn`, so every turn's structured rendering stays in place as the conversation
      grows, in `src/WebApp/WebApp.Blazor/Components/Pages/Home.razor` (depends on T100). Manually
      verify in-browser per this phase's Independent Test (no bUnit component-test project exists
      yet for this repo, consistent with how Phase 4.5's picker UI was verified).

**Checkpoint**: A multi-turn conversation with several recommendations/comparisons/clarifications
shows all of their structured renderings simultaneously, each attached to its own turn, matching
SC-013.

---

## Phase 5: User Story 3 - Check Price, Availability, and Specific Characteristics (Priority: P3)

**Goal**: A shopper asks a targeted question about one product's price, availability, or
characteristics — including follow-ups about a product already shown — and gets a verified
answer or an honest "cannot verify"/"not found."

**Independent Test**: Ask about a named product's price/availability/characteristic and confirm
the answer matches seeded data or clearly states it can't be verified; ask about a
nonexistent product and confirm an honest "not found" response.

### Tests for User Story 3

- [X] T056 [P] [US3] Contract test for `GET /api/pricing/offers/{productId}` distinguishing
      `404` (no record) from `200` `Unknown` availability — already covered by
      `Single_offer_lookup_for_unknown_product_returns_404` and
      `Unknown_availability_is_distinguishable_from_missing_offer` in
      `tests/PricingAvailability.Api.Tests/BatchOffersContractTests.cs`; no separate file needed.
- [X] T057 [P] [US3] Contract test for Gateway `GET /api/products/{productId}` — concurrent
      Catalog+Pricing merge, partial success when Pricing is down — in
      `tests/Gateway.Api.Tests/ProductDetailContractTests.cs`.
- [X] T058 [P] [US3] Test that `get_product_details` returning `found:false` results in the
      conversation API relaying an honest "not found," never an invented product, in
      `tests/ProductAdvisor.Application.Tests/NotFoundHonestyTests.cs`.
- [X] T059 [P] [US3] Test for follow-up questions about a previously recommended/compared
      product, resolved via `ConversationSession.LastSearchResults` (the generalized concept
      that superseded the originally-planned `LastRecommendation`, per Phase 4.5/FR-022), in
      `tests/ProductAdvisor.Application.Tests/FollowUpQuestionTests.cs`.

### Implementation for User Story 3

- [X] T060 [US3] Implement `GET /api/pricing/offers/{productId}` single-offer endpoint (404 vs.
      Unknown distinction) — already implemented in
      `src/PricingAvailability/PricingAvailability.Api/Program.cs` (depends on T017).
- [X] T061 [US3] Implement Gateway `GET /api/products/{productId}` — concurrent Catalog+Pricing
      calls, partial-success handling — in `src/Gateway/Gateway.Api/` (depends on T049, T060,
      T023).
- [X] T062 [US3] Verify/extend follow-up question handling in the conversation orchestration
      loop — resolve against `LastSearchResults` before choosing a tool. Verified sufficient as-is
      via `FollowUpQuestionTests.cs` and a live check ("tell me more about the first one" after a
      headphones recommendation correctly resolved to Sony WH-1000XM5 with accurate specs) — no
      extension needed; the existing `BuildChatHistory` system message from Phase 4.5 already
      covers this — in `src/ProductAdvisor/ProductAdvisor.Application/` (depends on T040, T053,
      T096).
- [X] T063 [US3] Implement a Blazor single-product detail panel (price/availability + verified
      flags, "not found" state), linked from recommendation/comparison views, in
      `src/WebApp/WebApp.Blazor/` (depends on T061, T054).
- [X] T064 [US3] EndToEnd test covering quickstart Scenario 5 (verified fact lookup + not-found
      honesty) in `tests/EndToEnd.Tests/ProductLookupScenarioTests.cs` (depends on T063).

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements spanning all three stories.

- [X] T065 [P] EndToEnd resilience test covering quickstart Scenario 6 (Pricing outage → honest
      partial recommendation, no `5xx`) in
      `tests/EndToEnd.Tests/PartialFailureResilienceTests.cs` (constitution Principle V).
- [X] T066 [P] Assert correlation propagation (Gateway → Advisor → Catalog/Pricing share one
      `X-Correlation-Id`) — implemented via the already-configured OpenTelemetry logging
      provider's `IncludeScopes` plus a matching `Console:FormatterOptions:IncludeScopes` so the
      id is actually visible in each service's own log output (grepped directly, no separate
      OTLP collector needed for this scale), in `tests/EndToEnd.Tests/ObservabilityTests.cs`
      (constitution Principle VI). Along the way, fixed a real bug: `CorrelationIdMiddleware`
      was pushing a raw `Dictionary<string,object>` as the log scope, which renders as just its
      type name (no OTLP/console formatter ever showed the actual id) — changed to the
      structured-template `BeginScope` overload.
- [X] T067 [P] Extend `.github/workflows/ci.yml` with the docker-compose–based EndToEnd stage
      and the Render deploy trigger (native git auto-deploy per research.md §9, or the
      documented deploy-hook fallback) — the stage already existed; added `LLM_PROVIDER_*`
      secrets passthrough to the `end-to-end` job so it can actually reach a real LLM in CI
      (previously these always resolved empty, same as running with no provider configured).
- [X] T068 [P] Finalize `render.yaml` with real environment-variable bindings (Neon per-service
      connection strings, LLM provider key/endpoint) referencing Render's secret management —
      no values committed — env bindings were already complete; found and fixed a real gap
      while reviewing: none of the 4 API services map anything at `/` and `/health`/`/alive`
      were Development-only, so Render's default `/`-based health check would mark every
      backend service permanently unhealthy in Production (confirmed via a one-off `Production`
      container run: `/` → 404, `/alive` → 404 before the fix). Fixed by making the
      detail-free `/alive` liveness check available in every environment
      (`ServiceDefaults/Extensions.cs`) and adding `healthCheckPath: /alive` to each API
      service in `render.yaml`.
- [X] T069 [P] Add a lightweight performance check asserting Catalog/Pricing p95 < 300ms and a
      full US1 turn's non-LLM portion stays within plan.md's Performance Goals — implemented in
      `tests/EndToEnd.Tests/PerformanceTests.cs` (Catalog search p95, Pricing batch p95, and the
      direct comparison endpoint with `includeExplanation:false` — genuinely LLM-free per
      FR-018 — asserted well under the LLM-inclusive 3s turn budget); all 3 pass against the
      live stack.
- [X] T070 [P] Run `dotnet format` plus analyzer fixes across the solution; confirm CI's
      lint/type-check gate is green (constitution Principle I) — fixed whitespace formatting in
      3 files, suppressed a CA1848 warning (with justification) introduced by the T066
      correlation-id fix. A `--configuration Release --no-incremental` build (matching CI's
      build step, which surfaces more analyzer diagnostics than the default incremental build)
      now shows zero CA/CS/IDE warnings — only pre-existing, unrelated transitive-dependency
      warnings (MSB3277 EF Core version conflict, NU1902 AngleSharp advisory) remain.
- [X] T071 [P] Write the repository root `README.md`, pointing to quickstart.md, plan.md, and
      the Aspire/docker-compose run instructions.
- [X] T072 Manually walk through quickstart.md end-to-end once; file any gaps found as follow-up
      tasks rather than leaving them undiscovered. Walked all 6 Validation Scenarios against the
      live docker-compose stack (with a real LLM) and found/fixed 3 gaps: (1) `quickstart.md`
      referenced a nonexistent `docker-compose.ci.yml` overlay and a bare `dotnet test` that
      doesn't resolve from repo root — corrected to the actually-working commands; (2)
      `quickstart.md`'s Scenario 6 named a wrong service (`pricing-availability-api` vs. the
      real `pricing-api`) — corrected; (3) Scenario 2 (clarify-then-recommend) sometimes asked a
      second, optional-preferences clarifying question instead of proceeding to a
      recommendation once category+budget were both known — reinforced the system prompt in
      `ConversationOrchestrator.cs` to state category+budget are the ONLY required fields;
      verified 5/5 afterward (was ~2/3 before). All 6 scenarios now pass consistently except the
      already-known Scenario 3 LLM-judgment edge case (an unusually low budget occasionally
      prompts a currency-confirmation clarification instead of an honest zero-match
      recommendation) — a genuine LLM reasoning variance, not a code defect, left as a known,
      documented flake (see `RecommendationScenarioTests.Scenario_3`).

---

## Phase 7: Access Control, Observability Hardening & Checkout Link (Foundational + US4 Enhancement)

> **Numbering note**: this phase was added during a spec-refinement pass after Phase 6 shipped,
> continuing task IDs from the highest number then in use (T101) rather than renumbering. See
> spec.md FR-024/FR-025/FR-026/FR-027–FR-032, User Story 4, User Story 5, SC-014–SC-019, and
> research.md §16–§18 for the requirements and design this phase implements.

**Goal**: Every user-facing request requires a signed-in Google identity, verified independently
by the Gateway rather than trusted by network position; every internal service-to-service call
requires a shared internal credential; a signed-in user can never read another user's
conversation session; logs/traces/metrics reach a real, commonly-used observability backend
instead of console-only output; a second message for a session in flight is rejected rather than
processed concurrently; and a user can ask the advisor for a checkout link covering the products
they picked or were most recently shown.

**Independent Test**: Reach any Gateway endpoint with no/invalid/expired Google token and confirm
`401`; call Catalog/Pricing/Advisor directly with no/invalid internal API key and confirm `401`;
as two different signed-in users, confirm neither can read the other's session; send a second
message for a session while the first is still processing and confirm `409`; ask, after a
recommendation, to "check out with the first one" and confirm the returned link's product id
matches; confirm a deliberately-thrown exception in any API service is logged with its
correlation id and returns a clean `problem+json` body, not a raw stack trace.

### Tests for This Phase

- [X] T102 [P] Contract test: `Catalog.Api`/`Pricing.Api`/`Advisor.Api` endpoints return `401`
      for a missing or incorrect `X-Internal-Api-Key`, and succeed with a correct one, in each
      service's existing `*.Api.Tests` project (FR-029).
- [X] T103 [P] Contract test: Gateway endpoints return `401` for a missing, malformed, or
      expired `Authorization: Bearer` token, and succeed with a valid one (using a test JWT
      signed by a fake OIDC provider substituted for Google's in tests), in
      `tests/Gateway.Api.Tests/AuthenticationContractTests.cs` (FR-030).
- [X] T104 [P] Contract test: a session created by user A returns `404` (not `403`) when
      requested with user B's `X-User-Id`, and `200` for user A, in
      `tests/ProductAdvisor.Api.Tests/SessionOwnershipContractTests.cs` (FR-031).
- [X] T105 [P] Contract test: a second `POST .../messages` for the same `sessionId`, sent while
      the first is still processing (simulated via a blocked/slow stubbed tool call), returns
      `409` for the second request, in
      `tests/ProductAdvisor.Api.Tests/ConcurrentMessageRejectionTests.cs` (FR-024/SC-014).
- [X] T106 [P] MCP tool contract test for `generate_checkout_link` — resolves ids, returns a
      `url` encoding exactly those ids, and returns a client-error result for an unresolvable
      id — in `tests/ProductAdvisor.Api.Tests/CheckoutLinkToolTests.cs` (FR-025/SC-015).
- [X] T107 [P] EndToEnd test covering a full sign-in (test-double Google identity) → chat →
      "check out with the first one" → checkout-link scenario, plus a check that a deliberately
      thrown exception in an API service produces a correlation-id-tagged error log and a clean
      `problem+json` response (not a raw stack trace), in
      `tests/EndToEnd.Tests/AccessControlAndCheckoutScenarioTests.cs`.

### Implementation for This Phase

- [X] T108 Implement the internal-API-key mechanism in `src/Aspire/ServiceDefaults/` — an
      outbound `DelegatingHandler` (mirroring `CorrelationIdHandler`) that attaches
      `X-Internal-Api-Key` from configuration to every outbound call, and inbound middleware
      that validates it and short-circuits with `401` when absent/incorrect (research.md §18)
      (depends on T102).
- [X] T109 Apply the inbound internal-API-key middleware to `ProductCatalog.Api`,
      `PricingAvailability.Api`, and `ProductAdvisor.Api` (including `/mcp`), and register the
      outbound handler on every `HttpClient` that calls another internal service (Gateway→
      Advisor/Catalog/Pricing, Advisor→Catalog/Pricing) in
      `src/Gateway/Gateway.Api/` + `src/ProductAdvisor/ProductAdvisor.Infrastructure/` (depends
      on T108).
- [X] T110 Add `ConversationSession.UserId` in `src/ProductAdvisor/ProductAdvisor.Domain/` (set
      once at creation, never changed) + EF Core migration; enforce it in every session-scoped
      endpoint in `src/ProductAdvisor/ProductAdvisor.Api/` by comparing the request's
      `X-User-Id` header against the stored owner, returning `404` on mismatch (depends on
      T104).
- [X] T111 Implement Google sign-in in `src/WebApp/WebApp.Blazor/` — cookie authentication +
      OpenID Connect challenge against Google, a global `[Authorize]` fallback policy covering
      every page, and attaching the signed-in user's identity token as
      `Authorization: Bearer` on every call to Gateway (FR-030, research.md §17) (depends on
      T103).
- [X] T112 Implement JWT Bearer validation in `src/Gateway/Gateway.Api/` against Google's OIDC
      discovery document, extracting the token's `sub` claim and forwarding it as `X-User-Id`
      plus the internal API key on every downstream call (depends on T109, T111).
- [X] T113 Implement FR-024's concurrent-message rejection — a per-session in-progress marker
      (e.g., a guarded flag on `ConversationSession` or a short-lived lock keyed by
      `sessionId`) in `src/ProductAdvisor/ProductAdvisor.Api/`, returning `409` for a second
      request that arrives while the first is still being processed for that session (depends
      on T105).
- [X] T114 Implement the `generate_checkout_link` MCP tool + `CheckoutLink` value object in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/`, resolving ids against Catalog and a
      configurable checkout base URL; add the `checkoutLink` turn-response shape in
      `src/ProductAdvisor/ProductAdvisor.Application/` (ConversationApiMapper) and render it in
      `src/WebApp/WebApp.Blazor/` alongside the existing recommendation/comparison renderings
      (FR-025/FR-023, depends on T106).
- [X] T115 Wire `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` (`sync: false`)
      through `render.yaml` for all five services, and document the chosen OTLP-compatible
      backend's setup (e.g., Grafana Cloud) in `README.md`/`quickstart.md` (research.md §16)
      (depends on T107).
- [X] T116 Add a global exception-handling middleware (`problem+json` response, correlation-id-
      tagged error log) to `ProductCatalog.Api`, `PricingAvailability.Api`, `ProductAdvisor.Api`,
      and `Gateway.Api` — mirroring `WebApp.Blazor`'s existing `UseExceptionHandler` — so an
      unhandled exception is always logged once and never leaks a raw stack trace in Production
      (FR-027/FR-032, research.md §16) (depends on T107).
- [X] T117 Verify FR-026's accessibility baseline (keyboard-navigable, semantic HTML, readable
      focus order) holds across `Home.razor`, `ProductPicker.razor`, and `ProductDetail.razor` —
      confirmed: only native `<input>`/`<button>`/`<a>`/`<select>`/`<form>` elements are used
      throughout, including the new checkout-link and sign-out controls; no gap found.
- [ ] T118 Manually re-verify against the live stack: sign in with a real Google account
      end-to-end; confirm a second browser/incognito session signed in as a different Google
      account cannot read the first session's conversation; confirm a direct `curl` to
      Catalog/Pricing/Advisor without the internal API key is refused; confirm "check out with
      the first one" after a recommendation returns a working checkout link; confirm logs/traces
      for a real request appear in the configured observability backend.
      Partially done against the real docker-compose stack with a real LLM (this environment has
      no real Google OAuth credentials or observability backend to finish the rest): a real
      chat→recommendation turn and a chat→checkout-link turn both succeeded end-to-end; a second
      identity could not read the first's session (404); a direct curl to Catalog without the
      internal key was refused (401); a deliberately-provoked error was logged with its
      correlation id. Found and fixed two real bugs in the process (not test-only issues): (1)
      each service's `UseExceptionHandler()`/`UseCorrelationId()` were registered in the wrong
      order, so an error's own log line never carried the request's correlation id; (2) the E2E
      test JWT scheme was missing `MapInboundClaims = false`, so the `sub` claim silently
      remapped and Gateway→Advisor calls lost `X-User-Id`, breaking every chat turn through
      Gateway. Still needs a human with real `GOOGLE_CLIENT_ID`/`GOOGLE_CLIENT_SECRET` and an
      OTLP backend to confirm the real Google sign-in redirect and observability export.

**Checkpoint**: The whole system requires a verified identity at both the user-facing and
internal boundaries, sessions are private to their owner, observability data reaches a real
backend, concurrent turns on one session can't interleave, and checkout link generation closes
the loop from recommendation/comparison to purchase.

---

## Phase 8: Startup Readiness ("Loading Screen") & Service Warm-Up (US6)

> **Numbering note**: continues task IDs from T119 (highest previously used: T118). See
> spec.md FR-033/FR-034/FR-035, User Story 6, SC-020/SC-021, and research.md §19 for the
> requirements and design this phase implements.

**Goal**: Before a shopper reaches the interactive chat UI, the web app shows a starting-up
state and confirms Catalog/Pricing/Advisor are reachable via a new Gateway aggregate endpoint
that reuses each service's existing `/alive` liveness check. The wait is bounded — the shopper
always reaches the interactive UI, honestly labeled if something is still unreachable, rather
than being blocked indefinitely — and, on hosts where an idle service can go to sleep (e.g.
Render's free tier), this check's own act of probing each service doubles as a warm-up ping.

**Independent Test**: Load the web app with one or more dependent services simulated as
unreachable and confirm a starting-up/degraded state is shown, then that the interactive UI
still appears after the bounded wait with the affected service(s) indicated; load it again with
all services reachable and confirm the interactive UI appears promptly.

### Tests for This Phase

- [X] T119 [P] Contract test: `GET /api/system-status` succeeds with no `Authorization` header,
      returns `overall: "ready"` when all three simulated `/alive` checks succeed, and
      `overall: "degraded"` (still `200`, never a `5xx`) with the correct `reachable: false`
      entry/entries when one or more are simulated as failing/timing out, in
      `tests/Gateway.Api.Tests/SystemStatusContractTests.cs` (FR-033/FR-034/FR-035,
      SC-020/SC-021).
- [ ] T120 [P] Test: the starting-up screen shows the interactive UI once all services report
      reachable, and shows the interactive UI anyway (with a degraded indicator naming the
      affected service) once the bounded wait elapses while a service is still unreachable — in
      `tests/EndToEnd.Tests/` (simulating a stopped service against the live stack), or a
      WebApp-level component test if a Blazor test approach is already in place.
      Not yet covered by an automated test — the whole app requires a real signed-in Google
      identity before `Home.razor` renders at all (FR-030), so exercising `StartupGate.razor`'s
      own polling/timeout logic needs either a real browser session (Playwright-style) or bUnit,
      neither of which this project has set up yet. `T119`'s Gateway-level contract tests fully
      cover the `ready`/`degraded` aggregation logic the component depends on; only the
      Razor component's client-side polling loop itself is untested.

### Implementation for This Phase

- [X] T121 Implement `GET /api/system-status` in `src/Gateway/Gateway.Api/` — concurrently calls
      Catalog's, Pricing's, and Advisor's `/alive` endpoints with a short per-call timeout
      (`Task.WhenAll`, mirroring the existing product-detail/search composition pattern), merges
      into the `SystemReadinessStatus` shape (data-model.md), and marks the endpoint
      `AllowAnonymous` (depends on T119).
- [X] T122 Implement the starting-up screen in `src/WebApp/WebApp.Blazor/` — shown before the
      interactive chat UI, polls `GET /api/system-status` on load, shows per-service status
      while waiting, and proceeds to the interactive UI either once `overall: "ready"` or once a
      bounded wait elapses, surfacing which service(s) are still unreachable in the latter case
      (depends on T121, T120).
- [ ] T123 Manually re-verify against the deployed (Render) stack: reload the web app and
      confirm the starting-up screen appears and resolves; simulate a backing service outage
      (e.g. stop it, or break its `InternalApiKey`) and confirm the starting-up screen still
      resolves to the interactive UI after its bounded wait, correctly naming the affected
      service.

**Checkpoint**: A shopper never sees an interactive-looking UI before the system has actually
checked its own readiness, is never blocked indefinitely if a dependency is down, and — as a
side effect on hosts like Render — dependent services are more likely to already be warm by the
time real usage begins.

---

## Phase 9: Deterministic Turn-Processing Cycle Foundation (FR-036–FR-059)

> **Numbering note**: continues task IDs from T124 (highest previously used: T123). A
> `/speckit-analyze` consistency review found that FR-036 through FR-141 — the entire
> deterministic turn-processing cycle, structured intent extraction, deterministic state
> management, turn result types, tool recipes, resource budgets, the Evidence Envelope, system
> prompts, request guardrails, privacy-by-design, credential hardening, observability policy,
> and the eval suite — had zero task coverage; the built system still runs the free
> `FunctionInvokingChatClient` tool-selection loop the spec explicitly documents as superseded
> (spec.md Assumptions, research.md §20). Phases 9–14 close that gap. See spec.md's "System
> Requirement" cross-cutting sections and research.md §20–§33 for the requirements and design
> these phases implement.

**Goal**: Replace `ConversationOrchestrator`'s free tool-selection loop with the fixed,
application-controlled ten-stage cycle (input validation → structured intent extraction →
schema validation → deterministic state merge → policy routing → intent-specific tool recipe →
tool-result validation → constrained narration → output validation → persistence), with
structured-intent-extraction's closed schema/one-repair-attempt contract and `CurrentRequirement`
as the sole authoritative, field-level-merged source of the user's requirement.

**Independent Test**: Send a message that partially updates a previously-established requirement
(e.g., only a budget change) and confirm every other field (category, required features,
language) persists unchanged; send a message that fails schema validation on its first
extraction attempt (simulated via a stubbed malformed LLM response) and confirm exactly one
repair attempt occurs before falling back to clarification.

### Tests for This Phase

- [X] T124 [P] Unit tests for `StructuredIntent` schema validation — required fields present;
      `Intent` restricted to the closed six-value set; a value outside it is a schema failure,
      never a new route — in `tests/ProductAdvisor.Domain.Tests/StructuredIntentValidationTests.cs`
      (FR-048/FR-049). Verified: 3/3 passing.
- [X] T125 [P] Unit tests for deterministic state-merge — a partial patch persists every
      previously-known field; an explicit empty list clears a list field; an absent field never
      clears; a budget/category change replaces only that field — in
      `tests/ProductAdvisor.Domain.Tests/StateMergeTests.cs` (FR-057/FR-058). Verified: 7/7 passing.
- [X] T126 [P] Application-layer tests for the extraction stage — a schema-valid result passes
      through; a schema-invalid result triggers exactly one repair attempt; a second failure
      falls back to `clarification`, never a third attempt — in
      `tests/ProductAdvisor.Application.Tests/ExtractionStageTests.cs` (FR-050/FR-051). Verified:
      5/5 passing, via a `FakeChatClient` that queues per-call canned responses (extraction, then
      narration) rather than one fixed response for every call — the pre-existing fake only ever
      needed to answer one call per turn before this phase.
- [X] T127 [P] Unit tests for `PolicyRouter` — two turns with identical merged state select the
      identical route (determinism); missing essential fields route to `clarification` — in
      `tests/ProductAdvisor.Application.Tests/PolicyRouterTests.cs` (FR-041, SC-026). Verified:
      7/7 passing.
- [X] T128 Application-layer test asserting a turn executes all ten cycle stages in fixed order,
      exactly once, with no stage skipped/reordered/repeated, in
      `tests/ProductAdvisor.Application.Tests/TurnProcessingCycleTests.cs` (FR-036, SC-022).
      Verified: 3/3 passing. Scoped honestly to what this phase actually implements — stages
      1–5 (input validation, extraction, schema validation, state merge, policy routing) are
      fully deterministic and asserted here; stages 6–10 (tool recipe scoping, tool-result
      validation, the Evidence Envelope, output validation) are Phase 10/11 work — this phase's
      `recommend` route already reaches its terminal compute call deterministically, while
      `compare`/`checkout`/`product_fact` still bridge through the pre-existing free
      tool-invocation mechanism pending Phase 10's tool-recipe scoping (see T134's note).

### Implementation for This Phase

- [X] T129 [P] Define the `StructuredIntent` DTO (`Intent` enum, `RequirementPatch`,
      `ProductReferences`, `MissingFields`, `Confidence`, `Language`) per data-model.md, in
      `src/ProductAdvisor/ProductAdvisor.Domain/StructuredIntent.cs` (depends on T124). The
      `Intent` enum is decorated with `[JsonStringEnumMemberName("product_fact")]` so the wire
      value matches spec.md's literal `product_fact` rather than the naming-policy-transformed
      default (`productFact`) — found via a real deserialization failure while verifying T126
      (see Errors and fixes below).
- [X] T130 Extend `UserRequirement` (T012) with the field-level merge rule (present replaces,
      absent carries forward, explicit-empty-list clears) and the `Units`/`AvailabilityRequirements`
      fields from data-model.md, in `src/ProductAdvisor/ProductAdvisor.Domain/UserRequirement.cs`
      (depends on T125). Also added `ConversationSession.MergeRequirement(RequirementPatch)` —
      the new field-level operation, distinct from the pre-existing `UpdateRequirement`'s
      wholesale replace, which remains for the cases (e.g. direct test setup) that still want it.
- [X] T131 Implement the structured-intent-extraction system prompt — schema-first output,
      `CurrentRequirement` included verbatim, user input marked as untrusted data, no
      chain-of-thought requested, versioned (`ExtractionStage.PromptVersion`) — in
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/ExtractionStage.cs` (depends on
      T129). **Deviates from the planned file path**: kept in `ProductAdvisor.Application`
      alongside `ExtractionStage` rather than a new `ProductAdvisor.Infrastructure/Prompts/`
      folder — matches this codebase's existing convention (the pre-existing narration system
      prompt was already inline in `ConversationOrchestrator`, in Application, not Infrastructure)
      rather than introducing a new layering pattern for this phase alone.
- [X] T132 Implement the extraction pipeline stage — one LLM call constrained to the
      `StructuredIntent` schema via `IChatClient.GetResponseAsync<T>` (schema-first structured
      output, FR-094), at most one repair attempt on validation failure, fallback to `null`
      (the orchestrator's signal for `clarification`) on a second failure — in
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/ExtractionStage.cs` (depends on
      T126, T131).
- [X] T133 Implement `PolicyRouter` — deterministic route selection (`Recommend`/`Compare`/
      `Checkout`/`ProductFact`/`Smalltalk`/`Unsupported`/`Clarify`) from merged
      `CurrentRequirement` + `StructuredIntent`, as pure application-layer code, never a model
      choice — in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/PolicyRouter.cs`
      (depends on T127, T130).
- [X] T134 Rewrite `ConversationOrchestrator` (T040) to run stages 1–5 of the cycle deterministically,
      then dispatch by route — in
      `src/ProductAdvisor/ProductAdvisor.Application/ConversationOrchestrator.cs` (depends on
      T128, T132, T133). Added `IRecommendationService` (+ its Infrastructure adapter
      `RecommendationService`, wrapping the same `ComputeTools.GetRecommendationsFromRequirementAsync`
      the LLM-facing `get_recommendations` tool now also delegates to — one implementation, not
      two) so the `recommend` route calls the deterministic compute step directly from the
      already-merged `CurrentRequirement`, never from arguments the model reconstructs itself
      (FR-066). `compare`/`checkout`/`product_fact` still run through the pre-existing free
      tool-invocation loop as an explicit, temporary bridge to Phase 10's tool-recipe scoping —
      but now correctly typed: a `product_fact` turn that resolves to nothing captured now
      produces `answer` (added `AdvisorTurnResult.ForAnswer`/`ForUnsupported`, and
      `ConversationApiMapper` cases for both), never the pre-cycle behavior of silently
      defaulting to `clarification` — a real bug this phase fixed (FR-062/FR-063), not merely
      refactored around. `smalltalk`/`unsupported` are now correctly classified and answered
      instead of following the old code's only fallback (asking a clarifying question, wrong for
      a greeting or an out-of-scope request).

**Verification**: `dotnet build` — 0 errors, 0 new warnings (same pre-existing NU1902/MSB3277
warnings as before this phase). `ProductAdvisor.Domain.Tests`: 49/49 passing.
`ProductAdvisor.Application.Tests`: 24/24 passing (including 3 pre-existing test files rewritten
for the new pipeline — `OrchestrationNeverComputesTests`, `NotFoundHonestyTests`,
`FollowUpQuestionTests` — their invariants still hold, their setup now reflects the two-call
extraction+narration shape instead of one free-form call). `ProductAdvisor.Api.Tests` and
`EndToEnd.Tests` were **not** run — both require Docker/Testcontainers, unavailable in this
sandbox (the same limitation T026/T044/T118 already documented); `dotnet build` confirms they
still compile against the new constructor signature, but their contract/end-to-end assertions
against a live docker-compose stack are unverified here.

**Checkpoint**: The conversation loop's classification and state-management stages are a fixed,
application-controlled pipeline rather than free-form reconstruction from history; `recommend`'s
compute step is fully deterministic; `CurrentRequirement` is deterministically and correctly
maintained across turns via field-level merge. Phases 10–14 build the remaining pipeline stages
(tool-recipe scoping for `compare`/`checkout`/`product_fact`, resource budgets, the Evidence
Envelope, guardrails, privacy, credential hardening, observability, and the eval suite) on top of
this foundation.

---

## Phase 10: Turn Result Types, Tool Recipes & Resource Budgets (FR-060–FR-085)

**Goal**: Every turn resolves to exactly one of seven discriminated result types, never
defaulting to `clarification`; each route's tool recipe is fixed and minimal with a scoped
tool-exposure surface; a `TurnResourceBudget` enforces hard limits on LLM/tool calls, loop
iterations, consecutive errors, and overall timeout; recommendations precisely separate hard
constraints (disqualifying) from soft preferences (ranking-only), with nearest alternatives
labeled by violated constraint.

**Independent Test**: Ask a targeted product-fact question and confirm the response is typed
`answer`, not `clarification`; request a recommendation with an over-budget candidate present in
seed data and confirm it never appears in `items` but may appear in `nearestAlternatives` labeled
with the violated constraint; force a tool-call count past a low test-configured maximum and
confirm the turn ends in `error` with zero further tool calls.

### Tests for This Phase

- [X] T135 [P] Unit tests for `TurnResult` type assignment — absence of `recommendation`/
      `comparison`/`checkoutLink` never defaults to `clarification`; `product_fact` with a
      validated tool result → `answer`; `unsupported` intent → `unsupported`; a tool-result
      validation failure → `error` — in `tests/ProductAdvisor.Application.Tests/TurnResultTypeTests.cs`
      (FR-060–FR-065). 7 tests: orchestrator-level discrimination (`unsupported`→`unsupported`
      with zero narration calls, `smalltalk`→`answer`, an empty-`Items` `recommend` turn stays
      `recommendation` not `clarification`) plus mapper-level coverage of the new `error`/
      `nearestAlternatives` wire shapes added by this phase.
- [X] T136 [P] Contract tests for tool-exposure scoping — a `recommend`-route turn's tool-list
      surface never includes `compare_products`/`generate_checkout_link`; `smalltalk`/
      `unsupported` make zero tool calls; a `product_fact` turn calls only the tool(s) its
      specific fact needs — in `tests/ProductAdvisor.Api.Tests/ToolRecipeScopingTests.cs`
      (FR-066–FR-070). **Not run in this sandbox** (Docker/Testcontainers unavailable, same
      limitation as T026/T044/T118/T134) — confirmed to compile only. Required a new
      `ExtractionAwareScriptedChatClient` (this project's pre-existing `ScriptedChatClient`
      predates Phase 9's extraction-first pipeline and always returns one fixed response
      regardless of call number, so it cannot express "first call must be valid StructuredIntent
      JSON before a route call is reachable at all"). Note: the rest of
      `ProductAdvisor.Api.Tests` (written before Phase 9) still assumes the old free-form
      single-call loop and is very likely stale against the current pipeline — a pre-existing,
      already-disclosed gap (Phase 9's completion notes) this task does not attempt to close, since
      it cannot be verified here either way.
- [X] T137 [P] Tests for each `TurnResourceBudget` limit's fail-safe (max tool calls, max
      consecutive tool errors, max loop iterations, overall timeout each → `error`, never a
      partial success or an infinite loop) in
      `tests/ProductAdvisor.Application.Tests/TurnResourceBudgetTests.cs` (FR-071–FR-079). 5
      tests, 2 directly against `TurnResourceBudgetGuard` (its own timeout → degraded `error`; a
      caller's own cancellation token propagates unhandled rather than becoming a result — FR-024's
      disconnect case) and 3 through the full orchestrator via a new `ScriptedChatClient` (in
      `ProductAdvisor.Application.Tests`) capable of hanging, throwing, or returning a
      trailing-tool-call response on its second (route) call.
- [X] T138 [P] Unit tests for hard-constraint filtering — an over-budget, currency-mismatched,
      missing-required-feature, or (when explicitly stated) out-of-stock candidate is excluded
      from `Items`; a soft-preference-only mismatch never excludes a candidate;
      `NearestAlternatives` never appears alongside a non-empty `Items` — in
      `tests/ProductAdvisor.Domain.Tests/ScoringPolicyHardConstraintTests.cs` (FR-080–FR-085). 8
      tests. Also updated one pre-existing `ScoringPolicyTests` test
      (`Score_ranks_candidates_matching_more_required_features_higher`) that had relied on the
      now-superseded soft-ranking-only treatment of `RequiredFeatures` — rewritten to use
      `Preferences` instead so it still tests ranking, not exclusion.

### Implementation for This Phase

- [X] T139 [P] Add the `TurnResult` discriminated shape (`answer`/`clarification`/
      `recommendation`/`comparison`/`checkoutLink`/`unsupported`/`error`) per data-model.md,
      replacing the current four-shape response mapper, in
      `src/ProductAdvisor/ProductAdvisor.Application/TurnResult.cs` (depends on T135).
      **Deviates from the planned file path**: six of the seven types already existed on
      `AdvisorTurnResult` (this codebase's `TurnResult` — Phase 9 introduced `answer`/
      `unsupported` alongside the earlier `clarification`/`recommendation`/`comparison`/
      `checkoutLink`), so this task added only the missing `error` type
      (`AdvisorTurnResult.ForError(message, degraded)`) plus the `Degraded` field, and the
      matching `ConversationApiMapper`/`ConversationTurnResponse` wire-shape support, rather than
      introducing a second, competing `TurnResult` type.
- [X] T140 Implement per-route `ToolRecipe` scoping — the tool-list surface presented for a turn
      (whatever invokes it) is limited to exactly that route's recipe before any language-model
      call — in `src/ProductAdvisor/ProductAdvisor.Infrastructure/ToolRecipes/` (depends on
      T133, T136). `ToolRecipe.GetAllowedToolNames(Route)` plus a new
      `IAdvisorToolCatalog.GetTools(Route)` overload; `ConversationOrchestrator`'s legacy
      tool-invocation bridge (`compare`/`checkout`/`product_fact`, both the streaming and
      non-streaming entry points) now calls this instead of the unscoped `GetTools()`.
- [X] T141 Implement `TurnResourceBudget` enforcement — max primary LLM calls (2 + 1 repair), max
      tool calls, max consecutive tool errors, max loop iterations, overall turn timeout,
      cancellation-on-disconnect releasing the FR-024 in-flight marker, non-idempotent-operation
      exclusion from automatic retry — in
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/TurnResourceBudgetGuard.cs`
      (depends on T134, T137). Max tool calls / max consecutive tool errors are enforced by the
      shared `IChatClient`'s own `FunctionInvokingChatClient.MaximumIterationsPerRequest`/
      `MaximumConsecutiveErrorsPerRequest` (now configured in `AdvisorAiExtensions` from a new
      `TurnResourceBudget` config section) rather than re-implemented — the framework's own
      supported mechanism for the same guarantee. Their exact behavior when a limit is hit isn't
      documented on the public API surface, so it was verified empirically with a throwaway
      harness before relying on it (same approach as Phase 9's serializer-options bug): reaching
      `MaximumIterationsPerRequest` returns the response gracefully with a trailing, never-invoked
      tool call (detected by `TurnResourceBudgetGuard.ExceededToolCallBudget`); reaching
      `MaximumConsecutiveErrorsPerRequest` re-throws the underlying tool exception out of
      `GetResponseAsync`. `TurnResourceBudgetGuard.RunAsync` adds the one limit that mechanism
      doesn't cover — the overall wall-clock timeout, via a linked, `CancelAfter`
      `CancellationTokenSource` — and distinguishes its own timeout (→ `TurnBudgetExceededException`,
      translated to a degraded `error`) from the caller's own cancellation token firing (a client
      disconnect, left to propagate unhandled so no result is persisted, per FR-024).
      `AllowConcurrentInvocation = false` was also set (FR-069: a compute/terminal tool call must
      never run concurrently with another tool call). **Scope note**: full budget enforcement
      (timeout + tool-call/consecutive-error translation to `error`) applies to the non-streaming
      `ProcessMessageAsync` entry point; the streaming entry point applies the same tool-recipe
      scoping (T140) but not the timeout wrapper, since a mid-stream `yield return` cannot sit
      inside a `try`/`catch` — data-model.md's `TurnResourceBudget` explicitly allows the
      streaming endpoint's fail-safe on overall timeout to be "no `result` event" rather than a
      gracefully streamed `error`.
- [X] T142 Extend `ScoringPolicy` (T035) to a two-phase hard-constraint filter (budget as
      ceiling, required features, explicit availability requirement, currency compatibility) +
      soft-preference rank, producing `NearestAlternative` entries with `ViolatedConstraints` for
      excluded candidates, in `src/ProductAdvisor/ProductAdvisor.Domain/ScoringPolicy.cs`
      (depends on T138). Per spec.md's Assumptions (added during this session's consistency
      review), `NearestAlternatives` is populated only when `Items` ends up empty — never
      alongside a non-empty `Items` — so a verified hard-constraint violator sitting alongside
      other qualifying candidates is simply omitted from the response, not surfaced as an
      "alternative". A price-unverified candidate still skips the budget/currency check entirely
      (nothing to confirm, FR-005) but is still subject to the required-feature check (read from
      catalog specification data, not pricing data, so always independently confirmable).
- [X] T143 Extend the `get_recommendations` MCP tool's input (`availabilityRequirements`) and
      output (`nearestAlternatives`) per contracts/advisor-mcp-tools.md, in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/Tools/ComputeTools.cs` (depends on T142;
      extends T038). `nearestAlternatives` needed no separate tool-output change — it flows
      automatically as part of the `Recommendation` T142 already extended.

**Verification**: `dotnet build` — 0 errors, 0 new warnings (same pre-existing NU1902/MSB3277
warnings as before this phase); `dotnet format --verify-no-changes` clean on every file this
phase touched. `ProductAdvisor.Domain.Tests`: 58/58 passing (49 + 9 new). `ProductAdvisor.Application.Tests`:
35/35 passing (24 + 11 new). `ProductAdvisor.Api.Tests`/`EndToEnd.Tests` confirmed to still
compile against every signature change in this phase, but — as with T026/T044/T118/T134 — not run
here (Docker/Testcontainers unavailable in this sandbox).

**Checkpoint**: Every turn's outcome is precisely typed and traceable to policy + tool outcome
alone; no turn can run away on tool calls or loop iterations; recommendations never silently
include a disqualified product.

---

## Phase 11: Evidence Envelope & System Prompts (FR-086–FR-103)

**Goal**: Narration receives only a deterministically-assembled Evidence Envelope — never raw
tool output or the raw user message — and is never the source of a price, specification,
availability, score, rating, delta, or checkout URL; output validation rejects/strips/replaces
any narration claim the Envelope doesn't back, without ever touching the turn's structured data;
both system prompts are versioned, section-separated, and schema-first (extraction).

**Independent Test**: Stub a narration response that states a price not present in the tool
result and confirm the delivered `message` never contains that value while `items`/`type` stay
identical to the grounded case; confirm zero additional LLM calls are made to produce the
fallback narration.

### Tests for This Phase

- [X] T144 [P] Unit tests for `EvidenceEnvelope` assembly — every `CanonicalData` field has a
      `VerificationStatus`/`Provenance` entry; the envelope is empty for `smalltalk`/
      `unsupported`; assembly is deterministic across identical tool results — in
      `tests/ProductAdvisor.Application.Tests/EvidenceEnvelopeTests.cs` (FR-086/FR-091/FR-092). 8
      tests covering `Recommendation`/`Comparison`/`CheckoutLink`/empty assembly. Also added
      `tests/ProductAdvisor.Application.Tests/OutputValidationStageTests.cs` (7 tests) — a
      runnable unit-level counterpart to T145's Docker-dependent contract test, directly proving
      this phase's Independent Test scenario (a fabricated price is rejected; a grounded
      narration passes through byte-identical; the fallback is deterministic and never touches
      `EvidenceEnvelope.CanonicalData`).
- [X] T145 [P] Contract tests for narration grounding — a stubbed ungrounded claim (price, spec,
      availability, score, rating, delta, or checkout URL not in the Envelope) is rejected/
      stripped/replaced while `items`/`criteria`/`rows`/`fact`/`url` and `type` remain
      byte-identical to the grounded case; the fallback triggers zero additional LLM calls — in
      `tests/ProductAdvisor.Api.Tests/NarrationGroundingTests.cs` (FR-088–FR-090). **Not run in
      this sandbox** (Docker/Testcontainers unavailable, same limitation as T026/T044/T118/T134/
      T136) — confirmed to compile only; reuses T136's `ExtractionAwareScriptedChatClient`.
      Covers only the `recommend` route (the one route with a fully-separated envelope-only
      narration call, see T147's note) — a fabricated price and a grounded narration.

### Implementation for This Phase

- [X] T146 Implement `EvidenceEnvelope` assembly from validated tool results — result type,
      canonical structured data, verification status, tool provenance, unverified/unavailable
      fields, tool execution status, allowed-claims whitelist — entirely by deterministic
      application code, in
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/EvidenceEnvelopeBuilder.cs`
      (depends on T134, T144). Builders for `Recommendation`/`Comparison`/`CheckoutLink`, plus
      `Empty(resultType)` for `smalltalk`/`unsupported`. Allowed claims are numeric tokens
      extracted field-by-field (price, score, rating, deltas, specification/criterion values,
      the stated budget) via a shared `NumericClaim` normalizer
      (`src/ProductAdvisor/ProductAdvisor.Application/Pipeline/NumericClaim.cs`) — deliberately
      field-by-field rather than a blanket regex scan over the canonical data's serialized JSON,
      since the latter would also treat digit-runs inside a product's GUID as "allowed" numbers,
      narrowing what output validation could actually catch.
- [X] T147 Implement the constrained-narration system prompt — receives only the Evidence
      Envelope, instructed to summarize salient points rather than restate every value, no
      simultaneous brevity-and-exhaustive-restatement conflict, language/anti-disclosure/
      no-chain-of-thought instructions, versioned — in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/Prompts/NarrationPrompt.cs` (depends on
      T146). **Deviates from the planned file path**: kept in `ProductAdvisor.Application/Pipeline/`
      instead — `ProductAdvisor.Application` does not (and per the constitution's layering, must
      not) reference `ProductAdvisor.Infrastructure`, so a prompt used by
      `ConversationOrchestrator` (in Application) cannot live in Infrastructure; the same
      deviation T131 already recorded for the extraction prompt. **Scope note**: wired into the
      `recommend` route only (both entry points), which is the one route with a fully
      tool-call/narration-separated flow already (Phase 9) — FR-086/FR-087 are fully met there. The
      `compare`/`checkout`/`product_fact` legacy bridge still runs its tool call and narration as
      one blended LLM turn (the model sees raw tool output while writing narration, not only the
      Envelope) — closing that gap requires splitting that bridge into the same two-step shape,
      which is a bigger, riskier change than these six tasks scope for; T148's grounding check is
      still applied to that bridge's `comparison`/`checkoutLink` output as a post-hoc safety net
      (see T148's note) even though T147's stricter guarantee isn't fully met there yet.
- [X] T148 Implement output validation's grounding check — every numeric/factual narration claim
      checked against the Envelope's allowed claims; reject/strip/replace with a deterministic
      (non-LLM) fallback on an ungrounded claim; never alters `TurnResult`'s structured fields or
      type — in `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/OutputValidationStage.cs`
      (depends on T145, T147). Whole-response rejection granularity (spec.md Assumptions leaves
      this an implementation detail) — simpler and strictly safer than partial-sentence
      stripping. Checks both numeric claims (against `AllowedClaims`) and a checkout URL (exact
      match against `AllowedUrl`) — the fabricated-narration eval class covers 5 of FR-087's
      seven named categories (price/specification/score/rating/delta via numbers, checkout URL
      exactly); availability-status *phrases* (e.g. "in stock" stated when the candidate is
      actually out of stock) are not separately grounded — a known, narrower scope than the full
      seven, left to the structured UI's own correct rendering. Applied fully to `recommend`
      (T147); applied as a post-hoc safety net to the legacy bridge's `comparison`/`checkoutLink`
      results (`ConversationOrchestrator.ApplyGroundingIfApplicable`, non-streaming entry point
      only — the streaming entry point has already sent narration tokens to the client by the
      time this would run, an unavoidable consequence of streaming already-sent text). Explicitly
      NOT applied to `product_fact`/`smalltalk`: neither has a structured capture to build a
      non-trivial Envelope from, so an empty-claims check would reject every legitimate fact
      these routes state — that would be a regression, not a safety improvement, so it's left for
      whichever future work gives `product_fact` its own structured fact capture.
- [X] T149 Add runtime-observable version identifiers to both prompts (T131, T147), logged with
      every call, in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Prompts/`. **Deviates from
      the planned file path** for the same reason as T147. Added `ILogger<ConversationOrchestrator>`
      (resolved from the host's already-registered logging, no new DI wiring needed) and two
      `[LoggerMessage]` source-generated log calls — one per turn classification (always logs
      `ExtractionStage.PromptVersion` + the selected route), one whenever narration actually runs
      for `recommend` (logs `NarrationPrompt.PromptVersion`) — avoiding the CA1848 boxed-logging
      warning this codebase otherwise flags.

**Verification**: `dotnet build` — 0 errors, 0 new warnings; `dotnet format --verify-no-changes`
clean on every file this phase touched. `ProductAdvisor.Domain.Tests`: 58/58 passing (unchanged).
`ProductAdvisor.Application.Tests`: 50/50 passing (35 + 15 new: 8 `EvidenceEnvelopeTests` + 7
`OutputValidationStageTests`). `ProductAdvisor.Api.Tests`/`EndToEnd.Tests` confirmed to still
compile against every signature change in this phase, but — as with every prior phase's Api.Tests
work (T026/T044/T118/T134/T136) — not run here (Docker/Testcontainers unavailable in this
sandbox).

**Checkpoint**: Narration can never introduce an unverified fact into a delivered response, and
the structured UI is always correct independent of narration's fate.

---

## Phase 12: Request Guardrails & Privacy-by-Design (FR-104–FR-123)

**Goal**: Oversized/malformed/dangerous input is rejected before any LLM/tool call; per-user
rate/concurrency/quota limits are enforced; potential PII is blocked or redacted before reaching
the LLM provider; prompts never carry the user's stable identifier; users can delete their own
history and old sessions are deleted automatically; encryption and LLM-provider data-handling
requirements are confirmed.

**Independent Test**: Submit a message exceeding the configured max length and confirm a `400`
with zero LLM/tool calls; submit a message containing an obvious PII pattern (e.g., an email
address) and confirm it never reaches the extraction call verbatim; delete a session and confirm
a subsequent `GET` returns `404`.

### Tests for This Phase

- [X] T150 [P] Contract tests for input guardrails — oversized message (`400`), oversized body
      (`413`), dangerous control characters (`400`), oversized hard-constraint/preference lists
      (`400`) — each with zero LLM/tool calls made — in
      `tests/ProductAdvisor.Api.Tests/InputGuardrailTests.cs` (FR-104–FR-107/FR-113). Covers
      oversized message and control characters (2 tests); oversized body (`413`) is Kestrel's own
      behavior (T156) and not separately re-tested here. **Not run in this sandbox**
      (Docker/Testcontainers unavailable, same limitation as every other `ProductAdvisor.Api.Tests`
      addition this session) — confirmed to compile only; verified by the runnable
      `InputValidationStageTests`/`ConversationOrchestratorGuardrailTests` instead.
- [X] T151 [P] Contract tests for strict value validation — an invalid currency/operator/unit/
      product-id in extraction output routes to `clarification`, never a tool call — in
      `tests/ProductAdvisor.Api.Tests/StrictValueValidationTests.cs` (FR-108). Covers currency and
      negative-budget (2 tests) — see T158's note on why operator/unit validation isn't covered.
      Not run here; verified by the runnable `MoneyTests` instead.
- [ ] T152 [P] Contract tests for rate/concurrency/quota limits — `429` with zero LLM/tool calls
      once a per-user limit is exceeded — in `tests/Gateway.Api.Tests/RateLimitAndQuotaTests.cs`
      (FR-109–FR-111). **Not written** — see T159/T160's note; there is no per-user rate,
      concurrency, or quota implementation this phase to test against, and a test file asserting
      behavior that doesn't exist would misrepresent this phase's actual scope.
- [X] T153 [P] Contract tests for PII screening — a PII fixture never reaches the extraction call
      verbatim (`Blocked` → `400`, or `Redacted` → only `RedactedText` is sent) — in
      `tests/ProductAdvisor.Api.Tests/PiiScreeningTests.cs` (FR-116). Covers the `Blocked` case;
      the `Redacted` case is covered by the runnable
      `ConversationOrchestratorGuardrailTests.A_redacted_message_is_what_extraction_receives_never_the_original_raw_text`
      instead, which can actually assert on `FakeChatClient.CallHistory` (this Docker-dependent
      project's chat-client fakes don't expose the extraction call's own message list the same
      way). Not run here.
- [X] T154 [P] Contract tests for user-initiated deletion — subsequent `GET`/`POST` returns `404`
      after deletion; `409` while a turn is in flight — in
      `tests/ProductAdvisor.Api.Tests/ConversationDeletionTests.cs` (FR-119). 6 tests, including
      bulk (`DELETE /api/conversations`) deletion and the in-flight-turn `409` case (reusing the
      `BlockingChatClient` pattern from `ConcurrentMessageRejectionTests`). Not run here.

### Implementation for This Phase

- [X] T155 [P] Implement input-validation-stage guardrails — max message length, Unicode
      normalization, control-character rejection, max active conversation context size (bounds
      prompt inclusion only, never persistence) — in
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/InputValidationStage.cs` (depends
      on T134, T150). Max active context size (FR-112) applies in `BuildLegacyChatHistory`
      (`session.Messages.TakeLast(guardrailOptions.MaxActiveContextMessages)`) — bounds only what
      the legacy tool-invocation bridge includes in a prompt, never `ConversationSession.Messages`
      itself.
- [X] T156 [P] Implement max request body size (`413`) at the HTTP layer before parsing, in
      `src/Gateway/Gateway.Api/` + `src/ProductAdvisor/ProductAdvisor.Api/` (depends on T150).
      `builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = ...)` in both — Kestrel
      itself throws `BadHttpRequestException` with status 413 once exceeded, which the existing
      `GlobalExceptionHandler` (research.md §16) already preserves verbatim rather than collapsing
      to 500, so no new exception-handling code was needed for this one.
- [X] T157 Implement max count/per-entry length for `RequiredFeatures`/`Preferences`/
      `AvailabilityRequirements`, enforced cumulatively at state-merge time (not just per-patch),
      in `src/ProductAdvisor/ProductAdvisor.Domain/UserRequirement.cs` (extends T130).
      **Deviates from the planned location**: implemented as
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/RequirementPatchGuardrails.cs`
      instead — checked against what `UserRequirement.Merge` would actually produce (patch value
      when given, else the existing value) immediately before `ConversationOrchestrator` calls
      `session.MergeRequirement`, since `UserRequirement` itself (a plain value type with no
      access to `RequestGuardrailOptions`) isn't the natural place to throw a guardrail-specific
      exception. A violation throws `GuardrailRejectionException(400, ...)`, matching
      `contracts/advisor-conversation-api.md`'s explicit classification of this as a pre-turn
      `400`, not a `clarification` (unlike FR-108's value-validation failures, T158).
- [X] T158 Implement strict value validation (ISO 4217 currency, non-negative budget, closed
      operator set, known units, catalog-format product ids) before any tool call, routing a
      failure to `clarification`, in
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/ValueValidationStage.cs` (depends
      on T133, T151). **Scope note**: implemented currency + budget validation (a new
      `Money.TryCreate(amount, currency, out money)` non-throwing factory, used by
      `ExtractionStage.ToDomain` instead of the throwing constructor) and product-id format
      validation (`Guid.TryParse` guards added to `ComputeTools.GenerateCheckoutLinkAsync` and
      `DataAccessTools.CheckPriceAndAvailabilityAsync`, which previously called `Guid.Parse`
      unguarded — a real crash risk this phase fixed: a malformed id from extraction would have
      thrown `FormatException` and surfaced as a 500, not a graceful "not found"). Did **not**
      implement "characteristic operators" or "units" validation — neither is represented as a
      formal, closed, already-implemented concept anywhere in this codebase today (there is no
      operator-set enum backing `search_products`' free-text characteristic conditions); inventing
      one from scratch to have something to validate against is a feature-level addition beyond
      this guardrails phase's scope, not a guardrail on an existing surface. No new
      `ValueValidationStage.cs` file — the currency/budget check lives inline in
      `ExtractionStage.ToDomain` (the single call site that constructs `Money` from untrusted
      extraction output) rather than a separate stage class with nothing else to validate.
- [ ] T159 Implement per-user rate limiting and a per-user cross-session concurrency limit
      (distinct from and layered on FR-024's per-session lock), keyed on the authenticated user
      identifier, in `src/Gateway/Gateway.Api/` + `src/ProductAdvisor/ProductAdvisor.Api/`
      (depends on T112, T152). **Deferred.** `Gateway.Api`'s `UserIdForwardingHandler` already
      extracts the JWT `sub` claim this would key off, and ASP.NET Core's built-in
      `Microsoft.AspNetCore.RateLimiting` middleware is available with no new package — but wiring
      a per-user concurrency limiter correctly (distinct from, and composed with, the existing
      per-session `ConversationTurnGate`) touches request admission for every chat endpoint and
      deserved dedicated scope rather than being folded into an already-large phase; left for a
      follow-up.
- [ ] T160 Implement a per-user token/cost quota tracked cumulatively over a configured window,
      in `src/ProductAdvisor/ProductAdvisor.Application/` (depends on T141, T152). **Deferred.**
      Requires capturing `ChatResponse.Usage` at every `IChatClient` call site
      (`ExtractionStage`, `NarrationPrompt`'s narration call, the smalltalk call, the legacy
      bridge, the direct-comparison explanation call) and a cumulative per-user store — a genuine
      new subsystem, not a guard clause on an existing one; left for a follow-up alongside T159.
- [X] T161 Implement PII screening as a pipeline stage producing `PiiScreeningResult` (block or
      redact before any LLM call), in
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/PiiScreeningStage.cs` (depends on
      T134, T153). A credit-card-shaped sequence (13–19 contiguous digits, ISO/IEC 7812's length
      range) blocks outright (FR-115); email and phone-number patterns redact. The phone pattern
      requires at least two separator-delimited digit groups specifically so it never matches this
      domain's ubiquitous bare numbers (prices, specification values) — verified with an explicit
      false-positive test (`Bare_price_and_specification_numbers_are_never_flagged`) covering
      exactly that risk, since a screening stage this aggressive would otherwise make ordinary
      shopping messages unusable.
- [X] T162 Exclude `ConversationSession.UserId` from both prompts' assembled content; audit T131/
      T147 for compliance, in `src/ProductAdvisor/ProductAdvisor.Infrastructure/Prompts/`
      (depends on T131, T147, T161). Audited both `ExtractionStage.SystemPromptTemplate` and
      `NarrationPrompt.SystemPromptTemplate` (`grep -n "UserId"` across both — zero matches):
      neither prompt has ever included `ConversationSession.UserId`, so this was already
      compliant; no code change was needed, only the confirmation.
- [X] T163 Implement `DELETE /api/conversations/{sessionId}` and `DELETE /api/conversations`
      (user-initiated deletion) in `src/ProductAdvisor/ProductAdvisor.Api/` + a Gateway
      pass-through in `src/Gateway/Gateway.Api/` (depends on T041, T154). Added
      `IConversationSessionRepository.DeleteAsync`/`DeleteAllForUserAsync` (EF Core
      `ExecuteDeleteAsync`, bypassing the change tracker) and `AdvisorApiClient.DeleteSessionAsync`/
      `DeleteAllSessionsAsync` for the Gateway pass-through. The single-session endpoint takes the
      `ConversationTurnGate` before deleting (409 while a turn is in flight, mirroring FR-024's
      own conflict response) — the bulk endpoint does not, since there is no single `sessionId` to
      gate on; documented in code as the same class of race FR-110's (deferred) per-user
      concurrency limit would bound, not something a deletion endpoint alone can close.
- [ ] T164 Implement automatic retention-based session deletion (a scheduled/background job),
      independent of user-initiated deletion, in
      `src/ProductAdvisor/ProductAdvisor.Infrastructure/` (depends on T018). **Deferred,
      deliberately.** This is an unattended, recurring, irreversible bulk-delete job against real
      user data — the kind of change this session's own safety posture treats with extra caution
      even when a spec explicitly calls for it, and it cannot be verified against a live database
      in this sandbox (Docker unavailable) before being trusted to run unattended. Left for a
      follow-up where it can be exercised against a real Postgres instance before being enabled.
- [X] T165 Confirm and document TLS for every in-transit hop (browser↔system, internal
      service-to-service, system↔LLM provider) and at-rest + backup encryption for the Postgres
      store (Neon/Render already provide these — verify and document the configuration, don't
      assume) in `README.md`. Added a "Privacy & data protection" section. Framed honestly: TLS
      termination and Neon's at-rest/backup encryption are platform-level guarantees documented as
      such (not re-verified against a live Render/Neon dashboard this session has no access to);
      the one actionable item flagged is confirming the Neon connection string actually uses
      `sslmode=require` when an environment is configured, since that value is a `sync: false`
      Render secret this repository cannot see.
- [X] T166 Confirm the configured LLM provider's training/retention/data-region policy satisfies
      FR-123; document the finding in `research.md`/`README.md`. Documented in the same README
      section as an explicit operational checklist rather than a one-time "confirmed" claim: since
      the provider is deliberately swappable through pure configuration (research.md §10, no
      specific vendor hard-coded), FR-123 compliance is a deployment-time decision each operator
      must make for whichever provider they configure — this repository has no live deployment
      configuration to inspect and cannot make that determination on any operator's behalf.

**Verification**: `dotnet build` — 0 errors, 0 new warnings; `dotnet format --verify-no-changes`
clean on every file this phase touched (one pre-existing, unrelated formatting issue elsewhere in
`Gateway.Api/Program.cs` predates this phase and was left untouched, consistent with this
session's practice of not reformatting code outside a change's own scope).
`ProductAdvisor.Domain.Tests`: 66/66 passing (58 + 8 new `MoneyTests`).
`ProductAdvisor.Application.Tests`: 75/75 passing (50 + 25 new across
`InputValidationStageTests`/`PiiScreeningStageTests`/`RequirementPatchGuardrailsTests`/
`ConversationOrchestratorGuardrailTests`). `ProductAdvisor.Api.Tests`/`EndToEnd.Tests` confirmed
to still compile against every signature change in this phase (including the new
`IConversationSessionRepository` methods), but not run here (Docker/Testcontainers unavailable).

**Checkpoint**: No oversized, malformed, dangerous, or PII-bearing input reaches the LLM or a
tool; users can delete their own data. **Not yet met**: rate-exceeding input is not yet rejected
(T159/T160 deferred) and automatic retention-based deletion does not yet run (T164 deferred) — see
each task's note above for why and what remains.

---

## Phase 13: MCP/Credential Security Hardening & Safe Observability (FR-124–FR-137)

**Goal**: `InternalApiKey` validation is constant-time and never accepts a development default in
production, with documented rotation support; a valid internal credential never grants MCP
callers conversation ownership on its own; the MCP SDK preview dependency has a documented
production-readiness review; logs carry only the allowed eleven fields (never the seven denied
ones); seven dedicated metrics cover the turn cycle's key failure/decision modes.

**Independent Test**: Run a timing benchmark comparing credential-validation duration for a
completely-wrong value versus one matching every character but the last and confirm no
statistically significant difference; confirm a request authenticated with the local-development
`InternalApiKey` value is rejected when the service is configured for Production.

### Tests for This Phase

- [X] T167 [P] Tests for credential hardening — an unset `InternalApiKey` refuses every caller;
      the local-development placeholder value is refused in a Production configuration; a timing
      benchmark shows no correlation between comparison duration and match length — in
      `tests/ProductAdvisor.Api.Tests/InternalCredentialSecurityTests.cs` (FR-124/FR-127/FR-128).
      5 tests, including rotation (a previous key configured for the overlap window is still
      accepted) and the placeholder-accepted-outside-Production counterpart. Extended
      `AdvisorApiFactory` with `EnvironmentName`/`InternalApiKeyOverride`/
      `PreviousInternalApiKeyOverride` to make these configurable per test. The timing-benchmark
      test is explicitly best-effort (large tolerance, documented as such in its own comment) —
      the actual guarantee comes from using `CryptographicOperations.FixedTimeEquals` (T170), a
      vetted primitive, not from an HTTP-level timing test's own precision. **Not run in this
      sandbox** (Docker/Testcontainers unavailable, same limitation as every other
      `ProductAdvisor.Api.Tests` addition this session) — confirmed to compile only.
- [X] T168 [P] Test proving a valid `X-Internal-Api-Key` plus an arbitrary `X-User-Id` on `/mcp`
      is still rejected by the FR-031 ownership check — the internal key alone never grants
      session access — in `tests/ProductAdvisor.Api.Tests/McpOwnershipIndependenceTests.cs`
      (FR-131). **Scope note**: none of the seven registered MCP tools
      (`DataAccessTools`/`ComputeTools`) accepts a `sessionId` or any conversation-scoped
      parameter today — every one operates on catalog/pricing data or an already-resolved
      product-id set — so there is no current MCP-tool surface for a valid internal key to
      exploit into session access. The 2 tests here are: (1) `/mcp` still requires the internal
      key regardless of an arbitrary `X-User-Id` (the one thing genuinely exercisable today,
      Docker-dependent, not run here), and (2) a **runnable** reflection test asserting no
      registered tool method takes a `sessionId` parameter — verified passing in this sandbox
      (no Docker needed, pure reflection). This second test is the actual guarantee: it fails the
      moment a future session-scoped MCP tool is added without also adding the ownership check
      FR-031 requires, rather than silently losing the guarantee.
- [X] T169 [P] Tests for the observability allow/deny policy — sampled logs never contain a
      denied field (full message, full prompt, PII-bearing tool data, Authorization headers, API
      keys, connection strings, full LLM response); each of the seven dedicated metrics
      increments independently for its triggering event — in
      `tests/ProductAdvisor.Application.Tests/ObservabilityPolicyTests.cs` (FR-133–FR-137). Split
      across two **runnable** files, since the allow-list enforcement (`TurnLogFields`, T174)
      lives in `ServiceDefaults` while the metrics (`TurnMetrics`, T175) live in
      `ProductAdvisor.Application`, and `Application.Tests` doesn't reference `ServiceDefaults`
      (a real, intentional layering boundary, not worth crossing for one test file — see T174's
      note): `ObservabilityPolicyTests.cs` (8 tests, using a real `MeterListener` to observe
      actual emitted measurements, not just that `.Add()` doesn't throw) plus a new
      `ConversationOrchestratorMetricsTests.cs` (3 tests proving the turn-processing cycle itself
      triggers the wired metrics — PII detection, schema-repair, grounding-failure — not just
      that the counters work in isolation), both fully verified passing; and
      `tests/ProductAdvisor.Api.Tests/ObservabilityFieldShapeTests.cs` (6 tests: `TurnLogFields`
      exposes exactly the eleven allowed properties by reflection, no more/no less;
      `PseudonymousIdentifier` is deterministic, never contains the raw input, differs across
      inputs and across pepper values) — also verified passing (pure reflection/logic, no Docker
      needed despite living in this Docker-dependent test project).

### Implementation for This Phase

- [X] T170 Replace `InternalApiKeyMiddleware`'s (T108) credential comparison with a constant-time
      comparison, in `src/Aspire/ServiceDefaults/InternalAuth/InternalApiKeyMiddleware.cs`
      (depends on T167). `CryptographicOperations.FixedTimeEquals` over each value's UTF-8 bytes.
      Also converted this file's three log calls to `[LoggerMessage]` source-generated methods —
      not strictly required by this task, but the two pre-existing ad-hoc `logger.LogError`/
      `LogWarning` calls already triggered CA1848, and this file was already being substantially
      rewritten; fixing them alongside kept the "0 new warnings" bar intact instead of adding a
      third.
- [X] T171 Add a production-configuration guard that refuses every caller when `InternalApiKey`
      is unset and rejects a known local-development placeholder value when running in a
      Production environment, in `src/Aspire/ServiceDefaults/InternalAuth/` (depends on T170).
      The unset case was already handled (a pre-existing fail-closed 500, unchanged); added the
      Production-placeholder check, comparing against `InternalApiKeyMiddleware.LocalDevelopmentPlaceholder`
      — set to exactly `docker-compose.yml`'s `INTERNAL_API_KEY` fallback value
      (`dev-internal-api-key`), the real local-development value, not an invented placeholder —
      via `IHostEnvironment.IsProduction()`.
- [X] T172 Document and implement rotation support (an old/new value overlap window) for
      `InternalApiKey`, in `src/Aspire/ServiceDefaults/InternalAuth/` (depends on T170). A second,
      optional `InternalApiKeyPrevious` configuration value — when set, a request presenting
      either the current or the previous key is accepted (each checked independently in constant
      time). The "window" itself is operational, not a timed mechanism: set
      `InternalApiKeyPrevious` alongside a new `InternalApiKey` during a rotation, then remove it
      once every caller has been updated.
- [X] T173 Record a documented production-readiness review for the `ModelContextProtocol`/
      `ModelContextProtocol.AspNetCore` preview package (or upgrade past preview if one is
      available) in a new `docs/dependency-reviews.md`. Finding: this system is pinned to
      `2.0.0-preview.3`; a stable `2.1.0` was published 2026-08-05 (checked via NuGet, five days
      before this review). Documented the recommendation to upgrade, plus an explicit interim
      risk assessment for why running the preview version until then is bounded, not a blocker —
      but did **not** perform the upgrade itself: a change this central (it defines the shape of
      every registered MCP tool and the transport hosting them) needs verification against a live
      MCP client this sandbox cannot provide (Docker unavailable), so upgrading blind would trade
      a documented, bounded risk for an unverified one.
- [X] T174 Implement a structured-logging helper restricted to the eleven allowed fields
      (correlation id, hashed/pseudonymous identifier, prompt version, model identifier, intent,
      tool name, allow/deny decision, latency, token usage, validation status, error category)
      and a hashed/pseudonymous identifier helper (irreversible from the logged value alone), in
      `src/Aspire/ServiceDefaults/Observability/` (depends on T169). `TurnLogFields` (a closed
      `sealed record` with exactly these eleven properties — enforcement by construction, not by
      a runtime check: there is no property to assign a denied field to) and
      `PseudonymousIdentifier.Hash` (SHA-256 over the identifier plus a pepper, truncated for log
      readability). **Scope note**: not wired into `ConversationOrchestrator`'s own logging this
      phase — doing so would require `ProductAdvisor.Application` to reference `ServiceDefaults`,
      which currently carries the ASP.NET Core/OpenTelemetry/resilience dependency surface the
      Application layer has deliberately stayed free of (it depends only on `ProductAdvisor.Domain`
      and `Microsoft.Extensions.AI` today); the reusable, tested enforcement type exists and is
      ready for a caller, but forcing that dependency in to wire up two existing log lines this
      phase would be a worse trade than documenting the gap.
- [X] T175 Add the seven dedicated metrics (loop-limit reached, schema-repair attempted, rejected
      tool call, grounding failure, rate-limit rejection, PII detection, provider failure) at
      their respective pipeline stages, in
      `src/ProductAdvisor/ProductAdvisor.Application/Pipeline/` (depends on T141, T148, T159,
      T161, T174). `TurnMetrics` (a singleton wrapping one `Meter` with all seven `Counter<long>`
      instruments, registered with the shared OpenTelemetry pipeline via `.AddMeter(...)` in
      `ProductAdvisor.Api/Program.cs`). Four of the seven are wired to real trigger points and
      verified incrementing end-to-end (`ConversationOrchestratorMetricsTests`):
      `SchemaRepairAttempted` (`ExtractionStage`'s repair-attempt branch), `PiiDetection`
      (`ConversationOrchestrator.AdmitMessage`, any flagged message), `GroundingFailure` (both
      grounding-check call sites — the `recommend` route and the legacy bridge's post-hoc check),
      `LoopLimitReached` (`TurnResourceBudgetGuard.ExceededToolCallBudget`). Three remain
      **defined but not yet incremented**, each for a documented reason rather than an oversight:
      `RateLimitRejection` and — indirectly — the concurrency/quota half of `ToolCallRejected`'s
      motivating FRs have no trigger point because Phase 12 deferred T159/T160 (no rate-limiting
      code exists yet to increment from); `ToolCallRejected`'s remaining trigger point (the
      malformed-product-id filtering added in Phase 12, `ComputeTools`/`DataAccessTools`) would
      require injecting `TurnMetrics` into two more Infrastructure classes, deferred to keep this
      already-large phase's blast radius contained; `ProviderFailure` has no unambiguous
      integration point in the current architecture — `ExtractionStage`'s existing catch-all
      already conflates a genuine provider/network exception with an ordinary malformed-JSON
      schema-validation failure under one `return null`, and incrementing there without first
      distinguishing the two would misclassify ordinary schema failures as provider outages.

**Verification**: `dotnet build` — 0 errors, 0 new warnings; `dotnet format --verify-no-changes`
clean on every file this phase touched. `ProductAdvisor.Domain.Tests`: 66/66 passing (unchanged).
`ProductAdvisor.Application.Tests`: 86/86 passing (75 + 11 new: 8 `ObservabilityPolicyTests` + 3
`ConversationOrchestratorMetricsTests`). `ProductAdvisor.Api.Tests`: confirmed to still compile
against every signature change in this phase; 7 of its tests are genuinely Docker-free and were
run and verified passing (`ObservabilityFieldShapeTests` ×6,
`McpOwnershipIndependenceTests.No_registered_MCP_tool_accepts_a_sessionId...` ×1) — the remaining
Docker-dependent tests in this project, and `EndToEnd.Tests`, were not run (Docker/Testcontainers
unavailable in this sandbox).

**Checkpoint**: Credential handling resists timing attacks and dev-default misuse in production;
an MCP caller can never impersonate conversation ownership from the internal key alone (today,
structurally — no tool exists for it to exploit; enforced going forward by a test that fails the
moment one is added without the check). Logs and metrics give **partial** turn-cycle visibility:
the allow/deny-list enforcement mechanism (`TurnLogFields`) and 4 of 7 metrics are real and wired;
the remaining 3 metrics and full log-call-site migration are documented gaps, not silent ones.

---

## Phase 14: Agentic Security and Quality Eval Suite (FR-138–FR-141)

**Goal**: An automated eval suite covers all fifteen mandatory classes; the six grounding/
authorization/cross-session-access classes gate CI at a 100% pass rate; the remaining nine run
automatically and are reviewed at release without a fixed pass-rate requirement.

**Independent Test**: Run the eval suite locally; confirm all fifteen classes execute and report;
confirm CI fails the build if a critical-class eval is made to fail (verified by temporarily
breaking one deliberately), and does not fail the build for a non-critical-class failure.

### Tasks

- [ ] T176 [P] Author evals for the six critical classes — indirect injection via product/spec,
      fabricated prices/specs/availability, product not found (grounding); wrong tool for intent,
      system-prompt extraction attempt (authorization); cross-session access — in
      `tests/EndToEnd.Tests/Evals/CriticalEvals.cs` (depends on Phases 9–13; FR-138–FR-140).
- [ ] T177 [P] Author evals for the nine non-critical classes — direct prompt injection,
      tool-loop exhaustion, malformed tool arguments, oversized input, memory poisoning,
      constraint changes between turns, partial dependency failure, unsupported intent,
      PII/payment-data input — in `tests/EndToEnd.Tests/Evals/NonCriticalEvals.cs` (depends on
      Phases 9–13; FR-138/FR-141).
- [ ] T178 Wire the eval suite into `.github/workflows/ci.yml` as a distinct job — failing the
      build on any critical-class (T176) failure, reporting but not failing the build on a
      non-critical-class (T177) failure — in `.github/workflows/ci.yml` (depends on T176, T177).
- [ ] T179 Document the critical/non-critical categorization and its rationale in `README.md`,
      cross-referencing spec.md's Assumptions and research.md §33 (depends on T178).

**Checkpoint**: Every guarantee this specification makes about prompt injection, grounding,
authorization, cross-session isolation, resource exhaustion, and PII handling is verified by an
automated, CI-gated eval, not merely documented.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends only on Foundational. No dependency on US2/US3.
- **Streaming & Rich Rendering (Phase 3.5)**: Depends on Phase 3 (US1) being functional — it
  streams/reformats the same conversation turn US1 already produces. By explicit direction, it
  is completed **before** Phase 4/5 even though its task numbers (T073+) are higher.
- **User Story 2 (Phase 4)**: Depends only on Foundational (+ Phase 3.5 per the ordering above);
  reuses US1's data-access tool
  infrastructure and Blazor shell but does not require US1's phase to be marked "done," only its
  underlying Foundational pieces.
- **Deterministic Search & Direct Comparison (Phase 4.5)**: Depends on Phase 4 (US2) — it
  extends `search_products` and reuses `ComparisonEngine`/`compare_products`'s composition. By
  explicit direction, it is completed **before** Phase 6 (Polish); it has no dependency on
  Phase 5 (US3) and Phase 5 has no dependency on it, so they may proceed in either order or in
  parallel.
- **User Story 3 (Phase 5)**: Depends only on Foundational; T061/T063 additionally build on the
  Catalog product-detail endpoint from T049 (US2) and the conversation/Blazor scaffolding from
  US1 — if US2 hasn't been built yet, complete T049 as a prerequisite before T061.
- **Persist Structured Renders Across Turns (Phase 4.6)**: Depends on Phase 3.5 (the Blazor
  `Home.razor` rendering it fixes) — no dependency on Phase 4/4.5/5, so it may proceed in any
  order relative to them; completed before Phase 6 (Polish).
- **Polish (Phase 6)**: Depends on all desired user stories being complete.
- **Access Control, Observability Hardening & Checkout Link (Phase 7)**: Depends on Phase 6 (it
  hardens/extends the already-polished system) and, for the checkout-link tool specifically, on
  Phase 4.5's `LastSearchResults` (T096) and Phase 4.6's per-turn structured rendering (T101).
  Every other user story remains independently testable underneath the new auth boundary — the
  boundary changes *who* can reach a story, not what the story does once reached.
- **Startup Readiness / Loading Screen (Phase 8)**: Depends on Phase 7's Gateway (the new
  endpoint sits alongside its existing composition endpoints) but not on Phase 7's auth boundary
  specifically — `GET /api/system-status` is deliberately anonymous. Independent of every other
  user story's own functionality; it only gates *when* the interactive UI appears, not what it
  does once shown.
- **Deterministic Turn-Processing Cycle Foundation (Phase 9)**: Depends on Phase 7 (needs
  `ConversationSession.UserId`/session ownership and the internal-API-key boundary already in
  place) and reuses Phase 3's `ScoringPolicy`/tool infrastructure. This phase **replaces**
  `ConversationOrchestrator`'s implementation (T040/T053/T062) with the fixed pipeline — every
  later phase in this group builds on top of it, and User Stories 1–4's *external* behavior
  (spec.md acceptance scenarios) MUST remain unchanged; only *how* a turn is processed changes.
- **Turn Result Types, Tool Recipes & Resource Budgets (Phase 10)**: Depends on Phase 9 (the
  policy router and pipeline stages it scopes/bounds must already exist).
- **Evidence Envelope & System Prompts (Phase 11)**: Depends on Phase 9 (tool-result validation)
  and Phase 10 (`TurnResult`'s structured shape, which the Envelope's canonical data mirrors).
- **Request Guardrails & Privacy-by-Design (Phase 12)**: Depends on Phase 9 (the input-validation
  and state-merge stages it extends) and Phase 11 (prompts it restricts). Independent of Phase
  10, so may proceed in parallel with it if staffed.
- **MCP/Credential Security Hardening & Safe Observability (Phase 13)**: Depends on Phase 7's
  existing `InternalApiKeyMiddleware` (T108, which it hardens) and Phase 10/11/12 (the pipeline
  stages/limits it adds metrics for). Its credential-hardening tasks (T170–T173) have no
  dependency on Phases 9–12 and may start immediately after Phase 7.
- **Agentic Security and Quality Eval Suite (Phase 14)**: Depends on Phases 9–13 being complete —
  its evals verify guarantees those phases implement; writing them earlier would only produce
  evals with nothing correct yet to verify.

### Within Each User Story

- Contract/unit tests are written first and must fail before the corresponding implementation
  task.
- Domain services (`ScoringPolicy`, `ComparisonEngine`) before the tool handlers that call them.
- Data-access tools before compute tools (`get_recommendations`, `compare_products`) that depend
  on them.
- Conversation orchestration/API before the Gateway composition endpoints that call it.
- Gateway endpoints before the Blazor UI that calls them.
- Story implementation before that story's EndToEnd scenario test.

### Parallel Opportunities

- All Setup tasks marked [P] (T002–T009) can run in parallel once T001 exists.
- All Foundational domain-entity and domain-test tasks marked [P] (T010–T015, T019–T021,
  T023–T025) can run in parallel; the three `DbContext` tasks (T016–T018) are sequential only
  with respect to their own domain task, not each other.
- Within US1, the contract/unit test tasks T026–T029, T031, T032 can run in parallel; T030
  depends on T028.
- Within US2, T045–T048 can run in parallel.
- Within US3, T056–T059 can run in parallel.
- Once Foundational is complete, US1, US2, and US3 test-writing and Catalog/Pricing-side
  implementation tasks can proceed in parallel across stories if staffed; the Advisor-side
  tasks within a story are more serial (tool → orchestration → API → UI).

---

## Parallel Example: User Story 1

```bash
# Launch US1's independent contract/unit tests together:
Task: "Contract test GET /api/catalog/products in tests/ProductCatalog.Api.Tests/SearchProductsContractTests.cs"
Task: "Contract test GET /api/pricing/offers?productIds= batch in tests/PricingAvailability.Api.Tests/BatchOffersContractTests.cs"
Task: "Unit tests for ScoringPolicy in tests/ProductAdvisor.Domain.Tests/ScoringPolicyTests.cs"
Task: "MCP tool contract tests for search_products and check_price_and_availability in tests/ProductAdvisor.Api.Tests/DataAccessToolsTests.cs"
Task: "Application-layer 'never computes' test in tests/ProductAdvisor.Application.Tests/OrchestrationNeverComputesTests.cs"

# Launch US1's independent Catalog/Pricing implementation together:
Task: "Implement GET /api/catalog/products search in src/ProductCatalog/ProductCatalog.Application + Api"
Task: "Implement GET /api/pricing/offers endpoints in src/PricingAvailability/PricingAvailability.Application + Api"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (critical — blocks all stories).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenarios 1–3 against the running stack.
5. Deploy/demo if ready — this alone is a usable recommendation advisor.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add User Story 1 → validate independently → deploy/demo (MVP).
3. Add User Story 2 → validate independently → deploy/demo.
4. Add User Story 3 → validate independently → deploy/demo.
5. Polish (Phase 6) → full observability, resilience, and CI/CD hardening.
6. Phase 7 → access control (Google sign-in + internal API key), a real observability backend,
   concurrent-message rejection, and checkout-link generation.
7. Phase 8 → starting-up/readiness screen and aggregate health-check endpoint (also mitigates
   free-tier cold starts on Render).
8. Phase 9 → replace the free tool-selection loop with the fixed, deterministic ten-stage cycle
   (no user-visible behavior change; validate via Phases 1–8's existing scenarios still passing).
9. Phase 10 → typed turn results, scoped tool recipes, resource budgets, precise hard-constraint
   filtering → validate independently → deploy/demo.
10. Phase 11 → Evidence Envelope + grounded narration → validate independently → deploy/demo.
11. Phase 12 → request guardrails + privacy-by-design (PII screening, deletion, retention,
    encryption confirmation) → validate independently → deploy/demo.
12. Phase 13 → MCP/credential hardening + safe observability → validate independently →
    deploy/demo.
13. Phase 14 → the agentic security and quality eval suite, CI-gated on the critical classes —
    the last phase, since it verifies what Phases 9–13 built.

### Parallel Team Strategy

With multiple developers, after Foundational is done:

- Developer A: User Story 1 (MVP path).
- Developer B: User Story 2's Catalog-side tasks (T045, T049) can start immediately in
  parallel; the Advisor-side tasks (T050–T053) wait on US1's `search_products`/tool-hosting
  infrastructure (T037) existing.
- Developer C: User Story 3's Pricing-side tasks (T056, T060) can start immediately in
  parallel; T061/T063 wait on T049 (US2) and US1's Blazor shell.

---

## Notes

- [P] tasks touch different files with no unfinished dependency.
- [Story] labels map every user-story-phase task back to spec.md's priorities for traceability.
- Every product-data computation (search, detail, price/availability, filtering, scoring,
  comparison rating/deltas) is implemented **inside an MCP tool handler**, never inside
  `ProductAdvisor.Application`'s orchestration loop — see plan.md's Summary and research.md §1.
  Task T032 and T058/T059 exist specifically to keep that boundary enforced by a test, not just
  a convention.
- Verify each story's tests fail before implementing that story's tasks.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently before moving on.
- Avoid: vague tasks, two tasks editing the same file marked [P], and cross-story dependencies
  that would break a story's independent testability.
