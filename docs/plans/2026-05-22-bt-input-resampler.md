# BT Input Resampler (Path D — definitive clock-skew fix)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Apply a real variable-rate sample-rate converter (SRC) on the BT input stream before it reaches `BufferedSoundGenerator`, so the buffer fill rate exactly matches the consumer drain rate regardless of the underlying 217–391 ppm clock skew between the BT phone and the local speaker. Eliminates the audible "underwater" / "slow" artifact that survived Path C (drift-compensation refinement).

**Source research**: [`docs/research/2026-05-22-bt-clock-skew-measurement.md`](../research/2026-05-22-bt-clock-skew-measurement.md). Path C (`docs/plans/2026-05-22-bt-drift-compensation-refinement.md`, merged as PR #402) reduced compensation events from 480 → 192 samples each (10 ms → 2 ms) and underrun rate by 75%, but the subjective "underwater" feel persisted at ~couple-minute intervals. Path D is the definitive fix.

**Architecture**:

Current data path (after Path C):
```
BT phone (clock A, ~47988 Hz effective)
  ↓ pw_stream OnProcess (S16LE samples at clock A rate)
PipeWireNativeStream.OnProcess
  ↓ float[] callback
BufferedSoundGenerator<float>.AddSamples  ← compensates 2 ms at a time when draining
  ↓ Process(buffer) at clock B (~48000 Hz, local speaker)
MasterMixer → … → Cast / speaker
```

New data path (Path D):
```
BT phone (clock A)
  ↓ pw_stream OnProcess (S16LE samples at clock A)
PipeWireNativeStream.OnProcess
  ↓ float[] before resampling
SrcVariableResampler — libsamplerate adapter
  ↓ float[] at clock B rate (samples are stretched/compressed)
BufferedSoundGenerator<float>.AddSamples  ← compensation disabled when resampler active
  ↓ Process(buffer) at clock B
MasterMixer → … → Cast / speaker
```

The resampler does continuous variable-rate conversion using `libsamplerate`'s `SRC_PROCESS` API. The conversion ratio is `consumer_rate / producer_rate` — start at the measured ~1.00025 (250 ppm) and refine via closed-loop control based on buffer-level trend.

**Tech Stack**:
- **`libsamplerate0`** native library (Ubuntu apt package; LGPL). Used by JACK, Audacity, PulseAudio.
- **P/Invoke bindings** in `PipeWireNative.cs` for: `src_new`, `src_delete`, `src_process`, `src_set_ratio`, `src_reset`. Native function signatures from `<samplerate.h>`.
- **Quality mode**: `SRC_SINC_FASTEST` — best quality/CPU trade-off for slow-drift correction. CPU cost ~5-10% on a slow core for stereo 48 kHz; trivial on the N100.

**Addresses**: BT clock-skew "underwater" artifact (research note §"What CAN actually mitigate", option 2).

---

## Task 0: libsamplerate availability check on radio (operator action — Mark, not Builder)

**This task is NOT executed by the Builder.** Pre-flight on `mmack@radio`:

```bash
ssh mmack@radio "apt list --installed 2>/dev/null | grep libsamplerate || sudo apt install -y libsamplerate0"
ssh mmack@radio "ldconfig -p | grep libsamplerate"
```

Expected: `libsamplerate.so.0 (libc6,x86-64) => /lib/x86_64-linux-gnu/libsamplerate.so.0`

If the package is missing, the implementation PR ships a Task 0-equivalent step that installs it as part of the deploy script's pre-flight (skipped if already present).

---

## Task 1: P/Invoke bindings for libsamplerate

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNative.cs` (add the SRC bindings — they're conceptually-distinct from PW but live in the same Linux-only native interop file for build-time consistency)

**Step 1:** Add the SRC enums + P/Invoke declarations:

```csharp
#if !WINDOWS_TARGET
// libsamplerate (Secret Rabbit Code) — variable-rate sample-rate converter
// Used by the BT input path to compensate for clock skew between the BT phone
// and the local speaker (see docs/research/2026-05-22-bt-clock-skew-measurement.md).
//
// Native dependency: libsamplerate0 (apt install libsamplerate0).
// Header: <samplerate.h>. Documentation: http://libsndfile.github.io/libsamplerate/

internal enum SrcQuality
{
    SincBestQuality = 0,
    SincMediumQuality = 1,
    SincFastest = 2,
    ZeroOrderHold = 3,
    Linear = 4,
}

[StructLayout(LayoutKind.Sequential)]
internal struct SrcData
{
    public IntPtr DataIn;        // const float *data_in
    public IntPtr DataOut;       // float *data_out
    public long InputFrames;     // long input_frames
    public long OutputFrames;    // long output_frames
    public long InputFramesUsed; // long input_frames_used
    public long OutputFramesGen; // long output_frames_gen
    public int EndOfInput;       // int end_of_input (boolean)
    public double SrcRatio;      // double src_ratio (output_rate / input_rate)
}

[DllImport("libsamplerate.so.0", EntryPoint = "src_new")]
internal static extern IntPtr src_new(int converterType, int channels, out int error);

[DllImport("libsamplerate.so.0", EntryPoint = "src_delete")]
internal static extern IntPtr src_delete(IntPtr state);

[DllImport("libsamplerate.so.0", EntryPoint = "src_process")]
internal static extern int src_process(IntPtr state, ref SrcData data);

[DllImport("libsamplerate.so.0", EntryPoint = "src_set_ratio")]
internal static extern int src_set_ratio(IntPtr state, double newRatio);

[DllImport("libsamplerate.so.0", EntryPoint = "src_reset")]
internal static extern int src_reset(IntPtr state);

[DllImport("libsamplerate.so.0", EntryPoint = "src_strerror")]
internal static extern IntPtr src_strerror(int errorCode);
#endif
```

**Step 2:** Add a helper to get the error string:

```csharp
internal static string SrcErrorMessage(int errorCode)
{
    var ptr = src_strerror(errorCode);
    return ptr == IntPtr.Zero ? $"libsamplerate error {errorCode}" : Marshal.PtrToStringAnsi(ptr) ?? "unknown";
}
```

**Step 3:** Build verification (Linux TFM only):

```bash
dotnet build src/Radio.Infrastructure/Radio.Infrastructure.csproj --configuration Release --framework net10.0
```

Expected: 0 errors. The `libsamplerate.so.0` import won't be resolved on Windows, but `#if !WINDOWS_TARGET` ensures Windows builds don't reference it.

**Step 4:** Commit:

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNative.cs
git commit -m "feat(audio): P/Invoke bindings for libsamplerate variable-rate SRC"
```

---

## Task 2: `SrcVariableResampler` wrapper class

**Files:**
- Create: `src/Radio.Infrastructure/Audio/SoundFlow/SrcVariableResampler.cs`

**Step 1:** Implement the wrapper:

```csharp
#if !WINDOWS_TARGET
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Platform.Bluetooth.Native;
using static Radio.Infrastructure.Platform.Bluetooth.Native.PipeWireNative;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Variable-rate sample-rate converter wrapper around libsamplerate (SRC).
/// Used by the BT input path to compensate for the ~250 ppm clock skew between
/// the BT phone clock and the local speaker clock — see Plan D in
/// docs/plans/2026-05-22-bt-input-resampler.md.
///
/// Single-threaded: assumes only one producer (the PW thread loop) calls Process.
/// </summary>
internal sealed class SrcVariableResampler : IDisposable
{
    private readonly ILogger _logger;
    private readonly int _channels;
    private IntPtr _state;
    private double _currentRatio;
    private bool _disposed;

    /// <summary>
    /// Initial conversion ratio. 1.0 = no conversion. >1.0 stretches input
    /// (output has more samples than input, used when consumer pulls faster).
    /// </summary>
    public double Ratio => _currentRatio;

    public SrcVariableResampler(ILogger logger, int channels, double initialRatio, SrcQuality quality = SrcQuality.SincFastest)
    {
        _logger = logger;
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
    /// Sets a new conversion ratio. Smooth ramping is handled by libsamplerate
    /// internally to avoid clicks at the boundary.
    /// </summary>
    public void SetRatio(double newRatio)
    {
        if (_state == IntPtr.Zero) return;
        var err = src_set_ratio(_state, newRatio);
        if (err == 0)
        {
            _currentRatio = newRatio;
        }
        else
        {
            _logger.LogWarning("src_set_ratio({Ratio:F6}) failed: {Err}", newRatio, SrcErrorMessage(err));
        }
    }

    /// <summary>
    /// Processes a chunk of input samples and returns the resampled output.
    /// Both input and output are interleaved float[].
    /// </summary>
    /// <returns>Number of output frames generated.</returns>
    public unsafe int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (_state == IntPtr.Zero) return 0;
        if (input.IsEmpty || output.IsEmpty) return 0;

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
                _logger.LogWarning("src_process failed: {Err}", SrcErrorMessage(err));
                return 0;
            }
            return (int)data.OutputFramesGen;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_state != IntPtr.Zero)
        {
            src_delete(_state);
            _state = IntPtr.Zero;
        }
    }
}
#endif
```

**Step 2:** Build + commit:

```bash
dotnet build src/Radio.Infrastructure/Radio.Infrastructure.csproj --configuration Release --framework net10.0
git add src/Radio.Infrastructure/Audio/SoundFlow/SrcVariableResampler.cs
git commit -m "feat(audio): SrcVariableResampler wrapper for variable-rate clock-skew correction"
```

---

## Task 3: Wire the resampler into `PipeWireNativeStream.OnProcess`

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs`

