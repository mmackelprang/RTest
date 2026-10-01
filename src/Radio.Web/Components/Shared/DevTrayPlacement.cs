namespace Radio.Web.Components.Shared;

/// <summary>
/// Where the <see cref="DevTray"/> opens (UI-25). The tray appears under the point the operator
/// triple-tapped, centred on it horizontally, and is kept inside the viewport with a margin. When
/// there is no room below the tap it opens above it instead; when there is room on neither side it
/// is clamped on-screen below the tap and may cover the tap point.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="Top"/> / <see cref="Bottom"/> is set. Opening above is expressed as a
/// <c>bottom</c> offset rather than a <c>top</c> one so the tray's lower edge sits just above the
/// tap whatever height its content actually renders at — it is laid out against
/// <see cref="MaxHeightPx"/>, which is the most it can be, not what it is.
/// </remarks>
public sealed record DevTrayPlacement(double Left, double? Top, double? Bottom)
{
  /// <summary>Tray width. Emitted inline by <see cref="DevTray"/>, so this is the only definition.</summary>
  public const double WidthPx = 480;

  /// <summary>
  /// The tallest the open tray may be; content beyond it scrolls. Emitted inline by
  /// <see cref="DevTray"/> as the open <c>max-height</c>, so the flip decision and the rendered
  /// limit cannot disagree.
  /// </summary>
  public const double MaxHeightPx = 440;

  /// <summary>Minimum distance kept between the tray and every viewport edge.</summary>
  public const double MarginPx = 12;

  /// <summary>Space between the tap point and the tray's near edge.</summary>
  public const double GapPx = 12;

  // The kiosk panel. Used only when the browser reports a nonsensical viewport.
  private const double FallbackViewportWidth = 1920;
  private const double FallbackViewportHeight = 720;

  /// <summary>True when the tray opens above the tap point.</summary>
  public bool OpensAbove => Bottom.HasValue;

  /// <summary>Computes the placement for a tap at (<paramref name="tapX"/>, <paramref name="tapY"/>), in CSS pixels.</summary>
  public static DevTrayPlacement ForTap(double tapX, double tapY, double viewportWidth, double viewportHeight)
  {
    var vw = IsUsable(viewportWidth) ? viewportWidth : FallbackViewportWidth;
    var vh = IsUsable(viewportHeight) ? viewportHeight : FallbackViewportHeight;
    var x = double.IsFinite(tapX) ? tapX : vw / 2;
    var y = double.IsFinite(tapY) ? tapY : 0;

    // Centre on the tap, then clamp. Max is applied last so that on a viewport narrower than the
    // tray the left margin wins and the tray overflows to the right rather than off the left edge.
    var left = Math.Max(MarginPx, Math.Min(x - WidthPx / 2, vw - MarginPx - WidthPx));

    var below = y + GapPx;
    if (below + MaxHeightPx <= vh - MarginPx)
    {
      return new DevTrayPlacement(left, below, null);
    }

    var aboveEdge = y - GapPx;
    if (aboveEdge - MaxHeightPx >= MarginPx)
    {
      return new DevTrayPlacement(left, null, vh - aboveEdge);
    }

    // Fits on neither side: keep it on-screen below the tap, as high as the bottom margin allows.
    return new DevTrayPlacement(left, Math.Max(MarginPx, Math.Min(below, vh - MarginPx - MaxHeightPx)), null);
  }

  private static bool IsUsable(double length) => double.IsFinite(length) && length > 0;
}
