# ADR: GV media as ducked event playback — one seam for voicemail audio and text-to-speech (supersedes ADR-022 D4)

- **ID:** ADR-029 (see `design/DECISION-LOG.md` for the one-line pointer)
- **Status:** Proposed — **Amendment 1 applied 2026-08-03** (owner answers + Designer round). Ready for Planner.
- **Date:** 2026-08-03 (amended same day)
- **Author:** Architect
- **Supersedes:** **ADR-022 Decision D4 in full** (voicemail audio via a native `<audio>` element pointed at `radio:5004`). **Narrowly amends ADR-022 D1** — D1's boundary rule still governs the GV *read* path (Web → `radio:5004` direct); it does **not** govern the GV *audio* path, whose stated premise ("no audio-engine involvement") the owner has now reversed. Everything else in ADR-022, ADR-024 and ADR-028 stands.
- **Scope:** `Radio.Web` + `Radio.API` + the `Radio.Core`/`Radio.Infrastructure` audio layer. RadioConsole still holds **no** Google credentials and never talks to Google; voicemail bytes are fetched from RotaryPhone's `gvbridge` proxy on `radio:5004` exactly as today — only the *fetcher* moves from the browser to Radio.API.
- **Parent ADRs:** [ADR-022](2026-06-20-gvbridge-voicemail-sms-integration.md) (gvbridge consumer), [ADR-024](2026-06-20-gv-mark-read-durable-readstate.md) (durable read-state), [ADR-028](2026-07-30-gv-sms-send-contract.md) (SMS send contract).
- **Drives:** the three-feature arc — **A** voicemail through the real audio chain, **B** speak-a-text, **C** canned responses (Designer-led; §11 records its one architectural consequence).

---

## 0. Amendment 1 — what changed on 2026-08-03, and why

The original ADR was written before the owner answered §14 and before the Designer's handoff landed. Both arrived; **five decisions moved and one was deleted.** Nothing below is a silent rewrite — each amended section carries an inline `⟨A1·n⟩` marker, and the superseded reasoning is kept where it still has explanatory value.

| # | What changed | Driver | Sections |
|---|---|---|---|
| **A1·1** | **D10 is new, and D-speech is reversed.** Message speech is no longer pinned to local `espeak-ng`. It **follows the currently selected TTS engine**, resolved as `TTS:DefaultEngine`. **`GvMedia:SpeechEngine` is deleted, not redefined.** Three engines are in play, not two; engine-unavailable is a stated failure, never a silent substitution. | **Owner:** *"the TTS engine in the radio console supports both Google and Azure TTS, so make sure the text messaging uses the currently selected TTS engine."* | §9 (rewritten), §2, §4, §10.2, §13, §14 Q2 |
| **A1·2** | **Voicemail cache stays on.** §14 Q1 answered: bounded on-disk cache at `./data/gvmedia/` is accepted; voicemail audio at rest is an accepted cost, not an open question. | Owner | §5.3, §14 Q1 |
| **A1·3** | **Initial-sender is out of scope — the console is reply-only.** New-recipient compose is removed rather than redesigned. One real consequence in the send path. | **Owner:** *"Being the initial text sender can remain out of scope for the radio console."* | §11.5 (new), §11.4, §12 item 10 |
| **A1·4** | **D7's navigate-away rule is flipped: playback survives navigation.** The Designer accepted §12 item 6's invitation and supplied the persistent affordance the rule was waiting for. Flipping it forced **three** further changes — the circuit backstop is re-scoped, `OwnerToken` is deleted, and `/sleep` needs its own rule. | Designer handoff §Answers item 6 | §7 (rewritten), §4, §8.1, §2 |
| **A1·5** | **§6.1's priority anchor is replaced.** A live check falsified the anchor: `PhoneIntegration:Enabled` has been `false` since the commit that introduced it and has never been flipped. The number (8) survives; the reasoning that justified it does not. | Live check settling §14 Q5 | §6.1, §14 Q5 |
| **A1·6** | **Two §1.2 facts confirmed and one §3.1 claim corrected.** The binary-ducking / dead-priority finding was independently confirmed and `design/INTEGRATIONS.md:464` now says so in the doc itself. §3.1's caller list was wrong in one particular. | Independent confirmation | §1.2 correction 2, §3.1 |

**What did *not* change, stated so it is not re-litigated:** the one-seam decision (D1), the asymmetric request arms (D2), server-side fetch (D3), the transport lift onto `IEventAudioSource` (D4), the anchor-not-tick broadcast model (D6), the auth-seam closure (D8), and the one-to-one reconciler constraint (D9). The Designer's round touched presentation and lifecycle; it left the seam alone.

---

## 1. Context

