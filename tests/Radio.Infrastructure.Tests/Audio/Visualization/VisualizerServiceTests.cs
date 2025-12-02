using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Visualization;

namespace Radio.Infrastructure.Tests.Audio.Visualization;

public class VisualizerServiceTests
{
  private readonly Mock<ILogger<VisualizerService>> _loggerMock;
  private readonly Mock<IOptions<VisualizerOptions>> _visualizerOptionsMock;
  private readonly Mock<IOptions<AudioEngineOptions>> _audioEngineOptionsMock;
  private readonly VisualizerOptions _visualizerOptions;
  private readonly AudioEngineOptions _audioEngineOptions;

  public VisualizerServiceTests()
  {
    _loggerMock = new Mock<ILogger<VisualizerService>>();

    _visualizerOptions = new VisualizerOptions
    {
      FFTSize = 1024,
      WaveformSampleCount = 256,
      PeakHoldTimeMs = 1000,
      PeakDecayRate = 0.95f,
      RmsSmoothing = 0.3f,
      ApplyWindowFunction = true,
      SpectrumSmoothing = 0.5f
    };

    _audioEngineOptions = new AudioEngineOptions
    {
      SampleRate = 48000,
      Channels = 2
    };

    _visualizerOptionsMock = new Mock<IOptions<VisualizerOptions>>();
    _visualizerOptionsMock.Setup(x => x.Value).Returns(_visualizerOptions);

    _audioEngineOptionsMock = new Mock<IOptions<AudioEngineOptions>>();
    _audioEngineOptionsMock.Setup(x => x.Value).Returns(_audioEngineOptions);
  }

  private VisualizerService CreateService()
  {
    return new VisualizerService(
      _loggerMock.Object,
      _visualizerOptionsMock.Object,
      _audioEngineOptionsMock.Object);
  }

  [Fact]
  public void Constructor_WithValidParameters_Succeeds()
  {
    // Act
    var service = CreateService();

    // Assert
    Assert.NotNull(service);
    Assert.Equal(_audioEngineOptions.SampleRate, service.SampleRate);
    Assert.Equal(_visualizerOptions.FFTSize, service.FFTSize);
    Assert.False(service.IsActive);
  }

