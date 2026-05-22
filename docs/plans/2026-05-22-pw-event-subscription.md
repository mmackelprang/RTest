# PipeWire Event Subscription Implementation Plan (Phase 2 / Plan E)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Replace `LinuxBluetoothService`'s `pw-cli ls Node` text-scrape (currently used by `ParsePwCliOutputForBtNode` + the periodic re-scan loop from Plan B) with a real PipeWire registry-event subscription via `pw_registry_add_listener` P/Invoke. Detection of node appearance/disappearance becomes event-driven (target ≤200 ms latency) instead of polled (currently bounded by `CaptureNodeRescanIntervalMs = 1000 ms` from Plan B).

**Architecture:**

- New P/Invoke bindings for `pw_context_*`, `pw_core_*`, `pw_registry_*`, and the `pw_registry_events` callback struct in `PipeWireNative.cs`.
- New `PipeWireRegistryListener` class encapsulating: context + core + registry creation, callback delegate pinning, `global_added` / `global_removed` event handling, and filter logic for `bluez_input.<MAC>.a2dp-source` node names.
- `LinuxBluetoothService` uses `PipeWireRegistryListener` as the primary node-presence source. The existing `RescanLoopAsync` from Plan B is retained as a *fallback*: starts only if `PipeWireRegistryListener.IsHealthy == false`. The existing `ParsePwCliOutputForBtNode` static method is retained for unit-test value (PR #314's 13 tests pass against it) and as the parser used by the fallback.

This plan benefits from Plan B being shipped first because:
1. The `CaptureNodeAvailable` event contract already exists; this plan only changes what triggers it (registry callback vs scrape).
2. The fallback path is already wired (the periodic scrape from Plan B).

The two are not strictly ordered, however — if Plan B is delayed for any reason, this plan can ship first by introducing the event and listener simultaneously.

**Tech Stack:** existing PipeWire native interop (PR #262), new P/Invoke for `pw_registry_*` and `pw_proxy_*`, existing `BluetoothOptions` and metrics infrastructure.

**Addresses**: FM-BT-1 (eliminates the pw-cli scrape race window from Plan B's gating logic), FM-BT-2 (faster detection of mid-session node disappearance), §6 Pattern 1 from [`docs/research/2026-05-22-bt-audio-stabilization.md`](../research/2026-05-22-bt-audio-stabilization.md) (RTest's unique use of pw-cli text-scrape vs reference systems' event APIs).

---

## Task 0: Author probe scripts (research deliverable)

**Files:**
- Create: `scripts/research/bt_pair_cycle_harness.sh` (if not already created in Plan B — confirm)
- Create: `scripts/research/bt_lifecycle_summarize.py`
- Create: `scripts/research/bt_lifecycle_compare.py`

**Step 1:** Confirm `bt_pair_cycle_harness.sh` exists from Plan B's Task 0. If not, create it per Plan B's spec.

**Step 2: `bt_lifecycle_summarize.py`** — reads two inputs:
- A pair-cycle log artifact (phone-side timestamps of enable/disable events)
- A radio-side journal artifact (`PW capture node appeared` and `PW capture node disappeared` log lines from `LinuxBluetoothService`)

For each cycle, computes `detection_latency_ms` (PW-node-appears timestamp − phone-enable timestamp) and `teardown_latency_ms` (PW-node-disappears timestamp − phone-disable timestamp). Outputs per-cycle CSV + summary:

```
cycles=<N>
detection_latency_ms_p50=<X>, p95=<Y>
teardown_latency_ms_p50=<A>, p95=<B>
failed_detections=<F>   # cycles where the node never appeared in the radio log
failed_teardowns=<T>
```

**Step 3: `bt_lifecycle_compare.py`** — reads two summary artifacts; PASS/FAIL against the §7 Idea #3 success criterion.

**Step 4: Commit**

```bash
git add scripts/research/bt_lifecycle_summarize.py scripts/research/bt_lifecycle_compare.py
# bt_pair_cycle_harness.sh from Plan B if not already there
git commit -m "scripts(research): BT lifecycle latency summarize + compare scripts"
```

---

## Task 1: P/Invoke bindings for `pw_registry_*`

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNative.cs`

**Step 1: Add the bindings**

PipeWire's registry API: `pw_context_connect(context, props, user_data_size) → pw_core *`; then `pw_core_get_registry(core, version, user_data_size) → pw_registry *`; then `pw_registry_add_listener(registry, &hook, &events_struct, user_data)`.

The events struct shape (from `pipewire/extensions/registry.h`):
```c
struct pw_registry_events {
    uint32_t version;
    void (*global)(void *data, uint32_t id, uint32_t permissions, const char *type, uint32_t version, const struct spa_dict *props);
    void (*global_remove)(void *data, uint32_t id);
};
```

Add to `PipeWireNative.cs`:

```csharp
#if !WINDOWS_TARGET
internal const uint PW_VERSION_REGISTRY_EVENTS = 0;
internal const uint PW_VERSION_REGISTRY = 3;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void PwRegistryGlobalDelegate(
  IntPtr userData, uint id, uint permissions,
  [MarshalAs(UnmanagedType.LPStr)] string type, uint version,
  IntPtr props);  // spa_dict* — read via pw_helper if needed

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void PwRegistryGlobalRemoveDelegate(IntPtr userData, uint id);

[StructLayout(LayoutKind.Sequential)]
internal struct PwRegistryEvents
{
  public uint Version;
  public IntPtr Global;        // PwRegistryGlobalDelegate function pointer
  public IntPtr GlobalRemove;  // PwRegistryGlobalRemoveDelegate function pointer
}

[DllImport("libpipewire-0.3.so.0", EntryPoint = "pw_context_new")]
internal static extern IntPtr pw_context_new(IntPtr loop, IntPtr props, IntPtr userDataSize);

[DllImport("libpipewire-0.3.so.0", EntryPoint = "pw_context_connect")]
internal static extern IntPtr pw_context_connect(IntPtr context, IntPtr props, IntPtr userDataSize);

[DllImport("libpipewire-0.3.so.0", EntryPoint = "pw_context_destroy")]
internal static extern void pw_context_destroy(IntPtr context);

[DllImport("libpipewire-0.3.so.0", EntryPoint = "pw_core_get_registry")]
internal static extern IntPtr pw_core_get_registry(IntPtr core, uint version, IntPtr userDataSize);

[DllImport("libpipewire-0.3.so.0", EntryPoint = "pw_core_disconnect")]
internal static extern int pw_core_disconnect(IntPtr core);

[DllImport("libpipewire-0.3.so.0", EntryPoint = "pw_proxy_add_listener")]
internal static extern void pw_proxy_add_listener(IntPtr proxy, IntPtr hook, IntPtr events, IntPtr data);

[DllImport("libpipewire-0.3.so.0", EntryPoint = "pw_proxy_destroy")]
internal static extern void pw_proxy_destroy(IntPtr proxy);

// pw_proxy_add_listener takes a hook (spa_hook) which is a small struct the caller owns.
// Allocate as a pinned 16-byte buffer.
internal const int SpaHookSize = 16;
#endif
```

**Note**: `pw_helper.c` (the existing native helper compiled to `libpw_helper.so` per MEMORY) may need an additional helper to read a property out of `spa_dict` — the `props` parameter to the `global` callback is a `spa_dict*`. Add a helper `pw_helper_spa_dict_lookup(dict_ptr, key) → char*` that returns the value string or null. Match the existing helper's layout in `/tmp/pw_helper.c` on Ubuntu.

**Step 2: Build (Linux-only path)**

```bash
dotnet build src/Radio.Infrastructure --configuration Release --framework net10.0
```
Expected: 0 warnings on the Linux TFM.

**Step 3: Commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNative.cs
git commit -m "feat(bt): P/Invoke bindings for pw_registry_* + spa_dict helper signature"
```

---

## Task 2: `PipeWireRegistryListener` class

**Files:**
- Create: `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireRegistryListener.cs`

**Step 1: Implement**

```csharp
#if !WINDOWS_TARGET
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using static Radio.Infrastructure.Platform.Bluetooth.Native.PipeWireNative;

namespace Radio.Infrastructure.Platform.Bluetooth.Native;

/// <summary>
/// Subscribes to the PipeWire registry for global add/remove events.
/// Filters BT capture nodes (name matches "bluez_input.&lt;MAC&gt;.a2dp-source") and
/// forwards as managed events. Replaces pw-cli text scraping.
///
/// Runs its own pw_thread_loop separate from the capture stream's loop —
/// keeping subscription survival independent of stream lifecycle.
/// </summary>
internal sealed class PipeWireRegistryListener : IDisposable
{
  private readonly ILogger _logger;
  private IntPtr _threadLoop;
  private IntPtr _context;
  private IntPtr _core;
  private IntPtr _registry;
  private IntPtr _hook;          // pinned spa_hook buffer
  private GCHandle _eventsHandle;
  private GCHandle _selfHandle;
  private PwRegistryEvents _events;
  private readonly PwRegistryGlobalDelegate _globalDelegate;
  private readonly PwRegistryGlobalRemoveDelegate _globalRemoveDelegate;
  private bool _disposed;

  /// <summary>Indicates whether the listener initialized successfully.</summary>
  public bool IsHealthy { get; private set; }

  public event EventHandler<BtNodeRegistryEventArgs>? NodeAppeared;
  public event EventHandler<BtNodeRegistryEventArgs>? NodeDisappeared;

  // Track id → (address, type) so we can fire NodeDisappeared with the right address
  private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, string> _idToAddress = new();

  public PipeWireRegistryListener(ILogger logger)
  {
    _logger = logger;
    _globalDelegate = OnGlobal;
    _globalRemoveDelegate = OnGlobalRemove;
  }

  public void Start()
  {
    try
    {
      _threadLoop = pw_thread_loop_new("radio-bt-registry", IntPtr.Zero);
      if (_threadLoop == IntPtr.Zero)
        throw new InvalidOperationException("pw_thread_loop_new failed");
      var loop = pw_thread_loop_get_loop(_threadLoop);

      _context = pw_context_new(loop, IntPtr.Zero, IntPtr.Zero);
      if (_context == IntPtr.Zero)
        throw new InvalidOperationException("pw_context_new failed");

      _core = pw_context_connect(_context, IntPtr.Zero, IntPtr.Zero);
      if (_core == IntPtr.Zero)
        throw new InvalidOperationException("pw_context_connect failed");

      _registry = pw_core_get_registry(_core, PW_VERSION_REGISTRY, IntPtr.Zero);
      if (_registry == IntPtr.Zero)
        throw new InvalidOperationException("pw_core_get_registry failed");

      _selfHandle = GCHandle.Alloc(this);
      _events = new PwRegistryEvents
      {
        Version = PW_VERSION_REGISTRY_EVENTS,
        Global = Marshal.GetFunctionPointerForDelegate(_globalDelegate),
        GlobalRemove = Marshal.GetFunctionPointerForDelegate(_globalRemoveDelegate)
      };
      _eventsHandle = GCHandle.Alloc(_events, GCHandleType.Pinned);
      _hook = Marshal.AllocHGlobal(SpaHookSize);

      pw_proxy_add_listener(_registry, _hook,
        _eventsHandle.AddrOfPinnedObject(),
        GCHandle.ToIntPtr(_selfHandle));

      pw_thread_loop_start(_threadLoop);

      IsHealthy = true;
      _logger.LogInformation("PipeWireRegistryListener started");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "PipeWireRegistryListener failed to start; fallback path will be used");
      IsHealthy = false;
      Cleanup();
    }
  }

  private static void OnGlobal(IntPtr userData, uint id, uint permissions, string type, uint version, IntPtr props)
  {
    PipeWireRegistryListener? self;
    try { self = GCHandle.FromIntPtr(userData).Target as PipeWireRegistryListener; }
    catch { return; }
    if (self == null) return;

    // Only PipeWire:Interface:Node matters for BT capture nodes
    if (type != "PipeWire:Interface:Node") return;

    // Read node.name via spa_dict helper
    var nameOrNull = ReadSpaDictKey(props, "node.name");
    if (nameOrNull == null) return;

    // Filter for bluez_input.<MAC>.a2dp-source
    if (!nameOrNull.StartsWith("bluez_input.")) return;
    if (!nameOrNull.EndsWith(".a2dp-source")) return;

    // Extract MAC: bluez_input.AA_BB_CC_DD_EE_FF.a2dp-source → AA:BB:CC:DD:EE:FF
    var dotMac = nameOrNull.Substring("bluez_input.".Length);
    var underscoreMac = dotMac.Substring(0, dotMac.Length - ".a2dp-source".Length);
    var address = underscoreMac.Replace('_', ':').ToUpperInvariant();

    self._idToAddress[id] = address;
    self._logger.LogInformation("PW registry: BT node appeared id={Id} address={Address}", id, address);
    self.NodeAppeared?.Invoke(self, new BtNodeRegistryEventArgs { Id = id, DeviceAddress = address });
  }

  private static void OnGlobalRemove(IntPtr userData, uint id)
  {
    PipeWireRegistryListener? self;
    try { self = GCHandle.FromIntPtr(userData).Target as PipeWireRegistryListener; }
    catch { return; }
    if (self == null) return;

    if (self._idToAddress.TryRemove(id, out var address))
    {
      self._logger.LogInformation("PW registry: BT node disappeared id={Id} address={Address}", id, address);
      self.NodeDisappeared?.Invoke(self, new BtNodeRegistryEventArgs { Id = id, DeviceAddress = address });
    }
  }

  private static string? ReadSpaDictKey(IntPtr propsPtr, string key)
  {
    // Calls into libpw_helper.so's pw_helper_spa_dict_lookup; returns null if key missing.
    var resultPtr = pw_helper_spa_dict_lookup(propsPtr, key);
    return resultPtr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(resultPtr);
  }

  [DllImport("libpw_helper.so")]
  private static extern IntPtr pw_helper_spa_dict_lookup(IntPtr dict, [MarshalAs(UnmanagedType.LPStr)] string key);

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    Cleanup();
  }

  private void Cleanup()
  {
    if (_threadLoop != IntPtr.Zero)
    {
      pw_thread_loop_lock(_threadLoop);
      try
      {
        if (_registry != IntPtr.Zero) { pw_proxy_destroy(_registry); _registry = IntPtr.Zero; }
        if (_core != IntPtr.Zero) { pw_core_disconnect(_core); _core = IntPtr.Zero; }
        if (_context != IntPtr.Zero) { pw_context_destroy(_context); _context = IntPtr.Zero; }
      }
      finally { pw_thread_loop_unlock(_threadLoop); }

      pw_thread_loop_stop(_threadLoop);
      pw_thread_loop_destroy(_threadLoop);
      _threadLoop = IntPtr.Zero;
    }
    if (_hook != IntPtr.Zero) { Marshal.FreeHGlobal(_hook); _hook = IntPtr.Zero; }
    if (_eventsHandle.IsAllocated) _eventsHandle.Free();
    if (_selfHandle.IsAllocated) _selfHandle.Free();
    IsHealthy = false;
  }
}

internal class BtNodeRegistryEventArgs : EventArgs
{
  public required uint Id { get; init; }
  public required string DeviceAddress { get; init; }
}
#endif
```

**Step 2:** Update `pw_helper.c` (Ubuntu source at `/tmp/pw_helper.c`) to add `pw_helper_spa_dict_lookup`:

```c
const char *pw_helper_spa_dict_lookup(const struct spa_dict *dict, const char *key) {
    if (!dict) return NULL;
    return spa_dict_lookup(dict, key);
}
```

Rebuild `libpw_helper.so` per the existing deploy procedure (per MEMORY: "libpw_helper.so compiled on Ubuntu, installed to `/usr/local/lib` via ldconfig").

**Step 3: Build + commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireRegistryListener.cs \
        # plus any pw_helper source update if checked in
git commit -m "feat(bt): PipeWireRegistryListener for event-driven node lifecycle"
```

---

## Task 3: Integrate into `LinuxBluetoothService` — primary path with fallback

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`

**Step 1: Add the listener field + init in constructor or `StartAsync`**

```csharp
private PipeWireRegistryListener? _registryListener;

// In StartAsync after BlueZ is initialized:
_registryListener = new PipeWireRegistryListener(_logger);
_registryListener.NodeAppeared += OnRegistryNodeAppeared;
_registryListener.NodeDisappeared += OnRegistryNodeDisappeared;
_registryListener.Start();

if (!_registryListener.IsHealthy)
{
  _logger.LogWarning("PipeWireRegistryListener unhealthy — falling back to periodic re-scan loop");
  // The existing RescanLoopAsync from Plan B continues to run as fallback.
}
else
{
  // Don't start the periodic re-scan loop — the listener is doing the job.
  _logger.LogInformation("PipeWireRegistryListener active — periodic re-scan disabled");
}
```

**Step 2: Gate the periodic re-scan loop on listener health**

In `EnsureRescanLoopRunning` (from Plan B):

```csharp
private void EnsureRescanLoopRunning()
{
  // If the event-driven listener is healthy, skip the periodic scrape entirely.
  if (_registryListener?.IsHealthy == true) return;

  // Existing periodic scrape logic from Plan B
  // ...
}
```

**Step 3: Map the listener's events to `CaptureNodeAvailable`**

```csharp
private void OnRegistryNodeAppeared(object? sender, BtNodeRegistryEventArgs e)
{
  lock (_knownNodesLock)
  {
    _knownNodeAddresses.Add(e.DeviceAddress);
  }
  _metricsCollector?.Increment("bluetooth.capture_node_appeared_total");
  // The PW serial is the registry id (uint) — use it directly
  CaptureNodeAvailable?.Invoke(this, new CaptureNodeAvailableEventArgs
  {
    DeviceAddress = e.DeviceAddress,
    PipeWireSerial = (int)e.Id
  });
}

private void OnRegistryNodeDisappeared(object? sender, BtNodeRegistryEventArgs e)
{
  lock (_knownNodesLock)
  {
    _knownNodeAddresses.Remove(e.DeviceAddress);
  }
  _metricsCollector?.Increment("bluetooth.capture_node_disappeared_total");
  // New event for downstream consumers — separate from existing reconnect path
  // because BT-layer disconnect signals may not yet have fired.
  _logger.LogInformation("BT capture node disappeared via registry event: {Address}", e.DeviceAddress);
}
```

**Step 4: Dispose the listener**

In `Dispose` / shutdown path:

```csharp
_registryListener?.Dispose();
_registryListener = null;
```

**Step 5: Build + commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs
git commit -m "feat(bt): use PipeWireRegistryListener as primary, scrape as fallback"
```

---

## Task 4: Unit tests for the listener filter logic

**Files:**
- Create: `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/Native/PipeWireRegistryFilterTests.cs`

**Step 1:** The listener's interop is not directly testable in unit tests (requires real PipeWire daemon). However, the *filter logic* — recognizing `bluez_input.<MAC>.a2dp-source` names and extracting the MAC — is pure and testable.

Extract the filter into a static helper:

```csharp
internal static class PipeWireRegistryFilter
{
  public static bool TryExtractBtCaptureAddress(string nodeName, out string address)
  {
    address = string.Empty;
    if (!nodeName.StartsWith("bluez_input.")) return false;
    if (!nodeName.EndsWith(".a2dp-source")) return false;
    var dotMac = nodeName.Substring("bluez_input.".Length);
    var underscoreMac = dotMac.Substring(0, dotMac.Length - ".a2dp-source".Length);
    if (underscoreMac.Length != 17) return false; // AA_BB_CC_DD_EE_FF = 17 chars
    address = underscoreMac.Replace('_', ':').ToUpperInvariant();
    return true;
  }
}
```

Refactor the listener's `OnGlobal` to use this helper.

**Step 2: Tests**

```csharp
public class PipeWireRegistryFilterTests
{
  [Theory]
  [InlineData("bluez_input.78_20_51_F5_FB_A7.a2dp-source", "78:20:51:F5:FB:A7")]
  [InlineData("bluez_input.aa_bb_cc_dd_ee_ff.a2dp-source", "AA:BB:CC:DD:EE:FF")]
  public void TryExtract_ValidBtNode_ReturnsTrue(string nodeName, string expected)
  {
    var ok = PipeWireRegistryFilter.TryExtractBtCaptureAddress(nodeName, out var address);
    Assert.True(ok);
    Assert.Equal(expected, address);
  }

  [Theory]
  [InlineData("alsa_input.usb-some-mic.analog-stereo")]
  [InlineData("bluez_input.78_20_51_F5_FB_A7.hfp-ag")]          // HFP, not A2DP
  [InlineData("bluez_input.too_short.a2dp-source")]              // malformed MAC
  [InlineData("bluez_input.78_20_51_F5_FB_A7_EXTRA.a2dp-source")] // too long
  [InlineData("")]
  public void TryExtract_InvalidNode_ReturnsFalse(string nodeName)
  {
    var ok = PipeWireRegistryFilter.TryExtractBtCaptureAddress(nodeName, out _);
    Assert.False(ok);
  }
}
```

**Step 3: Run + commit**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "PipeWireRegistryFilterTests" --configuration Release -v n
# Expected: 7 PASS

git add tests/Radio.Infrastructure.Tests/Platform/Bluetooth/Native/PipeWireRegistryFilterTests.cs \
        src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireRegistryListener.cs
git commit -m "test(bt): unit tests for PipeWireRegistryFilter"
```

---

## Task 5: Full build + test

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```
Expected: 0 warnings; all tests pass. The existing 13 `ParsePwCliOutputForBtNode` tests from PR #314 continue to pass (parser retained for fallback).

---

## Task 6: Deploy + integration test

```bash
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
```

Confirm libpw_helper.so still has the new `pw_helper_spa_dict_lookup` symbol:
```bash
ssh mmack@radio "nm -D /usr/local/lib/libpw_helper.so | grep pw_helper_spa_dict_lookup"
```
If absent, rebuild per MEMORY's `libpw_helper.so` deploy procedure:
```bash
ssh mmack@radio "cd /tmp && gcc -shared -fPIC -o libpw_helper.so pw_helper.c $(pkg-config --cflags --libs libpipewire-0.3) && sudo cp libpw_helper.so /usr/local/lib/ && sudo ldconfig"
```

Restart radio-api + verify the listener started:
```bash
ssh mmack@radio "journalctl -u radio-api --since '1 minute ago' | grep -iE 'PipeWireRegistryListener|PW registry'"
```
Expected: `PipeWireRegistryListener started`. If it logged `unhealthy`, the helper or P/Invoke binding is broken — fix before proceeding.

---

## Task 7: Verify acceptance criteria — the lifecycle-latency measurement

**Baseline probe** (against `main` — has Plan B's periodic scrape only, no event listener):

```bash
ssh mmack@radio "/opt/radio-console/scripts/research/bt_pair_cycle_harness.sh \
  --cycles 60 --period-sec 60" \
  > baseline_bt_lifecycle.txt

ssh mmack@radio "/opt/radio-console/scripts/research/sysload_capture.sh 3600" \
  > baseline_bt_lifecycle_sysload.txt

python3 scripts/research/bt_lifecycle_summarize.py \
  baseline_bt_lifecycle.txt baseline_bt_lifecycle_sysload.txt \
  > baseline_bt_lifecycle_classified.txt
```

**Post-change probe** (this branch deployed):

Same 60-cycle harness; same parsers; save as `after_bt_lifecycle_classified.txt`.

**Success criterion** (must hold):

- `detection_latency_ms_p95` drops from baseline (expected `>1000 ms` due to 1 s scrape interval) to `≤200 ms`
- `teardown_latency_ms_p95` drops to `≤500 ms` (today often "never" until reconnect)
- `failed_detections` drops to `0`
- `failed_teardowns` drops to `0`
- **Negative check**: existing 13 `ParsePwCliOutputForBtNode` unit tests continue to pass (fallback retained)
- **Negative check**: if `PipeWireRegistryListener.IsHealthy = false`, the fallback periodic-scrape path correctly takes over (verify via `journalctl` simulation: deliberately uninstall `libpw_helper.so` symbol, restart, verify warning + scrape resumes)

**Debug-agent verification**:

```bash
python3 scripts/research/bt_lifecycle_compare.py baseline_bt_lifecycle_classified.txt after_bt_lifecycle_classified.txt
```

Expected: `PASS`.

---

## Task 8: Open PR + merge

```bash
git push -u origin feat/pw-event-subscription

gh pr create --title "feat(bt): PipeWire event subscription replaces pw-cli scraping for BT node lifecycle" --body "$(cat <<'EOF'
## Summary

Implements [Plan E from the Cast/BT research arc](../docs/plans/2026-05-22-cast-bt-phase-1-2-arc.md). Replaces the `pw-cli ls Node` text scraping pattern used by `LinuxBluetoothService.GetAudioCaptureDeviceAsync` + Plan B's periodic re-scan with a real PipeWire registry-event subscription (`pw_registry_add_listener`).

§6 Pattern 1 from the research doc: RTest was the only system among the four reference systems still using text-scrape; this PR aligns with the reference cluster's event-driven approach.

Existing `ParsePwCliOutputForBtNode` + its 13 unit tests retained as the fallback parser. Plan B's periodic scrape is gated to run only when `PipeWireRegistryListener.IsHealthy == false`.

## Acceptance criteria (verified)

- `detection_latency_ms_p95` drops from >1000 ms to ≤200 ms
- `teardown_latency_ms_p95` drops to ≤500 ms
- `failed_detections` = 0; `failed_teardowns` = 0
- Existing 13 parser tests still pass (fallback intact)
- Fallback path correctly takes over when listener is unhealthy
- See attached `bt_lifecycle_compare.py` PASS artifact

## Test plan

- [x] 7 unit tests for `PipeWireRegistryFilter` (extracted from listener)
- [x] 13 existing `ParsePwCliOutputForBtNode` tests still pass
- [x] 60-cycle pair/unpair harness on `radio`
- [x] Healthy-listener path: detection p95 ≤ 200 ms
- [x] Unhealthy-listener fallback: scrape path resumes
- [x] libpw_helper.so rebuild + ldconfig verified on Ubuntu

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Merge once Mark approves.

---

## Out of scope

- **Replacing the actual capture stream connect path** (`pw_stream_connect`): no change. This plan only changes how we *discover* the node; once discovered, capture acquisition uses the same `PipeWireNativeStream` as before.
- **Subscribing to non-BT nodes**: the filter rejects everything except `bluez_input.<MAC>.a2dp-source`. Future plans (e.g. monitoring other PW nodes) would extend the filter.
- **Sink-side observation** (the local speaker sink): out of scope; this plan is about BT source detection.
- **Cast side**: Cast has no equivalent failure mode (no node discovery model — Cast uses mDNS/SSDP).
- **Windows BT path**: stub only.
