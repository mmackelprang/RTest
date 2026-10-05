# PLAN — `AUD-15` · The buffer stops draining, and the instrument that could have said so stops being switched off

> **Row:** `AUD-15`, [`docs/queue/AUD-15.md`](../../docs/queue/AUD-15.md). 🟠 **P1.** Filed 2026-09-07 from the
> `radio-api` file sink during ordinary BT playback.
> **Branch:** `fix/aud-15-bt-buffer-runs-empty`
> **Estimate:** **2 d** of build, **plus one box session of ≥30 minutes of sustained BT playback**. §0.10 derives both.
> **⛔ NOT auto-mergeable on green gates.** §0.11 gives the three reasons.
> **Planned against** `d1ceadcc`, and **re-verified against `main` at `841d39cf`**. Every line number below was
> read out of the tree at `d1ceadcc`; the checkout then moved under this plan (a Builder switched
> `fix/gv-markread-dark-409` → `main` mid-session, see §0.13), so
> `git diff --stat d1ceadcc 841d39cf` was run over all eight cited files — `BufferedSoundGenerator.cs`,
> `SrcVariableResampler.cs`, `PipeWireNativeStream.cs`, `PipeWireNative.cs`, `LinuxBluetoothService.cs`,
> `BluetoothOptions.cs`, `Radio.API/appsettings.json`, `tests/…/Audio/SoundFlow/` — and returned **empty**.
> Every line number holds at `841d39cf`.
> **Nothing on the box was touched while planning this.** The owner is away and the appliance is unattended.
> Every number in §0.3 is derived from the row's own log lines plus source read in this repo. §7 lists what is
> *not* established.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

The Bluetooth capture path used to have a closed-loop corrector for producer/consumer clock skew:
`BufferedSoundGenerator.CompensateClockDrift` watched the buffer level and duplicated a frame whenever it
started to drain. Path D (`docs/plans/2026-05-22-bt-input-resampler.md`) replaced it with a libsamplerate
resampler, and — correctly, to avoid double-correcting — **switched the old corrector off**
(`LinuxBluetoothService.cs:1326`, `disableDriftCompensation: _options.UseInputResampler`, and
`UseInputResampler` ships `true`). But the replacement is **open loop**: it applies a single constant ratio,
`1.00025`, set once at construction and never revisited. `SrcVariableResampler.SetRatio` exists, works, is
unit-tested, and **has zero callers in the tree**. So the BT path today has a fixed 250 ppm correction and no
feedback of any kind. The row's own numbers say the real skew on this phone is about **963 ppm**. The
uncorrected 713 ppm drains the 0.5 s startup cushion in roughly twelve minutes, after which the buffer runs at
essentially zero level and every scheduling wobble — the 21.9 ms interval the row noticed — lands directly in
the speakers as silence. **The callback timing is a symptom of having no cushion left, not the cause of the
cushion being gone.**

### 0.2 ⭐ What `OnProcess` actually does, statement by statement

`PipeWireNativeStream.OnProcess` (`src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs:308-501`),
in order, with what each can cost. This is scope question 1, answered.

| # | Lines | Statement | Can it exceed 1 ms? |
|---|---|---|---|
| 1 | `310-329` | `GCHandle.FromIntPtr` + `Target as` + null/disposed guards | No |
| 2 | `336-355` | SCHED_FIFO bump, `_rtPriorityApplied` guard | **Once per stream lifetime, and never on this box** — see `C-181` |
| 3 | `357-378` | `Stopwatch.GetTimestamp`, interval min/max/burst bookkeeping | No |
| 4 | `380` | `pw_stream_dequeue_buffer` — P/Invoke | No |
| 5 | `388-406` | **Four** `Marshal.PtrToStructure<T>` — `PwBuffer`, `SpaBuffer`, `SpaData`, `SpaChunk` | Unlikely; but each is a marshalling call and at least some may box — `C-186` |
| 6 | `430` | `ArrayPool<float>.Shared.Rent(sampleCount)` | **Allocates when the per-core stack is empty** — GC pressure on the PipeWire thread |
| 7 | `433-440` | S16→float scalar loop, `sampleCount` iterations (~1024) | No — microseconds |
| 8 | `450` | **`_resampler.Process` → `src_process`** (SINC_FASTEST, native) | Plausible tens of µs; **not measured** — `C-187` |
| 9 | `454`/`461` | `_onAudioData` → **`generator.AddSamples` → `Monitor.Enter(_bufferLock)`** | **Yes — this is the only unbounded wait on the path** |
| 10 | `475` | `pw_stream_queue_buffer` | No |
| 11 | `485` | **`DateTime.UtcNow` on every callback** (~94/s) | No, but it is unconditional and serves only the 10 s log gate |
| 12 | `488-499` | The 10-second `LogInformation` | **Excluded from the measured window — see below** |

⭐ **The reported `execution max` cannot be the logging.** `execMs` is computed in the `finally` at
`:477-481`, and the logging block at `:484-500` runs *after* the `try/finally` closes. `_maxOnProcessExecutionMs`
is then reset at `:498`, so each window starts clean. **The 14.20 ms was a real callback in that window, and its
own log line's cost is not in it.** That rules out the most obvious self-inflicted explanation, and it means the
instrument systematically *under*-reports: the single most expensive callback in every window — the one that
logs — is the one whose length is never counted (`C-188`).

**So the only statement on that path that can block for 14 ms is step 9, `Monitor.Enter(_bufferLock)`.** Which
brings us to whether that is the cause.

### 0.3 ⭐⭐ The arithmetic that changes the diagnosis

Four derivations, all from the row's own log lines. None of them needs the box.

**(A) The producer is delivering at exactly the right average rate, and the quantum is exactly as documented.**

The `count` field is cumulative (it is never reset — `:496-499` resets four fields and `_onProcessCount` is not
one of them, `C-189`). So consecutive samples give callback counts per 10-second window:

| Window | Count delta | Mean interval |
|---|---|---|
| 11:22:22 → 11:22:32 | 10285 − 9349 = **936** | 10.684 ms |
| 11:22:32 → 11:22:42 | 11222 − 10285 = **937** | 10.672 ms |
| 11:22:42 → 11:22:52 | 12160 − 11222 = **938** | 10.661 ms |

512 frames / 48000 Hz = **10.667 ms**. The three measurements bracket it within 0.2 %.

⭐ **This answers scope question 3 without touching the box, and it answers it in the safe direction.** The
quantum in effect *is* 512 frames / 10.67 ms, exactly as `CLAUDE.md` § *PipeWire Quantum Tuning* documents. The
`min=1.11ms / max=21.94ms` spread is jitter about a correct mean, not a wrong quantum. **No `pw-metadata`
enquiry is needed and none must be made** — `MEMORY.md` forbids `clock.force-quantum` and this derivation
removes the reason anyone would reach for it (`C-182`).

**(B) The sample rate and channel count are confirmed by the evidence itself, not assumed.**

`_maxBufferSamples = format.SampleRate * format.Channels * maxBufferSeconds`
(`BufferedSoundGenerator.cs:177-178`), and `LinuxBluetoothService.cs:1321` passes `maxBufferSeconds: 4.0f`.
The row's `384000` therefore forces `SampleRate × Channels = 96000` — 48 kHz stereo. Every number below rests
on the evidence, not on a config file nobody read.

**(C) ⭐⭐⭐ The silence deficit is ~713 ppm — a sustained rate shortfall, not jitter.**

| Sample | Zero samples | Window |
|---|---|---|
| 11:22:24 | 574 | 13.5 s |
| 11:22:36 | 488 | 11.7 s |
| 11:22:38 | 1274 | 2.3 s |
| 11:22:56 | 736 | 17.4 s |
| **Total** | **3072** | **44.9 s** |

The windows are back-to-back within sub-second log rounding, so this is ~45 s of continuous coverage. And the
accumulators are **not** reset when the 1 Hz throttle suppresses a line — `_underrunSamplesSinceLastLog += deficit`
runs on every underrun (`:464`) and is zeroed only when a line is actually emitted (`:492`). So no silence
escapes the total.

    3072 samples ÷ 44.9 s = 68.4 samples/s
    68.4 ÷ 96000 samples/s   = 713 ppm

Now solve for the true skew. The consumer takes 96 000 samples/s. The producer supplies `P` raw and the
resampler stretches by `r = 1.00025`, so `96000 − P·r = 68.4` ⇒ `P = 95907.6` ⇒ the ratio that would close the
gap is `96000 / 95907.6 =` **`1.000963`**.

⭐ **The configured ratio corrects 250 ppm of a skew that is about 963 ppm. It is roughly a quarter of what
this phone needs, and nothing anywhere adjusts it.**

**(D) A 4-second buffer cannot be emptied by 22 ms of jitter — and the cushion drains in ~12 minutes.**

`StartCaptureSubprocess` prefills 0.5 s (`LinuxBluetoothService.cs:1809`, `PreFillSilence(0.5f)`) = 48 000
samples = 12.5 % of the 384 000 ring. At a 68.4 samples/s shortfall:

    48000 ÷ 68.4 = 702 s ≈ 11.7 minutes

⛔ **This is the argument that disqualifies the timing hypothesis as a *cause*.** The worst interval in the
evidence is 21.94 ms against a 10.67 ms quantum — an excess of ~11 ms, or ~1050 samples, against a ring holding
384 000. A buffer that size absorbs that excess **366 times over**. Jitter of this magnitude cannot empty this
buffer, and it certainly cannot hold it empty across 32+ seconds. Only a sustained rate deficit can. The
timing numbers explain *when each individual dropout lands*; they do not explain why there is no cushion to
absorb it.

**(E) ⚠ `buffer: 0/384000` is a tautology, not a measurement.**

