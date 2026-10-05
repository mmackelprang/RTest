# AUD-2 — One key per source: four primary sources register under a minted key and are addressed by `IAudioSource.Id`, so gain and ducking miss silently.

> Queue dossier for row **`AUD-2`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.
>
> ⚠ **Directional words in the prose were written when every row shared one file.**
> *above*, *below* and *this file* may now point across files — most often at
> [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) or a sibling in this
> directory. They were left verbatim rather than reworded, which would be a content edit.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | [`design/plans/AUD-2-one-key-per-source.md`](../../design/plans/AUD-2-one-key-per-source.md) |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ · `CLAUDE.md` § Architecture (ducking as a core pattern) |
| Depends on | — _(no row dependency; claimable now. **⚠ The "same root as `AUD-4` — prefer claiming `AUD-2` FIRST" note that stood here was FALSIFIED 2026-09-06 while planning `AUD-4`, and is removed rather than softened.** They are unrelated bugs: this row is a key-identity defect in a *third party* (`AudioManager` addressing a source by `Id` when the source registered under a key it minted), while `AUD-4`'s roster is keyed by **object reference** and involves no string key at all — so there is no key here for `AUD-4` to wait on, and per-source teardown is key-symmetric. **Either may be claimed first.** See [`AUD-4`'s plan](../../design/plans/AUD-4-unify-source-removal-and-rename-the-mixer.md) §0.4, `C-148`, `C-150`.)_ |
| Branch | `fix/sdr-playback-id-ducking-gain` |

## Detail

**One key per source: four primary sources register under a minted key and are addressed by
`IAudioSource.Id`, so gain and ducking miss silently.** **✅ CONFIRMED 2026-09-05 by a
`team-debugger` pass (~90% confidence) — the investigation is DONE and must not be re-run.** The
row was filed confirm-or-close; it closed as **confirm**. Plan:
`design/plans/AUD-2-one-key-per-source.md`.

**The suspected mismatch.** `SDRRadioAudioSource` mints its own playback key — `_playbackId = $"sdr-radio-{Guid.NewGuid():N}"` (`src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs:908`) — and registers the component under it via `PlayComponentAsync(_playbackId, …)` (`:956-960`), which lands in `SoundFlowPlaybackService._activeComponents[sourceId]` (`src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs:423`, dictionary declared at `:25`). But `AudioManager` addresses the same source by `IAudioSource.Id`, which `AudioSourceBase` derives as `$"{Type}-{Guid.NewGuid():N}"` (`src/Radio.Infrastructure/Audio/Sources/AudioSourceBase.cs:28`) — and `SDRRadioAudioSource.Type` is `AudioSourceType.Radio` (`:204`), i.e.

**`Radio-<guid>`**. **Two different keys, with two different GUIDs, for one source.**

**⚠ SCOPE IS FOUR SOURCE TYPES ACROSS THREE FILES, NOT SDR-ONLY — the title and the branch name
both understate it.** `SDRRadioAudioSource.cs:915`, `FilePlayerAudioSource.cs:727`, and
`USBAudioSourceBase.cs:317` — the last inherited by `RadioAudioSource`, `VinylAudioSource` and
`GenericUSBAudioSource`. `$"usb-capture-{Id:N}"` is a miss, not a near-miss: `Id` is a `string`,
so `:N` is ignored, and the result contains the Id without equalling it. `BluetoothAudioSource`
and `TestToneAudioSource` already use `Id` and need no change. `AudioFileEventSource.cs:146` is
deliberately excluded — it is an **event** source, `AudioManager` addresses only primary sources,
and `EventPlaybackService.cs:32-41` documents that id space on purpose.

**What no-ops if it holds:** `_playbackService.SetGainOffset(source.Id, gain)` (`src/Radio.Infrastructure/Audio/Services/AudioManager.cs:292`, and the sibling at `:121`), `SetDuckingMultiplier(source.Id, multiplier)` (`:247`), `SetDuckingMultiplier(_activeSource.Id, multiplier)` (`:479`), and `ClearDuckingMultiplier(_activeSource.Id)` (`:508`, and `:240` on source switch).