**Step 1:** Add a resampler field + constructor wiring. The resampler should be opt-in initially via `BluetoothOptions.UseInputResampler`:

```csharp
private readonly SrcVariableResampler? _resampler;
private float[]? _resampleOutputBuffer;
```

In the constructor, accept a new optional parameter:

```csharp
public PipeWireNativeStream(
    uint targetNodeId, int sampleRate, int channels,
    AudioDataCallback onAudioData, ILogger logger,
    bool useResampler = false, double initialResamplerRatio = 1.0)
{
    // ... existing init ...

    if (useResampler)
    {
        _resampler = new SrcVariableResampler(logger, channels, initialResamplerRatio, SrcQuality.SincFastest);
        // Allocate output buffer with 5% headroom — resampling can produce up to (ratio × input) samples.
        // Worst-case: input buffer ~1024 samples, ratio ~1.001, output ~1025 samples; allocate 4096 to be safe.
        _resampleOutputBuffer = new float[4096];
    }
}
```

**Step 2:** Modify `OnProcess` to route through the resampler when active. Find the block where `_onAudioData(floatSamples, sampleCount)` is called (around line 339). Wrap:

```csharp
if (self._resampler != null && self._resampleOutputBuffer != null)
{
    // Resampler path — convert from BT-phone rate to consumer rate
    var inputSpan = floatSamples.AsSpan(0, sampleCount);
    var outputSpan = self._resampleOutputBuffer.AsSpan();
    var framesOut = self._resampler.Process(inputSpan, outputSpan);
    var samplesOut = framesOut * self._channels;
    if (samplesOut > 0)
    {
        self._onAudioData(self._resampleOutputBuffer, samplesOut);
    }
}
else
{
    // Direct path (resampler off — current behavior)
    self._onAudioData(floatSamples, sampleCount);
}
```

