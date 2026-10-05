# Rotary Phone UI Integration (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Phone page to Radio.Web that displays RotaryPhone call state, contacts, and call history by connecting to RotaryPhone.API at `http://localhost:5004`.

**Architecture:** New `PhoneApiService` talks to RotaryPhone.API over HTTP. A SignalR client connects to RotaryPhone.API's hub for real-time call state updates. PhonePage.razor renders 3 tabs (Dashboard/Contacts/History). A phone icon in MainLayout.razor's nav bar shows live call state and links to the page.

**Tech Stack:** Blazor Server, Radzen components (material-dark theme), SignalR client, HttpClient

**Design spec:** `docs/superpowers/specs/2026-03-11-rotary-phone-integration-design.md`

---

## File Map

### New Files

| File | Responsibility |
|------|---------------|
| `src/Radio.Web/Services/ApiClients/PhoneApiService.cs` | HTTP client for RotaryPhone.API (`/api/phone/*`, `/api/contacts/*`, `/api/callhistory/*`) |
| `src/Radio.Web/Services/Hub/PhoneHubService.cs` | SignalR client connecting to RotaryPhone.API hub at `http://localhost:5004/hub` |
| `src/Radio.Web/Components/Pages/PhonePage.razor` | Phone page with 3 tabs: Dashboard, Contacts, Call History |
| `tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs` | bUnit tests for PhonePage |
| `tests/Radio.Web.Tests/Services/PhoneApiServiceTests.cs` | Unit tests for PhoneApiService |

### Modified Files

| File | Changes |
|------|---------|
| `src/Radio.Web/Models/ApiModels.cs` | Add DTOs: `PhoneSystemStatusDto`, `PhoneCallStateDto`, `ContactDto`, `CallHistoryEntryDto` |
| `src/Radio.Web/Components/Layout/MainLayout.razor` | Add phone nav icon between System and Sleep |
| `src/Radio.Web/Program.cs` | Register `PhoneApiService` HttpClient (base URL `http://localhost:5004`) and `PhoneHubService` |
| `src/Radio.Web/Components/Pages/SystemConfigPage.razor` | Add Phone configuration section (HT801 IP, SIP port, RTP port) |

---

## Chunk 1: DTOs and API Service

### Task 1: Add Phone DTOs to ApiModels.cs

**Files:**
- Modify: `src/Radio.Web/Models/ApiModels.cs` (append after line 816)

- [ ] **Step 1: Add Phone DTOs**

Add these DTOs at the end of `ApiModels.cs`, after the existing `PhoneIntegrationConfigDto`:

```csharp
// RotaryPhone.API DTOs (calls http://localhost:5004)

public class PhoneSystemStatusDto
{
  public string Platform { get; set; } = "";
  public bool IsRaspberryPi { get; set; }
  public bool BluetoothEnabled { get; set; }
  public bool BluetoothConnected { get; set; }
  public string? BluetoothDeviceAddress { get; set; }
  public bool SipListening { get; set; }
  public string? SipListenAddress { get; set; }
  public int SipPort { get; set; }
  public string? Ht801IpAddress { get; set; }
  public bool? Ht801Reachable { get; set; }
}

public class PhoneCallStateDto
{
  public string CallState { get; set; } = "Idle";
  public string? DialedNumber { get; set; }
  public string? IncomingNumber { get; set; }
  public string? CallerName { get; set; }
  public string? Duration { get; set; }
}

public record ContactDto
{
  public string Id { get; init; } = "";
  public string Name { get; init; } = "";
  public string PhoneNumber { get; init; } = "";
  public string? Email { get; init; }
  public string? Notes { get; init; }
  public DateTime CreatedAt { get; init; }
  public DateTime ModifiedAt { get; init; }
}

public class ContactFormDto
{
  public string Name { get; set; } = "";
  public string PhoneNumber { get; set; } = "";
  public string? Email { get; set; }
}

public class CallHistoryEntryDto
{
  public string? Id { get; set; }
  public DateTime StartTime { get; set; }
  public DateTime? EndTime { get; set; }
  public string? Duration { get; set; }
  public string Direction { get; set; } = "Incoming";
  public string PhoneNumber { get; set; } = "";
  public string? AnsweredOn { get; set; }
  public string? PhoneId { get; set; }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Radio.Web --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/Models/ApiModels.cs
git commit -m "feat: add RotaryPhone DTOs for phone integration UI"
```

---

### Task 2: Create PhoneApiService

