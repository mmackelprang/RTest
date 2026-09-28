using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Platform.Display;

namespace Radio.Infrastructure.Tests.Platform.Display;

/// <summary>
/// Pins <see cref="PanelPowerService"/> (<c>ENC-22</c>): the timer, the knob wake, and above all the
/// safety rules — the knobs are the only wake source for a dark panel, so every rule here is one that
/// keeps a sealed cabinet from going dark with no way back.
///
/// <para>
/// Every timing assertion runs on a <see cref="FakeTimeProvider"/>, and every command assertion
/// rendezvouses with the service's pump through <see cref="PanelPowerService.PumpIdle"/> rather than
/// sleeping (CLAUDE.md, "Test Timing").
/// </para>
/// </summary>
public class PanelPowerServiceTests
{
  // --- Fakes -------------------------------------------------------------------------------

  private sealed class FakeControl : IPanelPowerControl
  {
    public List<bool> Commands { get; } = [];

    /// <summary>Results to return, in order; once empty, every command succeeds.</summary>
    public Queue<bool> Results { get; } = new();

    public Task<bool> SetPanelPowerAsync(bool on, CancellationToken cancellationToken = default)
    {
      lock (Commands)
      {
        Commands.Add(on);
      }

      return Task.FromResult(Results.Count == 0 || Results.Dequeue());
    }

    public bool? Last
    {
      get
      {
        lock (Commands)
        {
          return Commands.Count == 0 ? null : Commands[^1];
        }
      }
    }
  }

  private sealed class FakeSleep : ISleepService
  {
    public bool IsSleeping { get; set; }
    public bool IsSleepScreenVisible { get; private set; }
    public ConsoleWakeState WakeState => ConsoleWakeState.Awake;
    public event EventHandler<bool>? SleepScreenVisibilityChanged;

    public void Show(bool visible)
    {
      bool changed = IsSleepScreenVisible != visible;
      IsSleepScreenVisible = visible;
      if (changed)
      {
        SleepScreenVisibilityChanged?.Invoke(this, visible);
      }
    }

    public Task EnterSleepAsync() => Task.CompletedTask;
    public Task WakeAsync(string wakeSource = "unknown") => Task.CompletedTask;
    public Task SetSleepScreenVisibleAsync(bool visible) { Show(visible); return Task.CompletedTask; }
    public bool TryClaimWake() => false;
  }

  private sealed class FakeEncoder : IRotaryEncoderService
  {
    public bool IsConnected { get; private set; } = true;
    public RotaryEncoderConfigStatus ConfigStatus => RotaryEncoderConfigStatus.Configured;
#pragma warning disable CS0067 // Only ConnectionChanged is raised; the panel does not read the others.
    public event EventHandler<EncoderTurnedEventArgs>? EncoderTurned;
    public event EventHandler<EncoderButtonEventArgs>? ButtonPressed;
    public event EventHandler<EncoderConfigStatusEventArgs>? ConfigStatusChanged;
#pragma warning restore CS0067
    public event EventHandler<EncoderConnectionEventArgs>? ConnectionChanged;

    public void SetConnected(bool connected)
    {
      IsConnected = connected;
      ConnectionChanged?.Invoke(this, new EncoderConnectionEventArgs { IsConnected = connected, WasEverConnected = true });
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Dispose() { }
  }

  private sealed class MutableOptions : IOptionsMonitor<PanelPowerOptions>
  {
    private readonly List<Action<PanelPowerOptions, string?>> _listeners = [];
    public PanelPowerOptions CurrentValue { get; private set; }
    public MutableOptions(PanelPowerOptions value) { CurrentValue = value; }
    public PanelPowerOptions Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<PanelPowerOptions, string?> listener)
    {
      _listeners.Add(listener);
      return new Unsubscribe(() => _listeners.Remove(listener));
    }

    public void Set(PanelPowerOptions value)
    {
      CurrentValue = value;
      foreach (var l in _listeners.ToList())
      {
        l(value, null);
      }
    }

    private sealed class Unsubscribe(Action a) : IDisposable { public void Dispose() => a(); }
  }

  private sealed class Harness : IDisposable
  {
    public readonly FakeControl Control = new();
    public readonly FakeSleep Sleep = new();
    public readonly FakeEncoder Encoder = new();
    public readonly FakeTimeProvider Time = new();
    public readonly MutableOptions Options;
    public readonly PanelPowerService Service;

