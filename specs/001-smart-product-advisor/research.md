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

**Update (research.md §32, FR-133–FR-137)**: this pipeline's *transport* (OpenTelemetry, OTLP,
Grafana Cloud) is unchanged — the new requirements are a content-level allow/deny policy for what
gets logged/traced/metered through it for a conversational turn specifically, not a different
mechanism. The correlation id this section already establishes is item one on FR-133's allow-list
unchanged; what's new is an explicit boundary around everything else a turn-cycle log entry might
otherwise be tempted to include.

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

**Update (research.md §31, FR-124–FR-132)**: this decision's shared-secret baseline still holds
at this system's current scale, but two things are now sharpened rather than left implicit —
rotation is now a required *capability* (an old/new overlap window, not only "redeploy with a new
value" as a description of what already happens to work) and alternative (a) above ("per-service-
pair keys... revisit only if services genuinely need independent revocation") is now recorded as
this system's *preferred future direction* (FR-129, SHOULD) rather than a rejected alternative
with no documented path back to it. Neither change reopens (b) or (c) above, whose rejections
stand unchanged.

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

## 20. Turn-processing cycle: a fixed, application-controlled pipeline — explicitly not a free ReAct loop (FR-036–FR-047)

**Decision**: Every conversational turn is processed through one fixed, ordered, ten-stage
cycle — input validation → structured intent extraction → schema validation → deterministic
state merge → policy routing → intent-specific tool recipe → tool-result validation →
constrained narration → output validation → persistence — executed exactly once per turn, never
looped, reordered, or partially skipped. The **application layer** (`ProductAdvisor.Application`)
owns every transition between stages: it decides when a stage's output is valid enough to
advance, and it decides which stage runs next. The language model is invoked in exactly two of
the ten stages — structured intent extraction (turning the raw message + prior state into a
named intent and slot values) and constrained narration (describing an already-validated tool
result) — and has **no authority over control flow itself**: it cannot decide to skip a stage,
repeat one, call a tool the application layer's policy-routing/recipe step didn't select for the
current route, or decide "I'm not done yet, let me act again." Policy routing (which route a
merged state maps to) and the intent-specific tool recipe (the fixed call sequence for that
route) are both deterministic application-layer logic, not language-model choices. As before
(research.md §1), all product-data computation stays exclusively inside deterministic tools —
this cycle does not relocate that boundary, it only makes explicit and enforced, stage by stage,
everything that happens *around* it.

**Rationale**: A fixed, application-owned cycle is auditable and independently testable stage by
stage, gives every possible failure (bad input, malformed intent, invalid tool result, malformed
output) exactly one defined, honest outcome instead of an ad hoc one per code path, and removes
any possibility of unbounded model-driven looping or a model choosing an intent-inappropriate
tool sequence. It also closes a real gap in what this system does *today*: `research.md §1`
describes the current implementation as feeding the model the full tool catalog for a turn and
letting it "decide *when* to call" any of them — and `Microsoft.Extensions.AI`'s
`UseFunctionInvocation()` (`AdvisorAiExtensions`, research.md §10) implements this via an
internal loop that can call a tool, feed the result back to the model, and let the model request
another tool call, repeating until the model stops — which is, mechanically, a bounded ReAct-style
loop already, even though it is invisible from the orchestrator's point of view (one call to
`GetResponseAsync` per turn). FR-036–FR-047 close that gap deliberately: the fixed
per-route recipe (FR-042) replaces open tool-selection with a predetermined call sequence, and
FR-047 states outright that no stage of this cycle may be re-entered mid-turn at the model's
discretion.

**This is a requirements change, not yet a completed migration.** Writing this cycle down does
not retroactively make the current `ConversationOrchestrator`/`UseFunctionInvocation()`
implementation compliant with it — aligning the implementation (introducing an explicit intent-
classification step, a schema for its output, a routing table, and per-route fixed recipes in
place of open function-invocation) is follow-up implementation work, tracked separately from this
specification change.

**Alternatives considered**: (a) Keep today's open function-invocation loop and just document it
more thoroughly — rejected; "the model decides which tools to call, how many times, in what
order" is exactly the free-ReAct shape this requirement exists to rule out, no amount of
documentation changes that its failure modes (unbounded looping, intent-inappropriate tool
choice, narrating from an uninspected intermediate result) remain possible. (b) Let the language
model own routing (e.g., a system-prompt instruction like "decide which of these paths to take")
instead of deterministic policy routing — rejected; this is the same "never let the LLM be the
only place a decision can go wrong" reasoning already applied to product-data computation
(research.md §1) and comparison composition (research.md §14), just applied to *control flow*
instead of *arithmetic*. (c) Validate only the final output and skip intermediate-stage
validation (schema validation on extracted intent, tool-result validation) — rejected; catching
an invalid intermediate result late (at output validation) means the system may have already
spent a tool call or a narration call on bad data, and produces a less specific, less honest
failure than catching it at the stage where it actually went wrong.

## 21. Structured-intent-extraction output contract: closed schema, one repair attempt, confidence-gated clarification (FR-048–FR-054/SC-028–SC-031)

**Decision**: The structured-intent-extraction stage (research.md §20) produces exactly one
typed shape, `StructuredIntent` (data-model.md): a closed six-value `Intent` enum
(`Recommend`/`ProductFact`/`Compare`/`Checkout`/`Smalltalk`/`Unsupported`), a
`RequirementPatch`, `ProductReferences`, `MissingFields`, a `Confidence` value, and a `Language`
tag — validated against one formal schema before anything downstream may use it. On a schema
failure, the system re-prompts **exactly once** ("repair"); a second failure — or any failure
with no repair available — routes the turn to a clarification response instead of proceeding.
Any chain-of-thought the model emits while extracting is discarded before this shape is
constructed; it is never part of the API contract and never persisted. A `Confidence` below a
configured threshold is handled identically to `MissingFields` being non-empty: the turn asks a
focused clarification rather than merging the (uncertain) patch into session state.

**Rationale**:

- **Closed intent enum, not a free string**: policy routing (FR-041, research.md §20) is a
  deterministic function from state to route; a deterministic function needs a finite input
  domain. An open-ended intent string would push the "which route is this?" decision back onto
  pattern-matching arbitrary text, reintroducing exactly the kind of implicit, hard-to-audit
  judgment call this cycle exists to remove. Six values were chosen because they exhaustively
  cover this system's actual capabilities (`Recommend`/`Compare`/`ProductFact`/`Checkout` map
  onto User Stories 1–4) plus two explicit "not one of those" values (`Smalltalk` for a message
  with no product intent at all, `Unsupported` for a recognizable but out-of-scope ask) — so
  every message has a defined classification, and "we don't know what this is" is itself a valid,
  representable outcome rather than a forced, ill-fitting guess.
- **Exactly one repair attempt, not zero and not unbounded**: zero repair attempts would send
  every transient extraction slip (a model producing almost-valid JSON) straight to a
  clarification question, degrading the experience for failures that a single retry would likely
  fix. Unbounded retries would reintroduce the same "model decides how long to keep trying"
  shape this cycle rules out for tool-calling, and would violate this system's existing
  performance goals (plan.md) and its "avoid unnecessary LLM calls" posture (constitution) by
  letting one turn silently spend an unbounded number of model calls. One repair is the smallest
  number that meaningfully reduces false clarifications without reopening either problem.
- **Chain-of-thought excluded from the contract and from storage**: intermediate reasoning a
  model produces is not itself a verified fact — persisting or exposing it would create a second,
  unaudited channel that looks like grounded output but isn't checked against anything, directly
  undermining this system's core grounding guarantee (research.md §1, constitution Principle II).
  It also has no product purpose here: nothing downstream of extraction needs *why* the model
  chose an intent, only the validated intent itself. Excluding it is also the cheaper, safer
  default as providers increasingly discourage depending on or surfacing raw reasoning traces.
- **Low confidence routes to clarification, never a guess**: this is the same "no full match →
  say so, don't relax the constraint" honesty this specification already requires elsewhere
  (FR-010, FR-005) applied one stage earlier — an uncertain reading of what the user wants is
  treated exactly like *missing* information about what they want, because from the system's
  perspective they are the same problem: not enough to act on responsibly.

**Alternatives considered**: (a) An open, free-text `intent` field the routing stage parses
heuristically — rejected for the reasons above; it reintroduces an unaudited decision point.
(b) Retrying extraction until it validates (no repair limit) — rejected; unbounded cost/latency
and reintroduces an open-ended model-controlled loop. (c) Including a `reasoning`/`rationale`
field in the schema for debuggability — rejected; even a schema-sanctioned reasoning field is
still unverified model prose that must never be mistaken for a grounded fact, and this system
already has a dedicated, safer mechanism for observability (structured logs/traces, FR-027) that
doesn't risk that confusion. (d) Applying a `RequirementPatch` to session state even when
`Confidence` is low, then asking a clarifying follow-up afterward — rejected; it would let an
uncertain interpretation briefly become "current state" (visible to whatever runs next) before
being confirmed, violating FR-050's "invalid/uncertain output must not be used" posture in
spirit even when the schema technically validates.

