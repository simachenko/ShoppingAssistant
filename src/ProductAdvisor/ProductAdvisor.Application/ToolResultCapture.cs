using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

public sealed class ToolResultCapture : IToolResultCapture
{
    public Recommendation? Recommendation { get; private set; }
    public UserRequirement? RequirementUsed { get; private set; }
    public Comparison? Comparison { get; private set; }

    public void SetRecommendation(Recommendation recommendation, UserRequirement requirementUsed)
    {
        Recommendation = recommendation;
        RequirementUsed = requirementUsed;
    }

    public void SetComparison(Comparison comparison)
    {
        Comparison = comparison;
    }
}
