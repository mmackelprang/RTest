using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A <see cref="MiniAudioEngine"/> whose native device enumeration is serialized through
/// <see cref="NativeAudioDeviceGate"/>.
///
/// <para>This type exists so that serialization is a property of the <i>engine</i> rather
/// than of each call site. <c>UpdateAudioDevicesInfo</c> is virtual, so every caller —
/// including the ones in <c>SoundFlowAudioEngine</c> that this class never touches, and any
/// call site added in the future — is gated by construction. That is deliberately stronger
/// than sprinkling <c>lock</c> statements at the six call sites known today: a seventh call
/// site is safe the moment it is written, with no one having to remember the rule.</para>
///
/// <para>The constructor is private and construction goes through <see cref="Create"/> so an
/// ungated raw engine cannot be introduced by accident.</para>
/// </summary>
internal sealed class SerializedMiniAudioEngine : MiniAudioEngine
{
  private SerializedMiniAudioEngine()
  {
  }

  /// <summary>
  /// Creates an engine, holding the gate across construction.
  /// </summary>
  /// <remarks>
  /// Engine construction runs <c>ma_context_init</c>, which probes the JACK/PulseAudio/OSS/ALSA
  /// backends and performs an initial device enumeration. That initial enumeration re-enters
  /// this class through <see cref="UpdateAudioDevicesInfo"/> while the gate is already held by
  /// this thread — safe only because the gate is re-entrant (see
  /// <see cref="NativeAudioDeviceGate"/>).
  /// </remarks>
  internal static SerializedMiniAudioEngine Create() =>
    NativeAudioDeviceGate.Run(static () => new SerializedMiniAudioEngine());

  /// <summary>
  /// Enumerates native audio devices under the process-wide gate.
  /// </summary>
  /// <remarks>
  /// This is the call that aborted the process on 2026-08-10 when two threads reached it at
  /// once. Note that the base implementation is what publishes <c>PlaybackDevices</c> /
  /// <c>CaptureDevices</c>; callers that need a device list consistent with <i>their own</i>
  /// enumeration should read those properties inside their own
  /// <see cref="NativeAudioDeviceGate.Run{T}"/> region rather than relying on this override
  /// alone, which only guarantees that no two threads are inside the native call.
  /// </remarks>
  public override void UpdateAudioDevicesInfo() =>
    NativeAudioDeviceGate.Run(base.UpdateAudioDevicesInfo);

  // Device open/switch drives the same context main loop that enumeration does
  // (`ma_device_init__pulse` waits on `pa_operation`s by iterating it), so opening a device
  // concurrently with an enumeration is the same race. These are reachable today: a device
  // switch is dispatched fire-and-forget from DevicesController, and TryRecoverPlaybackDevice
  // enumerates and then opens. Gating them here rather than at their call sites keeps that
  // guarantee independent of the caller — which is the whole point of putting the gate on the
  // engine type.
  //
  // Start/Stop/Dispose on the returned AudioPlaybackDevice/AudioCaptureDevice also drive the
  // main loop, but they live on SoundFlow device types this class cannot intercept, so those
  // take the gate at their call sites in SoundFlowAudioEngine instead. Any new device
  // lifecycle call there must do the same — the engine override cannot cover it for you.

  public override AudioPlaybackDevice InitializePlaybackDevice(
    DeviceInfo? deviceInfo, AudioFormat format, DeviceConfig? config = null) =>
    NativeAudioDeviceGate.Run(() => base.InitializePlaybackDevice(deviceInfo, format, config));

  public override AudioCaptureDevice InitializeCaptureDevice(
    DeviceInfo? deviceInfo, AudioFormat format, DeviceConfig? config = null) =>
    NativeAudioDeviceGate.Run(() => base.InitializeCaptureDevice(deviceInfo, format, config));

  public override FullDuplexDevice InitializeFullDuplexDevice(
    DeviceInfo? playbackDeviceInfo, DeviceInfo? captureDeviceInfo, AudioFormat format,
    DeviceConfig? config = null) =>
    NativeAudioDeviceGate.Run(
      () => base.InitializeFullDuplexDevice(playbackDeviceInfo, captureDeviceInfo, format, config));

  public override AudioCaptureDevice InitializeLoopbackDevice(
    AudioFormat format, DeviceConfig? config = null) =>
    NativeAudioDeviceGate.Run(() => base.InitializeLoopbackDevice(format, config));

  public override AudioPlaybackDevice SwitchDevice(
    AudioPlaybackDevice oldDevice, DeviceInfo newDeviceInfo, DeviceConfig? config = null) =>
    NativeAudioDeviceGate.Run(() => base.SwitchDevice(oldDevice, newDeviceInfo, config));

  public override AudioCaptureDevice SwitchDevice(
    AudioCaptureDevice oldDevice, DeviceInfo newDeviceInfo, DeviceConfig? config = null) =>
    NativeAudioDeviceGate.Run(() => base.SwitchDevice(oldDevice, newDeviceInfo, config));

  public override FullDuplexDevice SwitchDevice(
    FullDuplexDevice oldDevice, DeviceInfo? newPlaybackInfo, DeviceInfo? newCaptureInfo,
    DeviceConfig? config = null) =>
    NativeAudioDeviceGate.Run(
      () => base.SwitchDevice(oldDevice, newPlaybackInfo, newCaptureInfo, config));
}
