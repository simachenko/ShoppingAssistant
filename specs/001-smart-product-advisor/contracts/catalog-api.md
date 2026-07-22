# Contract: Product Catalog Service API

Base path (internal, called by Advisor's MCP tools and by the Gateway for detail views):
`/api/catalog`. All responses are JSON. All endpoints are read-only for this feature (catalog
authoring/import is out of scope).

## GET /api/catalog/products

Search products by category, free-text keyword, and optional attribute filters.

**Query parameters**:

| Name | Type | Required | Notes |
|---|---|---|---|
| `category` | string | no | Category name or id; omit to search all categories |
| `q` | string | no | Free-text match against name/description/keywords |
| `maxPrice` | decimal | no | *(Advisor never sends this — Catalog has no price; kept absent intentionally)* |
| `page` / `pageSize` | int | no | Default `page=1`, `pageSize=20`, max `pageSize=100` |

**Response 200**:

```json
{
  "items": [
    {
      "productId": "guid",
      "name": "string",
      "brand": "string",
      "category": "string",
      "specifications": [ { "key": "camera_mp", "value": "50", "unit": "MP" } ]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 42
}
```

**Errors**: `400` invalid pagination values. Empty `items` (not an error) when nothing matches
— the Advisor is responsible for turning that into "no full match" messaging (FR-010), Catalog
itself just reports what exists.

## GET /api/catalog/products/{productId}

**Response 200**: a single item in the same shape as one entry of the search `items` array,
plus `description` and `isActive`.

**Errors**: `404` if `productId` is unknown — the Advisor MUST surface this as "product not
found," never invent a substitute (US3 acceptance scenario 2).

## GET /api/catalog/categories/{categoryId}

**Response 200**:

```json
{ "categoryId": "guid", "name": "string", "comparableAttributeKeys": ["camera_mp", "battery_mah", "price"] }
```

Used by the Advisor to build a `Comparison`'s shared, ordered criteria list (FR-006).

**Errors**: `404` unknown category.

## Contract test expectations

- Round-trip JSON shape for every response above (required fields present, correct types).
- `GET /products` with an unknown `category` returns `200` with an empty `items` array, not
  `404` (searching is not the same as looking up one resource).
- `GET /products/{id}` and `GET /categories/{id}` return `404` (not `200` with a null body) for
  unknown ids, so callers can distinguish "not found" from "found but empty."