**Files:**
- Create: `src/Radio.Web/Services/ApiClients/PhoneApiService.cs`
- Test: `tests/Radio.Web.Tests/Services/PhoneApiServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Radio.Web.Tests/Services/PhoneApiServiceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

public class PhoneApiServiceTests
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  private PhoneApiService CreateService(HttpClient httpClient)
  {
    return new PhoneApiService(httpClient, NullLogger<PhoneApiService>.Instance);
  }

  [Fact]
  public async Task GetSystemStatusAsync_ReturnsStatus_WhenApiAvailable()
  {
    var expected = new PhoneSystemStatusDto
    {
      Platform = "Linux",
      BluetoothConnected = true,
      SipListening = true,
      Ht801Reachable = true
    };
    var handler = new MockHttpHandler(JsonSerializer.Serialize(expected, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetSystemStatusAsync();

    Assert.NotNull(result);
    Assert.Equal("Linux", result.Platform);
    Assert.True(result.BluetoothConnected);
  }

  [Fact]
  public async Task GetSystemStatusAsync_ReturnsNull_WhenApiUnavailable()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.InternalServerError);
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetSystemStatusAsync();

    Assert.Null(result);
  }

  [Fact]
  public async Task GetCallStateAsync_ReturnsState()
  {
    var expected = new PhoneCallStateDto { CallState = "Ringing", IncomingNumber = "+15551234567" };
    var handler = new MockHttpHandler(JsonSerializer.Serialize(expected, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetCallStateAsync();

    Assert.NotNull(result);
    Assert.Equal("Ringing", result.CallState);
    Assert.Equal("+15551234567", result.IncomingNumber);
  }

  [Fact]
  public async Task GetContactsAsync_ReturnsList()
  {
    var expected = new List<ContactDto>
    {
      new() { Id = "1", Name = "Alice", PhoneNumber = "555-1234" }
    };
    var handler = new MockHttpHandler(JsonSerializer.Serialize(expected, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetContactsAsync();

    Assert.NotNull(result);
    Assert.Single(result);
    Assert.Equal("Alice", result[0].Name);
  }

  [Fact]
  public async Task GetCallHistoryAsync_ReturnsList()
  {
    var expected = new List<CallHistoryEntryDto>
    {
      new() { Direction = "Incoming", PhoneNumber = "555-9876", AnsweredOn = "RotaryPhone" }
    };
    var handler = new MockHttpHandler(JsonSerializer.Serialize(expected, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetCallHistoryAsync();

    Assert.NotNull(result);
    Assert.Single(result);
    Assert.Equal("RotaryPhone", result[0].AnsweredOn);
  }

  [Fact]
  public async Task IsAvailableAsync_ReturnsTrue_WhenApiReachable()
  {
    var handler = new MockHttpHandler(JsonSerializer.Serialize(
      new PhoneSystemStatusDto { Platform = "Linux" }, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.IsAvailableAsync();

    Assert.True(result);
  }

  [Fact]
  public async Task IsAvailableAsync_ReturnsFalse_WhenApiUnreachable()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.InternalServerError);
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.IsAvailableAsync();

    Assert.False(result);
  }

  /// <summary>
  /// Simple mock HTTP handler for testing API service methods.
  /// </summary>
  private class MockHttpHandler : HttpMessageHandler
  {
    private readonly string? _responseContent;
    private readonly HttpStatusCode _statusCode;

    public MockHttpHandler(string? responseContent = null, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
      _responseContent = responseContent;
      _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var response = new HttpResponseMessage(_statusCode);
      if (_responseContent != null)
      {
        response.Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json");
      }
      return Task.FromResult(response);
    }
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Radio.Web.Tests --filter "FullyQualifiedName~PhoneApiServiceTests" --configuration Release`
Expected: Build error — `PhoneApiService` class not found

- [ ] **Step 3: Implement PhoneApiService**

Create `src/Radio.Web/Services/ApiClients/PhoneApiService.cs`:

