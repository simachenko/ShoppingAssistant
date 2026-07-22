using ProductAdvisor.Domain;

namespace ProductAdvisor.Domain.Tests;

public class ConversationSessionTests
{
    [Fact]
    public void New_session_starts_in_Collecting_state_with_empty_requirement()
    {
        var session = new ConversationSession(Guid.NewGuid());

        Assert.Equal(ConversationState.Collecting, session.State);
        Assert.Equal(UserRequirement.Empty, session.CurrentRequirement);
        Assert.Null(session.PendingClarification);
    }

    [Fact]
    public void Constructor_throws_when_session_id_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new ConversationSession(Guid.Empty));
    }

    [Fact]
    public void StartRecommending_throws_when_essential_information_is_missing()
    {
        var session = new ConversationSession(Guid.NewGuid());
        session.UpdateRequirement(new UserRequirement { Category = "smartphones" }); // no budget yet

        Assert.Throws<InvalidOperationException>(session.StartRecommending);
        Assert.Equal(ConversationState.Collecting, session.State);
    }

    [Fact]
    public void StartRecommending_succeeds_once_category_and_budget_are_known()
    {
        var session = new ConversationSession(Guid.NewGuid());
        session.UpdateRequirement(new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
        });

        session.StartRecommending();

        Assert.Equal(ConversationState.Recommending, session.State);
    }

    [Fact]
    public void AskClarification_sets_pending_question_and_stays_in_Collecting()
    {
        var session = new ConversationSession(Guid.NewGuid());

        session.AskClarification(new ClarificationQuestion("Budget", "What's your budget?"));

        Assert.Equal(ConversationState.Collecting, session.State);
        Assert.NotNull(session.PendingClarification);
        Assert.Equal("Budget", session.PendingClarification!.MissingField);
    }

    [Fact]
    public void UpdateRequirement_clears_pending_clarification_and_drops_back_to_Collecting()
    {
        var session = new ConversationSession(Guid.NewGuid());
        session.UpdateRequirement(new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
        });
        session.StartRecommending();

        // The user changes their budget mid-conversation (FR-011): the prior recommendation
        // context is superseded, not silently merged.
        session.UpdateRequirement(session.CurrentRequirement with { Budget = new Money(20000m, "UAH") });

        Assert.Equal(ConversationState.Collecting, session.State);
        Assert.Null(session.PendingClarification);
        Assert.Equal(20000m, session.CurrentRequirement.Budget!.Amount);
    }

    [Fact]
    public void StartComparing_clears_pending_clarification()
    {
        var session = new ConversationSession(Guid.NewGuid());
        session.AskClarification(new ClarificationQuestion("Budget", "What's your budget?"));

        session.StartComparing();

        Assert.Equal(ConversationState.Comparing, session.State);
        Assert.Null(session.PendingClarification);
    }

    [Fact]
    public void AddMessage_appends_to_history_in_order()
    {
        var session = new ConversationSession(Guid.NewGuid());
        var first = new ConversationMessage("user", "hello", DateTimeOffset.UtcNow);
        var second = new ConversationMessage("assistant", "hi!", DateTimeOffset.UtcNow);

        session.AddMessage(first);
        session.AddMessage(second);

        Assert.Equal([first, second], session.Messages);
    }
}

public class UserRequirementTests
{
    [Fact]
    public void HasEssentialInformation_is_false_without_category_or_budget()
    {
        Assert.False(UserRequirement.Empty.HasEssentialInformation);
    }

    [Fact]
    public void HasEssentialInformation_is_false_with_only_category()
    {
        var requirement = new UserRequirement { Category = "smartphones" };

        Assert.False(requirement.HasEssentialInformation);
    }

    [Fact]
    public void HasEssentialInformation_is_false_with_only_budget()
    {
        var requirement = new UserRequirement { Budget = new Money(15000m, "UAH") };

        Assert.False(requirement.HasEssentialInformation);
    }

    [Fact]
    public void HasEssentialInformation_is_true_once_both_are_present()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };

        Assert.True(requirement.HasEssentialInformation);
    }
}