The owner has asked for three changes to the `/phone` Google Voice surface. Two of them — **A** (voicemail plays through the console's real output chain, with ducking, not through the browser) and **B** (a text message gets a play button that speaks it via TTS) — look like separate features and are not. Both are *"hand a Google Voice media item to the audio engine as a short, ducked, user-attended event."* The owner gets **one** mechanism; this ADR defines it.

The third, **C** (freeform SMS compose replaced by a small canned-response set), is a Designer-led UX change and is not this ADR's to design. §11 confirms-or-refutes the standing belief that the send plumbing is unaffected, and finds one real consequence that would otherwise have been missed.

### 1.1 Why A is a boundary change, not a bug fix

ADR-022 D4 chose a native `<audio src="http://radio:5004/api/gvbridge/voicemail/{id}/audio">`, and chose it deliberately and well: the gvbridge endpoint serves `Accept-Ranges: bytes`, so the browser got a seekable scrubber for free, and the album-art proxy precedent (`Program.cs` `MapGet("/api/albumart/{filename}")`, which buffers via `ReadAsByteArrayAsync` and forwards no `Range`) was explicitly rejected as disqualifying.

That decision is still *correct on its own terms*. What changed is the requirement. D4's supporting premise, stated in ADR-022 §3 D1, was:

> *"GV voicemail/SMS is pure RotaryPhone state with no audio-engine involvement (the voicemail recording plays in the browser's `<audio>`, not through the SoundFlow pipeline)."*

The owner has reversed that premise. Voicemail is now console audio: it must duck the radio, come out of the console's selected output, and behave like everything else this appliance plays. A browser `<audio>` element can do none of that — it is a second, parallel audio path that the mixer, the ducking service and the output chain know nothing about. On a Chromecast or exclusive-mode output it may not be audible at all.

So D4 is superseded not because it was wrong, but because the thing it optimised for (browser-side Range seeking) is no longer what matters, and what now matters (the real output chain) is something it structurally cannot provide.

### 1.2 What already exists in-tree (verified — this is the load-bearing context)

| Component | File:line | Current state |
|---|---|---|
| Event-source contract | `Radio.Core/Interfaces/Audio/IEventAudioSource.cs:8-33` | **Four members only:** `TimeSpan Duration`, `PlayAsync`, `StopAsync`, `event PlaybackCompleted`. **No `Position`, no `Seek`, no `Pause`/`Resume`.** |
| **Primary-source contract — already has the full transport surface** | `Radio.Core/Interfaces/Audio/IPrimaryAudioSource.cs:9-165` | `TimeSpan? Duration` :14, **`TimeSpan Position` :19**, **`bool IsSeekable` :24**, `PlayAsync` :40, **`PauseAsync` :47**, **`ResumeAsync` :54**, `StopAsync` :61, **`SeekAsync(TimeSpan)` :71**. `FilePlayerAudioSource.cs:117` → `IsSeekable => true`. |
| Ducking contract | `Radio.Core/Interfaces/Audio/IDuckingService.cs:8-81` | `StartDuckingAsync`/`StopDuckingAsync`/`StopAllDuckingAsync`, `GetPriority`/`SetPriority` (1-10), `ActiveEventCount`, `GetActiveEventsByPriority()`, `DuckingStateChanged`, `DuckingLevelChanged`. |
| Ducking implementation | `Radio.Infrastructure/Audio/Services/DuckingService.cs` | `DefaultEventPriority = 8` :30, `DefaultPrimaryPriority = 3` :35. Duck target is the fixed global `AudioOptions.DuckingPercentage` (20). See the correction below. |
| Announcement orchestration | `Radio.Core/Interfaces/Audio/IAnnouncementService.cs` | `AnnounceAsync(message, priority=5, ct)`, `PlaySoundWithAnnouncementAsync(soundPath, message, priority=5, ct)`, `StopAsync(ct)`. **Fire-and-forget: no handle, no id, no position, no state.** `StopAsync` is global and is exposed on **no** HTTP endpoint. |
| Announcement impl | `Radio.Infrastructure/Audio/Services/AnnouncementService.cs:188-197` | Single-slot `_activeSource` under a lock; `SetActiveSource` **cancels the previous CTS** — announcements already replace each other. Per-caller, not system-wide. |
| Audio-file event source | `Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs` | **Two ctors: file path :30-41 and `Stream` :52-65** — both require a caller-supplied `TimeSpan duration`. No seek. Completion is a wall-clock `await Task.Delay(_duration, ct)` :205. |
| Event-source factory | `Radio.Infrastructure/Audio/Services/AudioFileEventSourceFactory.cs` | `CreateFromFileAsync` :45, **`CreateFromStream(name, stream, duration)` :74**. Extensions `{.wav,.mp3,.ogg,.flac}` :139. Duration is **estimated from file size, never decoded** — MP3 at 16000 B/s :203. |
| TTS factory | `Radio.Core/Interfaces/Audio/ITTSFactory.cs` | `CreateAsync(text, TTSParameters?, ct)` → `IEventAudioSource`. `enum TTSEngine { ESpeak=0, Google=1, Azure=2 }` :67-77. `TTSParameters { Engine, Voice, Speed, Pitch }` :82-103. |
| TTS impl | `Radio.Infrastructure/Audio/Services/TTSFactory.cs:106-112` | **`ESpeak` shells out to `espeak-ng`** (:301-373, `--stdout`, stdin-piped text) — **local, offline, no API key.** `Google` :375-461 and `Azure` :463-523 are cloud HTTP. |
| TTS config | `Radio.Core/Configuration/TTSOptions.cs:17` / `Radio.API/appsettings.json:176` | Code default `DefaultEngine = "ESpeak"`; **deployed config overrides it to `"Google"`.** See §9. |
| **Engine resolution — the only site in the tree** ⟨A1·1⟩ | `Radio.Infrastructure/Audio/Services/TTSFactory.cs:71` | `parameters?.Engine ?? ParseEngine(opts.DefaultEngine)`, `opts = IOptionsMonitor<TTSOptions>.CurrentValue` (:68). **`TTSParameters.Engine` is non-nullable with an `ESpeak` initializer** (`ITTSFactory.cs:87`), so the fallback fires only when `parameters` is **entirely null**. §9.3. |
| **The field that looks like the answer and is not** ⟨A1·1⟩ | `Radio.Core/Configuration/TTSPreferences.cs:17` | `LastEngine` — **zero readers, zero writers.** Binds the same `"TTS"` section as `TTSOptions` (:12 == :12), which has no `LastEngine` key, so it is permanently `"ESpeak"`. `PreferencesPersistenceService:99` writes the default back every save period. §9.2. |
| **Azure TTS — fully implemented** ⟨A1·1⟩ | `TTSFactory.cs:463-484`; `TTSSecrets.cs` | REST to `https://{region}.tts.speech.microsoft.com/cognitiveservices/v1`, SSML rate/pitch, key + region from secrets. Gated on **both** `AzureAPIKey` and `AzureRegion` (`:258-259`, `:471-474`). The original draft never mentioned it. §9.4. |
| **Sleep is a route, under a different layout** ⟨A1·4⟩ | `Radio.Web/Components/Pages/Sleep.razor:1-2`; `MainLayout.razor:1045-1057`, `:418-430`, `:1073` | `@page "/sleep"` + **`@layout EmptyLayout`** — no `.topbar-primary`, so no transport chip. Entered by explicit tap, by server-pushed `SleepStateChanged`, **and by `idle-dimmer.js` on a timer.** §7.5. |
| **Radio.API → `radio:5004` REST already exists** | `Radio.Infrastructure/External/PhoneContactLookupService.cs:75-76` | Injected `HttpClient`, base URL from `PhoneIntegrationOptions.ContactsApiBaseUrl`, calls `{base}/api/contacts/lookup`. Masks numbers in logs (`***1234`). Registered at `AudioServiceExtensions.cs:435`. |
| Radio.API phone config | `Radio.API/appsettings.json:256-265` | `PhoneIntegration { Enabled: false, HubUrl: "http://radio:5004/hub", ContactsApiBaseUrl: "http://radio:5004", RingSoundPath, RingPriority: 9, AnnouncementPriority: 8 }` |
| Audio hub (server) | `Radio.API/Hubs/AudioStateHub.cs` → `ApiPaths.Hubs.Audio = "/hubs/audio"` | Broadcasts are emitted by `IHubContext<AudioStateHub>` in `Radio.API/Services/AudioStateUpdateService.cs` — `"PlaybackStateChanged"` :235, `"NowPlayingChanged"` :248, `"VolumeChanged"` :471, etc. |
| Audio hub (client) + state mirror | `Radio.Web/Services/Hub/AudioStateHubService.cs` + `Radio.Web/Services/AudioStateStore.cs` (singleton, `Program.cs:432`) | The **existing, working cross-client audio-state pattern**: one hub connection, a singleton cache, `Func<Task>` change events. Consumed by `MainLayout.razor:305-308`, `NowPlayingDock.razor:117-118`. **Completely unwired from the phone surface.** |
| Voicemail player (to be replaced) | `Radio.Web/Components/Pages/VoicemailPlayer.razor:8` | `<audio src="@AudioSrc" preload="none" @ref="_audioEl">`; `AudioSrc` :87 = `GvBridgeApi.GetVoicemailAudioUrl(Item.Id)`. Transport state is a private per-circuit enum :76. |
| Absolute-URL builder | `Radio.Web/Services/ApiClients/GvBridgeApiService.cs:134-140` | Builds the raw absolute URL string; **never issues a request**, so it never passes through the handler chain. |
| Auth seam | `Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs:11-28` | `DelegatingHandler`, header `X-RotaryPhone-Auth`, key from `RotaryPhone:Gv:AuthKey`. Attached to **five** typed clients (`Program.cs:323,338,356,373,389`). **Key is `""` in both appsettings → OFF.** |

**Four corrections to the assumptions this arc started from. Two of them change the design.**

1. **`gvbridge` genuinely appears nowhere in `src/Radio.API/` — confirmed, zero matches.** So does `AuthKey`/`X-RotaryPhone` — zero matches. **But `radio:5004` does appear**, twice, in `appsettings.json`, and `PhoneContactLookupService` already makes REST calls to that host through an injected `HttpClient`. **Radio.API talking to `radio:5004` is an established topology, not a new one.** That materially lowers the cost of D3.

2. **Priority currently arbitrates *nothing*. Ducking is binary and reference-counted.** This is the single most consequential finding here, and it is stronger than "the ducking service mixes." Read `DuckingService.StartDuckingAsync` (:96-144): the first event to arrive fades the primary to the fixed global `DuckingPercentage` (20); **every subsequent concurrent event changes nothing at all** (:138-143 is a debug log). Volume is restored only when the last event leaves (:170). `GetPriority` is read in exactly one place — inside `GetActiveEventsByPriority` (:267) — and **`GetActiveEventsByPriority` and `StopAllDuckingAsync` have zero non-test callers.** They are dead code. Only the single *primary* source is ducked (`AudioManager.cs:478`, keyed on `_activeSource.Id`); concurrent event sources are never ducked against each other.
   **Therefore `design/INTEGRATIONS.md`'s claim that "higher priority announcements can interrupt lower priority ones" is false today**, and every `SetPriority` call in `AnnouncementService` is currently decorative. **D5 introduces the first load-bearing use of priority in this system.**
   > **⟨A1·6⟩ Independently confirmed, and the doc is now corrected.** `design/INTEGRATIONS.md:464` strikes the old claim in place and records the mechanism, the two dead members, and the fact that the `Priority` field is *"accepted, validated, stored, and then ignored"* — pointing here for the fix. **This ADR's §6 framing and the corrected doc now agree**, so a reader arriving from either lands in the same place. No design change follows from the confirmation; the finding was already load-bearing.

3. **There are already two non-unified event paths, and one of them leaks.** `POST /api/notifications/announce` (ducked, awaits completion, cleans up) and `POST /api/sources/events/{tts,file}` (`SourcesController.cs:601,685`) — the latter injects `IDuckingService` at :44 and **never uses it**, so those events do not duck at all, and the sources added at :651/:716 are never removed, never disposed and never un-ducked. `PlayFileEvent` also double-plays (`:719` calls `PlayFileAsync`, then `:732` calls `PlayAsync` which re-enters it). **A third ad-hoc path would make this worse**; §3.2's one-seam argument is partly an argument against that.

4. **`IPrimaryAudioSource` already declares every transport member D4 needs** — `Position`, `IsSeekable`, `SeekAsync`, `PauseAsync`, `ResumeAsync` — with `FilePlayerAudioSource` implementing seek for local files today. D4 is therefore **not new API design**; it is lifting a proven in-tree contract onto the event interface.

### 1.3 The environment constrains the design

- The box is an **Intel N100** and is resource-constrained; heavy journald/CPU churn correlates with audible audio distortion (recorded repeatedly in `docs/BUILDER_QUEUE.md` and `design/INTEGRATIONS.md`). **Any design that adds a periodic tick, a poll, or a per-client timer is disqualified.** This is the hardest constraint here and it drives D6 and D7.
- **GV auth dies for ~9 minutes out of every 20.** `psidtsAgeSeconds` is the only honest field on `/api/gvbridge/status`; `available`/`cookiesValid`/`degraded` have been observed reporting healthy while both SMS endpoints returned hard 502s. A voicemail fetch has **roughly a 45% chance of landing in a blackout window**. That is not an edge case, and it is the strongest argument for D3's cache (§5.3).

---

## 2. Decisions (summary)

| # | Decision |
|---|----------|
| **D1** | **One seam, in Core: `IEventPlaybackService`.** Both voicemail audio and text-to-speech are submitted as an **`EventPlaybackRequest`** and return a **handle**. Exposed by Radio.API as `POST /api/audio/events`. `Radio.Web` calls **Radio.API**, not gvbridge, for anything audible. |
| **D2** | **The request is a closed discriminated set, and the two arms are deliberately asymmetric:** speech travels as **literal text**; voicemail travels as a **`(kind, id, duration)` reference**. Radio.API never receives a caller-supplied URL. |
| **D3** | **Radio.API resolves and fetches the voicemail itself** via a new `GvMediaClient` in `Radio.Infrastructure/External/` (sibling of `PhoneContactLookupService`), into a **bounded on-disk cache**, then plays it through the existing `AudioFileEventSource`. |
| **D4** | **`IEventAudioSource` gains `Position`, `IsSeekable`, `SeekAsync`, `PauseAsync`, `ResumeAsync` — copied verbatim from `IPrimaryAudioSource`.** Seeking is local to the cached file; **HTTP Range is no longer needed by anyone.** |
| **D5** | **Attended playback is a new exclusivity class at priority 6, and is the first real use of priority in this system.** Implemented by making `DuckingService` raise `DuckingStateChanged` on *every* `StartDuckingAsync` and having the playback service stop itself when a source of priority **≥ 8** starts. |
| **D6** | **Playback state is global, server-owned, and broadcast on the existing `/hubs/audio`** via `AudioStateUpdateService` → `AudioStateHubService` → `AudioStateStore`. Never per-client, never polled. |
| **D7** ⟨A1·4⟩ | **Three independent stop conditions — re-derived after the navigate-away flip:** a hard server-side **max-duration cap**, an explicit stop reachable from **every** route, and a **last-circuit-closed backstop** (net-new `CircuitHandler`). **Playback survives navigation.** The cap is still the only one that *guarantees* the console never gets stuck, and the flip does not weaken it. |
| **D8** | **Radio.API gains its own `GvMedia` config block and its own copy of the auth handler.** This is what **closes carried risk #3** (§10). |
| **D9** | **Canned responses (C) do not change the send contract — but they invalidate a probability assumption inside ADR-028 §4.4's accepted risk.** §11. |
| **D10** ⟨A1·1⟩ | **Message speech follows the currently selected TTS engine — resolved as `TTS:DefaultEngine`, the same value the announcement path already honours.** Not pinned, not overridden, no GV-specific engine key. Three engines (`ESpeak`/`Google`/`Azure`); an unavailable engine **fails the playback with a stated reason** and never silently substitutes another. §9. |

---

## 3. Decision D1 — the shared seam

**Decision: a single Core interface, `IEventPlaybackService`, in `src/Radio.Core/Interfaces/Audio/`, sitting *beside* `IAnnouncementService`, not inside it.**

```csharp
namespace Radio.Core.Interfaces.Audio;

public interface IEventPlaybackService
{
  Task<EventPlaybackSnapshot> StartAsync(EventPlaybackRequest request, CancellationToken ct = default);
  Task<bool> StopAsync(string playbackId, CancellationToken ct = default);
  Task<bool> SeekAsync(string playbackId, TimeSpan position, CancellationToken ct = default);
  Task<bool> PauseAsync(string playbackId, CancellationToken ct = default);
  Task<bool> ResumeAsync(string playbackId, CancellationToken ct = default);

  EventPlaybackSnapshot? Current { get; }
  event EventHandler<EventPlaybackSnapshot>? PlaybackChanged;
}
```

### 3.1 Why not extend `IAnnouncementService`

It is **fire-and-forget by design and by signature**: `AnnounceAsync` returns a bare `Task`, hands back no identity, and `StopAsync()` stops *everything* (and is exposed on no HTTP route — its only caller is `PhoneCallIntegrationService.cs:103`). It has no position, no state, no per-event addressability. Adding a handle, seek, pause and a state broadcast would not be an extension but a replacement that must keep working for its existing fire-and-forget callers.

> **⟨A1·6⟩ Correction — the caller list was wrong, and the corrected list sharpens §6.1.** `IAnnouncementService` has **exactly two injection sites in the whole tree**: `NotificationsController.cs:16,23` and `PhoneCallIntegrationService.cs:21,29`. **`PbapSyncService` does not consume it** (it exists at `Radio.Infrastructure/Bluetooth/PbapSyncService.cs` but never touches announcements), and the Web test form is not a third caller — it POSTs `/api/notifications/announce` (`SystemConfigPage.razor:1697-1702`), i.e. it *is* `NotificationsController`. Two consumers, one of which is dormant. That is the fact §6.1 is re-anchored on ⟨A1·5⟩.

So **`IAnnouncementService` stays exactly as it is** and keeps serving *unattended* announcements. `IEventPlaybackService` serves **attended** playback — the case where a user pressed a button, is listening on purpose, and expects transport controls. That attended/unattended split is the actual architectural insight of this ADR, and §6 and §7.2 both make it load-bearing rather than cosmetic.

Worth noting as precedent: `AnnouncementService` **already** implements "a new one replaces the old one" via its single-slot `_activeSource` + CTS cancel (`AnnouncementService.cs:188-197`). D5 rule 1 is the same idea, scoped to attended playback.

### 3.2 Why one seam and not two

The temptation is to reuse `POST /api/notifications/announce` for B (it already speaks text at a priority) and add a separate URL-player for A. That gives the owner two mechanisms with two lifecycles, two stop paths, two state models, and no way for the UI to express that pressing play on a text should cancel the voicemail currently playing. Both features need identical transport, ducking, multi-client state and stop semantics. They differ **only** in how the audio is acquired, and that difference belongs inside the implementation, not at the API.

It is also worth being blunt about the alternative's track record: **this codebase already has two event paths, and the second one (`/api/sources/events/*`) does not duck, leaks its sources, and double-plays files** (§1.2 correction 3). Adding a third ad-hoc path is exactly how that happened the first time.

Concretely, both arms share a **two-phase lifecycle** neither is free of:

- **Voicemail:** acquire = HTTP fetch from `radio:5004` (seconds; 502 during a blackout).
- **Speech:** acquire = TTS synthesis (a shell-out to `espeak-ng`, or a cloud round-trip on `Google`/`Azure`).

Both therefore need a `Preparing` state before audio starts, and both can fail *before* producing a sound. That shared shape is the strongest evidence they are one mechanism. It is also a **hard Designer input** — §12.

### 3.3 What Radio.Web calls

```
POST   /api/audio/events             → 202 EventPlaybackSnapshot
DELETE /api/audio/events/{id}        → 204 | 404
POST   /api/audio/events/{id}/seek   → 200 EventPlaybackSnapshot   { positionSeconds }
POST   /api/audio/events/{id}/pause  → 200 EventPlaybackSnapshot
POST   /api/audio/events/{id}/resume → 200 EventPlaybackSnapshot
GET    /api/audio/events/current     → 200 EventPlaybackSnapshot | 204
```

A new `EventPlaybackApiService` in `src/Radio.Web/Services/ApiClients/` calls these, registered in the **Radio.API** client family (`Program.cs:91-307`, `ApiConnectionLoggingHandler` only) — *not* the `RotaryPhone:ApiBaseUrl` family. `GvBridgeApiService.GetVoicemailAudioUrl` loses its only caller (`VoicemailPlayer.razor:87`) and should be **deleted**, along with `wwwroot/js/voicemail-player.js` and the `<audio>` element.

**Identity hazard the Planner must not step on:** `AudioFileEventSource` mints **two** ids — the public `IAudioSource.Id` (`AudioFileEvent-{guid}`, `AudioSourceBase.cs:28`) and an internal `_playbackId` (`audio-event-{guid}`, `AudioFileEventSource.cs:112`) used as the `SoundFlowPlaybackService` key — and they are **not equal**, whereas `TTSEventSource` uses `Id` directly as its playback key (`TTSEventSource.cs:145`). A cancel-by-id API built on the wrong one silently fails for one of the two arms. `EventPlaybackService` must own its own `playbackId` and map it to the source, not assume the two ids coincide.

---

## 4. Decision D2 — the request shape, and why the arms are asymmetric

```csharp
public enum EventPlaybackKind { Speech, RemoteMedia }
public enum RemoteMediaKind   { GvVoicemail }          // closed set; exactly one member today

public sealed record EventPlaybackRequest
{
  public required EventPlaybackKind Kind { get; init; }

  // Kind == Speech
  public string? Text { get; init; }                    // the literal utterance
  public string? VoiceId { get; init; }                 // null → TTSOptions.DefaultVoice  ⟨A1·1⟩
  public string? Engine { get; init; }                  // null → TTS:DefaultEngine (§9). Per-request
                                                        // override only; Radio.Web sends null.  ⟨A1·1⟩

  // Kind == RemoteMedia
  public RemoteMediaKind? MediaKind { get; init; }
  public string? MediaId { get; init; }                 // GV voicemail id — NEVER a URL
  public int? DurationSeconds { get; init; }            // from VoicemailItemDto; see §4.1

  public string? Label { get; init; }                   // "Voicemail from Jane" — display only
  public int Priority { get; init; } = 6;               // §6
}
```

> **⟨A1·4⟩ `OwnerToken` is deleted from the request.** It existed for exactly one purpose — §7.4's rule that *the initiating circuit navigating away stops the audio*. The Designer's persistent transport chip removes that rule (§7), the re-scoped backstop is a circuit **count** rather than a circuit **identity** (§7.3), and the stop endpoint was never ownership-checked in the first place. Nothing consumes it. A field whose only consumer was a deleted rule is the kind of vestigial state that misleads the next reader into believing an ownership model exists; **there is no ownership model — there is one set of speakers and one global playback.** If telemetry later wants provenance, that is a log line, not a request field.

**The asymmetry is deliberate.** Speech passes **the text itself**; voicemail passes **an identifier**. That follows from where the data already is and how big it is:

- The SMS body is **already in `Radio.Web`'s hands** (it arrived as `SmsMessageDto.Text`, `ApiModels.cs:1143`), is small, and Radio.API has no business acquiring SMS content — that would drag the whole gvbridge SMS read contract into the audio layer for no gain.
- The voicemail recording is **large, remote, and in nobody's hands yet**. Passing an id lets the fetch happen once, server-side, where it can be cached and authenticated.

**Radio.API never accepts a caller-supplied URL.** An endpoint that fetches an arbitrary URL on request is a server-side-request-forgery primitive, and "it's a LAN kiosk" is not a reason to build one. `MediaKind` is a closed enum; Radio.API maps `GvVoicemail` → `{GvMedia:BaseUrl}/api/gvbridge/voicemail/{id}/audio` from **its own** configuration. The URL never crosses the Web→API wire.

### 4.1 `DurationSeconds` is not optional decoration — it is a correctness fix

`AudioFileEventSource` **requires a caller-supplied duration** in both constructors, and the factory that would otherwise supply it **estimates duration from file size and never decodes** — MP3 at a flat `16000 B/s` (`AudioFileEventSourceFactory.cs:203`). Worse, `AudioFileEventSource` detects completion by **wall-clock `Task.Delay(_duration)`** (`:205`), not by an end-of-stream event. A bad duration therefore produces both a wrong progress bar *and* a wrong completion time.

We have the authoritative value: **`VoicemailItemDto.DurationSeconds`** (`ApiModels.cs:1128`), which gvbridge supplies. **Pass it through and use it.** Per ADR-022 §4.2, `DurationSeconds == 0` means *unknown* — in that case fall back to the factory estimate and mark the snapshot's `Duration` as null so the UI renders an indeterminate bar rather than a lie (§12 item 1).

### 4.2 Utterance composition belongs to `Radio.Web`

Radio.API receives a finished string and speaks it. Whether the utterance is `"Message from Jane: on my way"` or the bare body is **copy, and therefore Designer's** (§12). Two architectural constraints on whatever Designer chooses:

- **Cap it.** `GvMedia:MaxSpeechChars` (default **1000**), truncated with a spoken tail. An 8 000-character MMS blob must not hold the speakers for six minutes.
- **Normalise before speaking.** A raw SMS read aloud includes URLs character by character, emoji names and shortcode noise. A small **pure static helper in `Radio.Web`** — `GvSpeechText.ForMessage(SmsMessageDto)` — should collapse URLs to "a link", drop emoji and normalise whitespace. This follows the in-tree precedent of `GvDirection` (`ApiModels.cs:1192-1199`) and, in ADR-028 §8.3, the planned `GvCounterparty`: *classify and normalise client-side in a pure, unit-testable static rather than growing the cross-service DTO.* The same reasoning applies verbatim.

---

## 5. Decision D3 — Radio.API fetches, caches, and plays

**Decision: a new `GvMediaClient` in `src/Radio.Infrastructure/External/`, modelled directly on `PhoneContactLookupService`.**

```csharp
public sealed class GvMediaClient
{
  public GvMediaClient(ILogger<GvMediaClient> logger,
                       IOptionsMonitor<GvMediaOptions> options,
                       HttpClient httpClient);

  /// Returns a local cached path, fetching on miss. Throws GvMediaUnavailableException on 5xx/timeout.
  public Task<string> GetVoicemailFileAsync(string voicemailId, CancellationToken ct = default);
}
```

Registered beside its sibling in `Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs` (`PhoneContactLookupService` is registered at `:435`).

### 5.1 Why this is a small change, not a topology change

`PhoneContactLookupService` is already an `HttpClient` in `Radio.Infrastructure/External/` that reads a `radio:5004` base URL from options and calls a RotaryPhone REST route. `GvMediaClient` is the same shape, in the same folder, against the same host. Radio.API does not "learn about Google Voice" — it learns one URL template and one id. Follow that file's conventions, **including its log-masking discipline** (it masks numbers to `***1234`); voicemail ids and caller identity must be masked the same way.

**One genuine gap:** Radio.API registers **no `HttpClient` of its own** and has **no `DelegatingHandler` infrastructure at all** (zero `AddHttpClient`/`IHttpClientFactory` matches under `src/Radio.API/`). `RotaryPhoneAuthHandler` lives in `Radio.Web`. D8 requires an equivalent on the API side — either extract the handler to a shared location or add a small copy. This is real work, and it is the price of closing the auth seam (§10.1).

### 5.2 Materialise to a file, then play as an ordinary file event

Once the bytes are on local disk, a voicemail is *just another file event*: `AudioSourceType.AudioFileEvent` already exists, `AudioFileEventSource` already takes a path (`:30-41`) **or a `Stream`** (`:52-65`), the factory already exposes `CreateFromStream` (`:74`), and `SoundFlowPlaybackService.PlayFileAsync` handles local paths. **No new source type, no streaming decoder, no new SoundFlow component.** This is the largest simplification available here and it is why D4's seek becomes cheap.

Prefer the **path** constructor over the stream one: `SoundFlow.Providers.StreamDataProvider` is constructed directly over whatever stream it is given (`SoundFlowPlaybackService.cs:269`), and a non-seekable stream is untested on that path — while a local file is the well-trodden case and is what makes `SeekAsync` implementable.

Avoid the `SourcesController.PlayFileEvent` shape while doing it: that endpoint calls `PlayFileAsync` at `:719` **and** `PlayAsync` at `:732`, which re-enters `PlayFileAsync` — a double-play the new controller must not copy.

### 5.3 The cache is not an optimisation — it is the blackout mitigation

Fetch into a bounded LRU cache at **`./data/gvmedia/`** (sibling of the existing `./data/albumart/`), keyed by voicemail id, default cap **`GvMedia:CacheMaxMegabytes = 50`**.

Sizing: GV voicemails are MP3 and typically under 3 minutes; at ~64 kbps that is ~480 KB/minute, so ~1.4 MB for a 3-minute worst case. A 50 MB cap holds **~35-100 recordings** — comfortably the entire visible list.

The reason this matters is not disk economy:

> **Google Voice auth is dead ~9 minutes out of every 20.** A user who plays a voicemail and replays it 30 seconds later has a ~45% chance the second fetch would 502 if it went back to the network. With the cache, replay is a local file read and **always works**. Without it, playback would appear to fail at random on a wall clock the user cannot see — the exact symptom `design/INTEGRATIONS.md` warns makes test results "look random."

**Cost, stated plainly:** private voicemail audio now sits at rest on the box's disk, where previously it only ever streamed through a browser. The cache must be size-bounded, must live under the existing `./data/` tree, and eviction must actually delete.

> **⟨A1·2⟩ Owner decision: the cache is ENABLED. §14 Q1 is closed.** Voicemail audio at rest under `./data/gvmedia/`, bounded and LRU-evicted, is **accepted**. `CacheMaxMegabytes = 0` remains a supported configuration — it is the escape hatch, not the default — and choosing it re-exposes replay to the 9-in-20-minute blackout. Planner should treat cache-on as the shipping configuration and test the eviction path (a `0` cap must be a no-cache path, not an infinitely-evicting one).

---

## 6. Decision D5 — priority, and the attended-exclusivity rule

### 6.1 The numbers ⟨A1·5 — re-anchored; the number survived, its justification did not⟩

**The original anchor was falsified by a live check, and this section is rewritten rather than patched.** The first draft justified priority 6 by where it sat relative to the phone ring (9) and the caller-ID announcement (8). Those two have **no live occupant**:

> `PhoneIntegration:Enabled` is `false` at `src/Radio.API/appsettings.json:257`, is present in **no other** appsettings file (no `appsettings.Production.json` override), and is set by **no** `Environment=` line in either systemd unit (`deploy/common/radio-api.service`, `deploy/common/radio-web.service`, `deploy/debian-x64/setup.sh`). `git log -S'"PhoneIntegration"'` surfaces the block's introducing commit **`8d2a2ab`** and nothing that ever changed the flag. The effective value is `false`, and the honest description is **"never enabled"** — not "was on and drifted off." `PhoneCallIntegrationService` is one of only two `IAnnouncementService` consumers in the tree (§3.1 ⟨A1·6⟩) and it is the dormant one.

So the table has to distinguish what is *configured* from what can actually make a sound on this box:

| Priority | Occupant | Live? | Source |
|---|---|---|---|
| 9 | incoming-call ring | **dormant** — `PhoneIntegration:Enabled = false` | `PhoneIntegrationOptions.cs:28`, `appsettings.json:261` |
| 8 | caller-ID announcement | **dormant** — same flag | `PhoneIntegrationOptions.cs:31` |
| 8 | **`/api/notifications/announce` default** — `Math.Clamp(request.Priority ?? 8, 1, 10)` | **LIVE** — the external-event endpoint (Home Assistant, doorbell, laundry), documented in `design/INTEGRATIONS.md:385-456` and reachable from System Config's test form | `NotificationsController.cs:46` |
| 8 | **`DuckingService.DefaultEventPriority`** — the priority of any event that never called `SetPriority`, which is *every* event from `/api/sources/events/{tts,file}` | **LIVE (structural)** | `DuckingService.cs:30` |
| 5 | `IAnnouncementService.AnnounceAsync` default parameter | live only if a caller omits the argument; neither of the two callers does | `IAnnouncementService.cs:15` |
| 3 | `DuckingService.DefaultPrimaryPriority` | live | `DuckingService.cs:35` |

**Decision: attended GV playback — both voicemail and text-TTS — registers at priority 6. `GvMedia:PreemptAtPriority` stays 8.**

Both features get the **same** priority. They are the same class of thing, initiated the same way, by the same user, on the same screen; different numbers would imply an ordering with no meaning.

**The re-anchored reasoning — two live code facts, neither of which involves the phone service:**

1. **8 is this system's own definition of "an event that did not need to state its importance."** `DuckingService.DefaultEventPriority = 8` (`:30`) and `NotificationsController`'s `?? 8` (`:46`) both land there independently. Putting attended playback at 6 therefore means *anything that did not explicitly claim a rank still outranks a user listening to a recording* — the safe direction, and true whether or not phone integration ever ships.
2. **For speech over speech, stopping is strictly better than mixing, so preemption is the correct treatment at that boundary.** An announcement that talks across a voicemail leaves *both* unintelligible and the user replays anyway; an announcement that stops it leaves the announcement intelligible and the voicemail replayable at **zero cost** — it is a cached local file (§5.3). That argument is about the audio, not about who emitted it.

**The live consequence, stated plainly because it is new and the owner should see it:** with the phone service dormant, the *only* thing on this box that can preempt attended playback today is an external announcement posted to `/api/notifications/announce` at its default priority 8. A doorbell or laundry notification will **stop** a voicemail mid-play. The dormant ring/announcement at 9/8 are now **forward compatibility** — if the flag is ever flipped the rule already does the right thing — rather than the justification.

**Two honest caveats on that consequence:**

- **There are only two behaviours available, "stop" and "talk over" — there is no "wait your turn."** Sending priority 7 from a Home Assistant automation does not queue it behind the voicemail; it makes it *mix* (§6.2 rule 3). So the per-caller lever is real but blunt, and the global lever is `GvMedia:PreemptAtPriority`.
- **The one argument for 7 instead of 8**, recorded so it is a decision and not an oversight: `INTEGRATIONS.md:466-472` documents 7-8 as *"high importance (doorbell, notifications)"* and 9-10 as *"critical (phone calls, alarms)"*. A threshold of 7 would map the whole documented high-importance band onto preemption and leave only the 1-6 informational band mixing. **Rejected for now** — the live difference is nil (nothing in-tree emits 7), and 8 is anchored on two facts in *code* rather than on a guidance table in a doc. If the doorbell case proves annoying in practice, `PreemptAtPriority = 7` is a one-key change; that is why the key exists.

### 6.2 Priority alone does nothing — this decision has to build the mechanism

Per §1.2 correction 2, **priority currently arbitrates nothing**: ducking is binary, reference-counted, and pinned to a fixed global percentage. Concurrent events mix, and for attended speech that means two voices at once and neither intelligible.

**Decision: `EventPlaybackService` enforces three rules the ducking service does not.**

1. **Attended vs attended → replace.** Starting a new attended playback **stops the in-flight one first**. Pressing play on a text while a voicemail plays switches to the text. The user just expressed a fresh intent; queueing behind 40 seconds of voicemail would be baffling and mixing would be unintelligible. (Same shape as `AnnouncementService`'s existing single-slot cancel.)
2. **Priority ≥ `GvMedia:PreemptAtPriority` (default 8) → preempt.** A ring (9) or caller announcement (8) **stops** attended playback outright. It does **not** pause-and-resume: resuming a voicemail mid-word 20 seconds after a phone call is worse than restarting, and the recording is replayable at zero cost. The UI returns to an idle, replayable state (§12 item 4).
3. **Priority < 8 → unchanged.** Sub-8 events keep mixing, exactly as they do today over TTS announcements. **This ADR does not fix that.** It is pre-existing, fixing it means introducing a queue across every caller of `IAnnouncementService`, and that is separate work with its own risk. Recorded so the next reader does not mistake it for an oversight: *a Home Assistant announcement at priority 5 will talk over a voicemail.* If that proves annoying, the fix is a queue in `IEventPlaybackService`, not a priority tweak.

### 6.3 How rule 2 is actually implemented — the one required engine change

`AnnouncementService` and `EventPlaybackService` are separate services with separate slots, so preemption needs a signal between them. The cheapest correct one already exists in shape:

`AnnouncementService` (and any other event path) already calls `SetPriority(source, priority)` then `StartDuckingAsync(source)`, and `DuckingStateChangedEventArgs` already carries **`TriggeringSource`**. So `EventPlaybackService` can subscribe to `IDuckingService.DuckingStateChanged`, call `GetPriority(e.TriggeringSource)`, and stop itself when a higher-priority source starts — **with zero changes to `AnnouncementService` or any of its callers.**

**One small engine change makes it work:** today `DuckingStateChanged` fires only on the *transition* into ducking (`DuckingService.cs:108`, `needsTransition = !_isDucking`); a second event arriving while already ducking hits the debug-log branch at `:138-143` and raises nothing. **`StartDuckingAsync` must raise `DuckingStateChanged` on every call**, not only on transition.

This is safe: the only existing subscriber is `AudioManager.OnDuckingStateChanged` (`:490-515`), which acts **only** on `!e.IsDucking` (calling `ClearDuckingMultiplier` at `:508`). Additional events carrying `IsDucking: true` are a no-op for it.

---

## 7. Decision D7 — lifecycle, and the thing that actually stops it ⟨A1·4 — rewritten⟩

> **Amendment note.** The Designer accepted §12 item 6's invitation and built the affordance the navigate-away rule was waiting for: a **persistent console-playback chip in `.topbar-primary`**, present on every route, whose tap is Stop (handoff §Cross-3). **The rule flips: playback survives navigation.**
>
> Flipping it was not a one-line edit, because the old rule was load-bearing in three other places. Working through them is the substance of this rewrite:
> - **§7.1 is untouched and undiminished** — the cap never depended on a client, so the flip cannot weaken it. It is still the guarantee. Restated below with the reasoning made explicit, because it is now carrying more of the weight.
> - **§7.3 must change or it becomes a bug** — an owner-circuit teardown that unconditionally stops playback introduces a *new* failure the old rule masked.
> - **§7.4's ownership model is deleted entirely**, and `OwnerToken` with it (§4).
> - **§7.5 is new** — one route on this box has no topbar, and the console navigates itself there on an idle timer.
>
> The original framing below is kept verbatim because it is still exactly why any of this is needed.

This is the sharpest consequence of moving playback server-side, and it deserves to be blunt:

> Today, playback dies with the `<audio>` element. Close the tab and the sound stops — the browser guarantees it, for free. **Server-side there is no such guarantee, and nothing in the current architecture would ever stop the audio.** A user who taps play on a 3-minute voicemail and then closes the kiosk browser leaves the console talking to an empty room with no visible way to stop it.

Unacceptable on an appliance, so it gets three independent defences, in descending order of trustworthiness.

### 7.1 A hard max-duration cap — the one that actually works

`GvMedia:MaxPlaybackSeconds`, default **300**. On start, arm a `CancellationTokenSource.CancelAfter(...)`; on fire, stop and dispose the event regardless of client state.

This is **the** guarantee. No client cooperation, no heartbeat, no timer loop, no polling — one timer per in-flight event, and D5 rule 1 guarantees at most one in-flight attended event. It satisfies §1.3 completely. Everything below is a latency improvement on this backstop, not a substitute.

> **⟨A1·4⟩ Does the cap still guarantee the console cannot get stuck, now that playback survives navigation? Yes — and the flip does not touch it.** Spelled out, because this is the question the flip most obviously raises:
> - The cap is **armed server-side at `StartAsync`**, on the same object that owns the playback. It has no dependency on a circuit, a browser, a route, a hub connection or a navigation event. Nothing the client does — navigating, refreshing, closing, sleeping, losing the network — can extend it or disarm it.
> - **D5 rule 1 bounds the count**: starting an attended playback stops the in-flight one, so there is at most one armed timer at a time. The worst case is one event, capped.
> - What the flip *does* change is the cap's **role**, not its strength: it goes from "a backstop nobody should ever reach" to "the outer bound on how long audio can play with no client attached." The exposure is `min(MaxPlaybackSeconds, item duration)` — in practice a GV voicemail under 3 minutes, or a ≤1000-character utterance (§4.2) which is well under a minute.
> - **300 stays.** Lowering it to shrink the new exposure would truncate legitimate long voicemails to solve a case §7.3 already covers in seconds rather than minutes.

### 7.2 Explicit stop — the normal path, and it is now reachable from everywhere ⟨A1·4⟩

**Decision (amended): navigating away does NOT stop attended playback.** The transport's stop button remains the normal path, and it is joined by the Designer's persistent chip in `.topbar-primary` — one tap, no confirm, present on every route under `MainLayout`. **`VoicemailPlayer.DisposeAsync` must not stop playback**; component disposal is a rendering event, not a user intent.

The original rule was *"an attended event is bound to the surface that offers its only transport controls."* That premise — *only* — is what the chip retires. The underlying principle is unchanged and is worth stating in its durable form, because §7.5 depends on it:

> **Attended playback may not exist on a surface that offers no way to stop it.**

The old rule was one way to satisfy that principle (remove the playback); the chip is a better one (supply the control). §3.1's attended/unattended split still stands and is still load-bearing — but it is now about *ducking policy, exclusivity and transport*, not about lifetime. A primary source and an attended event now agree that navigation is not a stop signal; they still differ in everything else (§13's rejection of modelling voicemail as a primary source is unaffected — that was about mutual exclusion evicting the user's radio station, not about navigation).

### 7.3 Circuit teardown — re-scoped from "owner circuit" to "last circuit" ⟨A1·4⟩

`Radio.Web` has **no `CircuitHandler` today** (verified: zero matches across `src/`). One must be added — net-new, small, but real.

**The original design was: `OnCircuitClosedAsync` stops any attended playback whose `OwnerToken` matches the departing circuit. Under the flip that is no longer merely weak — it is wrong**, and the old navigate-away rule was masking it:

> Circuit A starts a voicemail. The kiosk browser refreshes (or the circuit drops and reconnects as a new one — routine on a wall panel). Circuit B is now displaying the chip and the live transport, correctly, because state is global (D6). Roughly three minutes later circuit A finally times out of Blazor's disconnect retention window, the handler fires, and **audio the user is actively watching stops for no visible reason.** Under the old rule this never surfaced, because playback would already have been stopped by the navigation.

**Decision: the backstop fires on "no circuits remain," not on "the initiating circuit left."** The `CircuitHandler` being added anyway maintains an O(1) live-circuit count across `OnCircuitOpenedAsync`/`OnCircuitClosedAsync`; when it reaches **zero**, stop any attended playback. This preserves the original intent exactly — *audio is playing and no client is watching* — while surviving refreshes, reconnects and multiple browsers.

**This does not violate §1.3.** The rejected "tracked client set" of the original §7.4 was rejected for producing a confusing *policy* (keep playing while any client has `/phone` open, including a phone asleep in another room). A counter is not that: it is an integer incremented in a handler that already exists for this purpose — **no timer, no poll, no per-client state, no message on the wire.**

**Be honest about its limits, unchanged:** Blazor Server does not tear a circuit down at tab close but after the disconnect retention window (default ~3 minutes), so this is a minutes-latency mechanism. For a short voicemail the recording will simply have finished first. **§7.1 remains the guarantee.** The acceptable alternative — drop §7.3 entirely and rely on the cap — is recorded as viable but worse: it trades an integer for up to five minutes of audio in an empty house.

### 7.4 Multiple clients — there is no ownership model, and that is the decision ⟨A1·4⟩

**The ownership model is deleted.** Every client on the surface is equal: all of them see the same playback (D6), any of them may stop it (the stop endpoint was never ownership-checked), and none of them can end it by leaving. There is one audio engine and one set of speakers, so there is one playback and no owner. `OwnerToken` is removed from the request (§4).

This is *simpler* than what it replaces, and the flip is what made it available: the old §7.4 needed an initiator because the initiator's navigation was a stop trigger. With no such trigger, the concept has no work left to do.

### 7.5 ⚠ One route on this box has no topbar — and the console navigates itself there ⟨A1·4 — new⟩

The flip rests on the Designer's premise that the chip lives in `.topbar-primary`, *"which is on every route."* **That is true of every route under `MainLayout`, and `/sleep` is not one of them:**

- `src/Radio.Web/Components/Pages/Sleep.razor:1-2` declares `@page "/sleep"` and **`@layout Radio.Web.Components.Layout.EmptyLayout`**. `MainLayout`'s `.topbar-primary` (`MainLayout.razor:33`) does not render there at all — so neither does the chip.
- `/sleep` is **not** reached only by a deliberate tap. `MainLayout.OnSleepStateChanged` navigates to it on a **server-pushed** `SleepStateChanged` (`:1045-1057`), and `idle-dimmer.js` drives the same path through `OnJsSleepRequested` (`:418-430`). **The console can put itself into a layout with no stop control, on an idle timer, while a voicemail is playing.**

That is precisely the condition §7.2's principle forbids, arriving with no user action, on a wall panel in a dark room — the exact scenario D7 exists for. Note the old rule covered it by accident: sleep is a real route (`:1062`), so entering it *was* a navigate-away and *did* stop the audio. The flip removes that coverage silently. It must not be left removed.

**Decision: entering `/sleep` stops attended playback.** This is not a reinstatement of the navigate-away rule; it is the durable principle applied to the one surface that fails it — *stop when entering a surface that offers no way to stop.* It is also consistent with what sleep already does: `HandleSleepButtonAsync` calls `SystemApi.SetSleepAsync(true)` **before** navigating (`:1066`), so quieting the room on the way into sleep is the established behaviour, not a new idea.

**The relaxation is available and belongs to the sleep arc, not to me.** If the sleep surface grows its own stop affordance, the principle is satisfied and this rule can go. That is a Designer/sleep-arc decision — the handoff explicitly declines to set sleep semantics (its **Q9**), and this ADR does not set them either. What it does is remove the option of leaving the question unanswered: **either sleep stops attended playback, or the sleep surface gains a control.** Silence resolves to "stop," because that is the safe direction. Raised as §14 Q8.

---

## 8. Decision D6 — multi-client state, and Decision D4 — seek

### 8.1 State is global, because the speakers are

There is one audio engine and one set of speakers, so playback state is **inherently global**; modelling it per-client would be modelling a fiction. Two browsers on `/phone` see the same thing, and either can stop it.

**This is not new machinery — it is the pattern already working next door.** `AudioStateUpdateService` broadcasts via `IHubContext<AudioStateHub>` on `/hubs/audio`; `AudioStateHubService` (one singleton SignalR client in Web) receives; `AudioStateStore` (singleton, `Program.cs:432`) caches and raises change events; `MainLayout` and `NowPlayingDock` consume. **The phone surface is simply unwired from it** — every `VoicemailPlayer` today holds private per-circuit transport state (`VoicemailPlayer.razor:76`), which is precisely why two browsers would play the same voicemail independently and neither would know.

The delta is therefore small and concrete:
1. Add an `"EventPlaybackChanged"` broadcast to `AudioStateUpdateService` (alongside `"PlaybackStateChanged"` at `:235`).
2. Register `On<EventPlaybackSnapshot>("EventPlaybackChanged", …)` in `AudioStateHubService` (alongside the twelve existing registrations at `:114-236`).
3. Cache the latest snapshot on `AudioStateStore` and expose a change event.

One hub connection between Web and API regardless of browser count, and **zero polling**. (Note `PhonePage` already runs a 5-second poll at `:263-265` for other reasons; this adds nothing to it.)

> **⟨A1·4⟩ The flip promotes `GET /api/audio/events/current` from a convenience to a load-bearing path, and adds one requirement to `AudioStateStore`.** Under the old rule playback could not outlive the surface, so nothing ever needed to *discover* an in-flight playback — a client either started it or there was none. Now a fresh circuit routinely arrives mid-playback: the user navigated to `/` and back, or the kiosk refreshed, or a second browser opened. Two things follow:
> 1. **`AudioStateStore` must be seeded from `/api/audio/events/current` when it first has no snapshot** — not only fed by hub broadcasts. Broadcasts fire on *transitions*, and a client that connects between transitions would otherwise render "nothing is playing" while the room is talking.
> 2. **The chip must be correct on first paint**, before any broadcast arrives — which is the same requirement stated from the topbar's side. This is a one-shot fetch per store initialisation, not a poll; the anchor model (§8.2) makes the fetched snapshot immediately sufficient to interpolate from.
>
> This is the mechanism behind the handoff's *"Returning to `/phone` re-attaches"* (§Cross-4). Planner: it is a real requirement, not an implied one.

**A prerequisite worth flagging:** there is currently **no** event/ducking broadcast of any kind — no `"DuckingStateChanged"`, no `"EventStarted"`. Ducking state reaches the UI only by polling `GET /api/audio`, which embeds `DuckingStateDto` (`AudioController.cs:74-79`). So this is the first push channel for event audio.

### 8.2 The snapshot, and the deliberate absence of a position tick

```csharp
public sealed record EventPlaybackSnapshot(
  string             Id,
  EventPlaybackKind  Kind,
  string?            Label,
  EventPlaybackState State,      // Preparing | Playing | Paused | Completed | Stopped | Failed
  TimeSpan?          Duration,   // null while Preparing, or when DurationSeconds == 0 (§4.1)
  TimeSpan           PositionAtBroadcast,
  DateTimeOffset     BroadcastAtUtc,
  string?            FailureReason);
```

**Decision: no periodic position broadcast. None — not at 10 Hz, not at 1 Hz.**

A position tick puts a timer on the server and a message on the wire for every open client, continuously, for the whole duration — on a box where churn is audible (§1.3). Instead the snapshot carries an **anchor** (`PositionAtBroadcast` + `BroadcastAtUtc` + `State`) and **the client interpolates locally**: while `State == Playing`, the progress bar advances from the anchor using the client's own clock (a CSS transition or local rAF), and every state transition re-anchors it.

This is strictly better than ticking: smoother (60 fps locally vs 10 Hz over the wire), free at steady state, and self-correcting at every transition. Drift over a 60-second voicemail is bounded by clock skew between two processes on the same box and is not perceptible on a progress bar.

### 8.3 D4 — seek is achievable, and HTTP Range is now irrelevant

ADR-022 D4 picked native `<audio>` *for* Range seeking. That reasoning does not carry over, and does not need to:

> **Because D3 materialises the recording to a local file before playing it, seeking is a local file operation. HTTP Range is needed by nothing. The capability ADR-022 went out of its way to preserve is preserved — by a different mechanism, on the correct side of the wire.**

The contract cost is real but **much smaller than it first appears, because the members already exist in-tree**: `IPrimaryAudioSource` declares `Position` (:19), `IsSeekable` (:24), `SeekAsync(TimeSpan)` (:71), `PauseAsync` (:47) and `ResumeAsync` (:54), and `FilePlayerAudioSource` (`:117`, `IsSeekable => true`) already implements seeking over a local file through `SoundFlowPlaybackService`. **D4 copies those five member signatures verbatim onto `IEventAudioSource` and lifts `FilePlayerAudioSource`'s implementation into `AudioFileEventSource`.** No new API is designed.

Two implementers exist (`AudioFileEventSource`, `TTSEventSource`), so the blast radius is two files. `TTSEventSource` returns `IsSeekable => false` and no-ops `SeekAsync` — seeking inside a spoken text message has no user value, and `AudioSourceState` already contains `Paused` so the state model needs no change.

**Latency budget** — this is what Designer needs, as a number:

| Hop | Cost |
|---|---|
| Browser → Blazor circuit (SignalR, same box) | ~5-20 ms |
| `Radio.Web` → `Radio.API` HTTP (same box) | ~1-5 ms |
| Decoder reposition in a small local MP3 | ~1-10 ms |
| Output device buffer already in flight | ~20-50 ms |
| **Total perceived seek latency** | **~30-85 ms** |

Under 100 ms reads as responsive for a discrete action. It is nowhere near a 16 ms frame budget for continuous scrubbing. §12 item 1 states the consequence unambiguously.

---

## 9. Decision D10 — speech follows the selected engine ⟨A1·1 — REVERSED and rewritten⟩

> **This section previously pinned message speech to local `espeak-ng` and introduced `GvMedia:SpeechEngine` as the escape hatch. The owner's instruction supersedes it:**
>
> > *"the TTS engine in the radio console supports both Google and Azure TTS, so make sure the text messaging uses the currently selected TTS engine."*
>
> Message speech is **not pinned to any engine**. It **follows the current selection**, and the key that would have let it diverge is **deleted rather than redefined**. The privacy analysis below is not erased — §9.1 keeps it, because it is what caused the owner to make this choice explicitly, which was its whole purpose.

### 9.1 The privacy analysis, preserved as an accepted owner-made trade

The original finding stands on the facts and is worth keeping in full, because a future reader must be able to see a **decision** here, not an oversight:

> `TTSOptions.cs:17` defaults `DefaultEngine` to **`ESpeak`**, but `Radio.API/appsettings.json:176` **overrides it to `"Google"`**. So as deployed, feature B transmits the body of the owner's private text messages to Google's Text-to-Speech API on every play. That sits beside an integration whose stated posture — in ADR-022, ADR-024 and ADR-028 alike — is *"RadioConsole holds no Google credentials and never talks to Google."* It also adds a failure mode voicemail does not have: **speaking a text requires internet and fails when it is down**, whereas a cached voicemail plays fine offline.

The original decision's *reasoning* — that sending private SMS bodies to a third party should be an **explicit owner choice, not an inherited config default** — was correct, and it worked: it put the question in front of the owner, and the owner answered it.

**The answer is: use the selected engine.** Recorded as an accepted, deliberate trade:

- **Accepted:** with `TTS:DefaultEngine = "Google"` as deployed, private SMS bodies reach Google's TTS API on each play. If the selection is changed to Azure, they reach Microsoft's instead. This is now a chosen exposure, reviewable and reversible in one place.
- **Still true, and still worth writing down:** the *GV* posture is unchanged — **RadioConsole holds no Google Voice credentials and never talks to Google Voice.** Voicemail and SMS content still arrive only from RotaryPhone's `gvbridge` on `radio:5004`. What changed is that the console's **own, pre-existing** cloud-TTS credential now carries message text as well as announcement text. One sentence, so it is not later mistaken for a boundary violation.
- **The control the owner retains is the selection itself.** Choosing `ESpeak` in System Config keeps every spoken word on the box — for announcements *and* messages, together. That is a better control than a hidden GV-specific key, because it is the one the owner already knows about.

### 9.2 What "the currently selected TTS engine" resolves to — and why it is not the field it looks like

Two fields could plausibly mean "currently selected," and they are **not** the same thing:

| Candidate | Declared | What it actually is |
|---|---|---|
| `TTSOptions.DefaultEngine` | `src/Radio.Core/Configuration/TTSOptions.cs:17` — code default `"ESpeak"`, **deployed `"Google"`** (`Radio.API/appsettings.json:176`) | **The system's only engine-selection input.** |
| `TTSPreferences.LastEngine` | `src/Radio.Core/Configuration/TTSPreferences.cs:17` — `"ESpeak"` | Looks like the persisted runtime selection. **It is dead.** |

"Currently selected" reads most naturally as the *persisted runtime selection*, which would be `LastEngine`. **Resolved against how the system actually reads engine selection today, that is wrong**, and taking it would have quietly reinstated the very thing the owner reversed:

1. **There is exactly one engine-resolution site in the tree.** `TTSFactory.CreateAsync` (`src/Radio.Infrastructure/Audio/Services/TTSFactory.cs:71`):
   ```csharp
   var engine = parameters?.Engine ?? ParseEngine(opts.DefaultEngine);   // opts = _options.CurrentValue :68
   ```
   `_options` is `IOptionsMonitor<TTSOptions>` (`:21`). Nothing else in `src/` decides a TTS engine.
2. **`LastEngine` has zero readers and zero writers.** Grep across `src/` finds the identifier only at its own declaration. The sole consumer of `TTSPreferences` at all is `PreferencesPersistenceService`, which serializes `_ttsPreferences.CurrentValue` back to the config store every save period (`:99`) — it round-trips a bound value that nothing ever mutates. **No user action has ever written it.**
3. **It cannot even receive a selection, because it binds a section that has no such key.** `TTSPreferences.SectionName` is `"TTS"` (`:12`) — the *same* section `TTSOptions` binds (`TTSOptions.cs:12`) — and the deployed `TTS` block (`appsettings.json:172-180`) contains `DefaultEngine`, not `LastEngine`. So `LastEngine` is permanently its compile-time default, **`"ESpeak"`**. Wiring message speech to it would have pinned message speech to espeak-ng *by accident*, while appearing to follow the user.
4. **The control the user actually operates writes `DefaultEngine`.** `SystemConfigPage.razor:678` is a dropdown two-way-bound to `_ttsConfig.DefaultEngine`, i.e. `TTS:DefaultEngine`. *(Do not confuse it with `SystemConfigPage.razor:885`'s `_selectedTTSEngine` — a page-local, never-persisted control that scopes the voice browser and the test-play form, initialised at `:2171` to the first **available** engine. It is a workbench control, not a system setting.)*

> **Decision: "the currently selected TTS engine" is `TTS:DefaultEngine`, read through `IOptionsMonitor<TTSOptions>` — the same value `TTSFactory.CreateAsync:71` already applies to announcements.** Message speech and announcements therefore agree **by construction**, not by copying a value between two places.
>
> **`TTSPreferences.LastEngine` is dead code.** This ADR does not consume it, does not revive it, and recommends it be deleted along with the rest of `TTSPreferences` if nothing else claims it — recorded as §14 Q9 rather than actioned here, since it is outside this arc.

**Two readers disagree today; say which governs.** The announcement path honours the config default — `AnnouncementService.cs:46,121` call `CreateAsync(message, cancellationToken: …)` with `parameters` null, so `:71`'s fallback fires. The events path does not: `SourcesController.PlayTTSEvent:623` builds `Engine = engine ?? TTSEngine.ESpeak`, **hardcoding ESpeak** when the request omits an engine. **The announcement path governs.** `/api/sources/events/*` is one of the two already-defective ad-hoc paths catalogued in §1.2 correction 3 — it does not duck, it leaks its sources and it double-plays files — and it is not a precedent for anything.

### 9.3 ⚠ The trap that makes this a real decision rather than a one-liner

`TTSParameters.Engine` is a **non-nullable `TTSEngine` with an initializer of `TTSEngine.ESpeak`** (`ITTSFactory.cs:87`). So the `??` at `TTSFactory.cs:71` fires **only when `parameters` itself is null** — the null-conditional lifts the whole object, not the property. **Passing any non-null `TTSParameters` bypasses the config default entirely, and if `Engine` was not set it silently selects ESpeak.** That is exactly why `SourcesController:623` looks the way it does: the type gives a caller no way to say *"unset."*

> **Requirement on the implementation:** `EventPlaybackService` must **resolve the engine explicitly and pass it**. Read `IOptionsMonitor<TTSOptions>.CurrentValue.DefaultEngine`, parse it exactly as `TTSFactory.ParseEngine` does (`:223-228` — `Enum.TryParse` ignoring case, falling back to `ESpeak` on garbage), and set `TTSParameters.Engine` to the result. **Do not rely on passing `parameters: null`.** The request already carries `VoiceId` (§4), so the moment a voice is attached the null-parameters path is gone and the default silently becomes ESpeak — reintroducing the pinning the owner just reversed, as a type-system accident.

*Optional cleanup, flagged for Planner rather than required here:* making `TTSParameters.Engine` a `TTSEngine?` would let "unset" be expressible, make `:71` behave as it reads, and fix `SourcesController:623` at the same time. Blast radius is small. Not required by this ADR — noted because it is the root cause and someone will otherwise re-discover it. ✅ **DONE 2026-09-03 by `TTS-9`** — removing eSpeak deleted the enum's zero value, which turned this from tidy-up into a correctness requirement: without it, "unset" would have silently meant `Google`. `TTSParameters.Engine` is now `TTSEngine?` with no initializer, resolved once in `TTSFactory.CreateAsync` against the configured default, which **throws** rather than falling back if that default is missing or unknown.

### 9.4 Three engines, and what happens when the selected one cannot run

> ⚠ **AMENDED 2026-09-03 by `TTS-9` — there are now TWO engines, not three. eSpeak has been removed entirely.**
> The **decision in this section is unchanged and survives intact**; what changed is one of the facts it reasons over.
> Read the section as written — it is the record of why the decision was made — with these four corrections:
>
> 1. **The `ESpeak` row of the table below no longer exists.** `GenerateESpeakAsync`, `IsESpeakAvailable`,
>    `GetESpeakVoicesAsync`, the `TTSEngine.ESpeak` member and `TTSOptions.ESpeakPath` are all deleted. The enum
>    is now `{ Google = 1, Azure = 2 }` — **explicitly numbered from 1 so that `default(TTSEngine)` is not a
>    valid engine.** Had the members been allowed to renumber from 0, `Google` would have silently become the
>    default value of the type, which is precisely the silent cloud substitution this section forbids.
> 2. **Point 1's `ESpeak → cloud` / `cloud → ESpeak` framing is obsolete, and the decision it supports is now
>    *more* load-bearing, not less.** With both survivors being cloud engines, a silent `Google → Azure`
>    substitution would ship a private SMS body to a **different** third party than the owner selected. The
>    exposure §9.1 exists to keep explicit did not go away; it lost its one privacy-safe direction.
> 3. **The "`espeak-ng` missing from `PATH`" example at the end of §9.4 is obsolete.** The remaining authoritative
>    synthesis failures are unsubstituted `${secret:` tags, revoked or expired keys, and network failure.
> 4. **`espeak-ng` is never installed** (`TTS-7` closed as *remove*, owner decision `D26`). ⚠ **Accepted
>    trade-off:** eSpeak was the only `IsOffline = true` engine, so **feature B now requires network.** Under
>    §9.4's own decision this surfaces correctly as `Failed` + `SpeechSynthesisFailed` rather than as silence.
>
> ⚠ **`PHN-1c` implementers, read this:** the optional cleanup flagged at the end of §9.3 — *"making
> `TTSParameters.Engine` a `TTSEngine?`"* — **has been done** by `TTS-9`, because removing the enum's zero value
> made it necessary rather than merely tidy. `TTSParameters.Engine` is now `TTSEngine?` with no initializer, and
> `SourcesController`'s `Engine = engine ?? TTSEngine.ESpeak` is now `Engine = engine`. **A `PHN-1c` branch cut
> before 2026-09-03 will need to rebase onto this.**

**Azure is fully implemented and the original ADR never mentioned it.** `TTSEngine` is `{ ESpeak = 0, Google = 1, Azure = 2 }` (`ITTSFactory.cs:67-77`), and all three are real generate paths:

| Engine | Generate | Advertised availability (`DetectAvailableEngines`) | Guard inside the generate path |
|---|---|---|---|
| `ESpeak` | `:301-373` — shells `espeak-ng --stdout`, stdin-piped text. Local, offline, no key. | `IsESpeakAvailable()` `:272-299` — runs `espeak-ng --version` | none — a missing binary fails at process start |
| `Google` | `:375-461` — REST to `texttospeech.googleapis.com`, key from `TTSSecrets.GoogleAPIKey` | `:247` — `!IsNullOrEmpty(GoogleAPIKey)` | `:383-387` — throws if empty **or still contains `${secret:`** |
| `Azure` | `:463-484` — REST to `https://{region}.tts.speech.microsoft.com/cognitiveservices/v1`, SSML with rate/pitch; key + region from `TTSSecrets` | `:258-259` — `!IsNullOrEmpty(AzureAPIKey) && !IsNullOrEmpty(AzureRegion)` | `:471-474` — throws if key **or** region empty |

**Availability is genuinely not guaranteed per engine, and the two definitions of "available" disagree.** Two defects, recorded because the decision below has to survive them:

- **(a) The advertise test is weaker than the generate guard for Google, and both are weak for Azure.** `appsettings.json:173-174` ships `"${secret:tts_google_api_key}"` / `"${secret:tts_azure_api_key}"`. On a box where the secret was never set, **Google advertises available and then throws at synthesis** (the generate guard catches the unsubstituted tag; the advertise test does not). Azure has the mirror problem: *neither* its advertise test nor its generate guard checks for the tag, so an unsubstituted Azure key passes both and fails at Microsoft's endpoint as a 401.
- **(b) `AvailableEngines` is cached for the process lifetime.** `_cachedEngines ??= DetectAvailableEngines()` (`:54`). Set a key from System Config → Secrets and the advertised answer does not change until `radio-api` restarts.

> **Decision: an unavailable engine FAILS the playback with a stated reason. It never silently substitutes another engine.**

Three reasons:

1. **The owner's instruction is to use the *selected* engine.** A silent fallback uses a different one. In the `ESpeak → cloud` direction that ships a private SMS body to a third party the owner did not select — the one substitution that must never happen silently, and precisely the exposure §9.1 exists to keep explicit. In the `cloud → ESpeak` direction it is privacy-safe but produces a mystery ("why does this one sound different?") and hides a misconfiguration with a one-place fix.
2. **This is a real state the play button must express, and the surface already exists.** `EventPlaybackSnapshot.State = Failed` + `FailureReason` (§8.2) is exactly this, and the Designer has already designed for it — the `Couldn't read that message.` toast (handoff §Cross-5) and the `Engine error` bubble state (§B4).
3. **Voicemail is unaffected.** The `RemoteMedia` arm never touches TTS, so an unavailable engine breaks feature B only. Nothing about A degrades.

**Two tiers, and the ADR says which is authoritative:**

- **Pre-flight — advisory, fast, precise.** Before synthesis, `EventPlaybackService` checks the resolved engine against `ITTSFactory.AvailableEngines`. On `IsAvailable == false`, return the snapshot as `Failed` with reason **`SpeechEngineUnavailable`**, without attempting synthesis. This exists only to turn the common misconfiguration into an immediate, precise answer.
- **Synthesis — authoritative.** `CreateAsync` still throws for everything the advertise test misses: defect (a)'s unsubstituted-tag cases, an expired or revoked key, a network failure, `espeak-ng` missing from `PATH`. Catch it, map to `Failed` with reason **`SpeechSynthesisFailed`**, log it with the engine name. **The generate path is the authority; the pre-flight only improves the message.**

**One small consequence of defect (b) that must not be skipped:** a cached advisory gate is only ever wrong in the *blocking* direction — it can report "unavailable" for an engine the owner just fixed, and keep doing so until `radio-api` restarts. So either `TTSFactory` invalidates `_cachedEngines` on an `IOptionsMonitor<TTSSecrets>` change (a one-line `OnChange` registration), **or** the pre-flight is dropped and synthesis becomes the only gate. Both are acceptable. **A cached pre-flight that blocks a now-working engine until restart is not.** — Planner's choice; flagged as §14 Q10.

### 9.5 `GvMedia:SpeechEngine` is deleted, not redefined

The key was invented to let message speech *diverge* from the system selection. The owner's instruction is that it must not diverge. Redefining it as an "override" would keep a **second place where engine selection lives**, and the failure mode is specific and bad: the owner changes the TTS engine in System Config, every announcement changes voice, and the texts keep speaking in the old one — with the explanation buried in a key no UI surfaces.

**So it is removed from the config table (§10.2) entirely.** The escape hatch that survives is better-scoped and already in the request: `EventPlaybackRequest.Engine` and `.VoiceId` (§4) let a *caller* override for one specific utterance without creating persistent hidden state. **`Radio.Web` sends both null for message speech**, which is the whole point — one selection, one place, honoured everywhere.

### 9.6 What this changes downstream — three notes for the Designer

The Designer's handoff was written against the pinned-local decision. Three of its statements are now superseded, and one of them is a factual assumption rather than a preference:

1. **The tonal-mismatch concern is resolved by removal.** Message speech now sounds **identical to announcements**, because it is literally the same engine, voice and parameters. The handoff's §Answers item 8 argument *"a different voice is a feature, not a defect"* is no longer available — there is no different voice. Its **Q5** (robotic-but-private vs better-but-cloud) is **closed by the owner**, not left to a listening test.
2. **⚠ §B4's synthesis-latency assumption is now false, and this one matters.** It reads: *"synthesis is on-box and there is no network round trip — but it is still not instant."* With `TTS:DefaultEngine = "Google"` as deployed, **every play of a text is a cloud round trip**, and there is still no cache on the speech path (`TTSFactory.cs:92-103` computes a cache key and then explicitly does not use it — `isCached` is a hardcoded `false`). So feature B's `Preparing` is longer, network-dependent, and **can fail at the network** — a *different* network from voicemail's (Google/Azure, not gvbridge on `radio:5004`), so the two failures are independent and both are real. §B4's `Preparing` design still works; its stated reasoning needs updating.
3. **The offline asymmetry is now live.** A cached voicemail plays with no internet at all; a spoken text does not, whenever the selection is a cloud engine. Worth one line of the design's awareness — not necessarily a UI state, since the existing `Couldn't read that message.` copy already covers it honestly.

---

## 10. Decision D8 — config, and closing carried risk #3

### 10.1 Does routing server-side close the auth seam? **Yes — but not automatically.**

`docs/BUILDER_QUEUE.md` § Carried risks #3 holds an open cross-repo ask: *keep the voicemail audio endpoint unauthenticated or token-in-query when `X-RotaryPhone-Auth` ships, because a native `<audio>` element cannot send the header.* ADR-022 §8.1 and FUTURE-WORK §12 item 4 record the same constraint.

The mechanism is precise, and the code confirms it exactly: `RotaryPhoneAuthHandler` is a **server-side `DelegatingHandler`** attached to five typed clients in `Radio.Web`. `GetVoicemailAudioUrl` (`GvBridgeApiService.cs:134-140`) is the one method that **never issues a request through that chain** — it only *reads* `_httpClient.BaseAddress` and returns a string, which is then handed to the browser's `<audio src>`. The browser does the fetch, so no handler can touch it. Today that is invisible because the key is `""`; **the moment the gate flips on, browser-side voicemail playback breaks while every other GV call keeps working.**

**Under this ADR, no browser ever fetches voicemail audio.** `GvMediaClient` fetches it, server-side, inside Radio.API, through a normal `HttpClient` that can carry any header. **The constraint dissolves.**

**The caveat that must not be lost:** it dissolves *only because Radio.API is given the credential and the handler*. It would **not** dissolve under the rejected design where Web hands Radio.API an opaque URL (§13), because Radio.API would neither know the URL was a gvbridge URL nor have a key to attach. And it is not free: Radio.API today has **no `AddHttpClient`, no `IHttpClientFactory`, and no `DelegatingHandler` infrastructure at all** (§5.1), so the handler must be extracted to a shared location or copied. The closure is a consequence of D2's `(kind, id)` reference plus D8's config and that handler work — not of "server-side" in the abstract.

**So, explicitly and for the record:**

> **Carried risk #3's audio-endpoint clause is closed by this ADR.** Once ADR-029 ships, RotaryPhone is free to make `/api/gvbridge/voicemail/{id}/audio` auth-required along with the rest of the `/api/gvbridge/*` prefix gate. **The standing cross-repo ask should be withdrawn** — and withdrawing it is a deliverable of this arc, not an afterthought: leaving a stale "please keep this endpoint unauthenticated" request in their queue actively discourages them from closing a real gap.
>
> The other clauses of carried risk #3 are unaffected (mark-read: closed by GV-4; `SendSmsResponse`: closed by ADR-028).

### 10.2 The config block

Radio.API gains its own `GvMedia` section. It deliberately does **not** reuse `PhoneIntegration:ContactsApiBaseUrl` even though both point at `radio:5004` — that key means "where the contacts API is", and overloading it couples two features that can be deployed and disabled independently (note `PhoneIntegration:Enabled` is currently `false`).

| Key | Default (`appsettings.json`) | Per-machine (`appsettings.Production.json`) | Consumer |
|---|---|---|---|
| `GvMedia:Enabled` | `false` | flip `true` when the arc ships | `EventPlaybackService` — gates the `RemoteMedia` arm |
| `GvMedia:BaseUrl` | `http://radio:5004` | override if host differs | `GvMediaClient` |
| `GvMedia:AuthKey` | `""` (empty = no header) | **set when the gate ships** | `GvMediaClient` auth handler |
| ~~`GvMedia:SpeechEngine`~~ | **DELETED ⟨A1·1⟩** — engine follows `TTS:DefaultEngine`; §9.5 | — | — |
| `GvMedia:CacheDirectory` | `./data/gvmedia` | — | cache |
| `GvMedia:CacheMaxMegabytes` | `50` — **owner-confirmed default ⟨A1·2⟩** | `0` disables caching entirely (§5.3) | cache |
| `GvMedia:MaxPlaybackSeconds` | `300` | — | the D7 hard cap |
| `GvMedia:MaxSpeechChars` | `1000` | — | speech truncation (§4.2) |
| `GvMedia:PreemptAtPriority` | `8` | — | the D5 rule-2 threshold |
| `GvMedia:FetchTimeoutSeconds` | `15` | — | `GvMediaClient` |

**A real cost, stated:** `RotaryPhone:Gv:AuthKey` (Web) and `GvMedia:AuthKey` (API) are now **two copies of one secret in two services' configuration**. Both are per-machine and belong in `appsettings.Production.json` (deploy overwrites `appsettings.json` — ADR-022 D8). Keeping them in sync is a deployment burden and a mismatch fails only as a 401 on voicemail playback. Mitigation: `deploy/` should write both from one source value, and Radio.API should log one clear warning at boot if `GvMedia:Enabled` is true while its `AuthKey` is empty.

The alternative — Radio.API asks Radio.Web for a short-lived token — adds a hop and a token lifecycle to solve a problem that exists once, at deploy time. Rejected.

---

## 11. Decision D9 — the consequence of C (canned responses)

The belief handed to this ADR was that C is *mostly a UX change and the send plumbing is unaffected*. **Three parts are confirmed. One is refuted, and it is the part that would have bitten.**

**Framing note the Planner needs:** **GV-5 is queued, not shipped.** `OutboundSmsReconciler` **does not exist in code** (zero matches in `src/` and `tests/`) — it is an ADR-028 design. The *shipped* de-dupe is inline at `PhonePage.razor:789-797`, matching on `Id.StartsWith("temp-")` + outbound + exact text + `|ΔSentAt| ≤ 30s`. Likewise `GvCounterparty`/`CanReply` **does not exist yet** — ADR-028 §8 is ratified but unimplemented. So both constraints below land **inside GV-5's implementation**, not as retrofits.

### 11.1 Confirmed — the wire contract does not change

ADR-028 §3 fixes the request as `{ toNumber, text, threadId, clientCorrelationId }`. A canned response is a `text` value like any other. Nothing about `GvBridgeSendService`, the nine-code taxonomy, the `Code` → exception → bubble mapping, or `RotaryPhone:Gv:SendEnabled` changes. **The `SmsSent` echo subscription and the reconciler's structure are unaffected.**

### 11.2 Confirmed — reply-ability gating is unchanged, and must be applied to the new affordance

ADR-028 §8 gates compose at the thread level: a short code or opaque sender ID is not repliable, the composer renders disabled with *"You can't reply to this sender."*, and `SendAsync` throws `SendNotRepliableException` as defence in depth. **A canned-response chip set is a composer.** It must be gated by the identical `GvCounterparty.CanReply` check. Roughly a third of inbound threads are un-repliable (ADR-028 §8.1), so this is the common case — and a row of tappable canned-reply chips on an un-repliable thread reintroduces exactly the failed-send lie §8.5 exists to prevent. **A constraint for Designer and Planner, not a new mechanism.**

### 11.3 Refuted — canned responses invalidate a probability assumption inside an accepted risk

ADR-028 §4.4 sets the outbound de-dupe key as exact `Id` first, then `(Outbound, normalised counterparty, ordinal-equal text, |ΔSentAt| ≤ 120s)`, and accepts a residual risk in as many words:

> *"two genuinely distinct sends of identical text to the same counterparty inside 120s collapse into one bubble. Judged strictly better than the alternative failure."*

That judgement was made when `text` was **freeform**, where two byte-identical messages inside two minutes is genuinely rare. **Canned responses draw `text` from a fixed set of five or six strings.** "OK", "On my way", "Call you later" become *the only things anyone ever sends*. Two taps of the same chip to the same person inside 120 seconds stops being a curiosity and becomes an ordinary interaction — and the second one silently vanishes.

The exact-id tier does not save it: ADR-028 §4.3 establishes that the poller's re-surfaced copy **always** carries a recomputed `csid:{threadId}:{sha1(text)[..12]}:{sentEpochMs}` id which by construction cannot equal the supplied `ClientCorrelationId`. The poller copy therefore always falls through to the fuzzy tier — exactly the tier whose collision probability C has just raised. (The shipped inline de-dupe's 30s window narrows but does not remove this.)

**This is not a reason to block C.** It is a small, precise constraint that must reach the implementer:

> **The fuzzy tier must be one-to-one.** A poller re-surface may reconcile against at most **one** un-reconciled optimistic bubble; a bubble already reconciled by an earlier echo is not an eligible match. Two distinct sends produce two bubbles and two poller copies, and each copy consumes exactly one bubble. This is *implied* by ADR-028 §4.4's *"REPLACED IN PLACE — never removed-and-appended, never double-added"* idempotence, but under freeform text it was never load-bearing. **Under canned responses it is.** Planner must ensure GV-5's reconciler consumes matches exclusively, with a regression test for *"same canned text, same counterparty, twice inside 120 s → two bubbles."*

### 11.4 Minor — the keyboard dependency does vanish after all ⟨A1·3⟩

FUTURE-WORK §12 item 3 records that compose uses the app-wide virtual keyboard, with the recipient field opting into the numeric layout via `data-keyboard="numeric"`. Canned responses remove that dependency for the message field; the original text said the recipient field might keep it alive, *"if new-recipient mode survives C."*

**It does not survive — see §11.5.** `PhoneTextsPanel.razor:122` is the repo's **only** `data-keyboard` consumer, so the count drops to **zero** and the virtual keyboard never appears on `/phone` again. Architectural note only: the keyboard still serves its other surfaces and is being evaluated separately; this arc simply removes `/phone`'s claim on it.

### 11.5 ⚠ Initial-sender is out of scope — what in the send path was relying on it ⟨A1·3 — new⟩

> **Owner:** *"Being the initial text sender can remain out of scope for the radio console. That's not a huge use case for this feature."*

**The console is reply-only.** New-recipient compose is **removed, not redesigned** — this closes the Designer's **Q1** at option **(b) accept reply-only**, and takes option (a) off the table with it. The ADR's job here is to say what in the send path was leaning on a case that no longer exists. Five things, and **only one of them is a removal**:

**1. `toNumber` does NOT go away — the field stays, one of its two sources goes.** This is the trap, and it is the reason this section exists. ADR-028 §3 fixes the request as `{ toNumber, text, threadId, clientCorrelationId }` and requires `toNumber` in **both** modes — reply mode sources it from the thread's counterparty, new-recipient mode from typed input with `threadId: null`. The *shipped* record is still the broken two-field `SendSmsRequest(string ThreadId, string Text)` (`src/Radio.Web/Models/ApiModels.cs:1187`, constructed positionally at `GvBridgeSendService.cs:74`), which omits `toNumber`, binds it to `null` on the wire, and **fails 100% of sends with `400 invalid_number`** (ADR-028 §2 finding 1). **Reply-only does not make that go away.** GV-5 must still send the four-field request. What disappears is the **`threadId: null` arm** and the "do not pre-normalise typed input" rule attached to it.

**2. `toNumber` now has exactly one source and no fallback — which promotes `GvCounterparty` from a gate to a dependency.** ADR-028 §8's `GvCounterparty`/`CanReply` (ratified, still **unimplemented** — zero matches in `src/` and `tests/`) was specified as the *composer gate*. With typed input gone it becomes the **sole supplier of the send's only required addressing field**. Two consequences for GV-5:
   - A classification bug is now a **send** bug, not merely a UI blemish. A counterparty that resolves to the wrong string produces a send to the wrong number, with no user-visible recipient field in which the mistake could have been noticed.
   - **ADR-028 §8.4's rule — *"`toNumber` is never a thread id"* — is promoted from defence-in-depth to load-bearing.** Their normalizer strips non-digits, so a synthesized thread id like `t.+19195551234` *silently* "normalises" to a plausible `+19195551234` rather than failing. With no typed-input escape hatch, a thread whose counterparty cannot be resolved has **no** way to produce a `toNumber` and **must be classified non-repliable** — never "send the thread id and hope."

**3. ADR-028 §5's inline-recipient-field error path is now unreachable, and `invalid_number` changes meaning.** ADR-028 (`:199`) routes `invalid_number` to *"the inline recipient-field error, not just a toast"* — a field that no longer exists. Toast-only is now the whole treatment, which is exactly what the Designer's §C5 already specifies, so the two agree. But note the semantic shift: with `toNumber` no longer user-typed, **`invalid_number` can no longer be user error.** If it fires, it is a `GvCounterparty` classification bug. Log it as such rather than only surfacing it — a silent toast on a defect the user cannot fix is how this class of bug stays invisible.

**4. ADR-028's latent thread-identity bug evaporates, and its planned fix should be kept anyway — demoted, not deleted.** ADR-028 (`:206`) records that after a *new-conversation* send, the response's `ThreadId` is RotaryPhone's synthesized, explicitly-UNVERIFIED `t.+<E164>` while the poller later surfaces the same conversation under Google's **real** id, so `PhonePage.BumpThread` (matching on `ThreadId` alone) inserts a **duplicate thread row**. GV-5 planned a normalised-counterparty fallback to fix it. **Reply-only means our sends never create a conversation, so the divergent-id case cannot arise from them.** Recommendation: **keep the fallback**, because it is cheap and also guards the poller's own thread-identity churn — but **downgrade it from "required fix" to "defensive"**, and release GV-5 from the obligation to test the new-conversation path, which is now untestable because we can no longer create one.

**5. `OutboundSmsReconciler` was not relying on it, and §11.3's constraint is unaffected.** The reconciler still **does not exist in code** (zero matches in `src/` and `tests/` — unchanged since the original draft). Its fuzzy key is `(Outbound, normalised counterparty, ordinal-equal text, |ΔSentAt| ≤ 120s)`, none of which came from new-recipient mode. One sub-argument does weaken: ADR-028 §4.3 gives three reasons exact-id matching is insufficient, and reason **2** (*"the thread id can differ — for a new conversation…"*) loses its force. Reasons **1** (the epoch differs — controller stamps `UtcNow`, poller uses Google's second-granularity `sentEpochMs`) and **3** (supplying `ClientCorrelationId` guarantees the poller's recomputed `csid:` diverges) are untouched and **each is independently sufficient**. So the fuzzy tier is still required, and **§11.3's one-to-one constraint remains the important constraint of this section** — canned responses make collisions ordinary, and reply-only does nothing to help.

**Scope boundary the Planner must not blur.** `Text back` from a call or voicemail is *not* uniformly in or out: opening a thread that **already exists** is navigation and stays available; creating one where **none exists** is being the initial sender and is **out** by the same ruling. Which of those the affordance is — and whether it appears at all — is the Designer's to spec; the architectural line is that **nothing may construct a send with `threadId: null`.**

---

## 12. CROSS-AGENT HANDOFF — Designer ⟨A1 — CONSUMED; kept as the record of the exchange⟩

> **Status: this section has been read and answered.** The Designer's handoff is **`docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md`**; its `§Answers to ADR-029 §12` responds item by item, and **that handoff is authoritative on presentation.** The items below are preserved as the record of what was asked, with the answers folded in. Three items moved:
>
> - **Item 6 — accepted, and it flipped D7.** The Designer designed the persistent transport (the chip in `.topbar-primary`) and asked for the navigate-away rule to be overridden. Done — §7 is rewritten ⟨A1·4⟩. **Their correction on placement is accepted and encoded:** the chip must **not** live in `NowPlayingDock`, because `MainLayout.razor:878` gates it on `IsDockVisible => !_isOnHome` — absent on Home, the worst place to lose a stop control. My original suggestion of the dock was wrong. **One thing their premise missed, added by §7.5:** `.topbar-primary` is on every route *under `MainLayout`*, and `/sleep` runs under `EmptyLayout` (`Sleep.razor:2`) — the console navigates itself there on an idle timer, so that route needs its own rule.
> - **Item 8 — obsolete.** §9 no longer pins the local engine ⟨A1·1⟩. Message speech follows the selection and therefore sounds **identical** to announcements; the tonal mismatch the Designer weighed no longer exists, and neither does the argument that it was doing useful work. §9.6 lists the three downstream corrections, including one factual assumption in §B4 that is now false.
> - **Item 10 — answered by the owner, not the Designer.** Initial-sender is out of scope; new-recipient mode goes ⟨A1·3⟩. §11.5 records what in the send path was leaning on it.
>
> Items 1-5, 7 and 9 were consumed as written and needed no change.

Designer was working the transport UI, the play-button affordance and the canned-response set **in parallel**. These are the points where this ADR constrains that work. **Item 1 was a hard dependency and was explicitly requested.**

**1. Scrubbing — the answer is unambiguous: tap-to-seek yes, live drag-scrub no.**

- **Guaranteed:** play, stop, seek-to-position, known total duration, a smooth progress bar, completion. Seek round-trip is **~30-85 ms** (§8.3).
- **Not available:** continuous scrubbing where audio follows the thumb in real time. That needs a ~16 ms budget; we have ~30-85 ms plus an output buffer.
- **Design to this pattern:** the thumb may be dragged freely and moves **locally with no audio response**; audio repositions **once, on release**. A tap anywhere on the bar seeks immediately. Skip ±15 s buttons are the cheapest good affordance and behave perfectly.
- **The progress bar is client-interpolated** from a broadcast anchor (§8.2), not server-ticked. It will be smooth. Do not design anything that assumes the server streams a position.
- **Pause/resume is available** — `IPrimaryAudioSource` already declares it and `FilePlayerAudioSource` already implements seeking over local files, so this is a lift rather than an invention (§8.3). It applies to **voicemail only**; seeking or pausing inside a spoken text message is not supported and has no user value.
- **Duration can legitimately be unknown.** `DurationSeconds == 0` means "unknown" in the GV contract (§4.1) — design an indeterminate progress state rather than rendering `0:00` as real. This is the same rule ADR-022 §4.2 already set for the voicemail row.

**2. Both play buttons need a `Preparing` state, and it is not instant.** Voicemail's first play does a network fetch; speech does a TTS synthesis step. Neither produces sound immediately. There is a real spinner moment between tap and audio — design it. **Replays of a cached voicemail are effectively instant** (§5.3), so the same button has two very different latencies.

**3. Playback can fail *before* any sound, and during a blackout it frequently will.** GV auth is dead ~9 minutes in every 20; a first-time voicemail fetch has roughly a 45% chance of hitting that window and returning 502. This needs calm, honest error copy and a retry affordance — and note this is the same window in which the *"Google Voice is reconnecting"* banner is known **not** to fire, because the status endpoint lies (`design/INTEGRATIONS.md`; `PhoneMessagesPanel.razor:14-20`). **Do not rely on that banner to explain a failed voicemail fetch; the player needs its own error state.**

**4. An incoming call or caller announcement STOPS playback — it does not pause it** (§6.2 rule 2). The UI must return to a clean idle/replayable state, not a paused-mid-track one. Users replay; that is expected and fine.

**5. Starting one attended playback stops the other** (§6.2 rule 1). At most one thing is ever playing. The play buttons across the whole `/phone` surface are therefore a **single-selection group**, not independent toggles — a real IA consequence worth designing for explicitly. (Note the voicemail list is already a one-at-a-time accordion via `_openVoicemailId`, `PhoneMessagesPanel.razor:285`, so this extends an existing behaviour to texts.)

**6. ~~Navigating away from `/phone` stops playback~~ — ⟨A1·4⟩ ANSWERED YES; THE RULE IS FLIPPED.** *(Original: "this rule is yours to change — if you design a global transport affordance in the topbar, the reason for the rule disappears and playback could survive navigation.")* The Designer did exactly that. **Playback now survives navigation** (§7 rewritten). Their correction to my suggestion is accepted: the chip goes in `.topbar-primary`, **not** `NowPlayingDock`, which `MainLayout.razor:878` hides on Home. Two things came back the other way and are now the Designer's to absorb: **`/sleep` runs under `EmptyLayout` and has no topbar** (§7.5 — entering it stops playback unless the sleep surface grows a control), and **`GET /api/audio/events/current` is now the re-attach path** (§8.1), so `AudioStateStore` must be seeded from it rather than waiting for a broadcast.

**7. Utterance copy for feature B is yours.** Radio.API speaks a finished string composed by `Radio.Web` (§4.2). Whether it is `"Message from Jane: on my way"` or the bare body is copy. Two constraints: **capped at 1000 characters**, and **normalised before speaking** (URLs collapsed to "a link", emoji dropped) — a raw SMS read literally by a TTS engine is unpleasant.

**8. ~~The message voice will sound robotic by default.~~ — ⟨A1·1⟩ WITHDRAWN.** *(Original: "§9 pins message speech to the local `espeak-ng` engine so private SMS bodies never leave the box… if the tonal mismatch matters, raise it.")* The owner reversed the pin. **Message speech follows the currently selected engine and therefore sounds identical to announcements** — same engine, same voice, same parameters. There is no tonal mismatch to design around and no listening test to run. **Read §9.6 instead:** it lists what this changes for the design, including one assumption in the handoff's §B4 that is now factually wrong (speech synthesis is a **cloud round trip** as deployed, not on-box, and there is no cache on the speech path).

**9. Canned responses must respect reply-ability.** ~1/3 of threads cannot be replied to at all (short codes, opaque sender IDs). The chip set must be **disabled with the existing "You can't reply to this sender." treatment** on those threads, reusing the `.phone-pill` affordance — not hidden, per ADR-028 §8.5 (§11.2).

**10. ~~Open for you: does new-recipient mode survive C?~~ — ⟨A1·3⟩ ANSWERED BY THE OWNER: NO.** Initial-sending is out of scope; the console is **reply-only**. The numeric virtual-keyboard dependency goes with it (count drops to zero). **§11.5 is the section to read** — the removal has one non-obvious consequence: `toNumber` **stays** in the request and now has a single source, which promotes `GvCounterparty` from a UI gate to the send path's sole addressing dependency.

---

## 13. Alternatives considered

- **Keep the browser `<audio>` and add a "duck the radio" API call alongside it.** Rejected. The two audio paths stay unsynchronised — the browser's clock and the engine's clock drift, the duck would not release if the browser stalled or the tab closed, and on a Cast or exclusive-mode output the browser audio may be inaudible. It also does nothing for feature B, so it fails the one-mechanism requirement outright.
- **`Radio.Web` fetches the bytes and pushes them to Radio.API.** Rejected. Pumps megabytes of audio through the Blazor Server circuit and then over a second HTTP hop, on an N100 already sensitive to load. Web has no reason to touch the bytes.
- **Radio.API accepts an opaque absolute URL to play.** Rejected on two independent grounds: it is an SSRF primitive by construction, and — decisively — **it would not close carried risk #3** (§10.1), because Radio.API would not know the URL was a gvbridge URL and would have no credential to attach. The `(kind, id)` reference costs nothing and buys both.
- **Reuse `POST /api/sources/events/file` (it already makes the engine play a file).** Rejected. It takes a filesystem path with no fetch step, **bypasses ducking entirely** (`SourcesController.cs:44` injects `IDuckingService` and never uses it), **leaks** its sources (never removed, never disposed — `:651`, `:716`), returns no usable lifecycle, and **double-plays** (`:719` + `:732`). It is the closest existing primitive and it is not close enough.
- **Model voicemail as a primary source** to inherit full transport for free. Rejected twice over: primary sources are mutually exclusive, so playing a voicemail would **evict the user's radio station** and force a re-select; and primary sources are *meant* to survive navigation, which is precisely the wrong lifecycle (§7.2).
- **Extend `IAnnouncementService` instead of adding `IEventPlaybackService`.** Rejected — §3.1. A fire-and-forget signature with existing callers and no notion of identity, position or state; the "extension" would be a rewrite in disguise.
- **Two endpoints — `/api/notifications/announce` for B, a URL player for A.** Rejected — §3.2. Two lifecycles, two stop paths, two state models, no way to express "starting this cancels that" — and this codebase already demonstrates where that road ends.
- **Broadcast a periodic position tick over SignalR.** Rejected — §8.2. Steady-state churn on a box where churn is audible, to produce something the client can compute locally and more smoothly from an anchor.
- **Stream the voicemail into the engine rather than caching to disk.** Rejected — §5.2/§5.3. `StreamDataProvider` over a non-seekable network stream is untested, the duration/completion model is estimate-and-timer based and does not survive an unknown-length remote stream, seeking becomes hard again, and it forfeits the blackout resilience that is the cache's main justification. The recordings are ~1 MB.
- **~~Ship feature B on the deployed `Google` TTS engine.~~ Rejected as the default — then ⟨A1·1⟩ ACCEPTED by the owner, for a better reason than the one originally weighed.** The original rejection was right about the *process*: sending private message bodies to a third party should be an explicit owner choice, not an inherited config default. It became one. The decision is not "Google" though — it is **"whatever is selected"** (§9.2), which is a different and more durable answer: it keeps engine choice in one owner-visible place and makes the privacy posture a consequence of that one choice rather than of a hidden GV-specific key.
- **Redefine `GvMedia:SpeechEngine` as an override instead of deleting it.** Rejected — §9.5. It would leave two places where engine selection lives, with a specific bad failure: the owner changes the engine in System Config, announcements change voice, and texts keep speaking in the old one, explained only by a key no UI surfaces. The per-request `EventPlaybackRequest.Engine`/`VoiceId` override survives and is better scoped — it affects one utterance, not a persistent hidden default.
- **Fall back to another engine when the selected one is unavailable.** Rejected — §9.4. In the `ESpeak → cloud` direction it would send a private SMS body to a third party the owner did not select, silently; in the `cloud → ESpeak` direction it hides a one-place-fixable misconfiguration behind a mystery voice change. Failing with a stated reason costs one snapshot state the design already has.

---

## 14. Open questions ⟨A1 — three closed, three added⟩

**Closed by Amendment 1:**

1. ~~**Voicemail audio at rest.**~~ **CLOSED ⟨A1·2⟩ — the cache is enabled.** Bounded LRU at `./data/gvmedia/`, default `CacheMaxMegabytes = 50`. Voicemail audio at rest is an accepted cost. `0` remains supported as an escape hatch, not the default. §5.3.
2. ~~**Robotic-but-private, or better-but-cloud?**~~ **CLOSED ⟨A1·1⟩ — neither; speech follows the currently selected engine.** The question presupposed a GV-specific pin that no longer exists. `TTS:DefaultEngine` is the single selection, deployed as `Google`, and private SMS bodies reaching it is an accepted, owner-made trade. §9. *(This also closes the Designer handoff's Q5 and retires its verification item 16b/17 listening test.)*
5. ~~**`PhoneIntegration:Enabled` is `false`.**~~ **CLOSED ⟨A1·5⟩ — it is "never enabled," not "drifted off."** Verified: `false` at `appsettings.json:257`, no `appsettings.Production.json` override, no `Environment=` override in either systemd unit; introduced by `8d2a2ab` and never flipped. §6.1 is re-anchored on two live code facts (`DuckingService.cs:30`, `NotificationsController.cs:46`) instead of on the dormant ring. `PreemptAtPriority` stays **8**, with the argument for 7 recorded and rejected.

**Still open:**

3. **Does `SeekAsync` on a small local MP3 behave through SoundFlow?** `FilePlayerAudioSource.IsSeekable => true` establishes the pattern exists for local files, but the exact `SoundFlowPlaybackService` call path has not been exercised for a short MP3 in an *event* source. If it misbehaves, seek degrades to stop-and-restart-at-offset — still workable for a ~1 MB local file, slightly worse latency. — **Planner to verify before sequencing; it changes nothing in this ADR's shape.**
4. **`AudioFileEventSource` completion is a wall-clock timer, not an end-of-stream event** (`:205`, with an in-code acknowledgement that a real implementation would listen for SoundFlow playback-end events). With `DurationSeconds` from the DTO this is accurate enough for the progress bar, but a seek mid-playback must **re-arm** that timer or completion will fire early. Worth fixing properly (subscribe to SoundFlow's end event) while the file is open. — **Planner.**
6. **Should `IAnnouncementService` eventually route through `IEventPlaybackService`, and should `/api/sources/events/*` be retired?** Unifying them would also fix §6.2 rule 3's mixing wart and the leak/no-duck/double-play defects in §1.2 correction 3, for every caller. Explicitly **not** in this arc. — **future; worth a queue row on its own.**
7. **The new-text chime** (`docs/BUILDER_QUEUE.md` § Documented fast-follows: *"Audible new-text chime — belongs in the Radio.API audio layer (ducking-aware), not Blazor"*) is a natural neighbour, at priority ~4 and **unattended** — so it uses `IAnnouncementService`, **not** this seam. Noted so the next reader sees it was considered and correctly excluded. — **future.**

**Added by Amendment 1:**

8. **Sleep semantics ⟨A1·4⟩ — does entering `/sleep` stop attended playback, or does the sleep surface grow a stop control?** §7.5 takes the safe position (**stop**) because `/sleep` runs under `EmptyLayout` and has no topbar, and the console navigates itself there on an idle timer. The alternative is a control on the sleep surface, which is the sleep arc's to design (it is the handoff's own **Q9**, explicitly deferred). **What is no longer available is leaving it unanswered** — under the old navigate-away rule this resolved itself; under the flip it does not. — **owner / Designer (sleep arc).**
9. **`TTSPreferences` is dead ⟨A1·1⟩.** `LastEngine`/`LastVoice`/`LastPitch`/`LastSpeed` have no readers and no writers, and the class binds the same `"TTS"` section as `TTSOptions` (`TTSPreferences.cs:12` == `TTSOptions.cs:12`) with none of its keys present in `appsettings.json` — so `PreferencesPersistenceService:99` writes back compile-time defaults every save period. This ADR does not consume it. Deleting it is outside this arc but should not be forgotten; it is a live trap for exactly the "currently selected engine" question §9.2 had to answer. — **future; worth a queue row.**
10. **Which gate does the speech pre-flight use ⟨A1·1⟩?** `AvailableEngines` is cached for the process lifetime (`TTSFactory.cs:54`), so an engine fixed from System Config → Secrets stays advertised as unavailable until `radio-api` restarts. Either invalidate `_cachedEngines` on an `IOptionsMonitor<TTSSecrets>` change, or drop the pre-flight and let synthesis be the only gate. **Both acceptable; a cached pre-flight that blocks a now-working engine is not.** Related and worth fixing while in the file: the advertise test and the generate guard disagree for Google, and both miss unsubstituted `${secret:` tags for Azure (§9.4 defect (a)). — **Planner.**

---

## 15. Consequences

**Good:**
- **One mechanism, as asked.** Voicemail and text-speech share a request type, a handle, a state model, a ducking policy, a stop path and a broadcast — differing only in how bytes are acquired.
- **Voicemail becomes real console audio** — ducks the radio, uses the selected output, works on Cast and exclusive-mode outputs where a browser `<audio>` may be silent.
- **Carried risk #3's audio clause closes** (§10.1), and the standing cross-repo ask on RotaryPhone can be withdrawn rather than carried indefinitely.
- **Replay survives the GV auth blackout** via the cache — a ~45%-likely failure becomes a local file read.
- **Zero steady-state cost on the N100**: no polling, no position ticks, one hub connection, one timer per in-flight event, at most one in-flight attended event.
- **Most of the machinery already exists.** Radio.API → `radio:5004` (`PhoneContactLookupService`), the cross-client state pattern (`AudioStateUpdateService`/`AudioStateHubService`/`AudioStateStore`), the transport member signatures (`IPrimaryAudioSource`), local-file seeking (`FilePlayerAudioSource`), a local TTS engine (`ESpeak`), and the file-event source itself. This ADR mostly **connects** things rather than inventing them.
- **Priority becomes load-bearing for the first time** (§1.2 correction 2) — a latent no-op in the ducking service turns into a real rule, anchored ⟨A1·5⟩ on the two live occupants of 8 rather than on a dormant service.
- The seek capability ADR-022 fought to preserve **is preserved**, on the correct side of the wire.
- **⟨A1·1⟩ Engine selection lives in exactly one place.** Message speech, announcements and the events path all resolve to `TTS:DefaultEngine`; there is no GV-specific engine key to drift out of sync, and the owner's privacy posture is a consequence of one visible setting rather than of a hidden default.
- **⟨A1·4⟩ Playback survives navigation**, which is the right behaviour for a wall panel: the sound is in the room, not in the page. Incidental navigation on a touch kiosk no longer silences the console — and the stop control is now reachable from **more** places than before, not fewer.
- **⟨A1·4⟩ The lifecycle model got simpler, not more complex.** The flip deleted an ownership concept (`OwnerToken`, §7.4) and replaced a circuit-identity match with an integer, while covering a refresh case the original design would have broken.

**Bad / costs:**
- **`IEventAudioSource` is a Core contract change** (five new members). Bounded — two implementers — and the signatures are copied from `IPrimaryAudioSource` rather than designed, but it is the widest blast radius here.
- **`DuckingService.StartDuckingAsync` must raise `DuckingStateChanged` unconditionally** (§6.3). Safe for the single existing subscriber, but it is a behavioural change in a shared audio service.
- **Radio.API gains HttpClient + DelegatingHandler infrastructure it does not have today** (§5.1), and **the auth key is duplicated** across two services' config (§10.2), with a deploy-time sync burden and a 401-only failure mode.
- **A `CircuitHandler` is net-new** in `Radio.Web`, and it is the *weakest* of the three stop mechanisms (§7.3) — worth having, not worth trusting. ⟨A1·4⟩ It now also owns a live-circuit count, and getting that count wrong fails in the bad direction (audio that stops for no reason, minutes later).
- **Private voicemail audio now sits at rest on disk** (§5.3) where previously it only streamed through a browser. ⟨A1·2⟩ Owner-accepted; no longer an open question.
- **⟨A1·1⟩ Private SMS bodies now reach a third-party TTS API** whenever the selected engine is a cloud one — which it is as deployed (`TTS:DefaultEngine = "Google"`). Owner-accepted and explicit (§9.1). The console's *Google Voice* posture is unchanged: it still holds no GV credentials and never talks to Google Voice.
- **⟨A1·1⟩ Speaking a text now requires the internet** whenever the selection is a cloud engine, and there is **no cache on the speech path** (`TTSFactory.cs:92-103` computes a cache key and then never uses it), so every play is a fresh round trip. A cached voicemail, by contrast, plays offline. Two features on one screen with opposite network dependencies.
- **⟨A1·4⟩ Audio can now play with no client attached**, bounded by `MaxPlaybackSeconds` (300) and by the item's own length. The cap is unweakened by the flip (§7.1), but the exposure it bounds is new.
- **⟨A1·4⟩ `/sleep` needed its own rule** (§7.5) — the flip's premise ("the chip is on every route") is true only under `MainLayout`, and the console navigates itself to an `EmptyLayout` route on an idle timer.
- **Sub-8 announcements still mix over attended playback** (§6.2 rule 3) — a pre-existing wart this ADR declines to fix, recorded so it is not mistaken for an oversight.
- **`GetVoicemailAudioUrl`, `voicemail-player.js` and the `<audio>` element are deleted**, along with their tests. Carried risk #1's absolute-URL rebuild becomes moot for audio (it still applies nowhere else).

**Contract risks to raise with the RotaryPhone session:**
1. **Withdraw the "keep the voicemail audio endpoint unauthenticated" ask** (carried risk #3). No longer needed and, left standing, it discourages them from closing a real gap.
2. **Confirm the audio endpoint's behaviour under server-side fetches.** Our cache fetches each recording once rather than once per play — *less* load than today — but their proxy's caching assumptions were built for a browser client issuing Range requests, and we will issue a single full-body GET.

---

### Handoff ⟨A1 — updated⟩

- **Designer** — §12 is **consumed**; the authoritative presentation spec is now `docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md`. Three things come back the other way and should be folded into it: **§7.5** (`/sleep` has no topbar and therefore no chip — playback stops there unless the sleep surface gains a control), **§8.1** (`GET /api/audio/events/current` is the re-attach path; `AudioStateStore` must be seeded from it so the chip is right on first paint), and **§9.6** (the engine reversal — the tonal-mismatch reasoning in §Answers item 8 is withdrawn, and §B4's "synthesis is on-box, no network round trip" is now factually wrong).
- **Planner** consumes: §3-§8 for the component list and API shape, §4.1 (`DurationSeconds` passthrough — a correctness fix, not decoration), §3.3 (the two-id hazard in `AudioFileEventSource`), §6.3 (the one required `DuckingService` change), §10.2 for config, §11.3 for the one-to-one reconciler constraint and its regression test — **which lands inside GV-5, since `OutboundSmsReconciler` and `GvCounterparty` do not exist yet** — and §14 Q3/Q4 as **verification tasks to run before sequencing**. **Added by Amendment 1:** **§9.2-§9.4** (resolve the engine explicitly from `TTS:DefaultEngine`; the `TTSParameters.Engine` non-nullable trap at `ITTSFactory.cs:87` will silently pin ESpeak if you pass a partially-filled `TTSParameters`; engine-unavailable is a `Failed` snapshot, never a fallback), **§7.3** (the backstop is a circuit **count**, not a circuit identity — getting this wrong stops audio minutes after a refresh), **§7.5** (the `/sleep` rule), **§8.1** (seed the store from `/current`), **§11.5** (reply-only: `toNumber` **stays**, `GvCounterparty` becomes its sole source, and ADR-028's thread-identity fix demotes to defensive), and **§14 Q10** (pick a pre-flight gate).
- **Owner** — §14 Q1, Q2 and Q5 are **closed**. One new decision is routed back: **§14 Q8**, sleep semantics, jointly with the sleep arc's Designer.
