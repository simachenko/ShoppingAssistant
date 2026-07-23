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
the conversation API layer or the LLM. The same `criteria`/`rows` are also reachable outside any
conversation via `POST /api/comparisons` below (FR-018) — both paths call the same computation.

**Errors**: `404` unknown `sessionId`; `400` empty message text; `503` (with a body explaining
the degraded state) if the LLM provider and/or both upstream services are unreachable after
resilience policies are exhausted — the Advisor still MUST respond, per constitution Principle
V, rather than the caller seeing a bare timeout.

## POST /api/conversations/{sessionId}/messages/stream

The streaming sibling of the endpoint above (FR-015/research.md §11) — same request body, same
turn semantics, same underlying tool calls — but the response is `text/event-stream` instead of
one JSON body, so the UI can show the advisor's narration as it's generated.

**Request**: identical to `POST /api/conversations/{sessionId}/messages`.

**Response 200** (`Content-Type: text/event-stream`) — a sequence of SSE events:

```text
event: token
data: {"delta": "I found a "}

event: token
data: {"delta": "smartphone that fits..."}

event: result
data: { ...exactly the same JSON shape POST .../messages returns for this turn... }
```

- `token` events (zero or more, in order): `delta` is the next slice of the LLM's narration
  text only — never a structured fact. Concatenating every `delta` in order reproduces the
  final `message`/`question` text.
- `result` event (exactly one, always last): the complete `ConversationTurnResponse` — same
  contract as the non-streaming endpoint, so a client can ignore `token` events entirely and
  still get the full answer from `result` alone.
- If the stream is interrupted or the provider can't stream, the connection still ends with a
  `result` event carrying the complete response (constitution Principle V) — a client MUST
  treat "connection closed without a `result` event" as a failure and fall back to
  `POST /api/conversations/{sessionId}/messages` for that turn.

**Errors**: same status codes as the non-streaming endpoint for failures that occur before the
stream starts (`404`/`400`); once the stream has started, failures are communicated by ending
the stream without a `result` event rather than an HTTP error status (the headers are already
committed).

## POST /api/comparisons

**Stateless, non-conversational** (FR-018, research.md §14): computes a product comparison
directly from a known product-id set — no `sessionId`, no conversation turn, no LLM
tool-selection step. This is what an explicit product-picker UI calls, and what proves the
comparison computation doesn't depend on the language model (SC-010): calling this with the same
`productIds` that a chat message would resolve to yields byte-identical `criteria`/`rows`.

**Request**:

```json
{ "productIds": ["guid", "guid"], "includeExplanation": true }
```

`includeExplanation` defaults to `true`. `productIds` requires 2–10 entries, same as
`compare_products` (`advisor-mcp-tools.md`).

**Response 200**:

```json
{
  "criteria": ["price", "camera_mp", "battery_mah", "availability"],
  "rows": [
    {
      "productId": "guid",
      "name": "string",
      "values": { "price": "14500 UAH", "camera_mp": "50", "battery_mah": null, "availability": "InStock" },
      "rating": 8.2,
      "deltasVsBest": { "price": "+1500 UAH vs cheapest", "camera_mp": "best in set", "battery_mah": "not verified" }
    }
  ],
  "explanation": "string | null"
}
```

`criteria`/`rows` are produced by the same shared comparison-composition service the
conversational `compare_products` path uses (research.md §14) — never recomputed or reshaped
here. `explanation`, when requested, comes from a **separate**, narrowly-scoped LLM call whose
only input is the `criteria`/`rows` above and whose instructions forbid introducing, altering, or
omitting a value (FR-019); if that call fails, is disabled, or times out, `explanation` is `null`
and `criteria`/`rows` are still returned in full — the comparison itself never depends on the
explanation succeeding.

**Errors**: `400` fewer than 2 or more than 10 `productIds`, or fewer than 2 of the given ids
resolve to a real product (nothing to compare). Unlike the conversational endpoints, this one
never returns `503` for an LLM-provider outage — the deterministic comparison has no LLM
dependency; only `explanation` can come back `null` because of one.

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
- For `POST .../messages/stream`: concatenating every `token` event's `delta` equals the
  `message`/`question` in the final `result` event; the `result` event's structured fields
  (`items`, `criteria`/`rows`, etc.) are byte-identical to what the non-streaming endpoint
  returns for the same stubbed tool output — streaming must not change the facts, only their
  delivery.
- A stream that's forcibly cut before its `result` event is asserted to be detectable as
  incomplete by the client (no silent "it just ended normally" false-positive).
- `POST /api/comparisons` and a conversational message that resolves to the same `productIds`
  (via a scripted/stubbed chat client calling `compare_products`) return byte-identical
  `criteria`/`rows` (SC-010) — asserted in the same test, not just separately.
- `POST /api/comparisons` with `includeExplanation: false` returns `explanation: null` without
  making any LLM call at all (asserted via a chat-client spy that records zero invocations).
- `POST /api/comparisons` with a failing/unavailable chat client and `includeExplanation: true`
  still returns `200` with the full `criteria`/`rows` and `explanation: null` — narration failure
  never fails the comparison (FR-019, constitution Principle V).
- `POST /api/comparisons` with fewer than 2 valid product ids returns `400`, never a `200` with
  a single-row or empty comparison.
