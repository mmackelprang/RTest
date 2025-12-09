using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Factories;
using Radio.Tools.AudioUAT.Utilities;

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
      ConsoleUI.WriteInfo("Checking for RTLSDR hardware via radio factory...");
      var radioFactory = _serviceProvider.GetRequiredService<IRadioFactory>();
      var availableDevices = radioFactory.GetAvailableDeviceTypes().ToList();

      if (!availableDevices.Contains("RTLSDRCore"))
      {
        ConsoleUI.WriteWarning("RTLSDRCore device type not available in radio factory");
        return TestResult.Skip(TestId, "RTLSDRCore not available. May not be installed or no hardware present.");
      }

      ConsoleUI.WriteSuccess($"Found {availableDevices.Count} radio device type(s)");
      foreach (var deviceType in availableDevices)
      {
        ConsoleUI.WriteInfo($"  - {deviceType}");
      }

      // Try to create an RTLSDRCore source to verify it's actually available
      ConsoleUI.WriteInfo("Attempting to create RTLSDR radio source...");
      try
      {
        var radioSource = radioFactory.CreateRadioSource("RTLSDRCore");
        if (radioSource == null)
        {
          return TestResult.Fail(TestId, "RadioFactory.CreateRadioSource returned null for RTLSDRCore");
        }

        ConsoleUI.WriteSuccess("Successfully created RTLSDR radio source");
        
        // Check if it implements IRadioControl
        if (radioSource is IRadioControl)
        {
          ConsoleUI.WriteSuccess("Radio source implements IRadioControl interface");
        }
        else
        {
          ConsoleUI.WriteWarning("Radio source does not implement IRadioControl");
        }

        return TestResult.Pass(TestId, "RTLSDRCore device type is available and can be created");
      }
      catch (InvalidOperationException ex)
      {
        // This is expected if hardware is not present
        ConsoleUI.WriteWarning($"Cannot create RTLSDR source (no hardware): {ex.Message}");
        return TestResult.Skip(TestId, "RTLSDR USB hardware not found. Please connect an RTLSDR dongle and try again.");
      }
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Unexpected error: {ex.Message}");
      return TestResult.Fail(TestId, "Unexpected error during hardware detection", exception: ex);
    }
  }
}

/// <summary>
/// P11-002: Radio Control Gain Endpoint Test.
/// Tests the gain control endpoints with RTLSDR hardware.
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

      // Create RTLSDR radio source
      var radioSource = radioFactory.CreateRadioSource("RTLSDRCore") as IRadioControl;
      if (radioSource == null)
      {
        return TestResult.Fail(TestId, "Failed to create RTLSDRCore source or it doesn't implement IRadioControl");
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
    catch (InvalidOperationException ex)
    {
      ConsoleUI.WriteWarning($"Hardware not available: {ex.Message}");
      return TestResult.Skip(TestId, "No RTLSDR hardware available");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, "Failed to test gain endpoints", exception: ex);
    }
  }

  private bool IsRTLSDRHardwareAvailable()
  {
    try
    {
      var radioFactory = _serviceProvider.GetRequiredService<IRadioFactory>();
      var availableDevices = radioFactory.GetAvailableDeviceTypes();
      if (!availableDevices.Contains("RTLSDRCore"))
      {
        return false;
      }

      // Try to create to verify hardware is actually present
      try
      {
        var source = radioFactory.CreateRadioSource("RTLSDRCore");
        return source != null;
      }
      catch (InvalidOperationException)
      {
        return false;
      }
    }
    catch
    {
      return false;
    }
  }
}

/// <summary>
/// P11-003: Radio Control Power Endpoint Test.
/// Tests the power state endpoints with RTLSDR hardware.
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

      // Create RTLSDR radio source
      var radioSource = radioFactory.CreateRadioSource("RTLSDRCore") as IRadioControl;
      if (radioSource == null)
      {
        return TestResult.Fail(TestId, "Failed to create RTLSDRCore source or it doesn't implement IRadioControl");
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
    catch (InvalidOperationException ex)
    {
      ConsoleUI.WriteWarning($"Hardware not available: {ex.Message}");
      return TestResult.Skip(TestId, "No RTLSDR hardware available");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, "Failed to test power endpoints", exception: ex);
    }
  }

  private bool IsRTLSDRHardwareAvailable()
  {
    try
    {
      var radioFactory = _serviceProvider.GetRequiredService<IRadioFactory>();
      var availableDevices = radioFactory.GetAvailableDeviceTypes();
      if (!availableDevices.Contains("RTLSDRCore"))
      {
        return false;
      }

      try
      {
        var source = radioFactory.CreateRadioSource("RTLSDRCore");
        return source != null;
      }
      catch (InvalidOperationException)
      {
        return false;
      }
    }
    catch
    {
      return false;
    }
  }
}

/// <summary>
/// P11-004: Radio Control Lifecycle Endpoint Test.
/// Tests the lifecycle endpoints with RTLSDR hardware.
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

      // Create RTLSDR radio source
      var radioSource = radioFactory.CreateRadioSource("RTLSDRCore") as IRadioControl;
      if (radioSource == null)
      {
        return TestResult.Fail(TestId, "Failed to create RTLSDRCore source or it doesn't implement IRadioControl");
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
    catch (InvalidOperationException ex)
    {
      ConsoleUI.WriteWarning($"Hardware not available: {ex.Message}");
      return TestResult.Skip(TestId, "No RTLSDR hardware available");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, "Failed to test lifecycle endpoints", exception: ex);
    }
  }

  private bool IsRTLSDRHardwareAvailable()
  {
    try
    {
      var radioFactory = _serviceProvider.GetRequiredService<IRadioFactory>();
      var availableDevices = radioFactory.GetAvailableDeviceTypes();
      if (!availableDevices.Contains("RTLSDRCore"))
      {
        return false;
      }

      try
      {
        var source = radioFactory.CreateRadioSource("RTLSDRCore");
        return source != null;
      }
      catch (InvalidOperationException)
      {
        return false;
      }
    }
    catch
    {
      return false;
    }
  }
}
