namespace Radio.Core.Configuration;

/// <summary>
/// TTS API credentials configuration for cloud-based TTS services.
/// Values are typically resolved from secret tags.
/// Loaded from the 'TTS' configuration section in secrets store.
/// </summary>
public class TTSSecrets
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "TTS";

  /// <summary>
  /// Gets or sets the Google Cloud Text-to-Speech API key.
  /// </summary>
  public string GoogleAPIKey { get; set; } = "";

  /// <summary>
  /// Gets or sets the Azure Cognitive Services Speech API key.
  /// </summary>
  public string AzureAPIKey { get; set; } = "";

  /// <summary>
  /// Gets or sets the Azure region for Speech Services (e.g., "eastus").
  /// </summary>
  public string AzureRegion { get; set; } = "";
}
