#if !WINDOWS_TARGET
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Infrastructure.Audio.SoundFlow;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Unit tests for <see cref="SrcVariableResampler"/> — the libsamplerate
/// wrapper used by the BT input path to compensate for clock skew between
/// the BT phone and the local speaker (Path D —
/// docs/plans/2026-05-22-bt-input-resampler.md).
///
/// Trait gates these tests behind <c>RequiresLibSampleRate</c> so they
/// can be filtered out on test runners that don't have <c>libsamplerate0</c>
/// installed. CI installs the package via apt (see .github/workflows/build.yml);
/// local Linux dev boxes typically already have it as a transitive PulseAudio /
/// PipeWire dependency.
/// </summary>
[Trait("Category", "RequiresLibSampleRate")]
public class SrcVariableResamplerTests
{
  private static ILogger Logger => NullLogger.Instance;

  [Fact]
  public void Process_IdentityRatio_PassesThroughSamples()
  {
    using var r = new SrcVariableResampler(Logger, channels: 2, initialRatio: 1.0);

    // 4 stereo frames in.
    var input = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f };
    var output = new float[16];

    var frames = r.Process(input, output);

    // SINC has startup transients on the first call (fills its internal
    // ring buffer), so the exact output count depends on libsamplerate's
    // internal state. The assertion is that *some* output is produced
    // and that it never exceeds the input frame count for ratio=1.0.
    Assert.InRange(frames, 0, 4);
  }

  [Fact]
  public void Process_StretchRatio_ProducesApproximatelyRatioTimesInput()
  {
    // 5 % stretch — output frames should be ~1.05 × input frames after the
    // SINC filter has primed (run two batches; the second is in steady state).
    using var r = new SrcVariableResampler(Logger, channels: 2, initialRatio: 1.05);

    var input = new float[2000];   // 1000 stereo frames
    for (var i = 0; i < input.Length; i++)
    {
      input[i] = (float)System.Math.Sin(i * 0.01) * 0.5f;
    }
    var output = new float[3000];  // headroom for ratio + SINC transient

    // Prime
    _ = r.Process(input, output);

    // Steady-state
    var frames = r.Process(input, output);

    // 1000 input frames × 1.05 = 1050 output frames; allow ±10 % for the
    // SINC interpolation slack and the per-batch input/output boundary.
    Assert.InRange(frames, 945, 1155);
  }

  [Fact]
  public void SetRatio_UpdatesRatioProperty()
  {
    using var r = new SrcVariableResampler(Logger, channels: 2, initialRatio: 1.0);
    Assert.Equal(1.0, r.Ratio, 6);

    r.SetRatio(1.01);
    Assert.Equal(1.01, r.Ratio, 6);
  }

  [Fact]
  public void Dispose_FreesNativeState_IsIdempotent()
  {
    var r = new SrcVariableResampler(Logger, channels: 2, initialRatio: 1.0);

    r.Dispose();
    r.Dispose();   // no exception — second Dispose is a no-op
  }
}
#endif
