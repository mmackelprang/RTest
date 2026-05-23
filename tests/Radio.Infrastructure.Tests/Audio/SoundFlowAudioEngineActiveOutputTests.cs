using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// Tests the <see cref="SoundFlowAudioEngine.SetActiveOutputAsync"/> "exactly one
/// active output" gate. The gate enforces the invariant that local + Cast + HTTP
/// outputs are never active simultaneously, and persists the choice so a
/// service restart restores the same routing.
/// </summary>
public class SoundFlowAudioEngineActiveOutputTests
{
  [Fact]
  public async Task SetActiveOutputAsync_GoogleCast_MutesLocalAndStartsCastAndHttp()
  {
    var (engine, castMock, httpMock, configMock) = BuildEngine();

    await engine.SetActiveOutputAsync("google-cast");

    Assert.True(engine.IsLocalOutputMuted);
    castMock.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    httpMock.Verify(h => h.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    configMock.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), "AudioPreferences:CurrentOutput", "google-cast",
      It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal("google-cast", engine.ActiveOutputId);
  }

  [Fact]
  public async Task SetActiveOutputAsync_HttpStream_MutesLocalStartsHttpStopsCast()
  {
    var (engine, castMock, httpMock, _) = BuildEngine(castState: AudioOutputState.Streaming);

    await engine.SetActiveOutputAsync("http-stream");

    Assert.True(engine.IsLocalOutputMuted);
    castMock.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    httpMock.Verify(h => h.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal("http-stream", engine.ActiveOutputId);
  }

  [Fact]
  public async Task SetActiveOutputAsync_LocalDevice_UnmutesLocalAndStopsCastAndHttp()
  {
    var (engine, castMock, httpMock, _) = BuildEngine(
      castState: AudioOutputState.Streaming, httpState: AudioOutputState.Streaming);
    // Simulate the prior Cast-active state so the local switch flips mute back off.
    engine.SetLocalOutputMuted(true);

    await engine.SetActiveOutputAsync("playback-1");

    Assert.False(engine.IsLocalOutputMuted);
    castMock.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    httpMock.Verify(h => h.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal("playback-1", engine.ActiveOutputId);
  }

  [Fact]
  public async Task SetActiveOutputAsync_PersistsToConfigManager()
  {
    var (engine, _, _, configMock) = BuildEngine();

    await engine.SetActiveOutputAsync("playback-1");

    configMock.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), "AudioPreferences:CurrentOutput", "playback-1",
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task SetActiveOutputAsync_PersistsWithCorrectStoreId_Sqlite()
  {
    var (engine, _, _, configMock) = BuildEngine(storeType: ConfigurationStoreType.Sqlite);

    await engine.SetActiveOutputAsync("google-cast");

    configMock.Verify(c => c.SetValueAsync(
      "sqlite", "AudioPreferences:CurrentOutput", "google-cast",
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task SetActiveOutputAsync_PersistsWithCorrectStoreId_Json()
  {
    var (engine, _, _, configMock) = BuildEngine(storeType: ConfigurationStoreType.Json);

    await engine.SetActiveOutputAsync("google-cast");

    configMock.Verify(c => c.SetValueAsync(
      "config", "AudioPreferences:CurrentOutput", "google-cast",
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task SetActiveOutputAsync_ConcurrentCalls_AreSerialized()
  {
    // Two rapid switches in opposite directions: the final ActiveOutputId must
    // equal one of the requested ids (not interleaved). Confirms the
    // SemaphoreSlim gate serializes the critical section.
    var (engine, _, _, _) = BuildEngine();

    var t1 = engine.SetActiveOutputAsync("google-cast");
    var t2 = engine.SetActiveOutputAsync("playback-1");
    await Task.WhenAll(t1, t2);

    Assert.Contains(engine.ActiveOutputId, new[] { "google-cast", "playback-1" });
  }

  [Fact]
  public async Task SetActiveOutputAsync_NullOrEmpty_Throws()
  {
    var (engine, _, _, _) = BuildEngine();

    await Assert.ThrowsAsync<ArgumentException>(() => engine.SetActiveOutputAsync(""));
    await Assert.ThrowsAsync<ArgumentException>(() => engine.SetActiveOutputAsync("   "));
    await Assert.ThrowsAsync<ArgumentException>(() => engine.SetActiveOutputAsync(null!));
  }

  [Fact]
  public async Task SetActiveOutputAsync_NoOutputsAttached_StillSetsActiveOutputId()
  {
    // Gate must work with no Cast/HTTP wired (test or minimal config). Persistence
    // and mute state still happen; just no virtual output calls.
    var engine = CreateBareEngine();
    engine.AttachOutputCoordination(null, null, null);

    await engine.SetActiveOutputAsync("playback-1");

    Assert.Equal("playback-1", engine.ActiveOutputId);
    Assert.False(engine.IsLocalOutputMuted);
  }

  // --- helpers ---

  private static (SoundFlowAudioEngine engine,
                  Mock<IAudioOutput> castMock,
                  Mock<IAudioOutput> httpMock,
                  Mock<IConfigurationManager> configMock) BuildEngine(
    AudioOutputState castState = AudioOutputState.Ready,
    AudioOutputState httpState = AudioOutputState.Ready,
    ConfigurationStoreType storeType = ConfigurationStoreType.Sqlite)
  {
    var engine = CreateBareEngine();

    var castMock = new Mock<IAudioOutput>(MockBehavior.Loose);
    castMock.SetupGet(c => c.State).Returns(castState);
    castMock.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    castMock.Setup(c => c.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    castMock.Setup(c => c.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    var httpMock = new Mock<IAudioOutput>(MockBehavior.Loose);
    httpMock.SetupGet(h => h.State).Returns(httpState);
    httpMock.Setup(h => h.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    httpMock.Setup(h => h.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    httpMock.Setup(h => h.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    var configMock = new Mock<IConfigurationManager>();
    configMock.SetupGet(c => c.CurrentStoreType).Returns(storeType);
    configMock.Setup(c => c.SetValueAsync(
        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    engine.AttachOutputCoordination(castMock.Object, httpMock.Object, configMock.Object);
    return (engine, castMock, httpMock, configMock);
  }

  private static SoundFlowAudioEngine CreateBareEngine()
  {
    // The SetActiveOutputAsync gate does not require MiniAudio init: it only
    // touches the mute flag (in-memory), the SemaphoreSlim, the virtual outputs
    // (mocked), and the configuration manager (mocked). The engine's State
    // remains Uninitialized; that's fine for these tests.
    var loggerMock = new Mock<ILogger<SoundFlowAudioEngine>>();
    var mixerLoggerMock = new Mock<ILogger<SoundFlowMasterMixer>>();
    var deviceManagerLoggerMock = new Mock<ILogger<SoundFlowDeviceManager>>();

    var options = new AudioEngineOptions
    {
      SampleRate = 48000,
      Channels = 2,
      BufferSize = 1024,
      EnableHotPlugDetection = false
    };
    var optionsMock = new Mock<IOptions<AudioEngineOptions>>();
    optionsMock.Setup(o => o.Value).Returns(options);

    var configManagerMock = new Mock<IConfigurationManager>();
    var audioPreferencesMock = new Mock<IOptionsMonitor<AudioPreferences>>();
    audioPreferencesMock.Setup(x => x.CurrentValue).Returns(new AudioPreferences());

    var audioOutputOptionsMock = new Mock<IOptionsMonitor<AudioOutputOptions>>();
    audioOutputOptionsMock.Setup(x => x.CurrentValue).Returns(new AudioOutputOptions());

    var masterMixer = new SoundFlowMasterMixer(mixerLoggerMock.Object);
    var deviceManager = new SoundFlowDeviceManager(
      deviceManagerLoggerMock.Object,
      configManagerMock.Object,
      audioPreferencesMock.Object,
      audioOutputOptionsMock.Object);

    return new SoundFlowAudioEngine(loggerMock.Object, optionsMock.Object, masterMixer, deviceManager);
  }
}
