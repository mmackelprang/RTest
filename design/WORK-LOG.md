# Work Log

Running log of development sessions, organized chronologically. Each entry captures what was done, key files changed, and any issues encountered. Updated by Claude Code at the end of each session.

---

## 2026-09-02 — ENC-12: giving ENC-11's safety response a voice

**PR:** _(filled in by Builder)_ · **Branch:** `feat/enc-12-fault-surfacing`

- `ENC-11` already drops the volume knob's per-event clamp from 6 units to 2 when a **safety** field
  fails to read back, and holds it there until a push verifies. That response is real, correct and
  **completely silent** — it reached one log line and the API and stopped. The owner experienced it as
  a volume knob that had quietly become sluggish, on a console inside sealed furniture. This row is the
  two surfaces that explain it: a badge legible from any route, and one notification, once.
- **A healthy boot stays completely silent.** No toast, no banner, no badge, and `Transient` is
  deliberately scored as nothing — a USB peripheral missing a report on the first try is ordinary, and
  badging it would train the owner to ignore the badge that matters.
- **The tier is now observable.** `ConfigStatus` was a silent auto-property; it now raises
  `ConfigStatusChanged` **on change only**, with the change detection in the setter so no assignment
  site can forget it. The retry loop assigns the same value repeatedly, and a broadcast per assignment
  would put SignalR traffic on the wire for a state that did not change, on a box where incidental load
  correlates with audible distortion.
- **Disconnect now resets the tier to `Unknown`**, and the reset lives inside `RaiseConnectionChanged`
  rather than at its five call sites. A device that was `Configured` and is then unplugged must not keep
  claiming it — and because the same value drives the volume clamp, an absent device now also holds the
  tight clamp, which is the correct direction for hardware nobody can verify.
- **The badge decisions are a pure static class**, `EncoderFaultRules`, following `BellHealthRules`
  exactly. `MainLayout` cannot be rendered in bUnit, so logic written inline there would ship with zero
  automated coverage; `MainLayout` gets branches with no logic in them. Three tiers get three **glyphs**
  rather than one glyph in two colours — colour alone fails WCAG 1.4.1 — and the `aria-label` carries
  the state in words regardless.
- **The anti-storm rule is a latch with a stated definition:** each browser session announces each
  severity at most once, and only on escalation; the memory is never reset. Degraded × 50 is one toast;
  Degraded → HardFault is two; anything de-escalating or recurring is silent. Nine tests pin every row
  of that table. The trade — a fault that clears and returns an hour later is silent the second time —
  is covered by the badge, which is stateless and on screen for the whole of it.
- **`AudioStateStore` is now actually constructed.** It was registered `AddSingleton` and had **zero
  consumers** in `Radio.Web`, so it had never once subscribed to the hub and its cache had never run.
  The badge seeds from that cache on every circuit start, so `Program.cs` now resolves it at startup —
  the same trap the file already documents for `EncoderHudService`.
- Corrected a shipped false comment on `AudioStateStore.EncoderConnection`, which claimed the field was
  per circuit when the store is a singleton. The comment was wrong, not the lifetime.
- **Not built here, deliberately:** anything on the Settings page or `IntegrationsController` (`ENC-8`
  owns every pixel and every endpoint there), tab deep-linking for the toast, and anything on `/sleep`.
  The first two are logged in `design/FUTURE-WORK.md`.

---

## 2026-09-02 — PHN-1a: the event-playback contracts (ADR-029 PR 1 of 7)