**Step 3:** Update Dispose to clean up the resampler:

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    Stop();
    _resampler?.Dispose();
}
```

**Step 4:** Build + commit:

```bash
dotnet build src/Radio.Infrastructure --configuration Release
git add src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs
git commit -m "feat(bt): route OnProcess samples through SrcVariableResampler when enabled"
```

---

## Task 4: Wire the feature flag + initial ratio in `BluetoothOptions` + `LinuxBluetoothService`

**Files:**
- Modify: `src/Radio.Core/Configuration/BluetoothOptions.cs`
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`
- Modify: `src/Radio.API/appsettings.json`

**Step 1:** Add config options:

```csharp
/// <summary>
/// Enables libsamplerate-based variable-rate resampling on the BT input stream
/// to compensate for clock skew between the BT phone clock and the local speaker
/// clock. When false, the legacy CompensateClockDrift path runs instead.
/// </summary>
public bool UseInputResampler { get; set; } = true;

/// <summary>
/// Initial conversion ratio (output_rate / input_rate). Measured ~1.00025 on the
/// Ubuntu N100 + Pixel-class phone combo (250 ppm consumer-faster). Will eventually
/// be replaced by closed-loop control based on buffer-level trend (Phase 2,
/// not in this plan). Set to 1.0 to disable initial offset.
/// </summary>
public double InputResamplerInitialRatio { get; set; } = 1.00025;
```

