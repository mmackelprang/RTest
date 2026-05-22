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

Ten modes, each independently capable of producing a distinct audible or operational failure. The matrix below is filled per system × mode during the research pass.

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

### Failure-mode matrix (to be filled)

For each cell:

- **Exposure** — Y/N + one-line "this system is vulnerable because…"
- **Mitigation** — concrete: detection mechanism, recovery path, telemetry surface
- **Evidence** — `[source-walked]`, `[doc-cited]`, or `[inferred-from-behavior]`

| Mode | RTest | PW-stock | bluez-alsa | AOSP-BT |
|---|---|---|---|---|
| FM-BT-1 — PW node never materializes | *[pending RTest source-walk + bug-history dive]* | *[pending]* | n/a (no PW) | *[pending]* |
| FM-BT-2 — PW node disappears mid-session | *[pending]* | *[pending]* | n/a (no PW) | *[pending]* |
| FM-BT-3 — Capture loop quiesces silently | *[pending — this is the known long-uptime bug; needs reproduction recipe + diagnostic probe before mitigation can be cited]* | *[pending]* | *[pending]* | *[pending]* |
| FM-BT-4 — Mixer-side generator stall | *[pending — `GeneratorStalled` event + `_recoveryInProgress` interlock wired; needs exposure/mitigation summary]* | n/a (no mixer between PW and output) | n/a | *[pending]* |
| FM-BT-5 — BT transport jitter / packet loss | *[pending]* | *[pending]* | *[pending]* | *[pending]* |
| FM-BT-6 — Codec quality degradation | *[pending — RTest may or may not be aware of codec; need to walk BluezAgent + transport setup]* | *[pending]* | *[pending — bluez-alsa exposes codec selection explicitly]* | *[pending]* |
| FM-BT-7 — Frame-alignment misalignment | *[pending — guard exists at PipeWireNativeStream.cs:L309-L316; need to confirm coverage of all PW BT chunk shapes]* | *[pending]* | *[pending — ALSA handles framing differently]* | *[pending]* |
| FM-BT-8 — AVRCP desync | *[pending — BluetoothAudioSource subscribes to MetadataChanged, PlaybackStatusChanged, PositionChanged; need to map full AVRCP state-sync surface]* | *[pending]* | n/a (bluez-alsa is A2DP only, no AVRCP) | *[pending]* |
| FM-BT-9 — Reconnect-after-walkaway | *[pending — `BluetoothReconnectionLoop` exists with exponential backoff; need to walk its trigger conditions]* | *[pending]* | *[pending]* | *[pending]* |
| FM-BT-10 — Adapter / profile contention | *[pending — dual-adapter split deployed; need to enumerate residual race conditions]* | *[pending]* | *[pending]* | *[pending]* |

---

## 5. Pipeline table (the apples-to-apples reference)

Ten rows × the same four columns. Each cell with an evidence tag.

