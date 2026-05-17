using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Mappers;

/// <summary>
/// Centralised projection of <see cref="IRadioControl"/> into
/// <see cref="RadioStateDto"/>. Both the REST controller and the SignalR
/// state-update background service depend on this so the two paths cannot
/// drift out of sync (PR 1 of the Radio Controller Polish arc adds the
/// <see cref="RadioStateDto.Clip"/>, <see cref="RadioStateDto.RssiDbu"/>,
/// <see cref="RadioStateDto.AppliedGain"/> fields and re-types
/// <see cref="RadioStateDto.ScanStopThreshold"/> from int-percent to
/// double-dBu).
/// </summary>
public static class RadioStateMapper
{
  // Linear-fit anchors for the raw-percent → dBu projection.
  //
  // IRadioControl.SignalStrength is an int already nominally in the 0-100
  // range, but the device-side scaler has historically produced values up to
  // ~118 when the front-end is overdriving. The UI must never paint > 100%
  // (it's a 20-bar meter), so we clamp here and surface the overshoot via
  // the dedicated <see cref="RadioStateDto.Clip"/> flag plus the
  // <see cref="RadioStateDto.RssiDbu"/> calibrated readout.
  //
  // The fit is linear because no calibrated curve is exposed by the RTL-SDR
  // or RF320 drivers — see the plan's open question #1. 0% → -60 dBu (noise
  // floor), 100% → 0 dBu (full-scale reference).

  public const double SignalMinDbu = -60.0;
  public const double SignalMaxDbu = 0.0;

  /// <summary>
  /// Projects an <see cref="IRadioControl"/> snapshot into the DTO shape the
  /// web UI consumes.
  /// </summary>
  /// <param name="radioSource">Radio control snapshot.</param>
  /// <param name="nowPlayingMatchId">
  /// Optional <see cref="FingerprintEventDto.MatchId"/> of the fingerprint
  /// match currently anchored as the playing track. PR 2 of the Radio
  /// Controller Polish arc — surfaces the active match to the recognition
  /// stream so the UI can render a NOW header + amber border above the
  /// correct row. Pass <c>null</c> when no match is currently anchored.
  /// </param>
  public static RadioStateDto MapToRadioStateDto(IRadioControl radioSource, string? nowPlayingMatchId = null)
  {
    var rawSignal = radioSource.SignalStrength;
    return new RadioStateDto
    {
      Frequency = radioSource.CurrentFrequency.Hertz,
      Band = radioSource.CurrentBand.ToString(),
      Step = radioSource.FrequencyStep.Hertz,
      SignalStrength = ClampSignalPercent(rawSignal),
      Clip = IsClipping(rawSignal),
      RssiDbu = SignalToDbu(rawSignal),
      AppliedGain = ComputeAppliedGain(radioSource),
      Equalizer = radioSource.EqualizerMode.ToString(),
      DeviceVolume = radioSource.DeviceVolume,
      IsScanning = radioSource.IsScanning,
      ScanDirection = radioSource.ScanDirection?.ToString(),
      ScanStopThreshold = PercentToDbu(radioSource.ScanStopThreshold),
      AutoGain = radioSource.AutoGainEnabled,
      Gain = (int?)radioSource.Gain,
      IsStereo = radioSource.IsStereo,
      RdsStationName = radioSource.RdsStationName,
      RdsProgramType = radioSource.RdsProgramType,
      RdsRadioText = radioSource.RdsRadioText,
      NowPlayingMatchId = nowPlayingMatchId,
    };
  }

  /// <summary>
  /// Clamps the raw signal-strength int into [0, 100]. Null pass-through so
  /// "no reading yet" is preserved.
  /// </summary>
  public static int? ClampSignalPercent(int? raw)
  {
    return raw.HasValue ? Math.Clamp(raw.Value, 0, 100) : null;
  }

  /// <summary>
  /// True when the raw front-end power reading exceeded the calibrated
  /// full-scale reference (100%). Drives the CLIP pill in the meter UI.
  /// </summary>
  public static bool IsClipping(int? raw)
  {
    return raw.HasValue && raw.Value > 100;
  }

  /// <summary>
  /// Linear projection of the clamped percent into the [-60, 0] dBu band.
  /// Overdrive (raw &gt; 100) saturates at 0 dBu — the <see cref="IsClipping"/>
  /// flag carries the "above full-scale" signal.
  /// </summary>
  public static double SignalToDbu(int? raw)
  {
    if (!raw.HasValue)
    {
      return SignalMinDbu;
    }

    var pct = Math.Clamp(raw.Value, 0, 100);
    return SignalMinDbu + ((SignalMaxDbu - SignalMinDbu) * pct / 100.0);
  }

  /// <summary>
  /// Projects the int-percent scan-stop threshold (configured on
  /// <c>RadioOptions</c>) into the dBu domain so the UI compares
  /// like-with-like against <see cref="SignalToDbu(int?)"/>.
  /// </summary>
  public static double PercentToDbu(int percent)
  {
    var pct = Math.Clamp(percent, 0, 100);
    return SignalMinDbu + ((SignalMaxDbu - SignalMinDbu) * pct / 100.0);
  }

  /// <summary>
  /// Returns the live applied gain. Today the device-side AGC selection
  /// isn't separately surfaced on <see cref="IRadioControl"/>; when AGC is
  /// on the manual <see cref="IRadioControl.Gain"/> still reflects whatever
  /// the device most recently reported (RTL-SDR auto-gain leaves the gain
  /// register populated). Either way the UI binds one float so it doesn't
  /// have to branch on AGC state.
  /// </summary>
  public static double ComputeAppliedGain(IRadioControl radioSource)
  {
    return radioSource.Gain;
  }
}
