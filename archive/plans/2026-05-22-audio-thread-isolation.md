# Audio Thread Isolation Implementation Plan (Phase 2 / Plan D)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Apply standard Linux embedded-audio isolation practice (CPU affinity, scheduling priority, IO niceness, RT-priority limits) to `radio-api` and `radio-web` systemd units. Optionally bump the PipeWire native capture thread and Cast/HTTP audio threads to `SCHED_FIFO` priority via P/Invoke `pthread_setschedparam`. Validates the MEMORY-documented "audio distortion correlates with SSH activity" gap closes under the two-scenario protocol.

**Architecture:**

- systemd unit changes are the *primary* win — `CPUAffinity=2,3` on `radio-api`, `CPUAffinity=0,1` on `radio-web`; `Nice=-5`; `IOSchedulingClass=2 IOSchedulingPriority=2` (best-effort, near-realtime); `LimitNICE=-5:0` + `LimitRTPRIO=99` to allow the process to actually use the requested priorities.
- The P/Invoke `pthread_setschedparam` bumps for the BT capture thread + HTTP/Cast encode threads are *optional* and ship in a separate task with explicit opt-in flag (`BluetoothOptions.UseRealtimeCaptureThread`, default `false`). They need testing on both Ubuntu and Pi before becoming default.

This plan ships *immediately after Phase 1 merges*; it does not depend on Phase 1 code changes but does benefit from Phase 1's `PROBE-SYS-LOAD` infrastructure already being established.

**Tech Stack:** systemd directives, `pthread_setschedparam` via P/Invoke (Linux only, `#if !WINDOWS_TARGET`), existing `BluetoothCaptureWatchdog` from Plan A for one of the success criteria.

**Addresses**: FM2 (Cast sender pipeline jitter — load-amplified), FM8 (Cast host resource contention), FM-BT-11 (BT host resource contention) from both research docs.

---

## Task 0: Author probe scripts (research deliverable)

**Files:**
- Create: `scripts/research/heavy_load_harness.sh`
- Create: `scripts/research/cast_load_compare.py`

**Step 1: `heavy_load_harness.sh`** — generates the "heavy load" scenario the §3 two-scenario protocol requires. Composed of three concurrent SSH-launched loops:
- `journalctl -f > /dev/null` (continuous log streaming)
- `while true; do sqlite3 /opt/radio-console/data/metrics/metrics.db 'SELECT COUNT(*) FROM gauges'; sleep 0.5; done` (DB busy-loop)
- A Playwright/curl loop hitting `radio-web` endpoints to simulate UI traffic

Takes positional arg: duration in seconds. Cleanly terminates all child loops on SIGINT.

**Step 2: `cast_load_compare.py`** — given pairs of `(baseline_light, baseline_heavy, after_light, after_heavy)` artifacts (each containing PROBE-CAST-BUFFER + PROBE-CAST-AUDIO + PROBE-SYS-LOAD), computes:
- Per-scenario `silence_events/h` and `bufferAhead_p5` deltas
- Cross-scenario gap (heavy − light) before and after
- `radio-api` CPU-on-cores-2,3 and `radio-web` CPU-on-cores-0,1 (post-change) — verifies pinning is taking effect
- PASS/FAIL against the §7 Idea #9 success criterion in the cast doc.

Reuses `bt_stall_compare.py`-style format.

**Step 3: Commit**

```bash
git add scripts/research/heavy_load_harness.sh scripts/research/cast_load_compare.py
git commit -m "scripts(research): heavy-load harness + cast load-compare script"
```

---

## Task 1: systemd unit — `radio-api.service`

**Files:**
- Modify: `deploy/common/radio-api.service`

**Step 1: Find the `[Service]` section.** Add the resource-isolation directives. Place them logically after `Group=` / `WorkingDirectory=` and before `Environment=`:

```ini
# === Audio-thread isolation (added 2026-05-22, plan D) ===
# Pin to cores 2,3 (leave 0,1 for OS + journald + sshd + radio-web).
# Verify against `nproc` on each deployment target — radio (N100) has 4 cores;
# Pi 5 has 4 cores; both layouts identical.
CPUAffinity=2 3

# Boost scheduling priority. Requires LimitNICE below to be actually grant-able.
Nice=-5
LimitNICE=-5:0

# IO best-effort near-realtime (class 2, priority 2). Class 1 (realtime) is
# usually overkill for application audio; class 2 + low prio matches CCRMA/JACK
# guidance.
IOSchedulingClass=2
IOSchedulingPriority=2

# Allow process to use SCHED_FIFO up to priority 99 if it requests it via
# pthread_setschedparam (Task 4 below — feature-flagged). Without this limit
# the call would fail with EPERM.
LimitRTPRIO=99

# Allow mlock for any future low-latency pages (not used today; cheap to allow).
LimitMEMLOCK=infinity
# === end audio-thread isolation ===
```

