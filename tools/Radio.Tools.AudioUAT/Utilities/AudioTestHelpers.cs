namespace Radio.Tools.AudioUAT.Utilities;

/// <summary>
/// Utility class for audio testing operations.
/// </summary>
public static class AudioTestHelpers
{
  /// <summary>
  /// Generates a sine wave tone for testing.
  /// </summary>
  /// <param name="frequency">The frequency in Hz.</param>
  /// <param name="durationMs">The duration in milliseconds.</param>
  /// <param name="sampleRate">The sample rate.</param>
  /// <param name="amplitude">The amplitude (0.0 to 1.0).</param>
  /// <returns>The generated samples.</returns>
  public static float[] GenerateSineWave(int frequency = 440, int durationMs = 1000,
    int sampleRate = 48000, float amplitude = 0.8f)
  {
    var samples = durationMs * sampleRate / 1000;
    var buffer = new float[samples];

    for (var i = 0; i < samples; i++)
    {
      var t = (double)i / sampleRate;
      buffer[i] = (float)(Math.Sin(2 * Math.PI * frequency * t) * amplitude);
    }

    return buffer;
  }

  /// <summary>
  /// Generates a stereo sine wave tone.
  /// </summary>
  /// <param name="frequency">The frequency in Hz.</param>
  /// <param name="durationMs">The duration in milliseconds.</param>
  /// <param name="sampleRate">The sample rate.</param>
  /// <param name="amplitude">The amplitude (0.0 to 1.0).</param>
  /// <returns>The generated interleaved stereo samples.</returns>
  public static float[] GenerateStereoSineWave(int frequency = 440, int durationMs = 1000,
    int sampleRate = 48000, float amplitude = 0.8f)
  {
    var samplesPerChannel = durationMs * sampleRate / 1000;
    var buffer = new float[samplesPerChannel * 2]; // Stereo interleaved

    for (var i = 0; i < samplesPerChannel; i++)
    {
      var t = (double)i / sampleRate;
      var sample = (float)(Math.Sin(2 * Math.PI * frequency * t) * amplitude);

      buffer[i * 2] = sample;     // Left channel
      buffer[i * 2 + 1] = sample; // Right channel
    }

    return buffer;
  }

  /// <summary>
  /// Generates silence samples.
  /// </summary>
  /// <param name="durationMs">The duration in milliseconds.</param>
  /// <param name="sampleRate">The sample rate.</param>
  /// <param name="channels">The number of channels.</param>
  /// <returns>The generated silence samples.</returns>
  public static float[] GenerateSilence(int durationMs = 1000, int sampleRate = 48000, int channels = 2)
  {
    var samples = durationMs * sampleRate / 1000 * channels;
    return new float[samples];
  }

  /// <summary>
  /// Generates a sweep from one frequency to another.
  /// </summary>
  /// <param name="startFrequency">The start frequency in Hz.</param>
  /// <param name="endFrequency">The end frequency in Hz.</param>
  /// <param name="durationMs">The duration in milliseconds.</param>
  /// <param name="sampleRate">The sample rate.</param>
  /// <param name="amplitude">The amplitude (0.0 to 1.0).</param>
  /// <returns>The generated sweep samples.</returns>
  public static float[] GenerateFrequencySweep(int startFrequency, int endFrequency,
    int durationMs = 1000, int sampleRate = 48000, float amplitude = 0.8f)
  {
    var samples = durationMs * sampleRate / 1000;
    var buffer = new float[samples];
    var phase = 0.0;

    for (var i = 0; i < samples; i++)
    {
      var progress = (double)i / samples;
      var frequency = startFrequency + (endFrequency - startFrequency) * progress;

      buffer[i] = (float)(Math.Sin(phase) * amplitude);
      phase += 2 * Math.PI * frequency / sampleRate;

      // Keep phase in reasonable range
      if (phase > 2 * Math.PI)
      {
        phase -= 2 * Math.PI;
      }
    }

    return buffer;
  }

  /// <summary>
  /// Generates a balance test pattern (alternating L/R).
  /// </summary>
  /// <param name="durationMs">Duration per side in milliseconds.</param>
  /// <param name="frequency">The tone frequency.</param>
  /// <param name="sampleRate">The sample rate.</param>
  /// <param name="amplitude">The amplitude.</param>
  /// <returns>Stereo samples with alternating channels.</returns>
  public static float[] GenerateBalanceTestPattern(int durationMs = 500, int frequency = 440,
    int sampleRate = 48000, float amplitude = 0.8f)
  {
    var samplesPerSide = durationMs * sampleRate / 1000;
    var totalSamples = samplesPerSide * 2 * 2; // 2 channels, 2 sides
    var buffer = new float[totalSamples];

    // Left channel only
    for (var i = 0; i < samplesPerSide; i++)
    {
      var t = (double)i / sampleRate;
      var sample = (float)(Math.Sin(2 * Math.PI * frequency * t) * amplitude);

      buffer[i * 2] = sample;     // Left
      buffer[i * 2 + 1] = 0;      // Right silent
    }

    // Right channel only
    var offset = samplesPerSide * 2;
    for (var i = 0; i < samplesPerSide; i++)
    {
      var t = (double)i / sampleRate;
      var sample = (float)(Math.Sin(2 * Math.PI * frequency * t) * amplitude);

      buffer[offset + i * 2] = 0;         // Left silent
      buffer[offset + i * 2 + 1] = sample; // Right
    }

    return buffer;
  }

  /// <summary>
  /// Calculates the RMS level of audio samples.
  /// </summary>
  /// <param name="samples">The audio samples.</param>
  /// <returns>The RMS level (0.0 to 1.0).</returns>
  public static float CalculateRmsLevel(float[] samples)
  {
    if (samples.Length == 0)
      return 0f;

    var sumSquares = 0.0;
    foreach (var sample in samples)
    {
      sumSquares += sample * sample;
    }

    return (float)Math.Sqrt(sumSquares / samples.Length);
  }

