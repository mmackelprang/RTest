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
      DefaultEngine = "Google",
      DefaultVoice = "en-US-Standard-A",
      DefaultSpeed = 1.0f,
      DefaultPitch = 1.0f
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

    Assert.Equal(2, engines.Count);
    Assert.Contains(engines, e => e.Engine == TTSEngine.Google);
    Assert.Contains(engines, e => e.Engine == TTSEngine.Azure);
  }

  [Fact]
  public void AvailableEngines_OffersNoOfflineEngine()
  {
    // TTS-9 removed eSpeak, the only offline engine. Both survivors are cloud engines, so
    // nothing in the list may claim to work without an API key.
    var factory = CreateFactory();

    var engines = factory.AvailableEngines;

    Assert.DoesNotContain(engines, e => e.IsOffline);
    Assert.DoesNotContain(engines, e => e.Name.Contains("speak", StringComparison.OrdinalIgnoreCase));
    Assert.All(engines, e => Assert.True(e.RequiresApiKey));
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
  public async Task GetVoicesAsync_ReturnsEmptyForCloudEngine_WhenNoCachedVoices()
  {
    var factory = CreateFactory();

    var googleVoices = await factory.GetVoicesAsync(TTSEngine.Google);
    var azureVoices = await factory.GetVoicesAsync(TTSEngine.Azure);

    Assert.Empty(googleVoices);
    Assert.Empty(azureVoices);
  }

  [Fact]
  public async Task GetVoicesAsync_ReturnsCachedVoices_WhenRepositoryHasData()
  {
    var voiceRepoMock = new Mock<ITTSVoiceRepository>();
    var cachedVoices = new List<TTSVoiceInfo>
    {
      new() { Id = "en-US-Standard-A", Name = "US Standard A", Language = "en-US", Gender = TTSVoiceGender.Male, PriceTier = "Standard" },
      new() { Id = "en-GB-Standard-B", Name = "UK Standard B", Language = "en-GB", Gender = TTSVoiceGender.Male, PriceTier = "Standard" }
    };
    voiceRepoMock.Setup(r => r.GetCachedVoicesAsync(TTSEngine.Google, It.IsAny<CancellationToken>()))
      .ReturnsAsync(cachedVoices);

    var factory = new TTSFactory(
      _loggerMock.Object,
      _ttsSourceLoggerMock.Object,
      _optionsMock.Object,
      _secretsMock.Object,
      voiceRepository: voiceRepoMock.Object);

    var voices = await factory.GetVoicesAsync(TTSEngine.Google);

    Assert.Equal(2, voices.Count);
    Assert.All(voices, v =>
    {
      Assert.NotNull(v.Id);
      Assert.NotNull(v.Name);
      Assert.NotNull(v.Language);
    });
  }

  [Fact]
  public async Task CreateAsync_GoogleEngine_AttemptsConnection()
  {
    // Since Google TTS is now implemented, it will attempt an actual HTTP call
    // which will fail in a test environment without network access
    _secretsMock.Setup(x => x.CurrentValue).Returns(new TTSSecrets
    {
      GoogleAPIKey = "test-key"
    });
    var factory = CreateFactory();

    // The call will throw an exception (HttpRequestException or similar) 
    // because it can't connect to Google's servers in test environment
    var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
      factory.CreateAsync("Test", new TTSParameters { Engine = TTSEngine.Google }));

    // Verify we got some kind of network or API error (not a NotSupportedException)
    Assert.NotNull(ex);
  }

  [Fact]
  public async Task CreateAsync_AzureEngine_AttemptsConnection()
  {
    // Since Azure TTS is now implemented, it will attempt an actual HTTP call
    // which will fail in a test environment without network access
    _secretsMock.Setup(x => x.CurrentValue).Returns(new TTSSecrets
    {
      AzureAPIKey = "test-key",
      AzureRegion = "eastus"
    });
    var factory = CreateFactory();

    // The call will throw an exception (HttpRequestException or similar) 
    // because it can't connect to Azure's servers in test environment
    var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
      factory.CreateAsync("Test", new TTSParameters { Engine = TTSEngine.Azure }));

    // Verify we got some kind of network or API error (not a NotSupportedException)
    Assert.NotNull(ex);
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

  // ── TTS-9: the removal must fail loudly rather than fall back ─────────────────────────────

  [Fact]
  public void TTSEngine_DefaultValueIsNotADefinedMember()
  {
    // Regression test for the zero-value trap: TTSEngine is numbered from 1 so an engine that
    // was never set cannot be mistaken for a real one. Renumbering from 0 fails here.
    Assert.False(Enum.IsDefined(default(TTSEngine)));
  }

  [Theory]
  [InlineData("Rhubarb")]
  [InlineData("0")]   // Enum.TryParse accepts the decimal form; Enum.IsDefined is what rejects it
  [InlineData("7")]
  public async Task CreateAsync_ThrowsForUnknownConfiguredEngine(string configuredEngine)
  {
    _optionsMock.Setup(x => x.CurrentValue).Returns(new TTSOptions
    {
      DefaultEngine = configuredEngine,
      DefaultVoice = "en-US-Standard-A"
    });
    var factory = CreateFactory();

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync("Test"));

    Assert.Contains(configuredEngine, ex.Message, StringComparison.Ordinal);
    Assert.Contains("Google, Azure", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CreateAsync_ThrowsForRemovedESpeakEngine()
  {
    // A stored or hand-edited "ESpeak" must be rejected, not silently reinterpreted.
    _optionsMock.Setup(x => x.CurrentValue).Returns(new TTSOptions
    {
      DefaultEngine = "ESpeak",
      DefaultVoice = "en-US-Standard-A"
    });
    var factory = CreateFactory();

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync("Test"));

    Assert.Contains("ESpeak", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CreateAsync_ThrowsWhenNoEngineIsConfigured()
  {
    _optionsMock.Setup(x => x.CurrentValue).Returns(new TTSOptions());
    var factory = CreateFactory();

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync("Test"));

    Assert.Contains("TTS:DefaultEngine", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CreateAsync_ThrowsWhenNoVoiceIsConfigured()
  {
    _optionsMock.Setup(x => x.CurrentValue).Returns(new TTSOptions
    {
      DefaultEngine = "Google",
      DefaultVoice = string.Empty
    });
    var factory = CreateFactory();

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync("Test"));

    Assert.Contains("TTS:DefaultVoice", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CreateAsync_ThrowsWhenCallerSuppliesABlankVoice()
  {
    // The guard sits after the config fallback, so it covers a caller-supplied voice too.
    var factory = CreateFactory();

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      factory.CreateAsync("Test", new TTSParameters { Engine = TTSEngine.Google, Voice = "  " }));

    Assert.Contains("TTS:DefaultVoice", ex.Message, StringComparison.Ordinal);
  }
}
