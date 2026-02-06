# Bluetooth Audio Input Implementation Plan

## Executive Summary

This document outlines a comprehensive plan to add Bluetooth audio input functionality to the Radio Console system. The implementation will allow the system to act as a Bluetooth speaker (A2DP sink) that phones and other Bluetooth-enabled devices can connect to. Audio received via Bluetooth will be treated as a primary audio source with full integration into the existing audio pipeline, including visualization, fingerprinting, and output routing.

## Project Goals

1. **Cross-Platform Support**: Work on both Raspberry Pi (Linux) and Windows development machines
2. **Seamless Integration**: Bluetooth audio flows through existing visualization, fingerprinting, and output systems
3. **User-Friendly**: Simple device naming, automatic pairing, and easy connection management
4. **Production Ready**: Comprehensive testing, documentation, and error handling
5. **Minimal Disruption**: Follow existing architecture patterns and coding standards

## Architecture Overview

### System Integration Points

```
Bluetooth Device (Phone/Tablet/Computer)
    ↓ [A2DP Audio Stream]
Platform-Specific Bluetooth Service (Linux/Windows)
    ↓ [PCM Audio Samples]
BluetoothAudioSource (implements IPrimaryAudioSource)
    ↓ [SoundFlow AudioCaptureDevice]
SoundFlowMasterMixer
    ↓ [Mixed Audio]
├── VisualizerService (FFT, Levels, Waveform)
├── BackgroundIdentificationService (Fingerprinting)
└── Audio Outputs (Local/Chromecast/HTTP Stream)
```

### Platform Abstraction Strategy

```
IBluetoothService (Interface)
    ├── LinuxBluetoothService (BlueZ via D-Bus)
    └── WindowsBluetoothService (32feet.NET or Windows.Devices.Bluetooth)
         ↑
BluetoothServiceFactory
    └── Uses RuntimeInformation.IsOSPlatform() to select implementation
```

## Implementation Phases

### Phase 1: Core Architecture & Interfaces (2-3 days)

**Objective**: Define the foundational types and interfaces without platform-specific code.

#### Tasks:

1. **Update Core Enums** (`src/Radio.Core/Interfaces/Audio/IAudioSource.cs`)
   - Add `Bluetooth` to `AudioSourceType` enum, maintaining logical grouping of primary audio sources
   - Resulting order should be: `Spotify`, `Radio`, `Vinyl`, `FilePlayer`, `GenericUSB`, `Bluetooth`, `TTS`, `AudioFileEvent`

2. **Create Configuration Classes** (`src/Radio.Core/Configuration/`)
   
   **`BluetoothOptions.cs`**:
   ```csharp
   public class BluetoothOptions
   {
     /// <summary>Device name advertised to Bluetooth clients (e.g., "Grandpa's Radio")</summary>
     public string DeviceName { get; set; } = "Radio Console";
     
     /// <summary>Automatically accept incoming connection requests</summary>
     public bool AutoAcceptConnections { get; set; } = true;
     
     /// <summary>Require pairing before connecting</summary>
     public bool RequirePairing { get; set; } = false;
     
     /// <summary>Enable Bluetooth on startup</summary>
     public bool EnableOnStartup { get; set; } = true;
     
     /// <summary>Automatically switch to Bluetooth source when device connects</summary>
     public bool AutoSwitchOnConnect { get; set; } = true;
     
     /// <summary>Audio quality settings</summary>
     public BluetoothAudioQuality AudioQuality { get; set; } = BluetoothAudioQuality.High;
   }
   
   public enum BluetoothAudioQuality
   {
     Standard, // 44.1kHz
     High      // 48kHz
   }
   ```

   **`BluetoothPreferences.cs`**:
   ```csharp
   public class BluetoothPreferences
   {
     /// <summary>Last connected device address</summary>
     public string? LastConnectedDevice { get; set; }
     
     /// <summary>List of paired device addresses</summary>
     public List<string> PairedDevices { get; set; } = new();
     
     /// <summary>Device trust list (auto-connect without prompt)</summary>
     public List<string> TrustedDevices { get; set; } = new();
   }
   ```

