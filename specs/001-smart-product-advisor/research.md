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