```csharp
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// HTTP client for RotaryPhone.API at http://localhost:5004.
/// Provides access to phone status, contacts, and call history.
/// </summary>
public class PhoneApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<PhoneApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public PhoneApiService(HttpClient httpClient, ILogger<PhoneApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  // Health check

  public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
  {
    try
    {
      var status = await _httpClient.GetFromJsonAsync<PhoneSystemStatusDto>(
        "/api/phone/system-status", JsonOptions, ct);
      return status != null;
    }
    catch
    {
      return false;
    }
  }

  // Phone status

  public async Task<PhoneSystemStatusDto?> GetSystemStatusAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PhoneSystemStatusDto>(
        "/api/phone/system-status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get phone system status");
      return null;
    }
  }

  public async Task<PhoneCallStateDto?> GetCallStateAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PhoneCallStateDto>(
        "/api/phone/status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get phone call state");
      return null;
    }
  }

  // Simulate controls (developer tools)

  public async Task<bool> SimulateHookAsync(bool offHook, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync(
        $"/api/phone/simulate/hook?offHook={offHook}", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to simulate hook state");
      return false;
    }
  }

  public async Task<bool> SimulateIncomingCallAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/phone/simulate/incoming", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to simulate incoming call");
      return false;
    }
  }

  public async Task<bool> SimulateDialAsync(string digits, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync(
        $"/api/phone/simulate/dial?digits={Uri.EscapeDataString(digits)}", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to simulate dial");
      return false;
    }
  }

  // Contacts

  public async Task<List<ContactDto>?> GetContactsAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<ContactDto>>(
        "/api/contacts", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get contacts");
      return null;
    }
  }

  public async Task<bool> CreateContactAsync(ContactFormDto contact, CancellationToken ct = default)
  {
    try
    {
      var content = new StringContent(
        JsonSerializer.Serialize(contact), Encoding.UTF8, "application/json");
      var response = await _httpClient.PostAsync("/api/contacts", content, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create contact");
      return false;
    }
  }

  public async Task<bool> UpdateContactAsync(string id, ContactFormDto contact, CancellationToken ct = default)
  {
    try
    {
      var content = new StringContent(
        JsonSerializer.Serialize(contact), Encoding.UTF8, "application/json");
      var response = await _httpClient.PutAsync($"/api/contacts/{id}", content, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to update contact {Id}", id);
      return false;
    }
  }

  public async Task<bool> DeleteContactAsync(string id, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/contacts/{id}", ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete contact {Id}", id);
      return false;
    }
  }

  // Call History

  public async Task<List<CallHistoryEntryDto>?> GetCallHistoryAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<CallHistoryEntryDto>>(
        "/api/callhistory", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get call history");
      return null;
    }
  }

  public async Task<bool> ClearCallHistoryAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync("/api/callhistory", ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to clear call history");
      return false;
    }
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Radio.Web.Tests --filter "FullyQualifiedName~PhoneApiServiceTests" --configuration Release`
Expected: 7/7 PASS

- [ ] **Step 5: Commit**

```bash
git add src/Radio.Web/Services/ApiClients/PhoneApiService.cs tests/Radio.Web.Tests/Services/PhoneApiServiceTests.cs
git commit -m "feat: add PhoneApiService for RotaryPhone.API communication"
```

---

### Task 3: Create PhoneHubService

**Files:**
- Create: `src/Radio.Web/Services/Hub/PhoneHubService.cs`

- [ ] **Step 1: Implement PhoneHubService**

Create `src/Radio.Web/Services/Hub/PhoneHubService.cs`:

```csharp
using Microsoft.AspNetCore.SignalR.Client;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR client that connects to RotaryPhone.API's hub for real-time
/// call state, incoming call, and history update notifications.
/// </summary>
public class PhoneHubService : IAsyncDisposable
{
  private readonly ILogger<PhoneHubService> _logger;
  private readonly IConfiguration _configuration;
  private HubConnection? _hubConnection;

  public event Action<string, string>? CallStateChanged;
  public event Action<string, string>? IncomingCall;
  public event Action? CallHistoryUpdated;
  public event Action<object>? SystemStatusChanged;

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  public PhoneHubService(ILogger<PhoneHubService> logger, IConfiguration configuration)
  {
    _logger = logger;
    _configuration = configuration;
  }

  public async Task StartAsync()
  {
    var hubUrl = _configuration.GetValue<string>("RotaryPhone:HubUrl") ?? "http://localhost:5004/hub";

    _hubConnection = new HubConnectionBuilder()
      .WithUrl(hubUrl)
      .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
      .Build();

    _hubConnection.On<string, string>("CallStateChanged", (phoneId, state) =>
    {
      _logger.LogDebug("Phone call state changed: {PhoneId} → {State}", phoneId, state);
      CallStateChanged?.Invoke(phoneId, state);
    });

    _hubConnection.On<string, string>("IncomingCall", (phoneId, phoneNumber) =>
    {
      _logger.LogInformation("Incoming call from {PhoneNumber}", phoneNumber);
      IncomingCall?.Invoke(phoneId, phoneNumber);
    });

    _hubConnection.On("CallHistoryUpdated", () =>
    {
      CallHistoryUpdated?.Invoke();
    });

    _hubConnection.On<object>("SystemStatusChanged", (status) =>
    {
      SystemStatusChanged?.Invoke(status);
    });

    _hubConnection.Reconnecting += ex =>
    {
      _logger.LogWarning(ex, "Phone hub reconnecting...");
      return Task.CompletedTask;
    };

    _hubConnection.Reconnected += connectionId =>
    {
      _logger.LogInformation("Phone hub reconnected: {ConnectionId}", connectionId);
      return Task.CompletedTask;
    };

    _hubConnection.Closed += ex =>
    {
      _logger.LogWarning(ex, "Phone hub connection closed");
      return Task.CompletedTask;
    };

    try
    {
      await _hubConnection.StartAsync();
      _logger.LogInformation("Connected to RotaryPhone hub at {Url}", hubUrl);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to connect to RotaryPhone hub at {Url} — phone features unavailable", hubUrl);
    }
  }

  public async Task StopAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.DisposeAsync();
      _hubConnection = null;
    }
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync();
  }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Radio.Web --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/Services/Hub/PhoneHubService.cs
git commit -m "feat: add PhoneHubService SignalR client for RotaryPhone real-time events"
```

