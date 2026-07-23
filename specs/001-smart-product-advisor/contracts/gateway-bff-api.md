# Contract: API Gateway / BFF (consumed by the Blazor web app)

Base path: `/api`. This is the single entry point the Blazor UI calls; it never calls Catalog,
Pricing, or Advisor directly. The Gateway generates an `X-Correlation-Id` if the incoming
request doesn't carry one and forwards it on every downstream call (constitution Principle VI).

## POST /api/chat/messages

Composition endpoint over the Advisor conversation API.

**Request**: `{ "sessionId": "guid | null", "text": "string" }` — `sessionId` null starts a new
session (Gateway calls Advisor's `POST /api/conversations` first, then forwards the message).

**Response 200**: the created/resolved `sessionId` plus the same discriminated
clarification/recommendation/comparison body defined in `advisor-conversation-api.md` (the
Gateway passes it through rather than reshaping it, to avoid two sources of truth for the
response contract).

## POST /api/chat/messages/stream

Streaming sibling of `POST /api/chat/messages` (research.md §11): same request shape
(`sessionId`/`text`), but proxies the Advisor's `POST /api/conversations/{sessionId}/messages/stream`
SSE response back to the caller almost verbatim, with one addition — the resolved `sessionId`
is merged into the final `result` event's JSON payload (the same composition the non-streaming
endpoint already does), so a client never has to make a separate call to learn which session it
ended up in.

**Request**: identical to `POST /api/chat/messages`.

**Response 200** (`Content-Type: text/event-stream`): the same `token`/`result` event sequence
defined in `advisor-conversation-api.md`, with `sessionId` added into the `result` event's data.
If `sessionId` was null, the Gateway creates the session first (as it does today), then opens
the stream for the message.

## GET /api/chat/{sessionId}

Pass-through of `GET /api/conversations/{sessionId}` on the Advisor service, for page reload /
history display.

## GET /api/products/{productId}

Composition endpoint for a plain product-detail view (outside the chat flow, e.g., a "view
product" link from a recommendation): calls Catalog's product-detail endpoint and Pricing's
single-offer endpoint concurrently (`Task.WhenAll`, per the Performance Goals in plan.md) and
merges them into one response shaped like a `ProductCandidate` (see data-model.md).

**Errors**: `404` if Catalog has no such product. If Catalog succeeds but Pricing fails/404s,
the response is still `200` with `priceVerified: false` / `availabilityVerified: false` —
partial success, not total failure.

## GET /api/products/search

Composition endpoint for the explicit product-picker UI (FR-020) — **no chat, no LLM
involvement at all**: calls Catalog's `POST /api/catalog/products/search` (category, free-text,
characteristics) to get candidates, then batch-fetches their offers from Pricing and
filters/sorts/limits by price range on that candidate set (the same pushdown-composition pattern
as `GET /api/products/{productId}` above, applied to a list instead of one item — research.md
§13).

**Request** (query string, mirroring the Catalog request body as query parameters): `category`,
`categoryId`, `q`, `characteristics` (repeated `key:operator:value[:valueTo]` entries),
`priceMin`, `priceMax`, `sortBy`, `page`, `pageSize`.

**Response 200**: array of `ProductCandidate`-shaped items (data-model.md) — same merge shape as
`GET /api/products/{productId}`, just for a list. A candidate whose price couldn't be verified
still appears, with `priceVerified: false`, rather than being silently dropped (constitution
Principle V, consistent with the single-product endpoint above).

**Errors**: `400` for an unrecognized characteristic `operator`, mirroring Catalog's own
validation.

## POST /api/products/compare

Composition endpoint over the Advisor's stateless comparison endpoint (FR-018) — passthrough
proxy to `POST /api/comparisons` on `ProductAdvisor.Api`, no reshaping (same principle as the
chat composition endpoints above: one source of truth for the response contract). This is what
the explicit product-picker's "Compare" button calls; it never touches `/api/chat/*` or any
`sessionId`.

**Request**: identical to `POST /api/comparisons` (`advisor-conversation-api.md`).

**Response 200**: identical to `POST /api/comparisons`'s response.

## Contract test expectations

- `X-Correlation-Id` is present on every downstream call the Gateway makes (asserted via a
  test `DelegatingHandler` capturing outgoing requests).
- `GET /api/products/{productId}` returns `200` with unverified price/availability when Pricing
  is simulated as down, not a `5xx`.
- `POST /api/chat/messages` with `sessionId: null` results in exactly one new session being
  created (no duplicate session creation on retry within the same logical request).
- `POST /api/chat/messages/stream` with `sessionId: null` creates exactly one session (same
  guarantee as the non-streaming endpoint) and the created `sessionId` appears in the `result`
  event.
- The Gateway's streamed `result` event, once `sessionId` is stripped back out, is
  byte-identical to what `POST /api/chat/messages` returns for the same underlying turn.
- `GET /api/products/search` with a `characteristics` condition and a `priceMin`/`priceMax` range
  returns only candidates satisfying both (SC-011), with unverified price handled the same
  partial-success way as `GET /api/products/{productId}`.
- `POST /api/products/compare` proxies `POST /api/comparisons` without reshaping — its response
  is byte-identical to calling the Advisor endpoint directly for the same request.
