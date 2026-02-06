using Radio.Core.Models.Audio;
using Radio.IntegrationTests.TestSupport;

namespace Radio.IntegrationTests.Audio;

/// <summary>
/// Integration tests for SoundFlow audio data flow.
/// Tests that audio samples can be captured and processed through the system.
/// </summary>
public class SoundFlowAudioDataIntegrationTests
{
  [Fact]
  public async Task MockAudioSampleProvider_WhenActive_CapturesSamples()
  {
    // Arrange
    var provider = new MockAudioSampleProvider();
    provider.SetActive(true, "TestFile.mp3", PlaySource.File);

    // Act
    var duration = TimeSpan.FromSeconds(1);
    var samples = await provider.CaptureAsync(duration);

    // Assert
    Assert.NotNull(samples);
    Assert.Equal(48000, samples.SampleRate);
    Assert.Equal(2, samples.Channels);
    Assert.Equal(duration, samples.Duration);
    // 48000 * 2 channels * 1 second = 96000 samples
    Assert.Equal(96000, samples.Samples.Length);
    Assert.Equal("TestFile.mp3", samples.SourceName);
  }

  [Fact]
  public async Task MockAudioSampleProvider_WhenInactive_ReturnsNull()
  {
    // Arrange
    var provider = new MockAudioSampleProvider();
    provider.SetActive(false);

    // Act
    var samples = await provider.CaptureAsync(TimeSpan.FromSeconds(1));

    // Assert
    Assert.Null(samples);
  }

  [Fact]
  public void MockAudioSampleProvider_SourceProperties_ReturnConfiguredValues()
  {
    // Arrange
    var provider = new MockAudioSampleProvider();
    provider.SetActive(true, "Radio: Test Song", PlaySource.Radio);

    // Assert
    Assert.True(provider.IsActive);
    Assert.Equal("Radio: Test Song", provider.SourceName);
    Assert.Equal(PlaySource.Radio, provider.SourceType);
  }

  [Fact]
  public async Task MockAudioSampleProvider_CustomSampleGenerator_ProducesCustomSamples()
  {
    // Arrange
    var provider = new MockAudioSampleProvider();
    provider.SetActive(true);
    provider.SetSampleGenerator(duration =>
      MockAudioSampleProvider.GenerateWhiteNoise(duration));

    // Act
    var samples = await provider.CaptureAsync(TimeSpan.FromSeconds(0.5));

    // Assert
    Assert.NotNull(samples);
    // White noise should have some variance (not all zeros or same value)
    var uniqueValues = samples.Samples.Take(100).Distinct().Count();
    Assert.True(uniqueValues > 10, "White noise should have significant variance");
  }

  [Fact]
  public void MockAudioSampleProvider_GenerateSineWave_ProducesValidWaveform()
  {
    // Act
    var samples = MockAudioSampleProvider.GenerateSineWave(
      TimeSpan.FromSeconds(0.1),
      sampleRate: 48000,
      channels: 2,
      frequency: 440.0);

    // Assert
    Assert.NotNull(samples);
    Assert.Equal(48000 * 2 / 10, samples.Length); // 0.1 seconds worth

    // Verify samples are in valid range
    Assert.All(samples, s => Assert.True(s >= -1.0f && s <= 1.0f));

    // Verify we have both positive and negative values (it's a sine wave)
    Assert.Contains(samples, s => s > 0);
    Assert.Contains(samples, s => s < 0);
  }

  [Fact]
  public void MockAudioSampleProvider_GenerateSilence_ProducesZeros()
  {
    // Act
    var samples = MockAudioSampleProvider.GenerateSilence(
      TimeSpan.FromSeconds(0.1),
      sampleRate: 48000,
      channels: 2);

    // Assert
    Assert.All(samples, s => Assert.Equal(0.0f, s));
  }

  [Fact]
  public void MockAudioSampleProvider_GenerateWhiteNoise_ProducesValidSamples()
  {
    // Act
    var samples = MockAudioSampleProvider.GenerateWhiteNoise(
      TimeSpan.FromSeconds(0.1),
      sampleRate: 48000,
      channels: 2);

    // Assert - samples should be in valid range
    Assert.All(samples, s => Assert.True(s >= -1.0f && s <= 1.0f));

    // White noise should have many unique values
    var uniqueValues = samples.Distinct().Count();
    Assert.True(uniqueValues > samples.Length / 10, "White noise should have high variance");
  }

