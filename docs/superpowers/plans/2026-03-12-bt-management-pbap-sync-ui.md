# Enhanced Bluetooth Management & PBAP Sync UI — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add service status badges to the BluetoothPage connected-device card and a "Sync from Phone" PBAP dropdown with merged contact list to the PhonePage Contacts tab.

**Architecture:** The BluetoothPage fetches three parallel API calls (BT status, phone integration status, PBAP sync status) to populate read-only service badges. The PhonePage Contacts tab adds a `PbapApiService` client to trigger PBAP sync and merge PBAP contacts with RotaryPhone contacts in a single grid with a source column.

**Tech Stack:** Blazor Server (Radzen components), HttpClient-based API services, existing REST endpoints (`/api/bluetooth/status`, `/api/integrations/phone/status`, `/api/bluetooth/pbap/*`)

**Spec:** `docs/superpowers/specs/2026-03-12-bt-management-pbap-sync-ui-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `src/Radio.Web/Services/ApiClients/PbapApiService.cs` | HttpClient wrapper for PBAP REST endpoints (sync, contacts, status) |

### Modified Files
| File | Changes |
|------|---------|
| `src/Radio.Web/Models/ApiModels.cs` (append after line 878) | Add PBAP DTOs: `PbapSyncResultDto`, `PbapSyncStatusDto`, `PbapDeviceSyncInfoDto`, `PbapContactDto` |
| `src/Radio.Web/Program.cs` (~line 253) | Register `PbapApiService` with `AddHttpClient<T>()` |
| `src/Radio.Web/Components/Pages/BluetoothPage.razor` (lines 76-129 + code-behind) | Expanded connected-device card with service badges, detail rows, context-aware disconnect |
| `src/Radio.Web/Components/Pages/PhonePage.razor` (lines 149-180 + code-behind) | Sync dropdown button, status bar, source column, merged contact list |
| `tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs` | Register `PbapApiService` in test DI, add tests for merged contact list and source column |

---

## Chunk 1: PBAP API Client & DTOs

### Task 1: Add PBAP DTOs to ApiModels.cs

**Files:**
- Modify: `src/Radio.Web/Models/ApiModels.cs:878` (append after last line)

- [ ] **Step 1: Add PBAP DTOs**

Append these DTOs at the end of `ApiModels.cs` (after the `CallHistoryEntryDto` class). These mirror the API response shapes from `PbapController`.

```csharp
// PBAP DTOs

public class PbapSyncResultDto
{
  public bool Success { get; set; }
  public int ContactCount { get; set; }
  public string? ErrorMessage { get; set; }
}

public class PbapSyncStatusDto
{
  public List<PbapDeviceSyncInfoDto> Devices { get; set; } = [];
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
  public List<string> PhoneNumbers { get; set; } = [];
}
```

- [ ] **Step 2: Build to verify DTOs compile**

Run: `dotnet build src/Radio.Web --configuration Release --verbosity quiet`
Expected: Build succeeded. 0 Warning(s).

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/Models/ApiModels.cs
git commit -m "feat: add PBAP DTOs to Web ApiModels"
```

---

### Task 2: Create PbapApiService

**Files:**
- Create: `src/Radio.Web/Services/ApiClients/PbapApiService.cs`

This follows the exact same pattern as `BluetoothApiService` and `IntegrationsApiService`: constructor injects `HttpClient` + `ILogger<T>`, static `JsonSerializerOptions`, try-catch with logging, `CancellationToken ct = default`.

- [ ] **Step 1: Create PbapApiService**

