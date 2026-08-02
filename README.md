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

## Deployment

`render.yaml` defines a Render Blueprint (one free-tier web service per deployable) that
auto-deploys on push to `main`; `.github/workflows/ci.yml` gates that with build/test/Docker
image validation/EndToEnd stages first. Real connection strings and the LLM provider key are set
in Render's dashboard, never committed.
