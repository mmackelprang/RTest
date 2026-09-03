using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers the PRESETS knob's list, preview, recall and save (ENC-7).
///
/// <para>
/// The load-bearing assertions are the ones the handoff chose this knob for: a turn plays nothing,
/// a press recalls <b>from any source</b> — activating the tuner, setting the band and tuning, in
/// that order — and the one gesture on the panel that writes data never reaches a destructive path.
/// </para>
///
/// <para>
/// ⚠ <b>How these tests rendezvous with the recall and the save, since both are fire-and-forget.</b>
/// <c>Press()</c> and <c>LongPress()</c> launch their work as <c>_ = …Async(…)</c> and return
/// without awaiting it, so an assertion made afterwards is only sound because <b>every fake these
/// tests reach returns an already-completed task</b> — <c>FakePresetBank</c>, <c>FakeRadioSource</c>,
/// <c>SelectorFakeAudioManager</c> and <c>FakeBandMemory</c> alike, and
/// <c>IServiceScopeFactory.CreateScope</c> is synchronous. With no true suspension point the work
/// runs to completion on the calling thread before the call returns, and the call stack itself is
/// the rendezvous. Per <c>CLAUDE.md</c> § "Test Timing", this is stated rather than assumed:
/// <b>giving any of those fakes a real await (a <c>Task.Delay</c>, a <c>Task.Yield</c>, a
/// <c>Task.Run</c>) silently converts every recall and save assertion in this file into a race.</b>
/// If a fake ever needs to suspend, add an explicit completion signal and wait on it; do not add a
/// sleep.
/// </para>
/// </summary>
public class PresetSelectorServiceTests
{
  private sealed class Harness : IDisposable
  {
    public readonly CallLog Log = new();
    public readonly SelectorFakeAudioManager Audio;
    public readonly FakeBandMemory BandMemory = new();
    public readonly RecordingSelectorSink Hud = new();
    public readonly FakeTimeProvider Time = new();
    public readonly PresetBankScope Scope = new();
    public readonly PresetSelectorService Selector;

    public Harness()
    {
      Audio = new SelectorFakeAudioManager(Log);
      Selector = new PresetSelectorService(
        NullLogger<PresetSelectorService>.Instance,
        Scope.Factory,
        () => Audio,
        () => BandMemory,
        Hud,
        Time);
    }

    public FakePresetBank Bank => Scope.Bank;

    /// <summary>The rows and highlight as the overlay last saw them.</summary>
    public EncoderHudEventArgs LastPreview =>
      Hud.Published.Last(p => p.Phase == EncoderHudPhase.SelectorPreview);

    public IReadOnlyList<EncoderSelectorRow> Rows => LastPreview.Rows!;

    public EncoderHudEventArgs LastNotice =>
      Hud.Published.Last(p => p.Phase == EncoderHudPhase.SelectorNotice);

    /// <summary>Makes a tuner the active source, already tuned where the test wants it.</summary>
    public FakeRadioSource ActiveRadio(RadioBand band = RadioBand.FM, long hertz = 98_500_000)
    {
      var radio = new FakeRadioSource(Log)
      {
        CurrentBand = band,
        CurrentFrequency = new Frequency(hertz),
      };
      Audio.Cached[AudioSourceType.Radio] = radio;
      Audio.ActiveSource = radio;
      return radio;
    }

    /// <summary>Stages a tuner that does not exist yet, so a recall has to create it.</summary>
    public FakeRadioSource CreatableRadio(RadioBand band = RadioBand.FM)
    {
      var radio = new FakeRadioSource(Log) { CurrentBand = band };
      Audio.Creatable[AudioSourceType.Radio] = radio;
      return radio;
    }

    public static string RowIdOf(RadioPreset preset) => $"preset:{preset.Id}";

    /// <summary>
    /// Opens the overlay (a press on a closed overlay opens without recalling) and walks the
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

    /// <summary>Highlights a row and presses, which is the whole of a recall from the user's side.</summary>
    public void Recall(RadioPreset preset)
    {
      HighlightRow(RowIdOf(preset));
      Selector.Press();
    }

    public void Dispose()
    {
      Selector.Dispose();
      Scope.Dispose();
    }
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

  // --- 1-2: turning previews ---------------------------------------------------------------------

