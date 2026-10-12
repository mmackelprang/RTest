using System.Globalization;
using Radio.Core.Models;
using Radio.Core.Models.Audio;

namespace Radio.Web.Components.Shared;

/// <summary>
/// Centralized frequency formatting and conversion utilities.
/// All radio API values are in Hz; these helpers convert to display units: kHz on AM and SW, MHz on
/// every other band (<see cref="BandUnits.IsKhz(string?)"/>, AUD-102).
/// </summary>
public static class FrequencyFormatter
{
  /// <summary>
  /// Formats a frequency in Hz to a display string with units: <c>5905 kHz</c> on SW,
  /// <c>1010 kHz</c> on AM, <c>101.50 MHz</c> on FM and the other MHz bands.
  /// </summary>
  public static string FormatFrequency(double frequencyHz, string band) =>
    FrequencyValue(frequencyHz, band) + FrequencyUnit(band);

  /// <summary>
  /// Returns just the numeric value portion (no units): whole kHz on AM and SW (one decimal when a
  /// 2.5 kHz step needs it: <c>5907.5</c>), MHz with two decimals otherwise.
  /// </summary>
  public static string FrequencyValue(double frequencyHz, string band) =>
    BandUnits.IsKhz(band)
      ? BandUnits.FormatKhz(frequencyHz)
      : (frequencyHz / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture);

  /// <summary>
  /// Returns the unit suffix for a band type, with its leading space.
  /// </summary>
  public static string FrequencyUnit(string band) => BandUnits.IsKhz(band) ? " kHz" : " MHz";

  /// <summary>
  /// Formats a frequency step in Hz to a display string with units.
  /// </summary>
  public static string FormatStep(double stepHz) =>
    stepHz >= 1_000_000
      ? $"{stepHz / 1_000_000.0:0.###} MHz"
      : $"{stepHz / 1_000.0:0.###} kHz";

  /// <summary>
  /// Converts a frequency in Hz to the frequency-entry dialogs' display units (kHz on AM and SW,
  /// MHz otherwise).
  /// </summary>
  public static double ToDisplayUnits(double frequencyHz, string? band) =>
    BandUnits.IsKhz(band) ? frequencyHz / 1_000.0 : frequencyHz / 1_000_000.0;

  /// <summary>
  /// Converts a value typed in a frequency-entry dialog (kHz on AM and SW, MHz otherwise) back to Hz.
  /// </summary>
  public static double FromDisplayUnits(double value, string? band) =>
    BandUnits.IsKhz(band) ? value * 1_000.0 : value * 1_000_000.0;

  /// <summary>
  /// The frequency-entry dialogs' placeholder: <c>Frequency (kHz)</c> on AM and SW, <c>Frequency (MHz)</c> otherwise.
  /// </summary>
  public static string DialogPlaceholder(string? band) =>
    BandUnits.IsKhz(band) ? "Frequency (kHz)" : "Frequency (MHz)";

  /// <summary>
  /// Converts a step in Hz to dialog-friendly display units.
  /// </summary>
  public static double GetDialogStep(double stepHz, string band) => ToDisplayUnits(stepHz, band);

  /// <summary>
  /// Gets the minimum frequency in display units for a band.
  /// Checks configured bands first, then falls back to well-known defaults.
  /// </summary>
  public static double GetMinFrequency(string band, IEnumerable<RadioBandModel>? availableBands)
  {
    var bandModel = availableBands?.FirstOrDefault(b => b.Type == band);
    if (bandModel != null)
    {
      return ToDisplayUnits(bandModel.MinFrequencyHz, bandModel.Type);
    }

    // The fallbacks are RTLSDRCore's BandPresets ranges, in display units (AUD-102).
    return band switch
    {
      "AM" => 530,           // AM: 530-1710 kHz (Medium Wave)
      "FM" => 87.5,          // FM: 87.5-108.0 MHz
      "AIR" => 108.0,        // Aircraft: 108.0-137.0 MHz (VHF Air Band)
      "SW" => 1711,          // Shortwave: 1711-30000 kHz (HF bands, above AM's top channel)
      "WB" => 162.400,       // Weather: 162.400-162.550 MHz (NOAA Weather Radio)
      "VHF" => 30.0,         // VHF: 30-300 MHz
      _ => 0
    };
  }

  /// <summary>
  /// Gets the maximum frequency in display units for a band.
  /// Checks configured bands first, then falls back to well-known defaults.
  /// </summary>
  public static double GetMaxFrequency(string band, IEnumerable<RadioBandModel>? availableBands)
  {
    var bandModel = availableBands?.FirstOrDefault(b => b.Type == band);
    if (bandModel != null)
    {
      return ToDisplayUnits(bandModel.MaxFrequencyHz, bandModel.Type);
    }

    return band switch
    {
      "AM" => 1710,          // AM: 530-1710 kHz (Medium Wave)
      "FM" => 108.0,         // FM: 87.5-108.0 MHz
      "AIR" => 137.0,        // Aircraft: 108.0-137.0 MHz (VHF Air Band)
      "SW" => 30_000,        // Shortwave: 1711-30000 kHz (HF bands)
      "WB" => 162.550,       // Weather: 162.400-162.550 MHz (NOAA Weather Radio)
      "VHF" => 300.0,        // VHF: 30-300 MHz
      _ => 1000
    };
  }
}
