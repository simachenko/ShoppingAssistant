# Contract: Product Advisor MCP Server Tools

Hosted at `/mcp` (Streamable HTTP transport) by `ProductAdvisor.Api`, via the official
`ModelContextProtocol` C# SDK. **Every product-data operation — including filtering, scoring,
comparison ratings, and cross-product deltas — is exposed as one of these tools, with a
deterministic C# handler.** There is no product-data computation anywhere outside this file's
tools: the Advisor's own conversation orchestration (`ProductAdvisor.Application`) only decides,
via the LLM, *which* tool to call and *when*, and relays each tool's output verbatim for the LLM
to narrate. The LLM may describe a returned rating, delta, or trade-off in more detail, but it
never calculates one — if a number is shown to the user, exactly one of the tools below produced
it.

## Tool: `search_products`

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

## Tool: `get_category`

**Description (as advertised to the LLM)**: "Resolve a product category's identity and its
comparable characteristics, by name or by id. Use this before searching or comparing by a
characteristic you're not sure is spelled/named exactly right in the catalog."

**Input schema**: `{ "name": "string?", "categoryId": "string?" }` — at least one required.

**Output**: `{ found: true, categoryId, name, comparableAttributeKeys[] }` or `{ found: false }` —
mirrors `GET /api/catalog/categories?name=` / `GET /api/catalog/categories/{id}` (FR-021).

## Tool: `get_product_details`

**Input schema**: `{ "productId": "string (guid)" }`, required.

**Output**: single product record (same shape as Catalog's product-detail response) or an
explicit `{ "found": false }` — never a fabricated record — mirroring the `404` case in
`catalog-api.md`.

## Tool: `check_price_and_availability`

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

## Tool: `get_recommendations`

**Description (as advertised to the LLM)**: "Given a fully-specified need (category, budget,
required features, preferences), return a ranked, deterministically scored set of matching
products with pre-computed match reasons and trade-offs — or an explanation of why nothing
matches. Do not attempt to filter, rank, or score candidates yourself; always call this tool
once category and budget are known."

**Input schema**:

```json
{
  "type": "object",
  "properties": {
    "category": { "type": "string" },
    "budget": { "type": "object", "properties": { "amount": { "type": "number" }, "currency": { "type": "string" } }, "required": ["amount", "currency"] },
    "requiredFeatures": { "type": "array", "items": { "type": "string" } },
    "preferences": { "type": "array", "items": { "type": "string" } }
  },
  "required": ["category", "budget"]
}
```

**Output**: `Recommendation` shape from `data-model.md` — `items[]` (each with `candidate`,
`matchedRequirements[]`, `tradeOffs[]`, `score`) or `unmetConstraintExplanation` when `items` is
empty. Internally calls `search_products` + `check_price_and_availability` (or their
Application-layer equivalents) and runs `ScoringPolicy` — none of that is visible to or
performed by the LLM; the LLM receives only the finished, deterministic result.

## Tool: `compare_products`

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