The row reads *"`buffer: 0/384000` on every sample — the ring buffer is not merely low, it is empty"* as its
strongest evidence. But that value is read at `:481-485`, **inside the `if (samplesWritten < buffer.Length)`
branch** — i.e. only on a callback that just found the buffer short. The branch cannot execute while the buffer
is healthy, so the field cannot report a healthy number. It is structurally incapable of saying anything else
(`C-183`).

And the level is genuinely *not* pinned at zero: if it were, every callback would underrun by a full quantum
and the silence rate would be 96 000 samples/s, not 68. The observed rate is **0.07 %** of that. The correct
statement is: *the buffer is running with no cushion and is dipping to empty a few times a second.* The
unbiased instrument that would have said so — `min=` and `fill=` in `LogStats` — is at `LogDebug`, which is the
next section.

### 0.4 ⚠ The lock is a red herring for the underruns — and we cannot currently say more than that

`MEMORY.md`'s standing note is accurate as far as it goes: `OnProcess` really does call `AddSamples`, which
really does take `_bufferLock` (`BufferedSoundGenerator.cs:235-246`), and that really is the only unbounded
wait on the callback path. Two things follow, and they point in opposite directions.

**It is not the cause of this row.** §0.3(D) settles that: a lock wait bounded by the consumer's own callback
period cannot drain a 375-quantum buffer. Even if every single `AddSamples` waited the full 14 ms, the samples
would arrive *late*, not *missing* — and the ring exists precisely to absorb late.

**But we cannot say whether it is even happening**, because the instrument is switched off. `BufferedSoundGenerator`
already measures exactly what the row wants to know:

```csharp
// BufferedSoundGenerator.cs:79-83
private long _addSamplesContentionCount;
private double _maxAddSamplesLockWaitMs;
private long _generateAudioContentionCount;
private double _maxGenerateAudioLockWaitMs;
```

…and reports them, along with the unbiased buffer level, in two lines at `:704` and `:712` — **both
`_logger.LogDebug`**. `src/Radio.API/appsettings.json:15-23` sets `MinimumLevel.Default: "Warning"` with
`Override: { "Radio": "Information" }`. Every logger on this path is a `Radio.*` `SourceContext`, so the
effective floor is **Information** and `LogDebug` reaches **neither the journal nor the file sink**.

⭐⭐ **That is why the row cannot answer its own scope question 1.** The evidence contains the `🔬 PipeWire
OnProcess` line (`LogInformation`, `:488`) and does not contain the `🔬 Timing (Single)` line (`LogDebug`,
`:712`) — and the second one is the one carrying `addSamples={AddContentions} (max {AddWait:F2}ms)`. The lock
question has a written, tested, allocation-free answer sitting in the binary with its output discarded
(`C-184`). **Task 1 turns it on. Nothing else in this plan should speculate about the lock until it has run.**

### 0.5 Scope question 2 — the counter arithmetic, audited

**The reported deltas are correct, but they are also incapable of being wrong, so they corroborate nothing.**
`_underrunCount++` (`:463`) and `_underrunCountSinceLastLog++` (`:465`) are adjacent unconditional statements
in the same branch, and the per-window counter is zeroed only where the line is emitted (`:493`). For any two
consecutive lines **from the same instance**, `Δ{TotalUnderruns} == {Count}` holds by construction. The row
notes the deltas reconcile and treats it as a check that passed; it is a check that cannot fail.

⭐ **It does establish one thing the row did not claim: all four lines came from a single generator instance.**
Two interleaved generators would draw `{TotalUnderruns}` from different objects and the sequence would not be
monotone with matching deltas. That matters, because §0.6 shows the label does not identify the instance.

Three real defects found in the same audit:

1. ⚠ **The first line of every capture session reports `in last 0.0s`.** `sinceLastLog` is forced to `0.0`
   when `_lastUnderrunLogTime == default` (`:477-478`) while `{Deficit}` covers everything since the generator
   started receiving. Any per-second rate computed from that line is a division by zero. None of the four
   samples is a first line, so §0.3(C) is unaffected — but the next person to do this arithmetic needs to know
   to discard it (`C-185`).
2. ⚠ **`{Count}` and `{TotalUnderruns}` are the same event counted twice**, which is why they cannot disagree.
   The genuinely useful pair — how much silence, over how long — is `{Deficit}` and `{Interval}`, and the line
   makes the reader compute the ratio themselves. Task 1 emits it.
3. ⚠ **The line does not say which generator it is.** §0.6.

### 0.6 Scope question 4 — `(Single)` is a **type name**, not a generator

The row reads *"the underrun label says `(Single)`, which suggests a specific generator."* It does not.
`:489` passes `typeof(T).Name` — `System.Single` is `float`. **Every `BufferedSoundGenerator<float>` in the
solution logs the identical label**, and there are five construction sites:

| Site | Source | Drift compensation | In this row's blast radius? |
|---|---|---|---|
| `LinuxBluetoothService.cs:1320` | **BT capture (native PipeWire)** — `maxBufferSeconds: 4.0f` | **disabled** (`disableDriftCompensation: _options.UseInputResampler`, ships `true`) | **this row** |
| `BluetoothAudioSource.cs:548` | **BT capture bridge** — `AudioCaptureDevice` → mixer, default 4.0 s | enabled | ⚠ **also BT, also `(Single)`** |
| `USBAudioSourceBase.cs:323` | Vinyl / Radio (Raddy) / GenericUSB | enabled | no — but see below |
| `SDRRadioAudioSource.cs:945` | SDR | enabled | no |
| `WasapiLoopbackCaptureSource.cs:44` | Windows loopback | enabled | no — Windows only |

⚠ **Two of the five are on the Bluetooth path**, so `(Single)` does not even narrow the evidence to one
subsystem, let alone one object. The `384000` capacity is what pins these four lines to
`LinuxBluetoothService.cs:1320` — it is the **only** site passing a non-default `maxBufferSeconds`… and it
passes `4.0f`, which *is* the default, so the discriminator is a coincidence rather than a signal. §0.5's
delta argument is what actually establishes a single instance.

⭐ **The fix costs one token and it already exists.** `GeneratorId` is a public property (`:110`), assigned
from an `Interlocked.Increment` (`:169`), and already logged at construction (`:191`) and disposal (`:855`).
It is absent from the one message anybody reads in anger. Task 1 adds it.

**Does the defect affect other capture sources?** *The specific cause does not* — `SrcVariableResampler` is
constructed in exactly one place (`PipeWireNativeStream.cs:143`), reached from exactly one place
(`LinuxBluetoothService.cs:1817`). The other four generators still run `CompensateClockDrift` and therefore
still have their closed loop. **But the observability defect affects all five**, and the USB family has a
second-order exposure worth naming: `USBAudioSourceBase.cs:323` takes every default, so it gets the same 4 s
ring with **no prefill at all** — no `PreFillSilence` call on that path. That is not this row (§6.1).

### 0.7 Scope question 5 — is it new, and how a Builder settles it for free

`total underruns: 55` is cumulative since the generator was constructed, which on this path is *per capture
session*, not per process — `LinuxBluetoothService.cs:1320` builds a fresh generator on every acquisition.
So `55` dates from the current BT connection, not from process start. The row's *"cumulative since process
start"* is one step too pessimistic (`C-190`).

**Three cheap tests, in increasing cost, none of them requiring a change to the box:**

1. **Read the file sink for the session's own start.** The generator logs
   `BufferedSoundGenerator #{GeneratorId} created` at Information (`:191`) and
   `Pre-filled buffer with {Samples} samples` (`:794`). The gap between that timestamp and the **first**
   `Buffer underrun` line is the observable §0.3(D) predicts at ~12 minutes. If it is ~12 minutes, the
   diagnosis is confirmed on historical data alone. If it is ~10 seconds, the diagnosis is wrong and this plan
   needs rewriting before Task 2.
2. **`git log` the two commits that could have introduced it.** Path D (`UseInputResampler` default `true` and
   `disableDriftCompensation`) is the change that removed the closed loop. Everything before it had
   `CompensateClockDrift` running on the BT generator. `git log --oneline -S'disableDriftCompensation'` and
   `git log --oneline -S'UseInputResampler'` bound the window.
3. **Only if 1 and 2 disagree**, the metric series. `audio.buffer.underrun_total` has been incremented on
   every underrun since well before this row (`:470`) and lands in `data/metrics/metrics.db`. That is a SQLite
   read on the box and `CLAUDE.md` warns DB reads on this box correlate with audible distortion — so it is
   third, not first.

**The single command for a human, when one is next at the appliance** (this is the *only* box command this
plan asks for before §4):

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -nE "BufferedSoundGenerator #|Pre-filled buffer|Buffer underrun" $F | head -40'
```

### 0.8 ⚠ What was ruled OUT, so nobody re-investigates it

The row is right that CPU contention is the obvious suspect and wrong to be started from. Four suspects were
checked and eliminated in source, and recording that is most of the value of this section.

| Suspect | Verdict | Evidence |
|---|---|---|
| **Priority inversion** — a SCHED_FIFO producer blocking on a lock held by a normal-priority consumer | ⛔ **Not live.** `BluetoothOptions.UseRealtimeCaptureThread` defaults `false` (`BluetoothOptions.cs:126`) and `src/Radio.API/appsettings.json` **does not set it** (only `UseInputResampler` and `InputResamplerInitialRatio` appear, `:236-237`). `docs/HANDOFF-GA-PUNCH-LIST.md:1141` (`LOG-10`) confirms it is unshipped and hard-blocked. `C-181` |
| **The 10-second log line inflating `execution max`** | ⛔ **Impossible.** `execMs` is computed inside the `finally` at `:477`, before the logging block at `:484`. §0.2 |
| **Burst delivery** — PipeWire batching, so a gap is followed by a catch-up | ⛔ **Contradicted.** `bursts=2` is **cumulative and never reset** (`C-189`), so it is 2 across all **12 160** callbacks of the session. There is essentially no sub-1 ms delivery. Late callbacks are not being made up. |
| **A wrong quantum** | ⛔ **Contradicted.** §0.3(A): mean interval 10.66–10.68 ms against a documented 10.667 ms. |

### 0.9 ⭐ The resampler has two independent defects beyond the missing feedback loop

Both are in `src/Radio.Infrastructure/Audio/SoundFlow/SrcVariableResampler.cs`, both are in the direction of
losing audio, and neither is detectable by the existing tests.

**(i) `Process` silently discards input frames libsamplerate did not consume.** `src_process` writes back
`input_frames_used`; the interop struct marshals it (`PipeWireNative.cs:338`, `ref SrcData` at `:353`, so the
value *is* returned to managed code); and the wrapper reads only `OutputFramesGen`:

```csharp
      var err = src_process(_state, ref data);
      ...
      return (int)data.OutputFramesGen;   // :156 — data.InputFramesUsed is never read