  [Fact]
  public void Constructor_WithNullLogger_ThrowsArgumentNullException()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => new VisualizerService(
      null!,
      _visualizerOptionsMock.Object,
      _audioEngineOptionsMock.Object));
  }

  [Fact]
  public void Constructor_WithNullVisualizerOptions_ThrowsArgumentNullException()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => new VisualizerService(
      _loggerMock.Object,
      null!,
      _audioEngineOptionsMock.Object));
  }

  [Fact]
  public void Constructor_WithNullAudioEngineOptions_ThrowsArgumentNullException()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => new VisualizerService(
      _loggerMock.Object,
      _visualizerOptionsMock.Object,
      null!));
  }

  [Fact]
  public void ProcessSamples_Array_SetsIsActiveTrue()
  {
    // Arrange
    var service = CreateService();
    var samples = GenerateStereoSamples(512);

    // Act
    service.ProcessSamples(samples);

    // Assert
    Assert.True(service.IsActive);
  }

  [Fact]
  public void ProcessSamples_Span_SetsIsActiveTrue()
  {
    // Arrange
    var service = CreateService();
    var samples = GenerateStereoSamples(512);

    // Act
    service.ProcessSamples(samples.AsSpan(), samples.Length);

    // Assert
    Assert.True(service.IsActive);
  }

  [Fact]
  public void GetSpectrumData_ReturnsValidData()
  {
    // Arrange
    var service = CreateService();
    var samples = GenerateStereoTone(1000f, 0.5f, 1024);
    service.ProcessSamples(samples);

    // Act
    var spectrum = service.GetSpectrumData();

    // Assert
    Assert.NotNull(spectrum);
    Assert.Equal(_visualizerOptions.FFTSize / 2, spectrum.BinCount);
    Assert.Equal(spectrum.BinCount, spectrum.Magnitudes.Length);
    Assert.Equal(spectrum.BinCount, spectrum.Frequencies.Length);
    Assert.True(spectrum.FrequencyResolution > 0);
    Assert.True(spectrum.Timestamp <= DateTimeOffset.UtcNow);
  }

  [Fact]
  public void GetSpectrumData_WithNoInput_ReturnsLowMagnitudes()
  {
    // Arrange
    var service = CreateService();

    // Act
    var spectrum = service.GetSpectrumData();

    // Assert - all magnitudes should be very low with no input
    Assert.All(spectrum.Magnitudes, m => Assert.True(m <= 1f));
  }

  [Fact]
  public void GetLevelData_ReturnsValidData()
  {
    // Arrange
    var service = CreateService();
    var samples = GenerateStereoTone(440f, 0.5f, 1024);
    service.ProcessSamples(samples);

    // Act
    var levels = service.GetLevelData();

    // Assert
    Assert.NotNull(levels);
    Assert.True(levels.LeftPeak >= 0f && levels.LeftPeak <= 1f);
    Assert.True(levels.RightPeak >= 0f && levels.RightPeak <= 1f);
    Assert.True(levels.LeftRms >= 0f && levels.LeftRms <= 1f);
    Assert.True(levels.RightRms >= 0f && levels.RightRms <= 1f);
    Assert.True(levels.Timestamp <= DateTimeOffset.UtcNow);
  }

  [Fact]
  public void GetLevelData_WithSilence_ReturnsZeroLevels()
  {
    // Arrange
    var service = CreateService();
    var silence = new float[1024];
    service.ProcessSamples(silence);

    // Act
    var levels = service.GetLevelData();

    // Assert
    Assert.Equal(0f, levels.LeftPeak);
    Assert.Equal(0f, levels.RightPeak);
    Assert.Equal(0f, levels.LeftRms);
    Assert.Equal(0f, levels.RightRms);
    Assert.False(levels.IsClipping);
  }

  [Fact]
  public void GetLevelData_WithClipping_DetectsClipping()
  {
    // Arrange
    var service = CreateService();
    var samples = GenerateStereoTone(440f, 1.0f, 1024); // Full amplitude
    service.ProcessSamples(samples);

    // Act
    var levels = service.GetLevelData();

    // Assert
    Assert.True(levels.IsClipping);
  }

  [Fact]
  public void GetWaveformData_ReturnsValidData()
  {
    // Arrange
    var service = CreateService();
    var samples = GenerateStereoSamples(256);
    service.ProcessSamples(samples);

    // Act
    var waveform = service.GetWaveformData();

    // Assert
    Assert.NotNull(waveform);
    Assert.Equal(_visualizerOptions.WaveformSampleCount, waveform.SampleCount);
    Assert.Equal(_visualizerOptions.WaveformSampleCount, waveform.LeftSamples.Length);
    Assert.Equal(_visualizerOptions.WaveformSampleCount, waveform.RightSamples.Length);
    Assert.True(waveform.Duration > TimeSpan.Zero);
    Assert.True(waveform.Timestamp <= DateTimeOffset.UtcNow);
  }

  [Fact]
  public void GetWaveformData_WithNoInput_ReturnsSilence()
  {
    // Arrange
    var service = CreateService();

    // Act
    var waveform = service.GetWaveformData();

    // Assert
    Assert.All(waveform.LeftSamples, s => Assert.Equal(0f, s));
    Assert.All(waveform.RightSamples, s => Assert.Equal(0f, s));
  }

  [Fact]
  public void Reset_ClearsAllData()
  {
    // Arrange
    var service = CreateService();
    var samples = GenerateStereoTone(1000f, 0.8f, 1024);
    service.ProcessSamples(samples);

    // Verify there's data
    Assert.True(service.IsActive);
    Assert.True(service.GetLevelData().LeftPeak > 0.5f);

    // Act
    service.Reset();

    // Assert
    Assert.False(service.IsActive);

    // Get fresh data after reset
    var levels = service.GetLevelData();
    Assert.Equal(0f, levels.LeftPeak);
    Assert.Equal(0f, levels.RightPeak);
  }

  [Fact]
  public void Dispose_SetsIsActiveFalse()
  {
    // Arrange
    var service = CreateService();
    var samples = GenerateStereoSamples(512);
    service.ProcessSamples(samples);

    // Act
    service.Dispose();

    // Assert
    Assert.False(service.IsActive);
  }

  [Fact]
  public void ProcessSamples_WhenDisposed_ThrowsObjectDisposedException()
  {
    // Arrange
    var service = CreateService();
    service.Dispose();
    var samples = GenerateStereoSamples(512);

    // Act & Assert
    Assert.Throws<ObjectDisposedException>(() => service.ProcessSamples(samples));
  }

  [Fact]
  public void GetSpectrumData_WhenDisposed_ThrowsObjectDisposedException()
  {
    // Arrange
    var service = CreateService();
    service.Dispose();

    // Act & Assert
    Assert.Throws<ObjectDisposedException>(() => service.GetSpectrumData());
  }

  [Fact]
  public void GetLevelData_WhenDisposed_ThrowsObjectDisposedException()
  {
    // Arrange
    var service = CreateService();
    service.Dispose();

    // Act & Assert
    Assert.Throws<ObjectDisposedException>(() => service.GetLevelData());
  }

  [Fact]
  public void GetWaveformData_WhenDisposed_ThrowsObjectDisposedException()
  {
    // Arrange
    var service = CreateService();
    service.Dispose();

    // Act & Assert
    Assert.Throws<ObjectDisposedException>(() => service.GetWaveformData());
  }

  [Fact]
  public void Reset_WhenDisposed_ThrowsObjectDisposedException()
  {
    // Arrange
    var service = CreateService();
    service.Dispose();

    // Act & Assert
    Assert.Throws<ObjectDisposedException>(() => service.Reset());
  }

  [Fact]
  public void Dispose_CanBeCalledMultipleTimes()
  {
    // Arrange
    var service = CreateService();

    // Act & Assert - should not throw
    service.Dispose();
    service.Dispose();
  }

  [Fact]
  public void LevelData_CreateEmpty_ReturnsProperDefaults()
  {
    // Act
    var levels = LevelData.CreateEmpty();

    // Assert
    Assert.Equal(0f, levels.LeftPeak);
    Assert.Equal(0f, levels.RightPeak);
    Assert.Equal(0f, levels.LeftRms);
    Assert.Equal(0f, levels.RightRms);
    Assert.Equal(float.NegativeInfinity, levels.LeftPeakDb);
    Assert.Equal(float.NegativeInfinity, levels.RightPeakDb);
    Assert.Equal(float.NegativeInfinity, levels.LeftRmsDb);
    Assert.Equal(float.NegativeInfinity, levels.RightRmsDb);
    Assert.Equal(0f, levels.MonoPeak);
    Assert.Equal(0f, levels.MonoRms);
    Assert.False(levels.IsClipping);
  }

  [Fact]
  public void SpectrumData_CreateEmpty_ReturnsProperDefaults()
  {
    // Act
    var spectrum = SpectrumData.CreateEmpty(512, 48000, 1024);

    // Assert
    Assert.Equal(512, spectrum.BinCount);
    Assert.Equal(512, spectrum.Magnitudes.Length);
    Assert.Equal(512, spectrum.Frequencies.Length);
    Assert.Equal(48000f / 1024f, spectrum.FrequencyResolution);
    Assert.All(spectrum.Magnitudes, m => Assert.Equal(0f, m));
    Assert.Equal(0f, spectrum.Frequencies[0]); // DC bin
  }

  [Fact]
  public void WaveformData_CreateEmpty_ReturnsProperDefaults()
  {
    // Act
    var waveform = WaveformData.CreateEmpty(256, 48000);

    // Assert
    Assert.Equal(256, waveform.SampleCount);
    Assert.Equal(256, waveform.LeftSamples.Length);
    Assert.Equal(256, waveform.RightSamples.Length);
    Assert.Equal(TimeSpan.FromSeconds(256.0 / 48000), waveform.Duration);
    Assert.All(waveform.LeftSamples, s => Assert.Equal(0f, s));
    Assert.All(waveform.RightSamples, s => Assert.Equal(0f, s));
  }

  /// <summary>
  /// Generates interleaved stereo samples with random values.
  /// </summary>
  private static float[] GenerateStereoSamples(int samplePairs)
  {
    var samples = new float[samplePairs * 2];
    var random = new Random(42);

    for (var i = 0; i < samples.Length; i++)
    {
      samples[i] = (float)(random.NextDouble() * 2 - 1);
    }

    return samples;
  }

  /// <summary>
  /// Generates interleaved stereo sine wave at the specified frequency.
  /// </summary>
  private float[] GenerateStereoTone(float frequency, float amplitude, int samplePairs)
  {
    var samples = new float[samplePairs * 2];

    for (var i = 0; i < samplePairs; i++)
    {
      var value = MathF.Sin(2f * MathF.PI * frequency * i / _audioEngineOptions.SampleRate) * amplitude;
      samples[i * 2] = value;     // Left channel
      samples[i * 2 + 1] = value; // Right channel
    }

    return samples;
  }
}
