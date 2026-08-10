# Contract: Product Advisor MCP Server Tools

Hosted at `/mcp` (Streamable HTTP transport) by `ProductAdvisor.Api`, via the official
`ModelContextProtocol` C# SDK. **Every product-data operation — including filtering, scoring,
comparison ratings, and cross-product deltas — is exposed as one of these tools, with a
deterministic C# handler.** There is no product-data computation anywhere outside this file's
tools: each turn's fixed, route-specific tool recipe (spec.md FR-066–FR-070, data-model.md
`ToolRecipe`) determines *which* of these tools are called and *in what order* — the language
model is invoked only within the recipe's already-scoped tool set (never a free choice from the
full catalog) and, separately, to narrate an Evidence Envelope built deterministically from
whatever the called tool(s) returned (spec.md FR-086–FR-092, data-model.md `EvidenceEnvelope`) —
never the raw tool response directly, and never with license to add a value the Envelope doesn't
already carry. The LLM may describe a returned rating, delta, or trade-off in more detail, but it
never calculates one, and output validation strips any claim it states that the Envelope doesn't
back — if a number is shown to the user, exactly one of the tools below produced it.

## Authentication

The `/mcp` endpoint requires the same `X-Internal-Api-Key` and `X-User-Id` headers as
`advisor-conversation-api.md` (FR-029/FR-031, research.md §17–§18) — every tool call happens
within an already-authenticated conversation turn; there is no anonymous or cross-user tool
invocation path. There is **no configuration under which `/mcp` serves a request without a valid
`X-Internal-Api-Key`** (FR-124, data-model.md `InternalCredentialPolicy`) — an unset or
misconfigured key on this service MUST refuse every caller, never accept a request as if
unauthenticated access were permitted. A valid `X-Internal-Api-Key` alone establishes only that
the caller is a legitimate internal caller; it does **not**, by itself, grant access to any
specific `ConversationSession`'s data or ownership (FR-131) — an `X-User-Id` presented alongside
it is still subject to the same session-ownership check `advisor-conversation-api.md` already
requires (FR-031), never trusted merely because the internal key was correct.

## Per-intent tool exposure (FR-066–FR-070, data-model.md `ToolRecipe`, research.md §24)

This server advertises all seven tools below to MCP clients generically, but **no single turn's
language-model-facing surface may see more than its route's own fixed recipe** — `product_fact`
sees at most `get_product_details`/`check_price_and_availability`; `recommend` sees only
`get_recommendations`; `compare` sees only its resolution tools plus `compare_products`;
`checkout` sees only its resolution tools plus `generate_checkout_link`; `smalltalk` and
`unsupported` see **none** of these seven tools at all. This is enforced by
`ProductAdvisor.Application` scoping the tool list per turn before any language-model call is
made for that turn — never by advertising the full catalog and relying on the model to
self-restrict. Each tool below is tagged with its **kind** for the concurrency rules in
FR-069/FR-070: **read-only** tools may run concurrently with each other within a recipe's
resolution phase only when mutually independent; **compute** tools are always the single
terminal call of a recipe and must never run concurrently with another compute call or a
stateful call (none exist in this catalog).

## Strict value validation before any tool call (FR-108, spec.md Assumptions)

