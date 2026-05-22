#if !WINDOWS_TARGET
using System;
using Microsoft.Extensions.Logging;
using static Radio.Infrastructure.Platform.Bluetooth.Native.PipeWireNative;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Managed wrapper around libsamplerate's variable-rate sample-rate converter.
/// Used by the BT input path to compensate for the ~250 ppm clock skew between
/// the BT phone clock and the local speaker clock — see Path D in
/// <c>docs/plans/2026-05-22-bt-input-resampler.md</c>.
///
/// <para>
/// Single-threaded: the wrapper assumes only one producer (the PipeWire thread
/// loop) calls <see cref="Process"/>, and that <see cref="SetRatio"/> /
/// <see cref="Reset"/> / <see cref="Dispose"/> are also serialized with that
/// producer. libsamplerate state is not internally synchronized.
/// </para>
///
/// <para>
/// Linux-only — gated behind <c>#if !WINDOWS_TARGET</c>. The Windows BT input
/// path is WasapiLoopbackCapture, which has no equivalent clock-skew problem
/// (the loopback source is the same clock as playback).
/// </para>
/// </summary>
internal sealed class SrcVariableResampler : IDisposable
{
  private readonly ILogger _logger;
  private readonly int _channels;
  private IntPtr _state;
  private double _currentRatio;
  private bool _disposed;

  /// <summary>
  /// Current conversion ratio (output_rate / input_rate). 1.0 = no conversion.
  /// &gt;1.0 produces more output samples than input (used when the consumer
  /// drains faster than the producer, e.g. ~1.00025 for the measured 250 ppm
  /// BT-vs-speaker skew on the Ubuntu N100 + Pixel-class phone combo).
  /// </summary>
  public double Ratio => _currentRatio;

  /// <summary>
  /// Creates a new variable-rate resampler.
  /// </summary>
  /// <param name="logger">Logger for init / error reporting (not on the hot path).</param>
  /// <param name="channels">Channel count (e.g. 2 for stereo). Must match the stream.</param>
  /// <param name="initialRatio">Initial conversion ratio (output_rate / input_rate).</param>
  /// <param name="quality">Quality mode; SincFastest is the default for the BT use case.</param>
  /// <exception cref="InvalidOperationException">If libsamplerate fails to allocate state (e.g. invalid channel count or quality).</exception>
  public SrcVariableResampler(
    ILogger logger,
    int channels,
    double initialRatio,
    SrcQuality quality = SrcQuality.SincFastest)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _channels = channels;
    _currentRatio = initialRatio;

    _state = src_new((int)quality, channels, out var err);
    if (_state == IntPtr.Zero)
    {
      throw new InvalidOperationException(
        $"src_new failed: {SrcErrorMessage(err)} (quality={quality}, channels={channels})");
    }

    _logger.LogInformation(
      "SrcVariableResampler initialized: quality={Quality}, channels={Channels}, initial ratio={Ratio:F6}",
      quality, channels, initialRatio);
  }

  /// <summary>
  /// Updates the conversion ratio. libsamplerate internally ramps the change
  /// across the next <see cref="Process"/> call to avoid clicks at the boundary,
  /// so callers can update freely without managing their own crossfade.
  /// </summary>
  /// <param name="newRatio">New ratio. 1.0 = no conversion.</param>
  public void SetRatio(double newRatio)
  {
    if (_state == IntPtr.Zero)
    {
      return;
    }

    var err = src_set_ratio(_state, newRatio);
    if (err == 0)
    {
      _currentRatio = newRatio;
    }
    else
    {
      _logger.LogWarning(
        "src_set_ratio({Ratio:F6}) failed: {Err}", newRatio, SrcErrorMessage(err));
    }
  }

  /// <summary>
  /// Resets the converter's internal state (clears filter buffers). Call when
  /// the source stream is interrupted (e.g. BT reconnect) so the next batch of
  /// samples doesn't blend with stale filter taps.
  /// </summary>
  public void Reset()
  {
    if (_state == IntPtr.Zero)
    {
      return;
    }
    var err = src_reset(_state);
    if (err != 0)
    {
      _logger.LogWarning("src_reset failed: {Err}", SrcErrorMessage(err));
    }
  }

  /// <summary>
  /// Processes a chunk of input samples and writes the resampled output. Both
  /// input and output are interleaved float buffers. Returns the number of
  /// frames written to <paramref name="output"/>.
  /// </summary>
  /// <param name="input">Input frames (interleaved, length must be a multiple of channels).</param>
  /// <param name="output">Output buffer (must be sized for at least ratio × input frames).</param>
  /// <returns>Number of frames written to <paramref name="output"/>. Zero on error or empty input.</returns>
  public unsafe int Process(ReadOnlySpan<float> input, Span<float> output)
  {
    if (_state == IntPtr.Zero || input.IsEmpty || output.IsEmpty)
    {
      return 0;
    }

    fixed (float* inPtr = input)
    fixed (float* outPtr = output)
    {
      var data = new SrcData
      {
        DataIn = (IntPtr)inPtr,
        DataOut = (IntPtr)outPtr,
        InputFrames = input.Length / _channels,
        OutputFrames = output.Length / _channels,
        InputFramesUsed = 0,
        OutputFramesGen = 0,
        EndOfInput = 0,
        SrcRatio = _currentRatio,
      };

      var err = src_process(_state, ref data);
      if (err != 0)
      {
        // Log on the hot path is acceptable here — libsamplerate errors are
        // rare (invalid ratio, NaN, etc.) and indicate a configuration bug
        // worth surfacing immediately rather than swallowing.
        _logger.LogWarning("src_process failed: {Err}", SrcErrorMessage(err));
        return 0;
      }

      return (int)data.OutputFramesGen;
    }
  }

  /// <summary>
  /// Releases the native converter state. Idempotent — safe to call repeatedly.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    if (_state != IntPtr.Zero)
    {
      src_delete(_state);
      _state = IntPtr.Zero;
    }
  }
}
#endif
