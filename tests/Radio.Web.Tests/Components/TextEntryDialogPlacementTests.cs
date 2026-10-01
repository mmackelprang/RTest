using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Components.Dialogs;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components;

/// <summary>
/// UI-24 — dialogs that hold a text field sit at the top of the screen, clear of the in-app keyboard.
///
/// <para>
/// The kiosk types with <c>js/virtual-keyboard.js</c>: GNOME's own on-screen keyboard never opens for
/// Chrome on this box (<c>docs/uat/2026-08-03-osk-wayland-viability/REPORT.md</c>). That keyboard is
/// fixed to the bottom of the screen, does not resize the page, and is 364 px tall (QWERTY) or 352
/// (numpad) — so on the 720 px panel everything below y ≈ 356 is hidden while it is up. Centred
/// dialogs put their field and buttons there; the owner found it on the Rename dialog.
/// </para>
///
/// <para>
/// bUnit computes no styles, so the placement itself is pinned here as stylesheet rules, the
/// hand-built overlays are checked for the shared classes (the panel's three in
/// <c>RadioControlPanelTests</c>, the Radio page's two below), and every <c>DialogService</c> call
/// site opening a dialog that contains a text field is checked for the shared CSS class — including
/// dialogs added later. The rendered geometry was measured in Chromium at 1920×720 (see
/// <c>docs/queue/UI-24.md</c>).
/// </para>
/// </summary>
public class TextEntryDialogPlacementTests : TestContext
{
  private static readonly Regex TextEntryTag =
    new(@"<(RadzenTextBox|RadzenTextArea|RadzenNumeric|RadzenPassword|RadzenMask|textarea|input(?![^>]*type=""(range|checkbox|radio|file|color|hidden|button|submit)""))\b",
      RegexOptions.IgnoreCase);

  [Fact]
  public void Css_TopAnchorsTheOverlayAndTheRadzenDialog_WithKioskSizedButtons()
  {
    var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Radio.Web", "wwwroot", "css", "design-system.css"));

    var overlay = Rule(css, @"\.kiosk-entry-overlay");
    overlay.Should().Contain("position: fixed");
    overlay.Should().Contain("align-items: flex-start", "the dialog hangs from the top, not the centre");
    overlay.Should().NotContain("align-items: center");
    overlay.Should().Contain("padding-top: var(--sp-6)");

    Rule(css, @"\.rz-dialog\.kiosk-entry-dialog").Should().Contain("top: var(--sp-6)",
      "Radzen centres .rz-dialog in its wrapper; a top offset is what moves it");

    Rule(css, @"\.kiosk-entry-actions \.rz-button").Should().Contain("min-height: 58px");
  }

  [Fact]
  public void Css_TheDeadMudBlazorKeyboardShiftIsGone()
  {
    // The old answer to this bug shifted `.mud-overlay .mud-paper` while the keyboard was up. Nothing
    // has rendered those classes since the move to Radzen, so it matched nothing — and looked like a
    // working fix to anyone reading the stylesheet.
    var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Radio.Web", "wwwroot", "css", "virtual-keyboard.css"));
    Regex.IsMatch(css, @"\.mud-paper\s*\{").Should().BeFalse();
  }

  [Fact]
  public void EveryDialogServiceDialogWithATextField_IsOpenedTopAnchored()
  {
    var root = RepoRoot();
    var components = Path.Combine(root, "src", "Radio.Web", "Components");

    // Dialog components that contain a text field, by file name.
    var textDialogs = Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories)
      .Where(f => TextEntryTag.IsMatch(File.ReadAllText(f)))
      .Select(Path.GetFileNameWithoutExtension)
      .ToHashSet(StringComparer.Ordinal);

    var openCall = new Regex(@"OpenAsync<(?:[\w.]+\.)?(?<name>\w+)>\((?<args>.*?)\);", RegexOptions.Singleline);
    var sites = new List<string>();
    var missing = new List<string>();
    foreach (var file in Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories))
    {
      foreach (Match m in openCall.Matches(File.ReadAllText(file)))
      {
        var name = m.Groups["name"].Value;
        if (!textDialogs.Contains(name))
        {
          continue;
        }
        sites.Add($"{Path.GetFileName(file)} → {name}");
        if (!m.Groups["args"].Value.Contains("KioskEntryDialog.CssClass", StringComparison.Ordinal))
        {
          missing.Add($"{Path.GetFileName(file)} → {name}");
        }
      }
    }

    // Today: the playlist save dialog and the file browser, from the queue panel and System Config.
    sites.Should().HaveCountGreaterThanOrEqualTo(3, "the scan must find the call sites it guards");
    missing.Should().BeEmpty("every dialog with a text field passes CssClass = KioskEntryDialog.CssClass");
    KioskEntryDialog.CssClass.Should().Be("kiosk-entry-dialog", "the class design-system.css §20a styles");
  }

  [Theory]
  [InlineData("_isFrequencyDialogOpen", "Set Frequency")]
  [InlineData("_isSavePresetDialogOpen", "Save Preset")]
  public void RadioPage_TextEntryDialogs_AreTopAnchored(string flag, string title)
  {
    Services.AddHermeticTestRig();
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl } })
      .Build();
    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddHttpClient<RadioApiService>();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddSingleton(sp => new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, sp.GetRequiredService<IConfiguration>(), transport: new OfflineHubTransport()));

    var cut = RenderComponent<RadioPage>();
    // Opened by flag: the hermetic rig has no radio state, so the page never shows the controls
    // that open these. Both dialogs render from the flag alone.
    typeof(RadioPage).GetField(flag, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, true);
    cut.Render();

    var overlay = cut.FindAll(".kiosk-entry-overlay").Should().ContainSingle().Subject;
    overlay.GetAttribute("style").Should().BeNull();
    var card = overlay.QuerySelector(":scope > .kiosk-entry-card")!;
    card.QuerySelector("h6")!.TextContent.Trim().Should().Be(title);
    card.QuerySelector("input").Should().NotBeNull();
    card.QuerySelectorAll(".kiosk-entry-actions > button").Should().HaveCount(2);
  }

  /// <summary>The body of the first rule whose selector is exactly <paramref name="selector"/> at the start of a line.</summary>
  private static string Rule(string css, string selector)
  {
    var m = Regex.Match(css, @"(?m)^" + selector + @"\s*\{(?<body>[^}]*)\}");
    m.Success.Should().BeTrue($"design-system.css must declare {selector}");
    return m.Groups["body"].Value;
  }

  private static string RepoRoot()
  {
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 10 && dir != null; i++)
    {
      if (File.Exists(Path.Combine(dir, "RadioConsole.sln")))
      {
        return dir;
      }
      dir = Path.GetDirectoryName(dir);
    }
    throw new FileNotFoundException("RadioConsole.sln not found by walking up from test base dir");
  }
}
