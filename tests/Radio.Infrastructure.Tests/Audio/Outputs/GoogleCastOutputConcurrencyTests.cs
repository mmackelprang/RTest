using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// Exercises the connect/teardown data race on <see cref="GoogleCastOutput"/>'s
/// <c>_client</c> / <c>_connectedReceiver</c>.
///
/// Before the fix these were mutated with no synchronization at all from two
/// concurrent actors: the startup auto-connect (which reached into the output
/// directly) and the output gate's teardown. The classic interleaving is a
/// teardown nulling <c>_connectedReceiver</c> between the connect's assignment
/// and its dereference, which surfaced as a NullReferenceException out of a
/// background task — or, worse, a connect publishing over a completed teardown
/// and leaving Cast streaming while the local sink had already been unmuted.
///
/// These tests need no Chromecast and no network: every device points at a
/// closed loopback port, so <c>ConnectAsync</c> fails fast and the interesting
/// part (the field-mutation interleaving) still happens.
/// </summary>
public class GoogleCastOutputConcurrencyTests
{
  [Fact]
  public async Task TeardownDuringConnect_DoesNotCorruptTheInFlightConnect()
  {
    // THE regression test for the race, made deterministic rather than hoped for.
    //
    // A listening socket is required: the reachability probe must SUCCEED so the
    // connect gets past it and into the window where the original code had
    // already stored the receiver in a field. The hook then fires a teardown at
    // exactly that point. Pre-fix, the teardown nulled _connectedReceiver and the
    // connect dereferenced it on the next line -> NullReferenceException.
    using var listener = StartLoopbackListener(out var port);
    await using var output = BuildOutput();
    await output.InitializeAsync();

    Exception? escaped = null;
    var teardownRan = false;

    output.ConnectRaceHookForTests = async () =>
    {
      // The output gate tearing Cast down while this connect is in flight.
      teardownRan = true;
      try { await output.DisconnectAsync(); }
      catch { /* teardown of a half-built connection may fail; not the point */ }
    };

    try
    {
      // Bounded: the listener speaks no Cast protocol, so ConnectChromecast has
      // nothing to complete against and would otherwise wait indefinitely. The
      // assertions below only depend on what happened at the hook, which fires
      // before that call.
      await output.ConnectAsync(Device("cast-a", port))
        .WaitAsync(TimeSpan.FromSeconds(10));
    }
    catch (NullReferenceException ex)
    {
      escaped = ex;
    }
    catch (Exception)
    {
      // Connect failing/timing out against a non-Chromecast listener is expected.
    }

    Assert.True(teardownRan, "hook did not fire — the test never reached the racing window");
    Assert.Null(escaped);

    // And the teardown must have won: a superseded connect may not resurrect it.
    Assert.Null(output.ConnectedDevice);
  }

  [Fact]
  public async Task ConcurrentConnectAndDisconnect_NeverCorruptsConnectionState()
  {
    await using var output = BuildOutput();
    await output.InitializeAsync();

    var device = Device("cast-a");
    var unexpected = new List<Exception>();

    // Hammer the two racing operations. Connect failures are expected (nothing
    // is listening); state corruption is not.
    for (var i = 0; i < 40; i++)
    {
      var connect = RunToleratingConnectFailureAsync(
        () => output.ConnectAsync(device), unexpected);
      var disconnect = RunToleratingConnectFailureAsync(
        () => output.DisconnectAsync(), unexpected);

      await Task.WhenAll(connect, disconnect);
    }

    Assert.True(unexpected.Count == 0,
      "Concurrent connect/disconnect corrupted state: " +
      string.Join(" | ", unexpected.Select(e => $"{e.GetType().Name}: {e.Message}")));
  }

