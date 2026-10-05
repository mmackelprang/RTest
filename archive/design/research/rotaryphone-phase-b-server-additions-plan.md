# RotaryPhone Phase B — Server-Side API Additions Plan

**Date:** 2026-05-24
**Repos involved:**
- **RotaryPhone** (`D:\prj\RotaryPhone`, branch `main`, HEAD `ba2b7ae`) — where ALL work in this plan lives
- **RTest** (`D:\prj\RTest\RTest`) — consumer; Phase A already shipping, Phase C will consume the additions described below

**Status:** Read-only investigation + planning document. No code changes made in either repo.

**Predecessor docs:**
- `D:\prj\RTest\RTest\design\research\rotaryphone-api-state-2026-05-24.md` — full API drift inventory (the "investigation report")
- `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` — cross-service boundary doc (will receive updates per Section 7)

---

## 1. Executive Summary

The investigation report flagged five RTest UI features that the user authorized to ship even if RotaryPhone-side API work is required (Q2-Q6). This document specifies what to add on the RotaryPhone side so RTest's Phase C UI work can consume them cleanly.

### What needs adding to RotaryPhone

| ID | Feature | Server change required | Effort |
|----|---------|------------------------|:------:|
| Q2 | Diagnostics controller consumable by RTest | **Mostly already exists** (`/api/diagnostics/*` shipped 2026-03-22). One small additive change to make `Ht801HealthStatus` deserialization-friendly. | **S** |
| Q3 | Audio-bridge stats in UI | **Already exposed** via `/api/diagnostics/status` (`GVAudioBridge.Stats`). Recommend adding a dedicated `GET /api/diagnostics/audio-bridge` endpoint for cheaper polling + cleaner DTO. | **S** |
| Q4 | HT801 reachability promotion to dashboard | **Already exposed** in two places (`/api/phone/system-status` and `/api/diagnostics/status`). No new endpoint needed — pure RTest UI work. Optionally add a richer `GET /api/diagnostics/ht801` snapshot for the dashboard card. | **XS** (no work) or **S** (with new endpoint) |
| Q5 | Two-badge UI (SipRegistered + CookiesValid) | **Required:** Surface `_sipTransport._registered` and `_accountClient.IsHealthyAsync` cached result on `GVApiAdapter`. Extend `GVBridgeController.GetStatus` response. | **S** |
| Q6 | Cookie management UI | **Required:** Add `GET /api/gvbridge/cookies` (read-only status), `POST /api/gvbridge/cookies` (paste-in JSON), and refresh trigger. CLI `gv-login` flow stays as-is for now. | **M** |

### Total effort

| Tier | Time estimate |
|------|---------------|
| Small (S) per task | 0.5-1 hour |
| Medium (M) | 2-4 hours |
| Whole Phase B | ~1 working day if shipped as 2 PRs |

### Recommended sequencing

1. **PR1 (small, ship first):** Q5 (SipRegistered/CookiesValid) + Q3 (dedicated audio-bridge endpoint) + Q2 (DTO fix). All three are tiny, all touch the diagnostics/gvbridge controllers + GVApiAdapter, and they unblock the most visible RTest UI improvements (the two-badge replacement). ~3 hours including tests.
2. **PR2 (medium, ship after PR1 lands):** Q6 cookie management. Touches new endpoints + auth code path. Wants its own review. ~4 hours.
3. **Q4 needs no server work.** RTest UI can consume `Ht801Reachable` immediately.

### Cross-repo coordination

RotaryPhone PRs are **additive** — they add fields, don't change existing ones. RTest Phase A is shipping `{ Available, ActiveMode }` and Phase C can then upgrade to `{ Available, ActiveMode, SipRegistered, CookiesValid }` once PR1 lands and is deployed to the Ubuntu box. **The two phases are decoupled in time.**

---

## 2. Q2 — Diagnostics Controller

### What already exists

