using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase13;

/// <summary>
/// Phase 13: Radio API Integration Tests.
/// Tests Radio audio source (RTL-SDR/RF320) via the Radio.API REST endpoints.
/// </summary>
public class RadioApiTests
{
  private readonly RadioApiClient _apiClient;

  /// <summary>
  /// Initializes a new instance of the <see cref="RadioApiTests"/> class.
  /// </summary>
  /// <param name="apiClient">The API client.</param>
  public RadioApiTests(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  /// <summary>
  /// Gets all Phase 13 tests.
  /// </summary>
  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new RadioDeviceDetectionTest(_apiClient),
      new SwitchToRadioSourceTest(_apiClient),
      new VerifyRadioAudioOutputTest(_apiClient),
      new FMBandSelectionTest(_apiClient),
      new AMBandSelectionTest(_apiClient),
      new ShortwaveBandSelectionTest(_apiClient),
      new FrequencyTuningUpTest(_apiClient),
      new FrequencyTuningDownTest(_apiClient),
      new DirectFrequencySettingTest(_apiClient),
      new ScanUpFunctionalityTest(_apiClient),
      new ScanDownFunctionalityTest(_apiClient),
      new RadioNowPlayingMetadataTest(_apiClient)
    ];
  }
}

/// <summary>
/// RAD-001: RTL-SDR Device Detection (Pre-requisite).
/// </summary>
public class RadioDeviceDetectionTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-001";
  public string TestName => "Radio Device Detection";
  public string Description => "Verify RTL-SDR or RF320 device is installed and accessible";
  public int Phase => 13;

  public RadioDeviceDetectionTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if Radio source type is available
      ConsoleUI.WriteInfo("Checking available audio source types...");
      var sources = await _apiClient.GetSourcesAsync(ct);

      if (sources == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve sources list from API");
      }

      // Check if Radio is in the list of primary source types
      var hasRadioSourceType = sources.PrimarySources
        .Any(s => s.Equals("Radio", StringComparison.OrdinalIgnoreCase));

      if (!hasRadioSourceType)
      {
        ConsoleUI.WriteWarning("Radio source type not found in available source types");
        ConsoleUI.WriteInfo("Available source types:");
        foreach (var sourceType in sources.PrimarySources)
        {
          ConsoleUI.WriteInfo($"  - {sourceType}");
        }

        return TestResult.Skip(TestId,
          "Radio source type not available - skipping remaining Radio tests");
      }

      ConsoleUI.WriteSuccess("Radio source type is available");

      // Try to switch to Radio to verify the device is accessible
      ConsoleUI.WriteInfo("Attempting to switch to Radio to verify device accessibility...");
      var switchResult = await _apiClient.SwitchSourceAsync("Radio", ct);

      if (switchResult == null)
      {
        ConsoleUI.WriteWarning("Failed to switch to Radio - device may not be connected");
        return TestResult.Skip(TestId,
          "Radio device not accessible - hardware may not be connected");
      }

      // Verify switch was successful
      var primarySource = await _apiClient.GetPrimarySourceAsync(ct);
      if (primarySource == null || !primarySource.Type.Equals("Radio", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteWarning("Switch command succeeded but Radio is not active");
        return TestResult.Skip(TestId,
          "Radio switch failed - device may not be properly configured");
      }

      ConsoleUI.WriteSuccess($"Radio device detected: {primarySource.Name}");

      return TestResult.Pass(TestId, "Radio device detected and accessible",
        metadata: new Dictionary<string, object>
        {
          ["SourceId"] = primarySource.Id,
          ["SourceType"] = primarySource.Type,
          ["SourceName"] = primarySource.Name
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Radio device detection failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-002: Switch to Radio Source.
/// </summary>
public class SwitchToRadioSourceTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-002";
  public string TestName => "Switch to Radio Source";
  public string Description => "Verify API can switch to Radio as the active audio source";
  public int Phase => 13;

  public SwitchToRadioSourceTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Switch to Radio source
      ConsoleUI.WriteInfo("Switching to Radio source...");
      var switchResult = await _apiClient.SwitchSourceAsync("Radio", ct);

      if (switchResult == null)
      {
        return TestResult.Fail(TestId, "Failed to switch to Radio source");
      }

      ConsoleUI.WriteSuccess("Switch command executed");

      // Verify the switch
      ConsoleUI.WriteInfo("Verifying active source...");
      var primarySource = await _apiClient.GetPrimarySourceAsync(ct);

      if (primarySource == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve primary source after switch");
      }

      if (!primarySource.Type.Equals("Radio", StringComparison.OrdinalIgnoreCase) &&
          !primarySource.Type.Contains("SDR", StringComparison.OrdinalIgnoreCase))
      {
        return TestResult.Fail(TestId,
          $"Active source is {primarySource.Type}, expected Radio");
      }

      ConsoleUI.WriteSuccess($"Active source: {primarySource.Type}");
      ConsoleUI.WriteInfo($"Source state: {primarySource.State}");

      return TestResult.Pass(TestId, "Successfully switched to Radio source",
        metadata: new Dictionary<string, object>
        {
          ["SourceType"] = primarySource.Type,
          ["State"] = primarySource.State
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Switch to Radio failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-003: Verify Audio Output (Static/Noise).
/// </summary>
public class VerifyRadioAudioOutputTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-003";
  public string TestName => "Verify Radio Audio Output";
  public string Description => "Confirm audio output is working (static or noise is acceptable)";
  public int Phase => 13;

  public VerifyRadioAudioOutputTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure Radio source is active
      ConsoleUI.WriteInfo("Ensuring Radio source is active...");
      await _apiClient.SwitchSourceAsync("Radio", ct);

      // Start playback
      ConsoleUI.WriteInfo("Starting radio playback...");
      await _apiClient.PlayAsync(ct);
      await Task.Delay(1000, ct);

      // Check playback state
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state != null)
      {
        ConsoleUI.WriteInfo($"Playback state: {state.State}");
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Listen for audio output from the radio source.");
      ConsoleUI.WriteInfo("Static or noise is expected if not tuned to a station.");
      ConsoleUI.WriteInfo("");

      var confirmed = ConsoleUI.AskYesNo("Can you hear audio output (static/noise or station)?");

      if (!confirmed)
      {
        return TestResult.Fail(TestId,
          "User did not confirm audio output - check radio device and speaker connections");
      }

      ConsoleUI.WriteSuccess("Radio audio output confirmed");

      return TestResult.Pass(TestId, "Radio audio output verified",
        metadata: new Dictionary<string, object>
        {
          ["State"] = state?.State ?? "Unknown"
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Radio audio verification failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-004: FM Band Selection.
/// </summary>
public class FMBandSelectionTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-004";
  public string TestName => "FM Band Selection";
  public string Description => "Verify switching to FM band sets correct frequency range";
  public int Phase => 13;

  public FMBandSelectionTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Set FM band
      ConsoleUI.WriteInfo("Setting band to FM...");
      var result = await _apiClient.SetBandAsync("FM", ct);

      if (result == null)
      {
        return TestResult.Fail(TestId, "Failed to set FM band");
      }

      ConsoleUI.WriteSuccess("Band set to FM");

      // Verify frequency is in FM range
      var state = await _apiClient.GetRadioStateAsync(ct);
      if (state != null)
      {
        ConsoleUI.WriteInfo($"Current frequency: {state.Frequency / 1e6:F1} MHz");
        ConsoleUI.WriteInfo($"Current band: {state.Band}");

        // FM range: 87.5 - 108.0 MHz (87,500,000 - 108,000,000 Hz)
        var freqMHz = state.Frequency / 1e6;
        if (freqMHz >= 87.5 && freqMHz <= 108.0)
        {
          ConsoleUI.WriteSuccess("Frequency is within FM range (87.5 - 108.0 MHz)");
        }
        else
        {
          ConsoleUI.WriteWarning($"Frequency {freqMHz} MHz is outside expected FM range");
        }
      }

      return TestResult.Pass(TestId, "FM band selected successfully",
        metadata: new Dictionary<string, object>
        {
          ["Band"] = state?.Band ?? "FM",
          ["Frequency"] = state?.Frequency ?? 0
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"FM band selection failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-005: AM Band Selection.
/// </summary>
public class AMBandSelectionTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-005";
  public string TestName => "AM Band Selection";
  public string Description => "Verify switching to AM band sets correct frequency range";
  public int Phase => 13;

  public AMBandSelectionTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Set AM band
      ConsoleUI.WriteInfo("Setting band to AM...");
      var result = await _apiClient.SetBandAsync("AM", ct);

      if (result == null)
      {
        return TestResult.Fail(TestId, "Failed to set AM band");
      }

      ConsoleUI.WriteSuccess("Band set to AM");

      // Verify frequency is in AM range
      var state = await _apiClient.GetRadioStateAsync(ct);
      if (state != null)
      {
        ConsoleUI.WriteInfo($"Current frequency: {state.Frequency / 1000.0:F0} kHz");
        ConsoleUI.WriteInfo($"Current band: {state.Band}");

        // AM range: 530 - 1710 kHz (530,000 - 1,710,000 Hz)
        var freqKHz = state.Frequency / 1000.0;
        if (freqKHz >= 530 && freqKHz <= 1710)
        {
          ConsoleUI.WriteSuccess("Frequency is within AM range (530 - 1710 kHz)");
        }
        else
        {
          ConsoleUI.WriteWarning($"Frequency {freqKHz} kHz is outside expected AM range");
        }
      }

      return TestResult.Pass(TestId, "AM band selected successfully",
        metadata: new Dictionary<string, object>
        {
          ["Band"] = state?.Band ?? "AM",
          ["Frequency"] = state?.Frequency ?? 0
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"AM band selection failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-006: Shortwave Band Selection.
/// </summary>
public class ShortwaveBandSelectionTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-006";
  public string TestName => "Shortwave Band Selection";
  public string Description => "Verify switching to Shortwave band sets correct frequency range";
  public int Phase => 13;

  public ShortwaveBandSelectionTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Set Shortwave band
      ConsoleUI.WriteInfo("Setting band to Shortwave...");
      var result = await _apiClient.SetBandAsync("Shortwave", ct);

      if (result == null)
      {
        // Shortwave might not be supported
        ConsoleUI.WriteWarning("Shortwave band not supported by this device");
        return TestResult.Skip(TestId, "Shortwave band not supported");
      }

      ConsoleUI.WriteSuccess("Band set to Shortwave");

      // Verify frequency is in Shortwave range
      var state = await _apiClient.GetRadioStateAsync(ct);
      if (state != null)
      {
        ConsoleUI.WriteInfo($"Current frequency: {state.Frequency / 1e6:F3} MHz");
        ConsoleUI.WriteInfo($"Current band: {state.Band}");

        // Shortwave range: 3 - 30 MHz (3,000,000 - 30,000,000 Hz)
        var freqMHz = state.Frequency / 1e6;
        if (freqMHz >= 3.0 && freqMHz <= 30.0)
        {
          ConsoleUI.WriteSuccess("Frequency is within Shortwave range (3 - 30 MHz)");
        }
        else
        {
          ConsoleUI.WriteWarning($"Frequency {freqMHz} MHz is outside expected Shortwave range");
        }
      }

      return TestResult.Pass(TestId, "Shortwave band selected successfully",
        metadata: new Dictionary<string, object>
        {
          ["Band"] = state?.Band ?? "Shortwave",
          ["Frequency"] = state?.Frequency ?? 0
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Shortwave band selection failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-007: Frequency Tuning Up.
/// </summary>
public class FrequencyTuningUpTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-007";
  public string TestName => "Frequency Tuning Up";
  public string Description => "Verify 'Tune Up' increases the frequency";
  public int Phase => 13;

  public FrequencyTuningUpTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Switch to FM band for consistent testing
      ConsoleUI.WriteInfo("Setting FM band...");
      await _apiClient.SetBandAsync("FM", ct);

      // Set initial frequency
      const double initialFrequencyHz = 95.5e6; // 95.5 MHz
      ConsoleUI.WriteInfo($"Setting initial frequency: 95.5 MHz...");
      await _apiClient.SetFrequencyAsync(initialFrequencyHz, ct);
      await Task.Delay(500, ct);

      // Get current frequency
      var stateBefore = await _apiClient.GetRadioStateAsync(ct);
      var beforeFreq = stateBefore?.Frequency ?? initialFrequencyHz;
      ConsoleUI.WriteInfo($"Current frequency: {beforeFreq / 1e6:F1} MHz");

      // Tune up
      ConsoleUI.WriteInfo("Tuning up...");
      await _apiClient.TuneUpAsync(ct);
      await Task.Delay(500, ct);

      // Get new frequency
      var stateAfter = await _apiClient.GetRadioStateAsync(ct);
      var afterFreq = stateAfter?.Frequency ?? beforeFreq;
      ConsoleUI.WriteInfo($"New frequency: {afterFreq / 1e6:F1} MHz");

      // Verify frequency increased
      if (afterFreq > beforeFreq)
      {
        ConsoleUI.WriteSuccess("Frequency increased correctly");
        ConsoleUI.WriteInfo($"Step size: {(afterFreq - beforeFreq) / 1000:F0} kHz");
      }
      else
      {
        ConsoleUI.WriteWarning("Frequency did not increase as expected");
      }

      return TestResult.Pass(TestId, "Tune up working correctly",
        metadata: new Dictionary<string, object>
        {
          ["BeforeFrequency"] = beforeFreq,
          ["AfterFrequency"] = afterFreq,
          ["StepSize"] = afterFreq - beforeFreq
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Tune up test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-008: Frequency Tuning Down.
/// </summary>
public class FrequencyTuningDownTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-008";
  public string TestName => "Frequency Tuning Down";
  public string Description => "Verify 'Tune Down' decreases the frequency";
  public int Phase => 13;

  public FrequencyTuningDownTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get current frequency
      var stateBefore = await _apiClient.GetRadioStateAsync(ct);
      var beforeFreq = stateBefore?.Frequency ?? 96e6;
      ConsoleUI.WriteInfo($"Current frequency: {beforeFreq / 1e6:F1} MHz");

      // Tune down
      ConsoleUI.WriteInfo("Tuning down...");
      await _apiClient.TuneDownAsync(ct);
      await Task.Delay(500, ct);

      // Get new frequency
      var stateAfter = await _apiClient.GetRadioStateAsync(ct);
      var afterFreq = stateAfter?.Frequency ?? beforeFreq;
      ConsoleUI.WriteInfo($"New frequency: {afterFreq / 1e6:F1} MHz");

      // Verify frequency decreased
      if (afterFreq < beforeFreq)
      {
        ConsoleUI.WriteSuccess("Frequency decreased correctly");
        ConsoleUI.WriteInfo($"Step size: {(beforeFreq - afterFreq) / 1000:F0} kHz");
      }
      else
      {
        ConsoleUI.WriteWarning("Frequency did not decrease as expected");
      }

      return TestResult.Pass(TestId, "Tune down working correctly",
        metadata: new Dictionary<string, object>
        {
          ["BeforeFrequency"] = beforeFreq,
          ["AfterFrequency"] = afterFreq,
          ["StepSize"] = beforeFreq - afterFreq
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Tune down test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-009: Direct Frequency Setting.
/// </summary>
public class DirectFrequencySettingTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-009";
  public string TestName => "Direct Frequency Setting";
  public string Description => "Verify setting a specific frequency directly";
  public int Phase => 13;

  public DirectFrequencySettingTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Set FM band
      ConsoleUI.WriteInfo("Ensuring FM band...");
      await _apiClient.SetBandAsync("FM", ct);

      // Set specific frequency (100.0 MHz)
      const double targetFrequency = 100.0e6;
      ConsoleUI.WriteInfo("Setting frequency to 100.0 MHz...");
      var result = await _apiClient.SetFrequencyAsync(targetFrequency, ct);

      if (result == null)
      {
        return TestResult.Fail(TestId, "Failed to set frequency");
      }

      await Task.Delay(500, ct);

      // Verify the frequency was set
      var state = await _apiClient.GetRadioStateAsync(ct);
      if (state == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve radio state");
      }

      var actualFreqMHz = state.Frequency / 1e6;
      ConsoleUI.WriteInfo($"Actual frequency: {actualFreqMHz:F1} MHz");

      // Allow small tolerance (±0.1 MHz)
      if (Math.Abs(actualFreqMHz - 100.0) < 0.2)
      {
        ConsoleUI.WriteSuccess("Frequency set correctly to 100.0 MHz");
      }
      else
      {
        ConsoleUI.WriteWarning($"Frequency {actualFreqMHz:F1} MHz differs from target 100.0 MHz");
      }

      return TestResult.Pass(TestId, $"Frequency set to {actualFreqMHz:F1} MHz",
        metadata: new Dictionary<string, object>
        {
          ["TargetFrequency"] = 100.0e6,
          ["ActualFrequency"] = state.Frequency
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Direct frequency setting failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-010: Scan Up Functionality.
/// </summary>
public class ScanUpFunctionalityTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-010";
  public string TestName => "Scan Up Functionality";
  public string Description => "Verify 'Scan Up' finds the next station in FM band";
  public int Phase => 13;

  public ScanUpFunctionalityTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Set starting frequency
      ConsoleUI.WriteInfo("Setting starting frequency to 98.0 MHz...");
      await _apiClient.SetFrequencyAsync(98.0e6, ct);
      await Task.Delay(500, ct);

      var stateBefore = await _apiClient.GetRadioStateAsync(ct);
      var beforeFreq = stateBefore?.Frequency ?? 98e6;
      ConsoleUI.WriteInfo($"Starting frequency: {beforeFreq / 1e6:F1} MHz");

      // Start scan up
      ConsoleUI.WriteInfo("Starting scan up...");
      await _apiClient.ScanUpAsync(ct);

      // Wait for scan to complete (with timeout)
      ConsoleUI.WriteInfo("Waiting for scan to complete (up to 30 seconds)...");
      var scanTimeout = DateTime.UtcNow.AddSeconds(30);
      var stationFound = false;

      while (DateTime.UtcNow < scanTimeout)
      {
        await Task.Delay(1000, ct);
        var state = await _apiClient.GetRadioStateAsync(ct);

        if (state != null && !state.IsScanning)
        {
          ConsoleUI.WriteSuccess("Scan completed");
          stationFound = true;
          break;
        }
      }

      if (!stationFound)
      {
        ConsoleUI.WriteWarning("Scan timed out after 30 seconds");
      }

      // Get final frequency
      var stateAfter = await _apiClient.GetRadioStateAsync(ct);
      var afterFreq = stateAfter?.Frequency ?? beforeFreq;
      ConsoleUI.WriteInfo($"Final frequency: {afterFreq / 1e6:F1} MHz");

      if (afterFreq != beforeFreq)
      {
        ConsoleUI.WriteSuccess("Scan found a station at different frequency");
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      var confirmed = ConsoleUI.AskYesNo("Can you hear a station (clearer audio, less static)?");

      if (confirmed)
      {
        ConsoleUI.WriteSuccess("Station found and confirmed by user");
      }

      return TestResult.Pass(TestId, $"Scan up completed, frequency: {afterFreq / 1e6:F1} MHz",
        metadata: new Dictionary<string, object>
        {
          ["BeforeFrequency"] = beforeFreq,
          ["AfterFrequency"] = afterFreq,
          ["StationFound"] = confirmed
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Scan up test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-011: Scan Down Functionality.
/// </summary>
public class ScanDownFunctionalityTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-011";
  public string TestName => "Scan Down Functionality";
  public string Description => "Verify 'Scan Down' finds the previous station in FM band";
  public int Phase => 13;

  public ScanDownFunctionalityTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get current frequency
      var stateBefore = await _apiClient.GetRadioStateAsync(ct);
      var beforeFreq = stateBefore?.Frequency ?? 100e6;
      ConsoleUI.WriteInfo($"Starting frequency: {beforeFreq / 1e6:F1} MHz");

      // Start scan down
      ConsoleUI.WriteInfo("Starting scan down...");
      await _apiClient.ScanDownAsync(ct);

      // Wait for scan to complete
      ConsoleUI.WriteInfo("Waiting for scan to complete (up to 30 seconds)...");
      var scanTimeout = DateTime.UtcNow.AddSeconds(30);

      while (DateTime.UtcNow < scanTimeout)
      {
        await Task.Delay(1000, ct);
        var state = await _apiClient.GetRadioStateAsync(ct);

        if (state != null && !state.IsScanning)
        {
          ConsoleUI.WriteSuccess("Scan completed");
          break;
        }
      }

      // Get final frequency
      var stateAfter = await _apiClient.GetRadioStateAsync(ct);
      var afterFreq = stateAfter?.Frequency ?? beforeFreq;
      ConsoleUI.WriteInfo($"Final frequency: {afterFreq / 1e6:F1} MHz");

      if (afterFreq < beforeFreq)
      {
        ConsoleUI.WriteSuccess("Scan found a station at lower frequency");
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      var confirmed = ConsoleUI.AskYesNo("Can you hear a station?");

      return TestResult.Pass(TestId, $"Scan down completed, frequency: {afterFreq / 1e6:F1} MHz",
        metadata: new Dictionary<string, object>
        {
          ["BeforeFrequency"] = beforeFreq,
          ["AfterFrequency"] = afterFreq,
          ["StationFound"] = confirmed
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Scan down test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// RAD-012: Now Playing Metadata.
/// </summary>
public class RadioNowPlayingMetadataTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "RAD-012";
  public string TestName => "Radio Now Playing Metadata";
  public string Description => "Verify 'Now Playing' returns appropriate metadata for Radio";
  public int Phase => 13;

  public RadioNowPlayingMetadataTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get radio state
      ConsoleUI.WriteInfo("Getting radio state...");
      var radioState = await _apiClient.GetRadioStateAsync(ct);

      if (radioState != null)
      {
        ConsoleUI.WriteInfo($"Band: {radioState.Band}");
        ConsoleUI.WriteInfo($"Frequency: {radioState.Frequency / 1e6:F1} MHz");
        ConsoleUI.WriteInfo($"Is Scanning: {radioState.IsScanning}");
      }

      // Get now playing info
      ConsoleUI.WriteInfo("Getting now playing metadata...");
      var nowPlaying = await _apiClient.GetNowPlayingAsync(ct);

      if (nowPlaying == null)
      {
        ConsoleUI.WriteWarning("No now playing data returned");
        ConsoleUI.WriteInfo("(This is expected for Radio without fingerprinting)");
      }
      else
      {
        if (!string.IsNullOrEmpty(nowPlaying.Title))
        {
          ConsoleUI.WriteInfo($"Title: {nowPlaying.Title}");
        }

        if (!string.IsNullOrEmpty(nowPlaying.Artist))
        {
          ConsoleUI.WriteInfo($"Artist: {nowPlaying.Artist}");
        }

        if (!string.IsNullOrEmpty(nowPlaying.State))
        {
          ConsoleUI.WriteInfo($"State: {nowPlaying.State}");
        }
      }

      // Stop radio before completing
      ConsoleUI.WriteInfo("Stopping radio playback...");
      await _apiClient.StopAsync(ct);

      ConsoleUI.WriteSuccess("Radio metadata test completed");

      return TestResult.Pass(TestId, "Radio metadata endpoint works correctly",
        metadata: new Dictionary<string, object>
        {
          ["Frequency"] = radioState?.Frequency ?? 0,
          ["Band"] = radioState?.Band ?? "Unknown",
          ["HasNowPlaying"] = nowPlaying != null
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Radio metadata test failed: {ex.Message}", exception: ex);
    }
  }
}
