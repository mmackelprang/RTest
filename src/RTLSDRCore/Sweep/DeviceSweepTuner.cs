using RTLSDRCore.Hardware;
using RTLSDRCore.Models;

namespace RTLSDRCore.Sweep;

/// <summary>
/// <see cref="ISweepTuner"/> over an <see cref="ISdrDevice"/> that the sweep
/// owns outright: it opens the device, reads with synchronous
/// <see cref="ISdrDevice.ReadSamples"/> (no streaming), and closes and
/// disposes the device on <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// Only for use while nothing else has the device open. Arbitrating that is
/// the caller's job.
/// </remarks>
public sealed class DeviceSweepTuner : ISweepTuner, IDisposable
{
  /// <summary>Sample rate used for sweeps, matching the receiver's FM capture rate.</summary>
  public const int SweepSampleRate = 240_000;

  /// <summary>Consecutive zero-sample reads tolerated before a read gives up.</summary>
  public const int MaxConsecutiveEmptyReads = 3;

  private readonly ISdrDevice _device;
  private readonly float _gainDb;
  private bool _disposed;

  /// <summary>Creates a tuner that takes ownership of <paramref name="device"/>.</summary>
  /// <param name="device">The device. Closed and disposed by <see cref="Dispose"/>.</param>
  /// <param name="gainDb">Manual tuner gain for the sweep, in dB.</param>
  public DeviceSweepTuner(ISdrDevice device, float gainDb)
  {
    _device = device ?? throw new ArgumentNullException(nameof(device));
    _gainDb = gainDb;
  }

  /// <inheritdoc/>
  public int SampleRate => SweepSampleRate;

  /// <summary>
  /// Opens the device and configures sample rate and manual gain.
  /// </summary>
  /// <exception cref="InvalidOperationException">The device could not be opened or configured.</exception>
  public void Open()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    if (!_device.IsOpen && !_device.Open())
    {
      throw new InvalidOperationException("SDR device could not be opened for a band sweep");
    }

    if (!_device.SetSampleRate(SweepSampleRate))
    {
      throw new InvalidOperationException($"SDR device rejected the sweep sample rate {SweepSampleRate} Hz");
    }

    _device.SetGainMode(false);
    _device.SetGain(_gainDb);
  }

  /// <inheritdoc/>
  public bool Tune(long frequencyHz) => _device.SetFrequency(frequencyHz);

  /// <inheritdoc/>
  /// <remarks>
  /// Calls <see cref="ISdrDevice.ReadSamples"/> until the buffer is full.
  /// After <see cref="MaxConsecutiveEmptyReads"/> consecutive reads that
  /// return nothing, returns the short count.
  /// </remarks>
  public int Read(Span<IqSample> buffer, CancellationToken ct)
  {
    int filled = 0;
    int emptyReads = 0;
    while (filled < buffer.Length)
    {
      ct.ThrowIfCancellationRequested();
      int read = _device.ReadSamples(buffer.Slice(filled));
      if (read <= 0)
      {
        emptyReads++;
        if (emptyReads >= MaxConsecutiveEmptyReads)
        {
          break;
        }
        continue;
      }

      emptyReads = 0;
      filled += Math.Min(read, buffer.Length - filled);
    }

    return filled;
  }

  /// <summary>Closes and disposes the device.</summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _device.Close();
    _device.Dispose();
  }
}