  [Fact]
  public void Turn_OpensTheOverlayAndMovesOneEntry()
  {
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Bank.Seed("KUOW", RadioBand.FM, 94_900_000);
    h.Bank.Seed("KNKX", RadioBand.FM, 88_500_000);

    // The first turn is the open: it applies the movement against the rows it has, which on a cold
    // overlay is none, and the bank arrives with the reload that follows.
    h.Selector.Turn(1);
    Assert.True(h.Selector.IsOpen);
    int before = h.LastPreview.HighlightIndex;

    // The second is an ordinary move, and one detent is one entry.
    h.Selector.Turn(1);

    Assert.Equal(before + 1, h.LastPreview.HighlightIndex);
    Assert.Equal(3, h.Rows.Count);
  }

  [Fact]
  public void Turn_PlaysNothing()
  {
    // The whole turn contract (handoff §4.4). Spinning the list must never tune, and must never
    // stand a source up.
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Bank.Seed("KUOW", RadioBand.FM, 94_900_000);
    var radio = h.ActiveRadio();

    for (int i = 0; i < 8; i++)
    {
      h.Selector.Turn(1);
    }

    Assert.Empty(h.Audio.GetOrCreateCalls);
    Assert.Empty(radio.FrequenciesSet);
    Assert.Empty(radio.BandsSet);
  }

  // --- 3-6: composition --------------------------------------------------------------------------

  [Fact]
  public void Rows_AreOrderedByBandThenSlot_MatchingTheOnScreenRail()
  {
    // Three orderings of this bank exist in one stack — the repository sorts by Name,
    // RadioControlPanel by band/slot/created, RadioPage by CreatedAt. The knob is a remote control
    // for RadioControlPanel's rail, so it follows that one.
    using var h = new Harness();
    h.Bank.Seed("Zebra FM", RadioBand.FM, 90_300_000);
    h.Bank.Seed("Alpha AM", RadioBand.AM, 1_010_000);
    h.Bank.Seed("Yankee FM", RadioBand.FM, 94_900_000);
    h.Bank.Seed("Bravo AM", RadioBand.AM, 710_000);

    h.Selector.Turn(1);

    Assert.Equal(
      new[] { "Alpha AM", "Bravo AM", "Zebra FM", "Yankee FM" },
      h.Rows.Select(r => r.Primary).ToArray());
  }

  [Fact]
  public void Rows_CarryThePerBandOrdinal_SoTwoBandsCanBothShowSlotOne()
  {
    // The plan's D-1. Ordinals are per band, so two rows reading "01" is honest rather than a bug —
    // and the band in the secondary line is what tells them apart.
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Bank.Seed("KIRO", RadioBand.AM, 710_000);

    h.Selector.Turn(1);

    Assert.All(h.Rows, r => Assert.Equal("01", r.Ordinal));
    Assert.Contains(h.Rows, r => r.Secondary == "AM 710 kHz");
    Assert.Contains(h.Rows, r => r.Secondary == "FM 90.3 MHz");
  }

  [Fact]
  public void Rows_MarkTheCurrentlyTunedPresetAsCurrent()
  {
    using var h = new Harness();
    var tuned = h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Bank.Seed("KUOW", RadioBand.FM, 94_900_000);
    h.ActiveRadio(RadioBand.FM, 90_300_000);

    h.Selector.Turn(1);

    var current = Assert.Single(h.Rows, r => r.IsCurrent);
    Assert.Equal(Harness.RowIdOf(tuned), current.Id);
  }

  [Fact]
  public void Rows_UseTheSameOneHertzToleranceAsTheOnScreenRail()
  {
    // RadioControlPanel.IsActivePreset uses < 1.0 Hz. A different tolerance here would let the
    // knob's marker and the rail's .is-active cue disagree about the same station.
    using var h = new Harness();
    var inside = h.Bank.Seed("Half a hertz away", RadioBand.FM, 90_300_000.5);
    var outside = h.Bank.Seed("Two hertz away", RadioBand.FM, 90_300_002);
    h.ActiveRadio(RadioBand.FM, 90_300_000);

    h.Selector.Turn(1);

    Assert.True(h.Rows.Single(r => r.Id == Harness.RowIdOf(inside)).IsCurrent);
    Assert.False(h.Rows.Single(r => r.Id == Harness.RowIdOf(outside)).IsCurrent);
  }

