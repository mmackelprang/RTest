# RotaryPhone API State — Investigation Report

**Date:** 2026-05-24
**Repo investigated:** `D:\prj\RotaryPhone` (branch `main`, HEAD `ba2b7ae`)
**Cross-referenced against:** `D:\prj\RTest\RTest` (branch `fix/device-display-config-reload`, main-tracking files)
**Status:** Read-only investigation. No files modified.

---

## 1. Executive Summary

RotaryPhone's last commit on `main` is **2026-03-30** — the same day the original RTest-side SIP UI plan was written. The repo has been quiet for ~8 weeks. **No new endpoints, hubs, or DTOs have been added since the SIP migration**, so the March-30 plan's *backend assumptions* are still accurate; only the *execution* never happened on the RTest side.

The drift is one-sided: **RTest still talks to the old (extension-era) API surface, while the RotaryPhone API surface has already moved past it**. Concretely:

- RTest expects a `/hubs/gvbridge` SignalR hub. **That hub does not exist** — `GVBridge` only ships a REST controller now. RTest's `GvBridgeHubService` connects to a dead URL on startup and falls into its retry loop forever.
- RTest's `GvBridgeStatusDto` expects `ExtensionConnected` / `ExtensionVersion`. **The actual response is `{ available, activeMode }`** — both client fields silently deserialize to `false`/`null`, so the Phone page shows "Chrome Extension Disconnected" permanently.
- The mode `GVBrowser` is still present in the `CallAdapterMode` enum but is **no longer registered as an adapter** (only `BluetoothHfp`, `SipTrunk`, `GVApi`). Clicking "GV Browser" in the RTest UI now hits `Conflict` from `CallAdapterRegistry.SwitchModeAsync`.
- One unfinished item on the RotaryPhone side blocks the cleanest fix: the SIP registration / cookie validity state is held privately inside `GVApiAdapter`; there's no `IsSipRegistered` / `AreCookiesValid` property to expose. The March-30 plan called this out as a precondition on the RotaryPhone side, and it was never done.

Recommendation: execute the March-30 RTest plan as-is for client-side changes (Tasks 1-4, 6, 8), drop Task 5 from the RTest builder's responsibility (it's a RotaryPhone change), and add a small handful of new tasks discovered during this investigation (mode-enum surface, GVTrunk hub still works, cookie management is CLI-only).

---

## 2. RotaryPhone Architecture Overview

### Current purpose

RotaryPhone bridges a vintage rotary phone (via Grandstream HT801 ATA / SIP) to one of three "remote call" paths:

1. **Bluetooth HFP** to a paired mobile (`BluetoothCallAdapter` → BlueZ HFP on `hci1`)
2. **SIP Trunk** to VoIP.ms (`SipTrunkCallAdapter` wrapping `GVTrunkAdapter` / SIPSorcery)
3. **Google Voice direct** (`GVApiAdapter` → SIP-over-WebSocket + DTLS-SRTP to `wss://web.voice.telephony.goog`)

Only **one path is active at a time**, swapped via `ICallAdapterRegistry.SwitchModeAsync()`. The local HT801 side (`ISipAdapter` / `SIPSorceryAdapter`) is always running regardless of mode — it's the "phone interface", separate from the "remote call" abstraction.

### Service layout

Single ASP.NET process: `RotaryPhoneController.Server` on **port 5004**.

| Project | Role |
|---|---|
| `RotaryPhoneController.Core` | Domain (CallManager, CallState, ISipAdapter, ICallAdapter, BluetoothCallAdapter, SipTrunkCallAdapter, PhoneManagerService) |
| `RotaryPhoneController.GVTrunk` | VoIP.ms SIP trunk (`GVTrunkAdapter`, `CallLogService`, `GmailSmsService`, REST `/api/gvtrunk/*`, hub `/hubs/gvtrunk`) |
| `RotaryPhoneController.GVBridge` | Google Voice direct path (`GVApiAdapter`, `GvSipTransport`, `GVAudioBridgeService`, REST `/api/gvbridge/*`). **No hub.** |
| `RotaryPhoneController.Server` | Hosts everything, has its own controllers (`/api/phone`, `/api/contacts`, `/api/callhistory`, `/api/bluetooth`, `/api/diagnostics`) + the `/hub` SignalR hub for call state |
| `RotaryPhoneController.Client` | React/TypeScript SPA, served from `wwwroot` |

### Deployment model

Per `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`, the service runs on the same Ubuntu box (`radio`) as Radio.API. It owns the Intel AX201 BT adapter (hci1) for voice/HFP; Radio.API owns the TP-Link UB500 (hci0) for music. WirePlumber is configured by Radio.API to ignore hci1 so RotaryPhone has full control of profile registration there. Systemd unit: `rotary-phone.service`. Boundary contract unchanged since 2026-03-21.

### What happened since the boundary doc was last updated

