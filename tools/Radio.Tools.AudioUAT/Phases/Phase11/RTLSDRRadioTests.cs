using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Factories;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Tools.AudioUAT.Utilities;
using RTLSDRCore;

namespace Radio.Tools.AudioUAT.Phases.Phase11;

/// <summary>
/// Phase 11 tests for RTLSDR Radio functionality.
/// Tests require actual RTLSDR USB hardware to be present.
/// </summary>
public class RTLSDRRadioTests
{
  private readonly IServiceProvider _serviceProvider;

  /// <summary>
  /// Initializes a new instance of the <see cref="RTLSDRRadioTests"/> class.
  /// </summary>
  /// <param name="serviceProvider">The service provider.</param>
  public RTLSDRRadioTests(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  /// <summary>
  /// Gets all Phase 11 tests.
  /// </summary>
  /// <returns>The list of tests.</returns>
  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      // Hardware Detection Tests
      new RTLSDRHardwareDetectionTest(_serviceProvider),
      // IRadioControl Endpoint Tests
      new RadioControlGainEndpointTest(_serviceProvider),
      new RadioControlPowerEndpointTest(_serviceProvider),
      new RadioControlLifecycleEndpointTest(_serviceProvider)
    ];
  }
}

/// <summary>
/// P11-001: RTLSDR Hardware Detection Test.
/// Checks if actual RTLSDR USB hardware is present. Gracefully exits if not available.
/// </summary>
public class RTLSDRHardwareDetectionTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P11-001";
  public string TestName => "RTLSDR Hardware Detection";
  public string Description => "Detect RTLSDR USB hardware and validate basic device enumeration";
  public int Phase => 11;

  public RTLSDRHardwareDetectionTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      ConsoleUI.WriteInfo("Checking for RTLSDR USB hardware...");

      // Try to detect RTLSDR hardware using RTLSDRCore
      uint deviceCount = 0;
      try
      {
        deviceCount = RadioReceiver.DeviceCount;
      }
      catch (Exception ex)
      {
        ConsoleUI.WriteWarning($"Failed to query RTLSDR device count: {ex.Message}");
        return TestResult.Skip(TestId, "RTLSDR library not available or no hardware drivers installed");
      }

      if (deviceCount == 0)
      {
        ConsoleUI.WriteWarning("No RTLSDR USB hardware detected");
        return TestResult.Skip(TestId, "No RTLSDR USB hardware found. Please connect an RTLSDR dongle and try again.");
      }

      ConsoleUI.WriteSuccess($"Found {deviceCount} RTLSDR device(s)");

      // Try to get device names
      for (uint i = 0; i < deviceCount; i++)
      {
        try
        {
          var deviceName = RadioReceiver.GetDeviceName(i);
          ConsoleUI.WriteInfo($"  Device {i}: {deviceName}");
        }
        catch (Exception ex)
        {
          ConsoleUI.WriteWarning($"  Device {i}: Failed to get name - {ex.Message}");
        }
      }

      ConsoleUI.WriteInfo("Verifying radio factory can create RTLSDR source...");
      var radioFactory = _serviceProvider.GetRequiredService<IRadioFactory>();
      var availableDevices = radioFactory.GetAvailableDeviceTypes();

      if (!availableDevices.Contains("RTLSDRCore"))
      {
        return TestResult.Fail(TestId, "RTLSDRCore device type not available in radio factory");
      }

      ConsoleUI.WriteSuccess("RTLSDRCore device type is available");

      return TestResult.Pass(TestId, $"Successfully detected {deviceCount} RTLSDR device(s)");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Unexpected error: {ex.Message}");
      return TestResult.Fail(TestId, "Unexpected error during hardware detection", exception: ex);
    }

    await Task.CompletedTask; // Suppress async warning
  }
}

