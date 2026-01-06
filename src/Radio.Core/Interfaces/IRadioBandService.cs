using Radio.Core.Models;

namespace Radio.Core.Interfaces;

public interface IRadioBandService
{
    IEnumerable<RadioBandModel> GetAvailableBands();
}
