using Microsoft.Extensions.AI;

namespace ProductAdvisor.Application.Tests;

public sealed class FakeToolCatalog : IAdvisorToolCatalog
{
    public IReadOnlyList<AITool> GetTools() => [];
}
