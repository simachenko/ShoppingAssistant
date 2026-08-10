using System.Diagnostics.Metrics;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The seven dedicated turn-cycle metrics spec.md FR-136 requires — each independently
/// incrementable and distinguishable from general request/error metrics, exported through the
/// same OpenTelemetry pipeline every other metric in this system already uses (research.md §16)
/// once <see cref="MeterName"/> is registered with the metrics provider. A singleton — counters
/// are inherently shared, cumulative state for the process's lifetime, not a per-turn/per-scope
/// object.
/// </summary>
public sealed class TurnMetrics : IDisposable
{
    public const string MeterName = "ProductAdvisor.TurnCycle";

    private readonly Meter _meter;

    public TurnMetrics()
    {
        _meter = new Meter(MeterName);
        LoopLimitReached = _meter.CreateCounter<long>("turn.loop_limit_reached", description: "A turn reached its configured loop/iteration limit (FR-074).");
        SchemaRepairAttempted = _meter.CreateCounter<long>("turn.schema_repair_attempted", description: "A structured-intent-extraction repair attempt and its outcome (FR-051).");
        ToolCallRejected = _meter.CreateCounter<long>("turn.tool_call_rejected", description: "A tool call was rejected before execution (FR-068/FR-073/FR-108).");
        GroundingFailure = _meter.CreateCounter<long>("turn.grounding_failure", description: "A narration claim was rejected/stripped/replaced for lacking Evidence Envelope support (FR-088).");
        RateLimitRejection = _meter.CreateCounter<long>("turn.rate_limit_rejection", description: "A request was rejected for exceeding a per-user rate or concurrency limit (FR-109/FR-110).");
        PiiDetection = _meter.CreateCounter<long>("turn.pii_detection", description: "A message was flagged for potential PII (FR-116).");
        ProviderFailure = _meter.CreateCounter<long>("turn.provider_failure", description: "An LLM-provider call failed after resilience policies were exhausted (research.md §6).");
    }

    public Counter<long> LoopLimitReached { get; }
    public Counter<long> SchemaRepairAttempted { get; }
    public Counter<long> ToolCallRejected { get; }
    public Counter<long> GroundingFailure { get; }
    public Counter<long> RateLimitRejection { get; }
    public Counter<long> PiiDetection { get; }
    public Counter<long> ProviderFailure { get; }

    public void Dispose() => _meter.Dispose();
}
