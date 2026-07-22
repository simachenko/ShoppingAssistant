<!--
Sync Impact Report
==================
Version change: [TEMPLATE] → 1.0.0 (initial ratification)
Modified principles: N/A (first fill of template placeholders)
Added sections:
  - Core Principles I–VI (Code Quality and Maintainability; Reliable and Grounded
    Behavior; Testing Standards; Consistent User Experience; Performance and
    Resilience; Observability and Safe Evolution)
  - Technology & Data Constraints (SECTION_2)
  - Development Workflow & Quality Gates (SECTION_3)
  - Governance
Removed sections: none (template placeholders only)
Templates requiring updates:
  - .specify/templates/plan-template.md ✅ no change needed (Constitution Check
    section already derives gates dynamically from this file)
  - .specify/templates/spec-template.md ✅ no change needed (generic, principle-agnostic)
  - .specify/templates/tasks-template.md ✅ no change needed (generic, principle-agnostic)
  - .claude/skills/speckit-*/SKILL.md ✅ no change needed (agent-agnostic references)
Follow-up TODOs:
  - TODO(RATIFICATION_DATE): original adoption date unknown; set to the date this
    version was authored (2026-07-22) since no prior ratified constitution existed.
-->

# Smart Retail Product Advisor Constitution

## Core Principles

### I. Code Quality and Maintainability

The codebase MUST be modular, readable, typed, and easy to extend. Every module
MUST have a single, clearly stated responsibility, and public functions,
classes, and MCP tool schemas MUST carry type annotations (or equivalent
schema definitions) that a reader can trust without opening the implementation.

Business logic, MCP tool integrations, LLM orchestration (prompting, tool-call
routing, response synthesis), and external data access (catalog, pricing,
inventory APIs) MUST be implemented in clearly separated layers or modules.
None of these layers may reach into another's internals directly; they MUST
communicate through explicit interfaces. This separation exists so that a
data-source outage, a prompt change, or a new MCP tool can be modified or
replaced without rewriting unrelated layers.

Configuration (endpoints, feature flags, model/provider selection, timeouts)
and secrets (API keys, tokens, credentials) MUST NOT be hard-coded or committed
to source control. They MUST be supplied via environment variables, a secrets
manager, or an equivalent externalized configuration mechanism.

Code quality checks (linting, static typing, formatting) MUST be automated and
runnable both locally and in CI, so quality is verified consistently rather
than by manual reviewer judgment alone.

**Rationale**: A product advisor that mixes prompting logic with data-fetching
code or hard-coded credentials becomes unsafe to change quickly and unsafe to
share; enforced modularity and externalized config keep the system extensible
and secure as MCP tools and data sources evolve.

### II. Reliable and Grounded Behavior

Product facts, prices, and availability presented to the user MUST originate
from approved data sources (catalog services, pricing APIs, inventory
feeds/MCP tools) — never from the model's own generated knowledge.

The agent MUST NOT fabricate missing information. When a fact cannot be
retrieved from an approved source, the agent MUST say so rather than
guessing, estimating, or inventing a plausible-sounding value.

Recommendations MUST respect the user's stated requirements (budget, features,
constraints) and MUST include the reasoning behind the recommendation (e.g.,
which criteria drove the choice), so the user can evaluate and trust the
suggestion rather than accept it blindly.

Uncertainty, missing data, and unavailable upstream services MUST be
communicated clearly and explicitly to the user, distinguishing "this
information is not available right now" from "this product does not have
this feature."

**Rationale**: Shopping decisions are consequential and factual; ungrounded or
fabricated prices, stock levels, or specs erode trust and can cause real
financial harm to the user.

### III. Testing Standards

