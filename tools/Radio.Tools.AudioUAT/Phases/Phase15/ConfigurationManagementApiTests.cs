using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase15;

/// <summary>
/// Phase 15.5: Configuration Management API Tests.
/// Tests configuration CRUD operations and section management.
/// </summary>
public class ConfigurationManagementApiTests
{
  private readonly RadioApiClient _apiClient;

  public ConfigurationManagementApiTests(RadioApiClient apiClient) => _apiClient = apiClient;

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return new List<IPhaseTest>
    {
      new TestGetAllConfiguration(_apiClient),
      new TestGetAudioConfiguration(_apiClient),
      new TestGetVisualizerConfiguration(_apiClient),
      new TestGetOutputConfiguration(_apiClient),
      new TestGetConfigurationSection(_apiClient),
      new TestUpdateConfigurationSection(_apiClient),
      new TestConfigurationRoundTrip(_apiClient),
      new TestConfigurationValidation(_apiClient)
    };
  }
}

/// <summary>
/// CFG-001: Get all configuration entries.
/// </summary>
internal class TestGetAllConfiguration : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "CFG-001";
  public string TestName => "Get all configuration";
  public string Description => "Verify all configuration entries can be retrieved";
  public int Phase => 15;

  public TestGetAllConfiguration(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var config = await _apiClient.GetAllConfigurationAsync(ct);

      if (config == null)
      {
        return TestResult.Fail(TestId, "API returned null for configuration");
      }

      ConsoleUI.WriteInfo($"Configuration entries found: {config.Configuration.Count}");

      if (config.Configuration.Count == 0)
      {
        ConsoleUI.WriteWarning("No configuration entries found (unexpected)");
        return TestResult.Fail(TestId, "Configuration is empty");
      }

      // Show first few entries
      ConsoleUI.WriteInfo("\nSample configuration entries:");
      foreach (var entry in config.Configuration.Take(5))
      {
        ConsoleUI.WriteInfo($"  {entry.Key}: {entry.Value}");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved all configuration");
      return TestResult.Pass(TestId, $"Retrieved {config.Configuration.Count} configuration entries");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// CFG-002: Get audio configuration.
/// </summary>
internal class TestGetAudioConfiguration : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "CFG-002";
  public string TestName => "Get audio configuration";
  public string Description => "Verify audio configuration section can be retrieved";
  public int Phase => 15;

  public TestGetAudioConfiguration(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var config = await _apiClient.GetConfigurationSectionAsync("audio", ct);

      if (config == null)
      {
        return TestResult.Fail(TestId, "API returned null for audio configuration");
      }

      ConsoleUI.WriteInfo($"Section: {config.Section}");
      ConsoleUI.WriteInfo($"Settings count: {config.Settings.Count}");

      if (config.Settings.Count > 0)
      {
        ConsoleUI.WriteInfo("\nAudio configuration settings:");
        foreach (var setting in config.Settings.Take(10))
        {
          ConsoleUI.WriteInfo($"  {setting.Key}: {setting.Value}");
        }
      }
      else
      {
        ConsoleUI.WriteWarning("No audio settings found");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved audio configuration");
      return TestResult.Pass(TestId, $"Retrieved {config.Settings.Count} audio settings");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// CFG-003: Get visualizer configuration.
/// </summary>
internal class TestGetVisualizerConfiguration : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "CFG-003";
  public string TestName => "Get visualizer configuration";
  public string Description => "Verify visualizer configuration section can be retrieved";
  public int Phase => 15;

  public TestGetVisualizerConfiguration(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var config = await _apiClient.GetConfigurationSectionAsync("visualizer", ct);

      if (config == null)
      {
        return TestResult.Fail(TestId, "API returned null for visualizer configuration");
      }

      ConsoleUI.WriteInfo($"Section: {config.Section}");
      ConsoleUI.WriteInfo($"Settings count: {config.Settings.Count}");

      if (config.Settings.Count > 0)
      {
        ConsoleUI.WriteInfo("\nVisualizer configuration settings:");
        foreach (var setting in config.Settings)
        {
          ConsoleUI.WriteInfo($"  {setting.Key}: {setting.Value}");
        }
      }
      else
      {
        ConsoleUI.WriteWarning("No visualizer settings found");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved visualizer configuration");
      return TestResult.Pass(TestId, $"Retrieved {config.Settings.Count} visualizer settings");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// CFG-004: Get output configuration.
/// </summary>
internal class TestGetOutputConfiguration : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "CFG-004";
  public string TestName => "Get output configuration";
  public string Description => "Verify output configuration section can be retrieved";
  public int Phase => 15;

  public TestGetOutputConfiguration(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var config = await _apiClient.GetConfigurationSectionAsync("output", ct);

      if (config == null)
      {
        return TestResult.Fail(TestId, "API returned null for output configuration");
      }

      ConsoleUI.WriteInfo($"Section: {config.Section}");
      ConsoleUI.WriteInfo($"Settings count: {config.Settings.Count}");

      if (config.Settings.Count > 0)
      {
        ConsoleUI.WriteInfo("\nOutput configuration settings:");
        foreach (var setting in config.Settings)
        {
          ConsoleUI.WriteInfo($"  {setting.Key}: {setting.Value}");
        }
      }
      else
      {
        ConsoleUI.WriteWarning("No output settings found");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved output configuration");
      return TestResult.Pass(TestId, $"Retrieved {config.Settings.Count} output settings");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// CFG-005: Get configuration by section name.
/// </summary>
internal class TestGetConfigurationSection : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "CFG-005";
  public string TestName => "Get configuration by section";
  public string Description => "Verify configuration can be retrieved by section name";
  public int Phase => 15;

  public TestGetConfigurationSection(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Test multiple section names
      var sections = new[] { "audio", "visualizer", "output", "spotify" };
      var results = new Dictionary<string, int>();

      foreach (var sectionName in sections)
      {
        try
        {
          var config = await _apiClient.GetConfigurationSectionAsync(sectionName, ct);
          if (config != null)
          {
            results[sectionName] = config.Settings.Count;
            ConsoleUI.WriteInfo($"Section '{sectionName}': {config.Settings.Count} settings");
          }
          else
          {
            results[sectionName] = 0;
            ConsoleUI.WriteWarning($"Section '{sectionName}': null response");
          }
        }
        catch (Exception ex)
        {
          ConsoleUI.WriteWarning($"Section '{sectionName}': {ex.Message}");
          results[sectionName] = -1;
        }
      }

      var successCount = results.Count(r => r.Value >= 0);
      if (successCount == 0)
      {
        return TestResult.Fail(TestId, "No sections could be retrieved");
      }

      ConsoleUI.WriteSuccess($"Successfully retrieved {successCount}/{sections.Length} configuration sections");
      return TestResult.Pass(TestId, $"Retrieved {successCount} sections successfully");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// CFG-006: Update configuration section (non-destructive test).
/// </summary>
internal class TestUpdateConfigurationSection : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "CFG-006";
  public string TestName => "Update configuration section";
  public string Description => "Verify configuration section can be updated via API";
  public int Phase => 15;

  public TestUpdateConfigurationSection(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get current audio configuration
      var currentConfig = await _apiClient.GetConfigurationSectionAsync("audio", ct);
      if (currentConfig == null)
      {
        ConsoleUI.WriteWarning("Could not retrieve audio configuration to test update");
        return TestResult.Skip(TestId, "Audio configuration not available");
      }

      ConsoleUI.WriteInfo($"Current audio settings: {currentConfig.Settings.Count}");

      // Create a copy with the same values (no actual changes to avoid side effects)
      var updatedSettings = new Dictionary<string, object>(currentConfig.Settings);

      // Update configuration
      ConsoleUI.WriteInfo("Sending configuration update request...");
      var success = await _apiClient.UpdateConfigurationSectionAsync("audio", updatedSettings, ct);

      if (!success)
      {
        return TestResult.Fail(TestId, "Configuration update returned false");
      }

      // Verify the update
      await Task.Delay(500, ct); // Give it time to persist
      var verifyConfig = await _apiClient.GetConfigurationSectionAsync("audio", ct);

      if (verifyConfig == null)
      {
        return TestResult.Fail(TestId, "Could not retrieve configuration after update");
      }

      if (verifyConfig.Settings.Count != currentConfig.Settings.Count)
      {
        return TestResult.Fail(TestId, $"Settings count changed: {currentConfig.Settings.Count} -> {verifyConfig.Settings.Count}");
      }

      ConsoleUI.WriteSuccess("Configuration update successful");
      return TestResult.Pass(TestId, "Successfully updated configuration section");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// CFG-007: Configuration round-trip test.
/// </summary>
internal class TestConfigurationRoundTrip : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "CFG-007";
  public string TestName => "Configuration round-trip";
  public string Description => "Verify configuration can be read, updated, and read back";
  public int Phase => 15;

  public TestConfigurationRoundTrip(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Use visualizer config for round-trip test (less critical than audio)
      ConsoleUI.WriteInfo("Step 1: Reading current visualizer configuration...");
      var originalConfig = await _apiClient.GetConfigurationSectionAsync("visualizer", ct);

      if (originalConfig == null || originalConfig.Settings.Count == 0)
      {
        ConsoleUI.WriteWarning("Visualizer configuration not available for round-trip test");
        return TestResult.Skip(TestId, "Visualizer configuration not available");
      }

      var originalSettingsCount = originalConfig.Settings.Count;
      ConsoleUI.WriteInfo($"Original settings count: {originalSettingsCount}");

      // Step 2: Update with same values
      ConsoleUI.WriteInfo("Step 2: Updating configuration with same values...");
      var success = await _apiClient.UpdateConfigurationSectionAsync("visualizer", originalConfig.Settings, ct);

      if (!success)
      {
        return TestResult.Fail(TestId, "Configuration update failed");
      }

      await Task.Delay(500, ct); // Allow time for persistence

      // Step 3: Read back and verify
      ConsoleUI.WriteInfo("Step 3: Reading configuration again to verify...");
      var verifiedConfig = await _apiClient.GetConfigurationSectionAsync("visualizer", ct);

      if (verifiedConfig == null)
      {
        return TestResult.Fail(TestId, "Could not read configuration after update");
      }

      if (verifiedConfig.Settings.Count != originalSettingsCount)
      {
        return TestResult.Fail(TestId, $"Settings count mismatch: {originalSettingsCount} -> {verifiedConfig.Settings.Count}");
      }

      ConsoleUI.WriteSuccess("Configuration round-trip successful");
      return TestResult.Pass(TestId, "Configuration persisted correctly through round-trip");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// CFG-008: Configuration validation.
/// </summary>
internal class TestConfigurationValidation : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "CFG-008";
  public string TestName => "Configuration validation";
  public string Description => "Verify configuration entries have valid structure";
  public int Phase => 15;

  public TestConfigurationValidation(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var config = await _apiClient.GetAllConfigurationAsync(ct);

      if (config == null)
      {
        return TestResult.Fail(TestId, "Configuration is null");
      }

      var issues = new List<string>();

      // Validate structure
      if (config.Configuration.Count == 0)
      {
        issues.Add("Configuration is empty");
      }

      foreach (var entry in config.Configuration)
      {
        if (string.IsNullOrWhiteSpace(entry.Key))
        {
          issues.Add("Found entry with null or empty key");
        }

        if (entry.Value == null)
        {
          issues.Add($"Entry '{entry.Key}' has null value");
        }
      }

      if (issues.Count > 0)
      {
        ConsoleUI.WriteWarning("Found validation issues:");
        foreach (var issue in issues.Take(10))
        {
          ConsoleUI.WriteWarning($"  - {issue}");
        }
        return TestResult.Fail(TestId, $"Found {issues.Count} configuration validation issue(s)");
      }

      ConsoleUI.WriteSuccess($"All {config.Configuration.Count} configuration entries are valid");
      return TestResult.Pass(TestId, $"Validated {config.Configuration.Count} configuration entries");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}
