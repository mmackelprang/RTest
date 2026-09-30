using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Sharpcaster;
using Sharpcaster.Channels;
using Sharpcaster.Interfaces;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Keeps an exception thrown inside one of SharpCaster 3.0.0's <c>async void</c> callbacks from
/// terminating the process, and reports it instead (AUD-84).
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> SharpCaster's <c>HeartbeatChannel</c> sends its PING from
/// <c>private async void TimerElapsed</c>, a <see cref="System.Timers.Timer"/> handler. When the
/// speaker vanishes mid-stream the write fails (measured on the box 2026-09-30:
/// <c>IOException: Broken pipe</c> out of <c>SslStream.WriteAsync</c>), and an exception leaving an
/// <c>async void</c> method with no <see cref="SynchronizationContext"/> is re-thrown on the thread
/// pool, which ends the process. No try/catch in our code can see it: the throw happens inside the
/// library, on a timer thread, long after any call of ours has returned. SharpCaster 3.0.0 is the
/// newest version on NuGet, so no upgrade fixes it.
/// </para>
/// <para>
/// <b>The mechanism used here.</b> An <c>async void</c> method captures
/// <see cref="SynchronizationContext.Current"/> when it is <i>invoked</i>, and on failure the
/// compiler-generated builder <c>Post</c>s the re-throw to that captured context instead of the
/// thread pool. It does so regardless of any <c>ConfigureAwait(false)</c> inside the method, because
/// the capture happens at entry, not at an await. So running the library's own callback with a
/// <see cref="CastFaultContext"/> installed turns a process kill into a call to our fault sink,
/// without changing a single line of the library's heartbeat logic.
/// </para>
/// <para>
/// The two entry points it is installed on, and how:
/// <list type="bullet">
///   <item><c>HeartbeatChannel</c>'s timer — by setting the timer's public
///   <see cref="System.Timers.Timer.SynchronizingObject"/> to <see cref="GuardedTimerInvoker"/>, which
///   runs the elapsed handler with the context installed. The timer is a private field, read by
///   reflection; that is the one private member this class depends on besides <c>Channels</c>.</item>
///   <item><c>HeartbeatChannel.OnMessageReceived</c> (PONG reply) and
///   <c>ConnectionChannel.OnMessageReceived</c> (CLOSE -&gt; <c>DisconnectAsync</c>) — both
///   <c>async void</c> overrides called from the receive loop. They are virtual, so
///   <see cref="GuardedHeartbeatChannel"/> and <see cref="GuardedConnectionChannel"/> override them
///   and call the base implementation with the context installed.</item>
/// </list>
/// </para>
/// <para>
/// <b>NOT covered</b>, stated so nobody reads more into this than it does:
/// <c>ChromecastClient.HeartBeatTimedOut</c> is also <c>async void</c>, but it is raised through
/// <c>ChromecastChannel.SafeInvokeEvent</c>, i.e. from <c>Task.Run</c>, where no context of ours is
/// current. Its body is <c>DisconnectAsync</c>, which, as read from the decompiled 3.0.0 source, performs no
/// socket write: <c>TrySetException</c> on pending requests, a token cancel, and stream/client
/// disposal inside SharpCaster's own try/catch, then the <c>Disconnected</c> event. It is assessed
/// as not throwing in practice, not proved. <see cref="GoogleCastOutput"/>'s <c>Disconnected</c>
/// handler must therefore never throw, because it runs inside that unguarded method.
/// </para>
/// <para>
/// <b>Re-applied on every connect.</b> <c>ChromecastClient.DisconnectAsync</c> replaces the heartbeat
/// channel with a fresh, unguarded one (<c>RecreateHeartbeatChannel</c>), and a client can be
/// reconnected after that. <see cref="TryHarden"/> is therefore called immediately before every
/// <c>ConnectChromecast</c>, and always installs fresh channels bound to that connection's fault sink.
/// </para>
/// </remarks>
internal static class SharpCasterCallbackGuard
{
  private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

