# `AUD-16` — retire the deprecated USB radio path now that RTL-SDR is the only supported tuner

[← Builder Queue index](../BUILDER_QUEUE.md)

🔵 **P3.** Filed 2026-09-08 by the `AUD-13` Planner, which found that the owner's deprecation is
**already implemented in behaviour** and that what remains is dead surface area, not a defect.

## Owner input this rests on

> *"The Radio USB connection has been deprecated so that the SDRRTL radio is really the only one
> supported."* — 2026-09-07

## What is already true, and therefore NOT this row

`AUD-13`'s planning established that the deprecation is live:

- `RadioFactory.cs:147` defaults to `RTLSDRCore`.
- `RadioFactory.cs:336-339` — `IsRF320Available()` opens with
  `if (string.IsNullOrWhiteSpace(usbPort)) return false;`, so a blank port means the RF320 is never
  offered or selected.
- `SystemConfigPage.razor:280` already ships the copy *"Leave empty if using RTL-SDR. Only set for
  RF320 USB audio capture."*

So **an empty `Devices:Radio:USBPort` is correct, documented configuration** — not a fault, and not
something to warn about. Anyone arriving here expecting to *make* the deprecation happen should stop
and re-read: it happened.

## What this row actually is

The RF320 USB capture path still exists in code, config schema, and UI. This row is the deliberate
decision about whether to **remove it** — and it is a scope question the plan must answer before
touching anything:

1. **Is the hardware genuinely gone?** The `RaddyRF320BT/` git submodule is still in the tree and
   `CLAUDE.md` describes it as the vintage radio protocol. Removing the audio path while the
   protocol submodule stays is either correct or half a change; decide which and say why.
2. **What is the blast radius?** `RadioAudioSource`, the `RadioFactory` branch, the `USBPort` config
   key, its `SystemConfigPage` field and copy, DTO surfaces, and any tests. Enumerate before
   estimating — this is the kind of removal that looks like an afternoon and touches thirty files.
3. **Deployed config.** ⚠ The SQLite store outranks both JSON layers and **an empty store entry still
   wins** (`DeviceOptionsResolver.cs:55-61`). If the key is removed, say what happens to a store row
   that still names it. `fingerprinting:fpcalcPath` is already sitting orphaned in that store from
   an earlier rename — that is the failure mode to avoid repeating.
4. **Or is the honest answer "leave it"?** Deprecated-but-working code that costs nothing is not
   automatically debt. The greenfield rule in `MEMORY.md` says no backward compatibility is owed,
   which makes removal *permitted*, not *required*. **A reasoned close as "keep, documented" is an
   acceptable outcome for this row.**

## Explicitly not this row

- **`AUD-13`** — the real defect there is the capture-device fallback binding the wrong jack on a
  **non-empty** port. Unrelated mechanism, and it must not be folded in.
- Anything about **vinyl**, which is USB-only and always will be per the same owner message.

## Verification

Whatever is removed, the gate is that RTL-SDR tuning still works end-to-end and no config path
throws on an existing deployed store. ⚠ Needs a box session; the tuner is hardware.

**Not auto-mergeable** if code is removed. Auto-mergeable if the outcome is documentation only.