`DiagnosticsController.cs` at `D:\prj\RotaryPhone\src\RotaryPhoneController.Server\Controllers\DiagnosticsController.cs` (added 2026-03-22, commit `3de9127`) provides:

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/diagnostics/status` | Full snapshot: `{ Sip, Ht801, GVBridge: { IsAvailable }, GVAudioBridge: { IsActive, Stats }, RecentSipMessages, RecentTimeline }` |
| GET | `/api/diagnostics/sip-log?count=50&method=?` | Recent SIP messages from ring buffer (max 200) |
| GET | `/api/diagnostics/timeline?count=50` | Call timeline events (INVITE_SENT, RINGING, CALL_ANSWERED, etc.) |
| POST | `/api/diagnostics/test-ring?phoneId=?` | Send test INVITE to HT801 |
| POST | `/api/diagnostics/test-audio` | Placeholder — returns `{ Message: "not yet implemented" }` |
| GET | `/api/diagnostics/ht801/config?phoneId=?` | Compare HT801 expected-vs-actual config |
| POST | `/api/diagnostics/ht801/validate?phoneId=?&autoFix=false` | Validate HT801 config with optional auto-fix |

All routes work today. None are currently consumed by RTest.

### What's missing for RTest consumption

The controller is functional, but the **`Ht801HealthStatus` record uses positional record syntax** (`record Ht801HealthStatus(bool IsReachable, double? PingMs, bool IsRegistered, …)`). With `System.Text.Json` defaults, positional records serialize the parameter names as JSON property names (PascalCase since they're parameters). That's fine for RTest's typed DTOs, **but** the inline anonymous `GVBridge = new { IsAvailable = ... }` and `GVAudioBridge = new { IsActive = ..., Stats = ... }` properties use camelCase property naming as anonymous types — actually, `System.Text.Json` default is PascalCase when serializing C# property names. The investigation report confirmed `{ Sip, Ht801, GVBridge, … }` as the actual on-wire shape.

**Verified shape (PascalCase keys):**

```json
{
  "Sip": { "IsListening": true, "ListenAddress": "0.0.0.0", "Port": 5060 },
  "Ht801": {
    "IsReachable": true, "PingMs": null, "IsRegistered": true,
    "RegistrationExpiresIn": null, "LastRegisterReceived": "2026-05-23T...",
    "HookState": null, "FirmwareVersion": null
  },
  "GVBridge": { "IsAvailable": true },
  "GVAudioBridge": {
    "IsActive": false,
    "Stats": {
      "InboundFramesSent": 0, "OutboundFramesReceived": 0,
      "InboundErrors": 0, "OutboundErrors": 0
    }
  },
  "RecentSipMessages": [ /* SipMessageEntry[] */ ],
  "RecentTimeline": [ /* CallTimelineEntry[] */ ]
}
```

### Design recommendation

**No new endpoints needed for Q2.** The existing `/api/diagnostics/status` shape is sufficient. RTest Phase C should:

1. Add typed DTOs to `Radio.Web/Models/ApiModels.cs` matching the shapes above
2. Add `DiagnosticsApiService` (parallel to `GvBridgeApiService` and friends) calling `/api/diagnostics/*`
3. Render the relevant fields on a "Phone Diagnostics" sub-page or as cards on the existing PhonePage

**Optional polish on the RotaryPhone side (~30 min):** Convert the anonymous-object responses in `GetStatus()` to a named `DiagnosticsStatusDto` record. This buys two things:

- Self-documenting OpenAPI schema (currently `IActionResult` returns hide the shape from Swagger)
- Stable JSON contract — anonymous types can drift if someone reorders fields

```csharp
// New file: Server/Controllers/Models/DiagnosticsStatusDto.cs
public record DiagnosticsStatusDto(
  SipStatusDto Sip,
  Ht801HealthStatus Ht801,
  GVBridgeStatusSnapshotDto GVBridge,
  GVAudioBridgeSnapshotDto GVAudioBridge,
  IReadOnlyList<SipMessageEntry> RecentSipMessages,
  IReadOnlyList<CallTimelineEntry> RecentTimeline);

public record SipStatusDto(bool IsListening, string? ListenAddress, int Port);
public record GVBridgeStatusSnapshotDto(bool IsAvailable);
public record GVAudioBridgeSnapshotDto(bool IsActive, AudioBridgeStats Stats);
```

Defer this until/unless Swagger drift becomes annoying. Not in PR1 scope.

### Test surface

Add to `RotaryPhoneController.Tests`:

- `DiagnosticsControllerTests` — at minimum, contract tests asserting that `GetStatus()` returns the documented shape via `WebApplicationFactory<Program>` or by directly invoking the controller with mocked deps. The investigation report's drift table is the contract.
- One round-trip test deserializing `DiagnosticsStatusDto` against a known JSON payload (regression guard).

### Boundary doc update

See Section 7 — adds a "Diagnostics" sub-section under "Integration Points Between the Two Services" describing the new RTest consumption.

### Effort: **S** (0 hours if no DTO refactor, 1 hour with DTO refactor + 1 test)

---

## 3. Q3 — Audio-Bridge Stats Endpoint

### What "audio-bridge stats" means in this codebase

Per `GVAudioBridgeService.cs` (line 232-357), the bridge:

- Receives 48 kHz PCM from Google Voice (Opus-decoded SIP audio)
- Resamples to 8 kHz
- Encodes to G.711 µ-law (PCMU)
- Sends via RTP to HT801 in 160-sample (20ms) frames
- Reverse path: HT801 RTP → µ-law → PCM → 48 kHz → SIP

Current `AudioBridgeStats`:

```csharp
public class AudioBridgeStats {
  public long InboundFramesSent;       // SIP → RTP
  public long OutboundFramesReceived;  // RTP → SIP
  public long InboundErrors;
  public long OutboundErrors;
  // Methods: RecordInboundSent, RecordOutboundReceived, RecordInboundError, RecordOutboundError
}
```

**No** packet loss / jitter / MOS / codec-negotiation telemetry today. The 4 monotonic counters are the entire surface. That's a useful "is audio flowing?" signal but not the rich call-quality dashboard people sometimes mean by "bridge stats".

### What already exposes them

`/api/diagnostics/status` returns `GVAudioBridge.Stats` as part of the full snapshot. That works but is **expensive to poll** for just stats (full snapshot includes 10 recent SIP messages + 10 timeline entries).

### Design recommendation

Add a dedicated lightweight endpoint:

```csharp
// In DiagnosticsController.cs
[HttpGet("audio-bridge")]
public IActionResult GetAudioBridge() => Ok(new AudioBridgeSnapshotDto(
    _gvAudioBridge.IsActive,
    _gvAudioBridge.Stats.InboundFramesSent,
    _gvAudioBridge.Stats.OutboundFramesReceived,
    _gvAudioBridge.Stats.InboundErrors,
    _gvAudioBridge.Stats.OutboundErrors,
    // Derived: % of frames flowing both directions when active, useful for at-a-glance health
    _gvAudioBridge.IsActive
      && _gvAudioBridge.Stats.InboundFramesSent > 0
      && _gvAudioBridge.Stats.OutboundFramesReceived > 0
));

public record AudioBridgeSnapshotDto(
  bool IsActive,
  long InboundFramesSent,
  long OutboundFramesReceived,
  long InboundErrors,
  long OutboundErrors,
  bool BidirectionalAudio);
```

**Future enhancement (out of scope for PR1):** add jitter and packet-loss tracking by hooking `RTPSession.OnRtpEvent` and `RTPSession.OnReceiveReport` (RTCP RR) inside `GVAudioBridgeService`. RTCP receiver reports already include fraction lost and inter-arrival jitter. This would require ~2 hours of additional work and is best deferred until the user actually needs the dashboard.

### Endpoint contract

| Method | Route | Request | Response |
|--------|-------|---------|----------|
| GET | `/api/diagnostics/audio-bridge` | — | `AudioBridgeSnapshotDto` (above) |

### Backing service

`GVAudioBridgeService` — no changes needed. Already in DI as singleton (registered by `GVBridgeServiceExtensions.AddGVBridge`).

### Test surface

- `DiagnosticsControllerTests.GetAudioBridge_ReturnsSnapshot` — verifies all six fields are present, `BidirectionalAudio = true` only when active + both counters > 0.
- Direct unit test on `AudioBridgeStats` already exists implicitly via `GVAudioBridgeServiceTests`; no new coverage needed.

### Boundary doc update

Add `GET /api/diagnostics/audio-bridge` to the integration points table (Section 7).

### Effort: **S** (1 hour incl. one test + DTO + endpoint)

---

## 4. Q4 — HT801 Reachability on Dashboard

### What already exists

`Ht801Reachable` is exposed in **two** existing endpoints:

1. **`GET /api/phone/system-status`** — `SystemStatus.Ht801Reachable` (`bool?`). Performs a live HTTP probe of HT801 IP each call via `IHT801ConfigService.TestConnectionAsync`. **This is the one RTest already deserializes** per the investigation report — RTest's `PhoneApiService.GetSystemStatusAsync` reads it correctly, and RTest's dashboard renders it as part of the System Status card.
2. **`GET /api/diagnostics/status`** — `Ht801.IsReachable` (derived from "did we ever see a REGISTER from this HT801?" — a softer signal).

### What's missing

**Nothing for the minimum-viable case.** The user's "yes add to dashboard" can be satisfied by RTest UI work alone — promote `Ht801Reachable` from "buried inside System Status card" to "top-level dashboard indicator" on the Phone page or Home page.

### Optional enhancement (recommended for Phase B PR1)

Add a richer dedicated endpoint that consolidates both signals plus the last-seen timestamp from `SipDiagnosticService`:

```csharp
// In DiagnosticsController.cs
[HttpGet("ht801")]
public IActionResult GetHt801([FromQuery] string? phoneId = null)
{
    phoneId ??= _config.Phones.FirstOrDefault()?.Id ?? "default";
    var ht801Config = _ht801Service.GetConfig(phoneId);
    var sipHealth = _diagnostics.GetHt801Health();
    bool? ipReachable = null;
    if (!string.IsNullOrEmpty(ht801Config.IpAddress) && ht801Config.IpAddress != "0.0.0.0")
    {
        var probe = await _ht801Service.TestConnectionAsync(ht801Config.IpAddress);
        ipReachable = probe.Success;
    }
    return Ok(new Ht801StatusDto(
        IpAddress: ht801Config.IpAddress,
        Extension: ht801Config.Extension,
        IpReachable: ipReachable,
        SipRegistered: sipHealth.IsRegistered,
        LastRegisterReceived: sipHealth.LastRegisterReceived,
        RegistrationExpiresInSeconds: sipHealth.RegistrationExpiresIn));
}

public record Ht801StatusDto(
  string? IpAddress, string Extension, bool? IpReachable,
  bool SipRegistered, DateTime? LastRegisterReceived,
  int? RegistrationExpiresInSeconds);
```

This is the **right shape for a dashboard card** because:

- `IpReachable` answers "Is the device on the network?"
- `SipRegistered` answers "Has it actually completed SIP registration?"
- `LastRegisterReceived` shows how fresh that signal is

A dashboard widget can show three colored dots (network/SIP/fresh) instead of a single ambiguous "Reachable" bool.

### Endpoint contract

| Method | Route | Request | Response |
|--------|-------|---------|----------|
| GET | `/api/diagnostics/ht801?phoneId=?` | optional phoneId | `Ht801StatusDto` |

### Backing service

Reuses `IHT801ConfigService` + `SipDiagnosticService` — both already in DI. No changes.

### Test surface

- `DiagnosticsControllerTests.GetHt801_NoSipHistory_ReturnsNotRegistered` — mocks SipDiagnosticService with empty buffer, asserts `SipRegistered=false`, `LastRegisterReceived=null`.
- `DiagnosticsControllerTests.GetHt801_WithSipHistory_ReturnsRegistered`.
- `DiagnosticsControllerTests.GetHt801_NoIp_ReturnsNullReachable` — config has empty `IpAddress`, endpoint returns `IpReachable=null` (not attempted) rather than `false`.

### Boundary doc update

Add `GET /api/diagnostics/ht801` to the integration points table.

### Effort: **S** (1 hour incl. 3 tests)

---

## 5. Q5 — SipRegistered / CookiesValid Fields on GvBridgeStatusDto

### Why this is the highest-value Phase B change

The investigation report flagged this as the **only thing blocking the cleanest fix** for the Phase A "single Available badge" UX. With these two fields, RTest can render:

- **GV API Available** — overall flag (already exposed)
- **SIP Registered** — green if `_sipTransport.IsRegistered == true`
- **Cookies Valid** — green if last `IsHealthyAsync` returned true

That converts a single ambiguous "Unavailable" state into actionable troubleshooting:

| Available | SipRegistered | CookiesValid | Meaning |
|:---------:|:-------------:|:------------:|---------|
| ✓ | ✓ | ✓ | Healthy |
| ✗ | ✗ | ✓ | SIP registration failed → check network / firewall |
| ✗ | ✗ | ✗ | Cookies stale → run `gv-login` |
| ✗ | ✓ | ✗ | Edge case (cookies just expired but SIP still alive) |

### What already exists internally

Both bits of state are tracked privately in `GVApiAdapter`:

- **SipRegistered:** `_sipTransport._registered` (private field in `GvSipTransport`). Set to `true` after `RegisterAsync` succeeds; never reset to false except via re-registration attempt. Already publicly exposed via `EnsureRegisteredAsync` (idempotent), but no read-only accessor.
- **CookiesValid:** computed by `_accountClient.IsHealthyAsync()` — fires periodically (every `CookieHealthCheckIntervalMinutes = 30` minutes by default) inside `OnHealthCheckTimer`. The result feeds `SetAvailable(healthy)` but is not cached as a separate property — the only signal externally is the rolled-up `IsAvailable`.

### What's missing

1. `GvSipTransport.IsRegistered` public property (delegates to `_registered`)
2. `GVApiAdapter.IsSipRegistered` public property (delegates to `_sipTransport?.IsRegistered ?? false`)
3. `GVApiAdapter.AreCookiesValid` public property — needs a new private cached field updated inside `RunHealthCheckAsync`
4. `GVBridgeController.GetStatus` response extended with the two new fields

### Design recommendation

**Step 1:** Expose `IsRegistered` on `GvSipTransport`:

```csharp
// GvSipTransport.cs
public bool IsRegistered => _registered;
```

**Step 2:** Expose two new properties on `GVApiAdapter` + cache cookie health:

```csharp
// GVApiAdapter.cs
private bool _areCookiesValid;  // cache last IsHealthyAsync result

public bool IsSipRegistered => _sipTransport?.IsRegistered ?? false;
public bool AreCookiesValid => _areCookiesValid;

// In RunHealthCheckAsync (existing), update the cache:
var healthy = await _accountClient.IsHealthyAsync();
_areCookiesValid = healthy;  // <-- NEW LINE

// Also update in ActivateAsync after the initial health check:
var healthy = await _accountClient.IsHealthyAsync(ct);
_areCookiesValid = healthy;  // <-- NEW LINE
```

**Step 3:** Extend `GVBridgeController.GetStatus`:

```csharp
[HttpGet("status")]
public IActionResult GetStatus()
{
    return Ok(new
    {
        available = _adapter.IsAvailable,
        activeMode = _registry.ActiveMode.ToString(),
        sipRegistered = _adapter.IsSipRegistered,  // NEW
        cookiesValid = _adapter.AreCookiesValid     // NEW
    });
}
```

**Naming note:** the existing response is camelCase (anonymous object property names start lowercase). New fields follow that convention. RTest's `GvBridgeStatusDto` uses PascalCase property names (`Available`, `ActiveMode`) — they map fine via `System.Text.Json`'s default property-name matching (case-insensitive when `PropertyNameCaseInsensitive=true` is configured, which RTest does in `Program.cs`). **Verify on RTest side** during Phase C that the configuration is in place; if not, RTest should add `[JsonPropertyName("sipRegistered")]` etc.

### Additivity guarantee

The response shape stays a superset. RTest Phase A (shipping now) reads `Available` and `ActiveMode` only — additional fields are ignored. Once PR1 ships, RTest Phase C can extend `GvBridgeStatusDto` to:

```csharp
public class GvBridgeStatusDto {
  public bool Available { get; set; }
  public string ActiveMode { get; set; } = "";
  public bool SipRegistered { get; set; }   // NEW (defaults to false if server is old)
  public bool CookiesValid { get; set; }    // NEW
}
```

If RTest Phase C is deployed before RotaryPhone PR1, `SipRegistered` and `CookiesValid` just default to `false` — UI shows two grey/warning badges. Not broken, just uninformative. Order doesn't matter for safety, just for UX completeness.

### Test surface

Add to `RotaryPhoneController.GVBridge.Tests`:

- `GVApiAdapterTests.IsSipRegistered_BeforeActivate_ReturnsFalse`
- `GVApiAdapterTests.AreCookiesValid_BeforeActivate_ReturnsFalse`
- `GVApiAdapterTests.AreCookiesValid_AfterFailedHealthCheck_ReturnsFalse` — needs `_accountClient` mocked or a real-network test gated by `[Trait("Category","Integration")]`
- Controller-level test in a new `GVBridgeControllerTests.cs` asserting the JSON response includes both keys

### Boundary doc update

Section 7 below — bump the integration table to show the new fields and add an "API contracts" sub-section.

### Effort: **S** (1.5 hours incl. 4 tests + manual smoke on Ubuntu)

---

## 6. Q6 — Cookie Management API

### Current state

**Zero HTTP surface for cookie management.** The flow is entirely CLI:

1. `dotnet RotaryPhoneController.Server -- gv-login` (handled in `Program.cs:21-44`, branches before `WebApplication.CreateBuilder`)
2. `CookieRetriever.RetrieveAndSaveAsync` uses Microsoft.Playwright + Chrome DevTools Protocol on port 9222 to:
   - Launch Chrome if needed (kills running Chrome first — disruptive)
   - Open `voice.google.com` and wait for user to log in manually
   - Extract all cookies via CDP
   - AES-256 encrypt to `data/gv-cookies.enc` with key at `data/gv-key.bin`
3. On normal startup, `GVApiAdapter.ActivateAsync` reads + decrypts both files

**The CLI requires:**
- Chrome installed on the server (✓ on Ubuntu box — already required for `gv-bridge-chrome.service` per boundary doc 2026-03-21 entry, though that service was killed by the SIP migration; Chrome may or may not still be installed)
- An interactive Chrome window (problematic for headless server)
- An out-of-band terminal session (SSH)

### Why a HTTP API helps

The investigation report's recommendation: **option (a) — paste-in cookies via HTTP — is the right v1.** Reasons:

- Doesn't require Chrome on the server (the user can run `gv-login` on their **dev machine**, copy the resulting cookie JSON, and POST it to the server)
- Doesn't require X11/Wayland on a headless box
- Avoids killing running Chrome (the current `CookieRetriever` kills all Chrome processes, which would nuke any RotaryPhone Chrome-extension dev work)
- Composable — RTest UI can offer a textarea + "Apply Cookies" button

**Option (b) — server-side `POST /api/gvbridge/cookies/refresh` invoking CDP** — has all the operational downsides above. Defer indefinitely unless headless-Chrome containers become a thing the user wants.

**Option (c) — both** — adds complexity without obvious value. Skip.

### Recommended endpoint shape

**Two endpoints. Authoritative model.**

#### `GET /api/gvbridge/cookies` — status

Read-only. Returns metadata about the currently-loaded cookie set (not the cookies themselves — they're secrets).

```csharp
public record GvCookieStatusDto(
  bool CookiesPresent,         // does the .enc file exist?
  bool CookiesValid,           // last IsHealthyAsync result (same as on /api/gvbridge/status)
  DateTime? LastValidatedAt,   // timestamp of last health-check call
  DateTime? LoadedAt,          // when the current cookie set was loaded into the adapter
  int? CookieCount,            // number of cookies in RawCookieHeader (informational)
  string? SapisidPrefix);      // first 8 chars of SAPISID (for "is this the right account?" verification)

[HttpGet("cookies")]
public IActionResult GetCookies() { ... }
```

**Security:** `SapisidPrefix` is the only piece of cookie data leaked, and only the first 8 characters. SAPISID is a high-entropy cookie (40+ chars) — 8-char prefix is enough for the user to recognize "yes this is my account" but not enough to forge SAPISIDHASH (requires full value + timestamp + URL). Acceptable risk.

#### `POST /api/gvbridge/cookies` — replace

Accepts a JSON body matching the cookie format produced by the user's local `gv-login`:

```csharp
public record SetCookiesRequest(
  string Sapisid,
  string Sid,
  string Hsid,
  string Ssid,
  string Apisid,
  string? Secure1Psid,
  string? Secure3Psid,
  string? RawCookieHeader);  // preferred — most reliable

[HttpPost("cookies")]
public async Task<IActionResult> SetCookies([FromBody] SetCookiesRequest request)
{
    // Validate required fields
    if (string.IsNullOrEmpty(request.Sapisid) || string.IsNullOrEmpty(request.Sid))
        return BadRequest(new { error = "Sapisid and Sid are required" });

    var cookieSet = new GvCookieSet { /* map from request */ };
    await _cookieStore.SaveAsync(cookieSet);

    // Re-activate adapter to pick up new cookies
    await _registry.SwitchModeAsync(CallAdapterMode.GVApi);  // forces re-activation

    return Ok(new { saved = true });
}
```

The "re-activate" step is critical — without it, `GVApiAdapter` holds the OLD `_cookieSet` in memory until the next process restart.

#### Optional: `POST /api/gvbridge/cookies/refresh` — trigger CDP login

**Defer to Phase 2.** The CDP flow as-is is incompatible with a headless server. If the user later wants this, the implementation would:

1. Run on **localhost only** (require `127.0.0.1` source) since it needs an interactive browser
2. Return a `202 Accepted` with a polling URL because CDP login is slow + user-interactive
3. Stream progress via SignalR

Not in PR2 scope.

### Backing service additions

`GVApiAdapter` needs:

- A way to **reload cookies without full deactivate/activate** (current code only loads cookies in `ActivateAsync`)
- A `LoadedAt` timestamp tracker
- A `LastValidatedAt` timestamp tracker (set inside `RunHealthCheckAsync`)

Suggest extracting a small `IGvCookieManager` interface:

```csharp
public interface IGvCookieManager
{
    Task<bool> SetCookiesAsync(GvCookieSet cookies, CancellationToken ct = default);
    GvCookieStatusDto GetStatus();
}