Boundary doc Change Log stops at 2026-03-21 (GV Bridge feature complete). After that, RotaryPhone shipped:

1. **2026-03-22 to 2026-03-24** — diagnostics page, audio bridge (Chrome extension–based), call detection pipeline
2. **2026-03-28** — first cut at direct GV HTTP API (Phase 1, replacing CDP)
3. **2026-03-30** — **The big one**: PR `feature/sip-wss-audio` (commit `4e24f88`) rips out the Chrome extension entirely and replaces it with SIP-over-WebSocket + DTLS-SRTP. This is what the RTest March-30 plan was reacting to.
4. **2026-03-30** — two follow-up fixes (`3d5061a`, `ba2b7ae`) for SIP-on-startup registration

**Nothing has shipped on `main` since 2026-03-30.** No new branches advanced past that date either.

---

## 3. API Surface Inventory

All controllers live under base URL `http://radio:5004`. All routes verified against current `main` source.

### `/api/phone` — `Server/Controllers/PhoneController.cs`

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/phone/status?phoneId={id?}` | — | `{ CallState, DialedNumber, IncomingNumber }` (anonymous object). No `CallerName`, no `Duration`. |
| POST | `/api/phone/simulate/incoming?phoneId={id=default}` | — | `string "Incoming call simulated"` |
| POST | `/api/phone/simulate/hook?phoneId={id=default}&offHook={bool=true}` | — | `string` |
| POST | `/api/phone/simulate/dial?phoneId={id=default}&digits={s}` | — | `string` |
| GET | `/api/phone/system-status` | — | `SystemStatus` class (see below) |
| GET | `/api/phone/ht801/validate?phoneId={id?}&autoFix={bool=false}` | — | `HT801ValidationResult` |

**`SystemStatus` shape** (from `Core/SystemStatus.cs`):

```csharp
public class SystemStatus {
  public string Platform;             // "Windows" | "Linux" | "Unknown"
  public bool IsRaspberryPi;
  public bool BluetoothEnabled;       // config flag, not connection
  public bool BluetoothConnected;
  public string? BluetoothDeviceAddress;
  public bool SipListening;
  public string? SipListenAddress;
  public int SipPort;
  public string? Ht801IpAddress;
  public bool? Ht801Reachable;
}
```

**Call-state shape** is *not* a named DTO — it's an anonymous object built inline in `PhoneController.GetStatus()`. The fields are PascalCase (`CallState`, `DialedNumber`, `IncomingNumber`) and there is no `CallerName` or `Duration` on the wire.

### `/api/contacts` — `Server/Controllers/ContactsController.cs`

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/contacts` | — | `IEnumerable<Contact>` |
| GET | `/api/contacts/{id}` | — | `Contact` or `404` |
| POST | `/api/contacts` | `Contact` (Id may be empty; server fills with GUID) | `201 Created` |
| PUT | `/api/contacts/{id}` | `Contact` | `204 NoContent` |
| DELETE | `/api/contacts/{id}` | — | `204` |
| GET | `/api/contacts/search?query={q}` | — | `IEnumerable<Contact>` |

### `/api/callhistory` — `Server/Controllers/CallHistoryController.cs`

| Method | Route | Response |
|---|---|---|
| GET | `/api/callhistory?phoneId={id?}` | `IEnumerable<CallHistoryEntry>` sorted by StartTime desc |
| DELETE | `/api/callhistory?phoneId={id?}` | `204` (ignores phoneId — always clears all) |

### `/api/bluetooth` — `Server/Controllers/BluetoothController.cs`

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/bluetooth/devices` | — | `{ paired, connected, adapterReady, adapterAddress }` |
| POST | `/api/bluetooth/discovery/start` | — | `200` |
| POST | `/api/bluetooth/discovery/stop` | — | `200` |
| POST | `/api/bluetooth/pair` | `{ Address }` | `200`/`400` |
| DELETE | `/api/bluetooth/devices/{address}` | — | `200`/`400` |
| POST | `/api/bluetooth/pairing/confirm` | `{ Address, Accept }` | `200`/`400` |
| PUT | `/api/bluetooth/adapter` | `{ Alias?, Discoverable? }` | `200`/`400` |
| POST | `/api/bluetooth/devices/{address}/connect` | — | `200`/`400` |
| POST | `/api/bluetooth/devices/{address}/disconnect` | — | `200`/`400` |

### `/api/diagnostics` — `Server/Controllers/DiagnosticsController.cs` (added 2026-03-22)

| Method | Route | Response |
|---|---|---|
| GET | `/api/diagnostics/status` | `{ Sip, Ht801, GVBridge: { IsAvailable }, GVAudioBridge: { IsActive, Stats }, RecentSipMessages, RecentTimeline }` |
| GET | `/api/diagnostics/sip-log?count=50&method=?` | `List<SipMessageRecord>` |
| GET | `/api/diagnostics/timeline?count=50` | `List<CallTimelineEntry>` |
| POST | `/api/diagnostics/test-ring?phoneId=?` | `{ Message, PhoneId }` |
| POST | `/api/diagnostics/test-audio` | `{ Message: "not yet implemented", Status: "placeholder" }` |
| GET | `/api/diagnostics/ht801/config?phoneId=?` | `List<ConfigParameter>` |
| POST | `/api/diagnostics/ht801/validate?phoneId=?&autoFix=false` | `HT801ValidationResult` |

**This entire controller is news to the RTest UI** — there's no client for it. Diagnostics page is RotaryPhone-side only (React, served at `/diagnostics`).

### `/api/gvtrunk` — `GVTrunk/Api/GVTrunkController.cs`

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/gvtrunk/status` | — | `{ isRegistered: bool, callState: string, activeCallDurationSeconds: int }` |
| GET | `/api/gvtrunk/calls` | — | `List<CallLogEntry>` (last 50) |
| GET | `/api/gvtrunk/sms` | — | `List<SmsNotification>` (last 20, in-memory) |
| POST | `/api/gvtrunk/dial` | `{ Number }` | `{ sessionId }` or `409` |
| POST | `/api/gvtrunk/reregister` | — | `{ status }` |

