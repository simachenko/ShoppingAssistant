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

FR-036–FR-047 (research.md §20) formalize *how* a turn reaches that tool call: not an open
function-invocation loop in which the model freely picks which tool(s) to call and when, but a
fixed, application-owned ten-stage cycle (input validation → structured intent extraction →
schema validation → deterministic state merge → policy routing → intent-specific tool recipe →
tool-result validation → constrained narration → output validation → persistence) in which the
model participates only inside two bounded stages and the application layer alone controls every
transition. This is a requirement that tightens the current implementation, not a description of
it as it stands today — aligning `ConversationOrchestrator` to a fixed per-route recipe in place
of today's open `UseFunctionInvocation()` loop is tracked as follow-up work (research.md §20).
The extraction stage's output is itself schema-constrained: a closed six-value intent set, at
most one repair attempt on schema failure, no chain-of-thought in the contract or in storage, and
a confidence value that routes to clarification rather than a guess when it's too low
(FR-048–FR-054, research.md §21). The deterministic state-merge stage keeps exactly one
authoritative `CurrentRequirement` per session — category, budget/currency, hard constraints,
soft preferences, language, units, and availability requirements — updated field-by-field from
each turn's patch (present replaces, absent is carried forward, never re-derived from the
transcript) so a partially-specified requirement survives across turns without the shopper
repeating themselves (FR-055–FR-059, research.md §22). Every completed turn's response is one of
seven mutually exclusive, explicitly-typed outcomes — `answer`, `clarification`,
`recommendation`, `comparison`, `checkoutLink`, `unsupported`, `error` — assigned from the
selected route and the tool recipe's validated outcome, never guessed from narration and never
defaulted to `clarification` just because no recommendation/comparison/checkout link resulted
(FR-060–FR-065, research.md §23). Each route's tool recipe is minimal and fixed — `product_fact`,
`recommend`, `compare`, and `checkout` each reach only the specific tools their route needs
(never the full seven-tool catalog), `smalltalk`/`unsupported` reach none, the exposed tool-list
surface itself is scoped per turn before the model is invoked, and within a recipe a stateful
call (none exist yet) is never allowed to run concurrently with a compute call or another
stateful call, while independent read-only lookups may (FR-066–FR-070, research.md §24). A fixed
`TurnResourceBudget` bounds every turn's actual cost: at most two primary LLM calls plus one
repair attempt, a configured max tool-call count and max consecutive-tool-error count, no
uncontrolled repetition of an identical tool call, a configured max loop-iteration count, an
overall turn timeout, cancellation (with no persistence and release of the FR-024 in-flight
marker) on client disconnect, and exclusion of non-idempotent operations from automatic retry —
every limit's existence and its `error`-typed fail-safe outcome are fixed by the specification,
only the numeric values are deployment configuration (FR-071–FR-079, research.md §25). A
recommendation's hard constraints — budget as a ceiling, required features, an explicit
availability requirement if stated, currency compatibility, and any other user-marked-mandatory
constraint — deterministically exclude a violating product outright rather than merely
down-ranking it, with the nearest excluded alternatives optionally surfaced separately and
labeled by what they violate; soft preferences only ever affect ranking within the
already-filtered set (FR-080–FR-085, research.md §26). Narration never sees a raw tool response
directly — the application layer assembles a deterministic Evidence Envelope (result type,
canonical structured data, per-field verification status, tool provenance, unverified/unavailable
fields, tool execution status, and an allowed-claims whitelist) that is narration's only factual
input; narration is never the source of a price, specification, availability status, score,
rating, delta, or checkout URL, and output validation rejects, strips, or replaces (with a
non-LLM deterministic fallback) any narrated claim the Envelope doesn't back, without ever
affecting the turn's canonical structured data or result type (FR-086–FR-092, research.md §27).
The two LLM-invoking stages are governed by two distinct, versioned system prompts — extraction
and narration — each separating system instructions, `CurrentRequirement`, user input, and (for
narration) the Evidence Envelope into distinguishable sections, marking user/catalog content as
untrusted data rather than instructions, requiring schema-first output for extraction,
instructing a response in the user's language, refusing to disclose their own content or
credentials, never requesting chain-of-thought, and reserving few-shot examples for genuinely
complex edge cases; narration is permitted to summarize salient differences but never forced to
be both brief and exhaustive at once (FR-093–FR-103, research.md §28). Before any of that
processing begins, a fixed set of request guardrails admits or rejects the request itself: max
message length, max HTTP body size, max count/length for hard-constraint and preference entries
(enforced cumulatively across turns), Unicode normalization with control-character rejection,
strict value validation for currency/budget/operators/units/product ids beyond mere schema shape,
a per-user rate limit, a per-user cross-session concurrency limit (distinct from FR-024's
per-session lock), a per-user token/cost quota, and a max active conversation context size
bounding what's included in a prompt without ever shrinking the persisted transcript or
`CurrentRequirement`. Every guardrail violation is rejected with zero language-model or tool
invocation (FR-104–FR-113, research.md §29). Conversation data is no longer treated as ordinary,
privacy-neutral application data (reversing an earlier clarification): every raw message is
screened for potential PII before any LLM-provider call (blocked or redacted, never passed
through), prompts carry only minimally necessary context and never the user's stable identifier
without functional need, users can delete their own history, old sessions are deleted
automatically per a retention policy, conversation data is encrypted in transit/at rest/backups,
and the configured LLM provider must meet defined training/retention/data-region requirements
(FR-114–FR-123, research.md §30). The MCP endpoint and every internal service-to-service
credential get nine additional hardening requirements on top of the existing shared-secret model:
no unauthenticated access under any configuration, secret-storage-only storage, rotation as a
supported capability with an old/new overlap window, no production fallback to a development
default, constant-time comparison, scoped-per-relationship credentials as the preferred future
direction, least-privilege tool execution, no automatic conversation-ownership grant from
MCP-transport authentication alone, and a distinct production-readiness review for preview/
prerelease dependencies such as this project's own MCP SDK package (FR-124–FR-132, research.md
§31). A turn's logs are limited to eleven allowed fields (correlation id, hashed/pseudonymous
identifier, prompt version, model identifier, intent, tool name, allow/deny decision, latency,
token usage, validation status, error category) and explicitly exclude the full raw user message,
full assembled prompt, PII-bearing tool data, credential headers, API keys, connection strings,
and the full raw LLM response; seven turn-cycle events — loop-limit reached, schema repair,
rejected tool call, grounding failure, rate-limit rejection, PII detection, and provider failure —
each get their own dedicated, independently-incrementable metric (FR-133–FR-137, research.md
§32). A mandatory, fifteen-class agentic security and quality eval suite — direct/indirect
prompt injection, system-prompt extraction, fabricated product data, wrong-tool selection,
tool-loop exhaustion, malformed/oversized input, cross-session access, memory poisoning,
mid-conversation constraint changes, product-not-found, partial dependency failure, unsupported
intent, and PII/payment-data input — verifies these guarantees rather than defining new ones;
grounding, authorization, and cross-session-access evals are release-blocking at 100%, while the
remaining classes run automatically and are reviewed at release without a fixed pass-rate
(FR-138–FR-141, research.md §33).

