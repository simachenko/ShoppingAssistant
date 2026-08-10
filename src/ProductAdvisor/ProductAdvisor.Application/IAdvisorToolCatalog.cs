using Microsoft.Extensions.AI;
using ProductAdvisor.Application.Pipeline;

namespace ProductAdvisor.Application;

/// <summary>
/// The set of tools available to the LLM this turn. Implemented in Infrastructure (which owns
/// the concrete tool handlers); the orchestrator only ever sees the abstraction so it cannot
/// depend on — or accidentally reimplement — any tool's logic.
/// </summary>
public interface IAdvisorToolCatalog
{
    IReadOnlyList<AITool> GetTools();

    /// <summary>
    /// The full catalog scoped to exactly the given route's <c>ToolRecipe</c> (spec.md
    /// FR-066–FR-070) — never the full seven-tool catalog for a route that doesn't need it.
    /// </summary>
    IReadOnlyList<AITool> GetTools(Route route);
}