```

`PipeWireNativeStream.OnProcess` then returns the pooled array to `ArrayPool` at `:466` and dequeues the next
PipeWire buffer. **Any frame libsamplerate declined to consume is gone.** libsamplerate does not promise full
consumption; the wrapper's own doc comment (`:122`, *"output buffer must be sized for at least ratio × input
frames"*) reads as though the author believed adequate output size implied it. That is precisely the
`CLAUDE.md` § *Pre-Merge Review* class — a comment asserting a guarantee the code does not have.

⚠ **Whether it fires in steady state is not established here** and is the one thing Task 2's test measures.
Filter priming on the first call certainly under-consumes; whether it recurs is the open question.

**(ii) `SetRatio` and `Reset` are both dead.** Verified by grep over `src/`: the only member of `_resampler`
ever invoked is `.Process` (`PipeWireNativeStream.cs:450`) and `?.Dispose()` (`:513`). So:

- the ratio is fixed at `InputResamplerInitialRatio` for the life of the stream — the open loop of §0.1;
- **`Reset()` is never called on BT reconnect**, contradicting its own doc comment (`:99-102`, *"Call when the
  source stream is interrupted (e.g. BT reconnect) so the next batch of samples doesn't blend with stale
  filter taps"*). A reconnect currently blends the new stream into the old stream's filter tail.

**(iii) ⚠ The existing tests cannot detect any of this.** `tests/Radio.Infrastructure.Tests/Audio/SoundFlow/SrcVariableResamplerTests.cs:41`
asserts `Assert.InRange(frames, 0, 4)` for four frames of input — **it accepts zero output**, so it cannot
fail. And `:66` asserts `Assert.InRange(frames, 945, 1155)` around an expected 1050 — a **±10 % band on a
component whose entire job is accuracy to 250 ppm (0.025 %)**. A resampler losing 10 % of its audio passes
that test. The tolerance is four hundred times wider than the effect being corrected (`C-187`).

### 0.10 The estimate — **2 d**, plus a box session that is not optional

| Work | Cost |
|---|---|
| Task 1 (observability: `GeneratorId`, the deficit rate, promote the contention line) | 0.25 d |
| Task 2 (frame conservation in the resampler + the test that measures it) | 0.5 d |
| Task 3 (the closed-loop ratio controller — the actual fix) | 0.75 d |
| Task 4 (`Reset()` on reconnect) + Task 5 (instrument-consistency corrections + comments) | 0.25 d |
| Task 6 (tests) | 0.25 d |

**What holds it to 2 d:** almost nothing needs inventing. The contention instrument is written and merely
suppressed. `GeneratorId` exists. `SetRatio` exists, is tested, and libsamplerate ramps a ratio change
internally so no crossfade is needed (`SrcVariableResampler.cs:74-76`). `BufferDiagnostics` already exposes
buffer level. The deterministic test seam — a subclass exposing `GenerateAudio` — is already in
`BufferedSoundGeneratorTests.cs:24-43`.

⚠ **What would push it to 3 d:** if Task 2's measurement shows `InputFramesUsed < InputFrames` in steady state.
Then the resampler needs a carry-over buffer for the unconsumed tail, which is a new piece of state on the
PipeWire thread, and Task 3's controller has to servo against a producer that is itself lossy. **Do Task 2
before Task 3 for exactly this reason.**

⚠ **And the box session is a separate cost this number does not contain.** §4 needs ≥30 minutes of
uninterrupted BT playback to observe a defect whose onset is ~12 minutes. There is no shortcut.

### 0.11 ⛔ Not auto-mergeable. Three reasons

1. **The fix cannot be observed by any gate this repository can run.** A closed-loop controller against a real
   clock skew needs a real clock skew. Every unit test in Task 6 pins the controller's *arithmetic*; not one
   can pin *that the buffer stops draining on this phone*. A fully green suite is consistent with the row
   being completely unfixed.
2. **Task 3 changes the sample rate of live audio, continuously.** A controller with a wrong sign, a wrong
   gain, or an unclamped output does not fail loudly — it detunes the music. That is a regression the suite
   cannot see and the owner would meet as pitch drift.
3. **UAT is a 30-minute listening session on an appliance the owner is currently away from**, and per
   `CLAUDE.md` the box is unattended and on WiFi. It cannot be run remotely and must not be started while
   nobody is present.

**Merge decision goes to the owner after §4 runs.**

### 0.12 ⚠ Ten constraints found while planning — numbering continues from `C-180` (`AUD-12`)

**`C-183`, `C-184`, `C-186` and `C-190` correct claims in the row.** **`C-185`, `C-187`, `C-188` and `C-189`
are defects in the instruments the row reasons from.** **`C-181` and `C-182` are hypotheses eliminated in
source.**

---

**`C-181` — ⚠ CORRECTS A LIKELY INVESTIGATION PATH. `UseRealtimeCaptureThread` is `false` on this box, so
there is no SCHED_FIFO producer and no priority inversion. Do not go looking for one.**

`BluetoothOptions.cs:126` defaults it `false`; `src/Radio.API/appsettings.json` does not override it (the
`Bluetooth` block sets `UseInputResampler` and `InputResamplerInitialRatio` at `:236-237` and nothing else);
`docs/HANDOFF-GA-PUNCH-LIST.md:1141` records `LOG-10` ("Enable `UseRealtimeCaptureThread`") as **not done and
hard-blocked on `LOG-6`**. Both the PipeWire capture thread and the MiniAudio playback thread therefore run at
normal priority, and .NET `Monitor` has no priority inheritance to matter. ⛔ **Do not enable it as part of
this row.** It is a separate, blocked, queued item, and turning it on while `_bufferLock` sits on the callback
path would *create* the inversion that does not currently exist.

⚠ **One residual unknown:** the repo has no `appsettings.Production.json`, but `CLAUDE.md` says per-machine
overrides live in one on the box. Both flags could be overridden there. §4.1 checks it with one command.

---

**`C-182` — the quantum question is answered off-box, and answering it on-box is forbidden.**

§0.3(A) derives a 10.66–10.68 ms mean callback interval from the `count` deltas, against a documented
10.667 ms. ⛔ **Do not run `pw-metadata` to confirm.** `MEMORY.md`: *"DO NOT use `pw-metadata
clock.force-quantum` — it disrupts the graph, causes bluez_input errors to skyrocket, and forces BT
reconnection."* The read-only `pw-metadata -n settings` is less dangerous but still touches the graph on an
unattended box, and it would confirm something already known.

---

**`C-183` — ⚠ NARROWS THE ROW'S HEADLINE EVIDENCE. `buffer: 0/384000` is read inside the underrun branch and
therefore cannot report a non-empty buffer. It is a tautology.**

`BufferedSoundGenerator.cs:456` opens `if (samplesWritten < buffer.Length)`; `:481-485` re-acquires the lock to
read `_count` for the message. The row's *"not merely low, it is empty"* is true of the instant sampled and
says nothing about the distribution. §0.3(E) shows the level is in fact oscillating: a truly pinned-at-zero
buffer would produce 96 000 zero samples/s and the observed rate is 68. The unbiased reading is `min=`/`fill=`
in `LogStats` (`:704`), which is dark — `C-184`.

---

**`C-184` — ⚠⚠ CHANGES THE WORK. The lock-contention instrument the row asks for is already written, already
allocation-free, and logged at `LogDebug`, which reaches no sink in production.**

`_addSamplesContentionCount` / `_maxAddSamplesLockWaitMs` / `_generateAudioContentionCount` /
`_maxGenerateAudioLockWaitMs` are maintained on every call (`:233-246`, `:362-376`) and emitted at `:712` —
`_logger.LogDebug`. `src/Radio.API/appsettings.json:15-23`: `MinimumLevel.Default: "Warning"`,
`Override: { "Radio": "Information" }`. Debug is below the floor for both the file sink and (per `CLAUDE.md`
§ *Deployment*) the Warning-restricted console sink. **The buffer fill/min/max line at `:704` is dark for the
same reason**, which is why nobody has an unbiased buffer level. Task 1.

---

**`C-185` — the first underrun line of every session reports `in last 0.0s` and must be discarded from any
rate arithmetic.**

`:477-478`, `sinceLastLog = _lastUnderrunLogTime == default ? 0.0 : …`, while `_underrunSamplesSinceLastLog`
covers everything since the first received sample. §0.3(C) is unaffected — none of the four samples is a first
line — but Task 1 makes the line self-describing so the trap is not re-set.

---

**`C-186` — ⚠ CORRECTS THE ROW'S FRAMING. "A callback that outlasts the quantum cannot keep the buffer fed" is
true of a one-quantum buffer and false of this one.**

The row states it as though it were general. This ring holds 384 000 samples = **375 quanta**. The worst
observed excess is 21.94 − 10.67 = 11.3 ms ≈ 1085 samples, which the ring absorbs 350× over. §0.3(D).
**Consequence for this plan:** the timing numbers are real and worth fixing, but they are a *second* row's
worth of work at most, and they are not why the buffer is empty. ⛔ **Do not design the fix around callback
latency.**

---

**`C-187` — ⚠ CHANGES THE WORK. `SrcVariableResampler.Process` discards `input_frames_used`, and the existing
tests have a ±10 % tolerance on a 250 ppm effect, so they cannot detect it.**

§0.9. `SrcVariableResamplerTests.cs:41` accepts `frames` in `[0, 4]` for 4 input frames — an assertion with no
failing input. `:66` accepts `[945, 1155]` for an expected 1050. **Task 2 must add the frame-conservation
assertion before Task 3 servos against this producer**, because a lossy producer and a controller trying to
correct for it will fight.

---

**`C-188` — the two `execution max` instruments in the evidence measure different windows, and neither says
which.**

`PipeWireNativeStream`: `execMs` is computed in the `finally` at `:477`, **before** the log block at `:484`, so
the logging callback's own cost is excluded — the reported max is an under-estimate that systematically omits
the most expensive callback of each window. `BufferedSoundGenerator`: `executionMs` is computed at `:501`,
**after** `LogStats()` at `:499` has already reset `_maxCallbackExecutionMs` to `0` at `:727` — so that
instrument's next window *opens* with the logging callback's cost in it. Opposite biases, same field name.
Task 5 documents both; neither is worth restructuring.

---

**`C-189` — in the `🔬 PipeWire OnProcess` line, `count` and `bursts` are lifetime totals while `min`, `max`
and `execution max` are per-window. The message does not distinguish them.**

`:496-499` resets `_maxOnProcessIntervalMs`, `_minOnProcessIntervalMs`, `_maxOnProcessExecutionMs` and
`_lastOnProcessLogTime`. `_onProcessCount` and `_onProcessBurstCount` are not reset. ⭐ **This is load-bearing
in both directions**: it is what makes §0.3(A)'s count-delta derivation valid, *and* it is why `bursts=2`
across all four samples means "2 in the whole session" rather than "2 per window" — which is what eliminates
the burst-delivery hypothesis (§0.8). Task 5 labels the fields.

---

**`C-190` — `total underruns` is per **generator**, not per process, and the generator is rebuilt on every
capture acquisition.**

The row says *"cumulative since process start"*. `_underrunCount` is an instance field and
`LinuxBluetoothService.cs:1320` constructs a new `BufferedSoundGenerator<float>` on each acquisition attempt.
So `55` dates from the current BT session. **This makes §0.7's onset test much cheaper than the row assumes** —
the session's own `#{GeneratorId} created` line is in the same file sink, a few hundred lines up.

