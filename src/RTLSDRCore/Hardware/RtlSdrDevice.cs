using System.Runtime.InteropServices;
using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using Serilog;

namespace RTLSDRCore.Hardware;

  /// <summary>
  /// RTL-SDR USB dongle device implementation
  /// </summary>
  public class RtlSdrDevice : ISdrDevice
  {
      private static readonly ILogger Logger = Log.ForContext<RtlSdrDevice>();

      /// <summary>
      /// Lowest frequency the tuner path receives, in Hz (24 MHz), and the value reported in
      /// <see cref="DeviceInfo.MinFrequencyHz"/> for a device without HF direct sampling. A fixed
      /// value matching an R820T-class tuner; the tuner is not queried for it. A device with HF
      /// direct sampling reports <see cref="HfDirectSampling.HfMinFrequencyHz"/> instead (AUD-99).
      /// </summary>
      public const long TunerMinFrequencyHz = 24_000_000;

      /// <summary>Shown in <see cref="DeviceInfo.TunerType"/> before the device has been opened in this process.</summary>
      public const string TunerNotYetRead = "Unknown (not opened yet)";

      // Tuner names read at open, by device key (see DeviceKey), so that enumeration, which cannot
      // open a device that may be in use, can report the real tuner once any instance has opened it.
      private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> TunerNames = new();

      // Device keys whose HF decision has been logged at Information; later opens log it at Debug.
      private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> HfDecisionLogged = new();

      /// <summary>
      /// Highest frequency reported in <see cref="DeviceInfo.MaxFrequencyHz"/>, in Hz (1.766 GHz).
      /// A fixed value matching an R820T-class tuner; the tuner is not queried for it.
      /// </summary>
      public const long TunerMaxFrequencyHz = 1_766_000_000;

      private readonly int _deviceIndex;
      private readonly HfDirectSamplingSetting _hfSetting;
      private IntPtr _deviceHandle = IntPtr.Zero;
      private DeviceInfo? _deviceInfo;
      private bool _isOpen;
      private bool _isStreaming;
      // The direct-sampling mode last read back from librtlsdr (0 off, 1 I, 2 Q). Volatile: the
      // receiver's threads read IsHfMode without the lock the switch is made under.
      private volatile int _directSamplingMode;
      private CancellationTokenSource? _streamingCts;
      private Task? _streamingTask;

      /// <inheritdoc/>
      public DeviceInfo DeviceInfo => _deviceInfo ?? throw new InvalidOperationException("Device not initialized");

      /// <inheritdoc/>
      public bool IsOpen => _isOpen;

      /// <inheritdoc/>
      public bool IsStreaming => _isStreaming;

      /// <inheritdoc/>
      public bool SupportsHfDirectSampling => HfInput != DirectSamplingMode.Off;

      /// <inheritdoc/>
      public bool IsHfMode => _isOpen && _directSamplingMode > 0;

      private DirectSamplingMode HfInput => _deviceInfo?.HfDirectSampling ?? DirectSamplingMode.Off;

      /// <inheritdoc/>
      public event EventHandler<IqSamplesEventArgs>? SamplesAvailable;

      /// <inheritdoc/>
      public event EventHandler<DeviceErrorEventArgs>? ErrorOccurred;

      /// <summary>
      /// Creates a new RTL-SDR device instance
      /// </summary>
      /// <param name="deviceIndex">Device index (0-based)</param>
      /// <param name="hfSetting">The <c>RTLSDR:HfDirectSampling</c> setting that decides this device's HF input (AUD-99).</param>
      public RtlSdrDevice(int deviceIndex, HfDirectSamplingSetting hfSetting = HfDirectSamplingSetting.Auto)
      {
          _deviceIndex = deviceIndex;
          _hfSetting = hfSetting;
          _deviceInfo = GetDeviceInfo(deviceIndex, hfSetting);
      }

      /// <summary>
      /// Enumerates all available RTL-SDR devices, with the HF decision of the default
      /// <see cref="HfDirectSamplingSetting.Auto"/> setting.
      /// </summary>
      /// <returns>List of device information</returns>
      public static IReadOnlyList<DeviceInfo> EnumerateDevices() => EnumerateDevices(HfDirectSamplingSetting.Auto);

      /// <summary>
      /// Enumerates all available RTL-SDR devices
      /// </summary>
      /// <param name="hfSetting">The setting that decides each device's HF input and lower frequency limit.</param>
      /// <returns>List of device information</returns>
      public static IReadOnlyList<DeviceInfo> EnumerateDevices(HfDirectSamplingSetting hfSetting)
      {
          var devices = new List<DeviceInfo>();

          try
          {
              var count = NativeMethods.rtlsdr_get_device_count();
              Logger.Information("Found {Count} RTL-SDR device(s)", count);

              for (uint i = 0; i < count; i++)
              {
                  var info = GetDeviceInfo((int)i, hfSetting);
                  if (info != null)
                  {
                      devices.Add(info);
                  }
              }
          }
          catch (DllNotFoundException ex)
          {
              Logger.Warning(ex, "RTL-SDR library not found. Install librtlsdr or rtl-sdr drivers.");
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error enumerating RTL-SDR devices");
          }

          return devices;
      }

      private static DeviceInfo? GetDeviceInfo(int index, HfDirectSamplingSetting hfSetting)
      {
          try
          {
              var name = NativeMethods.rtlsdr_get_device_name((uint)index);

              var manufacturer = new byte[256];
              var product = new byte[256];
              var serial = new byte[256];

              NativeMethods.rtlsdr_get_device_usb_strings((uint)index, manufacturer, product, serial);

              var productString = UsbString(product);
              var serialString = UsbString(serial);
              var hfInput = HfDirectSampling.Resolve(hfSetting, productString);

              return new DeviceInfo
              {
                  Index = index,
                  Name = Marshal.PtrToStringAnsi(name) ?? $"RTL-SDR Device {index}",
                  Manufacturer = UsbString(manufacturer),
                  Product = productString,
                  Serial = serialString,
                  Type = DeviceType.RTLSDR,
                  IsAvailable = true,
                  TunerType = TunerNames.TryGetValue(DeviceKey(index, serialString), out var tuner) ? tuner : TunerNotYetRead,
                  HfDirectSampling = hfInput,
                  MinFrequencyHz = hfInput != DirectSamplingMode.Off ? HfDirectSampling.HfMinFrequencyHz : TunerMinFrequencyHz,
                  MaxFrequencyHz = TunerMaxFrequencyHz,
                  SupportedSampleRates = new[] { 250000, 1024000, 1536000, 1792000, 1920000, 2048000, 2400000, 2560000, 2880000, 3200000 },
                  AvailableGains = new[] { 0f, 0.9f, 1.4f, 2.7f, 3.7f, 7.7f, 8.7f, 12.5f, 14.4f, 15.7f, 16.6f, 19.7f, 20.7f, 22.9f, 25.4f, 28.0f, 29.7f, 32.8f, 33.8f, 36.4f, 37.2f, 38.6f, 40.2f, 42.1f, 43.4f, 43.9f, 44.5f, 48.0f, 49.6f }
              };
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Failed to get device info for index {Index}", index);
              return null;
          }
      }

      // A NUL-terminated ASCII buffer filled by rtlsdr_get_device_usb_strings.
      private static string UsbString(byte[] buffer)
      {
          var length = Array.IndexOf(buffer, (byte)0);
          return System.Text.Encoding.ASCII.GetString(buffer, 0, length < 0 ? buffer.Length : length).Trim();
      }

      // Identifies one dongle across instances: the serial when it has one, the index otherwise.
      private static string DeviceKey(int index, string serial) =>
          string.IsNullOrEmpty(serial) ? $"index:{index}" : $"serial:{serial}";

      /// <summary>
      /// Reads the tuner type from the open device into <see cref="DeviceInfo.TunerType"/> and
      /// remembers it for later enumerations; then logs the HF decision, at Information the first
      /// time in this process for this dongle and at Debug after that.
      /// </summary>
      private void ReadTunerAndLogHfDecision()
      {
          var info = _deviceInfo;
          if (info == null)
          {
              return;
          }

          try
          {
              info.TunerType = HfDirectSampling.TunerName(NativeMethods.rtlsdr_get_tuner_type(_deviceHandle));
          }
          catch (Exception ex)
          {
              // An older librtlsdr without the export: report it rather than fail the open.
              Logger.Debug(ex, "Could not read the tuner type of RTL-SDR device {Index}", _deviceIndex);
              info.TunerType = "Unknown";
          }

          var key = DeviceKey(_deviceIndex, info.Serial);
          TunerNames[key] = info.TunerType;

          var level = HfDecisionLogged.TryAdd(key, true)
              ? Serilog.Events.LogEventLevel.Information
              : Serilog.Events.LogEventLevel.Debug;
          Logger.Write(level,
              "RTL-SDR device {Index}: product {Product}, tuner {Tuner}, HF direct sampling {HfMode} (RTLSDR:HfDirectSampling={Setting}), lower limit {MinFrequency}",
              _deviceIndex,
              string.IsNullOrEmpty(info.Product) ? "(none)" : info.Product,
              info.TunerType,
              info.HfDirectSampling == DirectSamplingMode.Off ? "Off" : $"{info.HfDirectSampling} branch",
              _hfSetting,
              RadioBand.FormatFrequency(info.MinFrequencyHz));
      }

      /// <inheritdoc/>
      public bool Open()
      {
          if (_isOpen)
          {
              Logger.Warning("Device is already open");
              return false;
          }

          try
          {
              var result = NativeMethods.rtlsdr_open(out _deviceHandle, (uint)_deviceIndex);
              if (result != 0)
              {
                  Logger.Error("Failed to open RTL-SDR device {Index}: error {Error}", _deviceIndex, result);
                  return false;
              }

              _isOpen = true;
              // Read, not assumed: rtlsdr_open switches direct sampling to mode 1 (the I branch)
              // itself when it finds no supported tuner (RTLSDR_TUNER_UNKNOWN), and off otherwise.
              _directSamplingMode = ReadDirectSamplingMode();
              Logger.Information("Opened RTL-SDR device {Index}: {Name}", _deviceIndex, _deviceInfo?.Name);
              ReadTunerAndLogHfDecision();

              // Reset buffer
              NativeMethods.rtlsdr_reset_buffer(_deviceHandle);

              return true;
          }
          catch (DllNotFoundException ex)
          {
              Logger.Error(ex, "RTL-SDR library not found");
              ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs("RTL-SDR library not found", ex));
              return false;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error opening RTL-SDR device");
              ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs("Error opening device", ex));
              return false;
          }
      }

      /// <inheritdoc/>
      public void Close()
      {
          if (!_isOpen)
    {
      return;
    }

    StopStreaming();

          try
          {
              NativeMethods.rtlsdr_close(_deviceHandle);
              _deviceHandle = IntPtr.Zero;
              _isOpen = false;
              _directSamplingMode = 0;
              Logger.Information("Closed RTL-SDR device {Index}", _deviceIndex);
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error closing RTL-SDR device");
          }
      }

      /// <inheritdoc/>
      public bool SetFrequency(long frequencyHz)
      {
          if (!_isOpen)
    {
      return false;
    }

    try
          {
              var result = NativeMethods.rtlsdr_set_center_freq(_deviceHandle, (uint)frequencyHz);
              if (result != 0)
              {
                  Logger.Error("Failed to set frequency to {Frequency}: error {Error}",
                      RadioBand.FormatFrequency(frequencyHz), result);

                  // LIBUSB_ERROR_PIPE (-9) means USB endpoint stalled — attempt recovery
                  if (result == -9)
                  {
                      Logger.Warning("USB pipe stalled (error -9), attempting buffer reset recovery");
                      try
                      {
                          NativeMethods.rtlsdr_reset_buffer(_deviceHandle);
                          // Retry once after reset
                          result = NativeMethods.rtlsdr_set_center_freq(_deviceHandle, (uint)frequencyHz);
                          if (result == 0)
                          {
                              Logger.Information("Recovered from USB pipe stall, frequency set to {Frequency}",
                                  RadioBand.FormatFrequency(frequencyHz));
                              return true;
                          }
                          Logger.Error("Retry after buffer reset also failed with error {Error}", result);
                      }
                      catch (Exception resetEx)
                      {
                          Logger.Error(resetEx, "Exception during USB pipe stall recovery");
                      }
                  }

                  return false;
              }

              Logger.Debug("Set frequency to {Frequency}", RadioBand.FormatFrequency(frequencyHz));
              return true;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error setting frequency");
              return false;
          }
      }

      /// <inheritdoc/>
      /// <remarks>
      /// <para>
      /// Calls <c>rtlsdr_set_direct_sampling</c> with this device's
      /// <see cref="DeviceInfo.HfDirectSampling"/> input (or 0 for off) and reads the mode back with
      /// <c>rtlsdr_get_direct_sampling</c>. A successful switch is logged at Information.
      /// Idempotence compares the mode wanted with the mode last read back. Callers in this repo
      /// (<c>RadioReceiver</c>, <c>DeviceSweepTuner</c>) skip the call when <see cref="IsHfMode"/> already
      /// matches, so a device <c>rtlsdr_open</c> left in mode 1 (an unknown tuner) is not moved to Q by them.
      /// </para>
      /// <para>
      /// A non-zero result, a read-back that disagrees, or an exception is logged at Warning and
      /// returns false, after recovery: librtlsdr's HF-on branch calls the tuner's <c>exit</c> and
      /// records the new mode even when a register write failed, so the hardware can be left
      /// half-switched. The mode is read back; if it is not the mode the device was in before, the
      /// previous mode is set once more; and <see cref="IsHfMode"/> then reports the mode read back
      /// last (off when that read fails).
      /// </para>
      /// </remarks>
      public bool SetHfMode(bool on)
      {
          if (!_isOpen)
          {
              // A closed device has no mode: IsHfMode is false while closed, and Open reads the
              // mode back. Switching on needs an open device.
              return !on;
          }

          if (on && !SupportsHfDirectSampling)
          {
              Logger.Debug("HF direct sampling requested on RTL-SDR device {Index}, which has none (product {Product})",
                  _deviceIndex, _deviceInfo?.Product);
              return false;
          }

          var mode = on ? HfInput : DirectSamplingMode.Off;
          int previous = _directSamplingMode;
          if ((int)mode == previous)
          {
              return true;
          }

          try
          {
              var result = NativeMethods.rtlsdr_set_direct_sampling(_deviceHandle, (int)mode);
              if (result != 0)
              {
                  Logger.Warning("RTL-SDR device {Index} refused direct sampling mode {Mode}: error {Error}",
                      _deviceIndex, mode, result);
                  RecoverAfterFailedSwitch(previous);
                  return false;
              }

              var readBack = NativeMethods.rtlsdr_get_direct_sampling(_deviceHandle);
              if (readBack != (int)mode)
              {
                  Logger.Warning("RTL-SDR device {Index} reports direct sampling mode {ReadBack} after mode {Mode} was set",
                      _deviceIndex, readBack, (int)mode);
                  RecoverAfterFailedSwitch(previous);
                  return false;
              }

              _directSamplingMode = readBack;
              if (on)
              {
                  Logger.Information(
                      "RTL-SDR device {Index}: HF direct sampling ON ({Mode} branch, mode {ModeNumber} read back); tuner in standby",
                      _deviceIndex, mode, readBack);
              }
              else
              {
                  Logger.Information(
                      "RTL-SDR device {Index}: HF direct sampling OFF (mode {ModeNumber} read back); tuner active",
                      _deviceIndex, readBack);
              }
              return true;
          }
          catch (Exception ex)
          {
              Logger.Warning(ex, "Error switching RTL-SDR device {Index} to direct sampling mode {Mode}", _deviceIndex, mode);
              RecoverAfterFailedSwitch(previous);
              return false;
          }
      }

      /// <summary>
      /// After a failed <see cref="SetHfMode"/>: reads librtlsdr's recorded mode back, sets
      /// <paramref name="previous"/> once more when that mode is not it, and caches the mode read back last (off when the
      /// read fails). Logs the outcome at Warning. Never throws.
      /// </summary>
      private void RecoverAfterFailedSwitch(int previous)
      {
          int actual = ReadDirectSamplingMode();
          if (actual != previous)
          {
              try
              {
                  int result = NativeMethods.rtlsdr_set_direct_sampling(_deviceHandle, previous);
                  if (result != 0)
                  {
                      Logger.Warning("RTL-SDR device {Index}: restoring direct sampling mode {Mode} failed: error {Error}",
                          _deviceIndex, previous, result);
                  }
              }
              catch (Exception ex)
              {
                  Logger.Warning(ex, "RTL-SDR device {Index}: error restoring direct sampling mode {Mode}", _deviceIndex, previous);
              }

              actual = ReadDirectSamplingMode();
          }

          _directSamplingMode = actual;
          Logger.Warning("RTL-SDR device {Index}: after the failed switch the device reads back direct sampling mode {Mode}",
              _deviceIndex, actual);
      }

      /// <summary>
      /// <c>rtlsdr_get_direct_sampling</c> as 0, 1 or 2; 0 when the call fails or returns an error.
      /// </summary>
      private int ReadDirectSamplingMode()
      {
          try
          {
              return Math.Max(0, NativeMethods.rtlsdr_get_direct_sampling(_deviceHandle));
          }
          catch (Exception ex)
          {
              Logger.Debug(ex, "Could not read the direct sampling mode of RTL-SDR device {Index}", _deviceIndex);
              return 0;
          }
      }

      /// <inheritdoc/>
      public long GetFrequency()
      {
          if (!_isOpen)
    {
      return 0;
    }

    try
          {
              return NativeMethods.rtlsdr_get_center_freq(_deviceHandle);
          }
          catch
          {
              return 0;
          }
      }

      /// <inheritdoc/>
      public bool SetSampleRate(int sampleRate)
      {
          if (!_isOpen)
    {
      return false;
    }

    try
          {
              var result = NativeMethods.rtlsdr_set_sample_rate(_deviceHandle, (uint)sampleRate);
              if (result != 0)
              {
                  Logger.Error("Failed to set sample rate to {SampleRate}: error {Error}", sampleRate, result);
                  return false;
              }

              Logger.Debug("Set sample rate to {SampleRate}", sampleRate);
              return true;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error setting sample rate");
              return false;
          }
      }

      /// <inheritdoc/>
      public int GetSampleRate()
      {
          if (!_isOpen)
    {
      return 0;
    }

    try
          {
              return (int)NativeMethods.rtlsdr_get_sample_rate(_deviceHandle);
          }
          catch
          {
              return 0;
          }
      }

      /// <inheritdoc/>
      public bool SetGainMode(bool automatic)
      {
          if (!_isOpen)
    {
      return false;
    }

    if (IsHfMode)
    {
      // The tuner is in standby under direct sampling; librtlsdr's tuner gain calls have nothing
      // to act on, and a failure would log at Error on every tune.
      Logger.Debug("Gain mode not set: HF direct sampling is on and the tuner is in standby");
      return true;
    }

    try
          {
              var result = NativeMethods.rtlsdr_set_tuner_gain_mode(_deviceHandle, automatic ? 0 : 1);
              if (result != 0)
              {
                  Logger.Error("Failed to set gain mode: error {Error}", result);
                  return false;
              }

              Logger.Debug("Set gain mode to {Mode}", automatic ? "automatic" : "manual");
              return true;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error setting gain mode");
              return false;
          }
      }

      /// <inheritdoc/>
      public bool SetGain(float gainDb)
      {
          if (!_isOpen)
    {
      return false;
    }

    if (IsHfMode)
    {
      // As SetGainMode: the tuner is in standby.
      Logger.Debug("Gain {Gain} dB not set: HF direct sampling is on and the tuner is in standby", gainDb);
      return true;
    }

    try
          {
              // RTL-SDR expects gain in tenths of dB
              var result = NativeMethods.rtlsdr_set_tuner_gain(_deviceHandle, (int)(gainDb * 10));
              if (result != 0)
              {
                  Logger.Error("Failed to set gain to {Gain} dB: error {Error}", gainDb, result);
                  return false;
              }

              Logger.Debug("Set gain to {Gain} dB", gainDb);
              return true;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error setting gain");
              return false;
          }
      }

      /// <inheritdoc/>
      public float GetGain()
      {
          if (!_isOpen)
    {
      return 0;
    }

    try
          {
              return NativeMethods.rtlsdr_get_tuner_gain(_deviceHandle) / 10.0f;
          }
          catch
          {
              return 0;
          }
      }

      /// <inheritdoc/>
      public bool SetFrequencyCorrection(int ppm)
      {
          if (!_isOpen)
    {
      return false;
    }

    try
          {
              var result = NativeMethods.rtlsdr_set_freq_correction(_deviceHandle, ppm);
              if (result != 0 && result != -2) // -2 means same value
              {
                  Logger.Error("Failed to set frequency correction to {PPM} ppm: error {Error}", ppm, result);
                  return false;
              }

              Logger.Debug("Set frequency correction to {PPM} ppm", ppm);
              return true;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error setting frequency correction");
              return false;
          }
      }

      /// <inheritdoc/>
      public int GetFrequencyCorrection()
      {
          if (!_isOpen)
    {
      return 0;
    }

    try
          {
              return NativeMethods.rtlsdr_get_freq_correction(_deviceHandle);
          }
          catch
          {
              return 0;
          }
      }

      /// <inheritdoc/>
      public bool StartStreaming()
      {
          if (!_isOpen)
          {
              Logger.Error("Cannot start streaming: device not open");
              return false;
          }

          if (_isStreaming)
          {
              Logger.Warning("Already streaming");
              return false;
          }

          Logger.Information("Starting RTL-SDR sample streaming");
          _isStreaming = true;
          _streamingCts = new CancellationTokenSource();

          _streamingTask = Task.Run(() => StreamingLoop(_streamingCts.Token));

          return true;
      }

      /// <inheritdoc/>
      public void StopStreaming()
      {
          if (!_isStreaming)
    {
      return;
    }

    Logger.Information("Stopping RTL-SDR sample streaming");

          try
          {
              NativeMethods.rtlsdr_cancel_async(_deviceHandle);
          }
          catch { }

          _streamingCts?.Cancel();
          _streamingTask?.Wait(TimeSpan.FromSeconds(2));
          _streamingCts?.Dispose();
          _streamingCts = null;
          _isStreaming = false;
      }

      private void StreamingLoop(CancellationToken ct)
      {
          const int bufferSize = 16384 * 2; // Interleaved I/Q bytes
          var rawBuffer = new byte[bufferSize];

          while (!ct.IsCancellationRequested && _isOpen)
          {
              try
              {
                  var bytesRead = 0;
                  var result = NativeMethods.rtlsdr_read_sync(_deviceHandle, rawBuffer, bufferSize, out bytesRead);

                  if (result != 0 || bytesRead == 0)
                  {
                      if (!ct.IsCancellationRequested)
                      {
                          Thread.Sleep(10);
                      }
                      continue;
                  }

                  // Convert raw bytes to IQ samples
                  var samples = ConvertToIqSamples(rawBuffer, bytesRead);
                  SamplesAvailable?.Invoke(this, new IqSamplesEventArgs(samples));
              }
              catch (OperationCanceledException)
              {
                  break;
              }
              catch (Exception ex)
              {
                  Logger.Error(ex, "Error in streaming loop");
                  ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs("Streaming error", ex));
                  Thread.Sleep(100);
              }
          }
      }

      private static IqSample[] ConvertToIqSamples(byte[] rawBuffer, int bytesRead)
      {
          var sampleCount = bytesRead / 2;
          var samples = new IqSample[sampleCount];

          for (var i = 0; i < sampleCount; i++)
          {
              // RTL-SDR returns unsigned 8-bit values centered at 127.5
              var iRaw = rawBuffer[i * 2];
              var qRaw = rawBuffer[i * 2 + 1];

              // Convert to normalized float (-1.0 to 1.0)
              var iFloat = (iRaw - 127.5f) / 127.5f;
              var qFloat = (qRaw - 127.5f) / 127.5f;

              samples[i] = new IqSample(iFloat, qFloat);
          }

          return samples;
      }

      /// <inheritdoc/>
      public int ReadSamples(Span<IqSample> buffer)
      {
          if (!_isOpen)
    {
      return 0;
    }

    var byteCount = buffer.Length * 2;
          var rawBuffer = new byte[byteCount];

          var result = NativeMethods.rtlsdr_read_sync(_deviceHandle, rawBuffer, byteCount, out var bytesRead);

          if (result != 0)
    {
      return 0;
    }

    var sampleCount = bytesRead / 2;
          for (var i = 0; i < sampleCount && i < buffer.Length; i++)
          {
              var iRaw = rawBuffer[i * 2];
              var qRaw = rawBuffer[i * 2 + 1];
              var iFloat = (iRaw - 127.5f) / 127.5f;
              var qFloat = (qRaw - 127.5f) / 127.5f;
              buffer[i] = new IqSample(iFloat, qFloat);
          }

          return sampleCount;
      }

      /// <inheritdoc/>
      public void Dispose()
      {
          Close();
          GC.SuppressFinalize(this);
      }

      /// <summary>
      /// Native P/Invoke methods for librtlsdr
      /// </summary>
      private static class NativeMethods
      {
          private const string LibraryName = "rtlsdr";

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern uint rtlsdr_get_device_count();

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern IntPtr rtlsdr_get_device_name(uint index);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_get_device_usb_strings(uint index, byte[] manufacturer, byte[] product, byte[] serial);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_open(out IntPtr dev, uint index);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_close(IntPtr dev);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_set_center_freq(IntPtr dev, uint freq);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern uint rtlsdr_get_center_freq(IntPtr dev);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_set_sample_rate(IntPtr dev, uint rate);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern uint rtlsdr_get_sample_rate(IntPtr dev);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_set_tuner_gain_mode(IntPtr dev, int manual);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_set_tuner_gain(IntPtr dev, int gain);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_get_tuner_gain(IntPtr dev);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_set_freq_correction(IntPtr dev, int ppm);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_get_freq_correction(IntPtr dev);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_reset_buffer(IntPtr dev);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_read_sync(IntPtr dev, byte[] buf, int len, out int nRead);

          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_cancel_async(IntPtr dev);

          // 0 = off, 1 = I-ADC input, 2 = Q-ADC input. Returns 0 on success.
          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_set_direct_sampling(IntPtr dev, int on);

          // Returns the mode set (0, 1 or 2), or -1 on error.
          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_get_direct_sampling(IntPtr dev);

          // Returns an rtlsdr_tuner value; 0 (RTLSDR_TUNER_UNKNOWN) on error.
          [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
          public static extern int rtlsdr_get_tuner_type(IntPtr dev);
      }
  }
