using System.Diagnostics.Metrics;
using ProductAdvisor.Application.Pipeline;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 13 (spec.md FR-136): each of the seven dedicated turn-cycle metrics is independently
/// incrementable and distinguishable from the others — verified by actually observing emitted
/// measurements through a <see cref="MeterListener"/> (proving the counters are really wired to
/// <see cref="TurnMetrics.MeterName"/>, not just that <c>.Add()</c> doesn't throw).
/// </summary>
public sealed class ObservabilityPolicyTests : IDisposable
{
    private readonly TurnMetrics _metrics = new();
    private readonly MeterListener _listener = new();
    private readonly List<(string Instrument, long Value)> _observed = [];

    public ObservabilityPolicyTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            _observed.Add((instrument.Name, measurement)));
        _listener.Start();
    }

    [Theory]
    [InlineData("turn.loop_limit_reached")]
    [InlineData("turn.schema_repair_attempted")]
    [InlineData("turn.tool_call_rejected")]
    [InlineData("turn.grounding_failure")]
    [InlineData("turn.rate_limit_rejection")]
    [InlineData("turn.pii_detection")]
    [InlineData("turn.provider_failure")]
    public void Every_required_metric_is_exposed_under_the_turn_cycle_meter(string instrumentName)
    {
        // Increment every counter once so InstrumentPublished has fired for all seven, then
        // assert the specific one under test was actually observed.
        IncrementAll();

        Assert.Contains(_observed, o => o.Instrument == instrumentName);
    }

    [Fact]
    public void Each_metric_increments_independently_of_the_others()
    {
        _metrics.GroundingFailure.Add(1);
        _metrics.GroundingFailure.Add(1);
        _metrics.PiiDetection.Add(1);

        Assert.Equal(2, _observed.Count(o => o.Instrument == "turn.grounding_failure"));
        Assert.Equal(1, _observed.Count(o => o.Instrument == "turn.pii_detection"));
        Assert.DoesNotContain(_observed, o => o.Instrument == "turn.loop_limit_reached");
    }

    private void IncrementAll()
    {
        _metrics.LoopLimitReached.Add(1);
        _metrics.SchemaRepairAttempted.Add(1);
        _metrics.ToolCallRejected.Add(1);
        _metrics.GroundingFailure.Add(1);
        _metrics.RateLimitRejection.Add(1);
        _metrics.PiiDetection.Add(1);
        _metrics.ProviderFailure.Add(1);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _metrics.Dispose();
    }
}