**PR:** [#528](https://github.com/mmackelprang/RTest/pull/528)

The type surface for the phone arc, and nothing else: `IEventPlaybackService`, the closed discriminated
`EventPlaybackRequest`, the `EventPlaybackSnapshot` anchor, and the five transport members `IEventAudioSource` was
missing. No controller, no route, no DI registration, no ducking change — **nothing plays a sound differently than
it did before.** The point is that PRs 2-5 build against one settled contract instead of inventing three.

- `IEventPlaybackService` sits **beside** `IAnnouncementService`, not inside it. The announcement service is
  fire-and-forget *by signature* — bare `Task`, no identity, one global `StopAsync` — and both its callers depend on
  exactly that. The split is attended vs unattended, which is what PRs 4 and 5 branch on.
- The request set has **asymmetric arms**: speech carries literal text, voicemail carries a `(kind, id, duration)`
  reference. There is deliberately **no URL field and never may be one** — an endpoint that fetches a caller-supplied
  URL is an SSRF primitive. Pinned by a reflection test on the type and by `Validate` rejecting a URL-bearing id.

**The ADR claim that scoped this row was false, and it changed the work.** ADR-029 §8.3 calls D4 "a lift rather than
an invention" because `FilePlayerAudioSource` "already implements seeking through `SoundFlowPlaybackService`". Both
halves are false: `SoundFlowPlaybackService` had **no seek method of any kind** across its whole public surface, and
`FilePlayerAudioSource.SeekCoreAsync` assigns `_position = position` — the field `Position` reads back — under an
`IsSeekable => true`. A seek reported success, moved the readout, and changed nothing audible. So this PR **built**
the primitive rather than lifting one. `GetPosition`, which returned `TimeSpan.Zero` behind the false comment
*"Position tracking not available in current SoundFlow API"*, now reads `SoundPlayerBase.Time`; it had **zero callers
repo-wide**, so the correction had no blast radius.

`FilePlayerAudioSource` was deliberately **not** fixed — it is a live primary path whose persisted resume position
hangs off that same field, so it needs its own UAT. Logged to `design/FUTURE-WORK.md` §14a.

**Two completion traps, one of which no document had ever named.** `AudioFileEventSource` decided "finished" from a
single wall-clock `Task.Delay(_duration)`, so a seek or a **pause** made it fire at the wrong time (ADR §14 Q4 caught
only the seek half). And pausing a `TTSEventSource` would have made its monitor loop read `!IsPlaying(Id)` as
*natural completion*, because `IsPlaying` is `player.State == PlaybackState.Playing` and a paused player fails it.
Both fixed by re-arming on transport events — no poll and no timer added.

**Process note, and the useful part of this entry.** Pre-merge review found 2 HIGH and 4 MEDIUM, and **7 of the 14
findings were defects in the plan, not in the implementation** — the implementer had followed the plan's literal code
faithfully. Both HIGHs were in that literal code: a `CancellationTokenSource` disposed outside the lock a waiter was
about to read `.Token` from, and `SignalTransportChange()` firing *before* the base class assigns `State`, so the
re-armed wait read the pre-transport state and would have raised `EndOfContent` **while paused** — precisely the
defect the mechanism existed to prevent. A MEDIUM found the plan's `ValidateMediaId` deny-list defeated by a
scheme-bearing id (`http:evil.example`), which RFC 3986 resolves as *absolute*, so PR 2's `new Uri(base, id)` would
have escaped the base — an SSRF hole inside the validator written to stop one. An allow-list backstop was added after
the named checks, so every pinned rejection reason is unchanged.

Both HIGHs were **unreachable in this PR** (nothing calls `PauseAsync`/`SeekAsync` on an event source until PR 3) and
were fixed anyway, because the code carried comments asserting behaviour it did not have — the `CLAUDE.md` §
Pre-Merge Review class this repo has now shipped five times. The comment-accuracy pass then caught three more
would-be instances **in the fixes themselves**, one of them written by the fixer and corrected in its own commit
rather than quietly amended.

**Left for later, deliberately:** `Label` has no length cap (belongs with PR 3's controller); a paused TTS source
keeps its 100 ms monitor poll (inherent to the existing loop); `IEventAudioSource.SeekAsync` stays `Task` rather than
`Task<bool>`, so a player-refused seek is not distinguishable at the seam — widening it breaks ADR D4's verbatim-lift
rule and is a Planner call for PR 3.

**No browser UAT, and none was owed** — PR 1 ships no user-visible surface. Three device-only checks are carried to
PR 6: that `Seek` repositions a real MP3, that `Time` advances, and that pausing a TTS source no longer reports
completion. All three degrade benignly if they fail.

---

## 2026-09-02 — ENC-4c: rotating the EncoderHud onto the right axis

**PR:** #526 · **Branch:** `fix/hud-vertical-geometry`

- `ENC-4` (below) shipped the HUD in horizontal quarters of the 1920 px width — correct for the knob
  **row** handoff Rev 3 described, and wrong for the panel that was actually built. The owner's drawing
  (`design/hardware/front-panel-layout_4.svg`) has four knobs in a **vertical column to the LEFT of the
  LCD** on a uniform 29.63 mm pitch. Rev 4 corrected the geometry, Rev 5 closed every question the
  rotation opened, and this is the 90° rotation of what shipped — no new component, copy or token.
- Cards anchor to `left: 24px` and band down the 720 px axis at **90 / 270 / 450 / 630**: the measured
  projections (93.05 / 271.02 / 448.98 / 626.95) deliberately rounded, because 3.05 px of worst-case
  deviation is 0.508 mm on the panel while the nearest wrong band is 178 px away, and a number like
  93.05 in a source file invites the next reader to re-measure it.
- **One definition of the panel:** `Radio.Core.Configuration.FrontPanelGeometry` — the four bands, the
  engraved names, the index→knob mapping and the drawing's px→mm scale, citing the drawing. §6.2 names
  four surfaces that need those facts, so a recut should move one line rather than five.
- **Vertical centring is clamped** to ≥ 8 px inside the viewport, and expressed on the independent
  `translate` property rather than `transform` so the entrance animation cannot drop it. `margin-left:
  -180px` had no vertical twin because card height varies — measured on the box, 178.5 / 92.5 / 113.5 px
  for the volume, frequency and track cards. At 178.5 px the volume card centred on band 90 would leave
  the top of the viewport, so the clamp is load-bearing rather than defensive.
- **One mirrored keyframe pair** (`encoderHudSlideInLeft` / `-OutLeft`), which is handoff §6.1's declared
  exception to its own "no new keyframes" rule, scoped to the Normal variant. The Sleep variant is placed
  by the drift wrapper rather than by an edge and keeps `.snackbar-enter`. **Do not "correct" this back**
  — it is declared, not drift. No new tokens; §6.9 is untouched.
- **§6.10, the phase contract:** an unrecognised HUD phase is now *not holding*. It renders nothing
  either way, so its only reachable effect was to suspend the 1500 ms dismissal timer and strand a card.
  `"Value"` needed its own arm to keep preserving `IsHolding`, or a turn mid-hold would collapse the
  progress ring — the obvious one-line edit would have fixed the stranding and broken the ring.
  `EncoderHudServiceTests.UnknownPhase_LeavesIsHoldingAlone` was **updated, not deleted**.
- Occlusion unchanged and accepted on every band (§6.2), with §6.7's mute invariant as the reason band 90
  is safe. The router's index→handler table is still un-remapped; that is `ENC-5` / `ENC-7`.
- **UAT** was driven through the kiosk's own CDP on `:9223` — the real Chrome on the real 1920×720 panel
  — measuring `getBoundingClientRect()` for three card variants × four bands. 12/12 pass: left anchor
  exact at 24.00 px, the clamp engaging on exactly bands 90 and 630 and nowhere else, and the
  mid-animation rect identical to the resting rect, which is the `translate`-survives-`transform` claim
  verified rather than asserted. 0 console errors.

**⚠ Found during UAT, not introduced here, and it affects every CSS change on this appliance.** The
kiosk was serving a **stale cached `design-system.css`** — 775 rules, missing the entire `ENC-4` block,
i.e. a copy predating a deploy earlier the same day. `radio-web` serves the stylesheet with `ETag` and
`Last-Modified` but **no `Cache-Control` header at all**, so Chrome applies heuristic freshness and can
serve it stale without revalidating; the deploy's kiosk relaunch does not help, because the profile's
HTTP cache survives the restart. **A CSS-only change can land, verify by SHA, and still not be on the
panel.** Left for Planner as a candidate row rather than fixed as a drive-by on the deploy path.

---

## 2026-09-02 — ENC-8: the encoder Settings surface

**PR:** #527

- Built the encoder Settings surface: five cards under System Config → Integrations → Rotary Encoders — Status,
  Device configuration (read-only, 24 fields × 4 encoders keyed to the cabinet engraving), Direction, Actions, and the
  pre-existing connection settings renamed so two different buttons are no longer both called "Save".
- Added `IRotaryEncoderProvisioning` as a second facet of the same `HidRotaryEncoderService` instance, retained
  sent/read-back state and timestamps, and the first code in this repo that sends `SaveConfig` (0x01).
- Flash staleness is decided by comparing a stored SHA-256 of the flashed bytes against the bytes the app would push
  now — a real byte comparison, because "differs from current design" is a claim about bytes that a timestamp cannot
  support.
- The mapping table is served from the router's own dispatch array, so `ENC-5`/`ENC-7`'s remap is a one-line edit.
- Deliberately absent: factory reset, pinned absent by a test. Factory tiers put one detent between silence and full.

**Task 4's hardware gate passed, and caught a HIGH regression — which is the argument for gates.** Unifying the boot
and maintenance read-back paths left the boot push awaiting a reply only the read loop could deliver, while the loop
could not start until the push returned. It does not hang; it times out on every attempt and settles in `Degraded`,
the tier that drops the volume clamp from 6 units per event to 2. Every boot would have left the volume knob sluggish
inside sealed furniture. Measured both ways on the appliance before and after the fix.

**Two defects were invisible to every automated gate in the repo.** The page could not deserialize its own API
response (`Radio.API` serializes enums as strings; the Web DTO enums had no converter), and the bUnit fixture uses a
hermetic rig that fails every HTTP call — so a null result is the *expected* test state and a green suite is
indistinguishable from a dead page. Separately, Radzen renders only the selected tab's body, so three markup-absence
assertions would have passed on markup that never contained the panel. Both were found by UAT and by deliberately
falsifying the new guards rather than trusting them.

**Durable lesson:** a negative assertion needs a positive one beside it, keyed to something that is not the copy under
test; and "one code path instead of two" is only safe once you have checked that the surviving path has all the
collaborators the deleted one had.

---

## 2026-09-02 — ENC-4: the EncoderHud

**PRs:** implementation `507b0d3`/`eb4005e`/`bd762d1`/`29acc01` (landed on `main` without a PR — see below), review + fixes #519

- Built `EncoderHud.razor` — one component, two hosts (`MainLayout`, and `Sleep.razor` with `Variant="Sleep"`).
  The card renders in the screen quarter above the knob that produced it (centres 240 / 720 / 1200 / 1680).
- Added a dedicated push channel for HUD updates. The existing `VolumeChanged` broadcast could not carry it: its one
  call site is a 500 ms change-detecting poller (2 Hz against a 100 ms requirement) and it reports what the volume
  now is, not which knob moved. The new channel coalesces to >= 50 ms, trailing-edge, final-value.
- Synthesised the long press host-side (the protocol reports raw press/release only): short fires on **release**,
  long fires **at** the 600 ms threshold while still held, and the release afterwards is inert.
- `ENC-4b`: turning the volume knob while muted unmutes and applies the delta in the same frame.
- Deliberately did **not** remap the router's index→handler table; that belongs to `ENC-5`/`ENC-7`. A test pins it.

**Process failure, recorded because it recurred.** The four implementation commits reached `main` **without a pull
request** and so were never reviewed pre-merge: a subagent switched the shared working tree off the feature branch
mid-cycle, and the branch was not re-verified before committing. The same thing happened a second time later in the
cycle, putting a fix commit onto an unrelated branch another session had just created. The owed review was then run
after the fact and found **three HIGH defects**, all live on `main` and on the appliance — a wrong mute state on
every volume-knob tap, a HUD card that could stay up indefinitely after a mid-hold disconnect, and an unguarded
event raise on a timer thread that could end the web process. All three fixed in #519.

**Durable lesson:** when subagents share the working tree, `git branch --show-current` must be re-checked
immediately before every commit and every push. A `git push` that prints `main -> main` is the last line of defence,
not the first.

---

## 2025-11-25 — Project Setup (Phases 0-1)

**PRs:** #2, #4, #13, #14, #16

- Created project plan with phased development approach
- Set up solution structure: Radio.Core, Radio.Infrastructure, Radio.API, Radio.Web
- Implemented Phase 1 configuration infrastructure (SQLite + JSON dual stores)
- Added ConfigurationManager CLI tool with Spectre.Console interactive menu
- CI/CD with GitHub Actions, CodeQL security scanning

**Key files:** `RadioConsole.sln`, all project scaffolding, `CLAUDE.md`

---

## 2025-11-26 — Core Engine & Audio Sources (Phases 2-5)

**PRs:** #17, #18, #20, #21, #22, #24

- **Phase 2:** Core audio engine with SoundFlow/MiniAudio integration — `SoundFlowAudioEngine`, `SoundFlowMasterMixer`, `TappedOutputStream`
- **Phase 3:** Primary audio sources — `FilePlayerAudioSource`, `RadioAudioSource`, `SpotifyAudioSource`
- **Phase 4:** Event audio sources — TTS (Google, Azure), audio file events
- **Phase 5:** Ducking & priority system — `IDuckingService`, fade policies
- Audio UAT testing tool for manual verification

**Key files:** `SoundFlowAudioEngine.cs`, `SoundFlowPlaybackService.cs`, `FilePlayerAudioSource.cs`, `TappedOutputStream.cs`

---

## 2025-12-03 — 2025-12-05 — API Layer & Queue System

**PRs:** #70-79, #83-86, #88-94, #96, #98-99

- Queue management API and `IPlayQueue` interface
- Spotify controller with search, browse, playback, OAuth PKCE
- Radio controller for RF320 device control
- Now Playing endpoint with structured metadata
- SignalR hub for real-time audio state broadcasting
- Metrics infrastructure with SQLite persistence
- Serilog file sink and system log retrieval API
- API codebase refactor — eliminated duplication, split God class

**Key files:** `AudioController.cs`, `SpotifyController.cs`, `RadioController.cs`, `DevicesController.cs`

---

## 2025-12-09 — 2025-12-12 — RTL-SDR & Web UI Foundation

**PRs:** #103, #105, #107, #110, #112, #114-128

- RTL-SDR audio streaming integration with `SDRRadioAudioSource`
- `RadioFactory` for device type selection (SDR vs RF320)
- Complete Web UI Phases 1-12: Navigation, Playback, Queue, Spotify Browse, File Browser, Radio Controls, System Config, Metrics Dashboard, Audio Visualization, Device Management, Play History
- MudBlazor Material 3 theme, LED fonts, 85/86 API endpoints wired
- bUnit testing infrastructure (35+ tests initially)

**Key files:** All `Radio.Web/Components/` pages, `RTLSDRCore/`

---

## 2025-12-19 — 2025-12-30 — UI Polish & Event Sources

**PRs:** #129-140

- Play History & Analytics UI
- User preference persistence via Configuration REST API
- Queue drag-drop, page transitions, log export
- Event Sources UI (TTS and File audio events)
- Audio engine initialization and startup preferences
- Google TTS voice validation

**Key files:** `Radio.Web/Components/Pages/`, various shared components

---

## 2025-12-31 — 2026-01-05 — Database Integration & Spotify

**PRs:** #142-164

- Database integration, SoundFlow playback improvements
- Configuration UI: device options, preferences, secrets management
- E2E UAT testing framework (43 tests)
- Librespot integration for native Spotify audio streaming
- Phase 12 Material 3 design system
- Queue/preferences persistence, file browser, virtual keyboard

**Key files:** `SpotifyAudioSource.cs`, various configuration stores

---

## 2026-01-07 — UAT Fixes

**PR:** #166

- Audio source switching fixes
- Fingerprinting observability improvements
- UI bug fixes from UAT testing

---

## 2026-02-06 — Fingerprinting & BT Planning

**PRs:** #171, #172

- `FpcalcUtility` for streamed audio fingerprinting (replaced AcoustID.NET — incompatible fingerprints)
- Bluetooth audio input implementation plan

**Key files:** `FpcalcUtility.cs`, `FingerprintTapModifier.cs`

---

## 2026-02-10 — Bluetooth Audio Pipeline

**PRs:** #174, #176

- Bluetooth A2DP audio pipeline: `LinuxBluetoothService`, `WindowsBluetoothService`, `BluetoothAudioSource`
- WASAPI loopback capture for Windows BT audio
- Album art file cache (`AlbumArtCacheService`)
- Cast audio streaming and pipeline optimizations
- Full playlist queue panel with state tracking and auto-skip
- Audio latency research and fixes

**Key files:** `LinuxBluetoothService.cs`, `WindowsBluetoothService.cs`, `BluetoothAudioSource.cs`, `GoogleCastOutput.cs`

---

## 2026-02-11 — Pi Deployment & Initial Testing

**PRs:** #177, #179

- Pi deployment scripts (`deploy-to-pi.sh`, `Deploy-ToPi.ps1`)
- Network binding fixes, missing file fixes
- Removed planning-with-files plugin

---

## 2026-02-12 — Raspberry Pi Debugging Marathon

**PRs:** #178, #180-193

A rapid-fire debugging session deploying and testing on physical Raspberry Pi hardware. 16 PRs in one day fixing issues discovered during real hardware testing:

1. **#178** — Missing `SqliteTTSVoiceRepository` implementation
2. **#180** — Embedded album art extraction via TagLib
3. **#181** — API test timeouts on Pi (use `CustomWebApplicationFactory`)
4. **#182** — D-Bus connection type for BT agent registration (system bus, not session)
5. **#183** — Google TTS `modelName` field required for newer voices
6. **#184** — Pi log noise from drive access and missing media directories
7. **#185** — Audio pipeline metrics + Cast MP3 on ARM64 (NAudio.Lame)
8. **#186** — BT connection drops, playlist race condition, enum serialization
9. **#187** — BluezAgent D-Bus methods not exported (A2DP authorization failures)
10. **#188** — Concurrent `MiniAudioEngine` crash during BT capture search
11. **#189** — Cast disconnect errors, BT event flooding/deduplication
12. **#190** — WirePlumber seat monitoring + BT capture routing for A2DP
13. **#191** — BT SBC codec pinning, visualization tap on MasterMixer, metrics concurrent read crash
14. **#192** — BT capture bridge via `BufferedSoundGenerator`, DI factory fixes
15. **#193** — Metrics flush crash from SQLite transaction/connection mismatch

**Key files changed:** `LinuxBluetoothService.cs`, `SoundFlowDeviceManager.cs`, `SoundFlowPlaybackService.cs`, `GoogleCastOutput.cs`, `HttpStreamOutput.cs`, metrics stores

---

## 2026-02-13 — Cast Streaming & BT UX (Phases 1-4)

**PRs:** #194, #195, #196

### Cast Audio Streaming
- **#194** — Cast audio stops immediately. Root cause: `TappedOutputStream` readers start at current write position with no buffered data. Fix: `CreateReader(readerId, lagBytes)` for immediate burst.
- Also: LAME `Flush()` writes end-of-stream data, killing Cast connection. Fix: flush HTTP output stream only.
- `StreamType.Buffered` vs `StreamType.Live` investigation: Live is correct for infinite streams; Buffered causes Chrome to download ~64KB and go FINISHED.
- CC1AD845 receiver app needs 2-3s to initialize — `LoadAsync` right after `LaunchApplicationAsync` silently fails.

### BT UX Improvements (#195)
- BT progress bar (AVRCP position/duration)
- BT next/prev buttons via `IMediaPlayer1` D-Bus
- BT album art download and cache
- BT play history with real AVRCP metadata
- AVRCP bidirectional volume sync (Linux)
- Cast idle session recovery
- Fingerprint skip after identification

### Dual-Service Deployment (#196)
- Split `radio-console.service` into `radio-api.service` + `radio-web.service`
- Directory layout: `/opt/radio-console/{api,web,data,logs}`
- Cross-compilation with `-f net8.0` for Linux ARM64

**Key files:** `TappedOutputStream.cs`, `GoogleCastOutput.cs`, `HttpStreamOutput.cs`, `BluetoothAudioSource.cs`, deploy scripts

---

## 2026-02-14 — Pi Hardware Verification

**PR:** #197

BT audio pipeline debugging on physical Pi:
1. `arecord` subprocess confirmed capturing real audio (strace: non-zero 24KB writes)
2. Serilog `Default: Warning` was hiding all audio pipeline logs — added `Radio: Information` override
3. Race condition in `GetAudioCaptureDeviceAsync` — two concurrent handlers with 0-timeout semaphore. Fixed with 30s timeout + `_activeGenerator` cache.
4. `SwitchPlaybackDevice` orphaned source components. Fixed with `PlaybackDeviceSwitched` event + subscriber re-attachment in `SoundFlowPlaybackService`.
5. Set device to playback-12 (bcm2835 Headphones), confirmed audio through soundbar

**Verified on Pi:** BT connect → arecord → generator → mixer → playback in <1s, AVRCP metadata flowing, volume sync at 68%, play history updated, audio output to soundbar via 3.5mm.

---

## 2026-02-15 — Phase 7: Audio Output UX

**PR:** #198 (pending review)

### 7.3 Cast Pause/Resume (fixed)
- `AudioController` called `PlayAsync()` for paused sources — for `FilePlayerAudioSource`, `PlayCoreAsync()` stops the current player and creates a new one from scratch. Fixed to call `ResumeAsync()` when source state is Paused.
- `TappedOutputStream.ReadForReader()` returned 0 bytes when empty, causing Cast HTTP stream to stall. Now returns PCM silence (zeroed bytes) to keep streams alive during pause.

### 7.2 Cast Mute Local Output (implemented)
- Decompiled SoundFlow DLL to confirm `SoundComponent.Process()` applies modifiers BEFORE volume. This means setting `MasterMixer.Volume = 0` silences local speakers while audio taps (HTTP/Cast, visualization, fingerprinting) still receive full-volume data.
- Added `IAudioEngine.SetLocalOutputMuted(bool)` — sets `_localOutputMuted` flag, updates playback device volume.
- DevicesController: mutes local on Cast selection, unmutes on local/HTTP selection.

### 7.4 BT Progress Bar (fixed)
- `NowPlayingPanel.razor` required both position AND duration to show progress. BT sources often have position but no duration. Now: seekable slider for seekable sources with duration, read-only progress bar for non-seekable sources with duration, elapsed time only when no duration available.

### 7.1 Device Filtering & Friendly Names (implemented)
- Added `DeviceDisplayOptions` config section: `HiddenDevicePatterns` (regex, default hides PulseAudio monitors) and `FriendlyNames` (substring → display name mapping).
- Applied in `SoundFlowDeviceManager.EnumerateDevices()` during device enumeration.

### 7.5 BT Next/Previous (logging improved)
- Code path is correctly wired: `AudioController` → `BluetoothAudioSource` → `LinuxBluetoothService` → `IMediaPlayer1.NextAsync()/PreviousAsync()`. Actual behavior depends on phone's AVRCP support. Upgraded logging from Debug to Warning with D-Bus path info.

**Key files:** `AudioController.cs`, `DevicesController.cs`, `SoundFlowAudioEngine.cs`, `TappedOutputStream.cs`, `NowPlayingPanel.razor`, `SoundFlowDeviceManager.cs`, `AudioOutputOptions.cs`, `IAudioEngine.cs`

---

## 2026-05-21 — 2026-05-22 — Cast stutter + BT audio stabilization research arc

**Branch:** `research/cast-bt-comparison`
**Commits:** 3b06f79, c5c2636, f0145d4, 1ed52e8, e3db645, bda7d7a, fbdfe4d (+ roadmap/work-log)

Research arc producing two comparison docs that contrast RTest's Cast and BT audio paths against known-good reference implementations. **No code changes** — explicit non-goal is implementation work. Output is consumed later by a separate plan.

### Cast doc — [`docs/research/2026-05-21-cast-stutter-comparison.md`](../docs/research/2026-05-21-cast-stutter-comparison.md)
- RTest HttpMp3 + DirectChannel vs SoundCloud (Shaka HLS-MSE) + Plex (HTTP MP3 byte-stream)
- 8 failure modes × 4 systems matrix; 10 pipeline rows × 4 systems
- §6 synthesis identified 5 patterns (DC's buffer depth 30× smaller than reference cluster; Web Audio scheduling on JS main thread unique to DC; shared transport for audio + metadata + control unique to DC; push-and-pace pacing model unique to DC; ABR only in SoundCloud)
- §7 had 8 speculative ideas; retrofitted with full 5-block measurement methodology in commit e3db645

### BT doc — [`docs/research/2026-05-22-bt-audio-stabilization.md`](../docs/research/2026-05-22-bt-audio-stabilization.md)
- RTest vs PW-stock (no RTest layer) + bluez-alsa + AOSP-BT (Bluedroid/Fluoride)
- 11 failure modes × 4 systems matrix; 11 pipeline rows × 4 systems
- §6 synthesis identified 6 patterns (RTest alone scrapes `pw-cli`; alone runs PW thread in-process with everything else; alone has codec invisibility; missing silent-quiesce watchdog; frame-alignment guard reveals API-surface mismatch; reference isolation is structural)
- §7 has 5 BT-specific ideas + 3 system-isolation ideas shared with cast doc; all with full 5-block measurement methodology

### Cross-cutting addition — concurrent-load discipline (commit bda7d7a)
MEMORY documents that audio distortion correlates with SSH activity, journald log queries, and SQLite reads on the Ubuntu N100 production box. Both docs were updated to:
- Add a host-resource-contention failure mode (Cast FM8, BT FM-BT-11)
- Add a host-resource-contention pipeline row
- Require every §7 probe to capture `PROBE-SYS-LOAD` (vmstat / iostat / pidstat / journalctl-rate / SSH-session-count) concurrently
- Run every probe under a two-scenario protocol (light vs heavy load) with the cross-scenario gap reported as a primary metric
- Add 3 system-isolation ideas (CPU affinity + SCHED_FIFO, synchronous-logging audit, gating background SQLite + fingerprint operations on audio-active state) in the cast doc, cross-referenced from the BT doc

### Roadmap addition
[`docs/ROADMAP.md`](../docs/ROADMAP.md) — new top-level roadmap, with this research arc as inaugural entry. Established the convention: research arcs do not produce implementation work; a separate plan in `docs/plans/` would scope any chosen ideas with the research's measurement blocks as acceptance criteria.

**Key files:** `docs/research/2026-05-21-cast-stutter-comparison.md`, `docs/research/2026-05-22-bt-audio-stabilization.md`, `docs/ROADMAP.md`

---

<!-- NEW SESSION ENTRIES GO ABOVE THIS LINE -->