## 22. Conversation state management: `CurrentRequirement` as sole source of truth, field-level deterministic merge (FR-055–FR-059/SC-032–SC-035)

**Decision**: `ConversationSession.CurrentRequirement` (data-model.md `UserRequirement`) is the
single authoritative record of the user's category, budget/currency, hard constraints (required
features), soft preferences, language, units, and availability requirements — for every stage
after deterministic state merge (research.md §20) and for every subsequent turn. Every
schema-valid `RequirementPatch` (research.md §21) is merged into it by one fixed rule: a field the
patch supplies replaces the corresponding field; a field the patch omits leaves the existing value
untouched. For the three list-typed fields only (`RequiredFeatures`, `Preferences`,
`AvailabilityRequirements`), an explicitly empty list in the patch is a real clear, distinguished
from the field's absence from the patch at all. No stage downstream of state merge is permitted to
re-derive any of these values by re-reading the raw message or the full conversation transcript —
they read `CurrentRequirement` instead.

**Rationale**:

- **One authoritative source, not "reconstruct from transcript"**: this system's models already
  scale linearly with turn count if every stage has to re-parse the whole conversation to find
  out what's currently true; it also reopens exactly the kind of implicit, per-call judgment call
  the turn-processing cycle (research.md §20) was introduced to remove — two turns with the same
  transcript could plausibly reconstruct slightly different "current" requirements depending on
  model variance. A single structured field, updated deterministically once per turn, removes
  that variance and keeps every later stage's read O(1) instead of O(transcript length).
- **Replace-on-presence, carry-forward-on-absence, not "patch replaces the whole object"**: users
  build up a requirement incrementally ("smartphones" → "up to 15000 UAH" → "good camera") across
  turns, per this system's existing multi-turn assumption (spec.md Assumptions, FR-011). If a
  patch that only mentions budget silently reset category, preferences, and everything else to
  unknown, the shopper would have to restate their entire requirement on every turn — directly
  contradicting FR-011's "preserve... until the user explicitly changes them." Field-level,
  presence-gated replacement is the only merge rule consistent with that existing guarantee.
- **Explicit-empty-list-as-clear, for list fields only**: scalar fields (`Category`, `Budget`,
  `Language`, `Currency`, `Units`) have no meaningful "user explicitly wants this unset" state in
  this system's scope — a category or budget is stated, changed, or not yet known, never
  deliberately nulled. List fields are different: a user can concretely say "actually, camera
  quality doesn't matter anymore," which is a real, distinct signal from simply not mentioning
  camera quality this turn. Modeling only list fields as clearable, and only via an explicit empty
  list rather than a sentinel value, keeps the merge rule simple while still representing the one
  case where "the user removed a constraint" is a real, expressible outcome.
- **`CurrentRequirement` as the only place these fields live**: mirrors the same reasoning already
  applied to `LastSearchResults` (research.md §15) and to product-data grounding generally
  (research.md §1) — a single, deterministically-maintained structure that later stages trust
  completely is easier to test, easier to audit, and removes an entire class of bug (a stage that
  reads a stale or inconsistently-reconstructed view of "what does the user want") that a
  transcript-re-derivation approach cannot rule out even with a very careful prompt.

**Alternatives considered**: (a) Re-deriving the active requirement from the full transcript on
every turn instead of maintaining `CurrentRequirement` — rejected; non-deterministic across
otherwise-identical turns, unbounded cost growth as a session lengthens, and reintroduces the
model as an implicit, unaudited source of "current state" the turn-processing cycle was designed
to remove. (b) Treating every `RequirementPatch` as a full replacement of `CurrentRequirement`
(no field-level merge) — rejected; forces the extraction stage to restate every already-known
field on every turn just to avoid losing it, which is both wasteful and fragile (an extraction
that forgets to restate a field would silently erase it, with no way to distinguish "forgot" from
"user wants it cleared"). (c) A generic "absent means clear" rule for every field, including
scalars — rejected; a `requirementPatch` that mentions only budget would silently erase category,
which no user consented to and which no other part of this system does elsewhere (FR-011
explicitly requires persistence "until the user explicitly changes them," not until any patch
merely fails to repeat it). (d) A separate explicit "clear" sentinel/flag per field instead of
reusing "explicitly empty list" for list fields — rejected as unnecessary complexity: an empty
list already unambiguously means "no items," so no additional sentinel is needed for the one
field-type where clearing is a meaningful, expressible action.

## 23. Turn result types: seven mutually exclusive outcomes, assigned by policy and tool outcome, never by narration (FR-060–FR-065/SC-036–SC-040)

**Decision**: A completed turn's response (`TurnResult`, data-model.md) is one of exactly seven
mutually exclusive types — `answer`, `clarification`, `recommendation`, `comparison`,
`checkoutLink`, `unsupported`, `error` — assigned from two inputs only: which route policy
routing selected (research.md §20, FR-041) and what that route's tool recipe actually returned
once tool-result validation passed or failed (FR-043). `clarification` is never a default for
"none of the other types fit"; it is produced only by an explicit missing-information or
ambiguous-reference determination. `answer` is a new first-class type for a validated
`product_fact` lookup (previously undifferentiated from `clarification` in the contract) and
also covers a `smalltalk` reply. `unsupported` and `error` give the two `StructuredIntent`
values and the two failure conditions that already existed in the specification's language
(`unsupported` in the closed intent set since research.md §21; tool-result-validation failure
since FR-043) their own representable outcome in the response contract, instead of only being
describable in prose.

**Rationale**:

- **A closed, exhaustive set of result types mirrors the closed intent set**: research.md §21
  already established that an open-ended `intent` string would reintroduce an unaudited
  judgment call at the extraction boundary; the same problem exists one layer downstream if the
  *response* contract only formally defines some outcomes and leaves the rest to be inferred
  from whatever the LLM's narration happens to say. Defining all seven up front closes that gap
  symmetrically.
- **`clarification` must not be a silent catch-all**: before this change, the specification's
  prose described several distinct "didn't produce a recommendation/comparison/checkout" cases
  (unsupported request, tool failure, low confidence) using language that could plausibly all
  route through the same `clarification` shape, since it was the only "something else" type
  available. That conflates three semantically different situations — "I need more information
  from you" (clarification), "this isn't something I can help with at all" (unsupported), and
  "something failed on my end" (error) — which need different UI treatment and different retry
  guidance; a shopper asked a `clarification`-shaped question for an out-of-scope request would
  reasonably try to answer it, when no answer would ever unblock the request.
- **`answer` as a first-class type, not a `clarification` or `recommendation` variant**: a
  successful `product_fact` lookup (US3 — "what's the camera resolution on X?") is neither a
  question back to the user (`clarification`) nor a ranked set of candidates (`recommendation`);
  giving it its own type lets the response contract match the shape of what actually happened,
  consistent with this system's existing "structured data, not prose-only" posture (FR-017).
- **`error` at `200`, not `503`, for in-turn failures**: constitution Principle V requires a
  partial/degraded outcome to be an honest partial *response*, not a failed *request* — and this
  system already retains every turn's structured rendering in conversation history (FR-023,
  research.md — none of the other six types are ever delivered as a bare HTTP error status for
  the same reason). A `5xx` for a total-outage turn was an inconsistency versus that existing
  posture: it made "this exact one failure mode" invisible to conversation history and to the
  client code path that renders `type`-discriminated turns, purely because of how it happened to
  be transported. Moving it in-band as `error` (with a `degraded` indicator distinguishing
  retryable-now from not-fulfillable-at-all) removes that special case.
- **Type determined by policy + tool outcome, never by narration**: this directly extends
  FR-019/FR-044's existing rule that narration can describe but never alter, invent, or omit a
  structured value — applying the identical guarantee one level up, to *which type of result* a
  turn even is. Without this, two turns with byte-identical routing and tool outcomes could
  render differently to the client purely because the LLM phrased its narration differently,
  which would make the response contract non-deterministic in a way nothing else in this system
  is (research.md §20's routing determinism, §21's schema validation, §14's byte-identical
  comparison computation all establish the same non-negotiable property elsewhere).

**Alternatives considered**: (a) Keep `clarification` as the implicit fallback for anything that
isn't `recommendation`/`comparison`/`checkoutLink` — rejected; conflates "ask the user for more"
with "this can't be helped" and "something failed," each of which needs different client
handling and different user-facing framing. (b) Fold `product_fact` answers into the existing
`recommendation` shape as a single-item, unscored recommendation — rejected; a factual lookup was
never ranked or filtered against a budget, so forcing it into `RecommendedItem`'s shape
(`score`, `matchedRequirements`, `tradeOffs`) would either leave those fields meaningless or
require fabricating them, and a first-class `answer` type with a `fact` field says exactly what
happened. (c) Keep total-outage turns as a `503` HTTP status rather than an in-band `error` type
— rejected per the "error at 200" rationale above; it was the one outcome inconsistent with this
system's otherwise-uniform "every turn produces a typed, renderable result" posture. (d) A single
combined `error`/`degraded` boolean flag layered onto every other type instead of a dedicated
`error` type — rejected; the other six types already have their own way to represent partial
degradation where meaningful (`priceVerified`/`availabilityVerified`/`verified: false`,
FR-005) — a separate flag on every type would duplicate that mechanism rather than covering the
one case those per-field flags can't: when *no* type-specific result exists to attach a flag to
at all.

