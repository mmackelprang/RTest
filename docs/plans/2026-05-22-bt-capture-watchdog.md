# BT Capture Watchdog Implementation Plan (Phase 1 / Plan A)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Detect FM-BT-3 (the known long-uptime PipeWire OnProcess quiescence bug) by polling `_lastOnProcessTimestamp` from the active `PipeWireNativeStream`; raise a `CaptureStreamStalled` event when the callback has been silent past threshold; wire into the existing `OnGeneratorStalled` recovery path with interlock-guarded dedup.

**Architecture:** New `BluetoothCaptureWatchdog` `BackgroundService`. Polls a public `LastOnProcessTimestamp` property exposed on `PipeWireNativeStream` (currently private). When wall-clock now minus the timestamp exceeds `BluetoothOptions.OnProcessStallThresholdMs` for `ConsecutiveStalledChecks` checks while the BT source is active, raises `CaptureStreamStalled`. `LinuxBluetoothService` re-raises this on `IBluetoothService` so `BluetoothAudioSource` can subscribe alongside its existing `CaptureStreamRecovered` and `OnGeneratorStalled` handlers — recovery flows through the *same* `_recoveryInProgress` interlock at [BluetoothAudioSource.cs:L363](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs), reusing the existing recovery path.

**Tech Stack:** .NET 10 `BackgroundService`, existing `PipeWireNativeStream` instrumentation, existing `BluetoothAudioSource` recovery interlock, Radio.Metrics counter for visibility.

**Addresses**: FM-BT-3 from [`docs/research/2026-05-22-bt-audio-stabilization.md`](../research/2026-05-22-bt-audio-stabilization.md) §4.

---

## Task 0: Author probe scripts (research deliverable)

**Files:**
- Create: `scripts/research/sysload_capture.sh`
- Create: `scripts/research/sysload_correlate.py`
- Create: `scripts/research/bt_stall_detect.py`
- Create: `scripts/research/bt_stall_compare.py`

These are *test instrumentation* used by the acceptance-criteria verification in Task 10. They live in `scripts/research/` (not shipped to production).

**Step 1: `sysload_capture.sh`** — runs `vmstat 1`, `iostat -x 1`, `pidstat -p $(pgrep -d, radio-api,radio-web,journald,sqlite3,sshd) 1`, and per-second `journalctl --since "1 second ago" -o cat | wc -l` + `pgrep sshd | wc -l` snapshots concurrently; all timestamped with monotonic clock; merged into a single tab-separated `sysload_<timestamp>.tsv` artifact. Takes one positional arg: duration in seconds. Used by every Phase 1+2 plan.

**Step 2: `sysload_correlate.py`** — takes two args: an audio-event-list artifact and a sysload tsv. For each audio event timestamp, computes the 5-second-pre-event median CPU%, total IO MB/s, log line rate, and active SSH session count. Outputs a classified event list: `event_ts, audio_metric_value, cpu_5s_median, io_5s_total, log_rate_5s_median, ssh_sessions_5s_max, classification`. Classification: `quiet_host` if CPU < 70 % AND log_rate < 100/s AND ssh_sessions == 0; else `load_correlated`.

**Step 3: `bt_stall_detect.py`** — reads stdin (journalctl text); detects windows where the `🔬 PipeWire OnProcess` log line has been absent for `>= --window` seconds despite the most recent `BluetoothAudioSource: state == Playing` log. Outputs events as one-per-line: `start_ts, end_ts, gap_seconds`. Accepts `--window N` (default 60s).

**Step 4: `bt_stall_compare.py`** — reads two classified-event artifacts (baseline + after); produces PASS/FAIL against the success criteria from Task 10; outputs per-metric deltas.

**Step 5: Build verification (sanity)**

Run: `bash scripts/research/sysload_capture.sh 10 && ls -la sysload_*.tsv`
Expected: a TSV file is produced with non-zero size.

**Step 6: Commit**

```bash
git add scripts/research/
git commit -m "scripts(research): add probe scripts for BT capture watchdog measurement"
```

