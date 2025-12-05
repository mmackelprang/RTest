using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the SystemController.
/// </summary>
public class SystemControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public SystemControllerTests(WebApplicationFactory<Program> factory)
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
}
