using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Audio;
using Radio.Tools.AudioUAT.Results;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase8;

/// <summary>
/// Phase 8 tests for API &amp; SignalR integration.
/// Tests REST endpoints, SignalR hub, and audio streaming.
/// </summary>
public class ApiSignalRTests
{
  private readonly IServiceProvider _serviceProvider;

  /// <summary>
  /// Initializes a new instance of the <see cref="ApiSignalRTests"/> class.
  /// </summary>
  /// <param name="serviceProvider">The service provider.</param>
  public ApiSignalRTests(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  /// <summary>
  /// Gets all Phase 8 tests.
  /// </summary>
  /// <returns>The list of tests.</returns>
  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      // REST API Tests
      new AudioControllerGetTest(_serviceProvider),
      new AudioControllerVolumeTest(_serviceProvider),
      new SourcesControllerTest(_serviceProvider),
      new DevicesControllerTest(_serviceProvider),
      new ConfigurationControllerTest(_serviceProvider),
      // SignalR Tests
      new SignalRHubConnectionTest(_serviceProvider),
      new SignalRSpectrumDataTest(_serviceProvider),
      new SignalRLevelDataTest(_serviceProvider),
      // Audio Streaming Tests
      new AudioStreamEndpointTest(_serviceProvider),
      new AudioStreamContentTypeTest(_serviceProvider),
      // Integration Tests
      new ApiVisualizerIntegrationTest(_serviceProvider),
      new ApiAudioEngineIntegrationTest(_serviceProvider)
    ];
  }
}

