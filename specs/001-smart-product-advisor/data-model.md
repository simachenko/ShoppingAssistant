# Phase 1 Data Model: Smart Product Advisor

Each bounded context owns the entities below in its own schema. No entity is shared or
duplicated across services as a writable copy; where one context needs another's data (e.g.,
Advisor needing a product's name), it holds only a lightweight, non-persisted reference/DTO
fetched at request time (marked *(reference only)* below).

## Product Catalog Service

### Product (aggregate root)

| Field | Type | Notes |
|---|---|---|
| `ProductId` | Guid | Identity, referenced by other services by value only |
| `Name` | string | Required, non-empty |
| `BrandId` | Guid | References `Brand` (same context) |
| `CategoryId` | Guid | References `Category` (same context) |
| `Description` | string | Free text used for search |
| `Specifications` | `List<Specification>` | Value objects, see below |
| `SearchKeywords` | `List<string>` | Denormalized for search matching |
| `IsActive` | bool | Soft-disable without deleting catalog history |

**Validation rules**: `Name` and `CategoryId` required; a `Product` must have at least one
`Specification` before it can be marked active (an incomplete draft cannot be searchable).

### Specification (value object)

| Field | Type | Notes |
|---|---|---|
| `Key` | string | e.g., `"camera_mp"`, `"battery_mah"` — category-defined attribute name |
| `Value` | string | Raw value; typed comparison handled by Advisor via `Key`-specific parsing |
| `Unit` | string? | e.g., `"MP"`, `"mAh"`; null if unit-less |

### Category (entity)

| Field | Type | Notes |
|---|---|---|
| `CategoryId` | Guid | Identity |
| `Name` | string | e.g., "Smartphones", "Laptops" |
| `ComparableAttributeKeys` | `List<string>` | The canonical, ordered set of `Specification.Key`s used whenever products in this category are compared — this is what guarantees FR-006/SC-002 (identical criteria, same order) |

### Brand (entity)

| Field | Type | Notes |
|---|---|---|
| `BrandId` | Guid | Identity |
| `Name` | string | Required, unique |

**Relationships**: `Product` → `Category` (many-to-one), `Product` → `Brand` (many-to-one),
`Product` *(owns)* → `Specification` (one-to-many, value objects).

### CharacteristicFilter (value object, request-only — not persisted)

| Field | Type | Notes |
|---|---|---|
| `Key` | string | Specification key to filter on, e.g. `"camera_mp"` |
| `Operator` | enum | `Equals`, `GreaterThanOrEqual`, `LessThanOrEqual`, `Between` |
| `Value` | string | Comparison value; parsed numerically when the operator is ordinal |
| `ValueTo` | string? | Required only when `Operator = Between`, the upper bound |

**Validation rules**: `ValueTo` MUST be present when `Operator = Between` and MUST be absent
otherwise. A `Key` that doesn't exist for any product in the searched scope yields zero matches
for that condition (spec.md edge case), not a validation error — an unknown attribute is a valid,
just unsatisfiable, filter.

**Evaluation**: applied in `ProductCatalog.Application` in-process, after category/free-text
filtering has already narrowed the row set via SQL (research.md §13) — `Specification` is stored
as a JSON document per product, which doesn't push cleanly into arbitrary per-operator SQL
predicates via EF Core's LINQ provider at this catalog's scale.

---

## Pricing and Availability Service

### Offer (aggregate root)

| Field | Type | Notes |
|---|---|---|
| `OfferId` | Guid | Identity |
| `ProductId` | Guid | *(reference only — no FK; correlates to Catalog's `Product.ProductId`)* |
| `Price` | Money (value object) | Current price |
| `Discount` | Discount? (value object) | Optional, see below |
| `Availability` | StockStatus (enum) | `InStock`, `LimitedStock`, `OutOfStock`, `Unknown` |
| `AsOf` | DateTimeOffset | Timestamp the price/availability was last confirmed — this is the "data freshness" the spec requires the advisor to be able to disclose |
| `Source` | string | Which upstream feed/system produced this record (manual seed data now; a real retailer API later) |

### Money (value object)

| Field | Type | Notes |
|---|---|---|
| `Amount` | decimal | > 0 |
| `Currency` | string | ISO 4217 code, e.g., `"UAH"` |

### Discount (value object)

| Field | Type | Notes |
|---|---|---|
| `PercentOff` | decimal | 0–100 |
| `ValidUntil` | DateTimeOffset? | Null = no expiry known |

**Validation rules**: `Price.Amount` must be non-negative; `Availability` MUST default to
`Unknown` (never silently `InStock`) if the upstream source didn't confirm it — this is what
lets the advisor say "cannot be verified" per FR-005 instead of guessing stock.

**Relationships**: `Offer` *(references)* `Product` by `ProductId` only — no cross-database
foreign key, no cross-service query; the Advisor joins Catalog and Pricing data in memory per
request.

---

## Product Advisor Service

### ConversationSession (aggregate root)

| Field | Type | Notes |
|---|---|---|
| `SessionId` | Guid | Identity |
| `UserId` | string | The owning user's stable identifier (Google token's `sub` claim), set once at creation from the caller's validated identity and never changed (FR-031, research.md §17) — every session-scoped request MUST be checked against this before returning the session's content |
| `Messages` | `List<ConversationMessage>` | Ordered turn history (role, text, timestamp) — persisted in full regardless of `RequestGuardrails`' max active conversation context size, which bounds only what's *included in a prompt* (FR-112), never what's stored or shown in the conversation view (FR-023) |
| `CurrentRequirement` | UserRequirement (value object) | The latest known snapshot of what the user wants — persists across turns until changed |
| `PendingClarification` | ClarificationQuestion? | Set when essential info is missing; cleared once answered |
| `LastSearchResults` | `List<SearchResultReference>` | The most recently shown search/recommendation/comparison candidates (id + name only) — lets an ordinal follow-up ("the first two", "the cheaper one") resolve to concrete ids (FR-022) instead of requiring the LLM to reconstruct them from prior prose. Replaced, never appended to, each time a new result set is produced — bounded, not a full history. |

