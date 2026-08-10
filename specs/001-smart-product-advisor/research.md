# Phase 0 Research: Smart Product Advisor

All technology choices in this feature's Technical Context were explicitly mandated by the
user (.NET 10, ASP.NET Core, DDD microservices, PostgreSQL + EF Core, MCP C# SDK,
Microsoft.Extensions.AI, Aspire/Docker Compose, OpenTelemetry, xUnit, GitHub Actions, Render,
Neon). No `NEEDS CLARIFICATION` markers remain in the Technical Context. This document instead
records the design decisions needed to turn those mandates into a coherent architecture, each
with rationale and rejected alternatives, so later phases don't re-litigate them.

## 1. How the Advisor uses MCP + Microsoft.Extensions.AI together (semantic-UI orchestration)

**Decision**: `ProductAdvisor.Api` hosts an MCP server (`ModelContextProtocol.AspNetCore`,
Streamable HTTP transport at `/mcp`) exposing **every product-data operation as a tool, with
nothing computed anywhere else**: `search_products`, `get_product_details`,
`check_price_and_availability` (data-access), plus `get_recommendations` (budget filtering +
requirement matching + deterministic scoring) and `compare_products` (shared-criteria values +
a deterministic per-product rating + computed cross-product deltas). All five tools are
adapted to `AIFunction`s (the MCP C# SDK provides this conversion) and handed to
`Microsoft.Extensions.AI`'s `IChatClient` with function calling. The Advisor's own
`ProductAdvisor.Application` code is reduced to a conversation/tool-calling loop: it feeds the
LLM the user message, current session state, and the tool catalog; executes whichever tool(s)
the LLM chooses to call; feeds results back; and relays the LLM's final natural-language
message. It contains **no product-data computation of its own** — filtering, scoring,
rating, and delta math live only inside the tool handlers (`ProductAdvisor.Infrastructure`,
backed by pure functions in `ProductAdvisor.Domain`), never in the orchestration loop itself.
The LLM decides *when* to call `get_recommendations`/`compare_products` (the same way it
decides when to call `search_products`) and may describe their output in more detail, but it
never performs the underlying calculation — the numbers it narrates always came from a tool
result already sitting in the conversation, verbatim.

**Rationale**: This keeps the LLM's authority exactly where the user scoped it (understanding
the request, choosing tools, clarifying, explaining) while making the Advisor itself a thin
"semantic UI": every fact, score, rating, and delta a user ever sees has exactly one possible
origin — a specific, unit-tested, deterministic tool — regardless of which turn or which tool
sequence the LLM chose. That is a stronger and simpler guarantee than "the orchestration code
happens to run scoring after every data-fetch," because it removes the orchestration layer from
the set of places that could ever contain a business rule. Hosting a real MCP server (rather
than only using function calling internally) also means the Advisor's tools are usable by any
standard MCP client (e.g., an IDE or Claude Desktop) for demos/testing, not just by the bundled
chat UI.

**Alternatives considered**: (a) Skip MCP and use plain `Microsoft.Extensions.AI` function
calling only — rejected because the project is explicitly MCP-based and the constitution/spec
context names MCP as the integration mechanism. (b) Let the LLM perform comparison/scoring via
prompting — rejected outright; it reintroduces the fabrication risk Principle II forbids. (c)
Keep filtering/scoring/comparison as a deterministic step the Application layer runs
automatically right after a data-fetch tool call, outside the tool-calling boundary (the
initial design) — rejected: that still lets the orchestration code make a business decision
(when and how to score) rather than being a pure relay, which blurs the "semantic UI" boundary
this revision is specifically meant to enforce. Moving that logic behind its own tool call
means the *only* thing that can ever decide "compute a recommendation now" is the LLM's tool
choice, and the *only* thing that can ever perform that computation is the tool handler.

One deliberate exception: whether **essential conversation fields** (category, budget) are
present is a check on conversation state, not a computation over product data, so it remains a
thin deterministic guard in `ProductAdvisor.Application` — it decides whether to let the LLM
proceed toward `get_recommendations` or must surface a `ClarificationQuestion` first. This
guard exists because FR-002/SC-005 require clarification *every time* essential info is
missing, which cannot be left to the LLM's discretion alone. It never touches product data, so
it does not violate the "no computation outside tools" rule — it only ever inspects the
session's own `UserRequirement` state.

## 2. Async messaging scope for v1

**Decision**: No message broker/queue is introduced in this version. All three services
communicate exclusively via synchronous HTTP APIs, because every Advisor operation (search,
price/availability check, compare, recommend) needs an immediate answer to respond to the
user. A lightweight in-process domain-event dispatch (e.g., MediatR notifications or a plain
`IDomainEventDispatcher`) is reserved inside each service for intra-service side effects (e.g.,
logging a "recommendation given" fact for analytics later), but no cross-service integration
events are implemented now.

