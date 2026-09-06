# AUD-2 — Confirm-or-close: is SDR gain/ducking silently dead because the source is registered under one key and addressed by another?

> Queue dossier for row **`AUD-2`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | _plan TBD (**investigate first**; scope depends entirely on the answer)_ |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ · `CLAUDE.md` § Architecture (ducking as a core pattern) |
| Depends on | — _(no row dependency. **Same root as AUD-4, seen from the ducking side rather than the teardown side — prefer claiming AUD-2 FIRST**: it is investigation-cheap and it decides the key identity that AUD-4's sweep depends on.)_ |
| Branch | `fix/sdr-playback-id-ducking-gain` |

## Detail

**Confirm-or-close: is SDR gain/ducking silently dead because the source is registered under one key and addressed by another?**

**⚠ INVESTIGATE FIRST. This is an unverified inference from a code read, not an observation, and the row may legitimately close with no code change.**

**The suspected mismatch.** `SDRRadioAudioSource` mints its own playback key — `_playbackId = $"sdr-radio-{Guid.NewGuid():N}"` (`src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs:908`) — and registers the component under it via `PlayComponentAsync(_playbackId, …)` (`:956-960`), which lands in `SoundFlowPlaybackService._activeComponents[sourceId]` (`src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs:423`, dictionary declared at `:25`). But `AudioManager` addresses the same source by `IAudioSource.Id`, which `AudioSourceBase` derives as `$"{Type}-{Guid.NewGuid():N}"` (`src/Radio.Infrastructure/Audio/Sources/AudioSourceBase.cs:28`) — and `SDRRadioAudioSource.Type` is `AudioSourceType.Radio` (`:204`), i.e.

**`Radio-<guid>`**. **Two different keys, with two different GUIDs, for one source.**

**What no-ops if it holds:** `_playbackService.SetGainOffset(source.Id, gain)` (`src/Radio.Infrastructure/Audio/Services/AudioManager.cs:292`, and the sibling at `:121`), `SetDuckingMultiplier(source.Id, multiplier)` (`:247`), `SetDuckingMultiplier(_activeSource.Id, multiplier)` (`:479`), and `ClearDuckingMultiplier(_activeSource.Id)` (`:508`, and `:240` on source switch).

**Why it would fail SILENTLY, which is the part that makes this worth a row:** none of those methods checks membership. `SetGainOffset` (`SoundFlowPlaybackService.cs:620`) and `SetDuckingMultiplier` (`:643`) write into `_gainOffsets` / `_duckingMultipliers` unconditionally and then call `ApplyEffectiveVolume(sourceId)`, whose two `TryGetValue` lookups (`_activePlayers`, `_activeComponents`) simply **miss** — no exception, no warning. `SetGainOffset` then logs `"Applied gain offset {Gain:F2} to source (SourceId={SourceId})"` at Debug: **the log line asserts a success it never verified.** Same class of trap as AUD-4's mixer log, and worth fixing in the same spirit even if the key mismatch turns out not to exist.

**Consequence if confirmed: TTS and event audio never duck the radio source**, and per-source gain never applies to it. Audio ducking is a documented core pattern in `CLAUDE.md` § Architecture ("Audio ducking with priority system (1-10 scale)"), so this would be a significant hole, not a cosmetic one.

**First task is confirmation, not repair.** Cheapest check: play SDR radio, fire a TTS event, and observe whether radio volume actually drops; corroborate by dumping the live keys (`SoundFlowPlaybackService` already exposes `_activePlayers.Keys` / component counts around `:734` and `:752`) and comparing them against the `IAudioSource.Id` the `AudioManager` is holding.

**⚠ Trap for the fix, if confirmed: do not paper over it by making `AudioManager` translate to `sdr-radio-*`.** The right answer is **one key per source, agreed by both layers** — the same conclusion AUD-4 reaches from the teardown side. Coordinate the two. _**Every anchor in this row re-verified 2026-08-11 against `main` @ `8b1ce0a` and all are byte-exact and unchanged:** `SDRRadioAudioSource.cs:204`/`:908`/`:956-960`, `SoundFlowPlaybackService.cs:25`/`:423`/`:620`/`:643`/`:734`/`:752`, `AudioManager.cs:121`/`:240`/`:247`/`:292`/`:479`/`:508`, and `AudioSourceBase.cs:28` — the last checked specifically because #468 touched that file (its hunk starts at `:97`).

**The inference this row is built on is therefore intact post-#468 and still unconfirmed** — nothing in #467/#468/#469 tested it either way._
