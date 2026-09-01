using System.Reflection;
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
///   <item>Header renders kicker + title + optional Auto pill.</item>
///   <item>Slider disabled while <c>IsAuto</c> is true; Reset disabled while Auto.</item>
///   <item>Slider value change fires <c>OnValueChanged</c>.</item>
///   <item>Peak meter segment count updates from <c>OnLevelData</c> hub pushes.</item>
///   <item>Auto pill click fires <c>OnAutoToggled</c>.</item>
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
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "RTLSDRCore"));

    var root = cut.Find(".gain-popover");
    Assert.Contains("is-open", root.ClassList);
  }

  [Fact]
  public void Popover_OmitsOpenClass_WhenIsOpenFalse()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, false)
      .Add(p => p.SourceType, "RTLSDRCore"));

    var root = cut.Find(".gain-popover");
    Assert.DoesNotContain("is-open", root.ClassList);
  }

  [Fact]
  public void Header_RendersKickerAndTitle()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "RTLSDRCore")
      .Add(p => p.SourceKicker, "SDR · RTL-SDR")
      .Add(p => p.Title, "RF gain"));

    Assert.Equal("SDR · RTL-SDR", cut.Find(".gain-popover-kicker").TextContent);
    Assert.Equal("RF gain", cut.Find(".gain-popover-title").TextContent);
  }

  [Fact]
  public void Header_OmitsKicker_WhenEmpty()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "FilePlayer")
      .Add(p => p.SourceKicker, string.Empty)
      .Add(p => p.Title, "Source gain"));

    Assert.Empty(cut.FindAll(".gain-popover-kicker"));
  }

  [Fact]
  public void AutoPill_Hidden_WhenShowAutoToggleFalse()
  {
    // File / Bluetooth sources don't expose AGC; the pill is omitted entirely.
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "FilePlayer")
      .Add(p => p.ShowAutoToggle, false));

    Assert.Empty(cut.FindAll(".gain-popover-auto"));
  }

  [Fact]
  public void AutoPill_RendersOnClass_WhenIsAuto()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "RTLSDRCore")
      .Add(p => p.ShowAutoToggle, true)
      .Add(p => p.IsAuto, true));

    var pill = cut.Find(".gain-popover-auto");
    Assert.Contains("is-on", pill.ClassList);
    Assert.Equal("Auto on", pill.TextContent.Trim());
  }

  [Fact]
  public void AutoPill_RendersOffClass_WhenNotAuto()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "RTLSDRCore")
      .Add(p => p.ShowAutoToggle, true)
      .Add(p => p.IsAuto, false));

    var pill = cut.Find(".gain-popover-auto");
    Assert.Contains("is-off", pill.ClassList);
    Assert.Equal("Auto off", pill.TextContent.Trim());
  }

  [Fact]
  public async Task AutoPill_Click_FiresOnAutoToggled()
  {
    var toggled = false;
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "RTLSDRCore")
      .Add(p => p.ShowAutoToggle, true)
      .Add(p => p.IsAuto, false)
      .Add(p => p.OnAutoToggled, () => { toggled = true; }));

    // Component handler is `async Task HandleAutoToggleAsync() => await
    // OnAutoToggled.InvokeAsync();` — `toggled` is set INSIDE the awaited
    // continuation. bUnit's sync Click() waits on the dispatch task, but on
    // slower CI runners the OnInitializedAsync hub-connect attempt can leave
    // the dispatcher queue non-empty, so the callback's continuation can lag
    // behind the assertion. Route the click through cut.InvokeAsync so the
    // full handler chain runs on the renderer's dispatcher and we await it.
    await cut.InvokeAsync(() => cut.Find(".gain-popover-auto").Click());

    Assert.True(toggled);
  }

  [Fact]
  public void Slider_DisabledAndDimmed_WhenIsAuto()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "RTLSDRCore")
      .Add(p => p.IsAuto, true));

    var sliderWrap = cut.Find(".gain-popover-slider");
    Assert.Contains("is-disabled", sliderWrap.ClassList);

    var input = cut.Find("input[type=range]");
    Assert.True(input.HasAttribute("disabled"));
  }

  [Fact]
  public async Task Slider_Input_FiresOnValueChanged_WhenNotAuto()
  {
    float? received = null;
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "FilePlayer")
      .Add(p => p.IsAuto, false)
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
  public void Reset_DisabledWhenAuto()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "RTLSDRCore")
      .Add(p => p.IsAuto, true));

    var reset = cut.Find(".gain-popover-reset");
    Assert.True(reset.HasAttribute("disabled"));
  }

  [Fact]
  public async Task Reset_Click_FiresOnResetAndOnValueChangedWithOne()
  {
    var resetFired = false;
    float? lastValue = null;
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "FilePlayer")
      .Add(p => p.IsAuto, false)
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
  public void Footer_ShowsAppliedGainDb_WhenAuto()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "RTLSDRCore")
      .Add(p => p.IsAuto, true)
      .Add(p => p.AppliedGainDb, 28.0)
      .Add(p => p.CurrentValue, 0.5f)); // would surface as -6dB but Auto suppresses it

    var value = cut.Find(".gain-popover-value").TextContent;
    Assert.Contains("28.0", value);
  }

  [Fact]
  public void Footer_ShowsSliderValueInDb_WhenManual()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "FilePlayer")
      .Add(p => p.IsAuto, false)
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
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "FilePlayer"));

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
      .Add(p => p.SourceType, "FilePlayer")
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
      .Add(p => p.SourceType, "FilePlayer")
      .Add(p => p.SegmentCount, 20));

    // Sanity: no segments lit before any level data arrives.
    Assert.Empty(cut.FindAll(".gain-popover-peak-segment.is-lit"));

    var hub = Services.GetRequiredService<AudioVisualizationHubService>();
    var eventField = typeof(AudioVisualizationHubService).GetField(
      nameof(AudioVisualizationHubService.OnLevelData),
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(eventField);
    var handler = (Func<LevelDataDto, Task>?)eventField!.GetValue(hub);
    Assert.NotNull(handler);

    // -12 dBFS → (−12 + 60) / 60 × 20 = 16 segments lit.
    var data = new LevelDataDto
    {
      LeftPeakDb = -12.0f,
      RightPeakDb = -18.0f,
      IsClipping = false
    };

    await cut.InvokeAsync(() => handler!.Invoke(data));

    var lit = cut.FindAll(".gain-popover-peak-segment.is-lit");
    Assert.Equal(16, lit.Count);
  }

  [Fact]
  public async Task PeakMeter_PicksLouderChannel()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "FilePlayer")
      .Add(p => p.SegmentCount, 20));

    var hub = Services.GetRequiredService<AudioVisualizationHubService>();
    var eventField = typeof(AudioVisualizationHubService).GetField(
      nameof(AudioVisualizationHubService.OnLevelData),
      BindingFlags.NonPublic | BindingFlags.Instance);
    var handler = (Func<LevelDataDto, Task>?)eventField!.GetValue(hub);
    Assert.NotNull(handler);

    // Right channel hotter than left — meter should follow right.
    var data = new LevelDataDto
    {
      LeftPeakDb = -36.0f,  // (−36 + 60) / 60 × 20 = 8
      RightPeakDb = -6.0f,  // (−6  + 60) / 60 × 20 = 18
      IsClipping = false
    };

    await cut.InvokeAsync(() => handler!.Invoke(data));

    var lit = cut.FindAll(".gain-popover-peak-segment.is-lit");
    Assert.Equal(18, lit.Count);
  }

  [Fact]
  public async Task PeakMeter_ClampsLevels_AtOrAboveZeroDbfs()
  {
    var cut = RenderComponent<GainControlPopover>(parameters => parameters
      .Add(p => p.IsOpen, true)
      .Add(p => p.SourceType, "FilePlayer")
      .Add(p => p.SegmentCount, 20));

    var hub = Services.GetRequiredService<AudioVisualizationHubService>();
    var eventField = typeof(AudioVisualizationHubService).GetField(
      nameof(AudioVisualizationHubService.OnLevelData),
      BindingFlags.NonPublic | BindingFlags.Instance);
    var handler = (Func<LevelDataDto, Task>?)eventField!.GetValue(hub);
    Assert.NotNull(handler);

    var data = new LevelDataDto
    {
      LeftPeakDb = 6.0f,    // pegged above 0
      RightPeakDb = 6.0f,
      IsClipping = true
    };

    await cut.InvokeAsync(() => handler!.Invoke(data));

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