**Rationale**: The user explicitly asked to "keep asynchronous messaging limited to scenarios
where it provides clear value" for the initial version. No current user story requires
fire-and-forget cross-service communication; introducing Kafka/RabbitMQ/Azure Service Bus now
would add infrastructure cost and operational complexity the free-tier hosting goal can't
absorb, with no corresponding feature benefit yet.

**Alternatives considered**: Outbox pattern + a broker for price-change notifications to
invalidate cached comparisons — rejected for v1 since no comparison caching layer exists yet;
revisit if/when caching is introduced.

## 3. Blazor hosting model

**Decision**: Blazor Web App using the Interactive Server render mode.

**Rationale**: The UI is a chat-style, server-driven experience (conversation state already
lives server-side in the Advisor service); Interactive Server avoids shipping a WASM runtime
and keeps the client thin, which suits a free-tier container with limited resources and a
"simple" UI as requested. SignalR's persistent connection is an acceptable trade-off at demo
scale (single-user sessions, no massive concurrency).

**Alternatives considered**: Blazor WebAssembly — rejected due to added download size/startup
cost and the need to expose more APIs directly to an untrusted client for no real benefit at
this scope. Static SSR + fetch-based JS chat widget — rejected as more custom plumbing than a
"simple Blazor interface" calls for.

## 4. Gateway / BFF implementation

**Decision**: `Gateway.Api` is a minimal ASP.NET Core project using YARP (`Yarp.ReverseProxy`)
for route-level proxying to Catalog/Pricing (only where the UI needs direct pass-through, e.g.
product detail lookups) plus a small set of its own composition endpoints for the chat flow
(`POST /api/chat/messages`, `GET /api/chat/{sessionId}`) that call the Advisor conversation API.
It is the single entry point the Blazor app talks to; it is also where a correlation ID is
generated if the incoming request doesn't already carry one.

**Rationale**: YARP is the standard, low-code ASP.NET Core reverse-proxy choice and avoids
hand-writing repetitive proxy controllers; a thin composition layer is still needed because the
chat flow legitimately aggregates/shapes data for the UI rather than being a pure pass-through.

**Alternatives considered**: Hand-rolled `HttpClient` forwarding for every route — rejected as
unnecessary boilerplate YARP already solves declaratively.

## 5. Multi-tenant-free, schema-per-service on one Postgres instance

**Decision**: One Neon Postgres project for the demo environment; one Postgres schema
(`catalog`, `pricing`, `advisor`) and one least-privileged database role per service, each
role granted access only to its own schema. Each service's EF Core `DbContext` sets
`HasDefaultSchema(...)` and owns its own migration history. No service's connection string can
resolve another service's schema.

**Rationale**: The user requires "separate logical databases or schemas" and explicitly allows
one shared managed instance for the free/demo environment, but the "no shared tables, no
cross-service database queries" rule has to be enforced by more than convention — per-schema
Postgres roles make a cross-schema query fail at the database layer, not just at code review.

**Alternatives considered**: One shared schema with table-name prefixes — rejected, doesn't
actually prevent cross-service queries and is exactly what the requirement rules out.

## 6. Resilience for outbound calls (HTTP + LLM)

**Decision**: Every outbound `HttpClient` (service-to-service and to the LLM provider) is
registered with `Microsoft.Extensions.Http.Resilience`'s standard resilience handler (timeout,
bounded retry with exponential backoff + jitter, circuit breaker). LLM-provider 429/5xx
responses are treated as retryable; after retries are exhausted the Advisor degrades to "I
can't reach the assistant right now" rather than hanging, and a Catalog/Pricing failure
degrades that one piece of data to "could not be verified" rather than failing the whole
conversation turn.

**Rationale**: Directly satisfies constitution Principle V (timeouts, controlled retries,
graceful fallback, honest partial responses) and matches the reality of a free-tier LLM API
(rate-limited) and free-tier Postgres/hosting (occasional cold starts).

**Alternatives considered**: Hand-rolled Polly policies — rejected in favor of the standard,
already-reviewed `Microsoft.Extensions.Http.Resilience` defaults, which reduce custom code to
maintain.

## 7. Observability and correlation

**Decision**: Use the .NET Aspire `ServiceDefaults` project (OpenTelemetry tracing + metrics +
health checks + `HttpClient`/EF Core/ASP.NET Core instrumentation, OTLP exporter) in every
service, including the Gateway and Blazor app. Correlation across services relies on the
automatically-propagated W3C `traceparent` header; a human-readable `X-Correlation-Id` is
additionally generated at the Gateway (or reused if the client already sent one) and added to
every log scope and forwarded on every downstream call, so support/debugging can search logs by
a single stable ID even when trace IDs roll per span.

