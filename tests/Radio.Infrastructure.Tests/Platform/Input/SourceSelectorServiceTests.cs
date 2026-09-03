using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers the SOURCE knob's list, preview and commit (ENC-5 Tasks 6 and 16).
///
/// <para>
/// The load-bearing assertions here are the D7 ones: a band committed while the radio is already
/// playing is a <b>band change</b> and not a source switch, and the current marker follows the
/// active <b>band</b> rather than the word "Radio". Both are the difference between this feature and
/// a re-implementation of the source cycler it replaces.
/// </para>
///
/// <para>
/// ⚠ <b>How these tests rendezvous with the commit, since it is fire-and-forget.</b>
/// <c>Press()</c> launches <c>CommitAsync</c> as <c>_ = CommitAsync(row)</c> and returns without
/// awaiting it, so an assertion made after <c>Press()</c> is only sound because <b>every fake in
/// <c>EncoderSelectorTestDoubles</c> returns an already-completed task</b>. With no true
/// suspension point, <c>CommitAsync</c> runs to completion synchronously on the calling thread
/// before <c>Press()</c> returns, and the call stack itself is the rendezvous — the assertions are
/// deterministic rather than timed. Per <c>CLAUDE.md</c> § "Test Timing", this is stated rather
/// than assumed: <b>giving any of those fakes a real await (a <c>Task.Delay</c>, a
/// <c>Task.Yield</c>, a <c>Task.Run</c>) silently converts every commit assertion in this file
/// into a race.</b> If a fake ever needs to suspend, add an explicit completion signal and wait on
/// it; do not add a sleep.
/// </para>
/// </summary>
public class SourceSelectorServiceTests
{
  private sealed class Harness : IDisposable
  {
    public readonly CallLog Log = new();
    public readonly SelectorFakeAudioManager Audio;
    public readonly FakeBandMemory BandMemory = new();
    public readonly RecordingSelectorSink Hud = new();
    public readonly FakeTimeProvider Time = new();
    public readonly SourceSelectorService Selector;

    public Harness()
    {
      Audio = new SelectorFakeAudioManager(Log);
      Selector = new SourceSelectorService(
        NullLogger<SourceSelectorService>.Instance,
        () => Audio,
        () => BandMemory,
        Hud,
        Time);
    }

    /// <summary>The rows and highlight as the overlay last saw them.</summary>
    public EncoderHudEventArgs LastPreview =>
      Hud.Published.Last(p => p.Phase == EncoderHudPhase.SelectorPreview);

    public IReadOnlyList<EncoderSelectorRow> Rows => LastPreview.Rows!;

    /// <summary>
    /// Opens the overlay (a press on a closed overlay opens without committing) and walks the
    /// highlight to <paramref name="rowId"/> one detent at a time, which is the only way a user can
    /// get there.
    /// </summary>
    public void HighlightRow(string rowId)
    {
      if (!Selector.IsOpen)
      {
        Selector.Press();
      }

      var preview = LastPreview;
      int target = IndexOf(preview.Rows!, rowId);
      int delta = target - preview.HighlightIndex;

      for (int i = 0; i < Math.Abs(delta); i++)
      {
        Selector.Turn(Math.Sign(delta));
      }
    }

    public void Dispose() => Selector.Dispose();
  }

  private static int IndexOf(IReadOnlyList<EncoderSelectorRow> rows, string id)
  {
    for (int i = 0; i < rows.Count; i++)
    {
      if (rows[i].Id == id)
      {
        return i;
      }
    }

    Assert.Fail($"No row with id '{id}' in [{string.Join(", ", rows.Select(r => r.Id))}]");
    return -1;
  }

  private static FakeRadioSource ActiveRadio(
    Harness h,
    RadioBand band = RadioBand.FM,
    IReadOnlyList<RadioBand>? supported = null)
  {
    var radio = new FakeRadioSource(h.Log)
    {
      CurrentBand = band,
      SupportedBands = supported ?? [RadioBand.FM, RadioBand.AM, RadioBand.SW, RadioBand.WB],
    };
    h.Audio.Cached[AudioSourceType.Radio] = radio;
    h.Audio.ActiveSource = radio;
    return radio;
  }

