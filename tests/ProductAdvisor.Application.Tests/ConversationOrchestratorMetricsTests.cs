using System.Diagnostics.Metrics;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 13: proves the turn-processing cycle actually increments its metrics at the real
/// decision points, not just that <see cref="TurnMetrics"/>'s counters exist in isolation.
/// </summary>
public sealed class ConversationOrchestratorMetricsTests : IDisposable
{
    private readonly TurnMetrics _metrics = new();
    private readonly MeterListener _listener = new();
    private readonly List<string> _observedInstruments = [];

    public ConversationOrchestratorMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => _observedInstruments.Add(instrument.Name));
        _listener.Start();
    }

    [Fact]
    public async Task A_pii_flagged_message_increments_the_pii_detection_metric()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Smalltalk());
        var orchestrator = BuildOrchestrator(chatClient);

        await Assert.ThrowsAsync<GuardrailRejectionException>(() => orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "My card is 4111 1111 1111 1111", CancellationToken.None));

        Assert.Contains("turn.pii_detection", _observedInstruments);
    }

    [Fact]
    public async Task A_schema_validation_failure_that_triggers_a_repair_attempt_increments_the_repair_metric()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Malformed, ExtractionJson.Smalltalk());
        var orchestrator = BuildOrchestrator(chatClient);

        await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "hello", CancellationToken.None);

        Assert.Contains("turn.schema_repair_attempted", _observedInstruments);
    }

    [Fact]
    public async Task An_ungrounded_narration_claim_increments_the_grounding_failure_metric()
    {
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items =
            [
                new RecommendedItem
                {
                    Candidate = new ProductCandidate
                    {
                        ProductId = Guid.NewGuid(),
                        Name = "Galaxy S24",
                        Price = new Money(14500m, "UAH"),
                        PriceVerified = true,
                    },
                    MatchedRequirements = [],
                    TradeOffs = ["ok"],
                    Score = 1m,
                },
            ],
        };
        var chatClient = new FakeChatClient(
            ExtractionJson.Recommend("smartphones", 15000m, "UAH"),
            "This phone is only 99999 UAH, a steal!"); // fabricated price, absent from the Evidence Envelope.
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, _metrics), new FakeRecommendationService(recommendation),
            new FakeStoreInfoRetrievalService(), TestBudgetGuard.Generous, new RequestGuardrailOptions(), _metrics, TestLogger.Instance);

        await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "I need a smartphone under 15000 UAH", CancellationToken.None);

        Assert.Contains("turn.grounding_failure", _observedInstruments);
    }

    private ConversationOrchestrator BuildOrchestrator(FakeChatClient chatClient) =>
        new(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, _metrics),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            new FakeStoreInfoRetrievalService(), TestBudgetGuard.Generous, new RequestGuardrailOptions(), _metrics, TestLogger.Instance);

    public void Dispose()
    {
        _listener.Dispose();
        _metrics.Dispose();
    }
}
