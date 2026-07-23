# Implementation Plan: Smart Product Advisor

**Branch**: `001-smart-product-advisor` | **Date**: 2026-07-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-smart-product-advisor/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

A conversational Product Advisor lets a shopper describe a need in natural language, ask
clarifying questions when essential details (category, budget) are missing, and produce
explainable, grounded recommendations and comparisons. The system is split into three
independently deployable DDD bounded contexts — **Product Catalog** (product/spec data),
**Pricing and Availability** (offers, price, stock), and **Product Advisor** (MCP server +
LLM orchestration, conversation state) — fronted by an ASP.NET Core API Gateway/BFF and a
Blazor web chat UI.

The Advisor service is deliberately a **semantic UI, not a business-logic engine**: its own
orchestration code (Application layer) only drives the LLM conversation — feeding it the user
message, the available tools, and the running session state, then relaying the LLM's next
message or tool call. It never computes anything about products itself. **Every operation on
product data — search, detail lookup, price/availability checks, budget filtering,
recommendation scoring, and product comparison (including per-product ratings and cross-product
deltas) — is exposed exclusively as an MCP tool with a deterministic C# implementation.** The
LLM's job is to decide which tools to call and to narrate/elaborate on their already-computed
output; it is never the source of a rating, a delta, a match, or a score. This makes grounding
(constitution Principle II) structural rather than prompt-dependent: there is exactly one place
— a specific, unit-tested tool implementation — that can produce any given fact or number, and
it is never the LLM.

The advisor's replies stream to the user progressively over SSE as the LLM generates them
(FR-015), and are rendered with real structure — Markdown for the LLM's narration, actual
HTML lists/tables (built by the UI, not the LLM) for the structured facts (FR-016/FR-017) —
detailed in research.md §11–§12.

Two capabilities are deliberately reachable **outside** the LLM's tool-selection decision, so
the highest-value operations don't depend on the model choosing to invoke them correctly
(FR-018–FR-022, research.md §13–§14): (1) product search accepts explicit, structured filters —
category, price range, and characteristic conditions — so retrieval narrows deterministically in
the data layer instead of depending on the LLM inferring the right search terms; (2) product
comparison exposes a direct, stateless HTTP endpoint that computes rating/delta/ranking from a
known product-id set without requiring a conversational turn at all — the LLM's only optional
role afterward is a narrow, constrained call that narrates the already-computed table, never
alters it. The LLM still legitimately uses **retrieval** tools (search, category lookup) to
ground itself in specific product ids from natural language — that is standard tool-use for
information-gathering, not computation — but the arithmetic (deltas, ratings, filtering) is never
its job, on either path. Within conversation, the Advisor also keeps a capped, per-session memory
of the most recently shown search/recommendation/comparison result (`LastSearchResults`) so
ordinal follow-ups ("the first two", "the cheaper one") resolve against known identifiers instead
of asking the LLM to reconstruct them from prior prose.

## Technical Context

**Language/Version**: C# 13 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: Entity Framework Core 10 + Npgsql (Catalog, Pricing); official
`ModelContextProtocol` C# SDK + `ModelContextProtocol.AspNetCore` (Advisor MCP server);
`Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.OpenAI` (or provider-specific connector)
for the swappable chat/LLM client; `Microsoft.Extensions.Http.Resilience` for timeouts/retry/
circuit-breaking on all outbound HTTP; YARP (`Yarp.ReverseProxy`) for the Gateway/BFF; Blazor
Web App (Interactive Server render mode) for the UI; .NET Aspire (`Aspire.Hosting`,
`Aspire.Hosting.PostgreSQL`) for local orchestration/service discovery/dashboard;
OpenTelemetry (`OpenTelemetry.Extensions.Hosting`, OTLP exporter) + `Microsoft.Extensions.Logging`
structured logging for tracing; ASP.NET Core's built-in Server-Sent Events support (or a manual
`text/event-stream` writer, confirmed at implementation time) for streaming advisor replies
(research.md §11); `Markdig` + an HTML allow-list sanitizer for rendering the LLM's narration
text safely (research.md §12) — structured facts are rendered by the UI's own Razor markup, not
through Markdown at all.

**Storage**: PostgreSQL. One managed Postgres instance for the free/demo environment (Neon),
one dedicated schema and one least-privileged database role per service (`catalog`, `pricing`);
each service owns its own EF Core `DbContext`, migration history, and schema — no shared
tables, no cross-schema queries. The Advisor service is stateful only for conversation history
(its own schema) and does not persist a shadow copy of product/price data — it fetches Catalog
and Pricing data per request through their HTTP APIs.

**Testing**: xUnit across all layers. Domain unit tests (no I/O). Application-layer tests with
fake/stub infrastructure ports. API contract tests via `WebApplicationFactory<TProgram>`
in-process `TestServer`. Infrastructure/integration tests against a real Postgres via
Testcontainers. Full cross-service recommendation/comparison scenarios run against
docker-compose–orchestrated services as a separate CI integration stage.

**Target Platform**: Linux containers. Each service ships its own Dockerfile image. Local dev
via .NET Aspire AppHost (primary) with an equivalent `docker-compose.yml` maintained for CI
validation and non-Aspire environments. Production: Render (container hosting) + Neon
(managed Postgres).

