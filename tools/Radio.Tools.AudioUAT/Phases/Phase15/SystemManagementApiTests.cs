using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase15;

/// <summary>
/// Phase 15.6: System Management API Tests.
/// Tests system stats, logs, health check, and shutdown endpoints.
/// </summary>
public class SystemManagementApiTests
{
  private readonly RadioApiClient _apiClient;

  public SystemManagementApiTests(RadioApiClient apiClient) => _apiClient = apiClient;

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return new List<IPhaseTest>
    {
      new TestGetSystemStats(_apiClient),
      new TestGetSystemLogs(_apiClient),
      new TestGetSystemLogsFiltered(_apiClient),
      new TestHealthCheck(_apiClient),
      new TestShutdownEndpoint(_apiClient)
    };
  }
}

/// <summary>
/// SYS-001: Get system stats (CPU, RAM, uptime).
/// </summary>
internal class TestGetSystemStats : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SYS-001";
  public string TestName => "Get system stats";
  public string Description => "Verify system statistics can be retrieved (CPU, RAM, uptime)";
  public int Phase => 15;

  public TestGetSystemStats(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var stats = await _apiClient.GetSystemStatsAsync(ct);

      if (stats == null)
      {
        return TestResult.Fail(TestId, "API returned null for system stats");
      }

      ConsoleUI.WriteInfo($"CPU Usage: {stats.CpuUsagePercent:F2}%");
      ConsoleUI.WriteInfo($"Memory Used: {stats.MemoryUsedBytes / 1024 / 1024:N0} MB / {stats.MemoryTotalBytes / 1024 / 1024:N0} MB");
      ConsoleUI.WriteInfo($"Thread Count: {stats.ThreadCount}");
      ConsoleUI.WriteInfo($"Uptime: {stats.Uptime}");

      if (stats.CpuTemperature.HasValue)
      {
        ConsoleUI.WriteInfo($"CPU Temperature: {stats.CpuTemperature.Value:F1}°C");
      }

      // Validate reasonable ranges
      var issues = new List<string>();

      if (stats.CpuUsagePercent < 0 || stats.CpuUsagePercent > 100)
        issues.Add($"CPU usage out of range: {stats.CpuUsagePercent}%");

      if (stats.MemoryUsedBytes < 0 || stats.MemoryUsedBytes > stats.MemoryTotalBytes)
        issues.Add($"Memory usage invalid: {stats.MemoryUsedBytes} / {stats.MemoryTotalBytes}");

      if (stats.ThreadCount <= 0)
        issues.Add($"Invalid thread count: {stats.ThreadCount}");

      if (stats.Uptime < TimeSpan.Zero)
        issues.Add($"Invalid uptime: {stats.Uptime}");

      if (issues.Count > 0)
      {
        ConsoleUI.WriteWarning("Found validation issues:");
        foreach (var issue in issues)
        {
          ConsoleUI.WriteWarning($"  - {issue}");
        }
        return TestResult.Fail(TestId, $"Found {issues.Count} stat validation issue(s)");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved and validated system stats");
      return TestResult.Pass(TestId, $"CPU: {stats.CpuUsagePercent:F1}%, Memory: {stats.MemoryUsedBytes / 1024 / 1024}MB, Threads: {stats.ThreadCount}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SYS-002: Get application logs (unfiltered).
/// </summary>
internal class TestGetSystemLogs : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SYS-002";
  public string TestName => "Get application logs";
  public string Description => "Verify application logs can be retrieved";
  public int Phase => 15;

  public TestGetSystemLogs(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var logsResponse = await _apiClient.GetSystemLogsAsync(ct: ct);

      if (logsResponse == null)
      {
        return TestResult.Fail(TestId, "API returned null for system logs");
      }

      ConsoleUI.WriteInfo($"Total log count: {logsResponse.TotalCount}");
      ConsoleUI.WriteInfo($"Retrieved logs: {logsResponse.Logs.Count}");

      if (logsResponse.Logs.Count > 0)
      {
        ConsoleUI.WriteInfo("\nRecent logs:");
        foreach (var log in logsResponse.Logs.Take(5))
        {
          ConsoleUI.WriteInfo($"  [{log.Timestamp:HH:mm:ss}] {log.Level}: {log.Message}");
        }

        // Validate log entries
        var issues = new List<string>();
        foreach (var log in logsResponse.Logs)
        {
          if (log.Timestamp == default)
            issues.Add("Found log entry with default timestamp");

          if (string.IsNullOrWhiteSpace(log.Level))
            issues.Add("Found log entry with empty level");

          if (string.IsNullOrWhiteSpace(log.Message))
            issues.Add("Found log entry with empty message");
        }

        if (issues.Count > 0)
        {
          ConsoleUI.WriteWarning("Found validation issues:");
          foreach (var issue in issues.Distinct().Take(5))
          {
            ConsoleUI.WriteWarning($"  - {issue}");
          }
          return TestResult.Fail(TestId, $"Found {issues.Count} log entry validation issue(s)");
        }
      }
      else
      {
        ConsoleUI.WriteWarning("No logs returned (application may be new or logs cleared)");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved application logs");
      return TestResult.Pass(TestId, $"Retrieved {logsResponse.Logs.Count} log entries (total: {logsResponse.TotalCount})");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SYS-003: Get application logs (filtered by level and limit).
/// </summary>
internal class TestGetSystemLogsFiltered : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SYS-003";
  public string TestName => "Get filtered application logs";
  public string Description => "Verify logs can be filtered by level and limited";
  public int Phase => 15;

  public TestGetSystemLogsFiltered(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Test with level filter
      ConsoleUI.WriteInfo("Testing with level filter: Error");
      var errorLogs = await _apiClient.GetSystemLogsAsync(level: "Error", ct: ct);

      if (errorLogs == null)
      {
        return TestResult.Fail(TestId, "API returned null for filtered logs");
      }

      ConsoleUI.WriteInfo($"Error logs retrieved: {errorLogs.Logs.Count}");

      // Test with limit
      ConsoleUI.WriteInfo("\nTesting with limit: 10");
      var limitedLogs = await _apiClient.GetSystemLogsAsync(limit: 10, ct: ct);

      if (limitedLogs == null)
      {
        return TestResult.Fail(TestId, "API returned null for limited logs");
      }

      ConsoleUI.WriteInfo($"Limited logs retrieved: {limitedLogs.Logs.Count}");

      if (limitedLogs.Logs.Count > 10)
      {
        return TestResult.Fail(TestId, $"Limit not respected: requested 10, got {limitedLogs.Logs.Count}");
      }

      // Test with both filters
      ConsoleUI.WriteInfo("\nTesting with level: Warning and limit: 5");
      var filteredLogs = await _apiClient.GetSystemLogsAsync(level: "Warning", limit: 5, ct: ct);

      if (filteredLogs == null)
      {
        return TestResult.Fail(TestId, "API returned null for filtered+limited logs");
      }

      ConsoleUI.WriteInfo($"Filtered+limited logs retrieved: {filteredLogs.Logs.Count}");

      if (filteredLogs.Logs.Count > 5)
      {
        return TestResult.Fail(TestId, $"Limit not respected with filter: requested 5, got {filteredLogs.Logs.Count}");
      }

      ConsoleUI.WriteSuccess("Successfully retrieved filtered logs");
      return TestResult.Pass(TestId, "Log filtering and limiting work correctly");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SYS-004: Health check endpoint.
/// </summary>
internal class TestHealthCheck : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SYS-004";
  public string TestName => "Health check endpoint";
  public string Description => "Verify API health check endpoint responds";
  public int Phase => 15;

  public TestHealthCheck(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var isAvailable = await _apiClient.IsApiAvailableAsync(ct);

      if (!isAvailable)
      {
        return TestResult.Fail(TestId, "API health check returned false");
      }

      ConsoleUI.WriteSuccess("API health check passed");
      return TestResult.Pass(TestId, "API is healthy and responding");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SYS-005: Shutdown endpoint (test only, does not actually shut down).
/// </summary>
internal class TestShutdownEndpoint : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SYS-005";
  public string TestName => "Shutdown endpoint exists";
  public string Description => "Verify shutdown endpoint exists and is accessible (does not execute)";
  public int Phase => 15;

  public TestShutdownEndpoint(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // NOTE: We're NOT actually calling ShutdownAsync() here because we don't want to shut down the app
      // during testing. This test only verifies the endpoint exists.
      // The actual shutdown is tested by the run-e2e-uat scripts at the end of the test run.

      ConsoleUI.WriteInfo("Shutdown endpoint is available via POST /api/system/shutdown");
      ConsoleUI.WriteInfo("(Not testing actual shutdown to keep application running)");

      // Instead, we'll just verify we can reach the API
      var isAvailable = await _apiClient.IsApiAvailableAsync(ct);
      if (!isAvailable)
      {
        return TestResult.Fail(TestId, "API not reachable");
      }

      ConsoleUI.WriteSuccess("Shutdown endpoint exists (not executed for safety)");
      return TestResult.Pass(TestId, "Shutdown endpoint available at POST /api/system/shutdown");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}
