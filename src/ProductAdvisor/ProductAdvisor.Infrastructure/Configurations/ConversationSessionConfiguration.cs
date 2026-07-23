using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.Configurations;

public sealed class ConversationSessionConfiguration : IEntityTypeConfiguration<ConversationSession>
{
    public void Configure(EntityTypeBuilder<ConversationSession> builder)
    {
        builder.ToTable("conversation_sessions");
        builder.HasKey(s => s.SessionId);
        builder.Property(s => s.SessionId).ValueGeneratedNever();
        builder.Property(s => s.State).HasConversion<string>().IsRequired();

        // Conversation-internal state is never queried independently of its owning session, so
        // it is stored inline as JSON rather than mapped across relational tables. Root-level
        // JSON owned types need every member explicitly declared for EF's design-time
        // constructor-binding to find them.
        builder.OwnsMany(s => s.Messages, messages =>
        {
            messages.ToJson();
            messages.Property(m => m.Role);
            messages.Property(m => m.Text);
            messages.Property(m => m.Timestamp);
        });

        builder.OwnsOne(s => s.CurrentRequirement, requirement =>
        {
            requirement.ToJson();
            requirement.Property(r => r.Category);
            requirement.Property(r => r.RequiredFeatures);
            requirement.Property(r => r.Preferences);
            requirement.Property(r => r.Language);
            requirement.Property(r => r.Currency);
            requirement.OwnsOne(r => r.Budget, budget =>
            {
                budget.Property(m => m.Amount);
                budget.Property(m => m.Currency);
            });
        });
        builder.Navigation(s => s.CurrentRequirement).IsRequired();

        builder.OwnsOne(s => s.PendingClarification, clarification =>
        {
            clarification.ToJson();
            clarification.Property(c => c.MissingField);
            clarification.Property(c => c.QuestionText);
        });

        builder.OwnsMany(s => s.LastSearchResults, results =>
        {
            results.ToJson();
            results.Property(r => r.ProductId);
            results.Property(r => r.Name);
        });
    }
}
