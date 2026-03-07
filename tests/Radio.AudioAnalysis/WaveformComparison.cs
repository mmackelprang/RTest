namespace Radio.AudioAnalysis;

/// <summary>
/// Compares reference and captured audio waveforms to detect distortion.
/// Uses cross-correlation for time alignment and sample-by-sample diff for analysis.
/// </summary>
public static class WaveformComparison
{
  /// <summary>
  /// Finds the time offset between reference and captured signals using cross-correlation.
  /// Returns the offset in samples that maximizes correlation (positive = captured is delayed).
  /// </summary>
  /// <param name="reference">Reference audio samples.</param>
  /// <param name="captured">Captured audio samples.</param>
  /// <param name="maxOffsetSamples">Maximum offset to search in either direction.</param>
  /// <returns>
  /// Tuple of (offsetSamples, correlationCoefficient).
  /// Positive offset means captured is delayed relative to reference.
  /// </returns>
  public static (int Offset, float Correlation) FindTimeOffset(
    ReadOnlySpan<float> reference, ReadOnlySpan<float> captured, int maxOffsetSamples = 4800)
  {
    var minLen = Math.Min(reference.Length, captured.Length);
    if (minLen == 0)
    {
      return (0, 0f);
    }

    // Limit search range to avoid exceeding buffer bounds
    maxOffsetSamples = Math.Min(maxOffsetSamples, minLen / 2);

    float bestCorrelation = float.MinValue;
    int bestOffset = 0;

    // Compute reference energy once
    double refEnergy = 0;
    for (int i = 0; i < minLen - maxOffsetSamples; i++)
    {
      refEnergy += reference[i] * reference[i];
    }

    for (int offset = -maxOffsetSamples; offset <= maxOffsetSamples; offset++)
    {
      double crossCorr = 0;
      double capEnergy = 0;
      int compareLen = minLen - Math.Abs(offset);

      int refStart = offset > 0 ? 0 : -offset;
      int capStart = offset > 0 ? offset : 0;
      int count = Math.Min(compareLen, minLen - Math.Max(refStart, capStart));

      for (int i = 0; i < count; i++)
      {
        var r = reference[refStart + i];
        var c = captured[capStart + i];
        crossCorr += r * c;
        capEnergy += c * c;
      }

      var denominator = Math.Sqrt(refEnergy * capEnergy);
      var correlation = denominator > 0 ? (float)(crossCorr / denominator) : 0f;

      if (correlation > bestCorrelation)
      {
        bestCorrelation = correlation;
        bestOffset = offset;
      }
    }

    return (bestOffset, bestCorrelation);
  }

