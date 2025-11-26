namespace Radio.Core.Configuration;

/// <summary>
/// Spotify API credentials configuration.
/// Values are typically resolved from secret tags.
/// Loaded from the 'Spotify' configuration section.
/// </summary>
public class SpotifySecrets
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "Spotify";

  /// <summary>
  /// Gets or sets the Spotify Client ID.
  /// </summary>
  public string ClientID { get; set; } = "";

  /// <summary>
  /// Gets or sets the Spotify Client Secret.
  /// </summary>
  public string ClientSecret { get; set; } = "";

  /// <summary>
  /// Gets or sets the Spotify Refresh Token for authorization.
  /// </summary>
  public string RefreshToken { get; set; } = "";
}
