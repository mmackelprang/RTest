using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using Radio.Core.Configuration;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// UI-21 — the preset bar mirrors the PRESETS knob's highlight while the knob's list is open (spec
/// 2026-10-01 §4.8).
///
/// <para>
/// The bar reads the knob off <see cref="EncoderHudService"/>, the singleton the overlay itself draws
/// from, so these tests drive that service exactly as the hub does — <see cref="EncoderHudService.Publish"/>
/// with the payload <c>PresetSelectorService.ComposeLocked</c> sends — and read what the bar asked
/// <c>preset-bar.js</c> to do from the module's recorded <c>reveal</c> calls.
/// </para>
///
/// <para>
/// Timing follows CLAUDE.md § Test Timing. The HUD's dismissal timer runs on a
/// <see cref="FakeTimeProvider"/> shared with the bar's manual-scroll hold, and both the publish and
/// the clock advance are performed <b>on the renderer's dispatcher</b> (<c>cut.InvokeAsync</c>), so
/// the bar's own <c>InvokeAsync</c> hop runs inline and every assertion — including the negative
/// "nothing was revealed" ones — is made after the work has happened, not after a wait.
/// </para>
/// </summary>
public class PresetBarKnobMirrorTests : TestContext
{
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
  private readonly EncoderHudService _hud;
  private readonly BunitJSModuleInterop _module;

