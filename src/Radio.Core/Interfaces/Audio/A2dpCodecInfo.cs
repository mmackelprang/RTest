namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Snapshot of the currently-negotiated A2DP codec for a Bluetooth device.
/// Returned by <see cref="IBluetoothService.GetA2dpCodecInfoAsync"/>.
/// </summary>
/// <remarks>
/// Layout per BlueZ's <c>net/bluetooth/a2dp-codecs.h</c>:
///   <list type="bullet">
///     <item>0x00 = SBC, 0x02 = MPEG-2/4 AAC, 0xFF = vendor-specific (aptX, LDAC, etc.)</item>
///     <item>SBC Configuration is 4 bytes (sample-freq/channel-mode/blocks/subbands/alloc/bitpool).</item>
///     <item>AAC Configuration is 6 bytes; vendor-specific is variable.</item>
///   </list>
/// </remarks>
public sealed record A2dpCodecInfo
{
  /// <summary>BlueZ codec ID (0x00 = SBC, 0x02 = AAC, 0xFF = vendor-specific).</summary>
  public required byte CodecId { get; init; }

  /// <summary>Human-readable codec name (e.g. "SBC", "AAC", "aptX", "aptX-HD", "LDAC").</summary>
  public required string CodecName { get; init; }

  /// <summary>Negotiated sample rate (e.g. 48000); 0 if unknown.</summary>
  public required int SampleRateHz { get; init; }

  /// <summary>SBC bitpool value (2–53 typical). Null for non-SBC codecs.</summary>
  public int? BitpoolOrNull { get; init; }

  /// <summary>Raw Configuration bytes (codec-specific layout). For diagnostics.</summary>
  public required byte[] RawConfiguration { get; init; }
}

/// <summary>
/// Event payload for <see cref="IBluetoothService.A2dpCodecChanged"/>. Raised on
/// transport attach (initial codec) and whenever BlueZ re-negotiates the codec
/// mid-session.
/// </summary>
public class A2dpCodecChangedEventArgs : EventArgs
{
  public required string DeviceAddress { get; init; }
  public required A2dpCodecInfo CodecInfo { get; init; }
}
