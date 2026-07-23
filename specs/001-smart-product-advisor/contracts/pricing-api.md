# Contract: Pricing and Availability Service API

Base path: `/api/pricing`. Read-only for this feature (offer ingestion/import is out of scope).

## GET /api/pricing/offers/{productId}

Current price/availability for a single product.

**Response 200**:

```json
{
  "productId": "guid",
  "price": { "amount": 14999.00, "currency": "UAH" },
  "discount": { "percentOff": 10, "validUntil": "2026-08-01T00:00:00Z" },
  "availability": "InStock",
  "asOf": "2026-07-22T09:00:00Z",
  "source": "string"
}
```

`availability` is one of `InStock`, `LimitedStock`, `OutOfStock`, `Unknown`. `discount` is
`null` when there is none. There is intentionally no `productName` field — Pricing does not
own product identity data; callers correlate by `productId` against Catalog.

**Errors**: `404` if there is no offer on record for that `productId` (distinct from
`Unknown` availability — `404` means "we have no pricing record at all," `Unknown` means "we
have a record but couldn't confirm stock"). The Advisor MUST treat both as "cannot be
verified," but log them differently.

## GET /api/pricing/offers?productIds={id1,id2,...}

Batch lookup for comparisons/recommendation candidate sets, so the Advisor can fetch pricing
for many candidates concurrently in one round trip instead of N sequential calls.

**Query parameters**: `productIds` — comma-separated list, max 50 per call.

**Response 200**:

```json
{
  "offers": [ { "productId": "guid", "price": { "amount": 14999.00, "currency": "UAH" }, "discount": null, "availability": "InStock", "asOf": "2026-07-22T09:00:00Z", "source": "string" } ],
  "notFound": ["guid-with-no-offer"]
}
```

Products with no offer record appear in `notFound`, not as an error for the whole batch —
partial results are always returned (constitution Principle V: honest partial response over
total failure).

**Errors**: `400` if `productIds` is empty or exceeds the max count.

**Price-range filtering (FR-020, research.md §13)**: Pricing does not accept a `maxPrice`/
`minPrice` query parameter — price-range filtering for a parametric product search is applied by
the *caller* (the Advisor's search tool, or the Gateway's picker-facing composition endpoint)
against this batch response, on the already category/characteristics-narrowed candidate id set
Catalog returned. This keeps the pushdown-filter composition pattern in one place (the caller)
rather than teaching Pricing about search semantics it doesn't otherwise need to know.

## Contract test expectations

- Single-offer `404` vs `Unknown`-availability `200` are distinguishable and both tested.
- Batch endpoint returns partial results (`offers` + `notFound`) rather than failing the whole
  request when some ids are missing.
- `asOf` is always present and parseable — this is what lets the Advisor disclose data
  freshness when asked.