**Note**: an earlier revision of this table listed a separate `LastRecommendation` field; that
was never implemented as its own field — `LastSearchResults` (generalized, research.md §15)
covers the same follow-up-resolution need for recommendations, comparisons, and searches alike,
so this table now reflects what actually exists rather than a superseded plan.

Streaming (research.md §11) is a transport/presentation concern only — a `ConversationMessage`
always stores the complete, final assistant text once a turn ends, never a partial fragment.
Whether the API delivered that text as one JSON response or as a sequence of SSE `token` events
has no bearing on what gets persisted.

**State transitions**: `Collecting` (gathering requirement, may hold a `PendingClarification`)
→ `Recommending` (requirement has at minimum Category + Budget, deterministic scoring runs) →
`Comparing` (user asked to compare specific/candidate products) → back to `Collecting` if the
user changes a constraint (FR-011 — prior recommendation is superseded, not silently merged).

### User (external identity — not persisted anywhere in this system)

| Field | Type | Notes |
|---|---|---|
| `UserId` | string | Google token's `sub` claim — stable, unique per Google account |
| `Email` | string | Google token's `email` claim — used for display only, never as the identity key (emails can change; `sub` cannot) |

There is no local `Users` table in any service. Every request's identity is established fresh
from a validated Google-issued token (research.md §17); `ConversationSession.UserId` is the only
place a user's identity is retained, and only as an opaque string used for ownership checks —
this system builds no account/profile/password system of its own (FR-030, spec.md Assumptions).

### CheckoutLink (value object, request-scoped — not persisted)

| Field | Type | Notes |
|---|---|---|
| `Url` | string | The retailer's checkout destination, with the selected products' `ProductId`s encoded as query parameters |
| `ProductIds` | `List<Guid>` | Exactly the products the user referenced (by name or by `LastSearchResults` ordinal/descriptive reference, FR-022) — never more, never fewer (SC-015) |

Constructed deterministically from already-known product identifiers by
`ProductAdvisor.Infrastructure` (FR-025) — the destination base URL is configuration, not
user/LLM input; the LLM never chooses or alters which products' ids end up in the link, only
narrates that the link was created.

### UserRequirement (value object — persisted as `ConversationSession.CurrentRequirement`, the
sole authoritative source of the fields below for every stage after deterministic state merge,
FR-055)

| Field | Type | Notes |
|---|---|---|
| `Category` | string? | Null until known |
| `Budget` | Money? | Hard constraint (FR-080) — a ceiling, never a target; a product priced above it, or priced in a currency other than `Currency` below, is excluded (FR-084) |
| `RequiredFeatures` | `List<string>` | Hard constraints (FR-080) — free-form statements extracted from NL input, including any "other constraint marked mandatory" that isn't budget/currency/availability specifically (e.g., a stated brand requirement); a product missing any of these is excluded. Bounded by `RequestGuardrails`' max count/per-entry length (FR-106), enforced cumulatively across turns, not just per-patch |
| `Preferences` | `List<string>` | Soft preferences (e.g., "good camera") — influence ranking/scoring only, never eligibility (FR-083), distinguished from hard requirements above. Bounded the same way as `RequiredFeatures` (FR-106) |
| `Language` | string | BCP-47 tag, preserved per FR-011 |
| `Currency` | string | ISO 4217, preserved per FR-011; hard constraint via currency compatibility (FR-080/FR-084) — a candidate priced in a different currency is excluded, never silently converted |
| `Units` | string? | Preferred measurement convention for ambiguous values (FR-011/FR-055); null until stated, preserved the same way as every other field |
| `AvailabilityRequirements` | `List<string>` | Explicit stock/timing conditions the user stated (e.g., "must be in stock now"); empty until stated (FR-055) — a hard constraint only when non-empty (FR-080/FR-085); when empty, availability is informational only (FR-012), never disqualifying. Bounded the same way as `RequiredFeatures`/`Preferences` (FR-106) |

**Validation rules**: A recommendation MAY only be produced once `Category` and `Budget` are
both non-null (constitution/spec "essential information" bar); otherwise a
`ClarificationQuestion` MUST be produced instead (FR-002).