---

### Task 4: Register services in Program.cs

**Files:**
- Modify: `src/Radio.Web/Program.cs`

- [ ] **Step 1: Add PhoneApiService HttpClient registration**

In `Program.cs`, after the existing `IntegrationsApiService` HttpClient registration, add:

```csharp
// RotaryPhone.API client (separate service on port 5004)
var phoneApiBaseUrl = builder.Configuration.GetValue<string>("RotaryPhone:ApiBaseUrl") ?? "http://localhost:5004";
builder.Services.AddHttpClient<PhoneApiService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});
```

- [ ] **Step 2: Add PhoneHubService registration**

After the existing hub service registrations, add:

```csharp
builder.Services.AddSingleton<PhoneHubService>();
```

And after `app.MapRazorComponents`, add the hub startup:

```csharp
// Start RotaryPhone hub connection
var phoneHub = app.Services.GetRequiredService<PhoneHubService>();
_ = phoneHub.StartAsync();
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Radio.Web --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/Radio.Web/Program.cs
git commit -m "feat: register PhoneApiService and PhoneHubService in DI"
```

---

## Chunk 2: Phone Page UI

### Task 5: Create PhonePage.razor — Dashboard tab

**Files:**
- Create: `src/Radio.Web/Components/Pages/PhonePage.razor`

- [ ] **Step 1: Create PhonePage with Dashboard tab**

Create `src/Radio.Web/Components/Pages/PhonePage.razor`:

