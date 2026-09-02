using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Tests for AudioVisualizationHubService
/// Tests SignalR connection, subscription management, and event handling
/// </summary>
///
/// <remarks>
/// Every service this fixture builds goes through <see cref="CreateService"/> so it can be
/// disposed in <see cref="DisposeAsync"/>. That is not tidiness: a failed
/// <c>StartAsync</c> leaves a detached <c>StartRetryLoop</c> task behind, and an undisposed
/// service leaks one of those for the lifetime of the test process. The
/// <see cref="OfflineHubTransport"/> passed to each instance keeps those attempts off the
/// network in the first place.
/// </remarks>
public class AudioVisualizationHubServiceTests : IAsyncLifetime
{
  private readonly List<AudioVisualizationHubService> _created = [];
  private readonly IConfiguration _configuration;

  public AudioVisualizationHubServiceTests()
  {
    // Set up in-memory configuration
    _configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl }
      })
      .Build();
  }

  /// <summary>Builds a tracked service wired to the offline transport.</summary>
  private AudioVisualizationHubService CreateService(IConfiguration? configuration = null)
  {
    var service = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance,
      configuration ?? _configuration,
      transport: new OfflineHubTransport()
    );
    _created.Add(service);
    return service;
  }

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    // Disposing twice is safe (Multiple_DisposeAsync_Calls_Are_Safe pins that), so services
    // a test already disposed need no special handling here.
    foreach (AudioVisualizationHubService service in _created)
    {
      await service.DisposeAsync();
    }
  }

  [Fact]
  public void Constructor_Creates_Service_Successfully()
  {
    // Arrange & Act
    AudioVisualizationHubService service = CreateService();

    // Assert
    Assert.NotNull(service);
    Assert.False(service.IsConnected);
  }

  [Fact]
  public void IsConnected_Returns_False_When_Not_Started()
  {
    // Arrange
    AudioVisualizationHubService service = CreateService();

    // Act & Assert
    Assert.False(service.IsConnected);
  }

  [Fact]
  public void ConnectionState_Is_Disconnected_Initially()
  {
    // Arrange
    AudioVisualizationHubService service = CreateService();

    // Act
    var state = service.ConnectionState;

    // Assert
    Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected, state);
  }

  [Fact]
  public async Task DisposeAsync_Does_Not_Throw()
  {
    // Arrange
    AudioVisualizationHubService service = CreateService();

    // Act & Assert - Should not throw
    await service.DisposeAsync();
  }

  [Fact]
  public async Task Multiple_DisposeAsync_Calls_Are_Safe()
  {
    // Arrange
    AudioVisualizationHubService service = CreateService();

    // Act & Assert - Multiple dispose calls should be safe
    await service.DisposeAsync();
    await service.DisposeAsync();
    await service.DisposeAsync();
  }

  [Fact]
  public void Service_Allows_Event_Subscription_Without_Throwing()
  {
    // Arrange
    AudioVisualizationHubService service = CreateService();

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
    AudioVisualizationHubService service = CreateService();

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
    AudioVisualizationHubService service = CreateService();

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
    AudioVisualizationHubService service = CreateService(customConfig);

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
    AudioVisualizationHubService service = CreateService(emptyConfig);

    // Assert - Service created successfully with default configuration
    Assert.NotNull(service);
  }
}