  // --- 1-2: turning previews -------------------------------------------------------------------

  [Fact]
  public void Turn_OpensTheOverlayAndMovesOneEntry()
  {
    using var h = new Harness();
    ActiveRadio(h);

    h.Selector.Turn(1);

    Assert.True(h.Selector.IsOpen);
    // The highlight seeds on the current row (FM, index 0) and one detent moves one entry.
    Assert.Equal(1, h.LastPreview.HighlightIndex);
  }

  [Fact]
  public void Turn_SwitchesNothing()
  {
    using var h = new Harness();
    ActiveRadio(h);

    for (int i = 0; i < 8; i++)
    {
      h.Selector.Turn(1);
    }

    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  // --- 3-8: composition and the current marker -------------------------------------------------

  [Fact]
  public void Composition_PutsFmFirstAndAmSecond()
  {
    // Not Enum.GetValues order - RadioBand declares AM first - and not recency.
    using var h = new Harness();
    ActiveRadio(h);

    h.Selector.Turn(1);

    Assert.Equal("band:FM", h.Rows[0].Id);
    Assert.Equal("band:AM", h.Rows[1].Id);
  }

  [Fact]
  public void Composition_OmitsSw_WhenTheTunerDoesNotSupportIt()
  {
    using var h = new Harness();
    ActiveRadio(h, supported: [RadioBand.FM, RadioBand.AM]);

    h.Selector.Turn(1);

    Assert.DoesNotContain(h.Rows, r => r.Id == "band:SW");
  }

  [Fact]
  public void Composition_IncludesSwAtPositionThree_WhenSupported()
  {
    using var h = new Harness();
    ActiveRadio(h, supported: [RadioBand.FM, RadioBand.AM, RadioBand.SW]);

    h.Selector.Turn(1);

    Assert.Equal("band:SW", h.Rows[2].Id);
  }

  [Fact]
  public void Composition_IsStableAcrossReopens_EvenWhenAvailabilityChanges()
  {
    // "Positions never move" is only achievable if the set does not change under the user's hand,
    // so a row that has become unavailable is dimmed in place rather than removed.
    using var h = new Harness();
    ActiveRadio(h);
    h.Selector.Turn(1);
    var before = h.Rows.Select(r => r.Id).ToArray();

    h.Audio.ActiveSource = null;
    h.Audio.Cached.Remove(AudioSourceType.Radio);
    h.Selector.Dismiss();
    h.Selector.Turn(1);

    Assert.Equal(before, h.Rows.Select(r => r.Id).ToArray());
    Assert.All(h.Rows.Where(r => r.Id.StartsWith("band:")), r => Assert.False(r.IsAvailable));
  }

  [Fact]
  public void Composition_WithNoTuner_IsNotCached_AndResolvesWhenATunerAppears()
  {
    // The regression this pins, found in pre-merge review. Composition is resolved once — but the
    // no-tuner fallback must NOT be what gets cached. A radio source does not exist until something
    // creates one, so a knob turned on a cold boot (the common case: the overlay is one of the
    // first things a hand reaches for) sees no tuner. Caching FM+AM at that moment froze the list
    // for the life of the process, and an SDR's SW and WB rows could never appear afterwards no
    // matter how long the tuner ran.
    using var h = new Harness();

    h.Selector.Turn(1);
    Assert.Equal(["band:FM", "band:AM"], h.Rows.Where(r => r.Id.StartsWith("band:")).Select(r => r.Id));

    // The tuner arrives, as it does a few seconds into a boot.
    ActiveRadio(h);
    h.Selector.Dismiss();
    h.Selector.Turn(1);

    Assert.Equal(
      ["band:FM", "band:AM", "band:SW", "band:WB"],
      h.Rows.Where(r => r.Id.StartsWith("band:")).Select(r => r.Id));
  }

  [Fact]
  public void Composition_OnceResolvedFromARealTuner_IsStableEvenIfItDisappears()
  {
    // The other half: once a real tuner HAS answered, that answer is the composition for the
    // session. This is what "positions never move" means, and it is why the fix above is about
    // which value gets cached rather than about caching at all.
    using var h = new Harness();
    ActiveRadio(h, supported: [RadioBand.FM, RadioBand.AM, RadioBand.SW]);
    h.Selector.Turn(1);
    var resolved = h.Rows.Where(r => r.Id.StartsWith("band:")).Select(r => r.Id).ToArray();

    h.Audio.ActiveSource = null;
    h.Audio.Cached.Remove(AudioSourceType.Radio);
    h.Selector.Dismiss();
    h.Selector.Turn(1);

    Assert.Equal(resolved, h.Rows.Where(r => r.Id.StartsWith("band:")).Select(r => r.Id));
  }

  [Fact]
  public void Composition_WithNoTuner_RendersFmAndAmDimmedWithAReason()
  {
    // State B: a dimmed row with a reason, not an absent row that gives the user nothing to aim at.
    using var h = new Harness();

    h.Selector.Turn(1);

    var bands = h.Rows.Where(r => r.Id.StartsWith("band:")).ToArray();
    Assert.Equal(new[] { "band:FM", "band:AM" }, bands.Select(r => r.Id).ToArray());
    Assert.All(bands, r =>
    {
      Assert.False(r.IsAvailable);
      Assert.Equal("no tuner detected", r.UnavailableReason);
    });
  }

  [Fact]
  public void CurrentMarker_TracksTheActiveBand_NotTheWordRadio()
  {
    // D7 requirement 3, and the single highest-weighted UAT check: on AM, row 2 is marked.
    using var h = new Harness();
    ActiveRadio(h, band: RadioBand.AM);

    h.Selector.Turn(1);

    Assert.False(h.Rows[0].IsCurrent);
    Assert.True(h.Rows[1].IsCurrent);
    Assert.Equal("band:AM", h.Rows[1].Id);
  }

  // --- 9-15: committing a band -----------------------------------------------------------------

  [Fact]
  public void CommitBand_WhileRadioIsActive_CallsSetBandAndNeverCreatesASource()
  {
    // D7 requirement 1. A band change on a playing radio must not tear down and stand up a source:
    // that is the input pattern the long-running capture-lifecycle bug lives on.
    using var h = new Harness();
    var radio = ActiveRadio(h);

    h.HighlightRow("band:AM");
    h.Selector.Press();

    Assert.Equal(new[] { RadioBand.AM }, radio.BandsSet);
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  [Fact]
  public void CommitBand_WhileRadioIsActive_PublishesNoCommittingPhase()
  {
    using var h = new Harness();
    ActiveRadio(h);

    h.HighlightRow("band:AM");
    h.Selector.Press();

    // No spinner: the path is instant because nothing is being torn down.
    Assert.Empty(h.Hud.OfPhase(EncoderHudPhase.SelectorCommitting));
  }

  [Fact]
  public void CommitBand_RestoresThatBandsLastTunedFrequency()
  {
    using var h = new Harness();
    var radio = ActiveRadio(h);
    h.BandMemory.Remembered[RadioBand.AM] = Frequency.FromKilohertz(1130);

    h.HighlightRow("band:AM");
    h.Selector.Press();

    Assert.Equal(new[] { 1_130_000L }, radio.FrequenciesSet.Select(f => f.Hertz).ToArray());
  }

  [Fact]
  public void CommitBand_FallsBackToTheBandDefault_WhenNothingIsRemembered()
  {
    using var h = new Harness();
    var radio = ActiveRadio(h);
    h.BandMemory.Defaults[RadioBand.AM] = Frequency.FromKilohertz(1000);

    h.HighlightRow("band:AM");
    h.Selector.Press();

    // Today's behaviour clamps the outgoing band's frequency into the incoming band's range, which
    // lands FM -> AM at 1710 kHz. The default is what replaces that.
    Assert.Equal(new[] { 1_000_000L }, radio.FrequenciesSet.Select(f => f.Hertz).ToArray());
  }

  [Fact]
  public void CommitBand_FromAnotherSource_ActivatesRadioThenSetsBandThenFrequency()
  {
    // D7 requirement 2, asserted in order: the source has to exist before the band can be set on
    // it, and the band has to be set before that band's frequency means anything.
    using var h = new Harness();
    var bluetooth = new FakePrimarySource(AudioSourceType.Bluetooth, "Phone");
    h.Audio.Cached[AudioSourceType.Bluetooth] = bluetooth;
    h.Audio.ActiveSource = bluetooth;

    // Cached but not active: a tuner created earlier in the session and switched away from. That
    // is what makes the band rows available while the commit is still a real source switch.
    var radio = new FakeRadioSource(h.Log) { CurrentBand = RadioBand.FM };
    h.Audio.Cached[AudioSourceType.Radio] = radio;
    h.Audio.Creatable[AudioSourceType.Radio] = radio;
    h.BandMemory.Remembered[RadioBand.AM] = Frequency.FromKilohertz(1130);

    h.HighlightRow("band:AM");
    h.Selector.Press();

    Assert.Equal(
      new[] { "GetOrCreate:Radio", "SetBand:AM", "SetFrequency:1130000" },
      h.Log.Entries.ToArray());
  }

  [Fact]
  public void CommitBand_FromAnotherSource_PublishesCommittingThenPreview()
  {
    using var h = new Harness();
    var bluetooth = new FakePrimarySource(AudioSourceType.Bluetooth, "Phone");
    h.Audio.Cached[AudioSourceType.Bluetooth] = bluetooth;
    h.Audio.ActiveSource = bluetooth;
    var radio = new FakeRadioSource(h.Log) { CurrentBand = RadioBand.FM };
    h.Audio.Cached[AudioSourceType.Radio] = radio;
    h.Audio.Creatable[AudioSourceType.Radio] = radio;

    h.HighlightRow("band:AM");
    int before = h.Hud.Published.Count;
    h.Selector.Press();

    var phases = h.Hud.Published.Skip(before).Select(p => p.Phase).ToArray();
    Assert.Equal(
      new[] { EncoderHudPhase.SelectorCommitting, EncoderHudPhase.SelectorPreview },
      phases);
  }

  [Fact]
  public void CommitBand_OnATunerThatIgnoresIt_DoesNotClaimSuccess()
  {
    // The RF320's SetBandAsync logs a warning and returns, so the absence of an exception is not
    // evidence the band changed. Without the read-back the selector would go on to "restore" the
    // new band's frequency onto a tuner still sitting on the old one.
    using var h = new Harness();
    var radio = ActiveRadio(h);
    radio.IgnoresBandChanges = true;
    h.BandMemory.Remembered[RadioBand.AM] = Frequency.FromKilohertz(1130);

    h.HighlightRow("band:AM");
    h.Selector.Press();

    Assert.Equal(new[] { RadioBand.AM }, radio.BandsSet);
    Assert.Equal(RadioBand.FM, radio.CurrentBand);
    Assert.Empty(radio.FrequenciesSet);
  }

  // --- 16-17: State E ---------------------------------------------------------------------------

  [Fact]
  public void CommitSource_ThatReturnsNull_PublishesFailed()
  {
    using var h = new Harness();
    var radio = ActiveRadio(h);
    radio.CurrentFrequency = Frequency.FromMegahertz(98.5);

    h.HighlightRow("source:Bluetooth");
    h.Selector.Press();

    var failed = Assert.Single(h.Hud.OfPhase(EncoderHudPhase.SelectorFailed));
    Assert.Equal("BLUETOOTH unavailable", failed.PrimaryText);
    // The second line is what is STILL playing, which is what stops the user concluding the knob
    // is broken and pressing it repeatedly.
    Assert.Equal("Staying on FM 98.5 MHz", failed.SecondaryText);
    Assert.Equal(EncoderInteractionTimings.SelectorFailedMs, failed.DurationMs);
  }

  [Fact]
  public void CommitSource_ThatThrows_PublishesFailed_AndDoesNotRethrow()
  {
    using var h = new Harness();
    ActiveRadio(h);
    h.Audio.CreateThrows[AudioSourceType.Bluetooth] = new InvalidOperationException("no adapter");

    h.HighlightRow("source:Bluetooth");
    var exception = Record.Exception(() => h.Selector.Press());

    Assert.Null(exception);
    // A commit with no terminal phase would leave State D's spinner up forever - it carries no
    // duration on purpose.
    Assert.Single(h.Hud.OfPhase(EncoderHudPhase.SelectorFailed));
  }

  // --- 18-21: State C, the one-rule press, and teardown ------------------------------------------

  [Fact]
  public void PressOnAnUnavailableRow_PublishesBlocked_AndLeavesTheOverlayOpen()
  {
    // State C: never a silent no-op. The flash is the answer, not a dismissal.
    using var h = new Harness();

    h.Selector.Turn(1);
    h.HighlightRow("band:FM");
    h.Selector.Press();

    var blocked = Assert.Single(h.Hud.OfPhase(EncoderHudPhase.SelectorBlocked));
    Assert.Equal(EncoderInteractionTimings.SelectorBlockedFlashMs, blocked.DurationMs);
    Assert.Equal(IndexOf(blocked.Rows!, "band:FM"), blocked.HighlightIndex);
    Assert.True(h.Selector.IsOpen);
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  [Fact]
  public void PressWithTheOverlayClosed_OpensIt_AndCommitsNothing()
  {
    // One rule, not two: with the overlay closed the highlight is what is already playing, so a
    // mis-grab is free.
    using var h = new Harness();
    ActiveRadio(h);

    h.Selector.Press();

    Assert.True(h.Selector.IsOpen);
    var preview = Assert.Single(h.Hud.Published);
    Assert.Equal(EncoderHudPhase.SelectorPreview, preview.Phase);
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  [Fact]
  public void IdleForFourSeconds_ClosesWithoutCommitting()
  {
    using var h = new Harness();
    ActiveRadio(h);
    h.Selector.Turn(1);
    Assert.True(h.Selector.IsOpen);

    h.Time.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.SelectorIdleDismissMs));

    Assert.False(h.Selector.IsOpen);
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  [Fact]
  public void Dismiss_ClosesWithoutCommitting()
  {
    // ENC-0's disconnect teardown: an overlay you can no longer navigate is a trap.
    using var h = new Harness();
    ActiveRadio(h);
    h.Selector.Turn(1);

    h.Selector.Dismiss();

    Assert.False(h.Selector.IsOpen);
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  // --- 22: the coupling a test has to hold -------------------------------------------------------

  [Fact]
  public void RowIconsAndAccents_MatchSourceTypeHelper()
  {
    // SourceTypeHelper lives in Radio.Web and Radio.Infrastructure cannot reference it, so these
    // four pairs are written out in both places. This is the assertion that keeps them equal.
    const string helper = "src/Radio.Web/Components/Shared/SourceTypeHelper.cs";

    using var h = new Harness();
    ActiveRadio(h);
    h.Selector.Turn(1);

    var expected = new Dictionary<string, (string Icon, string Accent)>
    {
      ["source:Bluetooth"] = ("bluetooth", "--source-bluetooth"),
      ["source:Vinyl"] = ("album", "--source-vinyl"),
      ["source:GenericUSB"] = ("usb", "--source-usb"),
      ["source:FilePlayer"] = ("audio_file", "--source-file"),
    };

    foreach (var (id, pair) in expected)
    {
      var row = h.Rows[IndexOf(h.Rows, id)];
      Assert.True(
        row.Icon == pair.Icon && row.AccentVar == pair.Accent,
        $"Row '{id}' renders ({row.Icon}, {row.AccentVar}) but {helper} maps it to " +
        $"({pair.Icon}, {pair.Accent}). Change both or neither.");
    }

    // The band rows share the radio pair, which the same helper defines for "Radio".
    Assert.Equal("radio", h.Rows[0].Icon);
    Assert.Equal("--source-radio", h.Rows[0].AccentVar);
  }
}
