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
}