Create `src/Radio.Web/Services/ApiClients/PbapApiService.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for PBAP (Phone Book Access Profile) endpoints
/// </summary>
public class PbapApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<PbapApiService> _logger;

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public PbapApiService(HttpClient httpClient, ILogger<PbapApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<PbapSyncResultDto?> SyncContactsAsync(string deviceAddress, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync(
        $"/api/bluetooth/pbap/sync?deviceAddress={Uri.EscapeDataString(deviceAddress)}", null, ct);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PbapSyncResultDto>(JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to sync PBAP contacts for {Address}", deviceAddress);
      return null;
    }
  }

  public async Task<List<PbapContactDto>?> GetContactsAsync(string deviceAddress, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<PbapContactDto>>(
        $"/api/bluetooth/pbap/contacts?deviceAddress={Uri.EscapeDataString(deviceAddress)}", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get PBAP contacts for {Address}", deviceAddress);
      return null;
    }
  }

  public async Task<PbapSyncStatusDto?> GetSyncStatusAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PbapSyncStatusDto>(
        "/api/bluetooth/pbap/status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get PBAP sync status");
      return null;
    }
  }
}
```

- [ ] **Step 2: Build to verify service compiles**

Run: `dotnet build src/Radio.Web --configuration Release --verbosity quiet`
Expected: Build succeeded. 0 Warning(s).

- [ ] **Step 3: Register PbapApiService in DI**

In `src/Radio.Web/Program.cs`, find the `IntegrationsApiService` registration block (around line 253):

```csharp
builder.Services.AddHttpClient<IntegrationsApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
```

Add the `PbapApiService` registration directly after it, following the same pattern:

```csharp
builder.Services.AddHttpClient<PbapApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(60); // PBAP sync can take a while
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});
```

Note: 60-second timeout because PBAP sync downloads the full phonebook which can take 30+ seconds for large contact lists.

- [ ] **Step 4: Build to verify DI registration**

Run: `dotnet build src/Radio.Web --configuration Release --verbosity quiet`
Expected: Build succeeded. 0 Warning(s).

- [ ] **Step 5: Commit**

```bash
git add src/Radio.Web/Services/ApiClients/PbapApiService.cs src/Radio.Web/Program.cs
git commit -m "feat: add PbapApiService web client with DI registration"
```

---

## Chunk 2: BluetoothPage — Expanded Connected Device Card

### Task 3: Add service status fields and data fetching to BluetoothPage

**Files:**
- Modify: `src/Radio.Web/Components/Pages/BluetoothPage.razor` (code-behind section, ~lines 305-601)

The BluetoothPage needs to fetch two additional API calls on load/refresh: phone integration status and PBAP sync status. It also needs to know the current audio source to determine A2DP status.

- [ ] **Step 1: Add service injections**

At the top of BluetoothPage.razor (around line 3-4), add the new service injections after the existing ones:

```razor
@inject IntegrationsApiService IntegrationsApi
@inject PbapApiService PbapApi
@inject SourcesApiService SourcesApi
```

Add the corresponding `using` if not already present:

```razor
@using Radio.Web.Services.ApiClients
```

- [ ] **Step 2: Add service status fields**

In the code-behind `@code` block (around line 306-310 where `_status`, `_isLoading`, etc. are declared), add:

```csharp
  private PhoneIntegrationStatusDto? _phoneStatus;
  private PbapSyncStatusDto? _pbapStatus;
  private string? _activeSourceType;
```

- [ ] **Step 3: Extend RefreshStatusAsync to fetch service data**

In `RefreshStatusAsync()` (around line 312-336), after the existing `_status = await BluetoothApi.GetStatusAsync()` call, add parallel fetches for the other two services. The key is to fetch all three in parallel when a device is connected:

Find the line that sets `_status`:
```csharp
_status = await BluetoothApi.GetStatusAsync();
```

After it (but still within the try block), add:

