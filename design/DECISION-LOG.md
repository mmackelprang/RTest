# Decision Log

Architectural and engineering decisions made during development, with context, rationale, and alternatives considered. Updated by Claude Code as significant decisions are made.

---

## ADR-001: Audio Engine — SoundFlow over NAudio/PortAudio

**Date:** 2025-11-26
**Status:** Accepted
**Context:** Need a cross-platform audio engine for Raspberry Pi (Linux ARM64) and Windows development.

**Decision:** Use SoundFlow library (MiniAudio backend) with component-based audio graph.

**Alternatives considered:**
- **NAudio** — Windows-only, no Linux/ARM64 support
- **PortAudio** — C interop complexity, no built-in audio graph
- **SDL2 audio** — Low-level, would need custom mixer/effects pipeline

**Rationale:** SoundFlow provides cross-platform support, modern .NET API with `SoundComponent` base class, built-in device hot-plug detection, and component-based mixing. The `SoundComponent.Process()` chain (inputs → generate → modifiers → volume → output) enables clean tap points for streaming and visualization.

**Consequences:** Locked into SoundFlow's processing model. Required decompiling DLL to confirm modifier ordering (ADR-012).

---

## ADR-002: Dual Configuration Stores (SQLite + JSON)

**Date:** 2025-11-25
**Status:** Accepted
**Context:** Need a configuration system that's human-editable during development but robust in production.

**Decision:** `IConfigurationStore` abstraction with `JsonConfigurationStore` and `SqliteConfigurationStore`, switchable via `appsettings.json`.

**Alternatives considered:**
- **JSON-only** — Adequate for dev, poor concurrency and no ACID guarantees
- **SQLite-only** — Not human-editable, harder to bootstrap
- **Environment variables** — Insufficient for complex hierarchical config

**Rationale:** JSON for development (edit with any text editor), SQLite for production (ACID, backup consistency, concurrent access). Hot-swap without code changes.

---

## ADR-003: Encrypted Secrets with Tag Substitution

**Date:** 2025-11-25
**Status:** Accepted
**Context:** API keys (Spotify, Google TTS, AcoustID) must not appear in version control or plaintext config files.

**Decision:** `${secret:identifier}` tags in config files, resolved at runtime by `ISecretsProvider`. Encryption via ASP.NET Data Protection (machine-key based).

**Rationale:** UI can display and edit tags without exposing plaintext values. Familiar pattern from UserSecrets. Tags survive config backups safely.

---

## ADR-004: Layered Architecture (Core/Infrastructure/API/Web)

**Date:** 2025-11-25
**Status:** Accepted
**Context:** Need clean separation of concerns for a complex audio system with multiple I/O backends.

**Decision:** Four-layer architecture:
- **Core** — Pure interfaces, models, events (zero dependencies)
- **Infrastructure** — SoundFlow, BlueZ, WinRT, external API integrations
- **API** — REST controllers, SignalR hubs, middleware
- **Web** — Blazor Server UI

**Rationale:** Core interfaces enable mocking for tests. Infrastructure swappable (could replace SoundFlow). API and Web independently deployable (see ADR-017).

---

## ADR-005: Blazor Server over SPA Framework

**Date:** 2025-12-09
**Status:** Accepted
**Context:** Need a responsive UI for the radio console, running on Raspberry Pi in kiosk mode.

**Decision:** Blazor Server with MudBlazor Material 3 component library.

> ⚠ **Superseded on the component library only — the Blazor Server half of this ADR still stands.**
> **There is no MudBlazor in this repository.** `Radio.Web.csproj` references `Radzen.Blazor` and
> nothing else UI-wise, and a search for `MudBlazor` across `src/` returns zero files. When the swap
> happened was not recorded, which is why the claim outlived it. `CLAUDE.md` carried the same error
> until PR #518; this note exists so the second copy does not send another session to the wrong
> component library. Noted while shipping `ENC-8` (2026-09-02).

**Alternatives considered:**
- **React/Vue SPA** — Separate frontend codebase, additional build tooling, need API serialization
- **Blazor WASM** — Heavy download, poor ARM performance
- **.NET MAUI** — Overkill for kiosk display, less community support

**Rationale:** Single .NET codebase. SignalR built-in for real-time audio state updates. Server-side rendering works well on Pi (minimal client-side JS). MudBlazor provides Material 3 design system out of the box.

**Trade-off:** Requires persistent WebSocket connection — acceptable for kiosk mode (always-on, local network).

---

## ADR-006: Ring Buffer for Multi-Consumer Audio Streaming

**Date:** 2025-12-03
**Status:** Accepted
**Context:** Multiple consumers need the same audio data simultaneously: HTTP stream, Google Cast, visualization, fingerprinting.

**Decision:** Custom `TappedOutputStream` with circular ring buffer and independent per-reader cursors.

**Alternatives considered:**
- **Copy per consumer** — Memory wasteful, latency from copying
- **Pub/sub with queues** — Complex, variable memory usage
- **OS pipes** — Platform-specific, limited consumer count

**Rationale:** Single write, multiple reads without copying. Lock-free reads for real-time performance. Fixed 5-second buffer provides predictable memory usage. Per-reader cursors prevent slow consumers from blocking fast ones.

**Key detail:** Reader lag parameter (`CreateReader(id, lagBytes)`) provides historical audio burst for Cast startup (ADR-010).

---

## ADR-007: Native fpcalc over AcoustID.NET

**Date:** 2026-02-06
**Status:** Accepted
**Context:** Need audio fingerprinting for track identification (auto-skip, play history metadata).