3. **Create Bluetooth Service Interface** (`src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`)
   ```csharp
   public interface IBluetoothService : IAsyncDisposable
   {
     /// <summary>Gets whether Bluetooth is available on this platform</summary>
     bool IsAvailable { get; }
     
     /// <summary>Gets the current Bluetooth adapter state</summary>
     BluetoothAdapterState State { get; }
     
     /// <summary>Gets the list of paired devices</summary>
     IReadOnlyList<BluetoothDeviceInfo> PairedDevices { get; }
     
     /// <summary>Gets the currently connected device</summary>
     BluetoothDeviceInfo? ConnectedDevice { get; }
     
     /// <summary>Start Bluetooth adapter and make device discoverable</summary>
     Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default);
     
     /// <summary>Stop Bluetooth adapter</summary>
     Task StopAsync(CancellationToken cancellationToken = default);
     
     /// <summary>Start device discovery</summary>
     Task StartDiscoveryAsync(CancellationToken cancellationToken = default);
     
     /// <summary>Stop device discovery</summary>
     Task StopDiscoveryAsync();
     
     /// <summary>Pair with a discovered device</summary>
     Task<bool> PairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default);
     
     /// <summary>Unpair a device</summary>
     Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default);
     
     /// <summary>Accept incoming connection</summary>
     Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default);
     
     /// <summary>Disconnect current device</summary>
     Task DisconnectAsync(CancellationToken cancellationToken = default);
     
     /// <summary>
     /// Gets the audio capture device representing the Bluetooth input stream.
     /// </summary>
     /// <remarks>
     /// The returned object is a SoundFlow-compatible audio capture device instance
     /// that can be passed directly into the SoundFlow audio pipeline on each
     /// supported platform.
     /// </remarks>
     /// <returns>
     /// A task that resolves to a SoundFlow-compatible audio capture device instance
     /// (for example, a platform-specific implementation of the SoundFlow audio
     /// capture device type), or <c>null</c> if no Bluetooth input is available.
     /// </returns>
     Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default);
     
     /// <summary>Event raised when adapter state changes</summary>
     event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged;
     
     /// <summary>Event raised when device connects</summary>
     event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;
     
     /// <summary>Event raised when device disconnects</summary>
     event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected;
     
     /// <summary>Event raised when new device discovered</summary>
     event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;
   }
   
   public enum BluetoothAdapterState
   {
     Unknown,
     Off,
     TurningOn,
     On,
     TurningOff,
     Error
   }
   
   public class BluetoothDeviceInfo
   {
     public required string Address { get; init; }
     public required string Name { get; init; }
     public bool IsPaired { get; init; }
     public bool IsConnected { get; init; }
     public DateTime? LastConnected { get; init; }
   }
   ```

**Deliverables**:
- Updated `AudioSourceType` enum
- `BluetoothOptions.cs` configuration class
- `BluetoothPreferences.cs` preferences class
- `IBluetoothService.cs` interface with supporting types
- Unit tests for configuration validation

---

### Phase 2: Platform-Specific Implementations (5-7 days)

**Objective**: Implement Bluetooth functionality for Linux and Windows.

#### 2.1: Platform Structure

Create directory structure:
```
src/Radio.Infrastructure/Platform/
├── Bluetooth/
│   ├── BluetoothServiceFactory.cs (Factory with platform detection)
│   ├── Linux/
│   │   ├── LinuxBluetoothService.cs
│   │   ├── BlueZManager.cs (D-Bus communication)
│   │   └── BlueZAudioCapture.cs (Audio routing)
│   └── Windows/
│       ├── WindowsBluetoothService.cs
│       └── WindowsAudioCapture.cs (Audio routing)
```

#### 2.2: Linux Implementation (Raspberry Pi)

**Technology**: BlueZ via D-Bus (using `Tmds.DBus` or direct D-Bus API)

**Key Components**:

1. **`LinuxBluetoothService.cs`**
   - Implements `IBluetoothService`
   - Communicates with BlueZ daemon via D-Bus
   - Manages A2DP sink profile registration
   - Routes audio through PulseAudio/ALSA loopback

2. **BlueZ D-Bus Objects**:
   - `/org/bluez` - BlueZ root
   - `/org/bluez/hci0` - Bluetooth adapter
   - `/org/bluez/hci0/dev_XX_XX_XX_XX_XX_XX` - Device objects

3. **Audio Routing**:
   - Option A: PulseAudio loopback module
     ```bash
     pactl load-module module-loopback source=bluez_sink.XX_XX_XX_XX_XX_XX
     ```
   - Option B: ALSA virtual device capture
   - Route to SoundFlow `AudioCaptureDevice`

4. **Required Linux Packages**:
   - `bluez` (Bluetooth stack)
   - `pulseaudio-module-bluetooth` (A2DP support)
   - `libasound2-dev` (ALSA development)

**NuGet Packages**:
- `Tmds.DBus` (for D-Bus communication)

**Implementation Steps**:
1. Research BlueZ D-Bus API documentation
2. Implement adapter discovery and power management
3. Implement A2DP sink profile registration
4. Implement pairing and connection management
5. Create audio capture pipeline to SoundFlow
6. Test on Raspberry Pi hardware

#### 2.3: Windows Implementation

**Technology**: 32feet.NET or Windows.Devices.Bluetooth

**Key Components**:

1. **`WindowsBluetoothService.cs`**
   - Implements `IBluetoothService`
   - Uses Windows Bluetooth APIs
   - Manages A2DP sink profile
   - Routes audio through Windows audio system

2. **Audio Routing**:
   - Use Windows audio endpoint enumeration
   - Capture Bluetooth audio device
   - Route to SoundFlow `AudioCaptureDevice`

3. **Required Windows Features**:
   - Bluetooth support enabled
   - Windows 10/11 with Bluetooth 4.0+

**NuGet Packages**:
- `InTheHand.Net.Bluetooth` (32feet.NET) or
- Windows SDK APIs (built-in)

**Implementation Steps**:
1. Research Windows Bluetooth API capabilities
2. Implement adapter discovery
3. Implement A2DP sink profile
4. Implement pairing and connection
5. Create audio capture pipeline
6. Test on Windows development machine

#### 2.4: Factory Implementation

