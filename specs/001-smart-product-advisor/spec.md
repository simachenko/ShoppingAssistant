# Feature Specification: Smart Product Advisor

**Feature Branch**: `001-smart-product-advisor`

**Created**: 2026-07-22

**Status**: Draft

**Input**: User description: "Build a smart product advisor for a retail website that helps users choose the most suitable product based on their needs, preferences, and budget. The advisor should answer questions about product characteristics, compare several products using consistent criteria, check current prices and availability, and provide clear, reasoned recommendations. Users should be able to describe their needs in natural language, for example: \"I need a smartphone with a good camera and a budget of up to 15,000 UAH.\" When important information is missing, the advisor should ask focused clarification questions before recommending products. Recommendations should explain why each suggested product matches the user's requirements, highlight important advantages and trade-offs, and respect explicit constraints such as budget, required features, and availability. The advisor must rely on available product data, clearly communicate when information cannot be verified, and never invent specifications, prices, or stock status. The goal is to reduce choice overload, make product comparison easier, and help users make confident and informed purchase decisions."

## Clarifications

### Session 2026-08-02

- Q: Does conversation history need special PII handling (redaction, encryption, limited retention) beyond ordinary data, or is it treated as ordinary application data? → A: No special PII handling required — conversation text (product needs, budgets) is treated as ordinary application data, not sensitive personal data.
- Q: What should happen if a second message arrives for the same session while a prior turn is still being processed? → A: Reject/ignore the second message — one turn completes before the next begins for a given session.
- Q: Is checkout/purchasing in scope for this feature? → A: Purchase processing itself is out of scope, but the advisor MUST be able to generate a checkout link for one or more products the user picked or that were most recently shown (reusing the session's retained result set, FR-022), with those products' identifiers encoded as URL query parameters, so the user can proceed to buy them outside the advisor.
- Q: What is the accessibility bar for the UI? → A: Baseline only — keyboard-navigable, semantic HTML, readable focus order; no formal WCAG conformance level required.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Get a Recommendation from a Natural-Language Need (Priority: P1)

A shopper describes what they need in plain language — for example, "I need a smartphone with a good camera and a budget of up to 15,000 UAH" — and the advisor returns one or more suitable products with clear reasoning, or, if essential details are missing, asks a single focused question before recommending anything.

**Why this priority**: This is the core value of the feature — turning an open-ended need into a confident, reasoned recommendation. Without this flow, there is no product.

**Independent Test**: Can be fully tested by submitting a request that includes category, budget, and at least one feature preference and confirming a recommendation with reasoning is returned; and by submitting a request missing an essential detail (e.g., no budget) and confirming the advisor asks one focused clarifying question instead of guessing.

**Acceptance Scenarios**:

1. **Given** a user states "I need a smartphone with a good camera and a budget of up to 15,000 UAH", **When** the advisor processes the request, **Then** it returns one or more recommended smartphones, each with an explanation of why it fits (e.g., camera quality, price within budget) and any notable trade-offs.
2. **Given** a user states "I need a good laptop" without stating a budget, **When** the advisor processes the request, **Then** it asks a single focused clarifying question about the missing essential detail (e.g., budget) before recommending anything.
3. **Given** a user's stated budget and required features, **When** no product in the catalog satisfies all of the constraints, **Then** the advisor clearly states that no full match exists and explains what is blocking a match, rather than presenting an out-of-budget or non-matching product as if it fit.

---

### User Story 2 - Compare Multiple Products Using Consistent Criteria (Priority: P2)

A shopper asks to compare two or more specific products (or is offered multiple candidates from a recommendation), and the advisor presents them side-by-side using the same criteria — such as price, key specifications, and availability — so the shopper can weigh trade-offs directly.

**Why this priority**: Comparison directly reduces choice overload and is the second most-used capability, but it builds on product data and reasoning already established by User Story 1.

**Independent Test**: Can be tested independently by requesting a comparison of two or three named products and confirming the response uses the identical set of criteria, in the same order, for every product, with values sourced from product data.

**Acceptance Scenarios**:

1. **Given** a user names two specific products, **When** the advisor compares them, **Then** the response lists both products against the same criteria (e.g., price, camera, battery, availability) in the same order.
2. **Given** one of the compared products has a characteristic that cannot be verified from product data, **When** the advisor compares them, **Then** it explicitly marks that value as unavailable/unverified rather than omitting it silently or guessing.
3. **Given** a known set of two or more product identifiers (e.g., selected explicitly rather than named in a chat message), **When** a comparison is requested directly for that set, **Then** the resulting ratings, deltas, and criteria are identical to what a conversational request for the same products would produce — the comparison computation does not depend on the language model deciding to run it.

---

### User Story 3 - Check Price, Availability, and Specific Characteristics (Priority: P3)

A shopper asks a targeted question about a specific product's characteristics, current price, or stock availability without necessarily wanting a full recommendation or comparison.

**Why this priority**: Quick, trustworthy fact lookups build user confidence and are useful on their own, but are lower priority than the core recommendation and comparison flows.

**Independent Test**: Can be tested by asking about a named product's price, availability, or a specific characteristic and confirming the answer matches product data, or clearly states that it cannot be verified.

**Acceptance Scenarios**:

1. **Given** a user asks "Is [Product X] in stock and what does it cost?", **When** the advisor answers, **Then** it states the current price and availability sourced from product data, or clearly states this cannot be verified if the data source doesn't have it.
2. **Given** a user asks about a product that does not exist in the catalog, **When** the advisor responds, **Then** it clearly states the product could not be found rather than inventing details about it.

---

### User Story 4 - Get a Checkout Link for Selected Products (Priority: P4)

A shopper who has been shown recommendations, a comparison, or search results — or who has explicitly picked one or more products — asks to proceed toward buying them, and the advisor returns a checkout link that identifies exactly those products.

**Why this priority**: This closes the loop from "confident decision" to "action," but depends entirely on the recommendation/comparison/search flows already having identified real products — it adds no value on its own and is the least-used capability of the four.

**Independent Test**: Can be tested by, after a recommendation/comparison/search response, asking to proceed to checkout for one or more of the shown products (by name or by ordinal/descriptive reference, e.g., "the first one") and confirming the returned link's product identifiers exactly match the ones referenced.

**Acceptance Scenarios**:

1. **Given** the session has a most-recently-shown set of products (from a search, recommendation, or comparison), **When** the user asks to check out with one or more of them (by name or by ordinal/descriptive reference), **Then** the advisor returns a checkout link whose query parameters identify exactly those products' identifiers.
2. **Given** the user asks to check out with a product that was never shown or picked in this session, **When** the advisor processes the request, **Then** it asks the user to first identify or search for the product rather than guessing which product is meant.

---

### User Story 5 - Sign In Securely Before Using the Advisor (Foundational — gates all other stories)

A visitor arrives at the site and must sign in with their Google account before they can chat, search, compare, or check out; once signed in, they only ever see their own conversation sessions, never another user's.

**Why this priority**: This is a trust-boundary prerequisite, not a feature slice on its own — every other user story (recommend, compare, look up a fact, check out) now runs *inside* an authenticated identity, so this must exist before those stories can be exercised end-to-end in their final form. It is called out separately because it is testable independently of what happens after sign-in.

**Independent Test**: Can be tested by attempting to reach any advisor page or API without a valid signed-in identity and confirming access is refused; and by, as two different signed-in users, confirming neither can read or continue the other's conversation session by id.

**Acceptance Scenarios**:

1. **Given** a visitor who is not signed in, **When** they try to use the advisor (chat, search, comparison, or product detail), **Then** they are directed to sign in with Google before any product data or conversation state is returned.
2. **Given** a user has successfully signed in with Google, **When** they use the advisor, **Then** every conversation session they create is tied to their identity.
3. **Given** two different signed-in users, **When** one attempts to access a conversation session id that belongs to the other, **Then** access is refused rather than returning the other user's conversation.
4. **Given** a signed-in user's Google session/token has expired, **When** they next interact with the advisor, **Then** they are asked to sign in again rather than being served a request under a stale or invalid identity.

---

### Edge Cases

- What happens when the user's stated budget is below the price of the cheapest relevant product? The advisor MUST communicate that no match exists rather than recommending an over-budget item.
- What happens when a required or requested characteristic isn't present in the available product data? The advisor MUST state that it cannot verify that characteristic rather than guessing.
- How does the advisor handle conflicting priorities (e.g., "cheapest" and "best camera" at once)? The advisor MUST surface the trade-off explicitly and may ask the user which priority matters more.
- What happens if product data (prices, availability, specifications) is temporarily unavailable? The advisor MUST inform the user that it cannot complete the request right now rather than fabricating an answer.
- What happens when the user changes a previously stated constraint mid-conversation (e.g., raises the budget)? The advisor MUST apply the updated constraint going forward and treat earlier recommendations as superseded.
- What happens when the user asks about a product category the retailer does not carry at all? The advisor MUST state that it isn't available rather than comparing or recommending unrelated items.
- What happens if the connection to the advisor is interrupted partway through a response, or the underlying language model doesn't support progressive delivery? The user MUST still end up with the complete response (falling back to delivering it all at once) rather than being left with a truncated, stuck, or silently failed answer.
- What happens when a follow-up reference to previously shown products ("the first two", "the cheaper one") is made with no prior search, recommendation, or comparison in the session? The advisor MUST ask which products are meant rather than guessing an identifier.
- What happens when a characteristic filter names an attribute that isn't defined for the category being searched? The system MUST treat it as zero matches for that condition rather than silently ignoring the filter.
- What happens when a price range filter would exclude every candidate that otherwise matches the category and characteristics? The system MUST still apply the price filter and report the honest "no match" outcome rather than relaxing it to "be helpful."
- What happens to a previously shown recommendation card list or comparison table in the conversation view when the user sends a further message (even an unrelated one)? The prior structured rendering MUST remain visible in its place in the conversation rather than disappearing once a new turn's result arrives.
- What happens when a second message for the same session arrives while a prior turn for that session is still being processed? The system MUST reject or ignore the second message rather than processing both concurrently — one turn completes before the next begins for a given session.
- What happens when the user asks to check out with no prior search, recommendation, or comparison in the session (or references a product never shown)? The advisor MUST ask which products are meant rather than guessing an identifier — the same honesty pattern as an ordinal follow-up with nothing to resolve against.
- What happens when a request to any user-facing endpoint arrives without a valid, current Google identity? The system MUST refuse the request (redirect to sign-in for a browser page load, or an authentication-failure response for an API call) rather than serving it anonymously or under a guessed identity.
- What happens when a request between two internal services arrives without the correct internal credential? The receiving service MUST refuse the request rather than processing it — internal endpoints are never reachable on the trust of network location alone.
- What happens when a signed-in user requests a conversation session id that belongs to a different user? The system MUST refuse access rather than returning that session's content, regardless of whether the id itself is guessable or was leaked.
- What happens when the observability/monitoring backend is temporarily unreachable? The system MUST continue serving requests normally — logging, tracing, and metrics export are never allowed to block or fail a user-facing request.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The advisor MUST accept product needs expressed in natural language, including budget, product category, and desired features or preferences.
- **FR-002**: The advisor MUST identify when essential information for a recommendation (at minimum: product category and budget) is missing and MUST ask a single focused clarifying question before offering a recommendation.
- **FR-003**: The advisor MUST limit clarification to the single most critical missing detail at a time rather than asking multiple questions at once.
- **FR-004**: The advisor MUST base all product facts, prices, and availability shown to the user on approved product data; it MUST NOT invent or estimate any specification, price, or stock status that is not present in that data.
- **FR-005**: When a requested fact (specification, price, or availability) cannot be found or verified in the product data, the advisor MUST clearly tell the user it could not be verified rather than presenting a guess as fact.
- **FR-006**: The advisor MUST be able to compare two or more products side-by-side using an identical set of comparison criteria, in the same order, for every product in the comparison.
- **FR-007**: The advisor MUST recommend only products that satisfy the user's explicit hard constraints (e.g., a stated budget ceiling); it MUST NOT present a disqualified product (such as one over budget) as a recommended match.
- **FR-008**: For each recommended product, the advisor MUST explain the reasoning behind the recommendation, referencing which of the user's stated requirements it satisfies.
- **FR-009**: For each recommended product, the advisor MUST surface at least one relevant trade-off, limitation, or disadvantage in addition to its advantages.
- **FR-010**: When no product satisfies all of the user's stated constraints, the advisor MUST clearly communicate that no full match exists and MUST explain what is blocking a match, rather than silently relaxing the constraints.
- **FR-011**: The advisor MUST preserve the user's stated language, currency, units, budget, and required features across a conversation until the user explicitly changes them.
- **FR-012**: The advisor MUST reflect current availability/stock status for any product it recommends or includes in a comparison.
- **FR-013**: Users MUST be able to ask follow-up questions about a specific recommended or compared product's characteristics, price, or availability.
- **FR-014**: The advisor MUST inform the user when product data needed to answer a request is temporarily unavailable, rather than responding as though the request succeeded.
- **FR-015**: The advisor's response MUST be delivered to the user progressively as it is generated, rather than only after the entire response is complete, so the user sees the answer forming instead of facing a blank wait on longer responses.
- **FR-016**: The advisor's explanatory text MUST use structured formatting (e.g., headings, emphasis, bullet lists) rather than a single dense paragraph, so key facts are easy to scan.
- **FR-017**: Recommendation and comparison data (specifications, matched requirements, trade-offs, comparison criteria) MUST be presented as structured lists/tables rather than only as prose, so the user can visually distinguish and compare product characteristics at a glance.
- **FR-018**: The system MUST expose a way to compute a product comparison — including rankings, ratings, and per-criterion deltas — directly from a known set of product identifiers, independent of whether a conversational exchange with the language model triggers it; the computation MUST be deterministic and MUST NOT differ depending on which path invoked it.
- **FR-019**: Whenever a comparison, recommendation, or search result includes a narrative explanation, that explanation MUST be produced by the language model strictly as commentary on already-computed values; it MUST NOT be able to alter, invent, or omit any value in the accompanying structured data, and its absence (e.g., if the language model is unavailable) MUST NOT prevent the structured data itself from being returned.
- **FR-020**: The system MUST support searching for products using explicit, structured filters — category, price range, and one or more product-characteristic conditions (e.g., a minimum camera resolution) — with results limited to products that satisfy every stated filter.
- **FR-021**: The system MUST allow resolving a product category's identity and its comparable characteristics by name, so a category reference can be grounded to a concrete identifier without the language model guessing one.
- **FR-022**: The advisor MUST retain the most recent set of product identifiers shown to the user (from a search, recommendation, or comparison) within the conversation session, so a follow-up reference to that set (e.g., "the first two", "the cheaper one") resolves against known identifiers rather than requiring the language model to reconstruct them from prior prose.
- **FR-023**: The conversation view MUST retain each turn's structured rendering (recommendation cards, comparison table, or clarification prompt) in place within the conversation history as further turns occur; a later turn's result MUST NOT cause an earlier turn's structured rendering to disappear from view.
- **FR-024**: The system MUST reject or ignore a second message submitted for a session while a prior turn for that same session is still being processed, so that two turns for one session never process concurrently.
- **FR-025**: The system MUST be able to generate a checkout link for one or more products the user has picked or that were most recently shown in the session (reusing the retained result set from FR-022), encoding those products' identifiers as URL query parameters, so the user can proceed to purchase them outside the advisor. Purchase processing, payment, cart management, and order fulfillment themselves are out of scope for this system.
- **FR-026**: The conversation and product-picker UI MUST be keyboard-navigable with a readable focus order, using semantic HTML elements for interactive controls (inputs, buttons, links) rather than non-semantic elements requiring custom keyboard handling; no formal WCAG conformance level is required.
- **FR-027**: Every service MUST log its own startup and shutdown, every request it handles (with enough context to trace that request across the services it causes to be called), and every error it encounters, using a common, industry-standard logging/tracing mechanism shared across all services rather than a bespoke per-service format.
- **FR-028**: Every service MUST expose a way to check whether it is running and accepting traffic (a health check), and MUST report performance and resource-usage indicators (e.g., request latency, error rate, memory/CPU usage) through a common, industry-standard monitoring mechanism.
- **FR-029**: Every call between internal services (Gateway, Advisor, Catalog, Pricing) MUST be authenticated with a shared internal credential; a service MUST refuse a request from another internal service that does not present a valid credential.
- **FR-030**: Every user-facing entry point MUST require the user to be signed in with a Google account before any product data, conversation state, or advisor response is returned; a request without a valid, current signed-in identity MUST be refused rather than served anonymously.
- **FR-031**: The system MUST bind each conversation session to the identity of the user who created it, and MUST refuse any attempt — by a different signed-in user — to read or continue a session that is not theirs.
- **FR-032**: Logging, tracing, and metrics collection MUST NOT block or fail a user-facing request if the observability backend they report to is unavailable — observability is always best-effort relative to the request it describes.

### Key Entities *(include if feature involves data)*

- **Product**: A catalog item the advisor can recommend, compare, or answer questions about — category, name/model, specifications, price, currency, and current availability/stock status.
- **User Need**: The parsed representation of what the shopper is looking for — product category, budget (amount and currency), required or preferred features, and any other explicit constraints.
- **Recommendation**: One or more suggested products tied to a User Need, each carrying the matched requirements, disclosed trade-offs, and any verification notes.
- **Comparison**: A set of two or more products evaluated against one shared list of criteria, with each product's value recorded for every criterion. Reachable both through conversation and through direct invocation with a known product set; both paths produce identical results because both call the same deterministic computation.
- **Clarification Question**: A single focused question raised when essential information is missing, tied to the specific missing piece of the User Need.
- **Search Filter**: A structured description of what a product search must satisfy — category, free-text keywords, a price range, and zero or more characteristic conditions (attribute name, comparison operator, value) — evaluated deterministically; never inferred or approximated by the language model.
- **Checkout Link**: A URL to the retailer's own checkout/purchase flow, carrying the identifiers of one or more products the user picked or was most recently shown, as query parameters — constructed deterministically from known identifiers, never guessed. This system does not implement the destination checkout flow itself.
- **User**: A signed-in shopper's identity, established via Google sign-in — at minimum a stable identifier and the account's email, used to bind conversation sessions to their owner and to refuse cross-user access. The system does not build its own account/password system; identity is entirely delegated to Google.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user who provides a product category, budget, and at least one feature preference receives a recommendation after no more than one clarifying question.
- **SC-002**: When two or more products are compared, 100% of the criteria shown are identical — same attributes, same order — across all compared products.
- **SC-003**: 100% of recommended products include at least one stated reason linked to the user's original requirements and at least one disclosed trade-off.
- **SC-004**: 0% of specifications, prices, or availability values presented to users are unverifiable against approved product data.
- **SC-005**: 100% of requests missing essential information (category or budget) receive a clarifying question before any recommendation is given.
- **SC-006**: 100% of responses affected by unavailable or unverifiable data explicitly state that limitation rather than silently omitting it.
- **SC-007**: For a fully specified request, a user can go from initial request to a final recommendation — including any single clarification round — within one conversational exchange.
- **SC-008**: For responses that take more than a couple of seconds to fully generate, the user sees the first part of the advisor's answer begin appearing within 3 seconds, rather than waiting for the entire response before seeing anything.
- **SC-009**: 100% of recommendation and comparison responses present specifications, matched requirements, trade-offs, and comparison criteria as visually distinct list/table elements rather than as a single block of prose text.
- **SC-010**: Comparing the same set of products twice — once triggered through conversation and once invoked directly — yields byte-identical ratings, deltas, and rankings, proving the computation does not depend on the language model or on which path invoked it.
- **SC-011**: 100% of products returned by a filtered search satisfy every stated filter condition (category, price range, and characteristic conditions) — zero results violate a stated filter.
- **SC-012**: When a session has a prior search, recommendation, or comparison result, 100% of ordinal follow-up references ("the first one", "the cheaper of the two") resolve to the correct previously-shown product.
- **SC-013**: After N conversation turns each producing a structured rendering (recommendation cards, comparison table, or clarification prompt), all N renderings remain visible and correctly attributed to their turn — none are removed or overwritten by a later turn's result.
- **SC-014**: When a second message for a session is submitted while a prior turn for that session is still processing, 100% of such attempts result in exactly one turn being processed at a time for that session — never two turns' effects interleaved or applied out of order.
- **SC-015**: 100% of generated checkout links encode exactly the product identifiers the user referenced (by name or by ordinal/session-memory reference) — no extra, missing, or incorrect product ids.
- **SC-016**: 100% of internal service-to-service calls without a valid internal credential are refused before any product/pricing/conversation data is returned.
- **SC-017**: 100% of user-facing requests without a valid, current signed-in Google identity are refused before any product data, conversation state, or advisor response is returned.
- **SC-018**: 0% of attempts by one signed-in user to read or continue another signed-in user's conversation session succeed.
- **SC-019**: 100% of service start events, service stop events, handled requests, and encountered errors appear in the shared logging/tracing mechanism; observability backend unavailability causes 0% of user-facing request failures.

## Assumptions

- The advisor operates against the retailer's own approved product data (prices, specifications, stock); it is not expected to source facts from arbitrary external sites.
- Conversation history (a session's messages, requirements, and shown results) is treated as ordinary application data, not sensitive personal data — no special PII redaction, encryption, or limited-retention handling is required beyond what the constitution already requires for credentials/secrets (never conversation text).
- A checkout link (FR-025) points to the retailer's own existing checkout/purchase flow; this system is responsible only for constructing that link from known product identifiers, never for payment, cart, or order processing themselves.
- Accessibility (FR-026) is a baseline, not a compliance target: using native, semantic HTML controls (inputs, buttons, links) rather than custom widgets is sufficient; no accessibility audit or formal WCAG conformance level is in scope for this project.
- Logging/tracing/monitoring (FR-027/FR-028) build on the already-adopted industry-standard mechanism (OpenTelemetry) rather than introducing a second, competing one; "commonly used tools or integrations" is satisfied by exporting to a real, widely-used observability backend instead of console-only output.
- The internal service credential (FR-029) is a single shared secret known to every service, rotated by redeploying with a new value; this is not a per-service-pair key scheme — proportionate to this system's scale, not a zero-trust mesh.
- Google sign-in (FR-030) gates every user-facing entry point (chat, search, comparison, product detail, checkout) — there is no anonymous/browse-only mode.
- Both the user-facing entry point and the internal boundary independently verify identity/credentials: the Google-issued identity is validated by the outermost service that receives it directly from the signed-in user's browser session, not merely trusted because it arrived through that path — the same "never trust network position alone" posture FR-029 applies internally.
- Session ownership (FR-031) is established at session-creation time from the caller's verified identity and never changes; there is no "share a session with another user" capability in this feature.
- The product catalog can span multiple product categories (not limited to smartphones); comparison criteria are defined per category based on the attributes available for that category.
- "Essential information" for an initial recommendation is, at minimum, product category and budget; feature preferences refine the recommendation but are not always required to attempt one.
- When no product fits the stated constraints, the advisor discloses the gap and may suggest the closest alternatives only if explicitly labeled as not fully matching; it will not silently exceed a stated budget.
- A conversation may span multiple turns; the advisor retains previously stated constraints (budget, category, required features) until the user changes them.
- Currency and units follow whatever the user specifies (e.g., UAH); no currency conversion is assumed unless the user requests it.
- Progressive delivery (FR-015) applies to the advisor's own explanatory text; the structured facts within a response (prices, specifications, matched requirements, comparison values) are only ever shown once fully known and verified — a fact is never displayed as a partial/guessed value while streaming, only the narration around it appears incrementally.
- Rich formatting (FR-016/FR-017) is a presentation concern: it changes how already-grounded data and text are displayed, not what data is shown. It never introduces a fact or number that didn't already come from an approved source.
- Characteristic filter conditions (FR-020) support equality, greater-than-or-equal, less-than-or-equal, and a numeric range; this covers the catalog's existing numeric and simple categorical attributes without introducing a general-purpose query language.
- The session's memory of prior search/recommendation/comparison results (FR-022) holds only the most recently shown set, not a full history of every result ever shown in the conversation.
- Explanatory narration attached to a directly-invoked comparison (FR-018/FR-019) is optional and best-effort: if it cannot be produced, the comparison's structured data is still returned in full rather than withholding the whole response.
- Structured-rendering retention (FR-023) is a client-side presentation concern layered on top of FR-017; the backend already returns each turn's full structured result independently, so retaining prior renderings requires no new backend data, only that the conversation view keep what it already received instead of discarding it on the next turn.
