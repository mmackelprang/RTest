using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Radio.API;
using Radio.IntegrationTests.TestSupport;

namespace Radio.IntegrationTests.SignalR;

/// <summary>
/// Integration tests for the AudioStateHub SignalR hub.
/// Tests real-time communication features.
/// </summary>
public class AudioStateHubIntegrationTests : IClassFixture<IntegrationTestWebApplicationFactory>, IAsyncLifetime
{
  private readonly IntegrationTestWebApplicationFactory _factory;
  private HubConnection? _hubConnection;

  public AudioStateHubIntegrationTests(IntegrationTestWebApplicationFactory factory)
  {
    _factory = factory;
  }

  public async Task InitializeAsync()
  {
    // Create a hub connection using the factory's server
    var server = _factory.Server;
    _hubConnection = new HubConnectionBuilder()
      .WithUrl(
        new Uri(server.BaseAddress, "/hubs/audio"),
        options =>
        {
          options.HttpMessageHandlerFactory = _ => server.CreateHandler();
        })
      .Build();
  }

  public async Task DisposeAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.DisposeAsync();
    }
  }

  [Fact]
  public async Task AudioStateHub_Connect_Succeeds()
  {
    // Act
    await _hubConnection!.StartAsync();

    // Assert
    Assert.Equal(HubConnectionState.Connected, _hubConnection.State);
  }

  [Fact]
  public async Task AudioStateHub_SubscribeToQueue_Succeeds()
  {
    // Arrange
    await _hubConnection!.StartAsync();

    // Act - Subscribe to queue
    await _hubConnection.InvokeAsync("SubscribeToQueue");

    // Assert - No exception means success
    Assert.Equal(HubConnectionState.Connected, _hubConnection.State);
  }

  [Fact]
  public async Task AudioStateHub_UnsubscribeFromQueue_Succeeds()
  {
    // Arrange
    await _hubConnection!.StartAsync();
    await _hubConnection.InvokeAsync("SubscribeToQueue");

    // Act - Unsubscribe from queue
    await _hubConnection.InvokeAsync("UnsubscribeFromQueue");

    // Assert - No exception means success
    Assert.Equal(HubConnectionState.Connected, _hubConnection.State);
  }

  [Fact]
  public async Task AudioStateHub_SubscribeToRadioState_Succeeds()
  {
    // Arrange
    await _hubConnection!.StartAsync();

    // Act - Subscribe to radio state
    await _hubConnection.InvokeAsync("SubscribeToRadioState");

    // Assert - No exception means success
    Assert.Equal(HubConnectionState.Connected, _hubConnection.State);
  }

  [Fact]
  public async Task AudioStateHub_UnsubscribeFromRadioState_Succeeds()
  {
    // Arrange
    await _hubConnection!.StartAsync();
    await _hubConnection.InvokeAsync("SubscribeToRadioState");

    // Act - Unsubscribe
    await _hubConnection.InvokeAsync("UnsubscribeFromRadioState");

    // Assert - No exception means success
    Assert.Equal(HubConnectionState.Connected, _hubConnection.State);
  }

  [Fact]
  public async Task AudioStateHub_MultipleConnections_AllConnect()
  {
    // Arrange
    var connections = new List<HubConnection>();
    var server = _factory.Server;

    try
    {
      // Create 3 connections
      for (int i = 0; i < 3; i++)
      {
        var connection = new HubConnectionBuilder()
          .WithUrl(
            new Uri(server.BaseAddress, "/hubs/audio"),
            options =>
            {
              options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
          .Build();
        connections.Add(connection);
      }

      // Act - Connect all
      foreach (var connection in connections)
      {
        await connection.StartAsync();
      }

      // Assert - All connected
      Assert.All(connections, c => Assert.Equal(HubConnectionState.Connected, c.State));
    }
    finally
    {
      foreach (var connection in connections)
      {
        await connection.DisposeAsync();
      }
    }
  }

  [Fact]
  public async Task AudioStateHub_ConnectDisconnect_HandlesProperly()
  {
    // Arrange
    await _hubConnection!.StartAsync();
    Assert.Equal(HubConnectionState.Connected, _hubConnection.State);

    // Act - Disconnect
    await _hubConnection.StopAsync();

    // Assert
    Assert.Equal(HubConnectionState.Disconnected, _hubConnection.State);
  }

  [Fact]
  public async Task AudioStateHub_Reconnect_Works()
  {
    // Arrange - Connect and disconnect
    await _hubConnection!.StartAsync();
    await _hubConnection.StopAsync();
    Assert.Equal(HubConnectionState.Disconnected, _hubConnection.State);

    // Act - Reconnect
    await _hubConnection.StartAsync();

    // Assert
    Assert.Equal(HubConnectionState.Connected, _hubConnection.State);
  }

  [Fact]
  public async Task AudioStateHub_SubscribeMultipleGroups_Works()
  {
    // Arrange
    await _hubConnection!.StartAsync();

    // Act - Subscribe to both groups
    await _hubConnection.InvokeAsync("SubscribeToQueue");
    await _hubConnection.InvokeAsync("SubscribeToRadioState");

    // Assert - Connection still active
    Assert.Equal(HubConnectionState.Connected, _hubConnection.State);

    // Cleanup - Unsubscribe
    await _hubConnection.InvokeAsync("UnsubscribeFromQueue");
    await _hubConnection.InvokeAsync("UnsubscribeFromRadioState");
  }
}
