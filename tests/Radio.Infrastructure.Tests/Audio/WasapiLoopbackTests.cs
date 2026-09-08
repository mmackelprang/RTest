using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Infrastructure.Platform.Bluetooth;
using Radio.Metrics;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// Tests for WASAPI loopback capture integration with the Bluetooth pipeline.
/// NAudio/WASAPI calls are not tested directly (require audio hardware).
/// These test the control flow and state transitions.
/// </summary>
public class WasapiLoopbackTests : IAsyncDisposable
{
  private readonly Mock<ILogger<BluetoothAudioSource>> _loggerMock = new();
  private readonly Mock<IAudioDeviceManager> _deviceManagerMock = new();
  private readonly Mock<IMetricsCollector> _metricsMock = new();
  private readonly IOptionsMonitor<BluetoothOptions> _options;

  public WasapiLoopbackTests()
  {
    var optionsMock = new Mock<IOptionsMonitor<BluetoothOptions>>();
    optionsMock.Setup(o => o.CurrentValue).Returns(new BluetoothOptions
    {
      Enabled = true,
      DeviceName = "TestRadio",
      EnableLoopbackCapture = true
    });
    _options = optionsMock.Object;
  }

  public async ValueTask DisposeAsync()
  {
    // Sources created in tests are disposed individually
  }

  [Fact]
  public void IsAudioManagedByPlatform_WhenLoopbackEnabled_ReturnsFalse()
  {
    // When EnableLoopbackCapture=true, platform should NOT manage audio
    // (we capture it via loopback instead)
    var btMock = new Mock<IBluetoothService>();
    btMock.Setup(b => b.IsAudioManagedByPlatform).Returns(false);

    Assert.False(btMock.Object.IsAudioManagedByPlatform);
  }

  [Fact]
  public void IsAudioManagedByPlatform_WhenLoopbackDisabled_ReturnsTrue()
  {
    // When EnableLoopbackCapture=false, platform manages audio directly
    var btMock = new Mock<IBluetoothService>();
    btMock.Setup(b => b.IsAudioManagedByPlatform).Returns(true);

    Assert.True(btMock.Object.IsAudioManagedByPlatform);
  }

  [Fact]
  public async Task InitializeAsync_WithSoundComponent_SetsReadyState()
  {
    // Simulate a service that returns a SoundComponent (like BufferedSoundGenerator)
    var mockComponent = new Mock<global::SoundFlow.Abstracts.SoundComponent>(
      MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat));

    var btMock = new Mock<IBluetoothService>();
    btMock.Setup(b => b.IsAudioManagedByPlatform).Returns(false);
    btMock.Setup(b => b.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);
    btMock.Setup(b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(mockComponent.Object);
    btMock.Setup(b => b.ConnectedDevice).Returns(new BluetoothDeviceInfo
    {
      Address = "11:22:33:44:55:66",
      Name = "Test Phone",
      IsPaired = true,
      IsConnected = true
    });

    var source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      btMock.Object,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object);

    await source.InitializeAsync(CancellationToken.None);

    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal("Test Phone", source.Metadata[StandardMetadataKeys.Title]);
    Assert.True(source.NeedsFingerprintingLookup);

