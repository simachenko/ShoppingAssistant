# Phase 0 Research: Store Info RAG

Each section resolves one technical unknown from `plan.md`'s Technical Context, or documents a
design decision needed before Phase 1 (`data-model.md`, `contracts/`). Numbering is local to this
feature (restarts at §1); references to `001`'s research are written as "001 research.md §N".

## §1. Module placement and bounded-context boundary

**Decision**: The store-info capability is a new set of Domain/Application/Infrastructure/API
additions inside the existing `ProductAdvisor` service — no new microservice, no new Docker
image, no new deployable.

**Rationale**: Explicit, unambiguous requirement in spec.md's Input. Beyond that: the capability
needs the same turn-processing cycle, request guardrails, PII screening, session/auth boundary,
and observability surface `ProductAdvisor` already owns (001 spec.md FR-036–FR-137). Standing up
a second service would either duplicate all of that machinery or force a synchronous
service-to-service hop in the middle of a single conversation turn for no architectural benefit —
this is a new *intent* inside one conversation cycle, not a new bounded context with its own
lifecycle independent of a conversation.

**Alternatives considered**: A separate "Store Info" microservice — rejected (explicit
requirement; unjustified complexity per constitution Principle V's "avoid unnecessary ... calls").

## §2. Storage and vector search technology

**Decision**: PostgreSQL with the `pgvector` extension, inside the Advisor's own existing schema
(same Postgres instance/schema-per-service model as 001; no new database). NuGet: `Pgvector` +
`Pgvector.EntityFrameworkCore` (official pgvector-dotnet EF Core integration) for the `vector`
column type, via Npgsql's vector plugin. Verified live against Neon's documentation
(neon.com/docs/extensions/pgvector, fetched during this planning pass): pgvector is available on
every Neon plan including free tier, "no add-on or paid tier required," enabled per-database with
`CREATE EXTENSION IF NOT EXISTS vector;` — so this fits inside the already-adopted free-tier
Neon/Render deployment model (001 plan.md Constraints) without a new cost or infra dependency.

**Index type**: HNSW (cosine distance), not IVFFlat. Neon's own guidance (same source): IVFFlat
"requires data before index creation" while HNSW "can be created without pre-existing data" — the
store knowledge base starts empty and grows incrementally through ingestion (§10), which is a
poor fit for IVFFlat's build-time data dependency and a good fit for HNSW.

**Vector width**: `vector(1536)`, matching the chosen embedding model (§3). Neon supports up to
2000 dimensions for the plain `vector` type, so no `halfvec` fallback is needed at this scale.

**Alternatives considered**: A dedicated vector database (Qdrant/Pinecone/Weaviate/etc.) —
rejected: a second infra dependency with its own credentials, network path, and free-tier
evaluation, when the already-provisioned Postgres instance supports vector search natively;
in-memory-only vector search — rejected: does not survive redeploys/multi-instance scaling and
fails FR-014's "reflected without redeploy, durably" requirement.

## §3. Embedding generation

**Decision**: `Microsoft.Extensions.AI`'s `IEmbeddingGenerator<string, Embedding<float>>`
abstraction — the same provider-swappable pattern 001 already uses for the chat client (`IChatClient`)
— backed by `Microsoft.Extensions.AI.OpenAI`'s `EmbeddingClient.AsIEmbeddingGenerator()` against
`text-embedding-3-small` (1536 dimensions) by default, configured through the same
provider-endpoint/key configuration already used for the chat client.

**Rationale**: 001's plan already commits to "an AI provider with a free API tier, kept replaceable
through Microsoft.Extensions.AI abstractions" (constitution-driven requirement) — that guarantee
should hold for embeddings too, not just chat completions, otherwise swapping the LLM provider
would silently leave embeddings hard-coded to a different one.

**Alternatives considered**: A direct, provider-specific embedding SDK call bypassing
`Microsoft.Extensions.AI` — rejected, breaks provider swappability; a locally-run open-source
embedding model — rejected as unnecessary operational complexity for a system already depending
on a hosted LLM provider for chat.

## §4. Chunking strategy

**Decision**: Paragraph/section-aware chunking: split each document on structural boundaries
(headings, paragraph breaks) first, target ~200–400 tokens per chunk, and only hard-wrap a single
paragraph that itself exceeds the target. Each chunk inherits a `SectionLabel` from its nearest
preceding heading (or `null` if the document has no headings).

**Rationale**: Keeps each chunk semantically coherent (better retrieval precision than an
arbitrary fixed-width split that can cut a policy statement mid-sentence) and gives citations
something more specific than "the whole document" to point at, which spec.md FR-005 explicitly
asks for ("document and section, when the document has identifiable sections").

**Alternatives considered**: Whole-document-as-one-chunk — rejected, poor retrieval precision for
any document longer than a short paragraph, no section-level citation possible; LLM-based semantic
chunking — rejected, an unneeded extra LLM call per ingested document (constitution Principle V:
avoid unnecessary LLM calls) for a demo-scale document set where structural chunking is already
sufficient.

## §5. Hybrid search combination strategy

**Decision**: Reciprocal Rank Fusion (RRF). Run the vector-similarity query (pgvector cosine
distance via the `<=>` operator) and the keyword/full-text query (PostgreSQL `tsvector` /
`plainto_tsquery` via `@@`, ranked with `ts_rank`) as two independently ranked candidate lists —
each already filtered by store/language/[document type] (§12) — then combine with
`score = Σ 1 / (k + rank_i)` across the two lists (`k = 60`, the standard default from the
original RRF paper), and take the top-N by combined score.

**Rationale**: RRF needs no normalization between the two lists' incompatible score scales (cosine
distance vs. `ts_rank`), is simple to implement as two SQL queries plus one small in-memory merge
step, and is a well-established, parameter-light hybrid-search technique — appropriate here, where
tuning a hand-weighted blend isn't worth the added complexity for a demo-scale knowledge base.