    public Harness(double minutes = 10, double stableSeconds = 30, double graceMs = 2000, bool withEncoder = true)
    {
      Options = new MutableOptions(new PanelPowerOptions
      {
        PanelOffAfterMinutes = minutes,
        EncoderStableSeconds = stableSeconds,
        WakeGraceMilliseconds = graceMs,
      });
      Service = new PanelPowerService(
        NullLogger<PanelPowerService>.Instance, Options, Sleep, Control, withEncoder ? Encoder : null, Time);
    }

    /// <summary>Starts the service and lets the start-up power-on land.</summary>
    public async Task StartAsync()
    {
      await Service.StartAsync(CancellationToken.None);
      await Service.PumpIdle;
    }

    /// <summary>Advances the clock and waits for whatever command that caused to be applied.</summary>
    public async Task AdvanceAsync(TimeSpan by)
    {
      Time.Advance(by);
      await Service.PumpIdle;
    }

    /// <summary>Puts the sleep screen up, then runs the full period out, so the panel is dark.</summary>
    public async Task GoDarkAsync()
    {
      Sleep.Show(true);
      await AdvanceAsync(TimeSpan.FromMinutes(Options.CurrentValue.PanelOffAfterMinutes));
      Assert.False(Control.Last);
      Assert.True(Service.IsPanelOff);
    }

    public void Dispose() => Service.Dispose();
  }

  private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

  // --- Start-up and shutdown -----------------------------------------------------------------

  [Fact]
  public async Task Start_PowersThePanelOn_EvenWithTheFeatureOff()
  {
    // Safety rule 3. A crash while dark must not survive the restart, and "the feature is off now"
    // says nothing about what the last process left the panel in.
    using var h = new Harness(minutes: 0);

    await h.StartAsync();

    Assert.Equal([true], h.Control.Commands);
  }

  [Fact]
  public async Task Stop_WhileDark_PowersThePanelOn()
  {
    using var h = new Harness();
    await h.StartAsync();
    await h.GoDarkAsync();

    await h.Service.StopAsync(CancellationToken.None);

    Assert.True(h.Control.Last);
  }

  // --- The timer ------------------------------------------------------------------------------

  [Fact]
  public async Task FeatureOff_NeverPowersOff()
  {
    using var h = new Harness(minutes: 0);
    await h.StartAsync();

    h.Sleep.Show(true);
    await h.AdvanceAsync(TimeSpan.FromDays(1));

    Assert.Equal([true], h.Control.Commands);
    Assert.False(h.Service.IsPanelOff);
  }

  [Fact]
  public async Task SleepScreen_PowersOffAfterThePeriod_AndNotBefore()
  {
    using var h = new Harness(minutes: 10);
    await h.StartAsync();

    h.Sleep.Show(true);
    await h.AdvanceAsync(TimeSpan.FromMinutes(10) - Tick);
    Assert.Equal([true], h.Control.Commands);

    await h.AdvanceAsync(Tick);
    Assert.Equal([true, false], h.Control.Commands);
    Assert.True(h.Service.IsPanelOff);
  }

  [Fact]
  public async Task TheIdlePath_WhereIsSleepingStaysFalse_StillPowersOff()
  {
    // Gotcha #9. idle-dimmer.js reaches /sleep without SetSleepAsync(true), so IsSleeping is false on
    // the path the owner actually meant. The timer is keyed on the screen.
    using var h = new Harness(minutes: 10);
    await h.StartAsync();
    h.Sleep.IsSleeping = false;

    h.Sleep.Show(true);
    await h.AdvanceAsync(TimeSpan.FromMinutes(10));

    Assert.False(h.Control.Last);
  }

  [Fact]
  public async Task Standby_WithNoSleepScreenReported_DoesNotPowerOff()
  {
    // The other half of keying on the screen: IsSleeping alone arms nothing. A browserless standby has
    // no page on screen to dark-out, and the timer must not be driven by the audio flag.
    using var h = new Harness(minutes: 10);
    await h.StartAsync();

    h.Sleep.IsSleeping = true;
    await h.AdvanceAsync(TimeSpan.FromMinutes(30));

    Assert.Equal([true], h.Control.Commands);
  }

  [Fact]
  public async Task LeavingTheSleepScreenBeforeThePeriod_DisarmsTheTimer()
  {
    using var h = new Harness(minutes: 10);
    await h.StartAsync();

    h.Sleep.Show(true);
    await h.AdvanceAsync(TimeSpan.FromMinutes(5));
    h.Sleep.Show(false);
    await h.AdvanceAsync(TimeSpan.FromMinutes(30));

    Assert.Equal([true], h.Control.Commands);
  }

