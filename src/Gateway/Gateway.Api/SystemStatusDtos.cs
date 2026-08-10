namespace Gateway.Api;

/// <summary>Response shape of <c>GET /api/system-status</c> (FR-033/FR-034/FR-035,
/// data-model.md "SystemReadinessStatus", research.md §19) — a point-in-time snapshot, never
/// persisted, built fresh on every call by concurrently probing each dependent service's own
/// <c>/alive</c> endpoint.</summary>
public sealed record SystemReadinessStatus(string Overall, IReadOnlyList<ServiceReadiness> Services);

public sealed record ServiceReadiness(string Name, bool Reachable, DateTimeOffset CheckedAt);
