# GV Messages — PR1: Foundation + IA Shell

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay the consumer-side foundation for the Google Voice (GV) Voicemail + Texts UI and restructure `/phone` into the unified **Messages feed** shell. This PR adds DTOs, extends the existing `GvBridgeApiService` with read methods, adds the GV SignalR events to `PhoneHubService`, introduces `GvBridgeStatusService` (status poll) and the `RotaryPhoneAuthHandler` seam (OFF), wires DI + config, and rebuilds `PhonePage` IA (segmented-filter feed + "More ▸" rail + badge model, missed calls folded into the feed). **No voicemail player and no texts conversation render in this PR** — PR2/PR3 fill those surfaces. PR1 ships the scaffolding plus a feed that already renders **call rows** (data already available).

**Owner-baked decisions in scope here:** unified feed + segmented filter (Designer Option C); "More ▸" expand-in-place rail; missed calls **count toward the unread badge** (decision 2); UI-local read-state with a flagged mark-read client seam (decision 4); auth handler seam OFF (ADR §8.1).

**Sources of truth (do not redesign):**
- Design handoff: `docs/design-handoffs/HANDOFF-phone-messages-voicemail-sms.md`
- ADR-022: `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md`
- Integration contract: `D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-voicemail-sms-ui-handoff.md`

**Tech stack:** Blazor Server, Radzen (material-dark), SignalR client, typed `HttpClient`, `design-system.css` tokens.

**Dependencies:** none. PR2 and PR3 depend on this PR.

---

## File Map

### New files

| File | Responsibility |
|------|---------------|
| `src/Radio.Web/Services/GvBridgeStatusService.cs` | Singleton ~10s `/api/gvbridge/status` poll; `Current` + `StatusChanged` + `IsAvailable` (drives reconnecting banner + Send gate). |
| `src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs` | `DelegatingHandler` that injects `X-RotaryPhone-Auth` **only when `RotaryPhone:Gv:AuthKey` is non-empty**. OFF today. |
| `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor` | The unified feed shell: panel header + segmented filter + feed rows (calls only in PR1) + detail-pane host + reconnecting banner. PR2/PR3 extend it. |
| `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs` | Unit tests for the new read methods + audio-URL builder. |
| `tests/Radio.Web.Tests/Services/GvBridgeStatusServiceTests.cs` | Unit tests for availability derivation + change-event firing. |
| `tests/Radio.Web.Tests/Services/RotaryPhoneAuthHandlerTests.cs` | Header-injection-only-when-key-set tests. |

### Modified files

| File | Changes |
|------|---------|
| `src/Radio.Web/Models/ApiModels.cs` | Add `VoicemailItemDto`, `VoicemailListDto`, `SmsMessageDto`, `SmsThreadDto`, `SmsThreadListDto`, `SmsThreadMessagesDto`, `SendSmsRequest`, `SendSmsResponse`, `GvDirection` helper; extend `GvBridgeStatusDto` with `SipRegistered`/`CookiesValid`. |
| `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` | Delete stale "no SMS routes" comment; add voicemail list/item + audio-URL builder + SMS threads/messages read methods. |
| `src/Radio.Web/Services/Hub/PhoneHubService.cs` | Add `GvSmsReceived` / `GvVoicemailReceived` events + `.On(...)` handlers on the existing `/hub` connection. |
| `src/Radio.Web/Components/Pages/PhonePage.razor` | Default `_activeTab = "messages"`; add Messages + "More ▸" rail entries + `_moreExpanded`; render legacy panels only under More; compute badge counts; render `PhoneMessagesPanel`. |
| `src/Radio.Web/Components/Layout/MainLayout.razor` | Add `.nav-badge` on the `/phone` pill + accessible count in `aria-label` (sourced from a shared count — see Task 8). |
| `src/Radio.Web/Services/PhoneUnreadState.cs` (new in this file map row, but listed here for the topbar badge) | Tiny scoped/singleton shared count surfaced to `MainLayout`. |
| `src/Radio.Web/Program.cs` | Register `RotaryPhoneAuthHandler`; add it to GV `HttpClient`s; register `GvBridgeStatusService` singleton + boot start; register `PhoneUnreadState`; add config defaults. |
| `src/Radio.Web/appsettings.json` | Add `RotaryPhone:Gv:{SendEnabled,StatusPollSeconds,AuthKey}` defaults. |
| `src/Radio.Web/appsettings.Production.json` | Document per-machine `RotaryPhone:ApiBaseUrl`/`HubUrl`/`Gv:AuthKey` overrides (create if absent). |
| `design/FUTURE-WORK.md` | Document UI-local read-state, GV mark-read seam, flagged send, auth handler OFF. |

---

## Chunk 1: DTOs

### Task 1: Add GV Voicemail + SMS DTOs to ApiModels.cs

**Files:**
- Modify: `src/Radio.Web/Models/ApiModels.cs` (append near the existing `GvBridgeStatusDto` / GV block, ~line 1091)

- [ ] **Step 1: Extend `GvBridgeStatusDto`**

Replace the existing `GvBridgeStatusDto` class body (lines ~1091–1100) so the two new fields exist and deserialization never fails if RotaryPhone omits them:

```csharp
public class GvBridgeStatusDto
{
  // Server shape (post-SIP-WSS migration, March 2026): { available, activeMode }.
  // SipRegistered / CookiesValid added per ADR-022 §4.4 — defensive/optional:
  // RotaryPhone may not populate them yet, so defaults keep deserialization safe.
  public bool Available { get; set; }
  public string ActiveMode { get; set; } = "";
  public bool SipRegistered { get; set; }
  public bool CookiesValid { get; set; }
}
```

- [ ] **Step 2: Add the GV (gvbridge) Voicemail + SMS DTOs**

Append a clearly-commented block. The comment MUST cross-reference the trunk SMS to prevent the documented collision (ADR §4.3):

