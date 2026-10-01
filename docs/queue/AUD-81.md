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
2. **`AUD-80`'s per-device memory (`AudioPreferences:CastDeviceVolumes`) — unchanged restore, console moves remembered.** The restore on connect and `AUD-5`'s initial-sync rule are unchanged. Console-driven `SET_VOLUME`s are **coalesced**: one is in flight and the latest wins. **Only a burst's final level is remembered per device**, not every intermediate step. *(Follow-up 2026-10-01:)* while the console is muted, a console move is **held, not sent** (see the follow-up section below). A held level is remembered only when it is sent, at the console's unmute, by the burst that sends it.
3. **The echo filter — a 3 s echo memory plus an explicit re-sync marker.** The speaker's reply to our own `SET_VOLUME`/`SET_MUTE` is recognised as an echo while the send is in flight and for 3 s after it completes (`EchoWindow`, `GoogleCastOutput.cs:314`), and never re-enters master volume. A level counts as our echo when it is within a tolerance of one of our recent pushes, or inside the range our pushes from the last 3 s span, widened by that tolerance. The tolerance is 0.01, or just over half the volume step the speaker reports in its receiver status (`Volume.StepInterval`, e.g. 1/15 on a 15-detent speaker; a speaker that also quantises `SET_VOLUME` to that step would report our 0.50 as 0.5333 — assumed device behaviour, not yet measured on the box's speakers), whichever is larger (`IsRecentVolumePush`). A push still in flight more than 3 s after it started (for example a timed-out one) matches only its own echo and does not widen the range. With no step reported, a speaker that rounds more coarsely than 0.01 still has the echo of a single push read as external. A genuine external change on the speaker (`AUD-5`'s path) re-anchors the curve and drops any queued console target. ⚠ **A status that arrives during a connect's initial sync is never treated as external** (`30a3524c`). This fixes a bug measured on the box during the build: on the first connect after a restart, the initial status read was taken as an external change, and `Synced mute from Cast device: false` **unmuted the console**. *(Follow-up 2026-10-01:)* one exception to "external". A status that shows the speaker **unmuted** at a level that is the echo of one of our recent level pushes, while the console is muted and the speaker is marked muted by the console, is not reported. The mute is re-asserted instead (`GoogleCastOutput.cs:2673`; see the follow-up section).
4. **Local speakers stay muted while casting** (the existing output gate; unchanged).

**Mute.**
- Console **mute** mutes the speaker while casting, and a console that is already muted mutes the speaker as the stream starts.
- *(Follow-up 2026-10-01)* **No level command may leave the speaker unmuted under a muted console.** A `SET_VOLUME` that changes the level unmutes the speaker (measured; see the follow-up section). So while the console is muted, console moves send no level. The console's unmute sends the held level **first**, then the unmute. Any level push that cannot be held is followed by a mute re-assert.
- **Nothing auto-unmutes.** The speaker is unmuted in three cases only:
  1. A deliberate console unmute while Cast is the active output (`OnMuteStateChanged`, `CastConsoleVolumeFollower.cs:272-299`). This unmutes the speaker whoever muted it.
  2. The reconcile when the gate makes Cast the active output, and only for a speaker this application muted for the console (`:328`) — on this connection, or on an earlier connection to the same device (next bullet).
  3. The teardown rule below.
- A console mute is pushed to any streaming Cast connection, even before the gate has marked Cast active. That is the safe direction.
- A console-muted speaker is **unmuted on a deliberate teardown**, never on a connection loss. It is unmuted only **after** our receiver application is confirmed stopped, so a muted console can never release audio to the room. If the stop cannot be confirmed within 3 s, the speaker is left muted and an Information line says so (`GoogleCastOutput.cs:3819-3822`). *(Follow-up 2026-10-01:)* if the stop is confirmed but the unmute cannot be sent on the closing connection, it is retried over a **fresh, short-lived connection that launches nothing** (`UnmuteOverFreshConnectionAsync`, `GoogleCastOutput.cs:3905`). The unmute is sent only if that connection's own status shows our application not running and the speaker muted. The box UAT found the speaker closes our connection when our application stops.
- **A console mute survives a lost connection, per device, in memory only** (F11, `RecallConsoleMuteAsync`, `GoogleCastOutput.cs:3715`). The output remembers the id of the device it last muted for the console until it sees that device unmuted (an acknowledged console or teardown unmute, an unmute reported by the speaker, or a reconnect whose initial read shows it unmuted). A new connection to that device whose initial read shows it still muted is marked "muted by console" again, so the reconcile above unmutes it if the console was unmuted meanwhile, it stays muted if not, and a deliberate teardown releases it. A connection to a different device is unaffected, and nothing is sent on the basis of the memory alone. It is lost on a radio-api restart: a speaker left muted that way is then treated like one muted on its own side (never unmuted for the console).
  - ⚠ **What it cannot tell:** "still muted by us" from "unmuted, then muted again by the owner on the speaker while we were disconnected". Nothing was observed in between, so in that case the mark is re-armed and the reconcile **unmutes the owner's mute**.
  - It is keyed by `ChromecastDeviceInfo.Id`. The cached and live records of one speaker can carry different ids (`ConnectAsync`'s "matched by IP (ID mismatch)" path, `GoogleCastOutput.cs:909`), so a reconnect through the other id misses the recall and the speaker stays muted. That is the safe direction.
  - One slot. It is replaced by a console mute on another device, and also by the late acknowledgement of a console mute whose connection has since been superseded.
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
- `Cast: speaker {Name} is still muted from an earlier console mute — treated as muted by the console`
- *(Follow-up 2026-10-01; all under the follower's namespace, so visible by default, except the last.)*
  - `Cast: console unmuted → speaker {Name} at {Level:P0}, unmuted`. Replaces the plain unmute line when a held level was sent first.
  - `Cast: console is muted → volume {Volume:P0} for {Name} held until the console is unmuted`. This is the after-start push.
  - `Cast: speaker {Name} was unmuted by a volume change while the console is muted — muted it again`
  - `Cast: speaker {Name} unmuted over a new connection after closing — …` / `… is already unmuted after closing (read over a new connection)` / `… left muted — a new connection still shows our receiver application running, …` / `… left muted — a new connection could not read its status; …` / `… left muted — it could not be reached over a new connection to unmute it ({Error}); …`
  - `Cast: speaker {Name} was muted — muted again after setting its volume to {Volume:P0}`. This line is under `…Audio.Outputs`.

**Not testable offline (box only):** the ordering of the teardown (receiver application stopped before the unmute), and the `StartAsync` → start-of-stream mute call. *(Follow-up 2026-10-01:)* also that a speaker unmutes on a level change and that a fresh connection reaches it after our application stops. The unit tests assume both from the box evidence. Only a box run shows that the fresh connection really lands the unmute.

**Deferred (LOW, logged by the hostile reviews, not fixed).**
- **F5:** a metadata-reload relaunch already past its debounce can relaunch our receiver application after the teardown unmute. The window is narrow.
- **F6:** a console mute that lands during a teardown can leave a stale "muted by console" mark. That is the safe direction (the speaker stays muted).
- **F7:** the first console move with an unknown speaker level maps with the identity curve, so it can jump.
- **F10:** `_lastSetMute` and `_lastSetVolume` are read as plain fields. One test comment about the device-switch path was flagged and is unchanged. `ReadSpeakerVolumeAsync` absorbs every status that arrives while a read hangs (up to its 3 s bound).
- **F11, restart:** the per-device console-mute memory is in process only, so a radio-api restart loses it (see Mute, above).
- **F11, failed initial read:** a reconnect whose initial status read fails never re-arms the mark. If the console was unmuted meanwhile, the speaker stays muted. That is the safe direction.
- **Echo, step midpoint (round-3 LOW-2):** for a push within 0.001 of the midpoint between two speaker steps (on a 1/15 speaker: 0.10, 0.30, 0.50, 0.70, 0.90), both neighbouring steps are inside the tolerance. A one-step change made on the speaker's own buttons within 3 s of that push is then absorbed as our echo. Master volume does not follow it, and `AUD-80` keeps the console's level. The next console move corrects it.
- **Echo, stale step (round-3 LOW-3):** the initial read records the speaker step only while its client is still `_client`, and a live-discovered device reuses the client across connects. So the guard is weaker than "per connection". The publish reset makes the practical risk negligible.
- **Echo, older baseline compare (round-3 LOW-1, remainder):** the baseline comparison for a status arriving more than 3 s after its send completed still uses 0.01. The remembered-volume restore on connect uses the step tolerance.

## 🔬 Box UAT 2026-10-01 and the follow-up fix

**Measured** 2026-10-01, 01:51–01:53 EDT, on `82f5840`. Office speaker (a Google Home Mini), `DirectChannel`, source stopped, console muted.

- **Connecting with the console muted.**
  - 01:51:16.977 `Cast: console is muted → speaker Office speaker muted as casting starts`.
  - 01:51:18.024 `Cast: Volume synced to 30 %`. This is the after-start push of the level the speaker already held, and it did **not** unmute the speaker.
  - `GET /api/devices/cast/volume` then returned `level 0.3, muted true, mutedByConsole true`.
- **Console volume 0.30 → 0.45 under the muted console.**
  - 01:51:23.381 `Cast: console volume 45 % → speaker 45 %`.
  - The speaker then reported `muted:false, knownMuted:false, mutedByConsole:true` while the console stayed muted. **A `SET_VOLUME` that changes the level unmutes this speaker.**
  - The console was not changed (no `Synced` line).
  - The speaker stayed unmuted under the muted console until a console unmute → mute toggle re-muted it (`Cast: console muted → speaker Office speaker muted`).
- **Stop Casting with the speaker muted for the console.**
  - 01:52:32.685 DirectChannel streaming stopped.
  - 32.785 a receiver status: `Cast device volume changed externally: 30 %, Muted: true (level changed: false)`.
  - 32.885 `Cast: loss reported while the output is "Stopping" … (the Cast connection closed (heartbeat timeout or the receiver closed it)`.
  - 34.785 `Cast: could not unmute speaker Office speaker before closing (TimeoutException) — it may stay muted`.
  - **Stopping our receiver application makes the speaker close our connection.** So the teardown unmute, sent after the application stop per the H1 design, could not be confirmed.
  - On the next connect, about 35 s later, the speaker read as unmuted. It is unknown whether the timed-out unmute landed anyway or the device cleared the mute when the application ended.

**What this branch (`fix/aud-81-muted-console-volume`) changes.**

- **D1: no level command leaves the speaker unmuted while it should be muted for the console.**
  - **Console moves while the console is muted are held, not sent.**
    - The console volume drain checks the console's mute at send time. While it is muted, the latest mapped level is parked (latest wins), with a Debug line only.
    - The console's unmute sends the held level **first**, waits for it (and for any console burst already running), then sends `SET_MUTE false`.
    - It logs one line: `Cast: console unmuted → speaker {Name} at {Level:P0}, unmuted`.
    - If the console is muted again while the level is on the network, that unmute is not sent.
    - A held level is **remembered for `AUD-80` when it is sent, not when it is held**, once per burst like any console level. The speaker never held it before then. A cast that ends while the console is still muted therefore leaves `AUD-80` with the level the speaker really kept.
    - A held level is dropped by a change reported by the speaker, by a new connection, by a newer level actually sent, and by the console coming back to the speaker's own level.
  - **The after-start push** (`SyncVolumeAfterStartAsync`) takes these steps in order:
    1. A muted speaker that already holds the level (within the echo tolerance) gets nothing. The box showed this case.
    2. A speaker muted for a still-muted console gets **no level now**. The level is held for the unmute, so there is no unmute window while our audio streams.
    3. Otherwise the level is pushed. If the speaker was muted on its own side, the mute is re-asserted straight after the push.
  - **The `AUD-80` restore on connect** to a speaker the read found muted re-asserts the mute straight after the push.
  - **Re-assert on status.**
    - The trigger: a receiver status that shows the speaker unmuted, while the console is muted and the speaker is marked muted by the console, at a level that is the echo of one of our recent level pushes (in flight, or completed within the 3 s echo window).
    - It is **not** reported as external, which would unmute the console and clear the mark. The mute is re-asserted once for that status, through the console mute drain, and logged at Information.
    - The re-assert is dropped if the console has been unmuted by the time its turn comes.
    - An unmute is told apart from the owner's by its level. An unmute whose level is not such an echo is external exactly as before (`AUD-5`). That covers every unmute more than 3 s after our last level push, and any unmute that comes with a level change made on the speaker.
    - What that gives up: the owner unmuting on the speaker **without** changing its level, within 3 s of a level push of ours, is re-muted. The console is muted at that moment, so the re-mute matches it.
  - The activation reconcile sends mutes only and needed no change. Its unmute now also sends a held level first.
- **D2: the teardown unmute of a console-muted speaker is confirmed even though stopping our application closes the connection.**
  - The H1 ordering is kept: the application is stopped before any unmute.
  - The trigger: the stop is confirmed, and the unmute on the closing connection then times out, throws or finds no receiver channel.
  - The output then opens a **fresh, short-lived connection** to the same device. SharpCaster 3.0.0's `ConnectChromecast` does TCP, TLS, CONNECT to the platform receiver and `GET_STATUS`, with no launch; this was checked against the decompiled source.
  - It reads that status. It sends `SET_MUTE false` only if no application with our `ApplicationId` is listed and the speaker is muted. Then it disconnects.
  - The whole step is bounded at 5 s on the injected clock. It logs one Information line with the outcome: unmuted, already unmuted, our application still running, status unreadable, or could not be reached.
  - It runs only from the deliberate-teardown path, **never on a connection loss**. It does not run once the device is no longer recorded as muted for the console; that is checked before connecting and again just before the `SET_MUTE`.
  - On a timeout the connection attempt is abandoned and told not to send. An unmute already on the wire then is not recalled.
  - New kind-C seam: `CastFreshConnectionOverrideForTests`.

**Still box-only:** whether the fresh connection actually reaches the speaker after it closed ours, and whether its unmute lands.
