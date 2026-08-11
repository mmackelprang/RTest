using SoundFlow.Backends.MiniAudio;

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
}