```csharp
// ─────────────────────────────────────────────────────────────────────
// GV (gvbridge) Voicemail + SMS — consumed by the Messages UI (PhonePage).
// NOTE: GV (gvbridge) SMS is NOT the same product as VoIP.ms trunk SMS.
// Trunk SMS = GvSmsNotificationDto + GvTrunkHubService.SmsReceived on
// /hubs/gvtrunk. GV SMS = SmsMessageDto + PhoneHubService.GvSmsReceived on
// /hub. Do NOT merge or rename these. (ADR-022 §4.3.)
// ─────────────────────────────────────────────────────────────────────

// ── GV Voicemail ──────────────────────────────────────────────
public record VoicemailItemDto(
  string Id,
  string ThreadId,
  string FromNumber,            // E.164
  string? FromName,             // null → UI shows number / contact lookup
  DateTime ReceivedAt,          // UTC; format to local for display
  int DurationSeconds,          // 0 = unknown → do NOT render "0:00" as real
  bool IsRead,                  // UI-LOCAL only — GV mark-read not in v1
  string? Transcript,           // null = pending/absent
  string AudioUrl);             // RELATIVE from server; rebuild absolute (ADR D4)

public record VoicemailListDto(
  IReadOnlyList<VoicemailItemDto> Items,
  string? NextPageToken,        // null = no more pages
  DateTime FetchedAtUtc);

// ── GV SMS ────────────────────────────────────────────────────
public record SmsMessageDto(
  string Id,
  string ThreadId,
  string Direction,             // "Inbound" | "Outbound"; UNKNOWN → Inbound
  string CounterpartyNumber,    // E.164
  string? Text,                 // null → render placeholder, do not crash
  DateTime SentAt,              // UTC
  bool IsRead);

public record SmsThreadDto(
  string ThreadId,
  string CounterpartyNumber,
  string? CounterpartyName,
  DateTime LastMessageAt,
  bool HasUnread,
  string? LastMessagePreview);

public record SmsThreadListDto(
  IReadOnlyList<SmsThreadDto> Threads,
  DateTime FetchedAtUtc);

public record SmsThreadMessagesDto(
  string ThreadId,
  IReadOnlyList<SmsMessageDto> Messages,
  DateTime FetchedAtUtc);

// ── Send (flagged; wired in PR3, endpoint ships later) ─────────
public record SendSmsRequest(string ThreadId, string Text);
public record SendSmsResponse(SmsMessageDto? Message, string? Error);  // shape provisional

// Defensive direction mapping: anything not exactly "Outbound" → Inbound.
// Never throw on an unrecognized value (ADR §4.2 provisional-data rule).
public static class GvDirection
{
  public const string Inbound = "Inbound";
  public const string Outbound = "Outbound";

  public static bool IsOutbound(string? direction) =>
    string.Equals(direction, Outbound, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Radio.Web --configuration Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Radio.Web/Models/ApiModels.cs
git commit -m "feat(web): add GV voicemail + SMS DTOs and extend GvBridgeStatusDto"
```

---

## Chunk 2: GvBridgeApiService read methods

### Task 2: Extend GvBridgeApiService

**Files:**
- Modify: `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`
- Test: `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs`. Reuse the existing `MockHttpHandler` test helper already in `Radio.Web.Tests` (used by `PhoneApiServiceTests`):

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

public class GvBridgeApiServiceVoicemailSmsTests
{
  private static readonly JsonSerializerOptions JsonOptions =
    new() { PropertyNameCaseInsensitive = true };

  private static GvBridgeApiService CreateService(HttpClient client) =>
    new(client, NullLogger<GvBridgeApiService>.Instance);

