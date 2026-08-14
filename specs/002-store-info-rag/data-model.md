# Phase 1 Data Model: Store Info RAG

All entities below live in `ProductAdvisor.Domain`, are persisted by `AdvisorDbContext` in the
existing `advisor` Postgres schema (research.md §1), and are configured via
`ProductAdvisor.Infrastructure/Configurations/*Configuration.cs` files, matching the pattern
`ConversationSessionConfiguration.cs` already establishes. Nothing here is shared with, or
duplicated from, `ProductCatalog`/`PricingAvailability` — this data model is exclusively store
reference/policy content, never product data.

## StoreDocument (aggregate root)

| Field | Type | Notes |
|---|---|---|
| `DocumentId` | Guid | Identity |
| `StoreId` | string | The deployment-configured store this document belongs to (research.md §4). Part of every query's mandatory filter (FR-020). |
| `Title` | string | e.g., "Delivery Terms", "Return Policy" — human-readable, shown in citations (FR-008) |
| `Language` | string | BCP-47/ISO tag (e.g. `"uk"`, `"en"`), matching `StructuredIntent.Language`'s convention |
| `DocumentType` | `DocumentType` (enum) | See below (FR-015) |
| `Status` | `DocumentStatus` (enum) | `Active` \| `Superseded` (FR-014) |
| `SupersedesDocumentId` | Guid? | The prior version this document replaces, if any — null for a document's first version |
| `CreatedAt` | DateTimeOffset | |
| `SupersededAt` | DateTimeOffset? | Set when `Status` transitions to `Superseded`; null while `Active` |