public class GvCookieManager : IGvCookieManager
{
    private readonly GvCookieStore _store;
    private readonly GVApiAdapter _adapter;
    private readonly ICallAdapterRegistry _registry;
    // ...
}
```

Register as singleton in `GVBridgeServiceExtensions.AddGVBridge`. Inject into `GVBridgeController`.

### File ownership

All changes contained within:

- `src/RotaryPhoneController.GVBridge/Adapters/GVApiAdapter.cs` (add 2 properties + 2 timestamp fields + Reload method)
- `src/RotaryPhoneController.GVBridge/Services/GvCookieManager.cs` (new)
- `src/RotaryPhoneController.GVBridge/Api/GVBridgeController.cs` (add 2 endpoints + DI of cookie manager)
- `src/RotaryPhoneController.GVBridge/Api/GvBridgeDtos.cs` (new — keeps records out of the controller file)
- `src/RotaryPhoneController.GVBridge/Extensions/GVBridgeServiceExtensions.cs` (register `IGvCookieManager`)

### Test surface

- `GvCookieManagerTests.SetCookies_ValidPayload_SavesAndReloads` — uses an in-memory `GvCookieStore` with temp file path
- `GvCookieManagerTests.SetCookies_MissingSapisid_ReturnsBadRequest` (via controller test)
- `GvCookieManagerTests.GetStatus_NoCookies_ReturnsCookiesPresentFalse`
- `GvCookieManagerTests.GetStatus_AfterSet_ReturnsCookiePrefix`
- Integration test (gated, `[Trait("Category","Integration")]`) that round-trips through the live `GVApiAdapter` re-activation path against a stub `IAccountClient`

### Security considerations

- **No authentication on /api/gvbridge/cookies POST** — RotaryPhone runs on internal LAN only (radio:5004 not exposed to internet). The same trust model already applies to ALL other endpoints (no auth on any of them). Worth flagging in the boundary doc that "if RotaryPhone is ever exposed beyond LAN, cookie endpoints need auth IMMEDIATELY".
- **Logs must not include cookie values** — review controller code to ensure no `LogInformation("Got cookies: {Cookies}", request)` slip-ups.
- **JSON body size limit** — set `[RequestSizeLimit(10_000)]` on the POST endpoint. Cookies are typically 2-4 KB; 10 KB is generous and avoids DoS via huge bodies.

### Boundary doc update

Section 7 — adds a "Cookie management" subsection under integration points, including the security caveat.

### Effort: **M** (3-4 hours incl. ~5 tests + GvCookieManager refactor + manual smoke)

---

## 7. Boundary Doc Updates

The boundary doc (`D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`) is the canonical source for cross-service contracts. The investigation report noted its Change Log is stale (last update 2026-03-21 despite major SIP rewrite in 2026-03-30). **PR1 and PR2 should both update the boundary doc.**

### Proposed additions

#### Add to **"Integration Points Between the Two Services"** section (after the existing "Radio Console → RotaryPhone (SignalR + REST)" subsection):

```markdown
### REST endpoints consumed by Radio Console (RTest UI)