```csharp
      // Fetch service statuses in parallel when device is connected
      if (_status?.ConnectedDevice != null)
      {
        var phoneTask = IntegrationsApi.GetPhoneStatusAsync();
        var pbapTask = PbapApi.GetSyncStatusAsync();
        var sourcesTask = SourcesApi.GetSourcesAsync();
        await Task.WhenAll(phoneTask, pbapTask, sourcesTask);
        _phoneStatus = phoneTask.Result;
        _pbapStatus = pbapTask.Result;
        var sources = sourcesTask.Result;
        _activeSourceType = sources?.FirstOrDefault(s => s.State == "Active")?.Type;
      }
      else
      {
        _phoneStatus = null;
        _pbapStatus = null;
        _activeSourceType = null;
      }
```

- [ ] **Step 4: Add helper methods for service badge data**

Add these helper methods in the `@code` block (near the bottom, before the closing brace):

```csharp
  // --- Service badge helpers ---

  private bool IsA2dpActive => _status?.ConnectedDevice != null
    && string.Equals(_activeSourceType, "Bluetooth", StringComparison.OrdinalIgnoreCase);

  private string A2dpStatusText => IsA2dpActive ? "Playing" : "Idle";

  private bool IsHfpActive => _phoneStatus is { Enabled: true, IsConnected: true };

  private string HfpStatusText => IsHfpActive
    ? "Connected to RotaryPhone"
    : (_phoneStatus?.Enabled == true ? "Disconnected" : "Not configured");

  private PbapDeviceSyncInfoDto? ConnectedDevicePbapInfo =>
    _pbapStatus?.Devices.FirstOrDefault(d =>
      string.Equals(d.DeviceAddress, _status?.ConnectedDevice?.Address, StringComparison.OrdinalIgnoreCase));

  private bool HasPbapSync => ConnectedDevicePbapInfo != null;

  private string PbapStatusText
  {
    get
    {
      var info = ConnectedDevicePbapInfo;
      if (info == null) return "Not synced";
      var ago = info.LastSynced.HasValue
        ? FormatTimeAgo(info.LastSynced.Value)
        : "unknown";
      return $"{info.ContactCount} synced ({ago})";
    }
  }

  private static string FormatTimeAgo(DateTime utcTime)
  {
    var span = DateTime.UtcNow - utcTime;
    if (span.TotalMinutes < 1) return "just now";
    if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
    if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
    return $"{(int)span.TotalDays}d ago";
  }
```

- [ ] **Step 5: Build to verify code compiles**

Run: `dotnet build src/Radio.Web --configuration Release --verbosity quiet`
Expected: Build succeeded. 0 Warning(s).

- [ ] **Step 6: Commit**

```bash
git add src/Radio.Web/Components/Pages/BluetoothPage.razor
git commit -m "feat: add service status fetching to BluetoothPage code-behind"
```

---

### Task 4: Add service badges and context-aware disconnect UI

**Files:**
- Modify: `src/Radio.Web/Components/Pages/BluetoothPage.razor` (markup section, lines 76-129)

Replace the existing connected-device card content with the expanded version showing service badges, detail rows, and context-aware disconnect.

- [ ] **Step 1: Replace connected-device card markup**

Find the existing connected device card (lines 76-129). Replace the content inside `<RadzenCard Style="padding:16px">` (keeping the RadzenCard wrapper). Replace the inner content — from `<div style="display:flex; flex-direction:column; gap:8px">` through its closing `</div>` and the closing `</RadzenCard>` — with this expanded version:

