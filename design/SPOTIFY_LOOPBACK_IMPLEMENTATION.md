# Spotify Loopback Implementation Plan

> **⚠️ OBSOLETE DOCUMENTATION**
> 
> This document describes the deprecated Loopback mode implementation. The Loopback mode has been **removed** as of January 2026 in favor of the **Integrated** mode, which uses librespot with direct audio pipe capture.
> 
> For current Spotify implementation, see:
> - `SPOTIFY_INTEGRATED_IMPLEMENTATION_SUMMARY.md` - Current implementation details
> - Configuration now uses `LibrespotPath` instead of `LoopbackDeviceName`
> 
> This document is preserved for historical reference only.

---

## Overview

Convert SpotifyAudioSource from **remote control** (Spotify Connect API) to **loopback audio capture** to enable visualization and unified audio processing through SoundFlow.

## Current Architecture

```
┌─────────────────────────────────────┐
│   SpotifyAudioSource (Current)     │
│                                     │
│  - Remote Control via API           │
│  - No audio data flows through code │
│  - GetSoundComponent() returns null │
│  - Cannot visualize or process      │
└─────────────────────────────────────┘
```

## Target Architecture

```
┌──────────────────────────────────────────────────────────────┐
│   Spotify Client (raspotify/librespot)                      │
│   ↓ (outputs to Windows Loopback Sink)                      │
└──────────────────────────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────────────────────────┐
│   Windows Loopback Virtual Audio Device                     │
│   ├── Sink (Recording)   ←── Spotify writes here           │
│   └── Source (Playback)  ←── SpotifyAudioSource reads here │
└──────────────────────────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────────────────────────┐
│   SpotifyAudioSource (Loopback)                             │
│   - Uses SoundFlow CaptureDevice                            │
│   - Captures from Loopback Source                           │
│   - Audio flows through mixer                               │
│   - Enables visualization                                    │
└──────────────────────────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────────────────────────┐
│   AudioManager → Mixer → Visualization → Output             │
└──────────────────────────────────────────────────────────────┘
```

## Implementation Steps

### Step 1: Windows Loopback Setup (Manual Configuration)

#### Install Virtual Audio Cable / VB-Audio Cable
- Download VB-Audio Virtual Cable (free): https://vb-audio.com/Cable/
- Install on Windows development machine
- Creates virtual audio device: "CABLE Input" and "CABLE Output"

#### Alternative: Windows Built-in Stereo Mix
1. Right-click speaker icon in system tray → Sounds
2. Go to "Recording" tab
3. Right-click empty space → "Show Disabled Devices"
4. Find "Stereo Mix" → Right-click → Enable
5. Set as default recording device

**Note:** Stereo Mix captures ALL system audio. VB-Audio Cable is isolated.

### Step 2: Install Spotify Connect Client

#### Option A: Librespot (Recommended for Windows)
```powershell
# Install Rust (if not already installed)
winget install Rustlang.Rust.GNU

# Clone and build librespot
git clone https://github.com/librespot-org/librespot.git
cd librespot
cargo build --release

# Run with loopback output
.\target\release\librespot.exe `
  --name "RadioConsole" `
  --backend alsa `
  --device "CABLE Input"  # or "Stereo Mix"
```

#### Option B: Raspotify (For Raspberry Pi)
```bash
# Install raspotify on Raspberry Pi
curl -sL https://dtcooper.github.io/raspotify/install.sh | sh

# Configure output device
sudo nano /etc/raspotify/conf
# Add: LIBRESPOT_DEVICE = "hw:Loopback,0,0"

# Restart service
sudo systemctl restart raspotify
```

### Step 3: Configure Loopback Device in Configuration

Update `audio-config` store:

```yaml
Devices:
  Spotify:
    Mode: "Loopback"  # or "RemoteControl"
    LoopbackDeviceName: "CABLE Output"  # Windows: VB-Cable
    # LoopbackDeviceName: "hw:Loopback,0,0"  # Linux: ALSA loopback
```

Add to `DeviceOptions`:

```csharp
public class SpotifyDeviceOptions
{
  /// <summary>Spotify integration mode.</summary>
  public SpotifyMode Mode { get; set; } = SpotifyMode.Loopback;
  