  [Fact]
  public async Task GetVoicemailsAsync_ReturnsList()
  {
    var dto = new VoicemailListDto(
      new[]
      {
        new VoicemailItemDto("vm1", "t1", "+15551234567", "Jane",
          DateTime.UtcNow, 42, false, "hi", "/api/gvbridge/voicemail/vm1/audio")
      },
      null, DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetVoicemailsAsync();

    Assert.NotNull(result);
    Assert.Single(result!.Items);
    Assert.Equal("vm1", result.Items[0].Id);
  }

  [Fact]
  public async Task GetVoicemailsAsync_ReturnsNull_OnError()
  {
    var handler = new MockHttpHandler(statusCode: System.Net.HttpStatusCode.InternalServerError);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetVoicemailsAsync();

    Assert.Null(result);
  }

  [Fact]
  public async Task GetSmsThreadsAsync_ReturnsThreads()
  {
    var dto = new SmsThreadListDto(
      new[] { new SmsThreadDto("t1", "+15551234567", "Mom",
        DateTime.UtcNow, true, "Did you eat?") },
      DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadsAsync();

    Assert.NotNull(result);
    Assert.Single(result!.Threads);
    Assert.True(result.Threads[0].HasUnread);
  }

  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsMessages()
  {
    var dto = new SmsThreadMessagesDto("t1",
      new[] { new SmsMessageDto("m1", "t1", "Inbound", "+15551234567",
        "hello", DateTime.UtcNow, false) },
      DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.NotNull(result);
    Assert.Single(result!.Messages);
  }

  [Fact]
  public void GetVoicemailAudioUrl_BuildsAbsoluteUrl_AgainstBaseAddress()
  {
    var client = new HttpClient(new MockHttpHandler("{}"))
    { BaseAddress = new Uri("http://radio:5004") };

    var url = CreateService(client).GetVoicemailAudioUrl("vm1");

    Assert.Equal("http://radio:5004/api/gvbridge/voicemail/vm1/audio", url);
  }
}
```

- [ ] **Step 2: Implement the methods + delete the stale comment**

In `GvBridgeApiService.cs`: replace the class-level doc-comment (lines 8–13, the "there are no SMS routes" assertion) with an accurate one, and add the read methods. Note the audio-URL builder uses `_httpClient.BaseAddress` so it produces an **absolute** `radio:5004` URL (ADR D4 — the single most likely silent-failure point):

```csharp
/// <summary>
/// HTTP client for RotaryPhone.API GV Bridge endpoints (radio:5004).
/// Covers GV availability/status, call-adapter mode, and the GV Voicemail + SMS
/// read API. NOTE: this is Google Voice SMS (/api/gvbridge/sms/*), NOT the
/// VoIP.ms trunk SMS surface in GvTrunkApiService.
/// </summary>
```

Add inside the class (after `SetAdapterModeAsync`):

```csharp
// ── GV Voicemail (read) ───────────────────────────────────────

public async Task<VoicemailListDto?> GetVoicemailsAsync(
  int count = 20, string? pageToken = null, CancellationToken ct = default)
{
  try
  {
    var url = $"/api/gvbridge/voicemail?count={count}";
    if (!string.IsNullOrEmpty(pageToken))
      url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
    return await _httpClient.GetFromJsonAsync<VoicemailListDto>(url, JsonOptions, ct);
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Failed to get GV voicemail list");
    return null;
  }
}

public async Task<VoicemailItemDto?> GetVoicemailAsync(
  string id, CancellationToken ct = default)
{
  try
  {
    return await _httpClient.GetFromJsonAsync<VoicemailItemDto>(
      $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}", JsonOptions, ct);
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Failed to get GV voicemail {Id}", id);
    return null;
  }
}

/// <summary>
/// Builds the ABSOLUTE radio:5004 URL for a voicemail recording, for binding to
/// an &lt;audio src&gt;. The DTO's relative AudioUrl resolves against the Web
/// origin (:5002) and 404s — ALWAYS rebuild absolute against the API base
/// address (ADR-022 D4 / contract risk #3). Never bind the relative AudioUrl.
/// </summary>
public string GetVoicemailAudioUrl(string id)
{
  var baseUri = _httpClient.BaseAddress
    ?? new Uri("http://radio:5004");
  return new Uri(baseUri, $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}/audio")
    .ToString();
}

// ── GV SMS (read) ─────────────────────────────────────────────

public async Task<SmsThreadListDto?> GetSmsThreadsAsync(
  int count = 20, CancellationToken ct = default)
{
  try
  {
    return await _httpClient.GetFromJsonAsync<SmsThreadListDto>(
      $"/api/gvbridge/sms/threads?count={count}", JsonOptions, ct);
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Failed to get GV SMS threads");
    return null;
  }
}

public async Task<SmsThreadMessagesDto?> GetSmsThreadMessagesAsync(
  string threadId, int count = 50, CancellationToken ct = default)
{
  try
  {
    return await _httpClient.GetFromJsonAsync<SmsThreadMessagesDto>(
      $"/api/gvbridge/sms/threads/{Uri.EscapeDataString(threadId)}?count={count}",
      JsonOptions, ct);
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}", threadId);
    return null;
  }
}
```

- [ ] **Step 3: Run the tests**

`dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests"`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs
git commit -m "feat(web): add GV voicemail + SMS read methods, delete stale no-SMS comment"
```

---

## Chunk 3: PhoneHubService GV events

### Task 3: Add GvSmsReceived / GvVoicemailReceived to PhoneHubService

**Files:**
- Modify: `src/Radio.Web/Services/Hub/PhoneHubService.cs`

> **Collision guard (ADR §6.1 / contract risk #2):** the wire event name is `"SmsReceived"`, which `GvTrunkHubService` ALSO handles — but on a different connection (`/hubs/gvtrunk`) with a different payload. They never collide at runtime. The C# event here MUST be named **`GvSmsReceived`** (not `SmsReceived`) so a reader can't confuse the two. The GV handler MUST live on `PhoneHubService` (the `/hub` connection), NOT on `GvTrunkHubService`.

- [ ] **Step 1: Add the events**

After the existing events (line 20, `SystemStatusChanged`) add:

```csharp
// GV (gvbridge) push — rides the existing /hub RotaryHub (ADR-022 D5).
// NOTE: "GvSmsReceived" deliberately differs from GvTrunkHubService.SmsReceived
// (/hubs/gvtrunk, different payload). Do not rename to plain SmsReceived.
public event Action<Radio.Web.Models.SmsMessageDto>? GvSmsReceived;
public event Action<Radio.Web.Models.VoicemailItemDto>? GvVoicemailReceived;
```

- [ ] **Step 2: Register the handlers**

Inside `StartAsync()`, alongside the existing `.On(...)` registrations (after the `SystemStatusChanged` handler, ~line 73):

```csharp
_hubConnection.On<Radio.Web.Models.SmsMessageDto>("SmsReceived", m =>
{
  _logger.LogDebug("GV SMS received on thread {ThreadId}", m.ThreadId);
  GvSmsReceived?.Invoke(m);
});

_hubConnection.On<Radio.Web.Models.VoicemailItemDto>("VoicemailReceived", v =>
{
  _logger.LogDebug("GV voicemail received {Id}", v.Id);
  GvVoicemailReceived?.Invoke(v);
});
```

- [ ] **Step 3: Build**

`dotnet build src/Radio.Web --configuration Release` — 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/Radio.Web/Services/Hub/PhoneHubService.cs
git commit -m "feat(web): add GvSmsReceived/GvVoicemailReceived events on PhoneHubService /hub"
```

---

## Chunk 4: RotaryPhoneAuthHandler seam (OFF)

### Task 4: Create the auth DelegatingHandler

**Files:**
- Create: `src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs`
- Test: `tests/Radio.Web.Tests/Services/RotaryPhoneAuthHandlerTests.cs`

> ADR §8.1: one place adds `X-RotaryPhone-Auth` when `RotaryPhone:Gv:AuthKey` is non-empty. Today the key is empty → no header. Mirrors the existing `ApiConnectionLoggingHandler` registration pattern.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Radio.Web.Services.Http;

namespace Radio.Web.Tests.Services;

public class RotaryPhoneAuthHandlerTests
{
  private sealed class CapturingHandler : HttpMessageHandler
  {
    public HttpRequestMessage? Last;
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken ct)
    {
      Last = request;
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
  }

  private static HttpClient Build(string? key, CapturingHandler inner)
  {
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:AuthKey"] = key })
      .Build();
    var handler = new RotaryPhoneAuthHandler(config) { InnerHandler = inner };
    return new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
  }

  [Fact]
  public async Task NoHeader_WhenKeyEmpty()
  {
    var inner = new CapturingHandler();
    await Build("", inner).GetAsync("/api/gvbridge/status");
    Assert.False(inner.Last!.Headers.Contains("X-RotaryPhone-Auth"));
  }

  [Fact]
  public async Task NoHeader_WhenKeyMissing()
  {
    var inner = new CapturingHandler();
    await Build(null, inner).GetAsync("/api/gvbridge/status");
    Assert.False(inner.Last!.Headers.Contains("X-RotaryPhone-Auth"));
  }

  [Fact]
  public async Task AddsHeader_WhenKeySet()
  {
    var inner = new CapturingHandler();
    await Build("secret123", inner).GetAsync("/api/gvbridge/status");
    Assert.True(inner.Last!.Headers.TryGetValues("X-RotaryPhone-Auth", out var vals));
    Assert.Equal("secret123", vals!.Single());
  }
}
```

- [ ] **Step 2: Implement the handler**

```csharp
using Microsoft.Extensions.Configuration;

namespace Radio.Web.Services.Http;

/// <summary>
/// Injects the X-RotaryPhone-Auth header on outbound GV requests ONLY when
/// RotaryPhone:Gv:AuthKey is non-empty. Today the key is empty → no header is
/// sent (honors the current LAN-only no-auth posture). One place to flip on
/// when the inter-service auth gate ships (ADR-022 §8.1).
/// </summary>
public sealed class RotaryPhoneAuthHandler : DelegatingHandler
{
  private const string HeaderName = "X-RotaryPhone-Auth";
  private readonly IConfiguration _configuration;

  public RotaryPhoneAuthHandler(IConfiguration configuration)
  {
    _configuration = configuration;
  }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var key = _configuration.GetValue<string>("RotaryPhone:Gv:AuthKey");
    if (!string.IsNullOrEmpty(key) && !request.Headers.Contains(HeaderName))
    {
      request.Headers.Add(HeaderName, key);
    }
    return base.SendAsync(request, cancellationToken);
  }
}
```

- [ ] **Step 3: Run the tests**

`dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~RotaryPhoneAuthHandlerTests"`

- [ ] **Step 4: Commit**

```bash
git add src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs tests/Radio.Web.Tests/Services/RotaryPhoneAuthHandlerTests.cs
git commit -m "feat(web): add RotaryPhoneAuthHandler header seam (off until AuthKey set)"
```

---

## Chunk 5: GvBridgeStatusService singleton

### Task 5: Create GvBridgeStatusService

**Files:**
- Create: `src/Radio.Web/Services/GvBridgeStatusService.cs`
- Test: `tests/Radio.Web.Tests/Services/GvBridgeStatusServiceTests.cs`

> ADR §6.2 + memory DI gotcha: a **singleton cannot inject a scoped/typed-`HttpClient` directly.** Resolve `GvBridgeApiService` via `IServiceScopeFactory` per poll. One poll for the whole app.

- [ ] **Step 1: Write the failing test (availability derivation + event)**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

public class GvBridgeStatusServiceTests
{
  [Fact]
  public void ApplyStatus_DerivesIsAvailable_AndFiresChange()
  {
    var svc = new GvBridgeStatusService(
      scopeFactory: null!, NullLogger<GvBridgeStatusService>.Instance, pollSeconds: 10);

    GvBridgeStatusDto? observed = null;
    var fired = 0;
    svc.StatusChanged += s => { observed = s; fired++; };

    // null status → degraded
    svc.ApplyStatusForTest(null);
    Assert.False(svc.IsAvailable);
    Assert.Equal(1, fired);

    // available
    svc.ApplyStatusForTest(new GvBridgeStatusDto { Available = true });
    Assert.True(svc.IsAvailable);
    Assert.Equal(2, fired);
    Assert.NotNull(observed);

    // no change in availability → still fires (UI may want fresh fields), but
    // IsAvailable holds
    svc.ApplyStatusForTest(new GvBridgeStatusDto { Available = true });
    Assert.True(svc.IsAvailable);
  }
}
```

- [ ] **Step 2: Implement the service**

`ApplyStatusForTest` is an `internal`-style seam exposed via a normal public method named for tests (project has no `InternalsVisibleTo` convention in these tests — keep it a plain method, documented as test-facing):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// Owns the single ~10s /api/gvbridge/status poll for the whole app and exposes
/// an observable availability state the Messages UI binds to (reconnecting
/// banner + Send gate). Singleton; resolves GvBridgeApiService via a scope per
/// poll because a singleton cannot inject a scoped/typed HttpClient
/// (ADR-022 §6.2). RadioConsole only reflects state — RotaryPhone does the
/// actual cookie recovery.
/// </summary>
public sealed class GvBridgeStatusService : IAsyncDisposable
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<GvBridgeStatusService> _logger;
  private readonly int _pollSeconds;
  private PeriodicTimer? _timer;
  private Task? _loop;
  private CancellationTokenSource? _cts;

  public GvBridgeStatusDto? Current { get; private set; }
  public bool IsAvailable { get; private set; }
  public event Action<GvBridgeStatusDto?>? StatusChanged;

  public GvBridgeStatusService(
    IServiceScopeFactory scopeFactory,
    ILogger<GvBridgeStatusService> logger,
    int pollSeconds = 10)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _pollSeconds = pollSeconds <= 0 ? 10 : pollSeconds;
  }

  public void Start()
  {
    if (_loop != null) return;
    _cts = new CancellationTokenSource();
    _timer = new PeriodicTimer(TimeSpan.FromSeconds(_pollSeconds));
    _loop = Task.Run(() => PollLoopAsync(_cts.Token));
  }

  private async Task PollLoopAsync(CancellationToken ct)
  {
    // Prime once immediately so the UI doesn't wait a full interval.
    await PollOnceAsync(ct);
    try
    {
      while (await _timer!.WaitForNextTickAsync(ct))
      {
        await PollOnceAsync(ct);
      }
    }
    catch (OperationCanceledException) { /* shutting down */ }
  }

  private async Task PollOnceAsync(CancellationToken ct)
  {
    try
    {
      using var scope = _scopeFactory.CreateScope();
      var api = scope.ServiceProvider.GetRequiredService<GvBridgeApiService>();
      var status = await api.GetStatusAsync(ct);
      ApplyStatus(status);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "GV status poll failed; treating as degraded");
      ApplyStatus(null);
    }
  }

  // Test-facing wrapper so unit tests can drive state without a scope factory.
  public void ApplyStatusForTest(GvBridgeStatusDto? status) => ApplyStatus(status);

  private void ApplyStatus(GvBridgeStatusDto? status)
  {
    Current = status;
    IsAvailable = status is { Available: true };
    StatusChanged?.Invoke(status);
  }

  public async ValueTask DisposeAsync()
  {
    _cts?.Cancel();
    _timer?.Dispose();
    if (_loop != null)
    {
      try { await _loop; } catch { /* ignore */ }
    }
    _cts?.Dispose();
  }
}
```

- [ ] **Step 3: Run the test**

`dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeStatusServiceTests"`

- [ ] **Step 4: Commit**

```bash
git add src/Radio.Web/Services/GvBridgeStatusService.cs tests/Radio.Web.Tests/Services/GvBridgeStatusServiceTests.cs
git commit -m "feat(web): add GvBridgeStatusService singleton with ~10s status poll"
```

---

## Chunk 6: Shared unread-count state (topbar badge source)

### Task 6: Create PhoneUnreadState

**Files:**
- Create: `src/Radio.Web/Services/PhoneUnreadState.cs`

> Handoff Open Decision 7 / Badge model: the topbar `/phone` pill badge needs the unread sum surfaced to `MainLayout`. A tiny singleton holds the count; `PhonePage` writes it, `MainLayout` reads + subscribes. Counts are UI-local truth in v1.

- [ ] **Step 1: Implement**

```csharp
namespace Radio.Web.Services;

/// <summary>
/// UI-local unread sum (unheard voicemail + unread SMS threads + missed calls)
/// surfaced from PhonePage to the topbar /phone pill badge in MainLayout.
/// Singleton so both the page and the layout share one source of truth.
/// v1 counts are UI-local only — a hard reload re-derives from isRead/hasUnread
/// (handoff Badge model). Missed calls DO contribute (owner decision 2).
/// </summary>
public sealed class PhoneUnreadState
{
  private int _count;
  public int Count => _count;
  public event Action<int>? Changed;

  public void Set(int count)
  {
    if (count == _count) return;
    _count = count < 0 ? 0 : count;
    Changed?.Invoke(_count);
  }
}
```

- [ ] **Step 2: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Services/PhoneUnreadState.cs
git commit -m "feat(web): add PhoneUnreadState shared count for topbar badge"
```

---

## Chunk 7: Config + DI wiring

### Task 7: Config keys

**Files:**
- Modify: `src/Radio.Web/appsettings.json`
- Modify/Create: `src/Radio.Web/appsettings.Production.json`

- [ ] **Step 1: Extend the `RotaryPhone` section in `appsettings.json`**

Replace the existing block (lines 17–20):

```json
  "RotaryPhone": {
    "HubUrl": "http://radio:5004/hub",
    "ApiBaseUrl": "http://radio:5004",
    "Gv": {
      "SendEnabled": false,
      "StatusPollSeconds": 10,
      "AuthKey": ""
    }
  },
```

- [ ] **Step 2: Document per-machine overrides in `appsettings.Production.json`**

Deploy overwrites `appsettings.json`, so per-machine values live here (memory: "always use appsettings.Production.json for per-machine overrides"). If the file exists, merge the `RotaryPhone` block; if not, create it with at least:

```json
{
  "RotaryPhone": {
    "ApiBaseUrl": "http://radio:5004",
    "HubUrl": "http://radio:5004/hub",
    "Gv": {
      "SendEnabled": false,
      "AuthKey": ""
    }
  }
}
```

(Comment in the PR description: flip `Gv:SendEnabled` to `true` and set `Gv:AuthKey` here when RotaryPhone's send endpoint / auth gate ship.)

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/appsettings.json src/Radio.Web/appsettings.Production.json
git commit -m "feat(web): add RotaryPhone:Gv config keys (SendEnabled/StatusPollSeconds/AuthKey)"
```

### Task 8: Program.cs registrations

**Files:**
- Modify: `src/Radio.Web/Program.cs`

- [ ] **Step 1: Register the auth handler (transient) before the GV clients**

Near the other handler registrations (before line 311). `ApiConnectionLoggingHandler` is already registered; add:

```csharp
builder.Services.AddTransient<Radio.Web.Services.Http.RotaryPhoneAuthHandler>();
```

- [ ] **Step 2: Add the auth handler to the GV `HttpClient`s**

On the `GvBridgeApiService` registration (lines 326–337) add the handler in the chain (after the logging handler):

```csharp
builder.Services.AddHttpClient<GvBridgeApiService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.AddHttpMessageHandler<Radio.Web.Services.Http.RotaryPhoneAuthHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});
```

(Do the same `.AddHttpMessageHandler<RotaryPhoneAuthHandler>()` insertion on the `PhoneApiService` client at lines 312–323, since GV push + REST share the host. The `GvBridgeSendService` client is added in PR3.)

- [ ] **Step 3: Register `PhoneUnreadState` + `GvBridgeStatusService` singletons**

After the hub singletons (line 371):

```csharp
builder.Services.AddSingleton<Radio.Web.Services.PhoneUnreadState>();
builder.Services.AddSingleton<Radio.Web.Services.GvBridgeStatusService>(sp =>
  new Radio.Web.Services.GvBridgeStatusService(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<Radio.Web.Services.GvBridgeStatusService>>(),
    builder.Configuration.GetValue("RotaryPhone:Gv:StatusPollSeconds", 10)));
```

- [ ] **Step 4: Start the status poll at boot (next to the hub starts)**

After line 497 (`_ = gvTrunkHub.StartAsync();`):

```csharp
var gvStatusService = app.Services.GetRequiredService<Radio.Web.Services.GvBridgeStatusService>();
gvStatusService.Start();
```

- [ ] **Step 5: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Program.cs
git commit -m "feat(web): wire auth handler + GvBridgeStatusService + PhoneUnreadState in DI"
```

---

## Chunk 8: IA shell — PhoneMessagesPanel + PhonePage restructure

### Task 9: Create PhoneMessagesPanel (feed shell, calls-only in PR1)

**Files:**
- Create: `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor`

> This panel is the unified feed host. In PR1 it renders the **segmented filter**, the **reconnecting banner**, the **detail pane host**, and **call rows** (data already exists via `CallHistory`). Voicemail rows (PR2) and text rows/conversation (PR3) are filled later behind `@* PR2 *@` / `@* PR3 *@` extension points. The segmented filter reuses `.mode-selector`/`.mode-btn` (the Dashboard Active-Mode control). All classes referenced below already exist in `design-system.css` (verified: `.mode-selector`, `.mode-btn`, `.list-item-touch`, `.empty-state`, `.skeleton-list-row`, `.nav-badge`, `.list-item-add`).

- [ ] **Step 1: Component shell + parameters**

```razor
@using Radio.Web.Models
@using Radio.Web.Services

@* Unified Messages feed (Designer Option C). Segmented filter over one
   newest-first feed. PR1 = shell + call rows; PR2 adds voicemail rows + player;
   PR3 adds text thread rows + conversation. *@

<div class="phone-messages">
  @if (!GvAvailable)
  {
    <div class="gv-reconnect-banner" role="status">
      <RadzenIcon Icon="sync_problem" />
      <span>Google Voice is reconnecting — voicemail and texts may be delayed.</span>
    </div>
  }

