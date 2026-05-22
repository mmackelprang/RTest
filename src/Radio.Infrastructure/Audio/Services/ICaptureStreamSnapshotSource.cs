namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Snapshot accessor consumed by <see cref="BluetoothCaptureWatchdog"/>. Returns
/// the currently active BT capture stream's last-OnProcess gap, or <c>null</c>
/// when no native stream is running. Extracted as an interface so the watchdog
/// can be unit-tested without instantiating <c>LinuxBluetoothService</c>'s
/// D-Bus dependencies.
/// </summary>
/// <remarks>
/// Implemented in production by <c>LinuxBluetoothService</c>; tests provide a
/// fake. Cross-TFM (compiles on Windows too — see <see cref="NullCaptureStreamSnapshotSource"/>).
/// </remarks>
internal interface ICaptureStreamSnapshotSource
{
  /// <summary>
  /// Returns the connected device's address and elapsed ms since the last
  /// OnProcess callback fired, or <c>null</c> when no native capture stream is
  /// active.
  /// </summary>
  (string Address, long ElapsedMs)? GetCaptureStreamSnapshot();

  /// <summary>
  /// Invoked when the watchdog has confirmed a stall (threshold + consecutive
  /// checks). Raises the BT service's <c>CaptureStreamStalled</c> event.
  /// </summary>
  void RaiseCaptureStreamStalled(string address, long elapsedMs, int consecutiveChecks);
}

/// <summary>
/// No-op fallback used when no Linux BT service is available (Windows, Mock,
/// or BT disabled). The watchdog's null check still drives a "watchdog disabled"
/// log path; this fallback is registered to ensure constructor injection works.
/// </summary>
internal sealed class NullCaptureStreamSnapshotSource : ICaptureStreamSnapshotSource
{
  public static readonly NullCaptureStreamSnapshotSource Instance = new();

  private NullCaptureStreamSnapshotSource() { }

  public (string Address, long ElapsedMs)? GetCaptureStreamSnapshot() => null;

  public void RaiseCaptureStreamStalled(string address, long elapsedMs, int consecutiveChecks)
  {
    // No-op: there is no underlying BT service to raise on this platform.
  }
}
