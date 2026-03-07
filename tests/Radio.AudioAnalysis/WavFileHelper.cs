namespace Radio.AudioAnalysis;

/// <summary>
/// WAV file read/write utilities for diagnostic audio capture and analysis.
/// </summary>
public static class WavFileHelper
{
  private const int BitsPerSample = 16;
  private const int WavHeaderSize = 36;

  /// <summary>
  /// Generates a stereo sine wave with distinct left/right channel frequencies.
  /// Designed for seamless looping when durationSamples is a multiple of both periods.
  /// </summary>
  /// <param name="leftHz">Left channel frequency in Hz.</param>
  /// <param name="rightHz">Right channel frequency in Hz.</param>
  /// <param name="sampleRate">Sample rate in Hz.</param>
  /// <param name="durationSamples">Number of samples per channel.</param>
  /// <param name="amplitude">Amplitude (0.0 to 1.0).</param>
  /// <returns>Interleaved stereo float array (length = durationSamples * 2).</returns>
  public static float[] GenerateStereoSineWave(int leftHz = 200, int rightHz = 300,
    int sampleRate = 48000, int durationSamples = 48000, float amplitude = 0.8f)
  {
    var buffer = new float[durationSamples * 2];

    for (var i = 0; i < durationSamples; i++)
    {
      buffer[i * 2] = (float)(Math.Sin(2.0 * Math.PI * leftHz * i / sampleRate) * amplitude);
      buffer[i * 2 + 1] = (float)(Math.Sin(2.0 * Math.PI * rightHz * i / sampleRate) * amplitude);
    }

    return buffer;
  }

  /// <summary>
  /// Generates a mono sine wave.
  /// </summary>
  public static float[] GenerateMonoSineWave(int frequencyHz = 440, int sampleRate = 48000,
    int durationSamples = 48000, float amplitude = 0.8f)
  {
    var buffer = new float[durationSamples];

    for (var i = 0; i < durationSamples; i++)
    {
      buffer[i] = (float)(Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate) * amplitude);
    }

    return buffer;
  }

  /// <summary>
  /// Writes float samples as a 16-bit PCM WAV file.
  /// </summary>
  /// <param name="filePath">Output file path.</param>
  /// <param name="samples">Interleaved float samples.</param>
  /// <param name="sampleRate">Sample rate in Hz.</param>
  /// <param name="channels">Number of audio channels.</param>
  public static void WriteWavFile(string filePath, float[] samples,
    int sampleRate = 48000, int channels = 2)
  {
    var dir = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(dir))
    {
      Directory.CreateDirectory(dir);
    }

    using var fs = new FileStream(filePath, FileMode.Create);
    using var writer = new BinaryWriter(fs);

    var bytesPerSample = BitsPerSample / 8;
    var dataSize = samples.Length * bytesPerSample;

    // RIFF header
    writer.Write("RIFF"u8);
    writer.Write(WavHeaderSize + dataSize);
    writer.Write("WAVE"u8);

    // Format chunk
    writer.Write("fmt "u8);
    writer.Write(16); // Chunk size
    writer.Write((short)1); // PCM format
    writer.Write((short)channels);
    writer.Write(sampleRate);
    writer.Write(sampleRate * channels * bytesPerSample); // Byte rate
    writer.Write((short)(channels * bytesPerSample)); // Block align
    writer.Write((short)BitsPerSample);

    // Data chunk
    writer.Write("data"u8);
    writer.Write(dataSize);

    foreach (var sample in samples)
    {
      var clamped = Math.Clamp(sample, -1f, 1f);
      writer.Write((short)(clamped * short.MaxValue));
    }
  }

  /// <summary>
  /// Reads a 16-bit PCM WAV file into float samples.
  /// </summary>
  /// <param name="filePath">Input WAV file path.</param>
  /// <param name="sampleRate">Output: sample rate read from the file.</param>
  /// <param name="channels">Output: number of channels read from the file.</param>
  /// <returns>Interleaved float samples normalized to [-1.0, 1.0].</returns>
  public static float[] ReadWavFile(string filePath, out int sampleRate, out int channels)
  {
    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
    using var reader = new BinaryReader(fs);

    // RIFF header
    var riff = reader.ReadBytes(4);
    if (riff[0] != 'R' || riff[1] != 'I' || riff[2] != 'F' || riff[3] != 'F')
    {
      throw new InvalidDataException("Not a valid WAV file (missing RIFF header)");
    }

    reader.ReadInt32(); // File size
    var wave = reader.ReadBytes(4);
    if (wave[0] != 'W' || wave[1] != 'A' || wave[2] != 'V' || wave[3] != 'E')
    {
      throw new InvalidDataException("Not a valid WAV file (missing WAVE format)");
    }

    // Find fmt chunk
    sampleRate = 0;
    channels = 0;
    int bitsPerSample = 0;

    while (fs.Position < fs.Length)
    {
      var chunkId = new string(reader.ReadChars(4));
      var chunkSize = reader.ReadInt32();

      if (chunkId == "fmt ")
      {
        var audioFormat = reader.ReadInt16();
        if (audioFormat != 1)
        {
          throw new InvalidDataException($"Unsupported audio format: {audioFormat} (only PCM supported)");
        }

        channels = reader.ReadInt16();
        sampleRate = reader.ReadInt32();
        reader.ReadInt32(); // Byte rate
        reader.ReadInt16(); // Block align
        bitsPerSample = reader.ReadInt16();

        // Skip any extra format bytes
        var extraBytes = chunkSize - 16;
        if (extraBytes > 0)
        {
          reader.ReadBytes(extraBytes);
        }
      }
      else if (chunkId == "data")
      {
        if (bitsPerSample != 16)
        {
          throw new InvalidDataException($"Unsupported bits per sample: {bitsPerSample} (only 16-bit supported)");
        }

        var sampleCount = chunkSize / (bitsPerSample / 8);
        var samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
          samples[i] = reader.ReadInt16() / 32768.0f;
        }

        return samples;
      }
      else
      {
        // Skip unknown chunks
        reader.ReadBytes(chunkSize);
      }
    }

    throw new InvalidDataException("No data chunk found in WAV file");
  }

  /// <summary>
  /// Calculates the RMS level of audio samples.
  /// </summary>
  public static float CalculateRms(ReadOnlySpan<float> samples)
  {
    if (samples.Length == 0)
    {
      return 0f;
    }

    double sumSquares = 0;
    foreach (var sample in samples)
    {
      sumSquares += sample * sample;
    }

    return (float)Math.Sqrt(sumSquares / samples.Length);
  }

  /// <summary>
  /// Calculates the peak absolute value of audio samples.
  /// </summary>
  public static float CalculatePeak(ReadOnlySpan<float> samples)
  {
    float peak = 0f;
    foreach (var sample in samples)
    {
      var abs = MathF.Abs(sample);
      if (abs > peak)
      {
        peak = abs;
      }
    }
    return peak;
  }

  /// <summary>
  /// Converts linear amplitude to decibels.
  /// </summary>
  public static float LinearToDb(float linear)
  {
    return linear <= 0f ? float.NegativeInfinity : 20f * MathF.Log10(linear);
  }
}