**Decision:** Invoke native `fpcalc` binary via `Process`, parse JSON output. Replaced AcoustID.NET NuGet package.

**Alternatives considered:**
- **AcoustID.NET** — Initially used, but produces **incompatible fingerprints** that fail all AcoustID API lookups
- **Port Chromaprint C++** — Too much effort for a utility function

**Rationale:** Native Chromaprint library guarantees correct fingerprints compatible with AcoustID web service. Supports streamed audio via stdin (no temp files). Duration accuracy critical — AcoustID requires duration within ~3 seconds of actual track length.

---

## ADR-008: Platform-Specific Bluetooth Implementations

**Date:** 2026-02-10
**Status:** Accepted
**Context:** Need Bluetooth A2DP audio input. No cross-platform .NET BT A2DP library exists.

**Decision:** Two independent implementations behind `IBluetoothService`:
- **Linux:** BlueZ D-Bus (`Tmds.DBus`) + PipeWire/PulseAudio capture via `arecord`
- **Windows:** WinRT `AudioPlaybackConnection` + WASAPI loopback (NAudio)

**Rationale:** Linux (Pi target) gets full audio pipeline integration. Windows provides development-only BT audio.

**Key details:**
- `arecord` subprocess confirmed via `strace` to capture real non-zero audio data
- WASAPI loopback captures audio pre-mute — muting default endpoint silences speakers while loopback still captures
- Windows `AudioPlaybackConnection` requires MSIX sparse package identity

---

## ADR-009: Multi-Target Framework for Windows/Linux

**Date:** 2026-02-10
**Status:** Accepted
**Context:** `Radio.Infrastructure` needs WinRT APIs (Windows BT) and BlueZ D-Bus (Linux BT).

**Decision:** Multi-target `Radio.Infrastructure`:
- `net8.0` — Linux + cross-platform code
- `net8.0-windows10.0.19041.0` — WinRT APIs

Downstream projects (API, Web) use conditional single-target:
```xml
<TargetFramework Condition="$([MSBuild]::IsOSPlatform('Windows'))">net8.0-windows10.0.19041.0</TargetFramework>
<TargetFramework Condition="!$([MSBuild]::IsOSPlatform('Windows'))">net8.0</TargetFramework>
```

**Rationale:** Avoids runtime platform checks. Compile-time exclusion of platform-specific code via `<Compile Remove>`. `WINDOWS_TARGET` define constant for `#if` guards.

**Critical gotcha:** Cross-compilation for Pi requires `-f net8.0` to override Windows TFM.

---

## ADR-010: Google Cast — StreamType.Live for Infinite Streams

**Date:** 2026-02-13
**Status:** Accepted
**Context:** Cast audio disconnects after ~64KB when using `StreamType.Buffered`.

**Decision:** Use `StreamType.Live` for all Cast media loads. Required headers: `Accept-Ranges: none`, `Cache-Control: no-cache`.

**Alternatives considered:**
- **StreamType.Buffered** — Chrome downloads ~64KB then goes FINISHED (disconnects)
- **StreamType.None** — Undocumented behavior, unreliable

**Rationale:** Live tells Cast receiver "this stream never ends." Chrome keeps the HTTP connection open indefinitely. Combined with chunked transfer encoding, provides continuous audio.

---

## ADR-011: Never Flush LAME Encoder for Cast Streams

**Date:** 2026-02-13
**Status:** Accepted
**Context:** Cast audio disconnects after first buffer fill. Root cause: `mp3Writer.Flush()` calls `lame_encode_flush()`.

**Decision:** Only flush the HTTP output stream (`context.Response.OutputStream.Flush()`), never call `mp3Writer.Flush()`.

**Rationale:** LAME's flush writes end-of-stream markers (VBR header, padding). Chrome interprets these as "download complete" and disconnects. Flushing only the HTTP stream pushes buffered MP3 frames to the network without end markers.

**Trade-off:** Cannot cleanly finalize MP3 on stream close. Acceptable — Cast streams are infinite by design.

---

## ADR-012: Local Output Muting via MasterMixer.Volume

**Date:** 2026-02-15
**Status:** Accepted
**Context:** When casting to Google Cast, local speakers should be silent but audio taps (Cast HTTP, visualization, fingerprinting) must continue receiving data.

**Decision:** Set `_playbackDevice.MasterMixer.Volume = 0` to silence local output. Taps continue receiving full-volume audio.

**Verification:** Decompiled `SoundFlow.dll` (`SoundComponent.Process()`) to confirm processing order:
1. Process inputs
2. GenerateAudio
3. **Apply modifiers** (taps capture audio here)
4. **ApplyVolumeAndPanning** (volume applied AFTER modifiers)
5. MixBuffers to output

**Alternatives considered:**
- **GainModifier at end of chain** — Would affect taps too (wrong)
- **Device-level muting** — OS-specific, wouldn't work on all platforms
- **Separate mixer for taps** — Over-engineered, duplicates audio path

**Rationale:** Simplest correct approach. Volume=0 silences speakers; modifiers (including `FingerprintTapModifier` and visualization tap) still get full audio because they run before volume is applied.

---

## ADR-013: BufferedSoundGenerator for BT Audio Bridge

**Date:** 2026-02-12
**Status:** Accepted
**Context:** BlueZ audio capture (via `arecord` or PipeWire monitor) produces raw PCM in a push model. SoundFlow mixer needs a `SoundComponent` (pull model).

