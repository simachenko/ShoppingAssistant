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
structured logging for tracing.

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
candidate set MUST be issued concurrently (`Task.WhenAll`) rather than sequentially.

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
| II. Reliable & Grounded Behavior | Product facts/prices/availability, and every derived number (score, rating, delta), are produced only inside deterministic MCP tool implementations that call Catalog/Pricing; the LLM only invokes tools and narrates their literal output, so it structurally cannot fabricate or calculate a fact, a rating, or a delta — the Advisor's own conversation-orchestration code never computes one either. | PASS |
| III. Testing Standards | xUnit unit tests for every domain rule (scoring, comparison delta/rating math) exercised directly and through its owning tool, contract tests per service API and per MCP tool, integration tests with Testcontainers, and cross-service recommendation/comparison scenarios in a dedicated CI stage; all required green before merge. | PASS |
| IV. Consistent UX | Comparison criteria and values are computed once, inside the `compare_products` tool, from the shared set of category attributes and applied identically to every product in the call — the LLM cannot selectively omit or reorder them because it never computes them; `ConversationSession` aggregate is the single place budget/currency/units/requirements are held so they cannot silently drift across turns. | PASS |
| V. Performance & Resilience | All outbound HTTP (service-to-service and to the LLM provider) goes through `Microsoft.Extensions.Http.Resilience` standard handlers (timeout, bounded retry+backoff, circuit breaker); independent Catalog/Pricing calls run concurrently; partial failures (e.g., Pricing down) degrade to an honest partial answer instead of failing the whole turn. | PASS |
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
│   ├── ProductCatalog.Application/          # Use cases: SearchProducts, GetProductDetails; port interfaces
│   ├── ProductCatalog.Infrastructure/        # EF Core DbContext ("catalog" schema), repositories
│   └── ProductCatalog.Api/                  # HTTP API (+ Dockerfile)
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
│   │                                          # from tool handlers below — never directly by the conversation loop.
│   ├── ProductAdvisor.Application/            # THIN: conversation/tool-calling loop + session persistence only.
│   │                                          # No product-data computation lives here (semantic UI, not a rules engine).
│   ├── ProductAdvisor.Infrastructure/         # MCP tool handlers (search/details/price-availability/recommend/compare),
│   │                                          # Catalog/Pricing HTTP clients, LLM client, EF Core (conversation store)
│   └── ProductAdvisor.Api/                    # MCP server endpoint (/mcp, all tools) + conversation HTTP API (+ Dockerfile)
│
├── Gateway/
│   └── Gateway.Api/                          # ASP.NET Core BFF: YARP routes + chat/composition endpoints (+ Dockerfile)
│
└── WebApp/
    └── WebApp.Blazor/                        # Blazor Web App (Interactive Server): chat, recommendations,
                                                # comparison view, price/availability display (+ Dockerfile)

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