**Project Type**: Backend microservices (3 bounded-context APIs) + API Gateway/BFF + Blazor
web frontend — multi-project .NET solution, one Docker image per deployable service.

**Performance Goals**: Catalog/Pricing read endpoints: p95 < 300 ms (excluding cold start on
free-tier hosting). Advisor conversation turns that only call Catalog/Pricing tools (no LLM
clarification loop beyond one call): p95 < 3 s end-to-end, dominated by the LLM call latency,
which is out of this system's direct control. Independent Catalog/Pricing lookups for the same
candidate set MUST be issued concurrently (`Task.WhenAll`) rather than sequentially. Streamed
turns (SC-008): first narration token visible to the user within 3 s even when the full answer
takes longer. The direct comparison endpoint (FR-018) completes without any LLM call at all when
narration isn't requested, so its latency is bounded by Catalog/Pricing lookups only. Filtered
search (FR-020) narrows by category via an indexed SQL predicate before any characteristic
filtering runs, so cost scales with the category's size, not the whole catalog's.

**Constraints**: Must run within free/low-cost tiers: Render free web services (cold starts
after idle acceptable for a demo), Neon free-tier Postgres (limited connections — use Neon's
pooled connection string), and an LLM provider with a free API tier (rate-limited — resilience
policies must treat 429s as retryable-with-backoff, not fatal). No message broker/queue
infrastructure introduced in this version (see research.md for the async-messaging decision).

**Scale/Scope**: Demonstration scale — hundreds to low thousands of products across a handful
of categories, single-user conversational sessions (no multi-tenant concerns), not
production e-commerce traffic volumes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Design response | Status |
|---|---|---|
| I. Code Quality & Maintainability | Every service is split into Domain / Application / Infrastructure / API projects with only interface-level coupling; config/secrets live in environment variables and .NET user-secrets/Aspire parameters, never in source; `dotnet format` + analyzers + `dotnet test` run in CI as a merge gate. | PASS |
| II. Reliable & Grounded Behavior | Product facts/prices/availability, and every derived number (score, rating, delta), are produced only inside deterministic MCP tool implementations that call Catalog/Pricing; the LLM only invokes tools and narrates their literal output, so it structurally cannot fabricate or calculate a fact, a rating, or a delta — the Advisor's own conversation-orchestration code never computes one either. Streaming (research.md §11) only staggers delivery of that same narration text; structured facts are always sent complete in the final `result` event, never as a partial/guessed value mid-stream. Comparison (FR-018) and search filtering (FR-020) go further: the deterministic computation is reachable through a plain endpoint independent of the LLM's tool-selection decision, and any narration attached to it (FR-019) is generated by a separate, constrained call whose only input is the already-computed data, so it cannot alter what it describes. | PASS |
| III. Testing Standards | xUnit unit tests for every domain rule (scoring, comparison delta/rating math) exercised directly and through its owning tool, contract tests per service API and per MCP tool, integration tests with Testcontainers, and cross-service recommendation/comparison scenarios in a dedicated CI stage; all required green before merge. | PASS |
| IV. Consistent UX | Comparison criteria and values are computed once, inside the `compare_products` tool, from the shared set of category attributes and applied identically to every product in the call — the LLM cannot selectively omit or reorder them because it never computes them; `ConversationSession` aggregate is the single place budget/currency/units/requirements are held so they cannot silently drift across turns. Category names and characteristics are resolved through a dedicated lookup (FR-021) rather than guessed, and `ConversationSession.LastSearchResults` (FR-022) gives every ordinal follow-up a single, consistent set of identifiers to resolve against. | PASS |
| V. Performance & Resilience | All outbound HTTP (service-to-service and to the LLM provider) goes through `Microsoft.Extensions.Http.Resilience` standard handlers (timeout, bounded retry+backoff, circuit breaker); independent Catalog/Pricing calls run concurrently; partial failures (e.g., Pricing down) degrade to an honest partial answer instead of failing the whole turn. SSE streaming (research.md §11) improves perceived responsiveness and still guarantees a complete final response — falling back to a buffered call if the provider/connection can't sustain a stream — rather than leaving the user with a stuck or truncated turn. | PASS |
| VI. Observability & Safe Evolution | OpenTelemetry tracing + structured logs across Gateway, MCP tool calls, services, EF Core, and LLM calls, correlated via W3C `traceparent` propagated automatically by ASP.NET Core/HttpClient instrumentation (Aspire `ServiceDefaults`); prompts/tool schemas/recommendation rules are plain version-controlled C#/config, not runtime-mutable state. | PASS |

No unjustified violations were identified; the **Complexity Tracking** table below is
intentionally empty. The multi-service topology itself is not a constitution violation — it is
an explicit, user-mandated architectural requirement (DDD bounded contexts, independent
deployability), not a choice made for its own sake by this plan.

## Project Structure

### Documentation (this feature)

