using System.Net.Http.Json;
using Radio.API.Models;
using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the SystemController.
/// </summary>
public class SystemControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public SystemControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetSystemStats_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/system/stats");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var stats = await response.Content.ReadFromJsonAsync<SystemStatsDto>();
    Assert.NotNull(stats);
  }

  [Fact]
  public async Task GetSystemStats_ReturnsValidData()
  {
    // Act
    var response = await _client.GetAsync("/api/system/stats");
    var stats = await response.Content.ReadFromJsonAsync<SystemStatsDto>();

    // Assert
    Assert.NotNull(stats);
    Assert.True(stats.CpuUsagePercent >= 0 && stats.CpuUsagePercent <= 100);
    Assert.True(stats.RamUsageMb > 0);
    Assert.True(stats.DiskUsagePercent >= 0 && stats.DiskUsagePercent <= 100);
    Assert.True(stats.ThreadCount > 0);
    Assert.NotEmpty(stats.AppUptime);
    Assert.NotEmpty(stats.SystemUptime);
    Assert.NotEmpty(stats.AudioEngineState);
    Assert.NotNull(stats.SystemTemperature);
  }

  [Fact]
  public async Task GetSystemLogs_WithDefaults_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var logs = await response.Content.ReadFromJsonAsync<SystemLogsDto>();
    Assert.NotNull(logs);
    Assert.NotNull(logs.Logs);
    Assert.NotNull(logs.Filters);
  }

  [Fact]
  public async Task GetSystemLogs_WithLevelFilter_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?level=error");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var logs = await response.Content.ReadFromJsonAsync<SystemLogsDto>();
    Assert.NotNull(logs);
    Assert.Equal("error", logs.Filters.Level);
  }

  [Fact]
  public async Task GetSystemLogs_WithLimit_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?limit=50");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var logs = await response.Content.ReadFromJsonAsync<SystemLogsDto>();
    Assert.NotNull(logs);
    Assert.Equal(50, logs.Filters.Limit);
  }

  [Fact]
  public async Task GetSystemLogs_WithMaxAge_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?maxAgeMinutes=60");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var logs = await response.Content.ReadFromJsonAsync<SystemLogsDto>();
    Assert.NotNull(logs);
    Assert.Equal(60, logs.Filters.MaxAgeMinutes);
  }

  [Fact]
  public async Task GetSystemLogs_WithInvalidLevel_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?level=invalid");

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task GetSystemLogs_WithInvalidLimit_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?limit=0");

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task GetSystemLogs_WithTooLargeLimit_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?limit=20000");

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task GetSystemLogs_ReturnsValidStructure()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?level=info&limit=10");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var logs = await response.Content.ReadFromJsonAsync<SystemLogsDto>();
    Assert.NotNull(logs);
    Assert.NotNull(logs.Logs);
    Assert.NotNull(logs.Filters);
    Assert.Equal("info", logs.Filters.Level);
    Assert.Equal(10, logs.Filters.Limit);
    Assert.True(logs.TotalCount >= 0);

    // If logs are returned, verify structure
    if (logs.Logs.Count > 0)
    {
      var firstLog = logs.Logs[0];
      Assert.NotEqual(default(DateTime), firstLog.Timestamp);
      Assert.NotEmpty(firstLog.Level);
      Assert.NotEmpty(firstLog.Message);
      // SourceContext and Exception may be null, so we don't assert them
    }
  }

  [Fact]
  public async Task GetSystemLogs_RespectsSizeLimit()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?level=info&limit=5");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var logs = await response.Content.ReadFromJsonAsync<SystemLogsDto>();
    Assert.NotNull(logs);
    Assert.True(logs.Logs.Count <= 5, $"Expected at most 5 logs, got {logs.Logs.Count}");
  }

  [Fact]
  public async Task GetSystemLogs_FiltersWarningAndAbove()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?level=warning&limit=100");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var logs = await response.Content.ReadFromJsonAsync<SystemLogsDto>();
    Assert.NotNull(logs);

    // If logs are returned, verify they are warning or higher
    if (logs.Logs.Count > 0)
    {
      foreach (var log in logs.Logs)
      {
        var level = log.Level.ToUpperInvariant();
        // Should be WRN, ERR, or FTL (warning, error, or fatal)
        Assert.True(
          level.Contains("WRN") || level.Contains("ERR") || level.Contains("FTL") ||
          level.Contains("WARNING") || level.Contains("ERROR") || level.Contains("FATAL"),
          $"Expected warning or higher, got {log.Level}");
      }
    }
  }

  [Fact]
  public async Task GetSystemLogs_FiltersErrorOnly()
  {
    // Act
    var response = await _client.GetAsync("/api/system/logs?level=error&limit=100");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var logs = await response.Content.ReadFromJsonAsync<SystemLogsDto>();
    Assert.NotNull(logs);

    // If logs are returned, verify they are error or higher
    if (logs.Logs.Count > 0)
    {
      foreach (var log in logs.Logs)
      {
        var level = log.Level.ToUpperInvariant();
        // Should be ERR or FTL (error or fatal)
        Assert.True(
          level.Contains("ERR") || level.Contains("FTL") ||
          level.Contains("ERROR") || level.Contains("FATAL"),
          $"Expected error or higher, got {log.Level}");
      }
    }
  }

  // --- ENC-6: the sleep-screen endpoint and the three-state response ---------------------------
  //
  // These drive the real endpoints through the class fixture's HttpClient, because that is what
  // this class is: there is no controller-construction helper to reuse. That also makes them the
  // only automated cover for the JSON shape the Web deserializes.

  /// <summary>
  /// Puts the console back in Awake through the same endpoints the facts below exercise.
  /// </summary>
  /// <remarks>
  /// ⚠ The fixture's host is shared by every fact in this class and <c>SleepService</c> is a
  /// singleton, so sleep state written by one fact is still set when the next one runs. Each sleep
  /// fact therefore resets on the way in <b>and</b> in a <c>finally</c> on the way out, rather than
  /// relying on an execution order xUnit does not promise.
  /// </remarks>
  private async Task ResetSleepStateAsync()
  {
    await _client.PostAsJsonAsync("/api/system/sleep", new { sleep = false });
    await _client.PostAsJsonAsync("/api/system/sleep-screen", new { visible = false });
  }

  [Fact]
  public async Task GetSleepState_ReportsBothTheAudioTruthAndTheWakeState()
  {
    await ResetSleepStateAsync();
    try
    {
      var response = await _client.GetAsync("/api/system/sleep");

      Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
      var body = await response.Content.ReadFromJsonAsync<SleepStateResponse>();
      Assert.NotNull(body);
      Assert.False(body!.IsSleeping);
      Assert.Equal("Awake", body.WakeState);
    }
    finally
    {
      await ResetSleepStateAsync();
    }
  }

  [Fact]
  public async Task SetSleepScreenVisible_True_PutsTheConsoleInAmbientAndSaysSo()
  {
    // This is the call the /sleep page makes on first render, and the response is how the page
    // learns which hint to draw without a second round trip.
    await ResetSleepStateAsync();
    try
    {
      var response = await _client.PostAsJsonAsync(
        "/api/system/sleep-screen", new { visible = true });

      Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
      var body = await response.Content.ReadFromJsonAsync<SleepStateResponse>();
      Assert.NotNull(body);
      Assert.Equal("Ambient", body!.WakeState);
      Assert.False(body.IsSleeping);

      // The flag is recorded, not merely echoed: a second, independent request sees it too.
      var followUp = await _client.GetFromJsonAsync<SleepStateResponse>("/api/system/sleep");
      Assert.Equal("Ambient", followUp!.WakeState);
    }
    finally
    {
      await ResetSleepStateAsync();
    }
  }

  [Fact]
  public async Task SetSleepScreenVisible_True_WhileSleeping_ReportsStandby()
  {
    await ResetSleepStateAsync();
    try
    {
      await _client.PostAsJsonAsync("/api/system/sleep", new { sleep = true });

      var response = await _client.PostAsJsonAsync(
        "/api/system/sleep-screen", new { visible = true });

      Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
      var body = await response.Content.ReadFromJsonAsync<SleepStateResponse>();
      Assert.NotNull(body);
      Assert.Equal("Standby", body!.WakeState);
      Assert.True(body.IsSleeping);
    }
    finally
    {
      await ResetSleepStateAsync();
    }
  }

  [Fact]
  public async Task SetSleepScreenVisible_SendsTheWakeStateAsAStringOnTheWire()
  {
    // ENC-8's lesson, pinned on the wire rather than on a typed read: the enum crosses as a
    // string. The typed deserialization above would accept a numeric wakeState just as happily,
    // and the Web's string-typed DTO would then hold a value it can never match against
    // nameof(ConsoleWakeState.Standby).
    //
    // The 501 branch SetSleepScreenVisible carries for a missing sleep service is deliberately not
    // covered here: Program.cs always registers SleepService, so it is unreachable through this
    // host. It stays in the controller because it matches the shipped POST /api/system/sleep
    // posture rather than inventing a second one.
    await ResetSleepStateAsync();
    try
    {
      var response = await _client.PostAsJsonAsync(
        "/api/system/sleep-screen", new { visible = true });

      // Whitespace-stripped so the assertion pins the shape rather than the serializer's
      // formatting settings.
      string json = new string(
        (await response.Content.ReadAsStringAsync()).Where(c => !char.IsWhiteSpace(c)).ToArray());

      Assert.Contains("\"wakeState\":\"Ambient\"", json);
    }
    finally
    {
      await ResetSleepStateAsync();
    }
  }
}
