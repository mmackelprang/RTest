using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase15;

/// <summary>
/// Phase 15.3: Source Management API Tests
/// Tests source listing, switching, capabilities, and state management.
/// </summary>
public class SourceManagementApiTests
{
  private readonly RadioApiClient _apiClient;

  public SourceManagementApiTests(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new TestListSources(_apiClient),
      new TestGetPrimarySource(_apiClient),
      new TestSwitchSources(_apiClient),
      new TestSourceCapabilities(_apiClient),
      new TestGetActiveEventSources(_apiClient),
      new TestSourceStateAfterSwitch(_apiClient)
    ];
  }
}

/// <summary>
/// SRC-001: List all available sources.
/// </summary>
public class TestListSources : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SRC-001";
  public string TestName => "List all available sources";
  public string Description => "Verify all audio sources can be retrieved via API";
  public int Phase => 15;

  public TestListSources(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var sources = await _apiClient.GetSourcesAsync(ct);
      if (sources == null)
      {
        return TestResult.Fail(TestId, "GetSources returned null");
      }

      if (sources.PrimarySources == null || sources.PrimarySources.Count == 0)
      {
        return TestResult.Fail(TestId, "No primary sources available");
      }

      var eventSourceCount = sources.ActiveSources?.Count(s => s.Category == "Event") ?? 0;
      ConsoleUI.WriteSuccess($"Found {sources.PrimarySources.Count} primary sources, {eventSourceCount} event sources");
      
      foreach (var sourceType in sources.PrimarySources)
      {
        ConsoleUI.WriteInfo($"  - {sourceType}");
      }

      return TestResult.Pass(TestId, $"Found {sources.PrimarySources.Count} primary sources");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SRC-002: Get current primary source.
/// </summary>
public class TestGetPrimarySource : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SRC-002";
  public string TestName => "Get current primary source";
  public string Description => "Verify current primary audio source can be retrieved via API";
  public int Phase => 15;

  public TestGetPrimarySource(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var primarySource = await _apiClient.GetPrimarySourceAsync(ct);
      
      if (primarySource == null)
      {
        ConsoleUI.WriteWarning("No primary source currently active");
        return TestResult.Skip(TestId, "No primary source currently active");
      }

      ConsoleUI.WriteSuccess($"Primary source: {primarySource.Type} - {primarySource.Name}");
      ConsoleUI.WriteInfo($"  State: {primarySource.State}");
      
      return TestResult.Pass(TestId, $"Primary source: {primarySource.Type}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SRC-003: Switch between sources (FilePlayer → Radio → Spotify).
/// </summary>
public class TestSwitchSources : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SRC-003";
  public string TestName => "Switch between sources";
  public string Description => "Verify switching between different audio sources via API";
  public int Phase => 15;

  public TestSwitchSources(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get available sources
      var sources = await _apiClient.GetSourcesAsync(ct);
      if (sources == null || sources.PrimarySources == null || sources.PrimarySources.Count < 2)
      {
        ConsoleUI.WriteWarning("Need at least 2 sources to test switching");
        return TestResult.Skip(TestId, "Not enough sources available");
      }

      var sourcesToTest = sources.PrimarySources.Take(2).ToList();
      ConsoleUI.WriteInfo($"Testing switch between: {sourcesToTest[0]} and {sourcesToTest[1]}");

      // Switch to first source
      ConsoleUI.WriteInfo($"Switching to {sourcesToTest[0]}...");
      var result1 = await _apiClient.SwitchSourceAsync(sourcesToTest[0], ct);
      if (result1 == null)
      {
        return TestResult.Fail(TestId, $"Failed to switch to {sourcesToTest[0]}");
      }

      await Task.Delay(500, ct);

      // Switch to second source
      ConsoleUI.WriteInfo($"Switching to {sourcesToTest[1]}...");
      var result2 = await _apiClient.SwitchSourceAsync(sourcesToTest[1], ct);
      if (result2 == null)
      {
        return TestResult.Fail(TestId, $"Failed to switch to {sourcesToTest[1]}");
      }

      await Task.Delay(500, ct);

      // Verify the switch
      var currentSource = await _apiClient.GetPrimarySourceAsync(ct);
      if (currentSource == null || currentSource.Type != sourcesToTest[1])
      {
        return TestResult.Fail(TestId, $"Expected source {sourcesToTest[1]}, got {currentSource?.Type ?? "null"}");
      }

      ConsoleUI.WriteSuccess($"Successfully switched between sources");
      return TestResult.Pass(TestId, $"Switched: {sourcesToTest[0]} → {sourcesToTest[1]}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SRC-004: Verify source capabilities (CanSeek, CanQueue, etc.).
/// </summary>
public class TestSourceCapabilities : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SRC-004";
  public string TestName => "Verify source capabilities";
  public string Description => "Verify source capability flags are correctly reported via API";
  public int Phase => 15;

  public TestSourceCapabilities(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var sources = await _apiClient.GetSourcesAsync(ct);
      if (sources == null || sources.PrimarySources == null || sources.PrimarySources.Count == 0)
      {
        return TestResult.Fail(TestId, "No sources available");
      }

      ConsoleUI.WriteInfo("Source capabilities:");
      foreach (var sourceType in sources.PrimarySources)
      {
        ConsoleUI.WriteInfo($"\n  {sourceType}:");
        
        // Get the playback state to check capabilities
        var currentSource = await _apiClient.GetPrimarySourceAsync(ct);
        if (currentSource?.Type == sourceType)
        {
          var state = await _apiClient.GetPlaybackStateAsync(ct);
          if (state != null)
          {
            ConsoleUI.WriteInfo($"    CanQueue: {state.CanQueue}");
            ConsoleUI.WriteInfo($"    CanSeek: {state.CanSeek}");
            ConsoleUI.WriteInfo($"    CanNext: {state.CanNext}");
            ConsoleUI.WriteInfo($"    CanPrevious: {state.CanPrevious}");
            ConsoleUI.WriteInfo($"    CanShuffle: {state.CanShuffle}");
            ConsoleUI.WriteInfo($"    CanRepeat: {state.CanRepeat}");
          }
        }
        else
        {
          ConsoleUI.WriteInfo($"    (Not active - switch to view capabilities)");
        }
      }

      ConsoleUI.WriteSuccess("Source capabilities retrieved");
      return TestResult.Pass(TestId, $"Checked capabilities for {sources.PrimarySources.Count} sources");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SRC-005: Get active event sources.
/// </summary>
public class TestGetActiveEventSources : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SRC-005";
  public string TestName => "Get active event sources";
  public string Description => "Verify active event audio sources can be retrieved via API";
  public int Phase => 15;

  public TestGetActiveEventSources(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var sources = await _apiClient.GetSourcesAsync(ct);
      if (sources == null)
      {
        return TestResult.Fail(TestId, "GetSources returned null");
      }

      var eventSourceCount = sources.ActiveSources?.Count(s => s.Category == "Event") ?? 0;
      ConsoleUI.WriteSuccess($"Found {eventSourceCount} event sources");

      if (eventSourceCount > 0 && sources.ActiveSources != null)
      {
        var eventSources = sources.ActiveSources.Where(s => s.Category == "Event");
        foreach (var eventSource in eventSources)
        {
          ConsoleUI.WriteInfo($"  - {eventSource.Type}: {eventSource.Name}");
        }
      }

      return TestResult.Pass(TestId, $"Event sources: {eventSourceCount}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SRC-006: Verify source state after switch.
/// </summary>
public class TestSourceStateAfterSwitch : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SRC-006";
  public string TestName => "Verify source state after switch";
  public string Description => "Verify source state is properly initialized after switching";
  public int Phase => 15;

  public TestSourceStateAfterSwitch(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get available sources
      var sources = await _apiClient.GetSourcesAsync(ct);
      if (sources == null || sources.PrimarySources == null || sources.PrimarySources.Count == 0)
      {
        return TestResult.Fail(TestId, "No sources available");
      }

      var sourceToTest = sources.PrimarySources[0];
      ConsoleUI.WriteInfo($"Testing state after switching to {sourceToTest}...");

      // Switch to source
      var switchResult = await _apiClient.SwitchSourceAsync(sourceToTest, ct);
      if (switchResult == null)
      {
        return TestResult.Fail(TestId, $"Failed to switch to {sourceToTest}");
      }

      await Task.Delay(500, ct);

      // Get current state
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null)
      {
        return TestResult.Fail(TestId, "Could not get playback state after switch");
      }

      // Verify state is consistent
      if (state.ActiveSource == null)
      {
        return TestResult.Fail(TestId, "ActiveSource is null after switch");
      }

      if (state.ActiveSource.Type != sourceToTest)
      {
        return TestResult.Fail(TestId, $"Expected source {sourceToTest}, got {state.ActiveSource.Type}");
      }

      ConsoleUI.WriteSuccess($"Source state verified: {state.ActiveSource.Type} ({state.ActiveSource.State})");
      return TestResult.Pass(TestId, $"State: {state.ActiveSource.Type} - {state.ActiveSource.State}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}