  // --- 7: the empty state ------------------------------------------------------------------------

  [Fact]
  public void EmptyBank_PublishesTheInstructionalEmptyState()
  {
    // State B, and the reason it earns its place: the knob teaches its own use. An empty list with
    // no instruction is a dead end on a surface with no other affordance.
    using var h = new Harness();

    h.Selector.Turn(1);

    var card = h.LastPreview;
    Assert.Empty(card.Rows!);
    Assert.Equal("NO STATIONS SAVED", card.EmptyPrimary);
    Assert.Equal("hold this knob to save what's playing", card.EmptySecondary);
    // It is a sentence spoken to the user, not a label. Uppercasing it would be a regression
    // against the handoff's mock, and nothing in the CSS does it.
    Assert.Equal(card.EmptySecondary, card.EmptySecondary!.ToLowerInvariant());
    Assert.Equal("0 saved", card.TitleSuffix);
  }

  [Fact]
  public void Press_OnAnOpenButEmptyOverlay_RepublishesTheEmptyState_AndReArmsTheWindow()
  {
    // There is no row to recall, but a silent return would publish nothing and leave the idle
    // window running from the press that opened the overlay - so pressing again would let the card
    // vanish under the user's finger, on the one screen with no other affordance.
    using var h = new Harness();

    h.Selector.Press();                                          // opens on an empty bank
    h.Time.Advance(TimeSpan.FromMilliseconds(3000));             // most of the 4 s window is gone
    int before = h.Hud.Published.Count;

    h.Selector.Press();                                          // the press this test is about

    Assert.Equal(before + 1, h.Hud.Published.Count);
    var card = h.LastPreview;
    Assert.Empty(card.Rows!);
    Assert.Equal("NO STATIONS SAVED", card.EmptyPrimary);

    // The window was re-armed rather than left to expire: 3000 + 1500 is past the original 4000 ms
    // deadline, and the overlay is still up. Driven by the injected clock, not by elapsed time.
    h.Time.Advance(TimeSpan.FromMilliseconds(1500));
    Assert.True(h.Selector.IsOpen);
  }

  // --- 8-14: recall ------------------------------------------------------------------------------

  [Fact]
  public void Recall_FromANonRadioSource_ActivatesRadioThenSetsBandThenFrequency()
  {
    // §4.3, and the reason this knob was chosen over three alternatives: it is never dead. The
    // ORDER is the assertion — tuning before the band is set lands somewhere else.
    using var h = new Harness();
    var preset = h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Audio.ActiveSource = new FakePrimarySource(AudioSourceType.Bluetooth, "Pixel 8");
    h.CreatableRadio();

    h.Recall(preset);

    Assert.Equal(
      new[] { "GetOrCreate:Radio", "SetBand:FM", "SetFrequency:90300000" },
      h.Log.Entries.ToArray());
  }

  [Fact]
  public void Recall_FromANonRadioSource_PublishesCommittingThenPreview()
  {
    // State D. The switch can take seconds, so the spinner is not optional polish — and every path
    // out of the recall has to replace it, or it stays up forever.
    using var h = new Harness();
    var preset = h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Audio.ActiveSource = new FakePrimarySource(AudioSourceType.Bluetooth, "Pixel 8");
    h.CreatableRadio();

    h.Recall(preset);

    var phases = h.Hud.Published.Select(p => p.Phase).ToList();
    int committing = phases.LastIndexOf(EncoderHudPhase.SelectorCommitting);
    Assert.True(committing >= 0, "a source switch must show State D");
    Assert.Equal(EncoderHudPhase.SelectorPreview, phases[^1]);
    Assert.True(committing < phases.Count - 1);
  }

  [Fact]
  public void Recall_WhileAlreadyOnRadio_DoesNotRecreateTheSource()
  {
    // Tearing the tuner down and standing it back up to change frequency would put a fade and a
    // spinner on the fastest path this knob has.
    using var h = new Harness();
    var preset = h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    var radio = h.ActiveRadio(RadioBand.FM, 94_900_000);

    h.Recall(preset);

    Assert.Empty(h.Audio.GetOrCreateCalls);
    Assert.Empty(h.Hud.OfPhase(EncoderHudPhase.SelectorCommitting));
    Assert.Equal(new Frequency(90_300_000), Assert.Single(radio.FrequenciesSet));
  }

