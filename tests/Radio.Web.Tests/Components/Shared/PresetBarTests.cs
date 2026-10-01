using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="PresetBar"/> — UI-20, spec 2026-10-01 §4.5.
///
/// <para>
/// The component decides and <c>preset-bar.js</c> measures and scrolls, so these tests stand in
/// for the browser at the seam: they play its reports in through <see cref="PresetBar.OnViewportChanged"/>
/// and <see cref="PresetBar.OnManualScroll"/>, and read what the component asked it to do from the JS
/// module's recorded <c>page</c> / <c>reveal</c> invocations.
/// </para>
///
/// <para>
/// Timing follows CLAUDE.md § Test Timing: the arrow auto-repeat and the 10 s manual-scroll hold run
/// on a <see cref="FakeTimeProvider"/>, advanced by the component's own constants, so nothing here
/// races a wall clock. Advancing the fake clock fires the repeat timer synchronously; the tick then
/// dispatches onto the renderer, which <c>WaitForAssertion</c> waits out — a rendezvous on the
/// observation, not a sleep.
/// </para>
/// </summary>
public class PresetBarTests : TestContext
{
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
  private readonly BunitJSModuleInterop _module;

  public PresetBarTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
    _module = JSInterop.SetupModule("./js/preset-bar.js");
    _module.Mode = JSRuntimeMode.Loose;
  }

  private static RadioPresetDto P(string id, string band, int slot, double freq = 92_300_000) =>
    new(id, $"Station {id}", freq, band, DateTimeOffset.UnixEpoch.AddMinutes(slot), slot);

  /// <summary>Eight FM presets, slots 1–8, ids f1..f8.</summary>
  private static List<RadioPresetDto> EightFm() =>
    Enumerable.Range(1, 8).Select(i => P($"f{i}", "FM", i, 88_100_000 + (i * 1_000_000))).ToList();

  private IRenderedComponent<PresetBar> RenderBar(
    IReadOnlyList<RadioPresetDto>? presets,
    string? active = null,
    string band = "FM",
    bool showSave = false,
    Action<ComponentParameterCollectionBuilder<PresetBar>>? extra = null)
  {
    return RenderComponent<PresetBar>(p =>
    {
      p.Add(x => x.Presets, presets)
       .Add(x => x.CurrentBand, band)
       .Add(x => x.ActivePresetId, active)
       .Add(x => x.ShowSavePlaceholder, showSave)
       .Add(x => x.SavePlaceholderLabel, $"{band} 92.30")
       .Add(x => x.Clock, _clock);
      extra?.Invoke(p);
    });
  }

  private IReadOnlyList<JSRuntimeInvocation> Calls(string identifier) => _module.Invocations[identifier];

  private static void Report(IRenderedComponent<PresetBar> cut, int first, int last, bool atStart, bool atEnd) =>
    cut.InvokeAsync(() => cut.Instance.OnViewportChanged(first, last, atStart, atEnd)).GetAwaiter().GetResult();

  private static string? Disabled(IRenderedComponent<PresetBar> cut, string arrow) =>
    cut.Find($".rcp-bar-{arrow}").GetAttribute("aria-disabled");

  // ── Arrows ───────────────────────────────────────────────────────────────────────────

  [Fact]
  public void Arrows_BothDisabled_WithFourOrFewerPresets_ButStillRendered()
  {
    var cut = RenderBar(EightFm().Take(4).ToList());

    Disabled(cut, "prev").Should().Be("true");
    Disabled(cut, "next").Should().Be("true");
    cut.FindAll(".rcp-bar-arrow.is-disabled").Should().HaveCount(2, "disabled arrows stay in place so nothing reflows");
  }

  [Fact]
  public void Arrows_PrevDisabledAtTheStart_NextDisabledAtTheEnd()
  {
    var cut = RenderBar(EightFm());
    Disabled(cut, "prev").Should().Be("true");
    Disabled(cut, "next").Should().Be("false");

    Report(cut, 2, 5, atStart: false, atEnd: false);
    Disabled(cut, "prev").Should().Be("false");
    Disabled(cut, "next").Should().Be("false");

    Report(cut, 4, 7, atStart: false, atEnd: true);
    Disabled(cut, "prev").Should().Be("false");
    Disabled(cut, "next").Should().Be("true");
  }

  [Fact]
  public void Arrow_Press_PagesOnce_ThenHoldRepeatsAfter600ms_Every400ms_UntilReleased()
  {
    var cut = RenderBar(EightFm());
    var next = cut.Find(".rcp-bar-next");

    next.PointerDown();
    Calls("page").Should().ContainSingle("a press pages once at once");
    Calls("page")[0].Arguments[1].Should().Be(1);

    _clock.Advance(PresetBar.RepeatDelay - TimeSpan.FromMilliseconds(1));
    Calls("page").Should().HaveCount(1, "no repeat before the hold threshold");

    _clock.Advance(TimeSpan.FromMilliseconds(1));
    cut.WaitForAssertion(() => Calls("page").Should().HaveCount(2));

    _clock.Advance(PresetBar.RepeatInterval);
    cut.WaitForAssertion(() => Calls("page").Should().HaveCount(3));

    cut.Find(".rcp-bar-next").PointerUp();
    _clock.Advance(PresetBar.RepeatInterval * 5);
    Calls("page").Should().HaveCount(3, "releasing the arrow stops the repeat");
  }

  [Theory]
  [InlineData("pointerleave")]
  [InlineData("pointercancel")]
  public void Arrow_Repeat_StopsWhenThePointerLeavesOrIsCancelled(string how)
  {
    var cut = RenderBar(EightFm());
    cut.Find(".rcp-bar-next").PointerDown();

    if (how == "pointerleave")
    {
      cut.Find(".rcp-bar-next").PointerLeave();
    }
    else
    {
      cut.Find(".rcp-bar-next").PointerCancel();
    }
    _clock.Advance(PresetBar.RepeatDelay + (PresetBar.RepeatInterval * 3));

    Calls("page").Should().ContainSingle();
  }

  [Fact]
  public void Arrow_Repeat_StopsAtTheEnd()
  {
    var cut = RenderBar(EightFm());
    cut.Find(".rcp-bar-next").PointerDown();
    Report(cut, 4, 7, atStart: false, atEnd: true);

    _clock.Advance(PresetBar.RepeatDelay + (PresetBar.RepeatInterval * 3));

    Calls("page").Should().ContainSingle("the strip reported it is at the end, so holding further pages nothing");
  }

  [Fact]
  public void DisabledArrow_PressDoesNothing()
  {
    var cut = RenderBar(EightFm());

    cut.Find(".rcp-bar-prev").PointerDown();
    _clock.Advance(PresetBar.RepeatDelay + PresetBar.RepeatInterval);

    Calls("page").Should().BeEmpty();
  }

  [Fact]
  public void Arrow_KeyboardActivation_Pages_ButThePointerClickAfterAPressDoesNot()
  {
    var cut = RenderBar(EightFm());

    cut.Find(".rcp-bar-next").Click(new MouseEventArgs { Detail = 1 });
    Calls("page").Should().BeEmpty("a pointer click follows a pointerdown that already paged");

    cut.Find(".rcp-bar-next").Click(new MouseEventArgs { Detail = 0 });
    Calls("page").Should().ContainSingle("Enter / Space on a focused arrow arrives as a click with detail 0");
  }

  // ── Caption ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public void Caption_ShowsHoldHint_WhenEverythingFits()
  {
    var cut = RenderBar(EightFm().Take(3).ToList());

    cut.Find(".rcp-bar-caption .rcp-presets-count").TextContent.Trim().Should().Be("PRESETS · 3 saved");
    cut.Find(".rcp-bar-caption .rcp-presets-hint").TextContent.Should().Contain("HOLD").And.Contain("FM");
    cut.FindAll(".rcp-bar-position").Should().BeEmpty();
  }

  [Fact]
  public void Caption_ShowsPosition_InsteadOfTheHint_WhenPaged()
  {
    var cut = RenderBar(EightFm());
    cut.Find(".rcp-bar-position").TextContent.Trim().Should().Be("1–4 of 8");
    cut.FindAll(".rcp-bar-caption .rcp-presets-hint").Should().BeEmpty();

    Report(cut, 3, 6, atStart: false, atEnd: false);
    cut.Find(".rcp-bar-position").TextContent.Trim().Should().Be("4–7 of 8");
  }

  [Fact]
  public void Caption_Position_CountsPresetsOnly_NotTheSaveCard()
  {
    // f1 f2 f3 | SAVE(FM) | w1 w2 — the SAVE card is item 3 but not a preset.
    var presets = new List<RadioPresetDto> { P("f1", "FM", 1), P("f2", "FM", 2), P("f3", "FM", 3), P("w1", "WB", 1), P("w2", "WB", 2) };
    var cut = RenderBar(PresetBar.OrderLikeKnob(presets), showSave: true);

    Report(cut, 2, 5, atStart: false, atEnd: true);

    cut.Find(".rcp-bar-position").TextContent.Trim().Should().Be("3–5 of 5");
  }

  // ── Keeping the playing preset in view ───────────────────────────────────────────────

  [Fact]
  public void FirstRender_RevealsThePlayingPreset_Instantly()
  {
    RenderBar(EightFm(), active: "f7");

    var reveal = Calls("reveal").Should().ContainSingle().Subject;
    reveal.Arguments[1].Should().Be(6);
    reveal.Arguments[2].Should().Be(false, "the first placement is not an animation");
  }

  [Fact]
  public void ActivePresetChange_RevealsIt_Smoothly()
  {
    var cut = RenderBar(EightFm(), active: "f1");

    cut.SetParametersAndRender(p => p.Add(x => x.ActivePresetId, "f6"));

    var reveal = Calls("reveal").Last();
    reveal.Arguments[1].Should().Be(5);
    reveal.Arguments[2].Should().Be(true);
  }

  [Fact]
  public void ActivePresetChange_WithinTenSecondsOfAManualScroll_DoesNotPullTheStripAway()
  {
    var cut = RenderBar(EightFm(), active: "f1");
    var before = Calls("reveal").Count;

    cut.InvokeAsync(() => cut.Instance.OnManualScroll());
    _clock.Advance(PresetBar.ManualScrollHold - TimeSpan.FromMilliseconds(1));
    cut.SetParametersAndRender(p => p.Add(x => x.ActivePresetId, "f6"));
    Calls("reveal").Should().HaveCount(before, "browsing must not be pulled away by a tune");

    _clock.Advance(TimeSpan.FromMilliseconds(1));
    cut.SetParametersAndRender(p => p.Add(x => x.ActivePresetId, "f8"));
    Calls("reveal").Should().HaveCount(before + 1);
    Calls("reveal").Last().Arguments[1].Should().Be(7);
  }

  [Fact]
  public void ArrowTap_CountsAsManual_ForTheTenSecondHold()
  {
    var cut = RenderBar(EightFm(), active: "f1");
    var before = Calls("reveal").Count;

    cut.Find(".rcp-bar-next").PointerDown();
    cut.Find(".rcp-bar-next").PointerUp();
    cut.SetParametersAndRender(p => p.Add(x => x.ActivePresetId, "f6"));

    Calls("reveal").Should().HaveCount(before);
  }

  [Fact]
  public void BandChange_ScrollsToTheNewBandsPlayingPreset_EvenDuringTheManualHold()
  {
    var presets = PresetBar.OrderLikeKnob(new[] { P("a1", "AM", 1), P("f1", "FM", 1), P("f2", "FM", 2), P("w1", "WB", 1), P("w2", "WB", 2) });
    var cut = RenderBar(presets, active: "f1", band: "FM");
    cut.InvokeAsync(() => cut.Instance.OnManualScroll());

    cut.SetParametersAndRender(p => p.Add(x => x.CurrentBand, "WB").Add(x => x.ActivePresetId, "w2"));

    var reveal = Calls("reveal").Last();
    reveal.Arguments[1].Should().Be(4, "w2 is the fifth card: a1 f1 f2 w1 w2");
    reveal.Arguments[2].Should().Be(true);
  }

  [Fact]
  public void BandChange_WithNothingPlaying_ScrollsToTheBandsFirstCard()
  {
    var presets = PresetBar.OrderLikeKnob(new[] { P("a1", "AM", 1), P("f1", "FM", 1), P("f2", "FM", 2), P("w1", "WB", 1), P("w2", "WB", 2) });
    var cut = RenderBar(presets, band: "AM");

    cut.SetParametersAndRender(p => p.Add(x => x.CurrentBand, "WB"));

    Calls("reveal").Last().Arguments[1].Should().Be(3);
  }

  [Fact]
  public void BandChange_ToABandWithNoPresets_ScrollsToItsSaveCard()
  {
    // Groups are AM, FM, WB and VHF has none, so its SAVE card goes where a VHF group would
    // sort: ordinally "VHF" < "WB", so between FM and WB — a1 f1 SAVE w1.
    var presets = PresetBar.OrderLikeKnob(new[] { P("a1", "AM", 1), P("f1", "FM", 1), P("w1", "WB", 1) });
    var cut = RenderBar(presets, band: "FM");

    cut.SetParametersAndRender(p => p.Add(x => x.CurrentBand, "VHF").Add(x => x.ShowSavePlaceholder, true));

    var save = cut.Find(".rcp-bar-save").ParentElement!;
    save.GetAttribute("data-item").Should().Be("2");
    Calls("reveal").Last().Arguments[1].Should().Be(2);
  }

  // ── The off-screen dot ───────────────────────────────────────────────────────────────

  [Fact]
  public void PlayingPresetOffScreenToTheRight_PutsTheDotOnTheNextArrow()
  {
    var cut = RenderBar(EightFm(), active: "f7");
    Report(cut, 0, 3, atStart: true, atEnd: false);

    cut.FindAll(".rcp-bar-prev .rcp-bar-dot").Should().BeEmpty();
    cut.FindAll(".rcp-bar-next .rcp-bar-dot").Should().ContainSingle();
    var describedBy = cut.Find(".rcp-bar-next").GetAttribute("aria-describedby");
    describedBy.Should().NotBeNullOrEmpty();
    cut.Find($"#{describedBy}").TextContent.Trim().Should().Be("Playing preset is to the right");
  }

  [Fact]
  public void PlayingPresetOffScreenToTheLeft_PutsTheDotOnThePrevArrow()
  {
    var cut = RenderBar(EightFm(), active: "f1");
    Report(cut, 4, 7, atStart: false, atEnd: true);

    cut.FindAll(".rcp-bar-prev .rcp-bar-dot").Should().ContainSingle();
    cut.FindAll(".rcp-bar-next .rcp-bar-dot").Should().BeEmpty();
    var describedBy = cut.Find(".rcp-bar-prev").GetAttribute("aria-describedby");
    cut.Find($"#{describedBy}").TextContent.Trim().Should().Be("Playing preset is to the left");
  }

  [Fact]
  public void PlayingPresetOnScreen_OrNothingPlaying_NoDot()
  {
    var cut = RenderBar(EightFm(), active: "f3");
    Report(cut, 0, 3, atStart: true, atEnd: false);
    cut.FindAll(".rcp-bar-dot").Should().BeEmpty();

    cut.SetParametersAndRender(p => p.Add(x => x.ActivePresetId, null));
    Report(cut, 4, 7, atStart: false, atEnd: true);
    cut.FindAll(".rcp-bar-dot").Should().BeEmpty();
    cut.FindAll(".rcp-bar-arrow[aria-describedby]").Should().BeEmpty();
  }

  // ── Cards, groups, the save card, states ─────────────────────────────────────────────

  [Fact]
  public void BandGroups_MarkTheFirstCardOfEachBandAfterTheFirst()
  {
    var presets = PresetBar.OrderLikeKnob(new[] { P("a1", "AM", 1), P("f1", "FM", 1), P("f2", "FM", 2), P("w1", "WB", 1) });
    var cut = RenderBar(presets);

    var starts = cut.FindAll(".rcp-bar-item.is-group-start")
      .Select(e => e.QuerySelector(".rcp-bar-card")!.GetAttribute("data-preset-id")).ToList();
    starts.Should().Equal("f1", "w1");
  }

  [Fact]
  public void SaveCard_SitsAtTheEndOfTheCurrentBandsGroup_AndRaisesOnSave()
  {
    var saved = 0;
    var presets = PresetBar.OrderLikeKnob(new[] { P("a1", "AM", 1), P("f1", "FM", 1), P("f2", "FM", 2), P("w1", "WB", 1) });
    var cut = RenderBar(presets, showSave: true, extra: p => p.Add(x => x.OnSave, () => { saved++; }));

    var order = cut.FindAll(".rcp-bar-viewport > [data-item]")
      .Select(e => e.QuerySelector(".rcp-bar-card")?.GetAttribute("data-preset-id") ?? "SAVE").ToList();
    order.Should().Equal("a1", "f1", "f2", "SAVE", "w1");
    cut.Find(".rcp-bar-save").TextContent.Should().Contain("＋ SAVE").And.Contain("FM 92.30");

    cut.Find(".rcp-bar-save").Click();
    saved.Should().Be(1);
  }

  [Fact]
  public void SaveCard_NotRendered_UnlessAskedFor()
  {
    var cut = RenderBar(EightFm(), showSave: false);
    cut.FindAll(".rcp-bar-save").Should().BeEmpty();
  }

  [Fact]
  public void List_HasRegionListAndListitemSemantics_AndArrowsControlTheList()
  {
    var cut = RenderBar(EightFm());

    cut.Find(".rcp-bar").GetAttribute("role").Should().Be("region");
    cut.Find(".rcp-bar").GetAttribute("aria-label").Should().Be("Presets");
    var list = cut.Find(".rcp-bar-viewport");
    list.GetAttribute("role").Should().Be("list");
    cut.FindAll(".rcp-bar-viewport > [role=listitem]").Should().HaveCount(8);
    cut.FindAll(".rcp-bar-arrow").Should().OnlyContain(a => a.GetAttribute("aria-controls") == list.Id);
    cut.FindAll(".rcp-bar-position[aria-live]").Should().BeEmpty("position text must not chatter while swiping");

    var hintId = cut.Find(".rcp-bar-card").GetAttribute("aria-describedby");
    cut.Find($"#{hintId}").TextContent.Trim().Should().Be("Hold for rename or delete");
  }

  [Fact]
  public void NoPresets_ShowsTheEmptyCard_ZeroCount_AndBothArrowsDisabled()
  {
    var cut = RenderBar(new List<RadioPresetDto>(), showSave: true);

    cut.Find(".rcp-bar-caption .rcp-presets-count").TextContent.Trim().Should().Be("PRESETS · 0 saved");
    cut.Find(".rcp-bar-msg.is-empty").TextContent.Should().Contain("NO STATIONS SAVED").And.Contain("hold FM");
    cut.FindAll(".rcp-bar-save").Should().ContainSingle();
    Disabled(cut, "prev").Should().Be("true");
    Disabled(cut, "next").Should().Be("true");
  }

  [Fact]
  public void LoadFailed_ShowsTheRetryCard_AndRaisesOnRetry()
  {
    var retries = 0;
    var cut = RenderBar(null, extra: p => p.Add(x => x.LoadFailed, true).Add(x => x.OnRetry, () => { retries++; }));

    cut.Find(".rcp-bar-caption .rcp-presets-count").TextContent.Trim().Should().Be("PRESETS");
    cut.FindAll(".rcp-bar-caption .rcp-presets-hint").Should().BeEmpty();
    cut.Markup.Should().NotContain("NO STATIONS SAVED");

    cut.Find(".rcp-bar-msg.is-error").Click();
    retries.Should().Be(1);
  }

  [Fact]
  public void Loading_ShowsFourGhostCards_AndNoListItems()
  {
    var cut = RenderBar(null);

    cut.FindAll(".rcp-bar-ghost").Should().HaveCount(PresetBar.VisibleCards);
    cut.FindAll("[role=listitem]").Should().BeEmpty();
    Calls("reveal").Should().BeEmpty();
  }

  // ── Order ────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void OrderLikeKnob_IsBandOrdinalThenSlot_ThenCreatedAt()
  {
    // The PRESETS knob composes band (StringComparer.Ordinal), then the per-band slot
    // (PresetSelectorService.cs:364-370). Ordinal puts "AIR" before "AM" and "VHF" before "WB".
    var presets = new[]
    {
      P("wb1", "WB", 1), P("vhf1", "VHF", 1), P("fm2", "FM", 2), P("am1", "AM", 1),
      P("fm1", "FM", 1), P("air1", "AIR", 1),
    };

    PresetBar.OrderLikeKnob(presets).Select(p => p.Id)
      .Should().Equal("air1", "am1", "fm1", "fm2", "vhf1", "wb1");
  }

  [Fact]
  public void Bar_RendersPresetsInTheOrderItIsGiven_NeverCurrentBandFirst()
  {
    // Spec §4.7: the current band is reached by scrolling, never by reordering.
    var presets = PresetBar.OrderLikeKnob(new[] { P("a1", "AM", 1), P("f1", "FM", 1), P("w1", "WB", 1) });
    var cut = RenderBar(presets, band: "WB");

    cut.FindAll(".rcp-bar-card").Select(e => e.GetAttribute("data-preset-id")).Should().Equal("a1", "f1", "w1");
  }

  [Fact]
  public void Dispose_StopsARunningRepeat()
  {
    var cut = RenderBar(EightFm());
    cut.Find(".rcp-bar-next").PointerDown();

    DisposeComponents();
    _clock.Advance(PresetBar.RepeatDelay + (PresetBar.RepeatInterval * 3));

    Calls("page").Should().ContainSingle();
  }
}
