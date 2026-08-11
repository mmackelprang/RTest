using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// Pins the connect/teardown data race on <see cref="GoogleCastOutput"/>'s
/// <c>_client</c> / <c>_connectedReceiver</c>.
///
/// Before the fix these were mutated with no synchronization at all from two
/// concurrent actors: the startup auto-connect (which reached into the output
/// directly) and the output gate's teardown. The interleaving that bit is a
/// teardown nulling <c>_connectedReceiver</c> between the connect's assignment
/// and its dereference, which surfaced as a NullReferenceException out of a
/// background task — or, worse, a connect publishing over a completed teardown
/// and leaving Cast streaming while the local sink had already been unmuted.
///
/// Deliberately ONE deterministic test rather than a stress loop. Concurrent
/// connect/disconnect loops were tried and discarded: with an unreachable device
/// the reachability probe fails before the connect ever reaches the racing
/// window, so they passed against the unfixed code — coverage theatre that also
/// added enough CPU load to destabilise neighbouring timing-sensitive tests.
/// This test instead drives the exact interleaving through a hook, and is
/// mutation-verified: it fails with NullReferenceException against field-based
/// receiver handling.
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

      // Stop listening before handing control back. The connect proceeds to a
      // Cast handshake we have no intention of satisfying; with the socket gone
      // it fails immediately with connection-refused instead of parking a task
      // on it for the rest of the run. That lingering work is not hypothetical —
      // it measurably destabilised the timing-sensitive Bluetooth fixtures.
      listener.Stop();
    };

    try
    {
      // Safety net only — with the listener stopped in the hook the connect
      // fails in milliseconds. The assertions below depend only on what happened
      // at the hook, which fires before the handshake.
      await output.ConnectAsync(Device("cast-a", port))
        .WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (NullReferenceException ex)
    {
      escaped = ex;
    }
    catch (Exception)
    {
      // Connect failing against a socket we just closed is the expected path.
    }

    Assert.True(teardownRan, "hook did not fire — the test never reached the racing window");
    Assert.Null(escaped);

    // And the teardown must have won: a superseded connect may not resurrect it.
    Assert.Null(output.ConnectedDevice);

    // Losing the race must leave the output USABLE. ConnectAsync sets
    // Connecting on entry and refuses to run unless the state is Ready/Stopped,
    // while ValidateCanInitialize only accepts Created/Error — so a path that
    // returns while still Connecting wedges Cast until the process restarts.
    Assert.NotEqual(AudioOutputState.Connecting, output.State);
  }

  [Fact]
  public async Task ConnectThatSucceedsButLostTheRace_DiscardsItselfAndStaysUsable()
  {
    // The supersede-after-SUCCESS path — the entire reason the generation check
    // exists, and unreachable offline without substituting the transport, since
    // a fake socket can never complete a Cast handshake and the connect would
    // instead divert into the error handler.
    //
    // Losing this race is the NORMAL outcome of a teardown landing during
    // startup auto-connect, so it must not be a one-way door: ConnectAsync sets
    // Connecting on entry and refuses to run unless Ready/Stopped, and
    // ValidateCanInitialize only accepts Created/Error — returning while still
    // Connecting wedges Cast until the process restarts.
    // The listener only has to satisfy the reachability probe that runs before
    // the transport; the override means nothing is ever spoken over it.
    using var listener = StartLoopbackListener(out var port);
    await using var output = BuildOutput();
    await output.InitializeAsync();

    // A teardown lands while this connect is in flight...
    output.ConnectRaceHookForTests = async () =>
    {
      try { await output.DisconnectAsync(); } catch { /* not the point */ }
    };
    // ...and then the connect nonetheless "succeeds" on the wire.
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;

    await output.ConnectAsync(Device("cast-a", port));

    // The teardown won: the superseded connect must not have published itself.
    Assert.Null(output.ConnectedDevice);
    Assert.NotEqual(AudioOutputState.Connecting, output.State);

    // And the output must still be connectable. This is the wedge assertion:
    // from Connecting, ConnectAsync throws "Cannot connect in state ...".
    output.ConnectRaceHookForTests = null;
    await output.ConnectAsync(Device("cast-b", port));

    Assert.Equal("cast-b", output.ConnectedDevice?.Id);
  }

  // --- helpers ---

  /// <summary>
  /// A loopback listener that accepts and immediately closes. The accept is what
  /// lets the Cast reachability probe succeed so the connect proceeds into the
  /// racing window; the immediate close makes the subsequent Cast handshake fail
  /// fast instead of hanging on a socket that never answers.
  /// </summary>
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
