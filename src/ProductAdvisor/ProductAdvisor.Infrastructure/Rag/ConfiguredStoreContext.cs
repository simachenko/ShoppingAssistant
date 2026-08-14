using Microsoft.Extensions.Options;
using ProductAdvisor.Application;

namespace ProductAdvisor.Infrastructure.Rag;

/// <summary>
/// Resolves the current store from deployment configuration (research.md §4) — the single-store
/// implementation of <see cref="IStoreContext"/> this deployment ships with. Swapping in a
/// per-session or per-tenant resolver later replaces only this class; every retrieval query
/// already applies the store filter unconditionally either way (spec.md 002 FR-020).
/// </summary>
public sealed class ConfiguredStoreContext(IOptions<StoreInfoOptions> options) : IStoreContext
{
    public string CurrentStoreId { get; } = options.Value.StoreId;
}