The advisor's replies stream to the user progressively over SSE as the LLM generates them
(FR-015), and are rendered with real structure — Markdown for the LLM's narration, actual
HTML lists/tables (built by the UI, not the LLM) for the structured facts (FR-016/FR-017) —
detailed in research.md §11–§12.

Two trust boundaries now wrap the whole system (FR-027–FR-032, research.md §16–§18): every
user-facing entry point requires a signed-in Google identity, verified independently by whichever
service first receives it from the browser; and every internal service-to-service call carries a
shared internal credential, verified by whichever service receives it, regardless of which
service is calling. Neither boundary trusts network position alone. Observability (structured
logs, request tracing, health checks, and metrics) uses the already-adopted OpenTelemetry
mechanism end-to-end, now exported to a real backend instead of console-only output, and is
always best-effort relative to the request it describes — an unreachable observability backend
never blocks or fails a user-facing request.

Before the shopper reaches an interactive chat screen at all, WebApp.Blazor shows a starting-up
state and polls a new Gateway endpoint that aggregates every dependent service's existing
liveness check (FR-033–FR-035, research.md §19) — no new health-check mechanism, just Gateway
fanning out to the same `/alive` endpoints FR-028 already requires and merging the result, the
same pushdown-composition pattern already used for product detail/search. The wait is bounded:
the shopper always reaches the interactive UI, honestly labeled if something is still
unreachable, rather than being blocked indefinitely.

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
through Markdown at all; `Microsoft.AspNetCore.Authentication.Google` (or the OpenID Connect
handler pointed at Google's OIDC endpoint) in `WebApp.Blazor` for the sign-in flow, plus
`Microsoft.AspNetCore.Authentication.JwtBearer` configured against Google's OIDC discovery
document in `Gateway.Api` to independently validate the same identity token (research.md §17); a
small internal-API-key `DelegatingHandler`/middleware pair (outbound: attach a header; inbound:
validate it) shared via `ServiceDefaults`, applied to every Gateway→backend and Advisor→backend
call (research.md §18).

**Observability backend**: OpenTelemetry's OTLP exporter (already wired in every service via
`ServiceDefaults`) points at a hosted, free-tier-friendly OTLP-compatible backend (e.g., Grafana
Cloud) for logs, traces, and metrics in production, configured entirely through
`OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` environment variables — no new
required local dependency; the Aspire Dashboard remains the local-dev view (research.md §16).

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
| I. Code Quality & Maintainability | Every service is split into Domain / Application / Infrastructure / API projects with only interface-level coupling; config/secrets live in environment variables and .NET user-secrets/Aspire parameters, never in source; `dotnet format` + analyzers + `dotnet test` run in CI as a merge gate. The internal API key and Google OAuth client secret follow the same rule — environment variables/Aspire parameters, `sync: false` in `render.yaml`, never committed. | PASS |
| II. Reliable & Grounded Behavior | Product facts/prices/availability, and every derived number (score, rating, delta), are produced only inside deterministic MCP tool implementations that call Catalog/Pricing; the LLM only invokes tools and narrates their literal output, so it structurally cannot fabricate or calculate a fact, a rating, or a delta — the Advisor's own conversation-orchestration code never computes one either. Streaming (research.md §11) only staggers delivery of that same narration text; structured facts are always sent complete in the final `result` event, never as a partial/guessed value mid-stream. Comparison (FR-018) and search filtering (FR-020) go further: the deterministic computation is reachable through a plain endpoint independent of the LLM's tool-selection decision, and any narration attached to it (FR-019) is generated by a separate, constrained call whose only input is the already-computed data, so it cannot alter what it describes. | PASS |
| III. Testing Standards | xUnit unit tests for every domain rule (scoring, comparison delta/rating math) exercised directly and through its owning tool, contract tests per service API and per MCP tool, integration tests with Testcontainers, and cross-service recommendation/comparison scenarios in a dedicated CI stage; all required green before merge. | PASS |
| IV. Consistent UX | Comparison criteria and values are computed once, inside the `compare_products` tool, from the shared set of category attributes and applied identically to every product in the call — the LLM cannot selectively omit or reorder them because it never computes them; `ConversationSession` aggregate is the single place budget/currency/units/requirements are held so they cannot silently drift across turns. Category names and characteristics are resolved through a dedicated lookup (FR-021) rather than guessed, and `ConversationSession.LastSearchResults` (FR-022) gives every ordinal follow-up a single, consistent set of identifiers to resolve against. | PASS |
| V. Performance & Resilience | All outbound HTTP (service-to-service and to the LLM provider) goes through `Microsoft.Extensions.Http.Resilience` standard handlers (timeout, bounded retry+backoff, circuit breaker); independent Catalog/Pricing calls run concurrently; partial failures (e.g., Pricing down) degrade to an honest partial answer instead of failing the whole turn. SSE streaming (research.md §11) improves perceived responsiveness and still guarantees a complete final response — falling back to a buffered call if the provider/connection can't sustain a stream — rather than leaving the user with a stuck or truncated turn. | PASS |
| VI. Observability & Safe Evolution | OpenTelemetry tracing + structured logs across Gateway, MCP tool calls, services, EF Core, and LLM calls, correlated via W3C `traceparent` propagated automatically by ASP.NET Core/HttpClient instrumentation (Aspire `ServiceDefaults`), now exported to a real OTLP backend (research.md §16) rather than console-only, satisfying FR-027/FR-028's "commonly used tools" requirement; prompts/tool schemas/recommendation rules are plain version-controlled C#/config, not runtime-mutable state. Auth failures (missing/invalid internal key, invalid/expired Google identity) are logged with the same correlation id as any other request, never silently dropped. | PASS |