  [Fact]
  public void TestAudioFileGenerator_CreateSineWaveFile_CreatesValidWavFile()
  {
    // Arrange
    var tempDir = Path.Combine(Path.GetTempPath(), $"AudioTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);

    try
    {
      // Act
      var filePath = TestAudioFileGenerator.CreateSineWaveFile(
        tempDir,
        "test-sine.wav",
        TimeSpan.FromSeconds(1),
        sampleRate: 48000,
        channels: 2,
        bitsPerSample: 16,
        frequency: 440.0);

      // Assert
      Assert.True(File.Exists(filePath));
      var fileInfo = new FileInfo(filePath);
      Assert.True(fileInfo.Length > 0);

      // Verify WAV header - should start with "RIFF"
      var header = new byte[4];
      using var stream = File.OpenRead(filePath);
      stream.Read(header, 0, 4);
      Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header));
    }
    finally
    {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  [Fact]
  public void TestAudioFileGenerator_CreateSilenceFile_CreatesValidWavFile()
  {
    // Arrange
    var tempDir = Path.Combine(Path.GetTempPath(), $"AudioTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);

    try
    {
      // Act
      var filePath = TestAudioFileGenerator.CreateSilenceFile(
        tempDir,
        "test-silence.wav",
        TimeSpan.FromSeconds(0.5));

      // Assert
      Assert.True(File.Exists(filePath));

      // Verify it's a valid WAV file with RIFF header
      var header = new byte[4];
      using var stream = File.OpenRead(filePath);
      stream.Read(header, 0, 4);
      Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header));
    }
    finally
    {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  [Fact]
  public void TestAudioFileGenerator_CreateChirpFile_CreatesValidWavFile()
  {
    // Arrange
    var tempDir = Path.Combine(Path.GetTempPath(), $"AudioTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);

    try
    {
      // Act
      var filePath = TestAudioFileGenerator.CreateChirpFile(
        tempDir,
        "test-chirp.wav",
        TimeSpan.FromSeconds(1),
        startFrequency: 100,
        endFrequency: 5000);

      // Assert
      Assert.True(File.Exists(filePath));
      var fileInfo = new FileInfo(filePath);
      Assert.True(fileInfo.Length > 0);
    }
    finally
    {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  [Fact]
  public async Task AudioSampleBuffer_Properties_AreCorrectlySet()
  {
    // Arrange
    var provider = new MockAudioSampleProvider();
    provider.SetActive(true, "TestSource", PlaySource.Radio);

    // Act
    var buffer = await provider.CaptureAsync(TimeSpan.FromSeconds(2));

    // Assert
    Assert.NotNull(buffer);
    Assert.Equal(48000, buffer.SampleRate);
    Assert.Equal(2, buffer.Channels);
    Assert.Equal(TimeSpan.FromSeconds(2), buffer.Duration);
    Assert.Equal("TestSource", buffer.SourceName);

    // SamplesPerChannel = Samples.Length / Channels
    Assert.Equal(buffer.Samples.Length / buffer.Channels, buffer.SamplesPerChannel);
  }

  [Fact]
  public async Task MockAudioSampleProvider_MultipleCapturesConcurrently_WorksCorrectly()
  {
    // Arrange
    var provider = new MockAudioSampleProvider();
    provider.SetActive(true);

    // Act - capture multiple times in parallel
    var tasks = Enumerable.Range(0, 5)
      .Select(_ => provider.CaptureAsync(TimeSpan.FromMilliseconds(100)))
      .ToArray();

    var results = await Task.WhenAll(tasks);

    // Assert - all captures should succeed
    Assert.All(results, r =>
    {
      Assert.NotNull(r);
      Assert.True(r.Samples.Length > 0);
    });
  }

  [Trait("Category", "RequiresAudioDevice")]
  [Fact(Skip = "Requires real audio device - run manually")]
  public async Task TappedOutputStream_CaptureAudio_ReturnsSamples()
  {
    // This test would require a real audio device and TappedOutputStream
    // Skip for automated runs, but document the expected behavior
    await Task.CompletedTask;
    Assert.True(true, "Test skipped - requires audio device");
  }
}