  [Fact]
  public void Recall_StopsAnInFlightScanFirst()
  {
    // The touchscreen's recall does this too. Tuning under a running scan lands somewhere else a
    // second later.
    using var h = new Harness();
    var preset = h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    var radio = h.ActiveRadio(RadioBand.FM, 94_900_000);
    radio.IsScanning = true;

    h.Recall(preset);

    Assert.Equal(1, radio.StopScanCalls);
    Assert.True(
      h.Log.Entries.IndexOf("StopScan") < h.Log.Entries.IndexOf("SetBand:FM"),
      "the scan has to stop before the tune, not after it");
  }

  [Fact]
  public void Recall_OfADeletedPreset_PublishesFailed_AndRefreshes()
  {
    // The touchscreen deleted it while the overlay was open on it. The bank is re-read only when
    // the overlay opens, so this row is reachable by design rather than by accident.
    using var h = new Harness();
    var doomed = h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Bank.Seed("KUOW", RadioBand.FM, 94_900_000);
    h.HighlightRow(Harness.RowIdOf(doomed));
    h.Bank.Presets.RemoveAll(p => p.Id == doomed.Id);

    h.Selector.Press();

    var failed = Assert.Single(h.Hud.OfPhase(EncoderHudPhase.SelectorFailed));
    Assert.Equal("That preset is gone", failed.PrimaryText);

    // ...and the list caught up, so the next turn does not offer the row again.
    h.Selector.Turn(1);
    Assert.Equal("KUOW", Assert.Single(h.Rows).Primary);
  }

  [Fact]
  public void Recall_WhenSetFrequencyThrows_PublishesFailed_AndDoesNotRethrow()
  {
    // SDRRadioAudioSource throws ArgumentOutOfRangeException when the receiver rejects a value.
    // Without the catch this would be an unobserved task exception and the user would be left with
    // silence and no card.
    using var h = new Harness();
    var preset = h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    var radio = h.ActiveRadio(RadioBand.FM, 94_900_000);
    radio.SetFrequencyThrows = new ArgumentOutOfRangeException("frequency");

    h.Recall(preset);

    var failed = Assert.Single(h.Hud.OfPhase(EncoderHudPhase.SelectorFailed));
    Assert.Equal("Could not tune that station", failed.PrimaryText);
  }

  [Fact]
  public void Recall_RecordsTheBandMemory()
  {
    // A recall is also a tune, so the band memory learns from it the same way a knob turn does —
    // otherwise leaving FM and coming back would land on where the dial was before the recall.
    using var h = new Harness();
    var preset = h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.ActiveRadio(RadioBand.FM, 94_900_000);

    h.Recall(preset);

    Assert.Equal((RadioBand.FM, new Frequency(90_300_000)), Assert.Single(h.BandMemory.Writes));
  }

  // --- 15-21: save -------------------------------------------------------------------------------

  [Fact]
  public void Save_OnRadio_AddsAPresetAndReportsItsSlot()
  {
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.ActiveRadio(RadioBand.FM, 101_500_000);

    h.Selector.LongPress();

    Assert.Equal(2, h.Bank.Presets.Count);
    var notice = h.LastNotice;
    // The slot is the per-band ordinal the bank derives afterwards; nothing was searched for and no
    // gap was filled.
    Assert.Equal("Saved to 02", notice.PrimaryText);
    Assert.Equal(EncoderInteractionTimings.SelectorNoticeMs, notice.DurationMs);
    // The overlay draws the title row for a notice as well as for a list, so the count has to
    // include the row just written. The bank is re-read before the notice is composed for exactly
    // this reason; composing it first reported the count from before the save.
    Assert.Equal("2 saved", notice.TitleSuffix);
  }

  [Fact]
  public void Save_OnAColdSession_HeadsItsNoticeWithACountItActuallyRead()
  {
    // A hold never opens the overlay, and the bank is otherwise read only on open, after a save and
    // after a recall - so on a cold session nothing has read it when a boundary notice is composed.
    // Before the read at the top of SaveAsync every such notice was headed "0 saved".
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Bank.Seed("KUOW", RadioBand.FM, 94_900_000);
    h.Audio.ActiveSource = new FakePrimarySource(AudioSourceType.Bluetooth, "Pixel 8");

    h.Selector.LongPress();

    var notice = h.LastNotice;
    Assert.Equal("Only radio stations can be saved", notice.PrimaryText);
    Assert.Equal("2 saved", notice.TitleSuffix);
  }

