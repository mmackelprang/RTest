using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the SourceBubble pill control introduced by PR 2 of the
/// design tightening arc. Asserts label/sub/icon rendering, chevron visibility
/// rules, click routing (body vs chevron stopPropagation), disabled-state
/// behaviour, and the contract that the data-source attribute is preserved
/// on the root element for CSS hooks / test selectors.
/// </summary>
public class SourceBubbleTests : TestContext
{
  public SourceBubbleTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void SourceBubble_RendersLabelSubAndIcon()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "radio")
      .Add(x => x.Label, "Radio")
      .Add(x => x.Sub, "92.5 FM")
      .Add(x => x.Accent, "--source-radio")
      .Add(x => x.DataSourceAttr, "radio"));

    var root = cut.Find("button.source-bubble");
    Assert.NotNull(root);
    Assert.Contains("Radio", cut.Find(".source-bubble-label").TextContent);
    Assert.Contains("92.5 FM", cut.Find(".source-bubble-sub").TextContent);
    // The chip wraps a RadzenIcon whose name was provided via Icon parameter.
    var chip = cut.Find(".source-bubble-chip");
    Assert.NotNull(chip);
  }

  [Fact]
  public void SourceBubble_DataSourceAttribute_IsPreserved_OnRoot()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "bluetooth")
      .Add(x => x.Label, "Bluetooth")
      .Add(x => x.Accent, "--source-bluetooth")
      .Add(x => x.DataSourceAttr, "bluetooth"));

    var root = cut.Find("button.source-bubble");
    Assert.Equal("bluetooth", root.GetAttribute("data-source"));
  }

  [Fact]
  public void SourceBubble_Active_WithDetail_RendersChevron()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "radio")
      .Add(x => x.Label, "Radio")
      .Add(x => x.Accent, "--source-radio")
      .Add(x => x.IsActive, true)
      .Add(x => x.HasDetail, true)
      .Add(x => x.DataSourceAttr, "radio"));

    var chevrons = cut.FindAll(".source-bubble-chevron");
    Assert.Single(chevrons);
  }

  [Fact]
  public void SourceBubble_Inactive_DoesNotRenderChevron_EvenWithDetail()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "radio")
      .Add(x => x.Label, "Radio")
      .Add(x => x.Accent, "--source-radio")
      .Add(x => x.IsActive, false)
      .Add(x => x.HasDetail, true)
      .Add(x => x.DataSourceAttr, "radio"));

    Assert.Empty(cut.FindAll(".source-bubble-chevron"));
  }

  [Fact]
  public void SourceBubble_Active_WithoutDetail_DoesNotRenderChevron()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "audio_file")
      .Add(x => x.Label, "File")
      .Add(x => x.Accent, "--source-file")
      .Add(x => x.IsActive, true)
      .Add(x => x.HasDetail, false)
      .Add(x => x.DataSourceAttr, "file"));

    Assert.Empty(cut.FindAll(".source-bubble-chevron"));
  }

  [Fact]
  public void SourceBubble_BodyClick_InvokesOnSwitch()
  {
    var switched = 0;
    var detailOpened = 0;
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "radio")
      .Add(x => x.Label, "Radio")
      .Add(x => x.Accent, "--source-radio")
      .Add(x => x.DataSourceAttr, "radio")
      .Add(x => x.OnSwitch, () => { switched++; })
      .Add(x => x.OnOpenDetail, () => { detailOpened++; }));

    cut.Find("button.source-bubble").Click();
    Assert.Equal(1, switched);
    Assert.Equal(0, detailOpened);
  }

  [Fact]
  public void SourceBubble_ChevronClick_InvokesOnOpenDetail_AndStopsPropagation()
  {
    var switched = 0;
    var detailOpened = 0;
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "radio")
      .Add(x => x.Label, "Radio")
      .Add(x => x.Accent, "--source-radio")
      .Add(x => x.IsActive, true)
      .Add(x => x.HasDetail, true)
      .Add(x => x.DataSourceAttr, "radio")
      .Add(x => x.OnSwitch, () => { switched++; })
      .Add(x => x.OnOpenDetail, () => { detailOpened++; }));

    cut.Find(".source-bubble-chevron").Click();
    Assert.Equal(1, detailOpened);
    // The chevron carries @onclick:stopPropagation, so the body's OnSwitch
    // handler must NOT have been invoked by this click.
    Assert.Equal(0, switched);
  }

  [Fact]
  public void SourceBubble_Disabled_BodyClick_DoesNotInvokeOnSwitch()
  {
    var switched = 0;
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "bluetooth")
      .Add(x => x.Label, "Bluetooth")
      .Add(x => x.Sub, "no device")
      .Add(x => x.Accent, "--source-bluetooth")
      .Add(x => x.IsDisabled, true)
      .Add(x => x.DataSourceAttr, "bluetooth")
      .Add(x => x.OnSwitch, () => { switched++; }));

    var root = cut.Find("button.source-bubble");
    Assert.True(root.HasAttribute("disabled"));
    // Even if bUnit lets the click through, the HandleBodyClick guard returns
    // Task.CompletedTask without invoking OnSwitch when IsDisabled.
    root.Click();
    Assert.Equal(0, switched);
  }

  [Fact]
  public void SourceBubble_Disabled_AppendsOfflineMarkerToSubLabel()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "bluetooth")
      .Add(x => x.Label, "Bluetooth")
      .Add(x => x.Sub, "no device")
      .Add(x => x.Accent, "--source-bluetooth")
      .Add(x => x.IsDisabled, true)
      .Add(x => x.DataSourceAttr, "bluetooth"));

    var sub = cut.Find(".source-bubble-sub").TextContent;
    Assert.Contains("offline", sub, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("no device", sub);
  }

  [Fact]
  public void SourceBubble_Disabled_StillShowsOfflineMarker_WhenSubIsEmpty()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "usb")
      .Add(x => x.Label, "USB")
      .Add(x => x.Accent, "--source-usb")
      .Add(x => x.IsDisabled, true)
      .Add(x => x.DataSourceAttr, "usb"));

    var subs = cut.FindAll(".source-bubble-sub");
    Assert.Single(subs);
    Assert.Contains("offline", subs[0].TextContent, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void SourceBubble_AriaLabel_IncludesSwitchToLabel()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "radio")
      .Add(x => x.Label, "Radio")
      .Add(x => x.Accent, "--source-radio")
      .Add(x => x.DataSourceAttr, "radio"));

    var root = cut.Find("button.source-bubble");
    Assert.Equal("Switch to Radio", root.GetAttribute("aria-label"));
  }

  [Fact]
  public void SourceBubble_ActiveBubble_HasAriaPressedTrue()
  {
    var cut = RenderComponent<SourceBubble>(p => p
      .Add(x => x.Icon, "radio")
      .Add(x => x.Label, "Radio")
      .Add(x => x.Accent, "--source-radio")
      .Add(x => x.IsActive, true)
      .Add(x => x.DataSourceAttr, "radio"));

    Assert.Equal("true", cut.Find("button.source-bubble").GetAttribute("aria-pressed"));
  }
}
