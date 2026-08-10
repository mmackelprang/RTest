using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// Unit tests for USB audio sources (Radio, Vinyl, and Generic USB).
/// </summary>
public class USBAudioSourceTests
{
  private readonly Mock<ILogger<RadioAudioSource>> _radioLoggerMock;
  private readonly Mock<ILogger<VinylAudioSource>> _vinylLoggerMock;
  private readonly Mock<ILogger<GenericUSBAudioSource>> _genericLoggerMock;
  private readonly Mock<IOptionsMonitor<DeviceOptions>> _deviceOptionsMock;
  private readonly Mock<IOptionsMonitor<RadioOptions>> _radioOptionsMock;
  private readonly Mock<IOptionsMonitor<GenericSourcePreferences>> _genericPreferencesMock;
  private readonly Mock<IAudioDeviceManager> _deviceManagerMock;
  private readonly DeviceOptions _deviceOptions;
  private readonly RadioOptions _radioOptions;
  private readonly GenericSourcePreferences _genericPreferences;

  public USBAudioSourceTests()
  {
    _radioLoggerMock = new Mock<ILogger<RadioAudioSource>>();
    _vinylLoggerMock = new Mock<ILogger<VinylAudioSource>>();
    _genericLoggerMock = new Mock<ILogger<GenericUSBAudioSource>>();

    _deviceOptions = new DeviceOptions
    {
      Radio = new RadioDeviceOptions { USBPort = "/dev/ttyUSB0" },
      Vinyl = new VinylDeviceOptions { USBPort = "/dev/ttyUSB1" }
    };

    _radioOptions = new RadioOptions
    {
      DefaultDevice = "RTLSDRCore",
      DefaultDeviceVolume = 50
    };

    _genericPreferences = new GenericSourcePreferences { USBPort = "" };

    _deviceOptionsMock = new Mock<IOptionsMonitor<DeviceOptions>>();
    _deviceOptionsMock.Setup(o => o.CurrentValue).Returns(_deviceOptions);

    _radioOptionsMock = new Mock<IOptionsMonitor<RadioOptions>>();
    _radioOptionsMock.Setup(o => o.CurrentValue).Returns(_radioOptions);

    _genericPreferencesMock = new Mock<IOptionsMonitor<GenericSourcePreferences>>();
    _genericPreferencesMock.Setup(o => o.CurrentValue).Returns(_genericPreferences);

    _deviceManagerMock = new Mock<IAudioDeviceManager>();
    _deviceManagerMock.Setup(d => d.IsUSBPortInUse(It.IsAny<string>())).Returns(false);
  }

  #region RadioAudioSource Tests