```razor
@page "/phone"
@inject PhoneApiService PhoneApi
@inject PhoneHubService PhoneHub
@inject NotificationService NotificationService
@implements IDisposable

<div class="phone-page">
  <RadzenTabs @bind-SelectedIndex="_selectedTab" class="phone-tabs">

    @* ═══ Tab 1: Dashboard ═══ *@
    <Tabs>
      <RadzenTabsItem Text="Dashboard" Icon="dashboard">
        <div class="phone-dashboard">

          @* System Status Card *@
          <RadzenCard class="phone-card">
            <h3 class="card-title">
              <RadzenIcon Icon="info" /> System Status
            </h3>
            <div class="status-grid">
              <div class="status-item">
                <span class="status-label">Bluetooth</span>
                <RadzenBadge
                  BadgeStyle="@(_systemStatus?.BluetoothConnected == true ? BadgeStyle.Success : BadgeStyle.Danger)"
                  Text="@(_systemStatus?.BluetoothConnected == true ? "Connected" : "Disconnected")" />
                @if (_systemStatus?.BluetoothDeviceAddress != null)
                {
                  <span class="status-detail">@_systemStatus.BluetoothDeviceAddress</span>
                }
              </div>
              <div class="status-item">
                <span class="status-label">SIP Server</span>
                <RadzenBadge
                  BadgeStyle="@(_systemStatus?.SipListening == true ? BadgeStyle.Success : BadgeStyle.Danger)"
                  Text="@(_systemStatus?.SipListening == true ? "Listening" : "Offline")" />
                @if (_systemStatus?.SipListenAddress != null)
                {
                  <span class="status-detail">@_systemStatus.SipListenAddress:@_systemStatus.SipPort</span>
                }
              </div>
              <div class="status-item">
                <span class="status-label">HT801 ATA</span>
                <RadzenBadge
                  BadgeStyle="@(_systemStatus?.Ht801Reachable == true ? BadgeStyle.Success : BadgeStyle.Danger)"
                  Text="@(_systemStatus?.Ht801Reachable == true ? "Online" : "Offline")" />
                @if (_systemStatus?.Ht801IpAddress != null)
                {
                  <span class="status-detail">@_systemStatus.Ht801IpAddress</span>
                }
              </div>
            </div>
          </RadzenCard>

          @* Phone Status Card *@
          <RadzenCard class="phone-card phone-status-card">
            <h3 class="card-title">
              <RadzenIcon Icon="phone" /> Phone Status
            </h3>
            <div class="phone-state-display">
              <RadzenBadge
                BadgeStyle="@GetCallStateBadgeStyle()"
                Text="@_callState.CallState"
                class="call-state-badge" />
              @if (_callState.CallState == "Ringing" && _callState.IncomingNumber != null)
              {
                <div class="call-info">
                  <RadzenIcon Icon="call_received" />
                  <span class="phone-number">@_callState.IncomingNumber</span>
                  @if (_callState.CallerName != null)
                  {
                    <span class="caller-name">@_callState.CallerName</span>
                  }
                </div>
              }
              else if (_callState.CallState == "InCall")
              {
                <div class="call-info">
                  <RadzenIcon Icon="phone_in_talk" />
                  <span class="phone-number">@(_callState.IncomingNumber ?? _callState.DialedNumber ?? "Unknown")</span>
                  @if (_callState.Duration != null)
                  {
                    <span class="call-duration">@_callState.Duration</span>
                  }
                </div>
              }
              else if (_callState.CallState == "Dialing" && _callState.DialedNumber != null)
              {
                <div class="call-info">
                  <RadzenIcon Icon="call_made" />
                  <span class="phone-number">@_callState.DialedNumber</span>
                </div>
              }
            </div>
          </RadzenCard>

          @* Developer Controls *@
          <RadzenAccordion class="phone-card">
            <Items>
              <RadzenAccordionItem Text="Developer Controls" Icon="code">
                <div class="dev-controls">
                  <div class="dev-section">
                    <span class="dev-label">Handset Simulation</span>
                    <div class="dev-buttons">
                      <RadzenButton Text="Lift Handset" Icon="phone"
                        Click="@(() => SimulateHookAsync(true))"
                        Disabled="@(_callState.CallState == "InCall")"
                        ButtonStyle="ButtonStyle.Secondary" Size="ButtonSize.Small" />
                      <RadzenButton Text="Drop Handset" Icon="phone_disabled"
                        Click="@(() => SimulateHookAsync(false))"
                        Disabled="@(_callState.CallState == "Idle")"
                        ButtonStyle="ButtonStyle.Secondary" Size="ButtonSize.Small" />
                    </div>
                  </div>
                  <div class="dev-section">
                    <span class="dev-label">Network Simulation</span>
                    <div class="dev-buttons">
                      <RadzenButton Text="Simulate Incoming Call" Icon="ring_volume"
                        Click="@SimulateIncomingCallAsync"
                        ButtonStyle="ButtonStyle.Warning" Size="ButtonSize.Small" />
                    </div>
                  </div>
                  <div class="dev-section">
                    <span class="dev-label">Dialer</span>
                    <div class="dev-dialer">
                      <RadzenTextBox @bind-Value="_dialDigits" Placeholder="Enter digits..."
                        class="dial-input" />
                      <RadzenButton Text="Dial" Icon="dialpad"
                        Click="@SimulateDialAsync"
                        Disabled="@(string.IsNullOrWhiteSpace(_dialDigits))"
                        ButtonStyle="ButtonStyle.Primary" Size="ButtonSize.Small" />
                    </div>
                  </div>
                </div>
              </RadzenAccordionItem>
            </Items>
          </RadzenAccordion>
        </div>
      </RadzenTabsItem>

      @* ═══ Tab 2: Contacts ═══ *@
      <RadzenTabsItem Text="Contacts" Icon="contacts">
        <div class="phone-contacts">
          <div class="contacts-header">
            <h3 class="card-title">Contacts</h3>
            <RadzenButton Text="Add Contact" Icon="person_add"
              Click="@OpenAddContactDialog"
              ButtonStyle="ButtonStyle.Primary" Size="ButtonSize.Small" />
          </div>
          <RadzenDataGrid Data="@_contacts" TItem="ContactDto"
            AllowSorting="true" AllowFiltering="true" FilterMode="FilterMode.Simple"
            class="contacts-grid">
            <Columns>
              <RadzenDataGridColumn TItem="ContactDto" Property="Name" Title="Name" Width="200px" />
              <RadzenDataGridColumn TItem="ContactDto" Property="PhoneNumber" Title="Phone" Width="160px">
                <Template Context="contact">
                  <span class="phone-number">@contact.PhoneNumber</span>
                </Template>
              </RadzenDataGridColumn>
              <RadzenDataGridColumn TItem="ContactDto" Property="Email" Title="Email" />
              <RadzenDataGridColumn TItem="ContactDto" Title="Actions" Width="140px" Sortable="false" Filterable="false">
                <Template Context="contact">
                  <RadzenButton Icon="edit" ButtonStyle="ButtonStyle.Light" Size="ButtonSize.Small"
                    Click="@(() => OpenEditContactDialog(contact))" class="action-btn" />
                  <RadzenButton Icon="delete" ButtonStyle="ButtonStyle.Danger" Size="ButtonSize.Small"
                    Click="@(() => DeleteContactAsync(contact))" class="action-btn" />
                </Template>
              </RadzenDataGridColumn>
            </Columns>
          </RadzenDataGrid>
        </div>
      </RadzenTabsItem>

      @* ═══ Tab 3: Call History ═══ *@
      <RadzenTabsItem Text="Call History" Icon="history">
        <div class="phone-history">
          <div class="history-header">
            <h3 class="card-title">Call History</h3>
            <RadzenButton Text="Clear History" Icon="delete_sweep"
              Click="@ClearCallHistoryAsync"
              ButtonStyle="ButtonStyle.Danger" Size="ButtonSize.Small"
              Disabled="@(_callHistory == null || _callHistory.Count == 0)" />
          </div>
          @if (_callHistory == null || _callHistory.Count == 0)
          {
            <div class="empty-state">No calls recorded</div>
          }
          else
          {
            <div class="history-list">
              @foreach (var entry in _callHistory)
              {
                <div class="history-item">
                  <RadzenIcon Icon="@GetCallDirectionIcon(entry)"
                    Style="@($"color: {GetCallDirectionColor(entry)}")" />
                  <div class="history-details">
                    <span class="phone-number">@entry.PhoneNumber</span>
                    <span class="history-time">@entry.StartTime.ToLocalTime().ToString("g")</span>
                  </div>
                  @if (entry.AnsweredOn != null)
                  {
                    <RadzenBadge Text="@entry.AnsweredOn"
                      BadgeStyle="@(entry.AnsweredOn == "RotaryPhone" ? BadgeStyle.Info : BadgeStyle.Light)" />
                  }
                  @if (entry.Duration != null)
                  {
                    <span class="call-duration">@entry.Duration</span>
                  }
                </div>
              }
            </div>
          }
        </div>
      </RadzenTabsItem>
    </Tabs>
  </RadzenTabs>
</div>

@code {
  private int _selectedTab;
  private PhoneSystemStatusDto? _systemStatus;
  private PhoneCallStateDto _callState = new();
  private List<ContactDto>? _contacts;
  private List<CallHistoryEntryDto>? _callHistory;
  private string _dialDigits = "";
  private bool _isAvailable;
  private System.Timers.Timer? _pollTimer;

  protected override async Task OnInitializedAsync()
  {
    _isAvailable = await PhoneApi.IsAvailableAsync();
    if (_isAvailable)
    {
      await RefreshAllAsync();
    }

    // Subscribe to SignalR events
    PhoneHub.CallStateChanged += OnCallStateChanged;
    PhoneHub.IncomingCall += OnIncomingCall;
    PhoneHub.CallHistoryUpdated += OnCallHistoryUpdated;
    PhoneHub.SystemStatusChanged += OnSystemStatusChanged;

    // Poll for status every 5 seconds as fallback
    _pollTimer = new System.Timers.Timer(5000);
    _pollTimer.Elapsed += async (_, _) => await PollStatusAsync();
    _pollTimer.Start();
  }

  private async Task RefreshAllAsync()
  {
    _systemStatus = await PhoneApi.GetSystemStatusAsync();
    var callState = await PhoneApi.GetCallStateAsync();
    if (callState != null) _callState = callState;
    _contacts = await PhoneApi.GetContactsAsync();
    _callHistory = await PhoneApi.GetCallHistoryAsync();
    await InvokeAsync(StateHasChanged);
  }

  private async Task PollStatusAsync()
  {
    try
    {
      var available = await PhoneApi.IsAvailableAsync();
      if (available != _isAvailable)
      {
        _isAvailable = available;
        if (available) await RefreshAllAsync();
        else await InvokeAsync(StateHasChanged);
      }
      else if (available)
      {
        var callState = await PhoneApi.GetCallStateAsync();
        if (callState != null) _callState = callState;
        _systemStatus = await PhoneApi.GetSystemStatusAsync();
        await InvokeAsync(StateHasChanged);
      }
    }
    catch { /* Polling failure is non-fatal */ }
  }

  // SignalR event handlers

  private void OnCallStateChanged(string phoneId, string state)
  {
    _callState.CallState = state;
    InvokeAsync(StateHasChanged);
  }

  private void OnIncomingCall(string phoneId, string phoneNumber)
  {
    _callState.CallState = "Ringing";
    _callState.IncomingNumber = phoneNumber;
    InvokeAsync(StateHasChanged);
  }

  private async void OnCallHistoryUpdated()
  {
    _callHistory = await PhoneApi.GetCallHistoryAsync();
    await InvokeAsync(StateHasChanged);
  }

  private void OnSystemStatusChanged(object status)
  {
    // Re-fetch typed status
    _ = Task.Run(async () =>
    {
      _systemStatus = await PhoneApi.GetSystemStatusAsync();
      await InvokeAsync(StateHasChanged);
    });
  }

  // Developer controls

  private async Task SimulateHookAsync(bool offHook)
  {
    await PhoneApi.SimulateHookAsync(offHook);
  }

  private async Task SimulateIncomingCallAsync()
  {
    await PhoneApi.SimulateIncomingCallAsync();
  }

  private async Task SimulateDialAsync()
  {
    if (!string.IsNullOrWhiteSpace(_dialDigits))
    {
      await PhoneApi.SimulateDialAsync(_dialDigits);
      _dialDigits = "";
    }
  }

  // Contacts CRUD

  private async Task OpenAddContactDialog()
  {
    var form = new ContactFormDto();
    var result = await ShowContactDialog("Add Contact", form);
    if (result)
    {
      await PhoneApi.CreateContactAsync(form);
      _contacts = await PhoneApi.GetContactsAsync();
    }
  }

  private async Task OpenEditContactDialog(ContactDto contact)
  {
    var form = new ContactFormDto
    {
      Name = contact.Name,
      PhoneNumber = contact.PhoneNumber,
      Email = contact.Email
    };
    var result = await ShowContactDialog("Edit Contact", form);
    if (result)
    {
      await PhoneApi.UpdateContactAsync(contact.Id, form);
      _contacts = await PhoneApi.GetContactsAsync();
    }
  }

  private async Task<bool> ShowContactDialog(string title, ContactFormDto form)
  {
    // Inline dialog using Radzen DialogService would be ideal,
    // but for simplicity use a flag-based approach
    // TODO: Replace with RadzenDialog when integrating
    return await Task.FromResult(false);
  }

  private async Task DeleteContactAsync(ContactDto contact)
  {
    var confirmed = await Task.FromResult(true); // TODO: confirmation dialog
    if (confirmed)
    {
      await PhoneApi.DeleteContactAsync(contact.Id);
      _contacts = await PhoneApi.GetContactsAsync();
    }
  }

  // Call History

  private async Task ClearCallHistoryAsync()
  {
    await PhoneApi.ClearCallHistoryAsync();
    _callHistory = await PhoneApi.GetCallHistoryAsync();
  }

  // Helpers

  private BadgeStyle GetCallStateBadgeStyle() => _callState.CallState switch
  {
    "Idle" => BadgeStyle.Success,
    "Ringing" => BadgeStyle.Warning,
    "InCall" => BadgeStyle.Danger,
    "Dialing" => BadgeStyle.Info,
    _ => BadgeStyle.Light
  };

  private static string GetCallDirectionIcon(CallHistoryEntryDto entry) => entry.Direction switch
  {
    "Incoming" when entry.AnsweredOn != null => "call_received",
    "Incoming" => "call_missed",
    "Outgoing" => "call_made",
    _ => "phone"
  };

  private static string GetCallDirectionColor(CallHistoryEntryDto entry) => entry.Direction switch
  {
    "Incoming" when entry.AnsweredOn != null => "var(--rz-success)",
    "Incoming" => "var(--rz-danger)",
    "Outgoing" => "var(--rz-info)",
    _ => "var(--rz-text-color)"
  };

  public void Dispose()
  {
    _pollTimer?.Stop();
    _pollTimer?.Dispose();
    PhoneHub.CallStateChanged -= OnCallStateChanged;
    PhoneHub.IncomingCall -= OnIncomingCall;
    PhoneHub.CallHistoryUpdated -= OnCallHistoryUpdated;
    PhoneHub.SystemStatusChanged -= OnSystemStatusChanged;
  }
}
```