| Method | Route | Purpose | Phase added |
|--------|-------|---------|:-----------:|
| GET | `/api/phone/system-status` | Platform / BT / SIP / HT801 reachability | A (existing) |
| GET | `/api/phone/status` | Current call state | A (existing) |
| GET | `/api/gvbridge/status` | GV API availability + SipRegistered + CookiesValid | B (PR1) |
| GET | `/api/gvbridge/adapter/mode` | Available + active call adapter mode | A (existing) |
| PUT | `/api/gvbridge/adapter/mode` | Switch active adapter mode | A (existing) |
| GET | `/api/gvbridge/cookies` | Cookie status metadata (no secrets) | B (PR2) |
| POST | `/api/gvbridge/cookies` | Replace cookie set from paste | B (PR2) |
| GET | `/api/gvtrunk/status` | VoIP.ms SIP trunk registration state | A (existing) |
| GET | `/api/gvtrunk/calls` | Call history (last 50) | A (existing) |
| GET | `/api/gvtrunk/sms` | SMS history (last 20, in-memory) | A (existing) |
| POST | `/api/gvtrunk/dial` | Place outbound call via trunk | A (existing) |
| POST | `/api/gvtrunk/reregister` | Force re-registration of trunk | A (existing) |
| GET | `/api/diagnostics/status` | Full diagnostics snapshot | B (existing, newly consumed) |
| GET | `/api/diagnostics/audio-bridge` | Audio-bridge stats only (cheap polling) | B (PR1) |
| GET | `/api/diagnostics/ht801` | Consolidated HT801 status (network + SIP + freshness) | B (PR1) |
| GET | `/api/diagnostics/sip-log` | Recent SIP messages | B (existing, newly consumed) |
| GET | `/api/diagnostics/timeline` | Call timeline events | B (existing, newly consumed) |
| GET | `/api/contacts/*` | Contact CRUD (already in use) | A (existing) |
| GET | `/api/callhistory` | Call history | A (existing) |

