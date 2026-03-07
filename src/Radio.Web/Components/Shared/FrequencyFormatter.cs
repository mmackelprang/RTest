using Radio.Core.Models;

namespace Radio.Web.Components.Shared;

/// <summary>
/// Centralized frequency formatting and conversion utilities.
/// All radio API values are in Hz; these helpers convert to display units (MHz/kHz).
/// </summary>
public static class FrequencyFormatter
{
  /// <summary>
  /// Formats a frequency in Hz to a display string with units.
  /// AM bands display in kHz, all others in MHz.
  /// </summary>
  public static string FormatFrequency(double frequencyHz, string band) => band switch
  {
    "AM" => $"{frequencyHz / 1_000.0:0} kHz",
    _ => $"{frequencyHz / 1_000_000.0:0.00} MHz"
  };

  /// <summary>
  /// Returns just the numeric value portion (no units).
  /// </summary>
  public static string FrequencyValue(double frequencyHz, string band) => band switch
  {
    "AM" => $"{frequencyHz / 1_000.0:0}",
    _ => $"{frequencyHz / 1_000_000.0:0.00}"
  };

  /// <summary>
  /// Returns the unit suffix for a band type.
  /// </summary>
  public static string FrequencyUnit(string band) => band == "AM" ? " kHz" : " MHz";

  /// <summary>
  /// Formats a frequency step in Hz to a display string with units.
  /// </summary>
  public static string FormatStep(double stepHz) =>
    stepHz >= 1_000_000
      ? $"{stepHz / 1_000_000.0:0.###} MHz"
      : $"{stepHz / 1_000.0:0.###} kHz";

  /// <summary>
  /// Converts a step in Hz to dialog-friendly display units.
  /// </summary>
  public static double GetDialogStep(double stepHz, string band) =>
    band == "AM" ? stepHz / 1_000.0 : stepHz / 1_000_000.0;

  /// <summary>
  /// Gets the minimum frequency in display units for a band.
  /// Checks configured bands first, then falls back to well-known defaults.
  /// </summary>
  public static double GetMinFrequency(string band, IEnumerable<RadioBandModel>? availableBands)
  {
    var bandModel = availableBands?.FirstOrDefault(b => b.Type == band);
    if (bandModel != null)
    {
      return bandModel.Type == "AM" ? bandModel.MinFrequencyHz / 1000.0 : bandModel.MinFrequencyHz / 1000000.0;
    }

    return band switch
    {
      "AM" => 520,           // AM: 520-1710 kHz (Medium Wave)
      "FM" => 87.5,          // FM: 87.5-108.0 MHz
      "AIR" => 108.0,        // Aircraft: 108.0-137.0 MHz (VHF Air Band)
      "SW" => 1.8,           // Shortwave: 1.8-30.0 MHz (HF bands)
      "WB" => 162.400,       // Weather: 162.400-162.550 MHz (NOAA Weather Radio)
      "VHF" => 136.0,        // VHF: 136.0-174.0 MHz (VHF band)
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
      return bandModel.Type == "AM" ? bandModel.MaxFrequencyHz / 1000.0 : bandModel.MaxFrequencyHz / 1000000.0;
    }

    return band switch
    {
      "AM" => 1710,          // AM: 520-1710 kHz (Medium Wave)
      "FM" => 108.0,         // FM: 87.5-108.0 MHz
      "AIR" => 137.0,        // Aircraft: 108.0-137.0 MHz (VHF Air Band)
      "SW" => 30.0,          // Shortwave: 1.8-30.0 MHz (HF bands)
      "WB" => 162.550,       // Weather: 162.400-162.550 MHz (NOAA Weather Radio)
      "VHF" => 174.0,        // VHF: 136.0-174.0 MHz (VHF band)
      _ => 1000
    };
  }
}
