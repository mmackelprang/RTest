using System.Globalization;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models;
using Radio.Core.Models.Audio;
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

    private readonly IRadioFactory? _radioFactory;

    /// <summary>
    /// Creates the band list service.
    /// </summary>
    /// <param name="radioFactory">
    /// Supplies the SDR device's receivable range for <see cref="RadioBandModel.IsReceivable"/>
    /// (AUD-102). Optional: without it, or when it reports no device, every band is receivable.
    /// </param>
    public RadioBandService(IRadioFactory? radioFactory = null)
    {
        _radioFactory = radioFactory;
    }

    public IEnumerable<RadioBandModel> GetAvailableBands()
    {
        SdrHardwareInfo? device = SdrReceivingDevice.Find(_radioFactory);

        var bands = typeof(BandPresets).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(RTLSDRCore.Models.RadioBand))
            .Select(p => (RTLSDRCore.Models.RadioBand)p.GetValue(null)!)
            .Select(b =>
            {
                var code = MapBandTypeToCode(b.Type);
                string? unavailable = UnavailableReason(b.Type, device);
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
                    IsReceivable = unavailable == null,
                    UnavailableReason = unavailable,
                };
            });

        return bands;
    }

    /// <summary>
    /// Why <paramref name="device"/> cannot receive <paramref name="band"/>, or null when it can or
    /// no device is known (AUD-102). The sentence is <see cref="BandReception.UnavailableReason"/>'s,
    /// the function the band map's reason is built on too. Exposed internal for unit testing.
    /// </summary>
    internal static string? UnavailableReason(RTLSDRCore.Enums.BandType band, SdrHardwareInfo? device) =>
        device == null
            ? null
            : BandReception.UnavailableReason(band, device.MinFrequencyHz, device.MaxFrequencyHz);

    /// <summary>
    /// Formats the band's frequency range as a UI-ready string in the band's unit
    /// (<see cref="BandUnits.IsKhz(string?)"/>: kHz on AM and SW, MHz otherwise). Uses a typographic
    /// en-dash (U+2013) between min/max to match the canvas mock. Exposed
    /// internal for unit testing via InternalsVisibleTo.
    /// </summary>
    internal static string FormatRange(string bandCode, long minHz, long maxHz)
    {
        if (BandUnits.IsKhz(bandCode))
        {
            // AUD-102: SW is in kHz like AM ("1711–30000 kHz"), the unit its readout uses.
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}–{1} kHz",
                BandUnits.FormatKhz(minHz),
                BandUnits.FormatKhz(maxHz));
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
