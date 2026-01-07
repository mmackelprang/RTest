namespace Radio.IntegrationTests.TestSupport;

/// <summary>
/// Generates test audio files (WAV format) for integration tests.
/// </summary>
public static class TestAudioFileGenerator
{
  /// <summary>
  /// Creates a WAV file with a sine wave of the specified duration.
  /// </summary>
  public static string CreateSineWaveFile(
    string directory,
    string fileName,
    TimeSpan duration,
    int sampleRate = 48000,
    int channels = 2,
    int bitsPerSample = 16,
    double frequency = 440.0)
  {
    var samples = GenerateSineWaveSamples(duration, sampleRate, channels, frequency);
    var filePath = Path.Combine(directory, fileName);
    WriteWavFile(filePath, samples, sampleRate, channels, bitsPerSample);
    return filePath;
  }

  /// <summary>
  /// Creates a WAV file with silence of the specified duration.
  /// </summary>
  public static string CreateSilenceFile(
    string directory,
    string fileName,
    TimeSpan duration,
    int sampleRate = 48000,
    int channels = 2,
    int bitsPerSample = 16)
  {
    var sampleCount = (int)(duration.TotalSeconds * sampleRate * channels);
    var samples = new float[sampleCount];
    var filePath = Path.Combine(directory, fileName);
    WriteWavFile(filePath, samples, sampleRate, channels, bitsPerSample);
    return filePath;
  }

  /// <summary>
  /// Creates a WAV file with white noise of the specified duration.
  /// </summary>
  public static string CreateWhiteNoiseFile(
    string directory,
    string fileName,
    TimeSpan duration,
    int sampleRate = 48000,
    int channels = 2,
    int bitsPerSample = 16)
  {
    var samples = GenerateWhiteNoiseSamples(duration, sampleRate, channels);
    var filePath = Path.Combine(directory, fileName);
    WriteWavFile(filePath, samples, sampleRate, channels, bitsPerSample);
    return filePath;
  }

  /// <summary>
  /// Creates a WAV file with a chirp (frequency sweep) of the specified duration.
  /// </summary>
  public static string CreateChirpFile(
    string directory,
    string fileName,
    TimeSpan duration,
    double startFrequency = 100.0,
    double endFrequency = 5000.0,
    int sampleRate = 48000,
    int channels = 2,
    int bitsPerSample = 16)
  {
    var samples = GenerateChirpSamples(duration, sampleRate, channels, startFrequency, endFrequency);
    var filePath = Path.Combine(directory, fileName);
    WriteWavFile(filePath, samples, sampleRate, channels, bitsPerSample);
    return filePath;
  }

  private static float[] GenerateSineWaveSamples(
    TimeSpan duration,
    int sampleRate,
    int channels,
    double frequency)
  {
    var sampleCount = (int)(duration.TotalSeconds * sampleRate * channels);
    var samples = new float[sampleCount];
    var samplesPerChannel = sampleCount / channels;

    for (int i = 0; i < samplesPerChannel; i++)
    {
      var value = (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
      for (int ch = 0; ch < channels; ch++)
      {
        samples[i * channels + ch] = value * 0.8f;
      }
    }

    return samples;
  }

  private static float[] GenerateWhiteNoiseSamples(
    TimeSpan duration,
    int sampleRate,
    int channels)
  {
    var random = new Random(42); // Deterministic for reproducible tests
    var sampleCount = (int)(duration.TotalSeconds * sampleRate * channels);
    var samples = new float[sampleCount];

    for (int i = 0; i < sampleCount; i++)
    {
      samples[i] = (float)(random.NextDouble() * 2 - 1) * 0.5f;
    }

    return samples;
  }

  private static float[] GenerateChirpSamples(
    TimeSpan duration,
    int sampleRate,
    int channels,
    double startFrequency,
    double endFrequency)
  {
    var sampleCount = (int)(duration.TotalSeconds * sampleRate * channels);
    var samples = new float[sampleCount];
    var samplesPerChannel = sampleCount / channels;
    var frequencyRate = (endFrequency - startFrequency) / samplesPerChannel;

    for (int i = 0; i < samplesPerChannel; i++)
    {
      var currentFrequency = startFrequency + frequencyRate * i;
      var value = (float)Math.Sin(2 * Math.PI * currentFrequency * i / sampleRate);
      for (int ch = 0; ch < channels; ch++)
      {
        samples[i * channels + ch] = value * 0.8f;
      }
    }

    return samples;
  }

  private static void WriteWavFile(
    string filePath,
    float[] samples,
    int sampleRate,
    int channels,
    int bitsPerSample)
  {
    using var stream = new FileStream(filePath, FileMode.Create);
    using var writer = new BinaryWriter(stream);

    var bytesPerSample = bitsPerSample / 8;
    var blockAlign = channels * bytesPerSample;
    var byteRate = sampleRate * blockAlign;
    var dataSize = samples.Length * bytesPerSample;

    // RIFF header
    writer.Write("RIFF"u8);
    writer.Write(36 + dataSize); // File size - 8
    writer.Write("WAVE"u8);

    // fmt chunk
    writer.Write("fmt "u8);
    writer.Write(16); // Chunk size
    writer.Write((short)1); // Audio format (PCM)
    writer.Write((short)channels);
    writer.Write(sampleRate);
    writer.Write(byteRate);
    writer.Write((short)blockAlign);
    writer.Write((short)bitsPerSample);

    // data chunk
    writer.Write("data"u8);
    writer.Write(dataSize);

    // Write samples
    if (bitsPerSample == 16)
    {
      foreach (var sample in samples)
      {
        var clampedSample = Math.Clamp(sample, -1.0f, 1.0f);
        var intSample = (short)(clampedSample * short.MaxValue);
        writer.Write(intSample);
      }
    }
    else if (bitsPerSample == 8)
    {
      foreach (var sample in samples)
      {
        var clampedSample = Math.Clamp(sample, -1.0f, 1.0f);
        var byteSample = (byte)((clampedSample + 1.0f) * 127.5f);
        writer.Write(byteSample);
      }
    }
    else
    {
      throw new ArgumentException($"Unsupported bits per sample: {bitsPerSample}");
    }
  }
}
