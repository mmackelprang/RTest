namespace Radio.API.Models;

/// <summary>
/// DTO for a radio band configuration.
/// </summary>
public class RadioBandDto
{
    /// <summary>
    /// The type of band (AM, FM, etc.).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the band.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Minimum frequency in Hz.
    /// </summary>
    public long MinFrequencyHz { get; set; }

    /// <summary>
    /// Maximum frequency in Hz.
    /// </summary>
    public long MaxFrequencyHz { get; set; }

    /// <summary>
    /// Default tuning step in Hz.
    /// </summary>
    public long DefaultStepHz { get; set; }

    /// <summary>
    /// Allowed step sizes for this band in Hz, sorted ascending.
    /// </summary>
    public long[] AllowedStepSizes { get; set; } = Array.Empty<long>();

    /// <summary>
    /// Default modulation type.
    /// </summary>
    public string DefaultModulation { get; set; } = string.Empty;

    /// <summary>
    /// Default bandwidth in Hz.
    /// </summary>
    public long DefaultBandwidthHz { get; set; }

    /// <summary>
    /// Description of the band.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
