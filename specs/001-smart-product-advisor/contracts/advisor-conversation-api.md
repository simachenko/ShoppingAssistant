# Contract: Product Advisor Conversation HTTP API

Base path: `/api/conversations`, hosted by `ProductAdvisor.Api` alongside the MCP endpoint.
This is what the Gateway/BFF calls on behalf of the Blazor chat UI — it is a separate surface
from `/mcp` (that one is for MCP-standard tool clients; this one drives the actual chat turn).

## Authentication

Every endpoint below requires a valid `X-Internal-Api-Key` header (FR-029, research.md §18) —
this service is never called directly by a browser, only by Gateway. Session-scoped endpoints
(everything except `POST /api/conversations`, which creates a new session) additionally require
an `X-User-Id` header — Gateway's already-validated caller identity (research.md §17), trusted
here only because the internal API key already establishes the caller is Gateway. `POST
/api/conversations` records the creating request's `X-User-Id` as the new session's owner
(FR-031); every other endpoint compares its `X-User-Id` against the session's stored owner and
returns `404` (never `403` — a non-owner must not learn the session id exists at all) on
mismatch.

**Transport and data protection** (FR-121/FR-122, data-model.md `DataProtectionPolicy`): every
call to this API, and every call this service makes onward (to Catalog, Pricing, the LLM
provider), MUST use encryption in transit — this is never optional for a deployment. Conversation
data at rest (the database backing `ConversationSession`) and any backup of it MUST be encrypted;
a backup MUST NOT be a less-protected copy of protected data.

## POST /api/conversations

Start a new session.

**Response 201**: `{ "sessionId": "guid" }`

## POST /api/conversations/{sessionId}/messages

Send one user message and get the advisor's next turn.

**Request**:

```json
{ "text": "I need a smartphone with a good camera and a budget of up to 15,000 UAH" }
```

**Response 200** — one of **seven** mutually exclusive shapes, discriminated by `"type"`
(spec.md FR-060–FR-065, data-model.md `TurnResult`): `answer`, `clarification`,
`recommendation`, `comparison`, `checkoutLink`, `unsupported`, `error`. The `type` is assigned
from policy routing's selected route plus that route's validated tool outcome — never inferred
from or overridden by the LLM's narration (FR-061); the absence of a `recommendation`,
`comparison`, or `checkoutLink` never causes a turn to default to `clarification` (FR-062). In
every shape carrying structured fields (`items`, `criteria`/`rows`, `rating`, `deltasVsBest`,
`fact`, etc.), those fields are copied verbatim from the corresponding MCP tool result
(`get_recommendations` / `compare_products` / a search-detail-price tool — see
`advisor-mcp-tools.md`); `message` is the LLM's natural-language narration of that same data and
MUST NOT introduce a number or fact that isn't already present in the structured fields
alongside it. This split lets the UI render trustworthy structured data even if the narration is
edited or regenerated.

**Narration grounding** (FR-086–FR-092, data-model.md `EvidenceEnvelope`): before this response
is sent, output validation checks every numeric/factual claim in `message`/`question` against
that turn's Evidence Envelope — the same canonical data underlying `items`/`criteria`/`rows`/
`fact`/`url`, never a separate source. A claim not traceable to the Envelope causes `message`/
`question` to be rejected, stripped, or replaced with a deterministic fallback (produced without
an extra LLM call) — this **never** changes `type` or any structured field; a client that ignores
`message`/`question` entirely still gets a complete, trustworthy answer from the structured
fields alone, exactly as if narration had never run.

**Answer** (FR-063, US3, populated from a search/detail/price tool result for a `product_fact`
intent — or, for a `smalltalk` intent with no product data involved, `fact: null`):

```json
{
  "type": "answer",
  "message": "string — LLM narration of the fact below, no new facts",
  "fact": {
    "productId": "guid",
    "name": "string",
    "attribute": "camera_mp",
    "value": "50",
    "verified": true
  }
}
```

