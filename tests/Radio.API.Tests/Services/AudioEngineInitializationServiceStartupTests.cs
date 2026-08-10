using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Services;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Xunit;

namespace Radio.API.Tests.Services;

/// <summary>
/// Integration-style tests asserting that the audio-engine startup path
/// routes through <see cref="IAudioEngine.SetActiveOutputAsync"/> for every
/// persisted CurrentOutput value. This is the test that pins the bug fix:
/// when CurrentOutput="google-cast" was persisted and the service restarted,
/// the pre-fix path failed to call SetLocalOutputMuted(true) so the soundbar
/// played alongside the Cast device. Going through the gate guarantees the
/// invariant.
/// </summary>
public class AudioEngineInitializationServiceStartupTests
{
  [Fact]
  public async Task StartAsync_PersistedCastOutput_CallsSetActiveOutputAsyncWithGoogleCast()
  {
    var (engineMock, service) = BuildService(persistedOutput: "google-cast");

    await service.StartAsync(CancellationToken.None);

    // The gate is the only path the startup code should take for virtual outputs.
    engineMock.Verify(
      e => e.SetActiveOutputAsync("google-cast", It.IsAny<CancellationToken>()),
      Times.Once);

    // SetLocalOutputMuted must NOT be called directly anymore — the gate owns it.
    engineMock.Verify(e => e.SetLocalOutputMuted(It.IsAny<bool>()), Times.Never);
  }

  [Fact]
  public async Task StartAsync_PersistedHttpStream_CallsSetActiveOutputAsyncWithHttpStream()
  {
    var (engineMock, service) = BuildService(persistedOutput: "http-stream");

    await service.StartAsync(CancellationToken.None);

    engineMock.Verify(
      e => e.SetActiveOutputAsync("http-stream", It.IsAny<CancellationToken>()),
      Times.Once);
    engineMock.Verify(e => e.SetLocalOutputMuted(It.IsAny<bool>()), Times.Never);
  }