```razor
      <RadzenCard Style="padding:16px">
        <div style="display:flex; flex-direction:column; gap:8px">
          <h6>Connected Device</h6>

          @if (_status.ConnectedDevice != null)
          {
            @* Device header row *@
            <div style="display:flex; flex-direction:row; gap:12px; align-items:center">
              <RadzenIcon Icon="bluetooth_connected" Style="font-size:2rem; color:var(--rz-success)" />
              <div style="display:flex; flex-direction:column; gap:0">
                <span style="font-size:1rem; font-weight:600">@_status.ConnectedDevice.Name</span>
                <span style="font-size:0.75rem; color:var(--text-low)">@_status.ConnectedDevice.Address</span>
              </div>
              <div style="flex:1"></div>
              <RadzenBadge Text="Connected" BadgeStyle="BadgeStyle.Success" IsPill="true" />
            </div>

            @* Service badges *@
            <div style="display:flex; gap:6px; flex-wrap:wrap; margin-top:4px">
              <RadzenBadge Text="@($"🎵 A2DP {A2dpStatusText}")"
                BadgeStyle="@(IsA2dpActive ? BadgeStyle.Info : BadgeStyle.Light)" IsPill="true" />
              <RadzenBadge Text="@($"📞 HFP {(IsHfpActive ? "Active" : "Idle")}")"
                BadgeStyle="@(IsHfpActive ? BadgeStyle.Success : BadgeStyle.Light)" IsPill="true" />
              <RadzenBadge Text="@($"📇 PBAP {(HasPbapSync ? "Synced" : "None")}")"
                BadgeStyle="@(HasPbapSync ? BadgeStyle.Success : BadgeStyle.Light)" IsPill="true" />
            </div>

            @* Service detail rows *@
            <div style="background:var(--rz-base-800); border-radius:6px; padding:10px; margin-top:4px; font-size:0.8rem">
              <div style="display:flex; justify-content:space-between; margin-bottom:4px">
                <span style="color:var(--rz-info)">Audio Streaming</span>
                <span style="color:var(--text-low)">@A2dpStatusText</span>
              </div>
              <div style="display:flex; justify-content:space-between; margin-bottom:4px">
                <span style="color:var(--rz-success)">Phone Integration</span>
                <span style="color:var(--text-low)">@HfpStatusText</span>
              </div>
              <div style="display:flex; justify-content:space-between">
                <span style="color:var(--rz-warning-light, #80cbc4)">Contacts</span>
                <span style="color:var(--text-low)">@PbapStatusText</span>
              </div>
            </div>

            @* Context-aware disconnect *@
            <div style="background:var(--rz-danger-lighter, #3e1010); border:1px solid var(--rz-danger-light, #5c2020); border-radius:6px; padding:10px; margin-top:4px">
              <div style="display:flex; justify-content:space-between; align-items:center">
                <div>
                  <div style="color:var(--rz-danger); font-size:0.85rem; font-weight:500">Disconnect Device</div>
                  <div style="color:var(--text-low); font-size:0.7rem; margin-top:2px">Stops audio, phone, and all BT services</div>
                </div>
                <RadzenButton Variant="Variant.Filled" ButtonStyle="ButtonStyle.Danger" Size="ButtonSize.Small"
                              Click="DisconnectAsync" Icon="link_off"
                              Disabled="@_isDisconnecting">
                  @if (_isDisconnecting)
                  {
                    <RadzenProgressBarCircular Size="ProgressBarCircularSize.Small" Mode="ProgressBarMode.Indeterminate" Style="margin-right:4px" />
                    <span>Disconnecting</span>
                  }
                  else
                  {
                    <span>Disconnect</span>
                  }
                </RadzenButton>
              </div>
            </div>
          }
          else if (_status.IsReconnecting)
          {
            <div style="display:flex; flex-direction:row; gap:12px; align-items:center">
              <RadzenProgressBarCircular Size="ProgressBarCircularSize.Small" Mode="ProgressBarMode.Indeterminate" />
              <span style="font-size:0.875rem">Reconnecting to last device...</span>
              <div style="flex:1"></div>
              <RadzenButton Variant="Variant.Outlined" ButtonStyle="ButtonStyle.Warning" Size="ButtonSize.Small"
                            Click="CancelReconnectAsync" Icon="stop" Text="Stop" />
            </div>
          }
          else
          {
            <RadzenAlert AlertStyle="AlertStyle.Info" Size="AlertSize.Small" AllowClose="false">
              @if (!string.IsNullOrEmpty(_status.LastDisconnectReason))
              {
                @GetDisconnectMessage(_status.LastDisconnectReason)
              }
              else
              {
                <span>No device connected. Pair a device below, or connect a previously paired device.</span>
              }
            </RadzenAlert>
          }
        </div>
      </RadzenCard>
```