- [ ] **Step 2: Add page CSS**

Append phone-specific styles to `src/Radio.Web/wwwroot/css/design-system.css` (or create a scoped CSS file `PhonePage.razor.css`):

```css
/* Phone Page Styles */
.phone-page { padding: 8px; height: 100%; }
.phone-dashboard { display: flex; flex-direction: column; gap: 12px; }
.phone-card { padding: 16px; }
.card-title { margin: 0 0 12px 0; font-size: 1.1rem; display: flex; align-items: center; gap: 8px; }
.status-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
.status-item { display: flex; flex-direction: column; gap: 4px; }
.status-label { font-size: 0.85rem; color: var(--rz-text-secondary-color); }
.status-detail { font-size: 0.8rem; font-family: monospace; opacity: 0.7; }
.phone-state-display { text-align: center; padding: 16px 0; }
.call-state-badge { font-size: 1.1rem !important; padding: 8px 24px !important; }
.call-info { margin-top: 12px; display: flex; align-items: center; justify-content: center; gap: 8px; }
.phone-number { font-family: monospace; font-size: 1.1rem; }
.caller-name { color: var(--rz-text-secondary-color); }
.call-duration { font-family: monospace; color: var(--rz-text-secondary-color); }
.dev-controls { display: flex; flex-direction: column; gap: 16px; }
.dev-section { display: flex; flex-direction: column; gap: 8px; }
.dev-label { font-size: 0.85rem; font-weight: 600; color: var(--rz-text-secondary-color); }
.dev-buttons { display: flex; gap: 8px; }
.dev-dialer { display: flex; gap: 8px; align-items: center; }
.dial-input { max-width: 200px; }
.contacts-header, .history-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
.contacts-grid { margin-top: 8px; }
.action-btn { margin: 0 2px; }
.empty-state { text-align: center; padding: 32px; color: var(--rz-text-secondary-color); }
.history-list { display: flex; flex-direction: column; gap: 4px; }
.history-item { display: flex; align-items: center; gap: 12px; padding: 8px 12px; border-radius: 4px; background: var(--rz-base-background-color); }
.history-details { flex: 1; display: flex; flex-direction: column; }
.history-time { font-size: 0.8rem; color: var(--rz-text-secondary-color); }
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Radio.Web --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/Radio.Web/Components/Pages/PhonePage.razor
git commit -m "feat: add PhonePage with Dashboard, Contacts, and Call History tabs"
```

