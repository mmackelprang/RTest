namespace RTLSDRCore.Sweep;

/// <summary>One retune of a band sweep and the channels measured from it.</summary>
/// <param name="CentreHz">Frequency the tuner is set to, in Hz.</param>
/// <param name="ChannelHz">
/// Channel centres measured from this tune's capture, ascending, in Hz. Each channel's offset
/// from the tuned centre is <c>channel − <paramref name="CentreHz"/></c>.
/// </param>
public sealed record SweepTune(long CentreHz, IReadOnlyList<long> ChannelHz);

/// <summary>
/// How one band is swept (AUD-91): which channels, which retunes measure them, and the
/// per-channel measurement window. Immutable. Build one with <see cref="BandSweepPlans.For"/>.
/// </summary>
public sealed class BandSweepPlan
{
  /// <summary>Creates a plan.</summary>
  /// <param name="band">Band code, e.g. <c>"FM"</c>, <c>"WB"</c>.</param>
  /// <param name="tunes">Retunes in sweep order. Their channels, concatenated, must be strictly ascending.</param>
  /// <param name="channelSpacingHz">Spacing of the channel grid, in Hz.</param>
  /// <param name="displayMinHz">Lower edge of the frequency axis a view of this band draws, in Hz.</param>
  /// <param name="displayMaxHz">Upper edge of the frequency axis a view of this band draws, in Hz.</param>
  /// <param name="halfWindowHz">Each channel is measured over bins within this distance of its centre.</param>
  /// <param name="dcExcludeHz">Bins within this distance of the tuned centre are never measured.</param>
  /// <param name="sampleRate">Capture rate the tune grouping was designed for, in samples per second.</param>
  /// <exception cref="ArgumentException">The tunes are empty, a tune has no channels, or the channels are not strictly ascending.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A numeric argument is not positive, or the display range is inverted.</exception>
  public BandSweepPlan(
    string band,
    IReadOnlyList<SweepTune> tunes,
    long channelSpacingHz,
    long displayMinHz,
    long displayMaxHz,
    int halfWindowHz,
    int dcExcludeHz,
    int sampleRate)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(band);
    ArgumentNullException.ThrowIfNull(tunes);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelSpacingHz);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfWindowHz);
    ArgumentOutOfRangeException.ThrowIfNegative(dcExcludeHz);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(displayMinHz, displayMaxHz);
    if (tunes.Count == 0)
    {
      throw new ArgumentException("A plan needs at least one tune.", nameof(tunes));
    }

    SweepTune[] tuneCopy = new SweepTune[tunes.Count];
    List<long> channels = new();
    for (int t = 0; t < tunes.Count; t++)
    {
      SweepTune tune = tunes[t] ?? throw new ArgumentException("Tunes must not be null.", nameof(tunes));
      if (tune.ChannelHz == null || tune.ChannelHz.Count == 0)
      {
        throw new ArgumentException($"Tune {t} has no channels.", nameof(tunes));
      }

      long[] tuneChannels = tune.ChannelHz.ToArray();
      foreach (long hz in tuneChannels)
      {
        if (channels.Count > 0 && hz <= channels[^1])
        {
          throw new ArgumentException("Channels across the tunes must be strictly ascending.", nameof(tunes));
        }
        channels.Add(hz);
      }
      tuneCopy[t] = new SweepTune(tune.CentreHz, tuneChannels);
    }

    Band = band;
    Tunes = tuneCopy;
    Channels = channels.ToArray();
    ChannelSpacingHz = channelSpacingHz;
    DisplayMinHz = displayMinHz;
    DisplayMaxHz = displayMaxHz;
    HalfWindowHz = halfWindowHz;
    DcExcludeHz = dcExcludeHz;
    SampleRate = sampleRate;
  }

  /// <summary>Band code: <c>"FM"</c>, <c>"WB"</c>, <c>"AIR"</c> or <c>"VHF"</c> for the built-in plans.</summary>
  public string Band { get; }

  /// <summary>Every channel centre in the plan, ascending, in Hz.</summary>
  public IReadOnlyList<long> Channels { get; }

  /// <summary>Spacing of the channel grid, in Hz.</summary>
  public long ChannelSpacingHz { get; }

  /// <summary>Lower edge of the frequency axis a view of this band draws, in Hz.</summary>
  public long DisplayMinHz { get; }

  /// <summary>Upper edge of the frequency axis a view of this band draws, in Hz.</summary>
  public long DisplayMaxHz { get; }

  /// <summary>Retunes in sweep order.</summary>
  public IReadOnlyList<SweepTune> Tunes { get; }

  /// <summary>Each channel is measured over FFT bins within this distance of its centre, in Hz.</summary>
  public int HalfWindowHz { get; }

  /// <summary>FFT bins within this distance of the tuned centre are never measured, in Hz.</summary>
  public int DcExcludeHz { get; }

  /// <summary>Capture rate the tune grouping was designed for; a sweep refuses a lower rate.</summary>
  public int SampleRate { get; }
}