  <div class="panel-header">
    <span class="panel-header-title">MESSAGES</span>
    <button type="button" class="phone-btn-sm btn-ghost"
            aria-label="Refresh messages" @onclick="OnRefresh">
      <RadzenIcon Icon="refresh" />
    </button>
  </div>

  <div class="mode-selector" role="tablist" aria-label="Message filter">
    @foreach (var seg in Segments)
    {
      <button type="button"
              class="mode-btn @(_filter == seg.Key ? "active" : "")"
              role="tab" aria-selected="@(_filter == seg.Key)"
              @onclick="@(() => _filter = seg.Key)">
        <span>@seg.Label</span>
        @if (seg.Count > 0)
        {
          <span class="phone-pill cyan">@seg.Count</span>
        }
      </button>
    }
  </div>

  <div class="phone-messages-body">
    <div class="phone-messages-feed">
      @RenderFeed()
    </div>
    <div class="phone-messages-detail">
      @RenderDetail()
    </div>
  </div>
</div>

@code {
  [Parameter] public bool GvAvailable { get; set; } = true;
  [Parameter] public List<CallHistoryEntryDto>? CallHistory { get; set; }
  [Parameter] public List<MergedContact> Contacts { get; set; } = [];
  [Parameter] public int UnheardVoicemailCount { get; set; }   // PR2 feeds this
  [Parameter] public int UnreadThreadCount { get; set; }       // PR3 feeds this
  [Parameter] public EventCallback OnRefresh { get; set; }

  private string _filter = "all";

  private IEnumerable<(string Key, string Label, int Count)> Segments => new[]
  {
    ("all", "All", 0),
    ("voicemail", "Voicemail", UnheardVoicemailCount),
    ("texts", "Texts", UnreadThreadCount),
    ("calls", "Calls", 0)
  };
}
```

- [ ] **Step 2: Feed + detail render fragments (calls only in PR1)**

Add to `@code` (RenderFeed/RenderDetail use `RenderFragment` builders or local methods returning markup; simplest is markup methods). Implement call rows using the existing direction-icon/colour logic so the rows match `PhoneHistoryPanel` verbatim (copy `GetCallDirectionIcon`/`GetCallDirectionColor`/`FormatDuration` from `PhoneHistoryPanel.razor` lines 89–119 — or, preferred, lift those three statics into a shared `PhoneCallFormatting` static helper in `Components/Pages/` and reference from both; do the lift here so PR2/PR3 reuse it):

```razor
@* RenderFeed: in PR1, only "all" and "calls" produce rows (call data only). *@
@functions {
  private RenderFragment RenderFeed() => builder =>
  {
    var showCalls = _filter is "all" or "calls";
    if (!showCalls)
    {
      // PR2 (voicemail) / PR3 (texts) fill these filters; until then show empty.
      BuildEmpty(builder, _filter == "voicemail" ? "voicemail" : "forum",
        _filter == "voicemail" ? "Voicemail coming online…" : "Texts coming online…");
      return;
    }

    if (CallHistory == null)
    {
      BuildSkeleton(builder, 5);
      return;
    }
    if (CallHistory.Count == 0)
    {
      BuildEmpty(builder, "call", "No recent calls.");
      return;
    }

    var seq = 0;
    foreach (var entry in CallHistory.OrderByDescending(c => c.StartTime))
    {
      var captured = entry;
      builder.OpenElement(seq++, "button");
      builder.AddAttribute(seq++, "type", "button");
      builder.AddAttribute(seq++, "class",
        "list-item-touch" + (_selectedCall == captured ? " list-item-active" : ""));
      builder.AddAttribute(seq++, "onclick",
        EventCallback.Factory.Create(this, () => _selectedCall = captured));
      // chip + title + type label + when + duration + chevron
      // (use PhoneCallFormatting.GetCallDirectionIcon/Color + FormatDuration +
      //  ResolveName(captured) — full literal markup omitted here for brevity but
      //  MUST be emitted; mirror PhoneHistoryPanel's row layout.)
      builder.CloseElement();
    }
  };

  private RenderFragment RenderDetail() => builder =>
  {
    if (_selectedCall == null)
    {
      BuildEmpty(builder, "forum", "Pick a message to open it here.");
      return;
    }
    // Call-detail card: caller, number (mono), direction, when (local),
    // duration (FormatDuration), answered-on pill. Call back / Text back are
    // NOT in v1 (decision 3) — omit those buttons for now.
  };
}
```

> **Note for the implementer:** the row/detail markup above is abbreviated to keep the plan readable, but it MUST be emitted as full literal markup. Because the feed is data-bound and the row grid is identical to `PhoneHistoryPanel`'s, the cleanest implementation is to lift `PhoneHistoryPanel`'s row template into a small shared `@helper`/`RenderFragment` (e.g. `PhoneFeedRows.razor` or a static markup method) and call it from both. The implementer should reuse `PhoneHistoryPanel.razor`'s existing row markup verbatim rather than re-author it. **Do NOT add Call back / Text back buttons** — those are deferred (owner decision 3); leave a `@* fast-follow: Call back / Text back (deferred) *@` marker.

- [ ] **Step 3: Shared helpers (skeleton/empty + name resolution)**

```razor
@code {
  private CallHistoryEntryDto? _selectedCall;

  private void BuildSkeleton(RenderTreeBuilder b, int rows)
  {
    var s = 0;
    for (var i = 0; i < rows; i++)
    {
      b.OpenElement(s++, "div");
      b.AddAttribute(s++, "class", "skeleton-list-row");
      b.OpenElement(s++, "div");
      b.AddAttribute(s++, "class", "skeleton skeleton-list-row-text");
      b.CloseElement();
      b.CloseElement();
    }
  }

  private void BuildEmpty(RenderTreeBuilder b, string icon, string text)
  {
    var s = 0;
    b.OpenElement(s++, "div");
    b.AddAttribute(s++, "class", "empty-state");
    b.OpenComponent<RadzenIcon>(s++);
    b.AddAttribute(s++, "Icon", icon);
    b.AddAttribute(s++, "class", "empty-state-icon");
    b.CloseComponent();
    b.OpenElement(s++, "div");
    b.AddAttribute(s++, "class", "empty-state-text");
    b.AddContent(s++, text);
    b.CloseElement();
    b.CloseElement();
  }

  private string ResolveName(CallHistoryEntryDto e)
  {
    if (!string.IsNullOrWhiteSpace(e.CallerName)) return e.CallerName!;
    var key = PhoneNumberNormalizer.Normalize(e.PhoneNumber);
    var match = Contacts.FirstOrDefault(c =>
      PhoneNumberNormalizer.Normalize(c.PhoneNumber) == key);
    return match?.Name ?? e.PhoneNumber;
  }
}
```

(If `RenderTreeBuilder`-heavy code reads awkwardly, the implementer MAY instead author the feed/detail as plain `@if/@foreach` Razor markup — that is preferred for readability. The builder snippets above only fix the contract: skeleton, empty, rows, detail card, the four filters, and selection state. Use whichever renders the same DOM with the same classes.)

- [ ] **Step 4: Add the layout CSS for the panel (no new tokens)**

In `design-system.css` §Ph, add the panel grid + the reconnecting banner (the banner CSS is from the handoff verbatim). These are layout dims only (house style allows hardcoded layout dims; zero `:root` changes):

```css
/* §Ph — Messages panel layout (PR1) */
.phone-messages { display: flex; flex-direction: column; height: 600px; min-height: 0; }
.phone-messages-body {
  display: grid; grid-template-columns: 1fr 520px;
  flex: 1; min-height: 0; overflow: hidden;
}
.phone-messages-feed { overflow-y: auto; min-height: 0; }
.phone-messages-detail {
  border-left: 1px solid var(--surface-separator);
  overflow-y: auto; min-height: 0;
}

/* §Ph — GV reconnecting banner (handoff verbatim) */
.gv-reconnect-banner {
  display: flex; align-items: center; gap: var(--sp-2);
  padding: var(--sp-2) var(--sp-4);
  background: rgba(240,168,48,0.10);
  border-bottom: 1px solid var(--surface-separator);
  color: var(--signal-amber);
  font-family: var(--font-body); font-size: 13px;
}
```

- [ ] **Step 5: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor src/Radio.Web/wwwroot/css/design-system.css
git commit -m "feat(web): add PhoneMessagesPanel feed shell (segmented filter + calls + banner)"
```

### Task 10: Restructure PhonePage IA (Messages default + More ▸ rail + badges)

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor`

- [ ] **Step 1: Rebuild the rail**

Replace the `.phone-tab-rail` block (lines 15–37) with Messages as the default-active top entry, a hairline divider, and a "More ▸" toggle that expands the four legacy tabs indented beneath it:

```razor
<div class="phone-tab-rail">
  <span class="phone-rail-heading">Phone</span>