| Row | What it captures | RTest | PW-stock | bluez-alsa | AOSP-BT |
|---|---|---|---|---|---|
| Capture origin | What kind of object delivers audio to userspace? | PipeWire capture node (e.g. `bluez_input.78:20:51:F5:FB:A7.a2dp-source`) acquired via `pw-cli` discovery + native `pw_stream_connect` against the node's `object.serial`. [source-walked, LinuxBluetoothService.cs:L1247-L1394; PipeWireNativeStream.cs:L67-L184] | *[pending]* | *[pending]* | *[pending]* |
| Discovery & negotiation | How does the audio path learn a new BT source exists? | BlueZ D-Bus `org.bluez.MediaTransport1` interface monitored via `WatchDevicePropertiesAsync` + `AttachMediaTransportAsync`; PW node discovered separately via `pw-cli ls Node` scrape with `ParsePwCliOutputForBtNode` parser ([PR #314 tests](../../tests/Radio.Infrastructure.Tests/Platform/Bluetooth/PipeWireNodeParsingTests.cs)). [source-walked, LinuxBluetoothService.cs:L648-L953,L1247] | *[pending]* | *[pending]* | *[pending]* |
| Profile selection | Which BT profiles are activated and accepted? | A2DP sink only on `hci0` (TP-Link UB500); HFP excluded by WirePlumber config `87-bt-adapter-select.lua`. [source-walked, deploy WP configs; doc-cited, RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md] | *[pending]* | *[pending]* | *[pending]* |
| Codec | Which codecs negotiable? Default? Fallback? | RTest does not enumerate or pin codec; relies on whatever BlueZ + PipeWire's `bluez5` plugin negotiate. Default usually SBC; AAC if both ends advertise. No application-level visibility into bitpool. [inferred-from-behavior + source-walked, LinuxBluetoothService.cs full file — no codec API calls present] | *[pending]* | *[pending — bluez-alsa exposes `--profile-a2dp-source` and codec selection explicitly]* | *[pending]* |
| Stream API | What API delivers audio bytes to the app? | PipeWire native `pw_stream` via P/Invoke. `OnProcess` callback fires on PW thread loop with S16_LE buffers; converted to float, frame-aligned, delivered via `AudioDataCallback` to `BufferedSoundGenerator<float>`. [source-walked, PipeWireNativeStream.cs:L231-L378] | *[pending]* | *[pending]* | *[pending]* |
| Transport buffer model | What buffer / quantum drives the data flow? | PW quantum forced to `default.clock.min-quantum = 512` (≈ 10.67 ms) via `~/.config/pipewire/pipewire.conf.d/99-radio-quantum.conf` to match BT transport quantum and eliminate xruns. [source-walked, deploy configs; doc-cited, MEMORY: "PipeWire Quantum Tuning"] | *[pending]* | *[pending — ALSA period_size + buffer_size]* | *[pending]* |
| Frame alignment & format conversion | Where does S16→float happen? How are non-aligned chunks handled? | S16_LE → float inline in `OnProcess` via unsafe `Span<short>` cast (no per-sample marshal). Frame-alignment guard: `sampleCount = sampleCount / channels * channels`. [source-walked, PipeWireNativeStream.cs:L309-L337] | *[pending]* | *[pending]* | *[pending]* |
| Recovery model | What triggers a restart / reconnect? | (a) `BluetoothReconnectionLoop` (exponential backoff on disconnect, [BluetoothReconnectionLoop.cs](../../src/Radio.Infrastructure/Platform/Bluetooth/BluetoothReconnectionLoop.cs)); (b) `CaptureStreamRecovered` event from `IBluetoothService`; (c) `SoundFlowPlaybackService.GeneratorStalled` → `OnGeneratorStalled` with `_recoveryInProgress` interlock dedup ([BluetoothAudioSource.cs:L354-L390](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)); (d) double-search guard in `GetAudioCaptureDeviceAsync` race ([LinuxBluetoothService.cs:L1022,L1045](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)). [source-walked] | *[pending]* | *[pending]* | *[pending]* |
| Observable telemetry | What counters / logs / events does the system surface? | (a) `🔬 PipeWire OnProcess` 10s-window stats (count, min/max interval, burst count for sub-1ms intervals, max execution ms) — [PipeWireNativeStream.cs:L364-L376](../../src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs); (b) `bluetooth.reconnect_*_total` counters in Radio.Metrics SQLite store; (c) BlueZ D-Bus property-change logs. [source-walked] | *[pending — `pw-top`, `pw-dump`, journal]* | *[pending — `bluealsa --syslog`]* | *[pending — Android `dumpsys media.audio_flinger`]* |
| Metadata channel (AVRCP) | How does track / play-state / volume info flow? | Separate from audio. BluezAgent + `AttachMediaPlayerAsync` ([LinuxBluetoothService.cs:L2043](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)) subscribes to `org.bluez.MediaPlayer1` D-Bus property changes; events bridge to `BluetoothAudioSource.OnMetadataChanged` / `OnPlaybackStatusChanged` / `OnPositionChanged`. [source-walked] | *[pending]* | n/a (no AVRCP in bluez-alsa) | *[pending]* |

---

## 6. Findings synthesis

*[To be written after the failure-mode matrix and pipeline table are filled. Should identify 4-6 patterns where RTest differs from the reference cluster on a measurable axis, in the same shape as the Cast doc §6.]*

---

## 7. Speculative — things RTest could try (research output, not a roadmap)

Each idea explicitly **is not a commitment**. A future plan would consume any one of these and turn it into real work via the normal Builder/queue flow.

**Every idea below carries five mandatory measurement blocks** (per §3 measurement-discipline tier). If any block is missing or vague, the idea is not ready to leave research — it remains a research note, not a queue candidate.

*[Ideas are written after §6 synthesis identifies patterns. The two entries below are illustrative drafts shown so the user can validate the structure before the research pass produces the full set; they will be tightened, validated against the source walk, or replaced once §4 / §5 are filled.]*

---

> **Idea (draft) — Detect FM-BT-3 capture quiescence proactively via watchdog on `OnProcess` interval**
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
> **Confidence**: **High** *(pending FM-BT-3 column fill — confidence rises if reference systems also detect callback-stall, falls if they sidestep it by never having the failure mode)*.
>
> **Baseline probe**:
> ```bash
> # Run for 72h on Ubuntu N100 with phone connected, periodic source switches.
> # Counts capture-stall events (windows with no '🔬 PipeWire OnProcess' line for ≥60s
> # despite BluetoothAudioSource state == Playing).
> ssh mmack@radio "journalctl -u radio-api --since '72 hours ago' -o cat" \
>   | python3 scripts/research/bt_stall_detect.py --window 60s \
>   > baseline_bt_stall.txt
> ```
> Output artifact: `baseline_bt_stall.txt` containing `events=<N>, mean_gap_minutes=<M>, max_gap_minutes=<X>, total_uptime_hours=<H>`.
>
> **Post-change probe**: identical command + parser, same 72 h soak. Output: `after_bt_stall.txt`.
>
> **Success criterion**:
> - `events` count drops to ≤1 over a 72 h soak (vs current observed multiple per week)
> - For any event that still occurs: `OnGeneratorStalled` recovery fires within ≤15 s of the stall (verified by log timestamps)
> - Audible-gap test (see §3 tools: loopback recording): no silence > 5 s in the 72 h recording
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

> **Idea (draft) — Gate `autoSwitchOnConnect` on PW capture node existence**
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
> **Baseline probe**:
> ```bash
> # Counts: (a) source switches to BT, (b) GetAudioCaptureDeviceAsync invocations,
> # (c) "waiting for PW node" log lines, over 24h with N pair/unpair cycles.
> ssh mmack@radio "journalctl -u radio-api --since '24 hours ago' -o cat" \
>   | python3 scripts/research/bt_autoswitch_audit.py \
>   > baseline_autoswitch.txt
> ```
> Output: `switches=<N>, getcapture_invocations=<M>, waiting_log_lines=<L>, retry_loop_hours=<T>`.
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

*[Further ideas — codec pinning, watchdog on `pw-top` xrun counter, AVRCP polling vs event-driven sync, reconnect-loop refinements, PW node lifecycle subscription via PW events instead of `pw-cli` scrape — to be drafted after §4 / §5 column fills and §6 synthesis.]*

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
- [x] §4 failure-mode catalog with 10 modes defined; RTest column citations seeded
- [x] §5 pipeline-table row definitions; RTest column citations seeded
- [x] §6 synthesis section stubbed
- [x] §7 speculative ideas section stubbed + 2 illustrative drafts to validate measurement structure
- [x] §8 out-of-band notes
- [x] §9 execution checklist

### Source walks
- [ ] RTest full BT source walk — fill RTest column of §4 + §5 with citations from `LinuxBluetoothService.cs`, `BluezAgent.cs`, `BluetoothAudioSource.cs`, `PipeWireNativeStream.cs`, `BluetoothAutoSwitchService.cs`, `BluetoothReconnectionLoop.cs`, `BluetoothMgmtMonitor.cs`
- [ ] PW-stock walk — `pipewire/spa/plugins/bluez5/` source + WirePlumber default policy + relevant blog posts
- [ ] bluez-alsa walk — `Arkq/bluez-alsa` source on GitHub (README + key handlers)
- [ ] AOSP-BT walk — `packages/modules/Bluetooth/system/` + `frameworks/av/services/audioflinger/`, pinned to a specific release tag

### Live-inspection probes
- [ ] `pw-top` and `pw-dump` capture during normal RTest BT session (control)
- [ ] `btmon` capture during normal RTest BT session (HCI-level reference for FM-BT-5 transport jitter)
- [ ] `journalctl` capture during the *known-bug* reproduction (FM-BT-3 long-uptime; needs the soak)
- [ ] Loopback audio recording during a 1 h normal session — baseline for FFT silence-run detection
- [ ] Same probes on a stock PW phone-to-speakers setup (no RTest) — strongest control

### Synthesis + ideas
- [ ] Write §6 synthesis after columns filled
- [ ] Promote §7 illustrative drafts to validated ideas (each with full 5-block measurement)
- [ ] Add remaining ideas (codec pinning, xrun watchdog, AVRCP polling, reconnect refinements, PW-event-driven node subscription)

### Self-review + surface
- [ ] Spec self-review (placeholders / contradictions / scope)
- [ ] Confirm every §7 idea has all five measurement blocks completed
- [ ] Surface to user for review