**Validation rules**: `Title`, `StoreId`, `Language` required, non-empty. `DocumentType` must be
one of the closed enum values (extensible only by adding a new enum member, per FR-015's
"extensible set"). A document must have at least one `Chunk` before it can be `Active` (an
empty document can never be a retrieval source — the same "cannot be searchable while
incomplete" rule 001's `Product.IsActive` already applies to product data, FR-013).
`SupersededAt` is set if-and-only-if `Status == Superseded` (FR-014).

**State transitions**: `Active → Superseded` only (one-directional; there is no `Superseded →
Active` transition — reactivating old content is authored as a new version instead, keeping the
audit trail linear). A `Superseded` document's `Chunk`s are retained (audit trail, FR-014) but
excluded from every retrieval query by the mandatory `WHERE "Status" = 'Active'` predicate
(research.md §6/§8) — this is enforced at the query layer, not by the caller remembering to
filter.

## DocumentChunk (child entity, owned by `StoreDocument`)

| Field | Type | Notes |
|---|---|---|
| `ChunkId` | Guid | Identity |
| `DocumentId` | Guid | FK to parent `StoreDocument` |
| `Order` | int | Position within the document (for citation display / traceability, FR-016) |
| `Content` | string | The chunk's text content — the only material narration is ever allowed to draw a claim from (FR-007) |
| `Embedding` | `Vector` (pgvector, fixed dimension — research.md §7) | Computed from `Content` at ingestion time (FR-017) |
| `ContentTsVector` | `NpgsqlTsVector` (generated column, `to_tsvector('simple', "Content")`) | Backs the keyword leg of hybrid search (research.md §8); not application-writable |

**Derived/denormalized for query convenience** (not separately validated, copied from parent at
write time so a hybrid-search query never needs a join to filter/rank): `StoreId`, `Language`,
`DocumentType`, `Status` — kept in sync only by `StoreDocument`'s own status-transition method
(never independently mutable on the chunk).

**Validation rules**: `Content` required, non-empty, bounded length (a chunking-size ceiling —
exact value a research.md §12 ingestion-tooling concern, not fixed here). `Order` unique within
a `DocumentId`. `Embedding`'s dimension must match the column's fixed dimension (research.md §7);
a mismatched dimension is a write-time failure, never silently truncated/padded.

**Relationships**: `StoreDocument` *(owns)* → `DocumentChunk` (one-to-many); `DocumentChunk` →
`StoreDocument` via `SupersedesDocumentId`'s document, transitively (not a direct FK on the chunk).

## DocumentType (enum)

`Delivery`, `Payment`, `Returns`, `Warranty`, `Loyalty`, `Contacts`, `Other` — the minimum set
FR-015 names. Extensible by adding a member; existing documents/chunks are unaffected by an
addition (never a breaking change to already-stored data).

## DocumentStatus (enum)

`Active`, `Superseded` (FR-014).

## StoreInfoMatch (value object, retrieval-result-only — not persisted)

| Field | Type | Notes |
|---|---|---|
| `ChunkId` | Guid | |
| `DocumentId` | Guid | |
| `DocumentTitle` | string | Denormalized at query time for the citation (FR-008) — avoids a second round trip to build a citation |
| `DocumentType` | `DocumentType` | |
| `Language` | string | |
| `Content` | string | The exact chunk text — this is what `AllowedClaims` (research.md §9) is derived from |
| `Score` | double | The fused (RRF) relevance score (research.md §8) — used for threshold cutoff (FR-011), not exposed to the end user |

## StoreInfoAnswer (value object, `IStoreInfoRetrievalService`'s return type — not persisted)

| Field | Type | Notes |
|---|---|---|
| `Matches` | `IReadOnlyList<StoreInfoMatch>` | Empty when nothing survived the relevance/confidence threshold — the single "not found" representation callers check (FR-009), never a separate boolean flag that could disagree with an empty list |

## Citation (value object — carried through `EvidenceEnvelope` and the wire contract)

| Field | Type | Notes |
|---|---|---|
| `DocumentId` | Guid | |
| `DocumentTitle` | string | |
| `ChunkId` | Guid | |

One `Citation` per distinct `StoreDocument` (via its matched `Chunk`s) that contributed to the
narrated answer — the set FR-008 requires be present on every store-policy answer.

## Extensions to existing 001 types

These are **additive** changes to already-existing records (no existing field is removed,
renamed, or given new validation that would reject previously-valid data) — 001's own contracts
and behavior for `recommend`/`compare`/`checkout`/`product_fact` turns are unaffected.

### `Intent` (enum, `ProductAdvisor.Domain`)

Add `StoreInfo` (wire value `store_info`, `[JsonStringEnumMemberName("store_info")]`) to the
closed set (research.md §3). The extraction prompt's instruction listing valid `intent` values
gains `store_info` alongside the existing six.

### `Route` (enum, `ProductAdvisor.Application.Pipeline`)

Add `StoreInfo` (research.md §3).

### `EvidenceEnvelope` (`ProductAdvisor.Application.Pipeline`)

Add `IReadOnlyList<Citation> Citations { get; init; } = []` — empty for every existing route
(`recommend`/`compare`/`checkoutLink`/`smalltalk`/`unsupported`), populated only by
`EvidenceEnvelopeBuilder.ForStoreInfo`.

### `AdvisorTurnResult` (`ProductAdvisor.Application`)

Add `IReadOnlyList<Citation>? Citations { get; init; }` (null for every existing factory method;
set by a new usage of `ForAnswer` for `store_info` turns — no new `Type` value is introduced,
per spec.md FR-024's "resolves to the existing `answer` result type").

### `ConversationTurnResponse` (`ProductAdvisor.Application.Contracts`, wire contract)

Add `IReadOnlyList<CitationResponse>? Citations` (optional/additive field — see
`contracts/advisor-conversation-api-additions.md`).

## New interfaces

### `IStoreInfoRetrievalService` (`ProductAdvisor.Application.Pipeline`)

```csharp
public interface IStoreInfoRetrievalService
{
    Task<StoreInfoAnswer> RetrieveAsync(
        string query, string language, CancellationToken cancellationToken);
}
```

Mirrors `IRecommendationService` exactly (research.md §2): the `store_info` route's terminal,
orchestrator-invoked call, implemented in `ProductAdvisor.Infrastructure` against
`AdvisorDbContext`. `query` is the user's message text (the same text extraction already
classified); `language` is `StructuredIntent.Language`. Store resolution (research.md §4) happens
inside the implementation via `IStoreContext`, never as a parameter the caller could override.

### `IStoreContext` (`ProductAdvisor.Application`)

```csharp
public interface IStoreContext
{
    string CurrentStoreId { get; }
}
```

A single-method seam isolating "how the current store is resolved" (research.md §4) so a future
multi-store change touches one implementation, not every query site.
