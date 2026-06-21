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

<!-- NEW ENTRIES GO ABOVE THIS LINE -->
