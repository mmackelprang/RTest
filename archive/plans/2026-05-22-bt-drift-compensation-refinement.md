# BT drift-compensation refinement (Path C)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Make `BufferedSoundGenerator<T>.CompensateClockDrift` produce sub-perceptual corrections instead of the current 10 ms duplications that listeners hear as "underwater" / "slow" audio during BT → Cast playback. Validated against the metrics PR #400 introduced.

**Source research**: [`docs/research/2026-05-22-bt-clock-skew-measurement.md`](../research/2026-05-22-bt-clock-skew-measurement.md) — measured 217–391 ppm BT-vs-speaker clock skew driving 120 compensation events / hour, ~10 ms of repeated audio per event.

**Architecture**: refinement to `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs:477-545`. Two independent changes:

1. **Smaller, more-frequent corrections.** Replace the "wait until buffer hits 15 %, then duplicate 10 ms" model with "every Process call where drain is detected, duplicate 1-2 ms with crossfade smoothing". The total compensated samples per minute stays the same (it has to, the underlying rate skew is unchanged); the per-event audible artifact drops below the perceptual threshold.

2. **Linear crossfade across the rewind boundary.** Currently, when `_readPos` is rewound by `deficit` samples, the next read starts at the rewind point with a hard cut. A simple cosine-ramp crossfade across ~32 samples (~0.7 ms at 48 kHz) blends the boundary, eliminating the discontinuity click that contributes to the "underwater" perception.

**Tech Stack**: `BufferedSoundGenerator<T>` (existing). No new dependencies. No DSP library required for either change — both are simple ring-buffer pointer arithmetic + a small per-sample multiply for the crossfade.

**Addresses**: BT clock-skew artifacts ("underwater" audio) documented in the research doc + measured via PR #400 metrics.

---

## Task 0: Confirm baseline metrics before any change

Before editing code, capture a 15-minute baseline against the *current* implementation deployed on `radio`. We need this artifact as the comparison reference.

**Step 1:** SSH to `mmack@radio`. Confirm radio-api is currently streaming BT → Cast (look at `journalctl -u radio-api -o cat | tail -20` and verify recent `BluetoothCodec` / `Clock drift compensation` log lines).

**Step 2:** Run the probe:

```bash
ssh mmack@radio "journalctl -u radio-api --since '15 minutes ago' -o short-iso" \
  | python3 scripts/research/bt_drift_analyze.py > baseline_drift_15min.txt
```

**Step 3:** Capture the metric counters too:

```bash
ssh mmack@radio "sqlite3 /opt/radio-console/data/metrics.db \
  \"SELECT md.Key, SUM(mm.ValueSum) FROM MetricData_Minute mm JOIN MetricDefinitions md ON mm.MetricId=md.Id WHERE md.Key LIKE 'audio.buffer.%' AND mm.Timestamp > strftime('%s','now','-15 minutes')*1000 GROUP BY md.Key;\"" \
  > baseline_metrics_15min.txt
```

**Step 4:** Commit both artifacts:

```bash
git add baseline_drift_15min.txt baseline_metrics_15min.txt
git commit -m "test: baseline drift-compensation metrics (pre-refinement, PR #400-instrumented)"
```