## 24. Per-intent tool recipes: minimal, fixed, scoped tool exposure and its concurrency rules (FR-066–FR-070/SC-041–SC-046)

**Decision**: Each of the four product-related routes (`product_fact`, `recommend`, `compare`,
`checkout`) has exactly one fixed recipe drawn from this system's seven-tool MCP catalog
(`contracts/advisor-mcp-tools.md`), never the full catalog: `product_fact` resolves an id then
calls only whichever of `get_product_details`/`check_price_and_availability` the specific fact
needs; `recommend` validates+normalizes `CurrentRequirement` then calls `get_recommendations`
exactly once; `compare` resolves ids then calls `compare_products` exactly once; `checkout`
resolves+validates ids then calls `generate_checkout_link` exactly once. `smalltalk` and
`unsupported` invoke no tool at all. The tool-list surface actually presented for a turn — not
merely the sequence eventually called — is scoped to that route's recipe before any
language-model call is made. Within a recipe, independent read-only resolution calls may run
concurrently when their outcome is guaranteed order-independent; a stateful tool call (none exist
yet) must never overlap a compute tool call (`get_recommendations`/`compare_products`/
`generate_checkout_link`) or another stateful call.

**Rationale**:

- **Minimal recipes, not the full catalog, per route**: FR-042 (research.md §20) already
  requires a fixed, predetermined tool sequence per route so the language model can't choose an
  arbitrary tool or ordering; this section makes that concrete per intent rather than leaving
  "which tools does `product_fact` actually need" implicit. Naming the exact tools per route also
  closes a gap the original ten-stage cycle language left open: FR-042 constrained *how many*
  calls and *in what order*, but not *which subset of the catalog* is even eligible.
- **Scoping the exposed tool-list surface, not just the eventual call sequence**: a model that
  can *see* `generate_checkout_link` while processing a `recommend` turn — even if it never
  happens to call it — is a wider attack/error surface than one that never sees it: a prompt
  injected into product data, a model bug, or a future prompt change could otherwise cause an
  unintended call that a purely "we only log/reject the wrong sequence after the fact" approach
  would only catch after the call already happened. Scoping the *list itself* per turn, before
  the model is invoked, is a stronger and cheaper guarantee than post-hoc validation alone (which
  FR-043 already requires regardless, as defense in depth).
- **`smalltalk`/`unsupported` see zero tools**: these two intents exist specifically to give "not
  a product request" a first-class classification (research.md §21) rather than forcing every
  message through a product-tool path; giving them tool access at all would reopen exactly the
  ambiguity that closed intent set was introduced to remove, and serves no purpose since neither
  route ever needs product data by definition.
- **Read-only concurrency, compute/stateful serialization**: this mirrors a pattern already
  established elsewhere in this system (`Task.WhenAll` for independent per-service or per-id
  lookups — research.md §19's startup check, the product-detail endpoint's pushdown composition)
  — concurrency is safe precisely when results don't depend on each other and the outcome is
  identical regardless of timing (the same guarantee FR-018/SC-010 already requires of
  `compare_products`). A recipe's terminal compute call, by contrast, is defined as *the* final
  result for that turn (FR-042's "intent-specific tool recipe... executes exactly once per
  turn") — running it concurrently with another compute or a stateful call would make "which
  result is final" ambiguous, and a stateful call racing a compute call risks the compute call
  observing a half-mutated state, which is precisely the kind of hard-to-audit non-determinism
  the whole turn-processing cycle (research.md §20) exists to rule out.
- **No stateful tool exists today, and the rule is included anyway**: this system's constitution
  (Principle V, Principle II) and this cycle's own determinism goals apply regardless of whether
  a stateful tool has been built yet; fixing the concurrency rule now, while the catalog is still
  entirely read-only/compute, means a future stateful tool (e.g., reserving inventory) is added
  into an already-defined safety rule rather than requiring a fresh specification pass to notice
  the gap after the fact.

**Alternatives considered**: (a) Advertise the full seven-tool catalog to every turn and rely
solely on the model's system prompt/instructions to self-restrict to the "right" tools —
rejected; this is exactly the kind of implicit, unaudited trust in model behavior the
turn-processing cycle (research.md §20) and closed intent set (research.md §21) were introduced
to remove, and it gives no defense if the model deviates. (b) Enforce the recipe only by
rejecting an out-of-recipe tool call after the fact (post-hoc validation) without also scoping
the exposed list — rejected as insufficient alone; FR-043's tool-result validation already
provides this as defense in depth, but scoping the list first is strictly stronger and prevents
the unintended call from being attempted at all, not just from succeeding. (c) Allow
`get_recommendations`/`compare_products` to run concurrently with each other when a turn's intent
is ambiguous between the two — rejected; FR-041/FR-049 already require the route to be resolved
to exactly one of the closed intent values before any tool recipe begins, so no turn ever needs
to speculatively run two different routes' terminal compute tools at once. (d) Treat
`generate_checkout_link` as a read-only tool since it makes no database write — rejected;
classifying it as *compute* (a deterministic construction whose result the recipe treats as the
turn's final structured output) rather than *read-only* (a lookup a recipe may run before its
terminal call) keeps the two-kind distinction meaningful by role in the recipe, not merely by
"does it touch a database."

## 25. Turn-level resource budgets: hard limits and fail-safe behavior on top of per-call resilience (FR-071–FR-079/SC-047–SC-055)

**Decision**: A `TurnResourceBudget` (data-model.md) of nine hard limits governs every turn:
at most two primary LLM calls (extraction, narration) plus at most one repair call; a configured
max tool-call count; a prohibition on uncontrolled identical-call repetition; a configured max
loop-iteration count for any bounded loop realizing a recipe; a configured max consecutive
tool-error count; a configured overall turn timeout; cancellation of in-flight work on client
disconnect (with release of the FR-024 in-flight-turn marker and no persistence); and exclusion
of non-idempotent operations from automatic resilience-layer retry. Every limit's *existence* and
*fail-safe outcome* (ending the turn in the `error` result type, FR-060–FR-065, or the streaming
endpoint's equivalent) are fixed by the specification; the specific numeric value configured for
each is a deployment detail.

**Rationale**:

- **A named, closed set of budgets, not an implicit "be reasonable" expectation**: constitution
  Principle V already requires timeouts, controlled retries, and avoiding unnecessary calls, but
  left "how many is unnecessary" and "what happens when a limit is hit" unstated. Naming each
  budget explicitly, and requiring a defined fail-safe for each, closes the same kind of gap
  research.md §20's turn-processing cycle closed for control flow — an unstated expectation isn't
  testable; a named limit with a defined fail-safe is.
- **Two primary calls plus one repair, not "however many the model needs"**: this makes explicit
  what FR-038/FR-044/FR-051 (research.md §20/§21) already implied architecturally — a turn's
  language-model usage is small and fixed in shape, not proportional to how much "thinking" a
  particular message seems to need. Capping it explicitly, rather than leaving it as an emergent
  property of the fixed cycle, gives a directly testable ceiling (SC-047) independent of whether
  every individual stage is implemented exactly as specified elsewhere.
- **A turn-level consecutive-error circuit breaker, distinct from per-call resilience**:
  research.md §6 already retries one HTTP call with backoff; that policy has no visibility into
  how many *different* calls have failed across a turn. Without a turn-level budget, a turn could
  exhaust its per-call retries on tool A, then do the same on tool B, then tool C, each
  "succeeding" at the individual-call resilience layer's job while the turn as a whole spends far
  longer and more resources than any single call's policy was designed to bound. A turn-level
  consecutive-error count catches exactly that compounding case.
- **Cancellation on disconnect, tied explicitly to FR-024's marker**: this system already
  serializes turns per session (FR-024, one turn in flight at a time) specifically so two turns
  never interleave. A turn that keeps running after its client disconnected — and, worse, never
  releases the FR-024 marker — would make every *subsequent* message for that session hang behind
  a turn nobody is waiting on anymore, turning one abandoned request into a session-wide outage.
  Tying cancellation explicitly to marker release closes that specific compounding failure mode.
- **Non-idempotent operations excluded from automatic retry**: `Microsoft.Extensions.Http.Resilience`
  (research.md §6) retries transparently by default; that is safe for every tool in today's
  catalog because all seven are read-only or side-effect-free compute (research.md §24's
  Alternatives-considered already established `generate_checkout_link` as compute, not stateful).
  It would not be safe for a hypothetical future tool with a real side effect (e.g., reserving
  inventory): a transient network failure *after* the side effect already happened, followed by
  an automatic retry, would double the effect. Fixing this exclusion now — while it currently
  applies to zero tools — means a future stateful tool is added under an already-correct policy
  rather than silently inheriting a retry policy that was only ever safe for read-only/compute
  calls.

**Alternatives considered**: (a) Leave resource limits as an unstated operational/ops concern
outside the specification — rejected; "the system should be fast and not waste calls" (as
constitution Principle V already states) is not independently testable without naming what the
limits are and what happens when they're hit, which is exactly the gap SC-047–SC-055 close.
(b) Fix concrete numeric values in the specification itself (e.g., "max 5 tool calls") — rejected;
this system's constitution and existing spec.md Assumptions already treat exact thresholds
(confidence threshold, FR-053; schema versioning, FR-048/FR-050) as deployment/tuning detail, not
a specification-level constant, and resource budgets are the same kind of value — likely to need
different settings for a resource-constrained free-tier deployment (plan.md, research.md §9)
versus a production one. (c) Allow automatic retry of any tool call regardless of idempotency,
relying on downstream idempotency keys to absorb duplicates — rejected as premature; this system
has no stateful tool today to design an idempotency-key scheme for, and inventing one
speculatively would add complexity with no corresponding capability yet — the simpler, safe
default (exclude non-idempotent calls from automatic retry entirely) is sufficient until a
stateful tool actually exists. (d) Let a turn keep running to completion after client disconnect
(on the theory that the result might still be useful, e.g. for caching) — rejected; this system
caches nothing per-turn beyond `ConversationSession` state, which FR-046 already gates behind
successful output validation for a turn the *caller* is still waiting on; continuing to spend
LLM/tool budget on a request nobody will ever see contradicts constitution Principle V's "avoid
unnecessary LLM calls, tool calls" outright.

## 26. Hard constraint vs. soft preference semantics: a precise, closed-then-extensible eligibility rule (FR-080–FR-085/SC-056–SC-060)

**Decision**: "Hard constraint," for User Story 1 recommendations, is defined precisely rather
than left as a synonym for `RequiredFeatures`: it is the user's stated maximum budget (a
ceiling), every `RequiredFeatures` entry, an explicit `AvailabilityRequirements` entry (only when
the user stated one), currency compatibility between a candidate's price and the user's stated
`Currency`, and any other user-marked-mandatory constraint (captured, per research.md's own
existing `RequiredFeatures` shape, as a free-form entry in that same list). A product confirmed
to violate any one of these is excluded from `Recommendation.Items` outright — never merely
scored lower — and MAY instead appear in a separate `NearestAlternatives` list, each entry
explicitly labeled with which constraint(s) it violated. `Preferences` (soft) affect only
`RecommendedItem.Score`/ranking within the already-hard-constraint-filtered set — a product is
never excluded for failing a preference.

**Rationale**:

- **A precise definition closes a real ambiguity, not just a wording gap**: FR-007 already said
  "explicit hard constraints (e.g., a stated budget ceiling)" and FR-055/FR-059 already named
  "hard constraints" as one of `CurrentRequirement`'s authoritative fields, but neither pinned
  down whether currency mismatches or user-stated availability counted, or how a "constraint" a
  user marks mandatory but which isn't a pre-defined `RequiredFeatures`-shaped feature statement
  should be classified. Left implicit, this is exactly the kind of judgment call research.md
  §20/§21 already argue against leaving to per-turn model inference — a currency-incompatible or
  availability-violating product could otherwise be scored-but-not-excluded by an
  under-specified `ScoringPolicy`, silently presenting an unusable product as a match.
- **Excluded, not merely down-ranked**: this system's core grounding promise (constitution
  Principle II, research.md §1) already requires the advisor to "respect explicit constraints
  such as budget, required features, and availability" and to never "present a disqualified
  product... as a recommended match" (FR-007). A scoring-only approach (down-rank instead of
  exclude) would let a hard constraint violation be buried under enough soft-preference matches
  to still appear as a top-ranked "recommendation" — silently violating the constraint the user
  stated was non-negotiable. Filtering before scoring is the only way to guarantee FR-007's
  existing "MUST NOT present a disqualified product" holds regardless of how well an excluded
  candidate otherwise scores.