**Decision:** Generic `BufferedSoundGenerator<T>` bridges push→pull. Audio pushed into thread-safe queue, pulled during `GenerateAudio()`.

**Rationale:** Same pattern used for SDR audio (float path) and file player (short path). Handles sample rate/format differences via generic type parameter.

---

## ADR-014: AddHostedService Factory Pattern

**Date:** 2026-02-10
**Status:** Accepted
**Context:** `AddHostedService<T>()` does NOT register the concrete type in DI — other services can't inject `T`.

**Decision:** Register singleton explicitly, then use factory:
```csharp
services.AddSingleton<MyService>();
services.AddHostedService(sp => sp.GetRequiredService<MyService>());
```

**Rationale:** Services that need to be both `IHostedService` (for lifecycle) and injectable (for other services) must be registered as singletons first. The factory pattern avoids creating two separate instances.

---

## ADR-015: Audio Ducking Priority System

**Date:** 2025-11-26
**Status:** Accepted
**Context:** Event audio (TTS announcements, doorbell) must be heard over music.

**Decision:** Priority scale 1-10. Primary sources (music, radio) at priority 5. Event sources (TTS, audio events) at 8-10. Configurable fade policies: smooth (500ms), quick (200ms), instant (0ms).

**Rationale:** Volume ducking (lower music volume) is less jarring than hard pause. Priority ensures announcements always win. Fade policies configurable per source type.

---

## ADR-016: Device Filtering via Regex Patterns

**Date:** 2026-02-15
**Status:** Accepted
**Context:** Device enumeration returns internal/virtual devices (PulseAudio monitors, loopback) that confuse users.

**Decision:** `DeviceDisplayOptions` config section with:
- `HiddenDevicePatterns` — Regex list (default: `^Monitor of `) for devices to hide
- `FriendlyNames` — Substring→display name mapping

Applied during enumeration in `SoundFlowDeviceManager`, not at API or UI layer.

**Rationale:** Filtering at enumeration time means all consumers (API, Web, SignalR) see consistent device lists. Regex provides flexible matching. Default pattern hides PulseAudio monitor devices which are never valid output targets.

---

## ADR-017: Dual-Service Systemd Deployment

**Date:** 2026-02-13
**Status:** Accepted
**Context:** Pi needs both API (port 5000) and Web UI (port 5002). Single service with both is fragile.

**Decision:** Two systemd services:
- `radio-api.service` — Radio.API, port 5000, audio/BT capabilities
- `radio-web.service` — Radio.Web, port 5002, depends on radio-api

Shared directory: `/opt/radio-console/{api,web,data,logs}`

