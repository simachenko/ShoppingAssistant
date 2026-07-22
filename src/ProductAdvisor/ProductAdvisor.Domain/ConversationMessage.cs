namespace ProductAdvisor.Domain;

/// <summary>Plain class (not a record) for the same EF Core constructor-binding reason as Money.</summary>
public sealed class ConversationMessage : IEquatable<ConversationMessage>
{
    public string Role { get; }
    public string Text { get; }
    public DateTimeOffset Timestamp { get; }

    public ConversationMessage(string role, string text, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role is required.", nameof(role));
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required.", nameof(text));

        Role = role;
        Text = text;
        Timestamp = timestamp;
    }

    public bool Equals(ConversationMessage? other) =>
        other is not null && Role == other.Role && Text == other.Text && Timestamp == other.Timestamp;

    public override bool Equals(object? obj) => Equals(obj as ConversationMessage);

    public override int GetHashCode() => HashCode.Combine(Role, Text, Timestamp);
}