**JSON conventions:** All response payloads are camelCase or PascalCase depending on whether the controller returns an anonymous object (camelCase) or a typed DTO/record (PascalCase). RTest's `Radio.Web` configures `JsonSerializerOptions.PropertyNameCaseInsensitive = true` so both work transparently. **New endpoints should prefer typed records** for OpenAPI/Swagger schema clarity.

**Polling cadence guidance for RTest:**

- `/api/phone/status` — 5 seconds (already)
- `/api/gvbridge/status` — 10 seconds (cheap, fast-changing)
- `/api/gvtrunk/status` — 10 seconds
- `/api/diagnostics/audio-bridge` — 2 seconds **only while audio bridge is active**, otherwise paused
- `/api/diagnostics/ht801` — 30 seconds (involves a network probe of HT801)
- `/api/gvbridge/cookies` — 60 seconds (cookie expiry is measured in days)
- `/api/diagnostics/sip-log`, `/api/diagnostics/timeline` — only on-demand (user opens diagnostics panel), do NOT poll
```

#### Add to **"Operational Notes"** section, new subsection:

```markdown
### Cookie Management Security

RotaryPhone's `/api/gvbridge/cookies` endpoints accept and return Google Voice authentication state. **They have no authentication today** because RotaryPhone listens only on the LAN (radio:5004). If RotaryPhone is ever exposed beyond the LAN (port forward, VPN ingress, Tailscale, etc.), `POST /api/gvbridge/cookies` becomes a credential-theft / account-hijack vector. **MUST add auth before any external exposure.** Suggested approach: API key in `appsettings.json`, required header `X-RotaryPhone-Auth: <key>` on cookie endpoints, validated by middleware.
```

#### Add to **Change Log** table:

```markdown
| 2026-05-24 | Radio Console session (planning) | Phase B server additions plan published at `D:/prj/RTest/RTest/design/research/rotaryphone-phase-b-server-additions-plan.md`. Will be executed in the RotaryPhone session as two PRs (PR1: SipRegistered/CookiesValid + audio-bridge endpoint + HT801 endpoint; PR2: cookie management). RTest Phase C consumes after PR1 deploys. |
| (PR1 merge date) | RotaryPhone session | PR1 merged: `/api/gvbridge/status` returns `sipRegistered` + `cookiesValid`; new `/api/diagnostics/audio-bridge` and `/api/diagnostics/ht801` endpoints. RTest Phase C can now consume. |
| (PR2 merge date) | RotaryPhone session | PR2 merged: cookie management endpoints `/api/gvbridge/cookies` (GET, POST). LAN-only — see Cookie Management Security note. |
```

---

## 8. Sequencing — PR Plan + Dependency Graph

### Recommended PR breakdown

```
PR1: gvbridge-status-fields + diagnostics-additions  (Phase B core, S)
  ├─ Task 1: Expose GvSipTransport.IsRegistered (1 line)
  ├─ Task 2: Expose GVApiAdapter.{IsSipRegistered, AreCookiesValid} (~10 lines + 4 tests)
  ├─ Task 3: Extend GVBridgeController.GetStatus (2 lines + 1 controller test)
  ├─ Task 4: Add DiagnosticsController.GetAudioBridge endpoint (15 lines + 1 test)
  ├─ Task 5: Add DiagnosticsController.GetHt801 endpoint (25 lines + 3 tests)
  ├─ Task 6: Update boundary doc Change Log + REST table
  └─ Task 7: PR description references this plan doc

