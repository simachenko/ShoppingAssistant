# Contract Additions: Product Advisor MCP Server Tools — `retrieve_store_info`

Additive to `specs/001-smart-product-advisor/contracts/advisor-mcp-tools.md` — every rule in that
file (authentication, per-intent tool exposure, strict value validation) applies unchanged; this
document adds one new tool and the one new per-route exposure rule it needs. No existing tool's
schema, behavior, or exposure changes.

## Tool: `retrieve_store_info` (kind: compute)

Classified as **compute**, not **read-only** (research.md §2), even though it only reads from
`DocumentChunk` — like `get_recommendations`, its output (a ranked, threshold-filtered,
citation-bearing match set) is the terminal, already-computed result the `store_info` route's
narration summarizes; it is never followed by another tool call in the same recipe, and per
FR-069/FR-070 it must never run concurrently with another compute or stateful call.

**Description (as advertised to the LLM/external MCP clients)**: "Search the store's reference
documentation (delivery, payment, returns, warranty, loyalty program, contacts, and other store
policies) for content relevant to a shopper's question. Returns matched fragments with their
source document, or an empty result when nothing in the knowledge base is relevant enough to
answer confidently. Never used for product price, availability, specifications, or comparisons —
those come only from the product-data tools in this catalog."

**Input schema**:

```json
{
  "type": "object",
  "properties": {
    "query": {
      "type": "string",
      "description": "The shopper's store-policy question, verbatim or lightly normalized"
    },
    "language": {
      "type": "string",
      "description": "BCP-47/ISO language tag the answer should prefer, e.g. 'uk', 'en'"
    }
  },
  "required": ["query", "language"]
}
```

Deliberately **no** `store` parameter (data-model.md `IStoreContext`, research.md §4) — the store
is resolved server-side from deployment configuration and can never be supplied or overridden by
a caller, consistent with FR-020's "never inferred from the question's free text." Deliberately
no `documentType` parameter either — type preference (FR-022) is derived internally
(research.md §5) rather than left to the caller to guess a value from the closed
`DocumentType` set correctly.

**Output schema** (`StoreInfoAnswer`, data-model.md):

```json
{
  "type": "object",
  "properties": {
    "matches": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "chunkId": { "type": "string" },
          "documentId": { "type": "string" },
          "documentTitle": { "type": "string" },
          "documentType": { "type": "string" },
          "language": { "type": "string" },
          "content": { "type": "string" }
        },
        "required": ["chunkId", "documentId", "documentTitle", "documentType", "language", "content"]
      }
    }
  },
  "required": ["matches"]
}
```

An empty `matches` array is the tool's own honest representation of "nothing relevant enough was
found" (FR-009/FR-011) — never a separate error or a `found: false` shape (unlike
`get_product_details`); the caller (narration, via the Evidence Envelope) is responsible for
turning an empty result into the honest user-facing statement, not this tool.

`Score` (data-model.md `StoreInfoMatch.Score`) is intentionally **not** in this schema — it is an
internal ranking/threshold signal, never a claim the LLM could restate as if it were a
fact about the document.

## Per-intent tool exposure addition (extends the existing table)

`store_info` sees only `retrieve_store_info` — **and, per research.md §2/§3, does not actually
reach it through the LLM-facing tool-invocation surface at all**: like `recommend`, this route's
terminal call is invoked directly by `ProductAdvisor.Application` from the already-classified
query, never offered to the model as a free tool choice. `retrieve_store_info` is still hosted as
a true MCP tool (this file) for the benefit of external MCP clients connecting to `/mcp` directly
(the same reason `get_recommendations` is both a direct orchestrator call and a hosted MCP tool),
but internally, `ConversationOrchestrator` never places it in a `ChatOptions.Tools` list for a
model-driven turn. No other route's recipe includes `retrieve_store_info`, and `store_info`'s
recipe includes no product-data tool (`search_products`, `get_category`, `get_product_details`,
`check_price_and_availability`, `get_recommendations`, `compare_products`,
`generate_checkout_link`) — enforced the same way every other exclusion in the existing table is:
`ToolRecipe.GetAllowedToolNames` never returns a set containing tools from a different route
(spec.md FR-004/FR-005).