  /// <summary>
  /// Replaces <paramref name="client"/>'s heartbeat and connection channels with guarded ones that
  /// report escaped callback exceptions to <paramref name="onFault"/>. Leaves every other channel,
  /// including any custom one already registered, where it was.
  /// </summary>
  /// <param name="client">The client, before <c>ConnectChromecast</c> is called on it.</param>
  /// <param name="onFault">
  /// Receives each exception that escaped a guarded callback. Called on a thread-pool thread. Must
  /// not throw; if it does, the exception is swallowed rather than allowed to end the process.
  /// </param>
  /// <param name="logger">Receives a Warning when the guard could not be installed.</param>
  /// <returns>
  /// True when both channels were replaced. False when SharpCaster's internals no longer match what
  /// this class expects — the client is then left unguarded, exactly as it was before AUD-84.
  /// </returns>
  public static bool TryHarden(ChromecastClient client, Action<Exception> onFault, ILogger logger)
  {
    ArgumentNullException.ThrowIfNull(client);
    ArgumentNullException.ThrowIfNull(onFault);

    try
    {
      var channelsProperty = typeof(ChromecastClient).GetProperty("Channels", PrivateInstance)
        ?? throw new MissingMemberException(nameof(ChromecastClient), "Channels");

      var existing = (channelsProperty.GetValue(client) as IEnumerable<IChromecastChannel>)?.ToList()
        ?? throw new InvalidOperationException("ChromecastClient.Channels is null");

      var context = new CastFaultContext(onFault);
      var heartbeat = new GuardedHeartbeatChannel(context) { Client = client };
      var connection = new GuardedConnectionChannel(context) { Client = client };

      var replaced = new List<IChromecastChannel>(existing.Count);
      var retired = new List<HeartbeatChannel>();
      var sawHeartbeat = false;
      var sawConnection = false;
      foreach (var channel in existing)
      {
        switch (channel)
        {
          case HeartbeatChannel oldHeartbeat:
            retired.Add(oldHeartbeat);
            if (!sawHeartbeat)
            {
              replaced.Add(heartbeat);
              sawHeartbeat = true;
            }
            break;

          case ConnectionChannel:
            if (!sawConnection)
            {
              replaced.Add(connection);
              sawConnection = true;
            }
            break;

          default:
            replaced.Add(channel);
            break;
        }
      }

      if (!sawHeartbeat || !sawConnection)
      {
        heartbeat.Dispose();
        throw new InvalidOperationException(
          $"ChromecastClient has no {(sawHeartbeat ? nameof(ConnectionChannel) : nameof(HeartbeatChannel))}");
      }

      // Published as a NEW list, never by mutating the existing one: the receive loop enumerates
      // Channels on its own thread, and a reference swap is the only change it cannot observe
      // half-done.
      channelsProperty.SetValue(client, replaced);

      // Only now, once the swap has happened, are the old heartbeat channels retired. Their
      // timers may still be armed — a reused client need not have been through DisconnectAsync
      // first — and left running one would ping on the new connection and, after a missed PONG,
      // raise the StatusChanged that makes the client disconnect itself. Dispose stops it.
      // Done after validation so a failed TryHarden leaves the client untouched.
      foreach (var oldHeartbeat in retired)
      {
        oldHeartbeat.StopTimeoutTimer();
        oldHeartbeat.Dispose();
      }

      return true;
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex,
        "Cast: could not guard SharpCaster's background callbacks — a Cast speaker dropping " +
        "mid-stream can terminate the process on this connection (AUD-84)");
      return false;
    }
  }

  /// <summary>
  /// Installs the timer guard on a heartbeat channel. Throws when the private timer field is not
  /// there, so a SharpCaster change fails loudly rather than leaving the guard silently absent.
  /// </summary>
  internal static void GuardTimer(HeartbeatChannel channel, CastFaultContext context)
  {
    var field = typeof(HeartbeatChannel).GetField("_timer", PrivateInstance)
      ?? throw new MissingFieldException(nameof(HeartbeatChannel), "_timer");
    var timer = field.GetValue(channel) as System.Timers.Timer
      ?? throw new InvalidOperationException("HeartbeatChannel._timer is not a System.Timers.Timer");
    timer.SynchronizingObject = new GuardedTimerInvoker(context);
  }
}

/// <summary>
/// A <see cref="SynchronizationContext"/> that runs everything posted to it on the thread pool and
/// hands any exception that escapes to a fault sink instead of letting it end the process.
/// </summary>
/// <remarks>
/// Its <see cref="Post"/> has to execute ordinary continuations too, not only re-throws: an
/// <c>await</c> without <c>ConfigureAwait(false)</c> inside a callback would resume through it.
/// SharpCaster 3.0.0 uses <c>ConfigureAwait(false)</c> throughout, so in practice only the
/// <c>async void</c> builder's re-throw arrives here — but a context that dropped work would turn a
/// future library change into a hang.
/// </remarks>
internal sealed class CastFaultContext : SynchronizationContext
{
  private readonly Action<Exception> _onFault;

  public CastFaultContext(Action<Exception> onFault)
  {
    _onFault = onFault;
  }

  /// <inheritdoc />
  public override void Post(SendOrPostCallback d, object? state)
  {
    ThreadPool.UnsafeQueueUserWorkItem(_ => Execute(d, state), null);
  }

  /// <inheritdoc />
  public override void Send(SendOrPostCallback d, object? state)
  {
    Execute(d, state);
  }

  /// <inheritdoc />
  public override SynchronizationContext CreateCopy() => this;

  /// <summary>
  /// Runs <paramref name="action"/> with this context installed, so any <c>async void</c> method it
  /// invokes captures this context. Restores the previous context afterwards.
  /// </summary>
  public void Run(Action action)
  {
    var previous = Current;
    SetSynchronizationContext(this);
    try
    {
      action();
    }
    catch (Exception ex)
    {
      // A synchronous throw — an async void method never produces one, but a future non-async
      // override could, and this is the timer's thread, where an escape would be swallowed by
      // System.Timers.Timer and lost rather than reported.
      Report(ex);
    }
    finally
    {
      SetSynchronizationContext(previous);
    }
  }