**Rationale**: Satisfies Principle VI's "important operations, MCP calls, failures, and
performance indicators MUST be logged... propagate a correlation identifier between all
services" without inventing a bespoke tracing mechanism.

**Alternatives considered**: Custom `X-Correlation-Id`-only propagation without OpenTelemetry —
rejected; would lose span-level timing/failure detail across the LLM/database/HTTP calls that
Principle VI also requires.

## 8. Contract testing approach

**Decision**: Contract tests are xUnit tests using `WebApplicationFactory<TProgram>` per
service, asserting request/response DTO shapes (serialization round-trip + required fields)
and status-code behavior for each documented endpoint (see `contracts/`). MCP tool contracts
are tested by invoking the tools through an in-process `McpClient` against the hosted MCP
endpoint and asserting the declared JSON schema and a couple of representative calls.

**Rationale**: This is a single-repo, small-team demo, not a multi-team consumer-driven-contract
situation — in-process contract tests give the "validate service API contracts" requirement
without the operational overhead of a Pact broker.

**Alternatives considered**: Pact.NET consumer-driven contracts — rejected as disproportionate
infrastructure for this scope; revisit if external teams start consuming these APIs
independently.

## 9. CI/CD and hosting wiring

**Decision**: GitHub Actions workflow builds the solution, runs `dotnet test` (unit +
contract + Testcontainers-backed integration tests), builds each service's Docker image to
validate it builds cleanly, and runs the docker-compose–based end-to-end suite. Deployment to
Render uses a `render.yaml` Blueprint (one web service per deployable: Catalog, Pricing,
Advisor, Gateway, WebApp) with Render's native git-triggered auto-deploy on push to the main
branch; environment variables (Neon connection strings per service role, LLM provider key,
inter-service base URLs) are configured as Render environment variables/secrets, never
committed.

**Rationale**: Render Blueprints are the lowest-effort way to get "deploy from GitHub" on a
free/low-cost tier without hand-rolled deploy scripting in Actions; GitHub Actions remains the
required quality gate (build/test/image validation) per constitution Principle III/Development
Workflow.

**Alternatives considered**: GitHub Actions building and pushing images to a registry, then
calling Render's deploy-hook API — kept as a documented fallback in `quickstart.md` if
Blueprint auto-deploy proves insufficient, but not the default.

## 10. LLM provider choice (kept swappable)

**Decision**: Default demo configuration targets a free-tier LLM provider through an
OpenAI-compatible endpoint (e.g., Google Gemini's OpenAI-compatible API or an equivalent free
tier), consumed purely through `Microsoft.Extensions.AI.IChatClient`. The concrete provider,
model name, endpoint, and API key are all configuration (environment variables/Aspire
parameters), never hard-coded, so the provider can be swapped without touching
`ProductAdvisor` code.

**Rationale**: Directly matches "use an AI provider with a free API tier and keep the provider
replaceable through Microsoft.Extensions.AI abstractions," and constitution Principle I's
externalized-configuration requirement.

**Alternatives considered**: Hard dependency on one vendor SDK — rejected; would violate both
the explicit swappability requirement and Principle I.

## 11. Streaming responses over SSE (FR-015/SC-008)

**Decision**: Add a streaming sibling to the existing conversation endpoint —
`POST /api/conversations/{sessionId}/messages/stream` on `ProductAdvisor.Api`, mirrored as
`POST /api/chat/messages/stream` on `Gateway.Api` — that responds with `text/event-stream`
instead of a single JSON body. The **non-streaming endpoints from §1 are kept as-is**; streaming
is additive, not a replacement, so existing contract tests and any non-streaming consumer keep
working unchanged.

Internally, the orchestrator calls `IChatClient.GetStreamingResponseAsync(messages, options, ct)`
instead of `GetResponseAsync` — `FunctionInvokingChatClient` (from `.UseFunctionInvocation()`,
already wired) supports streaming transparently: it still intercepts a function-call chunk,
invokes the real tool handler (still fully deterministic, still captured via
`IToolResultCapture` exactly as in §1), and resumes streaming the model's continuation. The SSE
response carries two event kinds:

- `event: token` (zero or more) — `data: {"delta": "..."}`, an incremental slice of the LLM's
  narration text, in order.
