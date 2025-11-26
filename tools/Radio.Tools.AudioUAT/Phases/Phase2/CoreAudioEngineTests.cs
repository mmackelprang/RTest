using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Audio;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase2;

/// <summary>
/// Phase 2 tests for Core Audio Engine functionality.
/// </summary>
public class CoreAudioEngineTests
{
  private readonly IServiceProvider _serviceProvider;

  /// <summary>
  /// Initializes a new instance of the <see cref="CoreAudioEngineTests"/> class.
  /// </summary>
  /// <param name="serviceProvider">The service provider.</param>
  public CoreAudioEngineTests(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  /// <summary>
  /// Gets all Phase 2 tests.
  /// </summary>
  /// <returns>The list of tests.</returns>
  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new EngineInitializationTest(_serviceProvider),
      new EngineStartStopTest(_serviceProvider),
      new DeviceEnumerationTest(_serviceProvider),
      new DefaultDeviceDetectionTest(_serviceProvider),
      new DeviceSelectionTest(_serviceProvider),
      new USBDeviceDetectionTest(_serviceProvider),
      new HotPlugDetectionTest(_serviceProvider),
      new MasterVolumeControlTest(_serviceProvider),
      new MasterMuteToggleTest(_serviceProvider),
      new BalanceControlTest(_serviceProvider),
      new OutputStreamTapTest(_serviceProvider),
      new EngineErrorRecoveryTest(_serviceProvider)
    ];
  }
}

/// <summary>
/// P2-001: Engine Initialization Test.
/// </summary>
public class EngineInitializationTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-001";
  public string TestName => "Engine Initialization";
  public string Description => "Initialize SoundFlow with MiniAudio backend";
  public int Phase => 2;

  public EngineInitializationTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var engine = _serviceProvider.GetRequiredService<IAudioEngine>();

      ConsoleUI.WriteInfo("Checking initial state...");
      if (engine.State != AudioEngineState.Uninitialized)
      {
        return TestResult.Fail(TestId, $"Expected state Uninitialized, got {engine.State}");
      }
      ConsoleUI.WriteSuccess($"Initial state: {engine.State}");

      ConsoleUI.WriteInfo("Initializing audio engine...");
      await engine.InitializeAsync(ct);

      if (engine.State != AudioEngineState.Ready)
      {
        return TestResult.Fail(TestId, $"Expected state Ready after init, got {engine.State}");
      }

      ConsoleUI.WriteSuccess($"Engine initialized successfully, state: {engine.State}");

      if (!engine.IsReady)
      {
        return TestResult.Fail(TestId, "IsReady should be true after initialization");
      }
      ConsoleUI.WriteSuccess("IsReady property is true");

      return TestResult.Pass(TestId, "Engine initialized successfully");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Initialization failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-002: Engine Start/Stop Test.
