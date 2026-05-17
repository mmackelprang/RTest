using FluentAssertions;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Unit tests for <see cref="VisualizerTelemetryService"/> — the
/// singleton that holds the visualizer's "updates/sec" value after
/// PR 4 moved it off the canvas (handoff §P1·3).
///
/// Verifies the read/write contract the dev tray (PR 6) will rely on:
///   * SetUpdatesPerSecond clamps negative values to zero.
///   * UpdatesPerSecond reflects the most recent value.
///   * UpdatesPerSecondChanged fires on changes and is debounced for
///     no-op writes (same value twice).
/// </summary>
public class VisualizerTelemetryServiceTests
{
  [Fact]
  public void UpdatesPerSecond_DefaultsToZero()
  {
    var svc = new VisualizerTelemetryService();
    svc.UpdatesPerSecond.Should().Be(0);
  }

  [Fact]
  public void SetUpdatesPerSecond_StoresValue()
  {
    var svc = new VisualizerTelemetryService();
    svc.SetUpdatesPerSecond(42);
    svc.UpdatesPerSecond.Should().Be(42);
  }

  [Fact]
  public void SetUpdatesPerSecond_NegativeValue_ClampsToZero()
  {
    var svc = new VisualizerTelemetryService();
    svc.SetUpdatesPerSecond(-5);
    svc.UpdatesPerSecond.Should().Be(0);
  }

  [Fact]
  public void SetUpdatesPerSecond_FiresChangedEvent_OnNewValue()
  {
    var svc = new VisualizerTelemetryService();
    var received = new List<int>();
    svc.UpdatesPerSecondChanged += v => received.Add(v);

    svc.SetUpdatesPerSecond(10);
    svc.SetUpdatesPerSecond(20);

    received.Should().Equal(new[] { 10, 20 });
  }

  [Fact]
  public void SetUpdatesPerSecond_SameValue_DoesNotFireEventTwice()
  {
    // Stable rates should not spam subscribers — write the same value
    // back-to-back and only the first should be observed.
    var svc = new VisualizerTelemetryService();
    var received = new List<int>();
    svc.UpdatesPerSecondChanged += v => received.Add(v);

    svc.SetUpdatesPerSecond(30);
    svc.SetUpdatesPerSecond(30);
    svc.SetUpdatesPerSecond(30);

    received.Should().Equal(new[] { 30 });
  }

  [Fact]
  public void SetUpdatesPerSecond_NoSubscribers_DoesNotThrow()
  {
    var svc = new VisualizerTelemetryService();
    var act = () => svc.SetUpdatesPerSecond(7);
    act.Should().NotThrow();
  }
}
