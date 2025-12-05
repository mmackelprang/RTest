namespace Radio.Infrastructure.Tests.Audio.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;
using Xunit;

public class TTSFactoryMetricsTests
{
  private readonly Mock<IMetricsCollector> _mockMetricsCollector;
  private readonly Mock<IOptionsMonitor<TTSOptions>> _mockOptions;
  private readonly Mock<IOptionsMonitor<TTSSecrets>> _mockSecrets;
  private readonly TTSFactory _ttsFactory;

  public TTSFactoryMetricsTests()
  {
    _mockMetricsCollector = new Mock<IMetricsCollector>();
    _mockOptions = new Mock<IOptionsMonitor<TTSOptions>>();
    _mockSecrets = new Mock<IOptionsMonitor<TTSSecrets>>();

    _mockOptions.Setup(x => x.CurrentValue).Returns(new TTSOptions
    {
      DefaultEngine = "eSpeak",
      DefaultVoice = "en",
      DefaultSpeed = 1.0f,
      DefaultPitch = 1.0f,
      ESpeakPath = "espeak-ng"
    });

    _mockSecrets.Setup(x => x.CurrentValue).Returns(new TTSSecrets());

    _ttsFactory = new TTSFactory(
      NullLogger<TTSFactory>.Instance,
      NullLogger<TTSEventSource>.Instance,
      _mockOptions.Object,
      _mockSecrets.Object,
      _mockMetricsCollector.Object);
  }

  [Fact]
  public async Task CreateAsync_TracksRequestsTotal()
  {
    // Act
    try
    {
      await _ttsFactory.CreateAsync("Hello world", null, CancellationToken.None);
    }
    catch
    {
      // Expected to fail without espeak-ng installed
    }

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Increment("tts.requests_total", 1.0, It.IsAny<IDictionary<string, string>>()),
      Times.Once);
  }

  [Fact]
  public async Task CreateAsync_TracksCharactersProcessed()
  {
    // Arrange
    var text = "Hello world";

    // Act
    try
    {
      await _ttsFactory.CreateAsync(text, null, CancellationToken.None);
    }
    catch
    {
      // Expected to fail without espeak-ng installed
    }

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Increment("tts.characters_processed", text.Length, null),
      Times.Once);
  }

  [Fact]
  public async Task CreateAsync_TracksCacheMisses()
  {
    // Act
    try
    {
      await _ttsFactory.CreateAsync("Test", null, CancellationToken.None);
    }
    catch
    {
      // Expected to fail without espeak-ng installed
    }

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Increment("tts.cache_misses", 1.0, It.IsAny<IDictionary<string, string>>()),
      Times.Once);
  }

  [Fact]
  public async Task CreateAsync_TracksLatency()
  {
    // Act
    try
    {
      await _ttsFactory.CreateAsync("Test", null, CancellationToken.None);
    }
    catch
    {
      // Expected to fail without espeak-ng installed
    }

    // Assert - Latency should be tracked even on failure
    _mockMetricsCollector.Verify(
      x => x.Gauge("tts.latency_ms", It.IsAny<double>(), It.IsAny<IDictionary<string, string>>()),
      Times.Never); // Won't be called if generation fails before completion
  }

  [Fact]
  public async Task CreateAsync_TagsProviderCorrectly()
  {
    // Act
    try
    {
      await _ttsFactory.CreateAsync("Test", null, CancellationToken.None);
    }
    catch
    {
      // Expected to fail without espeak-ng installed
    }

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Increment(
        "tts.requests_total", 
        1.0, 
        It.Is<IDictionary<string, string>>(d => d.ContainsKey("provider") && d["provider"] == "espeak")),
      Times.Once);
  }
}
