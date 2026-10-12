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
    /// <c>"87.5–108 MHz"</c> for FM, <c>"530–1710 kHz"</c> for AM, <c>"1711–30000 kHz"</c> for SW,
    /// whose unit is kHz like AM's since AUD-102). Composed
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

    /// <summary>
    /// True when the SDR device can receive the whole band (AUD-102). Computed from the device's
    /// receivable range (with or without HF direct sampling, AUD-99) by
    /// <c>RTLSDRCore.Bands.BandReception.UnavailableReason</c>, the function the band map's reason
    /// uses too. True when no SDR device is known (none enumerated, as on a dev box or in tests):
    /// the flag describes the device's limits, and with no device there are none to report. No UI
    /// reads it yet; the dimmed-pill UI was declined on 2026-10-02.
    /// </summary>
    public bool IsReceivable { get; set; } = true;

    /// <summary>
    /// Why the band is not receivable, as a sentence for the owner, e.g. <c>The AM Broadcast band
    /// (530–1,710 kHz) is below this tuner's 24 MHz lower limit and cannot be received.</c>; null when
    /// <see cref="IsReceivable"/> is true.
    /// </summary>
    public string? UnavailableReason { get; set; }
}