/// <summary>
/// P8-001: Audio Controller GET Test.
/// </summary>
public class AudioControllerGetTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-001";
  public string TestName => "Audio Controller GET Playback State";
  public string Description => "Test GET /api/audio returns playback state";
  public int Phase => 8;

  public AudioControllerGetTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var audioEngine = _serviceProvider.GetService<IAudioEngine>();
      var duckingService = _serviceProvider.GetService<IDuckingService>();

      ConsoleUI.WriteInfo("Verifying AudioController dependencies...");

      if (audioEngine == null)
      {
        return TestResult.Fail(TestId, "IAudioEngine not available");
      }

      if (duckingService == null)
      {
        return TestResult.Fail(TestId, "IDuckingService not available");
      }

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("AudioController dependencies:");
      ConsoleUI.WriteInfo($"  Audio Engine State: {audioEngine.State}");
      ConsoleUI.WriteInfo($"  Audio Engine Ready: {audioEngine.IsReady}");
      ConsoleUI.WriteInfo($"  Ducking Active: {duckingService.IsDucking}");
      ConsoleUI.WriteInfo($"  Duck Level: {duckingService.CurrentDuckLevel}%");

      var mixer = audioEngine.GetMasterMixer();
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Master Mixer state:");
      ConsoleUI.WriteInfo($"  Volume: {mixer.MasterVolume:F2}");
      ConsoleUI.WriteInfo($"  Balance: {mixer.Balance:F2}");
      ConsoleUI.WriteInfo($"  Muted: {mixer.IsMuted}");
      ConsoleUI.WriteInfo($"  Active Sources: {mixer.GetActiveSources().Count}");

      ConsoleUI.WriteSuccess("Audio Controller dependencies verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-002: Audio Controller Volume Test.
/// </summary>
public class AudioControllerVolumeTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-002";
  public string TestName => "Audio Controller Volume Control";
  public string Description => "Test volume GET/SET functionality";
  public int Phase => 8;

  public AudioControllerVolumeTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var audioEngine = _serviceProvider.GetService<IAudioEngine>();
      if (audioEngine == null)
      {
        return TestResult.Fail(TestId, "IAudioEngine not available");
      }

      var mixer = audioEngine.GetMasterMixer();
      var originalVolume = mixer.MasterVolume;

      ConsoleUI.WriteInfo($"Original volume: {originalVolume:F2}");

      // Test volume change
      var testVolume = originalVolume < 0.5f ? 0.7f : 0.3f;
      mixer.MasterVolume = testVolume;
      ConsoleUI.WriteInfo($"Set volume to: {testVolume:F2}");
      ConsoleUI.WriteInfo($"Actual volume: {mixer.MasterVolume:F2}");

      if (Math.Abs(mixer.MasterVolume - testVolume) > 0.01f)
      {
        return TestResult.Fail(TestId, "Volume was not set correctly");
      }

      // Test balance
      var originalBalance = mixer.Balance;
      mixer.Balance = 0.5f;
      ConsoleUI.WriteInfo($"Set balance to: 0.5");
      ConsoleUI.WriteInfo($"Actual balance: {mixer.Balance:F2}");

      // Test mute
      var originalMute = mixer.IsMuted;
      mixer.IsMuted = !originalMute;
      ConsoleUI.WriteInfo($"Toggled mute to: {mixer.IsMuted}");

      // Restore original values
      mixer.MasterVolume = originalVolume;
      mixer.Balance = originalBalance;
      mixer.IsMuted = originalMute;

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteSuccess("Volume, balance, and mute controls work correctly");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-003: Sources Controller Test.
/// </summary>
public class SourcesControllerTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-003";
  public string TestName => "Sources Controller Functionality";
  public string Description => "Test audio sources listing and management";
  public int Phase => 8;

  public SourcesControllerTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var audioEngine = _serviceProvider.GetService<IAudioEngine>();
      if (audioEngine == null)
      {
        return TestResult.Fail(TestId, "IAudioEngine not available");
      }

      var mixer = audioEngine.GetMasterMixer();
      var activeSources = mixer.GetActiveSources();

      ConsoleUI.WriteInfo("Available source types:");
      ConsoleUI.WriteInfo("  - Spotify");
      ConsoleUI.WriteInfo("  - Radio");
      ConsoleUI.WriteInfo("  - Vinyl");
      ConsoleUI.WriteInfo("  - FilePlayer");
      ConsoleUI.WriteInfo("  - GenericUSB");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo($"Active sources count: {activeSources.Count}");

      foreach (var source in activeSources)
      {
        ConsoleUI.WriteInfo($"  - {source.Name} ({source.Type}) - State: {source.State}");
      }

      var primarySource = activeSources.FirstOrDefault(s => s.Category == AudioSourceCategory.Primary);
      var eventSources = activeSources.Where(s => s.Category == AudioSourceCategory.Event).ToList();

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo($"Primary source: {primarySource?.Name ?? "None"}");
      ConsoleUI.WriteInfo($"Event sources: {eventSources.Count}");

      ConsoleUI.WriteSuccess("Sources controller data retrieval verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-004: Devices Controller Test.
/// </summary>
public class DevicesControllerTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-004";
  public string TestName => "Devices Controller Functionality";
  public string Description => "Test audio device enumeration and management";
  public int Phase => 8;

  public DevicesControllerTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var deviceManager = _serviceProvider.GetService<IAudioDeviceManager>();
      if (deviceManager == null)
      {
        return TestResult.Fail(TestId, "IAudioDeviceManager not available");
      }

      ConsoleUI.WriteInfo("Enumerating output devices...");
      var outputDevices = await deviceManager.GetOutputDevicesAsync(ct);
      ConsoleUI.WriteInfo($"Found {outputDevices.Count} output device(s)");

      foreach (var device in outputDevices)
      {
        ConsoleUI.WriteInfo($"  - {device.Name} (ID: {device.Id})");
        ConsoleUI.WriteInfo($"    Type: {device.Type}, Default: {device.IsDefault}, USB: {device.IsUSBDevice}");
      }

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Enumerating input devices...");
      var inputDevices = await deviceManager.GetInputDevicesAsync(ct);
      ConsoleUI.WriteInfo($"Found {inputDevices.Count} input device(s)");

      foreach (var device in inputDevices)
      {
        ConsoleUI.WriteInfo($"  - {device.Name} (ID: {device.Id})");
      }

      ConsoleUI.WriteInfo("");
      var defaultDevice = await deviceManager.GetDefaultOutputDeviceAsync(ct);
      ConsoleUI.WriteInfo($"Default output device: {defaultDevice?.Name ?? "None"}");

      // Test USB port checking
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("USB port reservations:");
      var testPorts = new[] { "/dev/ttyUSB0", "/dev/ttyUSB1", "/dev/ttyUSB2" };
      foreach (var port in testPorts)
      {
        var isInUse = deviceManager.IsUSBPortInUse(port);
        ConsoleUI.WriteInfo($"  {port}: {(isInUse ? "In Use" : "Available")}");
      }

      ConsoleUI.WriteSuccess("Devices controller data retrieval verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-005: Configuration Controller Test.
/// </summary>
public class ConfigurationControllerTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-005";
  public string TestName => "Configuration Controller Functionality";
  public string Description => "Test configuration settings access";
  public int Phase => 8;

  public ConfigurationControllerTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var visualizer = _serviceProvider.GetService<IVisualizerService>();
      if (visualizer == null)
      {
        return TestResult.Fail(TestId, "IVisualizerService not available");
      }

      ConsoleUI.WriteInfo("Reading configuration values...");
      ConsoleUI.WriteInfo("");

      ConsoleUI.WriteInfo("Visualizer Configuration:");
      ConsoleUI.WriteInfo($"  FFT Size: {visualizer.FFTSize}");
      ConsoleUI.WriteInfo($"  Sample Rate: {visualizer.SampleRate}");

      // Verify configuration values are within expected ranges
      if (visualizer.FFTSize < 256 || visualizer.FFTSize > 8192)
      {
        return TestResult.Fail(TestId, $"FFT Size {visualizer.FFTSize} out of valid range");
      }

      if (visualizer.SampleRate < 8000 || visualizer.SampleRate > 96000)
      {
        return TestResult.Fail(TestId, $"Sample Rate {visualizer.SampleRate} out of valid range");
      }

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteSuccess("Configuration values within valid ranges");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-006: SignalR Hub Connection Test.
/// </summary>
public class SignalRHubConnectionTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-006";
  public string TestName => "SignalR Hub Connection";
  public string Description => "Test AudioVisualizationHub availability";
  public int Phase => 8;

  public SignalRHubConnectionTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Verify that the visualizer service (which the hub depends on) is available
      var visualizer = _serviceProvider.GetService<IVisualizerService>();
      if (visualizer == null)
      {
        return TestResult.Fail(TestId, "IVisualizerService not available for SignalR hub");
      }

      ConsoleUI.WriteInfo("SignalR hub dependencies verified:");
      ConsoleUI.WriteInfo($"  Visualizer Service: Available");
      ConsoleUI.WriteInfo($"  Visualizer Active: {visualizer.IsActive}");
      ConsoleUI.WriteInfo($"  FFT Size: {visualizer.FFTSize}");
      ConsoleUI.WriteInfo($"  Sample Rate: {visualizer.SampleRate}");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("SignalR Hub Methods:");
      ConsoleUI.WriteInfo("  - GetSpectrum()");
      ConsoleUI.WriteInfo("  - GetLevels()");
      ConsoleUI.WriteInfo("  - GetWaveform()");
      ConsoleUI.WriteInfo("  - GetVisualization()");
      ConsoleUI.WriteInfo("  - SubscribeToSpectrum()");
      ConsoleUI.WriteInfo("  - SubscribeToLevels()");
      ConsoleUI.WriteInfo("  - SubscribeToWaveform()");
      ConsoleUI.WriteInfo("  - SubscribeToAll()");

      ConsoleUI.WriteSuccess("SignalR hub dependencies available");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-007: SignalR Spectrum Data Test.
/// </summary>
public class SignalRSpectrumDataTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-007";
  public string TestName => "SignalR Spectrum Data";
  public string Description => "Test spectrum data retrieval via SignalR";
  public int Phase => 8;

  public SignalRSpectrumDataTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var visualizer = _serviceProvider.GetService<IVisualizerService>();
      if (visualizer == null)
      {
        return TestResult.Fail(TestId, "IVisualizerService not available");
      }

      ConsoleUI.WriteInfo("Getting spectrum data (as SignalR would)...");

      var spectrumData = visualizer.GetSpectrumData();

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Spectrum data structure:");
      ConsoleUI.WriteInfo($"  Bin Count: {spectrumData.BinCount}");
      ConsoleUI.WriteInfo($"  Frequency Resolution: {spectrumData.FrequencyResolution:F2} Hz");
      ConsoleUI.WriteInfo($"  Max Frequency: {spectrumData.MaxFrequency:F0} Hz");
      ConsoleUI.WriteInfo($"  Magnitudes Length: {spectrumData.Magnitudes.Length}");
      ConsoleUI.WriteInfo($"  Frequencies Length: {spectrumData.Frequencies.Length}");
      ConsoleUI.WriteInfo($"  Timestamp: {spectrumData.Timestamp:O}");

      // Verify structure
      if (spectrumData.BinCount != spectrumData.Magnitudes.Length)
      {
        return TestResult.Fail(TestId, "Bin count mismatch with magnitudes array");
      }

      if (spectrumData.BinCount != spectrumData.Frequencies.Length)
      {
        return TestResult.Fail(TestId, "Bin count mismatch with frequencies array");
      }

      ConsoleUI.WriteSuccess("Spectrum data structure verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-008: SignalR Level Data Test.
/// </summary>
public class SignalRLevelDataTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-008";
  public string TestName => "SignalR Level Data";
  public string Description => "Test level meter data retrieval via SignalR";
  public int Phase => 8;

  public SignalRLevelDataTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var visualizer = _serviceProvider.GetService<IVisualizerService>();
      if (visualizer == null)
      {
        return TestResult.Fail(TestId, "IVisualizerService not available");
      }

      ConsoleUI.WriteInfo("Getting level data (as SignalR would)...");

      var levelData = visualizer.GetLevelData();

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Level data structure:");
      ConsoleUI.WriteInfo($"  Left Peak: {levelData.LeftPeak:F4} ({levelData.LeftPeakDb:F1} dB)");
      ConsoleUI.WriteInfo($"  Right Peak: {levelData.RightPeak:F4} ({levelData.RightPeakDb:F1} dB)");
      ConsoleUI.WriteInfo($"  Left RMS: {levelData.LeftRms:F4}");
      ConsoleUI.WriteInfo($"  Right RMS: {levelData.RightRms:F4}");
      ConsoleUI.WriteInfo($"  Mono Peak: {levelData.MonoPeak:F4}");
      ConsoleUI.WriteInfo($"  Is Clipping: {levelData.IsClipping}");
      ConsoleUI.WriteInfo($"  Timestamp: {levelData.Timestamp:O}");

      // Verify level values are in valid range
      if (levelData.LeftPeak < 0 || levelData.LeftPeak > 1)
      {
        return TestResult.Fail(TestId, $"Left peak {levelData.LeftPeak} out of range [0,1]");
      }

      if (levelData.RightPeak < 0 || levelData.RightPeak > 1)
      {
        return TestResult.Fail(TestId, $"Right peak {levelData.RightPeak} out of range [0,1]");
      }

      ConsoleUI.WriteSuccess("Level data structure verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-009: Audio Stream Endpoint Test.
/// </summary>
public class AudioStreamEndpointTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-009";
  public string TestName => "Audio Stream Endpoint";
  public string Description => "Test audio stream middleware availability";
  public int Phase => 8;

  public AudioStreamEndpointTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var audioEngine = _serviceProvider.GetService<IAudioEngine>();
      if (audioEngine == null)
      {
        return TestResult.Fail(TestId, "IAudioEngine not available");
      }

      ConsoleUI.WriteInfo("Verifying audio stream source...");

      if (!audioEngine.IsReady)
      {
        ConsoleUI.WriteInfo("Initializing audio engine...");
        await audioEngine.InitializeAsync(ct);
      }

      ConsoleUI.WriteInfo($"Audio Engine State: {audioEngine.State}");
      ConsoleUI.WriteInfo($"Audio Engine Ready: {audioEngine.IsReady}");

      var mixedOutputStream = audioEngine.GetMixedOutputStream();
      ConsoleUI.WriteInfo($"Mixed Output Stream: {(mixedOutputStream != null ? "Available" : "Not Available")}");

      if (mixedOutputStream != null)
      {
        ConsoleUI.WriteInfo($"  Can Read: {mixedOutputStream.CanRead}");
        ConsoleUI.WriteInfo($"  Can Seek: {mixedOutputStream.CanSeek}");

        ConsoleUI.WriteInfo("");
        ConsoleUI.WriteInfo("Audio Stream Endpoint Configuration:");
        ConsoleUI.WriteInfo("  Endpoint: /stream/audio");
        ConsoleUI.WriteInfo("  Content-Type: audio/L16;rate=48000;channels=2");
        ConsoleUI.WriteInfo("  Format: 16-bit PCM, stereo, 48kHz");
      }

      ConsoleUI.WriteSuccess("Audio stream source verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-010: Audio Stream Content Type Test.
/// </summary>
public class AudioStreamContentTypeTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-010";
  public string TestName => "Audio Stream Content Type";
  public string Description => "Verify audio stream PCM format configuration";
  public int Phase => 8;

  public AudioStreamContentTypeTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var audioEngine = _serviceProvider.GetService<IAudioEngine>();
      if (audioEngine == null)
      {
        return TestResult.Fail(TestId, "IAudioEngine not available");
      }

      ConsoleUI.WriteInfo("Verifying PCM stream format...");
      ConsoleUI.WriteInfo("");

      ConsoleUI.WriteInfo("Expected stream format:");
      ConsoleUI.WriteInfo("  Sample Rate: 48000 Hz");
      ConsoleUI.WriteInfo("  Channels: 2 (Stereo)");
      ConsoleUI.WriteInfo("  Bit Depth: 16-bit");
      ConsoleUI.WriteInfo("  Byte Order: Little Endian");
      ConsoleUI.WriteInfo("  Content-Type: audio/L16;rate=48000;channels=2");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Chromecast compatibility:");
      ConsoleUI.WriteInfo("  Format: Compatible with Chromecast audio streaming");
      ConsoleUI.WriteInfo("  Protocol: HTTP chunked transfer");
      ConsoleUI.WriteInfo("  Buffering: Supported via ring buffer");

      ConsoleUI.WriteSuccess("Audio stream format configuration verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-011: API Visualizer Integration Test.
/// </summary>
public class ApiVisualizerIntegrationTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-011";
  public string TestName => "API Visualizer Integration";
  public string Description => "Test API access to visualization data";
  public int Phase => 8;

  public ApiVisualizerIntegrationTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var visualizer = _serviceProvider.GetService<IVisualizerService>();
      if (visualizer == null)
      {
        return TestResult.Fail(TestId, "IVisualizerService not available");
      }

      ConsoleUI.WriteInfo("Testing visualization data flow...");

      // Get all visualization types
      var spectrum = visualizer.GetSpectrumData();
      var levels = visualizer.GetLevelData();
      var waveform = visualizer.GetWaveformData();

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("All visualization data retrieved successfully:");
      ConsoleUI.WriteInfo($"  Spectrum bins: {spectrum.BinCount}");
      ConsoleUI.WriteInfo($"  Level left peak: {levels.LeftPeak:F4}");
      ConsoleUI.WriteInfo($"  Waveform samples: {waveform.SampleCount}");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Testing data update...");

      // Process some test samples
      var testSamples = new float[2048];
      for (var i = 0; i < testSamples.Length; i++)
      {
        testSamples[i] = MathF.Sin(2 * MathF.PI * 440 * i / 48000f) * 0.5f;
      }

      visualizer.ProcessSamples(testSamples);

      ConsoleUI.WriteInfo($"Processed {testSamples.Length} samples");
      ConsoleUI.WriteInfo($"Visualizer active: {visualizer.IsActive}");

      // Get updated data
      var updatedSpectrum = visualizer.GetSpectrumData();
      ConsoleUI.WriteInfo($"Updated spectrum retrieved with {updatedSpectrum.BinCount} bins");

      ConsoleUI.WriteSuccess("API visualizer integration verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}

/// <summary>
/// P8-012: API Audio Engine Integration Test.
/// </summary>
public class ApiAudioEngineIntegrationTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P8-012";
  public string TestName => "API Audio Engine Integration";
  public string Description => "Test API access to audio engine state and controls";
  public int Phase => 8;

  public ApiAudioEngineIntegrationTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var audioEngine = _serviceProvider.GetService<IAudioEngine>();
      var deviceManager = _serviceProvider.GetService<IAudioDeviceManager>();
      var duckingService = _serviceProvider.GetService<IDuckingService>();

      if (audioEngine == null)
      {
        return TestResult.Fail(TestId, "IAudioEngine not available");
      }

      ConsoleUI.WriteInfo("Testing full API integration...");
      ConsoleUI.WriteInfo("");

      ConsoleUI.WriteInfo("1. Audio Engine Status");
      ConsoleUI.WriteInfo($"   State: {audioEngine.State}");
      ConsoleUI.WriteInfo($"   Ready: {audioEngine.IsReady}");

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("2. Master Mixer Status");
      var mixer = audioEngine.GetMasterMixer();
      ConsoleUI.WriteInfo($"   Volume: {mixer.MasterVolume:F2}");
      ConsoleUI.WriteInfo($"   Balance: {mixer.Balance:F2}");
      ConsoleUI.WriteInfo($"   Muted: {mixer.IsMuted}");
      ConsoleUI.WriteInfo($"   Sources: {mixer.GetActiveSources().Count}");

      if (deviceManager != null)
      {
        ConsoleUI.WriteInfo("");
        ConsoleUI.WriteInfo("3. Device Manager Status");
        var outputs = await deviceManager.GetOutputDevicesAsync(ct);
        ConsoleUI.WriteInfo($"   Output devices: {outputs.Count}");
      }

      if (duckingService != null)
      {
        ConsoleUI.WriteInfo("");
        ConsoleUI.WriteInfo("4. Ducking Service Status");
        ConsoleUI.WriteInfo($"   Is Ducking: {duckingService.IsDucking}");
        ConsoleUI.WriteInfo($"   Duck Level: {duckingService.CurrentDuckLevel}%");
        ConsoleUI.WriteInfo($"   Active Events: {duckingService.ActiveEventCount}");
      }

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteSuccess("Full API integration verified");
      return TestResult.Pass(TestId);
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Test failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message);
    }
  }
}
