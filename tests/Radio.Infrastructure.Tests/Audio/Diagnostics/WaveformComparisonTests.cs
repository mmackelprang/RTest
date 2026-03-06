using Radio.AudioAnalysis;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Diagnostics;

/// <summary>
/// Tests for <see cref="WaveformComparison"/> and <see cref="FrequencyAnalysis"/>.
/// Verifies cross-correlation offset detection, distortion event detection,
/// and THD measurement.
/// </summary>
public class WaveformComparisonTests
{
  [Fact]
  public void IdenticalSignals_ZeroError()
  {
    var tone = WavFileHelper.GenerateStereoSineWave(
      leftHz: 200, rightHz: 300, durationSamples: 48000);

    var report = WaveformComparison.Compare(tone, tone);

    Assert.True(report.IsClean, $"Expected clean, got: {report}");
    Assert.Equal(0f, report.RmsError);
    Assert.Equal(0f, report.PeakError);
    Assert.Equal(1.0f, report.GainRatio, 3);
    Assert.True(report.CorrelationCoefficient > 0.999f);
  }

  [Fact]
  public void TimeShiftedSignals_CorrectOffsetDetection()
  {
    var tone = WavFileHelper.GenerateStereoSineWave(
      leftHz: 200, rightHz: 300, durationSamples: 48000);

    // Shift by 100 samples
    var shifted = new float[tone.Length];
    Array.Copy(tone, 0, shifted, 100, tone.Length - 100);

    var (offset, correlation) = WaveformComparison.FindTimeOffset(tone, shifted, maxOffsetSamples: 200);

    Assert.InRange(offset, 95, 105); // Should detect ~100 sample offset
    Assert.True(correlation > 0.95f);
  }

