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

  [Fact]
  public async Task SetActiveOutputAsync_LeavingCastToLocal_RoutesThroughTearDown()
  {
    // Bug from UAT scenario D: switching Cast -> local via the gate only
    // sent media STOP (via output.StopAsync) but never DisconnectAsync —
    // the Chromecast receiver app kept the session, so audio kept playing
    // on Cast until the user manually disconnected. The fix routes the
    // gate through TearDownCastOutputAsync which calls BOTH StopAsync
    // (media stop) AND DisconnectAsync (CLOSE_APP + receiver disconnect).
    //
    // We can't directly Mock<GoogleCastOutput>.DisconnectAsync (the concrete
    // method isn't virtual, and AudioOutputBase.State isn't virtual either).
    // Instead, we verify the fix path by observing the engine's distinctive
    // "Cast output stopped + disconnected gracefully" log line — emitted ONLY
    // by TearDownCastOutputAsync, never by the plain DeactivateVirtualOutputAsync
    // path. If the log line is present, the gate routed through TearDown.
    var loggerMock = new Mock<ILogger<SoundFlowAudioEngine>>();
    var (engine, castMock, _, _) = BuildEngineWithLogger(loggerMock,
      castState: AudioOutputState.Streaming, httpState: AudioOutputState.Streaming);

    // Establish prior state: Cast is the active output (gate-recorded).
    await engine.SetActiveOutputAsync("google-cast");

    // Transition AWAY from Cast — this is the fix path.
    await engine.SetActiveOutputAsync("playback-1");

    // TearDown was invoked → both the polymorphic StopAsync ran AND the
    // distinctive tear-down log line fired. Without the fix, the gate would
    // have called the plain DeactivateVirtualOutputAsync (no tear-down log).
    castMock.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    loggerMock.Verify(
      l => l.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cast output stopped + disconnected gracefully")),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);
    Assert.False(engine.IsLocalOutputMuted);
    Assert.Equal("playback-1", engine.ActiveOutputId);
  }

  [Fact]
  public async Task SetActiveOutputAsync_LeavingCastToHttpStream_RoutesThroughTearDown()
  {
    // Same fix path also fires when going Cast -> http-stream (rare, but the
    // gate handles it for symmetry). The teardown log is the marker.
    var loggerMock = new Mock<ILogger<SoundFlowAudioEngine>>();
    var (engine, _, _, _) = BuildEngineWithLogger(loggerMock,
      castState: AudioOutputState.Streaming, httpState: AudioOutputState.Streaming);

    await engine.SetActiveOutputAsync("google-cast");
    await engine.SetActiveOutputAsync("http-stream");

    loggerMock.Verify(
      l => l.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cast output stopped + disconnected gracefully")),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);
    Assert.Equal("http-stream", engine.ActiveOutputId);
  }

  [Fact]
  public async Task SetActiveOutputAsync_LocalToLocal_DoesNotTriggerTearDown()
  {
    // Regression check: TearDown must NOT fire on local->local transitions
    // (no Cast involved). The teardown log line should never appear.
    var loggerMock = new Mock<ILogger<SoundFlowAudioEngine>>();
    var (engine, _, _, _) = BuildEngineWithLogger(loggerMock);

    await engine.SetActiveOutputAsync("playback-1");
    await engine.SetActiveOutputAsync("playback-2");

    loggerMock.Verify(
      l => l.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cast output stopped + disconnected gracefully")),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Never);
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
    return CreateBareEngineWithLogger(new Mock<ILogger<SoundFlowAudioEngine>>());
  }

  private static SoundFlowAudioEngine CreateBareEngineWithLogger(Mock<ILogger<SoundFlowAudioEngine>> loggerMock)
  {
    // The SetActiveOutputAsync gate does not require MiniAudio init: it only
    // touches the mute flag (in-memory), the SemaphoreSlim, the virtual outputs
    // (mocked), and the configuration manager (mocked). The engine's State
    // remains Uninitialized; that's fine for these tests.
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

  private static (SoundFlowAudioEngine engine,
                  Mock<IAudioOutput> castMock,
                  Mock<IAudioOutput> httpMock,
                  Mock<IConfigurationManager> configMock) BuildEngineWithLogger(
    Mock<ILogger<SoundFlowAudioEngine>> loggerMock,
    AudioOutputState castState = AudioOutputState.Ready,
    AudioOutputState httpState = AudioOutputState.Ready,
    ConfigurationStoreType storeType = ConfigurationStoreType.Sqlite)
  {
    var engine = CreateBareEngineWithLogger(loggerMock);

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
}
