namespace RTLSDRCore.Hardware;

/// <summary>
/// The RTL2832U direct-sampling input, as passed to <c>rtlsdr_set_direct_sampling</c> (AUD-99).
/// </summary>
public enum DirectSamplingMode
{
  /// <summary>Direct sampling off: the tuner chip feeds the RTL2832U (normal VHF/UHF reception).</summary>
  Off = 0,

  /// <summary>The I-branch ADC input is sampled directly.</summary>
  I = 1,

  /// <summary>The Q-branch ADC input is sampled directly. The branch HF-capable dongles wire to their HF input.</summary>
  Q = 2,
}

/// <summary>
/// The <c>RTLSDR:HfDirectSampling</c> configuration value (AUD-99): which direct-sampling input a
/// device uses for the HF bands, or whether it uses none.
/// </summary>
public enum HfDirectSamplingSetting
{
  /// <summary>
  /// Q when the USB product string is in <see cref="HfDirectSampling.KnownGoodProducts"/>; Off otherwise.
  /// </summary>
  Auto,

  /// <summary>Always the Q branch.</summary>
  Q,

  /// <summary>Always the I branch.</summary>
  I,

  /// <summary>Never: the device has no HF bands.</summary>
  Off,
}

/// <summary>
/// The rules that decide whether, and on which ADC branch, a dongle receives HF by direct sampling (AUD-99).
/// </summary>
/// <remarks>
/// A generic R820T dongle does not wire either ADC branch to its antenna socket, so HF cannot be
/// inferred from the chip set. The USB product string is what tells a dongle with a wired Q branch
/// (the Nooelec NESDR SMArt v5) apart from one without.
/// </remarks>
public static class HfDirectSampling
{
  /// <summary>The configuration key that holds the <see cref="HfDirectSamplingSetting"/>.</summary>
  public const string ConfigurationKey = "RTLSDR:HfDirectSampling";

  /// <summary>
  /// Lowest frequency reported in <see cref="Models.DeviceInfo.MinFrequencyHz"/> for a device with HF
  /// direct sampling, in Hz (100 kHz; Nooelec specifies 100 kHz to 1.75 GHz for the SMArt v5).
  /// </summary>
  public const long HfMinFrequencyHz = 100_000;

  /// <summary>
  /// USB product strings of dongles known to wire the Q branch to their HF input. Compared
  /// case-insensitively after trimming.
  /// </summary>
  public static IReadOnlyList<string> KnownGoodProducts { get; } = new[]
  {
    "NESDR SMArt v5",
  };

  /// <summary>
  /// The direct-sampling input a device with USB product string <paramref name="product"/> uses for
  /// HF under <paramref name="setting"/>.
  /// </summary>
  /// <param name="setting">The configured setting.</param>
  /// <param name="product">The device's USB product string; null or empty when unknown.</param>
  public static DirectSamplingMode Resolve(HfDirectSamplingSetting setting, string? product) => setting switch
  {
    HfDirectSamplingSetting.Q => DirectSamplingMode.Q,
    HfDirectSamplingSetting.I => DirectSamplingMode.I,
    HfDirectSamplingSetting.Off => DirectSamplingMode.Off,
    _ => IsKnownGoodProduct(product) ? DirectSamplingMode.Q : DirectSamplingMode.Off,
  };

  /// <summary>True when <paramref name="product"/> is in <see cref="KnownGoodProducts"/>.</summary>
  public static bool IsKnownGoodProduct(string? product)
  {
    if (string.IsNullOrWhiteSpace(product))
    {
      return false;
    }

    string trimmed = product.Trim();
    return KnownGoodProducts.Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Parses a configuration value: <c>Auto</c>, <c>Q</c>, <c>I</c> or <c>Off</c>, case-insensitive.
  /// Null or blank parses as <see cref="HfDirectSamplingSetting.Auto"/>. Numeric strings are rejected.
  /// </summary>
  /// <param name="value">The configured value.</param>
  /// <param name="setting">The setting, when the result is true; <see cref="HfDirectSamplingSetting.Auto"/> otherwise.</param>
  public static bool TryParseSetting(string? value, out HfDirectSamplingSetting setting)
  {
    setting = HfDirectSamplingSetting.Auto;
    if (string.IsNullOrWhiteSpace(value))
    {
      return true;
    }

    string trimmed = value.Trim();
    foreach (HfDirectSamplingSetting candidate in Enum.GetValues<HfDirectSamplingSetting>())
    {
      if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
      {
        setting = candidate;
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// The tuner chip named by librtlsdr's <c>rtlsdr_tuner</c> value, as returned by
  /// <c>rtlsdr_get_tuner_type</c>. <c>R820T</c> covers the R820T, R820T2 and R860, which
  /// librtlsdr does not tell apart.
  /// </summary>
  /// <param name="tunerType">The <c>rtlsdr_tuner</c> value.</param>
  public static string TunerName(int tunerType) => tunerType switch
  {
    1 => "E4000",
    2 => "FC0012",
    3 => "FC0013",
    4 => "FC2580",
    5 => "R820T",
    6 => "R828D",
    _ => "Unknown",
  };
}