- [ ] **Step 2: Build to verify markup compiles**

Run: `dotnet build src/Radio.Web --configuration Release --verbosity quiet`
Expected: Build succeeded. 0 Warning(s).

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/Components/Pages/BluetoothPage.razor
git commit -m "feat: add service badges and context-aware disconnect to BluetoothPage"
```

---

## Chunk 3: PhonePage — PBAP Sync & Merged Contacts

### Task 5: Add PBAP service injection and state fields to PhonePage

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor` (code-behind, lines 227-448)

- [ ] **Step 1: Add service injections**

At the top of PhonePage.razor (around line 3-4), add after the existing `@inject PhoneApiService PhoneApi`:

```razor
@inject PbapApiService PbapApi
@inject BluetoothApiService BluetoothApi
```

- [ ] **Step 2: Add PBAP state fields**

In the `@code` block (around lines 228-237 where the private fields are), add:

```csharp
  private PbapSyncStatusDto? _pbapStatus;
  private BluetoothStatusDto? _btStatus;
  private List<PbapContactDto>? _pbapContacts;
  private bool _isSyncing;
  private bool _showSyncDropdown;
```

- [ ] **Step 3: Add a merged contact model**

Add this helper record inside the `@code` block (near the bottom):

```csharp
  private record MergedContact(string? Id, string Name, string Phone, string? Email, string Source);

  private List<MergedContact> MergedContacts
  {
    get
    {
      var merged = new List<MergedContact>();

      // RotaryPhone contacts (manual)
      if (_contacts != null)
      {
        foreach (var c in _contacts)
          merged.Add(new MergedContact(c.Id, c.Name, c.PhoneNumber, c.Email, "Manual"));
      }

      // PBAP contacts
      if (_pbapContacts != null)
      {
        foreach (var c in _pbapContacts)
        {
          var phone = c.PhoneNumbers.FirstOrDefault() ?? "";
          merged.Add(new MergedContact(null, c.DisplayName, phone, null, "PBAP"));
        }
      }

      return merged.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
  }
```

- [ ] **Step 4: Fetch BT status and PBAP status on init**

In `OnInitializedAsync()` (around line 239-257), add these calls to the existing initialization. Find where `_contacts` is loaded (likely `RefreshContactsAsync` or similar) and add after it:

```csharp
      // Fetch BT and PBAP status for sync UI
      var btTask = BluetoothApi.GetStatusAsync();
      var pbapTask = PbapApi.GetSyncStatusAsync();
      await Task.WhenAll(btTask, pbapTask);
      _btStatus = btTask.Result;
      _pbapStatus = pbapTask.Result;

      // Load PBAP contacts if a device is connected
      if (_btStatus?.ConnectedDevice != null)
      {
        _pbapContacts = await PbapApi.GetContactsAsync(_btStatus.ConnectedDevice.Address);
      }
```

- [ ] **Step 5: Add sync handler method**

Add this method in the `@code` block:

```csharp
  private async Task SyncFromDeviceAsync(string deviceAddress)
  {
    _showSyncDropdown = false;
    _isSyncing = true;
    StateHasChanged();

    try
    {
      var result = await PbapApi.SyncContactsAsync(deviceAddress);
      if (result?.Success == true)
      {
        // Refresh PBAP contacts and status
        _pbapContacts = await PbapApi.GetContactsAsync(deviceAddress);
        _pbapStatus = await PbapApi.GetSyncStatusAsync();
        NotificationService.Notify(NotificationSeverity.Success, "Sync Complete",
          $"Synced {result.ContactCount} contacts");
      }
      else
      {
        NotificationService.Notify(NotificationSeverity.Error, "Sync Failed",
          result?.ErrorMessage ?? "Unknown error");
      }
    }
    catch (Exception ex)
    {
      NotificationService.Notify(NotificationSeverity.Error, "Sync Error", ex.Message);
    }
    finally
    {
      _isSyncing = false;
      StateHasChanged();
    }
  }
```