**`BluetoothServiceFactory.cs`**:
```csharp
public static class BluetoothServiceFactory
{
  public static IBluetoothService Create(
    ILogger logger,
    IOptions<BluetoothOptions> options)
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      logger.LogInformation("Creating Linux Bluetooth service (BlueZ)");
      return new LinuxBluetoothService(logger, options);
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      logger.LogInformation("Creating Windows Bluetooth service");
      return new WindowsBluetoothService(logger, options);
    }
    else
    {
      logger.LogWarning("Bluetooth not supported on this platform; using NullBluetoothService.");
      return new NullBluetoothService(logger); // No-op implementation
    }
  }
}

/// <summary>
/// No-op Bluetooth service used on unsupported platforms (e.g., macOS).
/// Implements IBluetoothService with safe defaults and warning logs.
/// </summary>
public sealed class NullBluetoothService : IBluetoothService
{
  private readonly ILogger _logger;

  public NullBluetoothService(ILogger logger)
  {
    _logger = logger;
  }

  public bool IsAvailable => false;
  public BluetoothAdapterState State => BluetoothAdapterState.Unknown;
  public IReadOnlyList<BluetoothDeviceInfo> PairedDevices => Array.Empty<BluetoothDeviceInfo>();
  public BluetoothDeviceInfo? ConnectedDevice => null;

  public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged { add { } remove { } }
  public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected { add { } remove { } }
  public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected { add { } remove { } }
  public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered { add { } remove { } }

  public Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("StartAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.FromResult(false);
  }

  public Task StopAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("StopAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.CompletedTask;
  }

  public Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("StartDiscoveryAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.CompletedTask;
  }

  public Task StopDiscoveryAsync()
  {
    _logger.LogWarning("StopDiscoveryAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.CompletedTask;
  }

  public Task<bool> PairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("PairDeviceAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.FromResult(false);
  }

  public Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("UnpairDeviceAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.FromResult(false);
  }

  public Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("AcceptConnectionAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.FromResult(false);
  }

  public Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("DisconnectAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.CompletedTask;
  }

  public Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("GetAudioCaptureDeviceAsync called on NullBluetoothService; Bluetooth is not supported on this platform.");
    return Task.FromResult<object?>(null);
  }

  public ValueTask DisposeAsync()
  {
    return ValueTask.CompletedTask;
  }
}
```

**Deliverables**:
- `LinuxBluetoothService` with BlueZ integration
- `WindowsBluetoothService` with Windows API integration
- `BluetoothServiceFactory` with platform detection
- Platform-specific audio capture implementations
- Unit tests with mocked platform APIs
- Platform-specific integration tests

---

### Phase 3: Audio Source Implementation (3-4 days)

**Objective**: Create `BluetoothAudioSource` that integrates with the existing audio pipeline.

#### Tasks:

1. **Create `BluetoothAudioSource.cs`** (`src/Radio.Infrastructure/Audio/Sources/Primary/`)
   
   **Extends**: `USBAudioSourceBase` (Bluetooth audio is captured via OS audio endpoints/loopback similar to USB sources)
   
   **Note**: While Bluetooth is network-based at the protocol level, the audio capture mechanism uses OS audio endpoints (PulseAudio loopback on Linux, Windows audio endpoints) similar to USB audio devices. `USBAudioSourceBase` provides useful functionality for device enumeration and audio capture that `BluetoothAudioSource` can benefit from. The USB port reservation mechanism won't be used, but the audio capture pipeline is identical.
   
   **Key Features**:
   ```csharp
   public class BluetoothAudioSource : USBAudioSourceBase
   {
     private readonly IBluetoothService _bluetoothService;
     private readonly BackgroundIdentificationService? _identificationService;
     private object? _soundComponent;
     private BluetoothDeviceInfo? _connectedDevice;
     
     public override string Name => "Bluetooth Audio";
     public override AudioSourceType Type => AudioSourceType.Bluetooth;
     public override TimeSpan? Duration => null; // Live stream
     public override TimeSpan Position => TimeSpan.Zero;
     public override bool IsSeekable => false;
     
     // Metadata includes connected device info
     public override IReadOnlyDictionary<string, object> Metadata { get; }
     
     // Simplified playback (no seek, next, previous for Bluetooth)
     public override bool SupportsNext => false;
     public override bool SupportsPrevious => false;
     public override bool SupportsShuffle => false;
     public override bool SupportsRepeat => false;
     
     // Pause behavior:
     // - Initial implementation: when the user presses Pause in the Radio Console UI
     //   while Bluetooth is the active source, playback is paused/muted in the local
     //   audio pipeline only. No AVRCP command is sent; the phone/tablet may continue
     //   playing, but its audio is not rendered by the console until playback resumes.
     // - Future enhancement (TODO): extend IBluetoothService to support AVRCP transport
     //   controls and, when available, have Pause also send an AVRCP "pause" command to
     //   the connected device in addition to pausing the local audio pipeline.
     //
     // The Play/Pause control should remain visible in the UI so Bluetooth behaves
     // consistently with other primary sources; BluetoothAudioSource is responsible for
     // implementing the local pause semantics described above.
     protected override async Task PlayCoreAsync(...)
     protected override async Task StopCoreAsync(...)
   }
   ```