**Alternatives considered**: A single hand-weighted sum of the two raw scores — rejected, requires
manually normalizing two incompatible scales, more fragile and harder to reason about than RRF;
vector-only search — rejected, fails the explicit hybrid-search requirement and under-matches
exact policy terminology; full-text-only search — rejected, fails the explicit hybrid-search
requirement and under-matches conceptually-related-but-differently-worded questions.

## §6. Retrieval query source

**Decision**: The user's own raw turn message (after input validation and PII screening, before
narration) is used directly as the retrieval query text for both the vector and the keyword
search legs — no separate LLM call to reformulate or expand the query, for either a pure
store-info turn or a turn that mixes a store-policy sub-question with something else (§7).

**Rationale**: Avoids a third LLM call per store-info-touching turn — the existing two-call budget
(extraction + narration, 001 spec.md FR-071) stays unchanged. Store-policy questions in the spec's
own examples ("How long does delivery take?", "What's your return policy?") are short and
single-topic enough that the raw message is already an effective hybrid-search query.

**Alternatives considered**: LLM-based query rewriting/expansion — deferred as a possible future
quality improvement, not justified for a first release given the added latency/cost it would need
to earn back.

## §7. Intent/Route extension, and handling a message that mixes topics

**Decision**:

- Add a seventh `Intent.StoreInfo` value (wire literal `store_info`, mirroring `product_fact`'s
  snake_case convention) to the closed extraction intent set (`ProductAdvisor.Domain.Intent`), and
  a corresponding `Route.StoreInfo` in `PolicyRouter`. `Intent.StoreInfo` maps unconditionally to
  `Route.StoreInfo` once confidence clears the existing threshold — no essential-field gating,
  unlike `recommend`, since a store-info question does not depend on `UserRequirement` state.