  [Fact]
  public void Save_UsesRdsStationNameStable_WhenPresent()
  {
    // The stable value, not the live PS: a rolling PS routinely reads "TOO HOT" or a song title at
    // the instant a hand completes a 600 ms hold.
    using var h = new Harness();
    var radio = h.ActiveRadio(RadioBand.FM, 101_500_000);
    radio.RdsStationNameStable = "KEXP";

    h.Selector.LongPress();

    Assert.Equal("KEXP", Assert.Single(h.Bank.Presets).Name);
  }

  [Fact]
  public void Save_FallsBackToTheDefaultName_WhenThereIsNoRdsName()
  {
    // "Default" here means THIS service's fallback, matching RadioControlPanel's save dialog:
    // "{Band} {formatted frequency}". It deliberately does not reach
    // RadioPreset.GetDefaultName, which formats the frequency as display units while
    // RadioPreset.Frequency holds hertz — that path would name this preset "FM - 101500000.0".
    using var h = new Harness();
    h.ActiveRadio(RadioBand.FM, 101_500_000);

    h.Selector.LongPress();

    var saved = Assert.Single(h.Bank.Presets);
    Assert.Equal("FM 101.5 MHz", saved.Name);
    Assert.DoesNotContain("101500000", saved.Name, StringComparison.Ordinal);
    // An explicit non-blank name is always passed, which is what keeps the service's own default
    // out of reach.
    Assert.False(string.IsNullOrWhiteSpace(Assert.Single(h.Bank.AddCalls).Name));
  }

  [Fact]
  public void Save_OnANonRadioSource_ReportsTheV1Boundary_AndWritesNothing()
  {
    // §4.4. The one context-limited gesture in the design says so out loud; a silent failure here
    // is the same defect the row exists to fix.
    using var h = new Harness();
    h.Audio.ActiveSource = new FakePrimarySource(AudioSourceType.Bluetooth, "Pixel 8");

    h.Selector.LongPress();

    var notice = h.LastNotice;
    Assert.Equal("Only radio stations can be saved", notice.PrimaryText);
    Assert.Equal(EncoderInteractionTimings.SelectorNoticeShortMs, notice.DurationMs);
    Assert.Empty(h.Bank.AddCalls);
    Assert.Empty(h.Bank.Presets);
  }

  [Fact]
  public void Save_WhenTheBankIsFull_ReportsPresetsFull_AndWritesNothing()
  {
    // The real ceiling is 50 and this will essentially never fire in the field, but the boundary
    // reports rather than swallows — and it writes nothing, which is the half that matters.
    using var h = new Harness();
    h.Bank.MaxPresets = 1;
    h.Bank.Seed("KIRO", RadioBand.AM, 710_000);
    h.ActiveRadio(RadioBand.FM, 101_500_000);

    h.Selector.LongPress();

    var notice = h.LastNotice;
    Assert.Equal("PRESETS FULL", notice.PrimaryText);
    Assert.Equal("replace a slot on screen", notice.SecondaryText);
    Assert.Equal("KIRO", Assert.Single(h.Bank.Presets).Name);
  }

  [Fact]
  public void Save_OfAStationAlreadySaved_ReportsAlreadySaved_AndWritesNothing()
  {
    // The plan's §0.3: the case the handoff does not cover and a guest hits first, because holding
    // the knob is what someone does when they cannot remember whether they already saved it.
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.ActiveRadio(RadioBand.FM, 90_300_000);

    h.Selector.LongPress();

    var notice = h.LastNotice;
    Assert.Equal("ALREADY SAVED · slot 01", notice.PrimaryText);
    Assert.Equal(EncoderInteractionTimings.SelectorNoticeShortMs, notice.DurationMs);
    Assert.Empty(h.Bank.AddCalls);
    Assert.Single(h.Bank.Presets);
  }

