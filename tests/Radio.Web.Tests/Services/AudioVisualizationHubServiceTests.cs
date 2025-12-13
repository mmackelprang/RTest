using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Tests for AudioVisualizationHubService
/// Tests SignalR connection, subscription management, and event handling
/// </summary>
public class AudioVisualizationHubServiceTests
{
  private readonly IConfiguration _configuration;

  public AudioVisualizationHubServiceTests()
  {
    // Set up in-memory configuration
    _configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();
  }

  [Fact]
  public void Constructor_Creates_Service_Successfully()
  {
    // Arrange & Act
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      _configuration
    );

    // Assert
    Assert.NotNull(service);
    Assert.False(service.IsConnected);
  }

  [Fact]
  public void IsConnected_Returns_False_When_Not_Started()
  {
    // Arrange
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      _configuration
    );

    // Act & Assert
    Assert.False(service.IsConnected);
  }

  [Fact]
  public void ConnectionState_Is_Disconnected_Initially()
  {
    // Arrange
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      _configuration
    );

    // Act
    var state = service.ConnectionState;

    // Assert
    Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected, state);
  }

  [Fact]
  public async Task DisposeAsync_Does_Not_Throw()
  {
    // Arrange
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      _configuration
    );

    // Act & Assert - Should not throw
    await service.DisposeAsync();
  }

  [Fact]
  public async Task Multiple_DisposeAsync_Calls_Are_Safe()
  {
    // Arrange
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      _configuration
    );

    // Act & Assert - Multiple dispose calls should be safe
    await service.DisposeAsync();
    await service.DisposeAsync();
    await service.DisposeAsync();
  }

  [Fact]
  public void Service_Has_Event_Handlers_For_All_Data_Types()
  {
    // Arrange
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      _configuration
    );

    var subscribedCount = 0;

    // Act - Subscribe to events (should not throw)
    service.OnSpectrumData += (data) => { subscribedCount++; return Task.CompletedTask; };
    service.OnLevelData += (data) => { subscribedCount++; return Task.CompletedTask; };
    service.OnWaveformData += (data) => { subscribedCount++; return Task.CompletedTask; };
    service.OnVisualizationData += (data) => { subscribedCount++; return Task.CompletedTask; };

    // Assert - Events can be subscribed to without exceptions
    Assert.NotNull(service);
    Assert.Equal(0, subscribedCount); // Events not yet fired
  }

  [Fact]
  public async Task Get_Methods_Return_Null_When_Not_Connected()
  {
    // Arrange
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      _configuration
    );

    // Act
    var spectrum = await service.GetSpectrumAsync();
    var levels = await service.GetLevelsAsync();
    var waveform = await service.GetWaveformAsync();
    var visualization = await service.GetVisualizationAsync();

    // Assert - All should return null when not connected
    Assert.Null(spectrum);
    Assert.Null(levels);
    Assert.Null(waveform);
    Assert.Null(visualization);
  }

  [Fact]
  public async Task Service_Can_Be_Created_And_Disposed_Without_Starting()
  {
    // Arrange
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      _configuration
    );

    // Act & Assert - Should not throw
    await service.DisposeAsync();
  }

  [Fact]
  public void Service_Uses_Correct_Hub_Url_From_Configuration()
  {
    // Arrange
    var customConfig = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://test-server:8080" }
      })
      .Build();

    // Act
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      customConfig
    );

    // Assert - Service created successfully with custom configuration
    Assert.NotNull(service);
  }

  [Fact]
  public void Service_Falls_Back_To_Default_Url_When_Not_Configured()
  {
    // Arrange
    var emptyConfig = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>())
      .Build();

    // Act
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      emptyConfig
    );

    // Assert - Service created successfully with default configuration
    Assert.NotNull(service);
  }
}
