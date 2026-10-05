# HANDOFF — RotaryPhone call-audio debugging ("no audio when calling the rotary phone")

**Prepared by:** Radio Console (RTest) session, 2026-06-13 — investigation only, no RotaryPhone edits.
**For:** the **RotaryPhone session** (`D:\prj\RotaryPhone`). The call-audio path is RotaryPhone-domain (Intel `hci1` voice / SIP / HT801) per the BT/audio boundary doc; fixes belong there.
**Symptom:** Calling the rotary phone from a cell phone connects but there's **no / broken call audio**.

---

## Live state pulled from `radio:5004` (2026-06-13)

| Endpoint | Result | Read |
|---|---|---|
| `/api/gvbridge/status` | `{available:true, activeMode:"GVApi", sipRegistered:true, cookiesValid:true}` | **Active call path is GVApi**, GV auth healthy |
| `/api/phone/system-status` | `{bluetoothConnected:true, sipListening:true, ht801IpAddress:"192.168.86.22", ht801Reachable:true}` | HT801 reachable — at **`.22`**, not the `.250` in the stale TODO doc |
| `/api/diagnostics/audio-bridge` | `{isActive:false, inboundFramesSent:0, outboundFramesReceived:0, bidirectionalAudio:false}` | GV audio bridge has **never carried a frame** (sampled with no call active) |

**Conclusion:** The active path is **GVApi → `GVAudioBridgeService`** (WebSocket PCM ↔ RTP G.711 → HT801), *not* the Bluetooth-HFP SCO bridge. Debug the GV path **first**. The SCO bridge (Bluetooth-HFP mode) has separate, confirmed bugs — covered below in case that's the mode in use.

> **Step 0 (confirm the mode):** Ask the user which adapter mode they call through. `GET /api/gvbridge/adapter/mode` shows `activeMode` + available modes `[BluetoothHfp, SipTrunk, GVApi]`. Currently `GVApi`. Pursue the matching track.

---

## TRACK A — GV path (`GVApi`, active mode) — debug this first

Audio: cell → Google Voice number → Chrome extension (tabCapture, 16 kHz PCM over WebSocket) → `GVAudioBridgeService` (resample 16↔8 kHz, G.711 µ-law, SIPSorcery `RTPSession`) → RTP to HT801 → rotary handset, and the reverse.

Key files:
- `src/RotaryPhoneController.GVBridge/Services/GVAudioBridgeService.cs` — binds `RTPSession` to `GVBridgeConfig.LocalRtpPort`, `SetDestination(HT801Ip:HT801RtpPort)`, inbound loop drains `InboundAudioQueue`, `OnRtpPacketReceived` → WS.
- `src/RotaryPhoneController.GVBridge/Models/GVBridgeConfig.cs` — `HT801Ip`, `HT801RtpPort`, `LocalRtpPort`.
- `src/RotaryPhoneController.GVBridge/Adapters/GVBrowserAdapter.cs` — `OnCallAnswered → _audioBridge.StartAsync()`, `OnCallEnded → StopAsync()`.

**Hypotheses, in priority order:**

- **H1 — RTP destination IP/port mismatch (TOP SUSPECT).** The HT801 moved to `192.168.86.22` (system-status), but the stale TODO doc and old config used `192.168.86.250`. If `GVBridgeConfig.HT801Ip` (appsettings on `radio`) still points at `.250` (or `HT801RtpPort` is wrong), the bridge streams RTP into the void — **SIP registration + ringing still work** (different config path), but there's **no media**. → Verify `GVBridgeConfig.HT801Ip`/`HT801RtpPort` against the live HT801 (`.22`) and the SDP the HT801 actually negotiates. Cheapest possible fix if it's just a stale IP.
- **H2 — bridge not starting on answer.** `OnCallAnswered` should flip `audio-bridge.isActive` true. **Place a live call and poll `GET /api/diagnostics/audio-bridge` during it.** If `isActive` stays false → the `GVBrowserAdapter` event chain isn't firing `StartAsync` (wiring/event-name drift).
- **H3 — no inbound media from Chrome.** If during a call `isActive:true` but `inboundFramesSent` stays 0 → WebSocket PCM frames aren't arriving from the extension (tabCapture / offscreen doc / `audioFrame` relay). Check `ChromeExtension/offscreen/audio-bridge.js` + `content/gv-bridge.js`.
- **H4 — one-way audio.** Compare `inboundFramesSent` (GV→phone) vs `outboundFramesReceived` (phone→GV). A zero on one side isolates the dead direction (HT801 RTP dest vs Chrome capture).

