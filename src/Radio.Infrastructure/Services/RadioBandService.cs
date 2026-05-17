using System.Globalization;
using Radio.Core.Interfaces;
using Radio.Core.Models;
using RTLSDRCore.Bands;

namespace Radio.Infrastructure.Services;

public class RadioBandService : IRadioBandService
{
    // Per-band preset capacity. PR 3 of the Radio Controller Polish arc
    // promotes this from a UI-side constant to a server-side projection so
    // future band additions don't require a Web-tier release. Defaults to
    // 16 for any unknown band type; WB caps at 4 because the actual Weather
    // Radio band only carries 7 channels in NOAA's allocation.
    private const int DefaultPresetCapacity = 16;
    private static readonly IReadOnlyDictionary<string, int> PresetCapacityByBandCode =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
      {
        ["FM"] = 16,
        ["AM"] = 16,
        ["SW"] = 16,
        ["VHF"] = 16,
        ["AIR"] = 16,
        ["WB"] = 4,
      };

    public IEnumerable<RadioBandModel> GetAvailableBands()
    {
        var bands = typeof(BandPresets).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(RTLSDRCore.Models.RadioBand))
            .Select(p => (RTLSDRCore.Models.RadioBand)p.GetValue(null)!)
            .Select(b =>
            {
                var code = MapBandTypeToCode(b.Type);
                return new RadioBandModel
                {
                    Type = code,
                    Name = b.Name,
                    MinFrequencyHz = b.MinFrequencyHz,
                    MaxFrequencyHz = b.MaxFrequencyHz,
                    DefaultStepHz = b.DefaultStepHz,
                    AllowedStepSizes = b.AllowedStepSizes,
                    DefaultModulation = b.DefaultModulation.ToString(),
                    DefaultBandwidthHz = b.DefaultBandwidthHz,
                    Description = b.Description,
                    Range = FormatRange(code, b.MinFrequencyHz, b.MaxFrequencyHz),
                    BandPresetCapacity = ResolveCapacity(code),
                };
            });

        return bands;
    }

    /// <summary>
    /// Formats the band's frequency range as a UI-ready string with the right
    /// unit (kHz for AM/SW HF, MHz for FM/VHF/WB/AIR). Uses a typographic
    /// en-dash (U+2013) between min/max to match the canvas mock. Exposed
    /// internal for unit testing via InternalsVisibleTo.
    /// </summary>
    internal static string FormatRange(string bandCode, long minHz, long maxHz)
    {
        // Shortwave covers 1.8–30 MHz on the radio: render in MHz for legibility
        // (kHz would print "1800–30000"). AM stays in kHz. Other bands ride the
        // MHz default because their floors are above 100 MHz.
        var useKhz = string.Equals(bandCode, "AM", StringComparison.OrdinalIgnoreCase);

        if (useKhz)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0}–{1:0} kHz",
                minHz / 1_000.0,
                maxHz / 1_000.0);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.##}–{1:0.##} MHz",
            minHz / 1_000_000.0,
            maxHz / 1_000_000.0);
    }

    /// <summary>
    /// Returns the per-band preset capacity. Unknown bands fall through to
    /// <see cref="DefaultPresetCapacity"/>. Exposed internal for unit testing.
    /// </summary>
    internal static int ResolveCapacity(string bandCode)
    {
        return PresetCapacityByBandCode.TryGetValue(bandCode, out var cap)
            ? cap
            : DefaultPresetCapacity;
    }

    private string MapBandTypeToCode(RTLSDRCore.Enums.BandType type)
    {
        return type switch
        {
            RTLSDRCore.Enums.BandType.AM => "AM",
            RTLSDRCore.Enums.BandType.FM => "FM",
            RTLSDRCore.Enums.BandType.Shortwave => "SW",
            RTLSDRCore.Enums.BandType.Aircraft => "AIR",
            RTLSDRCore.Enums.BandType.Weather => "WB",
            RTLSDRCore.Enums.BandType.VHF => "VHF",
            _ => type.ToString()
        };
    }
}