- `event: result` (exactly one, last) — `data: <the same JSON shape the non-streaming endpoint
  returns>` (contracts/advisor-conversation-api.md's `ConversationTurnResponse`): the full
  narration plus, if a tool produced one, the structured `items`/`criteria`/`rows` data. This
  keeps exactly one response contract regardless of whether a client streamed or not.

Server-side SSE writing uses ASP.NET Core's built-in SSE support for the installed package
version (`TypedResults.ServerSentEvents`/equivalent if present in this SDK's ASP.NET Core
release; otherwise a manual `text/event-stream` writer — confirmed at implementation time, both
produce the identical wire format above so callers are unaffected either way).

On the client side, Blazor's Interactive Server render mode already keeps a live connection to
the browser (SignalR) — so "the frontend streams" means the **Blazor component's own C# code**
opens the SSE request to the Gateway (via a server-side `HttpClient`, reading the response body
incrementally with .NET's built-in SSE parser) and updates its bound state per `token` event,
calling the normal Blazor re-render pipeline. No separate browser-side `EventSource` is needed
or used; the browser only ever talks to the Blazor circuit it already has open.

**Fallback (per spec's new edge case)**: if the provider doesn't support streaming, or the SSE
connection drops mid-turn, the Advisor still guarantees a final `result` event carrying the
complete response (falling back to a buffered call internally if needed); if the client's own
connection to the Gateway drops, the Blazor page falls back to calling the non-streaming
endpoint so the user always ends up with the complete answer, never a permanently truncated one
(constitution Principle V — graceful degradation, not a stuck UI).

**Rationale**: Keeps the "everything factual comes from a tool, captured once" guarantee from §1
completely intact — streaming only changes how the LLM's *narration* is delivered, never how or
when a fact is produced. A single additive endpoint (rather than replacing the existing one)
avoids destabilizing the already-verified US1 conversation API and its tests.

**Alternatives considered**: (a) WebSockets/SignalR hub end-to-end from Advisor through Gateway
to the browser — rejected as heavier than needed; SSE is a plain HTTP response, simpler to proxy
through a YARP-fronted Gateway, and we don't need bidirectional push (the client only ever sends
one message per turn). (b) Browser-side `EventSource` connecting directly to the Gateway —
rejected for a Blazor Server app specifically: it would mean maintaining UI state in JavaScript
and shipping it back into the Blazor circuit via JS interop, duplicating state the circuit
already owns server-side; consuming the stream server-side in the component is simpler and
keeps all state in one place. (c) Re-parsing/patching only the newly-arrived markdown delta —
rejected in favor of full-text reparse per token (see §12) for correctness.

## 12. Rich content rendering: Markdown for narration, real markup for facts (FR-016/FR-017/SC-009)

**Decision**: Two different rendering paths for two different kinds of content, kept
deliberately separate:

- The LLM's own narration (`message`/`question` text) is treated as Markdown and rendered via
  **Markdig** (`Markdown.ToHtml(text, pipeline)`) into HTML, **sanitized** before display (a
  restrictive Markdig pipeline with the raw-HTML-passthrough extension disabled, plus running
  the output through an HTML allow-list sanitizer) since LLM-generated text is not fully trusted
  content and Blazor's `MarkupString` bypasses Razor's normal HTML-encoding — skipping
  sanitization here would be a real stored/reflected-XSS-style risk.
- The **structured facts** (specifications, matched requirements, trade-offs, comparison
  criteria/rows/ratings/deltas) are rendered by the Blazor components' own Razor markup — real
  `<ul>/<li>` and `<table>` elements built directly from the typed response DTOs — **never** by
  asking the LLM to format them as Markdown itself. Letting the LLM format the facts would
  reopen exactly the risk the rest of this architecture exists to close (research.md §1): a
  "formatting" pass is still a pass where the LLM could alter a number. The rendered Markdown
  narration is supplementary commentary that sits alongside the code-rendered facts, never a
  substitute for them.
- While streaming (§11), the UI re-parses the **full accumulated narration text** through
  Markdig on every `token` event rather than incrementally patching previously-rendered HTML —
  correct-by-reconstruction (an unclosed `**bold**` mid-stream never renders as broken markup for
  more than the current token) and cheap enough at chat-message length that re-parsing per token
  has no perceptible cost.

**Rationale**: Directly satisfies FR-016/FR-017 while preserving constitution Principle II
(grounding) — rich formatting is purely presentational and can never become a second place a
fact could be fabricated or altered, because facts never pass through the LLM-formatted path at
all.

**Alternatives considered**: (a) Ask the LLM to also emit the comparison table/spec list as
Markdown and render that directly — rejected; would make the LLM the source of a "fact's"
presentation, one step from being the source of the fact itself, and harder to unit-test for
determinism than our own Razor markup. (b) A client-side JS Markdown library (e.g., `marked.js`)
instead of Markdig — rejected to keep the Blazor Server app's logic server-side and in C#,
consistent with not shipping business/formatting logic to the browser; also avoids a second
sanitization surface (JS-side) to maintain.

## 13. Deterministic parametric search and category/characteristics resolution (FR-020/FR-021/SC-011)

**Decision**: Product search accepts explicit, structured filters instead of relying on the LLM
to infer the right free-text query:

- **Category**: resolvable by id (existing) or by name (`GET /api/catalog/categories?name=`,
  reusing the already-implemented `FindCategoryByNameAsync` repository method), matched
  case-insensitively — the LLM (or any caller) grounds a category reference to a concrete id
  instead of guessing one.
- **Characteristics**: a small filter DSL — `{ key, operator, value, valueTo? }` with
  `operator ∈ { eq, gte, lte, between }` — covers the catalog's existing numeric (`camera_mp`,
  `battery_mah`, ...) and simple categorical (`noise_cancelling`) attributes without building a
  general-purpose query language.
- **Price range**: Catalog has no price data (bounded-context isolation, data-model.md), so a
  price filter cannot be pushed into Catalog's query. It is applied by whichever service composes
  Catalog + Pricing (the Advisor's search tool, or the Gateway's picker-facing endpoint): fetch
  the category/characteristics-narrowed candidate ids from Catalog first, batch-fetch their
  offers from Pricing, then filter/sort/limit by price on that already-small candidate set. This
  is the same "pushdown filter composition" pattern already used for cross-service data joins in
  this system (data-model.md's `ProductCandidate` assembly) — no new Pricing endpoint or query
  parameter is introduced.
- **Implementation boundary, stated explicitly**: `Product.Specifications` is stored as a JSON
  document per product (`OwnsMany(...).ToJson()`, `ProductConfiguration.cs`), which does not
  translate cleanly into arbitrary per-operator SQL predicates via EF Core's LINQ provider.
  Characteristic filtering is therefore applied **in Catalog's application layer, in-process,
  after** category/free-text narrowing has already reduced the row set via an indexed SQL
  predicate — not against the full, unfiltered catalog. This is an explicit, documented scale
  boundary appropriate to plan.md's Scale/Scope (hundreds to low thousands of products per
  category), not an oversight.

**Rationale**: Keeps the LLM out of the filtering/ranking arithmetic entirely (constitution
Principle II) while still letting it do the thing language models are legitimately good at —
mapping a natural-language ask ("phones under 25,000 UAH with a great camera") onto these
structured parameters. Reusing the existing pushdown-composition pattern for price avoids
inventing a second cross-service query mechanism.

**Alternatives considered**: (a) Postgres trigram search (`pg_trgm`) for fuzzier free-text
matching — a recognized, right-sized upgrade for this catalog's scale (see the "GooglePixel 9" /
"Samsung Galaxy S24" free-text matching fix already shipped for `search_products`'s `query`
parameter, which currently uses token-overlap + whitespace-stripped substring matching instead).
Documented here as the natural next step if free-text matching needs to get fuzzier (typo
tolerance), but not implemented now — it needs an extension + index + threshold-tuning pass that
isn't justified by the current dataset size. (b) A semantic/vector index over product
descriptions — rejected as disproportionate for a catalog of this size; it trades a determinism
problem for an embedding-model dependency and new infrastructure, solving a scale problem this
system doesn't have yet. (c) A CQRS read-model / dedicated search index (Elasticsearch/OpenSearch
-style), fed by Catalog/Pricing domain events — this is the textbook **correct** pattern at real
retail scale (unifying category + characteristics + price + availability into one filterable,
sortable, denormalized view, avoiding the pushdown-composition round trips entirely) and is
recorded here so the boundary is a conscious choice; not built for this feature because it
requires an event bus and a new service that plan.md's Scale/Scope doesn't justify for a
demonstration project.