**Merge rules** (FR-056–FR-059, applied by the deterministic state-merge stage from a schema-valid
`StructuredIntent.RequirementPatch`, never by the language model writing this object directly):
a field **present** with a value in the patch replaces the corresponding field here; a field
**absent** from the patch leaves the existing value here untouched — absence is never treated as
"clear this field." For the three list-typed fields (`RequiredFeatures`, `Preferences`,
`AvailabilityRequirements`) only, the patch MAY explicitly send an empty list to mean "the user no
longer wants this constraint," and that MUST be applied as a real clear, distinct from the field
being absent from the patch. Because this object is the sole authoritative source of these fields
(FR-055), no stage after state merge may re-derive any of them from the raw message or the full
conversation transcript — they are read from here.

### ClarificationQuestion (value object)

| Field | Type | Notes |
|---|---|---|
| `MissingField` | string | Which `UserRequirement` field is missing (e.g., `"Budget"`) |
| `QuestionText` | string | The single focused question surfaced to the user |

**Validation rule**: Only one `ClarificationQuestion` may be pending at a time (FR-003 — one
focused question, not a list).

### SearchResultReference (value object, part of `ConversationSession.LastSearchResults`)

| Field | Type | Notes |
|---|---|---|
| `ProductId` | Guid | *(reference only)* |
| `Name` | string | Display name only, for resolving an ordinal reference back to an id — no specs/price/availability are cached here; a follow-up that needs those re-fetches them fresh (research.md §15) |

### ProductCandidate (value object, *not persisted* — assembled per request from Catalog + Pricing HTTP responses)

| Field | Type | Notes |
|---|---|---|
| `ProductId` | Guid | *(reference only)* |
| `Name`, `BrandName`, `CategoryName` | string | From Catalog |
| `Specifications` | `List<Specification>` | From Catalog |
| `Price` | Money? | From Pricing; null + `PriceVerified = false` if unavailable |
| `Availability` | StockStatus? | From Pricing; null + `AvailabilityVerified = false` if unavailable |
| `PriceVerified` / `AvailabilityVerified` | bool | Drives the "cannot be verified" messaging (FR-005) |

### Recommendation (entity — the typed result of a `get_recommendations` tool call)

| Field | Type | Notes |
|---|---|---|
| `RecommendationId` | Guid | Identity |
| `Items` | `List<RecommendedItem>` | Ranked, deterministic-scored candidates that satisfy **every** hard constraint (FR-080/FR-081) — never a product confirmed to violate one |
| `UnmetConstraintExplanation` | string? | Set instead of `Items` being non-empty-but-wrong when nothing fully matches (FR-010) |
| `NearestAlternatives` | `List<NearestAlternative>` | Only ever populated alongside `UnmetConstraintExplanation` (i.e., only when `Items` is empty, FR-082) — never mixed into `Items`; MAY be empty even when `Items` is empty (surfacing alternatives is optional, not required) |

**Mutual exclusivity** (FR-010, extended by FR-081/FR-082): `Items` is non-empty XOR
(`UnmetConstraintExplanation` is set, optionally with `NearestAlternatives`) — never both a
non-empty `Items` and a set `UnmetConstraintExplanation`/non-empty `NearestAlternatives` at once.

Produced **only** by the `get_recommendations` tool handler (see
`contracts/advisor-mcp-tools.md`); the conversation orchestration loop never constructs one
itself — it only stores whatever the tool returned onto `ConversationSession.LastRecommendation`
and lets the LLM narrate it.

### RecommendedItem (value object)

| Field | Type | Notes |
|---|---|---|
| `Candidate` | ProductCandidate | The recommended product — already confirmed to satisfy every hard constraint (FR-080/FR-081) |
| `MatchedRequirements` | `List<string>` | Which parts of `UserRequirement` this product satisfies (FR-008) — deterministically derived (e.g., "budget ≤ X: yes") |
| `TradeOffs` | `List<string>` | At least one required (FR-009) — deterministically derived (e.g., any attribute below the category median is flagged); the LLM may elaborate on a flagged trade-off in prose but does not decide which attributes qualify |
| `Score` | decimal | Deterministic score from `ScoringPolicy`, driven by soft preferences (`UserRequirement.Preferences`, FR-083) — affects only ranking, never eligibility; never shown as a fabricated "fact" |

### NearestAlternative (value object, part of `Recommendation.NearestAlternatives`)

| Field | Type | Notes |
|---|---|---|
| `Candidate` | ProductCandidate | A product that does **not** satisfy every hard constraint — never eligible for `Recommendation.Items` |
| `ViolatedConstraints` | `List<string>` | Deterministically derived, one entry per violated hard constraint (e.g., `"budget: 16000 UAH exceeds 15000 UAH ceiling"`, `"currency: priced in USD, requirement stated UAH"`) — MUST name at least one; an alternative with zero violated constraints would, by definition, belong in `Items` instead (FR-081) |

Never presented to the user without its `ViolatedConstraints` labeling (FR-082) — the UI/narration
MUST render a `NearestAlternative` visibly distinct from a qualifying `RecommendedItem`, never
merged into the same list.

### Comparison (entity — the typed result of a `compare_products` tool call)

| Field | Type | Notes |
|---|---|---|
| `ComparisonId` | Guid | Identity |
| `Criteria` | `List<string>` | The shared, ordered attribute list (sourced from `Category.ComparableAttributeKeys`) — identical for every product in the set (FR-006/SC-002) |
| `Rows` | `List<ComparisonRow>` | One per compared product |