(Actually no — these artifacts shouldn't go in the repo. Keep them locally in `/tmp/` for now and reference them in the PR description.)

**Exit condition**: baseline artifacts saved locally. The PR description references the baseline numbers.

---

## Task 1: Smaller, more-frequent corrections

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs:477-545`

**Step 1:** Replace the existing `CompensateClockDrift` body. Two changes:

a) **Always run on every Process call** (remove the `(now - _lastDriftCheckTime).TotalSeconds < 2.0` early-return at lines 491-494). The 2-second cooldown was there to avoid reacting to transient jitter; with smaller per-event corrections the cooldown is unnecessary.

b) **Cap each correction at `MaxCompensationPerCallSamples = (int)(Format.SampleRate * Format.Channels * 0.002)`** — 2 ms instead of 10 ms. This is the key change. The total compensated samples per minute stays roughly the same (because it MUST — the underlying rate mismatch is unchanged), but each event is much shorter and harder to perceive.

The new method body (sketch — adapt to existing field naming):

```csharp
private void CompensateClockDrift(int channels)
{
  var now = DateTime.UtcNow;
  if (_lastDriftCheckTime == default)
  {
    _lastDriftCheckTime = now;
    lock (_bufferLock)
    {
      _lastDriftCheckLevel = _count;
    }
    return;
  }

  int currentLevel;
  lock (_bufferLock)
  {
    currentLevel = _count;
  }

  _driftCheckCount++;

  // Compensate when buffer is below threshold AND draining
  var isDraining = currentLevel < _driftCompensationThreshold
                   && currentLevel < _lastDriftCheckLevel
                   && _driftCheckCount > 3
                   && _totalSamplesReceived > 0;

  if (isDraining)
  {
    // Per-call cap: 2 ms of audio. Smaller = less audible per event,
    // even though the total samples compensated per second stays roughly
    // the same (driven by the underlying rate mismatch).
    var maxCompensationPerCall = (int)(Format.SampleRate * Format.Channels * 0.002);
    var deficit = Math.Min(_driftCompensationTarget - currentLevel, maxCompensationPerCall);
    var frameSamples = Math.Max(channels, 2);
    deficit = (deficit / frameSamples) * frameSamples;

    if (deficit > 0)
    {
      // Task 2 will add crossfade here. For now: existing rewind logic
      // operating on the smaller `deficit`.
      lock (_bufferLock)
      {
        if (_count + deficit <= _maxBufferSamples)
        {
          _readPos = (_readPos - deficit + _maxBufferSamples) % _maxBufferSamples;
          _count += deficit;
          _totalSamplesCompensated += deficit;
        }
      }

      // Counter + throttled log (existing pattern; preserved from PR #400)
      if (_metricsCollector != null && _metricsTags != null)
      {
        _metricsCollector.Increment("audio.buffer.drift_compensation_total", 1, _metricsTags);
        _metricsCollector.Increment("audio.buffer.drift_compensation_samples_total", deficit, _metricsTags);
      }
      _compensationCountSinceLastLog++;
      _compensationSamplesSinceLastLog += deficit;
      // ...rest of throttled log block unchanged...
    }
  }

  _lastDriftCheckLevel = currentLevel;
  _lastDriftCheckTime = now;
}
```

**Step 2:** Build clean.

```bash
dotnet build src/Radio.Infrastructure/Radio.Infrastructure.csproj --configuration Release
```

Expected: 0 errors. Pre-existing IDE0011 warnings only.

**Step 3:** Commit:

```bash
git add src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs
git commit -m "fix(audio): smaller-more-frequent drift corrections (2ms vs 10ms per event)"
```

---

## Task 2: Linear crossfade across the rewind boundary

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs` (the new `CompensateClockDrift` body from Task 1)

**Step 1:** When rewinding `_readPos`, the samples immediately AFTER the rewind point will be replayed. The discontinuity is between the LAST sample read before rewind, and the FIRST sample read after rewind (which is the duplicated content). This boundary is the click/pop.

Approach: **apply a cosine-ramp crossfade across `CrossfadeSamples = 32` samples** (~0.67 ms at 48 kHz). On the duplicated chunk, the first 32 samples ramp up from 0 to 1 multiplied with the original signal. This makes the boundary blend smoothly. Cost: 32 multiplies per compensation event = negligible.

The crossfade must operate on the buffer DATA, not the read position. So after rewinding `_readPos`, the next 32 samples that get READ from the buffer need to be attenuated. Two implementation strategies:

**(a) Buffer-write approach**: write the attenuated values directly back into the ring buffer at the rewound position. Simple but mutates the buffer (problematic if there are other readers).

**(b) Read-time approach**: track that we just did a rewind, and apply the ramp during the next 32 samples of `Process(buffer)`. More complex but doesn't mutate the buffer.

Choose **(a)** — `BufferedSoundGenerator<T>` is single-consumer (the master mixer), so buffer mutation is safe. The implementation:

```csharp
private const int CrossfadeSamples = 32;  // ~0.67ms at 48kHz

// Inside the `if (deficit > 0)` block, after the lock that rewinds _readPos:
if (typeof(T) == typeof(float))
{
  ApplyCrossfadeFloat(_readPos, Math.Min(CrossfadeSamples, deficit));
}
// else: short path — phase 2 deferral, log-only for non-float types

// New method:
private void ApplyCrossfadeFloat(int startPos, int rampLength)
{
  if (rampLength <= 0) return;
  var floatBuffer = _ringBuffer as float[];
  if (floatBuffer == null) return;

  lock (_bufferLock)
  {
    for (int i = 0; i < rampLength; i++)
    {
      // Cosine ramp: 0 → 1 over rampLength samples
      var phase = (i / (double)rampLength) * Math.PI;
      var gain = (float)((1.0 - Math.Cos(phase)) * 0.5);
      var idx = (startPos + i) % _maxBufferSamples;
      floatBuffer[idx] = floatBuffer[idx] * gain;
    }
  }
}
```

**Caveat — channel-pair alignment**: the crossfade should ideally be applied per-channel-pair (don't split a stereo frame). For stereo at 48 kHz, 32 samples = 16 stereo frames — already frame-aligned. For mono, 32 samples = 32 frames. Both are fine. If channel count is > 2, ensure `rampLength` is frame-aligned: `rampLength = (rampLength / channels) * channels;`.

**Step 2:** Build + commit:

```bash
dotnet build src/Radio.Infrastructure/Radio.Infrastructure.csproj --configuration Release
git add src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs
git commit -m "fix(audio): cosine-ramp crossfade across drift-compensation rewind boundary"
```

---

## Task 3: Unit tests

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/SoundFlow/BufferedSoundGeneratorTests.cs` (or wherever PR #400's tests live)

**Step 1:** Add three tests:

a) **`CompensateClockDrift_PerCallCap_LimitsToTwoMillisecondsOfSamples`** — populate a generator with float data, drain the buffer below threshold, call `Process` repeatedly, assert that `_totalSamplesCompensated` increases in increments ≤ `(int)(Format.SampleRate * Format.Channels * 0.002)`.

b) **`CompensateClockDrift_AppliesCrossfade_OnFloatRingBuffer`** — set up a buffer with a known signal (all 1.0f), drain to threshold, trigger compensation, assert that the first 32 samples after the rewind point have been attenuated by the cosine ramp (sample 0 ≈ 0.0, sample 16 ≈ 0.5, sample 31 ≈ 0.99).

c) **`CompensateClockDrift_NoLongerRunsLessOftenThan2Seconds`** — populate a draining buffer, call `Process` 10× in rapid succession (no `Task.Delay` between), assert that `_totalSamplesCompensated > 0` even though < 2 s elapsed (the new code path runs on every call when draining).

**Step 2:** Run tests:

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj --configuration Release --filter "BufferedSoundGeneratorTests"
```

Expected: all pre-existing + 3 new tests PASS.

**Step 3:** Commit:

```bash
git add tests/Radio.Infrastructure.Tests/Audio/SoundFlow/BufferedSoundGeneratorTests.cs
git commit -m "test(audio): drift-compensation refinement coverage (per-call cap + crossfade)"
```

---

## Task 4: Full build + test verification

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```

Expected: 0 errors, ~0 new warnings, ~2,200+ tests pass.

If `ModePicker_ClickingFall/Phase/Ring`, `AutoPill_Click_*`, or `Reset_Click_*` fails — that's the documented bUnit click-test flake family. Re-run failed jobs via `gh run rerun <run_id> --failed` after CI; only escalate after 3 consecutive same-test failures.

---

## Task 5: Deploy + verify acceptance criteria

**This task is operator-run by Mark** (Builder reports completion at end of Task 4 + opens the PR; Mark runs Task 5).

**Step 1:** After PR merge, deploy:

```bash
pwsh.exe -NoProfile -Command "& './deploy/Deploy-ToLinux.ps1' -TargetHost radio -Runtime linux-x64"
```

**Step 2:** Verify service restarted with new behavior:

```bash
ssh mmack@radio "journalctl -u radio-api --since '2 minutes ago' -o cat | grep -E '🔄 Clock drift compensation'" | head -5
```

Expected: log lines show smaller `duplicated` sample counts per event (target: ~96 samples = 2 ms at 48 kHz stereo) and higher event rate.

**Step 3:** Capture post-change metrics over a fresh 15-minute window of BT → Cast playback:

```bash
sleep 900   # 15 minutes
ssh mmack@radio "journalctl -u radio-api --since '15 minutes ago' -o short-iso" \
  | python3 scripts/research/bt_drift_analyze.py > after_drift_15min.txt
ssh mmack@radio "sqlite3 /opt/radio-console/data/metrics.db \
  \"SELECT md.Key, SUM(mm.ValueSum) FROM MetricData_Minute mm JOIN MetricDefinitions md ON mm.MetricId=md.Id WHERE md.Key LIKE 'audio.buffer.%' AND mm.Timestamp > strftime('%s','now','-15 minutes')*1000 GROUP BY md.Key;\"" \
  > after_metrics_15min.txt
```

**Step 4:** Compare against the Task 0 baseline.

**Success criteria** (in priority order):

1. **PRIMARY (subjective)**: Mark reports the "underwater" / "slow" feel during BT → Cast playback is gone or substantially reduced. *This is the ultimate criterion — if subjective improvement is not achieved, escalate to Path D regardless of the objective numbers.*

2. **OBJECTIVE — compensation event distribution shifts to smaller events**:
   - `audio.buffer.drift_compensation_total` events/hour **increases by ≥3×** (target: ≥360/hour vs baseline ~120/hour) — this is GOOD, smaller more-frequent corrections are the goal
   - `audio.buffer.drift_compensation_samples_total` samples/hour stays **within ±20 %** of baseline (target: 30–48 KB samples/hour vs baseline ~39 KB samples/hour) — confirms the same total compensation is being done, just redistributed
   - Per-event sample count (computed: `drift_compensation_samples_total / drift_compensation_total`) drops from ~96 samples (10 ms / 2) to ~96 samples (2 ms / 2) — wait, that's the same number. Computed: ratio drops from ~325 samples/event (current 10 ms cap with 4-channel-wide path) to ~65 samples/event (2 ms cap) — a ~5× reduction in per-event size

3. **OBJECTIVE — underrun rate drops**:
   - `audio.buffer.underrun_total` events/hour drops by **≥50 %** (target: ≤25/hour vs baseline 50/hour) — smaller more-frequent corrections should keep the buffer further from zero on average

4. **NEGATIVE CHECK — no new failure mode**:
   - `audio.buffer.fill_percent` mean stays roughly the same (within ±10 percentage points of baseline)
   - No new ERR/WRN log lines in the audio path beyond the existing ones

**Debug-agent verification**:

```bash
python3 scripts/research/bt_drift_compare.py baseline_drift_15min.txt after_drift_15min.txt baseline_metrics_15min.txt after_metrics_15min.txt
```

(`bt_drift_compare.py` is a new script — see Task 6.)

---

## Task 6: Comparator script for Task 5

**Files:**
- Create: `scripts/research/bt_drift_compare.py`

**Step 1:** Mirror the pattern of the existing `bt_stall_compare.py` from PR #390. Read two `bt_drift_analyze.py` artifacts + two metric-snapshot artifacts; output a PASS/FAIL summary against the criteria from Task 5.

Structure:

```python
#!/usr/bin/env python3
"""Compare baseline vs post-change drift metrics for Path C acceptance."""
import sys, re, argparse

def parse_drift_artifact(path):
    """Returns dict: {underrun_events_per_hour, comp_events_per_hour, ppm}."""
    # parse the bt_drift_analyze.py output format
    ...

def parse_metrics_artifact(path):
    """Returns dict: {key -> sum_value}."""
    # parse the sqlite output lines: "key|value"
    ...

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('baseline_drift')
    parser.add_argument('after_drift')
    parser.add_argument('baseline_metrics')
    parser.add_argument('after_metrics')
    args = parser.parse_args()

    base_drift = parse_drift_artifact(args.baseline_drift)
    after_drift = parse_drift_artifact(args.after_drift)
    base_m = parse_metrics_artifact(args.baseline_metrics)
    after_m = parse_metrics_artifact(args.after_metrics)

    # Criterion 2: comp events × at least 3
    comp_ratio = after_drift['comp_events_per_hour'] / max(base_drift['comp_events_per_hour'], 0.1)
    c2_events = comp_ratio >= 3.0
    # Criterion 2: comp samples within ±20%
    sample_ratio = after_m['audio.buffer.drift_compensation_samples_total'] / max(base_m['audio.buffer.drift_compensation_samples_total'], 1)
    c2_samples = 0.8 <= sample_ratio <= 1.2
    # Criterion 3: underrun events drop ≥50%
    underrun_ratio = after_drift['underrun_events_per_hour'] / max(base_drift['underrun_events_per_hour'], 0.1)
    c3 = underrun_ratio <= 0.5
    # Criterion 4: fill_percent mean within ±10
    # (computed from metrics)
    ...

    overall_pass = c2_events and c2_samples and c3 and c4
    print(f"Comp events ratio:    {comp_ratio:.2f}× (target ≥3.0): {'PASS' if c2_events else 'FAIL'}")
    print(f"Comp samples ratio:   {sample_ratio:.2f}× (target 0.8-1.2): {'PASS' if c2_samples else 'FAIL'}")
    print(f"Underrun ratio:       {underrun_ratio:.2f}× (target ≤0.5): {'PASS' if c3 else 'FAIL'}")
    # ...
    print(f"\nOVERALL (objective): {'PASS' if overall_pass else 'FAIL'}")
    print("Subjective (audible 'underwater' improvement): Mark UAT")
    sys.exit(0 if overall_pass else 1)

if __name__ == '__main__':
    main()
```

**Step 2:** Syntax-check + commit:

```bash
python3 -c "import ast; ast.parse(open('scripts/research/bt_drift_compare.py').read())"
git add scripts/research/bt_drift_compare.py
git commit -m "scripts(research): bt_drift_compare.py — Path C acceptance PASS/FAIL gate"
```

---

## Task 7: Open the PR

```bash
git push -u origin feat/bt-drift-compensation-refinement

gh pr create --title "fix(audio): refine BT drift compensation — smaller corrections + crossfade" --body "$(cat <<'EOF'
## Summary

Refines `BufferedSoundGenerator<T>.CompensateClockDrift` to produce sub-perceptual corrections instead of the current 10 ms duplications that listeners hear as 'underwater' / 'slow' audio.

This is **Path C** from the BT clock-skew research doc (`docs/research/2026-05-22-bt-clock-skew-measurement.md`), which measured 217–391 ppm skew between the BT phone clock and the local speaker clock — about 10–20× over the BT A2DP spec. The skew is unfixable in software (two crystals can't be sync'd), so the right move is to make the compensation that masks it less audible.

## Two changes

1. **Per-call compensation cap** drops from 10 ms (480 samples at 48 kHz) to 2 ms (96 samples). Total compensation per minute stays the same (driven by the underlying rate mismatch), but it's redistributed across ~5× more events that are each much shorter.

2. **Cosine-ramp crossfade** (~32 samples / ~0.67 ms) blended across the rewind boundary in the ring buffer. Eliminates the discontinuity click at the boundary that contributes to the perceived 'underwater' sound.

3. **Removed 2 s cooldown** between drift-checks so the smaller corrections can run as often as needed.

## Acceptance (per plan Task 5)

- [ ] Mark UAT subjective: 'underwater' feel reduced or gone
- [ ] `drift_compensation_total` events/hour increases ≥3× (more events, each smaller)
- [ ] `drift_compensation_samples_total` samples/hour stays within ±20 % of baseline
- [ ] `underrun_total` events/hour drops ≥50 %
- [ ] `fill_percent` mean within ±10 of baseline

If subjective UAT fails despite objective metrics passing: escalate to **Path D** (real variable-rate resampler).

## Test plan

- [x] Unit tests for the three new behaviors (per-call cap, crossfade math, no-cooldown)
- [x] `dotnet build --configuration Release` clean
- [x] `dotnet test --configuration Release` all pass
- [ ] Mark: deploy + 15-minute soak + bt_drift_compare.py PASS/FAIL

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Auto-merge after CI passes per the current tranche's authorization. Mark runs Task 5 post-merge.

---

## Out of scope

- **Real variable-rate resampling (Path D).** That's the definitive fix and the explicit fallback if this plan's subjective acceptance fails. A separate plan document captures Path D.
- **Phone clock identification.** Knowing WHICH phone has the 217 ppm skew would help (different phone might have less). Not actionable for the radio itself though.
- **Local speaker clock characterization.** Same reason; the speaker is a fixed crystal.
- **Cast HM HTTP pacing changes.** Architecturally unable to affect BT-input underrun — see research doc rejected-options section.
- **Different default-output routing** (e.g., bypass mixer when streaming BT → Cast). Major architectural change; out of scope.
