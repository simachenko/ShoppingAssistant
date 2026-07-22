# Contract: Product Advisor Conversation HTTP API

Base path: `/api/conversations`, hosted by `ProductAdvisor.Api` alongside the MCP endpoint.
This is what the Gateway/BFF calls on behalf of the Blazor chat UI — it is a separate surface
from `/mcp` (that one is for MCP-standard tool clients; this one drives the actual chat turn).

## POST /api/conversations

Start a new session.

**Response 201**: `{ "sessionId": "guid" }`

## POST /api/conversations/{sessionId}/messages

Send one user message and get the advisor's next turn.

**Request**:

```json
{ "text": "I need a smartphone with a good camera and a budget of up to 15,000 UAH" }
```

**Response 200** — one of three shapes, discriminated by `"type"`. In every shape, the
structured fields (`items`, `criteria`/`rows`, `rating`, `deltasVsBest`, etc.) are copied
verbatim from the corresponding MCP tool result (`get_recommendations` / `compare_products` —
see `advisor-mcp-tools.md`); `message` is the LLM's natural-language narration of that same
data and MUST NOT introduce a number or fact that isn't already present in the structured
fields alongside it. This split lets the UI render trustworthy structured data even if the
narration is edited or regenerated.

**Clarification** (FR-002/FR-003 — essential info missing):

```json
{
  "type": "clarification",
  "question": "What's your budget for this smartphone?"
}
```

**Recommendation** (FR-007–FR-010, populated from the `get_recommendations` tool result):

```json
{
  "type": "recommendation",
  "message": "string — LLM narration of the items below, no new facts",
  "items": [
    {
      "productId": "guid",
      "name": "string",
      "price": { "amount": 14500, "currency": "UAH" },
      "priceVerified": true,
      "availability": "InStock",
      "availabilityVerified": true,
      "matchedRequirements": ["budget ≤ 15000 UAH", "camera ≥ 50MP"],
      "tradeOffs": ["Battery capacity is below average for this category"],
      "score": 0.87
    }
  ],
  "unmetConstraintExplanation": null
}
```

`unmetConstraintExplanation` is non-null and `items` is `[]` when nothing satisfies the hard
constraints (FR-010) — this shape MUST NOT mix a non-empty `items` list with an
`unmetConstraintExplanation`, to keep the "no match" case unambiguous for the UI. `score` is
the deterministic `get_recommendations` output, included so the UI/tests can verify ranking
order independent of the narration text.

**Comparison** (FR-006, US2, populated from the `compare_products` tool result):

```json
{
  "type": "comparison",
  "message": "string — LLM narration of the criteria/rows below, no new facts",
  "criteria": ["price", "camera_mp", "battery_mah", "availability"],
  "rows": [
    {
      "productId": "guid",
      "name": "string",
      "values": { "price": "14500 UAH", "camera_mp": "50", "battery_mah": null, "availability": "InStock" },
      "rating": 8.2,
      "deltasVsBest": { "price": "+1500 UAH vs cheapest", "camera_mp": "best in set", "battery_mah": "not verified" }
    }
  ]
}
```

A `null` value in `values` means that criterion could not be verified for that product
(FR-005) — the UI renders this distinctly from a real "0"/empty value, never omits it.
`rating` and `deltasVsBest` are the deterministic `compare_products` output — never computed by
the conversation API layer or the LLM.

**Errors**: `404` unknown `sessionId`; `400` empty message text; `503` (with a body explaining
the degraded state) if the LLM provider and/or both upstream services are unreachable after
resilience policies are exhausted — the Advisor still MUST respond, per constitution Principle
V, rather than the caller seeing a bare timeout.

## GET /api/conversations/{sessionId}

**Response 200**: full transcript + `currentRequirement` snapshot (category, budget, required
features, preferences) — used by the UI to redisplay state on reload and by tests to assert
that constraints persisted correctly across turns (FR-011).

**Errors**: `404` unknown `sessionId`.

## Contract test expectations

- The three response `type`s are mutually exclusive and each round-trips through the DTO
  contract.
- A message with a previously-answered-but-now-missing field (simulating a constraint change)
  updates `currentRequirement` on `GET /api/conversations/{sessionId}` rather than appending a
  second, conflicting value.
- A simulated Pricing-service outage still yields a `200 recommendation` response with
  `priceVerified: false` / `availabilityVerified: false` items rather than a `5xx` — proving
  the partial-failure behavior required by constitution Principle V and spec edge cases.
- The `items`/`score` (recommendation) and `rows`/`rating`/`deltasVsBest` (comparison) fields
  are asserted to come from the underlying tool response used in the test fixture, independent
  of whatever `message` text a stubbed LLM returns — proving the API layer never recomputes or
  overrides tool output.