PR2: gvbridge-cookie-management  (Phase B extension, M, depends on PR1 not deployed)
  ├─ Task 1: Extract GvCookieManager service (~80 lines)
  ├─ Task 2: Add LoadedAt / LastValidatedAt tracking to GVApiAdapter (~6 lines)
  ├─ Task 3: Add Reload method to GVApiAdapter (separated from full Activate; ~20 lines)
  ├─ Task 4: GET /api/gvbridge/cookies endpoint (~15 lines + 2 tests)
  ├─ Task 5: POST /api/gvbridge/cookies endpoint (~30 lines + 3 tests)
  ├─ Task 6: Wire GvCookieManager in GVBridgeServiceExtensions
  ├─ Task 7: Update boundary doc with cookie security note + Change Log
  └─ Task 8: Manual smoke test on Ubuntu (paste cookies via curl, verify GV API recovers)
```

### Dependency graph

```
PR1 (independent of PR2)
   │
   ├─→ deploys to Ubuntu (radio-api service restart)
   │
   └─→ unblocks RTest Phase C (two-badge UI + audio-bridge widget + HT801 card)

PR2 (independent of PR1 functionally; sequenced for review clarity)
   │
   ├─→ deploys to Ubuntu
   │
   └─→ unblocks RTest Phase C (cookie management section in SystemConfigPage)