  /// <summary>
  /// Calculates the peak level of audio samples.
  /// </summary>
  /// <param name="samples">The audio samples.</param>
  /// <returns>The peak level (0.0 to 1.0).</returns>
  public static float CalculatePeakLevel(float[] samples)
  {
    if (samples.Length == 0)
      return 0f;

    var peak = 0f;
    foreach (var sample in samples)
    {
      var abs = Math.Abs(sample);
      if (abs > peak)
        peak = abs;
    }

    return peak;
  }

  /// <summary>
  /// Converts linear amplitude to decibels.
  /// </summary>
  /// <param name="linear">The linear amplitude.</param>
  /// <returns>The amplitude in dB.</returns>
  public static float LinearToDecibels(float linear)
  {
    if (linear <= 0)
      return float.NegativeInfinity;

    return 20f * (float)Math.Log10(linear);
  }

  /// <summary>
  /// Converts decibels to linear amplitude.
  /// </summary>
  /// <param name="dB">The amplitude in dB.</param>
  /// <returns>The linear amplitude.</returns>
  public static float DecibelsToLinear(float dB)
  {
    return (float)Math.Pow(10, dB / 20);
  }

  /// <summary>
  /// Detects if audio samples contain silence (below threshold).
  /// </summary>
  /// <param name="samples">The audio samples.</param>
  /// <param name="threshold">The silence threshold (default -60dB).</param>
  /// <returns>True if the samples are silent.</returns>
  public static bool IsSilent(float[] samples, float threshold = -60f)
  {
    var rms = CalculateRmsLevel(samples);
    var dB = LinearToDecibels(rms);
    return dB < threshold;
  }

  // WAV file format constants
  private const int BitsPerSample = 16;
  private const int WavHeaderSize = 36;
  private static readonly byte[] RiffChunkId = "RIFF"u8.ToArray();
  private static readonly byte[] WaveFormat = "WAVE"u8.ToArray();
  private static readonly byte[] FmtChunkId = "fmt "u8.ToArray();
  private static readonly byte[] DataChunkId = "data"u8.ToArray();

  /// <summary>
  /// Generates a WAV file from float samples.
  /// </summary>
  /// <param name="filePath">The output file path.</param>
  /// <param name="samples">The audio samples.</param>
  /// <param name="sampleRate">The sample rate.</param>
  /// <param name="channels">The number of channels.</param>
  public static void WriteWavFile(string filePath, float[] samples, int sampleRate = 48000, int channels = 2)
  {
    using var fs = new FileStream(filePath, FileMode.Create);
    using var writer = new BinaryWriter(fs);

    var bytesPerSample = BitsPerSample / 8;
    var dataSize = samples.Length * bytesPerSample;

    // WAV header
    writer.Write(RiffChunkId);
    writer.Write(WavHeaderSize + dataSize);
    writer.Write(WaveFormat);

    // Format chunk
    writer.Write(FmtChunkId);
    writer.Write(16); // Chunk size
    writer.Write((short)1); // Audio format (PCM)
    writer.Write((short)channels);
    writer.Write(sampleRate);
    writer.Write(sampleRate * channels * bytesPerSample); // Byte rate
    writer.Write((short)(channels * bytesPerSample)); // Block align
    writer.Write((short)BitsPerSample);

    // Data chunk
    writer.Write(DataChunkId);
    writer.Write(dataSize);

    // Write samples
    foreach (var sample in samples)
    {
      var clamped = Math.Clamp(sample, -1f, 1f);
      var pcm = (short)(clamped * short.MaxValue);
      writer.Write(pcm);
    }
  }

  /// <summary>
  /// Ensures test audio files exist, generating them if needed.
  /// </summary>
  /// <param name="assetsPath">The path to the assets directory.</param>
  public static void EnsureTestAudioFiles(string assetsPath)
  {
    var testAudioPath = Path.Combine(assetsPath, "test-audio");
    Directory.CreateDirectory(testAudioPath);

    // Generate 440Hz tone
    var tonePath = Path.Combine(testAudioPath, "tone-440hz.wav");
    if (!File.Exists(tonePath))
    {
      var tone = GenerateStereoSineWave(440, 1000);
      WriteWavFile(tonePath, tone);
    }

    // Generate silence
    var silencePath = Path.Combine(testAudioPath, "silence.wav");
    if (!File.Exists(silencePath))
    {
      var silence = GenerateSilence(1000);
      WriteWavFile(silencePath, silence);
    }

    // Generate a test announcement tone (3 beeps)
    var announcementPath = Path.Combine(testAudioPath, "test-announcement.wav");
    if (!File.Exists(announcementPath))
    {
      var announcement = GenerateAnnouncementTone();
      WriteWavFile(announcementPath, announcement);
    }
  }

  /// <summary>
  /// Generates a 3-beep announcement tone.
  /// </summary>
  /// <returns>The audio samples.</returns>
  private static float[] GenerateAnnouncementTone()
  {
    var beepDuration = 200;
    var pauseDuration = 100;
    var sampleRate = 48000;
    var frequency = 880;

    var beepSamples = GenerateStereoSineWave(frequency, beepDuration, sampleRate, 0.6f);
    var pauseSamples = GenerateSilence(pauseDuration, sampleRate);

    var result = new List<float>();
    for (var i = 0; i < 3; i++)
    {
      result.AddRange(beepSamples);
      if (i < 2)
        result.AddRange(pauseSamples);
    }

    return result.ToArray();
  }
}
