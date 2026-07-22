namespace Gateway.Api;

public sealed record ChatMessageRequest(Guid? SessionId, string Text);
