# Enhanced Bluetooth Management & PBAP Sync UI

**Date:** 2026-03-12
**Status:** Approved
**Branch:** `feature/bt-management-pbap-sync-ui`

## Summary

Enhance the BluetoothPage with an expanded connected-device panel showing active service badges (A2DP, HFP, PBAP) and context-aware disconnect. Add a PBAP "Sync from Phone" dropdown button to the PhonePage Contacts tab with device selection, sync status bar, and source column distinguishing PBAP vs manual contacts.

## Motivation

- Users have no visibility into what services are active on a connected BT device
- The disconnect button gives no context about what it will affect (audio, phone, contacts all drop)
- There is no UI to trigger PBAP contact sync — only the REST API endpoint
- PBAP contacts and RotaryPhone contacts are separate with no unified view

## Design

### 1. BluetoothPage — Expanded Connected Device Card

When a device is connected, the existing "Connected Device" card expands to show:

#### Service Badges

Colored pills indicating active services. Status is inferred from existing data (no new BlueZ queries):

| Badge | Condition to Show | Status Text |
|-------|------------------|-------------|
| A2DP Audio | `ConnectedDevice != null` && audio source is Bluetooth | "Playing" / "Idle" |
| HFP Phone | PhoneIntegration enabled && hub connected | "Connected to RotaryPhone" / "Disconnected" |
| PBAP Contacts | PBAP contacts exist for this device address | "{count} synced ({time} ago)" / "Not synced" |

#### Service Detail Rows

Below the badges, a compact info panel with one row per service showing current state.

#### Context-Aware Disconnect

Single red "Disconnect" button with subtitle text: "Stops audio, phone, and all BT services". This replaces the existing disconnect button (same API call, better UX context).

#### Data Sources

The page needs three pieces of data to populate the service panel:
1. **BT status** — already fetched via `GET /api/bluetooth/status` (has `ConnectedDevice`)
2. **Phone integration status** — `GET /api/integrations/phone/status` (has `IsConnected`, `CurrentState`)
3. **PBAP sync status** — `GET /api/bluetooth/pbap/status` (has device contact count, last sync time)

All three are fetched in parallel on page load and on BT status refresh.

### 2. PhonePage Contacts Tab — PBAP Sync

#### "Sync from Phone" Dropdown Button

Placed next to the existing "Add Contact" button in the Contacts tab header:
- Dropdown lists paired BT devices from `GET /api/bluetooth/status`
- Connected device is highlighted at the top
- Clicking a device triggers `POST /api/bluetooth/pbap/sync?deviceAddress={address}`
- Button shows a loading spinner during sync
- On completion, the contacts grid refreshes

#### Sync Status Bar

A compact bar below the action buttons showing:
- Last synced device name
- Time since last sync
- Contact count
- Freshness badge ("Fresh" / "Stale")

Data from `GET /api/bluetooth/pbap/status`.

#### Source Column

New column in the contacts DataGrid:
- "PBAP" badge for contacts synced from phone (from Radio.API PBAP endpoints)
- "Manual" badge for contacts entered via RotaryPhone (from RotaryPhone API)

#### Merged Contact List

The grid merges contacts from two sources:
1. **PBAP contacts** — `GET /api/bluetooth/pbap/contacts?deviceAddress={connectedDevice}` → Radio.API
2. **RotaryPhone contacts** — `GET /api/contacts` → RotaryPhone API (existing `PhoneApiService`)

Both are loaded in parallel and merged into a single list sorted by name. The source column distinguishes origin.

### 3. New Web Service Client

Create `PbapApiService` in `Radio.Web/Services/ApiClients/` to call Radio.API PBAP endpoints:

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `SyncContactsAsync(deviceAddress)` | `POST /api/bluetooth/pbap/sync` | Trigger PBAP sync |
| `GetContactsAsync(deviceAddress)` | `GET /api/bluetooth/pbap/contacts` | Get synced contacts |
| `GetSyncStatusAsync()` | `GET /api/bluetooth/pbap/status` | Get sync status |

### 4. New DTOs (Radio.Web)

```csharp
// Already exists partially — extend as needed
public class PbapSyncResultDto
{
    public bool Success { get; set; }
    public int ContactCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PbapSyncStatusDto
{
    public List<PbapDeviceSyncInfoDto> Devices { get; set; } = new();
}

public class PbapDeviceSyncInfoDto
{
    public string DeviceAddress { get; set; } = "";
    public string? DeviceName { get; set; }
    public int ContactCount { get; set; }
    public DateTime? LastSynced { get; set; }
    public bool IsStale { get; set; }
}

public class PbapContactDto
{
    public string DisplayName { get; set; } = "";
    public List<string> PhoneNumbers { get; set; } = new();
}
```

### 5. What Does NOT Change

- No per-profile disconnect (BlueZ `Device1` only supports device-level)
- No changes to BlueZ D-Bus code or `LinuxBluetoothService`
- No changes to the paired devices table or discovery UI on BluetoothPage
- No changes to the PBAP backend (PbapSyncService, PbapContactRepository, PbapController — all working)
- No changes to the API layer

## File Changes

### New Files
- `src/Radio.Web/Services/ApiClients/PbapApiService.cs` — Web client for PBAP API endpoints

### Modified Files
- `src/Radio.Web/Components/Pages/BluetoothPage.razor` — Expanded connected device card with service badges, detail rows, context-aware disconnect
- `src/Radio.Web/Components/Pages/PhonePage.razor` — Contacts tab: sync dropdown, status bar, source column, merged contact list
- `src/Radio.Web/Models/ApiModels.cs` — Add PBAP DTOs (PbapSyncResultDto, PbapSyncStatusDto, PbapDeviceSyncInfoDto, PbapContactDto)
- `src/Radio.Web/Program.cs` or DI registration — Register `PbapApiService` with HttpClient

## Testing

- **Manual testing** — Deploy to Ubuntu, verify BT page shows service badges for connected Pixel 8 Pro, verify disconnect button context text, verify PBAP sync from PhonePage contacts tab
- **bUnit tests** — Test BluetoothPage renders service badges when status includes connected device, test PhonePage renders merged contact list with source column
