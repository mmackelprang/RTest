using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using Serilog;
using System.Runtime.InteropServices;

namespace RTLSDRCore.Hardware
{
    /// <summary>
    /// RTL-SDR USB dongle device implementation
    /// </summary>
    public class RtlSdrDevice : ISdrDevice
    {
        private static readonly ILogger Logger = Log.ForContext<RtlSdrDevice>();

        private readonly int _deviceIndex;
        private IntPtr _deviceHandle = IntPtr.Zero;
        private DeviceInfo? _deviceInfo;
        private bool _isOpen;
        private bool _isStreaming;
        private CancellationTokenSource? _streamingCts;
        private Task? _streamingTask;

        /// <inheritdoc/>
        public DeviceInfo DeviceInfo => _deviceInfo ?? throw new InvalidOperationException("Device not initialized");

        /// <inheritdoc/>
        public bool IsOpen => _isOpen;

        /// <inheritdoc/>
        public bool IsStreaming => _isStreaming;

        /// <inheritdoc/>
        public event EventHandler<IqSamplesEventArgs>? SamplesAvailable;

        /// <inheritdoc/>
        public event EventHandler<DeviceErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// Creates a new RTL-SDR device instance
        /// </summary>
        /// <param name="deviceIndex">Device index (0-based)</param>
        public RtlSdrDevice(int deviceIndex)
        {
            _deviceIndex = deviceIndex;
            _deviceInfo = GetDeviceInfo(deviceIndex);
        }

        /// <summary>
        /// Enumerates all available RTL-SDR devices
        /// </summary>
        /// <returns>List of device information</returns>
        public static IReadOnlyList<DeviceInfo> EnumerateDevices()
        {
            var devices = new List<DeviceInfo>();

            try
            {
                var count = NativeMethods.rtlsdr_get_device_count();
                Logger.Information("Found {Count} RTL-SDR device(s)", count);

                for (uint i = 0; i < count; i++)
                {
                    var info = GetDeviceInfo((int)i);
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

        private static DeviceInfo? GetDeviceInfo(int index)
        {
            try
            {
                var name = NativeMethods.rtlsdr_get_device_name((uint)index);

                var manufacturer = new byte[256];
                var product = new byte[256];
                var serial = new byte[256];

                NativeMethods.rtlsdr_get_device_usb_strings((uint)index, manufacturer, product, serial);

                return new DeviceInfo
                {
                    Index = index,
                    Name = Marshal.PtrToStringAnsi(name) ?? $"RTL-SDR Device {index}",
                    Manufacturer = System.Text.Encoding.ASCII.GetString(manufacturer).TrimEnd('\0'),
                    Serial = System.Text.Encoding.ASCII.GetString(serial).TrimEnd('\0'),
                    Type = DeviceType.RTLSDR,
                    IsAvailable = true,
                    TunerType = "RTL2832U",
                    MinFrequencyHz = 24_000_000,
                    MaxFrequencyHz = 1_766_000_000,
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
                Logger.Information("Opened RTL-SDR device {Index}: {Name}", _deviceIndex, _deviceInfo?.Name);

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
                return;

            StopStreaming();

            try
            {
                NativeMethods.rtlsdr_close(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
                _isOpen = false;
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
            if (!_isOpen) return false;

            try
            {
                var result = NativeMethods.rtlsdr_set_center_freq(_deviceHandle, (uint)frequencyHz);
                if (result != 0)
                {
                    Logger.Error("Failed to set frequency to {Frequency}: error {Error}",
                        RadioBand.FormatFrequency(frequencyHz), result);
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
        public long GetFrequency()
        {
            if (!_isOpen) return 0;

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
            if (!_isOpen) return false;

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
            if (!_isOpen) return 0;

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
            if (!_isOpen) return false;

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
            if (!_isOpen) return false;

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
            if (!_isOpen) return 0;

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
            if (!_isOpen) return false;

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
            if (!_isOpen) return 0;

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
                return;

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
            if (!_isOpen) return 0;

            var byteCount = buffer.Length * 2;
            var rawBuffer = new byte[byteCount];

            var result = NativeMethods.rtlsdr_read_sync(_deviceHandle, rawBuffer, byteCount, out var bytesRead);

            if (result != 0)
                return 0;

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
        }
    }
}