  <button type="button" class="phone-rail-tab @(IsTab("messages") ? "active" : "")"
          @onclick='@(() => _activeTab = "messages")'>
    <RadzenIcon Icon="forum" />
    <span class="phone-rail-label">Messages</span>
    @if (UnreadSum > 0)
    {
      <span class="nav-badge" aria-hidden="true">@UnreadSum</span>
    }
  </button>

  <div class="phone-rail-divider"></div>

  <button type="button" class="phone-rail-tab phone-rail-more"
          aria-expanded="@_moreExpanded"
          @onclick="@(() => _moreExpanded = !_moreExpanded)">
    <RadzenIcon Icon="@(_moreExpanded ? "expand_more" : "chevron_right")" />
    <span class="phone-rail-label">More</span>
  </button>

  @if (_moreExpanded)
  {
    <button type="button" class="phone-rail-tab phone-rail-sub @(IsTab("dashboard") ? "active" : "")"
            @onclick='@(() => _activeTab = "dashboard")'>
      <RadzenIcon Icon="dashboard" /><span class="phone-rail-label">Dashboard</span>
    </button>
    <button type="button" class="phone-rail-tab phone-rail-sub @(IsTab("contacts") ? "active" : "")"
            @onclick='@(() => _activeTab = "contacts")'>
      <RadzenIcon Icon="contacts" /><span class="phone-rail-label">Contacts</span>
    </button>
    <button type="button" class="phone-rail-tab phone-rail-sub @(IsTab("history") ? "active" : "")"
            @onclick='@(() => _activeTab = "history")'>
      <RadzenIcon Icon="dialpad" /><span class="phone-rail-label">Dialer</span>
    </button>
    <button type="button" class="phone-rail-tab phone-rail-sub @(IsTab("diagnostics") ? "active" : "")"
            @onclick='@(() => _activeTab = "diagnostics")'>
      <RadzenIcon Icon="monitor_heart" /><span class="phone-rail-label">Diagnostics</span>
    </button>
  }
</div>
```

- [ ] **Step 2: Add the Messages content branch (before the dashboard branch)**

In the content `<div>` (after line 39), add the first branch and keep the four legacy branches unchanged below it:

```razor
@if (_activeTab == "messages")
{
  <PhoneMessagesPanel GvAvailable="_gvBridgeAvailable"
                      CallHistory="_callHistory"
                      Contacts="MergedContacts"
                      UnheardVoicemailCount="0"
                      UnreadThreadCount="0"
                      OnRefresh="RefreshMessagesAsync" />
}
else if (_activeTab == "dashboard")
{
  @* ...existing PhoneDashboardPanel unchanged... *@
}
```

> Voicemail/text counts are `0` in PR1 (no data yet). PR2 wires `UnheardVoicemailCount`; PR3 wires `UnreadThreadCount`. The `RefreshMessagesAsync` handler in PR1 just re-fetches call history (`_callHistory = await PhoneApi.GetCallHistoryAsync();`).

- [ ] **Step 3: Flip the default + add state + badge + status subscription**

In `@code`:
- Change `private string _activeTab = "dashboard";` → `private string _activeTab = "messages";`
- Add `private bool _moreExpanded;`
- Inject the new singletons at the top of the file:
  ```razor
  @inject GvBridgeStatusService GvBridgeStatus
  @inject PhoneUnreadState PhoneUnread
  ```
- Add a computed badge sum (PR1: voicemail/text counts are 0; missed calls DO count per decision 2):
  ```csharp
  private int MissedCallCount => _callHistory?
    .Count(c => c.Direction == CallDirection.Incoming
             && c.AnsweredOn == CallAnsweredOn.NotAnswered) ?? 0;
  // PR2/PR3 add _unheardVoicemail + _unreadThreads here.
  private int UnreadSum => MissedCallCount;
  ```
- In `OnInitializedAsync`, subscribe to status changes and seed `_gvBridgeAvailable` from the shared service, then publish the count:
  ```csharp
  _gvBridgeAvailable = GvBridgeStatus.IsAvailable;
  GvBridgeStatus.StatusChanged += OnGvStatusChanged;
  PhoneUnread.Set(UnreadSum);
  ```
- Add the handler + a publish helper:
  ```csharp
  private void OnGvStatusChanged(GvBridgeStatusDto? status)
  {
    if (_disposed) return;
    _gvBridgeAvailable = GvBridgeStatus.IsAvailable;
    PhoneUnread.Set(UnreadSum);
    _ = InvokeAsync(StateHasChanged);
  }