Produced **only** by one shared comparison-composition service inside
`ProductAdvisor.Infrastructure` (research.md §14), called from **two** entry points: the
conversational `compare_products` tool handler, and a new stateless `POST /api/comparisons`
endpoint that takes a known product-id set directly with no conversation turn at all (FR-018).
Both paths call the identical code, so results for the same product-id set are byte-identical
regardless of which one triggered it (SC-010) — the orchestration loop, in the conversational
case, just stores and relays whatever the shared service returned; it never constructs one.

### ComparisonRow (value object)

| Field | Type | Notes |
|---|---|---|
| `Candidate` | ProductCandidate | The compared product |
| `ValuesByCriterion` | `Dictionary<string, string?>` | Null value + verified flag (via `ProductCandidate`) when a criterion can't be verified for that product |
| `Rating` | decimal | Deterministic composite rating for this product from `ComparisonEngine`, computed the same way for every row in the set |
| `DeltasVsBest` | `Dictionary<string, string>` | Per-criterion computed difference from the best value present in the set (e.g., `"camera_mp": "-12 vs best"`, `"price": "+1500 UAH vs cheapest"`) — the LLM restates these, it does not compute them |

### ScoringPolicy (domain service, invoked only from the `get_recommendations` tool handler)

Pure function: `(UserRequirement, IEnumerable<ProductCandidate>) → (IEnumerable<RecommendedItem>,
IEnumerable<NearestAlternative>)`. Two-phase: **(1) hard-constraint filtering** (FR-080) —
deterministically excludes any candidate that violates budget (price > `Budget`, or price
verified in a currency other than `Currency`, FR-084), any `RequiredFeatures` entry, or, only
when non-empty, any `AvailabilityRequirements` entry (FR-085) — surviving candidates become
`RecommendedItem`s, excluded ones become `NearestAlternative`s carrying exactly which
constraint(s) they failed (FR-082); **(2) soft-preference scoring** (FR-083) — ranks only the
surviving candidates by how many `Preferences` entries they match, never re-excludes anything at
this phase — plus deterministic trade-off flagging. No I/O, no LLM call — fully unit-testable in
isolation (constitution Principle III), including a fixed candidate set asserted to split
identically into `Items`/`NearestAlternatives` across repeated calls. Called exclusively by the
`get_recommendations` tool's handler in `ProductAdvisor.Infrastructure`; the conversation
orchestration loop in `ProductAdvisor.Application` never calls it directly.

### ComparisonEngine (domain service, invoked only from the `compare_products` tool handler)

Pure function: `(IEnumerable<ProductCandidate>, List<string> criteria) → Comparison`. Computes
`ValuesByCriterion`, the deterministic `Rating` per row, and `DeltasVsBest` per row. No I/O, no
LLM call — unit-tested with fixed candidate sets so rating/delta output is asserted to be
identical across repeated calls (proving no non-determinism sneaks in). Called exclusively by
the shared comparison-composition service in `ProductAdvisor.Infrastructure` (research.md §14),
which both the `compare_products` tool handler and the direct `POST /api/comparisons` endpoint
call — never called directly by the orchestration loop, and never re-implemented a second time
for the direct-endpoint path.

### Optional comparison explanation (produced only by `POST /api/comparisons`, FR-019)

When `POST /api/comparisons` is called with `includeExplanation: true` (the default), a second,
narrowly-scoped LLM call — separate from the computation above — receives only the already-built
`Comparison` and returns a short narrative summary. It cannot see or influence
`ValuesByCriterion`/`Rating`/`DeltasVsBest`; if the call fails or is disabled, the response's
`explanation` field is `null` and the `comparison` data is still returned in full (constitution
Principle V — narration's absence never blocks the structured result).

**Relationships**: `ConversationSession` *(owns)* `UserRequirement`, `ClarificationQuestion?`,
`Recommendation?`, `LastSearchResults` (`List<SearchResultReference>`); `Recommendation` *(owns)*
`RecommendedItem`s, each holding a `ProductCandidate` (itself an in-memory join of Catalog +
Pricing data, never persisted as a duplicate table). `ScoringPolicy` is a domain service with
exactly one caller — the `get_recommendations` tool handler. `ComparisonEngine` is a domain
service with exactly one caller — the shared comparison-composition service in
`ProductAdvisor.Infrastructure` (research.md §14) — which is itself called from two places
(`compare_products` tool handler, `POST /api/comparisons`). Neither domain service is ever called
directly by the `ProductAdvisor.Application` conversation loop.

### SystemReadinessStatus (transient, request-scoped — not persisted)

The response shape of Gateway's `GET /api/system-status` (FR-033/FR-034/FR-035, research.md §19)
— a point-in-time snapshot only, never stored, and never a substitute for a request's own honest
handling of an unavailable dependency (constitution Principle V).

| Field | Type | Notes |
|---|---|---|
| `Overall` | `"ready"` \| `"degraded"` | `"degraded"` when one or more entries in `Services` are `Reachable: false` |
| `Services` | `List<ServiceReadiness>` | One entry per internal service the advisor depends on (Catalog, Pricing, Advisor) |

`ServiceReadiness`: `Name` (string, e.g. `"catalog-api"`), `Reachable` (bool — the result of
calling that service's own `/alive` with a short timeout), `CheckedAt` (`DateTimeOffset`, when
this particular check ran). Built fresh on every call to `GET /api/system-status` by concurrently
probing each service's existing liveness endpoint (`Task.WhenAll`, the same pushdown-composition
pattern as `GET /api/products/{productId}`) — no caching, no separate readiness store.