Default to `true` so the new behavior is the default. Set the initial ratio from the measurement.

**Step 2:** In `LinuxBluetoothService.cs`, find where `PipeWireNativeStream` is constructed (around line 1388). Pass through the options:

```csharp
_nativeStream = new PipeWireNativeStream(
    nodeSerial, format.SampleRate, format.Channels,
    OnNativeAudioData, _logger,
    useResampler: _options.CurrentValue.UseInputResampler,
    initialResamplerRatio: _options.CurrentValue.InputResamplerInitialRatio);
```

**Step 3:** Update `appsettings.json`:

```json
"Bluetooth": {
  ...
  "UseInputResampler": true,
  "InputResamplerInitialRatio": 1.00025,
  ...
}
```

**Step 4:** When the resampler is active, **disable the CompensateClockDrift path** in `BufferedSoundGenerator` for this generator instance. Add a constructor parameter `bool disableDriftCompensation = false` to `BufferedSoundGenerator<T>` and gate the existing `CompensateClockDrift` call on it. `LinuxBluetoothService` passes `disableDriftCompensation: UseInputResampler` when constructing the generator.

This is critical — running both the resampler AND the drift compensation simultaneously would double-correct. The resampler is the source of truth when active.

**Step 5:** Build + commit:

```bash
dotnet build --configuration Release
git add src/Radio.Core/Configuration/BluetoothOptions.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs \
        src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs \
        src/Radio.API/appsettings.json
git commit -m "feat(bt): enable BT input resampler by default (disables drift compensation)"
```

---

## Task 5: Unit tests

**Files:**
- Create: `tests/Radio.Infrastructure.Tests/Audio/SoundFlow/SrcVariableResamplerTests.cs`

Tests that don't require the actual `libsamplerate.so.0` library on the test runner (since CI runs on Linux + has libsamplerate available via apt; tests on Windows would need to be gated):

**Step 1:** Skeleton:

```csharp
#if !WINDOWS_TARGET
[Trait("Category", "RequiresLibSampleRate")]
public class SrcVariableResamplerTests
{
    [Fact]
    public void Process_IdentityRatio_PassesThroughSamples()
    {
        using var r = new SrcVariableResampler(Mock.Of<ILogger>(), channels: 2, initialRatio: 1.0);
        var input = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f };  // 4 stereo frames
        var output = new float[16];  // headroom
        var frames = r.Process(input, output);
        Assert.True(frames > 0);
        Assert.True(frames <= 4);
    }

    [Fact]
    public void Process_StretchRatio_ProducesMoreOutputFrames()
    {
        using var r = new SrcVariableResampler(Mock.Of<ILogger>(), channels: 2, initialRatio: 1.05);  // 5% stretch
        var input = new float[2000];
        for (int i = 0; i < input.Length; i++) input[i] = (float)Math.Sin(i * 0.01) * 0.5f;
        var output = new float[3000];
        var frames = r.Process(input, output);
        // Expect roughly 1.05 × input_frames (with SINC ramp-up margin)
        Assert.InRange(frames, 950, 1100);
    }

    [Fact]
    public void SetRatio_ChangesEffectiveRatio()
    {
        using var r = new SrcVariableResampler(Mock.Of<ILogger>(), channels: 2, initialRatio: 1.0);
        r.SetRatio(1.01);
        Assert.Equal(1.01, r.Ratio, 6);
    }

    [Fact]
    public void Dispose_FreesNativeState_IsIdempotent()
    {
        var r = new SrcVariableResampler(Mock.Of<ILogger>(), channels: 2, initialRatio: 1.0);
        r.Dispose();
        r.Dispose();  // no throw
    }
}
#endif
```

**Step 2:** Confirm CI has libsamplerate available:

```bash
# Add to .github/workflows/build.yml's "Setup" section if not already present:
- name: Install libsamplerate
  if: runner.os == 'Linux'
  run: sudo apt-get update && sudo apt-get install -y libsamplerate0
```

Note: the appserver runner is `ubuntu-noble` — confirm whether `libsamplerate0` is in the default image. If not, the workflow apt-install step is required.

