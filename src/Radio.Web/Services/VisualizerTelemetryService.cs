namespace Radio.Web.Services;

/// <summary>
/// Singleton holding the most recent visualizer "updates per second"
/// telemetry value (the rate at which the visualizer's <c>OnLevel</c> /
/// <c>OnSpectrum</c> / <c>OnWaveform</c> callbacks are firing).
///
/// <para>
/// Introduced in PR 4 of the design tightening arc (handoff §P1·3) to
/// remove the burned-in <c>"Updates: NN/sec"</c> overlay from the
/// visualizer canvas while keeping the value available for the dev
/// tray, which is built in PR 6 (handoff §P2·2).
/// </para>
///
/// <para>
/// Consumers should subscribe to <see cref="UpdatesPerSecondChanged"/>
/// for push notifications, or read <see cref="UpdatesPerSecond"/>
/// directly for the latest snapshot. The service stays in-process —
/// no SignalR, no persistence.
/// </para>
/// </summary>
public sealed class VisualizerTelemetryService
{
  private int _updatesPerSecond;

  /// <summary>
  /// The most recently published "updates per second" measurement.
  /// Defaults to zero until the visualizer publishes a value.
  /// </summary>
  public int UpdatesPerSecond => _updatesPerSecond;

  /// <summary>
  /// Fired when <see cref="UpdatesPerSecond"/> changes. The handler is
  /// invoked synchronously from the publishing thread; subscribers
  /// should marshal to their own UI dispatcher as required.
  /// </summary>
  public event Action<int>? UpdatesPerSecondChanged;

  /// <summary>
  /// Publishes a new "updates per second" value. No-op when the value
  /// matches the current value (saves UI churn for stable rates).
  /// </summary>
  /// <param name="updatesPerSecond">
  /// Non-negative rate. Negative inputs are clamped to zero.
  /// </param>
  public void SetUpdatesPerSecond(int updatesPerSecond)
  {
    var v = updatesPerSecond < 0 ? 0 : updatesPerSecond;
    if (_updatesPerSecond == v)
    {
      return;
    }

    _updatesPerSecond = v;
    UpdatesPerSecondChanged?.Invoke(v);
  }
}
