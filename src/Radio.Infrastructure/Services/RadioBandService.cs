using Radio.Core.Interfaces;
using Radio.Core.Models;
using RTLSDRCore.Bands;

namespace Radio.Infrastructure.Services;

public class RadioBandService : IRadioBandService
{
    public IEnumerable<RadioBandModel> GetAvailableBands()
    {
        var bands = typeof(BandPresets).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(RTLSDRCore.Models.RadioBand))
            .Select(p => (RTLSDRCore.Models.RadioBand)p.GetValue(null)!)
            .Select(b => new RadioBandModel
            {
                Type = MapBandTypeToCode(b.Type),
                Name = b.Name,
                MinFrequencyHz = b.MinFrequencyHz,
                MaxFrequencyHz = b.MaxFrequencyHz,
                DefaultStepHz = b.DefaultStepHz,
                DefaultModulation = b.DefaultModulation.ToString(),
                DefaultBandwidthHz = b.DefaultBandwidthHz,
                Description = b.Description
            });
            
        return bands;
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