  [Fact]
  public async Task EnablingWhileOnTheSleepScreen_ArmsWithoutARestart()
  {
    // The enable step in OWNER-REVIEW is an edit to appsettings.Production.json, read through
    // IOptionsMonitor; it must take effect on a console already sitting on /sleep.
    using var h = new Harness(minutes: 0);
    await h.StartAsync();
    h.Sleep.Show(true);

    h.Options.Set(new PanelPowerOptions { PanelOffAfterMinutes = 1 });
    await h.AdvanceAsync(TimeSpan.FromMinutes(1));

    Assert.False(h.Control.Last);
  }

  // --- The knob wake ---------------------------------------------------------------------------

  [Fact]
  public async Task AKnobOnADarkPanel_IsConsumedAndPowersItOn()
  {
    using var h = new Harness();
    await h.StartAsync();
    await h.GoDarkAsync();

    bool consumed = h.Service.OnEncoderInput("encoder-turn");
    await h.Service.PumpIdle;

    Assert.True(consumed);
    Assert.True(h.Control.Last);
    Assert.False(h.Service.IsPanelOff);
  }

  [Fact]
  public async Task InputDuringTheWakeGrace_IsConsumed_AndAfterItIsNot()
  {
    // The panel shows its firmware splash for ~2 s on power-up. A fast spin that lit it must not go on
    // to move the volume while nothing is visible.
    using var h = new Harness(graceMs: 2000);
    await h.StartAsync();
    await h.GoDarkAsync();

    Assert.True(h.Service.OnEncoderInput("encoder-turn"));
    await h.AdvanceAsync(TimeSpan.FromMilliseconds(1999));
    Assert.True(h.Service.OnEncoderInput("encoder-turn"));
    await h.AdvanceAsync(TimeSpan.FromMilliseconds(1));
    Assert.False(h.Service.OnEncoderInput("encoder-turn"));
  }

  [Fact]
  public async Task AKnobOnALitPanel_IsNotConsumed()
  {
    using var h = new Harness();
    await h.StartAsync();
    h.Sleep.Show(true);

    Assert.False(h.Service.OnEncoderInput("encoder-turn"));
    Assert.Equal([true], h.Control.Commands);
  }

  [Fact]
  public async Task AfterAKnobWake_ThePanelGoesDarkAgainAfterAnotherFullPeriod()
  {
    using var h = new Harness(minutes: 10);
    await h.StartAsync();
    await h.GoDarkAsync();

    h.Service.OnEncoderInput("encoder-turn");
    await h.Service.PumpIdle;
    await h.AdvanceAsync(TimeSpan.FromMinutes(10) - Tick);
    Assert.True(h.Control.Last);

    await h.AdvanceAsync(Tick);
    Assert.False(h.Control.Last);
  }

  [Fact]
  public async Task KnobActivityOnTheLitSleepScreen_RestartsTheCountdown()
  {
    using var h = new Harness(minutes: 10);
    await h.StartAsync();
    h.Sleep.Show(true);

    await h.AdvanceAsync(TimeSpan.FromMinutes(9));
    h.Service.OnEncoderInput("encoder-turn");
    await h.AdvanceAsync(TimeSpan.FromMinutes(9));

    Assert.Equal([true], h.Control.Commands);
  }

  [Fact]
  public async Task TheSleepScreenClosingWhileDark_PowersThePanelOn()
  {
    // A REST wake, an incoming call, any navigation off /sleep: the browser reports the screen hidden,
    // and a page the owner is meant to see must not be rendered into a dark panel.
    using var h = new Harness();
    await h.StartAsync();
    await h.GoDarkAsync();

    h.Sleep.Show(false);
    await h.Service.PumpIdle;

    Assert.True(h.Control.Last);
  }

  // --- The safety rules --------------------------------------------------------------------------

  [Fact]
  public async Task NoEncoder_NeverPowersOff()
  {
    using var h = new Harness(withEncoder: false);
    await h.StartAsync();

    h.Sleep.Show(true);
    await h.AdvanceAsync(TimeSpan.FromHours(2));

    Assert.Equal([true], h.Control.Commands);
  }

  [Fact]
  public async Task EncoderAbsentWhenTheTimerFires_DoesNotPowerOff()
  {
    // Safety rule 1.
    using var h = new Harness(minutes: 10);
    await h.StartAsync();
    h.Sleep.Show(true);

    h.Encoder.SetConnected(false);
    await h.AdvanceAsync(TimeSpan.FromHours(1));

    Assert.Equal([true], h.Control.Commands);
  }