Core business logic (recommendation ranking, comparison logic, requirement
matching) and MCP tools (each tool's contract: inputs, outputs, error modes)
MUST be covered by automated tests.

Integration tests MUST validate complete end-to-end scenarios: product search,
product comparison, and recommendation generation, exercising the real
interaction between orchestration logic and MCP tools (using test doubles for
external services where appropriate).

Important user scenarios and previously discovered defects MUST have
regression tests added so the same class of failure cannot silently
reappear.

Tests and code quality checks MUST pass before any change is merged or
released; a red test suite or failing lint/type check blocks the change.

**Rationale**: An advisor that silently regresses on comparison correctness or
recommendation quality is worse than one that is slow to ship — automated
tests are the mechanism that lets the system evolve quickly without
sacrificing correctness.

### IV. Consistent User Experience

Responses MUST be clear, concise, and consistent in structure (e.g.,
consistent presentation of product name, price, availability, and rationale
across turns and across product categories).

The agent MUST preserve the user's language, currency, units, budget, and
other explicit constraints throughout a conversation; it MUST NOT silently
switch currency, unit system, or language, or drop a previously stated
budget or requirement without being told to.

Comparisons between products MUST use consistent criteria across all
compared items (the same attributes, in the same order, using the same
units), so differences reflect real product differences rather than
inconsistent presentation.

When essential information is missing to satisfy the request (e.g., budget,
category, quantity, required feature), the agent MUST ask a single focused
clarifying question rather than guessing or proceeding on an assumption that
was never confirmed.

**Rationale**: Users compare products across multiple turns; inconsistent
structure, silently changed units/currency, or unstated assumptions make
comparisons untrustworthy and hard to follow.

### V. Performance and Resilience

The agent MUST provide responses within reasonable, measurable performance
limits (defined per-deployment as explicit latency targets, e.g., p95 response
time), and these targets MUST be tracked, not just aspirational.

External calls (to MCP tools, catalog/pricing/inventory APIs, and the LLM
provider) MUST use timeouts, controlled retries (bounded count, backoff), and
graceful fallbacks rather than allowing a single slow or failing dependency to
hang or crash the whole interaction.

Independent data requests (e.g., fetching price and availability for
different products, or from different sources) SHOULD run concurrently where
the underlying APIs and rate limits allow it, to reduce end-to-end latency.

The system MUST avoid unnecessary LLM calls, tool calls, and excessive
context; each call and each piece of context included in a prompt MUST serve
the current request.

Partial service failures (e.g., one data source is down) MUST result in an
honest partial response — clearly noting what could not be retrieved — rather
than a complete failure of the whole interaction.

**Rationale**: Retail users expect fast, responsive comparisons; unbounded
retries, serialized independent calls, and all-or-nothing failure handling
make the system slow, expensive, and brittle under real-world upstream
instability.

### VI. Observability and Safe Evolution

Important operations, MCP calls, failures, and performance indicators
(latency, retry counts, fallback triggers) MUST be logged and monitored so
that regressions and outages are detectable rather than discovered from user
complaints.

Logs MUST NOT expose credentials, API keys, tokens, or sensitive user data
(e.g., personally identifying information); logging code MUST be reviewed for
this before merge.

Prompts, MCP tool schemas, and recommendation rules MUST be version-controlled
alongside the code that depends on them, so changes to agent behavior are
traceable and diffable like any other code change.

Significant changes (new principles, new data sources, changed recommendation
logic, changed prompts) MUST be testable, reviewable, and reversible — each
such change MUST be deployable behind a mechanism that allows rollback without
a full redeploy of unrelated components.

**Rationale**: An LLM-driven advisor's behavior is defined as much by prompts
and rules as by code; without version control, logging, and reversibility for
these artifacts, failures become undebuggable and behavior changes become
unaccountable.

## Technology & Data Constraints

- All product facts, prices, and availability MUST be sourced through defined
  MCP tools or equivalent approved integrations; direct, ad hoc data scraping
  or hard-coded product data is prohibited.
- Approved data sources MUST be explicitly documented (which MCP server or API
  backs which fact) so grounding claims in Principle II are auditable.
- Secrets and environment-specific configuration MUST live in environment
  variables or a secrets manager, never in the repository, matching Principle I.
- Concurrency, timeout, and retry parameters (Principle V) MUST be
  configurable per deployment rather than hard-coded constants.

## Development Workflow & Quality Gates

- Every change MUST pass automated linting, type checks, and the full
  automated test suite before merge (Principles I and III).
- Pull requests that add or modify an MCP tool, a recommendation rule, or a
  prompt MUST include or update the corresponding tests (unit and, where the
  change affects an end-to-end scenario, integration) and MUST note the
  logging/observability impact (Principle VI).
- Pull requests that touch grounding logic (data retrieval, fact presentation)
  MUST be reviewed specifically against Principle II (no fabrication, clear
  handling of missing/uncertain data).
- Performance-sensitive changes (new external calls, changed concurrency)
  MUST state their expected latency impact and MUST include or preserve
  timeout/retry/fallback handling (Principle V).

## Governance

This constitution supersedes ad hoc conventions and prior undocumented
practice for the Smart Retail Product Advisor project. Where a proposed
change conflicts with a principle here, the principle wins unless the
constitution itself is amended.

**Amendment procedure**: Amendments are proposed via pull request modifying
this file, must include the rationale for the change and its expected impact
on existing features, and must update the Sync Impact Report at the top of
this file. Amendments require review and explicit approval before merge —
the same gate as any other change under this constitution.

**Versioning policy**: This constitution is versioned independently using
semantic versioning (MAJOR.MINOR.PATCH):
- MAJOR: Backward-incompatible governance changes or removal/redefinition of
  a principle.
- MINOR: A new principle or materially expanded guidance added.
- PATCH: Clarifications, wording fixes, and non-semantic refinements.

**Compliance review**: All pull requests and design reviews MUST verify
compliance with the principles above (see Development Workflow & Quality
Gates). Any deviation MUST be explicitly justified in the pull request
description and, for architectural decisions, recorded in the relevant
plan's Complexity Tracking section. Unjustified complexity or unexplained
principle violations are grounds for rejecting a change.

**Version**: 1.0.0 | **Ratified**: 2026-07-22 | **Last Amended**: 2026-07-22
