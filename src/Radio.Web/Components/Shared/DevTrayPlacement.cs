namespace Radio.Web.Components.Shared;

/// <summary>
/// Where the <see cref="DevTray"/> opens (UI-25): under the area that was pressed. Horizontally the
/// tray is centred on the tap's x; vertically it hangs <see cref="GapPx"/> below the pressed
/// element's bottom edge, so it never covers the control row the finger is on. It is clamped
/// <see cref="MarginPx"/> inside the viewport's side edges. When it does not fit below it opens above
/// the pressed element instead; when it fits on neither side it is pinned to the bottom margin.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="ForPress"/> constructs one, and it sets exactly one of <see cref="Top"/> /
/// <see cref="Bottom"/>. Opening above is a <c>bottom</c> offset rather than a <c>top</c> one so the
/// tray's lower edge sits just above the pressed element whatever height its content renders at — the
/// fit test uses <see cref="MaxHeightPx"/>, which is the most the tray can be, not what it is.
/// </para>
/// <para>
/// ⚠ On the kiosk the only gesture target sits in the 64 px top row, so the tray always opens below
/// it and the flip-above branch is not reachable there today. It is kept, and tested, so moving the
/// target cannot push the tray off the bottom of the screen.
/// </para>
/// </remarks>
public sealed record DevTrayPlacement
{
  /// <summary>Tray width. <see cref="DevTray"/> writes it inline; the stylesheet does not set one.</summary>
  public const double WidthPx = 480;

  /// <summary>
  /// The tallest the open tray may be; content beyond it scrolls. <see cref="DevTray"/> writes it inline
  /// as the open <c>max-height</c>, so the fit test here and the rendered limit cannot disagree.
  /// </summary>
  public const double MaxHeightPx = 440;

  /// <summary>Minimum distance kept between the tray and the viewport's edges.</summary>
  public const double MarginPx = 12;

  /// <summary>Space between the pressed element and the tray's near edge.</summary>
  public const double GapPx = 8;

  // The kiosk panel. Used only when the browser reports a nonsensical viewport.
  private const double FallbackViewportWidth = 1920;
  private const double FallbackViewportHeight = 720;

  private DevTrayPlacement(double left, double? top, double? bottom)
  {
    Left = left;
    Top = top;
    Bottom = bottom;
  }

  /// <summary>CSS <c>left</c>, in pixels.</summary>
  public double Left { get; }

  /// <summary>CSS <c>top</c>, in pixels, when the tray opens below; otherwise null.</summary>
  public double? Top { get; }

  /// <summary>CSS <c>bottom</c>, in pixels, when the tray opens above; otherwise null.</summary>
  public double? Bottom { get; }

  /// <summary>True when the tray opens above the pressed element.</summary>
  public bool OpensAbove => Bottom.HasValue;

  /// <summary>
  /// Placement for a press at x <paramref name="tapX"/> on an element spanning
  /// <paramref name="pressedTop"/>..<paramref name="pressedBottom"/> vertically, all in CSS pixels
  /// relative to the viewport. A bare point is a press with top equal to bottom.
  /// </summary>
  public static DevTrayPlacement ForPress(
    double tapX, double pressedTop, double pressedBottom, double viewportWidth, double viewportHeight)
  {
    var vw = IsUsable(viewportWidth) ? viewportWidth : FallbackViewportWidth;
    var vh = IsUsable(viewportHeight) ? viewportHeight : FallbackViewportHeight;
    // Coordinates outside the viewport cannot come from a real press; clamp them in so the result
    // stays on-screen anyway.
    var x = Math.Clamp(double.IsFinite(tapX) ? tapX : vw / 2, 0, vw);
    var top = Math.Clamp(double.IsFinite(pressedTop) ? pressedTop : 0, 0, vh);
    var bottom = Math.Clamp(double.IsFinite(pressedBottom) ? pressedBottom : top, top, vh);

    // Centre on the tap, then clamp. Max is applied last so that on a viewport narrower than the
    // tray the left margin wins and the tray overflows to the right rather than off the left edge.
    var left = Math.Max(MarginPx, Math.Min(x - WidthPx / 2, vw - MarginPx - WidthPx));

    var below = bottom + GapPx;
    if (below + MaxHeightPx <= vh - MarginPx)
    {
      return new DevTrayPlacement(left, below, null);
    }

    var aboveEdge = top - GapPx;
    if (aboveEdge - MaxHeightPx >= MarginPx)
    {
      return new DevTrayPlacement(left, null, vh - aboveEdge);
    }

    // Fits on neither side: place it so a full-height tray would end at the bottom margin. It then
    // overlaps the pressed element; on a viewport shorter than the tray plus both margins its top is
    // clamped to the top margin and a full-height tray overflows the bottom instead.
    return new DevTrayPlacement(left, Math.Max(MarginPx, vh - MarginPx - MaxHeightPx), null);
  }

  private static bool IsUsable(double length) => double.IsFinite(length) && length > 0;
}