  [Fact]
  public async Task EncoderBack_PowersOffOnlyAfterItHasStayedForTheStablePeriod()
  {
    using var h = new Harness(minutes: 10, stableSeconds: 30);
    await h.StartAsync();
    h.Sleep.Show(true);
    h.Encoder.SetConnected(false);
    await h.AdvanceAsync(TimeSpan.FromMinutes(20));

    h.Encoder.SetConnected(true);
    await h.AdvanceAsync(TimeSpan.FromSeconds(29));
    Assert.Equal([true], h.Control.Commands);

    await h.AdvanceAsync(Tick);
    Assert.False(h.Control.Last);
  }

  [Fact]
  public async Task AReconnectJustBeforeTheTimer_DefersThePowerOffToTheStablePeriod()
  {
    using var h = new Harness(minutes: 10, stableSeconds: 30);
    await h.StartAsync();
    h.Sleep.Show(true);

    await h.AdvanceAsync(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(10));
    h.Encoder.SetConnected(false);
    h.Encoder.SetConnected(true);
    await h.AdvanceAsync(TimeSpan.FromSeconds(10));
    Assert.Equal([true], h.Control.Commands);

    await h.AdvanceAsync(TimeSpan.FromSeconds(20));
    Assert.False(h.Control.Last);
  }

  [Fact]
  public async Task EncoderLostWhileDark_PowersThePanelOnAtOnce()
  {
    // Safety rule 2. The event, not a poll: no clock advance between the loss and the assertion.
    using var h = new Harness();
    await h.StartAsync();
    await h.GoDarkAsync();

    h.Encoder.SetConnected(false);
    await h.Service.PumpIdle;

    Assert.True(h.Control.Last);
    Assert.False(h.Service.IsPanelOff);
  }

  [Fact]
  public async Task APowerOnTheCompositorDidNotConfirm_IsRetried()
  {
    using var h = new Harness();
    await h.StartAsync();
    await h.GoDarkAsync();

    h.Control.Results.Enqueue(false);
    h.Encoder.SetConnected(false);
    await h.Service.PumpIdle;
    Assert.True(h.Service.IsPanelOff);

    await h.AdvanceAsync(TimeSpan.FromSeconds(5));

    Assert.Equal([true, false, true, true], h.Control.Commands);
    Assert.False(h.Service.IsPanelOff);
  }

  [Fact]
  public async Task AnUnconfirmedPowerOn_StillLetsAKnobRetryIt()
  {
    // Without this, a failed power-on would leave the panel dark with the service reporting it lit,
    // and the knob would dispatch into a black screen.
    using var h = new Harness();
    await h.StartAsync();
    await h.GoDarkAsync();

    h.Control.Results.Enqueue(false);
    Assert.True(h.Service.OnEncoderInput("encoder-turn"));
    await h.Service.PumpIdle;
    await h.AdvanceAsync(TimeSpan.FromMilliseconds(2000));

    Assert.True(h.Service.OnEncoderInput("encoder-turn"));
    await h.Service.PumpIdle;

    Assert.True(h.Control.Last);
    Assert.False(h.Service.IsPanelOff);
  }

  [Fact]
  public async Task AnUnconfirmedPowerOff_IsFollowedByAPowerOn()
  {
    // Pre-merge review H1. gdbus reports failure on a reply timeout, not a request failure, so a slow
    // compositor can take the panel dark AND report the off as failed. Treating that as "still lit"
    // left a possibly-dark panel with every knob dispatching and nothing ever sending "on".
    using var h = new Harness(minutes: 10);
    await h.StartAsync();
    h.Sleep.Show(true);

    h.Control.Results.Enqueue(false);
    await h.AdvanceAsync(TimeSpan.FromMinutes(10));

    Assert.Equal([true, false, true], h.Control.Commands);
    Assert.False(h.Service.IsPanelOff);
    Assert.False(h.Service.OnEncoderInput("encoder-turn"));
  }

  [Fact]
  public async Task AnUnconfirmedPowerOff_FollowedByAnUnconfirmedPowerOn_KeepsTheKnobAWakeSource()
  {
    // Both halves unconfirmed: the panel may be dark, so a knob must still be consumed and must kick
    // another power-on rather than dispatching into what may be a black screen.
    using var h = new Harness(minutes: 10);
    await h.StartAsync();
    h.Sleep.Show(true);

    h.Control.Results.Enqueue(false);
    h.Control.Results.Enqueue(false);
    await h.AdvanceAsync(TimeSpan.FromMinutes(10));
    Assert.True(h.Service.IsPanelOff);

    Assert.True(h.Service.OnEncoderInput("encoder-turn"));
    await h.Service.PumpIdle;

    Assert.Equal([true, false, true, true], h.Control.Commands);
    Assert.False(h.Service.IsPanelOff);
  }
}
