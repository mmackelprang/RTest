namespace RTLSDRCore.Sweep;

/// <summary>
/// The US FM broadcast channel plan: 87.9 MHz to 107.9 MHz on odd tenths,
/// 200 kHz apart — 101 channels.
/// </summary>
public static class FmChannelPlan
{
  /// <summary>First channel centre, in Hz (87.9 MHz).</summary>
  public const long FirstChannelHz = 87_900_000;

  /// <summary>Last channel centre, in Hz (107.9 MHz).</summary>
  public const long LastChannelHz = 107_900_000;

  /// <summary>Spacing between adjacent channel centres, in Hz.</summary>
  public const long ChannelSpacingHz = 200_000;

  /// <summary>Lower edge of the band as displayed (87.5 MHz). Not a channel.</summary>
  public const long DisplayMinHz = 87_500_000;

  /// <summary>Upper edge of the band as displayed (108.0 MHz). Not a channel.</summary>
  public const long DisplayMaxHz = 108_000_000;

  /// <summary>Channel centres in ascending order, in Hz.</summary>
  public static IReadOnlyList<long> Channels { get; } = CreateChannels();

  private static long[] CreateChannels()
  {
    int count = (int)((LastChannelHz - FirstChannelHz) / ChannelSpacingHz) + 1;
    long[] channels = new long[count];
    for (int i = 0; i < count; i++)
    {
      channels[i] = FirstChannelHz + i * ChannelSpacingHz;
    }
    return channels;
  }
}
