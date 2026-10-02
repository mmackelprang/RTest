using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.External;
using Radio.Infrastructure.External;

namespace Radio.API.Tests.Services;

/// <summary>
/// PHN-12: pins <see cref="PhoneCallClient"/> against RotaryPhone's hub contract over a REAL SignalR
/// connection — real serialization, real handler binding — to an in-process server that sends exactly
/// what RotaryPhone sends.
/// </summary>
/// <remarks>
/// <para>
/// <b>The declared contract</b>, from <c>D:\prj\RotaryPhone\src\RotaryPhoneController.Server\Services\
/// SignalRNotifierService.cs</c> <c>OnStateChanged</c> (read 2026-10-02):
/// </para>
/// <code>
/// _hubContext.Clients.All.SendAsync("CallStateChanged", phoneId, manager.CurrentState.ToString());
/// if (manager.CurrentState == CallState.Ringing &amp;&amp; manager.IncomingPhoneNumber != null)
///     _hubContext.Clients.All.SendAsync("IncomingCall", phoneId, manager.IncomingPhoneNumber);
/// </code>
/// <para>
/// with <c>CallState</c> = <c>Idle, Dialing, Ringing, InCall</c> (<c>RotaryPhoneController.Core/CallState.cs</c>).
/// <see cref="RotaryPhoneSends"/> below reproduces those two sends and nothing else. Until PHN-12 the
/// client bound the first as <c>(state, number)</c>; against this server it raised <c>Idle</c> for a ring
/// (the phone id parsed as a state), which is what the box's log showed, and never raised a number.
/// </para>
/// <para>
/// Waits are on the events themselves (a <see cref="TaskCompletionSource{T}"/> per expected event); the
/// timeout only bounds a failure, it never decides a pass.
/// </para>
/// </remarks>
public sealed class PhoneCallClientContractTests : IAsyncLifetime
{
  private const string PhoneId = "default";
  private const string Number = "5550137424";
  private static readonly TimeSpan FailAfter = TimeSpan.FromSeconds(15);

  private WebApplication _app = null!;
  private PhoneCallClient _client = null!;
  private readonly List<PhoneCallStateChangedEventArgs> _raised = [];
  private readonly object _gate = new();
  private TaskCompletionSource<PhoneCallStateChangedEventArgs> _next = NewWaiter();

  public async Task InitializeAsync()
  {
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseTestServer();
    builder.Services.AddSignalR();
    _app = builder.Build();
    _app.MapHub<RotaryHubStandIn>("/hub");
    await _app.StartAsync();

    var server = _app.GetTestServer();
    var options = new Mock<IOptionsMonitor<PhoneIntegrationOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new PhoneIntegrationOptions { HubUrl = "http://localhost/hub" });
    _client = new PhoneCallClient(NullLogger<PhoneCallClient>.Instance, options.Object, o =>
    {
      o.HttpMessageHandlerFactory = _ => server.CreateHandler();
      o.Transports = HttpTransportType.LongPolling;
    });
    _client.CallStateChanged += (_, e) =>
    {
      lock (_gate)
      {
        _raised.Add(e);
        _next.TrySetResult(e);
      }
    };

    await _client.StartAsync();
    Assert.True(_client.IsConnected, "the client did not connect to the in-process hub");
  }

  public async Task DisposeAsync()
  {
    await _client.DisposeAsync();
    await _app.DisposeAsync();
  }

  private static TaskCompletionSource<PhoneCallStateChangedEventArgs> NewWaiter() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

  private Task<PhoneCallStateChangedEventArgs> ArmWaiter()
  {
    lock (_gate)
    {
      _next = NewWaiter();
      return _next.Task;
    }
  }

  /// <summary>RotaryPhone's <c>SignalRNotifierService.OnStateChanged</c>, verbatim in shape.</summary>
  private async Task RotaryPhoneSends(string state, string? incomingNumber)
  {
    var hub = _app.Services.GetRequiredService<IHubContext<RotaryHubStandIn>>();
    await hub.Clients.All.SendAsync("CallStateChanged", PhoneId, state);
    if (state == "Ringing" && incomingNumber != null)
    {
      await hub.Clients.All.SendAsync("IncomingCall", PhoneId, incomingNumber);
    }
  }

  [Fact]
  public async Task ARing_ArrivesAsRinging_WithTheCallersNumber()
  {
    var ring = ArmWaiter();

    await RotaryPhoneSends("Ringing", Number);
    var e = await ring.WaitAsync(FailAfter);

    Assert.Equal(PhoneCallState.Ringing, e.State);
    Assert.Equal(Number, e.PhoneNumber);
    lock (_gate)
    {
      // Exactly one event for the ring: the bare CallStateChanged raised nothing of its own.
      Assert.Single(_raised);
    }
  }

  [Fact]
  public async Task PickingUp_ArrivesAsInCall()
  {
    var ring = ArmWaiter();
    await RotaryPhoneSends("Ringing", Number);
    await ring.WaitAsync(FailAfter);

    var next = ArmWaiter();
    await RotaryPhoneSends("InCall", null);

    Assert.Equal(PhoneCallState.InCall, (await next.WaitAsync(FailAfter)).State);
  }

  [Fact]
  public async Task TheCallerHangingUp_ArrivesAsEnded()
  {
    var ring = ArmWaiter();
    await RotaryPhoneSends("Ringing", Number);
    await ring.WaitAsync(FailAfter);

    var next = ArmWaiter();
    await RotaryPhoneSends("Idle", null);

    Assert.Equal(PhoneCallState.Ended, (await next.WaitAsync(FailAfter)).State);
  }

  /// <summary>An empty hub: RotaryPhone's sends go out through its <c>IHubContext</c>, as they do here.</summary>
  private sealed class RotaryHubStandIn : Hub
  {
  }
}