  [Fact]
  public void GainScaledSignal_DetectsGainError()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: 48000, amplitude: 0.8f);

    // Apply 50% gain reduction
    var gained = new float[reference.Length];
    for (int i = 0; i < reference.Length; i++)
      gained[i] = reference[i] * 0.5f;

    var report = WaveformComparison.Compare(reference, gained);

    Assert.False(report.IsClean);
    Assert.Contains(report.Events, e => e.Type == DistortionType.GainError);
    Assert.InRange(report.GainRatio, 0.45f, 0.55f); // ~0.5 gain ratio
  }

  [Fact]
  public void SilenceInserted_DetectsSilenceInsertion()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: 48000, amplitude: 0.8f);

    // Copy and insert 100 samples of silence at position 10000
    var captured = (float[])reference.Clone();
    for (int i = 10000; i < 10100; i++)
      captured[i] = 0f;

    var report = WaveformComparison.Compare(reference, captured);

    Assert.False(report.IsClean);
    Assert.Contains(report.Events, e => e.Type == DistortionType.SilenceInsertion);
  }

  [Fact]
  public void RepeatedSamples_DetectsRepetition()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: 48000, amplitude: 0.8f);

    // Copy and repeat a sample value 50 times at position 5000
    var captured = (float[])reference.Clone();
    var repeatedVal = captured[5000];
    if (repeatedVal == 0f) repeatedVal = 0.42f; // Ensure non-zero for detection
    for (int i = 5000; i < 5050; i++)
      captured[i] = repeatedVal;

    var report = WaveformComparison.Compare(reference, captured);

    Assert.False(report.IsClean);
    Assert.Contains(report.Events, e => e.Type == DistortionType.RepeatedSamples);
  }

  [Fact]
  public void ChannelSwap_Detected()
  {
    // Generate stereo with very different L/R content
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 200, rightHz: 800, durationSamples: 48000, amplitude: 0.8f);

    // Swap channels
    var swapped = new float[reference.Length];
    for (int i = 0; i < reference.Length / 2; i++)
    {
      swapped[i * 2] = reference[i * 2 + 1]; // L = original R
      swapped[i * 2 + 1] = reference[i * 2]; // R = original L
    }

    var report = WaveformComparison.Compare(reference, swapped,
      options: new ComparisonOptions { Channels = 2 });

    Assert.Contains(report.Events, e => e.Type == DistortionType.ChannelSwap);
  }

  [Fact]
  public void WavRoundTrip_PreservesSignal()
  {
    var original = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, rightHz: 880, durationSamples: 4800);

    var tempFile = Path.Combine(Path.GetTempPath(), $"wav_roundtrip_{Guid.NewGuid()}.wav");
    try
    {
      WavFileHelper.WriteWavFile(tempFile, original);
      var readBack = WavFileHelper.ReadWavFile(tempFile, out var sr, out var ch);

      Assert.Equal(48000, sr);
      Assert.Equal(2, ch);
      Assert.Equal(original.Length, readBack.Length);

      // 16-bit quantization introduces small error — should be < 1/32768
      for (int i = 0; i < original.Length; i++)
      {
        var error = MathF.Abs(original[i] - readBack[i]);
        Assert.True(error < 0.001f, $"Sample {i}: expected {original[i]:F6}, got {readBack[i]:F6}");
      }
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Fact]
  public void SilenceDetector_FindsZeroRuns()
  {
    var samples = new float[1000];
    // Fill with non-zero
    for (int i = 0; i < samples.Length; i++)
      samples[i] = 0.5f;

    // Insert two zero runs
    for (int i = 100; i < 120; i++) samples[i] = 0f; // 20-sample run
    for (int i = 500; i < 510; i++) samples[i] = 0f; // 10-sample run

    var runs = SilenceDetector.FindZeroRuns(samples, minRunLength: 8);

    Assert.Equal(2, runs.Count);
    Assert.Equal(100, runs[0].Start);
    Assert.Equal(20, runs[0].Length);
    Assert.Equal(500, runs[1].Start);
    Assert.Equal(10, runs[1].Length);
  }

  [Fact]
  public void SilenceDetector_FindsRepeatedSampleRuns()
  {
    var samples = new float[1000];
    for (int i = 0; i < samples.Length; i++)
      samples[i] = MathF.Sin(2 * MathF.PI * 440f * i / 48000f) * 0.5f;

    // Insert a repeated sample run
    for (int i = 300; i < 320; i++)
      samples[i] = 0.42f;

    var runs = SilenceDetector.FindRepeatedSampleRuns(samples, minRunLength: 8);

    Assert.Single(runs);
    Assert.Equal(300, runs[0].Start);
    Assert.Equal(20, runs[0].Length);
  }

  [Fact]
  public void SilenceDetector_FindsClippingRuns()
  {
    var samples = new float[1000];
    for (int i = 0; i < samples.Length; i++)
      samples[i] = MathF.Sin(2 * MathF.PI * 440f * i / 48000f) * 0.5f;

    // Insert clipping
    for (int i = 200; i < 215; i++)
      samples[i] = 1.0f;

    var runs = SilenceDetector.FindClippingRuns(samples, threshold: 0.999f, minRunLength: 4);

    Assert.Single(runs);
    Assert.Equal(200, runs[0].Start);
    Assert.Equal(15, runs[0].Length);
  }

  [Fact]
  public void THD_PureSineWave_LowDistortion()
  {
    // Pure 440Hz sine, stereo
    var samples = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, rightHz: 440, durationSamples: 48000, amplitude: 0.8f);

    var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(
      samples, sampleRate: 48000, channels: 2, expectedFrequencyHz: 440);

    // Pure sine wave should have < 1% THD (any residual is numerical precision)
    Assert.True(thd < 1.0f, $"THD of pure sine should be < 1%, got {thd:F2}%");
  }

  [Fact]
  public void THD_ClippedSineWave_HigherDistortion()
  {
    // Generate a sine wave then hard-clip it
    var samples = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, rightHz: 440, durationSamples: 48000, amplitude: 0.8f);

    // Hard clip at 0.5 — creates significant harmonics
    for (int i = 0; i < samples.Length; i++)
      samples[i] = Math.Clamp(samples[i], -0.5f, 0.5f);

    var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(
      samples, sampleRate: 48000, channels: 2, expectedFrequencyHz: 440);

    // Clipped sine should have measurable THD (typically 10-40%)
    Assert.True(thd > 5.0f, $"THD of clipped sine should be > 5%, got {thd:F2}%");
  }

  [SkippableFact]
  [Trait("Category", "CaptureAnalysis")]
  public void AnalyzeBtCapture_InputVsOutput()
  {
    var captureDir = Path.Combine(
      AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
      "data", "diagnostics", "bt-capture-60s-postfix");

    var inputPath = Path.Combine(captureDir, "generator-input.wav");
    var outputPath = Path.Combine(captureDir, "generator-output.wav");
    var postModPath = Path.Combine(captureDir, "post-modifiers.wav");

    Skip.IfNot(File.Exists(inputPath), "No capture files found — run capture first");

    var input = WavFileHelper.ReadWavFile(inputPath, out var inSr, out var inCh);
    var output = WavFileHelper.ReadWavFile(outputPath, out var outSr, out var outCh);
    var postMod = WavFileHelper.ReadWavFile(postModPath, out var pmSr, out var pmCh);

    // === Basic stats ===
    var inRms = WavFileHelper.CalculateRms(input);
    var inPeak = WavFileHelper.CalculatePeak(input);
    var outRms = WavFileHelper.CalculateRms(output);
    var outPeak = WavFileHelper.CalculatePeak(output);
    var pmRms = WavFileHelper.CalculateRms(postMod);
    var pmPeak = WavFileHelper.CalculatePeak(postMod);

    // Log basic info
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("=== BT Capture Analysis (60s) ===");
    sb.AppendLine($"Generator Input:  {input.Length} samples, {inSr}Hz {inCh}ch, RMS={WavFileHelper.LinearToDb(inRms):F1}dB, Peak={WavFileHelper.LinearToDb(inPeak):F1}dB");
    sb.AppendLine($"Generator Output: {output.Length} samples, {outSr}Hz {outCh}ch, RMS={WavFileHelper.LinearToDb(outRms):F1}dB, Peak={WavFileHelper.LinearToDb(outPeak):F1}dB");
    sb.AppendLine($"Post-Modifiers:   {postMod.Length} samples, {pmSr}Hz {pmCh}ch, RMS={WavFileHelper.LinearToDb(pmRms):F1}dB, Peak={WavFileHelper.LinearToDb(pmPeak):F1}dB");
    sb.AppendLine($"Sample diff: input={input.Length}, output={output.Length}, delta={output.Length - input.Length}");

    // === Input vs Output comparison ===
    sb.AppendLine();
    sb.AppendLine("--- Generator Input vs Output ---");
    var ioReport = WaveformComparison.Compare(input, output);
    sb.AppendLine($"SNR: {ioReport.SnrDb:F1} dB");
    sb.AppendLine($"RMS Error: {ioReport.RmsError:F6}");
    sb.AppendLine($"Peak Error: {ioReport.PeakError:F6}");
    sb.AppendLine($"Gain Ratio: {ioReport.GainRatio:F4}");
    sb.AppendLine($"Correlation: {ioReport.CorrelationCoefficient:F6}");
    sb.AppendLine($"IsClean: {ioReport.IsClean}");
    sb.AppendLine($"Events: {ioReport.Events.Count}");
    foreach (var evt in ioReport.Events.Take(20))
      sb.AppendLine($"  [{evt.Type}] offset={evt.SampleOffset} len={evt.Duration} sev={evt.Severity:F2}: {evt.Description}");
    if (ioReport.Events.Count > 20)
      sb.AppendLine($"  ... and {ioReport.Events.Count - 20} more events");

    // === Output vs Post-Modifiers comparison ===
    sb.AppendLine();
    sb.AppendLine("--- Generator Output vs Post-Modifiers ---");
    var opReport = WaveformComparison.Compare(output, postMod);
    sb.AppendLine($"SNR: {opReport.SnrDb:F1} dB");
    sb.AppendLine($"RMS Error: {opReport.RmsError:F6}");
    sb.AppendLine($"Peak Error: {opReport.PeakError:F6}");
    sb.AppendLine($"Gain Ratio: {opReport.GainRatio:F4}");
    sb.AppendLine($"Correlation: {opReport.CorrelationCoefficient:F6}");
    sb.AppendLine($"IsClean: {opReport.IsClean}");
    sb.AppendLine($"Events: {opReport.Events.Count}");
    foreach (var evt in opReport.Events.Take(20))
      sb.AppendLine($"  [{evt.Type}] offset={evt.SampleOffset} len={evt.Duration} sev={evt.Severity:F2}: {evt.Description}");
    if (opReport.Events.Count > 20)
      sb.AppendLine($"  ... and {opReport.Events.Count - 20} more events");

    // === Silence/repeat/clipping scan on each stage independently ===
    sb.AppendLine();
    sb.AppendLine("--- Independent Stage Scans ---");
    foreach (var (name, samples) in new[] {
      ("generator-input", input), ("generator-output", output), ("post-modifiers", postMod) })
    {
      var zeros = SilenceDetector.FindZeroRuns(samples, minRunLength: 20);
      var repeats = SilenceDetector.FindRepeatedSampleRuns(samples, minRunLength: 10);
      var clips = SilenceDetector.FindClippingRuns(samples, 0.999f, minRunLength: 4);
      sb.AppendLine($"  {name}: {zeros.Count} silence runs, {repeats.Count} repeated runs, {clips.Count} clipping runs");
      foreach (var z in zeros.Take(5))
        sb.AppendLine($"    silence: offset={z.Start} len={z.Length}");
      foreach (var r in repeats.Take(5))
        sb.AppendLine($"    repeated: offset={r.Start} len={r.Length} val={samples[r.Start]:F6}");
      foreach (var c in clips.Take(5))
        sb.AppendLine($"    clipping: offset={c.Start} len={c.Length}");
    }

    // === Time offset detection (input vs output) ===
    sb.AppendLine();
    sb.AppendLine("--- Time Offset Detection ---");
    var minLen = Math.Min(input.Length, output.Length);
    if (minLen > 9600)
    {
      var (offset, corr) = WaveformComparison.FindTimeOffset(
        input.AsSpan(0, Math.Min(minLen, 480000)),
        output.AsSpan(0, Math.Min(minLen, 480000)),
        maxOffsetSamples: 4800);
      sb.AppendLine($"Input→Output offset: {offset} samples ({offset / 96.0:F1} ms), correlation: {corr:F4}");
    }

    // === Silence gap deep analysis (channel alignment) ===
    sb.AppendLine();
    sb.AppendLine("--- Silence Gap Deep Analysis (generator-input) ---");
    var inputZeros = SilenceDetector.FindZeroRuns(input, minRunLength: 20);
    foreach (var (start, length) in inputZeros)
    {
      var isEven = length % 2 == 0;
      sb.AppendLine($"  Gap at {start}, length={length} ({(isEven ? "EVEN — frame-aligned" : "ODD — MISALIGNED!")})");
      sb.AppendLine($"    Duration: {length / 96.0:F2} ms ({length / 2} frames)");

      // Show samples around the gap
      var before = Math.Max(0, start - 6);
      var after = Math.Min(input.Length, start + length + 6);
      sb.Append("    Before: ");
      for (int i = before; i < start; i++)
        sb.Append($"{input[i]:F4} ");
      sb.AppendLine();
      sb.Append("    Gap start: ");
      for (int i = start; i < Math.Min(start + 6, start + length); i++)
        sb.Append($"{input[i]:F4} ");
      sb.AppendLine("...");
      sb.Append("    Gap end:   ...");
      for (int i = Math.Max(start, start + length - 6); i < start + length; i++)
        sb.Append($"{input[i]:F4} ");
      sb.AppendLine();
      sb.Append("    After:  ");
      for (int i = start + length; i < after; i++)
        sb.Append($"{input[i]:F4} ");
      sb.AppendLine();

      // Check channel swap after gap: if odd gap, L/R may be swapped
      if (!isEven && start + length + 20 < input.Length && start >= 20)
      {
        // Compare L/R energy pattern before and after gap
        float preL = 0, preR = 0, postL = 0, postR = 0;
        for (int i = 0; i < 10; i++)
        {
          preL += MathF.Abs(input[start - 20 + i * 2]);
          preR += MathF.Abs(input[start - 20 + i * 2 + 1]);
          postL += MathF.Abs(input[start + length + i * 2]);
          postR += MathF.Abs(input[start + length + i * 2 + 1]);
        }
        sb.AppendLine($"    Pre-gap  L avg={preL / 10:F4}, R avg={preR / 10:F4}");
        sb.AppendLine($"    Post-gap L avg={postL / 10:F4}, R avg={postR / 10:F4}");
        sb.AppendLine($"    Channel swap indicator: pre L/R ratio={(preL > 0 ? preR / preL : 0):F3}, post L/R ratio={(postL > 0 ? postR / postL : 0):F3}");
      }
    }

    // === Windowed correlation analysis (find where distortion occurs) ===
    sb.AppendLine();
    sb.AppendLine("--- Windowed Correlation (input vs output, 1s windows) ---");
    var windowSize = 96000; // 1 second stereo
    var ioMinLen = Math.Min(input.Length, output.Length);
    for (int w = 0; w + windowSize <= ioMinLen; w += windowSize)
    {
      var refWindow = input.AsSpan(w, windowSize);
      var capWindow = output.AsSpan(w, windowSize);
      double cc = 0, re = 0, ce = 0;
      for (int i = 0; i < windowSize; i++)
      {
        cc += refWindow[i] * capWindow[i];
        re += refWindow[i] * refWindow[i];
        ce += capWindow[i] * capWindow[i];
      }
      var denom = Math.Sqrt(re * ce);
      var corr = denom > 0 ? cc / denom : 0;
      var windowSec = w / 96000.0;
      var marker = corr < 0.9 ? " <<<" : "";
      sb.AppendLine($"  t={windowSec:F0}s: corr={corr:F4}{marker}");
    }

    // === Sample-level diff analysis: find largest error regions ===
    sb.AppendLine();
    sb.AppendLine("--- Largest Error Regions (input vs output) ---");
    var errorWindowSize = 4800; // 50ms windows
    var topErrors = new List<(int offset, float rmsError)>();
    for (int w = 0; w + errorWindowSize <= ioMinLen; w += errorWindowSize)
    {
      double errSum = 0;
      for (int i = w; i < w + errorWindowSize; i++)
      {
        var e = input[i] - output[i];
        errSum += e * e;
      }
      topErrors.Add((w, (float)Math.Sqrt(errSum / errorWindowSize)));
    }
    topErrors.Sort((a, b) => b.rmsError.CompareTo(a.rmsError));
    foreach (var (offset, rmsError) in topErrors.Take(10))
    {
      var timeSec = offset / 96000.0;
      sb.AppendLine($"  t={timeSec:F2}s (offset {offset}): RMS error={rmsError:F4} ({WavFileHelper.LinearToDb(rmsError):F1} dB)");
    }

    // Write results to file for easy viewing
    var resultsPath = Path.Combine(captureDir, "analysis-results.txt");
    File.WriteAllText(resultsPath, sb.ToString());

    // Also output to test runner
    Assert.True(true, sb.ToString()); // Always passes — this is a diagnostic test
  }
}