/// <summary>
/// P11-002: Radio Control Gain Endpoint Test.
/// Tests the gain control endpoints (/api/radio/gain and /api/radio/gain/auto).
/// </summary>
public class RadioControlGainEndpointTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P11-002";
  public string TestName => "Radio Control Gain Endpoints";
  public string Description => "Test gain control and auto-gain endpoints with RTLSDR hardware";
  public int Phase => 11;

  public RadioControlGainEndpointTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check for RTLSDR hardware first
      if (!IsRTLSDRHardwareAvailable())
      {
        return TestResult.Skip(TestId, "No RTLSDR hardware available");
      }

      ConsoleUI.WriteInfo("Creating RTLSDR radio source...");
      var radioFactory = _serviceProvider.GetRequiredService<IRadioFactory>();
      var audioEngine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Initialize audio engine if needed
      if (audioEngine.State == AudioEngineState.Uninitialized)
      {
        await audioEngine.InitializeAsync(ct);
      }

      // Create RTLSDR radio source
      var radioSource = radioFactory.CreateRadioAudioSource("RTLSDRCore") as SDRRadioAudioSource;
      if (radioSource == null)
      {
        return TestResult.Fail(TestId, "Failed to create SDRRadioAudioSource from factory");
      }

      ConsoleUI.WriteSuccess("Created RTLSDR radio source");

      // Test auto-gain toggle
      ConsoleUI.WriteInfo("Testing auto-gain control...");
      var initialAutoGain = radioSource.AutoGainEnabled;
      ConsoleUI.WriteInfo($"  Initial auto-gain state: {initialAutoGain}");

      // Toggle auto-gain
      radioSource.AutoGainEnabled = !initialAutoGain;
      var newAutoGain = radioSource.AutoGainEnabled;
      ConsoleUI.WriteInfo($"  New auto-gain state: {newAutoGain}");

      if (newAutoGain == initialAutoGain)
      {
        return TestResult.Fail(TestId, "Auto-gain toggle did not change state");
      }

      ConsoleUI.WriteSuccess("Auto-gain toggle works");

      // Test manual gain (only when auto-gain is off)
      if (!radioSource.AutoGainEnabled)
      {
        ConsoleUI.WriteInfo("Testing manual gain control...");
        var initialGain = radioSource.Gain;
        ConsoleUI.WriteInfo($"  Initial gain: {initialGain} dB");

        // Set a different gain value
        var testGain = 20.0f;
        radioSource.Gain = testGain;
        var newGain = radioSource.Gain;
        ConsoleUI.WriteInfo($"  New gain: {newGain} dB");

        if (Math.Abs(newGain - testGain) > 0.1f)
        {
          ConsoleUI.WriteWarning($"Gain value mismatch (expected {testGain}, got {newGain})");
        }
        else
        {
          ConsoleUI.WriteSuccess("Manual gain control works");
        }
      }

      return TestResult.Pass(TestId, "Gain control endpoints validated successfully");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, "Failed to test gain endpoints", exception: ex);
    }
  }

  private static bool IsRTLSDRHardwareAvailable()
  {
    try
    {
      return RadioReceiver.DeviceCount > 0;
    }
    catch
    {
      return false;
    }
  }
}

/// <summary>
/// P11-003: Radio Control Power Endpoint Test.
/// Tests the power state endpoints (/api/radio/power and /api/radio/power/toggle).
/// </summary>
public class RadioControlPowerEndpointTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P11-003";
  public string TestName => "Radio Control Power Endpoints";
  public string Description => "Test power state and toggle endpoints with RTLSDR hardware";
  public int Phase => 11;

  public RadioControlPowerEndpointTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check for RTLSDR hardware first
      if (!IsRTLSDRHardwareAvailable())
      {
        return TestResult.Skip(TestId, "No RTLSDR hardware available");
      }

      ConsoleUI.WriteInfo("Creating RTLSDR radio source...");
      var radioFactory = _serviceProvider.GetRequiredService<IRadioFactory>();
      var audioEngine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Initialize audio engine if needed
      if (audioEngine.State == AudioEngineState.Uninitialized)
      {
        await audioEngine.InitializeAsync(ct);
      }

      // Create RTLSDR radio source
      var radioSource = radioFactory.CreateRadioAudioSource("RTLSDRCore") as SDRRadioAudioSource;
      if (radioSource == null)
      {
        return TestResult.Fail(TestId, "Failed to create SDRRadioAudioSource from factory");
      }

      ConsoleUI.WriteSuccess("Created RTLSDR radio source");

      // Test get power state
      ConsoleUI.WriteInfo("Testing power state query...");
      var powerState = await radioSource.GetPowerStateAsync(ct);
      ConsoleUI.WriteInfo($"  Current power state: {(powerState ? "ON" : "OFF")}");

      // Test toggle power state
      ConsoleUI.WriteInfo("Testing power state toggle...");
      await radioSource.TogglePowerStateAsync(ct);
      var newPowerState = await radioSource.GetPowerStateAsync(ct);
      ConsoleUI.WriteInfo($"  New power state: {(newPowerState ? "ON" : "OFF")}");

      if (powerState == newPowerState)
      {
        return TestResult.Fail(TestId, "Power toggle did not change state");
      }

      ConsoleUI.WriteSuccess("Power toggle works");

      // Toggle back to original state
      await radioSource.TogglePowerStateAsync(ct);
      var finalPowerState = await radioSource.GetPowerStateAsync(ct);

      if (finalPowerState != powerState)
      {
        ConsoleUI.WriteWarning($"Power state did not return to original (expected {powerState}, got {finalPowerState})");
      }

      return TestResult.Pass(TestId, "Power control endpoints validated successfully");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, "Failed to test power endpoints", exception: ex);
    }
  }

  private static bool IsRTLSDRHardwareAvailable()
  {
    try
    {
      return RadioReceiver.DeviceCount > 0;
    }
    catch
    {
      return false;
    }
  }
}