  /// <summary>
  /// Compares reference and captured audio after time alignment.
  /// Produces a detailed distortion report with per-sample analysis.
  /// </summary>
  /// <param name="reference">Reference audio samples.</param>
  /// <param name="captured">Captured audio samples.</param>
  /// <param name="timeOffsetSamples">Time offset to apply (from FindTimeOffset).</param>
  /// <param name="options">Comparison thresholds and parameters.</param>
  /// <returns>Distortion report with detected events.</returns>
  public static DistortionReport Compare(float[] reference, float[] captured,
    int timeOffsetSamples = 0, ComparisonOptions? options = null)
  {
    options ??= new ComparisonOptions();

    // Align the signals
    int refStart = timeOffsetSamples > 0 ? 0 : -timeOffsetSamples;
    int capStart = timeOffsetSamples > 0 ? timeOffsetSamples : 0;
    int compareLen = Math.Min(reference.Length - refStart, captured.Length - capStart);

    if (compareLen <= 0)
    {
      return new DistortionReport
      {
        SnrDb = float.NegativeInfinity,
        RmsError = 1.0f,
        PeakError = 1.0f,
        GainRatio = 0f,
        TimeOffsetSamples = timeOffsetSamples,
        CorrelationCoefficient = 0f,
        TotalSamplesCompared = 0
      };
    }

    var refSpan = reference.AsSpan(refStart, compareLen);
    var capSpan = captured.AsSpan(capStart, compareLen);

    // Compute RMS levels
    var refRms = WavFileHelper.CalculateRms(refSpan);
    var capRms = WavFileHelper.CalculateRms(capSpan);
    var gainRatio = refRms > 0 ? capRms / refRms : 0f;

    // Compute error signal
    double sumErrorSq = 0;
    float maxError = 0f;
    double sumSignalSq = 0;

    for (int i = 0; i < compareLen; i++)
    {
      var error = capSpan[i] - refSpan[i];
      sumErrorSq += error * error;
      sumSignalSq += refSpan[i] * refSpan[i];

      var absError = MathF.Abs(error);
      if (absError > maxError)
      {
        maxError = absError;
      }
    }

    var rmsError = (float)Math.Sqrt(sumErrorSq / compareLen);
    var snrDb = sumErrorSq > 0
      ? (float)(10 * Math.Log10(sumSignalSq / sumErrorSq))
      : float.PositiveInfinity;

    // Detect distortion events
    var events = new List<DistortionEvent>();

    // Silence insertions in captured signal
    var zeroRuns = SilenceDetector.FindZeroRuns(capSpan, options.MinSilenceRunLength);
    foreach (var (start, length) in zeroRuns)
    {
      // Check if reference is also silent at this position — if so, it's intentional
      bool refSilent = true;
      for (int i = start; i < start + length && i < refSpan.Length; i++)
      {
        if (refSpan[i] != 0f) { refSilent = false; break; }
      }

      if (!refSilent)
      {
        events.Add(new DistortionEvent(
          DistortionType.SilenceInsertion,
          capStart + start,
          length,
          Math.Min(1f, length / 480f), // Severity scales with duration
          $"Silence insertion: {length} zero samples at offset {capStart + start}"));
      }
    }

    // Repeated samples in captured signal
    var repeatedRuns = SilenceDetector.FindRepeatedSampleRuns(capSpan, options.MinRepeatedRunLength);
    foreach (var (start, length) in repeatedRuns)
    {
      events.Add(new DistortionEvent(
        DistortionType.RepeatedSamples,
        capStart + start,
        length,
        Math.Min(1f, length / 480f),
        $"Repeated samples: value {capSpan[start]:F6} repeated {length} times at offset {capStart + start}"));
    }

    // Clipping in captured signal
    var clippingRuns = SilenceDetector.FindClippingRuns(
      capSpan, options.ClippingThreshold, options.MinClippingRunLength);
    foreach (var (start, length) in clippingRuns)
    {
      events.Add(new DistortionEvent(
        DistortionType.AmplitudeClipping,
        capStart + start,
        length,
        Math.Min(1f, length / 100f),
        $"Amplitude clipping: {length} clipped samples at offset {capStart + start}"));
    }

    // Gain error
    if (MathF.Abs(gainRatio - 1.0f) > options.MaxGainDeviation && refRms > 0.001f)
    {
      events.Add(new DistortionEvent(
        DistortionType.GainError,
        0,
        compareLen,
        Math.Min(1f, MathF.Abs(gainRatio - 1.0f) / 0.5f),
        $"Gain error: ratio {gainRatio:F4} (expected ~1.0, deviation {MathF.Abs(gainRatio - 1.0f):F4})"));
    }

    // Channel swap detection (for stereo)
    var channelResults = new List<ChannelAnalysis>();
    if (options.Channels == 2 && compareLen >= 4)
    {
      var channelSwapped = DetectChannelSwap(refSpan, capSpan);
      if (channelSwapped)
      {
        events.Add(new DistortionEvent(
          DistortionType.ChannelSwap,
          0,
          compareLen,
          1.0f,
          "Left and right channels appear swapped"));
      }

      // Per-channel analysis
      for (int ch = 0; ch < 2; ch++)
      {
        var chRefRms = CalculateChannelRms(refSpan, ch, 2);
        var chCapRms = CalculateChannelRms(capSpan, ch, 2);
        var chCapPeak = CalculateChannelPeak(capSpan, ch, 2);
        var chError = CalculateChannelRmsError(refSpan, capSpan, ch, 2);

        channelResults.Add(new ChannelAnalysis
        {
          RmsLevel = chCapRms,
          PeakLevel = chCapPeak,
          RmsError = chError
        });
      }
    }

    // Compute correlation at the applied offset for the report
    float correlation = 0f;
    if (compareLen > 0)
    {
      double crossCorr = 0, refE = 0, capE = 0;
      for (int i = 0; i < compareLen; i++)
      {
        crossCorr += refSpan[i] * capSpan[i];
        refE += refSpan[i] * refSpan[i];
        capE += capSpan[i] * capSpan[i];
      }
      var denom = Math.Sqrt(refE * capE);
      correlation = denom > 0 ? (float)(crossCorr / denom) : 0f;
    }

    return new DistortionReport
    {
      SnrDb = snrDb,
      ThdPercent = 0f, // THD requires FrequencyAnalysis — set externally if needed
      RmsError = rmsError,
      PeakError = maxError,
      GainRatio = gainRatio,
      TimeOffsetSamples = timeOffsetSamples,
      CorrelationCoefficient = correlation,
      ChannelResults = channelResults,
      Events = events,
      TotalSamplesCompared = compareLen
    };
  }

