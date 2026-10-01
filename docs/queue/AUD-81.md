# `AUD-81` — the console cannot change a Cast speaker's volume

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-29 by the `AUD-80` builder while tracing Cast volume.

## The finding

Cast audio is taken from the output tap, **before** the master volume is applied, and nothing sets `GoogleCastOutput.Volume` after construction. So moving the console's volume does not change what the Cast speaker plays; only the speaker (or Google Home) can. `AUD-80`'s restore therefore remembers volumes set **on the speaker**, which is the only place a Cast volume can be set today.

## Owner decision

Should the console's volume control drive a connected Cast speaker? If yes: route master-volume changes to `GoogleCastOutput.SetCastVolumeAsync` while Cast is the output (through `AUD-80`'s per-device store, so the value persists), and decide whether the local speakers mute or stay at their own level while casting.

## ✅ Owner ruling 2026-09-30 — the console volume drives the Cast speaker

Owner, 2026-09-30 ([`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) evening batch §D): *"I'd like the console volume to be able to change the cast volume."* The row is now buildable. It belongs with the casting work (the Cast arc, 2c).

**The ruling decides whether, not how. The design must settle:**

1. **How master volume maps to the speaker.** Master volume is today the local speakers' level, which casting never reads (Cast audio is tapped before it). Does the console's slider set the speaker's level directly while Cast is the output, or scale it?
2. **The interaction with `AUD-80`'s per-device remembered volume** (`AudioPreferences:CastDeviceVolumes`). Today it records levels set on the speaker and restores them on reconnect; once the console can set the level, the console's changes must be remembered there too, and a reconnect must not fight the slider.
3. **The echo filter.** Every push to the speaker baselines the echo filter first (`AUD-80`), and `AUD-5` still lets external speaker changes reach master volume. A console → speaker → console loop is the obvious hazard; the design must show it cannot oscillate or re-persist an echo as master (the original `AUD-80` bug).
4. Whether the local speakers mute or keep their own level while casting (from the question above).

## 🔬 2026-09-30 — reconfirmed on the box (MEASURED)

In the owner's casting baseline run 2026-09-30 (box on `b64c8cd`, Office speaker — a Google Home Mini — in `DirectChannel` mode), after `radio-api` had restarted and restored the Cast output: *"changing the volume on the console didn't affect the office speaker."* The behaviour this row describes is unchanged. The same run found [`AUD-84`](AUD-84.md) (a Cast speaker that drops mid-stream crashes `radio-api`), which touches the same client-lifetime code; see [`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) § Casting baseline.

## ✅ As built (batch D, 2026-10-01) — how the four design questions were settled

**Scope: while the active output is `google-cast` and the Cast output is `Streaming`.** Nothing changes for local or HTTP-only outputs.

1. **Mapping — a per-connection curve, not the raw level.** Every master-volume change drives the speaker through `CastConsoleVolumeCurve.Map` (`src/Radio.Infrastructure/Audio/Services/CastConsoleVolumeCurve.cs`). It is a monotonic, piecewise-linear curve through an anchor (console level m0, speaker level s0), taken per connection. So the **first move continues from where the speaker already is and never jumps**. Console 0 is silent. When m0 = s0 the curve is the identity.
   - Lower segment (m ≤ m0): s = s0·m/m0.
   - Upper segment (m ≥ m0): s = min(1, s0 + k·(m − m0)), with k = min((1 − s0)/(1 − m0), 3). **The upper slope is capped at 3**, so one encoder detent near the top can never jump the speaker; uncapped, m0 = 0.98 / s0 = 0.2 gives k = 40. **What the cap costs:** when it binds, the console cannot take the speaker to 100 % on that connection. At console 100 % the speaker sits at s0 + 3·(1 − m0). A change made on the speaker itself, or a new connection, re-anchors.
   - 📝 **Design decision for the owner (recorded, not changed):** the **lower** segment is **not** slope-capped. A curve from (0, 0) to (m0, s0) with every slope ≤ 3 exists only when s0 ≤ 3·m0. Capping it would give up either continuity or "console 0 is silent". It is steep only toward silence, which is the safe direction.
   - Degenerate anchors (unknown speaker level, or m0 ≤ 0.01) map with the identity.
2. **`AUD-80`'s per-device memory (`AudioPreferences:CastDeviceVolumes`) — unchanged restore, console moves remembered.** The restore on connect and `AUD-5`'s initial-sync rule are unchanged. Console-driven `SET_VOLUME`s are **coalesced**: one is in flight and the latest wins. **Only a burst's final level is remembered per device**, not every intermediate step.
3. **The echo filter — a 3 s echo memory plus an explicit re-sync marker.** The speaker's reply to our own `SET_VOLUME`/`SET_MUTE` is recognised as an echo for 3 s (`EchoWindow`, `GoogleCastOutput.cs:309`) and never re-enters master volume. A genuine external change on the speaker (`AUD-5`'s path) re-anchors the curve and drops any queued console target. ⚠ **A status that arrives during a connect's initial sync is never treated as external** (`30a3524c`). This fixes a bug measured on the box during the build: on the first connect after a restart, the initial status read was taken as an external change, and `Synced mute from Cast device: false` **unmuted the console**.
4. **Local speakers stay muted while casting** (the existing output gate; unchanged).

**Mute.**
- Console **mute** mutes the speaker while casting, and a console that is already muted mutes the speaker as the stream starts.
- **Nothing auto-unmutes.** The speaker is unmuted in three cases only:
  1. A deliberate console unmute while Cast is the active output (`CastConsoleVolumeFollower.cs:243-248`). This unmutes the speaker whoever muted it.
  2. The reconcile when the gate makes Cast the active output, and only for a speaker this application muted for the console (`:284`).
  3. The teardown rule below.
- A console mute is pushed to any streaming Cast connection, even before the gate has marked Cast active. That is the safe direction.
- A console-muted speaker is **unmuted on a deliberate teardown**, never on a connection loss. It is unmuted only **after** our receiver application is confirmed stopped, so a muted console can never release audio to the room. If the stop cannot be confirmed within 3 s, the speaker is left muted and an Information line says so (`GoogleCastOutput.cs:3288`).
- **Mute is never stored** in `AudioPreferences`.

**Surfaces.**
- New `GET /api/devices/cast/volume`: a live, bounded (3 s) status read. It returns `{ deviceName, level, muted, knownLevel, knownMuted, mutedByConsole }`, or 404 (no Cast output), 409 (not streaming to a connected speaker), 504 (no answer) or 502 (read failed).
- `GET /api/devices/cast/diagnostics` gains `speakerLevel`, `speakerMuted` and `speakerMutedByConsole`. These are the output's own view, not a live read.

**Log lines** (file sink). The follower's lines are under `Radio.Infrastructure.Audio.Services` and are visible by default. The `GoogleCastOutput` lines are under `…Audio.Outputs`, which `LOG-2` holds at Warning, so raise `Radio.Infrastructure.Audio` (`LOG-5`) to see them:
- `Cast: console volume {Console:P0} → speaker {Speaker:P0} on {Name}`
- `Cast: console muted → speaker {Name} muted` / `Cast: console unmuted → speaker {Name} unmuted`
- `Cast: console is muted → speaker {Name} muted as casting starts`
- `Cast: speaker {Name} unmuted before closing, after its receiver application stopped — it had been muted for the console`
- `Cast: speaker {Name} left muted — its receiver application could not be confirmed stopped, …`

**Not testable offline (box only):** the ordering of the teardown (receiver application stopped before the unmute), and the `StartAsync` → start-of-stream mute call.
