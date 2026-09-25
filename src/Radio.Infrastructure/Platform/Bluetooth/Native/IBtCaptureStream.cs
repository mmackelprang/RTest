#if !WINDOWS_TARGET
using System;

namespace Radio.Infrastructure.Platform.Bluetooth.Native;

/// <summary>
/// The slice of <see cref="PipeWireNativeStream"/> that <c>LinuxBluetoothService</c> uses.
/// </summary>
/// <remarks>
/// Exists as a test seam for AUD-10 Task C (the parked capture re-binding to a recreated node):
/// constructing a real <see cref="PipeWireNativeStream"/> calls <c>pw_init</c>, which needs
/// libpipewire, so the re-bind orchestration could not otherwise be exercised on a Windows dev box
/// or in CI. Production code has exactly one implementation.
/// </remarks>
internal interface IBtCaptureStream : IDisposable
{
  /// <summary>The <c>object.serial</c> this stream was asked to capture from.</summary>
  uint TargetNodeSerial { get; }

  /// <summary>Starts the stream. Throws on failure.</summary>
  void Start();

  /// <summary>
  /// Elapsed milliseconds since the last OnProcess callback, or <see cref="long.MaxValue"/> if none
  /// has fired yet.
  /// </summary>
  long MillisecondsSinceLastOnProcess();
}
#endif