  private static bool DetectChannelSwap(ReadOnlySpan<float> reference, ReadOnlySpan<float> captured)
  {
    // Compare per-channel cross-correlation:
    // Normal: corr(refL, capL) + corr(refR, capR)
    // Swapped: corr(refL, capR) + corr(refR, capL)
    int frames = Math.Min(reference.Length, captured.Length) / 2;
    if (frames < 100)
    {
      return false;
    }

    double refL_capL = 0, refR_capR = 0;  // Normal alignment
    double refL_capR = 0, refR_capL = 0;  // Swapped alignment
    double refLSq = 0, refRSq = 0, capLSq = 0, capRSq = 0;

    for (int i = 0; i < frames; i++)
    {
      var rL = reference[i * 2];
      var rR = reference[i * 2 + 1];
      var cL = captured[i * 2];
      var cR = captured[i * 2 + 1];

      refL_capL += rL * cL;
      refR_capR += rR * cR;
      refL_capR += rL * cR;
      refR_capL += rR * cL;
      refLSq += rL * rL;
      refRSq += rR * rR;
      capLSq += cL * cL;
      capRSq += cR * cR;
    }

    if (refLSq < 0.001 || refRSq < 0.001 || capLSq < 0.001 || capRSq < 0.001)
    {
      return false;
    }

    // Compute normalized correlations for each channel pair
    var normLL = refL_capL / Math.Sqrt(refLSq * capLSq);
    var normRR = refR_capR / Math.Sqrt(refRSq * capRSq);
    var normLR = refL_capR / Math.Sqrt(refLSq * capRSq);
    var normRL = refR_capL / Math.Sqrt(refRSq * capLSq);

    // Channels must be distinguishable — if L and R are identical, can't detect swap
    // Check by seeing if refL correlates differently with capL vs capR
    var normalScore = normLL + normRR;
    var swappedScore = normLR + normRL;

    // Swap detected when swapped alignment has significantly higher correlation
    return swappedScore > normalScore + 0.5;
  }

  private static float CalculateChannelRms(ReadOnlySpan<float> samples, int channel, int channels)
  {
    double sumSq = 0;
    int count = 0;
    for (int i = channel; i < samples.Length; i += channels)
    {
      sumSq += samples[i] * samples[i];
      count++;
    }
    return count > 0 ? (float)Math.Sqrt(sumSq / count) : 0f;
  }

  private static float CalculateChannelPeak(ReadOnlySpan<float> samples, int channel, int channels)
  {
    float peak = 0f;
    for (int i = channel; i < samples.Length; i += channels)
    {
      var abs = MathF.Abs(samples[i]);
      if (abs > peak)
      {
        peak = abs;
      }
    }
    return peak;
  }

  private static float CalculateChannelRmsError(
    ReadOnlySpan<float> reference, ReadOnlySpan<float> captured, int channel, int channels)
  {
    double sumSq = 0;
    int count = 0;
    int len = Math.Min(reference.Length, captured.Length);
    for (int i = channel; i < len; i += channels)
    {
      var diff = captured[i] - reference[i];
      sumSq += diff * diff;
      count++;
    }
    return count > 0 ? (float)Math.Sqrt(sumSq / count) : 0f;
  }
}
