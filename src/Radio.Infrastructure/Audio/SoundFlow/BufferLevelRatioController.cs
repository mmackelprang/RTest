namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Closed-loop control for the Bluetooth input resampler's conversion ratio (AUD-15): holds the
/// playback buffer near a target fill by nudging the ratio a few hundred ppm either side of its
/// measured seed.
/// </summary>
/// <remarks>
/// <para>
/// Path D (<c>docs/plans/2026-05-22-bt-input-resampler.md</c>) shipped a <b>static</b> ratio and
/// deferred this controller ("Phase 2"). A static ratio is right for only one phone on one day: a
/// residual error of ~100 ppm drains 100 ms of buffer in ~17 minutes, and with drift compensation
/// switched off for the resampler path nothing refills it. Measured on the box 2026-09-29: the buffer
/// sat at 1–3k of 384k samples and underran every few minutes.
/// </para>
/// <para>
/// The ratio is <c>output_rate / input_rate</c>, so <b>raising it produces more output samples</b> and
/// fills the buffer. The controller is proportional plus a slow integral on the buffer error, measured
/// in seconds of audio, averaged over <see cref="UpdateIntervalSeconds"/> so per-callback delivery
/// jitter does not reach the ratio. Every update is clamped to <c>base ± maxDeviationPpm</c> and moved
/// by at most <c>maxStepPpm</c>; 500 ppm is 0.9 cents of pitch, well below audibility, and
/// libsamplerate ramps each change across the next block.
/// </para>
/// <para>
/// Not thread-safe: called only from the capture thread that also owns the resampler.
/// </para>
/// </remarks>
internal sealed class BufferLevelRatioController
{
  /// <summary>How often the ratio is recomputed from the averaged level.</summary>
  public const double UpdateIntervalSeconds = 1.0;

  private readonly double _baseRatio;
  private readonly double _targetSeconds;
  private readonly double _samplesPerSecond;
  private readonly double _maxDeviation;
  private readonly double _maxStep;
  private readonly double _kp;
  private readonly double _ki;
  private readonly double _integralLimit;

  private double _integral;
  private double _windowStartSeconds = double.NaN;
  private double _levelSum;
  private int _levelCount;

  /// <summary>The ratio most recently returned (the base ratio until the first update).</summary>
  public double Ratio { get; private set; }

  /// <summary>The averaged level (samples) behind the most recent update, or -1 before the first.</summary>
  public double LastAveragedLevel { get; private set; } = -1;

  /// <param name="baseRatio">The measured seed ratio (<c>BluetoothOptions.InputResamplerInitialRatio</c>).</param>
  /// <param name="targetSamples">The buffer fill to hold, in interleaved samples.</param>
  /// <param name="samplesPerSecond">Interleaved samples per second (rate × channels).</param>
  /// <param name="maxDeviationPpm">Largest departure from <paramref name="baseRatio"/>.</param>
  /// <param name="maxStepPpm">Largest change per update.</param>
  /// <param name="kp">Proportional gain: ratio change per second of buffer error. 1e-3 → 100 ppm for 100 ms.</param>
  /// <param name="ki">Integral gain per second² of accumulated error.</param>
  public BufferLevelRatioController(
    double baseRatio,
    int targetSamples,
    int samplesPerSecond,
    double maxDeviationPpm = 500,
    double maxStepPpm = 20,
    double kp = 1e-3,
    double ki = 2e-5)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseRatio);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetSamples);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerSecond);

    _baseRatio = baseRatio;
    _targetSeconds = (double)targetSamples / samplesPerSecond;
    _samplesPerSecond = samplesPerSecond;
    _maxDeviation = maxDeviationPpm * 1e-6;
    _maxStep = maxStepPpm * 1e-6;
    _kp = kp;
    _ki = ki;
    // Anti-windup: the integral term alone may use the full deviation budget and no more.
    _integralLimit = ki > 0 ? _maxDeviation / ki : 0;
    Ratio = baseRatio;
  }

  /// <summary>
  /// Records one level observation. Returns the new ratio when an update interval has elapsed, else
  /// null (the caller leaves the resampler alone).
  /// </summary>
  /// <param name="levelSamples">Buffered samples right now.</param>
  /// <param name="nowSeconds">A monotonic time in seconds.</param>
  public double? Observe(int levelSamples, double nowSeconds)
  {
    if (double.IsNaN(_windowStartSeconds))
    {
      _windowStartSeconds = nowSeconds;
    }

    _levelSum += levelSamples;
    _levelCount++;

    var elapsed = nowSeconds - _windowStartSeconds;
    if (elapsed < UpdateIntervalSeconds)
    {
      return null;
    }

    var averageLevel = _levelSum / _levelCount;
    _levelSum = 0;
    _levelCount = 0;
    _windowStartSeconds = nowSeconds;
    LastAveragedLevel = averageLevel;

    // Positive error = buffer below target = produce more output = raise the ratio.
    var errorSeconds = _targetSeconds - averageLevel / _samplesPerSecond;
    _integral = Math.Clamp(_integral + errorSeconds * elapsed, -_integralLimit, _integralLimit);

    var correction = Math.Clamp(_kp * errorSeconds + _ki * _integral, -_maxDeviation, _maxDeviation);
    var desired = _baseRatio * (1.0 + correction);
    var maxStep = _baseRatio * _maxStep;
    Ratio = Math.Clamp(desired, Ratio - maxStep, Ratio + maxStep);
    return Ratio;
  }

  /// <summary>
  /// Discards the current averaging window without touching the ratio or the integral. Called while
  /// the buffer is priming, when its level says nothing about the clock relationship.
  /// </summary>
  public void SkipWindow()
  {
    _levelSum = 0;
    _levelCount = 0;
    _windowStartSeconds = double.NaN;
  }
}
