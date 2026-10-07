using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="GainControlPopover"/>.
///
/// The component is intentionally "dumb" — parent owns the API calls. These
/// tests verify the layout / state-flip behaviour the spec calls out:
///
/// <list type="bullet">
///   <item>Header renders kicker + title, and no AGC control (status-bar redesign, 2026-10-06).</item>
///   <item>Slider value change fires <c>OnValueChanged</c>.</item>
///   <item>Peak meter segment count updates from <c>OnLevelData</c> hub pushes.</item>
///   <item>Reset click fires <c>OnReset</c> AND <c>OnValueChanged</c> with 1.0.</item>
/// </list>
///
/// The hub-driven peak meter test mirrors the PR 2 wire-path regression pattern
/// — it grabs the real <see cref="AudioVisualizationHubService"/> in DI, reaches
/// in to the compiler-generated event backing field, and invokes the handler
/// directly. No SignalR transport required.
/// </summary>
public class GainControlPopoverTests : TestContext
{
  public GainControlPopoverTests()
  {
    // Hermetic rig: fails every outbound HTTP request and every SignalR
    // negotiate without touching the network, so this fixture's result never
    // depends on whether radio-api happens to be running locally.
    Services.AddHermeticTestRig();

    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddSingleton(sp =>
      new AudioVisualizationHubService(
        NullLogger<AudioVisualizationHubService>.Instance,
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()
      )
    );
  }