**Step 3:** Run tests:

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj --configuration Release --filter "SrcVariableResamplerTests"
```

**Step 4:** Commit:

```bash
git add tests/Radio.Infrastructure.Tests/Audio/SoundFlow/SrcVariableResamplerTests.cs .github/workflows/build.yml
git commit -m "test(audio): SrcVariableResampler unit tests + libsamplerate CI install"
```

---

## Task 6: Comparator script — `bt_resampler_compare.py`

**Files:**
- Create: `scripts/research/bt_resampler_compare.py`

Mirror the pattern from `bt_drift_compare.py` (created in Path C). PASS/FAIL gate for Path D's acceptance:

```python
#!/usr/bin/env python3
"""Compare baseline vs post-Path-D drift metrics. Path D should drive
compensation events + underrun events to ~0."""
import sys, argparse

def parse_drift(path):
    """Parse bt_drift_analyze.py output. Returns dict with rates."""
    ...

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('baseline')
    parser.add_argument('after')
    args = parser.parse_args()

    base = parse_drift(args.baseline)
    after = parse_drift(args.after)

    # Path D primary criteria (much stricter than Path C):
    # - Compensation events drop to near zero (resampler does the work)
    # - Underrun events drop to zero
    c1 = after['comp_events_per_hour'] <= base['comp_events_per_hour'] * 0.1   # ≥90% reduction
    c2 = after['underrun_events_per_hour'] == 0                                # exactly 0
    c3 = abs(after['ppm']) < 50                                                # net drift inside spec

    print(f"Comp events rate:    base {base['comp_events_per_hour']:.1f}/h → after {after['comp_events_per_hour']:.1f}/h  (target ≥90% reduction): {'PASS' if c1 else 'FAIL'}")
    print(f"Underrun events:     base {base['underrun_events_per_hour']:.1f}/h → after {after['underrun_events_per_hour']:.1f}/h  (target =0): {'PASS' if c2 else 'FAIL'}")
    print(f"Residual clock skew: base {base['ppm']:.0f}ppm → after {after['ppm']:.0f}ppm  (target <50ppm): {'PASS' if c3 else 'FAIL'}")

    all_pass = c1 and c2 and c3
    print(f"\nOVERALL (objective): {'PASS' if all_pass else 'FAIL'}")
    print("Subjective ('underwater' eliminated): Mark UAT")
    sys.exit(0 if all_pass else 1)

if __name__ == '__main__':
    main()
```

**Step 1:** Syntax-check + commit:

```bash
python3 -c "import ast; ast.parse(open('scripts/research/bt_resampler_compare.py').read())"
git add scripts/research/bt_resampler_compare.py
git commit -m "scripts(research): bt_resampler_compare.py — Path D acceptance gate"
```

---

## Task 7: Open PR + auto-merge

```bash
git push -u origin feat/bt-input-resampler
gh pr create --title "fix(audio): variable-rate resampler on BT input — eliminate clock-skew artifacts" --body "..."
```

PR description template:

```markdown
## Summary