---

### Task 6: Add phone icon to MainLayout nav bar

**Files:**
- Modify: `src/Radio.Web/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Add phone nav icon**

In `MainLayout.razor`, find the navigation icons section. Between the System (settings) link and the Sleep button, add:

```razor
  <a href="/system" class="@NavClass("/system")" title="Settings">
    <RadzenIcon Icon="settings" />
  </a>
  <a href="/phone" class="@NavClass("/phone")" title="Phone">
    <RadzenIcon Icon="phone" />
  </a>
  <span class="nav-icon" title="Sleep" @onclick="HandleSleepButtonAsync">
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Radio.Web --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/Components/Layout/MainLayout.razor
git commit -m "feat: add phone icon to navigation bar"
```

---

### Task 7: Add PhonePage bUnit tests

**Files:**
- Create: `tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs`

- [ ] **Step 1: Write PhonePage tests**

Create `tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Pages;

public class PhonePageTests : TestContext
{
  public PhonePageTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();

    // Register PhoneApiService with mock handler that returns empty/default data
    Services.AddHttpClient<PhoneApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).AddHttpMessageHandler(() => new EmptyResponseHandler());

    // Register PhoneHubService
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["RotaryPhone:HubUrl"] = "http://localhost:5004/hub"
      })
      .Build();
    Services.AddSingleton<IConfiguration>(config);
    Services.AddSingleton(new PhoneHubService(
      NullLogger<PhoneHubService>.Instance, config));
  }

  [Fact]
  public void PhonePage_Renders_WithTabs()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Dashboard", cut.Markup);
    Assert.Contains("Contacts", cut.Markup);
    Assert.Contains("Call History", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_SystemStatusSection()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("System Status", cut.Markup);
    Assert.Contains("Bluetooth", cut.Markup);
    Assert.Contains("SIP Server", cut.Markup);
    Assert.Contains("HT801 ATA", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_PhoneStatusSection()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Phone Status", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_DeveloperControls()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Developer Controls", cut.Markup);
  }

  private class EmptyResponseHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      // Return appropriate empty responses based on endpoint
      var path = request.RequestUri?.PathAndQuery ?? "";
      string content = path switch
      {
        var p when p.Contains("system-status") =>
          """{"platform":"Linux","sipListening":false,"ht801Reachable":false}""",
        var p when p.Contains("/api/phone/status") =>
          """{"callState":"Idle"}""",
        var p when p.Contains("/api/contacts") => "[]",
        var p when p.Contains("/api/callhistory") => "[]",
        _ => "{}"
      };
      return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
      {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
      });
    }
  }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Radio.Web.Tests --filter "FullyQualifiedName~PhonePageTests" --configuration Release`
Expected: 4/4 PASS

- [ ] **Step 3: Commit**

```bash
git add tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs
git commit -m "test: add PhonePage bUnit tests"
```

---

## Chunk 3: Configuration and Final Verification

### Task 8: Add Phone section to SystemConfigPage

**Files:**
- Modify: `src/Radio.Web/Components/Pages/SystemConfigPage.razor`

- [ ] **Step 1: Add Phone configuration tab/section**

Find the existing Integration-related tab in SystemConfigPage.razor and add a Phone subsection with fields for:
- HT801 IP Address (text input, default "192.168.1.10")
- SIP Port (numeric, default 5060)
- RTP Port (numeric, default 49000)
- Phone Enabled (switch/checkbox)

These write to RotaryPhone.API's config via `PhoneApiService` (or via Radio.API's existing integration config endpoints if they proxy to RotaryPhone).

Note: The exact implementation depends on SystemConfigPage's existing tab structure. Read the file first to find the right insertion point. Follow the existing pattern for config fields.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Radio.Web --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/Components/Pages/SystemConfigPage.razor
git commit -m "feat: add Phone configuration section to system settings"
```

---

### Task 9: Full build and test verification

**Files:** None (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test --configuration Release --verbosity normal`
Expected: All tests pass (known flaky: `WaveformComparisonTests.AnalyzeBatchCaptures_10Runs`)

- [ ] **Step 3: Commit any fixes**

If any build or test issues, fix and commit.

---

### Task 10: Deploy and visual verification

**Files:** None (deployment only)

- [ ] **Step 1: Deploy to Ubuntu**

Run: `./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`

- [ ] **Step 2: Verify Phone page renders**

Open `http://radio:5002/phone` in browser. Verify:
- 3 tabs visible (Dashboard, Contacts, Call History)
- System Status shows all items (will show "Offline" since RotaryPhone.API isn't deployed yet)
- Phone Status shows "Idle"
- Developer Controls section is collapsed but expandable
- Phone icon visible in nav bar
- Page matches Radio Console's dark theme

- [ ] **Step 3: Create PR**

Push branch, create PR with summary of all changes.