Note: PhonePage injects `NotificationService NotificationService` at line 4. The code above uses that name.

- [ ] **Step 6: Add helper for connected device sync info**

```csharp
  private PbapDeviceSyncInfoDto? ConnectedDeviceSyncInfo =>
    _pbapStatus?.Devices.FirstOrDefault(d =>
      string.Equals(d.DeviceAddress, _btStatus?.ConnectedDevice?.Address, StringComparison.OrdinalIgnoreCase));

  private static string FormatTimeAgo(DateTime utcTime)
  {
    var span = DateTime.UtcNow - utcTime;
    if (span.TotalMinutes < 1) return "just now";
    if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
    if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
    return $"{(int)span.TotalDays}d ago";
  }
```

- [ ] **Step 7: Build to verify code compiles**

Run: `dotnet build src/Radio.Web --configuration Release --verbosity quiet`
Expected: Build succeeded. 0 Warning(s).

- [ ] **Step 8: Commit**

```bash
git add src/Radio.Web/Components/Pages/PhonePage.razor
git commit -m "feat: add PBAP state fields and sync handler to PhonePage code-behind"
```

---

### Task 6: Update PhonePage Contacts tab markup

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor` (lines 149-180)

Replace the Contacts tab content with the PBAP sync dropdown, status bar, and merged contact grid with source column.

- [ ] **Step 1: Replace Contacts tab markup**

Find the Contacts tab (lines 149-180, the `<RadzenTabsItem Text="Contacts" Icon="contacts">` block). Replace its entire content with:

```razor
      @* ═══ Tab 2: Contacts ═══ *@
      <RadzenTabsItem Text="Contacts" Icon="contacts">
        <div class="phone-contacts">
          @* Action bar: Sync dropdown + Add Contact *@
          <div class="contacts-header">
            <h3 class="card-title">Contacts</h3>
            <div style="display:flex; gap:8px; align-items:center; position:relative">
              @* Sync from Phone dropdown *@
              <div style="position:relative">
                <RadzenButton Text="@(_isSyncing ? "Syncing..." : "Sync from Phone")"
                  Icon="@(_isSyncing ? "" : "smartphone")"
                  Click="@(() => _showSyncDropdown = !_showSyncDropdown)"
                  ButtonStyle="ButtonStyle.Info" Size="ButtonSize.Small"
                  Disabled="@_isSyncing">
                  @if (_isSyncing)
                  {
                    <RadzenProgressBarCircular Size="ProgressBarCircularSize.Small"
                      Mode="ProgressBarMode.Indeterminate" Style="margin-right:4px" />
                  }
                </RadzenButton>
                @if (_showSyncDropdown && _btStatus != null)
                {
                  @* Click-away overlay *@
                  <div style="position:fixed; top:0; left:0; right:0; bottom:0; z-index:99"
                       @onclick="@(() => _showSyncDropdown = false)"
                       @onclick:stopPropagation="true"></div>
                  <div style="position:absolute; top:100%; right:0; margin-top:4px; background:var(--rz-base-800);
                              border:1px solid var(--rz-base-600); border-radius:6px; padding:4px;
                              min-width:240px; z-index:100; box-shadow:0 4px 12px rgba(0,0,0,0.5)">
                    <div style="padding:6px 12px; font-size:0.75rem; color:var(--text-low);
                                border-bottom:1px solid var(--rz-base-700)">Select device to sync:</div>
                    @if (_btStatus.ConnectedDevice != null)
                    {
                      <div style="padding:8px 12px; font-size:0.85rem; cursor:pointer; border-radius:4px;
                                  background:var(--rz-info-lighter, rgba(21,101,192,0.2))"
                           @onclick="@(() => SyncFromDeviceAsync(_btStatus.ConnectedDevice.Address))"
                           @onclick:stopPropagation="true">
                        📱 @_btStatus.ConnectedDevice.Name
                        <span style="color:var(--rz-success); font-size:0.7rem">(connected)</span>
                      </div>
                    }
                    @foreach (var device in (_btStatus.PairedDevices ?? []).Where(d => !d.IsConnected))
                    {
                      <div style="padding:8px 12px; font-size:0.85rem; color:var(--text-low);
                                  cursor:pointer; border-radius:4px"
                           @onclick="@(() => SyncFromDeviceAsync(device.Address))"
                           @onclick:stopPropagation="true">
                        📱 @device.Name
                        <span style="font-size:0.7rem">(paired)</span>
                      </div>
                    }
                  </div>
                }
              </div>
              <RadzenButton Text="Add Contact" Icon="person_add"
                Click="@OpenAddContactDialog"
                ButtonStyle="ButtonStyle.Primary" Size="ButtonSize.Small" />
            </div>
          </div>

          @* Sync status bar *@
          @if (ConnectedDeviceSyncInfo != null)
          {
            var syncInfo = ConnectedDeviceSyncInfo;
            <div style="background:var(--rz-base-800); border-radius:4px; padding:8px 12px;
                        display:flex; justify-content:space-between; align-items:center; font-size:0.8rem; margin-bottom:8px">
              <span style="color:var(--text-low)">
                Last synced from @(syncInfo.DeviceName ?? syncInfo.DeviceAddress):
                @(syncInfo.LastSynced.HasValue ? FormatTimeAgo(syncInfo.LastSynced.Value) : "never")
                — @syncInfo.ContactCount contacts
              </span>
              <RadzenBadge Text="@(syncInfo.IsStale ? "Stale" : "Fresh")"
                BadgeStyle="@(syncInfo.IsStale ? BadgeStyle.Warning : BadgeStyle.Success)"
                IsPill="true" />
            </div>
          }

          @* Merged contacts grid *@
          <RadzenDataGrid Data="@MergedContacts" TItem="MergedContact"
            AllowSorting="true" AllowFiltering="true" FilterMode="FilterMode.Simple"
            class="contacts-grid">
            <Columns>
              <RadzenDataGridColumn TItem="MergedContact" Property="Name" Title="Name" Width="200px" />
              <RadzenDataGridColumn TItem="MergedContact" Property="Phone" Title="Phone" Width="160px">
                <Template Context="contact">
                  <span class="phone-number">@contact.Phone</span>
                </Template>
              </RadzenDataGridColumn>
              <RadzenDataGridColumn TItem="MergedContact" Property="Email" Title="Email" />
              <RadzenDataGridColumn TItem="MergedContact" Property="Source" Title="Source" Width="100px"
                Sortable="true" Filterable="true">
                <Template Context="contact">
                  @if (contact.Source == "PBAP")
                  {
                    <RadzenBadge Text="📱 PBAP" BadgeStyle="BadgeStyle.Info" IsPill="true"
                      Style="font-size:0.7rem" />
                  }
                  else
                  {
                    <RadzenBadge Text="✏️ Manual" BadgeStyle="BadgeStyle.Light" IsPill="true"
                      Style="font-size:0.7rem" />
                  }
                </Template>
              </RadzenDataGridColumn>
              <RadzenDataGridColumn TItem="MergedContact" Title="Actions" Width="100px"
                Sortable="false" Filterable="false">
                <Template Context="contact">
                  @if (contact.Source == "Manual" && contact.Id != null)
                  {
                    var original = _contacts?.FirstOrDefault(c => c.Id == contact.Id);
                    @if (original != null)
                    {
                      <RadzenButton Icon="edit" ButtonStyle="ButtonStyle.Light" Size="ButtonSize.Small"
                        Click="@(() => OpenEditContactDialog(original))"
                        class="action-btn" />
                      <RadzenButton Icon="delete" ButtonStyle="ButtonStyle.Danger" Size="ButtonSize.Small"
                        Click="@(() => DeleteContactAsync(original))"
                        class="action-btn" />
                    }
                  }
                </Template>
              </RadzenDataGridColumn>
            </Columns>
          </RadzenDataGrid>
        </div>
      </RadzenTabsItem>
