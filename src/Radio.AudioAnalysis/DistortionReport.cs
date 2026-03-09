namespace Radio.AudioAnalysis;

/// <summary>
/// Per-channel analysis results.
/// </summary>
public class ChannelAnalysis
{
  /// <summary>RMS level of the channel (0.0 to 1.0).</summary>
  public float RmsLevel { get; init; }

  /// <summary>Peak sample value in the channel.</summary>
  public float PeakLevel { get; init; }

  /// <summary>RMS error vs reference for this channel.</summary>
  public float RmsError { get; init; }
}

/// <summary>
/// Complete report from comparing reference and captured audio waveforms.
/// </summary>
public class DistortionReport
{
  /// <summary>Whether the captured audio is considered clean (no distortion above thresholds).</summary>
  public bool IsClean => Events.Count == 0;

  /// <summary>Signal-to-noise ratio in dB (higher = cleaner).</summary>
  public float SnrDb { get; init; }

  /// <summary>Total Harmonic Distortion percentage (lower = cleaner).</summary>
  public float ThdPercent { get; init; }

  /// <summary>RMS error between reference and captured signals.</summary>
  public float RmsError { get; init; }

  /// <summary>Peak sample error between reference and captured signals.</summary>
  public float PeakError { get; init; }

  /// <summary>Gain ratio (captured RMS / reference RMS). 1.0 = no gain change.</summary>
  public float GainRatio { get; init; }

  /// <summary>Time offset in samples found by cross-correlation alignment.</summary>
  public int TimeOffsetSamples { get; init; }

  /// <summary>Cross-correlation coefficient at best alignment (0.0 to 1.0).</summary>
  public float CorrelationCoefficient { get; init; }

  /// <summary>Per-channel analysis results.</summary>
  public List<ChannelAnalysis> ChannelResults { get; init; } = new();

  /// <summary>All detected distortion events.</summary>
  public List<DistortionEvent> Events { get; init; } = new();

  /// <summary>Total number of samples compared.</summary>
  public int TotalSamplesCompared { get; init; }

  public override string ToString()
  {
    var status = IsClean ? "CLEAN" : $"DISTORTED ({Events.Count} events)";
    return $"[{status}] SNR={SnrDb:F1}dB, THD={ThdPercent:F2}%, " +
           $"RMS Error={RmsError:F6}, Peak Error={PeakError:F6}, " +
           $"Gain={GainRatio:F4}, Offset={TimeOffsetSamples} samples, " +
           $"Correlation={CorrelationCoefficient:F4}";
  }
}