  [Fact]
  public async Task StartAsync_NoPersistedOutput_RoutesDefaultLocalDeviceThroughGate()
  {
    // No persisted preference → service falls back to the first IsDefault
    // local device. That path also must go through the gate so the local
    // sink is unmuted and any prior virtual outputs from the previous run
    // are torn down.
    var defaultDevice = new AudioDeviceInfo
    {
      Id = "playback-1",
      Name = "Soundbar",
      Type = AudioDeviceType.Output,
      IsDefault = true
    };
    var (engineMock, service) = BuildService(
      persistedOutput: null,
      outputDevices: new[] { defaultDevice });

    await service.StartAsync(CancellationToken.None);

    engineMock.Verify(
      e => e.SetActiveOutputAsync("playback-1", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_PersistedLocalDevice_RoutesThroughGate()
  {
    // A previously-selected local device must also go through the gate, so
    // that a service restart from "Cast → soundbar" actually stops Cast and
    // unmutes local, not just the device-manager swap.
    var soundbar = new AudioDeviceInfo
    {
      Id = "playback-2",
      Name = "USB DAC",
      Type = AudioDeviceType.Output,
      IsDefault = false
    };
    var (engineMock, service) = BuildService(
      persistedOutput: "playback-2",
      outputDevices: new[] { soundbar });

    await service.StartAsync(CancellationToken.None);

    engineMock.Verify(
      e => e.SetActiveOutputAsync("playback-2", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  // --- Cast-confirmation regression tests ---
  //
  // Selecting "Google Cast" in the output picker without connecting a device
  // persists CurrentOutput="google-cast" with an empty DefaultCastDeviceId.
  // On the next start the gate above correctly mutes the local sink for the
  // nominated Cast output — and, pre-fix, nothing ever unmuted it when the
  // Cast connect bailed out. Observed in production as 37.5 hours of total
  // silence from the wired soundbar, surviving a restart because the bad
  // preference stayed persisted.
  //
  // The invariant these tests pin is "exactly one WORKING output": the mute
  // stays, but a Cast output that cannot be confirmed must roll back to local
  // — through the gate, so the corrected preference is persisted too.

  [Fact]
  public async Task StartAsync_CastPersistedButNoDefaultCastDevice_FallsBackToLocalOutput()
  {
    // Bail-out 1: CurrentOutput=google-cast, no default Cast device configured.
    var soundbar = LocalDevice("playback-1", "Soundbar", isDefault: true);
    var (engineMock, service) = BuildService(
      persistedOutput: "google-cast",
      outputDevices: new[] { soundbar });

    await service.StartAsync(CancellationToken.None);
    await AwaitCastResolutionAsync(service);

    // The mute still happens — that part was never the bug.
    engineMock.Verify(
      e => e.SetActiveOutputAsync("google-cast", It.IsAny<CancellationToken>()),
      Times.Once);

    // ...but startup must not end there. Local is restored through the gate,
    // which unmutes AND rewrites the persisted preference.
    engineMock.Verify(
      e => e.SetActiveOutputAsync("playback-1", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_CastPersistedAndDeviceNotDiscovered_FallsBackToLocalOutput()
  {
    // Bail-out 2: a default Cast device IS configured, but it never turns up in
    // the discovery cache (unplugged, off the network, renamed).
    var soundbar = LocalDevice("playback-1", "Soundbar", isDefault: true);
    await using var castOutput = BuildRealCastOutput();

    var (engineMock, service) = BuildService(
      persistedOutput: "google-cast",
      outputDevices: new[] { soundbar },
      castOutput: castOutput,
      persistedCastDeviceId: "cast-device-that-is-not-here");

    // Empty cache file -> device is not found.
    service.CastDiscoverySettleDelay = TimeSpan.Zero;

    await service.StartAsync(CancellationToken.None);
    await AwaitCastResolutionAsync(service);

    engineMock.Verify(
      e => e.SetActiveOutputAsync("playback-1", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_CastConnectThrows_FallsBackToLocalOutput()
  {
    // Bail-out 3: the catch-all. The device is in the cache but connecting to it
    // throws (here: nothing is listening on the cached address).
    var soundbar = LocalDevice("playback-1", "Soundbar", isDefault: true);
    var cacheFile = WriteCastCacheFile("cast-unreachable", "127.0.0.1");
    await using var castOutput = BuildRealCastOutput(cacheFile);

    var (engineMock, service) = BuildService(
      persistedOutput: "google-cast",
      outputDevices: new[] { soundbar },
      castOutput: castOutput,
      persistedCastDeviceId: "cast-unreachable");

    service.CastDiscoverySettleDelay = TimeSpan.Zero;

    await service.StartAsync(CancellationToken.None);
    await AwaitCastResolutionAsync(service);

    engineMock.Verify(
      e => e.SetActiveOutputAsync("playback-1", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_CastNeverReachesStreaming_WatchdogFallsBackToLocalOutput()
  {
    // The durable guard. Nothing bails out here — the connect attempt is still
    // in flight when the watchdog's deadline passes. Cast is not Streaming, so
    // local is restored regardless of which code path stalled.
    var soundbar = LocalDevice("playback-1", "Soundbar", isDefault: true);
    var cacheFile = WriteCastCacheFile("cast-slow", "127.0.0.1");
    await using var castOutput = BuildRealCastOutput(cacheFile);

    var (engineMock, service) = BuildService(
      persistedOutput: "google-cast",
      outputDevices: new[] { soundbar },
      castOutput: castOutput,
      persistedCastDeviceId: "cast-slow");

    // Connect attempt parks in the discovery settle; watchdog fires first.
    service.CastDiscoverySettleDelay = TimeSpan.FromSeconds(30);
    service.CastConnectTimeoutOverride = TimeSpan.FromMilliseconds(50);

    await service.StartAsync(CancellationToken.None);
    await AwaitCastResolutionAsync(service);

    engineMock.Verify(
      e => e.SetActiveOutputAsync("playback-1", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_CastFallback_RollsBackExactlyOnce()
  {
    // The explicit bail-out and the watchdog must not both drive the gate.
    var soundbar = LocalDevice("playback-1", "Soundbar", isDefault: true);
    var (engineMock, service) = BuildService(
      persistedOutput: "google-cast",
      outputDevices: new[] { soundbar });

    service.CastConnectTimeoutOverride = TimeSpan.FromMilliseconds(50);

    await service.StartAsync(CancellationToken.None);
    await AwaitCastResolutionAsync(service);

    // Give the watchdog deadline room to pass, in case it was not retired.
    await Task.Delay(200);

    engineMock.Verify(
      e => e.SetActiveOutputAsync("playback-1", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_CastPersistedAndUserSwitchedOutput_DoesNotStompNewChoice()
  {
    // By the time the rollback runs the user may have picked another output.
    // The engine reports it as active; the fallback must leave it alone.
    var soundbar = LocalDevice("playback-1", "Soundbar", isDefault: true);
    var (engineMock, service) = BuildService(
      persistedOutput: "google-cast",
      outputDevices: new[] { soundbar });

    engineMock.SetupGet(e => e.ActiveOutputId).Returns("playback-2");
    service.CastConnectTimeoutOverride = TimeSpan.FromMilliseconds(50);

    await service.StartAsync(CancellationToken.None);
    await AwaitCastResolutionAsync(service);

    engineMock.Verify(
      e => e.SetActiveOutputAsync("playback-1", It.IsAny<CancellationToken>()),
      Times.Never);
  }

  [Fact]
  public async Task StartAsync_DefaultCastDeviceId_IsReadFromConfigStoreNotAppSettings()
  {
    // DevicesController writes DefaultCastDeviceId to the SQLite store, but the
    // startup path used to read it from IOptionsMonitor, which only ever sees
    // appsettings.json. A perfectly valid saved device was therefore invisible.
    var soundbar = LocalDevice("playback-1", "Soundbar", isDefault: true);
    await using var castOutput = BuildRealCastOutput();

    var (_, service, configManagerMock) = BuildServiceWithMocks(
      persistedOutput: "google-cast",
      outputDevices: new[] { soundbar },
      castOutput: castOutput,
      persistedCastDeviceId: "cast-from-sqlite");

    service.CastDiscoverySettleDelay = TimeSpan.Zero;

    await service.StartAsync(CancellationToken.None);
    await AwaitCastResolutionAsync(service);

    configManagerMock.Verify(c => c.GetValueAsync<string>(
        It.IsAny<string>(),
        "AudioPreferences:DefaultCastDeviceId",
        It.IsAny<ConfigurationReadMode>(),
        It.IsAny<CancellationToken>()),
      Times.AtLeastOnce);
  }

  // --- helpers ---

  private static AudioDeviceInfo LocalDevice(string id, string name, bool isDefault) => new()
  {
    Id = id,
    Name = name,
    Type = AudioDeviceType.Output,
    IsDefault = isDefault
  };

  /// <summary>
  /// Awaits the background confirm-or-roll-back task so assertions don't race it.
  /// </summary>
  private static async Task AwaitCastResolutionAsync(AudioEngineInitializationService service)
  {
    var task = service.CastAutoConnectTask;
    if (task == null)
    {
      return;
    }

    var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
    Assert.Same(task, completed);
    await task;
  }

  /// <summary>
  /// A real GoogleCastOutput pointed at a temp cache file. GetCachedDevicesAsync
  /// is documented as "no network I/O" — it reads this file — so the bail-out
  /// paths can be exercised deterministically offline.
  /// </summary>
  private static Radio.Infrastructure.Audio.Outputs.GoogleCastOutput BuildRealCastOutput(
    string? cacheFilePath = null)
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = cacheFilePath
      ?? Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");

    return new Radio.Infrastructure.Audio.Outputs.GoogleCastOutput(
      new Mock<ILogger<Radio.Infrastructure.Audio.Outputs.GoogleCastOutput>>().Object,
      Options.Create(options));
  }

  /// <summary>
  /// Writes a Cast device cache file containing a single device, so the
  /// cache-hit path can be reached without a real Chromecast.
  /// </summary>
  private static string WriteCastCacheFile(string deviceId, string ipAddress)
  {
    var path = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    // Serialised through the production types so the on-disk shape can't drift
    // away from what LoadCacheAsync deserialises.
    var cache = new Dictionary<string, Radio.Infrastructure.Audio.Outputs.CachedCastDevice>
    {
      [deviceId] = new()
      {
        Device = new Radio.Infrastructure.Audio.Outputs.ChromecastDeviceInfo
        {
          Id = deviceId,
          FriendlyName = "Test Cast Device",
          IpAddress = ipAddress,
          Port = 8009,
          Model = "Test"
        },
        LastSeen = DateTime.UtcNow
      }
    };
    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(cache));
    return path;
  }

  private static (Mock<IAudioEngine> engineMock, AudioEngineInitializationService service) BuildService(
    string? persistedOutput,
    IReadOnlyList<AudioDeviceInfo>? outputDevices = null,
    Radio.Infrastructure.Audio.Outputs.GoogleCastOutput? castOutput = null,
    string? persistedCastDeviceId = null)
  {
    var (engineMock, service, _) = BuildServiceWithMocks(
      persistedOutput, outputDevices, castOutput, persistedCastDeviceId);
    return (engineMock, service);
  }

  private static (
    Mock<IAudioEngine> engineMock,
    AudioEngineInitializationService service,
    Mock<IConfigurationManager> configManagerMock) BuildServiceWithMocks(
    string? persistedOutput,
    IReadOnlyList<AudioDeviceInfo>? outputDevices = null,
    Radio.Infrastructure.Audio.Outputs.GoogleCastOutput? castOutput = null,
    string? persistedCastDeviceId = null)
  {
    var loggerMock = new Mock<ILogger<AudioEngineInitializationService>>();
    var engineMock = new Mock<IAudioEngine>();
    var deviceManagerMock = new Mock<IAudioDeviceManager>();
    var serviceProviderMock = new Mock<IServiceProvider>();
    var audioPreferencesMock = new Mock<IOptionsMonitor<AudioPreferences>>();
    var masterMixerMock = new Mock<IMasterMixer>();
    var bluetoothOptionsMock = new Mock<IOptions<BluetoothOptions>>();
    var audioOutputOptionsMock = new Mock<IOptions<AudioOutputOptions>>();
    var configManagerMock = new Mock<IConfigurationManager>();

    audioPreferencesMock.Setup(x => x.CurrentValue).Returns(new AudioPreferences
    {
      CurrentSource = "Radio",
      CurrentOutput = "",
      MasterVolume = 75
    });
    bluetoothOptionsMock.Setup(x => x.Value)
      .Returns(new BluetoothOptions { Enabled = false, EnableOnStartup = false });
    audioOutputOptionsMock.Setup(x => x.Value).Returns(new AudioOutputOptions());

    // The startup path reads persisted CurrentOutput via IConfigurationManager.
    configManagerMock.SetupGet(c => c.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);
    configManagerMock.Setup(c => c.GetValueAsync<string>(
        It.IsAny<string>(),
        "AudioPreferences:CurrentOutput",
        It.IsAny<ConfigurationReadMode>(),
        It.IsAny<CancellationToken>()))
      .ReturnsAsync(persistedOutput);
    configManagerMock.Setup(c => c.GetValueAsync<string>(
        It.IsAny<string>(),
        "AudioPreferences:DefaultCastDeviceId",
        It.IsAny<ConfigurationReadMode>(),
        It.IsAny<CancellationToken>()))
      .ReturnsAsync(persistedCastDeviceId);

    // SetActiveOutputAsync is the gate — wire it up so calls don't fault.
    engineMock.Setup(e => e.SetActiveOutputAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var devices = outputDevices ?? Array.Empty<AudioDeviceInfo>();
    deviceManagerMock.Setup(x => x.GetOutputDevicesAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(devices);
    deviceManagerMock.Setup(x => x.GetInputDevicesAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<AudioDeviceInfo>());

    // Service provider returns the config manager + nulls for everything else.
    serviceProviderMock.Setup(x => x.GetService(typeof(IAudioManager))).Returns((object?)null);
    serviceProviderMock.Setup(x => x.GetService(typeof(IConfigurationManager))).Returns(configManagerMock.Object);
    serviceProviderMock.Setup(x => x.GetService(typeof(IBluetoothService))).Returns((object?)null);
    serviceProviderMock.Setup(x => x.GetService(typeof(Radio.Infrastructure.Audio.Services.BluetoothAutoSwitchService))).Returns((object?)null);
    serviceProviderMock.Setup(x => x.GetService(typeof(Radio.Infrastructure.Audio.Outputs.GoogleCastOutput))).Returns((object?)castOutput);
    serviceProviderMock.Setup(x => x.GetService(typeof(Radio.Infrastructure.Audio.Outputs.HttpStreamOutput))).Returns((object?)null);

    var service = new AudioEngineInitializationService(
      loggerMock.Object,
      engineMock.Object,
      deviceManagerMock.Object,
      audioPreferencesMock.Object,
      masterMixerMock.Object,
      bluetoothOptionsMock.Object,
      audioOutputOptionsMock.Object,
      serviceProviderMock.Object);

    return (engineMock, service, configManagerMock);
  }
}