**`CallLogEntry` actual shape** (positional record, serialized PascalCase):

```csharp
public record CallLogEntry(
  int Id, DateTime StartedAt, DateTime? EndedAt, string Direction,
  string RemoteNumber, string Status, int? DurationSeconds, string? Notes = null);
```

**`SmsNotification`:**

```csharp
public record SmsNotification(
  string FromNumber, string? Body, DateTime ReceivedAt, SmsType Type);
public enum SmsType { Sms, MissedCall }  // serialized as int by default!
```

### `/api/gvbridge` — `GVBridge/Api/GVBridgeController.cs` (CURRENT — post-SIP migration)

| Method | Route | Body | Response (anonymous objects) |
|---|---|---|---|
| GET | `/api/gvbridge/status` | — | `{ available: bool, activeMode: string }` |
| GET | `/api/gvbridge/adapter/mode` | — | `{ activeMode: string, modes: [{mode: string}] }` |
| PUT | `/api/gvbridge/adapter/mode` | `{ Mode }` | `{ activeMode }` or `400`/`409` |

**That's it.** No `/api/gvbridge/sms`, no `/api/gvbridge/sms/send`, no cookie endpoints, no SIP-status endpoint. The status response is just `available` (from `GVApiAdapter.IsAvailable`) and the registry's current mode.

### Endpoints RTest's client code calls that **do not exist**

| Client method | Endpoint | Reality |
|---|---|---|
| `GvBridgeApiService.GetRecentSmsAsync` | `GET /api/gvbridge/sms` | 404 |
| `GvBridgeApiService.SendSmsAsync` | `POST /api/gvbridge/sms/send` | 404 |

The SMS endpoints live under `/api/gvtrunk/sms` instead — they're a GVTrunk feature (Gmail polling), not a GVBridge one. The RTest `GvBridgeApiService` calls under `/api/gvbridge/sms*` have always been dead links (they were never implemented on the GVBridge side, even in the extension era).

---

## 4. SignalR Hubs

### `/hub` — `Server/Hubs/RotaryHub.cs` (UNCHANGED, still works)

Server → client events:

| Event | Payload | Source |
|---|---|---|
| `CallStateChanged` | `(string phoneId, CallState state)` | Broadcast by `SignalRNotifierService` on CallManager state changes |
| `IncomingCall` | `(string phoneId, string phoneNumber)` | Broadcast on incoming call |
| `CallHistoryUpdated` | `CallHistoryEntry` | Broadcast on history change |
| `SystemStatusChanged` | `SystemStatus` | Periodic broadcast |
| `CallerResolved` | `(string phoneNumber, string displayName)` | Broadcast after Radio.API resolves caller from PBAP |

Client → server (called by Radio.API):

| Method | Args | Purpose |
|---|---|---|
| `ReportCallerResolved` | `phoneNumber, displayName` | Radio.API reports back PBAP-resolved name; RotaryPhone updates CallManager + re-broadcasts |

**RTest's `PhoneHubService` connects to this hub and matches the contract. No changes needed.**

### `/hubs/gvtrunk` — `GVTrunk/Api/GVTrunkHub.cs` (UNCHANGED)

The hub class itself is empty. All push happens from `GVTrunkEventBridge` (a hosted service) via `IHubContext<GVTrunkHub>`:

| Event | Payload | Source |
|---|---|---|
| `RegistrationChanged` | `{ isRegistered: bool }` | `ITrunkAdapter.OnRegistrationChanged` |
| `SmsReceived` | `SmsNotification` | `ISmsProvider.OnSmsReceived` |
| `MissedCallReceived` | `SmsNotification` | `ISmsProvider.OnMissedCallReceived` |
| `CallStateChanged` | `{ phoneId: string, callState: string }` | per-phone `CallManager.StateChanged` |

