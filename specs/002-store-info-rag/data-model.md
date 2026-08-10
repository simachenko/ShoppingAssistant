# Data Model: Store Info RAG

This feature adds new entities to the existing **Product Advisor Service** bounded context
(`specs/001-smart-product-advisor/data-model.md` §"Product Advisor Service") and additively
extends several types already defined there. New entities are described in full; extensions to
existing 001 types are described as deltas, not full re-listings — see 001's `data-model.md` for
everything not repeated here.

## New entities

### Store (entity)

The scope a `StoreDocument`/`DocumentChunk` applies to (spec.md Key Entities, research.md §12).

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | |
| `Code` | string | Short stable identifier, e.g. `"main"` — used in filtering/config, not shown to users |
| `Name` | string | Display name |

A deployment has at least one `Store` row (seeded); the filtering mechanism supports more than
one from the start even though only one is populated today (spec.md Assumptions, research.md
§12). No other entity in this system currently references `Store` — `ConversationSession` does
not carry a store scope (see Assumptions below); the active store for a session is presently
always the single seeded row.

### StoreDocument (aggregate root)

One piece of store reference content on one topic (spec.md FR-010–FR-015).

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | |
| `StoreId` | Guid | FK → `Store` |
| `DocumentType` | `DocumentType` enum | `Delivery`, `Payment`, `Returns`, `Warranty`, `Loyalty`, `Contacts`, `Other` |
| `Language` | string | BCP-47-ish tag, e.g. `"en"`, `"uk"` — same language convention `UserRequirement.Language` already uses (001 data-model.md) |
| `Title` | string | |
| `SourceLabel` | string | Human-referenceable label used in citations (e.g. a document name); not necessarily a URL |
| `Status` | `DocumentStatus` enum | `Active`, `Withdrawn` (spec.md FR-015) |
| `UpdatedAtUtc` | DateTimeOffset | |

**Validation rules**: `DocumentType`/`Language`/`Title`/`SourceLabel` required and non-empty.
`Status` starts `Active`; transitions only to `Withdrawn` (one-way — a withdrawn document is
re-created as a new document if it needs to return, not un-withdrawn, keeping citation history
for anything already answered unambiguous).

