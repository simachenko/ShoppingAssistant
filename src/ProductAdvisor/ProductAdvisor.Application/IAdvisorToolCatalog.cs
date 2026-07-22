using Microsoft.Extensions.AI;

namespace ProductAdvisor.Application;

/// <summary>
/// The set of tools available to the LLM this turn. Implemented in Infrastructure (which owns
/// the concrete tool handlers); the orchestrator only ever sees the abstraction so it cannot
/// depend on — or accidentally reimplement — any tool's logic.
/// </summary>
public interface IAdvisorToolCatalog
{
    IReadOnlyList<AITool> GetTools();
}