/// </summary>
public class EngineStartStopTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-002";
  public string TestName => "Engine Start/Stop";
  public string Description => "Start and stop audio engine, verify state transitions";
  public int Phase => 2;

  public EngineStartStopTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var engine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Ensure engine is initialized
      if (engine.State == AudioEngineState.Uninitialized)
      {
        ConsoleUI.WriteInfo("Engine not initialized, initializing...");
        await engine.InitializeAsync(ct);
      }

      ConsoleUI.WriteInfo($"Current state: {engine.State}");

      // Start the engine
      ConsoleUI.WriteInfo("Starting audio engine...");
      await engine.StartAsync(ct);

      if (engine.State != AudioEngineState.Running)
      {
        return TestResult.Fail(TestId, $"Expected state Running after start, got {engine.State}");
      }
      ConsoleUI.WriteSuccess($"Engine started, state: {engine.State}");

      // Wait a moment
      await Task.Delay(500, ct);

      // Stop the engine
      ConsoleUI.WriteInfo("Stopping audio engine...");
      await engine.StopAsync(ct);

      if (engine.State != AudioEngineState.Ready)
      {
        return TestResult.Fail(TestId, $"Expected state Ready after stop, got {engine.State}");
      }
      ConsoleUI.WriteSuccess($"Engine stopped, state: {engine.State}");

      return TestResult.Pass(TestId, "Start/Stop transitions work correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Start/Stop test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-003: Device Enumeration Test.
/// </summary>
public class DeviceEnumerationTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-003";
  public string TestName => "Device Enumeration";
  public string Description => "List all audio output devices";
  public int Phase => 2;

  public DeviceEnumerationTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var deviceManager = _serviceProvider.GetRequiredService<IAudioDeviceManager>();

      ConsoleUI.WriteInfo("Enumerating output devices...");
      var devices = await deviceManager.GetOutputDevicesAsync(ct);

      if (devices.Count == 0)
      {
        ConsoleUI.WriteWarning("No output devices found");
        return TestResult.Fail(TestId, "No output devices found");
      }

      ConsoleUI.WriteSuccess($"Found {devices.Count} output device(s):");

      var deviceList = devices.Select(d => (
        d.Id,
        d.Name,
        d.Type.ToString(),
        d.IsDefault,
        d.IsUSBDevice
      )).ToList();

      ConsoleUI.DisplayDeviceList(deviceList);

      // Also enumerate input devices
      ConsoleUI.WriteInfo("Enumerating input devices...");
      var inputDevices = await deviceManager.GetInputDevicesAsync(ct);
      ConsoleUI.WriteSuccess($"Found {inputDevices.Count} input device(s)");

      if (inputDevices.Count > 0)
      {
        var inputList = inputDevices.Select(d => (
          d.Id,
          d.Name,
          d.Type.ToString(),
          d.IsDefault,
          d.IsUSBDevice
        )).ToList();
        ConsoleUI.DisplayDeviceList(inputList);
      }

      return TestResult.Pass(TestId, $"Found {devices.Count} output and {inputDevices.Count} input devices",
        metadata: new Dictionary<string, object>
        {
          ["OutputDeviceCount"] = devices.Count,
          ["InputDeviceCount"] = inputDevices.Count
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Device enumeration failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-004: Default Device Detection Test.
/// </summary>
public class DefaultDeviceDetectionTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-004";
  public string TestName => "Default Device Detection";
  public string Description => "Identify default audio output device";
  public int Phase => 2;

  public DefaultDeviceDetectionTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var deviceManager = _serviceProvider.GetRequiredService<IAudioDeviceManager>();

      ConsoleUI.WriteInfo("Getting default output device...");
      var defaultDevice = await deviceManager.GetDefaultOutputDeviceAsync(ct);

      if (defaultDevice == null)
      {
        ConsoleUI.WriteWarning("No default device found");
        return TestResult.Fail(TestId, "No default output device found");
      }

      ConsoleUI.WriteSuccess($"Default device: {defaultDevice.Name}");
      ConsoleUI.WriteInfo($"  ID: {defaultDevice.Id}");
      ConsoleUI.WriteInfo($"  Type: {defaultDevice.Type}");
      ConsoleUI.WriteInfo($"  Max Channels: {defaultDevice.MaxChannels}");

      if (defaultDevice.AlsaDeviceId != null)
      {
        ConsoleUI.WriteInfo($"  ALSA ID: {defaultDevice.AlsaDeviceId}");
      }

      if (defaultDevice.SupportedSampleRates.Length > 0)
      {
        ConsoleUI.WriteInfo($"  Sample Rates: {string.Join(", ", defaultDevice.SupportedSampleRates)}");
      }

      return TestResult.Pass(TestId, $"Default device: {defaultDevice.Name}",
        metadata: new Dictionary<string, object>
        {
          ["DeviceId"] = defaultDevice.Id,
          ["DeviceName"] = defaultDevice.Name,
          ["MaxChannels"] = defaultDevice.MaxChannels
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Default device detection failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-005: Device Selection Test.
/// </summary>
public class DeviceSelectionTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-005";
  public string TestName => "Device Selection";
  public string Description => "Switch between output devices";
  public int Phase => 2;

  public DeviceSelectionTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var deviceManager = _serviceProvider.GetRequiredService<IAudioDeviceManager>();

      var devices = await deviceManager.GetOutputDevicesAsync(ct);

      if (devices.Count == 0)
      {
        return TestResult.Skip(TestId, "No output devices available for selection test");
      }

      ConsoleUI.WriteInfo("Available devices:");
      for (var i = 0; i < devices.Count; i++)
      {
        var marker = devices[i].IsDefault ? " [DEFAULT]" : "";
        ConsoleUI.WriteInfo($"  [{i + 1}] {devices[i].Name}{marker}");
      }

      if (devices.Count == 1)
      {
        ConsoleUI.WriteWarning("Only one device available, selecting it...");
      }

      // Select the first device
      var device = devices[0];
      ConsoleUI.WriteInfo($"Selecting device: {device.Name}");
      await deviceManager.SetOutputDeviceAsync(device.Id, ct);
      ConsoleUI.WriteSuccess("Device selected successfully");

      // If there's a second device, try selecting it too
      if (devices.Count > 1)
      {
        var secondDevice = devices[1];
        ConsoleUI.WriteInfo($"Switching to device: {secondDevice.Name}");
        await deviceManager.SetOutputDeviceAsync(secondDevice.Id, ct);
        ConsoleUI.WriteSuccess("Switched to second device");

        // Switch back
        ConsoleUI.WriteInfo($"Switching back to: {device.Name}");
        await deviceManager.SetOutputDeviceAsync(device.Id, ct);
        ConsoleUI.WriteSuccess("Switched back successfully");
      }

      var askConfirmation = ConsoleUI.Confirm("Did the device selection work correctly?");
      if (!askConfirmation)
      {
        return TestResult.Fail(TestId, "User reported device selection did not work");
      }

      return TestResult.Pass(TestId, "Device selection works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Device selection failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-006: USB Device Detection Test.
/// </summary>
public class USBDeviceDetectionTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-006";
  public string TestName => "USB Device Detection";
  public string Description => "Detect USB audio devices with port info";
  public int Phase => 2;

  public USBDeviceDetectionTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var deviceManager = _serviceProvider.GetRequiredService<IAudioDeviceManager>();

      ConsoleUI.WriteInfo("Scanning for USB audio devices...");

      var outputDevices = await deviceManager.GetOutputDevicesAsync(ct);
      var inputDevices = await deviceManager.GetInputDevicesAsync(ct);

      var usbOutputDevices = outputDevices.Where(d => d.IsUSBDevice).ToList();
      var usbInputDevices = inputDevices.Where(d => d.IsUSBDevice).ToList();

      var totalUSB = usbOutputDevices.Count + usbInputDevices.Count;

      if (totalUSB == 0)
      {
        ConsoleUI.WriteWarning("No USB audio devices detected");
        ConsoleUI.WriteInfo("This may be expected if no USB audio devices are connected.");

        var hasUSBDevices = ConsoleUI.Confirm("Are there USB audio devices connected to this system?");
        if (hasUSBDevices)
        {
          return TestResult.Fail(TestId, "USB devices connected but not detected");
        }
        return TestResult.Pass(TestId, "No USB devices connected (as expected)");
      }

      ConsoleUI.WriteSuccess($"Found {totalUSB} USB audio device(s):");

      if (usbOutputDevices.Count > 0)
      {
        ConsoleUI.WriteInfo("USB Output Devices:");
        foreach (var device in usbOutputDevices)
        {
          ConsoleUI.WriteInfo($"  - {device.Name}");
          if (device.USBPort != null)
          {
            ConsoleUI.WriteInfo($"    USB Port: {device.USBPort}");
          }
        }
      }

      if (usbInputDevices.Count > 0)
      {
        ConsoleUI.WriteInfo("USB Input Devices:");
        foreach (var device in usbInputDevices)
        {
          ConsoleUI.WriteInfo($"  - {device.Name}");
          if (device.USBPort != null)
          {
            ConsoleUI.WriteInfo($"    USB Port: {device.USBPort}");
          }
        }
      }

      return TestResult.Pass(TestId, $"Found {totalUSB} USB device(s)",
        metadata: new Dictionary<string, object>
        {
          ["USBOutputDevices"] = usbOutputDevices.Count,
          ["USBInputDevices"] = usbInputDevices.Count
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"USB detection failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-007: Hot-Plug Detection Test.
/// </summary>
public class HotPlugDetectionTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-007";
  public string TestName => "Hot-Plug Detection";
  public string Description => "Connect/disconnect USB device during runtime";
  public int Phase => 2;

  public HotPlugDetectionTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var deviceManager = _serviceProvider.GetRequiredService<IAudioDeviceManager>();
      var engine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Ensure engine is initialized for hot-plug detection
      if (engine.State == AudioEngineState.Uninitialized)
      {
        await engine.InitializeAsync(ct);
      }

      var deviceChangedDetected = false;
      var changeType = DeviceChangeType.Added;
      var changedDevice = "";

      void OnDeviceChanged(object? sender, AudioDeviceChangedEventArgs e)
      {
        deviceChangedDetected = true;
        changeType = e.ChangeType;
        changedDevice = e.Device?.Name ?? "Unknown";
        ConsoleUI.WriteSuccess($"Device change detected: {e.ChangeType} - {changedDevice}");
      }

      deviceManager.DevicesChanged += OnDeviceChanged;

      try
      {
        var initialDevices = await deviceManager.GetOutputDevicesAsync(ct);
        ConsoleUI.WriteInfo($"Current device count: {initialDevices.Count}");

        ConsoleUI.WriteInfo("");
        ConsoleUI.WriteInfo("Hot-plug detection test (30 seconds):");
        ConsoleUI.WriteInfo("  1. Connect or disconnect a USB audio device");
        ConsoleUI.WriteInfo("  2. The system should detect the change automatically");
        ConsoleUI.WriteInfo("");

        // Wait for device change or timeout
        var timeout = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;

        bool ShouldContinueWaiting() =>
          !deviceChangedDetected &&
          DateTime.UtcNow - startTime < timeout &&
          !ct.IsCancellationRequested;

        while (ShouldContinueWaiting())
        {
          var remaining = timeout - (DateTime.UtcNow - startTime);
          Console.Write($"\rWaiting for device change... {remaining.TotalSeconds:F0}s remaining");
          await Task.Delay(500, ct);

          // Manually refresh to trigger detection
          await deviceManager.RefreshDevicesAsync(ct);
        }

        Console.WriteLine();

        if (deviceChangedDetected)
        {
          return TestResult.Pass(TestId, $"Hot-plug detected: {changeType} - {changedDevice}");
        }

        // Check if user wants to skip
        ConsoleUI.WriteWarning("No device change was detected.");
        var hadDevice = ConsoleUI.Confirm("Did you connect/disconnect a USB audio device during the test?");

        if (hadDevice)
        {
          return TestResult.Fail(TestId, "Device was changed but hot-plug was not detected");
        }

        return TestResult.Skip(TestId, "No USB device was connected/disconnected during test");
      }
      finally
      {
        deviceManager.DevicesChanged -= OnDeviceChanged;
      }
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Hot-plug test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-008: Master Volume Control Test.
/// </summary>
public class MasterVolumeControlTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-008";
  public string TestName => "Master Volume Control";
  public string Description => "Adjust master volume 0-100%";
  public int Phase => 2;

  public MasterVolumeControlTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var engine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Ensure engine is initialized
      if (engine.State == AudioEngineState.Uninitialized)
      {
        await engine.InitializeAsync(ct);
      }

      var mixer = engine.GetMasterMixer();

      ConsoleUI.WriteInfo("Testing volume control...");
      ConsoleUI.WriteInfo("");

      // Test various volume levels
      var testLevels = new[] { 0f, 0.25f, 0.5f, 0.75f, 1.0f };

      foreach (var level in testLevels)
      {
        mixer.MasterVolume = level;
        var readBack = mixer.MasterVolume;

        if (Math.Abs(readBack - level) > 0.01f)
        {
          return TestResult.Fail(TestId, $"Volume set to {level:P0} but read back {readBack:P0}");
        }

        ConsoleUI.WriteSuccess($"Volume set to {level:P0} - verified");
        await Task.Delay(100, ct);
      }

      // Interactive volume test
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Interactive volume test:");
      var currentVolume = (int)(mixer.MasterVolume * 100);

      var finalVolume = ConsoleUI.VolumeSlider(currentVolume, v =>
      {
        mixer.MasterVolume = v / 100f;
      });

      ConsoleUI.WriteInfo($"Final volume: {finalVolume}%");

      var volumeWorks = ConsoleUI.Confirm("Did the volume changes work correctly?");
      if (!volumeWorks)
      {
        return TestResult.Fail(TestId, "User reported volume control did not work");
      }

      return TestResult.Pass(TestId, "Volume control works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Volume test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-009: Master Mute Toggle Test.
/// </summary>
public class MasterMuteToggleTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-009";
  public string TestName => "Master Mute Toggle";
  public string Description => "Mute/unmute master output";
  public int Phase => 2;

  public MasterMuteToggleTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var engine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Ensure engine is initialized
      if (engine.State == AudioEngineState.Uninitialized)
      {
        await engine.InitializeAsync(ct);
      }

      var mixer = engine.GetMasterMixer();

      // Record initial state
      var initialMute = mixer.IsMuted;
      ConsoleUI.WriteInfo($"Initial mute state: {(initialMute ? "MUTED" : "UNMUTED")}");

      // Test mute
      ConsoleUI.WriteInfo("Setting mute to ON...");
      mixer.IsMuted = true;

      if (!mixer.IsMuted)
      {
        return TestResult.Fail(TestId, "Failed to set mute state to true");
      }
      ConsoleUI.WriteSuccess("Mute is ON");

      await Task.Delay(500, ct);

      // Test unmute
      ConsoleUI.WriteInfo("Setting mute to OFF...");
      mixer.IsMuted = false;

      if (mixer.IsMuted)
      {
        return TestResult.Fail(TestId, "Failed to set mute state to false");
      }
      ConsoleUI.WriteSuccess("Mute is OFF");

      await Task.Delay(500, ct);

      // Toggle test
      ConsoleUI.WriteInfo("Toggle test (press any key to toggle, ESC to finish)...");
      ConsoleUI.WriteInfo("");

      ConsoleKeyInfo key;
      do
      {
        var state = mixer.IsMuted ? "[red]MUTED[/]" : "[green]UNMUTED[/]";
        Spectre.Console.AnsiConsole.MarkupLine($"Current state: {state}");

        key = Console.ReadKey(true);
        if (key.Key != ConsoleKey.Escape)
        {
          mixer.IsMuted = !mixer.IsMuted;
        }
      } while (key.Key != ConsoleKey.Escape);

      // Restore initial state
      mixer.IsMuted = initialMute;
      ConsoleUI.WriteInfo($"Restored initial state: {(initialMute ? "MUTED" : "UNMUTED")}");

      var muteWorks = ConsoleUI.Confirm("Did the mute toggle work correctly?");
      if (!muteWorks)
      {
        return TestResult.Fail(TestId, "User reported mute toggle did not work");
      }

      return TestResult.Pass(TestId, "Mute toggle works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Mute test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-010: Balance Control Test.
/// </summary>
public class BalanceControlTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-010";
  public string TestName => "Balance Control";
  public string Description => "Adjust L/R balance";
  public int Phase => 2;

  public BalanceControlTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var engine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Ensure engine is initialized
      if (engine.State == AudioEngineState.Uninitialized)
      {
        await engine.InitializeAsync(ct);
      }

      var mixer = engine.GetMasterMixer();

      ConsoleUI.WriteInfo("Testing balance control...");
      ConsoleUI.WriteInfo("");

      // Test balance extremes
      var testValues = new[] { -1f, -0.5f, 0f, 0.5f, 1f };

      foreach (var value in testValues)
      {
        mixer.Balance = value;
        var readBack = mixer.Balance;

        if (Math.Abs(readBack - value) > 0.01f)
        {
          return TestResult.Fail(TestId, $"Balance set to {value:F1} but read back {readBack:F1}");
        }

        var label = value == 0 ? "CENTER" : value < 0 ? $"LEFT {(-value * 100):F0}%" : $"RIGHT {(value * 100):F0}%";
        ConsoleUI.WriteSuccess($"Balance set to {label} - verified");
        await Task.Delay(100, ct);
      }

      // Interactive balance test
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Interactive balance test:");
      var currentBalance = (int)(mixer.Balance * 100);

      var finalBalance = ConsoleUI.BalanceSlider(currentBalance, b =>
      {
        mixer.Balance = b / 100f;
      });

      ConsoleUI.WriteInfo($"Final balance: {finalBalance}");

      // Reset to center
      mixer.Balance = 0f;
      ConsoleUI.WriteInfo("Balance reset to center");

      var balanceWorks = ConsoleUI.Confirm("Did the balance control work correctly?");
      if (!balanceWorks)
      {
        return TestResult.Fail(TestId, "User reported balance control did not work");
      }

      return TestResult.Pass(TestId, "Balance control works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Balance test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-011: Output Stream Tap Test.
/// </summary>
public class OutputStreamTapTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-011";
  public string TestName => "Output Stream Tap";
  public string Description => "Access mixed output stream for streaming";
  public int Phase => 2;

  public OutputStreamTapTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var engine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Ensure engine is initialized
      if (engine.State == AudioEngineState.Uninitialized)
      {
        await engine.InitializeAsync(ct);
      }

      ConsoleUI.WriteInfo("Getting mixed output stream...");
      var stream = engine.GetMixedOutputStream();

      if (stream == null)
      {
        return TestResult.Fail(TestId, "GetMixedOutputStream returned null");
      }

      ConsoleUI.WriteSuccess("Output stream obtained successfully");

      // Check stream properties
      ConsoleUI.WriteInfo($"Stream can read: {stream.CanRead}");
      ConsoleUI.WriteInfo($"Stream can seek: {stream.CanSeek}");
      ConsoleUI.WriteInfo($"Stream can write: {stream.CanWrite}");

      if (!stream.CanRead)
      {
        return TestResult.Fail(TestId, "Output stream is not readable");
      }

      // Try to read some data
      ConsoleUI.WriteInfo("Attempting to read from stream...");

      var buffer = new byte[4096];
      var bytesRead = stream.Read(buffer, 0, buffer.Length);

      ConsoleUI.WriteInfo($"Read {bytesRead} bytes from stream");

      // Note: The stream may be empty if no audio is playing
      if (bytesRead == 0)
      {
        ConsoleUI.WriteWarning("No data available (normal if no audio is playing)");
      }
      else
      {
        // Check if we got valid PCM data (not all zeros)
        var hasNonZero = buffer.Take(bytesRead).Any(b => b != 0);
        if (hasNonZero)
        {
          ConsoleUI.WriteSuccess("Stream contains audio data");
        }
        else
        {
          ConsoleUI.WriteInfo("Stream contains silence (all zeros)");
        }
      }

      return TestResult.Pass(TestId, "Output stream tap is accessible",
        metadata: new Dictionary<string, object>
        {
          ["CanRead"] = stream.CanRead,
          ["BytesRead"] = bytesRead
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Output stream test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P2-012: Engine Error Recovery Test.
/// </summary>
public class EngineErrorRecoveryTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P2-012";
  public string TestName => "Engine Error Recovery";
  public string Description => "Force error condition and verify recovery";
  public int Phase => 2;

  public EngineErrorRecoveryTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var engine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Ensure engine is initialized
      if (engine.State == AudioEngineState.Uninitialized)
      {
        await engine.InitializeAsync(ct);
      }

      ConsoleUI.WriteInfo("Testing error handling...");

      // Test 1: Try to initialize an already initialized engine
      ConsoleUI.WriteInfo("Test 1: Attempting double initialization...");
      try
      {
        await engine.InitializeAsync(ct);
        ConsoleUI.WriteWarning("Double initialization did not throw (may be by design)");
      }
      catch (InvalidOperationException ex)
      {
        ConsoleUI.WriteSuccess($"Correctly rejected: {ex.Message}");
      }

      // Test 2: Try to start when already running
      if (engine.State == AudioEngineState.Ready)
      {
        await engine.StartAsync(ct);
      }

      if (engine.State == AudioEngineState.Running)
      {
        ConsoleUI.WriteInfo("Test 2: Attempting double start...");
        try
        {
          await engine.StartAsync(ct);
          ConsoleUI.WriteWarning("Double start did not throw (may be by design)");
        }
        catch (InvalidOperationException ex)
        {
          ConsoleUI.WriteSuccess($"Correctly rejected: {ex.Message}");
        }
      }

      // Test 3: Verify engine can recover after operations
      ConsoleUI.WriteInfo("Test 3: Verifying engine state after tests...");

      await engine.StopAsync(ct);
      ConsoleUI.WriteInfo($"Engine state after stop: {engine.State}");

      await engine.StartAsync(ct);
      ConsoleUI.WriteInfo($"Engine state after restart: {engine.State}");

      if (engine.State != AudioEngineState.Running)
      {
        return TestResult.Fail(TestId, "Engine failed to restart after error tests");
      }

      ConsoleUI.WriteSuccess("Engine recovered successfully after error tests");

      return TestResult.Pass(TestId, "Error handling and recovery work correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Error recovery test failed: {ex.Message}", exception: ex);
    }
  }
}