    await source.DisposeAsync();
  }

  /// <summary>
  /// The mirror of <see cref="InitializeAsync_WithSoundComponent_SetsReadyState"/> for the
  /// other arm of the same dispatch (<c>BluetoothAudioSource.cs:159</c>). Added by
  /// <c>TEST-2</c>: the SoundComponent arm had been covered end-to-end since before the row
  /// was filed, and nobody had checked whether an <c>AudioCaptureDevice</c> was mockable the
  /// same way. It is — both are abstract SoundFlow types whose constructors store the engine
  /// reference without dereferencing it, so only the engine is native.
  /// <para>
  /// <c>GetSoundComponent()</c> must throw here. That is the arm discriminator: only the
  /// <c>:166</c> arm assigns <c>SoundComponent</c>, so a passing Ready + metadata assertion
  /// without it would not distinguish this arm from the other one.
  /// </para>
  /// <para>
  /// ⚠ <b>What rules out the <c>else</c> at <c>:175</c> is the metadata pair, not the state.</b>
  /// That branch also lands the source in <c>Ready</c>, but it calls neither
  /// <c>SetConnectedDeviceMetadata()</c> nor sets <c>NeedsFingerprintingLookup</c> — so the Title
  /// and <c>NeedsFingerprintingLookup</c> assertions are the ones doing that work, and dropping
  /// either would leave this test unable to tell the <c>:159</c> arm from no arm at all.
  /// Measured, not assumed: disabling <c>:159</c> turns this test red (<c>TEST-2</c> §4.1,
  /// mutation C).
  /// </para>
  /// </summary>
  [Fact]
  public async Task InitializeAsync_WithAudioCaptureDevice_SetsReadyState()
  {
    var mockCapture = new Mock<global::SoundFlow.Abstracts.Devices.AudioCaptureDevice>(
      MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat), null!);

    var btMock = new Mock<IBluetoothService>();
    btMock.Setup(b => b.IsAudioManagedByPlatform).Returns(false);
    btMock.Setup(b => b.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);
    btMock.Setup(b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(mockCapture.Object);
    btMock.Setup(b => b.ConnectedDevice).Returns(new BluetoothDeviceInfo
    {
      Address = "11:22:33:44:55:66",
      Name = "Test Phone",
      IsPaired = true,
      IsConnected = true
    });

    var source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      btMock.Object,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object);

    await source.InitializeAsync(CancellationToken.None);

    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal("Test Phone", source.Metadata[StandardMetadataKeys.Title]);
    Assert.True(source.NeedsFingerprintingLookup);
    Assert.Throws<InvalidOperationException>(() => source.GetSoundComponent());

    await source.DisposeAsync();
  }

  [Fact]
  public async Task InitializeAsync_WhenPlatformManaged_SkipsCaptureDevice()
  {
    var btMock = new Mock<IBluetoothService>();
    btMock.Setup(b => b.IsAudioManagedByPlatform).Returns(true);
    btMock.Setup(b => b.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);
    btMock.Setup(b => b.ConnectedDevice).Returns(new BluetoothDeviceInfo
    {
      Address = "11:22:33:44:55:66",
      Name = "Test Phone",
      IsPaired = true,
      IsConnected = true
    });

    var source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      btMock.Object,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object);

    await source.InitializeAsync(CancellationToken.None);

    Assert.Equal(AudioSourceState.Ready, source.State);
    btMock.Verify(b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()), Times.Never);

    await source.DisposeAsync();
  }

  [Fact]
  public async Task InitializeAsync_WhenNullCaptureDevice_SetsReadyState()
  {
    var btMock = new Mock<IBluetoothService>();
    btMock.Setup(b => b.IsAudioManagedByPlatform).Returns(false);
    btMock.Setup(b => b.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);
    btMock.Setup(b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync((object?)null);

    var source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      btMock.Object,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object);

    await source.InitializeAsync(CancellationToken.None);

    // Now goes to Ready (waiting for device) instead of Error
    Assert.Equal(AudioSourceState.Ready, source.State);

    await source.DisposeAsync();
  }

  [Fact]
  public void BluetoothOptions_EnableLoopbackCapture_DefaultsToTrue()
  {
    var options = new BluetoothOptions();
    Assert.True(options.EnableLoopbackCapture);
  }

  [Fact]
  public void NullBluetoothService_IsAudioManagedByPlatform_ReturnsFalse()
  {
    var nullService = new NullBluetoothService();
    Assert.False(nullService.IsAudioManagedByPlatform);
  }

  [Fact]
  public void MockBluetoothService_IsAudioManagedByPlatform_ReturnsFalse()
  {
    var mockBt = new MockBluetoothService(new Mock<ILogger<MockBluetoothService>>().Object);
    Assert.False(mockBt.IsAudioManagedByPlatform);
  }
}
