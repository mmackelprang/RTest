# `AUD-11` — the BT capture stream silently falls back to the line-in when its target disappears

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** **Observed live on `radio` 2026-09-06.** This is the row that makes `AUD-10` *silent*
instead of loud, and it is the more dangerous of the two.

## Symptom

When the `bluez_input` node vanishes (see `AUD-10`), `radio-bt-stream` does not fail, stop, or log
anything. **PipeWire re-links it to the built-in analog capture** and it carries on happily
recording an unplugged jack.

Measured during the incident — the same stream, before and after:

```
# Healthy — reading from the phone:
radio-bt-stream:input_FL  <- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL   [active]

# After the BT node vanished — reading the line-in, still [active]:
radio-bt-stream:input_FL  <- alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FL   [active]
```

Every downstream indicator stays green. The graph shows an active capture, `Radio.API` shows an
active playback stream to the speakers, and the console reports a working Bluetooth source. The
only evidence anything is wrong is that the room is quiet.

## Mechanism — the likely cause is already documented in this repo

`MEMORY.md` records the design decision this behaviour follows from:

> **Use PW_ID_ANY (0xffffffff)** as targetId in `pw_stream_connect` — let PipeWire resolve via
> `target.object` property

With `PW_ID_ANY`, PipeWire is free to resolve the stream to **whatever the default source is** when
the intended target is absent. On this box the default input is
`alsa_input.pci-0000_00_1f.3.analog-stereo`, which is exactly where it landed.

⚠ **`PW_ID_ANY` was a deliberate fix, not an accident** — `MEMORY.md` records it as the resolution
to an earlier targeting problem in the PipeWire native interop work (PR #262). **Do not simply
revert it.** The plan must find a form that keeps whatever `PW_ID_ANY` bought while refusing to
bind to a node that is not the intended BT device. Verify the claim above against
`PipeWireNativeStream.cs` / `PipeWireNative.cs` before building on it; it is a memory note, not a
code reading.

## Why this outranks its own severity

This is the third instance this month of the failure mode `CLAUDE.md` § *Pre-Merge Review* exists
for — **a component reporting success while doing nothing**, alongside `AUD-2`'s silent gain/ducking
miss and `SoundFlowMasterMixer` logging a detach it never performed. Here the false signal is not a
log line but the graph itself, which is worse: it defeats the exact diagnostic a person would reach
for.

A fix that only re-targets, without also making a wrong binding *observable*, leaves the next
occurrence just as invisible. **Whatever the fix, the stream must be able to say it is attached to
the wrong thing.**

## Scope questions the plan must answer

1. Can the stream bind to the BT node specifically and **refuse** anything else, without
   reintroducing the problem `PW_ID_ANY` solved?
2. What should happen when the target is gone — stop, retry, or park in an explicit
   `WaitingForDevice` state? The current answer is "silently record something else", which is the
   only clearly wrong option.
3. Is the same pattern used by any other capture source? USB/Vinyl/GenericUSB all capture through
   related paths and may share it. Enumerate rather than assume.

## Verification

Reproduce by removing the target: play over BT, pause on the handset (which destroys the node per
`AUD-10`), then inspect `pw-link -l`. Today the stream re-appears on `alsa_input…capture_FL`; after
the fix it must not, and the state must be visible somewhere a person can read.

Manual recovery, for reference — this is what was used on the night:

```bash
pw-link -d alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FL radio-bt-stream:input_FL
pw-link -d alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FR radio-bt-stream:input_FR
```