**RTest's `GvTrunkHubService` matches this contract. No changes needed.**

### `/hubs/gvbridge` — **DOES NOT EXIST**

`GVBridgeServiceExtensions.MapGVBridge()` only registers `endpoints.MapControllers()`. There is no `MapHub<>()` call. RTest's `GvBridgeHubService` connects to a 404 and falls into its retry loop. The plan's assumption (delete the hub service) is verified correct.

### Confirmed: total hub list

`Program.cs` lines 365-367:
- `app.MapHub<RotaryHub>("/hub")`
- `app.MapGVTrunk()` → maps `/hubs/gvtrunk`
- `app.MapGVBridge()` → no hub, controllers only

---

## 5. Cookie / Auth Management

**No HTTP API. Cookies are managed entirely out-of-band.**

The flow:

1. User runs the CLI: `dotnet RotaryPhoneController.Server -- gv-login` (handled in `Program.cs:21-44`, branches before `WebApplication.CreateBuilder`)
2. CLI uses `CookieRetriever` (`GVBridge/Auth/CookieRetriever.cs`) to talk to Chrome's CDP debugger port, extract cookies, AES-256 encrypt them with a random key, and persist to:
   - `data/gv-cookies.enc` (configurable: `GVBridge.CookieFilePath`)
   - `data/gv-key.bin` (configurable: `GVBridge.CookieKeyFilePath`)
3. On normal startup, `GVApiAdapter.ActivateAsync` reads both files, decrypts the cookies, and uses them to authenticate `HttpClient` requests (`GvHttpClientHandler` injects SAPISIDHASH + cookies on every call).
4. `GvAccountClient.IsHealthyAsync()` periodically pings GV; if cookies are stale, `IsAvailable` flips to `false`.

**Implication for RTest UI:**

The March-30 plan suggested adding a "cookie management" section to `SystemConfigPage`. That would need a NEW endpoint on the RotaryPhone side (e.g., `POST /api/gvbridge/cookies/refresh` that shells out to `gv-login`, or an upload-cookies endpoint). **None exists today.** This is a Phase-2 nice-to-have, not a blocker for the SIP UI refresh.

The currently shipped surrogate: the user reads "GV API Available: false" on the dashboard and knows they need to SSH to the box and run `gv-login`. That's the entire UX story today.

---

## 6. Call Lifecycle & State Model

**Polling cadence:** 5s is still appropriate. The RotaryPhone API has no rate limits; both `/api/phone/status` and `/api/gvbridge/status` are cheap (in-memory reads). RTest's current `PhonePage` already polls every 5s and additionally every 30s for GV status — both are fine.

### Incoming call (current architecture, all paths)

1. Remote event fires:
   - BT path: `IBluetoothDeviceManager.OnIncomingCall(device, number)` (from `bt_manager.py` subprocess + CLIP)
   - SipTrunk path: `GVTrunkAdapter.OnIncomingCall(number)` (SIP INVITE from VoIP.ms)
   - GVApi path: `GvSipTransport.IncomingCallReceived` → `GVApiAdapter.OnIncomingCall(number)`
2. Active adapter raises `ICallAdapter.OnIncomingCall(string number)`
3. `CallManager` transitions `Idle → Ringing`, fires `StateChanged`
4. `SignalRNotifierService` broadcasts `IncomingCall(phoneId, number)` + `CallStateChanged(phoneId, "Ringing")` over `/hub`
5. `CallManager` also sends SIP INVITE to HT801 via `ISipAdapter.SendInviteToHT801` so the rotary bell rings
6. User picks up handset → HT801 sends SIP 200 OK → `CallManager.HandleHookChange(offHook=true)` → state `Ringing → InCall`
7. `CallManager` calls `ICallAdapter.OnCallAnsweredOnRotaryPhoneAsync()` so the active adapter starts its audio bridge (e.g., GVAudioBridge for GVApi mode)

### Outgoing call

Rotary dial path only (no UI dial today — the RTest dial buttons are simulate-only via `/api/phone/simulate/dial`):

1. HT801 decodes pulses → SIP NOTIFY/INFO with digits → `SIPSorceryAdapter` → `CallManager.HandleDigitsReceived`
2. `CallManager` transitions `Idle → Dialing`, calls `ICallAdapter.PlaceCallAsync(e164Number)`
3. Adapter places the call on its medium (HFP dial / SIP INVITE / GV SIP INVITE)
4. On answer event, state `Dialing → InCall`

### Hangup

Either side hangs up → adapter raises `OnCallEnded` → `CallManager` → `InCall → Idle`, broadcast `CallStateChanged(phoneId, "Idle")`.

### `CallState` enum values (serialized as strings)

`"Idle"`, `"Ringing"`, `"Dialing"`, `"InCall"` — verified by `CallState.ToString()` in `PhoneController.GetStatus()` and `GVTrunkController.GetStatus()`.