```

PR1 and PR2 **can be done in parallel** technically — they touch overlapping files (`GVApiAdapter.cs`, `GVBridgeController.cs`, boundary doc) but at non-conflicting sites. However, splitting them sequentially gives the user a clean PR1 review with all the lower-risk additive changes, then a focused PR2 review for the cookie surface which has security implications.

### Optional PR3 (not in Phase B scope, mentioned for completeness)

```
PR3: diagnostics-rtcp-stats  (Phase B+, M, future)
  └─ Add packet-loss + jitter tracking to GVAudioBridgeService via RTCP RR
```

Punt until user explicitly wants richer call-quality dashboards.

---

## 9. Cross-Repo Coordination — Keeping Phase C Synced

### Timeline

```
T+0   (now)            : RTest Phase A in flight (DTO + hub cleanup)
T+1   day              : RotaryPhone PR1 opened in RotaryPhone repo
T+2   days             : RotaryPhone PR1 merged + deployed to Ubuntu
T+2.5 days             : RTest Phase C begins consuming new fields
T+3   days             : RTest Phase C PR opened
T+4   days             : RotaryPhone PR2 opened
T+5   days             : RotaryPhone PR2 merged + deployed
T+6   days             : RTest Phase C extended with cookie UI
```

### Coordination mechanics

Per the boundary doc's "Passing Work Between Sessions" protocol:

1. **Radio Console session (current)** writes this plan doc (DONE) and writes a prompt file at `D:\prj\RotaryPhone\docs\prompts\2026-05-24-server-api-additions.md` referencing this plan. *(NOT done in this investigation — recommend the user creates that prompt or asks the next RotaryPhone session to read this plan directly.)*

2. **User switches to RotaryPhone session** and asks the agent to execute PR1 per Section 8.

3. **RotaryPhone session** completes PR1, updates the boundary doc Change Log, replies with the new endpoint specs (RTest can verify against them).

4. **User switches back to Radio Console session** and asks for RTest Phase C — by then the boundary doc has the post-merge entry, and the RTest agent can pull the latest contract.

5. Repeat for PR2.

### Avoiding drift during the gap

If RTest Phase A ships before RotaryPhone PR1 (likely — Phase A is RTest-side only and very small), the UI will show **a single "GV API Available" badge** instead of the two badges. That's acceptable and shipped. When PR1 lands, Phase C upgrades it to two badges. No reverse incompatibility because Phase A's DTO ignores unknown JSON fields.

### Verification ritual after each RotaryPhone PR

```powershell
# After RotaryPhone PR1 deploys (run from Windows or via ssh radio):
curl http://radio:5004/api/gvbridge/status | jq .
# Expect: { "available": ..., "activeMode": "...", "sipRegistered": ..., "cookiesValid": ... }

curl http://radio:5004/api/diagnostics/audio-bridge | jq .
# Expect: { "IsActive": ..., "InboundFramesSent": 0, ... }

curl http://radio:5004/api/diagnostics/ht801 | jq .
# Expect: { "IpAddress": "192.168.86.22", "Extension": "...", "IpReachable": true, ... }
```

```powershell
# After RotaryPhone PR2 deploys:
curl http://radio:5004/api/gvbridge/cookies | jq .
# Expect: { "CookiesPresent": true, "CookiesValid": true, "LoadedAt": "...", "SapisidPrefix": "abc12345" }

# POST test (use existing cookies dumped from gv-login output):
curl -X POST http://radio:5004/api/gvbridge/cookies \
  -H "Content-Type: application/json" \
  -d '{"Sapisid":"...","Sid":"...","Hsid":"...","Ssid":"...","Apisid":"..."}'