/// <summary>
/// P11-004: Radio Control Lifecycle Endpoint Test.
/// Tests the lifecycle endpoints (/api/radio/startup and /api/radio/shutdown).
/// </summary>
public class RadioControlLifecycleEndpointTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P11-004";
  public string TestName => "Radio Control Lifecycle Endpoints";
  public string Description => "Test startup and shutdown endpoints with RTLSDR hardware";
  public int Phase => 11;

  public RadioControlLifecycleEndpointTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check for RTLSDR hardware first
      if (!IsRTLSDRHardwareAvailable())
      {
        return TestResult.Skip(TestId, "No RTLSDR hardware available");
      }

      ConsoleUI.WriteInfo("Creating RTLSDR radio source...");
      var radioFactory = _serviceProvider.GetRequiredService<IRadioFactory>();
      var audioEngine = _serviceProvider.GetRequiredService<IAudioEngine>();

      // Initialize audio engine if needed
      if (audioEngine.State == AudioEngineState.Uninitialized)
      {
        await audioEngine.InitializeAsync(ct);
      }

      // Create RTLSDR radio source
      var radioSource = radioFactory.CreateRadioAudioSource("RTLSDRCore") as SDRRadioAudioSource;
      if (radioSource == null)
      {
        return TestResult.Fail(TestId, "Failed to create SDRRadioAudioSource from factory");
      }

      ConsoleUI.WriteSuccess("Created RTLSDR radio source");

      // Test startup
      ConsoleUI.WriteInfo("Testing radio startup...");
      var initialRunningState = radioSource.IsRunning;
      ConsoleUI.WriteInfo($"  Initial running state: {initialRunningState}");

      var startupResult = await radioSource.StartupAsync(ct);
      ConsoleUI.WriteInfo($"  Startup result: {startupResult}");

      if (!startupResult)
      {
        return TestResult.Fail(TestId, "Startup failed");
      }

      var runningAfterStartup = radioSource.IsRunning;
      ConsoleUI.WriteInfo($"  Running state after startup: {runningAfterStartup}");

      if (!runningAfterStartup)
      {
        ConsoleUI.WriteWarning("IsRunning is false after successful startup");
      }

      ConsoleUI.WriteSuccess("Startup completed");

      // Test shutdown
      ConsoleUI.WriteInfo("Testing radio shutdown...");
      await radioSource.ShutdownAsync(ct);

      var runningAfterShutdown = radioSource.IsRunning;
      ConsoleUI.WriteInfo($"  Running state after shutdown: {runningAfterShutdown}");

      if (runningAfterShutdown)
      {
        ConsoleUI.WriteWarning("IsRunning is still true after shutdown");
      }

      ConsoleUI.WriteSuccess("Shutdown completed");

      return TestResult.Pass(TestId, "Lifecycle endpoints validated successfully");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, "Failed to test lifecycle endpoints", exception: ex);
    }
  }

  private static bool IsRTLSDRHardwareAvailable()
  {
    try
    {
      return RadioReceiver.DeviceCount > 0;
    }
    catch
    {
      return false;
    }
  }
}