2. **Connection Management**:
   - Subscribe to `IBluetoothService` connection events
   - Update state when device connects/disconnects
   - Set metadata with device name, address, connection time

3. **Audio Pipeline Integration**:
   - Obtain `AudioCaptureDevice` from `IBluetoothService`
   - Return as `SoundComponent` for mixer
   - Handle audio format configuration (44.1kHz/48kHz)

4. **Fingerprinting Integration**:
   - Subscribe to `BackgroundIdentificationService.TrackIdentified`
   - Update metadata with identified track info
   - Same pattern as `VinylAudioSource` and `SDRRadioAudioSource`
   - **Note**: Bluetooth audio streaming from phones may include metadata (via AVRCP or similar protocols). Consider optional metadata extraction from Bluetooth protocols in Phase 2 implementations (TODO), which could supplement or reduce reliance on fingerprinting for track identification.

5. **Error Handling**:
   - Handle Bluetooth disconnections gracefully
   - Transition to `AudioSourceState.Error` on Bluetooth failures
   - Log detailed error information

6. **Metadata Management**:
   ```csharp
   Metadata["Source"] = "Bluetooth";
   Metadata["DeviceName"] = connectedDevice.Name;
   Metadata["DeviceAddress"] = connectedDevice.Address;
   Metadata["ConnectionTime"] = DateTime.UtcNow;
   // Fingerprinting adds: Title, Artist, Album, AlbumArtUrl
   // TODO: Future enhancement - extract metadata from AVRCP if available
   ```

**Deliverables**:
- `BluetoothAudioSource.cs` implementation
- Unit tests for `BluetoothAudioSource`
- Integration tests with mocked `IBluetoothService`

---

### Phase 4: Audio Manager Integration (2-3 days)

**Objective**: Register Bluetooth source with AudioManager and enable source switching.

#### Tasks:

1. **Update `AudioManager.GetOrCreateSourceAsync()`**
   ```csharp
   AudioSourceType.Bluetooth => CreateBluetoothSource(),
   ```

2. **Implement `CreateBluetoothSource()` Factory Method**
   ```csharp
   private IAudioSource CreateBluetoothSource()
   {
     _logger.LogDebug("Creating Bluetooth audio source");
     
     var source = new BluetoothAudioSource(
       _loggerFactory.CreateLogger<BluetoothAudioSource>(),
       _bluetoothService,
       _identificationService,
       _metricsCollector
     );
     
     return source;
   }
   ```

3. **Add Constructor Dependency**
   ```csharp
   public AudioManager(
     // ... existing parameters
     IBluetoothService bluetoothService,
     // ...
   )
   ```

4. **Handle Auto-Switch on Connection**
   ```csharp
   // In AudioManager constructor or initialization
   _bluetoothService.DeviceConnected += async (sender, e) =>
   {
     if (_options.CurrentValue.AutoSwitchOnConnect)
     {
       await SwitchSourceAsync(AudioSourceType.Bluetooth);
     }
   };
   ```

5. **Update DI Registration** (`src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`)
   ```csharp
   public static IServiceCollection AddSoundFlowAudio(
     this IServiceCollection services,
     IConfiguration configuration)
   {
     // ... existing registrations
     
     // Bluetooth service
     services.AddSingleton<IBluetoothService>(sp =>
     {
       var logger = sp.GetRequiredService<ILogger<IBluetoothService>>();
       var options = sp.GetRequiredService<IOptions<BluetoothOptions>>();
       return BluetoothServiceFactory.Create(logger, options);
     });
     
     // Configuration - use IOptionsMonitor for live reloading
     services.Configure<BluetoothOptions>(configuration.GetSection("Bluetooth"));
     services.Configure<BluetoothPreferences>(configuration.GetSection("BluetoothPreferences"));
     
     // Note: BluetoothOptions and BluetoothPreferences support live configuration updates
     // via IOptionsMonitor<T> pattern, allowing dynamic reconfiguration without restart
     
     return services;
   }
   ```

**Deliverables**:
- Updated `AudioManager` with Bluetooth support
- Updated DI registration
- Integration tests for source switching
- Tests for auto-switch functionality

---

### Phase 5: API & Control Layer (3-4 days)

**Objective**: Expose Bluetooth functionality through REST API and SignalR.

#### Tasks:

1. **Create `BluetoothController.cs`** (`src/Radio.API/Controllers/`)
   
   **Endpoints**:
   
   ```csharp
   [ApiController]
   [Route("api/[controller]")]
   public class BluetoothController : ControllerBase
   {
     [HttpGet("status")]
     public async Task<BluetoothStatusDto> GetStatus()
     {
       // Returns: state, connectedDevice, pairedDevices, isDiscoverable
     }
     
     [HttpPost("start")]
     public async Task<IActionResult> Start([FromBody] StartBluetoothDto dto)
     {
       // Start Bluetooth adapter with device name
     }
     
     [HttpPost("stop")]
     public async Task<IActionResult> Stop()
     {
       // Stop Bluetooth adapter
     }
     
     [HttpPost("discovery/start")]
     public async Task<IActionResult> StartDiscovery()
     {
       // Start device discovery
     }
     
     [HttpPost("discovery/stop")]
     public async Task<IActionResult> StopDiscovery()
     
     [HttpPost("pair")]
     public async Task<IActionResult> Pair([FromBody] PairDeviceDto dto)
     {
       // Pair with device by address
     }
     
     [HttpDelete("unpair/{deviceAddress}")]
     public async Task<IActionResult> Unpair(string deviceAddress)
     
     [HttpPost("connect")]
     public async Task<IActionResult> Connect([FromBody] ConnectDeviceDto dto)
     {
       // Accept connection from device
     }
     
     [HttpPost("disconnect")]
     public async Task<IActionResult> Disconnect()
     
     [HttpPut("settings")]
     public async Task<IActionResult> UpdateSettings([FromBody] BluetoothSettingsDto dto)
     {
       // Update device name, auto-connect, etc.
     }
     
     [HttpGet("devices/paired")]
     public async Task<List<BluetoothDeviceDto>> GetPairedDevices()
     
     [HttpGet("devices/discovered")]
     public async Task<List<BluetoothDeviceDto>> GetDiscoveredDevices()
   }
   ```

2. **Create DTOs** (`src/Radio.API/Models/`)
   - `BluetoothStatusDto`
   - `BluetoothDeviceDto`
   - `StartBluetoothDto`
   - `PairDeviceDto`
   - `ConnectDeviceDto`
   - `BluetoothSettingsDto`

3. **Update `SourcesController`**
   - Include `Bluetooth` in available sources
   - Support switching to Bluetooth source
   - Return Bluetooth connection status

4. **Create SignalR Hub Events** (or extend existing hub)
   ```csharp
   // In VisualizationHub or new BluetoothHub
   
   // Broadcast to clients
   await Clients.All.SendAsync("BluetoothStateChanged", state);
   await Clients.All.SendAsync("BluetoothDeviceConnected", device);
   await Clients.All.SendAsync("BluetoothDeviceDisconnected", device);
   await Clients.All.SendAsync("BluetoothDeviceDiscovered", device);
   ```

5. **Swagger Documentation**
   - Add XML comments to all endpoints
   - Document request/response models
   - Add examples for common scenarios

**Deliverables**:
- `BluetoothController` with full CRUD operations
- DTO models for API communication
- SignalR event broadcasting
- API unit tests
- Updated Swagger documentation

---

### Phase 6: Configuration & Persistence (2 days)

**Objective**: Enable configuration and persist Bluetooth settings.

#### Tasks:

1. **Update `appsettings.example.json`**
   ```json
   {
     "Bluetooth": {
       "DeviceName": "Grandpa's Radio",
       "AutoAcceptConnections": true,
       "RequirePairing": false,
       "EnableOnStartup": true,
       "AutoSwitchOnConnect": true,
       "AudioQuality": "High"
     }
   }
   ```

2. **Configuration Store Integration**
   - Register `BluetoothOptions` with configuration manager
   - Register `BluetoothPreferences` with configuration manager
   - Support dynamic updates via API

3. **Persistence Logic**
   - Save paired devices list on pair/unpair
   - Save last connected device on connection
   - Save trusted devices on user action
   - Restore on startup

4. **Configuration Validation**
   - Device name: 1-248 characters (Bluetooth spec)
   - Device name: Valid UTF-8, properly handle special characters including apostrophes (e.g., "Grandpa's Radio")
   - Audio quality: Valid enum value
   - Note: Ensure Bluetooth device name validation properly handles apostrophes and special characters in UTF-8 encoding, as some Bluetooth stacks may have restrictions on certain characters

5. **Migration Support**
   - Handle missing Bluetooth section gracefully
   - Provide sensible defaults

**Deliverables**:
- Updated `appsettings.example.json`
- Configuration persistence logic
- Configuration validation
- Tests for configuration loading and validation

---

### Phase 7: Visualization & Fingerprinting Verification (1-2 days)

**Objective**: Verify full integration with existing audio pipeline features and add UAT tests.

#### Tasks:

1. **Visualization Testing**
   - Start Bluetooth source
   - Connect device and play audio
   - Verify FFT spectrum updates in real-time
   - Verify level meters show correct dB values
   - Verify waveform display updates
   - Check SignalR hub broadcasts visualization data

2. **Fingerprinting Testing**
   - Play known song via Bluetooth
   - Verify `BackgroundIdentificationService` captures audio
   - Verify track identification updates metadata
   - Check metadata appears in API responses
   - Verify play history records identified tracks

3. **AudioUAT Testing**
   - Add Bluetooth source tests to `Radio.Tools.AudioUAT`
   - Test Bluetooth source initialization
   - Test device connection and disconnection
   - Test audio playback through Bluetooth
   - Test source switching to/from Bluetooth
   - Run smoke test of AudioUAT as part of checkoff

3. **Output Testing**
   - Route Bluetooth audio to local speakers
   - Route to Chromecast
   - Route to HTTP stream
   - Verify all outputs work correctly

4. **Ducking Integration**
   - Play Bluetooth audio
   - Trigger event source (TTS, notification)
   - Verify Bluetooth audio ducks appropriately
   - Verify restoration after event completes