  [Fact]
  public void Save_WhenTheAddRacesADuplicateNamedLikeTheCap_ReportsTheDuplicate_NotAFullBank()
  {
    // RadioPresetService's duplicate message interpolates the existing preset's NAME - "A preset
    // already exists for {band} - {frequency}: {name}" - so a station called "Maximum Rock"
    // satisfies the bank-full filter's Contains("Maximum") too. This passes only while the
    // duplicate arm is listed FIRST; swapping the two catch arms turns it red.
    //
    // Reachable through the TOCTOU window between PresetExistsAsync and AddPresetAsync, which is
    // what HideDuplicatesFromExistsCheck models.
    using var h = new Harness();
    h.Bank.Seed("Maximum Rock", RadioBand.FM, 101_500_000);
    h.Bank.HideDuplicatesFromExistsCheck = true;
    h.ActiveRadio(RadioBand.FM, 101_500_000);

    h.Selector.LongPress();

    var notice = h.LastNotice;
    Assert.Equal("ALREADY SAVED", notice.PrimaryText);
    Assert.DoesNotContain("FULL", notice.PrimaryText!, StringComparison.Ordinal);
    // The add threw, so nothing was written.
    Assert.Equal("Maximum Rock", Assert.Single(h.Bank.Presets).Name);
  }

  [Fact]
  public void Save_NeverCallsAnyOverwriteOrDeletePath()
  {
    // The guard for the one gesture on the panel that writes data. Replacement stays on the
    // touchscreen, behind the kebab, where it has a confirmation and an undo.
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    var radio = h.ActiveRadio(RadioBand.FM, 101_500_000);

    h.Selector.LongPress();                                   // a new station
    h.Selector.LongPress();                                   // the same one again: duplicate
    radio.CurrentFrequency = new Frequency(94_900_000);
    h.Bank.MaxPresets = 2;
    h.Selector.LongPress();                                   // and now the bank is full

    Assert.Empty(h.Bank.DeleteCalls);
    Assert.Empty(h.Bank.RenameCalls);
    // Exactly one write landed across all three holds, and it was an append.
    Assert.Equal(2, h.Bank.Presets.Count);
  }

  // --- 23: a bank read that fails ----------------------------------------------------------------

  [Fact]
  public void BankReadFailure_WithTheOverlayOpen_SaysSo_RatherThanShowingAnEmptyBank()
  {
    // The catch used to log and return, which left State B on screen - and State B reads
    // "NO STATIONS SAVED". A bank that could not be read is not a bank that is empty, and the user
    // has no other affordance on this screen to check against.
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    h.Bank.GetAllThrows = new InvalidOperationException("database is locked");

    h.Selector.Turn(1);

    var notice = h.LastNotice;
    Assert.Equal("Could not read your presets", notice.PrimaryText);
    Assert.Equal(EncoderInteractionTimings.SelectorNoticeMs, notice.DurationMs);
  }

  // --- 22: teardown ------------------------------------------------------------------------------

  [Fact]
  public void Dismiss_ClosesWithoutRecalling()
  {
    // ENC-0's disconnect path. An overlay you can no longer navigate is a trap, and tearing it
    // down must not commit the row it happened to be sitting on.
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    var radio = h.ActiveRadio(RadioBand.FM, 94_900_000);
    h.Selector.Turn(1);
    Assert.True(h.Selector.IsOpen);

    h.Selector.Dismiss();

    Assert.False(h.Selector.IsOpen);
    Assert.Empty(h.Audio.GetOrCreateCalls);
    Assert.Empty(radio.FrequenciesSet);
  }

  [Fact]
  public void IdleAfterATurn_DismissesWithoutRecalling()
  {
    // The overlay closes itself after SelectorIdleDismissMs with nothing committed, so the API-side
    // open flag stays in step with the card the Web has already dismissed. Driven by the injected
    // clock rather than by elapsed time — see the class remarks.
    using var h = new Harness();
    h.Bank.Seed("KEXP", RadioBand.FM, 90_300_000);
    var radio = h.ActiveRadio(RadioBand.FM, 94_900_000);
    h.Selector.Turn(1);
    Assert.True(h.Selector.IsOpen);

    h.Time.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.SelectorIdleDismissMs));

    Assert.False(h.Selector.IsOpen);
    Assert.Empty(radio.FrequenciesSet);
  }
}
