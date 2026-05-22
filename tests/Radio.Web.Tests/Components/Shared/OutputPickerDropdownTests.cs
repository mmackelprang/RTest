using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="OutputPickerDropdown"/> — the topbar "Out" pill's
/// popover-style picker introduced to replace the legacy /devices-page nav stub.
///
/// The component is intentionally presentational: the parent
/// (<c>MainLayout</c>) owns the API call and the "available outputs" list.
/// These tests verify the contracts the parent relies on:
///
/// <list type="bullet">
///   <item>Closed renders nothing — no row markup leaks into the DOM.</item>
///   <item>Open lists every non-Cast output (Cast is filtered: it has its own
///         popover).</item>
///   <item>Current selection gets the <c>is-active</c> class and a checkmark.</item>
///   <item>Clicking a row invokes <c>OnOutputSelected</c> with the chosen DTO
///         and closes the popover via <c>IsOpenChanged(false)</c>.</item>
///   <item>Clicking the click-away overlay closes the popover.</item>
/// </list>
///
/// Radzen components use JS interop for some icon/sizing behaviour, so
/// <see cref="JSRuntimeMode.Loose"/> is required — same pattern MEMORY documents
/// for MudBlazor tests.
/// </summary>
public class OutputPickerDropdownTests : TestContext
{
  public OutputPickerDropdownTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  /// <summary>
  /// When <c>IsOpen</c> is false, the popover root and click-away overlay must
  /// not render — otherwise stray overlays would block taps elsewhere on the
  /// topbar.
  /// </summary>
  [Fact]
  public void Closed_RendersNothing()
  {
    var cut = RenderComponent<OutputPickerDropdown>(p => p
      .Add(c => c.IsOpen, false)
      .Add(c => c.AvailableOutputs, BuildSampleOutputs()));

    Assert.Empty(cut.FindAll(".output-picker-popover"));
    Assert.Empty(cut.FindAll(".output-picker-row"));
  }

  /// <summary>
  /// Opening the picker must list every available output except the virtual
  /// <c>google-cast</c> sentinel (Cast lives in CastDeviceDropdown, not here).
  /// </summary>
  [Fact]
  public void Open_ListsNonCastOutputsOnly()
  {
    var outputs = BuildSampleOutputs();
    var cut = RenderComponent<OutputPickerDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.AvailableOutputs, outputs));

    var rows = cut.FindAll(".output-picker-row");
    // outputs contains 3 entries: local-1, usb-1, google-cast — Cast filtered out.
    Assert.Equal(2, rows.Count);

    var text = string.Join(" ", rows.Select(r => r.TextContent));
    Assert.Contains("Built-in Audio", text);
    Assert.Contains("USB Audio Device", text);
    Assert.DoesNotContain("Google Cast", text);
  }

  /// <summary>
  /// The row whose id matches <c>CurrentOutputId</c> should pick up the
  /// <c>is-active</c> class and render a check glyph so users see which
  /// output is live.
  /// </summary>
  [Fact]
  public void CurrentSelection_GetsActiveClassAndCheckmark()
  {
    var outputs = BuildSampleOutputs();
    var cut = RenderComponent<OutputPickerDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.AvailableOutputs, outputs)
      .Add(c => c.CurrentOutputId, "usb-1"));

    var active = cut.FindAll(".output-picker-row.is-active");
    Assert.Single(active);
    Assert.Contains("USB Audio Device", active[0].TextContent);

    var checks = cut.FindAll(".output-picker-checkmark");
    Assert.Single(checks);
  }

  /// <summary>
  /// Clicking a row must invoke <c>OnOutputSelected</c> with the chosen DTO
  /// and ask the parent to close the popover via <c>IsOpenChanged(false)</c>.
  /// </summary>
  [Fact]
  public void OutputSelected_InvokesCallbackAndCloses()
  {
    var outputs = BuildSampleOutputs();
    AudioDeviceDto? selected = null;
    var openStates = new List<bool>();

    var cut = RenderComponent<OutputPickerDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.AvailableOutputs, outputs)
      .Add(c => c.OnOutputSelected, EventCallback.Factory.Create<AudioDeviceDto>(
        this, d => selected = d))
      .Add(c => c.IsOpenChanged, EventCallback.Factory.Create<bool>(
        this, v => openStates.Add(v))));

    var rows = cut.FindAll(".output-picker-row");
    // Click the first non-Cast row (Built-in Audio).
    rows[0].Click();

    Assert.NotNull(selected);
    Assert.Equal("local-1", selected!.Id);
    // The popover must request close (false) at least once.
    Assert.Contains(false, openStates);
  }

  /// <summary>
  /// Clicking the click-away overlay must request the popover to close
  /// without firing <c>OnOutputSelected</c> — the user changed their mind.
  /// </summary>
  [Fact]
  public void ClickAway_ClosesPopover()
  {
    var outputs = BuildSampleOutputs();
    var selectedCount = 0;
    var openStates = new List<bool>();

    var cut = RenderComponent<OutputPickerDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.AvailableOutputs, outputs)
      .Add(c => c.OnOutputSelected, EventCallback.Factory.Create<AudioDeviceDto>(
        this, _ => selectedCount++))
      .Add(c => c.IsOpenChanged, EventCallback.Factory.Create<bool>(
        this, v => openStates.Add(v))));

    // The overlay is the first absolute-positioned div in the rendered output.
    // Per the component template, IsOpen=true emits the overlay before the
    // popover root. Find it via the fixed-inset style.
    var overlay = cut.FindAll("div").First(d =>
      d.GetAttribute("style")?.Contains("position: fixed") == true &&
      d.GetAttribute("style")?.Contains("inset: 0") == true);
    overlay.Click();

    Assert.Equal(0, selectedCount);
    Assert.Contains(false, openStates);
  }

  private static List<AudioDeviceDto> BuildSampleOutputs() => new()
  {
    new AudioDeviceDto { Id = "local-1", Name = "Built-in Audio", Type = "Playback" },
    new AudioDeviceDto { Id = "usb-1", Name = "USB Audio Device", Type = "Playback", IsUSBDevice = true, USBPort = "usb-0:1" },
    new AudioDeviceDto { Id = "google-cast", Name = "Google Cast", Type = "Cast" },
  };
}