  /// <summary>Loopback device name for audio capture.</summary>
  public string LoopbackDeviceName { get; set; } = "CABLE Output";
}

public enum SpotifyMode
{
  /// <summary>Remote control via Spotify Connect API (no audio data).</summary>
  RemoteControl,
  
  /// <summary>Loopback audio capture from Spotify client.</summary>
  Loopback
}
```

### Step 4: Implement SpotifyLoopbackAudioSource

Create new base class for USB-like audio sources:

**src/Radio.Infrastructure/Audio/Sources/Primary/USBAudioSourceBase.cs**
```csharp
namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Base class for audio sources that capture from USB or loopback devices.
/// Shared by Radio, Vinyl, Generic USB, and Spotify (loopback mode).
/// </summary>
public abstract class USBAudioSourceBase : PrimaryAudioSourceBase
{
  protected readonly IAudioDeviceManager DeviceManager;
  protected string? ReservedUSBPort { get; private set; }
  protected SoundFlow.Nodes.CaptureDevice? CaptureNode { get; private set; }

  protected USBAudioSourceBase(
    ILogger logger,
    IAudioDeviceManager deviceManager)
    : base(logger)
  {
    DeviceManager = deviceManager;
  }

  /// <summary>
  /// Initialize capture from a device.
  /// </summary>
  protected async Task InitializeUSBCaptureAsync(
    string deviceNameOrPort,
    CancellationToken cancellationToken = default)
  {
    // Check if USB port is available
    if (DeviceManager.IsUSBPortInUse(deviceNameOrPort))
    {
      throw new AudioDeviceConflictException(
        $"Device '{deviceNameOrPort}' is already in use by another source");
    }

    // Reserve the device
    DeviceManager.ReserveUSBPort(deviceNameOrPort, Id);
    ReservedUSBPort = deviceNameOrPort;

    // Find audio device info
    var devices = await DeviceManager.GetInputDevicesAsync(cancellationToken);
    var device = devices.FirstOrDefault(d => 
      d.Name.Contains(deviceNameOrPort, StringComparison.OrdinalIgnoreCase) ||
      d.USBPort == deviceNameOrPort);

    if (device == null)
    {
      throw new AudioDeviceNotFoundException(
        $"Audio device '{deviceNameOrPort}' not found. " +
        $"Available devices: {string.Join(", ", devices.Select(d => d.Name))}");
    }

    // Create SoundFlow capture node
    CaptureNode = new SoundFlow.Nodes.CaptureDevice(
      deviceId: device.Id,
      sampleRate: 48000,
      channels: 2,
      format: AudioFormat.Float32);

    State = AudioSourceState.Ready;
  }

  public override object GetSoundComponent()
  {
    if (CaptureNode == null)
    {
      throw new InvalidOperationException("Capture node not initialized");
    }
    return CaptureNode;
  }

  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    if (CaptureNode == null)
    {
      throw new InvalidOperationException("Capture not initialized");
    }

    // Start capturing audio
    CaptureNode.Start();
    State = AudioSourceState.Playing;
  }

  protected override async Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    if (CaptureNode != null)
    {
      CaptureNode.Pause();
    }
    State = AudioSourceState.Paused;
  }

  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    if (CaptureNode != null)
    {
      CaptureNode.Stop();
    }
    State = AudioSourceState.Stopped;
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    if (ReservedUSBPort != null)
    {
      DeviceManager.ReleaseUSBPort(ReservedUSBPort);
      ReservedUSBPort = null;
    }

    if (CaptureNode != null)
    {
      CaptureNode.Dispose();
      CaptureNode = null;
    }

    await base.DisposeAsyncCore();
  }
}
```

**Update SpotifyAudioSource to support both modes:**

```csharp
namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Spotify audio source with dual mode support:
/// - RemoteControl: Spotify Connect API (no audio data)
/// - Loopback: Audio capture from Spotify client via loopback device
/// </summary>
public class SpotifyAudioSource : USBAudioSourceBase, IPlayQueue
{
  private readonly IOptionsMonitor<SpotifySecrets> _secrets;
  private readonly IOptionsMonitor<SpotifyPreferences> _preferences;
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private SpotifyClient? _client;
  private CurrentlyPlayingContext? _currentPlayback;
  private Dictionary<string, object> _metadata = new();
  private TimeSpan _position;
  private TimeSpan? _duration;
  private bool _isAuthenticated;
  private Timer? _pollingTimer;
  private SpotifyMode _mode;