**Relationships**: has many `DocumentChunk` (owned collection — chunks never outlive their parent;
an update replaces all of a document's chunks in one transaction, research.md §10).

### DocumentChunk (entity, owned by `StoreDocument`)

A retrievable fragment of a `StoreDocument`'s content (spec.md FR-013, research.md §4).

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | |
| `StoreDocumentId` | Guid | FK → `StoreDocument` |
| `Ordinal` | int | Position within the parent document |
| `SectionLabel` | string? | Nearest preceding heading, or `null` |
| `Text` | string | The fragment's content |
| `Embedding` | `vector(1536)` (pgvector) | research.md §2/§3 |
| `SearchVector` | `tsvector` (Postgres, generated from `Text`) | research.md §5 |
| `StoreId` | Guid | Denormalized from parent, for filtering without a join (research.md §12) |
| `Language` | string | Denormalized from parent |
| `DocumentType` | `DocumentType` enum | Denormalized from parent |

**Validation rules**: `Text` non-empty; `Embedding` always populated at write time (no partially
ingested/embedding-pending chunk is ever retrievable — ingestion is one transaction, research.md
§10). Denormalized fields are always written identical to the parent `StoreDocument` at the same
transaction; there is no code path that updates one without the other.

**Indexes**: HNSW (cosine distance) on `Embedding`; GIN on `SearchVector`; B-tree on
`(StoreId, Language, DocumentType)` (research.md §12).

### RetrievalQuery (transient, request-scoped — not persisted)

The input to one hybrid-search retrieval call (research.md §5/§6).

| Field | Type | Notes |
|---|---|---|
| `QueryText` | string | The raw turn message (research.md §6) |
| `StoreId` | Guid | From the active session's store scope |
| `Language` | string? | Preferred language filter (spec.md FR-009/FR-018) |
| `DocumentType` | `DocumentType`? | Optional narrowing (spec.md FR-019) |
| `MaxResults` | int | Bounded, small (spec.md FR-020) |

### Citation (value object, part of a store-info `AdvisorTurnResult`)

| Field | Type | Notes |
|---|---|---|
| `DocumentTitle` | string | |
| `DocumentType` | `DocumentType` enum | |
| `SectionLabel` | string? | |
| `SourceLabel` | string | |

Built deterministically by the application from the `DocumentChunk`(s) an answer's citation
markers actually referenced (research.md §9) — never authored freely by the narration model.

## Extensions to existing 001 entities/types

### `Intent` (extends 001 data-model.md `StructuredIntent`'s intent set)

One new value: `StoreInfo` (wire literal `store_info`) — the closed set grows from six to seven
values (research.md §7). `PolicyRouter`'s `Route` enum gains a matching `StoreInfo` value.

### `StructuredIntent` (extends 001 data-model.md `StructuredIntent`)

One new field: `MentionsStorePolicy: bool` (default `false`) — filled by the same extraction call
that already produces `Intent`/`ProductReferences`/`MissingFields`; no added LLM call
(research.md §7).

### `TurnResult` / `AdvisorTurnResult` (extends 001 data-model.md `TurnResult`)

One new `type`: `storeInfo`.

| `type` | Populated from | Notes |
|---|---|---|
| `storeInfo` | `store_info`-routed turn's retrieval + grounded-narration result | Carries `Message` (narration, citation markers stripped) and `Citations` (structured list, see above) |

Additionally, the existing `answer` type (`product_fact`/`smalltalk`) gains an optional
`Citations` field, populated only when a `product_fact`-routed turn also had
`MentionsStorePolicy == true` (research.md §7) — `null`/empty for every other `answer` turn,
including all `smalltalk` turns and any `product_fact` turn without a store-policy sub-question.

`TurnResult`'s "exactly one `type` per turn" invariant (001 FR-060–FR-062) is unchanged — this
feature does not introduce a second `type` value per turn, only an optional additional field on
two existing/new types (research.md §7).

### `ToolRecipe` (extends 001 data-model.md `ToolRecipe`)

`store_info` retrieval is **not** an LLM-selectable tool call for the orchestrator's own
conversation flow — like `recommend`'s `get_recommendations`, it is invoked directly by
application code (a new `IStoreDocumentSearchService`, research.md §7), so
`ToolRecipe.GetAllowedToolNames(Route.StoreInfo)` is empty, exactly like
`Route.Recommend`'s entry today. `search_store_documents` remains registered on the `/mcp`
server's generic tool catalog for external MCP clients (per the catalog's general contract), but
`Route.ProductFact`'s LLM-selectable tool set is **unchanged** by `MentionsStorePolicy` — the
retrieval step for the mixed case is also invoked directly by application code, never added to
that turn's `chatOptions.Tools` (research.md §7/§8).

| Route | Recipe | Tool kind(s) used |
|---|---|---|
| `store_info` | Application calls `IStoreDocumentSearchService` directly (deterministic, always happens) → strict grounded narration | *(none — empty `ToolRecipe` entry, same as `recommend`)* |
| `product_fact` (extended) | *(unchanged LLM-selectable set)*: `search_products`, `get_product_details`, `check_price_and_availability`. When `MentionsStorePolicy == true`, the application *also* calls `IStoreDocumentSearchService` directly (not via the LLM) before/alongside the existing legacy call, and validates citations post-hoc (research.md §8) | read-only (LLM-selectable, unchanged) + one direct application call |

`IStoreDocumentSearchService` (and therefore any store-document evidence at all) is reachable
**only** from these two routes — never from `recommend`, `compare`, `checkout`, `smalltalk`, or
`unsupported` (spec.md FR-007).

### `EvidenceEnvelope` (extends 001 data-model.md `EvidenceEnvelope`)

For a `store_info`/RAG-touching turn: `CanonicalData` carries the retrieved `DocumentChunk`
references; `AllowedClaims` gains one `chunk:<DocumentChunkId>` citation token per retrieved
chunk, alongside any numeric-claim tokens already present for a concurrently-fetched product fact
(research.md §9). `OutputValidationStage.BuildFallback` gains a `store_info` branch: the
deterministic FR-006 "not enough information" message, used both as the standard grounding-failure
fallback and as the direct (LLM-call-skipped) response when retrieval returns zero sufficiently
relevant chunks.

## Assumptions carried from spec.md, made concrete here

- `ConversationSession` (001 data-model.md) is **not** extended with a store-scope field in this
  pass — every session uses the single seeded `Store` row. Real per-session store selection (for a
  deployment with more than one store) is scoped-for but not built; adding it later is additive to
  this schema (a new nullable/defaulted field), not a redesign.
- No `DocumentAuthoring`/CMS entity exists — ingestion (research.md §10) operates directly on
  `StoreDocument`/`DocumentChunk` through an internal API, matching spec.md's assumption that
  document-authoring tooling is out of scope for this feature.
