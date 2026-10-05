# Bluetooth audio stabilization — RTest vs known-good reference implementations

**Status**: research framework — *modes + pipeline rows defined, cells empty*. Each cell is filled in during the research execution pass.

**Author**: Mark + Claude (drafting pass 2026-05-22)

**Motivation**: RTest's Bluetooth audio path (phone → A2DP sink → PipeWire capture → SoundFlow mixer → local speakers) has two well-documented production failures and a long tail of less-documented ones:

- **Long-running capture device lifecycle bug** ([MEMORY](../../C:/Users/mark/.claude/projects/D--prj-RTest-RTest/memory/MEMORY.md), Known Bugs) — after days of uptime and several source switches, the PipeWire capture stops delivering audio. The PW node is still present and the generator is still attached, but `OnProcess` is no longer being invoked. Restart of `radio-api` clears it.
- **`autoSwitchOnConnect` retries on missing PW node** — when a phone pairs but the BT transport hasn't yet materialized a PipeWire capture node, RTest auto-switches to the BT source and the source retries acquisition for hours, contributing to the long-uptime degradation above.

The user reports that **stock PipeWire** (the same substrate, without RTest's wrapping) and **other open-source BT-audio appliances** (raspotify, balena BT-speaker images, bluez-alsa-based audiophile distros) do not exhibit these specific failures. Anecdotal — but a strong-enough signal to investigate the architectural differences.

Goal: understand what known-good implementations do differently along the *same axes where RTest fails*, so that any future RTest change is informed by observed reference behavior rather than guessed at.

**Explicit non-goal**: no RTest implementation work falls out of this document. The §7 "things RTest could try" section is *research output*, not a plan or commitment. A separate plan would consume any one of those ideas later if and when the team chose to act on it. **Every speculative idea in §7 carries a measurement-methodology block** so the change, if implemented, can be objectively demonstrated to help — or not (see §3 measurement-discipline tier).

---

## 1. Scope

### In scope
- **A2DP sink** on the Linux Pi/Ubuntu deployments. Phone (or other BT source) connects, RTest captures, audio plays through local speakers.
- Stabilization across long uptimes, source switches, and reconnect cycles.
- Audio-path failure modes (audible glitches, gaps, distortion, capture-stalls).
- AVRCP metadata + transport-control sync as it intersects with audio stability (e.g. volume desync producing audible jumps).

### Out of scope
- **HFP (voice / handsfree)** — RTest deliberately partitioned dual-adapter so that HFP voice goes to the Intel AX201 (`hci1`) and A2DP music goes to the TP-Link UB500 (`hci0`). RotaryPhone owns HFP; this doc does not relitigate that boundary. See [`docs/RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`](../../../RotaryPhone/docs/prompts/RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md).
- **Windows BT path** (`WindowsA2dpSinkManager`, `WasapiLoopbackCaptureSource`) — Linux deploy is the primary production target. Windows mentioned only where its behavior provides useful contrast to the Linux path.
- **BT pairing / discovery UX** — covered in `feature/bt-multi-device-ux` work, not relitigated here unless evidence implicates it in audio stability.
- **The Cast output path** — separate document (see [`2026-05-21-cast-stutter-comparison.md`](2026-05-21-cast-stutter-comparison.md)).

---

## 2. Reference systems

Four columns in every matrix in this document:

| Column key | System | Why included |
|---|---|---|
| **RTest** | Current RTest BT audio path on Linux (PipeWire native interop + BluezAgent + BluetoothAudioSource) | The system under study |
| **PW-stock** | Stock PipeWire + WirePlumber, with a phone connected as A2DP source, audio routed straight to the default sink — *no RTest layer in between* | Strongest control: same substrate, no RTest wrapping. If PW-stock is stable and RTest is not, the gap is in RTest's added code. |
| **bluez-alsa** | `bluealsa-aplay` reading A2DP audio directly into ALSA, no PipeWire involved (used by some audiophile distros, RetroPie, embedded BT receivers) | Alternative substrate. Sidesteps PipeWire entirely; useful for isolating whether the issue is PW-specific or BT-stack-specific. |
| **AOSP-BT** | Android's Bluetooth A2DP stack (Bluedroid → Fluoride), as it runs on a phone *being* an A2DP sink (e.g. car-mode audio, or Android-as-BT-speaker apps) | Public source, runs on billions of devices, lots of documented engineering decisions on stability and codec negotiation. Strong reference for "what's the load-bearing piece." |

Reference-column substitution policy: if reading the public source of any column reveals that the comparison axis is meaningless (e.g. AOSP-BT uses a completely different audio HAL that doesn't map onto our pipeline), the column is dropped and replaced with the next-best candidate (raspotify, pi-music-box, Sonos engineering blog, ALSA + bluez-tools direct). Log substitutions in §8.

---

## 3. Data collection methodology

### Evidence tier (per cell)

Same convention as the Cast doc. Each filled cell carries one of:

| Tag | Means | How obtained |
|---|---|---|
| `[source-walked]` | We read the code | RTest: open in tree; cite `file:line` ranges. PW-stock: `wireplumber`, `pipewire`, `pipewire/spa/plugins/bluez5/` source. bluez-alsa: GitHub `Arkq/bluez-alsa` source. AOSP-BT: `packages/modules/Bluetooth/system/` and `frameworks/av/services/audioflinger/` |
| `[doc-cited]` | We have a public reference | PipeWire wiki, BlueZ man pages, Android source.android.com BT pages, kernel `Documentation/bluetooth/`, public engineering blogs |
| `[inferred-from-behavior]` | We're reasoning from observable signals | `pw-top` output, `btmon` traces, `journalctl -u radio-api`, audible artifacts. Explicit, lower confidence |

Findings without an evidence tag should not appear in the filled doc.

### Measurement-discipline tier (per speculative idea in §7) — *new in this doc*

Every speculative improvement idea in §7 is structured as a **testable hypothesis**, not a wish. The Cast doc's §7 will be retrofitted to the same standard in a follow-up commit. Each idea carries five mandatory blocks:

1. **Evidence motivating this** — a pointer to a concrete *observable phenomenon* in the current system, not "this is a known pattern from elsewhere." Without this block, the idea is speculation and gets dropped.

2. **Baseline probe** — an exact, reproducible measurement of the current system that captures the phenomenon. Must be:
   - **Scripted** — a one-liner shell command, a `python3 scripts/...` invocation, or a labeled DevTools sequence. Not a paragraph of prose.
   - **Reproducible** — runs the same way each time, takes the same arguments, produces the same artifact shape.
   - **Bounded** — runs for a known duration / sample size, so before/after can be compared statistically.

3. **Post-change probe** — the *same* probe, run after the change is applied. The probe identity is what makes before/after comparable; any change to the probe between runs invalidates the comparison.

4. **Success criterion** — a quantitative pass/fail bar. Examples:
   - "p95 of `OnProcess` interval drops by ≥50% AND max ≤2× nominal quantum"
   - "Capture-stall events per uptime-week drops from N to 0 across a 7-day soak"
   - "Audible-gap detection (silence runs >50 ms via FFT analysis of recorded output) drops from M/h to ≤M/10/h"

   No success criterion = no measurement = idea stays in research, doesn't go to queue.

5. **Debug-agent verification steps** — a copy-pasteable sequence a debug agent (or a coding agent's CI step) can run to confirm the success criterion. Numbered, exact commands, no judgment calls. Must produce a single artifact (a number, a boolean, a side-by-side comparison) that the agent can include in its report.

### Concurrent-load discipline — every probe captures system load

[MEMORY](../../C:/Users/mark/.claude/projects/D--prj-RTest-RTest/memory/MEMORY.md) documents that on the Ubuntu N100 production host, audio distortion correlates with SSH activity, journald log queries, and SQLite DB reads. The host is resource-constrained and the BT capture pipeline (PipeWire native thread + downstream mixer) shares CPU/IO with background work. Any probe that captures *only* BT-layer metrics will miss load-correlated stalls.

**Every baseline and post-change probe in §7 must capture `PROBE-SYS-LOAD` concurrently** (shared with the Cast doc; see [`2026-05-21-cast-stutter-comparison.md#shared-probe-infrastructure`](2026-05-21-cast-stutter-comparison.md)). `PROBE-SYS-LOAD` runs `vmstat 1`, `iostat -x 1`, `pidstat -p $(pgrep -d, radio-api,radio-web,journald,sqlite3,sshd) 1`, per-second `journalctl … | wc -l` and `pgrep sshd | wc -l`, all time-aligned. Post-processing correlates BT-layer events (capture stalls, OnProcess interval spikes, generator stalls, reconnect events) against the load snapshot in the 5 seconds preceding each event.

A change is considered a *full* improvement only when its success criterion holds under **both** a **light-load** scenario (quiet host) and a **heavy-load** scenario (scripted `journalctl -f` + `sqlite3 metrics.db 'SELECT *'` busy-loop + radio-web hammer-load). A change that improves under light load but not heavy load is documented as a *half* improvement — both deltas recorded, so the reader can see whether system-isolation work is the missing co-requisite.

The system-isolation ideas in the Cast doc's §7 (Idea #9 CPU affinity, Idea #10 logging-path audit, Idea #11 background-op gating) are **shared** — they affect both Cast and BT paths because both paths live in the same `radio-api` process. Implementing them once benefits both outputs; success criteria reference each path's probes.

### Tools available for the research execution pass

- **Live SSH to `radio` (Ubuntu N100, primary)** and `piradio` (Pi 5). Both run `radio-api` under `mmack` user with PipeWire.
- **`pw-top`** — per-node packets/quantum/latency snapshots.
- **`btmon`** — kernel HCI trace; captures BT transport packets, ACL inter-arrival times, codec negotiation.
- **`pw-dump` / `pw-cli`** — node/link graph inspection.
- **`bluetoothctl`** — interactive BlueZ; can dump device + transport state.
- **`journalctl -u radio-api`** — RTest's own logging including the existing `🔬 PipeWire OnProcess: count=… interval min=… max=…` 10s-window stats from `PipeWireNativeStream.OnProcess` (see [PipeWireNativeStream.cs:L365-L371](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs)).
- **`Radio.Metrics` SQLite store** — RTest already records `bluetooth.reconnect_attempts_total`, `_success_total`, `_exhausted_total` ([BluetoothReconnectionLoop.cs:L123-L143](../../src/Radio.Infrastructure/Platform/Bluetooth/BluetoothReconnectionLoop.cs)); we can extend with stability-specific counters during the research execution pass without needing to commit production code.
- **Audio capture of system output** (loopback recording via `parecord` on a second sink, or USB loopback cable to a recorder) — for objective glitch-detection via FFT / silence-run analysis.
- **PR #314 unit tests** for `ParsePwCliOutputForBtNode` ([PipeWireNodeParsingTests.cs](../../tests/Radio.Infrastructure.Tests/Platform/Bluetooth/PipeWireNodeParsingTests.cs)) — the parsing path is already covered, so its measurement is *correctness* (existing tests pass) rather than *stability* (a runtime-load metric).

### Counter-discipline

- **Date-stamp every reference walk** — `[source-walked, AOSP main branch 2026-05-22]`. BT stacks evolve.
- **Date-stamp every baseline run** — soak-test results from a 3.6 GHz N100 box on a quiet WiFi LAN are not portable to a Pi 5 on a noisy WiFi LAN.
- **Pair every "improvement" claim with a baseline measurement** — never claim a change "fixed" anything without running the post-change probe and producing the artifact.

---

## 4. Failure-mode catalog (the diagnostic spine)

Eleven modes, each independently capable of producing a distinct audible or operational failure. The matrix below is filled per system × mode during the research pass.

### Modes

| # | Mode | Mechanism | Audible / operational signature |
|---|------|-----------|---|
| **FM-BT-1** | PW node never materializes | Phone pairs (BlueZ `Connected = true`, MediaTransport activated) but no PipeWire capture node ever appears. `pw-cli ls Node` shows no `bluez_input.*` matching the phone's MAC. | No audio from phone. Auto-switch retries hourly; logs fill with "waiting for PW node". User intervention required. |
| **FM-BT-2** | PW node disappears mid-session | BT transport drops, suspends, or hands off (e.g. another adapter steals the device); PW node goes idle or unregisters while session is logically active. | Audio stops mid-track. RTest's `MonitorBtPipelineAsync` may or may not notice depending on timing. |
| **FM-BT-3** | Capture loop quiesces silently | Native stream alive at PW layer, generator attached to mixer, but `OnProcess` is no longer invoked (or invoked with zero-size chunks). [The known long-uptime degradation bug.](../../C:/Users/mark/.claude/projects/D--prj-RTest-RTest/memory/MEMORY.md) | Silent output from BT source after hours/days of uptime. Source-switch + switch-back may or may not restore. |
| **FM-BT-4** | Mixer-side generator stall | Audio reaching generator buffer, but downstream (master mixer, modifiers, playback device) consumes faster than producer fills → buffer empty events. Surfaced by `SoundFlowPlaybackService.GeneratorStalled`. | Brief silences or stutters at the playback device. Distinct from FM-BT-3 because PW layer is healthy. |
| **FM-BT-5** | BT transport jitter / packet loss | Bursty A2DP packet arrival (WiFi co-channel interference, BT 2.4 GHz noise, distance, antenna obstruction). PipeWire's BT plugin smooths some of this but not all. | Micro-glitches, "static" bursts, audible packet drops. Correlates with RF environment. |
| **FM-BT-6** | Codec quality degradation | Bitpool collapse (SBC negotiates down under conditions), or codec re-negotiation (AAC → SBC after retries). | Lower fidelity, sometimes audible "compression" character. Often persists until reconnect. |
| **FM-BT-7** | Frame-alignment misalignment | A2DP chunk arrives non-frame-aligned (odd sample count); without guard, L↔R channels shift permanently. (RTest has guard at [PipeWireNativeStream.cs:L309-L316](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs).) | Persistent L↔R distortion / phase weirdness until restart. |
| **FM-BT-8** | AVRCP desync (volume, metadata, transport) | Phone's AVRCP target reports state X; PW + RTest hold state Y; user-perceived volume / now-playing / play-pause disagree. | Volume jumps when AVRCP catches up; metadata stale across track changes; play-pause hits wrong system. |
| **FM-BT-9** | Reconnect-after-walkaway | Phone walks out of range, comes back. Does RTest detect the disconnect? Reconnect? Restart the capture stream cleanly? Or is manual intervention required? | Audio doesn't resume after return; or resumes but with stale routing; or works fine. |
| **FM-BT-10** | Adapter / profile contention | A second profile (HFP) activates on the same adapter, suspending A2DP; OR another service (RotaryPhone) takes over the adapter. RTest's dual-adapter split addresses this *for the HFP-vs-A2DP-on-one-adapter* case, but cross-adapter pairing can still race. | A2DP suspends silently mid-session; reconnect required to restore. |
| **FM-BT-11** | Host resource contention | `radio-api` competes with concurrent system activity (SSH log queries, journald write throughput, SQLite WAL checkpoints, fingerprint backfill, `radio-web` on same box) for CPU / IO / memory on the Ubuntu N100 host; PipeWire native thread's `OnProcess` callback misses its scheduling window when host is busy. MEMORY explicitly documents the SSH-correlation. Shared with Cast doc's FM8 — same process, same host. | Audible glitches that don't correlate with BT-layer events (no transport drop, no PW node state change); correlate instead with host CPU spikes, log-volume surges, or SQLite checkpoint events. Distinct from FM-BT-3 (capture-loop quiesces silently for hours) because FM-BT-11 is transient per-event correlation. |

### Failure-mode matrix (to be filled)

For each cell:

- **Exposure** — Y/N + one-line "this system is vulnerable because…"
- **Mitigation** — concrete: detection mechanism, recovery path, telemetry surface
- **Evidence** — `[source-walked]`, `[doc-cited]`, or `[inferred-from-behavior]`

| Mode | RTest | PW-stock | bluez-alsa | AOSP-BT |
|---|---|---|---|---|
| FM-BT-1 — PW node never materializes | **Exposure:** Y — `LinuxBluetoothService.GetAudioCaptureDeviceAsync` scrapes `pw-cli ls Node` repeatedly until the `bluez_input.<MAC>.a2dp-source` node appears; `BluetoothAutoSwitchService` switches source on BlueZ `DeviceConnected` event without gating on node presence. Race window: BlueZ reports `Connected=true` before the MediaTransport activates the PW node, yielding hours of retry. **Mitigation:** Double-search guard in pre-lock + post-lock paths ([LinuxBluetoothService.cs:L1022,L1045](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)); no explicit retry cap on the autoSwitch path. [source-walked] | **Exposure:** Y but quieter — the same `pipewire-bluez5` SPA plugin creates the node on `MediaTransport1.State → pending/active`; if the transport never activates, no node ever appears. Without RTest's auto-switch layer, this surfaces as "audio doesn't play" rather than a retry loop — the user notices immediately and re-pairs. **Mitigation:** WirePlumber `bluez_monitor` policy logs the transport state transitions; no auto-retry at the routing layer. [doc-cited, https://gitlab.freedesktop.org/pipewire/pipewire/-/tree/master/spa/plugins/bluez5, 2026-05-22] | **Exposure:** Y but explicit failure — `bluealsa-aplay <MAC>` exits with `Couldn't open PCM` if the A2DP transport isn't ready; no retry built into the daemon. Typical deployments wrap it in a systemd unit with `Restart=on-failure` + `RestartSec=2`. **Mitigation:** Systemd-level retry; no application-level. [doc-cited, https://github.com/arkq/bluez-alsa/wiki/Getting-started, 2026-05-22] | **Exposure:** Low — Bluedroid's BTA-AV layer (`bta_av_main.cc`, `bta_av_aact.cc`) creates the AVDTP stream synchronously with the `BluetoothA2dp.ACTION_CONNECTION_STATE_CHANGED` broadcast; AudioPolicyManager binds the BT routing in the same code path. Failure to materialize surfaces as `STATE_DISCONNECTED` immediately, not as a silent retry. [doc-cited, https://cs.android.com/android/platform/superproject/main/+/main:packages/modules/Bluetooth/system/btif/src/btif_av.cc, 2026-05-22] |
| FM-BT-2 — PW node disappears mid-session | **Exposure:** Y — `LinuxBluetoothService.MonitorBtPipelineAsync` ([L162](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)) monitors but the disappearance path through `_nativeStream` disposal is conditional on signals (`CaptureStreamRecovered`, BlueZ disconnect) — silent transport state changes can leave the stream attached to a defunct node. **Mitigation:** Disposal at [L1402,L1511,L1928](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs) triggered on disconnect events; no PW-event subscription for direct node-state changes (relies on D-Bus + scrape). [source-walked] | **Exposure:** Y — WirePlumber detects via D-Bus `org.bluez.MediaTransport1.State` going `idle`; default policy un-routes the BT sink. Re-routing on transport re-activation is automatic. **Mitigation:** WirePlumber's `bluez_monitor.lua` handles the state transition explicitly. [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/blob/master/src/scripts/monitors/bluez/, 2026-05-22] | **Exposure:** Y — `bluealsa-aplay` sees EOF on the daemon socket and exits with status 1. **Mitigation:** Same systemd `Restart=on-failure` pattern; no application-layer continuity. [doc-cited, https://github.com/arkq/bluez-alsa, 2026-05-22] | **Exposure:** Low — `ACTION_CONNECTION_STATE_CHANGED → STATE_DISCONNECTED` triggers AudioPolicyManager to route audio to alternate sink (speaker). Reconnect attempted via `BluetoothAdapterServiceJni`-driven auto-connect for "trusted" devices. **Mitigation:** Built into the framework. [doc-cited, https://source.android.com/docs/core/connect/bluetooth, 2026-05-22] |
| FM-BT-3 — Capture loop quiesces silently | **Exposure:** Y (known production bug per [MEMORY](../../C:/Users/mark/.claude/projects/D--prj-RTest-RTest/memory/MEMORY.md)) — after days of uptime and source switches, `OnProcess` in [PipeWireNativeStream.cs](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs) stops being invoked while node + stream are still attached. No watchdog acts on the existing 10 s-window `_lastOnProcessTimestamp` signal. **Mitigation:** None today — manual `radio-api` restart required; `OnGeneratorStalled` handler ([BluetoothAudioSource.cs:L354-L390](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) fires only when the SoundFlow generator runs dry, which depends on downstream consumption — not directly tied to PW callback cessation. [source-walked, MEMORY-documented] | **Exposure:** Low — PW's bluez5 SPA plugin runs the audio thread on the daemon's main event loop (not a user-process thread); thread starvation from in-process competitors is structurally less possible. The known PW BT bugs in this category have been transport-state inconsistencies, not callback cessation. **Mitigation:** PW daemon's own `loop` health management. [inferred-from-architecture; https://docs.pipewire.org/page_overview.html, 2026-05-22] | **Exposure:** Low — `bluealsa` daemon and `bluealsa-aplay` are separate processes from any user app; ALSA blocking-write semantics make a quiesce *visible* (PCM write would block), not silent. **Mitigation:** Structural (separate process boundary). [inferred-from-architecture] | **Exposure:** Low — `audioserver` is a dedicated system process; A2DP encoder thread runs at SCHED_FIFO with explicit watchdogs in AudioFlinger. **Mitigation:** Framework-managed dedicated thread + watchdog. [doc-cited, https://source.android.com/docs/core/audio/avoiding_pi, 2026-05-22; AOSP `frameworks/av/services/audioflinger/Threads.cpp`] |
| FM-BT-4 — Mixer-side generator stall | **Exposure:** Y — `SoundFlowPlaybackService.GeneratorStalled` event fires when the `BufferedSoundGenerator<float>` runs dry while still attached to the master mixer ([BluetoothAudioSource.cs:L88-L94,L360-L390](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)). **Mitigation:** `_recoveryInProgress` interlock dedups concurrent recovery; `OnGeneratorStalled` triggers a re-acquisition path with bounded scope. [source-walked] | n/a — no in-process mixer between PW capture and the destination sink; the default policy routes the BT source's audio nodes directly to the speaker sink via PW's graph. | n/a — `bluealsa-aplay` writes directly to an ALSA PCM device; no intermediate mixer in the bluez-alsa daemon path. | **Exposure:** Y but heavily isolated — AudioFlinger's `FastMixer` thread uses non-blocking FIFOs and atomic state, with explicit underrun counters surfaced via `dumpsys media.audio_flinger`. **Mitigation:** Documented non-blocking-FIFO discipline + state-queue pattern. [doc-cited, https://source.android.com/docs/core/audio/avoiding_pi, 2026-05-22] |
| FM-BT-5 — BT transport jitter / packet loss | **Exposure:** Medium — RF jitter inherent to 2.4 GHz BT; RTest receives whatever the BlueZ + PW BT stack delivers. The `_onProcessBurstCount` counter ([PipeWireNativeStream.cs:L269-L272](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs)) directly observes burst delivery (intervals < 1 ms) indicative of packet bunching after jitter. **Mitigation:** Frame-alignment guard absorbs odd-sample-count chunks (FM-BT-7); downstream mixer absorbs small timing variation. No PLC, no explicit jitter buffer beyond mixer's nominal buffer. [source-walked] | **Exposure:** Medium — same RF substrate; PW's bluez5 plugin has a small (default 16 ms) jitter buffer per A2DP stream + SBC PLC for the SBC codec. **Mitigation:** Built-in jitter buffer; codec-aware PLC. [doc-cited, https://gitlab.freedesktop.org/pipewire/pipewire/-/tree/master/spa/plugins/bluez5, 2026-05-22] | **Exposure:** Medium — same RF substrate; `bluealsa-aplay` does minimal smoothing beyond ALSA's period_size buffering. **Mitigation:** ALSA period/buffer sizing per `--pcm-buffer-time` flag; audiophile distros tune this explicitly. [doc-cited, bluez-alsa wiki: ALSA plug-ins, 2026-05-22] | **Exposure:** Medium — same RF substrate; AudioFlinger's elastic buffer absorbs jitter; SBC PLC + AAC error concealment built in. **Mitigation:** Framework-level elastic buffer with explicit underrun counters. [doc-cited, AOSP `frameworks/av/services/audioflinger/`] |
| FM-BT-6 — Codec quality degradation | **Exposure:** Y but invisible — RTest does not enumerate or pin codec; relies on whatever BlueZ + PW negotiate. No application-layer log of negotiated codec or bitpool. Default is usually SBC; AAC if both ends support. **Mitigation:** None. [source-walked, LinuxBluetoothService.cs full file — no codec API calls] | **Exposure:** Y — `bluez_monitor.conf.d` exposes a `bluez5.codecs = [ sbc aac aptx ldac ... ]` array; PW prefers in the listed order. WP can pin via property. **Mitigation:** Explicit codec list + preference order via config; observable in `pw-cli enum-params <node-id> Format`. [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/blob/master/src/config/bluetooth.conf.d/, 2026-05-22] | **Exposure:** Y — `bluealsa --codec=aac:0,sbc:0` pins per-profile codecs at daemon startup. **Mitigation:** Explicit codec selection by flag; default SBC unless aptX/AAC/LDAC compiled in. [doc-cited, https://github.com/arkq/bluez-alsa, 2026-05-22] | **Exposure:** Y — `bta_av_co.cc` codec selection ladder LDAC > LHDC > aptX-HD > aptX > AAC > SBC; user-configurable in Developer Options. **Mitigation:** Explicit ladder + per-device user override. [doc-cited, https://cs.android.com/android/platform/superproject/main/+/main:packages/modules/Bluetooth/system/bta/av/bta_av_co.cc, 2026-05-22] |
| FM-BT-7 — Frame-alignment misalignment | **Exposure:** Y addressed — RTest had this class of bug; guard at [PipeWireNativeStream.cs:L309-L316](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs) rounds sample count down to a frame boundary on every callback. The comment explicitly notes "PipeWire BT transport can deliver non-frame-aligned chunks during packet loss/gaps." **Mitigation:** In-place guard with no logging — if the bug recurs through a different code path (e.g. a different capture API) the guard's coverage is implicit. [source-walked] | **Exposure:** Low — PW's SPA buffer pipeline enforces frame counts at the format-negotiation boundary; the BT plugin produces aligned buffers. The class of bug RTest's guard catches reflects RTest using a lower-level API (raw byte sizes from `spa_chunk`) rather than frame-counted SPA APIs. **Mitigation:** Structural via SPA buffer model. [inferred-from-architecture; doc-cited, https://docs.pipewire.org/page_spa_pod.html, 2026-05-22] | **Exposure:** Low — `bluealsa-aplay` reads from the daemon's PCM-aligned socket. ALSA's PCM interface enforces frame boundaries at the API surface. **Mitigation:** Structural via ALSA frame semantics. [doc-cited, ALSA PCM interface] | **Exposure:** Low — AudioFlinger's buffer-handling enforces frame alignment; A2DP encoder writes in frame-aligned units. **Mitigation:** Structural. [inferred-from-architecture] |
| FM-BT-8 — AVRCP desync | **Exposure:** Y — `BluetoothAudioSource` subscribes to `MetadataChanged`, `PlaybackStatusChanged`, `PositionChanged` from `IBluetoothService` ([BluetoothAudioSource.cs:L79-L83](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)); volume sync chain is phone-AVRCP → PW node vol → source gain (2×) → master vol → speaker sink, per MEMORY. **Mitigation:** Event-driven sync; volume chain documented in MEMORY but not surfaced in code as a single integration test. [source-walked, MEMORY] | **Exposure:** Y — WirePlumber's MPRIS bridge surfaces AVRCP metadata; volume sync depends on the loaded `bluez5.lua` policy script. Default policy syncs volume via `linear` scale. **Mitigation:** Policy-script-driven; well-tested in stock GNOME/KDE deployments. [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/wikis/Configuration#bluetooth, 2026-05-22] | n/a — bluez-alsa's `bluealsa` daemon does not handle AVRCP; metadata + transport control requires a separate `bluez-alsa-rfcomm` helper or external `bt-agent`. [doc-cited, https://github.com/arkq/bluez-alsa#what-is-not-supported, 2026-05-22] | **Exposure:** Y but framework-managed — Bluedroid's `bta_av_rc_*.cc` handles AVRCP target role; absolute-volume support negotiated automatically; track-change broadcasts via `BluetoothAvrcpController.SCAN_MODE_CHANGED`-style intents. **Mitigation:** Framework-managed with `dumpsys bluetooth_manager` observability. [doc-cited, AOSP `packages/modules/Bluetooth/system/bta/av/`] |
| FM-BT-9 — Reconnect-after-walkaway | **Exposure:** Y — `BluetoothReconnectionLoop` ([BluetoothReconnectionLoop.cs](../../src/Radio.Infrastructure/Platform/Bluetooth/BluetoothReconnectionLoop.cs)) implements exponential backoff with `bluetooth.reconnect_*_total` metrics; triggered on unexpected disconnect. After phone returns: BlueZ pairing state preserved; reconnect via `Connect` D-Bus method. **Mitigation:** Bounded retry attempts; metrics for visibility. [source-walked] | **Exposure:** Y — relies on BlueZ's built-in reconnect heuristics for "trusted" devices + the phone-side initiation. WirePlumber does not initiate reconnect. **Mitigation:** External (BlueZ + phone-side). [doc-cited, BlueZ documentation] | **Exposure:** Y — daemon waits passively for D-Bus events from BlueZ; reconnect requires phone-side initiation or external `bt-agent`. **Mitigation:** External. [doc-cited, https://github.com/arkq/bluez-alsa] | **Exposure:** Low — Bluedroid maintains a "preferred device" list; on detected unbond/walkaway then return, automatic reconnect attempts via `BluetoothAdapterService` reconnect path with priority ordering. **Mitigation:** Framework. [doc-cited, AOSP `packages/modules/Bluetooth/`] |
| FM-BT-10 — Adapter / profile contention | **Exposure:** Mitigated by dual-adapter split — TP-Link UB500 (`hci0`) reserved for A2DP music, Intel AX201 (`hci1`) for HFP voice; WP's `87-bt-adapter-select.lua` restricts WP to `hci0`. Residual exposure: cross-adapter device migration if a phone pairs with both adapters. **Mitigation:** Boundary doc + WP filter. [source-walked + doc-cited, deploy WP configs; RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md] | **Exposure:** Y — stock WP policy doesn't pin adapters per profile by default; profile contention on a single adapter is handled by BlueZ's profile-priority logic. **Mitigation:** Configurable via WP policy script + `bluez_monitor` filter properties. [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/wikis/Configuration#bluetooth] | **Exposure:** Y — `bluealsa --profile=a2dp-source` pins per-profile mode; concurrent HFP requires a second daemon instance. **Mitigation:** Explicit profile selection. [doc-cited, https://github.com/arkq/bluez-alsa] | **Exposure:** Low — Bluedroid's profile manager resolves contention deterministically at the BTA layer; A2DP and HFP can co-exist with explicit priority. **Mitigation:** Framework. [doc-cited, AOSP BTA layer] |
| FM-BT-11 — Host resource contention | **Exposure:** Y (acknowledged in MEMORY) — `radio-api` runs as `mmack` user on Ubuntu N100 with default Linux scheduling. No `CPUAffinity=`, no `SCHED_FIFO`, no IO niceness in [radio-api.service](../../deploy/radio-api.service). The PipeWire native callback (`OnProcess`) runs on a PW-owned thread loop but the loop itself is in `radio-api`'s process and shares CPU with `radio-web`, journald, SQLite, fingerprint backfill (15 s interval), and any concurrent SSH sessions. The existing `🔬 PipeWire OnProcess` 10 s-window stats ([PipeWireNativeStream.cs:L364-L376](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs)) already log `interval max` and `execution max` — these are the directly-observable signals when the host is loaded. **Mitigation:** None today. Recent async-logging work reduced overall log volume but didn't isolate the PW thread from synchronous-flush events. [doc-cited, MEMORY: "Audio distortion correlates with SSH activity"; source-walked, deploy/radio-api.service lacks resource directives; PipeWireNativeStream telemetry] | **Exposure:** Lower — PW-stock deployments typically run on lighter desktops without competing application workloads; but the failure mode exists if the host is loaded. WirePlumber + pipewire daemons themselves run as user-level services with no special isolation by default. [doc-cited, https://docs.pipewire.org/page_man_pipewire-daemon_1.html, 2026-05-22] | **Exposure:** Variable — bluez-alsa's `bluealsa` daemon is usually run with elevated permissions or a dedicated user; many audiophile distros run it on a low-latency kernel with `chrt` priority. [doc-cited, https://github.com/Arkq/bluez-alsa/wiki, 2026-05-22] | **Exposure:** N — Android isolates `audioserver` as a dedicated system process with `audio_policy_configuration` granting it elevated scheduling priority. The BT stack itself runs in `system_server` with realtime threads for the A2DP encoder. [doc-cited, https://source.android.com/docs/core/audio, 2026-05-22] |

---

## 5. Pipeline table (the apples-to-apples reference)

Ten rows × the same four columns. Each cell with an evidence tag.

| Row | What it captures | RTest | PW-stock | bluez-alsa | AOSP-BT |
|---|---|---|---|---|---|
| Capture origin | What kind of object delivers audio to userspace? | PipeWire capture node (e.g. `bluez_input.78:20:51:F5:FB:A7.a2dp-source`) acquired via `pw-cli` discovery + native `pw_stream_connect` against the node's `object.serial`. [source-walked, LinuxBluetoothService.cs:L1247-L1394; PipeWireNativeStream.cs:L67-L184] | Same PipeWire `bluez_input.<MAC>.a2dp-source` node — but routed by WirePlumber policy directly into the default audio graph, not consumed by an application-layer stream. [doc-cited, https://gitlab.freedesktop.org/pipewire/pipewire/-/tree/master/spa/plugins/bluez5, 2026-05-22] | ALSA PCM device exposed by the `bluealsa` daemon, addressable as `bluealsa:DEV=<MAC>,PROFILE=a2dp` plug name; consumed by `bluealsa-aplay` reading PCM frames. [doc-cited, https://github.com/arkq/bluez-alsa/blob/master/doc/bluealsa-aplay.1.rst, 2026-05-22] | Audio HAL `IBluetoothAudioPort` (AIDL) or legacy `audio.a2dp.default.so` HAL; data flows from BT controller → HCI → Bluedroid → HAL → AudioFlinger. [doc-cited, https://source.android.com/docs/core/audio/implement-shared-library, 2026-05-22; AOSP `hardware/interfaces/bluetooth/audio/`] |
| Discovery & negotiation | How does the audio path learn a new BT source exists? | BlueZ D-Bus `org.bluez.MediaTransport1` interface monitored via `WatchDevicePropertiesAsync` + `AttachMediaTransportAsync`; PW node discovered separately via `pw-cli ls Node` scrape with `ParsePwCliOutputForBtNode` parser ([PR #314 tests](../../tests/Radio.Infrastructure.Tests/Platform/Bluetooth/PipeWireNodeParsingTests.cs)). [source-walked, LinuxBluetoothService.cs:L648-L953,L1247] | WirePlumber's `bluez_monitor.lua` subscribes to D-Bus `org.bluez.Adapter1` + `org.bluez.MediaTransport1`; creates a PW node when transport state transitions to `pending`/`active`. **No `pw-cli` scrape — direct PW event API.** [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/blob/master/src/scripts/monitors/bluez/, 2026-05-22] | `bluealsa` daemon registers as a BlueZ media endpoint via `org.bluez.Media1.RegisterEndpoint`; receives `MediaTransport1` D-Bus signals directly. [doc-cited, https://github.com/arkq/bluez-alsa, 2026-05-22] | Bluedroid's `bta_av` layer initiates SDP query when a device connects; A2DP role + capabilities negotiated via AVDTP signaling. Java framework receives `BluetoothA2dp.ACTION_CONNECTION_STATE_CHANGED` broadcast. [doc-cited, AOSP `packages/modules/Bluetooth/system/bta/av/`] |
| Profile selection | Which BT profiles are activated and accepted? | A2DP sink only on `hci0` (TP-Link UB500); HFP excluded by WirePlumber config `87-bt-adapter-select.lua`. [source-walked, deploy WP configs; doc-cited, RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md] | Configurable via `bluez_monitor.conf.d/*.conf` — `bluez5.enable-hw-volume`, `bluez5.headset-roles`, `bluez5.codecs` properties drive which profiles WP accepts. Default: A2DP + HFP + HSP active. [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/wikis/Configuration#bluetooth, 2026-05-22] | Per-daemon-instance via `--profile=a2dp-source` / `--profile=a2dp-sink` / `--profile=hfp-ag` flags. Multiple daemons can run with different profile mixes. [doc-cited, https://github.com/arkq/bluez-alsa, 2026-05-22] | Framework-managed; user can disable individual profiles via Developer Options "Disable absolute volume", "Bluetooth audio codec" etc. [doc-cited, https://source.android.com/docs/core/connect/bluetooth] |
| Codec | Which codecs negotiable? Default? Fallback? | RTest does not enumerate or pin codec; relies on whatever BlueZ + PipeWire's `bluez5` plugin negotiate. Default usually SBC; AAC if both ends advertise. No application-level visibility into bitpool. [inferred-from-behavior + source-walked, LinuxBluetoothService.cs full file — no codec API calls present] | `bluez5.codecs = [ sbc sbc_xq aac aptx aptx_hd ldac ]` array property exposes preference order; can be filtered per-device. Negotiated codec observable in `pw-cli enum-params <node-id> Format`. [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/blob/master/src/config/bluetooth.conf.d/, 2026-05-22] | Explicit per-codec compile flags + runtime `--codec=<name>:<priority>` selection. Daemon negotiates the highest-priority mutually-supported codec. [doc-cited, https://github.com/arkq/bluez-alsa#features] | Ladder LDAC > LHDC > aptX-HD > aptX > AAC > SBC selected in `bta_av_co.cc`; user can override per-device. Bitpool dynamically adjusted by encoder. [doc-cited, https://cs.android.com/android/platform/superproject/main/+/main:packages/modules/Bluetooth/system/bta/av/bta_av_co.cc, 2026-05-22] |
| Stream API | What API delivers audio bytes to the app? | PipeWire native `pw_stream` via P/Invoke. `OnProcess` callback fires on PW thread loop with S16_LE buffers; converted to float, frame-aligned, delivered via `AudioDataCallback` to `BufferedSoundGenerator<float>`. [source-walked, PipeWireNativeStream.cs:L231-L378] | Same `pw_stream` API, but consumed by WirePlumber's routing layer — not an application-managed stream. PW's session manager links the BT node directly to the sink node in the graph. [doc-cited, https://docs.pipewire.org/group__pw__stream.html, 2026-05-22] | D-Bus + Unix-domain-socket between `bluealsa` daemon and `bluealsa-aplay`; ALSA PCM read API on the consumer side. [doc-cited, https://github.com/arkq/bluez-alsa, 2026-05-22] | AAudio MMAP for low-latency or AudioTrack/AudioRecord for legacy; A2DP HAL receives encoded frames via `IBluetoothAudioPort.writeAudioData()` (AIDL). [doc-cited, https://source.android.com/docs/core/audio/aaudio, 2026-05-22] |
| Transport buffer model | What buffer / quantum drives the data flow? | PW quantum forced to `default.clock.min-quantum = 512` (≈ 10.67 ms) via `~/.config/pipewire/pipewire.conf.d/99-radio-quantum.conf` to match BT transport quantum and eliminate xruns. [source-walked, deploy configs; doc-cited, MEMORY: "PipeWire Quantum Tuning"] | Stock PW uses `default.clock.quantum = 1024` (≈ 21 ms at 48 kHz) by default; BT plugin adds a small (~16 ms) internal jitter buffer per A2DP stream. Quantum auto-adjusts within `min-quantum`/`max-quantum` bounds. [doc-cited, https://docs.pipewire.org/page_man_pipewire-props_7.html, 2026-05-22] | ALSA `period_size` + `buffer_size` (set via `--pcm-buffer-time` / `--pcm-period-time` flags to `bluealsa-aplay`); default 500 ms buffer / 100 ms period. Audiophile distros tune this aggressively. [doc-cited, https://github.com/arkq/bluez-alsa, 2026-05-22] | AudioFlinger uses HAL-reported buffer-size; A2DP HAL typically reports 24 ms periods. FastMixer thread feeds the encoder at the buffer rate. [doc-cited, https://source.android.com/docs/core/audio/latency/design, 2026-05-22] |
| Frame alignment & format conversion | Where does S16→float happen? How are non-aligned chunks handled? | S16_LE → float inline in `OnProcess` via unsafe `Span<short>` cast (no per-sample marshal). Frame-alignment guard: `sampleCount = sampleCount / channels * channels`. [source-walked, PipeWireNativeStream.cs:L309-L337] | SPA format-pod negotiation enforces frame alignment at the buffer interface; conversion happens in `spa-audioconvert` plugin (or omitted if sink accepts source format). [doc-cited, https://gitlab.freedesktop.org/pipewire/pipewire/-/tree/master/spa/plugins/audioconvert, 2026-05-22] | ALSA PCM frame semantics enforce alignment at the API boundary; format conversion (S16→S24/F32) handled by `plug` plugin in `.asoundrc` chain if needed. [doc-cited, ALSA documentation] | AudioFlinger's `AudioMixer` handles format conversion; frame alignment enforced via `audio_buffer_t` size invariants. [inferred-from-architecture; AOSP `frameworks/av/services/audioflinger/AudioMixer.cpp`] |
| Recovery model | What triggers a restart / reconnect? | (a) `BluetoothReconnectionLoop` (exponential backoff on disconnect, [BluetoothReconnectionLoop.cs](../../src/Radio.Infrastructure/Platform/Bluetooth/BluetoothReconnectionLoop.cs)); (b) `CaptureStreamRecovered` event from `IBluetoothService`; (c) `SoundFlowPlaybackService.GeneratorStalled` → `OnGeneratorStalled` with `_recoveryInProgress` interlock dedup ([BluetoothAudioSource.cs:L354-L390](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)); (d) double-search guard in `GetAudioCaptureDeviceAsync` race ([LinuxBluetoothService.cs:L1022,L1045](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)). [source-walked] | WirePlumber's `bluez_monitor.lua` re-creates the node on transport state transitions; reconnect itself relies on BlueZ + phone-side initiation. No application-layer retry. [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/blob/master/src/scripts/monitors/bluez/] | External: systemd `Restart=on-failure` on the `bluealsa-aplay@<MAC>.service` unit + BlueZ auto-reconnect for trusted devices. Daemon-internal: minimal. [doc-cited, https://github.com/arkq/bluez-alsa/wiki, 2026-05-22] | Framework-managed: Bluedroid maintains preferred-device list; AudioPolicyManager handles sink fallback on disconnect; reconnect attempted via BluetoothAdapterService. AVRCP volume-sync also restored on reconnect. [doc-cited, https://source.android.com/docs/core/connect/bluetooth, 2026-05-22] |
| Observable telemetry | What counters / logs / events does the system surface? | (a) `🔬 PipeWire OnProcess` 10s-window stats (count, min/max interval, burst count for sub-1ms intervals, max execution ms) — [PipeWireNativeStream.cs:L364-L376](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs); (b) `bluetooth.reconnect_*_total` counters in Radio.Metrics SQLite store; (c) BlueZ D-Bus property-change logs. [source-walked] | `pw-top` (real-time per-node packets/quantum/latency), `pw-dump` (full graph snapshot as JSON), `pw-cli list-objects` (text listing), journalctl on `pipewire`/`wireplumber` user units. No application-level metrics aggregation — PW is observability-first via its CLI. [doc-cited, https://docs.pipewire.org/page_man_pw-top_1.html, 2026-05-22] | `bluealsactl` (control utility — query/set codec, get transport state, get PCM info), `bluealsa --syslog` for daemon logging, `--verbose` for D-Bus traces. [doc-cited, https://github.com/arkq/bluez-alsa#tools, 2026-05-22] | `dumpsys media.audio_flinger` (mixer state, threads, underrun counters), `dumpsys bluetooth_manager` (BT stack state), `logcat -b system -s BluetoothAdapter`, `adb shell cmd bluetooth_manager`. Detailed per-thread CPU + scheduling visible via `dumpsys`. [doc-cited, https://source.android.com/docs/core/audio/debugging, 2026-05-22] |
| Metadata channel (AVRCP) | How does track / play-state / volume info flow? | Separate from audio. BluezAgent + `AttachMediaPlayerAsync` ([LinuxBluetoothService.cs:L2043](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)) subscribes to `org.bluez.MediaPlayer1` D-Bus property changes; events bridge to `BluetoothAudioSource.OnMetadataChanged` / `OnPlaybackStatusChanged` / `OnPositionChanged`. [source-walked] | WirePlumber bridges AVRCP to MPRIS (D-Bus standard for media players); compatible with `playerctl` + GNOME/KDE media controls. Volume sync via `linear` or `cubic` scale per policy. [doc-cited, https://gitlab.freedesktop.org/pipewire/wireplumber/-/wikis/Configuration#bluetooth, 2026-05-22] | n/a (no AVRCP in bluez-alsa) — separate `bluez-alsa-rfcomm` helper or `bt-agent` required for transport control. [doc-cited, https://github.com/arkq/bluez-alsa#what-is-not-supported] | Framework-managed via `bta_av_rc_*.cc`; AVRCP target + controller roles both supported; absolute-volume negotiated automatically. Track changes broadcast as system intents. [doc-cited, AOSP `packages/modules/Bluetooth/system/bta/av/`] |
| Host resource-contention surface | What concurrent system activity competes with the BT capture / mixer threads? What isolation primitives are in use? | `radio-api` runs as `mmack` user on Ubuntu N100 / Pi 5; systemd unit has **no** `CPUAffinity=`, **no** `Nice=`, **no** `IOSchedulingClass=`, **no** `LimitRTPRIO=`. PipeWire `OnProcess` callback runs on PW thread loop *inside* radio-api's process — shares scheduling with everything else in the process. Concurrent contention: `radio-web` (same box), 3× SQLite DBs, journald, fingerprint backfill loop at 15 s (active for BT source per [BluetoothAudioSource.cs:L91-L94](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)), SSH activity (acknowledged correlation per MEMORY). Existing telemetry: `🔬 PipeWire OnProcess` log captures interval min/max + execution max + burst count every 10 s — directly observable signal for FM-BT-11. [source-walked + doc-cited, deploy/radio-api.service; PipeWireNativeStream.cs:L364-L376; MEMORY] | WirePlumber + pipewire run as user-level services; no special CPU/IO isolation by default. Audio routing thread shares the same priority class as the rest of pipewire's audio graph. [doc-cited, https://docs.pipewire.org/, 2026-05-22] | `bluealsa-aplay` typically run with `chrt -f` SCHED_FIFO in audiophile distros; many ship a low-latency kernel. [doc-cited, https://github.com/Arkq/bluez-alsa/wiki, 2026-05-22] | `audioserver` is a dedicated process with elevated scheduling; A2DP encoder thread runs at realtime priority. [doc-cited, https://source.android.com/docs/core/audio, 2026-05-22] |

---

## 6. Findings synthesis

Reading across the now-filled matrices, six patterns stand out. All six describe the *shape of the gap* between RTest's BT path and the reference cluster on at least one measurable axis.

### Pattern 1 — RTest is the only system that scrapes `pw-cli` for PipeWire node discovery

§5 "Discovery & negotiation" row: PW-stock's WirePlumber `bluez_monitor.lua` subscribes to PipeWire's native event API + BlueZ D-Bus signals. bluez-alsa registers as a BlueZ media endpoint and receives `MediaTransport1` D-Bus signals directly. AOSP's Bluedroid initiates SDP query in the BTA layer synchronously with device connection. RTest alone polls `pw-cli ls Node` output and parses text via `ParsePwCliOutputForBtNode` ([LinuxBluetoothService.cs:L1247-L1394](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)). §4 FM-BT-1 RTest cell documents the consequence: a race window between `Connected=true` and the PW node appearing, exploited by `autoSwitchOnConnect`'s unconditional source-switch.

### Pattern 2 — RTest's audio-pipeline thread runs in-process with all other application work; the reference systems run audio in dedicated daemons / processes

§5 "Stream API" + new "Host resource-contention surface" rows: PW-stock runs the PipeWire daemon as a separate user-level service; the BT plugin's audio thread lives in that daemon, isolated from any one application's load. bluez-alsa's daemon is a separate process from `bluealsa-aplay` (which is itself a separate process from any consumer). AOSP runs `audioserver` as a dedicated system process with SCHED_FIFO threads ([source.android.com/docs/core/audio/avoiding_pi](https://source.android.com/docs/core/audio/avoiding_pi)). RTest alone runs the PW thread loop *inside* `radio-api`, sharing scheduling with `radio-web`, journald, SQLite, fingerprinting, and any concurrent SSH activity — the substrate for FM-BT-11 documented in MEMORY ("audio distortion correlates with SSH activity").

### Pattern 3 — Codec choice is invisible to RTest; every reference system makes it explicit

§5 "Codec" row: PW-stock exposes `bluez5.codecs = [ sbc sbc_xq aac aptx ldac ]` array property with observable negotiated codec via `pw-cli enum-params`. bluez-alsa selects via `--codec=<name>:<priority>` flag. AOSP has the LDAC > LHDC > aptX-HD > aptX > AAC > SBC ladder with per-device override. RTest "does not enumerate or pin codec" — there is no application-level visibility into what codec is in use or its bitpool. §4 FM-BT-6 RTest exposure tag says "Y but invisible" — the failure mode exists but cannot be diagnosed from logs.

### Pattern 4 — RTest's recovery model is explicit-on-disconnect but absent on silent quiesce; every reference system handles silent failure structurally or with explicit watchdogs

§5 "Recovery model" row: RTest has `BluetoothReconnectionLoop` for explicit disconnects, `CaptureStreamRecovered` for some paths, and `OnGeneratorStalled` for downstream-consumption stalls — but no watchdog for the FM-BT-3 case where `OnProcess` simply stops being invoked while the stream object is still attached. PW-stock and bluez-alsa sidestep this by *structural isolation* (a separate daemon's main-loop is less likely to silently quiesce). AOSP applies *explicit watchdogs* in AudioFlinger threads ([source.android.com/docs/core/audio/avoiding_pi](https://source.android.com/docs/core/audio/avoiding_pi)). RTest has the symptom (FM-BT-3 documented as a production bug in MEMORY) but neither the structural protection nor the watchdog.

### Pattern 5 — RTest's frame-alignment guard is correct, but its existence reveals an API-surface mismatch

§5 "Frame alignment" row: RTest reads `spa_chunk.Size` raw byte counts and guards via `sampleCount = sampleCount / channels * channels` ([PipeWireNativeStream.cs:L309-L316](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs)). PW-stock applications use SPA format-pod negotiation which enforces frame alignment at the buffer interface. bluez-alsa relies on ALSA PCM's frame-counted semantics. AOSP's `audio_buffer_t` has size invariants enforced by the AudioMixer. RTest needs the guard because it operates one abstraction layer lower — using the *raw chunk byte count* SPA exposes via `spa_chunk` rather than the higher-level frame-counted APIs. The guard is correct; the API choice is the gap. §4 FM-BT-7 RTest cell flags this directly.

### Pattern 6 — Reference systems' isolation is *structural*; RTest would have to add isolation deliberately

Synthesizing Patterns 1, 2, and 4: the reference systems gain stability not primarily from individual mitigations but from the *process architecture* — separate daemons mediating BT, dedicated audio threads at elevated priority, framework-managed lifecycle. RTest's monolithic `radio-api` process couples BT audio to web UI, fingerprinting, metrics, and configuration management. Mitigations within RTest's architecture (watchdog on `OnProcess`, gate `autoSwitchOnConnect`, codec observability) reduce specific failure modes; but the structural gap means each new feature in `radio-api` is a new potential contention source for the BT audio thread. The system-isolation ideas in the cast doc's §7 (#9 CPU affinity, #10 logging audit, #11 background-op gating) and the sidecar-process idea below (#4 in this doc's §7) address this directly.

---

**Summary of patterns**: RTest differs from the reference cluster on discovery mechanism (Pattern 1), process isolation (Pattern 2), codec observability (Pattern 3), recovery completeness (Pattern 4), API-surface choice for frame counting (Pattern 5), and overall architectural shape (Pattern 6). The gaps cluster in two areas: *observability* (Patterns 1, 3) — where RTest does the work but can't see what happened — and *isolation* (Patterns 2, 4, 6) — where RTest's audio path competes with everything else in the process.

---

## 7. Speculative — things RTest could try (research output, not a roadmap)

Each idea explicitly **is not a commitment**. A future plan would consume any one of these and turn it into real work via the normal Builder/queue flow.

**Every idea below carries five mandatory measurement blocks** (per §3 measurement-discipline tier). If any block is missing or vague, the idea is not ready to leave research — it remains a research note, not a queue candidate.

Ideas are organized in two clusters: **#1-2** address specific known bugs (FM-BT-3 quiescence, FM-BT-1 race) and are the most-likely first-implementation candidates. **#3-5** address the architectural patterns identified in §6 (#3 the discovery-mechanism gap, #4 the codec-observability gap, #5 the structural-isolation gap) and are foundational — implementing #3 makes the others cleaner, and #5 is the largest-scope change with the broadest impact. The system-isolation ideas in the cast doc's §7 (#9 CPU affinity, #10 logging-path audit, #11 background-op gating) are shared with this doc because both pipelines run in `radio-api`.

---

> **Idea — Detect FM-BT-3 capture quiescence proactively via watchdog on `OnProcess` interval**
>
> **Addresses**: FM-BT-3 (capture loop quiesces silently — the known long-uptime bug)
>
> **Evidence motivating this** — `PipeWireNativeStream.OnProcess` already records `_lastOnProcessTimestamp` and logs interval stats every 10 s ([PipeWireNativeStream.cs:L257-L378](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs)). When the bug fires, the 10s log line stops appearing entirely — the callback isn't running. Today nothing acts on this signal; the user notices via silent speakers and restarts manually. So the *observable* phenomenon is already wired; the gap is detection + recovery.
>
> **What changes in RTest**: Add a background watchdog (Hosted Service or inline `Task.Run` on `BluetoothAudioSource`) that polls `_lastOnProcessTimestamp` (exposed via a new property on `PipeWireNativeStream`). If wall-clock now − `_lastOnProcessTimestamp` exceeds `BluetoothOptions.OnProcessStallThresholdMs` (default 5000) for 3 consecutive checks, raise `CaptureStreamStalled` (a new event). `BluetoothAudioSource.OnGeneratorStalled`'s recovery path already exists and is interlock-guarded — wire the new event into the same path.
>
> **Scope**: ~80 LOC. Files: `PipeWireNativeStream.cs` (expose timestamp), `LinuxBluetoothService.cs` (watchdog + event), `BluetoothAudioSource.cs` (subscribe), `BluetoothOptions.cs` (threshold). No protocol or external dependency change.
>
> **Risk / trade-off**: False positives during legitimate idle periods (BT source paused, no audio flowing). Mitigation: gate watchdog on PW node state (`bluez_input` reports `State = running` when audio is flowing — only watchdog under `running`).
>
> **Confidence**: **High** — confirmed by §4 FM-BT-3 column fills: reference systems sidestep the failure mode primarily by *structural* means (PW-stock + bluez-alsa daemon-process boundary; AOSP dedicated `audioserver` with explicit watchdogs). Within RTest's monolithic-process architecture, an explicit watchdog is the equivalent — the cleanest available mitigation short of the sidecar-process idea (#5 below).
>
> **Baseline probe**: 72 h soak with phone connected and periodic source switches, captured **alongside `PROBE-SYS-LOAD`** (per §3 concurrent-load discipline) so stall events can be classified as load-correlated vs not.
> ```bash
> # Audio probe: counts capture-stall events (windows with no '🔬 PipeWire OnProcess'
> # line for ≥60s despite BluetoothAudioSource state == Playing).
> ssh mmack@radio "journalctl -u radio-api --since '72 hours ago' -o cat" \
>   | python3 scripts/research/bt_stall_detect.py --window 60s \
>   > baseline_bt_stall.txt
>
> # Run concurrently for the full 72 h window:
> ssh mmack@radio "/opt/radio-console/scripts/research/sysload_capture.sh 259200" \
>   > baseline_bt_sysload.txt
>
> # Post-process: classify each stall event by load context
> python3 scripts/research/sysload_correlate.py \
>   baseline_bt_stall.txt baseline_bt_sysload.txt \
>   > baseline_bt_stall_classified.txt
> ```
> Output artifact: `baseline_bt_stall_classified.txt` containing `events_total=<N>, events_load_correlated=<L>, events_quiet_host=<Q>, mean_gap_minutes=<M>, max_gap_minutes=<X>, total_uptime_hours=<H>`. ("Load-correlated" = CPU >70 % OR `journalctl` line-rate >100/s OR active SSH session in the 5 s preceding the stall.)
>
> **Post-change probe**: identical command sequence + same parsers, same 72 h soak. Output: `after_bt_stall_classified.txt`.
>
> **Success criterion**:
> - `events_quiet_host` (stalls without concurrent host load) drops to `≤1` over the 72 h soak (this idea's primary target — these are the FM-BT-3 stalls the watchdog directly addresses)
> - For any `events_quiet_host` that remain: `OnGeneratorStalled` recovery fires within `≤15 s` of the stall
> - `events_load_correlated` is **expected to be unchanged or reduced only as a side effect** — this idea does not claim to fix FM-BT-11; that's the job of the system-isolation ideas (shared with Cast doc §7 Ideas #9-11). Reporting the delta separately makes the gap visible.
> - Audible-gap test (see §3 tools: loopback recording): no silence > 5 s in the 72 h recording during quiet-host windows
>
> **Debug-agent verification steps**:
> 1. `git checkout main && ./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`
> 2. SSH to `radio`, pair phone, start continuous playback, leave 72 h
> 3. Pull journal: `ssh mmack@radio "journalctl -u radio-api --since '72 hours ago' -o cat" > base_journal.log`
> 4. Run analyzer: `python3 scripts/research/bt_stall_detect.py --window 60s base_journal.log > baseline_bt_stall.txt`
> 5. `git checkout <feature-branch> && ./deploy/Deploy-ToLinux.ps1 ...`; repeat steps 2-4 to produce `after_bt_stall.txt`
> 6. Compare: `python3 scripts/research/bt_stall_compare.py baseline_bt_stall.txt after_bt_stall.txt` — outputs PASS / FAIL against the success criterion plus the per-metric deltas
>
> *(Analyzer scripts at `scripts/research/bt_stall_*.py` are not yet written; they are part of the research execution pass since the probe must be runnable before any code change.)*

---

> **Idea — Gate `autoSwitchOnConnect` on PW capture node existence**
>
> **Addresses**: FM-BT-1 (PW node never materializes — but RTest auto-switches anyway and retries acquisition for hours, contributing to FM-BT-3 long-uptime degradation)
>
> **Evidence motivating this** — `BluetoothAutoSwitchService.OnBluetoothDeviceConnected` ([BluetoothAutoSwitchService.cs:L56-L83](../../src/Radio.Infrastructure/Audio/Services/BluetoothAutoSwitchService.cs)) triggers source switch on BlueZ `DeviceConnected` event without checking whether the PW capture node is ready. `LinuxBluetoothService.GetAudioCaptureDeviceAsync` then enters its retry loop, scrapes `pw-cli` repeatedly, and consumes CPU + log volume for as long as the node remains absent. MEMORY references this as a hypothesized contributor to the long-uptime degradation (FM-BT-3).
>
> **What changes in RTest**: Before `GetOrCreateSourceAsync(Bluetooth, switchToSource: true)` fires, probe for the PW node via a short bounded check (e.g. 5 s with 500 ms polls). If found → switch as today. If not found → log + skip switch + register a one-shot subscriber that fires the switch when the node *does* appear (via a new `IBluetoothService.CaptureNodeAvailable` event).
>
> **Scope**: ~120 LOC. Files: `BluetoothAutoSwitchService.cs`, `IBluetoothService.cs`, `LinuxBluetoothService.cs` (raise event when `ParsePwCliOutputForBtNode` succeeds during periodic re-scan). No protocol change.
>
> **Risk / trade-off**: Loses the "quick auto-switch the moment BlueZ connects" snappiness if PW lags. Mitigation: 5 s probe window typically covers normal PW-node-materialization delay (which is sub-second on healthy systems); the explicit failure mode is the *long-duration* one this idea targets.
>
> **Confidence**: **High** — direct cause-effect with code evidence, no architectural unknowns.
>
> **Baseline probe**: 24 h scripted pair/unpair cycle, captured alongside `PROBE-SYS-LOAD` (per §3 concurrent-load discipline) so we can verify the `retry_loop_hours` reduction isn't being masked by an idle host coincidence.
> ```bash
> # Counts: (a) source switches to BT, (b) GetAudioCaptureDeviceAsync invocations,
> # (c) "waiting for PW node" log lines, over 24h with N pair/unpair cycles.
> ssh mmack@radio "journalctl -u radio-api --since '24 hours ago' -o cat" \
>   | python3 scripts/research/bt_autoswitch_audit.py \
>   > baseline_autoswitch.txt
>
> # Concurrent system-load capture for the same window:
> ssh mmack@radio "/opt/radio-console/scripts/research/sysload_capture.sh 86400" \
>   > baseline_autoswitch_sysload.txt
> ```
> Output: `switches=<N>, getcapture_invocations=<M>, waiting_log_lines=<L>, retry_loop_hours=<T>`, plus the sysload artifact for cross-reference (this idea doesn't filter by load context — included for completeness so a future reader can see whether the retry loop was load-amplified).
>
> **Post-change probe**: identical command + parser, after the change ships.
>
> **Success criterion**:
> - `waiting_log_lines` drops to ≤5 per pair/unpair cycle (vs current observed unbounded)
> - `retry_loop_hours` drops to 0 (no auto-switch occurring without a node present)
> - On healthy pair-with-node-ready cycles (control case), `getcapture_invocations` stays ≤2 — no regression for the happy path
>
> **Debug-agent verification steps**:
> 1. Set up: phone in airplane mode (forces no auto-pair on deploy), `mmack@radio` ready
> 2. Pre-change deploy on `main`, run for 24 h with scripted pair/unpair every 30 min (10 pairs without phone-side audio actually playing → simulates phone-without-A2DP-transport case)
> 3. Pull journal, run audit script → `baseline_autoswitch.txt`
> 4. Checkout feature branch, deploy, repeat step 2's 24 h soak with same scripted pair/unpair pattern
> 5. Pull journal, run audit script → `after_autoswitch.txt`
> 6. Run `python3 scripts/research/bt_autoswitch_compare.py baseline_autoswitch.txt after_autoswitch.txt` → PASS / FAIL + per-metric deltas

---

---

> **Idea — Replace `pw-cli` text scraping with direct PipeWire event subscription for node lifecycle**
>
> **Addresses**: FM-BT-1 (PW node never materializes — current scraping has a race window), FM-BT-2 (PW node disappears mid-session — current detection is indirect), foundational for several other ideas
>
> **Evidence motivating this** — §6 Pattern 1: every reference system uses event-driven node discovery; RTest is the only one that polls via `pw-cli ls Node` text scrape with the `ParsePwCliOutputForBtNode` parser ([LinuxBluetoothService.cs:L1247-L1394](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs); 13 unit tests added in PR #314). The *observable phenomenon*: time-to-detect a newly-appeared node is bounded below by the scrape interval (currently per-call inside `GetAudioCaptureDeviceAsync`'s retry loop); time-to-detect a node disappearance is undefined (relies on indirect signals like BlueZ disconnect or `OnGeneratorStalled`).
>
> **What changes in RTest**: Add `pw_registry_add_listener` via P/Invoke in `PipeWireNative.cs` / `PipeWireNativeStream.cs`; `LinuxBluetoothService` subscribes to `global_added` / `global_removed` events filtered for `bluez_input.<MAC>.a2dp-source` pattern. Raises new events `PipeWireBtNodeAppeared` / `PipeWireBtNodeDisappeared` for downstream consumers. Existing `ParsePwCliOutputForBtNode` retained as a fallback + as the basis for the unit tests.
>
> **Scope**: ~150 LOC: P/Invoke for `pw_registry_*`, event-bridge into `LinuxBluetoothService`. New tests in `PipeWireRegistryTests`. Existing `ParsePwCliOutputForBtNode` retained.
>
> **Risk / trade-off**: Registry event API has lifecycle subtleties (proxy lifetimes, thread affinity to PW thread loop); incorrect handling can crash or leak. Mitigation: extensive unit-test coverage via a mock registry, plus a fallback to the existing scraping path if the registry init fails.
>
> **Confidence**: **High** — matches the reference cluster's approach (§6 Pattern 1); eliminates a documented race window; foundation for cleaner versions of Ideas #1 and #2.
>
> **Baseline probe**: Pair/unpair cycle harness — script alternately enables/disables the phone's BT for 60 cycles over 1 hour; measure (a) time from phone-side enable to RTest detecting the PW node ("detection_latency_ms") and (b) time from phone-side disable to RTest detecting the disappearance ("teardown_latency_ms"). PROBE-SYS-LOAD captured concurrently.
> ```bash
> # Prereq instrumentation: emit metrics events
> #   "bluetooth.pw_node_detected" (with phone-side-enable wall-clock ts as event metadata)
> #   "bluetooth.pw_node_lost" (with phone-side-disable wall-clock ts)
> # in LinuxBluetoothService. Pair-cycle harness controls phone-side via adb (Android phone)
> # or AppleScript (iPhone via USB).
>
> ssh mmack@radio "/opt/radio-console/scripts/research/bt_pair_cycle_harness.sh \
>   --cycles 60 --period-sec 60" \
>   > baseline_bt_lifecycle.txt
>
> ssh mmack@radio "/opt/radio-console/scripts/research/sysload_capture.sh 3600" \
>   > baseline_bt_lifecycle_sysload.txt
>
> python3 scripts/research/bt_lifecycle_summarize.py \
>   baseline_bt_lifecycle.txt baseline_bt_lifecycle_sysload.txt \
>   > baseline_bt_lifecycle_classified.txt
> ```
> Output: `cycles=60, detection_latency_ms_p50=<X>, p95=<Y>, teardown_latency_ms_p50=<A>, p95=<B>, failed_detections=<F>, failed_teardowns=<T>`.
>
> **Post-change probe**: identical, after event-subscription ships.
>
> **Success criterion**:
> - `detection_latency_ms_p95` drops from baseline (expected `>1000 ms` due to scrape interval) to `≤200 ms`
> - `teardown_latency_ms_p95` drops to `≤500 ms` (today it's undefined / often "never until reconnect")
> - `failed_detections` drops to `0` (today potentially `>0` due to scrape window race)
> - **Negative check**: existing `ParsePwCliOutputForBtNode` unit tests continue to pass (fallback retained)
>
> **Debug-agent verification steps**:
> 1. Confirm pair-cycle harness operates the phone correctly on a `main` deploy (sanity: 1 cycle, observe node lifecycle in `pw-cli`)
> 2. Deploy `main`; run 60-cycle harness; save baseline
> 3. Deploy feature branch; repeat
> 4. `python3 scripts/research/bt_lifecycle_compare.py baseline_* after_*` — PASS/FAIL + per-percentile deltas

---

> **Idea — Surface negotiated A2DP codec + bitpool as observable metric**
>
> **Addresses**: FM-BT-6 (codec quality degradation, currently invisible to RTest)
>
> **Evidence motivating this** — §6 Pattern 3: every reference system makes codec choice explicit; RTest has no application-level visibility. Per [`pw-cli enum-params <node-id> Format`](https://docs.pipewire.org/page_spa_pod.html), the SPA format pod on a connected BT node carries the negotiated codec name. SBC bitpool is observable via `bluetoothctl info <MAC>` (BlueZ `MediaTransport1.Codec` + `Configuration` properties). The *observable phenomenon*: after a session where audio fidelity subjectively dropped, the user has no way to confirm whether the codec or bitpool actually changed — diagnosis depends on the user's memory rather than data.
>
> **What changes in RTest**: On every successful PipeWire stream connection, read the SPA format pod (already accessible during stream connect) and parse the codec identifier; query BlueZ `MediaTransport1.Codec` + `Configuration` for the bitpool / sample rate. Emit `bluetooth.a2dp.codec` and `bluetooth.a2dp.bitpool` gauges; log on change. Surface in the Radio.Web UI's BT panel.
>
> **Scope**: ~80 LOC: SPA pod parsing helper in `PipeWireNative.cs`, BlueZ property reader in `LinuxBluetoothService.cs`, metric emission, UI binding. Linux-only (Windows path is handled by `WindowsA2dpSinkManager` separately).
>
> **Risk / trade-off**: Format pod parsing on PipeWire requires correct SPA pod layout knowledge; the existing `pw_helper_build_s16le_format_pod` proves we can write one — reading requires the inverse. Mitigation: leverage `libspa-helpers` if available, or use `pw-cli enum-params` text-scrape as a Phase 1 implementation (with the understanding that Idea #3 above replaces the scraping in Phase 2).
>
> **Confidence**: **High** — observability addition, no behavior change; mostly safe.
>
> **Baseline probe**: 30-minute soak with three distinct phones connected sequentially (each forces a different codec negotiation if their AVDTP capability set differs). Capture: time-to-emit-codec-metric, codec-change events, bitpool-change events.
> ```bash
> # Prereq: instrumentation per the change above; before the change, the metric simply
> # doesn't exist, so the baseline run produces an empty artifact — that's the point.
>
> ssh mmack@radio "/opt/radio-console/scripts/research/bt_codec_observability_probe.sh \
>   --duration 1800 --phones 'phone-a,phone-b,phone-c'" \
>   > baseline_bt_codec.txt
> ```
> Output baseline: `events_emitted=0, codec_log_lines=0, ui_codec_displayed=false`.
>
> **Post-change probe**: identical command, after the change.
>
> **Success criterion**:
> - `events_emitted ≥ 3` (one per phone connect)
> - `codec_log_lines ≥ 3` with parseable codec names from a known set (`sbc`, `aac`, `aptx`, `aptx_hd`, `ldac`)
> - `ui_codec_displayed = true` (Radio.Web BT panel shows the current codec)
> - **Cross-reference check**: codec name reported matches `bluetoothctl info <MAC>` output for at least 2 of the 3 phones (the third is allowed to mismatch in case of non-standard codec naming; flagged for follow-up)
>
> **Debug-agent verification steps**:
> 1. Confirm 3 test phones with different codec capability sets are paired and reachable
> 2. Deploy `main`; run probe; save baseline (should report all zeros)
> 3. Deploy feature branch; repeat; save after
> 4. `python3 scripts/research/bt_codec_observability_compare.py baseline_* after_*` — PASS/FAIL + per-phone codec table

---

> **Idea — Extract BT capture into a dedicated `radio-bt-bridge` sidecar process**
>
> **Addresses**: FM-BT-3 (capture quiescence under long-uptime load), FM-BT-11 (host resource contention) — the *structural* fix corresponding to the system-isolation ideas in the cast doc
>
> **Evidence motivating this** — §6 Pattern 2 + Pattern 6: every reference system isolates BT audio handling in a dedicated process or daemon. RTest's PipeWire native thread runs inside `radio-api`, sharing the CLR's thread pool, GC, and JIT with web requests, fingerprinting, SQLite, and configuration. The *observable phenomenon*: when `radio-api` GC pauses (visible in `dotnet-counters monitor System.Runtime`), the `🔬 PipeWire OnProcess` log's `_maxOnProcessIntervalMs` correspondingly spikes; under heavy load, this correlates with FM-BT-3 quiescence.
>
> **What changes in RTest**: New `tools/Radio.BT.Bridge` project (`net10.0`, Linux-only). Hosts the `PipeWireNativeStream` capture, performs S16→float conversion + frame-alignment, ships float frames to `radio-api` via shared memory (`mmap` + lock-free ring buffer) or a Unix-domain socket. `radio-api`'s `LinuxBluetoothService` becomes a thin client of this bridge. The bridge process is small (no Blazor, no SQLite, no SignalR — just BT capture); systemd unit `radio-bt-bridge.service` runs it with `Nice=-10` and optionally `CPUAffinity=3`.
>
> **Scope**: ~600 LOC: new project skeleton, IPC mechanism (shared memory is the higher-perf option but more code than UDS), `radio-api` client wrapper. Plus systemd unit + deploy script updates. Substantial — comparable to the PipeWire-native-interop change in PR #262.
>
> **Risk / trade-off**: Higher complexity; another process to manage, another failure boundary. Mitigation: bridge keeps the existing `LinuxBluetoothService` interface, so consumers (`BluetoothAudioSource`) don't change. If the bridge crashes, `radio-api` falls back to the existing in-process path (degraded mode). Loses the `bluetooth.reconnect_*` metrics correlation with the rest of `radio-api`'s metrics unless they're re-emitted.
>
> **Confidence**: **Medium-high** — direct correspondence to the reference systems' architecture (§6 Pattern 2). Caveat: this is a *structural* change with broad impact, and its measured win may be smaller than expected if `radio-api`'s in-process load isn't currently the dominant contributor to FM-BT-3.
>
> **Baseline probe**: 72 h soak (same as Idea #1's watchdog probe) with full PROBE-SYS-LOAD + audio-recording PROBE-CAST-AUDIO-style probe applied to the BT-source-active session. Count: `events_total`, `events_quiet_host`, `events_load_correlated`, audio-output `silence_events_per_72h`.
>
> **Post-change probe**: identical, after bridge ships.
>
> **Success criterion** (must hold across two scenarios):
> - **Light load**: `events_quiet_host` does not regress (≤ baseline +1 over 72 h)
> - **Heavy load**: `events_load_correlated` drops by `≥80 %` (this is the idea's primary target — heavy-load events should drop because the BT thread is now in a separate, isolated process)
> - PROBE-SYS-LOAD shows: `radio-bt-bridge` CPU% nearly constant across light vs heavy host load (proving isolation), while baseline showed `radio-api`'s PW-thread CPU% varying widely
> - **Negative checks**: no new failure modes introduced (e.g., bridge crash rate `≤1/week`); IPC latency `≤2 ms` p99 (so the in-process consumer of the bridge doesn't introduce its own jitter); existing `BluetoothReconnectionLoop` metrics + AVRCP events continue to flow correctly through `radio-api`
>
> **Debug-agent verification steps**:
> 1. Verify `radio-bt-bridge.service` deploys + starts on `main` with the bridge code stubbed out as a no-op (sanity that the deploy machinery works before the real change)
> 2. Deploy `main` (in-process path); 72 h soak under light scenario; save artifacts
> 3. 72 h soak under heavy scenario; save artifacts
> 4. Deploy feature branch (bridge active); repeat both 72 h soaks
> 5. `python3 scripts/research/bt_bridge_compare.py baseline_* after_*` — PASS/FAIL per scenario + cross-scenario isolation delta
> 6. Per-scenario `pidstat` snapshots: confirm `radio-bt-bridge` CPU% stable; `radio-api` PW-thread CPU% absent post-change

---

*[Two further idea categories remain unexplored and could be added during the research execution pass: (a) AVRCP polling vs event-driven sync (FM-BT-8 — the current event-driven path through D-Bus is correct but lacks recovery on subscription gaps); (b) reconnect-loop refinements (FM-BT-9 — `BluetoothReconnectionLoop` could integrate with the PW-event subscription from Idea #3 to detect "phone is back" without scraping).]*

---

## 8. Out-of-band notes

- All reference walks date-stamped at fill time.
- If reading `Arkq/bluez-alsa` source reveals it's diverged enough from ALSA-direct semantics that the comparison column collapses to "another userspace BT daemon," the column will be replaced with `raspotify` (open-source, BT-A2DP-receiver Pi appliance) per §2 substitution policy.
- AOSP source URLs are pinned to a specific tag at fill time (e.g. `android-15.0.0_r1`) because Bluedroid/Fluoride churns.
- Measurement scripts referenced in §7 ideas (`bt_stall_detect.py`, `bt_stall_compare.py`, `bt_autoswitch_audit.py`, etc.) are written during the research execution pass and committed to `scripts/research/`. They are *part of the research deliverable* — not a separate prerequisite — because an idea without a runnable probe is incomplete per §3.

---

## 9. Execution checklist (for the research pass that fills this doc)

### Framework (this commit)
- [x] §1-3 scope, reference systems, methodology (incl. measurement-discipline tier)
- [x] §4 failure-mode catalog with 11 modes defined (FM-BT-11 added 2026-05-22 for host-resource-contention); RTest column citations seeded
- [x] §5 pipeline-table row definitions (11 rows incl. host-resource-contention surface added 2026-05-22); RTest column citations seeded
- [x] §6 synthesis section stubbed
- [x] §7 speculative ideas section stubbed + 2 illustrative drafts to validate measurement structure
- [x] §8 out-of-band notes
- [x] §9 execution checklist

### Source walks
- [x] RTest BT source walk — RTest column of §4 + §5 filled with citations from `LinuxBluetoothService.cs`, `BluetoothAudioSource.cs`, `PipeWireNativeStream.cs`, `BluetoothAutoSwitchService.cs`, `BluetoothReconnectionLoop.cs`
- [x] PW-stock walk — `[doc-cited]` references to `pipewire/spa/plugins/bluez5/` + WirePlumber `bluez_monitor.lua`; deeper source walk of `bluez_monitor` LUA logic still possible if needed
- [x] bluez-alsa walk — `[doc-cited]` references to `Arkq/bluez-alsa` README + wiki structure; deeper source walk of the daemon's media-endpoint handler still possible if needed
- [x] AOSP-BT walk — `[doc-cited]` references to `packages/modules/Bluetooth/system/bta/`, `frameworks/av/services/audioflinger/`, `source.android.com/docs/core/audio/`; pinned to AOSP main branch as of 2026-05-22

### Live-inspection probes (require LAN access — pending)
- [ ] `pw-top` and `pw-dump` capture during normal RTest BT session (control baseline)
- [ ] `btmon` capture during normal RTest BT session (HCI-level reference for FM-BT-5 transport jitter)
- [ ] `journalctl` capture during the *known-bug* reproduction (FM-BT-3 long-uptime; needs 72 h soak)
- [ ] Loopback audio recording during a 1 h normal session — baseline for FFT silence-run detection
- [ ] Same probes on a stock PW phone-to-speakers setup (no RTest) — strongest control for Pattern 2
- [ ] `PROBE-SYS-LOAD` baselines (light + heavy scenarios) on `radio` host

### Synthesis + ideas
- [x] §6 synthesis written — 6 patterns identified (scraping vs event-driven, in-process vs daemon, codec invisibility, recovery gaps, frame-alignment API mismatch, structural-vs-additive isolation)
- [x] §7 base illustrative drafts (watchdog on `OnProcess`, `autoSwitchOnConnect` gating) updated with PROBE-SYS-LOAD + load-correlation classification
- [x] §7 BT-specific ideas added: #3 PW event subscription, #4 codec observability, #5 `radio-bt-bridge` sidecar
- [x] §7 cross-references to cast doc's shared system-isolation ideas (#9 CPU affinity, #10 logging audit, #11 background-op gating) in §3
- [ ] §7 follow-ups noted at end of section — AVRCP recovery (FM-BT-8), reconnect-loop refinements (FM-BT-9) — to be drafted during research execution pass if patterns surface from live-inspection probes

### Probe scripts (research deliverable)
- [ ] `scripts/research/bt_stall_detect.py` — windowed `🔬 PipeWire OnProcess` absence detector
- [ ] `scripts/research/bt_autoswitch_audit.py` — counts switches / getcapture invocations / waiting-log lines / retry-loop hours from journal
- [ ] `scripts/research/bt_pair_cycle_harness.sh` — scripted pair/unpair cycle driver (phone-side automation)
- [ ] `scripts/research/bt_lifecycle_summarize.py` — detection/teardown latency distributions
- [ ] `scripts/research/bt_codec_observability_probe.sh` + `bt_codec_observability_compare.py` — multi-phone codec capture
- [ ] `scripts/research/bt_bridge_compare.py` — cross-scenario isolation delta computation
- [ ] `scripts/research/sysload_capture.sh` + `sysload_correlate.py` — shared with cast doc; not yet authored

### Self-review + surface
- [x] Framework self-review (placeholders / contradictions / scope)
- [x] Every §7 idea has all five measurement blocks completed
- [x] All idea probes capture PROBE-SYS-LOAD concurrently (per §3 concurrent-load discipline)
- [x] Surface to user for review (commits f0145d4 cast, 1ed52e8 BT framework, bda7d7a system-load, current commit reference fills + synthesis + ideas)