  public SpotifyAudioSource(
    ILogger<SpotifyAudioSource> logger,
    IOptionsMonitor<SpotifySecrets> secrets,
    IOptionsMonitor<SpotifyPreferences> preferences,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IAudioDeviceManager deviceManager,
    Radio.Core.Interfaces.IMetricsCollector? metricsCollector = null)
    : base(logger, deviceManager)
  {
    _secrets = secrets;
    _preferences = preferences;
    _deviceOptions = deviceOptions;
  }

  public override string Name => "Spotify";
  public override AudioSourceType Type => AudioSourceType.Spotify;

  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    _mode = _deviceOptions.CurrentValue.Spotify?.Mode ?? SpotifyMode.Loopback;

    if (_mode == SpotifyMode.Loopback)
    {
      await InitializeLoopbackModeAsync(cancellationToken);
    }
    else
    {
      await InitializeRemoteControlModeAsync(cancellationToken);
    }
  }

  private async Task InitializeLoopbackModeAsync(CancellationToken cancellationToken)
  {
    var loopbackDevice = _deviceOptions.CurrentValue.Spotify?.LoopbackDeviceName;
    if (string.IsNullOrEmpty(loopbackDevice))
    {
      throw new InvalidOperationException(
        "Loopback device not configured for Spotify. " +
        "Set Devices:Spotify:LoopbackDeviceName in configuration.");
    }

    Logger.LogInformation("Initializing Spotify in Loopback mode with device: {Device}", loopbackDevice);

    // Initialize USB capture from loopback device
    await InitializeUSBCaptureAsync(loopbackDevice, cancellationToken);

    // Still initialize Spotify API for metadata
    await InitializeSpotifyAPIAsync(cancellationToken);

    State = AudioSourceState.Ready;
  }

  private async Task InitializeRemoteControlModeAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Initializing Spotify in Remote Control mode");

    await InitializeSpotifyAPIAsync(cancellationToken);

    // No audio capture in remote control mode
    State = AudioSourceState.Ready;
  }

  private async Task InitializeSpotifyAPIAsync(CancellationToken cancellationToken)
  {
    var secrets = _secrets.CurrentValue;
    if (string.IsNullOrEmpty(secrets.ClientID) ||
        string.IsNullOrEmpty(secrets.ClientSecret) ||
        string.IsNullOrEmpty(secrets.RefreshToken))
    {
      Logger.LogWarning("Spotify API credentials not configured. Metadata will be unavailable.");
      return;
    }

    try
    {
      var config = SpotifyClientConfig.CreateDefault()
        .WithAuthenticator(new AuthorizationCodeAuthenticator(
          secrets.ClientID,
          secrets.ClientSecret,
          new AuthorizationCodeTokenResponse { RefreshToken = secrets.RefreshToken }
        ));

      _client = new SpotifyClient(config);

      var user = await _client.UserProfile.Current(cancellationToken);
      Logger.LogInformation("Spotify API authenticated as {UserId}", user.Id);
      _isAuthenticated = true;

      // Start metadata polling timer
      _pollingTimer = new Timer(
        _ => PollPlaybackStateAsync().GetAwaiter().GetResult(),
        null,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2));
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize Spotify API client");
      _isAuthenticated = false;
    }
  }

  public override object GetSoundComponent()
  {
    if (_mode == SpotifyMode.Loopback)
    {
      // Return SoundFlow capture node (audio flows through mixer)
      return base.GetSoundComponent();
    }
    else
    {
      // Remote control mode: no audio data
      return new object();
    }
  }

  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    if (_mode == SpotifyMode.Loopback)
    {
      // Start capturing audio from loopback
      await base.PlayCoreAsync(cancellationToken);
      
      // Optionally: Send play command via API
      if (_client != null)
      {
        try
        {
          await _client.Player.ResumePlayback(cancellationToken);
        }
        catch (Exception ex)
        {
          Logger.LogWarning(ex, "Failed to send play command to Spotify API");
        }
      }
    }
    else
    {
      // Remote control: use API only
      if (_client == null)
      {
        throw new InvalidOperationException("Spotify API client not initialized");
      }

      await _client.Player.ResumePlayback(cancellationToken);
      await UpdatePlaybackStateAsync(cancellationToken);
    }
  }

  // Rest of the existing implementation remains similar...
  // Polling, metadata, queue management, etc.
}
```

### Step 5: Update Configuration Models

**src/Radio.Core/Configuration/DeviceOptions.cs:**
```csharp
public class DeviceOptions
{
  public const string SectionName = "Devices";

