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
/// fixed to the bottom of the screen and does not resize the page; measured in Chromium at 1920×720 its
/// top edge is at y = 348 (QWERTY) or y = 360 (numpad), and everything below is hidden while it is up. Centred
/// dialogs put their field and buttons there; the owner found it on the Rename dialog.
/// </para>
///
/// <para>
/// bUnit computes no styles, so the placement itself is pinned here as stylesheet rules, and the
/// hand-built overlays are checked for the shared classes (the panel's three in
/// <c>RadioControlPanelTests</c>, the Radio page's two below). The rendered geometry was measured in
/// Chromium at 1920×720 (see <c>docs/queue/UI-24.md</c>).
/// </para>
///
/// <para>
/// <see cref="EveryDialogServiceDialogWithATextField_IsOpenedTopAnchored"/> is a source scan, and it
/// is exactly as wide as this: <c>.razor</c> and <c>.cs</c> files under <c>src/Radio.Web</c>; calls
/// to <c>Open</c>, <c>OpenAsync</c>, <c>OpenSide</c> and <c>OpenSideAsync</c> with a component type
/// argument; a dialog counts as holding a text field if its own markup, or a component it renders
/// (followed by name, transitively), contains one of <see cref="TextEntryTag"/>'s tags. A
/// non-generic open (a <c>RenderFragment</c> lambda) cannot be followed, so it fails the scan unless
/// it passes the class. It does not see a text field supplied as a <c>RenderFragment</c> parameter, or
/// a call through some other wrapper around <c>DialogService</c>.
/// </para>
/// </summary>
public class TextEntryDialogPlacementTests : TestContext
{
  private static readonly Regex TextEntryTag =
    new(@"<(RadzenTextBox|RadzenTextArea|RadzenNumeric|RadzenPassword|RadzenMask|RadzenAutoComplete|InputText|InputTextArea|InputNumber|textarea|input(?![^>]*type=""(range|checkbox|radio|file|color|hidden|button|submit)""))\b",
      RegexOptions.IgnoreCase);

  private static readonly Regex GenericOpen =
    new(@"\b(?:Open|OpenAsync|OpenSide|OpenSideAsync)<(?:[\w.]+\.)?(?<name>\w+)>\s*\((?<args>.*?)\);", RegexOptions.Singleline);

  private static readonly Regex NonGenericOpen =
    new(@"\bDialogService\.(?:Open|OpenAsync|OpenSide|OpenSideAsync)\s*\((?<args>.*?)\);", RegexOptions.Singleline);

  [Fact]
  public void Css_TopAnchorsTheOverlayAndTheRadzenDialog_WithKioskSizedButtons()
  {
    var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Radio.Web", "wwwroot", "css", "design-system.css"));

    var overlay = Rule(css, @"\.kiosk-entry-overlay");
    overlay.Should().Contain("position: fixed");
    overlay.Should().Contain("inset: 0");
    overlay.Should().Contain("z-index: 9999", "the old inline overlays' stacking, above the top bar's 1100");
    overlay.Should().Contain("align-items: flex-start", "the dialog hangs from the top, not the centre");
    overlay.Should().NotContain("align-items: center");
    overlay.Should().Contain("padding-top: var(--sp-6)");

    var radzen = Rule(css, @"\.rz-dialog\.kiosk-entry-dialog");
    radzen.Should().Contain("top: var(--sp-6)", "Radzen centres .rz-dialog in its wrapper; a top offset is what moves it");
    radzen.Should().Contain("max-height: calc(100% - 2 * var(--sp-6))", "a tall dialog must not run off the bottom");
    // Radzen stacks its wrapper at 1001, under the fixed top bar (1100), which hid the anchored
    // dialog's title and close button. The wrapper is raised to the hand-built overlays' level.
    Rule(css, @"\.rz-dialog-wrapper:has\(> \.rz-dialog\.kiosk-entry-dialog\)").Should().Contain("z-index: 9999");

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
    var web = Path.Combine(RepoRoot(), "src", "Radio.Web");
    var sources = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
      .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
      .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
      .ToDictionary(f => f, File.ReadAllText);

    // Components holding a text field: directly, or by rendering one that does (to a fixed point).
    var razor = sources.Where(s => s.Key.EndsWith(".razor", StringComparison.Ordinal))
      .ToDictionary(s => Path.GetFileNameWithoutExtension(s.Key), s => s.Value, StringComparer.Ordinal);
    var textBearing = razor.Where(r => TextEntryTag.IsMatch(r.Value)).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
    for (var grew = true; grew;)
    {
      grew = false;
      foreach (var (name, markup) in razor)
      {
        if (!textBearing.Contains(name) && textBearing.Any(t => Regex.IsMatch(markup, $@"<{t}\b")))
        {
          grew = textBearing.Add(name);
        }
      }
    }

    var sites = new List<string>();
    var missing = new List<string>();
    foreach (var (file, text) in sources)
    {
      foreach (Match m in GenericOpen.Matches(text))
      {
        var name = m.Groups["name"].Value;
        if (!textBearing.Contains(name))
        {
          continue;
        }
        sites.Add($"{Path.GetFileName(file)} → {name}");
        if (!m.Groups["args"].Value.Contains("KioskEntryDialog.CssClass", StringComparison.Ordinal))
        {
          missing.Add($"{Path.GetFileName(file)} → {name}");
        }
      }
      foreach (Match m in NonGenericOpen.Matches(text))
      {
        if (!m.Groups["args"].Value.Contains("KioskEntryDialog.CssClass", StringComparison.Ordinal))
        {
          missing.Add($"{Path.GetFileName(file)} → non-generic open (its content cannot be followed; pass the class or extend this scan)");
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