---

## 7. Drift Analysis — Per-File

For each RTest file, "RTest expects" is what's in the code today; "RotaryPhone actual" is what the server returns. Files marked **NEEDS UPDATE** are confirmed broken.

| RTest file | RTest expects | RotaryPhone actual | Status |
|---|---|---|---|
| `src/Radio.Web/Services/ApiClients/PhoneApiService.cs` | `/api/phone/system-status` returns `PhoneSystemStatusDto` shape | Matches exactly (`SystemStatus` class fields are identical PascalCase). | OK |
| same | `/api/phone/status` returns `PhoneCallStateDto` w/ `CallState, DialedNumber, IncomingNumber, CallerName, Duration` | Server returns only `CallState, DialedNumber, IncomingNumber`. `CallerName` and `Duration` are always null in deserialization. | **Cosmetic mismatch** — PhonePage code already handles nulls. Either trim DTO or add fields server-side. Low priority. |
| `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` | `GET /api/gvbridge/status → GvBridgeStatusDto { ExtensionConnected, ExtensionVersion, ActiveMode }` | Returns `{ available, activeMode }`. `ExtensionConnected` always false, `ExtensionVersion` always null. | **NEEDS UPDATE** |
| same | `GET /api/gvbridge/sms` (recent SMS) | 404 — endpoint doesn't exist on GVBridge. SMS lives at `/api/gvtrunk/sms`. | **NEEDS UPDATE** — delete `GetRecentSmsAsync` (or remove and redirect callers to `GvTrunkApiService.GetRecentSmsAsync`). |
| same | `POST /api/gvbridge/sms/send` | 404 — never existed. | **NEEDS UPDATE** — delete `SendSmsAsync`. (Was a stub for a feature that wasn't built.) |
| same | `PUT /api/gvbridge/adapter/mode { mode: "GVBrowser" }` | Mode enum still has `GVBrowser` but **no adapter is registered for it** since `feature/sip-wss-audio`. PUT will return 409 "No adapter registered for mode: GVBrowser". | **NEEDS UPDATE** — rename mode string to `GVApi`. |
| `src/Radio.Web/Services/ApiClients/GvTrunkApiService.cs` | `GET /api/gvtrunk/status → { isRegistered, callState, activeCallDurationSeconds }` | Matches. | OK |
| same | `GET /api/gvtrunk/calls → List<GvTrunkCallLogEntryDto>` | Matches (CallLogEntry has all fields RTest expects, plus optional `Notes`). | OK |
| same | `GET /api/gvtrunk/sms → List<GvSmsNotificationDto>` | Matches structurally. **Caveat:** `SmsType` is serialized as enum int (0/1) by default unless RotaryPhone's `Program.cs` sets a JSON converter — RTest deserializes it as a string `"Sms"/"MissedCall"`. Likely working because `System.Text.Json` default for enums is numeric; deserialization to a string-typed field may silently fail. Worth verifying. | **POTENTIAL BUG — verify** |
| same | `POST /api/gvtrunk/dial { number }` | Matches. | OK |
| same | `POST /api/gvtrunk/reregister` | Matches. | OK |
| `src/Radio.Web/Services/Hub/PhoneHubService.cs` | `/hub` with 4 events (`CallStateChanged`, `IncomingCall`, `CallHistoryUpdated`, `SystemStatusChanged`) | All present in `RotaryHub`. Plus `CallerResolved` exists but RTest doesn't subscribe (it's for Radio.API → server direction). | OK |
| `src/Radio.Web/Services/Hub/GvBridgeHubService.cs` | `/hubs/gvbridge` with `ExtensionConnectionChanged`, `ModeChanged` events | **Hub does not exist.** Connection fails forever (silent retry loop). | **NEEDS DELETE** |
| `src/Radio.Web/Services/Hub/GvTrunkHubService.cs` | `/hubs/gvtrunk` with `RegistrationChanged`, `SmsReceived`, `MissedCallReceived`, `CallStateChanged` | All match. | OK |
| `src/Radio.Web/Models/ApiModels.cs::GvBridgeStatusDto` | `ExtensionConnected, ExtensionVersion, ActiveMode` | `available, activeMode` | **NEEDS UPDATE** |
| `src/Radio.Web/Models/ApiModels.cs::GvAdapterModeDto / GvModeEntryDto` | `ActiveMode, Modes[].Mode` | Matches. | OK |
| `src/Radio.Web/Models/ApiModels.cs::GvSmsNotificationDto` | `FromNumber, Body, ReceivedAt, Type (string)` | `Type` deserialization caveat above. | Mostly OK |
| `src/Radio.Web/Models/ApiModels.cs::GvTrunkStatusDto` / `GvTrunkCallLogEntryDto` | Matches server. | | OK |
| `src/Radio.Web/Models/ApiModels.cs::PhoneCallStateDto` | Has extra `CallerName` and `Duration` server doesn't return. Tolerable. | | Trim to match (optional) |
| `src/Radio.Web/Components/Pages/PhonePage.razor` | "GV Browser" button → `SwitchModeAsync("GVBrowser")`. Reads `bridgeStatus.ExtensionConnected` and `ExtensionVersion`. Subscribes to `GvBridgeHub.ExtensionConnectionChanged` + `ModeChanged`. | All three of those linkages are broken. The "Chrome Extension" status card is always red. Switching to "GV Browser" fails silently. | **NEEDS UPDATE** (multiple sites within file) |
| `src/Radio.Web/Components/Pages/SystemConfigPage.razor` | Phone integration sub-tab uses hub URLs `RotaryPhone:HubUrl` (defaults `http://radio:5004/hub`) and contacts API. No GVBridge/GVTrunk-specific UI today. | Hub URL and contacts endpoints work fine. No cookie management UI exists. | OK as-is. (Cookie UI would be a NEW feature, not a drift fix.) |