  private async Task RefreshMessagesAsync()
  {
    var history = await PhoneApi.GetCallHistoryAsync();
    if (history != null) _callHistory = history;
    PhoneUnread.Set(UnreadSum);
    await InvokeAsync(StateHasChanged);
  }
  ```
- In `OnCallHistoryUpdated` / `PollStatusAsync` after `_callHistory` updates, call `PhoneUnread.Set(UnreadSum);` so missed-call badging stays live.
- In `Dispose`, add `GvBridgeStatus.StatusChanged -= OnGvStatusChanged;`.

- [ ] **Step 4: Rail divider/sub-tab CSS (no new tokens)**

In `design-system.css` §Ph:

```css
.phone-rail-divider { height: 1px; background: var(--surface-separator); margin: var(--sp-2) 0; }
.phone-rail-sub .phone-rail-label { padding-left: var(--sp-3); font-size: 13px; }
.phone-rail-tab .nav-badge { position: absolute; top: 6px; right: 6px; }
.phone-rail-tab { position: relative; }
```

- [ ] **Step 5: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Components/Pages/PhonePage.razor src/Radio.Web/wwwroot/css/design-system.css
git commit -m "feat(web): restructure PhonePage IA — Messages default + More rail + missed-call badge"
```

### Task 11: Topbar /phone pill badge (MainLayout)

**Files:**
- Modify: `src/Radio.Web/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Inject + subscribe**

Add `@inject PhoneUnreadState PhoneUnread` and `@implements IDisposable` (if not already). In the code block subscribe in `OnInitialized`:

```csharp
protected override void OnInitialized()
{
  _phoneUnread = PhoneUnread.Count;
  PhoneUnread.Changed += OnPhoneUnreadChanged;
}
private int _phoneUnread;
private void OnPhoneUnreadChanged(int c)
{
  _phoneUnread = c;
  InvokeAsync(StateHasChanged);
}
public void Dispose() => PhoneUnread.Changed -= OnPhoneUnreadChanged;
```

(If `MainLayout` already implements `IDisposable`/`OnInitialized`, merge into the existing members rather than duplicating.)

- [ ] **Step 2: Render the badge on the `/phone` pill (mirror the Queue pill at line ~121)**

On the `/phone` nav pill (the phone icon link, ~line 140), add inside the pill:

```razor
@if (_phoneUnread > 0)
{
  <span class="nav-badge" aria-hidden="true">@_phoneUnread</span>
}
```

And set the accessible label on the pill: `aria-label="@($"Phone, {_phoneUnread} unread")"` (or `"Phone"` when 0).

- [ ] **Step 3: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Components/Layout/MainLayout.razor
git commit -m "feat(web): add unread badge to topbar /phone pill (UI-local count)"
```

---

## Chunk 9: Documentation

### Task 12: FUTURE-WORK + INTEGRATIONS

**Files:**
- Modify: `design/FUTURE-WORK.md`
- Modify: `design/INTEGRATIONS.md` (per memory rule — integration service change)

- [ ] **Step 1: Add a FUTURE-WORK entry**

Document, with the standard what-exists / what's-needed / gotchas / priority structure:
- **UI-local voicemail/SMS read-state** — heard/read does not persist to GV; hard reload re-derives from `isRead`/`hasUnread`. Needs a GV mark-read endpoint on RotaryPhone (decision 4). Gotcha: missed-call badging is also UI-local.
- **GV mark-read client seam** — the flagged client method (added in PR2) that becomes the wire call when RotaryPhone ships GV mark-read.
- **GV SMS send** — flagged off (`RotaryPhone:Gv:SendEnabled=false`); built in PR3, lights up when `POST /api/gvbridge/sms/send` ships. Confirm `SendSmsResponse` shape first.
- **RotaryPhoneAuthHandler** — header seam OFF (`RotaryPhone:Gv:AuthKey` empty). Gotcha: native `<audio>` cannot send the header — if the audio endpoint ever becomes auth-required, the direct-`<audio>` approach breaks (ADR §8.1).

- [ ] **Step 2: Update INTEGRATIONS.md**

Add a "Google Voice (gvbridge) Messages" subsection: base host `radio:5004`, REST under `/api/gvbridge/*`, push on `/hub` (`SmsReceived`/`VoicemailReceived`), the config keys, and the GV-SMS-vs-trunk-SMS distinction.

- [ ] **Step 3: Commit**

```bash
git add design/FUTURE-WORK.md design/INTEGRATIONS.md
git commit -m "docs: document GV messages UI-local state, send/auth seams (PR1)"
```

---

## Test Plan

**Unit (must pass before PR):**
- `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests|FullyQualifiedName~GvBridgeStatusServiceTests|FullyQualifiedName~RotaryPhoneAuthHandlerTests"` — all green.
- Full suite: `dotnet test --configuration Release` — no regressions (known-flaky `AudioApiService` timeout tests excluded per memory).
- Build: `dotnet build --configuration Release` — 0 warnings (warnings-as-errors in Release).

**Specific assertions covered by unit tests:**
- `GetVoicemailAudioUrl("vm1")` returns the **absolute** `http://radio:5004/api/gvbridge/voicemail/vm1/audio` (contract risk #3 — the silent-failure point).
- Read methods return `null` on non-200 (UI error-state contract).
- `RotaryPhoneAuthHandler` sends **no** header when key empty/missing; sends it when set.
- `GvBridgeStatusService.IsAvailable` is `false` on null status, `true` on `Available==true`; `StatusChanged` fires each poll.

**UAT (Tester, at 1920×720, deploy first via `./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`):**
1. `/phone` lands on **Messages** (not Dashboard); no shell vertical scrollbar.
2. Segmented filter shows `All · Voicemail · Texts · Calls`; All/Calls render call rows newest-first; Voicemail/Texts show the "coming online" placeholder (PR1 only).
3. Tapping a call row selects it (cyan left border) and renders the call-detail card in the 520px detail pane; no Call back / Text back buttons (deferred).
4. **More ▸** expands to reveal Dashboard / Contacts / Dialer / Diagnostics; each opens the **unchanged** legacy panel; collapse hides them.
5. A missed call in history makes the **Messages rail badge** and the **topbar /phone pill badge** show the missed-call count (decision 2). Answering/clearing updates it.
6. Stop the RotaryPhone gvbridge (or point `ApiBaseUrl` at a dead host): within ~10s the **calm amber reconnecting banner** appears above the filter; restoring it auto-clears the banner.
7. Console: no unhandled exceptions; no 404s for `/api/gvbridge/*` when the bridge is up.

**Self-review checklist (Planner ran):**
- No placeholders/TBD in shipped code paths (the abbreviated row markup in Task 9 is explicitly flagged "must be emitted as full literal markup" with the reuse path named — not a code TBD, an implementer instruction).
- Stale "no SMS routes" comment deleted (contract risk #1).
- GV event named `GvSmsReceived`, on `PhoneHubService`/`/hub` (contract risk #2).
- Audio URL rebuilt absolute (contract risk #3).
- Auth handler OFF by default; missed calls count toward badge (decisions baked in).
