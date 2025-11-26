using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;

namespace Radio.Infrastructure.Tests.Audio.Services;

public class TTSFactoryTests
{
  private readonly Mock<ILogger<TTSFactory>> _loggerMock;
  private readonly Mock<ILogger<TTSEventSource>> _ttsSourceLoggerMock;
  private readonly Mock<IOptionsMonitor<TTSOptions>> _optionsMock;
  private readonly Mock<IOptionsMonitor<TTSSecrets>> _secretsMock;

  public TTSFactoryTests()
  {
    _loggerMock = new Mock<ILogger<TTSFactory>>();
    _ttsSourceLoggerMock = new Mock<ILogger<TTSEventSource>>();
    _optionsMock = new Mock<IOptionsMonitor<TTSOptions>>();
    _secretsMock = new Mock<IOptionsMonitor<TTSSecrets>>();

    // Default options
    _optionsMock.Setup(x => x.CurrentValue).Returns(new TTSOptions
    {
      DefaultEngine = "ESpeak",
      DefaultVoice = "en",
      DefaultSpeed = 1.0f,
      DefaultPitch = 1.0f,
      ESpeakPath = "espeak-ng"
    });

    // Default secrets (empty for tests)
    _secretsMock.Setup(x => x.CurrentValue).Returns(new TTSSecrets());
  }

  private TTSFactory CreateFactory()
  {
    return new TTSFactory(
      _loggerMock.Object,
      _ttsSourceLoggerMock.Object,
      _optionsMock.Object,
      _secretsMock.Object);
  }

  [Fact]
  public void AvailableEngines_ContainsAllEngines()
  {
    var factory = CreateFactory();

    var engines = factory.AvailableEngines;

    Assert.Equal(3, engines.Count);
    Assert.Contains(engines, e => e.Engine == TTSEngine.ESpeak);
    Assert.Contains(engines, e => e.Engine == TTSEngine.Google);
    Assert.Contains(engines, e => e.Engine == TTSEngine.Azure);
  }

  [Fact]
  public void AvailableEngines_ESpeakIsOffline()
  {
    var factory = CreateFactory();

    var espeak = factory.AvailableEngines.First(e => e.Engine == TTSEngine.ESpeak);

    Assert.True(espeak.IsOffline);
    Assert.False(espeak.RequiresApiKey);
  }

  [Fact]
  public void AvailableEngines_GoogleRequiresApiKey()
  {
    var factory = CreateFactory();

    var google = factory.AvailableEngines.First(e => e.Engine == TTSEngine.Google);

    Assert.True(google.RequiresApiKey);
    Assert.False(google.IsOffline);
  }

  [Fact]
  public void AvailableEngines_AzureRequiresApiKey()
  {
    var factory = CreateFactory();

    var azure = factory.AvailableEngines.First(e => e.Engine == TTSEngine.Azure);

    Assert.True(azure.RequiresApiKey);
    Assert.False(azure.IsOffline);
  }

  [Fact]
  public void AvailableEngines_GoogleUnavailableWithoutKey()
  {
    var factory = CreateFactory();

    var google = factory.AvailableEngines.First(e => e.Engine == TTSEngine.Google);

    Assert.False(google.IsAvailable);
  }

  [Fact]
  public void AvailableEngines_GoogleAvailableWithKey()
  {
    _secretsMock.Setup(x => x.CurrentValue).Returns(new TTSSecrets
    {
      GoogleAPIKey = "test-api-key"
    });
    var factory = CreateFactory();

    var google = factory.AvailableEngines.First(e => e.Engine == TTSEngine.Google);

    Assert.True(google.IsAvailable);
  }

  [Fact]
  public void AvailableEngines_AzureUnavailableWithoutKeyOrRegion()
  {
    var factory = CreateFactory();

    var azure = factory.AvailableEngines.First(e => e.Engine == TTSEngine.Azure);

    Assert.False(azure.IsAvailable);
  }

  [Fact]
  public void AvailableEngines_AzureAvailableWithKeyAndRegion()
  {
    _secretsMock.Setup(x => x.CurrentValue).Returns(new TTSSecrets
    {
      AzureAPIKey = "test-api-key",
      AzureRegion = "eastus"
    });
    var factory = CreateFactory();

    var azure = factory.AvailableEngines.First(e => e.Engine == TTSEngine.Azure);

    Assert.True(azure.IsAvailable);
  }

  [Fact]
  public async Task CreateAsync_ThrowsForEmptyText()
  {
    var factory = CreateFactory();

    await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateAsync(""));
    await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateAsync("   "));
  }

  [Fact]
  public async Task CreateAsync_ThrowsForNullText()
  {
    var factory = CreateFactory();

    await Assert.ThrowsAsync<ArgumentNullException>(() => factory.CreateAsync(null!));
  }

  [Fact]
  public async Task GetVoicesAsync_ReturnsVoicesForGoogle()
  {
    var factory = CreateFactory();

    var voices = await factory.GetVoicesAsync(TTSEngine.Google);

    Assert.NotEmpty(voices);
    Assert.All(voices, v =>
    {
      Assert.NotNull(v.Id);
      Assert.NotNull(v.Name);
      Assert.NotNull(v.Language);
    });
  }

  [Fact]
  public async Task GetVoicesAsync_ReturnsVoicesForAzure()
  {
    var factory = CreateFactory();

    var voices = await factory.GetVoicesAsync(TTSEngine.Azure);

    Assert.NotEmpty(voices);
    Assert.All(voices, v =>
    {
      Assert.NotNull(v.Id);
      Assert.NotNull(v.Name);
      Assert.NotNull(v.Language);
    });
  }

  [Fact]
  public async Task GetVoicesAsync_ReturnsDefaultVoicesForESpeak()
  {
    var factory = CreateFactory();

    // This will fall back to default voices if espeak is not installed
    var voices = await factory.GetVoicesAsync(TTSEngine.ESpeak);

    Assert.NotEmpty(voices);
  }

  [Fact]
  public async Task CreateAsync_GoogleEngine_ThrowsNotImplemented()
  {
    _secretsMock.Setup(x => x.CurrentValue).Returns(new TTSSecrets
    {
      GoogleAPIKey = "test-key"
    });
    var factory = CreateFactory();

    var ex = await Assert.ThrowsAsync<NotImplementedException>(() =>
      factory.CreateAsync("Test", new TTSParameters { Engine = TTSEngine.Google }));

    Assert.Contains("Google", ex.Message);
  }

  [Fact]
  public async Task CreateAsync_AzureEngine_ThrowsNotImplemented()
  {
    _secretsMock.Setup(x => x.CurrentValue).Returns(new TTSSecrets
    {
      AzureAPIKey = "test-key",
      AzureRegion = "eastus"
    });
    var factory = CreateFactory();

    var ex = await Assert.ThrowsAsync<NotImplementedException>(() =>
      factory.CreateAsync("Test", new TTSParameters { Engine = TTSEngine.Azure }));

    Assert.Contains("Azure", ex.Message);
  }

  [Fact]
  public async Task CreateAsync_GoogleEngine_ThrowsWithoutApiKey()
  {
    var factory = CreateFactory();

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
      factory.CreateAsync("Test", new TTSParameters { Engine = TTSEngine.Google }));
  }

  [Fact]
  public async Task CreateAsync_AzureEngine_ThrowsWithoutApiKeyOrRegion()
  {
    var factory = CreateFactory();

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
      factory.CreateAsync("Test", new TTSParameters { Engine = TTSEngine.Azure }));
  }
}
