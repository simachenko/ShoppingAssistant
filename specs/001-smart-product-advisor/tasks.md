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

## Phase 5: User Story 3 - Check Price, Availability, and Specific Characteristics (Priority: P3)

**Goal**: A shopper asks a targeted question about one product's price, availability, or
characteristics — including follow-ups about a product already shown — and gets a verified
answer or an honest "cannot verify"/"not found."

**Independent Test**: Ask about a named product's price/availability/characteristic and confirm
the answer matches seeded data or clearly states it can't be verified; ask about a
nonexistent product and confirm an honest "not found" response.

### Tests for User Story 3

- [ ] T056 [P] [US3] Contract test for `GET /api/pricing/offers/{productId}` distinguishing
      `404` (no record) from `200` `Unknown` availability in
      `tests/PricingAvailability.Api.Tests/SingleOfferContractTests.cs`.
- [ ] T057 [P] [US3] Contract test for Gateway `GET /api/products/{productId}` — concurrent
      Catalog+Pricing merge, partial success when Pricing is down — in
      `tests/Gateway.Api.Tests/ProductDetailContractTests.cs`.
- [ ] T058 [P] [US3] Test that `get_product_details` returning `found:false` results in the
      conversation API relaying an honest "not found," never an invented product, in
      `tests/ProductAdvisor.Application.Tests/NotFoundHonestyTests.cs`.
- [ ] T059 [P] [US3] Test for follow-up questions about a previously recommended/compared
      product, resolved via `ConversationSession.LastRecommendation`, in
      `tests/ProductAdvisor.Application.Tests/FollowUpQuestionTests.cs`.

### Implementation for User Story 3

- [ ] T060 [US3] Implement `GET /api/pricing/offers/{productId}` single-offer endpoint (404 vs.
      Unknown distinction) in `src/PricingAvailability/PricingAvailability.Application/` +
      `.Api/` (depends on T017).
- [ ] T061 [US3] Implement Gateway `GET /api/products/{productId}` — concurrent Catalog+Pricing
      calls, partial-success handling — in `src/Gateway/Gateway.Api/` (depends on T049, T060,
      T023).
- [ ] T062 [US3] Wire follow-up question handling into the conversation orchestration loop —
      resolve against `LastRecommendation`/comparison context before choosing a tool — in
      `src/ProductAdvisor/ProductAdvisor.Application/` (depends on T040, T053).
- [ ] T063 [US3] Implement a Blazor single-product detail panel (price/availability + verified
      flags, "not found" state), linked from recommendation/comparison views, in
      `src/WebApp/WebApp.Blazor/` (depends on T061, T054).
- [ ] T064 [US3] EndToEnd test covering quickstart Scenario 5 (verified fact lookup + not-found
      honesty) in `tests/EndToEnd.Tests/ProductLookupScenarioTests.cs` (depends on T063).

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements spanning all three stories.

- [ ] T065 [P] EndToEnd resilience test covering quickstart Scenario 6 (Pricing outage → honest
      partial recommendation, no `5xx`) in
      `tests/EndToEnd.Tests/PartialFailureResilienceTests.cs` (constitution Principle V).
- [ ] T066 [P] Assert distributed trace/correlation propagation (Gateway → Advisor → MCP tool
      call → Catalog/Pricing → LLM call share one trace/correlation id) via an OpenTelemetry
      test exporter in `tests/EndToEnd.Tests/ObservabilityTests.cs` (constitution Principle VI).
- [ ] T067 [P] Extend `.github/workflows/ci.yml` with the docker-compose–based EndToEnd stage
      and the Render deploy trigger (native git auto-deploy per research.md §9, or the
      documented deploy-hook fallback).
- [ ] T068 [P] Finalize `render.yaml` with real environment-variable bindings (Neon per-service
      connection strings, LLM provider key/endpoint) referencing Render's secret management —
      no values committed.
- [ ] T069 [P] Add a lightweight performance check asserting Catalog/Pricing p95 < 300ms and a
      full US1 turn's non-LLM portion stays within plan.md's Performance Goals.
- [ ] T070 [P] Run `dotnet format` plus analyzer fixes across the solution; confirm CI's
      lint/type-check gate is green (constitution Principle I).
- [ ] T071 [P] Write the repository root `README.md`, pointing to quickstart.md, plan.md, and
      the Aspire/docker-compose run instructions.
- [ ] T072 Manually walk through quickstart.md end-to-end once; file any gaps found as follow-up
      tasks rather than leaving them undiscovered.

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
- **User Story 3 (Phase 5)**: Depends only on Foundational; T061/T063 additionally build on the
  Catalog product-detail endpoint from T049 (US2) and the conversation/Blazor scaffolding from
  US1 — if US2 hasn't been built yet, complete T049 as a prerequisite before T061.
- **Polish (Phase 6)**: Depends on all desired user stories being complete.

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