5. **Edge Cases**
   - Device disconnects mid-playback
   - Multiple connection requests
   - Audio format changes
   - Buffer underruns

**Deliverables**:
- Verification test suite
- Edge case tests
- Integration test documentation

---

### Phase 8: Testing (4-5 days)

**Objective**: Comprehensive testing across all layers.

#### 8.1: Unit Tests

**Test Projects**:
- `Radio.Core.Tests/` - Configuration classes
- `Radio.Infrastructure.Tests/Audio/Sources/` - `BluetoothAudioSource` tests
- `Radio.Infrastructure.Tests/Platform/Bluetooth/` - Platform service tests
- `Radio.API.Tests/Controllers/` - `BluetoothController` tests

**Test Coverage**:
- Configuration validation
- Bluetooth service state machine
- Connection/disconnection flows
- Audio source lifecycle
- API endpoint logic
- Error handling
- Target: >80% code coverage for all new code
- Note: All tests use xUnit (established testing framework). If Bluetooth management UI is added in the Web layer (Blazor), corresponding bUnit tests must be created for those components.

**Mocking Strategy**:
- Mock `IBluetoothService` for audio source tests
- Mock platform-specific D-Bus/Windows APIs for service tests
- Use `IOptionsMonitor` test helpers

#### 8.2: Integration Tests

**Test Scenarios**:
1. Start Bluetooth adapter
2. Make device discoverable
3. Pair with mock device
4. Accept connection
5. Switch to Bluetooth source
6. Verify audio flows through mixer
7. Disconnect device
8. Verify cleanup

**Test Projects**:
- `Radio.IntegrationTests/` - End-to-end flows

#### 8.3: Platform-Specific Testing

**Raspberry Pi Testing**:
1. Flash SD card with Raspberry Pi OS
2. Install .NET 8 runtime
3. Deploy application
4. Configure BlueZ
5. Test with real phone/tablet
6. Verify audio quality
7. Measure latency

**Windows Testing**:
1. Test on Windows 10/11
2. Pair real Bluetooth device
3. Verify audio capture
4. Test UI interactions
5. Verify configuration persistence

**Test Checklist**:
- [ ] Adapter starts successfully
- [ ] Device name advertises correctly
- [ ] Phone can discover Radio Console
- [ ] Pairing works (with and without PIN)
- [ ] Audio plays with acceptable latency (<200ms)
- [ ] Audio quality is clear (no dropouts)
- [ ] Visualization updates in real-time
- [ ] Fingerprinting identifies tracks
- [ ] Disconnection handled gracefully
- [ ] Reconnection works automatically
- [ ] Configuration persists across restarts

**Deliverables**:
- Complete unit test suite (>80% coverage)
- Integration test suite
- Platform-specific test results
- Performance and latency measurements
- Bug fixes from testing

---

### Phase 9: Documentation (2-3 days)

**Objective**: Comprehensive documentation for users and developers.

#### 9.1: Design Documentation

**Update `/design/AUDIO.md`**:
- Add "Bluetooth Audio Source" section (consolidate into existing design document to avoid documentation sprawl)
- Document architecture and components
- Explain platform-specific implementations
- Include sequence diagrams
- Document Bluetooth setup and configuration (consolidated section)

**Update `/design/CONFIGURATION.md`**:
- Add `BluetoothOptions` reference
- Add `BluetoothPreferences` reference
- Document configuration schema

**Update `/design/API_REFERENCE.md`**:
- Document all Bluetooth controller endpoints
- Include request/response examples
- Document SignalR events

**Note**: Following the project's preference for consolidated documentation over documentation sprawl, Bluetooth setup guides and architecture details should be added as sections within existing design documents (`AUDIO.md`, `CONFIGURATION.md`) rather than creating separate `BLUETOOTH_SETUP.md` or `BLUETOOTH_ARCHITECTURE.md` files.

#### 9.2: Setup Information (to be added to `/design/AUDIO.md`)

**Raspberry Pi Setup**:
```bash
# Install BlueZ
sudo apt-get update
sudo apt-get install bluez pulseaudio-module-bluetooth

# Configure BlueZ for A2DP sink
sudo nano /etc/bluetooth/main.conf
# Add: Class = 0x200414 (Audio sink)

# Restart BlueZ
sudo systemctl restart bluetooth

# Make device discoverable
bluetoothctl
> discoverable on
> pairable on
> agent NoInputNoOutput
> default-agent
```

**Note**: The BlueZ configuration requires root privileges and manual system configuration. The application should document whether it needs elevated privileges for initial setup, or if there's a way to configure BlueZ programmatically via D-Bus. Consider including a setup script or installation guide that handles these system-level configurations.

**Windows Setup**:
- Enable Bluetooth in Windows Settings
- Ensure Bluetooth drivers are up-to-date
- Configure application permissions

3. **Configuration**
   - Setting device name
   - Configuring auto-accept
   - Pairing requirements

4. **Troubleshooting**
   - "Bluetooth adapter not found"
   - "Audio not routing correctly"
   - "Device won't pair"
   - "Poor audio quality"
   - Platform-specific issues

#### 9.3: User Documentation