### StructuredIntent (transient, request-scoped — not persisted)

The formal-schema-validated output of the turn-processing cycle's structured-intent-extraction
stage (FR-036–FR-054, research.md §20/§21). Built fresh on every turn; superseded, never
accumulated; the schema itself is versioned but its exact serialization format is an
implementation detail left open by spec.md's Assumptions.

| Field | Type | Notes |
|---|---|---|
| `Intent` | enum: `Recommend` \| `ProductFact` \| `Compare` \| `Checkout` \| `Smalltalk` \| `Unsupported` | Closed set (FR-048/FR-049) — any other value is a schema-validation failure, never a new route |
| `RequirementPatch` | object | The changes this turn implies for `UserRequirement`/session state — applied by deterministic state merge (FR-040, field-level merge rules FR-056–FR-059) to `ConversationSession.CurrentRequirement`, never by the model writing state directly. Shape mirrors `UserRequirement`'s fields (category, budget/currency, hard constraints, soft preferences, language, units, availability requirements), each independently optional so a patch can supply just one field; the three list-typed fields distinguish "absent" (leave untouched) from "explicitly empty" (clear), see `UserRequirement`'s Merge rules above. |
| `ProductReferences` | `List<string>` | Products the user referred to, by name or by ordinal/session-memory reference (resolved against `LastSearchResults`, FR-022) |
| `MissingFields` | `List<string>` | Essential information still absent for `Intent` — drives whether policy routing selects the clarification route |
| `Confidence` | number | Below the system's configured threshold (an implementation/tuning detail, spec.md Assumptions) routes to a focused clarification (FR-053), never a best-guess |
| `Language` | string | The message's language, carried forward to preserve the user's stated language in the response (FR-011/FR-054) |

**Explicitly excluded from this shape**: any chain-of-thought, rationale, or other intermediate
reasoning the model produced while extracting (FR-052) — that text never crosses the extraction
stage's boundary, is never part of this record, and is never persisted or returned by any API.

**Validation**: an extraction attempt that fails schema validation against this shape is retried
at most once (a single repair attempt, FR-051); a second failure — or a first failure with no
repair configured — falls back to a clarification turn rather than a `StructuredIntent` instance
ever being constructed from invalid data. A `StructuredIntent` therefore either fully satisfies
this shape or does not exist for that turn.

### TurnResult (the discriminated shape returned to the client for every completed turn, FR-060–FR-065)

Exactly one of seven mutually exclusive types, assigned by policy routing's selected route
together with that route's validated tool outcome (never inferred from or overridden by
narration text, FR-061). The absence of a `recommendation`, `comparison`, or `checkoutLink`
never defaults a turn to `clarification` (FR-062) — each type below is a first-class outcome.

| `type` | Populated from | Notes |
|---|---|---|
| `answer` | `product_fact`-intent search/detail/price tool result (US3), or nothing (`smalltalk`) | Carries a verified fact (`value`/`verified` copied from the tool result, FR-004/FR-005) or, for `smalltalk`, no structured field at all — just a plain reply (FR-063, spec.md Assumptions) |
| `clarification` | Missing-info/ambiguous-reference determination only (FR-002/FR-039/FR-050/FR-053) | Never a generic fallback for "nothing else fit" (FR-062) |
| `recommendation` | `get_recommendations` tool result | See `Recommendation`/`RecommendedItem` above |
| `comparison` | `compare_products` tool result | See `Comparison`/`ComparisonRow` above |
| `checkoutLink` | `generate_checkout_link` tool result | See `CheckoutLink` above |
| `unsupported` | `unsupported`-intent route (FR-048/FR-049) | Explains the request is out of scope; never remapped to `clarification` or `error` (FR-064) |
| `error` | Tool-result validation failure (FR-043) or an unavailable dependency leaving no other type producible | Carries a `degraded` indicator: `true` = temporary/retryable, `false` = not fulfillable at all (FR-065). Reserved for when *no* type-specific result can be honestly produced — a partial outage that still yields a type-specific result (e.g., pricing down mid-recommendation) stays that type with unverified fields (FR-005), it does not escalate to `error` (spec.md Assumptions). |

