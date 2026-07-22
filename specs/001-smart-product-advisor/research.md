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
