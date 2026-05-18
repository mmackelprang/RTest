namespace Radio.Web.Formatting;

/// <summary>
/// Color values for chart contexts where CSS variables can't be resolved
/// (e.g. inline canvas drawing, Plot.NET configs, JS-interop SVG attributes).
/// Mirrors the design-system signal tokens. Keep in sync with --signal-red /
/// --signal-amber / --accent-primary / --signal-green / --text-low in
/// <c>src/Radio.Web/wwwroot/css/design-system.css §2</c>.
/// </summary>
/// <remarks>
/// Extracted from MetricsDashboardPage.razor in PR A follow-up #7 so chart
/// code can't silently drift from the design tokens; previously each
/// dashboard cell carried its own inline hex literal.
/// </remarks>
public static class ChartColors
{
  /// <summary>Critical / error indicator. Mirrors <c>--signal-red</c>.</summary>
  public const string Red = "#F87171";

  /// <summary>Warning / scrub / radio source indicator. Mirrors <c>--signal-amber</c>.</summary>
  public const string Amber = "#F0A830";

  /// <summary>Primary accent / data series. Mirrors <c>--accent-primary</c>.</summary>
  public const string Cyan = "#5CD4E8";

  /// <summary>Healthy / OK indicator. Mirrors <c>--signal-green</c>.</summary>
  public const string Green = "#4ADE80";

  /// <summary>Muted axis / grid / low-emphasis text. Mirrors <c>--text-low</c>.</summary>
  public const string TextLow = "#4B5563";
}