- **Currency compatibility as its own named hard constraint**: this system's existing "no
  currency conversion is assumed unless the user requests it" assumption (spec.md) already
  implies a currency-mismatched product's price isn't comparable to the user's stated budget at
  all — presenting it as a match (or even attempting a same-currency budget comparison against
  it) would be either silently wrong or an invented conversion, both violating FR-004's
  no-fabrication guarantee. Naming it explicitly as a hard constraint, rather than leaving it as
  an implicit consequence of "no conversion," makes the exclusion rule testable (SC-059) instead
  of merely implied.
- **Availability as conditionally hard, not always hard**: FR-012 already requires availability
  to be *shown* for every recommended/compared product regardless of what the user asked, but
  showing an out-of-stock product's status is different from *excluding* it. Making availability
  a hard constraint unconditionally would silently narrow every recommendation to only in-stock
  items even for a shopper who is fine ordering something currently out of stock and didn't ask
  to exclude it — over-constraining based on data the user never asked to be filtered by.
  Gating it on the user's explicit statement (`AvailabilityRequirements` non-empty) keeps FR-012
  informational by default and hard only when the user actually said so.
- **Nearest alternatives as optional and separately labeled, not merged**: constitution Principle
  IV already requires "when essential information is missing, the agent MUST ask a focused
  clarification rather than make assumptions" and, by the same honesty logic, presenting a
  disqualified product as if it were a qualifying one would be a worse failure than omitting it
  — but omitting *any* near-miss information entirely can also be less useful than the existing
  "explain what's blocking a match" (FR-010) allows. Allowing (not requiring) a labeled
  alternative list gives the honest middle ground already implied by FR-010's "explain what is
  blocking a match" without ever blurring the boundary between "this matches" and "this doesn't,
  here's why."

**Alternatives considered**: (a) Treat every `CurrentRequirement` field as potentially either
hard or soft depending on how strongly the user phrased it, inferred by the language model per
turn — rejected; this is exactly the unaudited, per-call judgment call research.md §20/§21 exist
to eliminate, and would make the same stated budget "hard" on one turn and "soft" on another
depending on model variance. (b) Score hard-constraint violations very low instead of excluding
them, relying on ranking alone to keep them out of view — rejected per the "excluded, not merely
down-ranked" rationale above; a low score is still an inclusion, and nothing prevents it from
surfacing when few or no fully-qualifying candidates exist, which is precisely the scenario FR-010
already requires an honest "no match" response for instead. (c) Make availability always a hard
constraint (ignore stock status categorically) — rejected; over-constrains recommendations for
shoppers who never asked for in-stock-only results, narrowing `Items` based on data the user
didn't request as a filter. (d) Silently convert a mismatched-currency price for comparison
purposes — rejected outright; this system assumes no currency conversion without the user
requesting it (spec.md Assumptions), and a silent conversion would be exactly the kind of
invented/assumed value FR-004 already prohibits. (e) Require `NearestAlternatives` to always be
populated when `Items` is empty — rejected; the tool handler may have no near-misses worth
surfacing (e.g., zero candidates in the category at all), and forcing a non-empty list would
pressure the deterministic handler into fabricating marginal "alternatives" just to satisfy the
shape, which contradicts FR-004's no-fabrication guarantee.

## 27. Evidence Envelope: a deterministic, checked boundary between tool results and narration (FR-086–FR-092/SC-061–SC-067)

**Decision**: Between tool-result validation and constrained narration, the application layer
assembles an `EvidenceEnvelope` (data-model.md) — result type, canonical structured data,
per-field verification status, tool provenance, an explicit unverified/unavailable list, tool
execution status, and a deterministically-derived allowed-claims whitelist. Narration receives
only this Envelope, never a raw tool response, and is never the source of a price, specification,
availability status, score, rating, delta, or checkout URL. Output validation (FR-045) is
extended to check narration's numeric/factual claims against the Envelope's allowed claims,
rejecting/stripping/replacing (with a non-LLM deterministic fallback) any claim the Envelope
doesn't back — without ever touching the turn's canonical structured data or result type.

**Rationale**:

- **Closing a real enforcement gap, not adding redundant ceremony**: this system's grounding
  promise has always existed at the *construction* level — FR-019/FR-044 already say narration
  "MUST NOT be able to alter, invent, or omit any value," and the prior wording of an Assumption
  (now superseded, spec.md) treated that construction-level guarantee as sufficient and explicitly
  said output validation "does not itself verify that a narrated fact is true." That is a real
  gap: "the narration call's prompt instructs it not to invent facts" is a behavioral hope, not an
  enforced guarantee — nothing previously *checked* that the model actually complied before its
  text reached the user. The Evidence Envelope's allowed-claims check turns the existing
  construction-level intent into an enforced, testable property (SC-062/SC-063), consistent with
  how every other stage in this cycle (research.md §20/§21) already replaced "the model should
  behave" with "the application checks and enforces."
- **A single deterministic Envelope, not ad hoc grounding per response type**: `answer`,
  `recommendation`, `comparison`, and `checkoutLink` each have their own structured shape (FR-060,
  `contracts/advisor-conversation-api.md`), and each could independently need its own "what can
  narration say" logic. A single Envelope shape (result type + canonical data + verification +
  provenance + allowed claims), assembled the same way for every route, keeps the grounding check
  itself uniform — one output-validation implementation checks every route's narration the same
  way, rather than seven bespoke checks that could individually drift out of sync.