---

## Task 1: Expose `LastOnProcessTimestamp` from `PipeWireNativeStream`

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs:L44-L50` (existing instrumentation fields)

**Step 1:** Add a public read-only property mirroring the existing `_lastOnProcessTimestamp` field (currently private). Use `Volatile.Read` since `OnProcess` runs on the PipeWire thread loop.

```csharp
/// <summary>
/// Wall-clock-equivalent stopwatch timestamp of the most recent OnProcess callback.
/// Zero if OnProcess has not fired yet. Used by BluetoothCaptureWatchdog to detect
/// FM-BT-3 silent quiescence. Safe to read from any thread.
/// </summary>
public long LastOnProcessTimestamp => Volatile.Read(ref _lastOnProcessTimestamp);

/// <summary>
/// Returns the elapsed milliseconds since the last OnProcess callback, or
/// long.MaxValue if no callback has fired yet.
/// </summary>
public long MillisecondsSinceLastOnProcess()
{
  var last = LastOnProcessTimestamp;
  if (last == 0) return long.MaxValue;
  return (long)((Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency * 1000.0);
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Radio.Infrastructure --configuration Release`
Expected: 0 warnings, 0 errors.

**Step 3: Commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs
git commit -m "feat(bt): expose LastOnProcessTimestamp on PipeWireNativeStream"
```

---

## Task 2: BluetoothOptions — add watchdog config

**Files:**
- Modify: `src/Radio.Core/Configuration/BluetoothOptions.cs`

**Step 1:** Add three properties to `BluetoothOptions`:

```csharp
/// <summary>
/// Threshold in milliseconds for the OnProcess-interval watchdog (FM-BT-3 detection).
/// When wall-clock now minus the last OnProcess timestamp exceeds this for
/// <see cref="ConsecutiveStalledChecks"/> consecutive watchdog ticks, the watchdog
/// raises CaptureStreamStalled. Set to 0 to disable the watchdog entirely.
/// </summary>
public int OnProcessStallThresholdMs { get; set; } = 5000;

/// <summary>
/// How often the watchdog checks the OnProcess timestamp (milliseconds).
/// </summary>
public int WatchdogTickIntervalMs { get; set; } = 2000;

/// <summary>
/// Number of consecutive watchdog ticks past <see cref="OnProcessStallThresholdMs"/>
/// required before the watchdog raises the stall event. Default 3 (~6 s @ 2 s tick)
/// to suppress single transient hiccups.
/// </summary>
public int ConsecutiveStalledChecks { get; set; } = 3;
```

**Step 2: Update `appsettings.json` defaults**

In `src/Radio.API/appsettings.json` under `Bluetooth`:

```json
"OnProcessStallThresholdMs": 5000,
"WatchdogTickIntervalMs": 2000,
"ConsecutiveStalledChecks": 3
```

**Step 3: Build + commit**

Run: `dotnet build src/Radio.Core --configuration Release`
Expected: 0 warnings.

```bash
git add src/Radio.Core/Configuration/BluetoothOptions.cs src/Radio.API/appsettings.json
git commit -m "feat(bt): add OnProcess watchdog config options"
```

---

## Task 3: `CaptureStreamStalled` event on `IBluetoothService`

**Files:**
- Modify: `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs` (stub)
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs` (stub)

**Step 1: Add event to interface**

```csharp
/// <summary>
/// Raised when the watchdog detects that the BT capture stream's OnProcess callback
/// has been silent past the configured threshold (FM-BT-3 detection).
/// Subscribers should attempt recovery via the same path used for OnGeneratorStalled.
/// </summary>
event EventHandler<CaptureStreamStalledEventArgs>? CaptureStreamStalled;
```

Plus the event-args class in the same file:

```csharp
public class CaptureStreamStalledEventArgs : EventArgs
{
  public required string DeviceAddress { get; init; }
  public required long ElapsedMsSinceLastCallback { get; init; }
  public required int ConsecutiveStalledChecks { get; init; }
}
```

**Step 2: Implement in `LinuxBluetoothService`** — declare the event field; the raise call comes from Task 5's watchdog wiring.

**Step 3: Stub in Windows + Mock implementations** — just `public event EventHandler<CaptureStreamStalledEventArgs>? CaptureStreamStalled;` with no raise calls.

**Step 4: Build + commit**

```bash
git add src/Radio.Core/Interfaces/Audio/IBluetoothService.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs
git commit -m "feat(bt): add CaptureStreamStalled event on IBluetoothService"
```

---

## Task 4: `BluetoothCaptureWatchdog` background service

**Files:**
- Create: `src/Radio.Infrastructure/Audio/Services/BluetoothCaptureWatchdog.cs`
- Modify: `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`

**Step 1: Implement the watchdog**

The watchdog needs to read `LastOnProcessTimestamp` from the *currently active* `PipeWireNativeStream`. The cleanest approach is to introduce a small accessor on `LinuxBluetoothService` (Linux-only path) that returns the current stream's timestamp + the connected device's address, or null if no stream is active.

Add to `LinuxBluetoothService`:

```csharp
/// <summary>
/// Snapshot for the watchdog: address + elapsed ms since last OnProcess.
/// Returns null if no native capture stream is active.
/// </summary>
internal (string Address, long ElapsedMs)? GetCaptureStreamSnapshot()
{
  var stream = _nativeStream;
  var device = ConnectedDevice;
  if (stream == null || device == null) return null;
  return (device.Address, stream.MillisecondsSinceLastOnProcess());
}

/// <summary>
/// Invoked by the watchdog when a stall is confirmed. Raises CaptureStreamStalled.
/// </summary>
internal void RaiseCaptureStreamStalled(string address, long elapsedMs, int consecutive)
{
  _metricsCollector?.Increment("bluetooth.capture_stall_detected_total");
  _logger.LogWarning(
    "BluetoothCaptureWatchdog: stall detected for {Address}, elapsed {Elapsed}ms after {N} consecutive checks",
    address, elapsedMs, consecutive);
  CaptureStreamStalled?.Invoke(this, new CaptureStreamStalledEventArgs
  {
    DeviceAddress = address,
    ElapsedMsSinceLastCallback = elapsedMs,
    ConsecutiveStalledChecks = consecutive
  });
}
```

Now create `BluetoothCaptureWatchdog`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Platform.Bluetooth;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Periodic watchdog that detects FM-BT-3 (silent OnProcess quiescence on a long-running
/// PipeWire capture stream). When the active stream's MillisecondsSinceLastOnProcess
/// exceeds BluetoothOptions.OnProcessStallThresholdMs for ConsecutiveStalledChecks ticks,
/// raises CaptureStreamStalled on IBluetoothService.
/// </summary>
public sealed class BluetoothCaptureWatchdog : BackgroundService
{
  private readonly ILogger<BluetoothCaptureWatchdog> _logger;
  private readonly IOptionsMonitor<BluetoothOptions> _options;
  private readonly LinuxBluetoothService? _linuxService;
  private int _consecutiveStalledChecks;

  public BluetoothCaptureWatchdog(
    ILogger<BluetoothCaptureWatchdog> logger,
    IOptionsMonitor<BluetoothOptions> options,
    LinuxBluetoothService? linuxService = null)  // null on Windows / Mock
  {
    _logger = logger;
    _options = options;
    _linuxService = linuxService;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (_linuxService == null)
    {
      _logger.LogInformation("BluetoothCaptureWatchdog: no Linux BT service, watchdog disabled");
      return;
    }

    _logger.LogInformation("BluetoothCaptureWatchdog: starting (threshold={Threshold}ms, tick={Tick}ms, consecutive={N})",
      _options.CurrentValue.OnProcessStallThresholdMs,
      _options.CurrentValue.WatchdogTickIntervalMs,
      _options.CurrentValue.ConsecutiveStalledChecks);

    while (!stoppingToken.IsCancellationRequested)
    {
      var opts = _options.CurrentValue;
      if (opts.OnProcessStallThresholdMs <= 0)
      {
        // watchdog disabled
        await Task.Delay(opts.WatchdogTickIntervalMs, stoppingToken);
        continue;
      }

      var snapshot = _linuxService.GetCaptureStreamSnapshot();
      if (snapshot == null)
      {
        // no active stream; reset consecutive counter
        _consecutiveStalledChecks = 0;
      }
      else if (snapshot.Value.ElapsedMs >= opts.OnProcessStallThresholdMs)
      {
        _consecutiveStalledChecks++;
        if (_consecutiveStalledChecks >= opts.ConsecutiveStalledChecks)
        {
          _linuxService.RaiseCaptureStreamStalled(
            snapshot.Value.Address, snapshot.Value.ElapsedMs, _consecutiveStalledChecks);
          _consecutiveStalledChecks = 0; // reset so we don't fire every tick after the first detection
        }
      }
      else
      {
        _consecutiveStalledChecks = 0;
      }

      await Task.Delay(opts.WatchdogTickIntervalMs, stoppingToken);
    }
  }
}
```

**Step 2: DI registration**

In `AudioServiceExtensions.cs`, after the existing BT registrations:

```csharp
// FM-BT-3 watchdog (Linux-only; depends on LinuxBluetoothService having been registered as concrete type)
services.AddSingleton<BluetoothCaptureWatchdog>();
services.AddHostedService(sp => sp.GetRequiredService<BluetoothCaptureWatchdog>());
```

Use the `AddSingleton + AddHostedService(factory)` pattern (see MEMORY: "DI / Hosted Service Gotchas").

**Step 3: Build + commit**

```bash
git add src/Radio.Infrastructure/Audio/Services/BluetoothCaptureWatchdog.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs \
        src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs
git commit -m "feat(bt): add BluetoothCaptureWatchdog to detect FM-BT-3 quiescence"
```

---

## Task 5: Wire into `BluetoothAudioSource` recovery path

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`

**Step 1: Subscribe to the new event** in the constructor (alongside the existing subscriptions at L79-L84):

```csharp
_bluetoothService.CaptureStreamStalled += OnCaptureStreamStalled;
```

**Step 2: Implement handler** — funnel to the existing `OnGeneratorStalled` path so the same `_recoveryInProgress` interlock dedups both stall sources:

```csharp
private void OnCaptureStreamStalled(object? sender, CaptureStreamStalledEventArgs e)
{
  Logger.LogWarning(
    "BluetoothAudioSource: capture stream stall detected via watchdog ({Address}, elapsed={Elapsed}ms); triggering recovery",
    e.DeviceAddress, e.ElapsedMsSinceLastCallback);
  // Reuse the existing recovery path; the interlock guards against double-fire.
  OnGeneratorStalled(BluetoothSourceId);  // or whatever the existing sourceId constant is — verify in code
}
```

(Use whatever identifier `OnGeneratorStalled` already receives — verify by reading the existing method signature at L360.)

**Step 3: Unsubscribe in `DisposeCore`** (alongside L297-L301):

```csharp
_bluetoothService.CaptureStreamStalled -= OnCaptureStreamStalled;
```

**Step 4: Build + commit**

```bash
git add src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs
git commit -m "feat(bt): route CaptureStreamStalled through existing recovery interlock"
```

---

## Task 6: Unit tests for watchdog logic

**Files:**
- Create: `tests/Radio.Infrastructure.Tests/Audio/Services/BluetoothCaptureWatchdogTests.cs`

**Step 1:** Mock `LinuxBluetoothService` is impractical (concrete class with heavy dependencies). Instead, refactor `BluetoothCaptureWatchdog` to depend on a thin interface `ICaptureStreamSnapshotSource` exposing `GetCaptureStreamSnapshot()` + `RaiseCaptureStreamStalled()`. `LinuxBluetoothService` implements this internal interface.

Re-do the constructor in `BluetoothCaptureWatchdog` to take `ICaptureStreamSnapshotSource?` instead of `LinuxBluetoothService?`. The DI registration becomes:

```csharp
services.AddSingleton<ICaptureStreamSnapshotSource>(sp =>
  sp.GetService<LinuxBluetoothService>() ?? NullSnapshotSource.Instance);
```

**Step 2: Write tests**

```csharp
public class BluetoothCaptureWatchdogTests
{
  [Fact]
  public async Task NoActiveStream_DoesNotRaise()
  {
    var source = new FakeSnapshotSource(snapshot: null);
    var watchdog = CreateWatchdog(source, threshold: 1000, tick: 50, consecutive: 2);
    await RunForMs(watchdog, 250);
    Assert.Equal(0, source.RaiseCount);
  }

  [Fact]
  public async Task BelowThreshold_DoesNotRaise()
  {
    var source = new FakeSnapshotSource(("AA:BB:CC:DD:EE:FF", elapsedMs: 500));
    var watchdog = CreateWatchdog(source, threshold: 1000, tick: 50, consecutive: 2);
    await RunForMs(watchdog, 250);
    Assert.Equal(0, source.RaiseCount);
  }

  [Fact]
  public async Task AboveThreshold_RaisesAfterConsecutiveChecks()
  {
    var source = new FakeSnapshotSource(("AA:BB:CC:DD:EE:FF", elapsedMs: 6000));
    var watchdog = CreateWatchdog(source, threshold: 5000, tick: 50, consecutive: 3);
    await RunForMs(watchdog, 250); // ~5 ticks
    Assert.Equal(1, source.RaiseCount);
  }

  [Fact]
  public async Task DisabledByZeroThreshold_DoesNotRaise()
  {
    var source = new FakeSnapshotSource(("AA:BB:CC:DD:EE:FF", elapsedMs: 99999));
    var watchdog = CreateWatchdog(source, threshold: 0, tick: 50, consecutive: 1);
    await RunForMs(watchdog, 250);
    Assert.Equal(0, source.RaiseCount);
  }

  [Fact]
  public async Task IntermittentStall_ResetsCounter()
  {
    var source = new FakeSnapshotSource();
    var watchdog = CreateWatchdog(source, threshold: 5000, tick: 50, consecutive: 3);
    var task = Task.Run(() => watchdog.StartAsync(CancellationToken.None));
    await Task.Delay(100);
    source.Set(("AA:BB:CC:DD:EE:FF", elapsedMs: 6000));
    await Task.Delay(100); // ~2 ticks above threshold
    source.Set(("AA:BB:CC:DD:EE:FF", elapsedMs: 100));   // recovery
    await Task.Delay(100); // ~2 ticks healthy
    source.Set(("AA:BB:CC:DD:EE:FF", elapsedMs: 6000));
    await Task.Delay(100); // ~2 ticks above threshold again
    await watchdog.StopAsync(CancellationToken.None);
    // Never reached 3 consecutive above-threshold → no raise
    Assert.Equal(0, source.RaiseCount);
  }
}
```

Plus a `FakeSnapshotSource` test helper + `CreateWatchdog`/`RunForMs` builders.

**Step 3: Run tests**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothCaptureWatchdogTests" --configuration Release -v n
```
Expected: 5 tests PASS.

**Step 4: Commit**

```bash
git add tests/Radio.Infrastructure.Tests/Audio/Services/BluetoothCaptureWatchdogTests.cs \
        src/Radio.Infrastructure/Audio/Services/BluetoothCaptureWatchdog.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs \
        src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs
git commit -m "test(bt): unit tests for BluetoothCaptureWatchdog + interface refactor"
```

---

## Task 7: Full build + test verification

**Step 1:**
```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```
Expected: 0 warnings; all ~1,697+ tests pass.

---

## Task 8: Deploy to Ubuntu

```bash
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
```

Verify watchdog started:
```bash
ssh mmack@radio "journalctl -u radio-api -p info --since '1 minute ago' | grep -i 'BluetoothCaptureWatchdog'"
```
Expected: `BluetoothCaptureWatchdog: starting (threshold=5000ms, tick=2000ms, consecutive=3)`.

---

## Task 9: Verify acceptance criteria (the measurement-discipline contract)

These are the success criteria from the research's 5-block measurement structure ([`docs/research/2026-05-22-bt-audio-stabilization.md`](../research/2026-05-22-bt-audio-stabilization.md) §7 Idea #1). A debug agent runs this Task as the merge gate.

**Baseline probe** (run against `main` BEFORE this branch ships):

```bash
ssh mmack@radio "journalctl -u radio-api --since '72 hours ago' -o cat" \
  | python3 scripts/research/bt_stall_detect.py --window 60s \
  > baseline_bt_stall.txt

ssh mmack@radio "/opt/radio-console/scripts/research/sysload_capture.sh 259200" \
  > baseline_bt_sysload.txt

python3 scripts/research/sysload_correlate.py \
  baseline_bt_stall.txt baseline_bt_sysload.txt \
  > baseline_bt_stall_classified.txt
```

**Post-change probe** (run after this branch deploys, same 72 h soak):

```bash
# same commands, produce after_*.txt
```

**Success criterion** — all must hold:

- `events_quiet_host` (stalls without concurrent host load) drops to `≤1` over a 72 h soak (vs current observed multiple per week)
- For any event that still occurs: a `CaptureStreamStalled` log line fires within `≤15 s` of the stall (verified by log-timestamp correlation)
- `events_load_correlated` is *expected to be unchanged or reduced only as side effect* — this plan does not claim to fix FM-BT-11; that's Phase 2 / Plan D
- No false positives during legitimate idle periods (verified by `0` stall events in a 1 h soak with the BT source paused on the phone side)

**Debug-agent verification**:

```bash
python3 scripts/research/bt_stall_compare.py baseline_bt_stall_classified.txt after_bt_stall_classified.txt
```

Expected output: `PASS` plus per-metric deltas.

**If FAIL: do not merge.** Diagnose: is the watchdog firing too aggressively (false positives), or not firing on the known-bug case (false negatives)?

---

## Task 10: Open PR + merge

```bash
git push -u origin feat/bt-capture-watchdog

gh pr create --title "feat(bt): capture watchdog for FM-BT-3 long-uptime quiescence" --body "$(cat <<'EOF'
## Summary

Implements [Plan A from the Cast/BT research arc](../docs/plans/2026-05-22-cast-bt-phase-1-2-arc.md) — a periodic watchdog that detects FM-BT-3 (silent PipeWire OnProcess callback cessation on a long-running BT capture stream), addressing the production bug documented in MEMORY ("Long-running capture device lifecycle bug").

Reuses the existing `_recoveryInProgress` interlock in BluetoothAudioSource so watchdog-driven recovery and downstream-stall recovery share the same dedup path.

## Acceptance criteria (verified)

- See attached `bt_stall_compare.py` PASS artifact in PR conversation
- 72 h soak baseline vs post-change with two-scenario PROBE-SYS-LOAD

## Test plan

- [x] Unit tests for watchdog logic (5 cases incl. threshold, consecutive, disabled, intermittent)
- [x] Build clean across Linux + Windows TFMs
- [x] Deploy to `radio` (Ubuntu N100) + 72 h soak
- [x] Verify `events_quiet_host ≤ 1` over 72 h
- [x] Verify `events_load_correlated` not regressed
- [x] Verify no false positives in 1 h paused-phone soak

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Merge via `gh pr merge --squash --delete-branch` once Mark approves.

---

## Out of scope

- **Load-correlated stalls (FM-BT-11)**: this plan does not claim to fix them; that's Plan D (CPU affinity).
- **Recovery quality**: this plan triggers the existing recovery path; whether recovery itself works as well as it could is a separate question.
- **Cast-side watchdogs**: Cast's HM mode has `LoadMediaWithRecoveryAsync`; DC mode would need its own watchdog (Cast research §7 Idea #8, deferred to Phase 3+).
- **Windows BT path**: stub only; the watchdog is Linux-only.
