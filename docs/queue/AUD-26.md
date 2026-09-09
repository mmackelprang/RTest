# `AUD-26` — switching source during an active duck leaves the new source at full volume

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-09 by `AUD-2`'s Builder, found while fixing the ducking key mismatch.

## What is wrong

`AudioManager.cs`, in the source-switch path: **`StopAsync` removes the entry from
`_duckingMultipliers` before registration reads it back.** So a source switched in while a duck is
active registers with **no multiplier** and plays at **full volume** under the ducking event.

## ⭐ Pre-existing, but `AUD-2` made it OBSERVABLE for the first time

⛔ **This is the important framing and it must not be lost.** Before `AUD-2` shipped
([#642](https://github.com/mmackelprang/RTest/pull/642), `f4d71b28`), **ducking did not work at all** —
the multiplier was written under `_activeSource.Id` while sources registered under minted keys, so
*nothing* ducked and this defect was invisible behind a larger one.

⚠ **Expect this class of thing after any fix that restores a broken mechanism**: defects that were
masked become reachable. **Do not read "new symptom after `AUD-2`" as "`AUD-2` caused it."**

## Scope questions for the plan

1. **Is the removal in `StopAsync` correct in isolation?** Clearing a stopped source's multiplier is
   defensible; the defect may be the **ordering** against registration, not the removal itself.
2. ⚠ **Which switch paths are affected?** Source switch is the observed one. **Establish whether the
   same read-after-remove ordering exists on the reconnect and error-recovery paths** before scoping
   the fix to one call site.
3. **What SHOULD the new source's level be mid-duck?** ⚠ Not obvious, and it is a design question, not
   a bug question: inheriting the current duck is one answer, starting un-ducked and being ducked on
   the next event is another. **Say which and why.**

## Verification

⭐ **Assert the PRESENCE of attenuation on the newly-registered source** — read back the registered
component's `Volume`, as `AUD-2`'s tests do. ⛔ **An indexer write to a wrong key throws nothing and
returns nothing**, so "did not throw" and "the dictionary holds the key" both pass on a broken tree.
`AUD-2`'s test seam (reading the *registered component's* volume) is the pattern to copy.

⚠ **Beware a vacuous test**: if the fixture ducks to `1.0`, or the source registers at `1.0` anyway,
the assertion passes on the defect. **Duck to a value distinguishable from full.**

## Related

- **`AUD-2`** — shipped, the parent. Read its PR for the key-registration map (four `AudioSourceType`s
  across five concrete classes) before touching registration.
- ⚠ **`BluetoothAudioSource` derives from `USBAudioSourceBase` but overrides three methods without
  calling base**, and declares its own `_playbackId` shadowing the base field. `AUD-2`'s Builder found
  this; it makes "all USB sources behave alike" false.