- **Reject/strip/replace, never re-prompt the model to "fix" it**: FR-071 already caps a turn at
  one narration call; letting output validation trigger a second narration attempt to correct an
  ungrounded first attempt would both violate that budget and risk repeating the identical
  failure (an LLM asked to "fix" its own ungrounded claim has no more grounding information than
  it had the first time). A deterministic, application-authored fallback — built only from data
  already proven trustworthy (the Envelope) — is strictly safer and cheaper than a corrective LLM
  round-trip.
- **Structured data is never held hostage to narration's fate**: this directly extends FR-019's
  existing "narration's absence... MUST NOT prevent the structured data itself from being
  returned" (already established for the case where the LLM is simply unavailable) to the new
  case where narration *was* produced but failed the grounding check. The reasoning is identical
  either way — the structured, tool-sourced data was always independently valid and delivering it
  never depended on narration succeeding.
- **Provenance and verification status are first-class, not derivable after the fact**: without
  an explicit per-field verification/provenance record, a grounding check could only ask "does
  this claim match *some* string in the canonical data," which would accept a claim that
  restates an *unverified* value as if it were confirmed (spec.md edge cases). Carrying
  verification status and provenance inside the Envelope itself, rather than deriving it
  on-demand, lets the grounding check also catch that more subtle failure mode — a numerically
  correct value stated with unwarranted confidence.

**Alternatives considered**: (a) Trust narration's compliance with its system-prompt instructions
alone, with no downstream check — rejected; this is exactly the superseded Assumption's gap this
section closes, and is inconsistent with every other stage in this cycle already having an
explicit, checked contract rather than an instructed-but-unverified expectation. (b) Give
narration the raw tool response object directly instead of an Envelope — rejected; a raw tool
response has no explicit allowed-claims whitelist or verification/provenance separation, so
"what's grounded" would have to be re-derived ad hoc at validation time from the same object
narration saw, duplicating logic and creating a chance for the two derivations to disagree. (c) On
an ungrounded claim, fail the whole turn to the `error` result type instead of stripping/replacing
narration — rejected; the turn's structured data is unaffected by a narration defect, and
discarding an otherwise-successful `recommendation`/`comparison`/`answer` result over a narration
problem would be a worse outcome for the user than simply presenting the structured data with
safer or no narration. (d) Allow a second, corrective LLM call to regenerate narration after a
grounding failure — rejected per the "reject/strip/replace, never re-prompt" rationale above;
violates FR-071's call budget and doesn't reliably fix the underlying issue. (e) Perform the
grounding check only for `recommendation`/`comparison` (the routes with the most structured data)
and skip it for `answer`/`checkoutLink`/`smalltalk`/`unsupported` — rejected; a fabricated price
in an `answer` turn or a subtly wrong URL in a `checkoutLink` turn is exactly as harmful as one in
a `recommendation` turn, and an unenforced route would be a predictable place for an ungrounded
claim to slip through unnoticed.

## 28. System prompt authoring requirements: two prompts, separated sections, untrusted data, no leakage, versioned (FR-093–FR-103/SC-068–SC-076)

**Decision**: This system authors exactly two system prompts — `Extraction` and `Narration`
(data-model.md `SystemPrompt`) — each required to: use schema-first output where applicable
(extraction); include `CurrentRequirement` verbatim; separate system instructions, application
state, user input, and tool/catalog data into distinguishable sections; mark user input and
catalog/tool data as untrusted data rather than instructions; instruct the model to respond in
the user's language; refuse to disclose the prompt's own content, credentials, or configuration;
never request chain-of-thought; carry a runtime-visible version identifier; and reserve few-shot
examples for genuinely complex edge cases. The narration prompt specifically may ask for
salience-based summarization but never both brevity and exhaustive value restatement at once.

**Rationale**:

- **Two prompts, not one shared prompt**: the two LLM-invoking stages already have entirely
  different jobs — extraction turns free text into a closed structured shape (research.md §21);
  narration turns already-validated structured data into prose (research.md §27) — and different
  input contracts (extraction sees the raw message; narration sees only the Evidence Envelope,
  never the raw message, FR-087). A shared prompt would either have to be generic enough to cover
  both (weakening the schema-first and untrusted-data-marking guarantees specific to each) or
  carry dead instructions relevant to only one stage on every call, wasting context (constitution
  Principle V's "avoid... excessive context").
- **Schema-first, not "please output JSON"**: research.md §21 already requires validating
  extraction output against a formal schema and treating a failure as exactly that — a schema
  failure, with one bounded repair. A free-text "output JSON matching this shape" instruction
  relies entirely on the model's compliance and produces malformed output far more often than a
  provider's native schema-constrained generation mode; schema-first output reduces how often the
  one allowed repair attempt (FR-051) is even needed, which is both a correctness and a resource
  ([research.md §25] TurnResourceBudget) improvement.
- **Explicit section separation and untrusted-data marking**: this is the direct prompt-level
  mechanism implementing something this system has assumed implicitly since its first
  grounding-focused decision (research.md §1) but never stated as a prompt-construction
  requirement — that user-supplied and catalog-supplied text is *data the model reasons about*,
  never *instructions the model follows*. Product descriptions and user messages are both
  external, potentially adversarial input in a system whose trust boundary (constitution
  Principle II) already requires product facts to come only from approved sources; a prompt that
  doesn't clearly separate "what the system is telling the model to do" from "what a user or a
  catalog entry said" cannot reliably resist a prompt-injection attempt embedded in either.
- **No system-prompt/credential disclosure, no chain-of-thought**: both are extensions of
  guarantees this system already makes elsewhere — FR-052 already discards any chain-of-thought
  the model produces during extraction and never persists it; requiring prompts to never *request*
  it in the first place (rather than only discarding it after the fact) reduces the chance it
  leaks into a response before the discard step runs at all, and reduces token cost on every call
  (constitution Principle V). Anti-disclosure of the system prompt/credentials is a standard,
  low-cost defense against a class of prompt-extraction attempts that this system, as a
  consumer-facing chat surface with no other input sanitization layer in front of the LLM calls,
  has no other mitigation for.
- **A runtime-visible version identifier**: the constitution already requires prompts to be
  version-controlled (Principle VI) as a source-control practice, but source control alone
  doesn't answer "which prompt version actually produced *this* turn's behavior" once multiple
  versions have existed across a deployment's history — especially relevant given this system's
  correlation-id-based tracing (research.md §7) already exists specifically to make a request's
  full causal chain inspectable. A version identifier surfaced at runtime and logged per call
  closes that gap without requiring a git-history cross-reference during an incident.
- **Few-shot only for genuinely complex edge cases**: every example added to a prompt is context
  included on every call for that stage, in tension with constitution Principle V's "avoid...
  excessive context; each call and each piece of context included in a prompt MUST serve the
  current request." A default few-shot set for the common, already-simple case adds ongoing token
  cost and latency to serve a request that didn't need the extra examples; reserving them for
  specific edge cases keeps the common path lean while still allowing the tool where the model
  genuinely benefits from it.
- **Narration: summarize, don't force a false choice**: a prompt that says both "keep responses
  short" and "restate every value in the table" gives the model two instructions it cannot
  simultaneously satisfy for any result with more than a couple of criteria (e.g., a 6-criterion
  comparison across 3 products) — it will violate one or the other unpredictably per call, which
  is exactly the kind of per-call inconsistency this system's determinism-focused stages
  (research.md §20/§21/§24/§27) exist to eliminate everywhere else. Since the full table is
  already guaranteed to reach the client regardless of narration (FR-089), the prompt loses
  nothing by permitting summarization instead of demanding both properties from the same text.

**Alternatives considered**: (a) One shared, parameterized prompt template for both stages —
rejected per the "two prompts, not one shared prompt" rationale above; the stages' input
contracts and purposes are different enough that sharing a template would weaken guarantees
specific to each. (b) Rely on the model's general instruction-following ability to distinguish
"data" from "instructions" without explicit prompt-level section separation and untrusted-data
marking — rejected; this is the same "trust the model's judgment without an enforced structure"
pattern research.md §20/§24 already reject for tool selection and turn control flow, applied here
to prompt-injection resistance instead. (c) Detect and filter prompt-injection attempts in user/
catalog content before it reaches the prompt, instead of (or in addition to) marking it as
untrusted data — deferred, not rejected outright: content-based injection detection is a
meaningfully different, heavier mechanism (its own false-positive/false-negative tradeoffs) than
data/instruction separation, and this specification fixes the latter as the baseline requirement
without mandating the former (spec.md Assumptions). (d) Allow chain-of-thought in the narration
prompt specifically, on the theory that narration is "just formatting" and lower-risk than
extraction — rejected; narration produces user-facing text directly, and any exposed reasoning
trace there is at least as likely to leak un-vetted content into a delivered response as it would
be during extraction, where FR-052 already forbids it. (e) Version prompts only via git commit
hash, with no separate application-level version identifier — rejected; a commit hash identifies
source-controlled *content* but requires cross-referencing deployment history to map a specific
turn's timestamp back to "which commit was actually live," which is slower and less direct during
an incident than a version value already present in that turn's own logs.

## 29. Request guardrails: admission-control and resource-protection limits, enforced before LLM/tool cost is incurred (FR-104–FR-113/SC-077–SC-086)

**Decision**: `RequestGuardrails` (data-model.md) is a fixed set of limits enforced either
before a turn begins or independent of any single turn: max raw-message length, max HTTP body
size, max count/length for hard-constraint and preference list entries, Unicode normalization
with control-character rejection, strict value validation for currency/budget/operators/units/
product ids, a per-user rate limit, a per-user cross-session concurrency limit, a per-user
token/cost quota, and a max active conversation context size bounding what's included in a
prompt. Every violation is fail-safe: rejected with zero language-model or tool invocation,
never silently truncated or coerced.

**Rationale**:

- **Cheapest rejection first, consistently**: this system already rejects an empty/whitespace
  message before any language-model call (FR-037) specifically because that's the cheapest
  possible point to reject invalid input — before incurring any LLM/tool cost. Every guardrail
  here extends that same principle to every other way a request could be abusive, oversized, or
  malformed, rather than leaving FR-037 as the only cheap-rejection case and letting every other
  attack/misuse vector reach (and cost) an LLM or tool call before being caught.
- **Body size and rate/concurrency/quota limits are admission-control, not turn-cycle stages**:
  `TurnResourceBudget` (research.md §25) already bounds what an *admitted* turn may cost; these
  guardrails answer a logically prior question — should this request be admitted at all. Keeping
  them conceptually distinct (and enforced earlier, often before FR-036's ten-stage cycle even
  starts) matches how authentication (FR-030) already sits outside the cycle rather than as one
  of its ten stages — some checks are about the *request*, not the *turn*.
- **List count/length limits, enforced cumulatively, not just per-patch**: FR-057/FR-058 already
  established that `CurrentRequirement` list fields carry forward and accumulate patch-by-patch
  across turns (research.md §22). Without a limit enforced against the *merged* result, not just
  each individual patch, a user (or an automated abuser) could send many small, individually
  reasonable patches that cumulatively grow `RequiredFeatures`/`Preferences` without bound —
  inflating every future prompt's size and cost. Enforcing the limit at merge time, not just at
  patch-intake time, closes that accumulation path.
- **Unicode normalization before length/content checks, not after**: normalization can change a
  string's effective length (e.g., combining-character sequences collapsing into precomposed
  forms) — checking length before normalizing would let a crafted message understate its
  effective size and pass a check it should fail. Normalizing first, then checking, closes that
  gap and also gives every downstream stage (extraction, persistence, display) one canonical
  representation to reason about instead of several equivalent-but-distinct byte sequences for
  the same visible text.
