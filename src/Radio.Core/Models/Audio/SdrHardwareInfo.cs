namespace Radio.Core.Models.Audio;

/// <summary>
/// One SDR dongle as the radio factory sees it (AUD-99 diagnostics).
/// </summary>
/// <param name="Index">Device index.</param>
/// <param name="Name">The driver's name for the device's USB IDs (for an RTL-SDR, librtlsdr's table name).</param>
/// <param name="Manufacturer">USB manufacturer string.</param>
/// <param name="Product">USB product string, e.g. <c>NESDR SMArt v5</c>.</param>
/// <param name="Serial">USB serial string.</param>
/// <param name="TunerType">Tuner chip, read when the device was last opened in this process; <c>Unknown (not opened yet)</c> before that.</param>
/// <param name="HfDirectSampling">The direct-sampling input used for AM and SW: <c>Q</c>, <c>I</c>, or <c>Off</c> when the device has no HF bands.</param>
/// <param name="HfSetting">The <c>RTLSDR:HfDirectSampling</c> setting the decision was made with.</param>
/// <param name="IsHfMode">True when the device is open in HF direct-sampling mode now.</param>
/// <param name="IsInUse">True when this is the device of the current radio source (created and not yet disposed; it may be stopped).</param>
/// <param name="MinFrequencyHz">Lowest receivable frequency, in Hz.</param>
/// <param name="MaxFrequencyHz">Highest receivable frequency, in Hz.</param>
public sealed record SdrHardwareInfo(
  int Index,
  string Name,
  string Manufacturer,
  string Product,
  string Serial,
  string TunerType,
  string HfDirectSampling,
  string HfSetting,
  bool IsHfMode,
  bool IsInUse,
  long MinFrequencyHz,
  long MaxFrequencyHz);