### 0.13 Things Builder must NOT do

- ⛔ **Do not start from CPU contention.** `C-186`. The row says so and §0.3(D) proves it.
- ⛔ **Do not run `pw-metadata`, `pw-cli`, or anything else against the graph on `radio`.** `C-182`, and the
  box is unattended. §4 is the only place the box is touched, and it needs a human present.
- ⛔ **Do not enable `UseRealtimeCaptureThread`.** `C-181`. It is `LOG-10`, it is blocked on `LOG-6`, and
  enabling it while `AddSamples` takes a lock on the callback path would create a priority inversion that does
  not exist today.
- ⛔ **Do not re-enable `CompensateClockDrift` on the BT generator** as a shortcut. §1.3 explains why the
  double-correction it was disabled to prevent is a real hazard, and why the closed loop belongs in the
  resampler rather than beside it.
- ⛔ **Do not raise `PreFillSilence` above 0.5 s to "give it more cushion".** That is 0.5 s of added Bluetooth
  latency for the listener and it does not stop a monotonic drain — it postpones it. §1.2.
- ⛔ **Do not promote the two `LogStats` lines to Warning.** Information is the correct level and it reaches
  the file sink only. Log volume on this box correlates with audible distortion (`CLAUDE.md`, and the
  `MEMORY.md` note on SSH activity); the file at `:95-97` already carries a comment about that exact feedback
  loop.
- ⛔ **Do not touch `docs/BUILDER_QUEUE.md` or `docs/queue/AUD-15.md` from this plan.** §8 carries the wording.
- ⛔ **Do not conflate this with `AUD-10` or with the RotaryPhone HFP disconnect.** §6.3.
- ⚠ **A Builder was working in this checkout while this plan was written**, and the branch moved from
  `fix/gv-markread-dark-409` (`d1ceadcc`) to `main` (`841d39cf`) mid-session. None of the eight cited files
  differed across that move (header). Branch `fix/aud-15-bt-buffer-runs-empty` from the project's main branch,
  and **re-check that the eight files are still unchanged before trusting a line number** — this row's
  reasoning is unusually line-dependent.

---

## 1. Decision

### 1.1 The fix is a closed loop on the resampler ratio — not a retune, not a bigger buffer

Four options were considered.

| Option | Verdict |
|---|---|
| **Closed-loop ratio control servoing to the prefill level** ✅ | **Taken.** It is the fix the Path D plan itself deferred (`docs/plans/2026-05-22-bt-input-resampler.md:644`: *"Closed-loop ratio control (Phase 2)… Defer until static-ratio validates the architecture"*). Static ratio has now been validated and found wanting, by 4×. Corrects **any** skew, on any phone, and re-corrects when it changes. |
| **Retune `InputResamplerInitialRatio` to ~1.00096** | **Rejected as the fix, adopted as the emergency lever.** It is one config value and it would help *this* phone today. But it is measured from one 45-second sample of one handset, it is wrong for the next device, and it has no answer for thermal drift. §6.2 keeps it as the documented rollback if Task 3 misbehaves on the box. |
| **Re-enable `CompensateClockDrift` alongside the resampler** | **Rejected.** `disableDriftCompensation` exists precisely to stop two correctors fighting, and the failure mode of double-correction is audible artifacting rather than a clean error. Correcting in the resampler is also strictly better: libsamplerate ramps a ratio change internally (`SrcVariableResampler.cs:74-76`), whereas `CompensateClockDrift` duplicates a chunk and crossfades over the seam (`:592-604`). One is resampling; the other is a splice. |
| **Enlarge the ring / prefill more** | **Rejected.** A monotonic drain empties any finite buffer; this only changes when. And prefill is latency the listener pays. |

⚠ **The honest limitation, and it goes in the PR body.** A controller that adjusts playback rate to hold a
buffer level is trading pitch accuracy for continuity. At the magnitudes involved — a ratio moving within
±2000 ppm, i.e. ±0.2 %, or about 3.5 cents — it is well below the ~5–6 cent threshold at which pitch change
becomes perceptible to most listeners, and far below what the existing 250 ppm static correction already
does unconditionally. **But it is a real trade and the clamp in §1.4 is what bounds it.**

### 1.2 What the loop servos to — **the prefill level, so latency does not change**

Target = the level `PreFillSilence(0.5f)` establishes: 48 000 samples, 12.5 % of the 384 000 ring, 0.5 s of
audio. Deliberately **not** a new, larger target:

- **Latency is unchanged.** The listener's BT-to-speaker delay is set by the standing buffer level. Servoing
  to the level the path already prefills to keeps today's latency exactly.
- **It is a level the system has already demonstrated it can reach**, on every capture start.
- ⭐ **0.5 s of cushion is worth 47 of the worst observed jitter events** (11.3 ms excess each). Once the loop
  holds it, §0.3(D)'s absorption argument stops being theoretical and starts being the operating condition.

### 1.3 Why the loop lives in the resampler and not beside it

`CompensateClockDrift` had the loop and the correction in one place, and Path D removed both together. The
new arrangement splits them: `PipeWireNativeStream` owns the correction (the resampler) and
`BufferedSoundGenerator` owns the only observation of level. Task 3 closes that gap with a **lock-free level
read** rather than by moving either component.

⚠ **`BufferedSoundGenerator.GetDiagnostics()` must not be the level source.** It takes `_bufferLock` (`:822`),
and the controller runs on the PipeWire thread — adding a lock acquisition to the callback path to fix a
problem the lock is suspected of is a bad trade. Task 3 adds a `Volatile.Read` accessor instead. `_count` is
an `int`, so the read is atomic and torn values are impossible; a stale value by one callback is irrelevant to
a loop with a 1-second period.

### 1.4 Controller shape — proportional, clamped, slow

**Proportional only.** No integral term: the plant is an integrator already (buffer level is the integral of
rate error), so proportional feedback on level is structurally sufficient to null a constant rate offset.
Adding an I term to an integrating plant is how you get overshoot and hunting.

Three safety properties, all of them non-negotiable:

- **Clamped output.** `ratio ∈ [0.998, 1.002]` — ±2000 ppm, about **8×** the largest skew this hardware has
  ever shown. Outside that band the correct conclusion is that something is broken, not that the rate needs
  changing. A clamp excursion logs at Warning.
- **Slow update.** Once per second, not per callback. The skew is a property of two crystals; it does not
  change at audio rate. A slow loop also means the controller costs one `Volatile.Read` and one timestamp
  compare on the hot path.
- **Deadband.** No adjustment while the level is within ±10 % of target. Prevents the ratio dithering around
  the setpoint and keeps the steady state genuinely steady.

---

## 2. Tasks

### Task 1 — make the two instruments that answer this row visible

**File:** `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs`

**(a)** The underrun warning gains the generator's identity and the number §0.3(C) had to be computed by hand.
Replace `:486-491`:

