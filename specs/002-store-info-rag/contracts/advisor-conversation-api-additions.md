# Contract Additions: Advisor Conversation API — Store Info

Additive to `specs/001-smart-product-advisor/contracts/advisor-conversation-api.md`'s
`POST /api/conversations/{sessionId}/messages` (and its streaming sibling,
`.../messages/stream`) response contract. Nothing in 001's contract changes shape; this file only
adds one new `type` value and one new optional field on an existing type.

## `storeInfo` — new `TurnResult` type (spec.md FR-001–FR-009, data-model.md `TurnResult`)

```json
{
  "type": "storeInfo",
  "message": "string — LLM narration grounded in the citations below, no claim without one",
  "citations": [
    {
      "documentTitle": "Return Policy",
      "documentType": "returns",
      "sectionLabel": "Opened items",
      "sourceLabel": "store-policies/returns-en.md"
    }
  ]
}
```

- `message` MUST NOT contain a policy claim without a corresponding entry in `citations` — output
  validation enforces this before the response is sent (research.md §9); `message` never contains
  raw citation markers, only prose — citations are always the separate structured `citations`
  array, the same "structured facts aren't parsed out of prose" split 001 already uses for
  `recommendation`/`comparison`.
- `citations` is `[]` **only** when `message` is the fixed "I don't have enough information to
  answer that" response (spec.md FR-006) — every other `storeInfo` response has at least one
  citation entry (spec.md SC-001/SC-003).
- A `storeInfo` response never contains `items`, `criteria`/`rows`, `url`, or `fact` — those remain
  exclusive to `recommendation`/`comparison`/`checkoutLink`/`answer` respectively (spec.md
  FR-007/FR-008, mirroring 001's existing per-`type` field exclusivity contract test pattern).

**Example — insufficient information (FR-006)**:

```json
{
  "type": "storeInfo",
  "message": "I don't have enough information to answer that — I couldn't find anything about a loyalty program in what's currently on file for this store.",
  "citations": []
}
```

## `answer` — extended with an optional `citations` field (spec.md FR-008, US2)

001's existing `answer` shape (`type`, `message`, `fact`) gains one new optional field:

```json
{
  "type": "answer",
  "message": "string — LLM narration of fact below, plus any store-policy answer covered by citations",
  "fact": {
    "productId": "guid",
    "name": "string",
    "attribute": "in_stock",
    "value": "true",
    "verified": true
  },
  "citations": [
    {
      "documentTitle": "Return Policy",
      "documentType": "returns",
      "sectionLabel": "Time window",
      "sourceLabel": "store-policies/returns-en.md"
    }
  ]
}
```

- `citations` is present (non-empty array) **only** when the same message also asked a
  store-policy question alongside a product-fact question (spec.md US2/FR-008, `StructuredIntent.
  MentionsStorePolicy == true`, research.md §7). For every other `answer` turn — including every
  `smalltalk` turn — `citations` is absent/`null`, exactly matching 001's existing behavior for
  this type (no wire-format change for any existing caller that doesn't send a mixed message).
- The same "no policy claim without a citation" rule from the `storeInfo` section above applies to
  the store-policy portion of `message` here; the product-fact portion of `message` keeps using
  001's existing `fact.value`/`fact.verified` grounding, unchanged.

## Contract test expectations (additive to 001's own contract test list)

- A `store_info`-intent message with knowledge-base coverage → `type: "storeInfo"`, `citations`
  non-empty, every citation traceable to a chunk actually retrieved for that request.
- A `store_info`-intent message with no relevant coverage → `type: "storeInfo"`, `citations: []`,
  fixed FR-006 message — and no narration LLM call was made for that turn (verifiable via
  `TurnMetrics`/tool-call count in a test double, research.md §9/§13).
- A `product_fact`-intent message that also asks a store-policy question → `type: "answer"`,
  `fact` populated as usual, `citations` non-empty and grounded.
- A pure `product_fact` or `smalltalk` message (no store-policy sub-question) →
  `citations` absent, byte-for-byte the same response shape 001's existing contract tests already
  assert — this feature must not regress any existing `answer`-type contract test.
- A `recommend`/`compare`/`checkout` turn never contains a `citations` field under any input
  (spec.md FR-007 boundary, research.md §7's explicit v1 scope limit).
