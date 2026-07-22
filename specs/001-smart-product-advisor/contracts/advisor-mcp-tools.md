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
category, optionally matching a free-text query. Returns product identity and specifications
only — no price, stock, rating, or comparison."

**Input schema**:

```json
{
  "type": "object",
  "properties": {
    "category": { "type": "string", "description": "Product category, e.g. 'smartphones'" },
    "query": { "type": "string", "description": "Optional free-text keywords" }
  },
  "required": ["category"]
}
```

**Output**: JSON array of `{ productId, name, brand, specifications[] }` — mirrors
`catalog-api.md`'s search response `items`.

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