**Update `/README.md`**:
- Add Bluetooth to features list
- Update audio sources section:
  ```markdown
  ## Audio Sources
  
  - **Radio**: RTL-SDR and Raddy RF320 USB receivers
  - **Vinyl**: USB turntable input
  - **Spotify**: Integrated Spotify playback
  - **File Player**: Local MP3/FLAC/WAV files
  - **Bluetooth**: Phone/tablet wireless audio (NEW!)
  - **Generic USB**: Any USB audio device
  ```
- Link to Bluetooth setup guide

**Create User Quick Start**:
```markdown
## Using Bluetooth Audio

1. Navigate to Settings > Bluetooth
2. Set device name (e.g., "Grandpa's Radio")
3. Enable "Auto-accept connections"
4. Click "Start Bluetooth"
5. On your phone, search for Bluetooth devices
6. Connect to "Grandpa's Radio"
7. Play audio from any app
```

#### 9.4: Developer Documentation

**Note**: Architecture details should be consolidated into `/design/AUDIO.md` to avoid documentation sprawl, following the project's preference for fewer, more comprehensive documents.

**Code Comments**:
- XML documentation on all public APIs
- Internal implementation comments
- Complex algorithm explanations
- Mark deferred work with clear TODO comments for discoverability

**Deliverables**:
- Updated `/design/AUDIO.md` (with Bluetooth architecture and setup sections)
- Updated `/design/CONFIGURATION.md`
- Updated `/design/API_REFERENCE.md`
- Updated `/README.md`
- User quick start guide

---

## Technical Specifications

### Bluetooth Profiles

**A2DP (Advanced Audio Distribution Profile)**:
- Role: Sink (receiver)
- Codecs: SBC (required), AAC (optional), aptX (optional)
- Sample Rates: 44.1kHz, 48kHz
- Bit Depths: 16-bit

**AVRCP (Audio/Video Remote Control Profile)**:
- Optional: For play/pause/skip controls
- Phase 2 enhancement

### Audio Format

**Input**: Bluetooth A2DP stream
- Sample Rate: 44.1kHz or 48kHz (configurable)
- Channels: 2 (stereo)
- Bit Depth: 16-bit
- Format: PCM

**Output**: Same as other audio sources
- Processed by SoundFlow mixer
- Visualization: FFT at 2048 samples
- Fingerprinting: 10-second samples every 30 seconds

### Performance Targets

- **Latency**: <200ms from Bluetooth to speakers
- **Audio Dropouts**: <0.1% under normal conditions
- **CPU Usage**: <5% on Raspberry Pi 5 (audio processing only)
- **Memory**: <50MB additional for Bluetooth services

### Platform Requirements

**Raspberry Pi (Linux)**:
- Raspberry Pi 4 or newer (Bluetooth 5.0+)
- Raspberry Pi OS (Debian-based)
- BlueZ 5.50+
- PulseAudio 12.0+

**Windows**:
- Windows 10 version 1803+ or Windows 11
- Bluetooth 4.0+ adapter
- .NET 8 runtime

### Security Considerations

1. **Pairing**: Support both secure (PIN) and simple pairing
2. **Device Trust**: Maintain list of trusted devices
3. **Auto-Accept**: Configurable to prevent unauthorized connections
4. **Data Privacy**: No audio recording or transmission beyond local network
5. **DoS Prevention & Rate Limiting**:
   - Implement per-device and global rate limiting for pairing/connection attempts:
     - Per-device: e.g., `MaxPairingAttemptsPerDevicePerMinute` (default: 5)
     - Global: e.g., `MaxPairingAttemptsGlobalPerMinute` (default: 30)
   - Enforce a maximum number of concurrent connections:
     - e.g., `MaxConcurrentBluetoothConnections` (default: 1 primary audio device)
   - Temporary backoff:
     - When a device exceeds per-device limits, block pairing/connection attempts from that device for a cooldown window
     - e.g., `PairingCooldownMinutesPerDevice` (default: 10)
   - Blacklisting:
     - If a device repeatedly exceeds limits over a longer window, add it to a blacklist and refuse further connections until explicitly removed
     - Track violation counts per device (e.g., MAC address / unique ID) and persist with existing JSON/SQLite persistence
     - Example thresholds:
       - `MaxDosViolationsBeforeBlacklist` (default: 3)
       - `BlacklistDurationHours` (default: 24; 0 = permanent until manual removal)
   - Configuration:
     - All above limits should be configurable via application configuration (e.g., `appsettings.json`):
       - `Bluetooth.Security.MaxPairingAttemptsPerDevicePerMinute`
       - `Bluetooth.Security.MaxPairingAttemptsGlobalPerMinute`
       - `Bluetooth.Security.MaxConcurrentBluetoothConnections`
       - `Bluetooth.Security.PairingCooldownMinutesPerDevice`
       - `Bluetooth.Security.MaxDosViolationsBeforeBlacklist`
       - `Bluetooth.Security.BlacklistDurationHours`
     - The Bluetooth connection management services (Linux/Windows) must read these values via DI-provided configuration and enforce them consistently.
   - Monitoring & logging:
     - Log rate-limit hits and blacklist events via Serilog with structured properties (device ID, counts, timestamps) to support diagnostics
     - Consider exposing aggregated counters via metrics (e.g., for future observability tooling)

