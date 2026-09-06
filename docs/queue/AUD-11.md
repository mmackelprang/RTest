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

⚠ **Corrected 2026-09-06 after the plan was written — the paragraph below was the filer's first
guess and is only half right.** The real mechanism is `node.autoconnect`, not `PW_ID_ANY`; see the
correction after the quote. Kept rather than deleted because the wrong guess is the one a reader
arrives with.

The project's auto-memory (`~/.claude/projects/…/memory/MEMORY.md` — **not a file in this repo**,
despite earlier revisions of this dossier citing it as one) records the design decision this
behaviour was first attributed to:

> **Use PW_ID_ANY (0xffffffff)** as targetId in `pw_stream_connect` — let PipeWire resolve via
> `target.object` property

With `PW_ID_ANY`, PipeWire is free to resolve the stream to **whatever the default source is** when
the intended target is absent. On this box the default input is
`alsa_input.pci-0000_00_1f.3.analog-stereo`, which is exactly where it landed.

### ⚠ The correction — `PW_ID_ANY` is not the mechanism, and there is no trade-off

Established by [the plan](../../design/plans/AUD-11-the-capture-that-recorded-the-wrong-jack.md) by
reading the code rather than the memory note:

**The fallback decision is not in this repo at all.** `PipeWireNativeStream.cs:204` sets
`node.autoconnect = true` alongside `target.object`. Per `pipewire-props(7)`, `node.autoconnect`
*"instructs the session manager to automatically connect this node to some other node"* — an
instruction with **no failure mode** — while `target.object` is only a **preference**. An
unresolvable preference plus an outstanding instruction yields the default source.

**`PW_ID_ANY` bought no binding guarantee.** It removed a *competing* numeric target that was being
fed an `object.serial` into a parameter meaning node **id**. That is the whole purchase. So it is
not a constraint on the fix: **`PW_ID_ANY` stays untouched and the fix is additive** —
`node.dont-reconnect = true`, documented as *"also inhibits that the node is moved to another
sink/source."* The one move genuinely unavailable is passing `_targetNodeId` as `targetId` again.

**This repo already knew, and the defence was lost in a migration.** `LinuxBluetoothService.cs:1985-1987`
documents the same behaviour for the old `pw-record` path, which guarded it **twice**
(`-P node.autoconnect=false` plus an explicit re-link). The native path that replaced it in #262
inherited neither. This is a regression, not a novel defect.

**A second route the symptom description misses:** `LinuxBluetoothService.cs:1814` converts "could
not read `object.serial`" into the literal target `0` — reachable on a cold connect with nothing
having disappeared.

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
