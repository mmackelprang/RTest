# `AUD-25` — the API reports a Bluetooth "Turntable" as CONNECTED, with an empty address, while BlueZ has nothing

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-09, found incidentally while diagnosing a vinyl audio report that turned out
to be a raised cue lever.

## What was observed

`GET /api/sources` returns, under `bluetoothDevices`:

```json
{ "address": "", "name": "Turntable", "isPaired": true, "isConnected": true, "lastConnected": null }
```

⛔ **Every one of those fields is suspect at once:** an **empty address** on a device claiming to be
**paired AND connected**, with a null `lastConnected`.

Measured against the three layers below it, at the same moment:

| Layer | Says |
|---|---|
| `pactl list short sources \| grep -i bluez` | **nothing** — no bluez node exists |
| `bluetoothctl devices Connected` (on `hci0`, ours) | **nothing** — no device connected |
| `pw-cli ls Node` filtered for turntable/vinyl/phono | **nothing** |

⭐ Note the other entries in the same response are well-formed — `John's iPhone`, `Pixel 10 Pro XL`
and `mark's Tab A7 Lite` all carry real MAC addresses and `isConnected: false`. **Only this one is
malformed, and it is the only one claiming to be connected.**

## ⛔ Why this is worth a row despite harming nothing today

**It is a status field asserting a connection that does not exist** — the exact class this repo has
spent the week removing (`AUD-2`'s silent gain write, `AUD-24`'s silent seek, `UI-14`, the
`SoundFlowMasterMixer` "removed from mixer" log that is still a lie).

⚠ **The harm is to the NEXT diagnosis, not to audio.** On the day it was found it produced a live
false lead: with the owner reporting no vinyl audio, an entry reading *"Turntable, connected"* is a
plausible explanation, and it cost a detour to falsify. **On a box where `Vinyl` is USB-only
(owner decision, `AUD-13`), a connected Bluetooth "Turntable" is a contradiction that will mislead
whoever reads it next.**

## Scope questions for the plan

1. **Where does the entry come from?** An empty address suggests a persisted/remembered record rather
   than a live BlueZ query — ⚠ **establish whether `bluetoothDevices` is sourced from config, from a
   cache, or from BlueZ**, because the fix differs completely.
2. ⚠ **Is `isConnected` computed or stored?** If stored, it is stale state that nothing invalidates.
   If computed, it is computing from something that is not BlueZ.
3. **Should an address-less device be returned at all?** A device with no address cannot be connected
   to, so it is not actionable by any caller.
4. ⚠ **Does anything CONSUME `isConnected` from this endpoint?** ⛔ **Scope the grep beyond `src/`** —
   the `psidtsAgeSeconds` incident on 2026-09-09 established that a consumer can be a shell script
   invisible to a `src/`-scoped search. **A positive control validates the instrument, never the
   search space.**

## Verification

⭐ **Assert the ABSENCE of phantom entries against a live BlueZ read**, in the same breath — the row is
only closed when `/api/sources` and `bluetoothctl devices Connected` **agree**. A unit test with a
mocked BlueZ layer cannot see this defect; it is a disagreement *between* layers.

⚠ **Do not close it by deleting the "Turntable" record.** That removes today's symptom and leaves the
mechanism that produced it. Establish question 1 first.

## Related

- ⚠ **Not `AUD-13`** — that row is about an empty device config matching every device. This is a
  phantom entry in a status response. They may share a cause; **check, do not assume.**
- Found during the vinyl investigation recorded at
  [`docs/diagnostics/vinyl-signal-floor.md`](../diagnostics/vinyl-signal-floor.md).
