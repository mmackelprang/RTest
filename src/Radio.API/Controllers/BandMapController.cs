using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Models;
using Radio.Infrastructure.Audio.Services;

namespace Radio.API.Controllers;

/// <summary>
/// The stored FM band map and its sweep (AUD-76).
/// </summary>
[ApiController]
[Route("api/radio/bandmap")]
[Produces("application/json")]
public class BandMapController : ControllerBase
{
  private readonly BandMapService _bandMap;

  /// <summary>
  /// Initializes a new instance of the BandMapController.
  /// </summary>
  public BandMapController(BandMapService bandMap)
  {
    _bandMap = bandMap;
  }

  /// <summary>
  /// Gets the stored FM band map and the sweep status.
  /// </summary>
  /// <returns>The map; before the first sweep, an empty channel list with null scan time and age.</returns>
  /// <response code="200">Returns the map and sweep status.</response>
  [HttpGet]
  [ProducesResponseType(typeof(BandMapResponseDto), StatusCodes.Status200OK)]
  public ActionResult<BandMapResponseDto> Get()
  {
    BandMap? map = _bandMap.CurrentMap;
    return Ok(BandMapResponseDto.From(map, _bandMap.GetAge(map), _bandMap.GetStatus()));
  }

  /// <summary>
  /// Requests a sweep. While the radio is playing, the receiver silences its output for the
  /// sweep (roughly 15 s for the 101 FM channels) and then retunes to its current frequency.
  /// </summary>
  /// <returns>The sweep status.</returns>
  /// <response code="202">A sweep was started, or one was already running.</response>
  /// <response code="409">The SDR device is busy and cannot be swept right now.</response>
  /// <response code="503">Sweeps are disabled, or no SDR device is available.</response>
  [HttpPost("scan")]
  [ProducesResponseType(typeof(BandSweepStatusDto), StatusCodes.Status202Accepted)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public ActionResult<BandSweepStatusDto> Scan()
  {
    BandSweepRequestResult result = _bandMap.RequestSweep();
    BandSweepStatusDto status = BandSweepStatusDto.From(result.Status);

    if (result.Outcome != BandSweepRequestOutcome.Unavailable)
    {
      return Accepted(status);
    }

    return result.Reason switch
    {
      BandMapService.ReasonDisabled => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Band map sweeps are disabled" }),
      BandMapService.ReasonNoSdrDevice => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "No SDR device is available" }),
      BandMapService.ReasonRadioBusy => Conflict(new { error = "The radio cannot be swept right now" }),
      _ => Conflict(new { error = "The SDR device is busy" }),
    };
  }
}