---

## 8. Refreshed SIP UI Execution Plan

Supersedes `docs/plans/2026-03-30-rotaryphone-sip-ui-update.md`. Tasks renumbered, with explicit status flags:

- **(carry-over)** = the March-30 plan had this; still needed
- **(new)** = discovered during this investigation
- **(skip / not us)** = RotaryPhone-side change, out of RTest's scope
- **(done)** = already fine

### Phase A — DTO + client shim (no UI changes yet)

**Task 1 (carry-over)** — Update `src/Radio.Web/Models/ApiModels.cs:1053-1058`

Replace `GvBridgeStatusDto` to match server reality:

```csharp
public class GvBridgeStatusDto {
  public bool Available { get; set; }
  public string ActiveMode { get; set; } = "";
}
```

Drop `ExtensionConnected` and `ExtensionVersion` entirely. **Do NOT** add `SipRegistered` / `CookiesValid` yet — those are not exposed by the server today (see Task 8 below).

**Task 2 (carry-over)** — Delete `src/Radio.Web/Services/Hub/GvBridgeHubService.cs`

Whole file. Then in `src/Radio.Web/Extensions/ServiceCollectionExtensions.cs` (or wherever it's registered — verify), remove its DI registration. Then in `PhonePage.razor`:

- Drop the `@inject GvBridgeHubService GvBridgeHub` line (around line 6)
- Drop `GvBridgeHub.ExtensionConnectionChanged += OnExtensionConnectionChanged` subscription (line 455)
- Drop `GvBridgeHub.ModeChanged += OnModeChanged` subscription (line 456)
- Drop `OnExtensionConnectionChanged`, `OnModeChanged` handler methods (lines 571-583)
- Drop the unsubscribes in `Dispose()` (lines 821-822)
- Remove `_gvExtensionVersion` field (line 406)

**Task 3 (carry-over, narrowed)** — Update `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`

- `IsAvailableAsync` / `GetStatusAsync`: no signature change, but the DTO now matches reality
- **DELETE `GetRecentSmsAsync`** (lines 85-97) — endpoint never existed
- **DELETE `SendSmsAsync`** (lines 99-113) — endpoint never existed
- Leave `GetAdapterModeAsync`, `SetAdapterModeAsync` alone

**Task 4 (new)** — Reconcile `PhonePage.razor` GV-mode refs

Three sites in `PhonePage.razor` reference the string `"GVBrowser"`:

- Line 87: button text `"GV Browser"` → `"GV API"`
- Line 89: `ButtonStyle="@(_gvActiveMode == "GVBrowser" ? ButtonStyle.Primary : ButtonStyle.Light)"` → `"GVApi"`
- Line 91: `Click="@(() => SwitchModeAsync("GVBrowser"))"` → `"GVApi"`

Also lines 430-432 in `OnInitializedAsync` and 485-487 in `RefreshGvStatusAsync`:

- `_gvBridgeConnected = bridgeStatus.ExtensionConnected;` → DELETE
- `_gvExtensionVersion = bridgeStatus.ExtensionVersion;` → DELETE
- Keep `_gvActiveMode = bridgeStatus.ActiveMode;`

Add a derived `_gvBridgeAvailable` field bound to `bridgeStatus.Available`. Replace the "CHROME EXTENSION" status column (lines 97-106) with a single "GV API" badge:

```razor
<div class="status-column">
  <span class="status-column-label">GV API</span>
  <RadzenBadge
    BadgeStyle="@(_gvBridgeAvailable ? BadgeStyle.Success : BadgeStyle.Warning)"
    Text="@(_gvBridgeAvailable ? "Available" : "Unavailable")" />
</div>
```

(Defer the SipRegistered / CookiesValid split badges until Task 8 lands on the RotaryPhone side.)

### Phase B — Polish (still RTest-only)

**Task 5 (new)** — Trim `PhoneCallStateDto` in `ApiModels.cs:983-990`

Drop `CallerName` and `Duration` — server never sends them. PhonePage uses them with null guards (lines 141, 152), so simply remove them from the DTO and remove the dead UI branches.

**Task 6 (new)** — Verify `SmsType` deserialization between RotaryPhone and RTest

Two safer options:

1. Pin the field type in RTest `GvSmsNotificationDto` to a numeric enum: `public SmsType Type { get; set; }` with the same enum order RotaryPhone uses (`Sms=0, MissedCall=1`)
2. Add `[JsonConverter(typeof(JsonStringEnumConverter))]` on `SmsType` on the RotaryPhone side (out-of-scope for RTest builder, see Task 9)

For RTest-only fix: read the type as `int Type { get; set; }` and convert in code. Safer than a sliently-failing string.

### Phase C — Adapter clean-up + tests

**Task 7 (new)** — Verify `Radio.API` does NOT subscribe to `/hubs/gvbridge` anywhere

Grep `Radio.API` for `gvbridge` SignalR usage. The boundary doc says Radio.API listens to `/hub` (CallManager hub) for incoming-call resolution. Make sure no stray `HubConnectionBuilder().WithUrl(".../hubs/gvbridge")` exists in `PhoneCallIntegrationService.cs` or its neighbours. (My read says it's clean, but verify.)

**Task 8 (skip / not us)** — RotaryPhone server changes (cited in March-30 plan Task 5)

This requires changes in `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge`:

- `GVApiAdapter`: expose `IsSipRegistered` (delegate to `_sipTransport?.IsRegistered`) and `AreCookiesValid` (cache last `IsHealthyAsync` result)
- `GVBridgeController.GetStatus`: extend response to `{ available, activeMode, sipRegistered, cookiesValid }`

Once that ships, RTest can add the two extra fields to `GvBridgeStatusDto` and render two badges instead of one. **Track as a follow-up prompt to the RotaryPhone session via the boundary doc Change Log.**

### Phase D — Test + ship

**Task 9 (carry-over)** — Build, run tests, manual UAT against running services

```powershell
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
```

UAT checklist:

- [ ] PhonePage loads with no SignalR connection failures in browser console
- [ ] Mode buttons: "Bluetooth", "SIP Trunk", "GV API" (no "GV Browser")
- [ ] Clicking "GV API" calls `PUT /api/gvbridge/adapter/mode {Mode:"GVApi"}` and returns 200
- [ ] Status card shows GV API as Available/Unavailable, not "Chrome Extension Disconnected"
- [ ] SIP Trunk register / reregister still works (`GvTrunk` path is untouched)
- [ ] Incoming call still rings rotary + announces via TTS + ducks music (cross-service integration)
- [ ] Call history tab still populates from `/api/callhistory`
- [ ] PBAP contacts tab still works (Radio.API-side)

**Task 10 (carry-over)** — Commit on a feature branch, PR to main

Already on `fix/device-display-config-reload` — DON'T commit this work there. Spin a new branch `feat/rotaryphone-sip-ui-refresh` (per user's global "always branch" rule).

### Task dependency graph

```
Task 1 (DTO) ─┐
              ├─ Task 4 (PhonePage GVApi rename)
Task 2 (Hub delete) ─┘
                          ↓
Task 3 (Service trim) ─ Task 5 (CallStateDto trim) ─ Task 6 (SmsType fix) ─ Task 7 (Radio.API audit)
                          ↓
Task 8 (RotaryPhone-side, blocks the "two-badge" UI but not the cleanup)
                          ↓
Task 9 + 10 (Build, test, PR)
```

Tasks 1-7 can ship in a single PR without Task 8. The PhonePage just shows a single "Available/Unavailable" badge until the server exposes more.

---

## 9. Open Questions

Need user input before Builder executes:

1. **Cookie management UI in `SystemConfigPage`** — March-30 plan flagged this as "nice-to-have". Today there's no API for it. Should the Builder skip this entirely, or should we add a prompt to the RotaryPhone session asking them to expose a `POST /api/gvbridge/cookies/refresh` shim around `gv-login`?

2. **GVBrowser enum value** — `CallAdapterMode.GVBrowser` is still in the RotaryPhone enum but unused. Leave it (it's a domain enum, not RTest's call)? Or include in a follow-up RotaryPhone prompt to delete it for cleanliness?

3. **Diagnostics page** — RotaryPhone has a full `/api/diagnostics/*` controller and React-side diagnostics page. Should RTest's UI consume any of this (e.g., link to it from PhonePage; or proxy `/api/diagnostics/status` into the System Status card)? Currently RTest has no awareness.

4. **`GVAudioBridgeService.Stats`** — Diagnostics returns audio-bridge stats. Useful for the dashboard? Or out-of-scope?

5. **SipRegistered / CookiesValid badges** — If the answer to Task 8 is "we'll prompt RotaryPhone", do we ship Phase A-C now and follow up with the badge split later, or wait for the full picture?

6. **HT801 reachability** — Server already returns `Ht801Reachable` in `system-status`. The RTest dashboard renders it correctly. No action needed, but worth flagging: this is the only "third leg" RTest currently shows. Should it become a top-level status indicator?

---

## 10. Out of Scope (RotaryPhone-side observations, NOT part of UI refresh)

For situational awareness only — these are RotaryPhone-side things noticed during the read that may be relevant later but should not block the UI refresh:

- **`TODO-remaining-work.md` (RotaryPhone)** flags two open items:
  - HT801 may need a factory reset if SIP INVITEs stop arriving (operational, not a code fix)
  - SCO audio bridge for HFP voice (`scripts/bt_manager.py` ctypes bind workaround + `ScoRtpBridge.cs` RTP framing) — scaffolded, not tested end-to-end. Doesn't affect GV/SIP paths.
- **Setup docs are stale** (`SETUP-GVBridge.md` last updated 2026-03-23, references the deleted Chrome extension architecture). This is a RotaryPhone-side doc-hygiene issue. If the user wants, we can add a prompt to the RotaryPhone session to refresh these.
- **`PRD-GVBrowserBridge.md` and React `/components/gvbridge/` and `useGVBridge.ts`** still exist on the RotaryPhone side. Mostly noise — the React UI probably has the same drift the Blazor UI does.
- **Boundary doc Change Log has not been updated since 2026-03-21** despite the major SIP rewrite. Worth nudging via the doc, but not blocking.
- **`CallAdapterMode.GVBrowser`** enum value lingers without a registered adapter. Cosmetic.
- **`GVApiAdapter` registers as `ICallAdapter` but not via the typed `GVApiAdapter` singleton chain like `BluetoothCallAdapter` does** — slight DI inconsistency. Works fine, not a bug.
- **`Server/Program.cs:73`** registers `AddSignalR()` unconditionally; `GVBridgeServiceExtensions` does not register a hub but also doesn't call AddSignalR. Fine for now — the global registration covers it. If GVBridge ever adds a hub back, watch the wiring.
- **Dual logging path**: `Serilog.ILogger` is registered alongside `ILogger<T>`. GVBridge/GVTrunk services inject Serilog directly. Fine, just inconsistent with Core which uses `ILogger<T>`. Not a UI concern.

---

## Appendix — Key file paths

### RotaryPhone (source of truth)

- `D:\prj\RotaryPhone\src\RotaryPhoneController.Server\Program.cs` — host wiring, hub mapping
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Server\Controllers\PhoneController.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Server\Controllers\DiagnosticsController.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Server\Hubs\RotaryHub.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Api\GVBridgeController.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Extensions\GVBridgeServiceExtensions.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Adapters\GVApiAdapter.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Models\GVBridgeConfig.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVTrunk\Api\GVTrunkController.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVTrunk\Api\GVTrunkHub.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVTrunk\Extensions\GVTrunkServiceExtensions.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVTrunk\Models\CallLogEntry.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVTrunk\Models\SmsNotification.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Core\SystemStatus.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Core\CallAdapterMode.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Core\ICallAdapter.cs`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Core\CallAdapterRegistry.cs`
- `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` (boundary contract)
- `D:\prj\RotaryPhone\docs\TODO-remaining-work.md`

### RTest (consumer; needs updates per Phase A-C)

- `D:\prj\RTest\RTest\src\Radio.Web\Services\ApiClients\PhoneApiService.cs` (mostly OK)
- `D:\prj\RTest\RTest\src\Radio.Web\Services\ApiClients\GvBridgeApiService.cs` (NEEDS UPDATE)
- `D:\prj\RTest\RTest\src\Radio.Web\Services\ApiClients\GvTrunkApiService.cs` (OK)
- `D:\prj\RTest\RTest\src\Radio.Web\Services\Hub\PhoneHubService.cs` (OK)
- `D:\prj\RTest\RTest\src\Radio.Web\Services\Hub\GvBridgeHubService.cs` (NEEDS DELETE)
- `D:\prj\RTest\RTest\src\Radio.Web\Services\Hub\GvTrunkHubService.cs` (OK)
- `D:\prj\RTest\RTest\src\Radio.Web\Models\ApiModels.cs` lines 969-1098 (NEEDS UPDATE for GvBridgeStatusDto, optional trim for PhoneCallStateDto)
- `D:\prj\RTest\RTest\src\Radio.Web\Components\Pages\PhonePage.razor` (NEEDS UPDATE — see Task 4)
- `D:\prj\RTest\RTest\src\Radio.Web\Components\Pages\SystemConfigPage.razor` (no changes needed today; potential Phase-2 cookie UI)
- `D:\prj\RTest\RTest\docs\plans\2026-03-30-rotaryphone-sip-ui-update.md` (SUPERSEDED by this report)
- `D:\prj\RTest\RTest\docs\rotaryphone-sip-integration-prompt.md` (still useful context; this report extends, doesn't replace)