No unjustified violations were identified; the **Complexity Tracking** table below is
intentionally empty. The multi-service topology itself is not a constitution violation — it is
an explicit, user-mandated architectural requirement (DDD bounded contexts, independent
deployability), not a choice made for its own sake by this plan.

**Note on Access Control**: the constitution (v1.0.0) does not yet have a dedicated principle
for authentication/authorization; FR-029–FR-031's internal-API-key and Google-OAuth requirements
are covered here as an extension of Principle I (secrets externalized) and Principle VI
(auth failures are logged, safe evolution), but a future constitution amendment adding an
explicit Access Control principle would make this gate reviewable on its own rather than folded
into two adjacent principles — recommended as a follow-up, not a blocker for this plan.

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
│   └── ServiceDefaults/                    # Shared OTel (now OTLP-exported), health checks, resilience,
│                                              # service discovery, correlation id, and the internal-API-key
│                                              # outbound handler + inbound validation middleware (research.md §18)
│
├── ProductCatalog/
│   ├── ProductCatalog.Domain/               # Product, Category, Brand, Specification (entities/VOs, rules)
│   ├── ProductCatalog.Application/          # Use cases: SearchProducts (incl. structured characteristic
│   │                                          # filters), GetProductDetails, GetCategoryByName; port interfaces
│   ├── ProductCatalog.Infrastructure/        # EF Core DbContext ("catalog" schema), repositories
│   └── ProductCatalog.Api/                  # HTTP API incl. POST .../products/search,
│                                                # GET .../categories?name= — every endpoint requires the
│                                                # internal API key (FR-029, research.md §18), never called
│                                                # directly by a browser (+ Dockerfile)
│
├── PricingAvailability/
│   ├── PricingAvailability.Domain/          # Offer, Price, Discount, StockStatus (entities/VOs, rules)
│   ├── PricingAvailability.Application/      # Use cases: GetOffer, GetOffersBatch; port interfaces
│   ├── PricingAvailability.Infrastructure/   # EF Core DbContext ("pricing" schema), repositories
│   └── PricingAvailability.Api/              # HTTP API — internal API key required on every endpoint,
│                                                # same as Catalog (+ Dockerfile)
│
├── ProductAdvisor/
│   ├── ProductAdvisor.Domain/                # Pure deterministic algorithms only: ScoringPolicy, ComparisonEngine
│   │                                          # (rating + delta math), budget/requirement matching. Called only
│   │                                          # from tool handlers/the direct comparison endpoint below — never
│   │                                          # directly by the conversation loop. ConversationSession also holds
│   │                                          # LastSearchResults (capped) for ordinal follow-up resolution and
│   │                                          # a UserId binding its owner (FR-031).
│   ├── ProductAdvisor.Application/            # The fixed ten-stage Turn Processing Cycle (FR-036–FR-047,
│   │                                          # research.md §20) — input validation → structured intent
│   │                                          # extraction → schema validation → deterministic state merge →
│   │                                          # policy routing → intent-specific tool recipe → tool-result
│   │                                          # validation → constrained narration (via an Evidence Envelope,
│   │                                          # FR-086–FR-092) → output validation → persistence. Session
│   │                                          # persistence only; no product-data computation lives here
│   │                                          # (semantic UI, not a rules engine) — replaces the earlier free
│   │                                          # `FunctionInvokingChatClient` tool-selection loop (Phase 9+).
│   ├── ProductAdvisor.Infrastructure/         # MCP tool handlers (search incl. filters/category-lookup/details/
│   │                                          # price-availability/recommend/compare/checkout-link) + a shared
│   │                                          # comparison composition service reused by both compare_products
│   │                                          # and the direct HTTP endpoint (never two independent
│   │                                          # implementations of the same computation), per-route
│   │                                          # `ToolRecipe` scoping (FR-066–FR-070), the two versioned system
│   │                                          # prompts (FR-093–FR-103), PII screening (FR-116), Catalog/
│   │                                          # Pricing HTTP clients, LLM client, EF Core (conversation store)
│   └── ProductAdvisor.Api/                    # MCP server endpoint (/mcp, all tools) + conversation HTTP API,
│                                                # incl. the SSE .../messages/stream endpoint, + the stateless
│                                                # POST /api/comparisons direct endpoint — internal API key
│                                                # required (called only by Gateway, never a browser)
│                                                # (+ Dockerfile)
│
├── Gateway/
│   └── Gateway.Api/                          # ASP.NET Core BFF: YARP routes + chat/composition endpoints,
│                                                # incl. the SSE .../api/chat/messages/stream endpoint,
│                                                # GET /api/products/search (Catalog+Pricing composition, no LLM),
│                                                # POST /api/products/compare (proxies the Advisor's direct
│                                                # comparison endpoint), POST /api/products/checkout-link
│                                                # (FR-025), JWT Bearer validation of the caller's Google
│                                                # identity token on every endpoint (FR-030, research.md §17),
│                                                # session-ownership enforcement (FR-031), and the anonymous
│                                                # GET /api/system-status aggregate-readiness endpoint consumed
│                                                # by WebApp's starting-up screen (FR-033–FR-035, research.md
│                                                # §19) (+ Dockerfile)
│
└── WebApp/
    └── WebApp.Blazor/                        # Blazor Web App (Interactive Server): Google sign-in
                                                # (cookie auth + OIDC challenge, FR-030), a starting-up screen
                                                # that polls GET /api/system-status before showing the
                                                # interactive chat UI (FR-033–FR-035), chat (consumes the
                                                # Gateway's SSE stream server-side, forwarding the signed-in
                                                # user's identity token), recommendations, comparison view,
                                                # an explicit product-picker page (search/filter + select +
                                                # Compare button, no chat/LLM involvement),
                                                # price/availability display — Markdig-rendered narration +
                                                # Razor-rendered structured facts (+ Dockerfile)

