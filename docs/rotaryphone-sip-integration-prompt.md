# RotaryPhone SIP Integration — RTest UI Update Prompt

## Context

The RotaryPhone project has undergone a **major architectural change** — the Chrome extension-based Google Voice audio relay has been replaced with direct SIP-over-WebSocket + DTLS-SRTP signaling. This eliminates the Chrome dependency entirely but **breaks several RTest UI integrations** that referenced the old architecture.

**PR:** mmackelprang/RotaryPhone#19 (`feature/sip-wss-audio`)

## What Changed in RotaryPhone

### Removed
- `GVBridgeService` (Chrome extension WebSocket server on port 8765)
- `GVBridgeHub` SignalR hub at `/hubs/gvbridge`
- `ExtensionMessage` models (MuteTab, UnmuteTab, AnswerMessage, etc.)
- `GvCallClient` / `GvSmsClient` (HTTP API for call control)
- `GvSignalerClient` (long-poll for incoming calls)
- `GvCookieJar` / `GvCookieRotationService`
- `GvLoginTool` (Playwright-based cookie extraction)
- Chrome extension directory
- `CallAdapterMode.GVBrowser` is effectively replaced by `CallAdapterMode.GVApi`

### Added
- `GvSipTransport` — SIP-over-WebSocket signaling + DTLS-SRTP + Opus audio (1100 lines)
- `GvSipCredentialProvider` — fetches SIP credentials from `sipregisterinfo/get`
- `CookieRetriever` — Chrome/Chromium CDP cookie extraction (replaces GvLoginTool)
- `GvCookieSet` — raw cookie header capture (replaces per-cookie GvCookieJar)
- Audio bridge now handles 48kHz Opus ↔ 8kHz G.711 (was 16kHz ↔ 8kHz)

### API Changes
- `GET /api/gvbridge/adapter/mode` — still works, but `GVBrowser` mode no longer exists; use `GVApi`
- `PUT /api/gvbridge/adapter/mode` — same
- `/hubs/gvbridge` — **DELETED**. No more SignalR hub for GV Bridge status.
- Extension connection status — **GONE**. No Chrome extension means no connection status.
- SIP registration status — available via `GvSipTransport` but **not yet exposed as REST/SignalR endpoint**

### Call Flow Change
**Before:** GVApiAdapter → HTTP API `call/create` → Chrome extension clicks Answer button → tabCapture audio → WebSocket → RTP → HT801

**After:** GVApiAdapter → SIP INVITE via WebSocket to `wss://web.voice.telephony.goog` → DTLS-SRTP Opus audio → resample → RTP → HT801

No browser in the loop. Calls are pure SIP.

## What Needs Updating in RTest

### 1. PhonePage.razor — Mode Selector

**Current:** Shows three buttons: `Bluetooth HFP`, `SIP Trunk`, `GV Browser`
**Problem:** `GV Browser` mode no longer exists. The extension connection status badge is meaningless.
**Fix:** Replace `GV Browser` with `GV API (SIP)`. Remove extension connection indicator. Add SIP registration status instead.

### 2. GvBridgeHubService.cs — DELETED Hub

**Current:** Connects to `/hubs/gvbridge` for extension status events
**Problem:** That hub no longer exists
**Fix:** Either delete `GvBridgeHubService.cs` entirely, or repurpose it to poll REST endpoint for GV API status. The simplest approach: delete the hub service, use REST polling only (via `GvBridgeApiService`).

### 3. GvBridgeApiService.cs — Endpoint Updates

**Current:** Calls endpoints like:
- `GET /api/gvbridge/status` — returns `GvBridgeStatusDto` with `ExtensionConnected`, `ExtensionVersion`
- `GET /api/gvbridge/adapter/mode` — returns current mode
- `PUT /api/gvbridge/adapter/mode` — switches mode

**Fix:**
- `GET /api/gvbridge/status` needs updating on RotaryPhone side to return SIP registration status instead of extension status. The DTO should change:
  ```csharp
  // OLD
  public class GvBridgeStatusDto
  {
      public string ActiveMode { get; set; }
      public bool ExtensionConnected { get; set; }
      public string? ExtensionVersion { get; set; }
  }

  // NEW
  public class GvBridgeStatusDto
  {
      public string ActiveMode { get; set; }      // "GVApi" instead of "GVBrowser"
      public bool SipRegistered { get; set; }     // SIP REGISTER succeeded
      public bool CookiesValid { get; set; }      // Health check passing
      public bool Available { get; set; }          // Adapter is active
  }
  ```
- Mode switch: `GVBrowser` → `GVApi` in all mode string comparisons
- Adapter/mode endpoint still works, just different mode values

