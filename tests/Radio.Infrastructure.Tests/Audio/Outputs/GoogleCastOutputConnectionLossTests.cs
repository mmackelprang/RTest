using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Sharpcaster;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-84 / AUD-37: when a published Cast connection goes away, <see cref="GoogleCastOutput"/>
/// must notice, leave <c>Streaming</c>, and raise <see cref="GoogleCastOutput.Disconnected"/> with
/// <see cref="ChromecastDisconnectedEventArgs.IsConnectionLost"/> set — once — so the console can
/// restore its local speakers. Deliberate disconnects and stale reports must not.
/// </summary>
/// <remarks>
/// The connection is established the way <c>GoogleCastOutputConcurrencyTests</c> does it: a
/// loopback listener satisfies the reachability probe and the transport/status overrides stand in
/// for a Cast handshake no fake socket can complete. Everything after that — the guard installed
/// by ConnectAsync, the generation checks, the teardown — is the real code.
/// No assertion races a wall clock: each test waits on the event being raised, or on
/// <c>LastConnectionLossHandling</c> completing. Timeouts are hang guards only.
/// </remarks>
public class GoogleCastOutputConnectionLossTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(15);

  [Fact]
  public async Task BrokenTransportUnderTheHeartbeat_TearsTheConnectionDown_AndReportsItLost()
  {
    // End to end through the guard ConnectAsync installed: a PING arrives, the PONG write hits
    // a broken pipe inside SharpCaster's async-void handler, and the output must hear about it.
    using var listener = StartLoopbackListener(out var port);
    await using var output = await ConnectedOutputAsync(port);
    var lost = CaptureLoss(output);

    var client = ClientOf(output);
    SharpCasterCallbackGuardTests.PlantBrokenTransport(client);
    client.HeartbeatChannel.OnMessageReceived("{\"type\":\"PING\"}", "PING");

    var e = await lost.Task.WaitAsync(HangGuard);
    await output.LastConnectionLossHandling.WaitAsync(HangGuard);

    Assert.True(e.IsConnectionLost);
    Assert.Equal("cast-a", e.Device?.Id);
    Assert.Equal("a SharpCaster background send failed", e.Reason);
    Assert.Null(output.ConnectedDevice);
    Assert.NotEqual(AudioOutputState.Streaming, output.State);
    Assert.Equal(AudioOutputState.Error, output.State);
  }

  [Fact]
  public async Task SharpCasterDisconnected_OnThePublishedConnection_IsReportedLost()
  {
    // SharpCaster raises Disconnected from its own teardown after a heartbeat timeout or a
    // receiver CLOSE — the speaker going away without any write of ours failing.
    using var listener = StartLoopbackListener(out var port);
    await using var output = await ConnectedOutputAsync(port);
    var lost = CaptureLoss(output);

    RaiseClientDisconnected(ClientOf(output));

    var e = await lost.Task.WaitAsync(HangGuard);
    Assert.True(e.IsConnectionLost);
    Assert.Null(output.ConnectedDevice);
  }

  [Fact]
  public async Task RepeatedLossReports_ForOneConnection_RaiseTheEventOnce()
  {
    using var listener = StartLoopbackListener(out var port);
    await using var output = await ConnectedOutputAsync(port);
    var events = new List<ChromecastDisconnectedEventArgs>();
    output.Disconnected += (_, e) => { lock (events) { events.Add(e); } };
    var generation = PublishedGeneration(output);

    output.ReportConnectionLost(generation, "first", null);
    await output.LastConnectionLossHandling.WaitAsync(HangGuard);
    output.ReportConnectionLost(generation, "second", null);
    await output.LastConnectionLossHandling.WaitAsync(HangGuard);

    Assert.Equal("first", Assert.Single(events).Reason);
  }

  [Fact]
  public async Task LossReport_ForASupersededGeneration_IsIgnored()
  {
    // The anti-vacuity twin of the tests above: a report must name the CURRENT connection.
    using var listener = StartLoopbackListener(out var port);
    await using var output = await ConnectedOutputAsync(port);
    var events = new List<ChromecastDisconnectedEventArgs>();
    output.Disconnected += (_, e) => { lock (events) { events.Add(e); } };

    output.ReportConnectionLost(PublishedGeneration(output) - 1, "stale", null);
    await output.LastConnectionLossHandling.WaitAsync(HangGuard);

    Assert.Empty(events);
    Assert.Equal("cast-a", output.ConnectedDevice?.Id);
  }

  [Fact]
  public async Task DeliberateDisconnect_IsNotReportedAsLost_EvenWhenTheClientThenRaisesDisconnected()
  {
    // Our own teardown makes SharpCaster raise Disconnected too. That must not be mistaken for
    // a lost speaker — the console would otherwise override the output the user just chose.
    using var listener = StartLoopbackListener(out var port);
    await using var output = await ConnectedOutputAsync(port);
    var client = ClientOf(output);
    var events = new List<ChromecastDisconnectedEventArgs>();
    output.Disconnected += (_, e) => { lock (events) { events.Add(e); } };

    await output.DisconnectAsync();
    RaiseClientDisconnected(client);
    await output.LastConnectionLossHandling.WaitAsync(HangGuard);

    var e = Assert.Single(events);
    Assert.False(e.IsConnectionLost);
  }

  [Fact]
  public void DirectChannelSendFailures_AreReportedOnTheTenthInARow_AndOnlyOnce()
  {
    var reports = 0;
    var service = new DirectCastStreamingService(
      NullLogger.Instance,
      new Mock<IAudioEngine>().Object,
      new DirectCastAudioChannel("urn:x-cast:test.audio", NullLogger.Instance),
      new GoogleCastOutputOptions(),
      onSendsFailing: _ => reports++);

    for (var i = 1; i < DirectCastStreamingService.ConsecutiveSendFailuresBeforeReport; i++)
    {
      service.ReportIfSendsKeepFailing(new IOException("Broken pipe"));
    }
    Assert.Equal(0, reports);

    service.ReportIfSendsKeepFailing(new IOException("Broken pipe"));
    Assert.Equal(1, reports);

    for (var i = 0; i < 25; i++)
    {
      service.ReportIfSendsKeepFailing(new IOException("Broken pipe"));
    }
    Assert.Equal(1, reports);
  }

  // --- helpers ---

  private static async Task<GoogleCastOutput> ConnectedOutputAsync(int port)
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath =
      Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    var output = new GoogleCastOutput(new Mock<ILogger<GoogleCastOutput>>().Object, Options.Create(options));

    await output.InitializeAsync();
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests = () => Task.FromResult<(float Volume, bool Muted)?>(null);
    await output.ConnectAsync(new ChromecastDeviceInfo
    {
      Id = "cast-a",
      FriendlyName = "Office speaker",
      IpAddress = "127.0.0.1",
      Port = port,
      Model = "test"
    });

    Assert.Equal("cast-a", output.ConnectedDevice?.Id);
    return output;
  }

  private static TaskCompletionSource<ChromecastDisconnectedEventArgs> CaptureLoss(GoogleCastOutput output)
  {
    var tcs = new TaskCompletionSource<ChromecastDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
    output.Disconnected += (_, e) =>
    {
      if (e.IsConnectionLost)
      {
        tcs.TrySetResult(e);
      }
    };
    return tcs;
  }

  private static ChromecastClient ClientOf(GoogleCastOutput output)
  {
    var field = typeof(GoogleCastOutput).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance);
    return (ChromecastClient)field!.GetValue(output)!;
  }

  private static int PublishedGeneration(GoogleCastOutput output)
  {
    var field = typeof(GoogleCastOutput).GetField("_publishedGeneration", BindingFlags.NonPublic | BindingFlags.Instance);
    return (int)field!.GetValue(output)!;
  }

  private static void RaiseClientDisconnected(ChromecastClient client)
  {
    // OnDisconnected is the protected virtual SharpCaster's own teardown calls.
    var method = typeof(ChromecastClient).GetMethod("OnDisconnected", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(method);
    method!.Invoke(client, null);
  }

  private static System.Net.Sockets.TcpListener StartLoopbackListener(out int port)
  {
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

    _ = Task.Run(async () =>
    {
      try
      {
        while (true)
        {
          using var client = await listener.AcceptTcpClientAsync();
          client.Close();
        }
      }
      catch
      {
        // Listener stopped.
      }
    });

    return listener;
  }
}
