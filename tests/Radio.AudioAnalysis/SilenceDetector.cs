namespace Radio.AudioAnalysis;

/// <summary>
/// Detects silence, repeated samples, and clipping in audio buffers.
/// Extracted from FmAudioDropoutDiagnosticTests for reuse.
/// </summary>
public static class SilenceDetector
{
  /// <summary>
  /// Finds contiguous runs of zero-valued samples (silence insertions / dropouts).
  /// </summary>
  /// <param name="samples">Interleaved audio samples.</param>
  /// <param name="minRunLength">Minimum consecutive zeros to report.</param>
  /// <returns>List of (startIndex, length) tuples for each zero run.</returns>
  public static List<(int Start, int Length)> FindZeroRuns(ReadOnlySpan<float> samples, int minRunLength = 4)
  {
    var runs = new List<(int Start, int Length)>();
    int runStart = -1;
    int runLength = 0;

    for (int i = 0; i < samples.Length; i++)
    {
      if (samples[i] == 0f)
      {
        if (runStart < 0) runStart = i;
        runLength++;
      }
      else
      {
        if (runLength >= minRunLength)
          runs.Add((runStart, runLength));
        runStart = -1;
        runLength = 0;
      }
    }

    if (runLength >= minRunLength)
      runs.Add((runStart, runLength));

    return runs;
  }

  /// <summary>
  /// Finds contiguous runs of repeated (identical) sample values.
  /// Indicates buffer stall or stuck read pointer.
  /// </summary>
  /// <param name="samples">Interleaved audio samples.</param>
  /// <param name="minRunLength">Minimum consecutive identical samples to report.</param>
  /// <returns>List of (startIndex, length) tuples for each repeated run.</returns>
  public static List<(int Start, int Length)> FindRepeatedSampleRuns(
    ReadOnlySpan<float> samples, int minRunLength = 8)
  {
    var runs = new List<(int Start, int Length)>();
    if (samples.Length < 2) return runs;

    int runStart = 0;
    int runLength = 1;

    for (int i = 1; i < samples.Length; i++)
    {
      if (samples[i] == samples[i - 1] && samples[i] != 0f)
      {
        runLength++;
      }
      else
      {
        if (runLength >= minRunLength)
          runs.Add((runStart, runLength));
        runStart = i;
        runLength = 1;
      }
    }

    if (runLength >= minRunLength)
      runs.Add((runStart, runLength));

    return runs;
  }

  /// <summary>
  /// Finds contiguous runs of clipped samples (at or near +/-1.0).
  /// </summary>
  /// <param name="samples">Interleaved audio samples.</param>
  /// <param name="threshold">Absolute value threshold for clipping detection.</param>
  /// <param name="minRunLength">Minimum consecutive clipped samples to report.</param>
  /// <returns>List of (startIndex, length) tuples for each clipping run.</returns>
  public static List<(int Start, int Length)> FindClippingRuns(
    ReadOnlySpan<float> samples, float threshold = 0.999f, int minRunLength = 4)
  {
    var runs = new List<(int Start, int Length)>();
    int runStart = -1;
    int runLength = 0;

    for (int i = 0; i < samples.Length; i++)
    {
      if (MathF.Abs(samples[i]) >= threshold)
      {
        if (runStart < 0) runStart = i;
        runLength++;
      }
      else
      {
        if (runLength >= minRunLength)
          runs.Add((runStart, runLength));
        runStart = -1;
        runLength = 0;
      }
    }

    if (runLength >= minRunLength)
      runs.Add((runStart, runLength));

    return runs;
  }
}
