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
| `Messages` | `List<ConversationMessage>` | Ordered turn history (role, text, timestamp) |
| `CurrentRequirement` | UserRequirement (value object) | The latest known snapshot of what the user wants — persists across turns until changed |
| `PendingClarification` | ClarificationQuestion? | Set when essential info is missing; cleared once answered |
| `LastRecommendation` | Recommendation? | The most recent recommendation set produced, for follow-up questions (US3) |

**State transitions**: `Collecting` (gathering requirement, may hold a `PendingClarification`)
→ `Recommending` (requirement has at minimum Category + Budget, deterministic scoring runs) →
`Comparing` (user asked to compare specific/candidate products) → back to `Collecting` if the
user changes a constraint (FR-011 — prior recommendation is superseded, not silently merged).

### UserRequirement (value object)

| Field | Type | Notes |
|---|---|---|
| `Category` | string? | Null until known |
| `Budget` | Money? | Null until known; ceiling, not a target |
| `RequiredFeatures` | `List<string>` | Free-form feature statements extracted from NL input |
| `Preferences` | `List<string>` | Soft preferences (e.g., "good camera") distinguished from hard requirements |
| `Language` | string | BCP-47 tag, preserved per FR-011 |
| `Currency` | string | ISO 4217, preserved per FR-011 |

**Validation rules**: A recommendation MAY only be produced once `Category` and `Budget` are
both non-null (constitution/spec "essential information" bar); otherwise a
`ClarificationQuestion` MUST be produced instead (FR-002).

### ClarificationQuestion (value object)

| Field | Type | Notes |
|---|---|---|
| `MissingField` | string | Which `UserRequirement` field is missing (e.g., `"Budget"`) |
| `QuestionText` | string | The single focused question surfaced to the user |

**Validation rule**: Only one `ClarificationQuestion` may be pending at a time (FR-003 — one
focused question, not a list).

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
| `Items` | `List<RecommendedItem>` | Ranked, deterministic-scored candidates that satisfy hard constraints |
| `UnmetConstraintExplanation` | string? | Set instead of `Items` being non-empty-but-wrong when nothing fully matches (FR-010) |

Produced **only** by the `get_recommendations` tool handler (see
`contracts/advisor-mcp-tools.md`); the conversation orchestration loop never constructs one
itself — it only stores whatever the tool returned onto `ConversationSession.LastRecommendation`
and lets the LLM narrate it.

### RecommendedItem (value object)

| Field | Type | Notes |
|---|---|---|
| `Candidate` | ProductCandidate | The recommended product |
| `MatchedRequirements` | `List<string>` | Which parts of `UserRequirement` this product satisfies (FR-008) — deterministically derived (e.g., "budget ≤ X: yes") |
| `TradeOffs` | `List<string>` | At least one required (FR-009) — deterministically derived (e.g., any attribute below the category median is flagged); the LLM may elaborate on a flagged trade-off in prose but does not decide which attributes qualify |
| `Score` | decimal | Deterministic score from `ScoringPolicy`, used only for ranking — never shown as a fabricated "fact" |

### Comparison (entity — the typed result of a `compare_products` tool call)

| Field | Type | Notes |
|---|---|---|
| `ComparisonId` | Guid | Identity |
| `Criteria` | `List<string>` | The shared, ordered attribute list (sourced from `Category.ComparableAttributeKeys`) — identical for every product in the set (FR-006/SC-002) |
| `Rows` | `List<ComparisonRow>` | One per compared product |

Produced **only** by the `compare_products` tool handler; like `Recommendation`, the
orchestration loop just stores and relays it.

### ComparisonRow (value object)

| Field | Type | Notes |
|---|---|---|
| `Candidate` | ProductCandidate | The compared product |
| `ValuesByCriterion` | `Dictionary<string, string?>` | Null value + verified flag (via `ProductCandidate`) when a criterion can't be verified for that product |
| `Rating` | decimal | Deterministic composite rating for this product from `ComparisonEngine`, computed the same way for every row in the set |
| `DeltasVsBest` | `Dictionary<string, string>` | Per-criterion computed difference from the best value present in the set (e.g., `"camera_mp": "-12 vs best"`, `"price": "+1500 UAH vs cheapest"`) — the LLM restates these, it does not compute them |

### ScoringPolicy (domain service, invoked only from the `get_recommendations` tool handler)

Pure function: `(UserRequirement, IEnumerable<ProductCandidate>) → IEnumerable<RecommendedItem>`.
Deterministic budget filtering (hard exclude over-budget candidates — FR-007), required-feature
matching, preference-based scoring, and trade-off flagging. No I/O, no LLM call — fully
unit-testable in isolation (constitution Principle III). Called exclusively by the
`get_recommendations` tool's handler in `ProductAdvisor.Infrastructure`; the conversation
orchestration loop in `ProductAdvisor.Application` never calls it directly.

### ComparisonEngine (domain service, invoked only from the `compare_products` tool handler)

Pure function: `(IEnumerable<ProductCandidate>, List<string> criteria) → Comparison`. Computes
`ValuesByCriterion`, the deterministic `Rating` per row, and `DeltasVsBest` per row. No I/O, no
LLM call — unit-tested with fixed candidate sets so rating/delta output is asserted to be
identical across repeated calls (proving no non-determinism sneaks in). Called exclusively by
the `compare_products` tool's handler; never called directly by the orchestration loop.

**Relationships**: `ConversationSession` *(owns)* `UserRequirement`, `ClarificationQuestion?`,
`Recommendation?`; `Recommendation` *(owns)* `RecommendedItem`s, each holding a `ProductCandidate`
(itself an in-memory join of Catalog + Pricing data, never persisted as a duplicate table).
`ScoringPolicy` and `ComparisonEngine` are domain services with exactly one caller each — their
owning tool handler — never the `ProductAdvisor.Application` conversation loop.
