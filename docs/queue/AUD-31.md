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
