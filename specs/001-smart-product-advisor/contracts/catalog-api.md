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

## GET /api/catalog/categories?name={name}

Resolve a category's identity and comparable characteristics by name (FR-021), so a category
reference is never guessed as an internal id.

**Query parameters**: `name` (string, required) — matched case-insensitively.

**Response 200**: same shape as `GET /api/catalog/categories/{categoryId}`.

**Errors**: `404` if no category matches that name.

## POST /api/catalog/products/search

Parametric product search (FR-020): category, free-text, and structured characteristic filters,
evaluated deterministically — no natural-language interpretation happens in this service. This
is additive to `GET /api/catalog/products` above (kept as-is for the simple category+text case
`get_recommendations` already relies on).

**Request**:

```json
{
  "categoryId": "guid | null",
  "category": "string | null",
  "query": "string | null",
  "characteristics": [
    { "key": "camera_mp", "operator": "gte", "value": "48" }
  ],
  "page": 1,
  "pageSize": 20
}
```

`operator` is one of `eq`, `gte`, `lte`, `between` (the last requires both `value` and `valueTo`).
Supply either `categoryId` or `category` (name) — not required, but characteristics rarely make
sense uncategorized. `characteristics` may be empty or omitted (equivalent to the plain search
above with structured request framing).

**Response 200**: same shape as `GET /api/catalog/products`.

**Note on implementation (research.md §13)**: `characteristics` filters are evaluated in the
Catalog service's application layer, in-process, **after** category/free-text narrowing has
already reduced the candidate set via an indexed SQL predicate — `Product.Specifications` is
stored as a JSON document per product, which doesn't push cleanly into arbitrary per-operator SQL
predicates via EF Core's LINQ provider at this catalog's scale. This is an explicit, documented
scale boundary appropriate to plan.md's Scale/Scope, not an oversight; the recognized way to
remove it at real retail scale is a queryable relational specification table or a dedicated
search index populated via events — out of scope for this feature.

**Errors**: `400` for an unknown `operator`, or a `between` filter missing `valueTo`.

## Contract test expectations

- Round-trip JSON shape for every response above (required fields present, correct types).
- `GET /products` with an unknown `category` returns `200` with an empty `items` array, not
  `404` (searching is not the same as looking up one resource).
- `GET /products/{id}` and `GET /categories/{id}` return `404` (not `200` with a null body) for
  unknown ids, so callers can distinguish "not found" from "found but empty."
- `GET /categories?name=` resolves a known category case-insensitively and `404`s for an unknown
  name.
- `POST /products/search` with a `gte`/`lte`/`between` characteristic filter returns only
  products whose specification satisfies that condition (SC-011) — asserted against a fixed
  seeded dataset, not just "returns something."
- `POST /products/search` with a characteristic `key` that doesn't exist for the category returns
  `200` with an empty `items` array, not an error (a valid, just unsatisfiable, filter).
- `POST /products/search` with an unrecognized `operator`, or a `between` filter missing
  `valueTo`, returns `400`.
