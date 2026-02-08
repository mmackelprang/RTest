using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.IntegrationTests.TestSupport;

/// <summary>
/// Mock audio sample provider for integration tests.
/// Returns configurable test audio samples without requiring real audio hardware.
/// </summary>
public class MockAudioSampleProvider : IAudioSampleProvider
{
  private bool _isActive = false;
  private string _sourceName = "MockAudioSource";
  private PlaySource _sourceType = PlaySource.File;
  private Func<TimeSpan, float[]>? _sampleGenerator;

  /// <summary>
  /// Gets whether the mock source is currently active.
  /// </summary>
  public bool IsActive => _isActive;

  /// <summary>
  /// Gets the name of the audio source.
  /// </summary>
  public string SourceName => _sourceName;

  /// <summary>
  /// Gets the source type for play history.
  /// </summary>
  public PlaySource SourceType => _sourceType;

  /// <summary>
  /// Gets or sets the source file path for file-based sources.
  /// </summary>
  public string? SourceFilePath { get; set; }

  /// <summary>
  /// Sets the mock source as active and configures its properties.
  /// </summary>
  public void SetActive(bool active, string? sourceName = null, PlaySource? sourceType = null)
  {
    _isActive = active;
    if (sourceName != null) _sourceName = sourceName;
    if (sourceType != null) _sourceType = sourceType.Value;
  }

  /// <summary>
  /// Sets a custom sample generator function.
  /// </summary>
  public void SetSampleGenerator(Func<TimeSpan, float[]> generator)
  {
    _sampleGenerator = generator;
  }

  /// <summary>
  /// Generates a sine wave of the specified duration.
  /// </summary>
  public static float[] GenerateSineWave(TimeSpan duration, int sampleRate = 48000, int channels = 2, double frequency = 440.0)
  {
    var totalSamples = (int)(duration.TotalSeconds * sampleRate * channels);
    var samples = new float[totalSamples];
    var samplesPerChannel = totalSamples / channels;

    for (int i = 0; i < samplesPerChannel; i++)
    {
      var value = (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
      for (int ch = 0; ch < channels; ch++)
      {
        samples[i * channels + ch] = value * 0.5f; // 50% amplitude
      }
    }

    return samples;
  }

  /// <summary>
  /// Generates silence of the specified duration.
  /// </summary>
  public static float[] GenerateSilence(TimeSpan duration, int sampleRate = 48000, int channels = 2)
  {
    var totalSamples = (int)(duration.TotalSeconds * sampleRate * channels);
    return new float[totalSamples];
  }

  /// <summary>
  /// Generates white noise of the specified duration.
  /// </summary>
  public static float[] GenerateWhiteNoise(TimeSpan duration, int sampleRate = 48000, int channels = 2)
  {
    var random = new Random(42); // Deterministic for tests
    var totalSamples = (int)(duration.TotalSeconds * sampleRate * channels);
    var samples = new float[totalSamples];

    for (int i = 0; i < totalSamples; i++)
    {
      samples[i] = (float)(random.NextDouble() * 2 - 1) * 0.3f;
    }

    return samples;
  }

  public Task<AudioSampleBuffer?> CaptureAsync(TimeSpan duration, CancellationToken ct = default)
  {
    if (!_isActive)
    {
      return Task.FromResult<AudioSampleBuffer?>(null);
    }

    var sampleRate = 48000;
    var channels = 2;
    var samples = _sampleGenerator?.Invoke(duration)
      ?? GenerateSineWave(duration, sampleRate, channels);

    var buffer = new AudioSampleBuffer
    {
      Samples = samples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = duration,
      SourceName = _sourceName
    };

    return Task.FromResult<AudioSampleBuffer?>(buffer);
  }
}