  [Fact]
  public void RadioAudioSource_Constructor_SetsCorrectProperties()
  {
    // Act
    var source = CreateRadioSource();

    // Assert
    Assert.Equal("Radio (RF320)", source.Name);
    Assert.Equal(AudioSourceType.Radio, source.Type);
    Assert.Equal(AudioSourceCategory.Primary, source.Category);
    Assert.False(source.IsSeekable);
    Assert.Null(source.Duration);
    Assert.Equal(TimeSpan.Zero, source.Position);
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public async Task RadioAudioSource_PlayAsync_InitializesAndPlays()
  {
    // Arrange
    var source = CreateRadioSource();

    // Act
    await source.PlayAsync();

    // Assert
    Assert.Equal(AudioSourceState.Playing, source.State);
    _deviceManagerMock.Verify(d => d.ReserveUSBPort("/dev/ttyUSB0", It.IsAny<string>()), Times.Once);
  }

  [Fact]
  public async Task RadioAudioSource_PlayAsync_WhenPortInUse_ThrowsConflictException()
  {
    // Arrange
    _deviceManagerMock.Setup(d => d.IsUSBPortInUse("/dev/ttyUSB0")).Returns(true);
    var source = CreateRadioSource();

    // Act & Assert
    await Assert.ThrowsAsync<AudioDeviceConflictException>(() => source.PlayAsync());
  }

  [Fact]
  public async Task RadioAudioSource_DisposeAsync_ReleasesUSBPort()
  {
    // Arrange
    var source = CreateRadioSource();
    await source.PlayAsync();

    // Act
    await source.DisposeAsync();

    // Assert
    _deviceManagerMock.Verify(d => d.ReleaseUSBPort("/dev/ttyUSB0"), Times.Once);
    Assert.Equal(AudioSourceState.Disposed, source.State);
  }

  [Fact]
  public async Task RadioAudioSource_SeekAsync_ThrowsNotSupportedException()
  {
    // Arrange
    var source = CreateRadioSource();

    // Act & Assert
    await Assert.ThrowsAsync<NotSupportedException>(
      () => source.SeekAsync(TimeSpan.FromSeconds(10)));
  }

  #endregion

  #region VinylAudioSource Tests

  [Fact]
  public void VinylAudioSource_Constructor_SetsCorrectProperties()
  {
    // Act
    var source = CreateVinylSource();

    // Assert
    Assert.Equal("Vinyl Turntable", source.Name);
    Assert.Equal(AudioSourceType.Vinyl, source.Type);
    Assert.Equal(AudioSourceCategory.Primary, source.Category);
    Assert.False(source.IsSeekable);
    Assert.Null(source.Duration);
    Assert.Equal(TimeSpan.Zero, source.Position);
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public async Task VinylAudioSource_PlayAsync_InitializesAndPlays()
  {
    // Arrange
    var source = CreateVinylSource();

    // Act
    await source.PlayAsync();

    // Assert
    Assert.Equal(AudioSourceState.Playing, source.State);
    _deviceManagerMock.Verify(d => d.ReserveUSBPort("/dev/ttyUSB1", It.IsAny<string>()), Times.Once);
  }

  [Fact]
  public async Task VinylAudioSource_PlayAsync_WhenPortInUse_ThrowsConflictException()
  {
    // Arrange
    _deviceManagerMock.Setup(d => d.IsUSBPortInUse("/dev/ttyUSB1")).Returns(true);
    var source = CreateVinylSource();

    // Act & Assert
    await Assert.ThrowsAsync<AudioDeviceConflictException>(() => source.PlayAsync());
  }

  [Fact]
  public async Task VinylAudioSource_DisposeAsync_ReleasesUSBPort()
  {
    // Arrange
    var source = CreateVinylSource();
    await source.PlayAsync();

    // Act
    await source.DisposeAsync();

    // Assert
    _deviceManagerMock.Verify(d => d.ReleaseUSBPort("/dev/ttyUSB1"), Times.Once);
    Assert.Equal(AudioSourceState.Disposed, source.State);
  }

  #endregion

  #region GenericUSBAudioSource Tests

  [Fact]
  public void GenericUSBAudioSource_Constructor_SetsCorrectProperties()
  {
    // Act
    var source = CreateGenericSource();

    // Assert
    Assert.Equal("Generic USB Audio", source.Name);
    Assert.Equal(AudioSourceType.GenericUSB, source.Type);
    Assert.Equal(AudioSourceCategory.Primary, source.Category);
    Assert.False(source.IsSeekable);
    Assert.Null(source.Duration);
    Assert.Equal(TimeSpan.Zero, source.Position);
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public async Task GenericUSBAudioSource_InitializeWithPortAsync_InitializesSuccessfully()
  {
    // Arrange
    var source = CreateGenericSource();

    // Act
    await source.InitializeWithPortAsync("/dev/ttyUSB2");

    // Assert
    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal("/dev/ttyUSB2", source.USBPort);
    _deviceManagerMock.Verify(d => d.ReserveUSBPort("/dev/ttyUSB2", It.IsAny<string>()), Times.Once);
    Assert.Equal("/dev/ttyUSB2", _genericPreferences.USBPort);
  }

  [Fact]
  public async Task GenericUSBAudioSource_InitializeWithPortAsync_WhenPortInUse_ThrowsConflictException()
  {
    // Arrange
    _deviceManagerMock.Setup(d => d.IsUSBPortInUse("/dev/ttyUSB2")).Returns(true);
    var source = CreateGenericSource();

    // Act & Assert
    await Assert.ThrowsAsync<AudioDeviceConflictException>(
      () => source.InitializeWithPortAsync("/dev/ttyUSB2"));
  }

  [Fact]
  public async Task GenericUSBAudioSource_InitializeWithDeviceAsync_InitializesSuccessfully()
  {
    // Arrange
    var source = CreateGenericSource();
    var device = new AudioDeviceInfo
    {
      Id = "test-device",
      Name = "Test USB Device",
      Type = AudioDeviceType.Input,
      IsUSBDevice = true,
      USBPort = "/dev/ttyUSB3"
    };

    // Act
    await source.InitializeWithDeviceAsync(device);

    // Assert
    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal("test-device", source.DeviceId);
    Assert.Contains("Test USB Device", source.Metadata.Values);
  }

  [Fact]
  public async Task GenericUSBAudioSource_InitializeWithDeviceAsync_NonUSBDevice_ThrowsArgumentException()
  {
    // Arrange
    var source = CreateGenericSource();
    var device = new AudioDeviceInfo
    {
      Id = "test-device",
      Name = "Test Device",
      Type = AudioDeviceType.Input,
      IsUSBDevice = false
    };

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
      () => source.InitializeWithDeviceAsync(device));
  }

  [Fact]
  public async Task GenericUSBAudioSource_PlayAsync_WhenInitialized_PlaysSuccessfully()
  {
    // Arrange
    var source = CreateGenericSource();
    await source.InitializeWithPortAsync("/dev/ttyUSB2");

    // Act
    await source.PlayAsync();

    // Assert
    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task GenericUSBAudioSource_DisposeAsync_ReleasesUSBPort()
  {
    // Arrange
    var source = CreateGenericSource();
    await source.InitializeWithPortAsync("/dev/ttyUSB2");

    // Act
    await source.DisposeAsync();

    // Assert
    _deviceManagerMock.Verify(d => d.ReleaseUSBPort("/dev/ttyUSB2"), Times.Once);
    Assert.Equal(AudioSourceState.Disposed, source.State);
    Assert.Null(source.USBPort);
  }

  #endregion

  #region Common State Management Tests

  [Fact]
  public async Task AllUSBSources_PauseAsync_WhenPlaying_PausesPlayback()
  {
    // Arrange
    var radio = CreateRadioSource();
    var vinyl = CreateVinylSource();
    var generic = CreateGenericSource();

    await radio.PlayAsync();
    await vinyl.PlayAsync();
    await generic.InitializeWithPortAsync("/dev/ttyUSB2");
    await generic.PlayAsync();

    // Act
    await radio.PauseAsync();
    await vinyl.PauseAsync();
    await generic.PauseAsync();

    // Assert
    Assert.Equal(AudioSourceState.Paused, radio.State);
    Assert.Equal(AudioSourceState.Paused, vinyl.State);
    Assert.Equal(AudioSourceState.Paused, generic.State);
  }

  [Fact]
  public async Task AllUSBSources_ResumeAsync_WhenPaused_ResumesPlayback()
  {
    // Arrange
    var radio = CreateRadioSource();
    await radio.PlayAsync();
    await radio.PauseAsync();

    // Act
    await radio.ResumeAsync();

    // Assert
    Assert.Equal(AudioSourceState.Playing, radio.State);
  }

  [Fact]
  public async Task AllUSBSources_StopAsync_WhenPlaying_StopsPlayback()
  {
    // Arrange
    var radio = CreateRadioSource();
    await radio.PlayAsync();

    // Act
    await radio.StopAsync();

    // Assert
    Assert.Equal(AudioSourceState.Stopped, radio.State);
  }

  [Fact]
  public async Task AllUSBSources_StateChanged_EventRaised()
  {
    // Arrange
    var radio = CreateRadioSource();
    var stateChanges = new List<AudioSourceState>();
    radio.StateChanged += (s, e) => stateChanges.Add(e.NewState);

    // Act
    await radio.PlayAsync();
    await radio.PauseAsync();
    await radio.StopAsync();

    // Assert
    Assert.Contains(AudioSourceState.Ready, stateChanges);
    Assert.Contains(AudioSourceState.Playing, stateChanges);
    Assert.Contains(AudioSourceState.Paused, stateChanges);
    Assert.Contains(AudioSourceState.Stopped, stateChanges);
  }

  [Fact]
  public async Task AllUSBSources_Operations_AfterDispose_ThrowObjectDisposedException()
  {
    // Arrange
    var radio = CreateRadioSource();
    await radio.DisposeAsync();

    // Act & Assert
    await Assert.ThrowsAsync<ObjectDisposedException>(() => radio.PlayAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => radio.PauseAsync());
  }

  #endregion

  #region Cross-Source Contamination Tests

  // USBAudioSourceBase.OnTrackIdentified is inherited by Vinyl, Radio (RF320),
  // GenericUSB and Bluetooth. TrackIdentified is broadcast to EVERY subscriber,
  // so the `State != Playing && State != Paused` check is only a proxy for
  // "am I active": a non-active source can sit in Playing/Paused and adopt
  // another source's track.

  [Fact]
  public void OnTrackIdentified_WhileDifferentSourceIsActive_DoesNotUpdateMetadata()
  {
    // Arrange — the vinyl source is Playing (so the state check alone would let
    // the identification through) but a DIFFERENT source is the active one.
    var foreignSource = new Mock<IAudioSource>().Object;
    var source = new VinylAudioSource(
      _vinylLoggerMock.Object,
      _deviceOptionsMock.Object,
      _deviceManagerMock.Object,
      getActiveSource: () => foreignSource);

    SetDefaultVinylMetadata(source);
    SetState(source, AudioSourceState.Playing);

    // Act — the radio's audio is fingerprinted and broadcast to every subscriber.
    InvokeOnTrackIdentified(source, CreateTrackMetadata("Radio Song", "Radio Artist"), 0.95);

    // Assert — the vinyl source kept its own default metadata.
    Assert.Equal("Vinyl", source.Metadata[StandardMetadataKeys.Title]);
    Assert.False(source.Metadata.ContainsKey("IdentificationConfidence"));
  }

  [Fact]
  public void OnTrackIdentified_WhileThisSourceIsActive_UpdatesMetadata()
  {
    // Arrange — the vinyl source is both Playing and the active source.
    VinylAudioSource? active = null;
    active = new VinylAudioSource(
      _vinylLoggerMock.Object,
      _deviceOptionsMock.Object,
      _deviceManagerMock.Object,
      getActiveSource: () => active);

    SetDefaultVinylMetadata(active);
    SetState(active, AudioSourceState.Playing);

    // Act
    InvokeOnTrackIdentified(active, CreateTrackMetadata("Vinyl Song", "Vinyl Artist"), 0.95);

    // Assert — metadata updated exactly as before the guard existed.
    Assert.Equal("Vinyl Song", active.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Vinyl Artist", active.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal(0.95, active.Metadata["IdentificationConfidence"]);
  }

  /// <summary>
  /// Populates the default metadata the source would normally get from
  /// InitializeAsync, which needs real USB capture hardware.
  /// </summary>
  private static void SetDefaultVinylMetadata(VinylAudioSource source)
  {
    var method = typeof(USBAudioSourceBase).GetMethod(
      "SetDefaultMetadata",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    method!.Invoke(source, new object[] { "Vinyl", "Vinyl", "Turntable" });
  }

  /// <summary>
  /// Sets the source state via reflection — the real transitions require USB
  /// capture hardware.
  /// </summary>
  private static void SetState(VinylAudioSource source, AudioSourceState state)
  {
    var stateField = typeof(Radio.Infrastructure.Audio.Sources.AudioSourceBase).GetField(
      "_state",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    stateField!.SetValue(source, state);
  }

  private static TrackMetadata CreateTrackMetadata(string title, string artist)
  {
    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = title,
      Artist = artist,
      Album = "Some Album",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Invokes the private USBAudioSourceBase.OnTrackIdentified handler via reflection.
  /// </summary>
  private static void InvokeOnTrackIdentified(
    VinylAudioSource source,
    TrackMetadata track,
    double confidence)
  {
    var method = typeof(USBAudioSourceBase).GetMethod(
      "OnTrackIdentified",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    method!.Invoke(source, new object?[] { null, new TrackIdentifiedEventArgs(track, confidence) });
  }

  #endregion

  #region Helpers

  private RadioAudioSource CreateRadioSource()
  {
    return new RadioAudioSource(
      _radioLoggerMock.Object,
      _deviceOptionsMock.Object,
      _radioOptionsMock.Object,
      _deviceManagerMock.Object);
  }

  private VinylAudioSource CreateVinylSource()
  {
    return new VinylAudioSource(
      _vinylLoggerMock.Object,
      _deviceOptionsMock.Object,
      _deviceManagerMock.Object);
  }

  private GenericUSBAudioSource CreateGenericSource()
  {
    return new GenericUSBAudioSource(
      _genericLoggerMock.Object,
      _genericPreferencesMock.Object,
      _deviceManagerMock.Object);
  }

  #endregion
}
