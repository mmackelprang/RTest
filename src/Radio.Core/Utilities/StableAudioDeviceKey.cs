namespace Radio.Core.Utilities;

/// <summary>
/// Builds and interprets the persisted identity of an audio output device (AUD-6).
///
/// <para>
/// <b>The defect this replaces.</b> Device ids were minted as <c>$"playback-{i}"</c> straight off
/// the enumeration index, and that string reached storage. Resolution back to hardware was pure
/// string parsing — an <c>int.TryParse</c> — not a lookup. So a saved preference did not name a
/// device, it named a <i>position in a list</i>, and the list reorders. Measured across one deploy
/// restart: <c>playback-1</c> meant the soundbar on Aug 10 and USB Audio Out on Aug 11. Nothing in
/// the store changed; the meaning did. In a sealed cabinet that presents as "no sound from the
/// speakers" after a routine restart — a laptop-and-SSH recovery for what looks like dead hardware.
/// </para>
///
/// <para>
/// <b>Why the device's own id was not used.</b> <c>SoundFlow.Structs.DeviceInfo.Id</c> is an
/// <c>nint</c> — a pointer into MiniAudio's native <c>ma_device_id</c> union, valid only inside the
/// process that enumerated it. Three consecutive runs over the same three devices produced three
/// entirely different sets of values. Persisting one yields a key that is meaningless on the next
/// start, which is <i>worse</i> than the ordinal: a stale ordinal still resolves to some device,
/// while a stale pointer resolves to nothing at all.
/// </para>
///
/// <para>
/// <b>The known weakness, stated rather than hidden.</b> Two devices with identical raw names
/// collide. On this appliance they do not today — the box exposes a single playback sink — but a
/// second identical USB DAC would produce two devices with one key. The resolver therefore treats
/// an ambiguous key as <i>unresolved</i> and says so, rather than silently selecting the first
/// match. Picking one arbitrarily is how you get audio out of the wrong speaker with nothing in the
/// log to explain it.
/// </para>
/// </summary>
public static class StableAudioDeviceKey
{
  /// <summary>Prefix marking a name-based output key.</summary>
  public const string OutputPrefix = "out:";

  /// <summary>Prefix of the superseded enumeration-ordinal ids.</summary>
  public const string LegacyOrdinalPrefix = "playback-";

  /// <summary>Builds the persisted key for an output device from its raw platform name.</summary>
  public static string ForOutput(string rawName)
  {
    ArgumentNullException.ThrowIfNull(rawName);
    return OutputPrefix + rawName;
  }

  /// <summary>
  /// True for a pre-AUD-6 ordinal id such as <c>playback-1</c>.
  ///
  /// <para>
  /// These are deliberately treated as <b>unset</b> rather than migrated in place. The ordinal's
  /// meaning is already lost — resolving it against today's enumeration would bake in whatever
  /// ordering happens to exist at migration time, and the entire defect is that that ordering is
  /// not trustworthy. One re-selection in the UI costs less than a preference that is confidently
  /// wrong.
  /// </para>
  /// </summary>
  public static bool IsLegacyOrdinal(string? deviceId) =>
    deviceId is not null && deviceId.StartsWith(LegacyOrdinalPrefix, StringComparison.Ordinal);

  /// <summary>
  /// Extracts the raw platform name from an output key, or null if this is not one.
  /// </summary>
  public static string? RawNameFrom(string? deviceId) =>
    deviceId is not null && deviceId.StartsWith(OutputPrefix, StringComparison.Ordinal)
      ? deviceId[OutputPrefix.Length..]
      : null;
}