**Rationale:** Independent restart (API can restart without killing UI session). Different security profiles (API needs audio/BT groups, Web doesn't). Clear separation of concerns.

---

## ADR-018: SharpCaster v3.0.0 API Surface

**Date:** 2026-02-10
**Status:** Accepted
**Context:** Google Cast integration needed. SharpCaster v3 has breaking API changes from v2.

**Decision:** Use SharpCaster v3.0.0 with corrected namespace/class names:
- `Sharpcaster.Channels` (not `Interfaces`)
- `ChromecastLocator` (not `MdnsChromecastLocator`)
- `MediaChannel`/`ReceiverChannel` (not I-prefixed)
- Set both `ContentId` AND `ContentUrl` on Media object

**Rationale:** Only maintained .NET Cast library. v3 changes are documented here to prevent rediscovery.

---

## ADR-019: Cast Receiver Initialization Delay

**Date:** 2026-02-13
**Status:** Accepted
**Context:** `LoadAsync` right after `LaunchApplicationAsync` silently fails on CC1AD845 receiver.

**Decision:** Wait 2-3 seconds after launching the Cast application before loading media. Alternative: rely on metadata-triggered reload.

**Rationale:** CC1AD845 default media receiver needs initialization time. No error thrown — just silent failure. 2s delay is reliable across tested devices.

---

## ADR-020: Silence Injection for Paused Cast Streams

**Date:** 2026-02-15
**Status:** Accepted
**Context:** When audio source pauses, `TappedOutputStream.ReadForReader()` returns 0 bytes, causing Cast HTTP stream to stall and eventually timeout.

**Decision:** Return PCM silence (zeroed byte arrays) when ring buffer is empty. Reader position is NOT advanced — when real audio resumes, it plays immediately without gaps.

**Rationale:** Keeps HTTP connection alive. Cast receiver continues playing (inaudible silence). Smooth resume when source unpauses. Alternative (close/reopen stream) causes audible gaps and metadata loss.

---

## ADR-021: PlayAsync vs ResumeAsync for Source State

**Date:** 2026-02-15
**Status:** Accepted
**Context:** `AudioController` called `PlayAsync()` for all play requests, including resuming paused sources. For `FilePlayerAudioSource`, `PlayCoreAsync()` stops current player and creates new one from scratch.

**Decision:** Check source state before calling play. If `Paused`, call `ResumeAsync()` instead of `PlayAsync()`.

**Rationale:** `PlayAsync()` is destructive — it stops and recreates the audio player. `ResumeAsync()` continues from the paused position. Cast streams, visualization, and fingerprinting all benefit from uninterrupted resume.

---

## ADR-022: GV Voicemail + SMS Integration (gvbridge consumer)

**Date:** 2026-06-20
**Status:** Proposed (Architect)
**Summary:** `Radio.Web` consumes RotaryPhone's `gvbridge` voicemail + GV-SMS contract directly at `radio:5004` (no Radio.API proxy), extends the existing `GvBridgeApiService` + `PhoneHubService` (`/hub`), plays voicemail audio via a native `<audio>` element pointed at an absolute `radio:5004` Range-capable URL (NOT the no-Range album-art proxy), polls `/api/gvbridge/status` via a new `GvBridgeStatusService` singleton, and gates SMS send behind `RotaryPhone:Gv:SendEnabled` (default off). Future `X-RotaryPhone-Auth` header wired as an off-by-default seam.

**Full ADR:** [`design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md`](decisions/2026-06-20-gvbridge-voicemail-sms-integration.md)

---

## ADR-023: Pin SQLitePCLRaw 3.0.x to clear NU1903 (GHSA-2m69-gcr7-jv3q)

**Date:** 2026-06-20
**Status:** Accepted
**Context:** `dotnet build -c Release` (and `deploy/Deploy-ToLinux.ps1`) failed solution-wide on **NU1903** / **GHSA-2m69-gcr7-jv3q** (CVE-2025-6965): `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 ships SQLite < 3.50.2 (aggregate-term memory-corruption bug). With `NuGetAudit` (default on in .NET 10) + `TreatWarningsAsErrors`, the advisory is a build error across all ~16 SQLite-referencing projects. The advisory has **no patched 2.1.x release** — the 2.x `lib.e_sqlite3` line is deprecated. `Microsoft.Data.Sqlite` (even 10.0.9) only declares `bundle_e_sqlite3 (>= 2.1.11)`, which NuGet resolves to the vulnerable 2.1.11 floor, so a `Microsoft.Data.Sqlite` bump alone does **not** fix it.

**Decision:** Add an explicit direct `PackageReference` to `SQLitePCLRaw.bundle_e_sqlite3` **3.0.3** and `SQLitePCLRaw.core` **3.0.3** in the four projects that directly reference `Microsoft.Data.Sqlite` (Radio.Configuration, Radio.Metrics, Radio.Fingerprinting, Radio.Infrastructure). The 3.0.x line replaces the deprecated `lib.e_sqlite3` with `SourceGear.sqlite3` (>= 3.50.4.5, i.e. SQLite past the 3.50.2 fix) and carries no advisory.

**Alternatives considered:**
- **Bump `Microsoft.Data.Sqlite` only** — rejected; its `>= 2.1.11` floor still resolves the vulnerable native lib.
- **Targeted `NuGetAuditSuppress` for NU1903** — rejected; a real patched version exists (3.0.x), so suppression would have masked a genuinely fixable CVE. Reserved as fallback only if no patched version had existed.
- **Global `NuGetAudit=false`** — rejected; disables auditing wholesale and hides future advisories.

**Verification:** Full-solution `dotnet build -c Release` (NuGetAudit enabled, no override flags) → 0 errors, no NU1903. Resolved native dependency is `SourceGear.sqlite3` 3.50.4.5; `lib.e_sqlite3` no longer appears in any restore graph. Test suite green except pre-existing Windows-only (`libsamplerate.so.0`) and `Category=Integration` (live NWS API) failures, both unrelated and handled by CI.

---

## ADR-024: GV Mark-Read / Durable Read-State — GV write-through (supersedes ADR-022 D4)

**Date:** 2026-06-20
**Status:** Accepted (Architect)
**Supersedes:** ADR-022's UI-local read-state stance (`VoicemailItemDto.IsRead` note, §10 mark-read stub, §12 open question #3).
**Summary:** RotaryPhone ratified the durable mark-read contract. Read-state is now **GV write-through — Google is the single source of truth, no local read-state store on either side.** Two idempotent routes (`POST /api/gvbridge/voicemail/{id}/read`, `POST /api/gvbridge/sms/threads/{threadId}/read`), body `{ "isRead": bool }`, each returning the updated frozen DTO (`200` applied-or-no-op, `404` unknown, `502` upstream-GV — keep optimistic flip and reconcile). A unified `ReadStateChanged` event rides the existing `/hub` (broadcast unconditionally incl. originator → consumer de-dupes by `(id/threadId + isRead)`). Consumer delta (GV-4, behind `RotaryPhone:Gv:MarkReadEnabled` default-off, builds now): wire existing `MarkVoicemailReadAsync` + add `MarkSmsThreadReadAsync` on `GvBridgeApiService`, add `ReadStateChangedDto` + handler on `PhoneHubService`, drop UI-local read-state. Unread best-effort (v1 sends `isRead:true` only, toggle hidden). Auth: no new posture — covered by the existing `/api/gvbridge/*` prefix gate.

**Full ADR:** [`design/decisions/2026-06-20-gv-mark-read-durable-readstate.md`](decisions/2026-06-20-gv-mark-read-durable-readstate.md)

---

## ADR-025: RDS ticker — JS/WAAPI offset-preserving engine + decoder complete-before-partial RT policy

**Date:** 2026-07-19
**Status:** Accepted
**Context:** The kiosk RDS marquee (RdsCard → RdsScrollMarquee, HANDOFF-rds-accumulating-scroll + HANDOFF-rds-inline-scroll-revision) showed four production bugs: (1) large position "jerks" on every RT append — the pure-CSS keyframes animated `translateX(100%→-100%)` (percent of the track's own width) with a per-render `--scroll-duration`, so any text change reinterpreted the elapsed fraction against new geometry; (2) truncated/garbled text — the decoder's `RtConfirmThreshold=2` counted consecutive identical assemblies (~0.5 s apart) and published stalled partial prefixes ("Simo") and CRC-aliased corruption ("GivJ It Away", "MaIonna" — verified in radio-20260717.txt), the buffer front-trimmed mid-chunk at its cap, and the static-fit branch used a 7 px/char × 420 px approximation against real in-card 14 px/0.18em typography (measured 10.2 px/char, 418 px container); (3) leak concerns over a long kiosk session; (4) effective scroll speed varied with buffer length (percent keyframes travel 2×trackWidth over a duration computed for container+trackWidth).

**Decision:** Three coordinated changes. (a) **Decoder:** extract RT assembly into `RadioTextAssembler` with per-character double-receive (a slot's value must be decoded twice — one-off CRC-aliased corruption cannot land) and complete-before-partial confirmation (complete = 64 chars via full reception or 0x0D fill confirms at threshold 2; incomplete prefixes need 32 stable assemblies ≈ two full segment cycles, so reception gaps repair before publishing while broken-encoder stations still display in ~10-30 s). (b) **Buffer:** chunk-aware `RdsAccumulatingScrollBuffer` — whole-chunk front eviction (head always a chunk boundary), prefix-extension and same-length minor-correction chunks replace the last chunk in place. (c) **Marquee:** replace the CSS keyframes with `wwwroot/js/rds-marquee.js` driving the same transform via the Web Animations API (still compositor-thread — the HANDOFF §4 N100 rationale holds); the engine owns the offset explicitly, `MarqueeTextDiff` classifies each text change (Continuation/InPlaceSwap/Reset) and the engine restarts its leg from the preserved offset with trimmedChars × measured-char-width compensation; static-fit and speed are driven from real measurements; pause/reduced-motion/SR-mirror behaviours preserved; instances keyed by C#-generated id so dispose survives DOM detach. Plus `RdsScrollSpeedPolicy`: 1.5× catch-up above 75 % buffer fill.

**Alternatives considered:**
- **Keep CSS animation, suppress more re-renders** — already done (PR-era ShouldRender guards); cannot help because any REAL append must change track width, which is itself the restart/snap trigger.
- **rAF-driven transform** — works but runs per-frame on the main thread; WAAPI keeps per-frame work on the compositor and JS only runs at text changes / leg boundaries.
- **Raise RtConfirmThreshold only** — rejected; consecutive-assembly counting confirms any stable state (including corrupt chars that sit in the buffer) regardless of threshold, and higher thresholds punish legitimate complete messages.

**Verification:** 12 `RadioTextAssemblerTests` (direct group 2A/2B frames: loss, corruption, A/B rotation, unterminated), 32 buffer tests incl. a 5 000-append leak bound, 13 `MarqueeTextDiffTests`, bUnit interop-contract tests (init-once / append-with-trim / swap / reset / dispose-on-unmount / zero calls on telemetry ticks), and a Playwright harness against the real engine in Chromium: measured 40.1 px/s at configured 40, append offset 48.8→48.8 (no snap), front-trim glyph position 84.7→84.7 px (invariant), hover pause drift 0.00 px, clean dispose (docs/uat/rds-marquee-harness-results.png). Final confirmation needs the live kiosk + real RDS station (owner UAT).

**Consequences:** New-message display latency rises from ~2-5 s to ~5-15 s (two segment cycles) — the price of never publishing partial/corrupt text. HANDOFF-rds-accumulating-scroll §6.f ("accept the restart jump for v1") is superseded — the JS engine is that anticipated follow-up. Handoff §6.e's drop-oldest-CHARS is refined to drop-oldest-CHUNKS.

---

## ADR-026: Suppress GHSA-pgww-w46g-26qg (AngleSharp, NU1902) in Radio.Web.Tests

**Date:** 2026-07-19
**Status:** Accepted
**Context:** A newly published advisory against AngleSharp 1.2.0 (transitive via `bunit.web` 1.40.0) turns NU1902 into a restore **failure** on any fresh restore under `TreatWarningsAsErrors` (main only kept building via cached restore assets; CI and new worktrees break). Per ADR-023's hierarchy, upgrading to a patched version is preferred — but AngleSharp 1.3+ changes the `IHtmlCollection` ABI and bunit 1.40 throws `MissingMethodException` at runtime (verified with 1.5.2), and no bunit release carrying a patched AngleSharp exists yet.

**Decision:** Targeted `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-pgww-w46g-26qg" />` in `Radio.Web.Tests.csproj` only — the ADR-023 sanctioned fallback for exactly this case (patched version exists but cannot be adopted). Risk is nil in practice: AngleSharp only parses our own bUnit render output in tests; it never sees untrusted input and ships in no production artifact. Revisit when bunit publishes a release on AngleSharp ≥ patched.

---

## ADR-027: Clear NU1903 (System.Security.Cryptography.Xml) by direct-pinning 10.0.10

**Date:** 2026-07-29
**Status:** Accepted
**Context:** Five high-severity advisories landed against `System.Security.Cryptography.Xml` 10.0.8 — GHSA-23rf-6693-g89p, GHSA-8q5v-6pqq-x66h, GHSA-cvvh-rhrc-wg4q, GHSA-g8r8-53c2-pm3f, GHSA-mmjf-rqrv-855v. The package is **transitive**: `Microsoft.AspNetCore.DataProtection` 10.0.8 declares it. With `NuGetAudit` on by default in .NET 10 plus `TreatWarningsAsErrors`, each advisory becomes an NU1903 **restore error** — 10 errors on a `Radio.Web` build, surfacing via `Radio.Configuration` (direct `DataProtection` reference) and `Radio.Infrastructure` (inherits it by ProjectReference). As with ADR-026 this breaks only a **fresh** restore; `main` reproduced it identically, and CI stayed green because the Linux runner restores only the `net10.0` TFM against cached assets.

**Decision:** ADR-023's **preferred** branch — upgrade, not suppress. All five advisories are first patched in 10.0.10. A `NuGetAuditSuppress` was rejected outright: ADR-023 reserves it for when a patched version exists but cannot be adopted (ADR-026's AngleSharp/bunit ABI break), which does not apply here.

Two upgrade routes were implemented independently and in parallel, which is worth recording because the losing one is the more obvious:

- **Shipped (PR #459, `d64b6d5`):** direct-pin `System.Security.Cryptography.Xml` 10.0.10 in `Radio.Configuration.csproj`. A direct reference overrides the transitive resolution, and the pin propagates to every downstream project through the existing ProjectReference graph. This follows the **SQLitePCLRaw pin precedent already in that same csproj**, so the file now has one consistent idiom for "hold a transitive dependency at a known-good version."
- **Rejected (implemented on `feat/bell-failure-surfacing`, then dropped):** bump the parent `Microsoft.AspNetCore.DataProtection` 10.0.8 → 10.0.10 in `Radio.Configuration` and `Radio.Tools.ConfigurationManager`, plus `DataProtection.Extensions` in two test projects. Equally correct and arguably tidier in principle — it avoids naming a package we do not consume directly — but it touches four files, needs the four to be kept in lockstep, and duplicates a fix already on `main`. Carrying both would have left two competing mechanisms for one advisory.

The rule for next time: **prefer the direct pin**, matching the SQLitePCLRaw precedent, unless the parent bump also brings something we independently want.

**Verification:** `dotnet build RadioConsole.sln -c Release` with NuGet auditing **enabled** and no override flags → `0 Error(s)` (was 10 NU1903 errors). `Radio.Web.Tests` 835/835, `Radio.Configuration.Tests` 115/115.

---

## ADR-028: GV SMS Send — real contract, error taxonomy, and outbound echo de-dupe (supersedes ADR-022 D7)

**Date:** 2026-07-30
**Status:** Accepted (provenance verified 2026-07-31) — ready for Builder as GV-5; ships behind `RotaryPhone:Gv:SendEnabled`, default OFF.
**Supersedes:** ADR-022 D7 in full (request shape, response shape, error model, and the "confirm `SendSmsResponse` before wiring" item). ADR-022 §8 config surface unaffected.
**Summary:** Derived from RotaryPhone's as-built `GvSmsController.cs` rather than their docs, which are stale. Four defects found in GV-3's send path, the first fatal: our `SendSmsRequest(ThreadId, Text)` omits **`ToNumber`**, so their normalizer sees `null` and **every send returns `400 invalid_number`** — send was never functional, only silent. Decisions: send the real four-field request `{ toNumber, text, threadId, clientCorrelationId }` and never route a thread id into `toNumber`; wire `ClientCorrelationId` (their handler uses it verbatim as the echo's `Id`, so tier-1 id matching works and the bubble id never re-keys — optimistic ids become `rc:{guid}` not `temp-{guid}`); map the full **nine-code** taxonomy (`invalid_text` was missing from the previously logged eight) to typed exceptions and bubble treatments, with `send_disabled` (409) treated as an **availability state, not a failed send**; subscribe to the **`SmsSent`** echo channel (we never did — GV-3's de-dupe was unreachable dead code); and de-dupe idempotently keyed by exact `Id` then `(Outbound, normalized counterparty, ordinal-equal text, |ΔSentAt| ≤ 120s)`, replaced **in place**. §8 (added 2026-07-31) adds thread **reply-ability**: ~1/3 of inbound threads are short codes or opaque sender IDs that cannot be replied to; classify client-side via a pure `GvCounterparty` static (no DTO change), gate compose before the POST, never render an impossible send as a failed one.

**Full ADR:** [`design/decisions/2026-07-30-gv-sms-send-contract.md`](decisions/2026-07-30-gv-sms-send-contract.md)

---

## ADR-029: GV media as ducked event playback — one seam for voicemail audio and TTS (supersedes ADR-022 D4)

**Date:** 2026-08-03 (**Amendment 1** applied same day)
**Status:** Proposed — Amendment 1 applied (owner answers + Designer round). Ready for Planner.
**Supersedes:** ADR-022 **D4** in full (voicemail via a native `<audio>` pointed at `radio:5004`); narrowly amends **D1** (its boundary rule still governs the GV *read* path, not the *audio* path).
**Context:** The owner asked for three `/phone` changes: **A** voicemail through the console's real output chain with ducking, **B** a play button on a text that speaks it via TTS, **C** canned responses replacing freeform compose (Designer-led). A and B are one architectural problem — *hand a GV media item to the audio engine as a ducked, user-attended event* — and get one mechanism. D4 was correct on its own terms (native `<audio>` bought free HTTP Range seeking); what changed is its stated premise, that voicemail has "no audio-engine involvement." A browser `<audio>` is a second audio path the mixer, ducking service and output chain know nothing about, and may be inaudible on Cast or exclusive-mode outputs.

**Decision:** A new Core seam **`IEventPlaybackService`** beside (not inside) `IAnnouncementService` — the latter stays fire-and-forget for *unattended* announcements; the new one serves *attended* playback needing a handle, transport and state. Exposed as `POST /api/audio/events` + seek/pause/stop; `Radio.Web` calls **Radio.API**, not gvbridge, for anything audible. The request is a closed discriminated set with **deliberately asymmetric arms** — speech carries **literal text** (already in Web's hands, small), voicemail carries a **`(kind, id, durationSeconds)` reference** (large, remote, and a caller-supplied URL would be an SSRF primitive). Radio.API resolves the URL from its own config and fetches via a new `GvMediaClient` in `Radio.Infrastructure/External/`, modelled on the existing `PhoneContactLookupService`, into a bounded LRU cache at `./data/gvmedia/` — **the cache is blackout mitigation, not an optimization**: GV auth is dead ~9 min in every 20, so a replay has ~45% odds of 502ing if it went back to the network. Materializing to a local file makes voicemail an ordinary `AudioFileEvent` and makes **HTTP Range irrelevant** — seeking is local, so D4's whole rationale is satisfied on the correct side of the wire. `IEventAudioSource` gains `Position`/`IsSeekable`/`SeekAsync`/`PauseAsync`/`ResumeAsync` **copied verbatim from `IPrimaryAudioSource`**, which already declares them all. State is global (one engine, one set of speakers), broadcast on the existing `/hubs/audio` via `AudioStateUpdateService` → `AudioStateHubService` → `AudioStateStore` — the pattern already working for the radio and simply unwired from the phone surface — with **no position tick**: the snapshot carries an anchor and the client interpolates, because steady-state churn is audible on the N100.

**Two findings that changed the design.** (1) **Priority currently arbitrates nothing.** `DuckingService` is binary and reference-counted — the first event fades the primary to a fixed global 20%, every subsequent concurrent event changes nothing, and `GetActiveEventsByPriority`/`StopAllDuckingAsync` have zero non-test callers. `INTEGRATIONS.md`'s "higher priority announcements can interrupt lower priority ones" is false today and every `SetPriority` call is decorative. So attended playback at **priority 6** is the first load-bearing use of priority: attended-replaces-attended, **preempted (stopped, not paused) by ≥ 8**, sub-8 keeps mixing (a recorded pre-existing wart). Mechanism: `DuckingService.StartDuckingAsync` must raise `DuckingStateChanged` on *every* call rather than only on transition — safe, since its lone subscriber acts only on `!IsDucking`. (2) **Lifecycle is the sharpest cost of going server-side**: playback no longer dies with the `<audio>` element, so three defenses — a hard `MaxPlaybackSeconds` cap (the only real guarantee, and poll-free), an explicit stop, and a net-new `CircuitHandler` backstop (weakest; Blazor holds circuits ~3 min past tab close).

**Auth:** routing server-side **closes carried risk #3's audio clause** — but only because Radio.API is given the credential and a handler; it would *not* close under the rejected opaque-URL design. RotaryPhone can now auth-gate the audio endpoint and the standing cross-repo ask should be **withdrawn**. Cost: Radio.API has no `AddHttpClient`/`DelegatingHandler` infrastructure today, and the auth key is duplicated across two services' config.

**Amendment 1 (2026-08-03) — five decisions moved; see the ADR's §0 for the full log.**

- **Speech engine REVERSED, and further than a config flip (new D10).** The original §9 pinned message speech to local `espeak-ng` for privacy. The owner reversed it: *"make sure the text messaging uses the currently selected TTS engine."* Resolved against the code, **"currently selected" is `TTS:DefaultEngine`** — read at the tree's only engine-resolution site, `TTSFactory.cs:71` — **not** `TTSPreferences.LastEngine`, which has zero readers (selecting an engine from it would have re-pinned espeak-ng by accident). ⚠ **Corrected 2026-09-03 by `TTS-9`.** This parenthetical originally read *"zero readers, zero writers, and binds a section with no such key (so it is permanently `ESpeak`)"*, and two thirds of that was wrong. `LastEngine` **is** written — `PreferencesPersistenceService.cs:102` serialises the whole `TTSPreferences` object into the `TTS` section — and the section **does** carry the key: the live appliance store holds `TTS:LastEngine|ESpeak` and `TTS:LastVoice|en`, read directly off the box on 2026-09-03. The value round-trips rather than being absent. **The load-bearing half was right and still is: it has no readers, nothing parses it into a `TTSEngine`, and it is not the engine-resolution site** — which is also why removing eSpeak needed no config-store migration. Its *default* is now `string.Empty`; the stored value survives until overwritten. **`GvMedia:SpeechEngine` is deleted, not redefined**; the only override is per-request. Azure is a third fully-implemented engine the draft never mentioned; an unavailable engine **fails with a stated reason and never silently substitutes another**, because a fallback in the ESpeak→cloud direction would ship a private SMS body to a party the owner did not select. The privacy analysis is **kept as an accepted, owner-made trade** — it did its job by forcing an explicit choice. Practical upshot: message speech now sounds **identical** to announcements, removing the tonal-mismatch concern, and gaining a cloud round trip per play (there is no cache on the speech path).
- **Navigate-away rule FLIPPED — playback survives navigation (D7 rewritten).** The Designer supplied the persistent transport chip the rule was waiting for, in `.topbar-primary` — **not** `NowPlayingDock`, which `MainLayout.razor:878` hides on Home. The flip forced three further changes: the circuit backstop is re-scoped from *owner circuit* to **last circuit** (the original would have stopped audio ~3 min after any kiosk refresh), **`OwnerToken` is deleted** along with the ownership model it served, and **`/sleep` needs its own rule** — it runs under `EmptyLayout` with no topbar and the console navigates itself there on an idle timer, so entering it stops playback unless that surface grows a control. **The max-duration cap is unweakened**: it is armed server-side and depends on no client, so it still guarantees the console cannot get stuck.
- **Priority re-anchored (§6.1).** A live check falsified the anchor: `PhoneIntegration:Enabled` is **"never enabled"** — `false` in the only appsettings that declares it, no Production override, no systemd `Environment=` override, introduced by `8d2a2ab` and never flipped. `PreemptAtPriority` stays **8**, now anchored on the two *live* occupants of 8 (`DuckingService.DefaultEventPriority`, `NotificationsController`'s `?? 8`) rather than on the dormant ring. Live consequence, newly stated: an external announcement at its default priority 8 **stops** a voicemail.
- **Voicemail cache ENABLED** — voicemail audio at rest under `./data/gvmedia/` is owner-accepted; `CacheMaxMegabytes = 0` is an escape hatch, not the default.
- **Initial-sender OUT of scope — the console is reply-only.** New-recipient compose is removed. Non-obvious consequence: **`toNumber` stays** in ADR-028's request (reply mode needs it too, or their server 400s), but it now has a **single source**, which promotes `GvCounterparty` from a composer gate to the send path's sole addressing dependency — a classification bug becomes a send bug. ADR-028's latent duplicate-thread-row fix demotes from *required* to *defensive*, since we can no longer create a conversation.

**Consequence for C:** the send contract, `SmsSent` echo and reply-ability gating are **unaffected** — but canned responses **invalidate a probability assumption** inside ADR-028 §4.4's accepted risk: drawing `text` from a fixed set of five or six strings makes "two identical sends to the same counterparty inside 120s" ordinary rather than rare, and the poller's re-surfaced copy always falls through to the fuzzy tier. GV-5's reconciler must therefore match **one-to-one** (a poller copy consumes at most one un-reconciled bubble), with a regression test.

**Full ADR:** [`design/decisions/2026-08-03-gv-audio-through-engine.md`](decisions/2026-08-03-gv-audio-through-engine.md)

---

## ADR-029 arc: two decisions the `PHN-1b` plan closed for every PR in the arc

**Date:** 2026-09-02
**Status:** Accepted — binds ADR-029 PRs 3-7
**Amends:** [ADR-029](decisions/2026-08-03-gv-audio-through-engine.md) §4.2 and §10.2 (the first item overrides them)
**Context:** `PHN-1a` (PR 1) shipped behaviour that contradicted the ADR in one place and left an "open question for PR 3" in a doc comment in another. Both were arc-level questions rather than PR-local ones, so `PHN-1b` (PR 2) settled them for the whole arc rather than letting each PR re-decide.

**Decision 1 — `MaxSpeechChars` is a REJECTION, not a truncation. ADR-029 §4.2 is overridden.**
§4.2 says over-length speech is *"truncated with a spoken tail"* and §10.2's config table repeats it; PR 1 shipped `EventPlaybackRejection.TextTooLong` instead, and that is what stands. Three reasons, in order of weight: (1) §4.2's own governing rule is that **utterance composition belongs to `Radio.Web`**, and truncating with a spoken tail *is* composition — it changes what is said and adds words the caller did not write, so doing it in Radio.API contradicts the section it is written in. (2) A truncating server that returns `200` is the same untruth PR 1 already refused when it made a non-seekable `SeekAsync` **throw** rather than no-op per the ADR: a caller that posts 8 000 characters, gets `200`, and hears 1 000 has been misled in exactly the same way. (3) `EventPlaybackRequest.Validate` is a pure method on a `sealed record` and would have to become a mutator; truncation cannot be expressed as a rejection reason.
**Consequences:** PR 3 maps `TextTooLong` → `400` with the named reason, like every other rejection. **`PHN-3` owns truncation** — client-side and visible, before the post, in `GvSpeechText.ForMessage`, which is also the only place a spoken tail can be composed in the same voice as the rest of the utterance. The word "truncation" does not appear in this arc's server code. ⚠ A reviewer citing ADR §4.2 to re-add it should be pointed here.

**Decision 2 — `IEventAudioSource.SeekAsync` stays `Task`. Closed as "no", not deferred again.**
PR 1's doc comment on `IEventPlaybackService.SeekAsync` described widening to `Task<bool>` as *"an open question for PR 3"*. It is closed. Widening breaks D4's only justification — that the five signatures are copied **verbatim** from `IPrimaryAudioSource` — leaving either two seek shapes in the codebase or a change to `IPrimaryAudioSource`, which drags in `FilePlayerAudioSource`: a live primary-source path with a persisted resume position hanging off the same field, out of scope, and logged with its own UAT requirement in `design/FUTURE-WORK.md` §14a. The information is not lost, it arrives by a different route: `Position` reads through to the player, so a refused seek shows up as **an anchor that did not move** in the next snapshot and the scrubber snaps back — the correct user-visible behaviour, delivered over the broadcast mechanism that already exists.
**Consequences:** `PHN-1b` corrected that comment in the same PR (doc-only, no behaviour) rather than leaving `main` carrying a statement this decision already knew to be untrue — the `CLAUDE.md` § Pre-Merge Review failure class this repo has now shipped five times.

**Plan of record:** [`design/plans/PHN-1b-gvmedia-client-cache-and-auth.md`](plans/PHN-1b-gvmedia-client-cache-and-auth.md) §0.3

---

<!-- NEW ENTRIES GO ABOVE THIS LINE -->
