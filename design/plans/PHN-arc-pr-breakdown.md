# PHN arc - PR breakdown for ADR-029 (D25 = full arc)

**Status:** `[PLAN - 2026-09-02]` - **Decision:** D25 answered **A, the full arc**. No stopgap.
**Source of truth:** `design/decisions/2026-08-03-gv-audio-through-engine.md` (787 lines, 10 decisions).
This document does **not** re-decide anything in the ADR. It sequences it.

## Why a breakdown at all

`PHN-1` is scoped at 1.5-2 weeks and `PHN-2` at 3-4 days. That is not one PR. Worse, the pieces carry
sharply different risk: extending an interface is safe and mechanical, while changing how ducking
arbitrates priority is the first load-bearing use of a subsystem the ADR proves is currently
decorative. Landing those together would bury the change most likely to make the room sound wrong
inside a diff of interface plumbing.

## Ordering constraint that already exists

**O6:** `PHN-1` (the seam) before or with `PHN-2` / `PHN-3`. ADR-029's argument is that
voicemail-through-the-engine and speak-a-text are *one mechanism, not two features* - shipping either
first means building the seam twice, and the second build inherits the first's shortcuts.

## The PRs

| # | Scope | ADR | Risk | Est. |
|---|---|---|---|---|
| **1** | **Core contracts, no behaviour change.** `IEventPlaybackService` + `EventPlaybackRequest` (closed discriminated set, asymmetric arms). Extend `IEventAudioSource` with `Position` / `IsSeekable` / `SeekAsync` / `PauseAsync` / `ResumeAsync`, implemented on `AudioFileEventSource` and the TTS source. | D1, D2, D4 | **Low** - D4 lifts a contract `IPrimaryAudioSource` already declares and `FilePlayerAudioSource` already implements. Not new API design. | 1-2 d |
| **2** | **`GvMediaClient` + bounded disk cache + API-side auth.** New client in `Radio.Infrastructure/External/`, modelled on `PhoneContactLookupService`. Radio.API gains its own `GvMedia` config block and a copy of the auth handler. No endpoint yet. | D3, D8 | **Medium** - Radio.API has *zero* `AddHttpClient` today, so the handler infrastructure is genuinely net-new. Eviction must actually delete. | 2-3 d |
| **3** | **`EventPlaybackService` + `POST /api/audio/events`.** Handle lifecycle and the `playbackId` mapping. | D1, D2, D3 | **Medium** | 2-3 d |
| **4** | **Priority becomes load-bearing.** `DuckingService` raises `DuckingStateChanged` on *every* `StartDuckingAsync`; attended playback stops itself when a source of priority >= 8 starts. | D5 | **HIGH - the one to review hardest.** | 2-3 d |
| **5** | **Server-owned playback state + the three stop conditions.** Broadcast over the existing `/hubs/audio`. Max-duration cap, explicit stop reachable from every route, last-circuit-closed `CircuitHandler`. | D6, D7 | **Medium** | 2-3 d |
| **6** | **`PHN-2`: retire the `<audio>` element.** `VoicemailPlayer.razor` calls the seam. The user-visible change, and the first point at which mute / volume / balance / ducking / Cast routing apply to voicemail. | Feature A | **Medium** | 2-3 d |
| **7** | **Feature C: canned replies.** Added to the arc by D19 - *"a few simple/canned responses will suffice"*, explicitly without an on-screen keyboard. | D9 | **Low-Medium** | 2-3 d |

## Traps the ADR names explicitly - carry these onto the rows

1. **Do NOT use `POST /api/sources/events/{tts,file}` as the template.** `SourcesController.cs:601`
   injects `IDuckingService` and never uses it, so those events **do not duck**. `:651` adds a mixer
   source that is **never removed or disposed** - it leaks per play. `PlayFileEvent` **double-plays**
   (`:719` then `:732` re-enters). Copying it propagates three bugs.

2. **The identity hazard, which will silently half-work.** `AudioFileEventSource` mints **two** ids -
   the public `IAudioSource.Id` (`AudioFileEvent-{guid}`) and an internal `_playbackId`
   (`audio-event-{guid}`) used as the `SoundFlowPlaybackService` key - and they are **not equal**,
   whereas `TTSEventSource` uses `Id` directly. A cancel-by-id API built on the wrong one fails for
   exactly one of the two arms. `EventPlaybackService` must own its own `playbackId` and map it.

3. **Never accept a caller-supplied URL.** Voicemail travels as `(kind, id, duration)`; Radio.API maps
   `GvVoicemail` to `{GvMedia:BaseUrl}/api/gvbridge/voicemail/{id}/audio` from **its own** config. An
   endpoint that fetches an arbitrary URL is an SSRF primitive, and the ADR is explicit that "it's a
   LAN kiosk" is not a defence.

4. **The cache is blackout mitigation, not optimisation.** GV auth is dead ~9 minutes in every 20, so
   a replay has roughly 45% odds of 502ing without it. It also puts private voicemail audio at rest on
   disk where it previously only streamed - so it must be size-bounded, live under `./data/`, and
   eviction must really delete.

5. **No polls, ticks, or per-client timers.** The box is an Intel N100 where CPU churn correlates with
   audible distortion. The ADR disqualifies any such design outright; D6 and D7 are shaped by it.

6. **`DurationSeconds` is a correctness fix, not decoration.** `AudioFileEventSourceFactory` estimates
   duration from file size (MP3 at 16000 B/s) and never decodes, and completion is a wall-clock
   `Task.Delay(_duration)`. A wrong duration means playback that ends early or hangs.

## Verification shape

Unit tests carry PRs 1-5. **PR 6 is the one that needs UAT on the box**, and the check is exactly the
thing the row exists to fix: play a voicemail while the radio is on and confirm the radio **ducks**,
that **mute** silences it, that **master volume** moves it, and that with **Cast active** it goes to
the Cast device rather than the local speakers. Nothing short of that settles Feature A.