  private void Execute(SendOrPostCallback d, object? state)
  {
    var previous = Current;
    SetSynchronizationContext(this);
    try
    {
      d(state);
    }
    catch (Exception ex)
    {
      Report(ex);
    }
    finally
    {
      SetSynchronizationContext(previous);
    }
  }

  private void Report(Exception ex)
  {
    try
    {
      _onFault(ex);
    }
    catch
    {
      // The sink is documented as must-not-throw. If it does anyway, swallowing here is the whole
      // point of this class: the alternative is the process exit it exists to prevent.
    }
  }
}

/// <summary>
/// The <see cref="ISynchronizeInvoke"/> handed to SharpCaster's heartbeat timer. The timer calls
/// <see cref="BeginInvoke"/> instead of the handler directly; this runs the handler immediately, on
/// the timer's own thread, with a <see cref="CastFaultContext"/> installed — and then re-arms the
/// timer if the handler left it stopped.
/// </summary>
/// <remarks>
/// <b>Why the re-arm (AUD-84, pre-merge review M2).</b> SharpCaster's timer is one-shot
/// (<c>AutoReset = false</c>). Its handler sends a PING and sets <c>_triedToPing</c>, but never
/// restarts the timer; only an inbound message does. So against a speaker that has simply gone
/// silent, the "PING already sent, still nothing" branch — the library's only heartbeat timeout,
/// which raises the <c>StatusChanged</c> its client disconnects on — can never run. Re-arming
/// after each elapse restores what the library evidently intended: PING after 10 s of silence,
/// declare the connection dead 10 s after that. Any inbound message still restarts the timer and
/// clears <c>_triedToPing</c> exactly as before, so a live speaker sees no change.
/// </remarks>
internal sealed class GuardedTimerInvoker : ISynchronizeInvoke
{
  private readonly CastFaultContext _context;

  public GuardedTimerInvoker(CastFaultContext context)
  {
    _context = context;
  }

  /// <summary>Always true, so <see cref="System.Timers.Timer"/> routes every elapse through <see cref="BeginInvoke"/>.</summary>
  public bool InvokeRequired => true;

  /// <inheritdoc />
  public IAsyncResult BeginInvoke(Delegate method, object?[]? args)
  {
    object? result = null;
    _context.Run(() => result = method.DynamicInvoke(args));
    Rearm(args);
    return new CompletedResult(result);
  }

  private static void Rearm(object?[]? args)
  {
    // The timer passes itself as the sender. Enabled is false here unless the handler (or an
    // inbound message racing it) already restarted it.
    if (args is { Length: > 0 } && args[0] is System.Timers.Timer timer && !timer.Enabled)
    {
      try
      {
        timer.Start();
      }
      catch (ObjectDisposedException)
      {
        // The channel was retired or its client disconnected; nothing left to keep alive.
      }
    }
  }

  /// <inheritdoc />
  public object? EndInvoke(IAsyncResult result) => (result as CompletedResult)?.Result;

  /// <inheritdoc />
  public object? Invoke(Delegate method, object?[]? args)
  {
    object? result = null;
    _context.Run(() => result = method.DynamicInvoke(args));
    return result;
  }

  private sealed class CompletedResult : IAsyncResult
  {
    private static readonly ManualResetEvent Signalled = new(true);

    public CompletedResult(object? result)
    {
      Result = result;
    }

    public object? Result { get; }

    public object? AsyncState => null;

    public WaitHandle AsyncWaitHandle => Signalled;

    public bool CompletedSynchronously => true;

    public bool IsCompleted => true;
  }
}

/// <summary>
/// SharpCaster's heartbeat channel with its two <c>async void</c> entry points guarded: the timer
/// (via <see cref="SharpCasterCallbackGuard.GuardTimer"/>) and the PONG reply in
/// <see cref="OnMessageReceived"/>. The ping/pong/timeout logic itself is the library's, unchanged.
/// </summary>
internal sealed class GuardedHeartbeatChannel : HeartbeatChannel
{
  private readonly CastFaultContext _context;

  public GuardedHeartbeatChannel(CastFaultContext context)
    : base(null)
  {
    _context = context;
    SharpCasterCallbackGuard.GuardTimer(this, context);
  }

  /// <inheritdoc />
  public override void OnMessageReceived(string messagePayload, string type)
  {
    _context.Run(() => base.OnMessageReceived(messagePayload, type));
  }
}

/// <summary>
/// SharpCaster's connection channel with its <c>async void</c> CLOSE handler guarded. On CLOSE the
/// library calls <c>ChromecastClient.DisconnectAsync</c> from inside that handler.
/// </summary>
internal sealed class GuardedConnectionChannel : ConnectionChannel
{
  private readonly CastFaultContext _context;

  public GuardedConnectionChannel(CastFaultContext context)
    : base(null)
  {
    _context = context;
  }

  /// <inheritdoc />
  public override void OnMessageReceived(string messagePayload, string type)
  {
    _context.Run(() => base.OnMessageReceived(messagePayload, type));
  }
}