```

- [ ] **Step 2: Build to verify markup compiles**

Run: `dotnet build src/Radio.Web --configuration Release --verbosity quiet`
Expected: Build succeeded. 0 Warning(s).

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/Components/Pages/PhonePage.razor
git commit -m "feat: add PBAP sync dropdown, status bar, and merged contacts to PhonePage"
```

---

## Chunk 4: Tests & Verification

### Task 7: Update PhonePage bUnit tests

**Files:**
- Modify: `tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs`

The PhonePage now injects `PbapApiService` and `BluetoothApiService`, so the test DI container needs to register them.

- [ ] **Step 1: Add PbapApiService and BluetoothApiService to test DI**

In the `PhonePageTests` constructor (lines 15-36), after the `PhoneApiService` registration, add:

```csharp
    // Register PbapApiService with mock handler
    Services.AddHttpClient<PbapApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5000");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // Register BluetoothApiService with mock handler
    Services.AddHttpClient<BluetoothApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5000");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());
```

- [ ] **Step 2: Update EmptyResponseHandler for PBAP endpoints**

In the `EmptyResponseHandler.SendAsync` method (lines 76-89), add cases for the PBAP and BT endpoints:

```csharp
        var p when p.Contains("/api/bluetooth/pbap/status") =>
          """{"devices":[]}""",
        var p when p.Contains("/api/bluetooth/pbap/contacts") => "[]",
        var p when p.Contains("/api/bluetooth/status") =>
          """{"isAvailable":true,"state":"Powered","isDiscovering":false,"pairedDevices":[],"discoveredDevices":[]}""",
```