## 14. Direct (non-conversational) comparison invocation (FR-018/FR-019/SC-010)

**Decision**: The deterministic comparison computation (`ComparisonEngine`, candidate assembly
from Catalog + Pricing) is factored into one shared service inside `ProductAdvisor.Infrastructure`
that is called from **two** entry points that must never drift apart:

1. The existing `compare_products` MCP tool (conversational; the LLM supplies the product ids,
   usually resolved moments earlier via `search_products`/`get_category`).
2. A new stateless `POST /api/comparisons` endpoint on `ProductAdvisor.Api` that takes a product-id
   set directly — no `sessionId`, no conversation turn, no LLM tool-selection step at all. This is
   what an explicit "pick products, click Compare" UI calls.

Both paths produce the `Comparison` shape from data-model.md, and because both call the identical
composition code, results for the same product-id set are byte-identical regardless of path
(SC-010) — this is asserted directly by a contract test, not just claimed.

`POST /api/comparisons` accepts an optional `includeExplanation` flag (default `true`). When set,
a **separate**, narrowly-scoped `IChatClient` call is made whose only input is the already-computed
`Comparison` and whose system prompt instructs it to summarize, never invent, alter, or omit a
value. If that call fails or is disabled, `explanation` is `null` and the (already fully computed)
`comparison` data is still returned in full — constitution Principle V's "honest partial response"
applied to this endpoint specifically, and FR-019's requirement that narration's absence never
blocks the structured result.

