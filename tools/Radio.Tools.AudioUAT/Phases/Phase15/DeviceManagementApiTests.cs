using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase15;

/// <summary>
/// Phase 15.4: Device Management API Tests.
/// Tests device enumeration, selection, and USB device management.
/// </summary>
public class DeviceManagementApiTests
{
  private readonly RadioApiClient _apiClient;

  public DeviceManagementApiTests(RadioApiClient apiClient) => _apiClient = apiClient;

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return new List<IPhaseTest>
    {
      new TestListOutputDevices(_apiClient),
      new TestListInputDevices(_apiClient),
      new TestGetDefaultOutputDevice(_apiClient),
      new TestSetOutputDevice(_apiClient),
      new TestListUsbDevices(_apiClient),
      new TestRefreshDevices(_apiClient),
      new TestDeviceProperties(_apiClient)
    };
  }
}

/// <summary>
/// DEV-001: List output devices.
/// </summary>
internal class TestListOutputDevices : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "DEV-001";
  public string TestName => "List output devices";
  public string Description => "Verify output devices can be retrieved via API";
  public int Phase => 15;

  public TestListOutputDevices(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var devices = await _apiClient.GetOutputDevicesAsync(ct);

      if (devices == null)
      {
        return TestResult.Fail(TestId, "API returned null for output devices");
      }

      ConsoleUI.WriteInfo($"Found {devices.Count} output device(s)");
      foreach (var device in devices)
      {
        var defaultMarker = device.IsDefault ? " [DEFAULT]" : "";
        var usbMarker = device.IsUSBDevice ? $" [USB: {device.USBPort}]" : "";
        ConsoleUI.WriteInfo($"  - {device.Name} (ID: {device.Id}){defaultMarker}{usbMarker}");
      }

      if (devices.Count == 0)
      {
        ConsoleUI.WriteWarning("No output devices found");
        return TestResult.Skip(TestId, "No output devices available to test");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved output devices");
      return TestResult.Pass(TestId, $"Retrieved {devices.Count} output device(s)");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// DEV-002: List input devices.
/// </summary>
internal class TestListInputDevices : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "DEV-002";
  public string TestName => "List input devices";
  public string Description => "Verify input devices can be retrieved via API";
  public int Phase => 15;

  public TestListInputDevices(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var devices = await _apiClient.GetInputDevicesAsync(ct);

      if (devices == null)
      {
        return TestResult.Fail(TestId, "API returned null for input devices");
      }

      ConsoleUI.WriteInfo($"Found {devices.Count} input device(s)");
      foreach (var device in devices)
      {
        var defaultMarker = device.IsDefault ? " [DEFAULT]" : "";
        var usbMarker = device.IsUSBDevice ? $" [USB: {device.USBPort}]" : "";
        ConsoleUI.WriteInfo($"  - {device.Name} (ID: {device.Id}){defaultMarker}{usbMarker}");
      }

      if (devices.Count == 0)
      {
        ConsoleUI.WriteWarning("No input devices found");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved input devices");
      return TestResult.Pass(TestId, $"Retrieved {devices.Count} input device(s)");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// DEV-003: Get default output device.
/// </summary>
internal class TestGetDefaultOutputDevice : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "DEV-003";
  public string TestName => "Get default output device";
  public string Description => "Verify default output device can be retrieved";
  public int Phase => 15;

  public TestGetDefaultOutputDevice(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var device = await _apiClient.GetDefaultOutputDeviceAsync(ct);

      if (device == null)
      {
        ConsoleUI.WriteWarning("No default output device found");
        return TestResult.Skip(TestId, "No default output device available");
      }

      ConsoleUI.WriteInfo($"Default device: {device.Name} (ID: {device.Id})");
      ConsoleUI.WriteInfo($"  Type: {device.Type}");
      ConsoleUI.WriteInfo($"  Max Channels: {device.MaxChannels}");
      if (device.IsUSBDevice)
      {
        ConsoleUI.WriteInfo($"  USB Port: {device.USBPort}");
      }

      ConsoleUI.WriteSuccess($"Successfully retrieved default output device: {device.Name}");
      return TestResult.Pass(TestId, $"Default device: {device.Name}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// DEV-004: Set output device.
/// </summary>
internal class TestSetOutputDevice : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "DEV-004";
  public string TestName => "Set output device";
  public string Description => "Verify output device can be changed via API";
  public int Phase => 15;

  public TestSetOutputDevice(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get available devices
      var devices = await _apiClient.GetOutputDevicesAsync(ct);
      if (devices == null || devices.Count == 0)
      {
        ConsoleUI.WriteWarning("No output devices available");
        return TestResult.Skip(TestId, "No output devices to test with");
      }

      // Get current default device
      var currentDevice = await _apiClient.GetDefaultOutputDeviceAsync(ct);
      var currentDeviceId = currentDevice?.Id;

      ConsoleUI.WriteInfo($"Current device: {currentDevice?.Name ?? "None"}");

      // Find a different device to switch to
      var targetDevice = devices.FirstOrDefault(d => d.Id != currentDeviceId);
      if (targetDevice == null)
      {
        ConsoleUI.WriteWarning("Only one output device available, cannot test switching");
        return TestResult.Skip(TestId, "Only one output device available");
      }

      ConsoleUI.WriteInfo($"Attempting to switch to: {targetDevice.Name}");

      // Switch device
      var success = await _apiClient.SetOutputDeviceAsync(targetDevice.Id, ct);
      if (!success)
      {
        return TestResult.Fail(TestId, "Device switch returned false");
      }

      ConsoleUI.WriteInfo("Waiting for device switch to complete...");
      await Task.Delay(1000, ct);

      // Verify switch
      var newDefaultDevice = await _apiClient.GetDefaultOutputDeviceAsync(ct);
      if (newDefaultDevice?.Id != targetDevice.Id)
      {
        // Try to restore original device
        if (currentDeviceId != null)
        {
          await _apiClient.SetOutputDeviceAsync(currentDeviceId, ct);
        }
        return TestResult.Fail(TestId, $"Device switch failed: expected {targetDevice.Id}, got {newDefaultDevice?.Id ?? "null"}");
      }

      ConsoleUI.WriteSuccess($"Successfully switched to device: {targetDevice.Name}");

      // Restore original device
      if (currentDeviceId != null)
      {
        ConsoleUI.WriteInfo($"Restoring original device: {currentDevice?.Name}");
        await _apiClient.SetOutputDeviceAsync(currentDeviceId, ct);
      }

      return TestResult.Pass(TestId, $"Successfully switched output device to {targetDevice.Name}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// DEV-005: List USB devices.
/// </summary>
internal class TestListUsbDevices : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "DEV-005";
  public string TestName => "List USB devices";
  public string Description => "Verify USB audio devices can be retrieved";
  public int Phase => 15;

  public TestListUsbDevices(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var devices = await _apiClient.GetUsbDevicesAsync(ct);

      if (devices == null)
      {
        return TestResult.Fail(TestId, "API returned null for USB devices");
      }

      ConsoleUI.WriteInfo($"Found {devices.Count} USB audio device(s)");
      foreach (var device in devices)
      {
        ConsoleUI.WriteInfo($"  - {device.Name} (ID: {device.Id})");
        ConsoleUI.WriteInfo($"    USB Port: {device.USBPort ?? "Unknown"}");
        ConsoleUI.WriteInfo($"    Max Channels: {device.MaxChannels}");
      }

      if (devices.Count == 0)
      {
        ConsoleUI.WriteWarning("No USB audio devices found");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved USB device list");
      return TestResult.Pass(TestId, $"Retrieved {devices.Count} USB device(s)");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// DEV-006: Refresh device list.
/// </summary>
internal class TestRefreshDevices : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "DEV-006";
  public string TestName => "Refresh device list";
  public string Description => "Verify device list can be refreshed via API";
  public int Phase => 15;

  public TestRefreshDevices(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get device count before refresh
      var devicesBefore = await _apiClient.GetOutputDevicesAsync(ct);
      var countBefore = devicesBefore?.Count ?? 0;
      ConsoleUI.WriteInfo($"Devices before refresh: {countBefore}");

      // Refresh devices
      ConsoleUI.WriteInfo("Refreshing device list...");
      var success = await _apiClient.RefreshDevicesAsync(ct);

      if (!success)
      {
        return TestResult.Fail(TestId, "Device refresh returned false");
      }

      // Get device count after refresh
      await Task.Delay(500, ct); // Give it time to refresh
      var devicesAfter = await _apiClient.GetOutputDevicesAsync(ct);
      var countAfter = devicesAfter?.Count ?? 0;
      ConsoleUI.WriteInfo($"Devices after refresh: {countAfter}");

      ConsoleUI.WriteSuccess("Device refresh completed successfully");
      return TestResult.Pass(TestId, $"Device refresh successful (before: {countBefore}, after: {countAfter})");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// DEV-007: Verify device properties.
/// </summary>
internal class TestDeviceProperties : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "DEV-007";
  public string TestName => "Verify device properties";
  public string Description => "Verify device properties are complete and valid";
  public int Phase => 15;

  public TestDeviceProperties(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var devices = await _apiClient.GetOutputDevicesAsync(ct);

      if (devices == null || devices.Count == 0)
      {
        ConsoleUI.WriteWarning("No output devices to validate");
        return TestResult.Skip(TestId, "No output devices available");
      }

      var issues = new List<string>();

      foreach (var device in devices)
      {
        ConsoleUI.WriteInfo($"Validating device: {device.Name}");

        // Check required properties
        if (string.IsNullOrWhiteSpace(device.Id))
        {
          issues.Add($"Device '{device.Name}' has null or empty Id");
        }

        if (string.IsNullOrWhiteSpace(device.Name))
        {
          issues.Add($"Device '{device.Id}' has null or empty Name");
        }

        if (string.IsNullOrWhiteSpace(device.Type))
        {
          issues.Add($"Device '{device.Name}' has null or empty Type");
        }

        if (device.MaxChannels <= 0)
        {
          issues.Add($"Device '{device.Name}' has invalid MaxChannels: {device.MaxChannels}");
        }

        // If USB device, should have USB port
        if (device.IsUSBDevice && string.IsNullOrWhiteSpace(device.USBPort))
        {
          issues.Add($"USB device '{device.Name}' has no USBPort specified");
        }

        // Validate supported sample rates
        if (device.SupportedSampleRates != null && device.SupportedSampleRates.Length > 0)
        {
          ConsoleUI.WriteInfo($"  Supported sample rates: {string.Join(", ", device.SupportedSampleRates)} Hz");
        }
      }

      if (issues.Count > 0)
      {
        ConsoleUI.WriteWarning("Found validation issues:");
        foreach (var issue in issues)
        {
          ConsoleUI.WriteWarning($"  - {issue}");
        }
        return TestResult.Fail(TestId, $"Found {issues.Count} device property validation issue(s)");
      }

      ConsoleUI.WriteSuccess($"All {devices.Count} devices have valid properties");
      return TestResult.Pass(TestId, $"Validated {devices.Count} device(s) successfully");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}
