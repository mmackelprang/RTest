using System.Net.Security;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Infrastructure.Audio.Outputs;
using Sharpcaster;
using Sharpcaster.Channels;
using Sharpcaster.Interfaces;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-84: an exception escaping one of SharpCaster 3.0.0's <c>async void</c> callbacks must be
/// reported to us instead of terminating the process.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>How these fail when the guard is removed: by taking the test host down.</b> That is not a
/// flaw in the tests, it is the defect reproducing — an exception re-thrown on the thread pool from
/// an <c>async void</c> method ends any .NET process, the test host included, exactly as it ended
/// radio-api on the box on 2026-09-30. The run is then reported as aborted, which is a failure.
/// When mutation-checking, run this class on its own (<c>--filter</c>) so the abort is attributable.
/// </para>
/// <para>
/// The faulting transport is <see cref="BrokenPipeSslStream"/> planted in the client's private
/// <c>_stream</c>: SharpCaster's real <c>SendAsync</c> runs and its <c>WriteAsync</c> throws the
/// same <see cref="IOException"/> the box logged. Nothing else about the client is faked.
/// </para>
/// <para>
/// No assertion races a wall clock (CLAUDE.md § Test Timing): each test waits on the fault being
/// DELIVERED to a <see cref="TaskCompletionSource{TResult}"/>. The <c>WaitAsync</c> timeouts are hang
/// guards that only decide how long a failing run takes, never whether a passing one passes.
/// </para>
/// </remarks>
public class SharpCasterCallbackGuardTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(15);

  [Fact]
  public async Task HeartbeatTimerPing_OnABrokenTransport_IsReportedInsteadOfThrown()
  {
    // THE measured crash: HeartbeatChannel.TimerElapsed -> SendAsync -> Broken pipe.
    var client = ClientWithBrokenTransport();
    var faults = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

    Assert.True(SharpCasterCallbackGuard.TryHarden(client, ex => faults.TrySetResult(ex), NullLogger.Instance));

    // Fire the library's own timer, on its own thread, through its own handler — only the
    // 10 s interval is shortened so the test does not wait for it.
    var timer = HeartbeatTimer(client.HeartbeatChannel);
    timer.Interval = 1;
    client.HeartbeatChannel.StartTimeoutTimer();

    var fault = await faults.Task.WaitAsync(HangGuard);

    var io = Assert.IsType<IOException>(fault);
    Assert.Contains("Broken pipe", io.Message);
  }

  [Fact]
  public async Task HeartbeatAgainstASilentSpeaker_TimesOut()
  {
    // Pre-merge review M2. Writes SUCCEED (a vanished peer without a TCP reset) and nothing comes
    // back. SharpCaster's one-shot timer sends one PING and is never re-armed, so its timeout
    // branch, the StatusChanged its client disconnects on, could never run. The guard re-arms it.
    var client = new ChromecastClient();
    PlantTransport(client, new SilentSslStream());
    Assert.True(SharpCasterCallbackGuard.TryHarden(client, _ => { }, NullLogger.Instance));

    var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.HeartbeatChannel.StatusChanged += (_, _) => timedOut.TrySetResult();

    HeartbeatTimer(client.HeartbeatChannel).Interval = 1;
    client.HeartbeatChannel.StartTimeoutTimer();

    await timedOut.Task.WaitAsync(HangGuard);
    client.HeartbeatChannel.Dispose();
  }

  [Fact]
  public async Task PongReplyToAPing_OnABrokenTransport_IsReportedInsteadOfThrown()
  {
    // The second async-void heartbeat entry point: the receive loop hands a PING to
    // OnMessageReceived, which awaits a PONG send.
    var client = ClientWithBrokenTransport();
    var faults = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
    Assert.True(SharpCasterCallbackGuard.TryHarden(client, ex => faults.TrySetResult(ex), NullLogger.Instance));

    client.HeartbeatChannel.OnMessageReceived("{\"type\":\"PING\"}", "PING");

    var fault = await faults.Task.WaitAsync(HangGuard);
    Assert.IsType<IOException>(fault);
  }

  [Fact]
  public void TryHarden_ReplacesHeartbeatAndConnection_KeepsEveryOtherChannel()
  {
    // Also the canary for the two private members the guard reads by reflection
    // (ChromecastClient.Channels, HeartbeatChannel._timer): if a SharpCaster change moves
    // either, TryHarden returns false and this fails here rather than on the box.
    var client = new ChromecastClient();
    var custom = new DirectCastAudioChannel("urn:x-cast:test.audio", NullLogger.Instance) { Client = client };
    SetChannels(client, Channels(client).Append(custom).ToList());
    var before = Channels(client);

    Assert.True(SharpCasterCallbackGuard.TryHarden(client, _ => { }, NullLogger.Instance));

    var after = Channels(client);
    Assert.Equal(before.Count, after.Count);
    Assert.IsType<GuardedHeartbeatChannel>(Assert.Single(after.OfType<HeartbeatChannel>()));
    Assert.IsType<GuardedConnectionChannel>(Assert.Single(after.OfType<ConnectionChannel>()));
    Assert.Contains(custom, after);
    Assert.Same(before.OfType<ReceiverChannel>().Single(), after.OfType<ReceiverChannel>().Single());
    Assert.Same(before.OfType<MediaChannel>().Single(), after.OfType<MediaChannel>().Single());
    Assert.All(after, ch => Assert.Same(client, ((ChromecastChannel)ch).Client));

    // The client's own accessors must resolve to the guarded instances — that is what
    // ConnectChromecast starts the timer on.
    Assert.IsType<GuardedHeartbeatChannel>(client.HeartbeatChannel);
    Assert.IsType<GuardedConnectionChannel>(client.ConnectionChannel);
  }

  [Fact]
  public void TryHarden_OnAClientAlreadyGuarded_InstallsFreshChannels_AndDisposesTheOldTimer()
  {
    // Every connect re-hardens (SharpCaster's DisconnectAsync swaps an unguarded heartbeat
    // back in, and clients are reused). The replaced heartbeat's timer must be dead, or it
    // would keep pinging on the next connection and eventually make the client disconnect
    // itself after a missed PONG.
    var client = new ChromecastClient();
    Assert.True(SharpCasterCallbackGuard.TryHarden(client, _ => { }, NullLogger.Instance));
    var first = client.HeartbeatChannel;
    var firstTimer = HeartbeatTimer(first);

    Assert.True(SharpCasterCallbackGuard.TryHarden(client, _ => { }, NullLogger.Instance));

    Assert.NotSame(first, client.HeartbeatChannel);
    Assert.Single(Channels(client).OfType<HeartbeatChannel>());
    Assert.Throws<ObjectDisposedException>(() => firstTimer.Start());
  }

  [Fact]
  public async Task FaultContext_RunsOrdinaryPostedWork_AndReportsWhatThrows()
  {
    // Post must execute continuations, not only the async-void re-throw — a context that
    // dropped work would turn a future library change into a hang.
    var faults = new List<Exception>();
    var context = new CastFaultContext(ex => { lock (faults) { faults.Add(ex); } });

    var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    context.Post(_ => ran.SetResult(), null);
    await ran.Task.WaitAsync(HangGuard);

    var thrown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    context.Post(_ =>
    {
      thrown.SetResult();
      throw new InvalidOperationException("posted failure");
    }, null);
    await thrown.Task.WaitAsync(HangGuard);

    // The report happens after the callback returns control; wait for it by polling the
    // recorded list rather than the clock.
    await WaitUntilAsync(() => { lock (faults) { return faults.Count == 1; } });
    Assert.IsType<InvalidOperationException>(faults.Single());
  }

  // --- helpers ---

  /// <summary>
  /// An SslStream whose writes fail the way the box's did. SharpCaster types its field as
  /// SslStream and calls WriteAsync(ReadOnlyMemory, CancellationToken), which is virtual.
  /// </summary>
  private sealed class BrokenPipeSslStream : SslStream
  {
    public BrokenPipeSslStream()
      : base(new MemoryStream())
    {
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
      return ValueTask.FromException(new IOException(
        "Unable to write data to the transport connection: Broken pipe."));
    }
  }

  internal static ChromecastClient ClientWithBrokenTransport()
  {
    var client = new ChromecastClient();
    PlantBrokenTransport(client);
    return client;
  }

  /// <summary>An SslStream whose writes succeed and go nowhere: a peer that is silently gone.</summary>
  private sealed class SilentSslStream : SslStream
  {
    public SilentSslStream()
      : base(new MemoryStream())
    {
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
      return ValueTask.CompletedTask;
    }
  }

  internal static void PlantBrokenTransport(ChromecastClient client) => PlantTransport(client, new BrokenPipeSslStream());

  private static void PlantTransport(ChromecastClient client, SslStream stream)
  {
    var field = typeof(ChromecastClient).GetField("_stream", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);
    field!.SetValue(client, stream);
  }

  private static System.Timers.Timer HeartbeatTimer(HeartbeatChannel channel)
  {
    var field = typeof(HeartbeatChannel).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);
    return (System.Timers.Timer)field!.GetValue(channel)!;
  }

  private static List<IChromecastChannel> Channels(ChromecastClient client)
  {
    var prop = typeof(ChromecastClient).GetProperty("Channels", BindingFlags.NonPublic | BindingFlags.Instance);
    return ((IEnumerable<IChromecastChannel>)prop!.GetValue(client)!).ToList();
  }

  private static void SetChannels(ChromecastClient client, List<IChromecastChannel> channels)
  {
    var prop = typeof(ChromecastClient).GetProperty("Channels", BindingFlags.NonPublic | BindingFlags.Instance);
    prop!.SetValue(client, channels);
  }

  private static async Task WaitUntilAsync(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + HangGuard;
    while (!condition())
    {
      Assert.True(DateTime.UtcNow < deadline, "condition not reached before the hang guard");
      await Task.Yield();
      await Task.Delay(1);
    }
  }
}