```csharp
                  // AUD-15: GeneratorId is the fix for C-184's sibling problem — "(Single)" is
                  // typeof(T).Name, so EVERY BufferedSoundGenerator<float> in the solution logs the
                  // identical label. There are five, two of them on the Bluetooth path
                  // (LinuxBluetoothService.cs:1320 and BluetoothAudioSource.cs:548). Without the id,
                  // a reader cannot tell which object is underrunning. See plan AUD-15 C-184, §0.6.
                  //
                  // DeficitRate is the number the AUD-15 diagnosis was derived from by hand:
                  // zero-samples per second, as a fraction of the consumer's sample rate. A sustained
                  // non-zero value is a RATE DEFICIT (producer slower than consumer) and points at
                  // clock skew; a value that spikes and returns to zero is JITTER. Those two have
                  // different fixes and the old message could not distinguish them.
                  //
                  // ⚠ buffer={Buffered} is read inside this branch and therefore CANNOT report a
                  // healthy buffer — it is a tautology, not a measurement (C-183). The unbiased
                  // reading is min=/fill= in LogStats, which is why that line is Information now.
                  var deficitPpm = sinceLastLog > 0.0
                    ? _underrunSamplesSinceLastLog / sinceLastLog
                      / (Format.SampleRate * Format.Channels) * 1_000_000.0
                    : 0.0;
                  _logger.LogWarning(
                      "⚠️ Buffer underrun (#{GeneratorId}, {Type}): {Count} underruns, {Deficit} zero samples " +
                      "in last {Interval:F1}s = {DeficitPpm:F0} ppm deficit " +
                      "(buffer: {Buffered}/{Capacity} — sampled inside the underrun branch, see AUD-15; " +
                      "total underruns: {TotalUnderruns})",
                      GeneratorId, typeof(T).Name, _underrunCountSinceLastLog, _underrunSamplesSinceLastLog,
                      sinceLastLog, deficitPpm,
                      buffered, _maxBufferSamples, _underrunCount);
```

⚠ **`deficitPpm` is guarded on `sinceLastLog > 0.0` because of `C-185`** — the first line of a session has
`sinceLastLog == 0.0` by construction (`:477-478`) and would otherwise divide by zero. It reports `0 ppm`
there, which is visibly wrong-in-the-safe-direction rather than `∞`.

**(b)** Promote the two `LogStats` lines from `LogDebug` to `LogInformation` (`:704` and `:712`). Change only
the method call; leave every argument as it is. Add above the first:

```csharp
                // AUD-15 C-184: these two lines were LogDebug, and Radio.API's Serilog floor is
                // Information (appsettings.json:15-23 — Default "Warning", Override "Radio":
                // "Information"). So the ONLY unbiased buffer-level reading in the system, and the
                // ONLY lock-contention measurement in the system, were computed on every callback
                // and then discarded. AUD-15 could not answer its own question about _bufferLock
                // because of this.
                //
                // Information, deliberately, NOT Warning: Radio.API's console sink is restricted to
                // Warning and under systemd the console IS the journal. Information reaches the FILE
                // sink only (/opt/radio-console/logs/radio-*.txt), where two lines per ten seconds per
                // generator costs nothing. Log volume on this box correlates with audible audio
                // distortion — see the comment at :95-97 about exactly that feedback loop. Do not
                // raise these to Warning.
```

---

### Task 2 — the resampler stops silently dropping input frames

**⚠ Run this task's test before starting Task 3.** If frames are being dropped in steady state, Task 3's
controller is servoing against a lossy producer and its gain calculation changes.

**File:** `src/Radio.Infrastructure/Audio/SoundFlow/SrcVariableResampler.cs`

Add a result type beside the class, and replace `Process`'s return:

```csharp
/// <summary>
/// Outcome of one <see cref="SrcVariableResampler.Process"/> call.
/// </summary>
/// <param name="FramesGenerated">Frames written to the output span.</param>
/// <param name="FramesConsumed">
/// Input frames libsamplerate actually consumed. ⚠ AUD-15: this is NOT always equal to the input
/// frame count, and the difference is audio that the caller must either re-submit or account for as
/// lost. The pre-AUD-15 wrapper returned only FramesGenerated and the caller returned the input
/// buffer to the ArrayPool immediately, so any shortfall was discarded silently and permanently.
/// </param>
internal readonly record struct SrcProcessResult(int FramesGenerated, int FramesConsumed);
```

```csharp
  /// <summary>
  /// Processes a chunk of input samples and writes the resampled output. Both
  /// input and output are interleaved float buffers.
  /// </summary>
  /// <remarks>
  /// ⚠ AUD-15: libsamplerate does NOT guarantee it consumes the whole input span, and the size of
  /// the output span is not what determines whether it does. Callers MUST inspect
  /// <see cref="SrcProcessResult.FramesConsumed"/>. The previous signature returned only the
  /// generated count and its doc comment implied that a sufficiently large output buffer was enough
  /// — a guarantee libsamplerate has never made. See plan AUD-15 C-187.
  /// </remarks>
  /// <param name="input">Input frames (interleaved, length must be a multiple of channels).</param>
  /// <param name="output">Output buffer.</param>
  /// <returns>Frames generated and frames consumed. Both zero on error or empty input.</returns>
  public unsafe SrcProcessResult Process(ReadOnlySpan<float> input, Span<float> output)
  {
    if (_state == IntPtr.Zero || input.IsEmpty || output.IsEmpty)
    {
      return default;
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
        return default;
      }

      return new SrcProcessResult((int)data.OutputFramesGen, (int)data.InputFramesUsed);
    }
  }
```

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs`

Replace the resampler branch at `:442-456`:

```csharp
        if (self._resampler != null && self._resampleOutputBuffer != null)
        {
          // Path D: route input through libsamplerate variable-rate SRC,
          // stretching the BT-clock stream to match the consumer clock.
          //
          // ⚠ AUD-15: loop until libsamplerate has consumed the whole input. It does not promise to
          // do so in one call, and the input buffer is returned to the ArrayPool the moment this
          // block exits — so an unconsumed tail is audio destroyed, not audio deferred. Before
          // AUD-15 the return value's FramesConsumed was not even read. See plan AUD-15 C-187.
          //
          // The loop is bounded by construction: FramesConsumed is non-negative and, when it is
          // zero, the guard below breaks rather than spinning. In practice the steady state is a
          // single iteration.
          var totalFrames = sampleCount / self._channels;
          var framesOffset = 0;
          while (framesOffset < totalFrames)
          {
            var inputSpan = floatSamples.AsSpan(
              framesOffset * self._channels,
              (totalFrames - framesOffset) * self._channels);
            var outputSpan = self._resampleOutputBuffer.AsSpan();
            var result = self._resampler.Process(inputSpan, outputSpan);

            var samplesOut = result.FramesGenerated * self._channels;
            if (samplesOut > 0)
            {
              self._onAudioData(self._resampleOutputBuffer, samplesOut);
            }

            if (result.FramesConsumed <= 0)
            {
              // libsamplerate made no progress on this span. Count it and stop; spinning here would
              // block the PipeWire thread loop, which is far worse than dropping one buffer.
              self._resamplerStalledFrames += totalFrames - framesOffset;
              break;
            }
            framesOffset += result.FramesConsumed;
          }
        }
        else
```

Add the counter and its report beside the other instrumentation fields (`:56-63`):

```csharp
  // AUD-15: input frames libsamplerate declined to consume and which were therefore lost. Expected
  // to stay at zero in steady state; a rising value means the producer is lossy and any clock-skew
  // controller servoing against it is fighting a moving target. Reported in the 10 s stats line.
  private long _resamplerStalledFrames;
```

and extend the 10-second line (`:488-494`) — this also discharges `C-189` by labelling which fields are
cumulative:

```csharp
      self._logger.LogInformation(
        "🔬 PipeWire OnProcess: count={Count} (lifetime), interval min={Min:F2}ms max={Max:F2}ms (this window), " +
        "bursts={Bursts} (lifetime), execution max={Exec:F2}ms (this window, excludes this log call), " +
        "resamplerStalledFrames={Stalled} (lifetime)",
        self._onProcessCount,
        self._minOnProcessIntervalMs == double.MaxValue ? 0 : self._minOnProcessIntervalMs,
        self._maxOnProcessIntervalMs, self._onProcessBurstCount,
        self._maxOnProcessExecutionMs, self._resamplerStalledFrames);
```

---

### Task 3 — close the loop on the ratio

**File:** `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs`

A lock-free level accessor for the controller (§1.3):

```csharp
    /// <summary>
    /// Current buffer fill as a fraction of capacity, in [0, 1]. Lock-free.
    /// </summary>
    /// <remarks>
    /// ⚠ AUD-15: deliberately NOT <see cref="GetDiagnostics"/>, which takes _bufferLock. This is read
    /// once per second by the BT clock-skew controller running on the PipeWire thread loop — the same
    /// thread that calls AddSamples — and adding a lock acquisition to the callback path to service a
    /// controller would be a poor trade on a path already suspected of contention.
    ///
    /// _count is an int, so Volatile.Read is atomic and a torn value is impossible. The value may be
    /// one callback stale, which is irrelevant to a loop with a 1 s period.
    /// </remarks>
    public double BufferFillFraction =>
        _maxBufferSamples == 0 ? 0.0 : (double)Volatile.Read(ref _count) / _maxBufferSamples;