  [Fact]
  public async Task ConcurrentConnectsToDifferentDevices_LeaveExactlyOneWinner()
  {
    // Two connects racing each other must not interleave into a hybrid
    // connection (one device's receiver against the other's client).
    await using var output = BuildOutput();
    await output.InitializeAsync();

    var unexpected = new List<Exception>();

    for (var i = 0; i < 20; i++)
    {
      var a = RunToleratingConnectFailureAsync(() => output.ConnectAsync(Device("cast-a")), unexpected);
      var b = RunToleratingConnectFailureAsync(() => output.ConnectAsync(Device("cast-b")), unexpected);
      await Task.WhenAll(a, b);
    }

    Assert.True(unexpected.Count == 0,
      "Racing connects corrupted state: " +
      string.Join(" | ", unexpected.Select(e => $"{e.GetType().Name}: {e.Message}")));
  }

  [Fact]
  public async Task DisconnectDuringConnect_ClearsConnectedDevice()
  {
    // The outcome that matters for the dual-output bug: once a teardown has run,
    // a connect that completes afterwards must not resurrect ConnectedDevice.
    await using var output = BuildOutput();
    await output.InitializeAsync();

    var unexpected = new List<Exception>();

    for (var i = 0; i < 20; i++)
    {
      var connect = RunToleratingConnectFailureAsync(
        () => output.ConnectAsync(Device("cast-a")), unexpected);
      await Task.Yield();
      var disconnect = RunToleratingConnectFailureAsync(
        () => output.DisconnectAsync(), unexpected);
      await Task.WhenAll(connect, disconnect);
    }

    // Final explicit teardown is the last word.
    await RunToleratingConnectFailureAsync(() => output.DisconnectAsync(), unexpected);

    Assert.Null(output.ConnectedDevice);
    Assert.True(unexpected.Count == 0,
      "Teardown-during-connect corrupted state: " +
      string.Join(" | ", unexpected.Select(e => $"{e.GetType().Name}: {e.Message}")));
  }

  // --- helpers ---

  /// <summary>
  /// Runs a lifecycle operation, swallowing the failures that are legitimate for
  /// an unreachable device while recording the ones that indicate torn state.
  /// </summary>
  private static async Task RunToleratingConnectFailureAsync(
    Func<Task> operation, List<Exception> unexpected)
  {
    try
    {
      await Task.Run(operation);
    }
    catch (NullReferenceException ex)
    {
      // The signature failure of the original race.
      lock (unexpected) { unexpected.Add(ex); }
    }
    catch (ObjectDisposedException ex)
    {
      lock (unexpected) { unexpected.Add(ex); }
    }
    catch (InvalidOperationException ex) when (
      ex.Message.Contains("Client not initialized", StringComparison.OrdinalIgnoreCase))
    {
      // Only torn state can produce this after a successful InitializeAsync.
      lock (unexpected) { unexpected.Add(ex); }
    }
    catch (InvalidOperationException)
    {
      // "Cannot connect in state X" — legitimate: the racing operation moved the
      // state machine first. Not a corruption signal.
    }
    catch (Exception)
    {
      // Unreachable device: socket errors, timeouts, SharpCaster wrappers.
    }
  }

  /// <summary>
  /// A loopback listener that accepts (and ignores) connections, so the Cast
  /// reachability probe succeeds and the connect proceeds into the racing window.
  /// </summary>
  private static System.Net.Sockets.TcpListener StartLoopbackListener(out int port)
  {
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

    // Accept and immediately close. The probe's TCP connect succeeds (which is
    // what gets the connect into the racing window), while the subsequent Cast
    // handshake fails fast instead of hanging on a socket that never answers.
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
        // Listener disposed at test teardown.
      }
    });

    return listener;
  }

  // port defaults to 9 (discard) — reliably closed, so the reachability probe
  // fails fast instead of hanging on a real connect timeout. Pass a live
  // listener's port to get past the probe and into the racing window.
  private static ChromecastDeviceInfo Device(string id, int port = 9) => new()
  {
    Id = id,
    FriendlyName = $"Fake {id}",
    IpAddress = "127.0.0.1",
    Port = port,
    Model = "Test"
  };

  private static GoogleCastOutput BuildOutput()
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath =
      Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");

    return new GoogleCastOutput(
      new Mock<ILogger<GoogleCastOutput>>().Object,
      Options.Create(options));
  }
}
