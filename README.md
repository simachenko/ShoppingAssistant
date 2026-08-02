# Smart Retail Product Advisor

An MCP-based conversational advisor that helps shoppers find, compare, and check facts about
products — built as a set of independent, DDD-modeled microservices behind a single Gateway/BFF
and a Blazor UI. Product facts, prices, and comparisons are always computed deterministically;
the language model only understands requests, chooses tools, and narrates already-computed
results — it never invents a price, spec, or ranking.

## Services

| Service | Responsibility |
|---|---|
| `ProductCatalog` | Products, categories, brands, specifications, deterministic parametric search |
| `PricingAvailability` | Current prices, discounts, stock status |
| `ProductAdvisor` | MCP server, conversation orchestration, recommendation/comparison computation |
| `Gateway` | Single BFF entry point for the UI — composes the above, no direct client access to them |
| `WebApp` (Blazor) | Chat UI, explicit product-picker/comparison UI, single-product detail view |

Each service owns its own data (separate schemas on one shared Postgres instance for this demo)
and is reachable only through its documented HTTP API — see
[`specs/001-smart-product-advisor/contracts/`](specs/001-smart-product-advisor/contracts/) for
every endpoint's request/response shape.

## Running locally

Requires the .NET 10 SDK and Docker Desktop (or a compatible engine) running.

**Option A — .NET Aspire** (primary dev path; starts Postgres, applies migrations, wires up
service discovery, and opens the Aspire dashboard for live traces/logs/metrics):

```bash
dotnet run --project src/Aspire/AppHost
```

**Option B — Docker Compose** (CI-parity fallback):

```bash
docker compose up --build
```

Either way, an LLM provider must be configured (any `Microsoft.Extensions.AI`-compatible,
OpenAI-style endpoint) via `LlmProvider__Endpoint` / `LlmProvider__ApiKey` / `LlmProvider__Model`
— as environment variables for Docker Compose, or as Aspire parameters for the AppHost path.
Never commit real values; see `render.yaml` for how these map to secrets in production.

The whole app also requires sign-in (FR-030) and an internal service credential (FR-029):

- `INTERNAL_API_KEY` — any shared secret string; Docker Compose defaults it to
  `dev-internal-api-key` for local runs if unset, but a real value is required in `render.yaml`.
- `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` — from a
  [Google OAuth 2.0 Client ID](https://console.cloud.google.com/apis/credentials) (Web application
  type), with `http://localhost:5000/signin-google` (Docker Compose) added as an authorized
  redirect URI. WebApp performs the sign-in and needs both values; Gateway only needs
  `GOOGLE_CLIENT_ID`, to validate the resulting token's audience against Google's own OIDC
  discovery document (research.md §17) — it never talks to Google itself.

## Testing

```bash
dotnet test src/ProductAdvisor.slnx
```

Runs every unit, domain, application, and contract test (contract tests spin up their own
Testcontainers-managed Postgres — no manually-running stack required). The one suite that does
need a live stack and a real LLM is `tests/EndToEnd.Tests`, which exercises full conversation
scenarios against `docker compose up --build`:

```bash
docker compose up --build -d
dotnet test tests/EndToEnd.Tests/EndToEnd.Tests.csproj
docker compose down -v
```

## Documentation

- [`specs/001-smart-product-advisor/spec.md`](specs/001-smart-product-advisor/spec.md) — feature requirements and success criteria
- [`specs/001-smart-product-advisor/plan.md`](specs/001-smart-product-advisor/plan.md) — architecture, tech stack, performance/scale goals
- [`specs/001-smart-product-advisor/quickstart.md`](specs/001-smart-product-advisor/quickstart.md) — manual validation walkthrough for every user-facing scenario
- [`specs/001-smart-product-advisor/data-model.md`](specs/001-smart-product-advisor/data-model.md) — entities and their relationships
- [`specs/001-smart-product-advisor/research.md`](specs/001-smart-product-advisor/research.md) — the technical decisions behind the architecture, and why
- [`.specify/memory/constitution.md`](.specify/memory/constitution.md) — the non-negotiable project principles (grounded facts, deterministic computation, resilience, observability)

## Observability

Locally, `dotnet run --project src/Aspire/AppHost` opens the Aspire Dashboard with live
traces/logs/metrics for every service — no extra setup needed (`docker compose up` alone
exports nothing, since there's no dashboard process to receive it).

In deployed environments (or to point Docker Compose at a real backend too), set
`OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` — every service already reads these
standard OpenTelemetry env vars (`src/Aspire/ServiceDefaults/Extensions.cs`) and, when set,
exports traces/metrics/logs via OTLP instead of (or alongside) the local dashboard
(FR-027/FR-028, research.md §16). Any OTLP-compatible backend works; a convenient free-tier
option is [Grafana Cloud](https://grafana.com/products/cloud/):

1. Create a free Grafana Cloud stack, then find its OTLP endpoint under
   **Connections → Add new connection → OpenTelemetry (OTLP)**.
2. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to the shown endpoint URL, and
   `OTEL_EXPORTER_OTLP_HEADERS` to `Authorization=Basic <base64(instanceId:apiKey)>` (Grafana
   Cloud shows the exact value to copy).
3. Set `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` — the .NET SDK defaults to gRPC, but Grafana
   Cloud's gateway (and most managed OTLP endpoints) only accept HTTP/protobuf; without this,
   export fails silently with nothing in the app logs (`docker-compose.yml` already defaults
   this for local runs; `render.yaml` sets it as a plain, non-secret value for all five).
4. Apply the endpoint/headers to every service — `render.yaml` declares them (`sync: false`)
   for all five; for Docker Compose, set them in `.env` before `docker compose up`.

An unreachable/misconfigured OTLP backend never blocks a request — export failures are
swallowed by the OpenTelemetry SDK's own batching/retry behavior, not surfaced to callers
(FR-032, constitution Principle V).

## Deployment

`render.yaml` defines a Render Blueprint (one free-tier web service per deployable) that
auto-deploys on push to `main`; `.github/workflows/ci.yml` gates that with build/test/Docker
image validation/EndToEnd stages first. Real connection strings and the LLM provider key are set
in Render's dashboard, never committed.