```

**File:** `src/Radio.Core/Configuration/BluetoothOptions.cs`

```csharp
  /// <summary>
  /// When true, the BT input resampler's conversion ratio is adjusted at runtime to hold the capture
  /// buffer at <see cref="InputResamplerTargetFillPercent"/>, instead of staying fixed at
  /// <see cref="InputResamplerInitialRatio"/>. AUD-15.
  /// </summary>
  /// <remarks>
  /// ⚠ Default true. The static ratio it replaces was measured at 250 ppm on one handset and the
  /// AUD-15 evidence puts the real skew on that pairing at ~963 ppm — so the shipped constant was
  /// correcting about a quarter of the error, and the residual drained the 0.5 s startup cushion in
  /// roughly twelve minutes, after which every scheduling wobble became audible silence. Set false to
  /// fall back to the fixed ratio; see plan AUD-15 §6.2 for the rollback procedure.
  /// </remarks>
  public bool UseAdaptiveResamplerRatio { get; set; } = true;

  /// <summary>
  /// Target capture-buffer fill, as a percentage of capacity, for the adaptive ratio controller.
  /// Default 12.5, which is exactly what PreFillSilence(0.5f) establishes on a 4 s ring.
  /// </summary>
  /// <remarks>
  /// ⚠ Do not raise this to "get more cushion". The standing buffer level IS the listener's
  /// Bluetooth latency; 12.5 % of 4 s is the 0.5 s the path already prefills, so holding it here
  /// changes nothing the listener perceives. See plan AUD-15 §1.2.
  /// </remarks>
  public double InputResamplerTargetFillPercent { get; set; } = 12.5;

  /// <summary>
  /// Proportional gain for the adaptive ratio controller, expressed as ppm of ratio adjustment per
  /// percentage point of fill error. Default 40.
  /// </summary>
  /// <remarks>
  /// Sizing: a full-scale error of 12.5 points (buffer empty against a 12.5 % target) yields
  /// 12.5 x 40 = 500 ppm of correction, which is over half the ~963 ppm skew AUD-15 measured — so the
  /// loop closes a worst-case error in a small number of update periods without ever commanding a
  /// large step. Raising this buys faster recovery and risks hunting.
  /// </remarks>
  public double InputResamplerGainPpmPerPercent { get; set; } = 40.0;
```

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs`

New fields beside the resampler (`:46-47`):

```csharp
  // AUD-15: adaptive ratio control. Null when disabled, in which case the resampler keeps the fixed
  // ratio it was constructed with — the pre-AUD-15 behaviour.
  private readonly Func<double>? _bufferFillFraction;
  private readonly double _targetFill;
  private readonly double _gainPpmPerPercent;
  private readonly double _baseRatio;
  private long _lastRatioUpdateTicks;
  private int _ratioClampWarnings;
```

Constructor parameters, appended after `initialResamplerRatio` (all optional, so no existing call site breaks):

```csharp
    bool useResampler = false, double initialResamplerRatio = 1.0,
    Func<double>? bufferFillFraction = null,
    double targetFillPercent = 12.5, double gainPpmPerPercent = 40.0)
```

…and in the body, immediately after the `if (useResampler)` block:

```csharp
    _baseRatio = initialResamplerRatio;
    _targetFill = targetFillPercent / 100.0;
    _gainPpmPerPercent = gainPpmPerPercent;
    // Only arm the controller when there is both a resampler to command and a level to read.
    _bufferFillFraction = _resampler != null ? bufferFillFraction : null;
```

The controller itself, called from `OnProcess` immediately after the `pw_stream_queue_buffer` finally block —
i.e. **outside** the measured execution window, beside the existing 10-second log gate:

```csharp
  /// <summary>
  /// AUD-15: holds the capture buffer at its target fill by trimming the resampler ratio.
  /// </summary>
  /// <remarks>
  /// ⭐ This is the closed loop that Path D removed and did not replace. Path D switched
  /// BufferedSoundGenerator.CompensateClockDrift off for the BT generator
  /// (LinuxBluetoothService.cs:1326) — correctly, to avoid double-correcting — and substituted a
  /// SINGLE CONSTANT ratio that nothing ever revisited. SrcVariableResampler.SetRatio has existed and
  /// been unit-tested since that PR with zero callers. The constant was measured at 250 ppm; AUD-15's
  /// evidence puts the real skew on that pairing at ~963 ppm.
  ///
  /// Proportional only, on purpose: buffer level is already the integral of rate error, so the plant
  /// integrates and P-control is sufficient to null a constant offset. An I term on an integrating
  /// plant is how this starts overshooting and hunting.
  ///
  /// ⚠ Runs on the PipeWire thread loop, so it must not block. It does not: one Volatile.Read behind
  /// the Func, one timestamp compare, and — at most once per second — one src_set_ratio, which
  /// libsamplerate ramps internally across the next Process call (SrcVariableResampler.cs:74-76), so
  /// no crossfade is needed here.
  ///
  /// ⚠ Called AFTER the try/finally that measures execution time, so the controller's cost is not
  /// folded into the execution-max instrument AUD-15 reasons from. See plan AUD-15 C-188.
  /// </remarks>
  private void UpdateResamplerRatio()
  {
    if (_bufferFillFraction == null || _resampler == null)
    {
      return;
    }

    var now = Stopwatch.GetTimestamp();
    if (_lastRatioUpdateTicks != 0
      && (now - _lastRatioUpdateTicks) / (double)Stopwatch.Frequency < 1.0)
    {
      return;
    }
    _lastRatioUpdateTicks = now;

    var fill = _bufferFillFraction();
    var errorPercent = (_targetFill - fill) * 100.0;

    // Deadband: within 10 % of target, leave the ratio alone. Without this the ratio dithers around
    // the setpoint forever and the steady state is never actually steady.
    if (Math.Abs(errorPercent) < _targetFill * 100.0 * 0.10)
    {
      return;
    }

    // Buffer below target => we need MORE output per input => raise the ratio.
    var newRatio = _baseRatio + (errorPercent * _gainPpmPerPercent / 1_000_000.0);

    // ⛔ Clamp is not optional. +/-2000 ppm is about 8x the largest skew this hardware has shown. An
    // excursion past it means something is broken (a wedged consumer, a disposed generator reading
    // zero fill forever) and the right response is to stop detuning the music and say so.
    const double MinRatio = 0.998;
    const double MaxRatio = 1.002;
    if (newRatio < MinRatio || newRatio > MaxRatio)
    {
      newRatio = Math.Clamp(newRatio, MinRatio, MaxRatio);
      // Throttled: once per 60 clamped updates, i.e. about once a minute while pinned.
      if (_ratioClampWarnings++ % 60 == 0)
      {
        _logger.LogWarning(
          "BT resampler ratio clamped to {Ratio:F6} (buffer fill {Fill:P1}, target {Target:P1}). " +
          "A persistently clamped ratio means the buffer is not responding to rate correction — " +
          "suspect a stalled consumer rather than clock skew. See AUD-15.",
          newRatio, fill, _targetFill);
      }
    }

    if (Math.Abs(newRatio - _resampler.Ratio) > 1e-9)
    {
      _resampler.SetRatio(newRatio);
    }
  }
```

The call site, immediately before the existing 10-second log gate at `:484`:

```csharp
    // AUD-15: adaptive clock-skew control. Self-throttled to 1 Hz internally; the per-callback cost
    // is one timestamp compare.
    self.UpdateResamplerRatio();
```

Extend the 10-second line to carry the ratio and the fill, so the loop is observable in the file sink:

```csharp
        "resamplerStalledFrames={Stalled} (lifetime), ratio={Ratio:F6}, fill={Fill:P1}",
```

with `self._resampler?.Ratio ?? 1.0` and `self._bufferFillFraction?.Invoke() ?? 0.0` appended to the argument
list.

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`

Wire it at `:1817-1829`. ⚠ **`generator` is already captured by the existing `_onAudioData` closure on the
line above, so this introduces no new lifetime concern**:

```csharp
        useResampler: _options.UseInputResampler,
        initialResamplerRatio: _options.InputResamplerInitialRatio,
        // AUD-15: give the stream a lock-free read of the buffer it is filling, so the resampler
        // ratio can be servoed to hold the level instead of being a constant that nothing revisits.
        // Null when adaptive control is disabled, which restores the fixed-ratio behaviour exactly.
        bufferFillFraction: _options.UseAdaptiveResamplerRatio
          ? () => generator.BufferFillFraction
          : null,
        targetFillPercent: _options.InputResamplerTargetFillPercent,
        gainPpmPerPercent: _options.InputResamplerGainPpmPerPercent);
```

**File:** `src/Radio.API/appsettings.json` — in the `Bluetooth` block beside `:236-237`:

```json
    "UseInputResampler": true,
    "InputResamplerInitialRatio": 1.00025,
    "UseAdaptiveResamplerRatio": true,
    "InputResamplerTargetFillPercent": 12.5,
    "InputResamplerGainPpmPerPercent": 40.0,
```

---

### Task 4 — call `Reset()` on reconnect, as its own doc comment instructs

`SrcVariableResampler.Reset` (`:103-114`) has zero callers, and its summary says *"Call when the source stream
is interrupted (e.g. BT reconnect) so the next batch of samples doesn't blend with stale filter taps."* Today
a reconnect blends the new stream into the old stream's filter tail.

The stream is torn down and rebuilt on reconnect (`StartCaptureSubprocess` → `StopCaptureSubprocess` at
`LinuxBluetoothService.cs:1804`), so a fresh `SrcVariableResampler` is constructed each time and the doc
comment's scenario is **already covered by construction**. ⭐ **The correct fix is therefore to correct the
comment, not to add a call** — this is exactly the `CLAUDE.md` § *Pre-Merge Review* class, a comment
describing a discipline the code does not need and nobody follows.

Replace `SrcVariableResampler.cs:98-102`:

```csharp
  /// <summary>
  /// Resets the converter's internal state (clears filter buffers).
  /// </summary>
  /// <remarks>
  /// ⚠ AUD-15: this has no callers, and that is correct — not an oversight. The comment here used to
  /// say "call when the source stream is interrupted (e.g. BT reconnect)", which implied a discipline
  /// the BT path does not need: PipeWireNativeStream owns its resampler and is itself destroyed and
  /// rebuilt on every capture acquisition (LinuxBluetoothService.StartCaptureSubprocess calls
  /// StopCaptureSubprocess first), so a reconnect always gets a brand-new converter with empty filter
  /// state. Kept for a future caller that reuses one stream across interruptions; delete it if none
  /// appears.
  /// </remarks>