Every field below that carries a currency, budget/price, characteristic operator, unit, or
product identifier is validated against its known format/range/set (ISO 4217 for currency, a
non-negative number for budget/price, `search_products`' closed operator enum, a known unit set,
and the catalog's identifier format for product ids) **before** the corresponding tool is
called — a value that fails this check never reaches a tool call; the turn routes to
`clarification` instead (spec.md Assumptions). This is stricter than each tool's own JSON Schema
below, which only constrains shape (e.g., "a string"), not value validity (e.g., "a real ISO
4217 code").

## Tool: `search_products` (kind: read-only)

**Description (as advertised to the LLM)**: "Search the retailer's catalog for products in a
category, optionally matching a free-text query, a price range, and structured characteristic
conditions (e.g., camera resolution at least 48 MP). Returns product identity, specifications,
and — when a price range is given — verified price/availability. Do not filter, sort, or rank
the results yourself; every condition you can express here is applied deterministically by this
tool."

**Input schema** (FR-020, research.md §13 — new fields are all optional and additive; existing
callers passing only `category`/`query` are unaffected):

```json
{
  "type": "object",
  "properties": {
    "category": { "type": "string", "description": "Product category name, e.g. 'Smartphones'" },
    "categoryId": { "type": "string", "description": "Category id, if already known (e.g. from get_category)" },
    "query": { "type": "string", "description": "Optional free-text keywords" },
    "characteristics": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "key": { "type": "string", "description": "Specification key, e.g. 'camera_mp'" },
          "operator": { "type": "string", "enum": ["eq", "gte", "lte", "between"] },
          "value": { "type": "string" },
          "valueTo": { "type": "string", "description": "Required only when operator is 'between'" }
        },
        "required": ["key", "operator", "value"]
      }
    },
    "priceMin": { "type": "number" },
    "priceMax": { "type": "number" },
    "sortBy": { "type": "string", "enum": ["price_asc", "price_desc", "name"] },
    "limit": { "type": "integer", "description": "Max results to return, e.g. 10 for 'top 10 phones'" }
  }
}
```

**Output**: JSON array of `{ productId, name, brand, specifications[], price?, priceVerified?,
availability?, availabilityVerified? }` — mirrors `catalog-api.md`'s search response `items`,
extended with price/availability fields (present only when `priceMin`/`priceMax`/`sortBy` was
given, since that's what triggers the Pricing composition step below).

**Composition (research.md §13)**: when `priceMin`/`priceMax`/`sortBy`/`limit` is present, the
tool handler calls Catalog's `POST /api/catalog/products/search` (category + `characteristics`,
narrowed server-side) to get candidate ids, then batch-fetches their offers from Pricing, filters
by price range, sorts, and limits — entirely inside the tool handler, never visible to or
performed by the LLM.

## Tool: `get_category` (kind: read-only)

**Description (as advertised to the LLM)**: "Resolve a product category's identity and its
comparable characteristics, by name or by id. Use this before searching or comparing by a
characteristic you're not sure is spelled/named exactly right in the catalog."

**Input schema**: `{ "name": "string?", "categoryId": "string?" }` — at least one required.

**Output**: `{ found: true, categoryId, name, comparableAttributeKeys[] }` or `{ found: false }` —
mirrors `GET /api/catalog/categories?name=` / `GET /api/catalog/categories/{id}` (FR-021).

## Tool: `get_product_details` (kind: read-only)

**Input schema**: `{ "productId": "string (guid)" }`, required.

**Output**: single product record (same shape as Catalog's product-detail response) or an
explicit `{ "found": false }` — never a fabricated record — mirroring the `404` case in
`catalog-api.md`.

## Tool: `check_price_and_availability` (kind: read-only)

**Input schema**:

```json
{
  "type": "object",
  "properties": {
    "productIds": { "type": "array", "items": { "type": "string" }, "maxItems": 50 }
  },
  "required": ["productIds"]
}
```

**Output**: `{ offers: [...], notFound: [...] }` mirroring `pricing-api.md`'s batch response,
including `asOf` freshness and `Unknown` availability where applicable — the tool MUST NOT
collapse "unknown"/"not found" into a guessed value before returning to the LLM.

## Tool: `get_recommendations` (kind: compute — recommend route's terminal call)

**Description (as advertised to the LLM)**: "Given a fully-specified need (category, budget,
required features, availability requirements, preferences), return a ranked, deterministically
scored set of matching products with pre-computed match reasons and trade-offs — or an
explanation of why nothing matches, optionally with nearest alternatives labeled by which
constraint they violate. Do not attempt to filter, rank, or score candidates yourself; always
call this tool once category and budget are known."

**Input schema**:

```json
{
  "type": "object",
  "properties": {
    "category": { "type": "string" },
    "budget": { "type": "object", "properties": { "amount": { "type": "number" }, "currency": { "type": "string" } }, "required": ["amount", "currency"] },
    "requiredFeatures": { "type": "array", "items": { "type": "string" } },
    "availabilityRequirements": { "type": "array", "items": { "type": "string" }, "description": "Hard constraint only when non-empty (FR-080/FR-085); omit or send empty when the user never stated one — availability then stays informational (FR-012)" },
    "preferences": { "type": "array", "items": { "type": "string" } }
  },
  "required": ["category", "budget"]
}
```

**Output**: `Recommendation` shape from `data-model.md` — `items[]` (each satisfying every hard
constraint: budget, `requiredFeatures`, `availabilityRequirements` when given, and currency
compatibility with `budget.currency`, FR-080/FR-081; with `candidate`, `matchedRequirements[]`,
`tradeOffs[]`, and a `score` driven only by `preferences`, FR-083) — or, when `items` is empty,
`unmetConstraintExplanation` plus an optional `nearestAlternatives[]` (each with `candidate` and
`violatedConstraints[]` naming exactly which hard constraint(s) it failed, FR-082). `items` and
`unmetConstraintExplanation`/`nearestAlternatives` are mutually exclusive — never both populated
for the same call. Internally calls `search_products` + `check_price_and_availability` (or their
Application-layer equivalents) and runs `ScoringPolicy` — none of that is visible to or
performed by the LLM; the LLM receives only the finished, deterministic result.

## Tool: `compare_products` (kind: compute — compare route's terminal call)

**Description (as advertised to the LLM)**: "Given two or more product ids, return their
specifications side-by-side using one shared set of criteria, plus a deterministic rating per
product and computed deltas versus the best value in the set for each criterion. Do not compute
comparisons, ratings, or differences yourself — always call this tool and only elaborate on
its output."

**Input schema**:

```json
{
  "type": "object",
  "properties": {
    "productIds": { "type": "array", "items": { "type": "string" }, "minItems": 2, "maxItems": 10 }
  },
  "required": ["productIds"]
}
```

**Output**: `Comparison` shape from `data-model.md` — `criteria[]` (ordered, identical for every
row) and `rows[]`, each with `candidate`, `valuesByCriterion`, deterministic `rating`, and
`deltasVsBest`. Internally calls `get_product_details` + `check_price_and_availability` for
each id and runs `ComparisonEngine` — again, entirely inside the tool handler.

**Not the only way to reach this computation (FR-018, research.md §14)**: this tool handler and
the stateless `POST /api/comparisons` endpoint (`advisor-conversation-api.md`) both call the same
shared comparison-composition service — comparing the same product-id set through either path
yields byte-identical `rating`/`deltasVsBest` (SC-010). Use this tool when ids need to be
resolved from conversation first (e.g., via `search_products`/`get_category`); call
`POST /api/comparisons` directly when the ids are already known (e.g., an explicit product picker
with no chat involved).

## Tool: `generate_checkout_link` (kind: compute — checkout route's terminal call)

**Description (as advertised to the LLM)**: "Given one or more product ids the user wants to buy
— resolved from their names or from an ordinal/descriptive reference to the most recently shown
results — return a checkout link listing exactly those products. Do not construct the link
yourself; always call this tool, and if you cannot resolve which products the user means, ask
rather than guessing."

**Input schema**:

```json
{
  "type": "object",
  "properties": {
    "productIds": { "type": "array", "items": { "type": "string" }, "minItems": 1 }
  },
  "required": ["productIds"]
}
```

**Output**: `{ "url": "string", "productIds": ["..."] }` (FR-025, `CheckoutLink` in
`data-model.md`) — the LLM relays `url` verbatim; it never edits or reconstructs it. If none of
the supplied ids resolve to a real product, the tool returns a client-error result rather than a
link to a partially-wrong set (mirrors `compare_products`'s "nothing to compare" handling).

**Composition**: resolves each id against Catalog (existence check only — a checkout link does
not need price/availability) and, using configuration for the retailer's checkout base URL,
builds the URL deterministically. The LLM's only role beforehand is resolving *which* ids the
user means — typically already done for it by `ConversationSession.LastSearchResults` (FR-022) —
never deciding what goes into the link once ids are known.

## Tool contract test expectations

- Each tool's declared JSON schema is validated against the MCP tool-list response (schema
  drift between this doc and the running server fails the test).
- `get_product_details` for an unknown id returns `{ "found": false }`, not a `404` transport
  error swallowed into a fabricated success — the LLM must be able to see and relay "not
  found."
- `check_price_and_availability` called with an empty and with an over-limit `productIds` array
  both return a client-error tool result, not a partial silent success.
- `get_recommendations` and `compare_products` are called twice with identical input in the same
  test and asserted to return byte-for-byte identical `score`/`rating`/`deltasVsBest` values —
  proving the computation is deterministic and does not depend on any LLM call happening in
  between.
- `compare_products` with fewer than 2 ids returns a client-error tool result (nothing to
  compare).
- A test invokes each tool through an in-process `McpClient` end-to-end (not just unit-testing
  the C# method) so the transport/schema layer is covered, not just the handler logic.
- A separate `ProductAdvisor.Application.Tests` suite stubs all five tools and asserts the
  orchestration loop never produces a `score`, `rating`, or `delta` value on its own — every
  number in its output can be traced back to a stubbed tool response.
- `get_category` resolves a known category by name and by id, and returns `{ found: false }` for
  an unknown one — never a fabricated category.
- `search_products` called with a `characteristics` condition returns only products satisfying
  that condition (SC-011); called with `priceMin`/`priceMax` returns only products whose verified
  price falls in range, and each returned item's `price`/`availability` matches what
  `check_price_and_availability` would independently report for the same id.
- `compare_products` (tool) and `POST /api/comparisons` (direct endpoint) are called with the
  same product-id set in the same test and asserted to return byte-for-byte identical `rating`/
  `deltasVsBest` values (SC-010) — the two entry points are not two independent implementations.
- `generate_checkout_link` returns a `url` whose query parameters encode exactly the resolved
  product ids (SC-015) — no extra, missing, or incorrect ones; called with an id that doesn't
  resolve to a real product returns a client-error result, never a link built from a guess.
- Every tool call made without a valid `X-Internal-Api-Key`/`X-User-Id` pair on the underlying
  `/mcp` request is rejected before the tool handler runs (FR-029/FR-031).
- For each route (`product_fact`, `recommend`, `compare`, `checkout`), the tool-list surface
  presented to the language model for a stubbed turn is asserted to contain only that route's
  recipe tools (FR-068) — e.g., a `recommend` turn's surface never includes `compare_products` or
  `generate_checkout_link`.
- A stubbed `smalltalk` turn and a stubbed `unsupported` turn are each asserted to make zero
  calls into any of the seven tools above (FR-067/SC-044).
- A `product_fact` turn asking only about a specification is asserted to call
  `get_product_details` and never `check_price_and_availability`; one asking only about price or
  availability is asserted to call `check_price_and_availability` and never
  `get_product_details` (FR-066/SC-041).
- A stubbed `compare`/`checkout` turn whose product references resolve to fewer ids than that
  route requires is asserted to never call `compare_products`/`generate_checkout_link` — the
  turn's result type is `clarification` instead (FR-066, spec.md edge cases).
- A stubbed `compare` turn's resolution-phase `get_product_details`/`check_price_and_availability`
  calls for multiple already-known ids are asserted to be able to execute concurrently, while the
  route's terminal `compare_products` call is asserted to start only after all resolution calls
  for that turn have completed and validated (FR-069/FR-070).
- `get_recommendations` called with a fixture containing an over-budget candidate, a candidate
  missing a required feature, a candidate priced in a different currency than `budget.currency`,
  and (when `availabilityRequirements` is non-empty) an out-of-stock candidate is asserted to
  exclude all four from `items` (FR-080/FR-081/FR-084/SC-056/SC-059) regardless of how well each
  otherwise matches.
- `get_recommendations` called with `availabilityRequirements` omitted/empty and an out-of-stock
  candidate that satisfies every other constraint is asserted to include that candidate in
  `items` (FR-085/SC-060) — availability is informational only in this case.
- `get_recommendations` called with a fixture where every candidate satisfies every hard
  constraint but only some match a stated preference is asserted to include all of them in
  `items`, ranked with preference-matching candidates first — none excluded for lacking a
  preference (FR-083/SC-058).
- `get_recommendations` called with a fixture where no candidate satisfies every hard constraint
  is asserted to return `items: []`, a non-null `unmetConstraintExplanation`, and (when the
  handler chooses to populate it) `nearestAlternatives[]` where every entry's
  `violatedConstraints[]` is non-empty and names an actual violated constraint from that fixture
  (FR-082/SC-057) — never returned together with a non-empty `items`.
- A test that unsets the service's expected `InternalApiKey` configuration entirely (not merely
  supplies a wrong caller-side value) is asserted to still reject every request to `/mcp` — never
  falling back to "accept anything" (FR-124/FR-127/SC-095/SC-097).
- A test that authenticates with the well-known local-development `InternalApiKey` value against
  a service configured with production settings is asserted to be rejected — the development
  value MUST NOT validate in a production configuration (FR-127).
- A benchmark/timing test comparing credential-validation duration for a completely-wrong value
  versus a value matching every character except the last is asserted to show no statistically
  significant difference, confirming constant-time comparison (FR-128/SC-098).
- A tool call made with a valid `X-Internal-Api-Key` and an arbitrary, non-owning `X-User-Id` for
  a given `sessionId` reference is asserted to still be rejected by the same ownership check
  `advisor-conversation-api.md` enforces (FR-031) — a valid internal key never substitutes for
  that check (FR-131/SC-100).