  public PresetBarKnobMirrorTests()
  {
    _hud = new EncoderHudService(timeProvider: _clock);
    Services.AddSingleton(_hud);
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
    _module = JSInterop.SetupModule("./js/preset-bar.js");
    _module.Mode = JSRuntimeMode.Loose;
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _hud.Dispose();
    }
    base.Dispose(disposing);
  }

  private static RadioPresetDto P(string id, string band, int slot, double freq = 92_300_000) =>
    new(id, $"Station {id}", freq, band, DateTimeOffset.UnixEpoch.AddMinutes(slot), slot);

  /// <summary>Eight FM presets, slots 1–8, ids f1..f8.</summary>
  private static List<RadioPresetDto> EightFm() =>
    Enumerable.Range(1, 8).Select(i => P($"f{i}", "FM", i, 88_100_000 + (i * 1_000_000))).ToList();

  /// <summary>
  /// The PRESETS knob's preview card as <c>PresetSelectorService.ComposeLocked</c> builds it: every row,
  /// <c>preset:{id}</c> ids, the highlight index, and the idle-dismiss duration.
  /// </summary>
  private static EncoderHudDto KnobPreview(IEnumerable<string> presetIds, int highlight, string phase = "SelectorPreview") =>
    new()
    {
      EncoderIndex = 2,
      Label = "PRESETS",
      Phase = phase,
      Title = "PRESETS",
      Rows = presetIds.Select(id => new EncoderSelectorRowDto { Id = $"preset:{id}", Primary = id, AccentVar = "--source-radio" }).ToList(),
      HighlightIndex = highlight,
      DurationMs = EncoderInteractionTimings.SelectorIdleDismissMs,
    };

  private static EncoderHudDto KnobPreview(IReadOnlyList<RadioPresetDto> presets, int highlight) =>
    KnobPreview(presets.Select(p => p.Id), highlight);

  private IRenderedComponent<PresetBar> RenderBar(IReadOnlyList<RadioPresetDto> presets, string? active = null, string band = "FM") =>
    RenderComponent<PresetBar>(p => p
      .Add(x => x.Presets, presets)
      .Add(x => x.CurrentBand, band)
      .Add(x => x.ActivePresetId, active)
      .Add(x => x.Clock, _clock));

  private static void Publish(IRenderedComponent<PresetBar> cut, EncoderHudDto card) =>
    cut.InvokeAsync(() => cut.Services.GetRequiredService<EncoderHudService>().Publish(card)).GetAwaiter().GetResult();

  private void Advance(IRenderedComponent<PresetBar> cut, TimeSpan by) =>
    cut.InvokeAsync(() => _clock.Advance(by)).GetAwaiter().GetResult();

  private void DismissHud(IRenderedComponent<PresetBar> cut) =>
    cut.InvokeAsync(() => _hud.Dismiss()).GetAwaiter().GetResult();

  /// <summary>The browser reporting a swipe (UI-20's manual-scroll hold starts now).</summary>
  private static void ManualScroll(IRenderedComponent<PresetBar> cut) =>
    cut.InvokeAsync(() => cut.Instance.OnManualScroll()).GetAwaiter().GetResult();

  private IReadOnlyList<JSRuntimeInvocation> Reveals => _module.Invocations["reveal"];

  /// <summary>Preset ids of the cards carrying the knob highlight.</summary>
  private static List<string?> Highlighted(IRenderedComponent<PresetBar> cut) =>
    cut.FindAll(".rcp-bar-item.is-knob-highlight")
      .Select(e => e.QuerySelector(".rcp-bar-card")!.GetAttribute("data-preset-id"))
      .ToList();

  // ── Reading the knob off the HUD payload ────────────────────────────────────────────────

  [Theory]
  [InlineData("SelectorPreview")]
  [InlineData("SelectorBlocked")]
  public void KnobPresetIdOf_ReadsTheHighlightedPresetRow_InThePhasesTheOverlayDrawsTheListIn(string phase)
  {
    // EncoderSelectorOverlay draws the list with a highlighted row in both. The PRESETS knob only
    // publishes Preview; Blocked is accepted so the bar follows the overlay's own rule.
    PresetBar.KnobPresetIdOf(KnobPreview(new[] { "a", "b", "c" }, 1, phase)).Should().Be("b");
  }

  [Theory]
  [InlineData("SelectorCommitting")]
  [InlineData("SelectorFailed")]
  [InlineData("SelectorNotice")]
  [InlineData("Value")]
  [InlineData("SomePhaseFromANewerApi")]
  public void KnobPresetIdOf_PhasesThatDoNotDrawTheList_AreNothing(string phase)
  {
    // The message phases still carry the rows and an index, but the overlay draws a line of text
    // instead of the list (EncoderSelectorOverlay.IsMessagePhase) — so there is no highlighted row on
    // screen to mirror. An unknown phase draws nothing at all.
    PresetBar.KnobPresetIdOf(KnobPreview(new[] { "a", "b" }, 1, phase)).Should().BeNull();
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(2)]
  public void KnobPresetIdOf_AnIndexOutsideTheRows_IsNothing(int highlight)
  {
    PresetBar.KnobPresetIdOf(KnobPreview(new[] { "a", "b" }, highlight)).Should().BeNull();
  }

  [Fact]
  public void KnobPresetIdOf_TheSourceKnobsList_NoCard_AndRowlessPayloads_AreNothing()
  {
    // Every row of the SOURCE list, highlighted in turn. "source:Spotify" matters: it is longer than
    // the prefix, so only the prefix test — not the length test — keeps it from reading as preset
    // "Spotify".
    var rows = new List<EncoderSelectorRowDto>
    {
      new() { Id = "band:FM", Primary = "FM" },
      new() { Id = "band:VHF", Primary = "VHF" },
      new() { Id = "source:Spotify", Primary = "Spotify" },
    };
    for (var i = 0; i < rows.Count; i++)
    {
      var source = new EncoderHudDto { Phase = "SelectorPreview", Title = "SOURCE", Rows = rows, HighlightIndex = i };
      PresetBar.KnobPresetIdOf(source).Should().BeNull($"the SOURCE knob's rows are band: and source:, never preset: ({rows[i].Id})");
    }
    PresetBar.KnobPresetIdOf(null).Should().BeNull();
    PresetBar.KnobPresetIdOf(new EncoderHudDto { Phase = "SelectorPreview", Rows = null, HighlightIndex = 0 }).Should().BeNull();
    PresetBar.KnobPresetIdOf(KnobPreview(new[] { "" }, 0)).Should().BeNull("a bare prefix names no preset");
  }

  // ── The mirror on screen ─────────────────────────────────────────────────────────────────

  [Fact]
  public void KnobPreview_MarksTheMatchingCard_AndRevealsIt_Smoothly()
  {
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f1");
    Highlighted(cut).Should().BeEmpty();

    Publish(cut, KnobPreview(presets, 6));

    Highlighted(cut).Should().Equal("f7");
    var reveal = Reveals.Last();
    reveal.Arguments[1].Should().Be(6);
    reveal.Arguments[2].Should().Be(true);
  }

  [Fact]
  public void TurningTheKnob_MovesTheMark_OneCardAtATime_AndRevealsEachNewCard()
  {
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f1");

    foreach (var i in new[] { 0, 1, 2, 5, 7, 6 })
    {
      Publish(cut, KnobPreview(presets, i));

      Highlighted(cut).Should().Equal(presets[i].Id);
      Reveals.Last().Arguments[1].Should().Be(i);
    }
  }

  [Fact]
  public void EveryDetent_LandsOnTheCardAtTheSamePosition_InTheBarsOwnOrder()
  {
    // The order pin from the bar's side: rows composed in the knob's order (band by name, then
    // slot — PresetSelectorServiceTests pins that the knob really composes them so) highlight the
    // card at the same position, for every position, across band groups.
    var presets = PresetBar.OrderLikeKnob(new[]
    {
      P("w1", "WB", 1), P("v1", "VHF", 1), P("f2", "FM", 2), P("a1", "AM", 1), P("f1", "FM", 1), P("air1", "AIR", 1),
    });
    var cut = RenderBar(presets);
    var cards = cut.FindAll(".rcp-bar-card").Select(e => e.GetAttribute("data-preset-id")).ToList();

    for (var i = 0; i < presets.Count; i++)
    {
      Publish(cut, KnobPreview(presets, i));
      Highlighted(cut).Should().Equal(cards[i]);
    }
  }

  [Fact]
  public void KnobRestingOnThePlayingPreset_KeepsBothCues()
  {
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f3");

    Publish(cut, KnobPreview(presets, 2));

    var item = cut.Find(".rcp-bar-item.is-knob-highlight");
    item.QuerySelector(".rcp-bar-card")!.ClassList.Should().Contain("is-active", "the knob mark adds to the playing cue, it does not replace it");
    item.QuerySelector(".rcp-bar-card")!.GetAttribute("aria-current").Should().Be("true");
  }

  [Fact]
  public void OverlayIdleDismiss_ClearsTheMark_AndReturnsTheStripToThePlayingPreset()
  {
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f2");
    Publish(cut, KnobPreview(presets, 7));
    var before = Reveals.Count;

    Advance(cut, TimeSpan.FromMilliseconds(EncoderInteractionTimings.SelectorIdleDismissMs - 1));
    Highlighted(cut).Should().Equal("f8");

    Advance(cut, TimeSpan.FromMilliseconds(1));

    Highlighted(cut).Should().BeEmpty();
    Reveals.Should().HaveCount(before + 1);
    Reveals.Last().Arguments[1].Should().Be(1, "back to f2, the playing preset");
    Reveals.Last().Arguments[2].Should().Be(true);
  }

  [Fact]
  public void OverlayClose_WithinTenSecondsOfAManualScroll_LeavesTheStripWhereTheUserPutIt()
  {
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f2");
    Publish(cut, KnobPreview(presets, 7));
    ManualScroll(cut);
    var before = Reveals.Count;

    DismissHud(cut);

    Highlighted(cut).Should().BeEmpty();
    Reveals.Should().HaveCount(before, "UI-20's hold: browsing is not pulled away");
  }

  [Fact]
  public void KnobTurn_RevealsItsCard_EvenDuringTheManualHold_ButASwipeBetweenTurnsIsLeftAlone()
  {
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f1");
    Publish(cut, KnobPreview(presets, 3));
    ManualScroll(cut);
    var before = Reveals.Count;

    // A re-publish of the same highlight (the API republishes on a bank reload) moves nothing.
    Publish(cut, KnobPreview(presets, 3));
    Reveals.Should().HaveCount(before, "the knob has not moved, so the swipe stands");

    Publish(cut, KnobPreview(presets, 6));
    Reveals.Should().HaveCount(before + 1, "turning the knob is itself browsing, and the latest thing the user did");
    Reveals.Last().Arguments[1].Should().Be(6);
  }

  [Fact]
  public void ARadioStateTick_WhileTheKnobsListIsUp_DoesNotPullASwipeBackToTheKnobsCard()
  {
    // The re-publish case above never reaches ChooseReveal — the bar does not re-render for an
    // unchanged mirror. The panel, though, re-renders the bar on every radio-state tick (~500 ms), and
    // that render must not treat "the knob's list is up" as "reveal the knob's card again".
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f1");
    Publish(cut, KnobPreview(presets, 3));
    ManualScroll(cut);
    var before = Reveals.Count;

    cut.SetParametersAndRender(p => p.Add(x => x.Presets, presets.ToList()));
    cut.SetParametersAndRender(p => p.Add(x => x.Presets, presets.ToList()));

    Reveals.Should().HaveCount(before);
    Highlighted(cut).Should().Equal("f4");
  }

  [Fact]
  public void TurningOntoARowTheBarHasNoCardFor_DoesNotBounceTheStrip_AndClosingStillReturnsIt()
  {
    // Review M2. The knob's list is still open on such a row, so it must not read as the list
    // closing: that would scroll back to the playing preset mid-turn, and the next detent would
    // scroll forward again.
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f1");
    var rows = new[] { "f1", "f2", "gone", "f3", "f4", "f5", "f6", "f7", "f8" };
    Publish(cut, KnobPreview(rows, 7)); // f7
    var before = Reveals.Count;

    Publish(cut, KnobPreview(rows, 2)); // "gone"

    Highlighted(cut).Should().BeEmpty();
    Reveals.Should().HaveCount(before, "the list is still open — nothing to return from yet");

    DismissHud(cut);

    Reveals.Should().HaveCount(before + 1);
    Reveals.Last().Arguments[1].Should().Be(0, "the list closed, so back to f1, the playing preset");
  }

  [Fact]
  public void AStaleKnobRow_DoesNotHoldTheStrip_AgainstAPlayingPresetChange()
  {
    // A knob row with no card is not a mirror, so it must not hijack the reveal rules either: the
    // playing preset changing still brings it into view.
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f1");
    Publish(cut, KnobPreview(new[] { "gone" }, 0));

    cut.SetParametersAndRender(p => p.Add(x => x.ActivePresetId, "f6"));

    Reveals.Last().Arguments[1].Should().Be(5);
  }

  [Fact]
  public void AnotherCardReplacingTheKnobsList_ClearsTheMark()
  {
    // The HUD shows one card at a time: a volume turn replaces the PRESETS overlay on screen, so the
    // bar must stop mirroring a list nobody can see.
    var presets = EightFm();
    var cut = RenderBar(presets);
    Publish(cut, KnobPreview(presets, 4));

    Publish(cut, new EncoderHudDto { EncoderIndex = 0, Label = "VOLUME", Phase = "Value", VolumePercent = 40 });

    Highlighted(cut).Should().BeEmpty();
  }

  [Fact]
  public void APressThatStartsASourceSwitch_ClearsTheMark_WithTheList()
  {
    var presets = EightFm();
    var cut = RenderBar(presets);
    Publish(cut, KnobPreview(presets, 4));

    Publish(cut, KnobPreview(presets.Select(p => p.Id), 4, "SelectorCommitting"));

    Highlighted(cut).Should().BeEmpty();
  }

  [Fact]
  public void AKnobRowTheBarHasNoCardFor_MarksNothing_AndMovesNothing()
  {
    // The knob reads the bank only when its overlay opens, so a preset deleted from the touchscreen
    // since then is still a row on the knob — and the match is by id, so the bar marks nothing rather
    // than the card that slid into its position.
    var presets = EightFm();
    var cut = RenderBar(presets, active: "f1");
    var before = Reveals.Count;

    Publish(cut, KnobPreview(new[] { "f1", "f2", "gone", "f3" }, 2));

    Highlighted(cut).Should().BeEmpty();
    Reveals.Should().HaveCount(before);
  }

  [Fact]
  public void HudTraffic_ThatDoesNotChangeTheMirror_DoesNotReRenderTheBar()
  {
    // A volume knob turning publishes at up to 20 Hz. None of it concerns the bar.
    var cut = RenderBar(EightFm());
    var renders = cut.RenderCount;

    for (var v = 0; v < 10; v++)
    {
      Publish(cut, new EncoderHudDto { EncoderIndex = 0, Label = "VOLUME", Phase = "Value", VolumePercent = v });
    }

    cut.RenderCount.Should().Be(renders);
  }

  [Fact]
  public void MountingWhileTheKnobsListIsOpen_PlacesTheStripOnTheKnobsCard()
  {
    var presets = EightFm();
    _hud.Publish(KnobPreview(presets, 5));

    var cut = RenderBar(presets, active: "f1");

    Highlighted(cut).Should().Equal("f6");
    var reveal = Reveals.Should().ContainSingle().Subject;
    reveal.Arguments[1].Should().Be(5);
    reveal.Arguments[2].Should().Be(false, "the first placement is not an animation");
  }

  [Fact]
  public void Dispose_Unsubscribes_FromTheSingleton()
  {
    var cut = RenderBar(EightFm());
    var renders = cut.RenderCount;

    DisposeComponents();
    _hud.Publish(KnobPreview(EightFm(), 3));

    // Not the proof on its own: a handler left subscribed would still render nothing, because it
    // checks the disposed flag first. The backing-field check below is what proves the unsubscribe.
    cut.RenderCount.Should().Be(renders);
    typeof(EncoderHudService)
      .GetField(nameof(EncoderHudService.StateChanged), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
      .GetValue(_hud).Should().BeNull("the bar is the only subscriber in this fixture, and it has gone");
  }
}