```

---

### Task 5 — the comments that assert more than the code does

Three, all `CLAUDE.md` § *Pre-Merge Review* class, all found in §0.

**(a)** `PipeWireNativeStream.cs:146-151` — the `_resampleOutputBuffer` sizing comment says *"8192 floats
(~85 ms of stereo 48 kHz audio) is several × the largest plausible single callback"*, which is true and which
the author took as sufficient for full input consumption. Task 2 shows it is not. Append:

```csharp
      // ⚠ AUD-15: adequate output size does NOT imply libsamplerate consumed the whole input.
      // src_process reports input_frames_used and it is the caller's job to read it — see the loop in
      // OnProcess and plan AUD-15 C-187. Sizing this buffer larger does not remove that obligation.
```

**(b)** `BufferedSoundGenerator.cs:437-447` — the `CompensateClockDrift` call-site comment explains why it is
skipped under Path D but does not say what replaced it. Append:

```csharp
        // ⚠ AUD-15: "the resampler smoothly stretches the producer stream to match the consumer rate"
        // was true only of the RATE IT WAS TOLD ABOUT. Until AUD-15 the ratio was a constant set once
        // at construction, so disabling this hook removed the system's only feedback loop and
        // replaced it with an open-loop guess that was ~4x too small on the measured hardware. The
        // loop now lives in PipeWireNativeStream.UpdateResamplerRatio. This hook stays off for the BT
        // generator — two correctors fighting is what disableDriftCompensation exists to prevent.
```

**(c)** `PipeWireNativeStream.cs:56-63` — label the instrumentation fields by reset semantics (`C-189`):

```csharp
  // Instrumentation: OnProcess delivery timing.
  // ⚠ AUD-15 C-189: MIXED SEMANTICS, and the log line used to hide it. _onProcessCount and
  // _onProcessBurstCount are LIFETIME totals; _max/_minOnProcessIntervalMs and
  // _maxOnProcessExecutionMs are reset every 10 s and are PER-WINDOW. Both facts are load-bearing:
  // the lifetime count is what lets a reader derive the mean callback interval from two consecutive
  // log lines (that derivation is how AUD-15 established the quantum was correct without touching
  // the box), and the lifetime burst count is what showed PipeWire was not batching deliveries.
```

---

## 3. Test plan

`CLAUDE.md` § *Test Timing* governs. **Every test below synchronizes on an observation and none races a wall
clock**, with one explicitly-labelled exception in 3.4.

⭐ **The seam already exists.** `BufferedSoundGeneratorTests.cs:24-43` has a `TestBufferedGenerator<T>`
subclass exposing `GenerateAudio` through a public `Read`. Every consumer-side test drives it directly: the
generator's callback is invoked by the test thread, so there is no timer to race.

### 3.1 `BufferedSoundGeneratorTests` — the underrun message

- `Underrun_LogMessage_IncludesGeneratorId` — drain, underrun once, assert the emitted message contains
  `#{GeneratorId}`. Extends the existing `Underrun_LogsAtWarning_WithThrottle` verification shape at `:144`.
- `Underrun_DeficitPpm_IsZeroOnFirstLine` — pins `C-185`: the first line of a session has `sinceLastLog == 0`
  and must report `0 ppm`, not `Infinity` or `NaN`.
- `BufferFillFraction_TracksCount_WithoutTakingTheLock` — add samples, read the fraction, `Read` some, read
  again. Asserts monotone behaviour and the `[0, 1]` range. (It cannot assert lock-freedom; the XML doc
  carries that claim and the reviewer checks it against the code.)

### 3.2 `SrcVariableResamplerTests` — the assertions the existing ones could not make

⚠ These carry the existing `[Trait("Category", "RequiresLibSampleRate")]`. Per `CLAUDE.md`, four tests in this
class are known-failing on Windows (`libsamplerate.so.0`, `TEST-5`) and that is not a regression.

- `Process_ReportsFramesConsumed` — the assertion `C-187` says does not exist. Feed a 512-frame stereo buffer
  at ratio 1.00025 with generous output space; assert `FramesConsumed > 0` and record it.
- `Process_SteadyState_ConsumesAllInput` — prime with one batch, then assert
  `result.FramesConsumed == inputFrames` on the second. ⚠ **If this fails, Task 2's loop is load-bearing and
  Task 3's gain needs revisiting — stop and report before continuing** (§0.10).
- `Process_SteadyState_OutputMatchesRatioWithin100Ppm` — replaces the ±10 % band. Sum generated frames over
  ~50 batches of 512 frames at ratio 1.001 and assert the aggregate is within **100 ppm** of
  `totalInput × 1.001`. Aggregating over many batches is what makes a tight tolerance achievable: per-batch
  boundary effects average out, and 100 ppm is still 10× tighter than the effect being corrected.
- `SetRatio_ChangesSubsequentOutputCount` — pins that the controller's one lever actually moves the plant:
  process at 1.0, `SetRatio(1.01)`, process the same input, assert the second produces more frames.

### 3.3 New — `PipeWireNativeStreamRatioControlTests`

⚠ **`PipeWireNativeStream` is `#if !WINDOWS_TARGET` and its `OnProcess` needs a live PipeWire daemon, so the
controller must not be tested through it.** Extract `UpdateResamplerRatio`'s arithmetic into a pure static
that the class calls, exactly as `AUD-11` did with `BuildStreamProperties` and as `LinuxBluetoothService` did
with `ParsePwCliOutputForBtNode`:

```csharp
  /// <summary>
  /// Pure ratio computation for <see cref="UpdateResamplerRatio"/>. Extracted as a static so AUD-15's
  /// control law can be pinned by a unit test with no PipeWire daemon, no native library and no
  /// timing. Radio.Infrastructure.Tests targets net10.0, so WINDOWS_TARGET is undefined there and
  /// this file compiles into the test build; only a native CALL would fail, and this makes none.
  /// </summary>
  internal static double ComputeRatio(
    double baseRatio, double fill, double targetFill, double gainPpmPerPercent,
    out bool clamped)
```

Tests, all pure and instant:

- `ComputeRatio_BufferBelowTarget_RaisesRatio` — the sign. Getting this backwards makes the row worse and
  nothing else would catch it.
- `ComputeRatio_BufferAboveTarget_LowersRatio`.
- `ComputeRatio_WithinDeadband_ReturnsBaseRatio`.
- `ComputeRatio_EmptyBuffer_ClampsAndFlags` — `fill = 0` against target `0.125` at gain 40 gives
  `1.00025 + 500 ppm = 1.00075`, inside the clamp; assert **not** clamped, and assert the value, so the gain
  sizing in `BluetoothOptions`' doc comment is pinned by a test rather than by prose.
- `ComputeRatio_AbsurdGain_IsClamped` — gain 10 000 with an empty buffer must clamp to `1.002` and set the
  flag.
- `ComputeRatio_AtTarget_IsExactlyBaseRatio` — no drift at the setpoint.

### 3.4 The one test that is a bounded negative check, and says so

`RatioController_DoesNotUpdateMoreThanOncePerSecond` needs elapsed time, because the 1 Hz throttle is a
wall-clock property. ⚠ **Per `CLAUDE.md` § *Test Timing*, state the direction it fails in**: starvation can
only make the controller update *less* often, which strengthens the assertion rather than flipping it. It is
therefore safe, in the same sense `DisabledByZeroThreshold_DoesNotRaise` is. **Do not "improve" it into a
`Task.Delay` that asserts an update *did* happen** — that is the shape `TEST-4` exists to warn about.

### 3.5 The full gate

Per `CLAUDE.md`, never pipe `dotnet test` into `tail`:

```bash
dotnet test RadioConsole.sln -c Release > /tmp/aud15-test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/aud15-test.log
```

Read the per-project summary lines. Known-failing on Windows and not a regression: four
`SrcVariableResamplerTests` (`libsamplerate.so.0`, `TEST-5`) — ⚠ **which now includes the new tests in 3.2, so
the expected Windows failure count in this class rises.** Say the new number in the PR body. Also
`NwsObservationIntegrationTests.RealNwsCall_*` (live network, `Category=Integration`, CI-excluded).

---

## 4. Verification on the box — a human must be present

⛔ **Nothing in §4 may run while the appliance is unattended.** The owner is away as of 2026-09-07.

### 4.1 Before deploying — settle the one config unknown (`C-181`)

The repo has no `appsettings.Production.json`, but `CLAUDE.md` says per-machine overrides live in one. One
read-only command:

```bash
ssh mmack@radio 'cat /opt/radio-console/api/appsettings.Production.json 2>/dev/null | head -60'
```

If it overrides `UseInputResampler`, `UseRealtimeCaptureThread`, or `InputResamplerInitialRatio`, **say so in
the PR body before merging** — §0.3's arithmetic assumes the shipped values.

### 4.2 Establish the onset, for free, from the existing logs (§0.7)

The command in §0.7. **This is the cheapest confirmation of the whole diagnosis**: a ~12-minute gap between
`Pre-filled buffer with 48000 samples` and the first `Buffer underrun` matches §0.3(D)'s prediction to within
the precision of a single 45-second sample. A ~10-second gap falsifies it, and Task 3 should not ship until
the plan is rewritten.

### 4.3 Deploy

```powershell
./deploy/Deploy-ToLinux.ps1
```

Defaults are `-TargetHost radio -Runtime linux-x64` since `OPS-1`. Confirm the SHA on both services:

```bash
curl -s http://radio:5000/api/health/version
curl -s http://radio:5002/api/health/version
```

### 4.4 The acceptance test — ≥30 minutes of sustained BT playback