**Step 2: Verify systemd unit parses**

After deployment, on the target host:
```bash
ssh mmack@radio "sudo systemd-analyze verify /opt/radio-console/radio-api.service"
```
Expected: no errors.

**Step 3: Commit**

```bash
git add deploy/common/radio-api.service
git commit -m "feat(deploy): pin radio-api to cores 2,3 with Nice=-5 + RT-prio limits"
```

---

## Task 2: systemd unit — `radio-web.service`

**Files:**
- Modify: `deploy/common/radio-web.service` (or whatever the project's web service file is named)

**Step 1: Find the `[Service]` section.** Add the complementary affinity:

```ini
# === Complement to radio-api's pin to cores 2,3 (plan D, 2026-05-22) ===
# Keep radio-web on cores 0,1 so it doesn't crowd the audio cores.
CPUAffinity=0 1
# Web is not audio-critical — default scheduling priority.
# === end ===
```

**Step 2: Commit**

```bash
git add deploy/common/radio-web.service
git commit -m "feat(deploy): pin radio-web to cores 0,1 (complement to api pin)"
```

---

## Task 3: Deploy + verify systemd-level changes (no code)

**Step 1: Deploy**

```bash
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
```

**Step 2: Verify directives are honored**

```bash
ssh mmack@radio "sudo systemctl daemon-reload && sudo systemctl restart radio-api radio-web"

# Verify CPU affinity:
ssh mmack@radio "taskset -p \$(pgrep radio-api)"   # expect mask 0xC (cores 2,3)
ssh mmack@radio "taskset -p \$(pgrep radio-web)"   # expect mask 0x3 (cores 0,1)

# Verify nice:
ssh mmack@radio "ps -o pid,nice,comm \$(pgrep radio-api)"   # expect NI=-5

# Verify IO sched:
ssh mmack@radio "sudo ionice -p \$(pgrep radio-api)"   # expect: best-effort: prio 2

# Verify RT-prio limit:
ssh mmack@radio "cat /proc/\$(pgrep radio-api)/limits | grep RTPRIO"   # expect Max=99
```

Expected: all directives are reflected on the running process. If any fail, the systemd unit didn't reload — repeat `daemon-reload`.

**Step 3:** **No commit at this task** — this is a deployment-verification task.

---

## Task 4: (optional, feature-flagged) SCHED_FIFO bump on the PipeWire native capture thread

**Files:**
- Modify: `src/Radio.Core/Configuration/BluetoothOptions.cs`
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs`

**Step 1: Add config flag**

```csharp
/// <summary>
/// When true, the PipeWire capture thread is bumped to SCHED_FIFO priority 50
/// via pthread_setschedparam. Requires systemd LimitRTPRIO ≥ 50. Linux-only.
/// Default false — turn on after measurement confirms the systemd-level isolation
/// in Plan D's Tasks 1-3 is insufficient.
/// </summary>
public bool UseRealtimeCaptureThread { get; set; } = false;

/// <summary>
/// SCHED_FIFO priority to apply when UseRealtimeCaptureThread is true.
/// Values 50-70 are typical for audio (above default kernel IRQ threads at ~50,
/// below kernel critical threads at 99). Default 50.
/// </summary>
public int RealtimeCaptureThreadPriority { get; set; } = 50;
```

**Step 2: P/Invoke for `pthread_setschedparam`**

Add to `PipeWireNative.cs` (or a small new `LinuxPosixThread.cs`):

```csharp
#if !WINDOWS_TARGET
[StructLayout(LayoutKind.Sequential)]
internal struct SchedParam
{
  public int sched_priority;
}

internal const int SCHED_FIFO = 1;

[DllImport("libpthread.so.0", EntryPoint = "pthread_self")]
internal static extern IntPtr pthread_self();

[DllImport("libpthread.so.0", EntryPoint = "pthread_setschedparam", SetLastError = true)]
internal static extern int pthread_setschedparam(IntPtr thread, int policy, ref SchedParam param);
#endif
```

**Step 3: Apply inside `OnProcess` on first call**

In `PipeWireNativeStream.OnProcess`, gate the priority bump behind a once-only flag + the options check:

```csharp
private bool _rtPriorityApplied;

private static void OnProcess(IntPtr userData)
{
  // ... existing GCHandle resolution ...

  if (!self._rtPriorityApplied && self._useRealtime)
  {
    self._rtPriorityApplied = true;
    var param = new SchedParam { sched_priority = self._rtPriority };
    var result = pthread_setschedparam(pthread_self(), SCHED_FIFO, ref param);
    if (result != 0)
    {
      self._logger.LogWarning("pthread_setschedparam(SCHED_FIFO, {Prio}) failed with errno {Errno}",
        self._rtPriority, Marshal.GetLastPInvokeError());
    }
    else
    {
      self._logger.LogInformation("PipeWire capture thread bumped to SCHED_FIFO priority {Prio}", self._rtPriority);
    }
  }

  // ... existing OnProcess body ...
}
```

Constructor takes new args `bool useRealtime, int rtPriority` from `BluetoothOptions`.

**Step 4: Build + commit**

```bash
git add src/Radio.Core/Configuration/BluetoothOptions.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNative.cs
git commit -m "feat(bt): optional SCHED_FIFO bump on PW capture thread (feature-flagged)"
```

---

## Task 5: Documentation — deploy README + boundary doc

**Files:**
- Modify: `deploy/README.md` (or wherever deploy steps are documented)
- Modify: `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` if cores 2,3 are crossing into RotaryPhone's space

**Step 1: Document the affinity layout** in `deploy/README.md`:

```markdown
### CPU affinity layout (added 2026-05-22)

Both production deployment targets have 4 cores. We split:
  - cores 0,1: OS + journald + sshd + radio-web
  - cores 2,3: radio-api (BT capture, Cast encode, all audio paths)

If RotaryPhone is co-located on `radio` (it is not currently), document
RotaryPhone's affinity layout in `docs/prompts/RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`
to avoid double-pinning.
```

**Step 2:** Check the boundary doc — does RotaryPhone run on the same host? Per MEMORY: "RotaryPhone is UI-only" and accessed via REST — so no co-location. No boundary doc update needed.

**Step 3: Commit**

```bash
git add deploy/README.md
git commit -m "docs(deploy): document CPU affinity layout"
```

---

## Task 6: Full build + test

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```
Expected: 0 warnings; all tests pass. (The optional SCHED_FIFO code path is feature-flagged off by default — no test coverage required for now; the systemd changes have no unit-test surface.)

---

## Task 7: Deploy + integration test on Ubuntu (the load-isolation win)

```bash
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
ssh mmack@radio "sudo systemctl daemon-reload && sudo systemctl restart radio-api radio-web"
```

Re-verify Task 3 checks. All directives should still take effect.

---

## Task 8: Verify acceptance criteria — the two-scenario protocol

**This is the load-validation moment.** Run the same probe under both light and heavy scenarios, on baseline (`main`) and post-change (this branch).

**Baseline — light load** (`main` deploy, no heavy harness running):
```bash
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64  # against main
# Start Cast DC playback of reference playlist
arecord -D plughw:CARD=USB_Audio -d 3600 -f cd /tmp/cast_baseline_light.wav &
ssh mmack@radio "/opt/radio-console/scripts/research/sysload_capture.sh 3600" > baseline_sysload_light.txt
# Wait 1 h
python3 scripts/research/cast_audio_glitch.py /tmp/cast_baseline_light.wav --silence-min-ms 50 > baseline_audio_light.txt
ssh mmack@radio "sqlite3 /opt/radio-console/data/metrics/metrics.db 'SELECT * FROM gauges WHERE metric=\"cast.dc.buffer_ahead_s\" AND ts > strftime(\"%s\",\"now\",\"-1 hour\")*1000'" | python3 scripts/research/cast_dc_buffer_summarize.py > baseline_buffer_light.txt
```

**Baseline — heavy load**:
```bash
# Same playlist, but with the heavy harness running concurrently
ssh mmack@radio "/opt/radio-console/scripts/research/heavy_load_harness.sh 3600" &
# Repeat the audio capture + sysload + buffer-summarize, save as baseline_*_heavy.txt
```

**Post-change — light + heavy**: deploy the feature branch and repeat both scenarios. Save as `after_*_light.txt` and `after_*_heavy.txt`.

**Success criterion** (must hold across both scenarios):

- **Light load**: `silence_events/h` from PROBE-CAST-AUDIO does not regress (≤ baseline-light +1 event); `bufferAhead` p5 does not regress (≥ baseline-light p5 −0.5 s)
- **Heavy load**: `silence_events/h` drops by `≥ 80 %` vs baseline-heavy; `bufferAhead` p5 stays within `1.0 s` of light-load p5
- **Cross-scenario gap** (heavy − light): on baseline, expect e.g. `+8` silence events/h of degradation; post-change, this gap drops to `≤ +2` events/h
- PROBE-SYS-LOAD post-change shows `radio-api` CPU% on cores 2,3 (taskset -p mask reads as `0xc`) and `radio-web` CPU% on cores 0,1 (mask `0x3`)
- **Negative check**: no new failure modes — RDS station-name decode, Spotify playback, file playback all work as before (smoke test on the 5 most-used features)

**Debug-agent verification**:

```bash
python3 scripts/research/cast_load_compare.py \
  baseline_audio_light.txt baseline_audio_heavy.txt \
  after_audio_light.txt after_audio_heavy.txt
```

Expected: `PASS` plus deltas.

**If FAIL**: diagnose whether the affinity is being honored (`taskset -p`) or whether the cross-scenario gap is bigger than expected and Task 4 (SCHED_FIFO bump) needs to be enabled.

---

## Task 9: Verify on Pi as well

```bash
./deploy/Deploy-ToPi.ps1 -PiHost piradio -PiUser radio
ssh radio@piradio "sudo systemctl daemon-reload && sudo systemctl restart radio-api radio-web"
ssh radio@piradio "taskset -p \$(pgrep radio-api)"   # expect 0xC
```

Run a shorter (15 min) audio-capture validation on Pi — confirm no regression. Pi has 4 cores; same layout applies.

---

## Task 10: Open PR + merge

```bash
git push -u origin feat/audio-thread-isolation

gh pr create --title "feat(deploy): isolate audio-pipeline threads via CPU affinity + scheduling priority" --body "$(cat <<'EOF'
## Summary

Implements [Plan D from the Cast/BT research arc](../docs/plans/2026-05-22-cast-bt-phase-1-2-arc.md). Applies standard Linux embedded-audio isolation practice — `radio-api` pinned to cores 2,3 with `Nice=-5` + `LimitRTPRIO=99` + IO best-effort near-realtime; `radio-web` pinned to cores 0,1.

Closes the MEMORY-documented load gap ("audio distortion correlates with SSH activity") by structurally separating audio-critical scheduling from journald + sshd + web traffic.

Optional SCHED_FIFO bump on the PipeWire capture thread is included but feature-flagged off (`BluetoothOptions.UseRealtimeCaptureThread = false`); turn it on later if measurement shows the systemd-level isolation is insufficient.

## Acceptance criteria (verified)

- Heavy-load `silence_events/h` drops by ≥80 % vs baseline-heavy
- Light-load: no regression
- Cross-scenario gap drops from baseline ~+8/h to ≤+2/h
- `taskset -p` confirms pinning on both Ubuntu (radio) and Pi (piradio)
- See attached `cast_load_compare.py` PASS artifact

## Test plan

- [x] systemd-analyze verify
- [x] Affinity / nice / IO-sched / RT-prio verified via `taskset`, `ps`, `ionice`, `/proc/.../limits` on radio
- [x] Same verifications on piradio
- [x] Two-scenario probe runs (light + heavy) on radio
- [x] 15-min smoke test on piradio
- [x] Negative-check: RDS, Spotify, file-playback all work as before

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Merge once Mark approves.

---

## Out of scope

- **CPU isolation via `isolcpus=` kernel boot param**: a stricter form of isolation that removes cores from the kernel's normal scheduler entirely. Not needed for this round — `CPUAffinity=` is enough.
- **PREEMPT_RT kernel**: real-time kernel patches. Would help further but a kernel-swap is a heavy operation; defer to Phase 4+.
- **Per-thread affinity inside radio-api** (e.g. binding the BT capture thread to a specific core within 2,3): the kernel scheduler within the affinity mask is usually fine; per-thread pinning is a Phase 4 micro-optimization.
- **Windows path**: no equivalent on Windows (the dev environment is on Windows but production is Linux). NOPs on Windows.
- **Logging-path audit (idea #10)**: addresses load contribution from synchronous flushes; this plan addresses load contention from external processes. They're complementary — if Plan D's verification shows residual gap, Plan #10 is the next move.
- **Background-op gating (idea #11)**: same reasoning — complementary, not redundant.
