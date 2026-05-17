namespace Radio.Web.Components.Shared;

/// <summary>
/// Centralized source type icon and CSS attribute mapping.
/// Replaces duplicated switch expressions in MainLayout and NowPlayingPanel.
/// </summary>
public static class SourceTypeHelper
{
  /// <summary>
  /// Gets the Material icon name for a source type string.
  /// Handles both API source type names ("FilePlayer") and fingerprint source names ("File").
  /// </summary>
  public static string GetIcon(string sourceType) => sourceType switch
  {
    "Vinyl" => "album",
    "FilePlayer" or "File" => "audio_file",
    "Radio" or "RTLSDRCore" or "RF320" => "radio",
    "Bluetooth" => "bluetooth",
    "GenericUSB" => "usb",
    "TestTone" => "graphic_eq",
    _ => "music_note"
  };

  /// <summary>
  /// Gets the CSS data-source attribute value for a source type string.
  /// Used for styling components based on active source.
  /// </summary>
  public static string GetDataAttribute(string sourceType) => sourceType switch
  {
    "Vinyl" => "vinyl",
    "FilePlayer" or "File" => "file",
    "Radio" or "RTLSDRCore" or "RF320" => "radio",
    "Bluetooth" => "bluetooth",
    "GenericUSB" => "usb",
    "TestTone" => "testtone",
    _ => "file"
  };

  /// <summary>
  /// Gets the CSS custom-property name (e.g. <c>--source-radio</c>) for the
  /// per-source accent colour. Used by SourceBubble, the IN cluster swatch
  /// in MainLayout, and the source-color dot in NowPlayingDock.
  /// Falls back to <c>--accent-primary</c> for unknown / unmapped types.
  /// </summary>
  public static string GetAccentVar(string sourceType) => sourceType switch
  {
    "Vinyl" => "--source-vinyl",
    "FilePlayer" or "File" => "--source-file",
    "Radio" or "RTLSDRCore" or "RF320" => "--source-radio",
    "Bluetooth" => "--source-bluetooth",
    "GenericUSB" or "USB" => "--source-usb",
    _ => "--accent-primary"
  };

  /// <summary>
  /// Single source of truth for which source types have a dedicated detail
  /// surface (radio control panel, Bluetooth pairing page). Consumed by
  /// <c>MainLayout</c> to drive the chevron affordance on <c>SourceBubble</c>
  /// (handoff §P1·2) and by other places that need to gate detail-only UI.
  ///
  /// Future source types default to <c>false</c> and must be added explicitly.
  /// </summary>
  public static bool HasDetail(string sourceType) =>
    SourcesWithDetail.Contains(sourceType);

  private static readonly HashSet<string> SourcesWithDetail =
    new(StringComparer.OrdinalIgnoreCase)
    {
      "Radio",
      "RTLSDRCore",
      "RF320",
      "Bluetooth"
    };
}