**Diagnostic procedure (one live call):**
1. `GET /api/diagnostics/timeline` + `/api/diagnostics/sip-log` — did INVITE → 180 Ringing → 200 OK complete? (INVITE-timeout diagnoses are auto-generated; SDP-port suggestion is relevant here.)
2. During the call, poll `/api/diagnostics/audio-bridge` every ~2 s — watch `isActive` + the two frame counters + `*Errors`.
3. `POST /api/diagnostics/test-audio` (sends a 1 kHz tone via RTP to HT801) and `POST /api/diagnostics/test-ring` — isolates "is the RTP→HT801 leg alive" from the Chrome/WS leg.

---

## TRACK B — Bluetooth-HFP SCO path (only if `activeMode == BluetoothHfp`)

Two **confirmed** bugs (both must be fixed for SCO audio to work end-to-end):

- **B1 — Python 3.12 `BTPROTO_SCO` bind is broken.** `scripts/bt_manager.py:_accept_sco` (~line 479) does `sco_listen = socket.socket(AF_BLUETOOTH, SOCK_SEQPACKET, BTPROTO_SCO)` then `sco_listen.bind(bytes(6))`. Python 3.12's `socket.bind()` rejects `BTPROTO_SCO` sockaddr → SCO never accepts → no audio path at all. **Fix (per `docs/TODO-remaining-work.md`):** call C `bind()` via `ctypes` directly (tested-working fix exists, needs applying). Note there's a second SCO entry point `ScoAudioBridge.start()` (~line 73) using `socket.fromfd` — reconcile which path is live.
- **B2 — RTP framing missing in `ScoRtpBridge.cs`.** This is the "RTP framing still TODO" item, and it's real:
  - `ScoToRtpLoop` (lines 86-87): sends `G711Codec.EncodeMuLaw(pcm)` straight over UDP to HT801 with **no 12-byte RTP header** prepended → HT801 receives non-RTP garbage.
  - `RtpToScoLoop` (line 109): runs `G711Codec.DecodeMuLaw(rtpData)` over the **entire** received datagram **including the RTP header** (doesn't strip the 12 bytes) → corrupted audio to the phone.
  - Also `_rtpClient = new UdpClient(0)` (line 61, ephemeral) — the HT801 must send RTP to a known port (SDP-advertised); an unmatched port → inbound silence.
  - **Fix:** add proper RTP header build/parse (seq, timestamp, SSRC, PT=0 PCMU), or — simpler and consistent with the GV path — replace the hand-rolled `UdpClient` plumbing with a SIPSorcery `RTPSession` (which `GVAudioBridgeService` already uses correctly).

---

## Boundary reminders (read `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` first)

- Voice/HFP/SCO = Intel `hci1` (`10:91:D1:FE:00:46`), owned by RotaryPhone. `bluetoothctl -- select 10:91:D1:FE:00:46` before any BT command.
- WirePlumber does **not** manage `hci1` (RotaryPhone handles SCO/eSCO routing itself).
- **Do not pair the same device on both adapters** (rule #8) — duplicate PipeWire devices break profile resolution.
- The RTest deploy just **restarted wireplumber** (cycles BT) — if BT looks off, reconnect per the recovery sequence before debugging.
- When you change anything, update the boundary doc Change Log.

---

## Ready-to-paste prompt for the RotaryPhone session

> We're debugging **no/broken call audio when calling the rotary phone from a cell**. Read this handoff (`D:\prj\RTest\RTest\docs\HANDOFF-rotaryphone-call-audio-debug.md`) and the boundary doc first, then use `superpowers:systematic-debugging` — find root cause before fixing.
>
> Live state (2026-06-13): active mode = **GVApi**, SIP registered, cookies valid, HT801 reachable at **192.168.86.22**, audio-bridge `isActive:false`/0 frames.
>
> **Start on TRACK A (GV path).** First verify H1: does `GVBridgeConfig.HT801Ip`/`HT801RtpPort` (appsettings on `radio`) match the live HT801 at `.22`? Then place a live call and poll `/api/diagnostics/audio-bridge` + read `/api/diagnostics/timeline` + `/sip-log` to localize where media dies (bridge not starting / no Chrome PCM in / RTP not reaching HT801 / one-way). Only pursue TRACK B (SCO bugs B1 `_accept_sco` ctypes bind + B2 `ScoRtpBridge.cs` RTP framing) if the user is actually on Bluetooth-HFP mode. Update the boundary-doc Change Log for any change.
