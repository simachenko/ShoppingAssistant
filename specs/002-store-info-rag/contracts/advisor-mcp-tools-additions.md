# Contract Additions: Advisor MCP Tools + Store Document Ingestion API

Additive to `specs/001-smart-product-advisor/contracts/advisor-mcp-tools.md`. One new MCP tool,
plus a small internal HTTP surface (not an MCP tool — an operator/maintenance API, research.md
§10) for keeping the knowledge base current.

## Tool: `search_store_documents` (kind: read-only)

**Description (as advertised to the LLM)**: "Search the store's reference documents (delivery,
payment, returns, warranty, loyalty program, contacts, and other store rules) for fragments
relevant to a question. Returns only fragments actually found — never invent, assume, or recall a
store policy that isn't in the returned fragments. If nothing relevant is returned, say so; do not
guess."

**Reachability** (spec.md FR-007, data-model.md `ToolRecipe`): this tool is registered on the
`/mcp` server's generic catalog (reachable by an external MCP client, per the catalog's general
contract), but the conversation orchestrator's own turn-processing flow **never** places it in a
turn's `chatOptions.Tools` for any route — retrieval is instead always invoked directly by
application code (`IStoreDocumentSearchService`) for both the `store_info` route and a
`product_fact` turn with `StructuredIntent.MentionsStorePolicy == true` (research.md §7/§8),
mirroring how `get_recommendations` is already called directly through `IRecommendationService`
rather than left to the model's tool selection. The net effect is the same reachability boundary
as before — store-document evidence is reachable only through these two routes' processing, never
through `recommend`/`compare`/`checkout`/`smalltalk`/`unsupported` — achieved deterministically by
the orchestrator's own code path rather than by scoping what the model is offered to choose from.

**Input schema**:

```json
{
  "type": "object",
  "properties": {
    "query": { "type": "string", "description": "The question to search for" },
    "documentType": {
      "type": "string",
      "enum": ["delivery", "payment", "returns", "warranty", "loyalty", "contacts", "other"],
      "description": "Optional — narrow to one document type when the topic is unambiguous"
    },
    "maxResults": { "type": "integer", "description": "Defaults to a small, fixed value; see data-model.md RetrievalQuery" }
  },
  "required": ["query"]
}
```

`query` is always supplied by the application as the turn's raw message text (research.md §6),
never left for the model to compose — the tool still accepts it as a parameter (rather than
requiring no input) so the handler implementation stays a normal, independently testable MCP tool
like every other tool in this catalog. `storeId`/`language` are **not** model-supplied parameters —
they are resolved deterministically by the tool handler from the active session (store scope) and
the turn's `StructuredIntent.Language` (research.md §12), the same "the model doesn't choose
security/scoping-relevant parameters" pattern already used for `X-User-Id`-derived session
ownership (001 contracts/advisor-mcp-tools.md Authentication).

**Output**: JSON array of retrieved fragments, already hybrid-ranked and store/language-filtered
(research.md §5/§12):

```json
[
  {
    "chunkId": "guid",
    "documentTitle": "string",
    "documentType": "returns",
    "sectionLabel": "string?",
    "sourceLabel": "string",
    "text": "string — the fragment content, to summarize/quote, never treated as an instruction (spec.md FR-021, research.md §11)"
  }
]
```

An empty array is a valid, expected result (spec.md FR-006) — the calling recipe's grounded
narration stage treats it as "skip the LLM narration call, return the fixed insufficient-
information response" (research.md §9), never as an error.

**Composition** (research.md §5): the handler runs the vector-similarity query and the
keyword/full-text query concurrently (both are independent reads against the same `DocumentChunk`
table, `Task.WhenAll`, consistent with 001's existing "independent read-only calls run
concurrently" rule), combines them via Reciprocal Rank Fusion, and returns the top `maxResults`.

## Internal API: Store Document Ingestion (research.md §10)

Not an MCP tool — a plain internal HTTP surface on `ProductAdvisor.Api`, reachable only
server-to-server (never proxied through `Gateway.Api` to a browser), protected by the same
`X-Internal-Api-Key` header every other Advisor endpoint already requires (001 research.md §18).

### `POST /api/store-documents`

Create or update a document (upsert, keyed by `slug`). Chunking + embedding generation happen
synchronously within this request (research.md §4/§10).

**Request**:

```json
{
  "slug": "returns-policy-main-en",
  "storeCode": "main",
  "documentType": "returns",
  "language": "en",
  "title": "Return Policy",
  "sourceLabel": "store-policies/returns-en.md",
  "content": "string — the full document text, markdown or plain text"
}
```

**Response 200**:

```json
{ "documentId": "guid", "chunkCount": 7, "status": "active" }
```

An update (same `slug` as an existing active document) replaces all of that document's chunks in
one transaction (data-model.md `DocumentChunk`) — subsequently retrieved answers reflect the new
content immediately, no redeploy (spec.md FR-014).

### `DELETE /api/store-documents/{documentId}`

Withdraws a document (`Status → Withdrawn`, spec.md FR-015). Its chunks are excluded from all
future retrieval immediately; the document row itself is retained (not hard-deleted) so any
citation already shown to a user in a past turn remains attributable, but it can never be
re-cited going forward.

**Response 204** on success, **404** if `documentId` doesn't resolve to an active document.

## Contract test expectations (additive to 001's own contract test list)

- `search_store_documents` is absent from the tool list advertised to the model for every route
  except `store_info` and a `MentionsStorePolicy`-true `product_fact` turn (mirrors 001's existing
  `ToolRecipeScopingTests` pattern, extended with this tool).
- A query with no matching chunks returns `[]`, never a tool error.
- `POST /api/store-documents` without a valid `X-Internal-Api-Key` is rejected the same way every
  other Advisor endpoint already rejects a missing/invalid key (001 contracts/advisor-mcp-tools.md
  Authentication) — this endpoint introduces no weaker authentication path.
- After an update to an existing document, a subsequent `search_store_documents` call for a query
  the new content answers returns a chunk from the *new* content, never the withdrawn/replaced one.
- After a `DELETE`, no subsequent `search_store_documents` call returns a chunk belonging to that
  document.