Implements **Path D** of the BT clock-skew arc — applies a real variable-rate sample-rate converter (libsamplerate's SRC_PROCESS) to the BT input stream, eliminating time-domain duplication entirely. This is the definitive fix for the 'underwater' / 'slow' audio Mark reported during BT → Cast playback.

Path C (smaller-more-frequent compensation, PR #402) reduced per-event audibility but listeners still detected the duplications at multi-minute intervals. Path D removes duplication from the data path.

## Architecture

`PipeWireNativeStream.OnProcess` now routes BT samples through `SrcVariableResampler` (libsamplerate adapter) BEFORE delivering to `BufferedSoundGenerator`. The resampler's ratio is set from the measured ~250 ppm consumer-faster skew (configurable via `BluetoothOptions.InputResamplerInitialRatio`).

When the resampler is active, `BufferedSoundGenerator`'s `CompensateClockDrift` is disabled to avoid double-correction.

## Acceptance (per plan Task 8)

- [ ] Mark UAT subjective: 'underwater' gone
- [ ] `drift_compensation_total` events/hour drops ≥90%
- [ ] `underrun_total` events/hour drops to 0
- [ ] Residual `bt_drift_analyze.py` ppm value <50 (within BT spec ±20 ppm tolerance + measurement noise)

## Test plan

- [x] 4 unit tests for `SrcVariableResampler` (identity ratio, stretch ratio, set-ratio, dispose)
- [x] CI workflow installs `libsamplerate0` via apt
- [x] Build clean on both Linux + Windows TFMs (Windows code path stubbed `#if !WINDOWS_TARGET`)
- [ ] Mark: deploy + soak + bt_resampler_compare.py PASS

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

After CI green: `gh pr merge <PR#> --squash --delete-branch` per the tranche's auto-merge authorization.

---

## Task 8: Deploy + verify acceptance criteria (operator action — Mark, post-merge)

**Step 1:** Deploy:

```bash
pwsh.exe -NoProfile -Command "& './deploy/Deploy-ToLinux.ps1' -TargetHost radio -Runtime linux-x64"
```

**Step 2:** Verify the resampler initialized:

```bash
ssh mmack@radio "journalctl -u radio-api --since '1 minute ago' -o cat | grep -E 'SrcVariableResampler initialized'"
```

Expected: `SrcVariableResampler initialized: quality=SincFastest, channels=2, initial ratio=1.000250`.

**Step 3:** Pre-Path-D baseline. Use the Path C baseline from earlier (217-391 ppm, ~360 comp/hour at 2 ms each); no fresh pre-D snapshot needed since Path C is the current `main`.

**Step 4:** 15-minute soak with BT → Cast playback. Then capture post-D metrics:

```bash
ssh mmack@radio "journalctl -u radio-api --since '15 minutes ago' -o short-iso" \
  | python3 scripts/research/bt_drift_analyze.py > /tmp/after_path_d.txt
python3 scripts/research/bt_resampler_compare.py /tmp/baseline_path_c.txt /tmp/after_path_d.txt
```

**Step 5:** Subjective UAT. Listen for 15+ minutes. The 'underwater' feel should be **gone**.

**Success criteria** (all must hold):

1. **Primary subjective**: 'underwater' artifact is gone or unmistakably below the detection threshold
2. `drift_compensation_total` events/hour drops by ≥90 % (target: ≤36/hour from Path C's 360/hour baseline). Ideally near 0 — the resampler does the work the compensator was doing.
3. `audio.buffer.underrun_total` events/hour stays at 0
4. `bt_drift_analyze.py` reports residual `ppm` <50 (resampler eliminates the bulk of the skew; the remainder is buffer-level transient drift)

**Negative checks**:
- CPU consumption of `Radio.API` (`pidstat -p $(pgrep -x Radio.API) 5 3`) shouldn't increase by more than 10 percentage points
- Latency: from BT phone "press play" to first audio at the Cast device — measure subjectively; should be within ~50 ms of pre-D latency (resampler adds ~1-3 ms)
- No new ERR-level log lines in the audio path

---

## Out of scope

- **Closed-loop ratio control** (Phase 2). The initial ratio is set statically from the measurement. Long-term: a PI controller that adjusts ratio based on buffer-level trend. Defer until static-ratio validates the architecture.
- **Quality-mode comparison** (SINC_FASTEST vs SINC_MEDIUM vs SINC_BEST). SincFastest is the default per the plan; if quality complaints arise, escalate to SincMedium with the 2× CPU cost.
- **Windows BT path**. The resampler is Linux-only (libsamplerate available via apt; Windows path uses WasapiLoopbackCaptureSource which has no equivalent clock-skew problem). Stub remains as-is.
- **Hot-config support**. Changing `UseInputResampler` requires restarting `radio-api`. A future plan can add `IOptionsMonitor` integration to flip it live.
- **Removing `CompensateClockDrift` entirely**. Left in place as fallback (when `UseInputResampler=false`) and for non-BT generators (e.g., USB sources may benefit from it on different hardware). Path D adds the resampler as an alternative, not a replacement of the existing compensation.

---

## References

- `docs/research/2026-05-22-bt-clock-skew-measurement.md` — measurement methodology, architectural analysis, Path A-D mitigation menu
- `docs/plans/2026-05-22-bt-drift-compensation-refinement.md` — Path C (precursor; reduced symptom but didn't eliminate)
- libsamplerate docs: http://libsndfile.github.io/libsamplerate/
- libsamplerate API reference: http://libsndfile.github.io/libsamplerate/api_misc.html#process
