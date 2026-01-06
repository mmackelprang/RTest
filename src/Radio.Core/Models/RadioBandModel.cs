namespace Radio.Core.Models;

public class RadioBandModel
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long MinFrequencyHz { get; set; }
    public long MaxFrequencyHz { get; set; }
    public long DefaultStepHz { get; set; }
    public string DefaultModulation { get; set; } = string.Empty;
    public long DefaultBandwidthHz { get; set; }
    public string Description { get; set; } = string.Empty;
}