Constrained narration (the cycle's narration stage) only describes whichever type policy routing
and the tool recipe already determined — it has no authority to select or change `type` itself.

### ToolRecipe (per-route, not a persisted entity — FR-066–FR-070, research.md §24)

The fixed, minimal set of MCP tools (`contracts/advisor-mcp-tools.md`) each policy-routing route
may invoke for a turn. Never the full seven-tool catalog; a tool outside the current route's
recipe is not reachable for that turn (FR-068).

| Route | Recipe (in order) | Tool kind(s) used |
|---|---|---|
| `product_fact` | resolve product → exact id, then `get_product_details` and/or `check_price_and_availability` (only what the fact needs) | read-only |
| `recommend` | validate essential fields (FR-002) → normalize `CurrentRequirement` → `get_recommendations` (exactly once) | compute (terminal) |
| `compare` | resolve every referenced product → exact ids (≥2 required, else `clarification`) → `compare_products` (exactly once) | read-only (resolution) + compute (terminal) |
| `checkout` | resolve every referenced product → exact ids → validate concrete/non-empty (else `clarification`) → `generate_checkout_link` (exactly once) | read-only (resolution) + compute (terminal) |
| `smalltalk` | *(none)* | — zero tool calls |
| `unsupported` | *(none)* | — zero tool calls |

**Tool kinds**: *read-only* (`search_products`, `get_category`, `get_product_details`,
`check_price_and_availability`) — independent read-only calls within a recipe's resolution phase
may run concurrently only when mutually order-independent and deterministically identical
regardless of concurrency (FR-070). *Compute* (`get_recommendations`, `compare_products`,
`generate_checkout_link`) — the single terminal call a recipe treats as producing that turn's
final result; MUST NOT run concurrently with a *stateful* tool call or another compute call
(FR-069). *Stateful* (none in this catalog today — forward-looking category only) — a tool that
creates or mutates persisted/shared state; MUST NOT run concurrently with a compute call or
another stateful call.

### TurnResourceBudget (deployment configuration, not persisted — FR-071–FR-079, research.md §25)

The set of hard limits enforced for every turn's processing. Values are configuration (may
differ per environment); the existence of each limit and its fail-safe outcome when reached are
fixed by spec.md, not the numbers themselves.

| Limit | Enforced against | Fail-safe on reaching it |
|---|---|---|
| Max primary LLM calls | Always exactly 2 (extraction + narration), fixed, not configurable | N/A — architectural, not a tunable ceiling |
| Max repair attempts | Always exactly 1 (FR-051), fixed, not configurable | Second failure → `clarification` |
| Max tool calls per turn | Total tool calls placed by a turn's recipe | `error` |
| Identical-call repetition | Same tool + same input called again within a turn | Counts against the two budgets below — never exempt |
| Max loop iterations | Any bounded loop realizing a recipe (e.g., per-id resolution) | End the loop; `error` if no valid result |
| Max consecutive tool errors | A run of failing tool calls within a turn | `error` |
| Overall turn timeout | Wall-clock time from input validation through persistence | `error`, or the streaming endpoint's no-`result`-event failure |
| Client disconnect | Detected mid-turn | Cancel in-flight work; persist nothing; release the FR-024 in-flight-turn marker |
| Non-idempotent operation | Any stateful tool call (FR-069) | Excluded from automatic resilience-layer retry (research.md §6) |

These budgets are layered on top of, not a replacement for, each individual outbound call's own
`Microsoft.Extensions.Http.Resilience` policy (research.md §6, per-call timeout/retry/circuit
breaker) — the per-call policy governs one call's own resilience; `TurnResourceBudget` governs
the turn as a whole across however many such calls it makes.

### EvidenceEnvelope (transient, request-scoped — not persisted — FR-086–FR-092, research.md §27)

The single, deterministically-assembled package the constrained-narration language-model call
receives as its **only** factual input. Built by application code from that turn's already
tool-result-validated data (FR-043), after the recipe's terminal call and before narration is
invoked; never built by the language model, never influenced by narration text (which doesn't
exist yet at assembly time).

| Field | Type | Notes |
|---|---|---|
| `ResultType` | Same closed set as `TurnResult.Type` | Known before narration runs — policy routing and the tool recipe already determined it |
| `CanonicalData` | Route-specific structured shape (`Recommendation`, `Comparison`, `CheckoutLink`, a fact record, or empty for `smalltalk`/`unsupported`) | Byte-identical to what `TurnResult` will also carry — narration never sees a different copy |
| `VerificationStatus` | `Dictionary<field, bool>` | One entry per verifiable field (mirrors `ProductCandidate.PriceVerified`/`AvailabilityVerified`, `ComparisonRow` null-handling) |
| `Provenance` | `Dictionary<field, string>` | Which tool call produced each part of `CanonicalData` (e.g., `"price" → "check_price_and_availability"`) — every `CanonicalData` field MUST have an entry here (FR-092) |
| `UnverifiedOrUnavailableFields` | `List<string>` | Fields present in `VerificationStatus` as `false`, surfaced as an explicit list for narration/validation convenience |
| `ToolExecutionStatus` | `List<{ Tool: string, Succeeded: bool }>` | One entry per tool call actually made in this turn's recipe |
| `AllowedClaims` | `List<string>` | Deterministically derived from `CanonicalData` — the exact whitelist output validation checks narration against (FR-088) |

**Empty envelopes**: for `smalltalk`/`unsupported` turns (zero-tool-call recipes, FR-067),
`CanonicalData`/`Provenance`/`AllowedClaims` are empty — this is not "no envelope," it's an
envelope that correctly permits zero numeric/factual product claims in that turn's narration.

**Relationship to `TurnResult`**: the Envelope is narration's internal input and is never itself
returned to the client; `TurnResult` is the turn's external output, assembled from the same
`CanonicalData` after narration completes (or its deterministic fallback is substituted, FR-090).
A rejected/stripped/replaced narration changes only `TurnResult.Message`/`Question` — never
`TurnResult`'s structured fields or `Type`, both of which come from `CanonicalData` directly
(FR-089).