**Rationale**: Directly answers the concern that motivated this revision — comparison math must
never depend on the LLM choosing to invoke it correctly or on the LLM being available at all. It
also keeps `compare_products` (useful for MCP-standard clients and the conversational flow) rather
than removing it, since resolving "compare the Galaxy S24 and the Pixel 9" from prose into ids is
still a legitimate, retrieval-flavored job for the LLM+search tools — only the arithmetic moves
outside the conversation entirely.

**Alternatives considered**: (a) Remove `compare_products` and force all comparison through the
direct endpoint, requiring the UI/Gateway to resolve product names before calling it — rejected;
it would break the natural conversational "compare X and Y" flow, which is still valuable and,
per §1's tool-boundary rule, was never the source of incorrect math to begin with (only the
resolution-to-ids step was previously fragile, and that's fixed independently — see the free-text
`search_products` matching fix). (b) Generate the explanation inline as part of the same call that
computes the comparison (single LLM+compute pass) — rejected in favor of two clearly separated
calls, so the deterministic computation can be measured, tested, and consumed (SC-010) completely
independently of whether narration succeeds, fails, or is even requested.

## 15. Session memory of prior search/recommendation/comparison results (FR-022/SC-012)

**Decision**: `ConversationSession` gains `LastSearchResults: IReadOnlyList<SearchResultReference>`
(`ProductId` + `Name` only — not full specs/pricing, which are re-fetched when actually needed),
set whenever `search_products`, `get_recommendations`, or `compare_products` produces a candidate
list, and **replaced** (not appended to) on every new one. This gives the orchestrator a single,
consistent, bounded place to resolve an ordinal follow-up ("the first two", "the cheaper one")
against concrete product ids before calling `compare_products`/`get_product_details` — the LLM
still does the (legitimate) language-understanding work of matching "the cheaper one" to a
position in the list, but the list itself is exact, not reconstructed from prior prose.

**Rationale**: Generalizes the pattern `ConversationSession.LastRecommendation` already
established for US3 follow-ups, rather than adding a second, parallel memory field with different
semantics. Capping to the single most recent result (not a history) keeps session storage bounded
regardless of how long a conversation runs.

**Alternatives considered**: (a) Keep relying on the LLM re-reading the conversation transcript to
recover which products were shown — rejected; it's exactly the reliability gap this whole revision
exists to close, and degrades further as a conversation gets longer. (b) Store the full
`ProductCandidate`/`ComparisonRow` objects (specs, price, availability) in session memory instead
of just id+name — rejected as unnecessary duplication of data Catalog/Pricing already own and
that can go stale; a follow-up that needs a full detail re-fetches it fresh, which also means the
answer reflects current price/availability, not what was true when the list was first shown.

## 16. Observability backend for logging, tracing, and metrics (FR-027/FR-028/FR-032/SC-019)

**Decision**: Keep the already-adopted OpenTelemetry SDK (tracing, metrics, logging providers;
`AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddRuntimeInstrumentation`,
correlation id) in every service, but point its OTLP exporter at a real, hosted,
free-tier-friendly OTLP-compatible backend (e.g., Grafana Cloud's free tier, which accepts logs,
traces, and metrics over OTLP in one place) for deployed environments, configured purely through
`OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` environment variables (`sync: false`
in `render.yaml`). Locally, the .NET Aspire Dashboard remains the default view — no change to
local dev. Server start/stop is already emitted by the ASP.NET Core Generic Host's own lifecycle
logs (`Microsoft.Hosting.Lifetime`); this decision ensures those, request-tracing spans, and
error-level logs actually reach a durable, queryable backend instead of only a container's
ephemeral stdout. A minimal global exception-handling middleware is added to the four API
services (Catalog, Pricing, Advisor, Gateway) — mirroring `WebApp.Blazor`'s existing
`UseExceptionHandler` — so every unhandled exception is logged once, with the request's
correlation id attached, and returns a clean `application/problem+json` response instead of a
framework-default page or an unlogged bare 500.

**Rationale**: Directly satisfies "it is better to use commonly used tools or integrations for
this" without introducing a second, competing logging mechanism alongside OpenTelemetry (which is
already the constitution's Principle VI direction, research.md §7) — OpenTelemetry's OTLP
protocol is itself the industry-common integration point, and Grafana Cloud (or any OTLP-
compatible SaaS with a free tier) is a commonly-used destination for it. Exporting to a real
backend is the only gap between what already exists and "commonly used tools" being genuinely
satisfiable — the SDK, instrumentation, and correlation-id propagation are already built
(research.md §7, and the Phase 6 correlation-id-scope-rendering fix).