- **Strict value validation beyond schema shape**: schema validation (FR-039, research.md §21)
  already confirms a field's *shape* ("currency is a string"); it says nothing about whether that
  string is a *real* currency code. Because `CurrentRequirement.Currency`/`Budget` are hard
  constraints that directly gate a product's eligibility (FR-080/FR-084, research.md §26), an
  unvalidated currency or budget value reaching `get_recommendations` could silently produce a
  meaningless or exploitable filtering result. Validating value-level correctness — not just
  shape — before any tool call closes that gap the same way FR-108 closes it for operators, units,
  and product ids.
- **Per-user, not only per-session, concurrency and rate limiting**: FR-024 already prevents two
  turns from interleaving *within one session*, but says nothing about a user opening many
  sessions and running them concurrently — each individually FR-024-compliant, collectively
  unbounded. A per-user limit (FR-109/FR-110), keyed off the same authenticated identity FR-030
  already establishes, closes that gap without weakening FR-024's own per-session guarantee —
  the two checks are independent and both apply (spec.md Assumptions).
- **A token/cost quota distinct from the per-turn budget**: `TurnResourceBudget` (research.md
  §25) already caps a single turn's own LLM calls; it has no visibility into how many turns a
  user has run over time. A cumulative, time-windowed quota is the mechanism that actually bounds
  a user's total cost exposure to this system — the per-turn budget bounds "how expensive can one
  turn be," the quota bounds "how much can one user cost in total."
- **A context-flooding bound that trims what's included in a prompt, never what's persisted**:
  this system's conversation view (FR-023) and session-reload endpoint already depend on the full
  transcript remaining available; the risk this guardrail addresses is specifically *prompt*
  size/cost growing unbounded as a session lengthens, not the stored history itself. Bounding only
  what's included in a prompt — while `CurrentRequirement` (FR-055, the actual source of truth for
  "what does the user currently want," research.md §22) remains fully present regardless of the
  bound — means the guardrail protects against cost/attack-surface growth without weakening this
  system's existing state-authority guarantees.

**Alternatives considered**: (a) Rely solely on `TurnResourceBudget` (per-turn limits) without any
admission-control-level guardrails — rejected; a per-turn budget caps one turn's cost but does
nothing to prevent a high *volume* of turns, an oversized single request, or a slowly-accumulated
oversized `CurrentRequirement` across many individually-small turns. (b) Enforce message-length
and control-character checks *after* extraction, treating a bad result the same as a schema
validation failure — rejected; this would still incur the cost of an LLM call for input that
could have been rejected for free, violating the same "cheapest rejection point" principle FR-037
already established. (c) Silently truncate an oversized message, list, or context window instead
of rejecting — rejected outright, per FR-113 and this system's broader honesty posture (FR-004/
FR-005/FR-010): silent truncation can change the actual meaning of a request without telling the
user, producing a response to a request they didn't really make. (d) A single combined per-user
"activity limit" instead of three distinct rate/concurrency/quota mechanisms — rejected; rate
(requests per time), concurrency (simultaneous in-flight turns), and quota (cumulative token/cost)
bound three different resources and can each be exceeded independently of the others (e.g., a
user could be well under their rate limit while still holding several long-running concurrent
turns, or under both while having exhausted a cost quota from a few very expensive prior turns) —
collapsing them into one number would lose the ability to diagnose and communicate *which*
resource was actually exhausted.

## 30. Privacy-by-design for conversation data: reversing the "ordinary application data" assumption (FR-114–FR-123/SC-087–SC-094)

**Decision**: This system's earlier stance — conversation history is ordinary application data
needing no special PII/privacy handling (spec.md Clarifications, Session 2026-08-02) — is
reversed. Conversation data now gets: PII screening before any LLM-provider call (block or
redact, never pass through, mirroring FR-088's reject/strip/replace posture for narration);
minimal-necessary-context prompts, now framed as a privacy control and not only a cost control;
exclusion of the stable user identifier from prompts absent functional need; a user-initiated
deletion capability; automatic retention-based deletion; encryption in transit, at rest, and for
backups; and LLM-provider selection criteria covering training use, retention, and data region.

**Rationale**:

- **Why the reversal, and why now**: the original 2026-08-02 clarification treated PII risk as
  scoped to this system's stated domain (budgets, categories, feature preferences) — reasonable
  for what the system *asks* for, but not for what a user might *volunteer* in free text. A chat
  interface accepting open natural-language input (FR-001) has no way to prevent a user from
  typing an address, phone number, or other personal detail into a message about a smartphone
  budget; the original assumption didn't account for that gap. Reversing it, rather than leaving
  it as a silently-outdated decision, follows the same "Superseded" transparency pattern already
  used elsewhere in this document when a stronger requirement supersedes an earlier one (e.g.,
  research.md §27's grounding-check reversal) — the historical decision and its date remain
  visible in the Clarifications log rather than being silently rewritten.
- **PII screening before the LLM call, not after**: this system's LLM provider is an external,
  third-party service (research.md §10) outside this system's own trust boundary — anything sent
  to it has effectively left this system's control. Screening (and blocking/redacting) before
  that call is the only point where this system can still act on a detection; screening
  after-the-fact would be too late to prevent the exposure it exists to prevent. The
  block-or-redact choice deliberately mirrors FR-088's already-established "reject, strip, or
  replace" pattern for the opposite direction of risk (ungrounded narration reaching the user) —
  the same shape of problem (unwanted content must not cross a specific boundary) gets the same
  shape of solution on both sides.
- **Minimal-necessary-context as a privacy control, not only a cost one**: FR-095/FR-112 already
  require bounded, purpose-scoped prompt content for cost/context-flooding reasons (research.md
  §28/§29). Framing the same bound as a privacy control too doesn't add a new mechanism — it adds
  a second reason the existing one matters: every piece of session data included in a prompt is
  also a piece of data exposed to the third-party LLM provider, so "include only what's needed"
  already does double duty once privacy is considered a first-class concern.
- **Excluding the stable user identifier absent functional need**: `ConversationSession.UserId`
  (the Google `sub` claim, research.md §17) is exactly the kind of durable, cross-session
  identifier whose presence in LLM-provider logs/training data (if the provider retains or trains
  on it) would let conversations be linked back to a specific person across time, independent of
  whatever the conversation content itself reveals. Neither prompt has any functional reason to
  know *who* is asking — only *what* they're asking for — so there is no capability lost by
  excluding it, only reduced exposure.
- **User-initiated and automatic retention-based deletion, both required**: relying on only one
  of these leaves a gap the other closes. User-initiated deletion serves a user who actively wants
  their data gone now; automatic retention-based deletion serves the far larger set of sessions no
  one will ever think to delete, which would otherwise accumulate indefinitely — inconsistent with
  a privacy-by-design posture that treats "keep it forever unless someone asks" as the wrong
  default.
- **Encryption in transit/at rest/backups, and LLM-provider selection criteria**: these are
  baseline expectations for a system now treating conversation content as privacy-sensitive,
  consistent with the constitution's existing (but narrower) requirement that "logs MUST NOT
  expose credentials or sensitive user data" (Principle VI) — this section generalizes that same
  posture from logs specifically to conversation data broadly, and extends it to the LLM
  provider itself: this system's existing "keep the provider replaceable" design (research.md
  §10) already treats provider choice as a configuration decision, so adding training/retention/
  region as selection *criteria*, not just capability/cost, is a natural extension of a decision
  this system already makes explicitly rather than implicitly.

**Alternatives considered**: (a) Leave the original 2026-08-02 assumption in place and treat PII
exposure as an acceptable residual risk for a retail product-advisor use case — rejected; a chat
interface with open natural-language input cannot bound what a user might type, and the
downside (a user's personal data reaching a third-party LLM provider, possibly retained or
trained on) is a real, foreseeable harm this system can prevent at negligible cost by screening
before the call. (b) Screen for PII only in the raw message, not also constrain what's included
from session state — rejected; `CurrentRequirement`/session data is itself user-supplied
information, subject to the same minimal-necessary-context and stable-identifier-exclusion
principles as the raw message, not just a separate, unscreened channel. (c) Require content-level
redaction only, never allow blocking the whole message — rejected; some messages may be
predominantly or entirely PII with no separable "safe" remainder (e.g., a message that is just a
phone number), and forcing redaction-only would either produce a nonsensical near-empty message
or risk redaction logic failing to fully remove sensitive content — blocking is the safer default
when redaction can't cleanly isolate the PII. (d) Make retention-based deletion the only
mechanism, without a separate user-initiated delete capability — rejected; a fixed retention
window doesn't serve a user who wants their specific data gone immediately (e.g., after realizing
they included something sensitive), and privacy-by-design conventionally expects both a
default-safe lifecycle *and* explicit user control, not one substituting for the other.

