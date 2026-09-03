namespace Radio.Core.Models;

public class RadioBandModel
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long MinFrequencyHz { get; set; }
    public long MaxFrequencyHz { get; set; }
    public long DefaultStepHz { get; set; }
    public long[] AllowedStepSizes { get; set; } = Array.Empty<long>();
    public string DefaultModulation { get; set; } = string.Empty;
    public long DefaultBandwidthHz { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Human-formatted sub-range string for the tall band pills (e.g.
    /// <c>"76–108 MHz"</c> for FM, <c>"530–1700 kHz"</c> for AM). Composed
    /// server-side so the UI doesn't repeat unit-selection logic. PR 3 of
    /// the Radio Controller Polish arc.
    /// </summary>
    public string Range { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of preset slots for this band (e.g. 16 FM,
    /// 4 WB). Drives the <c>PRESETS · n saved</c> header count and the
    /// empty-slot placeholder logic. PR 3 of the Radio Controller Polish
    /// arc.
    /// </summary>
    public int BandPresetCapacity { get; set; }
}
