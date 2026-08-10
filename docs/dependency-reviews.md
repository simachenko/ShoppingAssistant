# Dependency Reviews

Documented production-readiness reviews for dependencies that warrant one — primarily preview/
prerelease packages this system depends on for a core capability (spec.md FR-129's preview-
dependency production-readiness review, research.md §18).

## ModelContextProtocol / ModelContextProtocol.AspNetCore

**Reviewed**: 2026-08-10 (Phase 13, spec.md FR-129)

**Currently pinned version**: `2.0.0-preview.3` (`src/ProductAdvisor/ProductAdvisor.Infrastructure/ProductAdvisor.Infrastructure.csproj`,
`src/ProductAdvisor/ProductAdvisor.Api/ProductAdvisor.Api.csproj`) — a **preview** package.

**Role in this system**: the official C# SDK for the Model Context Protocol. `ModelContextProtocol`
provides `[McpServerToolType]`/`[McpServerTool]` (used by `DataAccessTools`/`ComputeTools`, the
Advisor's seven MCP tools) and `AIFunctionFactory` (used by `AdvisorToolCatalog` to expose those
same tool handlers in-process for the chat client's own function-invocation loop).
`ModelContextProtocol.AspNetCore` provides `AddMcpServer()`/`WithHttpTransport()`/`MapMcp("/mcp")`
(`ProductAdvisor.Api/Program.cs`). This is a core, load-bearing dependency — not an optional or
easily-substitutable one — since it's the actual MCP transport implementation, not merely a
convenience wrapper this system could drop.

**Finding**: a **stable 2.1.0 release is now available** (published 2026-08-05, five days before
this review), no longer marked preview/prerelease. This system is currently five minor versions
and one prerelease-to-stable transition behind.

**Recommendation**: upgrade `2.0.0-preview.3` → `2.1.0` (or the current stable release at upgrade
time) in both `.csproj` files above. This is **not done as part of this review** — a package
upgrade this central (it defines the exact shape of every registered MCP tool and the transport
hosting them) needs to be verified against a live MCP client and the full tool-invocation path
before being trusted, and this sandbox has no Docker/live-LLM access to do that verification with
(the same limitation documented throughout this project's `tasks.md` for every
`ProductAdvisor.Api.Tests`/`EndToEnd.Tests` addition). Tracked as a follow-up, not silently
deferred.

**Interim risk assessment** (why running on the preview version until that upgrade lands is an
acceptable, bounded risk rather than a blocker): the preview package is already in active use
against this system's full test suite (`ProductAdvisor.Api.Tests`' MCP tool contract tests) and
deployed via `render.yaml`; the specific APIs this system depends on
(`McpServerToolTypeAttribute`/`McpServerToolAttribute`/`AIFunctionFactory`/`AddMcpServer`/
`WithHttpTransport`/`MapMcp`) are stable, well-established surface area for this SDK, not
recently-added or experimental members within it. The risk this review flags is staleness (missing
five versions' worth of fixes and the preview→stable stabilization itself), not a known defect in
the pinned version.

**Next review**: whenever this dependency is next touched (a version bump for any other reason,
or the next scheduled dependency audit) — re-check for a newer stable release and re-evaluate this
finding rather than treating the recommendation above as a one-time note that can go stale.