## 31. MCP endpoint and service-to-service credential security: hardening the existing shared-secret model (FR-124–FR-132/SC-095–SC-103)

**Decision**: The MCP endpoint and internal service-to-service calls (research.md §18) get nine
additional, concrete hardening requirements layered on the existing shared-secret model: no
unauthenticated access path under any configuration; secret-storage-only credential storage;
rotation as a supported capability (old/new overlap window); no production fallback to a
hardcoded development default; constant-time credential comparison; scoped-per-relationship
credentials as the preferred future direction (SHOULD, not a mandatory migration); least-
privilege tool execution; no automatic conversation-ownership grant from MCP-transport
authentication alone; and a distinct production-readiness review for preview/prerelease
dependencies.

**Rationale**:

- **This system already lived the "no production default" gap once**: during this project's own
  deployment history, `advisor-api` returned a bare, unlogged `500` for every request — including
  a correctly-formed MCP `initialize` handshake — because its `InternalApiKey` was unset in the
  production environment; a separate, distinct incident involved a request authenticated with the
  local Docker Compose default value (`dev-internal-api-key`) being rejected by the real deployed
  service specifically because that default was never, and should never have been, valid in
  production. FR-127 turns the second half of that lesson (a dev-only value must never work in
  prod) into an explicit, testable requirement rather than something the team merely learned
  informally from having hit it once.
- **Constant-time comparison closes a real, common class of vulnerability**: a naive string- or
  byte-equality check (e.g., `==`, `.Equals()`) on a secret typically short-circuits at the first
  mismatched byte, meaning correct-prefix guesses take measurably longer to reject than
  incorrect-prefix guesses — a textbook timing side-channel that lets an attacker recover a
  credential byte-by-byte given enough attempts and a low-noise timing signal. Every credential
  check this system performs (`InternalApiKey` validation, and any future scoped credential under
  FR-129) is exactly the kind of check this vulnerability class targets, and the fix (a
  fixed-time comparison) is a small, well-understood, low-cost change relative to the risk it
  closes.