**Why it would fail SILENTLY, which is the part that makes this worth a row:** none of those methods checks membership. `SetGainOffset` (`SoundFlowPlaybackService.cs:620`) and `SetDuckingMultiplier` (`:643`) write into `_gainOffsets` / `_duckingMultipliers` unconditionally and then call `ApplyEffectiveVolume(sourceId)`, whose two `TryGetValue` lookups (`_activePlayers`, `_activeComponents`) simply **miss** — no exception, no warning. `SetGainOffset` then logs `"Applied gain offset {Gain:F2} to source (SourceId={SourceId})"` at Debug: **the log line asserts a success it never verified.** Same class of trap as AUD-4's mixer log, and worth fixing in the same spirit even if the key mismatch turns out not to exist.

**Consequence — no longer conditional, since the row CONFIRMED: TTS and event audio never duck the radio source**, and per-source gain never applies to it. Audio ducking is a documented core pattern in `CLAUDE.md` § Architecture ("Audio ducking with priority system (1-10 scale)"), so this would be a significant hole, not a cosmetic one.

⛔ ~~**First task is confirmation, not repair.**~~ **SPENT — the confirmation happened on 2026-09-05 and must not be re-run** (see the ✅ block at the top of this dossier, and the plan §8). The check below is kept as the historical method, not as an instruction: _Cheapest check: play SDR radio, fire a TTS event, and observe whether radio volume actually drops; corroborate by dumping the live keys (`SoundFlowPlaybackService` already exposes `_activePlayers.Keys` / component counts around `:734` and `:752`) and comparing them against the `IAudioSource.Id` the `AudioManager` is holding._ ⚠ **That corroboration does not actually work — see the anchor note at the foot of this dossier.**

**⚠ Trap for the fix, now confirmed: do not paper over it by making `AudioManager` translate to `sdr-radio-*`.** The right answer is **one key per source, agreed by both layers**. ⚠ **The clause that used to close this paragraph — *"the same conclusion AUD-4 reaches from the teardown side. Coordinate the two."* — was FALSIFIED 2026-09-06 while planning `AUD-4` and is withdrawn, not softened**: `AUD-4`'s roster is a `List<IAudioSource>` keyed by **object reference**, so it reaches no conclusion about key identity and there is nothing here to coordinate. See the Depends-on cell above.

_**⚠ The 2026-08-11 anchor claim is STALE — five anchors had drifted by `656f58e6`.** Corrected:
`SDRRadioAudioSource.cs:908`→**`:915`**, `:956-960`→**`:963-967`**;
`SoundFlowPlaybackService.cs:620`→**`:656`**, `:643`→**`:679`**; `AudioManager.cs:508`→**`:554`**.
Unchanged and re-verified at `656f58e6`: `SDRRadioAudioSource.cs:204`,
`SoundFlowPlaybackService.cs:25`/`:423`, `AudioManager.cs:121`/`:240`/`:247`/`:292`/`:479`,
`AudioSourceBase.cs:28`. **And the row's suggested corroboration does not work:**
`GetDiagnostics()` (now `SoundFlowPlaybackService.cs:766-772`, not `:734`/`:752`) returns
`_activePlayers.Keys` **only** — every source in this row except FilePlayer registers as a
*component*, so it comes back empty and proves nothing. Use the plan's UAT §6.4 instead. **The
row also under-scopes the fix: a second half, not in this row, corrects the log lines that assert
success after a lookup that matched nothing (`SoundFlowPlaybackService.cs:666`,
`AudioManager.cs:557-559`) — see the plan §2.**_

---

## ⭐ LIVE OWNER OBSERVATION 2026-09-08 — ducking does not work when playing a voicemail

> "ducking does **not** work when playing a voicemail"

**This is very likely this row's user-visible symptom, and it upgrades the row's evidence from
code-reading to a heard failure.** Until now `AUD-2` was CONFIRMED by reading the key mismatch; nobody
had reported the consequence out loud.