```text
specs/001-smart-product-advisor/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── ProductAdvisor.sln
│
├── Aspire/
│   ├── AppHost/                            # .NET Aspire orchestrator (local dev/debug entrypoint)
│   └── ServiceDefaults/                    # Shared OTel, health checks, resilience, service discovery
│
├── ProductCatalog/
│   ├── ProductCatalog.Domain/               # Product, Category, Brand, Specification (entities/VOs, rules)
│   ├── ProductCatalog.Application/          # Use cases: SearchProducts (incl. structured characteristic
│   │                                          # filters), GetProductDetails, GetCategoryByName; port interfaces
│   ├── ProductCatalog.Infrastructure/        # EF Core DbContext ("catalog" schema), repositories
│   └── ProductCatalog.Api/                  # HTTP API incl. POST .../products/search,
│                                                # GET .../categories?name= (+ Dockerfile)
│
├── PricingAvailability/
│   ├── PricingAvailability.Domain/          # Offer, Price, Discount, StockStatus (entities/VOs, rules)
│   ├── PricingAvailability.Application/      # Use cases: GetOffer, GetOffersBatch; port interfaces
│   ├── PricingAvailability.Infrastructure/   # EF Core DbContext ("pricing" schema), repositories
│   └── PricingAvailability.Api/              # HTTP API (+ Dockerfile)
│
├── ProductAdvisor/
│   ├── ProductAdvisor.Domain/                # Pure deterministic algorithms only: ScoringPolicy, ComparisonEngine
│   │                                          # (rating + delta math), budget/requirement matching. Called only
│   │                                          # from tool handlers/the direct comparison endpoint below — never
│   │                                          # directly by the conversation loop. ConversationSession also holds
│   │                                          # LastSearchResults (capped) for ordinal follow-up resolution.
│   ├── ProductAdvisor.Application/            # THIN: conversation/tool-calling loop + session persistence only.
│   │                                          # No product-data computation lives here (semantic UI, not a rules engine).
│   ├── ProductAdvisor.Infrastructure/         # MCP tool handlers (search incl. filters/category-lookup/details/
│   │                                          # price-availability/recommend/compare) + a shared comparison
│   │                                          # composition service reused by both compare_products and the direct
│   │                                          # HTTP endpoint (never two independent implementations of the same
│   │                                          # computation), Catalog/Pricing HTTP clients, LLM client, EF Core
│   │                                          # (conversation store)
│   └── ProductAdvisor.Api/                    # MCP server endpoint (/mcp, all tools) + conversation HTTP API,
│                                                # incl. the SSE .../messages/stream endpoint, + the stateless
│                                                # POST /api/comparisons direct endpoint (+ Dockerfile)
│
├── Gateway/
│   └── Gateway.Api/                          # ASP.NET Core BFF: YARP routes + chat/composition endpoints,
│                                                # incl. the SSE .../api/chat/messages/stream endpoint,
│                                                # GET /api/products/search (Catalog+Pricing composition, no LLM),
│                                                # and POST /api/products/compare (proxies the Advisor's direct
│                                                # comparison endpoint) (+ Dockerfile)
│
└── WebApp/
    └── WebApp.Blazor/                        # Blazor Web App (Interactive Server): chat (consumes the Gateway's
                                                # SSE stream server-side), recommendations, comparison view,
                                                # an explicit product-picker page (search/filter + select +
                                                # Compare button, no chat/LLM involvement),
                                                # price/availability display — Markdig-rendered narration +
                                                # Razor-rendered structured facts (+ Dockerfile)

tests/
├── ProductCatalog.Domain.Tests/
├── ProductCatalog.Application.Tests/
├── ProductCatalog.Api.Tests/                  # contract tests (WebApplicationFactory) + Testcontainers integration
├── PricingAvailability.Domain.Tests/
├── PricingAvailability.Application.Tests/
├── PricingAvailability.Api.Tests/
├── ProductAdvisor.Domain.Tests/               # scoring/comparison-delta/rating/budget-validation math, pure unit tests
├── ProductAdvisor.Application.Tests/          # conversation/tool-calling loop only (stubbed tool results) — asserts
│                                                # the orchestrator never computes a fact itself, only relays tool output
├── ProductAdvisor.Api.Tests/                   # MCP tool contract tests (incl. get_recommendations/compare_products
│                                                # determinism) + conversation API contract tests
└── EndToEnd.Tests/                             # docker-compose–driven, cross-service recommendation scenarios

docker-compose.yml                              # Local/CI parity: Postgres + all 5 services
render.yaml                                      # Render Blueprint: one web service per deployable + env wiring
.github/workflows/ci.yml                         # build, test, docker image validation, deploy trigger
```

**Structure Decision**: One solution, one project-set per bounded context following
Domain → Application → Infrastructure → API, plus a Gateway/BFF and a Blazor UI as their own
deployables, plus Aspire projects that exist only for local orchestration (never deployed as
services themselves). Test projects mirror the service layout 1:1 so a domain rule change and
its test live in obviously paired locations. This directly matches the three bounded contexts,
the Gateway/BFF, and the Blazor UI called for in the requested architecture, with independent
Dockerfiles per deployable so each ships as its own container per the "package every
microservice as a separate Docker container" requirement.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations were identified during this design; this table is intentionally
left empty.