**Alternatives considered**: (a) Serilog with a file/console sink — rejected; would duplicate
OpenTelemetry's logging provider with a second, differently-configured mechanism for no added
capability, and Serilog's own OTLP sink would just re-wrap what OpenTelemetry's exporter already
does natively. (b) Self-hosted Seq via a new docker-compose/Render service — rejected as the
default; it's a genuinely good local logging UX but is one more container to deploy, pay for, and
keep healthy on Render, and only covers logs (not traces/metrics) — left as a documented
alternative for local-only use if a contributor prefers it, not the production path. (c)
Provider-specific SDKs (e.g., a vendor's proprietary agent) instead of OpenTelemetry — rejected;
would violate the swappability this system already relies on for the LLM provider (research.md
§10) and lock observability to one vendor.

## 17. User authentication: Google sign-in, validated independently at the Gateway (FR-030/FR-031/SC-017/SC-018)

**Decision**: `WebApp.Blazor` adds ASP.NET Core cookie authentication plus an OpenID Connect
challenge against Google's OIDC endpoint, so every page requires a signed-in Google account
(FR-030) before rendering. After sign-in, `WebApp.Blazor` attaches the user's Google-issued ID
token as a Bearer token on every call it makes to `Gateway.Api`. `Gateway.Api` independently
validates that same token — via `Microsoft.AspNetCore.Authentication.JwtBearer` configured
against Google's OIDC discovery document (`https://accounts.google.com/.well-known/openid-configuration`)
— on every endpoint, rather than trusting that a call which merely arrived from the WebApp's
network address is legitimate. The token's `sub` claim becomes the durable `UserId`;
`ConversationSession` gains a `UserId` set once at creation from the caller's validated identity
(FR-031, data-model.md), and every session-scoped endpoint (`GET /api/conversations/{id}`,
`POST /api/conversations/{id}/messages`, etc.) checks the requesting identity against the
session's owner, refusing (404, not 403 — never confirm a session id exists to a non-owner) on
mismatch.

**Rationale**: Independent validation at the Gateway means the identity check doesn't rely on
"only the WebApp can reach the Gateway" staying true forever — a future second client (a mobile
app, a partner integration) authenticates the same way without the Gateway's trust model
changing, and a compromised or misconfigured internal network segment can't impersonate a user
just by being able to route a request to the Gateway. This is the same "never trust network
position alone" posture research.md §18 applies to the internal boundary, applied here to the
user-facing one. Google was named explicitly by the requirement (not a generic "any OAuth
provider"), and Google's OIDC support means no custom token-issuance code is needed anywhere in
this system — both `WebApp.Blazor` and `Gateway.Api` validate against Google's own published
keys/discovery document.

Since Catalog/Pricing/Advisor never see the user's Google token directly (only Gateway does),
Gateway forwards the validated `UserId` (the token's `sub` claim) to Advisor as a trusted internal
header (`X-User-Id`) alongside the internal API key (research.md §18) whenever it calls an
Advisor endpoint that creates or touches a session. Advisor trusts this header's value *only*
because the internal API key already establishes that the caller is Gateway — the same
"trusted subsystem" pattern the correlation id already uses (research.md §7), applied to
identity instead of tracing.

**Alternatives considered**: (a) WebApp-only validation, with the Gateway trusting any call as
already-authenticated because it's "internal" — rejected per the explicit requirement that the
Gateway also validates the Google identity, and because it would make the Gateway's own API
contract silently depend on which client happens to be calling it today. (b) A custom
username/password or magic-link auth system — rejected; the requirement explicitly names Google,
and building bespoke credential storage would also newly trigger PII-handling obligations this
system's Clarifications session (spec.md) deliberately avoided. (c) Session-cookie-only auth
between WebApp and Gateway (no Bearer token forwarding) — rejected; it would mean the Gateway
never sees the user's actual identity, making FR-031's cross-user session check impossible to
enforce at the Gateway layer, only in the browser/WebApp, which a direct API call could bypass.

## 18. Internal service-to-service authentication: a shared API key (FR-029/SC-016)

**Decision**: A single shared secret (`InternalApiKey`, one value per environment, injected via
environment variable / Aspire parameter / Render `sync: false` env var) is attached as a header
(e.g. `X-Internal-Api-Key`) by a `DelegatingHandler` registered on every outbound `HttpClient`
that calls another internal service — Gateway→Advisor, Gateway→Catalog, Gateway→Pricing,
Advisor→Catalog, Advisor→Pricing — mirroring exactly how `CorrelationIdHandler` already attaches
the correlation id to every outbound call (research.md §7). A small piece of inbound middleware,
added to Catalog/Pricing/Advisor's (and, for its own internal-facing routes, Gateway's) request
pipeline, validates the header's presence and value before any other request handling runs,
returning `401` immediately on a missing or incorrect key. Catalog and Pricing — which are never
called by anything except Gateway or Advisor, never directly by a browser — require *only* this
key; they have no Google-identity concept at all.

**Rationale**: Matches "every service-to-service call" from the clarifying answer literally and
uniformly — one policy, one shared secret, applied the same way everywhere, rather than a mixed
policy that's harder to reason about or audit. Piggy-backing on the existing
`DelegatingHandler`/middleware pattern already established for the correlation id means this adds
no new architectural concept, just a second header following the same mechanism.

**Alternatives considered**: (a) Per-service-pair keys (a distinct secret for each caller→callee
pair) — rejected as disproportionate key-management overhead for this system's scale (5
services, one shared trust domain); revisit only if services genuinely need independent
revocation, which nothing here currently requires. (b) mTLS between services — rejected; Render's
free-tier container networking and this project's demo scope don't warrant the certificate
issuance/rotation machinery mTLS requires, and a shared API key gives the same "reject
unauthenticated internal traffic" guarantee FR-029 actually asks for. (c) Reusing the Google
identity token for internal calls too (skip a separate internal credential) — rejected; Catalog
and Pricing have no reason to understand Google's token format or validate against Google's
discovery document, and it would couple purely-internal services to a user-facing identity
provider for no benefit — the internal boundary and the user-facing boundary are deliberately
independent mechanisms answering different questions ("is this a legitimate internal caller?" vs.
"is this a legitimate signed-in user?").

## 19. Startup readiness ("loading screen"): an aggregate status endpoint on Gateway (FR-033/FR-034/FR-035/SC-020/SC-021)

**Decision**: Gateway exposes a new, unauthenticated (`AllowAnonymous`, same posture as `/alive`
— research.md §16/§18) endpoint, `GET /api/system-status`, that concurrently calls Catalog's,
Pricing's, and Advisor's existing `/alive` endpoints (`Task.WhenAll`, the same pushdown-
composition pattern as `GET /api/products/{productId}`, research.md §13) with a short per-call
timeout, and returns a per-service reachable/unreachable status plus an overall
`ready`/`degraded` summary. No new health-check mechanism is introduced — this endpoint only
*aggregates and exposes* the same liveness signal FR-028 already requires every service to
expose, from the one place (Gateway) the WebApp is allowed to call at all (constitution's
single-entry-point rule, plan.md Summary). WebApp.Blazor polls this endpoint on initial load,
shows a starting-up state while doing so, and — after a bounded wait (a small, fixed number of
attempts/seconds) — proceeds to the interactive chat UI regardless of outcome, surfacing which
service(s) are still unreachable if any are (FR-034).

**Rationale**: Reuses existing infrastructure (each service's `/alive`, the Gateway's established
fan-out-and-merge pattern) rather than inventing a second "are you up" protocol. Keeping this
endpoint anonymous — like `/alive` itself — means the startup check can run *before* the Google
sign-in redirect completes too, so probing it can help mitigate cold starts (see below) as early
as possible in the visit, not only after authentication finishes. Bounding the wait (rather than
blocking until every service responds) is the same "never let observability/readiness concerns
fail a user-facing flow" posture FR-032 already establishes for telemetry, applied to startup.

**A useful side effect, not a guarantee**: on a host where a free/idle service can go to sleep
(e.g., Render's free tier), the act of this endpoint probing each service's `/alive` is itself a
request that prompts a sleeping service to wake up — so by the time the shopper's first real chat
message is sent, the service has likely already been "warmed" by the startup check. This project
does not add a test asserting Render specifically wakes up faster because of this — that would be
testing a third party's infrastructure behavior, not this system — it's simply a beneficial
consequence of a check the system needs anyway.

**Alternatives considered**: (a) WebApp calling Catalog/Pricing/Advisor's `/alive` endpoints
directly, bypassing Gateway — rejected; it breaks the established "Gateway is the single entry
point, WebApp never calls the others directly" rule (plan.md Summary) for no benefit, and would
require exposing three more hostnames to the browser-facing app. (b) Blocking indefinitely until
every dependent service reports healthy — rejected; a genuinely down dependency would leave the
shopper stuck on a starting-up screen forever, which is exactly the kind of silent, unbounded
failure the constitution's Principle V ("honest partial response, not complete failure") already
rules out elsewhere in this system. (c) A dedicated, separate "warm-up service" or scheduled
background job pinging every service on a timer — rejected as disproportionate infrastructure for
this system's scale; the on-demand, per-page-load check already accomplishes the same warm-up
side effect without an always-running extra process.
