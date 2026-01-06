using Microsoft.AspNetCore.Mvc;
using Radio.Core.Interfaces;
using Radio.Core.Models;

namespace Radio.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RadioBandsController : ControllerBase
{
    private readonly IRadioBandService _radioBandService;

    public RadioBandsController(IRadioBandService radioBandService)
    {
        _radioBandService = radioBandService;
    }

    /// <summary>
    /// Gets all available radio bands.
    /// </summary>
    /// <returns>A list of radio bands.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RadioBandModel>), StatusCodes.Status200OK)]
    public IActionResult GetBands()
    {
        var bands = _radioBandService.GetAvailableBands();
        return Ok(bands);
    }
}
