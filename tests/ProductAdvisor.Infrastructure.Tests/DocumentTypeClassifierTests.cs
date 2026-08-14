using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure.Rag;
using Xunit;

namespace ProductAdvisor.Infrastructure.Tests;

/// <summary>
/// The document-type ranking preference (spec.md 002 FR-022, research.md §5). The important case
/// is the last one: an unclassifiable question must yield no preference at all, because a wrong
/// preference silently biases every such question toward an unrelated policy area.
/// </summary>
public class DocumentTypeClassifierTests
{
    [Theory]
    [InlineData("How long does delivery take?", DocumentType.Delivery)]
    [InlineData("Скільки триває доставка?", DocumentType.Delivery)]
    [InlineData("What payment methods do you accept?", DocumentType.Payment)]
    [InlineData("Які способи оплати?", DocumentType.Payment)]
    [InlineData("Can I return an opened item?", DocumentType.Returns)]
    [InlineData("Як оформити повернення?", DocumentType.Returns)]
    [InlineData("What does the warranty cover?", DocumentType.Warranty)]
    [InlineData("How does the loyalty programme work?", DocumentType.Loyalty)]
    [InlineData("What is your phone number?", DocumentType.Contacts)]
    public void A_question_about_one_policy_area_is_classified_as_that_type(string question, DocumentType expected)
    {
        Assert.Equal(expected, DocumentTypeClassifier.Classify(question));
    }

    [Theory]
    [InlineData("Do you have anything nice?")]
    [InlineData("Tell me more about that")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_question_that_matches_no_policy_area_yields_no_preference(string question)
    {
        // Null, never a default type: retrieval then searches across all types rather than
        // forcing an incorrect filter (FR-022).
        Assert.Null(DocumentTypeClassifier.Classify(question));
    }

    [Fact]
    public void The_strongest_keyword_match_wins_when_a_question_touches_two_areas()
    {
        // "paid ... send it back" leans returns; the classifier picks one type to *boost*, and a
        // wrong pick here costs ranking position only — both documents remain retrievable.
        var classified = DocumentTypeClassifier.Classify(
            "I paid for this and now I want to return it and get a refund");

        Assert.Equal(DocumentType.Returns, classified);
    }
}