  public RadioDeviceOptions Radio { get; set; } = new();
  public VinylDeviceOptions Vinyl { get; set; } = new();
  public CastDeviceOptions Cast { get; set; } = new();
  public SpotifyDeviceOptions Spotify { get; set; } = new();  // NEW
}

public class SpotifyDeviceOptions
{
  /// <summary>
  /// Spotify integration mode.
  /// - RemoteControl: Use Spotify Connect API (no audio data flows through app)
  /// - Loopback: Capture audio from Spotify client via virtual/loopback device
  /// </summary>
  public SpotifyMode Mode { get; set; } = SpotifyMode.Loopback;
  
  /// <summary>
  /// Name of the loopback/virtual audio device to capture from.
  /// Windows: "CABLE Output", "Stereo Mix"
  /// Linux: "hw:Loopback,0,0"
  /// </summary>
  public string LoopbackDeviceName { get; set; } = "CABLE Output";
}
```

**src/Radio.Core/Enums/Audio/SpotifyMode.cs:**
```csharp
namespace Radio.Core.Enums.Audio;

public enum SpotifyMode
{
  /// <summary>
  /// Remote control via Spotify Connect API.
  /// No audio data flows through the application.
  /// Cannot visualize or process audio.
  /// </summary>
  RemoteControl,
  
  /// <summary>
  /// Audio capture via loopback/virtual device.
  /// Audio flows through SoundFlow mixer.
  /// Enables visualization and audio processing.
  /// </summary>
  Loopback
}
```

### Step 6: Update GenericUSBAudioSource

Refactor existing `GenericUSBAudioSource` to use `USBAudioSourceBase`:

```csharp
namespace Radio.Infrastructure.Audio.Sources.Primary;

public class GenericUSBAudioSource : USBAudioSourceBase
{
  private readonly IOptionsMonitor<GenericSourcePreferences> _preferences;

  public GenericUSBAudioSource(
    ILogger<GenericUSBAudioSource> logger,
    IOptionsMonitor<GenericSourcePreferences> preferences,
    IAudioDeviceManager deviceManager)
    : base(logger, deviceManager)
  {
    _preferences = preferences;
  }

  public override string Name => "Generic USB Audio";
  public override AudioSourceType Type => AudioSourceType.GenericUSB;

  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    var savedPort = _preferences.CurrentValue.USBPort;
    if (!string.IsNullOrEmpty(savedPort))
    {
      await InitializeUSBCaptureAsync(savedPort, cancellationToken);
    }
    else
    {
      Logger.LogWarning("No USB port configured for generic USB source");
      State = AudioSourceState.Ready;
    }
  }
}
```

### Step 7: Testing Plan

#### Manual Testing

1. **Verify Loopback Device Setup**
   ```powershell
   # List audio devices
   Get-WmiObject Win32_SoundDevice | Select-Object Name, Status
   ```

2. **Test Spotify Client Output**
   - Start librespot with loopback output
   - Open Spotify app → Connect to "RadioConsole"
   - Play a song
   - Verify audio plays through loopback device

3. **Test Audio Capture**
   - Start RadioConsole with Spotify source
   - Verify audio data is captured from loopback
   - Check visualization displays audio data

#### Unit Tests

**tests/Radio.Infrastructure.Tests/Audio/Sources/SpotifyAudioSourceTests.cs:**
```csharp
public class SpotifyAudioSourceTests
{
  [Fact]
  public async Task InitializeAsync_LoopbackMode_InitializesCaptureDevice()
  {
    // Arrange
    var deviceManager = new Mock<IAudioDeviceManager>();
    var deviceOptions = CreateDeviceOptions(SpotifyMode.Loopback, "CABLE Output");
    
    deviceManager
      .Setup(m => m.GetInputDevicesAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[]
      {
        new AudioDeviceInfo
        {
          Id = "cable-1",
          Name = "CABLE Output",
          Type = AudioDeviceType.Input,
          IsUSBDevice = false
        }
      });

    var source = new SpotifyAudioSource(
      Mock.Of<ILogger<SpotifyAudioSource>>(),
      Mock.Of<IOptionsMonitor<SpotifySecrets>>(),
      Mock.Of<IOptionsMonitor<SpotifyPreferences>>(),
      deviceOptions,
      deviceManager.Object);

    // Act
    await source.InitializeAsync();

    // Assert
    Assert.Equal(AudioSourceState.Ready, source.State);
    deviceManager.Verify(m => m.ReserveUSBPort("CABLE Output", source.Id), Times.Once);
  }