### SystemPrompt (deployment/configuration artifact, not persisted per-turn — FR-093–FR-103, research.md §28)

Exactly two instances exist: `Extraction` and `Narration`. Each is versioned (`VersionId`) and
its version is logged with every call that used it (FR-101) — distinct from source-control
history, a runtime-observable property.

| Section | Extraction prompt | Narration prompt |
|---|---|---|
| System instructions | Task definition, closed intent set, schema-first output directive (FR-094), language-response instruction (FR-098), anti-disclosure instruction (FR-099), no-chain-of-thought instruction (FR-100) | Task definition, "summarize salient points, don't restate every value" directive (FR-103), language-response instruction, anti-disclosure instruction, no-chain-of-thought instruction |
| Application/session state | `CurrentRequirement` (FR-095), verbatim | `CurrentRequirement` (FR-095), verbatim |
| User input | Raw user message, explicitly marked untrusted data (FR-097) | *(not applicable — narration doesn't see the raw message, only the Envelope, FR-087)* |
| Tool/catalog data | *(not applicable — extraction runs before any tool call)* | `EvidenceEnvelope` (FR-086), explicitly marked untrusted-as-instruction but authoritative-as-fact (FR-097) |
| Few-shot examples | Only for specific, genuinely complex edge cases (FR-102) — none by default | Only for specific, genuinely complex edge cases (FR-102) — none by default |

Both prompts share the same non-negotiable properties (FR-096–FR-102) despite governing
different stages; the table above only documents where each section's *content* differs.

### RequestGuardrails (deployment configuration, not persisted — FR-104–FR-113, research.md §29)

Admission-control and resource-protection limits, enforced before or independent of a single
turn's own processing — distinct from `TurnResourceBudget` (which governs an already-admitted
turn's own LLM/tool usage). Every violation fails safe: a controlled rejection with zero
language-model or tool invocation (FR-113).

| Guardrail | Enforced against | Enforcement point |
|---|---|---|
| Max message length | Raw user message | Input-validation stage, before extraction |
| Max request body size | HTTP request body | Transport layer, before parsing into a turn |
| Max count / per-entry length | `RequiredFeatures`, `Preferences`, `AvailabilityRequirements` | `requirementPatch` validation and state-merge (applies cumulatively across turns, not just per-patch) |
| Unicode normalization + control-character rejection | Raw user message | Input-validation stage, before extraction |
| Strict format/range/set validation | Currency, budget, characteristic operators, units, product ids | Extraction-output schema validation and tool-input validation |
| Per-user rate limit | Requests keyed by authenticated user identifier (FR-030) | Request admission, independent of per-session limit |
| Per-user concurrency limit | Turns in flight across all of a user's sessions | Request admission, generalizes FR-024's per-session lock |
| Token/cost quota | Cumulative LLM token/cost usage per user per configured window | Request admission, independent of per-turn `TurnResourceBudget` |
| Max active conversation context size | Content included in a prompt from `ConversationSession.Messages` | Prompt assembly — bounds what's *included*, never what's *persisted* (FR-023's full transcript is unaffected) |

The exact numeric value of each guardrail is deployment configuration; the guardrail's existence
and its no-LLM/no-tool-invocation fail-safe are fixed by spec.md (same posture as
`TurnResourceBudget`).

### DataProtectionPolicy (deployment configuration + lifecycle process, not a single persisted
record — FR-114–FR-123, research.md §30)

The privacy-by-design controls governing conversation data across its lifecycle. **Supersedes**
this document's earlier framing of `ConversationSession`/`UserRequirement` as data needing no
special privacy handling (spec.md's superseded Assumption, Clarifications Session 2026-08-10).

| Control | Applies to | Notes |
|---|---|---|
| PII screening | Raw user message, before any LLM-provider call | Block or redact on detection (FR-116) — never pass through unredacted; independent of `RequestGuardrails` (content sensitivity vs. size/format/rate) |
| Minimal-necessary-context prompts | `SystemPrompt` assembly (both Extraction and Narration) | Extends FR-095/FR-112 with an explicit privacy rationale, not only a cost rationale (FR-117) |
| Stable-identifier exclusion | `ConversationSession.UserId` | MUST NOT appear in any prompt absent a stated functional need (FR-118) — neither current prompt has one |
| User-initiated deletion | A session, or all of a user's sessions | New capability — see `contracts/advisor-conversation-api.md` `DELETE` endpoints (FR-119) |
| Retention-based automatic deletion | Sessions older than a configured retention period | Independent of and in addition to user-initiated deletion (FR-120) |
| Encryption in transit | Browser↔system, internal service-to-service, system↔LLM provider | FR-121 |
| Encryption at rest + backups | The persistent store and any backup of it | Backup MUST be encrypted at least as strongly as the primary store (FR-122) |
| LLM-provider requirements | The configured LLM provider (research.md §10's swappable-provider design) | No training on submitted content (or explicitly disabled), bounded/known retention, known/acceptable data region (FR-123) — evaluated at provider-selection time, not per request |

### PiiScreeningResult (transient, request-scoped — not persisted, FR-116)

| Field | Type | Notes |
|---|---|---|
| `Flagged` | bool | Whether potential PII was detected in the raw message |
| `Action` | `"Blocked"` \| `"Redacted"` | Which of the two allowed responses was taken when `Flagged` is true |
| `RedactedText` | string? | The message with flagged spans replaced (e.g., `[redacted]`), present only when `Action = "Redacted"` — this, never the original raw text, is what may proceed to extraction |

Produced by a screening step that runs before the structured-intent-extraction call — for
`Flagged = false`, the original message proceeds unchanged; for `Flagged = true`, only
`RedactedText` (if `Action = "Redacted"`) may proceed, or the turn is rejected outright (if
`Action = "Blocked"`) before any language-model call is made, mirroring the input-validation
stage's existing "reject before any LLM call" posture (FR-037/FR-104).

### InternalCredentialPolicy (deployment configuration, not persisted — FR-124–FR-132, research.md
§31)

Governs the MCP endpoint's and every internal service-to-service call's credential (elaborates
`InternalApiKey`, research.md §18). Not a new credential mechanism — a set of hardening
requirements layered on the existing one.

| Control | Requirement | Notes |
|---|---|---|
| Access without credential | Never served, under any configuration | FR-124 |
| Storage | Secret-storage mechanism only (env var, secrets manager, Aspire parameter, Render `sync: false`) | FR-125 — never source control, never hardcoded |
| Rotation | Supported without an application code change; bounded old/new overlap window | FR-126 |
| Production default | MUST NOT fall back to a development/example value | FR-127 — an unconfigured production credential refuses every caller |
| Comparison | Constant-time | FR-128 — execution time independent of match length |
| Scoping | SHOULD be scoped per service relationship as the system grows | FR-129 — single shared secret remains an acceptable baseline today (research.md §18) |
| Tool execution | Least-privilege — no more access than the specific call needs | FR-130 |
| Conversation ownership | Never granted automatically from credential presentation alone | FR-131 — still requires the same `ConversationSession` ownership check as the conversation API (FR-031) |
| Preview/prerelease dependencies | Distinct production-readiness review required before production reliance | FR-132 — e.g., this system's own `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` preview package |

### ObservabilityPolicy (governs the shared logging/tracing/metrics pipeline, not persisted —
FR-133–FR-137, research.md §32)

Content-level allow/deny rules for what a turn's logs may carry, layered on the existing
OpenTelemetry-based pipeline (research.md §7/§16) — not a new pipeline.

| Allowed (FR-133) | Denied by default (FR-134) |
|---|---|
| Correlation id | Full raw user message |
| Hashed/pseudonymous user/session identifier (never the raw stable id, FR-137) | Full assembled prompt (any section) |
| Prompt version (FR-101) | Tool call arguments/results containing PII |
| Model identifier | Authorization/credential header values |
| Classified intent (FR-048's closed set) | API keys |
| Tool name(s) invoked | Database/service connection strings |
| Allow/deny decision (coarse, e.g. `"rate-limit: denied"` — never the triggering content) | Full raw LLM response text |
| Latency | |
| Token usage | |
| Validation status per stage | |
| Coarse error category | |

**Dedicated metrics** (FR-136), each distinguishable from general request/error metrics: loop/
iteration-limit reached (FR-074); schema-repair attempted, with outcome (FR-051); tool call
rejected (FR-068/FR-073/FR-108); grounding failure (FR-088); rate-limit rejection
(FR-109/FR-110); PII detection, block or redact (FR-116); LLM-provider failure after resilience
exhausted (research.md §6). Concurrently-applicable metrics for one event are each incremented
independently — never mutually suppressing.

### EvalSuite (test-suite artifact, not a runtime entity — FR-138–FR-141, research.md §33)

The mandatory minimum set of agentic security/quality eval classes, each verifying an existing
guarantee rather than defining new behavior. **Critical** classes gate release at 100%; the rest
run automatically but aren't fixed at 100% by this specification.

| # | Class | Critical category | Verifies |
|---|---|---|---|
| 1 | Direct prompt injection | — | FR-097 |
| 2 | Indirect injection (product/spec) | Grounding | FR-097/FR-088 |
| 3 | System-prompt extraction attempt | Authorization | FR-099/FR-100 |
| 4 | Fabricated prices/specs/availability | Grounding | FR-088/FR-089 |
| 5 | Wrong tool for intent | Authorization | FR-068 |
| 6 | Tool-loop exhaustion | — | FR-072/FR-074/FR-075 |
| 7 | Malformed tool arguments | — | FR-108 |
| 8 | Oversized input | — | FR-104–FR-106/FR-113 |
| 9 | Cross-session access | Cross-session | FR-031 |
| 10 | Memory poisoning | — | FR-057/FR-091/FR-004 |
| 11 | Constraint changes between turns | — | FR-057/FR-058/FR-011 |
| 12 | Product not found | Grounding | FR-004 |
| 13 | Partial dependency failure | — | FR-014, constitution Principle V |
| 14 | Unsupported intent | — | FR-064/FR-067 |
| 15 | PII/payment-data input | — | FR-115/FR-116 |

The critical/non-critical split (FR-140/FR-141) is this specification's own explicit,
revisable judgment call (spec.md Assumptions) — not an inherent property of the fifteen classes.