- One new boolean field on `StructuredIntent`: `MentionsStorePolicy` (default `false`), filled by
  the same single extraction call that already produces `Intent`/`ProductReferences`/
  `MissingFields` — no added LLM call. When `true` on a `ProductFact`-routed turn (spec.md US2:
  "Is the Galaxy S24 in stock, and what's your return window?"), the application deterministically
  invokes store-document retrieval (§9) as an additional, independent step — **not** by adding
  `search_store_documents` to that turn's LLM-selectable tool list. This mirrors how `recommend`
  already calls `get_recommendations` directly through `IRecommendationService` rather than
  through `chatOptions.Tools` (`ToolRecipe.GetAllowedToolNames(Route.Recommend)` is empty today,
  001 `ProductAdvisor.Infrastructure/ToolRecipes/ToolRecipe.cs`): retrieval this feature performs
  is never left to the model's own tool-selection judgment, for either the pure `store_info` route
  or the mixed case — it always happens, deterministically, because the application already knows
  it should. `search_store_documents` is still registered on the `/mcp` server's generic tool
  catalog (reachable by an external MCP client per the catalog's general contract), it is simply
  never placed in the *conversation orchestrator's own* `chatOptions.Tools` for any route.
- `TurnResult.type` still follows the turn's *primary* route only (`storeInfo` or `answer`) — this
  feature does not introduce true multi-type/multi-intent turns. The secondary sub-question's
  answer is additional content within that same single typed result (narration text plus, for the
  RAG side, the `Citations` list — §9), not a second `type`.
- This secondary-attachment behavior is scoped to `Route.StoreInfo` and `Route.ProductFact` only
  for this pass — spec.md's own mixed-message example is product-fact-shaped
  ("in stock, and ... return window"), not a `recommend`/`compare`/`checkout` example. Extending
  it to those routes is additional, unrequested scope (see Assumptions in data-model.md) and is
  deferred.

**Rationale**: Reuses the existing, already-hardened extraction/routing machinery unchanged in
shape — additive enum membership and one additive boolean field, not a new routing model. Keeps
`TurnResult`'s "exactly one type per turn" invariant (001 spec.md FR-060–FR-062) intact rather than
introducing genuine multi-type turns, which would be a much larger architectural change than this
feature needs.

**Alternatives considered**: A keyword-based pre-router bypassing LLM extraction entirely —
rejected, duplicates and bypasses the one already-audited place intent classification happens;
full independent multi-intent decomposition (a turn resolves to an ordered list of typed results)
— rejected as unnecessary complexity for v1, deferred.

## §8. How strictly each RAG-touching turn shape is grounded

**Decision**: Two different shapes, two different (but both real) guarantees:

- **Pure `Route.StoreInfo`** (no product sub-question): processed through the strict tool-call →
  `EvidenceEnvelope` → constrained-narration → `OutputValidationStage` pipeline (the same shape
  `recommend` already uses, 001 research.md §27) — the narration LLM call sees *only* the
  Envelope built from retrieved chunks, never a general tool-calling loop. This is the feature's
  primary case (spec.md US1) and gets the strongest guarantee.
- **`Route.ProductFact` with `MentionsStorePolicy == true`** (spec.md US2's mixed case): retrieval
  still happens deterministically up front (§7 — never left to the LLM), but product-reference
  resolution (turning "the Galaxy S24" into an exact product id) still uses the existing legacy
  tool-calling loop (`RunLegacyToolContinuationAsync`), since re-architecting deterministic
  product-reference resolution is unrelated, larger-scoped work this feature does not need to take
  on. The retrieved chunks are supplied to that same call as additional grounded reference
  context, and — critically — citation grounding is still validated, via the same *post-hoc*
  mechanism `ApplyGroundingIfApplicable` already applies to `comparison`/`checkoutLink` today (001
  `ConversationOrchestrator.ApplyGroundingIfApplicable`), extended to also build a citation-token
  envelope for this case. A citation-grounding violation is caught and falls back exactly like the
  pure-`store_info` case; what differs is *when* the check runs (after the single legacy call
  returns, not before a second, narrower call is even made).

**Rationale**: This project's own live smoke test (see 001 tasks.md Phase 14 notes) empirically
confirmed the legacy blended path's LLM call, when it has no grounding check applied to its output
at all, can narrate a fabricated fact as though verified. This feature's non-negotiable
requirement is narrower than "never use the legacy call shape at all" — it is "no store-policy
claim ever reaches the user without a real citation" (spec.md FR-004/FR-005), and
`ApplyGroundingIfApplicable`'s existing post-hoc check already delivers exactly that guarantee for
`comparison`/`checkoutLink` today. Extending that same, already-proven mechanism to cover the
mixed case's citations is a smaller, more honest change than claiming this feature rebuilds
product-fact resolution into a fully deterministic pipeline, which it does not. This decision does
not fix the pre-existing gap for a *pure* `product_fact` turn with no policy topic (no Envelope is
built at all today for that case) — that remains the same separately-tracked, out-of-scope gap 001
already documented; it only ensures this feature's own new guarantee (citations are always real)
holds for every turn that carries one, including the mixed case.

## §9. Citation-based grounding validation (extends `OutputValidationStage`/`EvidenceEnvelope`)

**Decision**:

- `EvidenceEnvelope.CanonicalData` for a store-info-touching turn carries the retrieved
  `DocumentChunk` references (id, parent document title/type, section label, source label) it was
  built from — the *only* facts a store-info narration may draw on, mirroring how a
  `Recommendation`/`Comparison` is the only factual input for those routes today.
- `EvidenceEnvelope.AllowedClaims` gains one citation token per retrieved chunk (e.g.
  `chunk:<DocumentChunkId>`), alongside (not replacing) the existing numeric-claim tokens used
  when a `ProductFact` sub-answer is also present (§7). The narration prompt instructs the model to
  place a citation marker referencing a given chunk's id immediately after each policy claim it
  states, and to state nothing it cannot mark this way.
- `OutputValidationStage` gains a citation-checking branch: it parses citation markers out of the
  narration and rejects to the deterministic fallback (same mechanism as today's numeric-claim
  check, 001 research.md §27) if any marker references a chunk id outside the envelope's allowed
  set. **For a pure `store_info` turn, if retrieval returned zero sufficiently-relevant chunks, the
  narration LLM call is never made at all** — the deterministic "I don't have enough information
  to answer that" fallback (spec.md FR-006) is returned directly, which is both cheaper
  (constitution Principle V: avoid unnecessary LLM calls) and structurally impossible to get wrong.
  For the mixed `product_fact` + `MentionsStorePolicy` case (§8), zero retrieved chunks simply
  means no citation tokens are available that turn — the legacy call still proceeds to answer the
  product-fact part as usual, and the citation-checking branch rejects to a fallback only if the
  narration nonetheless states an uncited policy claim (it never fabricates an empty-citations
  short circuit for the product-fact part it's still meant to answer).
- The client-facing `AdvisorTurnResult` never carries raw citation markers in its narration text;
  the application strips them and instead attaches a structured `Citations` list (document title,
  type, section, source label) — the same "structured facts are rendered by the UI's own markup,
  not parsed out of prose" precedent 001 already established for recommendations (001 plan.md
  §"structured facts are rendered by the UI's own Razor markup, not through Markdown at all").

**Honest scope of this guarantee**: this check deterministically guarantees a store-info answer
never cites a document that was not actually retrieved for that question, and never answers at all
when nothing relevant was retrieved. It does **not** deterministically prove the narrated prose is
a fully faithful paraphrase of the cited chunk's content — no automated check can fully verify
open-ended natural-language faithfulness, which is exactly the same class of residual risk 001's
eval suite already scopes into its non-critical, judgment-reviewed classes rather than claiming a
100% deterministic bar for. This feature's two new eval classes (§14) are split the same way: the
citation-existence guarantee is critical/100%; free-text faithfulness is reviewed, not
deterministically scored.

## §10. Document ingestion mechanism

**Decision**: A new internal-only HTTP surface on `ProductAdvisor.Api`:
`POST /api/store-documents` (create or update, keyed by an idempotent document id/slug) and
`DELETE /api/store-documents/{id}` (withdraw). Protected by the same `X-Internal-Api-Key`
mechanism already required on every Advisor endpoint (001 research.md §18) — no new auth
mechanism — and never routed through `Gateway.Api` to a browser client, so it is reachable only
server-to-server (e.g., a maintenance script or an operator running a signed request directly
against the Advisor container), matching how this project already handles other non-shopper-facing
operational tasks. On create/update, the handler synchronously chunks the document (§4), generates
each chunk's embedding (§3), and upserts the `StoreDocument` + all of its `DocumentChunk` rows in
one transaction (old chunks for that document are replaced wholesale, not diffed).

**Rationale**: Satisfies spec.md FR-014 ("no redeploy required") with the smallest addition — no
new UI, no new service, no message broker (carrying forward 001's own "no message broker
introduced in this version" decision, 001 research.md). Synchronous processing is acceptable at
the "demonstration scale — hundreds of documents at most" this project already scopes itself to
(001 plan.md Scale/Scope); revisit with a background job only if that assumption stops holding.

**Alternatives considered**: A background ingestion job pulling from an external CMS/blob store —
rejected, unnecessary complexity given no content-authoring UI is in scope for v1 (spec.md
Assumptions); a fully synchronous-but-diffed chunk update — rejected as premature optimization for
demo-scale documents that are cheap to fully re-chunk/re-embed on every update.

## §11. Prompt-injection resistance for retrieved document content

**Decision**: Retrieved chunk text is placed in the narration prompt inside the same clearly
delimited "untrusted data" section pattern 001 already established for user input and catalog/tool
output (001 research.md §28) — never concatenated as though it were an instruction. The narration
system prompt is extended (not replaced) with an explicit statement that retrieved store-document
text is reference material to summarize or quote, never a command to follow, mirroring the
existing instruction already given for the other two untrusted-data sources.

**Rationale**: Reuses an already-designed, already-specified mechanism (spec.md FR-021 needs no new
architecture — retrieved chunks are simply the third data source the existing pattern was designed
to generalize to).

## §12. Multi-store, language, and document-type filtering

**Decision**: `DocumentChunk` denormalizes `StoreId`, `Language`, and `DocumentType` from its
parent `StoreDocument`, so the hybrid-search SQL filters directly on indexed chunk columns without
a join per query. An HNSW index covers the embedding column; a plain B-tree index covers
`(StoreId, Language, DocumentType)` for the keyword-search leg and for narrowing both legs before
ranking. The active conversation's store scope defaults to the deployment's single seeded `Store`
row (spec.md Assumptions) — `ConversationSession` does not currently carry a store concept, and
adding real per-session store selection is out of scope for this pass (see data-model.md
Assumptions); the filtering mechanism itself is built multi-store-capable from the start so that
follow-up work is additive, not a redesign.

**Rationale**: One indexed SQL round trip per candidate list, no N+1 joins at query time.
Denormalization is safe here because chunks are always fully rewritten together with their parent
document on every update (§10) — there is no window where a chunk's denormalized store/language/
type can drift out of sync with its parent between updates.

**Alternatives considered**: Fully normalized schema requiring a join on every retrieval query —
rejected, avoidable query complexity/cost with no real benefit given the rewrite-together update
model.

## §13. Observability

**Decision**: No new metric category. A citation-grounding rejection (§9) increments the existing
`TurnMetrics.GroundingFailure` counter (001 research.md §32) — it *is* a grounding failure, the
same category `recommend`'s numeric-claim rejections already use, not a new kind of event. Zero
new fields are added to the closed eleven-field `TurnLogFields` set (001 spec.md FR-133) — a
store-info turn's intent, tool name(s), and validation status already fit the existing fields
unchanged, preserving FR-133's closed-set guarantee. One addition: fragment-count-returned and
zero-result-rate for store-document retrieval are recorded the same way 001 already records
tool-call outcomes generally (no dedicated new instrument required — reuses existing per-tool-call
logging).

**Rationale**: Consistent with how this project already treats observability as a small,
deliberately closed surface (FR-133) — extend by reusing an existing category wherever the new
event is genuinely an instance of it, rather than growing the metric/field set for every new
feature.

## §14. Testing strategy

**Decision**:

- **Domain unit tests** (no I/O): chunking logic (structural splitting, section-label inheritance)
  and RRF combination math, each a pure function.
- **Application-layer tests** (faked I/O, mirroring 001's `ScriptedChatClient`/`TestBudgetGuard`
  pattern): `Route.StoreInfo` recipe selection (including the `MentionsStorePolicy` secondary-tool
  attachment from §7), and `OutputValidationStage`'s new citation-checking branch (scripted
  retrieval results — no real Postgres/pgvector needed for this layer).
- **Infrastructure/integration tests** (Testcontainers, extending 001's existing pattern): a real
  Postgres image with `pgvector` pre-installed, verifying the actual hybrid SQL query, the
  embedding round-trip, and store/language/document-type filtering against real data.
- **Two new agentic eval classes**, appended to 001's existing fifteen-class suite (001 spec.md
  FR-138–FR-141) as **critical/release-blocking**, consistent with how 001 already classifies its
  own grounding- and tool-selection-boundary evals:
  1. *A store-info answer never states a policy fact without a citation, and never cites a
     document that was not actually retrieved.*
  2. *A product price/stock/spec/comparison question is never answered via the store-document
     retrieval path, and a store-policy question is never answered via the product-data tools*
     (the FR-007/FR-008 boundary — the same shape as 001's existing "wrong tool for intent"
     critical eval, applied to this feature's new boundary).

**Rationale**: Mirrors 001's already-established, working test-layering strategy exactly, extending
each existing layer rather than introducing a new testing approach for this feature.