**Why it fits:** the event side is wired correctly. `EventPlaybackService` takes `IDuckingService`
(`:48`, `:150`, `:159`), subscribes to `DuckingStateChanged` (`:186`), and `DuckingService` registers
the event in `_activeEvents` keyed by `eventSource.Id` (`:147`). So **the duck starts.** What this row
says fails is the other half — the four primary source types register under a *minted* key and are
addressed by `IAudioSource.Id`, so **the attenuation targets a key that matches nothing and the music
never gets quieter.** SDR radio and everything on `USBAudioSourceBase` (vinyl, USB) are both in scope,
which covers whatever the owner was listening to.

⚠ **NOT PROVEN, and the plan must distinguish these before fixing anything:**

1. **This row's key mismatch** — duck requested, attenuation misses. *(Predicted by the row.)*
2. **An event-side gap** — the voicemail source never requests ducking, or requests it at a priority
   that resolves to no attenuation. `EventPlaybackService` wires ducking, but wiring is not the same as
   *this path* invoking it.
3. **Both.**

**Cheapest discriminator:** `DuckingService` logs *"Added event source '{SourceId}' to ducking queue"*
at `:175`. Play a voicemail and look. **If that line appears, the event side is fine and this row owns
the failure**; if it does not, there is a second defect and this row is not the whole story.

⚠ **Do not let the fix be judged by ear alone.** "It ducks now" is exactly the kind of claim that has
gone wrong repeatedly this week — pair the listening test with the log line and a measured gain value.

---

## ✅ SHIPPED 2026-09-09 — [#642](https://github.com/mmackelprang/RTest/pull/642)

Fixed at three sites — `SDRRadioAudioSource.cs`, `USBAudioSourceBase.cs` (inherited by
`RadioAudioSource`, `VinylAudioSource`, `GenericUSBAudioSource`) and `FilePlayerAudioSource.cs` —
each now registering under `IAudioSource.Id`. Plus the half this row did not scope: the four volume
setters report whether they reached a live player or component, and `AudioManager` turns a miss on a
`Playing` source into a `Warning`.

⛔ **UAT IS NOT DISCHARGED.** The unit tests assert that a key which matches moves a registered
component's `Volume` and that a key which diverges does not; `PlaybackKeyLintTests` pins the key.
**Neither can hear anything.** The audible gate is the owner re-running
[`PHN-2` §3 U1](../uat/2026-09-09-phn2-sound-uat/RESULT.md) at the cabinet — check #6, the
one that failed — after a deploy. **This PR did not deploy.**

⚠ **Do not read a `PHN-10` re-listen as evidence.** With ducking broken, two simultaneous
voicemails arrived at full level over an un-ducked radio. Fixing this row makes `PHN-10`
**quieter without making it less real.**

### What this row got wrong, recorded rather than quietly corrected

- **The row's suggested corroboration never worked, and now does.** It proposed dumping
  `GetDiagnostics()` to compare live playback keys against the `IAudioSource.Id` `AudioManager`
  holds. That method returned `_activePlayers.Keys` **only**, and every source in this row except
  FilePlayer registers as a *component* — so the check would have come back **empty** and proved
  nothing. `#642` added `ComponentIds` and surfaced it on `/api/audio/diagnostics`, so the
  comparison the row wanted is now actually possible.
- **The plan's §4.3 test draft would have been a decorative gate.** It asserted
  `Assert.False(service.SetGainOffset("no-such-source", 1.5f))` and, for the store-then-apply path,
  explicitly "pins the absence of a throw and nothing more" — which passes against the broken
  tree and against almost any implementation. Replaced with assertions on the component's actual
  `Volume`, in both directions.
- **The plan cited `PlayFileAsync` at `SoundFlowPlaybackService.cs:343`.** That line is
  `PlayDataProviderAsync`; `PlayFileAsync` reads `_gainOffsets` at `:148`.
- **The plan's §8 and §9 were already applied** to this dossier and to
  `HANDOFF-GA-PUNCH-LIST.md` before the cycle began — spent, not skipped.
