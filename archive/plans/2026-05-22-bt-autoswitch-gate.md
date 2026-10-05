# BT AutoSwitch Gate Implementation Plan (Phase 1 / Plan B)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Stop `BluetoothAutoSwitchService` from forcing a source-switch when the BlueZ `Connected` event fires before the PipeWire capture node is ready (FM-BT-1). Instead, probe for node presence first; if absent, register a one-shot subscriber so the switch fires when the node *does* appear — bounded by a max-wait timeout.

**Architecture:**

- New method `IBluetoothService.IsCaptureNodeAvailableAsync(string deviceAddress, CancellationToken ct)` — returns true if a `bluez_input.<MAC>.a2dp-source` PW node currently exists.
- New event `IBluetoothService.CaptureNodeAvailable` — fires when `LinuxBluetoothService`'s periodic re-scan detects a previously-absent node has appeared.
- `BluetoothAutoSwitchService.OnBluetoothDeviceConnected` is rewritten as follows: short-bounded probe (≤ 5 s with 500 ms polls) for node presence. If present → switch as today. If still absent → log + subscribe to `CaptureNodeAvailable` with a `BluetoothOptions.AutoSwitchMaxWaitMs` timeout (default 60 s). Switch fires when the event hits, or the subscription is torn down on timeout.

This plan leaves `LinuxBluetoothService.GetAudioCaptureDeviceAsync`'s existing `pw-cli`-scrape implementation in place; the periodic re-scan that raises `CaptureNodeAvailable` re-uses the same `ParsePwCliOutputForBtNode` parser. Plan E (PW event subscription) replaces the scrape with a real event API, at which point this plan's periodic re-scan is replaced with a direct event subscription — but this plan ships independently of Plan E.

**Tech Stack:** existing `LinuxBluetoothService` + `ParsePwCliOutputForBtNode`, existing `BluetoothAutoSwitchService` orchestration, new periodic re-scan loop, Radio.Metrics counter for visibility.

**Addresses**: FM-BT-1 from [`docs/research/2026-05-22-bt-audio-stabilization.md`](../research/2026-05-22-bt-audio-stabilization.md) §4 (its contribution to FM-BT-3 long-uptime degradation is addressed indirectly).

---

## Task 0: Author probe scripts (research deliverable)

**Files:**
- Create: `scripts/research/bt_autoswitch_audit.py`
- Create: `scripts/research/bt_autoswitch_compare.py`
- Create: `scripts/research/bt_pair_unpair_harness.sh`