**The counter must stop climbing.** Connect the handset, play continuously for at least 30 minutes — more than
twice §0.3(D)'s 12-minute onset — and then:

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "Buffer underrun" $F'
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep "PipeWire OnProcess" $F | tail -5'
```

**Pass criteria, all four:**

1. `total underruns` is **flat** across the last 15 minutes — not merely growing more slowly.
2. `fill=` in the `PipeWire OnProcess` line holds near **12.5 %** rather than trending to zero. ⭐ This is the
   direct observation the row never had, and Task 1 is what makes it exist.
3. `ratio=` has settled and is **not pinned at a clamp** (0.998 / 1.002), and no `ratio clamped` warning is
   recurring.
4. `resamplerStalledFrames=` is **0**, or is flat and small.

**And a listening check that no gate can replace:** the music must not be perceptibly detuned. §1.1 argues it
cannot be at these magnitudes; the owner's ears are the only instrument that settles it.

⚠ **Bound every query** (`--since '-30min'`, `tail`) and do not tail continuously — `CLAUDE.md` and
`MEMORY.md` both record that heavy log reads on this box correlate with audible distortion, which would
contaminate the very measurement being taken.

---

## 5. Docs impact

- `design/INTEGRATIONS.md` — per `MEMORY.md`, update if the BT capture section describes the resampler as
  fixed-ratio.
- `docs/plans/2026-05-22-bt-input-resampler.md:644` — its "Deferred / Future" section lists closed-loop ratio
  control as Phase 2. Add a line recording that `AUD-15` shipped it and why the static ratio was insufficient.
  ⚠ **Do not rewrite that plan's history**; it was correct to defer, and the deferral is exactly what made the
  defect findable.
- `design/FUTURE-WORK.md` — per `MEMORY.md`, record §6.1's USB prefill gap.
- ⛔ **`CLAUDE.md` needs no change.** Its § *PipeWire Quantum Tuning* claim of 512 frames / 10.67 ms was
  **confirmed** by §0.3(A), which is worth saying in the PR body precisely because the row treated it as
  suspect.

---

## 6. Deliberately out of scope

### 6.1 Follow-up rows this planning found and did not fix

- **`AUD-16` (proposed) — the USB/SDR generators have no startup cushion.** `USBAudioSourceBase.cs:323` and
  `SDRRadioAudioSource.cs:945` construct `BufferedSoundGenerator<float>` with every default and **never call
  `PreFillSilence`**, unlike both BT paths (`LinuxBluetoothService.cs:1809`, `BluetoothAudioSource.cs:554`).
  They still have `CompensateClockDrift`, so they are not exposed to *this* row's cause, but they start from
  zero cushion and rely on the drift compensator to build one. Not fixed here: different sources, different
  hardware, and Task 1's newly-visible `min=`/`fill=` lines will say whether it matters before anyone guesses.
- **`AUD-17` (proposed) — `PipeWireNativeStream.OnProcess` allocates on the PipeWire thread.**
  `ArrayPool<float>.Shared.Rent` at `:430` allocates whenever the per-core stack is empty, and the four
  `Marshal.PtrToStructure` calls at `:388-406` are marshalling operations on a real-time path (`C-186`). The
  generator has GC-correlation instrumentation (`_gcCorrelatedMissedDeadlines`); the stream has none. Worth a
  row **after** Task 1's contention numbers exist, not before.
- **The callback-timing question itself.** `interval max=21.94ms` and `execution max=14.20ms` are real and
  unexplained. §0.3(D) shows they are not this row's cause; §0.4 shows the instrument that would explain them
  is currently dark. **File a row once Task 1 has produced one week of `addSamples=` numbers** — with data,
  not with the speculation this plan deliberately refused to write.

### 6.2 The rollback, if Task 3 misbehaves on the box

Two levers, in order, neither requiring a redeploy of code:

1. `"UseAdaptiveResamplerRatio": false` — restores the exact pre-`AUD-15` fixed-ratio behaviour. Tasks 1, 2, 4
   and 5 all remain in force, so the observability improvements survive the rollback.
2. If the loop is off and the buffer still drains, raise `"InputResamplerInitialRatio"` toward the
   **`1.00096`** §0.3(C) derives. ⚠ **That number is one 45-second sample of one handset.** It is a mitigation
   for this pairing, not a fix, which is why §1.1 rejected it as the primary.

Both need a `radio-api` restart (`docs/plans/2026-05-22-bt-input-resampler.md:647` — the resampler flags are
not hot-reloadable). ⛔ **Restarting is a service interruption on a box that may be playing music; do it with
the owner's knowledge.**

### 6.3 Two defects this row is not

- **`AUD-10`** — the A2DP *transport* dying on pause. There, audio stops arriving because the source is gone.
  Here the transport is healthy for the entire 32-second window: `count` climbs by ~937 every 10 seconds
  throughout (§0.3(A)), which a dead transport cannot do. **The distinguishing observable:
  `AUD-10` stops the callbacks; `AUD-15` leaves them arriving on schedule and slightly short.**
- **The RotaryPhone HFP disconnect** (boundary doc, 2026-09-07). Same journal, different adapter, different
  service, different profile. Per `CLAUDE.md` § *Cross-Service Boundary*, HFP on `hci1` is RotaryPhone's; this
  row is entirely within A2DP on `hci0`. ⛔ **Nothing in this plan touches a WirePlumber config, a
  `bluetoothctl` adapter selection, or anything under `/etc/wireplumber/`**, so the boundary doc needs no
  update.

---

## 7. ⚠ What this plan does NOT establish

Stated plainly, because the row's whole instruction was not to substitute a plausible cause for a measured one.

1. **The 713 ppm figure is one 45-second sample of one handset**, and it assumes the four log windows are
   contiguous and that the deficit is entirely rate mismatch rather than partly jitter. Task 1's `{DeficitPpm}`
   field exists so the next reader gets the number continuously instead of deriving it.
2. **Whether `src_process` under-consumes in steady state is unknown.** §0.9(i) shows the wrapper *could* be
   losing frames and that nothing would report it. Task 2's `Process_SteadyState_ConsumesAllInput` is the
   measurement, and it must run before Task 3.
3. **What occupies the 14.20 ms callback is not established, and this plan deliberately does not guess.**
   §0.2 narrows it to one statement — `Monitor.Enter(_bufferLock)` — by elimination, and §0.4 shows the
   instrument that would confirm it is switched off. Task 1 turns it on. **A Builder who finds themselves
   writing a fix for callback latency has left this plan's scope** (`C-186`).
4. **Whether the box overrides any of these flags in `appsettings.Production.json`.** §4.1.
5. **The onset date.** §0.7 gives three ways to establish it; none has been run, because running them means
   touching an unattended appliance.

---

## 8. Queue row wording

⛔ **This plan does not edit `docs/BUILDER_QUEUE.md` or `docs/queue/AUD-15.md`** — a Builder is concurrently
editing the queue index for a different row. Whoever updates them should match the schema of the rows already
there and use the following.

**For `docs/BUILDER_QUEUE.md` § Queue** — plan link and the two facts a dispatcher needs:

> Plan: `design/plans/AUD-15-the-buffer-that-runs-empty.md`. **2 d + a ≥30-minute box session with the owner
> present.** ⛔ Not auto-mergeable — closed-loop control on the live audio path; confirmable only on the
> appliance.

**Amendments for `docs/queue/AUD-15.md`.** The row's evidence is sound; four of its *inferences* did not
survive reading the source, and the amendments below are the corrections. Nothing needs deleting.

> **Amended 2026-09-07 after planning. Cause identified — it is not CPU contention and it is not the callback
> timing.** Path D disabled `BufferedSoundGenerator.CompensateClockDrift` on the BT generator
> (`LinuxBluetoothService.cs:1326`) and replaced it with a resampler whose ratio is a **constant set once at
> construction**: `SrcVariableResampler.SetRatio` has zero callers in the tree. The shipped constant corrects
> **250 ppm**; the row's own numbers put the real skew at **~963 ppm**. The uncorrected residual drains the
> 0.5 s startup cushion in ~12 minutes, after which ordinary jitter lands as audible silence. Plan §0.3.
>
> Four corrections to this row, all recorded as constraints in the plan:
>
> - **`(Single)` is `typeof(T).Name`, not a generator.** Five `BufferedSoundGenerator<float>` sites log the
>   identical label and **two of them are on the Bluetooth path**. `GeneratorId` exists and is absent from the
>   message. Plan §0.6.
> - **`buffer: 0/384000` is a tautology.** It is read inside the underrun branch, which cannot execute while
>   the buffer is healthy. The unbiased reading (`min=`/`fill=` in `LogStats`) is at `LogDebug`, below
>   `Radio.API`'s Information floor, and therefore reaches no sink. Plan `C-183`, `C-184`.
> - **"A callback that outlasts the quantum cannot keep the buffer fed" does not hold for this buffer.** The
>   ring is 375 quanta deep; the worst observed excess is ~11 ms. Jitter of that size cannot empty it. Plan
>   `C-186`.
> - **`total underruns` is per generator, not per process** — the generator is rebuilt on every capture
>   acquisition, so `55` dates from the current BT session and the onset is a few hundred lines up in the same
>   file sink. Plan `C-190`.
>
> **Scope question 3 is answered off-box and needs no box command.** The cumulative `count` deltas give a mean
> callback interval of 10.66–10.68 ms against the documented 10.667 ms — the quantum is exactly as
> `CLAUDE.md` describes. Plan §0.3(A). ⛔ Do not run `pw-metadata`.
>
> **Ruled out in source, so nobody re-investigates:** priority inversion (`UseRealtimeCaptureThread` is
> `false` and unshipped — `LOG-10` is blocked on `LOG-6`); the 10-second log line inflating `execution max`
> (it is measured before the log runs); burst delivery (`bursts=2` is a **lifetime** count across 12 160
> callbacks). Plan §0.8.