### 4. ApiModels.cs — DTO Updates

Update `GvBridgeStatusDto` to match the new RotaryPhone API response (see above).

### 5. PhoneCallIntegrationService.cs — Call State Events

**Current:** Listens for `CallStateChanged(state, phoneNumber, callerName)` from RotaryPhone SignalR hub at `/hub`
**Status:** This still works — CallManager hasn't changed. The SignalR hub at port 5004 is the CallManager hub, not the GVBridge hub.
**No change needed** for incoming call announcements.

### 6. SystemConfigPage.razor — Cookie Management

**New requirement:** Add a section for Google Voice cookie management:
- Button: "Extract GV Cookies" — triggers `gv-login` CLI on the server
- Status: Shows whether cookies are present and valid (health check)
- Instructions: "Ensure Chrome/Chromium is running on the server with voice.google.com logged in"

This is a **nice-to-have** for UAT, not a blocker.

## Files to Modify in RTest

| File | Change |
|---|---|
| `src/Radio.Web/components/Pages/PhonePage.razor` | Replace "GV Browser" with "GV API (SIP)", remove extension badge, add SIP status |
| `src/Radio.Web/services/ApiClients/GvBridgeApiService.cs` | Update endpoints/DTOs for new API |
| `src/Radio.Web/services/ApiClients/GvBridgeHubService.cs` | Delete or stub out (hub no longer exists) |
| `src/Radio.Web/models/ApiModels.cs` | Update `GvBridgeStatusDto` |
| `src/Radio.API/services/PhoneCallIntegrationService.cs` | No change needed (uses CallManager hub, not GVBridge hub) |

## Files to Add/Modify in RotaryPhone

The RotaryPhone side needs a few endpoints exposed for the new architecture:

| File | Change |
|---|---|
| `src/RotaryPhoneController.GVBridge/Api/GVBridgeController.cs` | Update status endpoint to return SIP registration + cookie validity |
| `src/RotaryPhoneController.GVBridge/Adapters/GVApiAdapter.cs` | Add `IsSipRegistered` and `AreCookiesValid` properties |

## Testing Checklist for UAT

- [ ] PhonePage loads without errors (no SignalR connection failures)
- [ ] Mode selector shows "Bluetooth HFP", "SIP Trunk", "GV API (SIP)"
- [ ] Switching to "GV API (SIP)" mode works
- [ ] SIP registration status displays correctly
- [ ] Incoming call announcement still works (ring sound + TTS + ducking)
- [ ] Call state updates in real-time on PhonePage Dashboard
- [ ] Contacts tab loads (PBAP + manual contacts)
- [ ] Call history tab shows recent calls
- [ ] System status card shows correct device states

## Cookie Setup for UAT

Before testing:
1. On the `radio` Ubuntu box, start Chrome/Chromium:
   ```bash
   chromium-browser --remote-debugging-port=9222 --user-data-dir=/tmp/gv-chrome &
   ```
2. Navigate to `voice.google.com` and log in with `mmackelprang@gmail.com`
3. Run cookie extraction:
   ```bash
   cd /opt/rotary-phone
   dotnet run --framework net10.0 -- gv-login
   ```
4. Start the RotaryPhone server normally

## Architecture Reference

```
┌─────────────────────────────────────────────┐
│ Radio.Web (Blazor UI, port 5002)            │
│ ├─ PhonePage.razor (mode selector, status)  │
│ ├─ GvBridgeApiService (REST to port 5004)   │
│ └─ PhoneHubService (SignalR to port 5004)   │
└──────────────┬──────────────────────────────┘
               │ REST + SignalR
┌──────────────▼──────────────────────────────┐
│ RotaryPhone.API (port 5004)                 │
│ ├─ CallManager (state machine)              │
│ ├─ GVApiAdapter (SIP-over-WSS)              │
│ │  ├─ GvSipTransport (INVITE/BYE/Opus)     │
│ │  └─ GVAudioBridgeService (48k↔8k RTP)    │
│ ├─ SIPSorceryAdapter (HT801 SIP)            │
│ └─ Cookie auth (SAPISIDHASH + rotation)     │
└──────────────┬──────────────────────────────┘
               │ SIP/RTP
         ┌─────▼─────┐     SIP-over-WSS + DTLS-SRTP
         │   HT801   │     ┌─────────────────────────┐
         │   ATA     │     │ Google Voice SIP         │
         └─────┬─────┘     │ (web.voice.telephony.goog)│
               │            └─────────────────────────┘
         ┌─────▼─────┐
         │ Rotary    │
         │ Phone     │
         └───────────┘
```
