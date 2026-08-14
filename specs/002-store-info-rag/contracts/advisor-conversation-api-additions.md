# Contract Additions: Product Advisor Conversation HTTP API — store-policy answers

Additive to `specs/001-smart-product-advisor/contracts/advisor-conversation-api.md` — every rule
in that file (authentication, transport/data protection, the seven-result-type contract, the
narration-grounding guarantee) applies unchanged. This document adds one optional field to the
existing `POST /api/conversations/{sessionId}/messages` response shape; no existing field,
status code, or endpoint changes.

## `POST /api/conversations/{sessionId}/messages` — `citations` field addition

A store-policy question (`store_info` intent, spec.md 002 FR-002) resolves to the **existing**
`answer` result type — it does **not** introduce an eighth result type (spec.md 002 FR-024). The
response gains one new optional field, present only on `answer`-type responses produced by a
`store_info` turn (`null`/absent on every other route's response, including a `smalltalk`-intent
`answer`, which carries no product or document data at all):

```json
{
  "type": "answer",
  "message": "Delivery within Kyiv typically takes 1–2 business days...",
  "citations": [
    { "documentId": "guid", "documentTitle": "Delivery Terms", "chunkId": "guid" }
  ]
}
```

**`citations`** (`CitationResponse[]`, optional): one entry per distinct source document
`message` actually drew a claim from (spec.md 002 FR-008) — copied verbatim from that turn's
`EvidenceEnvelope.Citations` (data-model.md), never separately re-derived by narration. Absent or
an empty array is only valid when `message` itself is the fixed "could not find this in the
store's reference material" honesty fallback (spec.md 002 FR-009) — an `answer` produced by a
`store_info` turn MUST NOT state a specific policy claim with no corresponding `citations` entry
(spec.md 002 FR-007/FR-008), matching the existing narration-grounding guarantee this file already
documents system-wide (`OutputValidationStage` rejects/strips any claim `AllowedClaims` doesn't
back, which for a `store_info` turn is derived only from cited chunks).

```json
{
  "type": "answer",
  "message": "I couldn't find information about that in our store's reference material.",
  "citations": []
}
```

## `CitationResponse` (new type, `ProductAdvisor.Application.Contracts`)

| Field | Type | Notes |
|---|---|---|
| `DocumentId` | Guid | |
| `DocumentTitle` | string | Human-readable source name, suitable for direct display (e.g., "Delivery Terms") |
| `ChunkId` | Guid | The specific fragment cited — not required for UI display, present for traceability/audit (spec.md 002 FR-026) |

## Gateway/WebApp pass-through

`Gateway.Api`'s chat composition endpoint and `WebApp.Blazor`'s chat rendering copy `citations`
through unchanged (the same "structured facts are rendered by the UI's own Razor markup, not
through Markdown" treatment `specs/001-smart-product-advisor/plan.md`'s Project Structure already
gives `items`/`rows` for recommendations/comparisons) — a citation is a structured fact about
provenance, not narration text, and MUST remain visible to the user even if `message` is
edited/regenerated, consistent with the existing split between narration and structured data this
contract already establishes.