  [Fact]
  public async Task GetSoundComponent_LoopbackMode_ReturnsCaptureNode()
  {
    // Arrange
    var source = CreateSpotifySource(SpotifyMode.Loopback);
    await source.InitializeAsync();

    // Act
    var component = source.GetSoundComponent();

    // Assert
    Assert.IsType<SoundFlow.Nodes.CaptureDevice>(component);
  }

  [Fact]
  public void GetSoundComponent_RemoteControlMode_ReturnsPlaceholder()
  {
    // Arrange
    var source = CreateSpotifySource(SpotifyMode.RemoteControl);

    // Act
    var component = source.GetSoundComponent();

    // Assert
    Assert.NotNull(component);
    Assert.IsNotType<SoundFlow.Nodes.CaptureDevice>(component);
  }
}
```

## Pros and Cons

### Pros ✅
- **Decouples Spotify client from application code**
- **Stable**: Uses official Spotify client (raspotify/librespot)
- **Full audio access**: Enables visualization, EQ, effects
- **Unified processing**: All audio sources use same pipeline
- **Cross-platform**: Works on Windows (dev) and Linux (Pi)
- **No API rate limits**: Audio capture is independent of API

### Cons ⚠️
- **Requires OS-level configuration**: Manual setup of loopback device
- **Additional component**: Must run Spotify client separately
- **Latency**: Small additional latency from loopback (typically 10-50ms)
- **Device management**: Need to ensure loopback device is not in use
- **Platform-specific**: Device names differ between Windows/Linux

## Deployment Notes

### Windows Development
```yaml
# appsettings.Development.json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "CABLE Output"
    }
  }
}
```

### Linux/Raspberry Pi Production
```yaml
# appsettings.Production.json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "hw:Loopback,0,0"
    }
  }
}
```

### Raspotify Configuration
```bash
# /etc/raspotify/conf
LIBRESPOT_NAME="RadioConsole"
LIBRESPOT_DEVICE_TYPE="speaker"
LIBRESPOT_BACKEND="alsa"
LIBRESPOT_DEVICE="hw:Loopback,0,0"
LIBRESPOT_BITRATE="320"
LIBRESPOT_INITIAL_VOLUME="75"
```

## Migration Path

1. **Phase 1**: Implement `USBAudioSourceBase` (3-4 hours)
2. **Phase 2**: Refactor existing USB sources to use base class (2-3 hours)
3. **Phase 3**: Add `SpotifyMode` enum and configuration (1 hour)
4. **Phase 4**: Update `SpotifyAudioSource` for dual mode (4-5 hours)
5. **Phase 5**: Test on Windows with VB-Cable (2-3 hours)
6. **Phase 6**: Test on Raspberry Pi with raspotify (2-3 hours)
7. **Phase 7**: Documentation and deployment scripts (2 hours)

**Total Estimated Time:** 16-21 hours

## Success Criteria

- [ ] `USBAudioSourceBase` created and tested
- [ ] `SpotifyAudioSource` supports both modes
- [ ] Loopback mode captures audio from Spotify client
- [ ] Visualization works with Spotify audio
- [ ] Configuration allows easy mode switching
- [ ] Works on both Windows (VB-Cable) and Linux (ALSA loopback)
- [ ] USB port conflict detection works for loopback device
- [ ] Unit tests cover both modes
- [ ] Documentation updated with setup instructions
