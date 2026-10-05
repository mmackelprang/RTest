# `AUD-31` — a phantom "removed from BlueZ" evicts the connected phone from our cache

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2** until its consequence is measured — see *What is not known*. Filed 2026-09-25 from the same
cabinet sitting as `AUD-30`.

## What was observed

```
11:28:20.879  Bluetooth device removed from BlueZ: Pixel 10 Pro XL (B0:D5:FB:D2:0D:68) — evicted from cache
11:29:43.883  Bluetooth device removed from BlueZ: Pixel 10 Pro XL (B0:D5:FB:D2:0D:68) — evicted from cache
```

⛔ **Minutes later, and with nothing re-paired, `bluetoothctl info B0:D5:FB:D2:0D:68` on `hci0` reported
`Paired: yes`, `Bonded: yes`, `Connected: yes`**, and the phone was streaming. BlueZ did not forget our
phone. We logged that it had.

## Mechanism

`OnInterfaceRemoved` (`LinuxBluetoothService.cs:927-946`) handles `InterfacesRemoved` for **any** object
path that includes `Device1`, under **any** adapter. It evicts by path (`EvictDevice`, `:956`), which also
**disposes that path's `Connected` watcher**.

**Candidate — same as `AUD-30`, same caveat:** a `Device1` for the Pixel under RotaryPhone's `hci1` being
removed. The doc comment's own premise (`:920-926`: fires *"when a device is unpaired or when a
discovered-but-unpaired device ages out"*) is compatible with that: a temporary `hci1` object aging out
would produce exactly this line about a phone that is paired on `hci0`. ⚠ **Not established** — the line
does not log the path, so these two sightings cannot tell `hci0` from `hci1`.

## What is not known — and it sets the priority

- **Whether the eviction ever hits the `hci0` path.** If it does, we dispose the watcher for the phone we
  are streaming from, and **later real disconnects stop being seen**. That would be P1. If it only ever
  hits `hci1`, the damage is a misleading log line (and, via `AUD-30`, a phantom teardown). P2 until
  someone reads a path.

## First task

Same as `AUD-30` step 1: **log `{ObjectPath}` on the eviction line** (`:944-946`), deploy, read it on the
next sighting. Plan the two rows together — one path-scoping change fixes both if the hypothesis holds,
and neither should be "fixed" before the path has been read.

## Verification

Over a full sitting with the recorder running: no `removed from BlueZ` line for a device that
`bluetoothctl` on `hci0` still reports paired; with the logging shipped, every eviction line names its
adapter.

---

## 🚧 BUILT 2026-09-25 — shipped with `AUD-30`, branch `fix/aud-30-31-scope-device-objects-to-adapter`, PR [#666](https://github.com/mmackelprang/RTest/pull/666)

Mechanism confirmed by `AUD-30`'s cross-service timestamps. `OnInterfaceRemoved` now ignores any path not
under the selected adapter (`LinuxBluetoothService.cs:1121`), and its doc comment no longer claims the
signal fires only on unpair / age-out, or only for our adapter. The eviction line carries
`{ObjectPath}`. Full change, tests and box checklist: [`AUD-30.md` § BUILT](AUD-30.md).

### ⚠ This row overstated its own risk — correction

*"a phantom `removed from BlueZ` … **evicts the connected phone from our cache**"* and the P1 scenario
(*"we dispose the watcher for the phone we are streaming from"*) **do not follow from the confirmed
mechanism.** `_deviceCache`, `_watchedDevicePaths` and `_devicePropertyWatchers` are all keyed by
**object path**, not address. The `hci1` removal evicted the `hci1` entry — a second cache entry for the
same MAC — and disposed the `hci1` watcher. The `hci0` entry and its `Connected` watcher were never
touched. The damage was the misleading line plus the `AUD-30` teardown that the `hci1` entry enabled,
i.e. **P2 as filed was right; the P1 branch was not reachable this way.**

Tests: `ForeignDeviceRemoved_IsIgnoredByTheHandlerItself_EvenIfCached` (the observed line, with an
`hci1` entry placed where the old code put one), `ForeignDeviceAddedThenRemoved_LeavesNoTrace`,
`OurDeviceRemoved_StillEvicts_AndLogsItsPath`.

**Box PASS:** over a sitting with RotaryPhone refusing the Pixel on `hci1`, no `removed from BlueZ` line
at all; any that does appear names `/org/bluez/hci0/…` and corresponds to a real unpair.