# Expect: { "saved": true }
# Watch radio-api logs for: "GVApiAdapter activated — SIP transport ready"
```

---

## 10. Questions for User

These need answers before either PR begins:

### Q-A: Field naming on `GvBridgeStatusDto`

The plan uses **`sipRegistered`** and **`cookiesValid`** (camelCase, matching existing anonymous-object convention). Confirm acceptable. Alternative naming options if you'd prefer:

- `sip: { registered: true, ... }` — nested, more extensible
- `auth: { cookiesValid: true, expiresAt: ... }` — nested with future-proofing

Recommendation: **flat** for v1, nest later if you add more.

### Q-B: HT801 endpoint — new or skip?

The dashboard work could just use existing `/api/phone/system-status.Ht801Reachable`. Add the dedicated `/api/diagnostics/ht801` endpoint with richer fields (SIP registration + freshness) — or skip and only do RTest-side promotion?

Recommendation: **add it.** The 3-dot indicator (network/SIP/fresh) is much more diagnostic than a single bool. ~1 hour of work.

### Q-C: Cookie endpoint security model

Currently RotaryPhone has **no auth on any endpoint**. POST /api/gvbridge/cookies inherits that. Confirm acceptable for v1 (LAN-only assumption) — or add a simple API-key header check now to avoid the future-exposure risk?

Recommendation: **defer auth to a future security-hardening PR** that covers all endpoints uniformly. Don't single out cookies. But add the boundary-doc security note (Section 7).

### Q-D: Cookie endpoint POST shape — individual fields vs raw header

`GvCookieSet` supports both individual cookie fields AND a `RawCookieHeader` blob (the raw `Cookie:` header string, which Google requires because there are many cookies beyond the core 7: SIDCC, __Secure-1PSIDCC, NID, etc.).

Should the POST endpoint accept:

- (a) Individual fields only (cleaner API, but won't work — Google rejects requests without the extra cookies)
- (b) RawCookieHeader only (simplest, just paste in browser DevTools "Cookie" header value)
- (c) Both, with RawCookieHeader preferred when present

Recommendation: **(c).** RawCookieHeader is the realistic path. Individual fields fallback for completeness / migration.

### Q-E: Polling cadence for audio-bridge endpoint

Plan section 7 suggests "2 seconds only while bridge is active". Acceptable, or do you want a different rate? (The endpoint is genuinely cheap — atomic reads of `Interlocked.Read`-backed counters — 100ms would also work.)

Recommendation: **2 seconds during call, paused otherwise.** Matches the visual update cadence humans expect.

### Q-F: Should RotaryPhone also expose a SignalR push for status changes?

Currently `/hubs/gvtrunk` pushes `RegistrationChanged` for the trunk path. There's NO push for GVApi mode (the investigation report confirmed `/hubs/gvbridge` doesn't exist). Should PR1 also add a hub or SignalR event for `GvBridgeStatusChanged` so the RTest UI doesn't have to poll?

Recommendation: **skip for Phase B.** Polling every 10s is fine for the GV status fields (they don't change rapidly). Add a hub only if a future use case actually needs sub-second push.

### Q-G: Anything in PR2 we should skip?

The full PR2 surface is `GET /cookies` + `POST /cookies` + GvCookieManager service. If you'd prefer the absolute minimum, **just `POST /cookies`** is sufficient — RTest can use `/api/gvbridge/status.cookiesValid` to know if it worked. The GET endpoint is a UX polish (shows which cookies are loaded). Worth keeping IMO, but cuttable.

Recommendation: **keep GET.** The `SapisidPrefix` field gives users "is this my account?" verification before they apply new cookies — important when juggling multiple Google accounts.

---

## Appendix — Key file paths

### RotaryPhone source files (touched by Phase B)

- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Adapters\GVApiAdapter.cs` — PR1 + PR2
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Sip\GvSipTransport.cs` — PR1 (1 line)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Api\GVBridgeController.cs` — PR1 + PR2
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Extensions\GVBridgeServiceExtensions.cs` — PR2
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Services\GvCookieManager.cs` — PR2 (NEW)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Api\GvBridgeDtos.cs` — PR2 (NEW)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Server\Controllers\DiagnosticsController.cs` — PR1 (2 new endpoints)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge.Tests\Adapters\GVApiAdapterTests.cs` — PR1 + PR2 (existing)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge.Tests\Services\GvCookieManagerTests.cs` — PR2 (NEW)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge.Tests\Api\GVBridgeControllerTests.cs` — PR1 + PR2 (likely NEW)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Tests\DiagnosticsControllerTests.cs` — PR1 (likely NEW)
- `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` — PR1 + PR2 (Change Log + integration table)

### Reference files (read-only in Phase B planning)

- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Services\GVAudioBridgeService.cs` — defines `AudioBridgeStats` (consumed unchanged)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Core\Diagnostics\SipDiagnosticService.cs` — defines `Ht801HealthStatus` + REGISTER tracking (consumed unchanged)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Auth\GvCookieSet.cs` — cookie shape
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Auth\GvCookieStore.cs` — AES-256 file-based persistence
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Models\GVBridgeConfig.cs` — `CookieFilePath`, `CookieKeyFilePath`, `CookieHealthCheckIntervalMinutes`
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Clients\GvAccountClient.cs` — `IsHealthyAsync` ground truth
- `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Auth\CookieRetriever.cs` — CDP-based CLI flow (unchanged, kept as `gv-login`)
- `D:\prj\RotaryPhone\src\RotaryPhoneController.Server\Program.cs` — DI wiring + `gv-login` CLI branch (unchanged)
- `D:\prj\RTest\RTest\design\research\rotaryphone-api-state-2026-05-24.md` — full investigation report

### RTest files (Phase C, NOT in Phase B scope)

- `D:\prj\RTest\RTest\src\Radio.Web\Models\ApiModels.cs` — DTOs to extend with `SipRegistered` / `CookiesValid` / cookie status / audio bridge / ht801 records
- `D:\prj\RTest\RTest\src\Radio.Web\Services\ApiClients\GvBridgeApiService.cs` — add cookie GET/POST methods
- `D:\prj\RTest\RTest\src\Radio.Web\Services\ApiClients\DiagnosticsApiService.cs` — NEW (Phase C)
- `D:\prj\RTest\RTest\src\Radio.Web\Components\Pages\PhonePage.razor` — two-badge replacement + HT801 card
- `D:\prj\RTest\RTest\src\Radio.Web\Components\Pages\SystemConfigPage.razor` — cookie management section
