using Radio.AudioAnalysis;

namespace Radio.AudioAnalysis.Tests;

public class WavFileHelperTests
{
  [Fact]
  public void GenerateMonoSineWave_ReturnsCorrectLength()
  {
    var samples = WavFileHelper.GenerateMonoSineWave(440, 48000, 4800);
    Assert.Equal(4800, samples.Length);
  }

  [Fact]
  public void GenerateStereoSineWave_ReturnsCorrectLength()
  {
    var samples = WavFileHelper.GenerateStereoSineWave(200, 300, 48000, 4800);
    Assert.Equal(9600, samples.Length); // 4800 * 2 channels
  }

  [Fact]
  public void GenerateMonoSineWave_PeakMatchesAmplitude()
  {
    var samples = WavFileHelper.GenerateMonoSineWave(440, 48000, 48000, 0.5f);
    var peak = WavFileHelper.CalculatePeak(samples);
    Assert.InRange(peak, 0.49f, 0.51f);
  }

  [Fact]
  public void GenerateStereoSineWave_ChannelsAreDifferent()
  {
    var samples = WavFileHelper.GenerateStereoSineWave(200, 300, 48000, 48000);

    // Extract left and right channels
    var left = new float[48000];
    var right = new float[48000];
    for (int i = 0; i < 48000; i++)
    {
      left[i] = samples[i * 2];
      right[i] = samples[i * 2 + 1];
    }

    // They should have similar RMS but different waveforms
    var leftRms = WavFileHelper.CalculateRms(left);
    var rightRms = WavFileHelper.CalculateRms(right);
    Assert.InRange(leftRms, 0.5f, 0.6f);
    Assert.InRange(rightRms, 0.5f, 0.6f);

    // Cross-correlation should be low (different frequencies)
    double crossCorr = 0;
    for (int i = 0; i < left.Length; i++)
    {
      crossCorr += left[i] * right[i];
    }
    crossCorr /= left.Length;
    Assert.InRange(Math.Abs(crossCorr), 0, 0.1);
  }

  [Fact]
  public void WriteAndReadWavFile_RoundTrips()
  {
    var tempPath = Path.Combine(Path.GetTempPath(), $"wav_test_{Guid.NewGuid():N}.wav");
    try
    {
      var original = WavFileHelper.GenerateStereoSineWave(440, 880, 48000, 4800, 0.5f);
      WavFileHelper.WriteWavFile(tempPath, original, 48000, 2);

      var loaded = WavFileHelper.ReadWavFile(tempPath, out var sampleRate, out var channels);

      Assert.Equal(48000, sampleRate);
      Assert.Equal(2, channels);
      Assert.Equal(original.Length, loaded.Length);

      // 16-bit quantization introduces some error
      for (int i = 0; i < original.Length; i++)
      {
        Assert.InRange(loaded[i] - original[i], -0.001f, 0.001f);
      }
    }
    finally
    {
      File.Delete(tempPath);
    }
  }

  [Fact]
  public void ReadWavFile_ThrowsOnInvalidFile()
  {
    var tempPath = Path.Combine(Path.GetTempPath(), $"bad_wav_{Guid.NewGuid():N}.wav");
    try
    {
      File.WriteAllText(tempPath, "not a wav file");
      Assert.Throws<InvalidDataException>(() =>
        WavFileHelper.ReadWavFile(tempPath, out _, out _));
    }
    finally
    {
      File.Delete(tempPath);
    }
  }

  [Fact]
  public void CalculateRms_ReturnsZeroForEmptySpan()
  {
    Assert.Equal(0f, WavFileHelper.CalculateRms(ReadOnlySpan<float>.Empty));
  }

  [Fact]
  public void CalculateRms_CorrectForKnownSignal()
  {
    // RMS of a full-cycle sine wave of amplitude A = A / sqrt(2)
    var samples = WavFileHelper.GenerateMonoSineWave(100, 48000, 48000, 1.0f);
    var rms = WavFileHelper.CalculateRms(samples);
    Assert.InRange(rms, 0.707f - 0.01f, 0.707f + 0.01f);
  }

  [Fact]
  public void CalculatePeak_ReturnsZeroForSilence()
  {
    var silence = new float[100];
    Assert.Equal(0f, WavFileHelper.CalculatePeak(silence));
  }

  [Fact]
  public void LinearToDb_KnownValues()
  {
    Assert.Equal(0f, WavFileHelper.LinearToDb(1.0f), 1);
    Assert.InRange(WavFileHelper.LinearToDb(0.5f), -6.1f, -6.0f);
    Assert.Equal(float.NegativeInfinity, WavFileHelper.LinearToDb(0f));
  }
}
