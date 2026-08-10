using Microsoft.Extensions.AI;
using ProductAdvisor.Application.Pipeline;

namespace ProductAdvisor.Application.Tests;

public sealed class FakeToolCatalog : IAdvisorToolCatalog
{
    public IReadOnlyList<AITool> GetTools() => [];

    public IReadOnlyList<AITool> GetTools(Route route) => [];
}