tests/
├── ProductCatalog.Domain.Tests/
├── ProductCatalog.Application.Tests/
├── ProductCatalog.Api.Tests/                  # contract tests (WebApplicationFactory) + Testcontainers integration,
│                                                # incl. missing/invalid internal-API-key rejection (FR-029)
├── PricingAvailability.Domain.Tests/
├── PricingAvailability.Application.Tests/
├── PricingAvailability.Api.Tests/              # incl. missing/invalid internal-API-key rejection (FR-029)
├── ProductAdvisor.Domain.Tests/               # scoring/comparison-delta/rating/budget-validation math, pure unit tests
├── ProductAdvisor.Application.Tests/          # conversation/tool-calling loop only (stubbed tool results) — asserts
│                                                # the orchestrator never computes a fact itself, only relays tool output
├── ProductAdvisor.Api.Tests/                   # MCP tool contract tests (incl. get_recommendations/compare_products
│                                                # determinism) + conversation API contract tests, incl.
│                                                # missing/invalid internal-API-key rejection (FR-029)
├── Gateway.Api.Tests/                          # incl. missing/invalid/expired Google identity token rejection
│                                                # (FR-030) and cross-user session-access rejection (FR-031)
└── EndToEnd.Tests/                             # docker-compose–driven, cross-service recommendation scenarios,
                                                 # incl. a full sign-in → chat → checkout-link scenario and a
                                                 # correlation-id-through-an-auth-failure observability check

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