  [Fact]
  public void Popover_RendersOpenClass_WhenIsOpenTrue()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true));

    var root = cut.Find(".gain-popover");
    Assert.Contains("is-open", root.ClassList);
  }

  [Fact]
  public void Popover_OmitsOpenClass_WhenIsOpenFalse()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, false));

    var root = cut.Find(".gain-popover");
    Assert.DoesNotContain("is-open", root.ClassList);
  }

  [Fact]
  public void Header_RendersKickerAndTitle()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceKicker, "SDR · RTL-SDR")
      .Add(p => p.Title, "Source gain"));

    Assert.Equal("SDR · RTL-SDR", cut.Find(".gain-popover-kicker").TextContent);
    Assert.Equal("Source gain", cut.Find(".gain-popover-title").TextContent);
  }

  [Fact]
  public void Header_OmitsKicker_WhenEmpty()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceKicker, string.Empty)
      .Add(p => p.Title, "Source gain"));

    Assert.Empty(cut.FindAll(".gain-popover-kicker"));
  }

  [Fact]
  public void HasNoAgcControl_AndTheSliderAndResetAreAlwaysLive()
  {
    // Owner decision 4 (2026-10-06): the Auto pill toggled the tuner's RF AGC — a different control
    // from this playback-gain slider — and AGC now lives only in the radio panel's status bar. With
    // it went the AGC-on state that disabled the slider and Reset.
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceKicker, "SDR · RTL-SDR")
      .Add(p => p.Title, "Source gain"));

    Assert.Empty(cut.FindAll(".gain-popover-auto"));
    Assert.DoesNotContain("Auto on", cut.Markup);
    Assert.DoesNotContain("Auto off", cut.Markup);
    Assert.False(cut.Find("input[type=range]").HasAttribute("disabled"));
    Assert.False(cut.Find(".gain-popover-reset").HasAttribute("disabled"));
    Assert.DoesNotContain("is-disabled", cut.Find(".gain-popover-slider").ClassList);
  }

  [Fact]
  public async Task Slider_Input_FiresOnValueChanged()
  {
    float? received = null;
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.CurrentValue, 1.0f)
      .Add(p => p.OnValueChanged, (float v) => { received = v; }));

    // Component handler is `async Task HandleSliderInput(...)` that awaits
    // `OnValueChanged.InvokeAsync(...)` — `received` is set INSIDE the awaited
    // continuation. bUnit's sync Input() waits on the dispatch task, but on
    // slower CI runners the OnInitializedAsync hub-connect attempt can leave
    // the dispatcher queue non-empty, so the callback's continuation can lag
    // behind the assertion. Route the input through cut.InvokeAsync so the
    // full handler chain runs on the renderer's dispatcher and we await it.
    await cut.InvokeAsync(() => cut.Find("input[type=range]").Input("0.75"));

    Assert.NotNull(received);
    Assert.Equal(0.75f, received!.Value, precision: 2);
  }

  [Fact]
  public async Task Reset_Click_FiresOnResetAndOnValueChangedWithOne()
  {
    var resetFired = false;
    float? lastValue = null;
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.CurrentValue, 0.5f)
      .Add(p => p.OnReset, () => { resetFired = true; })
      .Add(p => p.OnValueChanged, (float v) => { lastValue = v; }));

    // Component handler is `async Task HandleResetAsync()` that awaits
    // `OnReset.InvokeAsync()` then `OnValueChanged.InvokeAsync(...)` — both
    // callbacks set their flags INSIDE awaited continuations. bUnit's sync
    // Click() waits on the dispatch task, but on slower CI runners the
    // OnInitializedAsync hub-connect attempt can leave the dispatcher queue
    // non-empty, so the callback continuations can lag behind the assertion.
    // Route the click through cut.InvokeAsync so the full handler chain runs
    // on the renderer's dispatcher and we await it.
    await cut.InvokeAsync(() => cut.Find(".gain-popover-reset").Click());

    Assert.True(resetFired);
    Assert.Equal(1.0f, lastValue);
  }

  [Fact]
  public void Footer_ShowsSliderValueInDb()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.CurrentValue, 1.0f));

    var value = cut.Find(".gain-popover-value").TextContent;
    // 20·log10(1.0) == 0 → "+0.0 dB"
    Assert.Contains("0.0", value);
    Assert.Contains("dB", value);
  }

  [Fact]
  public void Body_RendersScaleLabels()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true));

    var scale = cut.Find(".gain-popover-scale").TextContent;
    Assert.Contains("+6", scale);
    Assert.Contains("+3", scale);
    Assert.Contains("0", scale);
    Assert.Contains("−12", scale);
    Assert.Contains("−24", scale);
    Assert.Contains("−∞", scale);
  }

  [Fact]
  public void PeakMeter_RendersConfiguredSegmentCount()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SegmentCount, 20));

    Assert.Equal(20, cut.FindAll(".gain-popover-peak-segment").Count);
  }

  // ─── Hub wire-path regression ──────────────────────────────────────────────
  //
  // PR 2's Tester catch was that the recognition stream looked wired but didn't
  // actually consume the typed payload. Apply the same discipline here — fire
  // a real OnLevelData event through the real AudioVisualizationHubService and
  // assert the peak meter lights up. Without this test the popover could be
  // subscribed to the wrong event source and the visual would silently never
  // animate.

  [Fact]
  public async Task PeakMeter_LightsSegments_OnHubLevelData()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SegmentCount, 20));

    // Sanity: no segments lit before any level data arrives.
    Assert.Empty(cut.FindAll(".gain-popover-peak-segment.is-lit"));

    var hub = Services.GetRequiredService<AudioVisualizationHubService>();

    // -12 dBFS → (−12 + 60) / 60 × 20 = 16 segments lit.
    var data = new LevelDataDto
    {
      LeftPeakDb = -12.0f,
      RightPeakDb = -18.0f,
      IsClipping = false
    };

    await cut.InvokeAsync(() => HubEventFire.FireAsync(
      hub, nameof(AudioVisualizationHubService.OnLevelData), data));

    var lit = cut.FindAll(".gain-popover-peak-segment.is-lit");
    Assert.Equal(16, lit.Count);
  }

  [Fact]
  public async Task PeakMeter_PicksLouderChannel()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SegmentCount, 20));

    var hub = Services.GetRequiredService<AudioVisualizationHubService>();

    // Right channel hotter than left — meter should follow right.
    var data = new LevelDataDto
    {
      LeftPeakDb = -36.0f,  // (−36 + 60) / 60 × 20 = 8
      RightPeakDb = -6.0f,  // (−6  + 60) / 60 × 20 = 18
      IsClipping = false
    };

    await cut.InvokeAsync(() => HubEventFire.FireAsync(
      hub, nameof(AudioVisualizationHubService.OnLevelData), data));

    var lit = cut.FindAll(".gain-popover-peak-segment.is-lit");
    Assert.Equal(18, lit.Count);
  }

  [Fact]
  public async Task PeakMeter_ClampsLevels_AtOrAboveZeroDbfs()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SegmentCount, 20));

    var hub = Services.GetRequiredService<AudioVisualizationHubService>();

    var data = new LevelDataDto
    {
      LeftPeakDb = 6.0f,    // pegged above 0
      RightPeakDb = 6.0f,
      IsClipping = true
    };

    await cut.InvokeAsync(() => HubEventFire.FireAsync(
      hub, nameof(AudioVisualizationHubService.OnLevelData), data));

    var lit = cut.FindAll(".gain-popover-peak-segment.is-lit");
    Assert.Equal(20, lit.Count);
  }

  [Fact]
  public void FormatGainDb_Boundaries()
  {
    Assert.Equal("−∞ dB", GainControlPopover.FormatGainDb(0.0f));
    Assert.Equal("+0.0 dB", GainControlPopover.FormatGainDb(1.0f));
    // 20·log10(2) ≈ 6.02 → "+6.0 dB"
    Assert.StartsWith("+6.", GainControlPopover.FormatGainDb(2.0f));
    // 20·log10(0.5) ≈ −6.02 → "-6.0 dB"
    Assert.StartsWith("-6.", GainControlPopover.FormatGainDb(0.5f));
  }
}
