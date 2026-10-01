using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Models;
using Radio.Infrastructure.Audio.Services;
using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Sweep;

namespace Radio.API.Controllers;

/// <summary>
/// The stored band maps and their sweeps (AUD-76; per band since AUD-91).
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
  /// Gets a band's stored map, its frequency axis, and the sweep status.
  /// </summary>
  /// <param name="band">Band code (AM, FM, SW, AIR, WB, VHF); omitted for the radio's current band.</param>
  /// <returns>
  /// The map; before the band's first sweep, an empty channel list with null scan time and age.
  /// A band that cannot be scanned has <c>mappable: false</c>, the reason, and its preset range as the axis.
  /// </returns>
  /// <response code="200">Returns the map and sweep status.</response>
  /// <response code="400">The band is not a band code.</response>
  [HttpGet]
  [ProducesResponseType(typeof(BandMapResponseDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult<BandMapResponseDto> Get([FromQuery] string? band = null)
  {
    if (!TryResolveBand(band, out BandType bandType))
    {
      return InvalidBand(band);
    }

    string code = BandSweepPlans.BandCode(bandType);
    BandMap? map = _bandMap.GetMap(code);
    BandMapResponseDto dto = BandMapResponseDto.From(map, _bandMap.GetAge(map), _bandMap.GetStatus());
    return Ok(WithAxis(dto with { Band = code }, bandType, map));
  }

  /// <summary>
  /// Requests a sweep of a band. While the radio is playing, the receiver silences its output for
  /// the sweep (about 21 s for the 101 FM channels, about 30 s for AIR, seconds for WB and VHF)
  /// and then retunes to its current frequency.
  /// </summary>
  /// <param name="band">Band code (AM, FM, SW, AIR, WB, VHF); omitted for the radio's current band.</param>
  /// <returns>The sweep status.</returns>
  /// <response code="202">A sweep of the band was started, or one of the same band was already running.</response>
  /// <response code="400">The band is not a band code.</response>
  /// <response code="409">
  /// The band cannot be scanned on this tuner, a sweep of another band is running (the error names
  /// it), the radio cannot be swept right now, or the SDR device is busy.
  /// </response>
  /// <response code="503">Sweeps are disabled, or no SDR device is available.</response>
  [HttpPost("scan")]
  [ProducesResponseType(typeof(BandSweepStatusDto), StatusCodes.Status202Accepted)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public ActionResult<BandSweepStatusDto> Scan([FromQuery] string? band = null)
  {
    if (!TryResolveBand(band, out BandType bandType))
    {
      return InvalidBand(band);
    }

    string? unavailable = BandSweepPlans.UnavailableReason(bandType);
    if (unavailable != null)
    {
      return Conflict(new { error = unavailable });
    }

    BandSweepRequestResult result = _bandMap.RequestSweep(BandSweepPlans.BandCode(bandType));
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
      BandMapService.ReasonBandNotReceivable => Conflict(new { error = "This band cannot be scanned" }),
      BandMapService.ReasonOtherBandSweeping => Conflict(new
      {
        error = $"The {result.Status.Band ?? "other"} band is being scanned; try again when it finishes",
      }),
      _ => Conflict(new { error = "The SDR device is busy" }),
    };
  }

  /// <summary>Null resolves to the radio's current band; anything else must be a band code.</summary>
  private bool TryResolveBand(string? band, out BandType bandType) =>
    BandSweepPlans.TryParseBandCode(band ?? _bandMap.CurrentBand, out bandType);

  private BadRequestObjectResult InvalidBand(string? band) =>
    BadRequest(new { error = $"Invalid band: {band}. Valid values are: {string.Join(", ", BandSweepPlans.BandCodes)}" });

  /// <summary>
  /// Fills the axis fields. A mappable band uses its sweep plan; a stored map that recorded its
  /// range (VHF's window, which follows the radio) overrides the plan's display range and
  /// spacing, and VHF's first and last channel are then the window's edges. A band that cannot
  /// be scanned uses its preset range and default step.
  /// </summary>
  private BandMapResponseDto WithAxis(BandMapResponseDto dto, BandType bandType, BandMap? map)
  {
    string code = BandSweepPlans.BandCode(bandType);
    BandSweepPlan? plan = _bandMap.GetPlan(code);
    if (plan == null)
    {
      RTLSDRCore.Models.RadioBand preset = BandPresets.GetBand(bandType);
      return dto with
      {
        Mappable = false,
        UnavailableReason = BandSweepPlans.UnavailableReason(bandType),
        DisplayMinHz = preset.MinFrequencyHz,
        DisplayMaxHz = preset.MaxFrequencyHz,
        FirstChannelHz = preset.MinFrequencyHz,
        LastChannelHz = preset.MaxFrequencyHz,
        ChannelSpacingHz = preset.DefaultStepHz,
      };
    }

    bool storedRange = map != null && map.RangeMinHz > 0 && map.RangeMaxHz > map.RangeMinHz;
    long displayMin = storedRange ? map!.RangeMinHz : plan.DisplayMinHz;
    long displayMax = storedRange ? map!.RangeMaxHz : plan.DisplayMaxHz;
    // VHF's display range is its first-to-last channel window (BandSweepPlans.For).
    bool vhfStored = storedRange && bandType == BandType.VHF;
    return dto with
    {
      Mappable = true,
      UnavailableReason = null,
      DisplayMinHz = displayMin,
      DisplayMaxHz = displayMax,
      FirstChannelHz = vhfStored ? displayMin : plan.Channels[0],
      LastChannelHz = vhfStored ? displayMax : plan.Channels[^1],
      ChannelSpacingHz = map != null && map.ChannelSpacingHz > 0 ? map.ChannelSpacingHz : plan.ChannelSpacingHz,
    };
  }
}
