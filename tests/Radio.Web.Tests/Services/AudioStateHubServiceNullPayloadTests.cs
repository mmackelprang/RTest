using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The hub client's null-payload contract at the SignalR boundary (queue row `UI-12`).
/// </summary>
/// <remarks>
/// ⚠⚠ THESE DRIVE THE On&lt;T&gt; HANDLER BODY, NOT THE EVENT, AND THE DIFFERENCE IS THE WHOLE TEST.
/// `HubEventFire` reflects the compiler-generated backing field and invokes subscribers directly
/// (HubEventFire.cs:54-61), which is DOWNSTREAM of the guard — a test written that way passes
/// whether the guard is present, absent or inverted. That is `UI-7` `C-213`'s failure mode wearing a
/// new disguise. The internal On…MessageAsync seam (UI-12 Task 1) exists so these assertions can run
/// at all; before it, no test in this assembly could reach the guard (AudioStateHubServiceTests.cs
/// :148-158 records exactly that).
///
/// ⚠ NO ASSERTION HERE USES A WALL CLOCK — `CLAUDE.md` § *Test Timing*. Every handler is awaited to
/// completion before anything is asserted, so each observation is a fact about control flow.
///
/// 📌 What these do NOT prove: that a null can ever arrive. It cannot — the sole server-side sender
/// is provably non-null (UI-12 §0.2). These pin the BOUNDARY's behaviour if one ever does.
/// </remarks>
public class AudioStateHubServiceNullPayloadTests
{
  private static AudioStateHubService NewHub(List<(LogLevel Level, string Message)> sink) =>
    new(
      new CapturingLogger<AudioStateHubService>(sink),
      new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport());

  private static RadioStateDto RadioState() =>
    new(98.5, "FM", 0.2, 40, false, null, 0.5, 20, false, "Rock", 75);

  // --- The headline: a null payload never reaches a subscriber ---------------------------------

  /// <summary>
  /// ⭐ THE DISCRIMINATING TEST. Before `UI-12` this subscriber received null and
  /// RadioControlPanel.razor:1053 (`if (dto.RdsRelevantChanged)`) threw a NullReferenceException
  /// that NotifyAsync swallowed. Revert Task 1's guard and `received` becomes true — RED.
  /// </summary>
  [Fact]
  public async Task NullRadioStatePayloadIsNotDispatchedToSubscribers()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var received = false;

    hub.RadioStateChanged += _ => { received = true; return Task.CompletedTask; };

    await hub.OnRadioStateMessageAsync(null);

    Assert.False(received, "a null payload must not reach a subscriber typed Func<RadioStateDto, Task>");
  }

  /// <summary>
  /// ⭐ The half that answers the row's actual objection. The row refused a guard because it would
  /// drop a broadcast SILENTLY. Delete the LogWarning in AcceptPayload and this fails while the test
  /// above still passes.
  /// </summary>
  [Fact]
  public async Task NullRadioStatePayloadIsLoggedAsAWarningNamingTheEvent()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);

    await hub.OnRadioStateMessageAsync(null);

    Assert.Contains(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("RadioStateChanged"));
  }

  /// <summary>A real payload is still delivered, unchanged. The guard must not cost the happy path.</summary>
  /// <remarks>
  /// ⚠ This is the regression half. Task 1 rewrites the ONLY production path that dispatches
  /// RadioStateChanged; if the rewrite dropped the NotifyAsync call the two tests above would both
  /// still pass. Invert Task 1c's condition and this fails.
  /// </remarks>
  [Fact]
  public async Task NonNullRadioStatePayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = RadioState();
    RadioStateDto? seen = null;

    hub.RadioStateChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnRadioStateMessageAsync(dto);

    Assert.Same(dto, seen);
    // Narrowed to the rejection message rather than "no Warning at all": the broad form would fail
    // this test for the wrong reason the day an unrelated, legitimate warning is added to the hub
    // client, and the property under test is only that the guard did not fire.
    Assert.DoesNotContain(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("null payload"));
  }

  /// <summary>Every subscriber is still awaited — Task 1 must not have bypassed the UI-7 fan-out.</summary>
  [Fact]
  public async Task NonNullRadioStatePayloadStillReachesEverySubscriber()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var order = new List<int>();

    hub.RadioStateChanged += _ => { order.Add(1); return Task.CompletedTask; };
    hub.RadioStateChanged += _ => { order.Add(2); return Task.CompletedTask; };
    hub.RadioStateChanged += _ => { order.Add(3); return Task.CompletedTask; };

    await hub.OnRadioStateMessageAsync(RadioState());

    Assert.Equal([1, 2, 3], order);
  }

  // --- The three Encoder events keep the behaviour they already had ----------------------------

  /// <summary>
  /// The Encoder guards are PRESERVED, not deleted. These three were already true before `UI-12`
  /// (the inline `if (dto != null)` at the old :248/:262/:276) and must stay true after it — this
  /// row changed where the check lives and made it audible, not whether it happens.
  /// </summary>
  [Fact]
  public async Task NullEncoderConnectionPayloadIsNotDispatched()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var received = false;

    hub.EncoderConnectionChanged += _ => { received = true; return Task.CompletedTask; };

    await hub.OnEncoderConnectionMessageAsync(null);

    Assert.False(received);
    Assert.Contains(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("EncoderConnectionChanged"));
  }

  [Fact]
  public async Task NullEncoderConfigStatusPayloadIsNotDispatched()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var received = false;

    hub.EncoderConfigStatusChanged += _ => { received = true; return Task.CompletedTask; };

    await hub.OnEncoderConfigStatusMessageAsync(null);

    Assert.False(received);
    Assert.Contains(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("EncoderConfigStatusChanged"));
  }

  [Fact]
  public async Task NullEncoderHudPayloadIsNotDispatched()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var received = false;

    hub.EncoderHudChanged += _ => { received = true; return Task.CompletedTask; };

    await hub.OnEncoderHudMessageAsync(null);

    Assert.False(received);
    Assert.Contains(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("EncoderHudChanged"));
  }

  [Fact]
  public async Task NonNullEncoderHudPayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = new EncoderHudDto { EncoderIndex = 1, Label = "Volume", Phase = "Value" };
    EncoderHudDto? seen = null;

    hub.EncoderHudChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnEncoderHudMessageAsync(dto);

    Assert.Same(dto, seen);
  }
}
