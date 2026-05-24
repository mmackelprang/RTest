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

  // --- helpers ---

  private static (Mock<IAudioEngine> engineMock, AudioEngineInitializationService service) BuildService(
    string? persistedOutput,
    IReadOnlyList<AudioDeviceInfo>? outputDevices = null)
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
    serviceProviderMock.Setup(x => x.GetService(typeof(Radio.Infrastructure.Audio.Outputs.GoogleCastOutput))).Returns((object?)null);
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

    return (engineMock, service);
  }
}
