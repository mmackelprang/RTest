# RotaryPhone SIP Integration — RTest UI Update Plan

> **For agentic workers:** Read `docs/rotaryphone-sip-integration-prompt.md` for full context on what changed in RotaryPhone.

**Goal:** Update RTest's Phone UI and API clients to work with RotaryPhone's new SIP-over-WebSocket architecture (Chrome extension removed).

**Scope:** UI-only changes in Radio.Web + one REST endpoint update in RotaryPhone.

---

## Task 1: Update ApiModels.cs

**File:** `src/Radio.Web/models/ApiModels.cs`

Update `GvBridgeStatusDto`:

```csharp
// Replace existing GvBridgeStatusDto with:
public class GvBridgeStatusDto
{
    public string ActiveMode { get; set; } = "";
    public bool SipRegistered { get; set; }
    public bool CookiesValid { get; set; }
    public bool Available { get; set; }
}
```

Remove any references to `ExtensionConnected` or `ExtensionVersion`.

---

## Task 2: Delete GvBridgeHubService.cs

**File:** `src/Radio.Web/services/ApiClients/GvBridgeHubService.cs`

Delete this file entirely — the `/hubs/gvbridge` SignalR hub no longer exists in RotaryPhone. The GV Bridge status will be polled via REST instead.

Remove any DI registration for `GvBridgeHubService` in `Program.cs` or service registration files.

---

## Task 3: Update GvBridgeApiService.cs

**File:** `src/Radio.Web/services/ApiClients/GvBridgeApiService.cs`

Update the status endpoint to match the new DTO. The REST endpoint `/api/gvbridge/status` returns the updated `GvBridgeStatusDto` with `SipRegistered`, `CookiesValid`, `Available` instead of `ExtensionConnected`.

The mode endpoint (`/api/gvbridge/adapter/mode`) still works with the same API but mode values change:
- Old: `"GVBrowser"` → New: `"GVApi"`

---

## Task 4: Update PhonePage.razor

**File:** `src/Radio.Web/components/Pages/PhonePage.razor`

### Mode Selector Changes

Replace the three mode buttons:
- `Bluetooth HFP` — unchanged
- `SIP Trunk` — unchanged
- `GV Browser` → `GV API (SIP)`

### Extension Status → SIP Status

Replace:
```razor
<!-- OLD: Chrome Extension status -->
<MudChip Color="@(status.ExtensionConnected ? Color.Success : Color.Error)">
    Extension: @(status.ExtensionConnected ? "Connected" : "Disconnected")
</MudChip>
```

With:
```razor
<!-- NEW: SIP registration + cookie status -->
<MudChip Color="@(status.SipRegistered ? Color.Success : Color.Warning)">
    SIP: @(status.SipRegistered ? "Registered" : "Not Registered")
</MudChip>
<MudChip Color="@(status.CookiesValid ? Color.Success : Color.Error)">
    Cookies: @(status.CookiesValid ? "Valid" : "Expired")
</MudChip>
```

### Remove Extension-Related UI

Remove any UI elements that reference:
- Chrome extension connection status
- Extension version display
- "Extension disconnected" warnings

---

## Task 5: Update RotaryPhone GVBridgeController

**File (in RotaryPhone project):** `src/RotaryPhoneController.GVBridge/Api/GVBridgeController.cs`

Add/update the status endpoint to expose SIP registration and cookie validity:

```csharp
[HttpGet("status")]
public IActionResult GetStatus()
{
    var adapter = _adapter; // GVApiAdapter
    return Ok(new
    {
        ActiveMode = adapter.Mode.ToString(),
        SipRegistered = adapter.IsSipRegistered,
        CookiesValid = adapter.AreCookiesValid,
        Available = adapter.IsAvailable,
    });
}
```

This requires adding `IsSipRegistered` and `AreCookiesValid` properties to `GVApiAdapter`.

---

## Task 6: Update PhonePage Poll Loop

**File:** `src/Radio.Web/components/Pages/PhonePage.razor`

The existing poll loop likely calls `GvBridgeHubService` for real-time updates. Since the hub is gone, update to use REST polling:

Replace SignalR subscription with periodic REST poll (every 5 seconds):
```csharp
// In OnInitializedAsync or lifecycle method:
_pollTimer = new Timer(async _ =>
{
    try
    {
        var status = await GvBridgeApiService.GetStatusAsync();
        await InvokeAsync(() =>
        {
            _gvBridgeStatus = status;
            StateHasChanged();
        });
    }
    catch { /* ignore poll failures */ }
}, null, 0, 5000);
```

---

## Task 7: Build and Test

```bash
# Build RTest
cd d:/prj/rtest/rtest
dotnet build --configuration Release

# Run tests
dotnet test --configuration Release

# Manual UAT: Start both services and verify PhonePage
```

### UAT Checklist
- [ ] PhonePage loads without JS/SignalR errors
- [ ] Mode buttons show: Bluetooth HFP, SIP Trunk, GV API (SIP)
- [ ] SIP status badge updates (Registered/Not Registered)
- [ ] Cookie status badge updates (Valid/Expired)
- [ ] Switching modes works
- [ ] Incoming call announcement works (ring + TTS + ducking)
- [ ] Call state chip updates (Idle → Ringing → InCall → Idle)
- [ ] Contacts tab works
- [ ] Call history tab works

---

## Task 8: Commit and PR

```bash
git add -A
git commit -m "feat(phone): update GV Bridge UI for SIP-over-WebSocket architecture

Replace Chrome extension references with SIP registration status.
Delete GvBridgeHubService (hub removed from RotaryPhone).
Update mode selector: GV Browser → GV API (SIP).

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Dependencies

| Task | Depends On | Notes |
|------|-----------|-------|
| Tasks 1-4 | RotaryPhone PR #19 merged | Need new API responses |
| Task 5 | Access to RotaryPhone repo | GVBridgeController update |
| Task 6 | Task 2 (hub deleted) | Poll replaces hub |
| Task 7 | All above | Integration testing |
