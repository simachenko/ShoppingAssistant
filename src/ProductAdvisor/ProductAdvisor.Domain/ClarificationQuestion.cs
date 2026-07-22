namespace ProductAdvisor.Domain;

/// <summary>
/// A single focused question raised when essential information is missing (FR-002/FR-003).
/// Only one may be pending on a ConversationSession at a time. Plain class (not a record) —
/// EF Core's owned-JSON constructor binding gets confused by a record's synthesized copy
/// constructor, the same reason Specification and Money are plain classes.
/// </summary>
public sealed class ClarificationQuestion : IEquatable<ClarificationQuestion>
{
    public string MissingField { get; }
    public string QuestionText { get; }

    public ClarificationQuestion(string missingField, string questionText)
    {
        if (string.IsNullOrWhiteSpace(missingField))
            throw new ArgumentException("MissingField is required.", nameof(missingField));
        if (string.IsNullOrWhiteSpace(questionText))
            throw new ArgumentException("QuestionText is required.", nameof(questionText));

        MissingField = missingField;
        QuestionText = questionText;
    }

    public bool Equals(ClarificationQuestion? other) =>
        other is not null && MissingField == other.MissingField && QuestionText == other.QuestionText;

    public override bool Equals(object? obj) => Equals(obj as ClarificationQuestion);

    public override int GetHashCode() => HashCode.Combine(MissingField, QuestionText);
}