## Dependencies

### NuGet Packages

**Linux**:
- `Tmds.DBus` (v0.15+) - D-Bus communication with BlueZ
- Existing: `SoundFlow`, `Microsoft.Extensions.*`

**Windows**:
- `InTheHand.Net.Bluetooth` (v4.1+) - 32feet.NET
  OR
- Built-in Windows SDK APIs
- Existing: `SoundFlow`, `Microsoft.Extensions.*`

### System Dependencies

**Raspberry Pi**:
```bash
sudo apt-get install \
  bluez \
  pulseaudio-module-bluetooth \
  libasound2-dev
```

**Windows**:
- No additional system dependencies (built-in Bluetooth stack)

## Risk Mitigation

### Technical Risks

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| BlueZ D-Bus complexity | High | Medium | Use proven library (Tmds.DBus), extensive testing |
| Audio latency on Pi | Medium | Medium | Optimize buffer sizes, use hardware acceleration |
| Windows Bluetooth API limitations | High | Low | Research 32feet.NET capabilities early, fallback to Windows APIs |
| Platform differences | Medium | High | Strong abstraction layer, platform-specific test plans |
| Device compatibility | Medium | Medium | Test with multiple phone/tablet models |

### Operational Risks

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Bluetooth interference | Low | Medium | Document troubleshooting, support channel selection |
| User pairing confusion | Low | High | Clear UI instructions, automatic pairing option |
| Configuration complexity | Low | Low | Sensible defaults, guided setup wizard |

## Timeline Estimate

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| 1. Core Architecture | 2-3 days | None |
| 2. Platform Implementations | 5-7 days | Phase 1 |
| 3. Audio Source | 3-4 days | Phase 1, 2 |
| 4. Audio Manager Integration | 2-3 days | Phase 3 |
| 5. API & Control | 3-4 days | Phase 4 |
| 6. Configuration | 2 days | Phase 5 |
| 7. Verification | 1-2 days | Phase 6 |
| 8. Testing | 4-5 days | Phase 7 |
| 9. Documentation | 2-3 days | Phase 8 |

**Total Estimated Duration**: 24-35 days (≈3.5-5 weeks)

**Critical Path**: Phases 1 → 2 → 3 → 4 → 8

**Progress Tracking**: Update PROJECT_STATUS.md to reflect Bluetooth implementation phases in progress, completed, and planned future phases.

## Success Criteria

### Functional Requirements
- ✅ Bluetooth adapter starts on system boot (configurable)
- ✅ Device name is user-configurable
- ✅ Phone/tablet can discover and pair with system
- ✅ Audio plays through Bluetooth with <200ms latency
- ✅ Bluetooth audio integrates with visualization
- ✅ Bluetooth audio integrates with fingerprinting
- ✅ Connection/disconnection handled gracefully
- ✅ API provides full Bluetooth control
- ✅ Configuration persists across restarts
- ✅ Works on both Raspberry Pi and Windows

### Non-Functional Requirements
- ✅ Code coverage >80%
- ✅ No regressions in existing audio sources
- ✅ Comprehensive documentation
- ✅ Follows existing code style and patterns
- ✅ Performance within targets
- ✅ Secure pairing and connection management

## Future Enhancements (Out of Scope)

**Note**: If any of these features are partially implemented or scaffolded during the main implementation, they must be marked with clear TODO comments for easy discoverability, and these should be noted in followup documentation.

1. **AVRCP Support**: Media controls from phone (play/pause/skip) - TODO if scaffolded
2. **Multiple Simultaneous Connections**: Support 2+ devices at once - TODO if scaffolded
3. **Codec Selection**: Manual codec preference (AAC, aptX) - TODO if scaffolded
4. **Bluetooth Source**: Transmit audio to Bluetooth speakers (inverse of this project) - TODO if scaffolded
5. **Hands-Free Profile**: Phone call support (HFP) - TODO if scaffolded
6. **UI Enhancements**: Blazor pages for Bluetooth management - TODO if scaffolded
7. **Automatic Device Switching**: Priority-based device selection - TODO if scaffolded
8. **Connection History**: Log of past connections with statistics - TODO if scaffolded

## Conclusion

This comprehensive plan provides a clear roadmap for adding Bluetooth audio input to the Radio Console system. The implementation follows existing architectural patterns, maintains cross-platform compatibility, and integrates seamlessly with the current audio pipeline. Upon completion, users will be able to connect their phones and tablets to the Radio Console as if it were a regular Bluetooth speaker, with full access to visualization and fingerprinting features.

The phased approach allows for incremental development, testing, and validation at each stage. Platform-specific implementations are isolated, making the system maintainable and extensible. Comprehensive testing and documentation ensure a production-ready feature.

## Next Steps

After review and approval of this plan:

1. Create GitHub issues for each phase
2. Assign priority labels and milestones
3. Begin Phase 1 implementation
4. Schedule weekly progress reviews
5. Conduct user acceptance testing after Phase 7
6. Deploy to staging environment after Phase 8
7. Production rollout after Phase 9

---

**Document Version**: 1.0  
**Date**: 2026-02-06  
**Status**: Pending Review  
**Author**: AI Assistant (Claude)