`fact.value`/`fact.verified` are copied verbatim from the underlying tool result (FR-004/FR-005)
— the LLM never supplies `value` itself, only narrates it; `verified: false` (with `value` still
shown if partially known, or `null` if not) is how an unverifiable fact is represented (FR-005),
never silently omitted. `fact` is `null` for a `smalltalk` turn — a plain conversational reply
with no product data to attach (FR-060 Assumptions).

**Clarification** (FR-002/FR-003 — essential info missing):

```json
{
  "type": "clarification",
  "question": "What's your budget for this smartphone?"
}
```

**Recommendation** (FR-007–FR-010/FR-080–FR-085, populated from the `get_recommendations` tool
result):

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
  "unmetConstraintExplanation": null,
  "nearestAlternatives": []
}
```

Every entry in `items` has already been confirmed to satisfy **every** hard constraint — stated
budget (as a ceiling), every required feature, an explicit availability requirement if one was
stated, and currency compatibility (FR-080/FR-081/FR-084/FR-085); `score` reflects only soft
`Preferences` and never determines eligibility (FR-083). `unmetConstraintExplanation` is non-null
and `items` is `[]` when nothing satisfies every hard constraint (FR-010); in that case,
`nearestAlternatives` MAY be non-empty:

```json
{
  "type": "recommendation",
  "message": "string — LLM narration explaining no full match, and the alternatives below",
  "items": [],
  "unmetConstraintExplanation": "No smartphone in Smartphones matches every requirement: budget ≤ 15000 UAH, camera ≥ 50MP.",
  "nearestAlternatives": [
    {
      "productId": "guid",
      "name": "string",
      "price": { "amount": 16200, "currency": "UAH" },
      "priceVerified": true,
      "availability": "InStock",
      "availabilityVerified": true,
      "violatedConstraints": ["budget: 16200 UAH exceeds 15000 UAH ceiling"]
    }
  ]
}
```

This shape MUST NOT mix a non-empty `items` list with a set `unmetConstraintExplanation` or a
non-empty `nearestAlternatives` — the "no full match" case (with optional alternatives) and the
"qualifying matches" case are mutually exclusive, to keep the UI's rendering unambiguous
(FR-081/FR-082, data-model.md `Recommendation`'s mutual-exclusivity rule). Every
`nearestAlternatives` entry's `violatedConstraints` is non-empty and names the specific hard
constraint(s) that product failed — the UI MUST render it visibly distinct from a qualifying
`items` entry, never merged into the same list. `score`/`violatedConstraints` are the
deterministic `get_recommendations` output, included so the UI/tests can verify ranking and
disqualification independent of the narration text.

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

**Checkout Link** (FR-025, US4, populated from the `generate_checkout_link` tool result):

```json
{
  "type": "checkoutLink",
  "message": "string — LLM narration confirming what's in the link, no new facts",
  "url": "https://retailer.example/checkout?productIds=<guid>,<guid>",
  "productIds": ["guid", "guid"]
}
```

`url`/`productIds` are copied verbatim from the tool result (SC-015) — the conversation API
layer never edits the link. If the user's referenced products can't be resolved, the turn
returns a **Clarification** shape instead (asking which products are meant), never a
partially-wrong checkout link.

**Unsupported** (FR-064, populated when structured-intent-extraction resolves `intent:
"unsupported"`, FR-048/FR-049 — a recognized but out-of-scope request):

```json
{
  "type": "unsupported",
  "message": "string — explains the request is out of scope for this advisor"
}
```

Never remapped to `clarification` (which implies more information would make it fulfillable) or
to `error` (which implies something failed rather than the request being recognized-but-out-of-
scope).

**Error** (FR-065, populated when tool-result validation fails, FR-043, or when no other type
can be honestly produced for the turn — this shape **supersedes** returning a bare `503` for a
total LLM-provider/upstream-services outage, since the turn still completed and its outcome
needs the same in-conversation-history treatment as any other turn, FR-023):

```json
{
  "type": "error",
  "message": "string — honest explanation of what could not be completed",
  "degraded": true
}
```

`degraded: true` marks a temporary/retryable condition (e.g., a dependency currently
unreachable); `degraded: false` marks a request that cannot be fulfilled at all regardless of
retry. This shape is returned with HTTP `200` — the turn itself completed and produced an
honest, typed outcome; the API layer reserves non-`200` statuses for request-level failures
(below), never for an in-turn outcome that already has a typed representation.

**Errors**: `401` missing/invalid `X-Internal-Api-Key`, or a missing/invalid/expired
`X-User-Id`/Google identity upstream at the Gateway (FR-029/FR-030); `404` unknown `sessionId`
**or** a `sessionId` that exists but belongs to a different user (FR-031 — the response is
identical either way, so a non-owner cannot distinguish "doesn't exist" from "not yours"); `409`
a second message arrives for a session while a prior turn for that session is still processing
(FR-024 — the caller should retry once the in-flight turn completes, not immediately); `400`
empty message text, a message exceeding the configured max length, a message containing
rejected control characters, a message whose PII screening result is `Blocked`
(data-model.md `PiiScreeningResult`, FR-116), or a `requirementPatch`-shaped input exceeding the
configured max count/per-entry length for hard constraints/preferences
(FR-037/FR-104/FR-106/FR-107/FR-116 — each fails input validation before a turn is even started,
so there is no turn to attach a typed result to); `413` a request body exceeding the configured
maximum size (FR-105, rejected before the body is parsed at all); `429` the caller's
authenticated user identifier has exceeded its configured rate limit (FR-109), per-user
concurrency limit (FR-110), or token/cost quota (FR-111) — the response body indicates which
one, and, where applicable, when the limit resets. When PII screening instead produces
`Redacted`, the turn proceeds normally using the redacted text — this is not an error case, and
the extraction call receives only `RedactedText`, never the original raw message (FR-116).
Every guardrail rejection above (`400`/`413`/`429`) is produced without any language-model or
tool invocation (FR-113). Every other in-turn failure — including a total LLM-provider or
upstream-services outage after resilience policies are exhausted, reaching the configured
overall turn timeout, or reaching any other `TurnResourceBudget` limit (FR-071–FR-079,
data-model.md) — is delivered as the **Error** shape above at `200`, not as a `5xx`, per
constitution Principle V.

**Resource budgets and cancellation** (FR-071–FR-079, data-model.md `TurnResourceBudget`): a
turn's language-model calls, tool calls, loop iterations, consecutive tool errors, and total
processing time are each governed by a configured hard limit; reaching any of them ends the turn
in the **Error** shape above rather than continuing past it. If the calling HTTP connection is
closed before the turn completes, the server cancels that turn's in-flight work, persists no
state for it, and releases the `409`-producing in-flight-turn marker (FR-024) so a subsequent
message for the same session is not blocked by a turn that will never finish — the caller does
not receive a response in this case (the connection is already gone), but the session is left in
a clean, retryable state rather than stuck.

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
- If the stream is interrupted, the provider can't stream, or the turn reaches its configured
  overall timeout or any other `TurnResourceBudget` limit (FR-071–FR-079), the connection still
  ends with a `result` event carrying the **Error** shape (constitution Principle V) whenever the
  server can still write to the connection; a client MUST treat "connection closed without a
  `result` event" as a failure and fall back to `POST /api/conversations/{sessionId}/messages`
  for that turn.
- If the client itself disconnects before a `result` event is sent, the server cancels the
  turn's in-flight work and releases the session's in-flight-turn marker (FR-024/FR-077) exactly
  as described for the non-streaming endpoint — a disconnected streaming client is not treated
  differently from a disconnected non-streaming one.

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

## DELETE /api/conversations/{sessionId}

User-initiated deletion of a single conversation session (FR-119, data-model.md
`DataProtectionPolicy`).

**Response 204**: the session and its messages/state are deleted; a subsequent `GET`/`POST` for
the same `sessionId` returns `404` as if it never existed.

**Errors**: `404` unknown `sessionId` **or** a `sessionId` belonging to a different user (FR-031
— same non-owner-indistinguishable posture as every other session-scoped endpoint); `409` a
turn for that session is still processing (mirrors FR-024 — deletion is retried by the caller
once the in-flight turn's own cancellation/completion clears the marker, FR-077, rather than
deleting out from under an in-flight turn).

## DELETE /api/conversations

User-initiated deletion of **all** of the caller's own conversation sessions (FR-119). Scoped
entirely by `X-User-Id` — there is no request body identifying which sessions, since it always
means "all sessions owned by this caller."

**Response 204**: every session owned by the caller is deleted.

**Errors**: none beyond the standard `401` (missing/invalid `X-Internal-Api-Key`) — this
endpoint has no session-ownership check to fail since it does not take a `sessionId`.

## Contract test expectations

- The seven response `type`s (FR-060) are mutually exclusive and each round-trips through the
  DTO contract.
- A `product_fact`-intent message whose search/detail/price tool result validates returns
  `answer`, never `clarification` or `recommendation` (FR-063/SC-038) — asserted with a stubbed
  tool result and a spy on the LLM narration call to confirm the type wasn't influenced by it.
- An `unsupported`-intent message (stubbed extraction) returns `unsupported`, never
  `clarification` or `error` (FR-064/SC-039).
- A stubbed tool-result-validation failure within a turn's recipe returns `error` at `200` (not
  a `5xx`) with `degraded: true`, and the same for a simulated total LLM-provider/upstream-
  services outage — both asserted to be renderable in conversation history the same as any other
  turn's structured result (FR-065/SC-040, FR-023).
- A turn whose route selects `unsupported` or `error` is asserted to never fall through to
  `clarification` by default — the test fixture confirms the `type` is driven by the stubbed
  route/tool outcome, not by the absence of `recommendation`/`comparison`/`checkoutLink`
  (FR-062/SC-037).
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
- A conversational `recommend` turn with a stubbed `get_recommendations` result containing an
  over-budget item, a currency-mismatched item, and a fully-qualifying item is asserted to relay
  `items` containing only the qualifying product — the API layer never re-includes an item the
  tool already excluded (FR-081/SC-056/SC-059).
- A conversational `recommend` turn with a stubbed `get_recommendations` "no match" result
  (`items: []`, `nearestAlternatives` populated) is asserted to relay `nearestAlternatives`
  unchanged, and asserted to never combine it with a non-empty `items` (FR-082, data-model.md
  `Recommendation` mutual-exclusivity rule).
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
- Every session-scoped endpoint returns `401` when called with a missing/incorrect
  `X-Internal-Api-Key`, and `404` when `X-User-Id` doesn't match the session's stored owner
  (FR-029/FR-031) — asserted as the same status/body as a genuinely unknown `sessionId`.
- A second `POST .../messages` for the same `sessionId`, sent while the first is still being
  processed (simulated via a slow/blocked stubbed tool call), returns `409` for the second
  request rather than both being processed (FR-024/SC-014).
- `generate_checkout_link` resolved through a conversational message returns a `checkoutLink`
  turn whose `url`/`productIds` match what the same ids would produce through the MCP tool
  directly (mirrors the `POST /api/comparisons` byte-identical-paths pattern).
- A stubbed extraction result that fails schema validation on both the original attempt and its
  one repair attempt is asserted to make exactly two extraction calls (never a third) before
  falling back to `clarification` (FR-071/SC-047).
- A stubbed recipe configured to exceed the test-configured max tool-call count is asserted to
  stop placing further tool calls and return the `Error` shape at `200` (FR-072/SC-048).
- A stubbed recipe configured to exceed the test-configured max consecutive tool-error count is
  asserted to stop attempting further tool calls and return the `Error` shape at `200`
  (FR-075/SC-050).
- A turn whose processing is held past the test-configured overall turn timeout is asserted to
  return the `Error` shape at `200` (non-streaming) or end the stream with an `Error`-typed
  `result` event (streaming) within a bounded time of the timeout (FR-076/SC-051).
- A test that cancels the underlying HTTP request mid-turn (simulating client disconnect) is
  asserted to observe: no further stubbed tool/LLM calls after cancellation, no persisted state
  for that turn, and a following `POST .../messages` for the same `sessionId` succeeding
  immediately rather than returning `409` (FR-077/SC-052).
- A stubbed narration response containing a price/spec/availability/score/rating/delta/checkout
  URL not present in the stubbed tool result's Evidence Envelope is asserted to produce a
  response whose `message` does not contain that ungrounded value, while `items`/`criteria`/
  `rows`/`fact`/`url` and `type` are asserted to be byte-identical to what the same stubbed tool
  result would produce with a fully-grounded narration (FR-088/FR-089/SC-062/SC-063/SC-065).
- A stubbed narration response that is entirely ungrounded (e.g., describes a different product
  than the one in the tool result) is asserted to be replaced by a deterministic fallback
  `message`, asserted via a spy to have triggered zero additional LLM calls while producing it
  (FR-090/SC-064).
- A stubbed narration response for a fact the Envelope marks unverified, but which the narration
  states as confirmed, is asserted to be treated as an ungrounded claim and rejected/stripped the
  same as a fully fabricated value (FR-088, spec.md edge cases).
- A message exceeding the test-configured max length, and a message containing a rejected control
  character, are each asserted to return `400` with zero stubbed LLM/tool calls made
  (FR-104/FR-107/SC-077/SC-080/SC-085).
- A request body exceeding the test-configured max size is asserted to return `413` before the
  body is parsed, with zero stubbed LLM/tool calls made (FR-105/SC-078/SC-085).
- A `requirementPatch`-shaped input exceeding the test-configured max count/per-entry length for
  `requiredFeatures`/`preferences`/`availabilityRequirements` is asserted to return `400` rather
  than being silently truncated to the limit (FR-106/SC-079).
- A caller exceeding the test-configured per-user rate limit, per-user concurrency limit, or
  token/cost quota is asserted to receive `429` with zero stubbed LLM/tool calls made for the
  rejected request (FR-109/FR-110/FR-111/SC-082/SC-083/SC-085).
- A stubbed extraction result containing a currency code outside the ISO 4217 fixture set, an
  out-of-range budget, an operator outside `search_products`' closed set, or a malformed product
  id is asserted to produce a `clarification` turn rather than proceeding to a tool call with
  that value (FR-108/SC-081, spec.md Assumptions).
- A session whose message history exceeds the test-configured max active conversation context
  size is asserted, via a spy on the assembled prompt, to include only the bounded portion, while
  `GET /api/conversations/{sessionId}` still returns the full, unbounded transcript
  (FR-112/SC-084).
- A message containing a PII fixture (e.g., an email address unrelated to the product request)
  is asserted, via a spy on the extraction call's input, to never appear verbatim in that call —
  either the request is rejected with `400` (`Blocked`) or the extraction call receives only
  `RedactedText` (`Redacted`), never the original raw message (FR-116/SC-087).
- A spy on both the extraction and narration prompts for a stubbed turn is asserted to never
  contain the session's `X-User-Id`/`UserId` value anywhere in the assembled prompt content
  (FR-118/SC-089).
- `DELETE /api/conversations/{sessionId}` is asserted to make a subsequent `GET`/`POST` for the
  same `sessionId` return `404`, and `DELETE /api/conversations` is asserted to do the same for
  every session previously created under that `X-User-Id` (FR-119/SC-090).
- A stubbed session older than the test-configured retention period is asserted to be
  automatically deleted by the retention process without any explicit deletion request, verified
  the same way as an explicit `DELETE` (a subsequent `GET`/`POST` returns `404`)
  (FR-120/SC-091).