(`sysload_capture.sh` + `sysload_correlate.py` were authored in Plan A's Task 0; reuse.)

**Step 1: `bt_autoswitch_audit.py`** — reads stdin (journalctl text); counts: (a) `GetOrCreateSourceAsync(Bluetooth` invocations, (b) `GetAudioCaptureDeviceAsync` retries, (c) `waiting for PW node` log lines (existing log message at [LinuxBluetoothService.cs:L1131-L1141](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)), (d) wall-clock duration spent in retry loops (computed from first-to-last waiting-line timestamps per session). Outputs:
```
switches=<N>
getcapture_invocations=<M>
waiting_log_lines=<L>
retry_loop_hours=<T>
```

**Step 2: `bt_pair_unpair_harness.sh`** — scripted pair/unpair cycle harness. Drives the test phone via either:
- `adb shell svc bluetooth disable && svc bluetooth enable` (Android), or
- AppleScript via SSH to a macOS host with `blueutil` (iPhone via USB)

Takes args: `--cycles N --period-sec X --simulate-no-audio` (if `--simulate-no-audio` is set, the phone is paired but doesn't start playing audio — the FM-BT-1 trigger condition).

**Step 3: `bt_autoswitch_compare.py`** — reads two audit artifacts; produces PASS/FAIL against the success criteria; outputs per-metric deltas.

**Step 4: Commit**

```bash
git add scripts/research/bt_autoswitch_audit.py \
        scripts/research/bt_autoswitch_compare.py \
        scripts/research/bt_pair_unpair_harness.sh
git commit -m "scripts(research): add probe scripts for BT autoswitch gate measurement"
```

---

## Task 1: BluetoothOptions — add gate config

**Files:**
- Modify: `src/Radio.Core/Configuration/BluetoothOptions.cs`
- Modify: `src/Radio.API/appsettings.json`

**Step 1:** Add three properties:

```csharp
/// <summary>
/// How long the auto-switch probes for the PipeWire capture node before falling back
/// to event-driven wait (milliseconds). The probe interval is fixed at 500 ms.
/// </summary>
public int AutoSwitchProbeWindowMs { get; set; } = 5000;

/// <summary>
/// Maximum time the auto-switch waits for a CaptureNodeAvailable event before giving up
/// (milliseconds). Default 60 s — typical phone-attaches-A2DP delay is well under that.
/// </summary>
public int AutoSwitchMaxWaitMs { get; set; } = 60000;

/// <summary>
/// Interval at which LinuxBluetoothService re-scans for newly-appeared PW BT nodes
/// (milliseconds). The re-scan raises CaptureNodeAvailable for any node not present in
/// the previous scan.
/// </summary>
public int CaptureNodeRescanIntervalMs { get; set; } = 1000;
```

**Step 2: Update appsettings.json defaults** in the `Bluetooth` section.

**Step 3: Build + commit**

```bash
git add src/Radio.Core/Configuration/BluetoothOptions.cs src/Radio.API/appsettings.json
git commit -m "feat(bt): add auto-switch gate config options"
```

---

## Task 2: `CaptureNodeAvailable` event + probe method on `IBluetoothService`

**Files:**
- Modify: `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs`
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs`

**Step 1: Add to interface**

```csharp
/// <summary>
/// Raised when a previously-absent PipeWire BT capture node has appeared for the
/// connected device. Fires once per appearance; subscribers wishing to act repeatedly
/// must re-subscribe.
/// </summary>
event EventHandler<CaptureNodeAvailableEventArgs>? CaptureNodeAvailable;

/// <summary>
/// Probes whether a PipeWire BT capture node currently exists for the given device.
/// On non-Linux platforms returns true (audio routing is platform-managed).
/// </summary>
Task<bool> IsCaptureNodeAvailableAsync(string deviceAddress, CancellationToken ct = default);
```

Plus event args class:

```csharp
public class CaptureNodeAvailableEventArgs : EventArgs
{
  public required string DeviceAddress { get; init; }
  public required int PipeWireSerial { get; init; }
}
```

**Step 2: Implement `IsCaptureNodeAvailableAsync` in `LinuxBluetoothService`**

Wrap the existing `pw-cli ls Node` + `ParsePwCliOutputForBtNode` pipeline; return true if a matching node is found. *Does not acquire* — pure probe.

```csharp
public async Task<bool> IsCaptureNodeAvailableAsync(string deviceAddress, CancellationToken ct = default)
{
  var prefix = $"bluez_input.{deviceAddress.Replace(':', '_').ToLowerInvariant()}";
  try
  {
    var output = await RunPwCliListNodesAsync(ct);  // extract this from existing GetAudioCaptureDeviceAsync
    var (nodeName, _, _) = ParsePwCliOutputForBtNode(output, prefix);
    return nodeName != null;
  }
  catch (OperationCanceledException) { throw; }
  catch (Exception ex)
  {
    _logger.LogDebug(ex, "IsCaptureNodeAvailableAsync probe failed (treating as not-available)");
    return false;
  }
}
```

Extract `RunPwCliListNodesAsync(ct)` as a private helper from the existing scrape code at [L1247](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs) — share between probe and full acquisition.

**Step 3: Implement `CaptureNodeAvailable` raising** — see Task 3.

**Step 4: Stub on Windows + Mock** — `IsCaptureNodeAvailableAsync` returns `Task.FromResult(true)` (the platform manages routing); `CaptureNodeAvailable` event declared but never fires.

**Step 5: Build + commit**

```bash
git add src/Radio.Core/Interfaces/Audio/IBluetoothService.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs
git commit -m "feat(bt): add CaptureNodeAvailable event + IsCaptureNodeAvailableAsync probe"
```

---

## Task 3: Periodic node re-scan loop in `LinuxBluetoothService`

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`

**Step 1: Add private state**

```csharp
private CancellationTokenSource? _rescanCts;
private Task? _rescanTask;
private readonly HashSet<string> _knownNodeAddresses = new(StringComparer.OrdinalIgnoreCase);
private readonly object _knownNodesLock = new();
```

**Step 2: Start the re-scan loop on first `DeviceConnected`**

In the existing device-connected handler, start `_rescanTask` if not already running:

```csharp
private void EnsureRescanLoopRunning()
{
  if (_rescanTask != null && !_rescanTask.IsCompleted) return;
  _rescanCts = new CancellationTokenSource();
  var token = _rescanCts.Token;
  _rescanTask = Task.Run(() => RescanLoopAsync(token), token);
}

private async Task RescanLoopAsync(CancellationToken ct)
{
  var interval = _options.CurrentValue.CaptureNodeRescanIntervalMs;
  while (!ct.IsCancellationRequested)
  {
    try
    {
      var connectedAddress = ConnectedDevice?.Address;
      if (connectedAddress != null)
      {
        var available = await IsCaptureNodeAvailableAsync(connectedAddress, ct);
        bool wasKnown;
        lock (_knownNodesLock)
        {
          wasKnown = _knownNodeAddresses.Contains(connectedAddress);
          if (available) _knownNodeAddresses.Add(connectedAddress);
          else _knownNodeAddresses.Remove(connectedAddress);
        }
        if (available && !wasKnown)
        {
          _logger.LogInformation("PW capture node appeared for {Address}", connectedAddress);
          _metricsCollector?.Increment("bluetooth.capture_node_appeared_total");
          CaptureNodeAvailable?.Invoke(this, new CaptureNodeAvailableEventArgs
          {
            DeviceAddress = connectedAddress,
            PipeWireSerial = 0  // serial is filled by full acquisition path; not needed here
          });
        }
      }
    }
    catch (OperationCanceledException) { break; }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Rescan loop iteration failed");
    }

    try { await Task.Delay(interval, ct); }
    catch (OperationCanceledException) { break; }
  }
}
```

**Step 3: Stop the re-scan loop on disconnect / dispose**

In disconnect handler + `Dispose`:

```csharp
_rescanCts?.Cancel();
_rescanCts?.Dispose();
_rescanCts = null;
_rescanTask = null;
lock (_knownNodesLock) _knownNodeAddresses.Clear();
```

**Step 4: Build + commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs
git commit -m "feat(bt): periodic PW node re-scan loop raising CaptureNodeAvailable"
```

---

## Task 4: Gate `BluetoothAutoSwitchService.OnBluetoothDeviceConnected`

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Services/BluetoothAutoSwitchService.cs`

**Step 1:** Rewrite the handler. Replace the existing `OnBluetoothDeviceConnected` at [L56-L83](../../src/Radio.Infrastructure/Audio/Services/BluetoothAutoSwitchService.cs) with:

```csharp
private async void OnBluetoothDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
{
  try
  {
    var opts = _bluetoothOptions.CurrentValue;
    if (!opts.AutoSwitchOnConnect) return;
    if (!_bluetoothService.IsAvailable)
    {
      _logger.LogWarning("Bluetooth auto-switch skipped; adapter not available");
      return;
    }

    var audioManager = _getAudioManager();
    if (audioManager.ActiveSource?.Type == AudioSourceType.Bluetooth)
    {
      _logger.LogDebug("BT device connected but BT is already active source; skipping switch");
      return;
    }

    // Short-bounded probe for PW capture node presence — typical happy path
    using var probeCts = new CancellationTokenSource(opts.AutoSwitchProbeWindowMs);
    var nodeReady = await ProbeForNodeAsync(e.Device.Address, probeCts.Token);
    if (nodeReady)
    {
      _logger.LogInformation("BT auto-switch: PW node ready, switching immediately for {Address}", e.Device.Address);
      await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true);
      return;
    }

    // Node not ready inside probe window → defer to event subscription
    _logger.LogInformation(
      "BT auto-switch: PW node not ready for {Address} after {Probe}ms; subscribing to CaptureNodeAvailable (max wait {Max}ms)",
      e.Device.Address, opts.AutoSwitchProbeWindowMs, opts.AutoSwitchMaxWaitMs);
    _bluetoothService.MetricsCollector?.Increment("bluetooth.autoswitch_deferred_total");
    await WaitForNodeOrTimeoutAsync(e.Device.Address, opts.AutoSwitchMaxWaitMs);
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Failed to auto-switch to Bluetooth after device connect {Device}", e.Device.Address);
  }
}

private async Task<bool> ProbeForNodeAsync(string address, CancellationToken ct)
{
  while (!ct.IsCancellationRequested)
  {
    if (await _bluetoothService.IsCaptureNodeAvailableAsync(address, ct)) return true;
    try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return false; }
  }
  return false;
}

private async Task WaitForNodeOrTimeoutAsync(string address, int timeoutMs)
{
  var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  EventHandler<CaptureNodeAvailableEventArgs>? handler = null;
  handler = (_, e) =>
  {
    if (string.Equals(e.DeviceAddress, address, StringComparison.OrdinalIgnoreCase))
      tcs.TrySetResult(true);
  };
  _bluetoothService.CaptureNodeAvailable += handler;
  try
  {
    var timeoutTask = Task.Delay(timeoutMs);
    var winner = await Task.WhenAny(tcs.Task, timeoutTask);
    if (winner == tcs.Task)
    {
      _logger.LogInformation("BT auto-switch: PW node arrived for {Address}, switching", address);
      var audioManager = _getAudioManager();
      if (audioManager.ActiveSource?.Type != AudioSourceType.Bluetooth)
        await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true);
    }
    else
    {
      _logger.LogWarning("BT auto-switch: PW node did not appear within {Max}ms for {Address}; abandoning switch", timeoutMs, address);
      _bluetoothService.MetricsCollector?.Increment("bluetooth.autoswitch_abandoned_total");
    }
  }
  finally
  {
    _bluetoothService.CaptureNodeAvailable -= handler;
  }
}
```

Note: `_bluetoothService.MetricsCollector` may not exist as a public property — if not, accept `IMetricsCollector?` in the `BluetoothAutoSwitchService` constructor and use that.

**Step 2: Build + commit**

```bash
git add src/Radio.Infrastructure/Audio/Services/BluetoothAutoSwitchService.cs
git commit -m "feat(bt): gate autoSwitchOnConnect on PW capture-node availability"
```

---

## Task 5: Unit tests

**Files:**
- Create: `tests/Radio.Infrastructure.Tests/Audio/Services/BluetoothAutoSwitchServiceTests.cs`

**Step 1: Mockable interfaces** — the test needs to drive `IBluetoothService.IsCaptureNodeAvailableAsync` + `CaptureNodeAvailable` event. Use Moq or a hand-rolled `FakeBluetoothService`.

**Step 2: Write tests for the three execution paths**

```csharp
[Fact]
public async Task NodeReadyInsideProbeWindow_SwitchesImmediately()
{
  var fakeBt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
  var fakeAudio = new FakeAudioManager();
  var svc = CreateService(fakeBt, fakeAudio, probeMs: 1000, maxWaitMs: 30000);
  await svc.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
  Assert.True(fakeAudio.SwitchedToBluetooth);
  Assert.Equal(0, fakeBt.CaptureNodeAvailableSubscriberCount);
}

[Fact]
public async Task NodeArrivesAfterProbe_SwitchesViaEvent()
{
  var fakeBt = new FakeBluetoothService { IsCaptureNodeAvailable = false };
  var fakeAudio = new FakeAudioManager();
  var svc = CreateService(fakeBt, fakeAudio, probeMs: 200, maxWaitMs: 5000);
  var connectTask = svc.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
  await Task.Delay(400);  // probe window expires; subscription active
  Assert.False(fakeAudio.SwitchedToBluetooth);
  fakeBt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
  await connectTask;
  Assert.True(fakeAudio.SwitchedToBluetooth);
}

[Fact]
public async Task NodeNeverArrives_TimesOutWithoutSwitch()
{
  var fakeBt = new FakeBluetoothService { IsCaptureNodeAvailable = false };
  var fakeAudio = new FakeAudioManager();
  var svc = CreateService(fakeBt, fakeAudio, probeMs: 100, maxWaitMs: 300);
  await svc.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
  Assert.False(fakeAudio.SwitchedToBluetooth);
  Assert.Equal(1, fakeBt.AbandonedSwitchCount);
}

[Fact]
public async Task AlreadyActiveBluetoothSource_SkipsSwitch()
{
  var fakeBt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
  var fakeAudio = new FakeAudioManager { ActiveSourceType = AudioSourceType.Bluetooth };
  var svc = CreateService(fakeBt, fakeAudio, probeMs: 1000, maxWaitMs: 30000);
  await svc.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
  Assert.False(fakeAudio.GetOrCreateCalled);
}

[Fact]
public async Task AutoSwitchDisabled_SkipsEntirely()
{
  var fakeBt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
  var fakeAudio = new FakeAudioManager();
  var svc = CreateService(fakeBt, fakeAudio, probeMs: 1000, maxWaitMs: 30000, autoSwitchEnabled: false);
  await svc.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
  Assert.False(fakeAudio.GetOrCreateCalled);
}
```

**Step 3: Run tests**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothAutoSwitchServiceTests" --configuration Release -v n
```
Expected: 5 PASS.

**Step 4: Commit**

```bash
git add tests/Radio.Infrastructure.Tests/Audio/Services/BluetoothAutoSwitchServiceTests.cs
git commit -m "test(bt): unit tests for BluetoothAutoSwitchService gating"
```

---

## Task 6: Full build + test

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```
Expected: 0 warnings; all tests pass.

---

## Task 7: Deploy to Ubuntu

```bash
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
```

Verify re-scan loop running:
```bash
ssh mmack@radio "journalctl -u radio-api --since '1 minute ago' | grep -E 'PW capture node|autoswitch'"
```

---

## Task 8: Verify acceptance criteria

**Baseline probe** (against `main`, 24 h scripted pair/unpair):

```bash
ssh mmack@radio "/opt/radio-console/scripts/research/bt_pair_unpair_harness.sh --cycles 48 --period-sec 1800 --simulate-no-audio" &

# Concurrent sysload capture:
ssh mmack@radio "/opt/radio-console/scripts/research/sysload_capture.sh 86400" > baseline_autoswitch_sysload.txt

# After 24 h:
ssh mmack@radio "journalctl -u radio-api --since '24 hours ago' -o cat" \
  | python3 scripts/research/bt_autoswitch_audit.py \
  > baseline_autoswitch.txt
```

**Post-change probe** (same 24 h with new branch deployed): produce `after_autoswitch.txt`.

**Success criterion**:

- `waiting_log_lines` per pair-without-audio cycle drops to `≤5` (vs current observed unbounded)
- `retry_loop_hours` drops to `0` (no auto-switch occurring without a node present)
- On healthy pair-with-node-ready cycles (control): `getcapture_invocations ≤ 2` (no regression for the happy path)
- New counter `bluetooth.autoswitch_deferred_total` matches the expected ~50 % of cycles (those that simulated no audio)
- New counter `bluetooth.autoswitch_abandoned_total` is `0` for cycles where the phone *eventually* plays audio (within the 60 s window)

**Debug-agent verification**:

```bash
python3 scripts/research/bt_autoswitch_compare.py baseline_autoswitch.txt after_autoswitch.txt
```

Expected: `PASS`.

---

## Task 9: Open PR + merge

```bash
git push -u origin feat/bt-autoswitch-gate

gh pr create --title "feat(bt): gate autoSwitchOnConnect on PW capture-node availability" --body "$(cat <<'EOF'
## Summary

Implements [Plan B from the Cast/BT research arc](../docs/plans/2026-05-22-cast-bt-phase-1-2-arc.md) — stops `BluetoothAutoSwitchService` from forcing source-switch + retry-loop when the BlueZ `Connected` event fires before the PipeWire capture node has materialized (FM-BT-1).

Two-phase wait: short-bounded probe (5 s) → event-driven subscription (60 s timeout). Contributes to reducing FM-BT-3 long-uptime degradation by eliminating the hours-of-retry-loop pathology documented in MEMORY.

## Acceptance criteria (verified)

- `waiting_log_lines` per cycle ≤ 5 (was unbounded)
- `retry_loop_hours` = 0
- Happy-path `getcapture_invocations ≤ 2`
- See attached `bt_autoswitch_compare.py` PASS artifact

## Test plan

- [x] Unit tests for 5 execution paths
- [x] 24 h scripted pair/unpair harness on `radio` (with `--simulate-no-audio`)
- [x] Concurrent PROBE-SYS-LOAD captured
- [x] Verified counters: `autoswitch_deferred_total`, `autoswitch_abandoned_total`, `capture_node_appeared_total`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Merge once Mark approves.

---

## Out of scope

- **Replacing `pw-cli` scrape with PW events**: that's Plan E. This plan ships the periodic re-scan version; Plan E later replaces it cleanly.
- **Recovery from a stuck `autoswitch_abandoned_total`**: once we abandon, the user must trigger source switch manually. Re-attempting after Nth failure is Phase 3+.
- **Cast-side equivalent**: no equivalent failure mode on the cast side (Cast is push-driven; no analogous race).
- **Windows BT path**: stub only.
