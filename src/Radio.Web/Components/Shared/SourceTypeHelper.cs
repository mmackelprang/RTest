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

  /// <summary>
  /// Single source of truth for the "radio family" — the set of source types
  /// that share the radio control panel + tuning workflow. Consumed by
  /// <c>MainLayout.IsRadioSource</c> (chevron click → "/" + show radio panel)
  /// and <c>QueueHistoryPanel</c> (default-tab selection treats the whole
  /// family as one source). Deliberately distinct from <see cref="HasDetail"/>
  /// — Bluetooth has a detail surface but is not part of the radio family.
  ///
  /// Future radio-style source types default to <c>false</c> and must be added
  /// explicitly. Returns <c>false</c> for null / empty inputs.
  /// </summary>
  public static bool IsRadioFamily(string? sourceType) =>
    !string.IsNullOrEmpty(sourceType) && RadioFamilyTypes.Contains(sourceType);

  private static readonly HashSet<string> RadioFamilyTypes =
    new(StringComparer.OrdinalIgnoreCase)
    {
      "Radio",
      "RTLSDRCore",
      "RF320"
    };

  /// <summary>
  /// Destinations the chevron tap on a <c>SourceBubble</c> can route to
  /// (Arc 3 PR C folded-in item #13). Extracted from
  /// <c>MainLayout.HandleSourceDetailAsync</c> so the routing decision is
  /// unit-testable without spinning up Radzen + bUnit.
  /// </summary>
  public enum SourceDetailRoute
  {
    /// <summary>Source has no detail surface; chevron tap is a no-op.</summary>
    None,
    /// <summary>Radio family — navigate Home and show the radio control panel.</summary>
    RadioPanel,
    /// <summary>Bluetooth — navigate to the BT pairing page.</summary>
    BluetoothPage,
  }

  /// <summary>
  /// Resolves the detail-surface destination for a given source type. Returns
  /// <see cref="SourceDetailRoute.None"/> for null / empty / unknown types so
  /// <c>MainLayout</c> can switch over the enum without inline string checks.
  /// </summary>
  public static SourceDetailRoute GetDetailRoute(string? sourceType)
  {
    if (string.IsNullOrEmpty(sourceType))
    {
      return SourceDetailRoute.None;
    }
    if (IsRadioFamily(sourceType))
    {
      return SourceDetailRoute.RadioPanel;
    }
    if (sourceType.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase))
    {
      return SourceDetailRoute.BluetoothPage;
    }
    return SourceDetailRoute.None;
  }
}
