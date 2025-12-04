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
}