- **Least-privilege tool execution and no-implicit-ownership are two sides of the same principle
  applied at different layers**: FR-066–FR-070 (research.md §24) already scope *which tools* a
  turn may call; FR-130/FR-131 apply the same "least necessary" discipline to *what each call can
  access* once it runs, and to *what an authenticated MCP caller is entitled to* beyond "this is a
  legitimate internal caller." Without FR-131 specifically, a valid internal credential plus a
  self-asserted user reference could be mistaken for equivalent to FR-031's actual ownership
  check — collapsing two genuinely different questions ("is this caller internal?" and "does this
  caller own this session?") into one, which is exactly the kind of conflated trust boundary
  research.md §18's own rejected alternative (c) already warned against for the Google-identity
  case.
- **Rotation as a capability, not just "redeploy with a new value"**: the original decision
  (research.md §18) described rotation only as an operational fact (redeploying changes the
  value); it didn't require the *mechanism* to tolerate a rollout window where old and new values
  briefly coexist. Without that tolerance, rotating the credential requires either accepting a
  brief total outage (every instance must update atomically) or accepting that some in-flight
  requests will fail during rollout — neither of which is compatible with this system's existing
  "graceful, no unnecessary failure" posture (constitution Principle V). Requiring overlap support
  makes rotation something that can actually be exercised routinely, not just something the
  architecture nominally allows in theory.
- **Preview-dependency production-readiness review**: this project's own MCP server integration
  depends on `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` at a `2.0.0-preview.3`
  version — a real, current example of exactly the situation FR-132 addresses, not a hypothetical.
  A preview package can change its public API, its security posture, or its behavior without the
  same stability guarantees a stable release carries; using it without ever having explicitly
  evaluated that risk (as opposed to simply reaching for it because it's the official SDK and the
  only option available) is a gap worth closing explicitly, especially for a system whose MCP
  server is a primary trust boundary (this section's own subject).

**Alternatives considered**: (a) Leave credential hardening entirely to general secure-coding
practice/code review, with no explicit spec-level requirement — rejected; this system already
demonstrated, via its own deployment incidents, that an informally-understood expectation ("of
course we won't use the dev key in prod") is not the same as an enforced, testable one, which is
exactly the gap FR-124–FR-132 close. (b) Mandate an immediate migration to per-service-pair
scoped credentials now, rather than a SHOULD — rejected; research.md §18's original
disproportionate-overhead rationale for this system's current 5-service scale still holds, and
forcing an immediate migration would be scope creep relative to what was actually requested
(scoped credentials as the *preferred direction*, not an immediate mandate). (c) Require mTLS
instead of a hardened shared-secret model — rejected for the same reasons research.md §18
originally rejected it (free-tier hosting/demo-scope certificate machinery); the hardening in this
section closes the gaps that actually mattered (production defaults, timing attacks, rotation)
without requiring a wholesale mechanism change. (d) Treat "internal credential valid" and "may act
as this user" as one combined check for MCP-endpoint simplicity — rejected per the
least-privilege/no-implicit-ownership rationale above; conflating them would silently reintroduce
a privilege-escalation path the moment any external or less-trusted MCP client is ever permitted
to reach the endpoint with a caller-supplied user reference.

## 32. Safe observability for the agentic turn cycle: a closed allow/deny list and seven dedicated metrics (FR-133–FR-137/SC-104–SC-111)

**Decision**: A turn's logs are limited to eleven allowed fields (correlation id; hashed/
pseudonymous user/session identifier; prompt version; model identifier; classified intent; tool
name(s); allow/deny decisions; latency; token usage; validation status; error category) and
explicitly exclude seven categories by default (full raw user message; full assembled prompt;
PII-bearing tool arguments/results; Authorization/credential headers; API keys; connection
strings; full raw LLM response). Seven turn-cycle events each get their own dedicated,
distinguishable metric: loop/iteration-limit reached, schema-repair attempted, tool call
rejected, grounding failure, rate-limit rejection, PII detection, and LLM-provider failure.

**Rationale**:

- **A closed list, not "log what seems useful, avoid what seems sensitive"**: constitution
  Principle VI already states the general rule ("Logs MUST NOT expose credentials... or sensitive
  user data"), but a general rule alone leaves every individual logging call site to make its own
  judgment call about what "sensitive" means for that specific field — exactly the kind of
  per-call, unaudited decision this entire specification has consistently replaced with an
  explicit, testable contract everywhere else it appears (the turn-processing cycle, research.md
  §20; tool recipes, research.md §24; the Evidence Envelope, research.md §27). An enumerated
  allow-list closes the same gap for logging: a reviewer (or an automated check) can verify a log
  statement against a fixed list instead of re-deriving "is this sensitive?" from first principles
  every time.
- **The deny-list mirrors, rather than duplicates, guarantees this system already makes
  elsewhere**: "full raw user message"/"full assembled prompt" extends FR-116/FR-117's
  minimal-necessary-context and PII-screening guarantees from *what reaches the LLM provider* to
  *what reaches the logging backend* — the same content is sensitive for the same reasons in
  both directions. "API keys"/"connection strings"/"Authorization headers" extends FR-125's
  secret-storage-only requirement the same way: a credential correctly kept out of source control
  but then accidentally logged in plaintext (e.g., via a debug-level HTTP client trace that
  includes request headers) would defeat FR-125's protection just as thoroughly as committing it
  to git would. This section doesn't invent new sensitivity judgments — it applies judgments this
  specification already made elsewhere to one more surface (logs) that could otherwise leak them.
- **Hashed/pseudonymous identifiers, not raw ones, even in logs**: FR-118 already excludes the
  raw stable user identifier from LLM-provider prompts on privacy-by-design grounds (research.md
  §30); the same identifier appearing in plaintext in logs — a place operators, on-call
  responders, and potentially third-party observability vendors (Grafana Cloud, research.md §16)
  can all see — would reopen exactly the cross-session correlation risk FR-118 was written to
  close, just through a different door. Requiring irreversibility (FR-137), not just "hash it,"
  matters because a trivially-reversible transformation (e.g., base64, an unsalted hash of a
  low-entropy value) provides the appearance of protection without the substance.
- **Seven dedicated metrics, not one generic error counter**: this specification already defines
  seven meaningfully distinct failure/decision modes across its other sections — a resource-budget
  limit (research.md §25), a schema-repair attempt (research.md §21), an out-of-recipe or
  strict-validation tool rejection (research.md §24/§29), a narration grounding failure
  (research.md §27), an admission-control rejection (research.md §29), a PII-screening action
  (research.md §30), and a provider-resilience exhaustion (research.md §6). Funneling all seven
  into one "error count" metric would make it impossible to distinguish, from monitoring alone,
  "the system's prompts are systematically producing malformed extraction output" from "users are
  hitting the rate limit" from "the LLM provider is down" — each of which calls for a completely
  different operational response, and each of which this specification's own sections already
  give a name to.
- **Derivable-without-capturing, for allowed fields computed from denied content**: token usage
  and validation status are values *computed from* prompt/response content, not the content
  itself; requiring that they be logged without also capturing what they were computed from
  (FR-135) closes a subtle loophole where "we need the prompt to compute the token count" could be
  used to justify logging the prompt anyway — the computation and the logging are different steps,
  and only the computed value, never its input, needs to survive past that computation.

**Alternatives considered**: (a) Rely on general-purpose log-scrubbing/redaction middleware to
detect and strip sensitive content from whatever gets logged, rather than an allow-list of what
may be logged in the first place — rejected as a weaker guarantee; scrubbing after the fact can
miss content it wasn't specifically trained/configured to recognize (the same "how do you detect
this reliably" problem PII screening already grapples with, research.md §30), whereas an
allow-list only requires verifying that logged content stays *within* a small, fixed set — a much
easier property to test and audit. (b) Log full prompts/responses to a separate,
access-restricted "debug" backend distinct from the shared observability backend — deferred, not
adopted as this system's default; nothing in this specification prohibits an operator maintaining
such a channel outside the shared pipeline this section governs (spec.md edge cases), but making
it the default would create a second de facto storage location for exactly the sensitive content
this section exists to keep out of logs, undermining the guarantee for anyone who assumes "not in
the shared logs" means "not logged anywhere." (c) A single combined "agentic cycle anomaly" metric
covering all seven event types with a label/dimension distinguishing them, instead of seven
separate metrics — considered equivalent in most observability backends (a labeled metric and
seven separate metrics are often interchangeable at the storage layer) and left as an
implementation choice; what this specification fixes is that the seven event types remain
independently queryable and independently incrementable (SC-108/SC-111), not that they must be
seven physically distinct metric names.

## 33. The agentic security and quality eval suite: fifteen classes, verifying rather than defining, with a critical/non-critical release gate (FR-138–FR-141/SC-112–SC-118)

**Decision**: A mandatory, minimum fifteen-class eval suite (data-model.md `EvalSuite`) verifies
guarantees this specification already makes elsewhere — it defines no new system behavior of its
own, only test coverage over existing FRs. Six classes (grounding: fabricated values, indirect
injection, product not found; authorization: wrong tool for intent, system-prompt extraction;
cross-session: cross-session access) are release-blocking at 100%; the remaining nine run
automatically and are reviewed at release but aren't fixed at 100% by this specification.

**Rationale**:

- **A verification suite, not a new requirements source**: every one of the fifteen classes maps
  to functional requirements this specification already defines elsewhere (the table in
  data-model.md `EvalSuite` traces each one). This is deliberate: an eval suite whose classes
  didn't map to an existing, named guarantee would be testing an undocumented expectation,
  reintroducing exactly the kind of implicit, unaudited behavior this entire specification has
  worked to eliminate (research.md §20 onward). Every eval class here has a specific FR it proves
  compliant or non-compliant, never a vague "seems safe" judgment.
- **Why these fifteen, not a larger or smaller set**: they were supplied as the mandatory minimum
  and map cleanly onto categories of attack/failure this specification's own architecture already
  anticipates and defends against by construction — prompt-injection and system-prompt-extraction
  (the prompt contract, research.md §28), fabrication (the Evidence Envelope, research.md §27),
  tool misuse and loop exhaustion (tool recipes and resource budgets, research.md §24/§25),
  malformed/oversized input (request guardrails, research.md §29), cross-session access and
  memory poisoning (session ownership and state-merge determinism, research.md §17/§22),
  dependency and intent-classification edge cases (resilience and the closed intent set,
  research.md §6/§21), and PII/payment-data handling (privacy-by-design, research.md §30). The
  suite exists precisely because a specification-level guarantee that is never exercised by a
  test is only a promise, not a verified property.
- **Why exactly grounding, authorization, and cross-session are the 100%-required categories**:
  these three correspond to the failure modes this system's own architecture makes *deterministic*
  rather than dependent on model behavior — grounding is enforced by the Evidence Envelope's
  allowed-claims check (FR-088, application code, not model judgment); authorization/tool-scoping
  is enforced by the tool-exposure surface itself being scoped per route before the model is
  invoked (FR-068); cross-session access is enforced by a plain ownership comparison (FR-031, no
  model involvement at all). Because these three properties are backed by deterministic
  application-layer checks rather than probabilistic model behavior, 100% pass is both achievable
  and the correct bar — a flaky result in one of these categories means the deterministic
  enforcement itself has a bug, not that "the model sometimes misbehaves," which is a
  meaningfully different (and more urgent) class of problem.
- **Why the other nine classes don't get a specification-fixed 100% requirement**: several of
  them (direct prompt injection generally, memory poisoning, unsupported-intent classification)
  have a component that depends on the language model's classification behavior under adversarial
  or ambiguous input — even with every deterministic guard this specification requires already in
  place (untrusted-data marking, closed intent sets, schema validation), the model's *specific
  wording choices* in an edge case can vary in ways that don't compromise any hard guarantee but
  could still cause a narrowly-written eval assertion to flap. Fixing a numeric pass-rate bar for
  these in the specification itself would either be set too strictly (spurious release blocks for
  non-security-relevant wording variance) or too loosely (a number chosen without real operational
  data) — this specification instead requires that they exist, run, and are reviewed, leaving the
  actual bar to be set and tuned operationally, consistent with this document's established
  pattern for every other tunable numeric threshold (FR-053, FR-079, FR-101, FR-120).
- **The critical/non-critical mapping is an explicit, documented judgment call**: "grounding,"
  "authorization," and "cross-session" were supplied as category names without a pre-existing
  enumeration mapping specific classes to them. Rather than leave that mapping implicit (and
  therefore silently arguable later), spec.md's Assumptions record the exact mapping made here,
  explicitly framed as revisable — so a future disagreement about the categorization is a
  documented amendment, not a rediscovery.

**Alternatives considered**: (a) Require 100% for all fifteen classes — rejected; several classes
have an irreducible model-behavior component under adversarial input where a specification-fixed
100% bar would either be unrealistic given genuine LLM output variance or would require masking
that variance with looser assertions that defeat the eval's purpose — better to be honest that
these are held to a "must exist, must run, must be reviewed" bar rather than falsely claim a
100% guarantee the underlying mechanism can't deterministically provide. (b) Require 0%
(informational only, no release gate at all) — rejected; this would make even the deterministic,
100%-achievable categories (grounding/authorization/cross-session) advisory rather than
enforced, undermining exactly the guarantees this specification treats as non-negotiable
elsewhere (FR-004, FR-031, FR-088). (c) Define the critical/non-critical split by listing FR
numbers directly in FR-140 rather than eval-class numbers — considered equivalent in substance;
eval-class numbers were used because they map 1:1 to the enumerated list in FR-138 and the
`EvalSuite` table, keeping the mapping in one place rather than duplicating a long FR-number list
inline. (d) Treat PII/payment-data input (class 15) as critical, given how strict FR-114–FR-123
already are — considered but not adopted as the default categorization here; recorded explicitly
in spec.md Assumptions as a class a future revision could promote to critical, since the
underlying FR-115/FR-116 requirements are already hard MUSTs regardless of which eval-suite
tier verifies them.