These should be added before the `_ => "{}"` fallback line.

- [ ] **Step 3: Add test for merged contacts grid with source column**

```csharp
  [Fact]
  public void PhonePage_ContactsTab_Renders_SourceColumn()
  {
    var cut = RenderComponent<PhonePage>();
    // The contacts grid should have a Source column header
    Assert.Contains("Source", cut.Markup);
  }

  [Fact]
  public void PhonePage_ContactsTab_Renders_SyncButton()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Sync from Phone", cut.Markup);
  }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release --verbosity normal --filter "PhonePage"`
Expected: All PhonePage tests pass.

- [ ] **Step 5: Commit**

```bash
git add tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs
git commit -m "test: update PhonePage tests for PBAP sync and merged contacts"
```

---

### Task 8: Build verification and full test run

- [ ] **Step 1: Full solution build**

Run: `dotnet build --configuration Release`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 2: Run all tests**

Run: `dotnet test --configuration Release --verbosity normal`
Expected: All ~1,697 tests pass (or match baseline — known flaky tests are AudioApiService timeout tests).

- [ ] **Step 3: Final commit if any fixes needed**

If build/test issues were found and fixed, commit with appropriate message.

---

## Summary

| Task | What | Files |
|------|------|-------|
| 1 | PBAP DTOs | `ApiModels.cs` |
| 2 | PbapApiService + DI | `PbapApiService.cs`, `Program.cs` |
| 3 | BT page code-behind (service fetching) | `BluetoothPage.razor` |
| 4 | BT page markup (badges, disconnect) | `BluetoothPage.razor` |
| 5 | Phone page code-behind (PBAP state) | `PhonePage.razor` |
| 6 | Phone page markup (sync UI, grid) | `PhonePage.razor` |
| 7 | bUnit test updates | `PhonePageTests.cs` |
| 8 | Build & test verification | — |
