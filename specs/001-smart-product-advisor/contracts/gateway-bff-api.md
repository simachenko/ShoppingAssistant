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

## Contract test expectations

- `X-Correlation-Id` is present on every downstream call the Gateway makes (asserted via a
  test `DelegatingHandler` capturing outgoing requests).
- `GET /api/products/{productId}` returns `200` with unverified price/availability when Pricing
  is simulated as down, not a `5xx`.
- `POST /api/chat/messages` with `sessionId: null` results in exactly one new session being
  created (no duplicate session creation on retry within the same logical request).
