# GV Messages — PR2: Voicemail Surface

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the **Voicemail** half of the Messages feed: voicemail rows (all list states), the inline accordion **player** with a seekable Range-backed scrubber (all playback + transcript states, incl. 502 audio error), the `GvVoicemailReceived` SignalR new-arrival path (never steal screen / never pause audio), the calm reconnecting-banner gate (already wired in PR1, now consumed by voicemail), and the **UI-local mark-heard + flagged GV mark-read client seam** (decision 4).

**Owner-baked decisions in scope here:**
- Voicemail rows fold into the unified feed (Designer Option C) — under the **Voicemail** filter and interleaved in **All**.
- **UI-local read-state is the functional behavior** in v1; add a **flagged no-op mark-read client method** mirroring the send pattern (decision 4). No GV mark-read endpoint exists yet.
- **No Call back / Text back quick actions** in the player (decision 3) — leave a documented fast-follow marker.

**Sources of truth (do not redesign):**
- Design handoff Screen A + Screen B: `docs/design-handoffs/HANDOFF-phone-messages-voicemail-sms.md`
- ADR-022 D4 (audio URL), D5 (push), D6 (status): `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md`
- Contract (voicemail HTTP + audio 502 + Range): `D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-voicemail-sms-ui-handoff.md`

**Tech stack:** Blazor Server, Radzen, native HTML5 `<audio>` + minimal JS interop for seek/timeupdate, `design-system.css` tokens.

**Dependencies:** **PR1 must be merged.** This PR consumes PR1's DTOs (`VoicemailItemDto`/`VoicemailListDto`), `GvBridgeApiService.GetVoicemailsAsync`/`GetVoicemailAsync`/`GetVoicemailAudioUrl`, `PhoneHubService.GvVoicemailReceived`, `GvBridgeStatusService`, `PhoneUnreadState`, and `PhoneMessagesPanel`'s extension points.

---

## File Map

### New files

| File | Responsibility |
|------|---------------|
| `src/Radio.Web/Components/Pages/VoicemailRow.razor` | A single voicemail feed row (chip + unread dot + caller + preview + duration + chevron) with the inline accordion player slot. |
| `src/Radio.Web/Components/Pages/VoicemailPlayer.razor` | Inline accordion: transport, seekable scrubber, time readout, transcript states, audio-error state. |
| `src/Radio.Web/wwwroot/js/voicemail-player.js` | Tiny JS interop module: play/pause, seek from tap fraction, `timeupdate`/`ended`/`error` callbacks to .NET. |
| `tests/Radio.Web.Tests/Components/VoicemailRowTests.cs` | bUnit: row states (unread dot, duration "—" on 0, transcript preview fallbacks). |
| `tests/Radio.Web.Tests/Components/VoicemailPlayerTests.cs` | bUnit: idle/buffering/error transitions, transcript present/pending/absent, audio src is the absolute URL. |

### Modified files

| File | Changes |
|------|---------|
| `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` | Add the **flagged mark-read client seam** (`MarkVoicemailReadAsync`, no-op today). |
| `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor` | Render voicemail rows in the `voicemail` + `all` filters; own the voicemail list + states + new-arrival; expose `UnheardVoicemailCount` upward; accordion open/close (one at a time). |
| `src/Radio.Web/Components/Pages/PhonePage.razor` | Pass the live unheard-voicemail count into `PhoneMessagesPanel`; fold voicemail into `UnreadSum`; subscribe to `GvVoicemailReceived`; toast on new arrival. |
| `src/Radio.Web/wwwroot/css/design-system.css` §Ph | Add voicemail row + player + scrubber-hit-area styles (no new tokens). |
| `design/FUTURE-WORK.md` | Note the mark-read seam is wired-but-no-op; GV mark-read endpoint requested from RotaryPhone. |

---

## Chunk 1: Mark-read client seam (flagged, no-op)

### Task 1: Add MarkVoicemailReadAsync to GvBridgeApiService

**Files:**
- Modify: `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`
- Test: `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs` (extend PR1's file)

> Decision 4: v1 ships UI-local read-state. This method is the **single flagged seam** that becomes the wire call when RotaryPhone ships GV mark-read. It mirrors the send pattern: it is wired into the call sites now, returns `false` (no-op), and is gated on a config flag so flipping the flag + adding the route lights it up. **Do not** make it throw — mark-read is fire-and-forget; a no-op must be silent so the UI's optimistic local flip is never disturbed.

- [ ] **Step 1: Add a failing test**

```csharp
[Fact]
public async Task MarkVoicemailReadAsync_NoOps_WhenFlagOff()
{
  // No HTTP call should be made; returns false (not-persisted).
  var handler = new MockHttpHandler("{}");  // would 200 if called
  var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
  var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
      { ["RotaryPhone:Gv:MarkReadEnabled"] = "false" })
    .Build();
  var svc = new GvBridgeApiService(client,
    NullLogger<GvBridgeApiService>.Instance, config);

  var result = await svc.MarkVoicemailReadAsync("vm1");

  Assert.False(result);
  Assert.Equal(0, handler.RequestCount);  // never hit the network
}
```

> The `MockHttpHandler` used across `Radio.Web.Tests` must expose a `RequestCount`. If it does not yet, add a small `public int RequestCount` increment in its `SendAsync` (one-line change in the existing test helper). PR1's tests still pass.

- [ ] **Step 2: Thread `IConfiguration` into `GvBridgeApiService`**

Add an `IConfiguration` constructor param (the typed-`HttpClient` DI supplies it automatically):

```csharp
private readonly IConfiguration _configuration;

public GvBridgeApiService(HttpClient httpClient,
  ILogger<GvBridgeApiService> logger, IConfiguration configuration)
{
  _httpClient = httpClient;
  _logger = logger;
  _configuration = configuration;
}
```

- [ ] **Step 3: Implement the flagged seam**

```csharp
/// <summary>
/// FLAGGED SEAM (decision 4). v1 read-state is UI-local; there is no GV
/// mark-read endpoint yet. When RotaryPhone ships POST
/// /api/gvbridge/voicemail/{id}/read, flip RotaryPhone:Gv:MarkReadEnabled=true
/// and this becomes the wire call. Today it is a silent no-op returning false
/// (not persisted) — the caller has ALREADY flipped the row heard locally, so a
/// no-op must never disturb that. Fire-and-forget; never throws.
/// </summary>
public async Task<bool> MarkVoicemailReadAsync(string id, CancellationToken ct = default)
{
  if (!_configuration.GetValue("RotaryPhone:Gv:MarkReadEnabled", false))
  {
    return false;  // UI-local only in v1
  }
  try
  {
    var response = await _httpClient.PostAsync(
      $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}/read", null, ct);
    return response.IsSuccessStatusCode;
  }
  catch (Exception ex)
  {
    _logger.LogDebug(ex, "Mark-read failed for voicemail {Id} (non-fatal)", id);
    return false;
  }
}
```

- [ ] **Step 4: Add the config default**

In `appsettings.json` under `RotaryPhone:Gv`, add `"MarkReadEnabled": false`.

- [ ] **Step 5: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests"
git add src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs src/Radio.Web/appsettings.json tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs
git commit -m "feat(web): add flagged no-op MarkVoicemailReadAsync seam (UI-local read-state v1)"
```

---

## Chunk 2: Voicemail player JS interop

### Task 2: voicemail-player.js

**Files:**
- Create: `src/Radio.Web/wwwroot/js/voicemail-player.js`
- Modify: `src/Radio.Web/Components/App.razor` (or `_Host`/layout) to reference the module if not auto-loaded (prefer `JSRuntime.InvokeAsync<IJSObjectReference>("import", ...)` from the component — no global script tag needed).

> The scrubber must seek without re-download (Range works on the direct `radio:5004` audio endpoint — ADR D4). Native `<audio>` already does Range; the JS only wires play/pause, sets `currentTime` from a tap fraction, and pushes `timeupdate`/`ended`/`error` back to .NET. Keep it tiny and module-scoped.

- [ ] **Step 1: Write the module**

```javascript
// Tiny interop for the voicemail inline player. The <audio> element does Range
// natively against radio:5004 (ADR D4) — this only bridges events to .NET.
export function attach(audio, dotnet) {
  if (!audio) return null;
  const onTime = () => dotnet.invokeMethodAsync('OnTimeUpdate',
    audio.currentTime || 0, isFinite(audio.duration) ? audio.duration : 0);
  const onEnded = () => dotnet.invokeMethodAsync('OnEnded');
  const onError = () => dotnet.invokeMethodAsync('OnAudioError');
  const onPlaying = () => dotnet.invokeMethodAsync('OnPlaying');
  const onWaiting = () => dotnet.invokeMethodAsync('OnBuffering');
  audio.addEventListener('timeupdate', onTime);
  audio.addEventListener('ended', onEnded);
  audio.addEventListener('error', onError);
  audio.addEventListener('playing', onPlaying);
  audio.addEventListener('waiting', onWaiting);
  return {
    play: () => audio.play().catch(() => dotnet.invokeMethodAsync('OnAudioError')),
    pause: () => audio.pause(),
    // fraction in [0,1] from the tap x over the scrubber width
    seekFraction: (f) => {
      if (isFinite(audio.duration) && audio.duration > 0) {
        audio.currentTime = Math.max(0, Math.min(1, f)) * audio.duration;
      }
    },
    dispose: () => {
      audio.removeEventListener('timeupdate', onTime);
      audio.removeEventListener('ended', onEnded);
      audio.removeEventListener('error', onError);
      audio.removeEventListener('playing', onPlaying);
      audio.removeEventListener('waiting', onWaiting);
    }
  };
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Radio.Web/wwwroot/js/voicemail-player.js
git commit -m "feat(web): add voicemail-player JS interop module (seek + event bridge)"
```

---

## Chunk 3: VoicemailPlayer component

### Task 3: VoicemailPlayer.razor

**Files:**
- Create: `src/Radio.Web/Components/Pages/VoicemailPlayer.razor`
- Test: `tests/Radio.Web.Tests/Components/VoicemailPlayerTests.cs`

> Screen B. Player opens **inline under the row** (accordion). States: idle/ready, buffering (first play), playing, paused, ended, audio-error (incl. 502 surfaced as the HTML5 `error` event). Transcript: present / pending (null + recent) / absent (null + not recent). Mark-heard fires on open/play (UI-local flip + flagged seam call). Scrubber hit area ≥24px; transport is a real `<button>`. The `<audio src>` MUST be the **absolute** URL from `GvBridgeApiService.GetVoicemailAudioUrl(id)` (never the relative DTO field).

- [ ] **Step 1: Write the failing bUnit test**

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Components.Pages;

namespace Radio.Web.Tests.Components;

public class VoicemailPlayerTests : TestContext
{
  private VoicemailItemDto Vm(int duration = 42, string? transcript = "hi",
    DateTime? received = null) =>
    new("vm1", "t1", "+15551234567", "Jane",
      received ?? DateTime.UtcNow, duration, false, transcript,
      "/api/gvbridge/voicemail/vm1/audio");

  private void Register()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;  // MudBlazor/JS-interop components
    var client = new HttpClient(new MockHttpHandler("{}"))
    { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder().Build();
    Services.AddSingleton(new GvBridgeApiService(client,
      NullLogger<GvBridgeApiService>.Instance, config));
  }

  [Fact]
  public void Renders_AbsoluteAudioSrc()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p => p.Add(x => x.Item, Vm()));
    var audio = cut.Find("audio");
    Assert.Equal("http://radio:5004/api/gvbridge/voicemail/vm1/audio",
      audio.GetAttribute("src"));
  }

  [Fact]
  public void TranscriptPresent_RendersBody()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p => p.Add(x => x.Item, Vm(transcript: "Hello there")));
    Assert.Contains("Hello there", cut.Markup);
  }

  [Fact]
  public void TranscriptPending_WhenNullAndRecent()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p =>
      p.Add(x => x.Item, Vm(transcript: null, received: DateTime.UtcNow)));
    Assert.Contains("Transcript pending", cut.Markup);
  }

  [Fact]
  public void TranscriptAbsent_WhenNullAndOld()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p =>
      p.Add(x => x.Item, Vm(transcript: null, received: DateTime.UtcNow.AddHours(-2))));
    Assert.Contains("No transcript available", cut.Markup);
  }

  [Fact]
  public void Duration_RendersEmDash_WhenZero()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p => p.Add(x => x.Item, Vm(duration: 0)));
    // total shows --:-- not 0:00 when duration unknown
    Assert.Contains("--:--", cut.Markup);
  }
}
```

- [ ] **Step 2: Implement VoicemailPlayer.razor**

```razor
@using Radio.Web.Models
@using Radio.Web.Services.ApiClients
@inject GvBridgeApiService GvBridgeApi
@inject IJSRuntime JS
@implements IAsyncDisposable

<div class="vm-player">
  <audio src="@AudioSrc" preload="none" @ref="_audioEl"></audio>

  @if (_state == PlayerState.AudioError)
  {
    <div class="vm-player-error" role="alert">
      <RadzenIcon Icon="error_outline" />
      <span>Couldn't load this recording.</span>
      <button type="button" class="phone-btn-sm" @onclick="RetryAsync">Retry</button>
    </div>
  }
  else
  {
    <div class="vm-player-transport">
      <button type="button" class="transport-btn-primary"
              aria-label="@PlayPauseLabel" @onclick="TogglePlayAsync">
        @if (_state == PlayerState.Buffering)
        {
          <span class="spinner"></span>
        }
        else
        {
          <RadzenIcon Icon="@(_state == PlayerState.Playing ? "pause" : "play_arrow")" />
        }
      </button>

      <div class="vm-scrubber" role="slider"
           aria-label="Playback position"
           aria-valuemin="0" aria-valuemax="@_totalForAria" aria-valuenow="@((int)_current)"
           @onclick="OnScrubberClick" @ref="_scrubberEl">
        <div class="now-playing-dock-progress">
          <div class="now-playing-dock-progress-bar" style="width:@(ProgressPercent)%"></div>
        </div>
      </div>

      <span class="vm-time">@FormatTime(_current) / @TotalDisplay</span>
    </div>

    @if (_state == PlayerState.Buffering)
    {
      <div class="vm-buffering-note">Fetching recording…</div>
    }
  }

  <div class="vm-transcript">
    <div class="vm-transcript-heading">Transcript</div>
    @if (!string.IsNullOrWhiteSpace(Item.Transcript))
    {
      <div class="vm-transcript-body">@Item.Transcript</div>
    }
    else if (IsRecent)
    {
      <div class="vm-transcript-pending" aria-live="polite">
        Transcript pending — Google is still transcribing this voicemail.
      </div>
    }
    else
    {
      <div class="vm-transcript-absent">No transcript available.</div>
    }
  </div>

  @* fast-follow (deferred, owner decision 3): Call back / Text back actions *@
</div>

@code {
  [Parameter, EditorRequired] public VoicemailItemDto Item { get; set; } = default!;
  [Parameter] public EventCallback OnHeard { get; set; }

  private enum PlayerState { Idle, Buffering, Playing, Paused, Ended, AudioError }
  private PlayerState _state = PlayerState.Idle;
  private double _current;
  private double _duration;
  private ElementReference _audioEl;
  private ElementReference _scrubberEl;
  private IJSObjectReference? _module;
  private IJSObjectReference? _player;
  private DotNetObjectReference<VoicemailPlayer>? _self;
  private bool _heardSent;

  private string AudioSrc => GvBridgeApi.GetVoicemailAudioUrl(Item.Id);
  private bool IsRecent => (DateTime.UtcNow - Item.ReceivedAt) < TimeSpan.FromMinutes(30);
  private bool DurationKnown => Item.DurationSeconds > 0 || _duration > 0;
  private double EffectiveDuration => _duration > 0 ? _duration : Item.DurationSeconds;
  private int _totalForAria => Item.DurationSeconds > 0 ? Item.DurationSeconds : 0;
  private double ProgressPercent =>
    EffectiveDuration > 0 ? Math.Clamp(_current / EffectiveDuration * 100, 0, 100) : 0;
  private string TotalDisplay => DurationKnown ? FormatTime(EffectiveDuration) : "--:--";
  private string PlayPauseLabel =>
    _state == PlayerState.Playing ? "Pause" : $"Play voicemail from {Item.FromName ?? Item.FromNumber}";

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (firstRender)
    {
      _self = DotNetObjectReference.Create(this);
      _module = await JS.InvokeAsync<IJSObjectReference>(
        "import", "./js/voicemail-player.js");
      _player = await _module.InvokeAsync<IJSObjectReference>("attach", _audioEl, _self);
    }
  }

  private async Task TogglePlayAsync()
  {
    await MarkHeardOnceAsync();
    if (_state == PlayerState.Playing)
    {
      await _player!.InvokeVoidAsync("pause");
      _state = PlayerState.Paused;
    }
    else
    {
      _state = PlayerState.Buffering;  // first-play stall is expected (ADR/contract)
      await _player!.InvokeVoidAsync("play");
    }
  }

  private async Task OnScrubberClick(MouseEventArgs e)
  {
    // Compute fraction from clientX vs the scrubber's bounding box (JS reads box).
    var fraction = await _module!.InvokeAsync<double>("fractionFromEvent", _scrubberEl, e.ClientX);
    await _player!.InvokeVoidAsync("seekFraction", fraction);
  }

  private async Task RetryAsync()
  {
    _state = PlayerState.Idle;
    StateHasChanged();
    await TogglePlayAsync();
  }

  private async Task MarkHeardOnceAsync()
  {
    if (_heardSent) return;
    _heardSent = true;
    await OnHeard.InvokeAsync();                         // UI-local flip (parent)
    await GvBridgeApi.MarkVoicemailReadAsync(Item.Id);   // flagged no-op seam
  }

  [JSInvokable] public Task OnTimeUpdate(double current, double duration)
  {
    _current = current;
    if (duration > 0) _duration = duration;
    return InvokeAsync(StateHasChanged);
  }
  [JSInvokable] public Task OnPlaying() { _state = PlayerState.Playing; return InvokeAsync(StateHasChanged); }
  [JSInvokable] public Task OnBuffering() { if (_state != PlayerState.Playing) _state = PlayerState.Buffering; return InvokeAsync(StateHasChanged); }
  [JSInvokable] public Task OnEnded() { _state = PlayerState.Ended; _current = 0; return InvokeAsync(StateHasChanged); }
  [JSInvokable] public Task OnAudioError() { _state = PlayerState.AudioError; return InvokeAsync(StateHasChanged); }

  private static string FormatTime(double seconds)
  {
    if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
    var ts = TimeSpan.FromSeconds(Math.Floor(seconds));
    return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
  }

  public async ValueTask DisposeAsync()
  {
    try { if (_player != null) await _player.InvokeVoidAsync("dispose"); } catch { }
    try { if (_player != null) await _player.DisposeAsync(); } catch { }
    try { if (_module != null) await _module.DisposeAsync(); } catch { }
    _self?.Dispose();
  }
}
```

> **Add `fractionFromEvent` to `voicemail-player.js`** (used by `OnScrubberClick`):
> ```javascript
> export function fractionFromEvent(el, clientX) {
>   const r = el.getBoundingClientRect();
>   return r.width > 0 ? (clientX - r.left) / r.width : 0;
> }
> ```

- [ ] **Step 3: Player CSS (§Ph, no new tokens)**

```css
/* §Ph — Voicemail inline player (PR2) */
.vm-player { padding: var(--sp-3) var(--sp-4); display: flex; flex-direction: column; gap: var(--sp-3); }
.vm-player-transport { display: flex; align-items: center; gap: var(--sp-3); }
.vm-scrubber { flex: 1; padding: 11px 0; cursor: pointer; }   /* 3px bar + pad ≥24px hit area */
.vm-time { font-family: var(--font-mono); font-variant-numeric: tabular-nums; color: var(--text-medium); font-size: 13px; }
.vm-buffering-note { font-family: var(--font-mono); font-size: 12px; color: var(--text-low); }
.vm-player-error { display: flex; align-items: center; gap: var(--sp-2); color: var(--signal-red); }
.vm-transcript-heading { font-family: var(--font-mono); font-size: 11px; text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-low); margin-bottom: var(--sp-1); }
.vm-transcript-body { color: var(--text-high); line-height: 1.5; }
.vm-transcript-pending { color: var(--text-medium); font-style: italic; }
.vm-transcript-absent { color: var(--text-low); }
```

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~VoicemailPlayerTests"
git add src/Radio.Web/Components/Pages/VoicemailPlayer.razor src/Radio.Web/wwwroot/js/voicemail-player.js src/Radio.Web/wwwroot/css/design-system.css tests/Radio.Web.Tests/Components/VoicemailPlayerTests.cs
git commit -m "feat(web): add VoicemailPlayer inline accordion (seek + transcript + audio-error)"
```

---

## Chunk 4: VoicemailRow component

### Task 4: VoicemailRow.razor

**Files:**
- Create: `src/Radio.Web/Components/Pages/VoicemailRow.razor`
- Test: `tests/Radio.Web.Tests/Components/VoicemailRowTests.cs`

> Screen A row: `[🎙 cyan chip][unread dot if unheard][caller + transcript-first-line preview][duration mono][▸]`. Unheard = dot + bold caller; heard = dimmer, no dot. `durationSeconds == 0` → "—". Preview fallback: transcript first line, else "Transcript pending…" (recent) / "No transcript available." Tapping toggles the accordion (`VoicemailPlayer`). New-arrival rows animate via `.list-item-add` (parent adds the class).

- [ ] **Step 1: Write the failing bUnit test**

```csharp
using Bunit;
using Radio.Web.Models;
using Radio.Web.Components.Pages;

namespace Radio.Web.Tests.Components;

public class VoicemailRowTests : TestContext
{
  private VoicemailItemDto Vm(bool isRead = false, int duration = 42,
    string? transcript = "Hey, calling about…", string? name = "Jane") =>
    new("vm1", "t1", "+15551234567", name, DateTime.UtcNow, duration, isRead,
      transcript, "/api/gvbridge/voicemail/vm1/audio");

  [Fact]
  public void Unheard_ShowsUnreadDot()
  {
    var cut = RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, Vm(isRead: false))
      .Add(x => x.Expanded, false));
    Assert.Single(cut.FindAll(".unread-dot"));
  }

  [Fact]
  public void Heard_NoUnreadDot()
  {
    var cut = RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, Vm(isRead: true))
      .Add(x => x.Expanded, false));
    Assert.Empty(cut.FindAll(".unread-dot"));
  }

  [Fact]
  public void ZeroDuration_RendersEmDash()
  {
    var cut = RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, Vm(duration: 0))
      .Add(x => x.Expanded, false));
    Assert.Contains("—", cut.Markup);
    Assert.DoesNotContain("0:00", cut.Markup);
  }
}
```

- [ ] **Step 2: Implement VoicemailRow.razor**

```razor
@using Radio.Web.Models

<div class="vm-row-wrap @(IsNew ? "list-item-add" : "")">
  <button type="button"
          class="list-item-touch @(Expanded ? "list-item-active" : "")"
          @onclick="OnToggle">
    <span class="vm-chip"><RadzenIcon Icon="voicemail" /></span>
    @if (!Item.IsRead)
    {
      <span class="unread-dot"></span>
    }
    <span class="vm-row-main">
      <span class="vm-row-title @(Item.IsRead ? "" : "vm-row-unread")">@DisplayName</span>
      <span class="vm-row-preview">@Preview</span>
    </span>
    @if (!SingleStreamView)
    {
      <span class="vm-type-label">vmail</span>
    }
    <span class="vm-row-duration">@DurationDisplay</span>
    <RadzenIcon Icon="@(Expanded ? "expand_more" : "chevron_right")" />
  </button>

  @if (Expanded)
  {
    <VoicemailPlayer Item="Item" OnHeard="OnHeard" />
  }
</div>

@code {
  [Parameter, EditorRequired] public VoicemailItemDto Item { get; set; } = default!;
  [Parameter] public bool Expanded { get; set; }
  [Parameter] public bool IsNew { get; set; }
  [Parameter] public bool SingleStreamView { get; set; }   // hide type label in Voicemail filter
  [Parameter] public string? ResolvedName { get; set; }
  [Parameter] public EventCallback OnToggle { get; set; }
  [Parameter] public EventCallback OnHeard { get; set; }

  private string DisplayName => ResolvedName
    ?? (!string.IsNullOrWhiteSpace(Item.FromName) ? Item.FromName! : Item.FromNumber);

  private bool IsRecent => (DateTime.UtcNow - Item.ReceivedAt) < TimeSpan.FromMinutes(30);

  private string Preview => !string.IsNullOrWhiteSpace(Item.Transcript)
    ? FirstLine(Item.Transcript!)
    : (IsRecent ? "Transcript pending…" : "No transcript available.");

  private static string FirstLine(string s)
  {
    var nl = s.IndexOf('\n');
    return nl >= 0 ? s[..nl] : s;
  }

  private string DurationDisplay
  {
    get
    {
      if (Item.DurationSeconds <= 0) return "—";
      var ts = TimeSpan.FromSeconds(Item.DurationSeconds);
      return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }
  }
}
```

- [ ] **Step 3: Row CSS (§Ph, no new tokens)**

```css
/* §Ph — Voicemail row (PR2) */
.vm-chip {
  width: 44px; height: 44px; border-radius: 10px; flex-shrink: 0;
  display: flex; align-items: center; justify-content: center;
  background: color-mix(in srgb, var(--accent-primary) 14%, transparent);
  color: var(--accent-primary);
}
.vm-row-main { display: flex; flex-direction: column; min-width: 0; flex: 1; }
.vm-row-title { color: var(--text-medium); }
.vm-row-title.vm-row-unread { color: var(--text-high); font-weight: 600; }
.vm-row-preview { color: var(--text-medium); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.vm-type-label { font-family: var(--font-mono); font-size: 10px; text-transform: uppercase; color: var(--text-low); }
.vm-row-duration { font-family: var(--font-mono); font-variant-numeric: tabular-nums; color: var(--text-medium); }
```

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~VoicemailRowTests"
git add src/Radio.Web/Components/Pages/VoicemailRow.razor src/Radio.Web/wwwroot/css/design-system.css tests/Radio.Web.Tests/Components/VoicemailRowTests.cs
git commit -m "feat(web): add VoicemailRow with unread dot, duration fallback, transcript preview"
```

---

## Chunk 5: Wire voicemail into the feed + new-arrival path

### Task 5: PhoneMessagesPanel — voicemail list + states + accordion + new-arrival

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor`

> The panel now owns the voicemail list. States: loading (skeleton ~5), loaded, empty ("No voicemails."), error+Retry, refresh-error (keep last good + warning toast). Accordion: only one open at a time (opening collapses any other). New-arrival: prepend the row, animate `.list-item-add`, mark-heard flips the row + decrements count. **The voicemail list is fetched/owned here; the parent (`PhonePage`) feeds the push + count.**

- [ ] **Step 1: Add voicemail parameters + state**

```csharp
[Parameter] public List<VoicemailItemDto>? Voicemails { get; set; }
[Parameter] public bool VoicemailLoading { get; set; }
[Parameter] public bool VoicemailError { get; set; }
[Parameter] public EventCallback OnRetryVoicemail { get; set; }
[Parameter] public EventCallback<string> OnVoicemailHeard { get; set; }  // id → parent flips IsRead + count

private string? _openVoicemailId;   // accordion: one open at a time
private string? _newVoicemailId;    // most-recent arrival → .list-item-add once
```

- [ ] **Step 2: Render voicemail rows in the `voicemail` and `all` filters**

In the `RenderFeed` logic (PR1 stub had only `all`/`calls`), branch by filter. For `voicemail` (single-stream) and `all` (interleaved), emit `VoicemailRow`s with the states:

```razor
@* inside RenderFeed for voicemail/all *@
@if (VoicemailLoading && Voicemails == null)
{
  @* skeleton ~5 rows (BuildSkeleton) *@
}
else if (VoicemailError && Voicemails == null)
{
  <div class="empty-state">
    <RadzenIcon Icon="cloud_off" class="empty-state-icon" />
    <div class="empty-state-text">Couldn't load voicemail.</div>
    <button type="button" class="phone-btn-sm" @onclick="OnRetryVoicemail">Retry</button>
  </div>
}
else if (Voicemails is { Count: 0 } && _filter == "voicemail")
{
  <div class="empty-state">
    <RadzenIcon Icon="voicemail" class="empty-state-icon" />
    <div class="empty-state-text">No voicemails.</div>
  </div>
}
else if (Voicemails != null)
{
  @foreach (var vm in Voicemails.OrderByDescending(v => v.ReceivedAt))
  {
    var captured = vm;
    <VoicemailRow Item="captured"
                  Expanded="@(_openVoicemailId == captured.Id)"
                  IsNew="@(_newVoicemailId == captured.Id)"
                  SingleStreamView="@(_filter == "voicemail")"
                  ResolvedName="@ResolveVoicemailName(captured)"
                  OnToggle="@(() => ToggleVoicemail(captured.Id))"
                  OnHeard="@(() => HeardVoicemail(captured.Id))" />
  }
}
```

> When `_filter == "all"`, voicemail rows must be **interleaved by timestamp** with call rows (and text-thread rows in PR3). The clean implementation is a single typed feed list: build a `List<FeedItem>` (a small `record FeedItem(DateTime When, FeedKind Kind, object Payload)`), sort once by `When` desc, and switch on `Kind` to emit the right row component. Introduce that `FeedItem` projection now (calls + voicemail); PR3 adds the texts case. For the single-stream filters, just project the one source.

- [ ] **Step 3: Accordion + heard handlers**

```csharp
private void ToggleVoicemail(string id)
{
  _openVoicemailId = _openVoicemailId == id ? null : id;  // collapse others
  if (_newVoicemailId == id) _newVoicemailId = null;        // clear new-flag on open
}

private async Task HeardVoicemail(string id)
{
  // Local flip happens in the parent (it owns the list + count); bubble up.
  await OnVoicemailHeard.InvokeAsync(id);
}

private string ResolveVoicemailName(VoicemailItemDto vm)
{
  if (!string.IsNullOrWhiteSpace(vm.FromName)) return vm.FromName!;
  var key = PhoneNumberNormalizer.Normalize(vm.FromNumber);
  var match = Contacts.FirstOrDefault(c => PhoneNumberNormalizer.Normalize(c.PhoneNumber) == key);
  return match?.Name ?? vm.FromNumber;
}

// Called by the parent when GvVoicemailReceived fires, to flag the new row.
public void FlagNewVoicemail(string id) { _newVoicemailId = id; StateHasChanged(); }
```

- [ ] **Step 4: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor
git commit -m "feat(web): render voicemail rows in feed with list states + accordion + new-arrival flag"
```

### Task 6: PhonePage — fetch voicemail, push, toast, count

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor`

- [ ] **Step 1: Add voicemail state + fetch**

```csharp
private List<VoicemailItemDto>? _voicemails;
private bool _voicemailLoading;
private bool _voicemailError;
private readonly HashSet<string> _locallyHeard = new();   // UI-local read-state

private async Task LoadVoicemailsAsync()
{
  _voicemailLoading = true; _voicemailError = false;
  var list = await GvBridgeApi.GetVoicemailsAsync();
  if (list != null)
  {
    _voicemails = ApplyLocalHeard(list.Items.ToList());
  }
  else if (_voicemails == null)
  {
    _voicemailError = true;   // only show error if we have no prior good list
  }
  else
  {
    NotificationService.Notify(NotificationSeverity.Warning, "Couldn't refresh",
      "Showing the last update.");
  }
  _voicemailLoading = false;
  PhoneUnread.Set(UnreadSum);
  await InvokeAsync(StateHasChanged);
}

private List<VoicemailItemDto> ApplyLocalHeard(List<VoicemailItemDto> items) =>
  items.Select(v => _locallyHeard.Contains(v.Id)
    ? v with { IsRead = true } : v).ToList();
```

- [ ] **Step 2: Fold voicemail into the count + load on init**

```csharp
private int UnheardVoicemailCount =>
  _voicemails?.Count(v => !v.IsRead) ?? 0;

// update UnreadSum (decision 2 keeps missed calls in the sum):
private int UnreadSum => MissedCallCount + UnheardVoicemailCount;  // + UnreadThreadCount in PR3
```

In `OnInitializedAsync`, after the existing fetches, call `_ = LoadVoicemailsAsync();` (don't block the page), and subscribe:

```csharp
PhoneHub.GvVoicemailReceived += OnGvVoicemailReceived;
```

- [ ] **Step 3: New-arrival handler (never steal screen / never pause audio)**

```csharp
private void OnGvVoicemailReceived(VoicemailItemDto vm)
{
  if (_disposed) return;
  _voicemails ??= new();
  if (_voicemails.All(v => v.Id != vm.Id))
  {
    _voicemails.Insert(0, _locallyHeard.Contains(vm.Id) ? vm with { IsRead = true } : vm);
  }
  _messagesPanel?.FlagNewVoicemail(vm.Id);   // animate the row
  // Calm toast — never modal, never audio (hard rule). Suppress if already open
  // is handled in the panel's open-state; for v1 the toast is low-urgency Info.
  NotificationService.Notify(NotificationSeverity.Info, "New voicemail",
    $"{vm.FromName ?? vm.FromNumber}{(vm.DurationSeconds > 0 ? $" · {FormatVmDuration(vm.DurationSeconds)}" : "")}");
  PhoneUnread.Set(UnreadSum);
  _ = InvokeAsync(StateHasChanged);
}

private void OnVoicemailHeard(string id)
{
  _locallyHeard.Add(id);
  if (_voicemails != null)
  {
    var idx = _voicemails.FindIndex(v => v.Id == id);
    if (idx >= 0) _voicemails[idx] = _voicemails[idx] with { IsRead = true };
  }
  PhoneUnread.Set(UnreadSum);
  _ = InvokeAsync(StateHasChanged);
}

private static string FormatVmDuration(int s)
{
  var ts = TimeSpan.FromSeconds(s);
  return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
}
```

- [ ] **Step 4: Pass everything into the panel + capture the ref**

Update the `PhoneMessagesPanel` usage:

```razor
<PhoneMessagesPanel @ref="_messagesPanel"
                    GvAvailable="_gvBridgeAvailable"
                    CallHistory="_callHistory"
                    Contacts="MergedContacts"
                    Voicemails="_voicemails"
                    VoicemailLoading="_voicemailLoading"
                    VoicemailError="_voicemailError"
                    UnheardVoicemailCount="UnheardVoicemailCount"
                    UnreadThreadCount="0"
                    OnRefresh="RefreshMessagesAsync"
                    OnRetryVoicemail="LoadVoicemailsAsync"
                    OnVoicemailHeard="OnVoicemailHeard" />
```

Add the field `private PhoneMessagesPanel? _messagesPanel;` and unsubscribe in `Dispose`: `PhoneHub.GvVoicemailReceived -= OnGvVoicemailReceived;`. Also have `RefreshMessagesAsync` call `await LoadVoicemailsAsync();`.

- [ ] **Step 5: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Components/Pages/PhonePage.razor
git commit -m "feat(web): wire voicemail fetch + GvVoicemailReceived + toast + UI-local heard state"
```

---

## Chunk 6: Documentation

### Task 7: Update FUTURE-WORK

**Files:**
- Modify: `design/FUTURE-WORK.md`

- [ ] **Step 1:** Update the PR1 voicemail entry: mark-read seam is now **wired but no-op** (`MarkVoicemailReadAsync`, gated on `RotaryPhone:Gv:MarkReadEnabled=false`). Add the deferred **Call back / Text back** quick actions (owner decision 3) as a fast-follow item. Note the **open thread back to RotaryPhone** deliverable: request GV mark-read be pulled forward (decision 4) and keep the voicemail audio endpoint unauthenticated (ADR §8.1).

- [ ] **Step 2: Commit**

```bash
git add design/FUTURE-WORK.md
git commit -m "docs: voicemail mark-read seam wired-no-op; defer call/text-back quick actions"
```

---

## Test Plan

**Unit / component (must pass before PR):**
- `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~VoicemailRowTests|FullyQualifiedName~VoicemailPlayerTests|FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests"` — green.
- Full suite + build — no regressions, 0 warnings.

**Component assertions covered:**
- `<audio src>` is the **absolute** `radio:5004` URL (regression guard on contract risk #3).
- Transcript: present → body; null+recent → "Transcript pending…"; null+old → "No transcript available."
- `durationSeconds == 0` → "—" / "--:--", never "0:00".
- Unread dot present iff `!IsRead`.
- `MarkVoicemailReadAsync` no-ops (no HTTP) while the flag is off.

**UAT (Tester, 1920×720, deploy first):**
1. Voicemail filter: skeleton → list → tap a row → inline player opens (accordion), any other open player collapses.
2. First play shows the **buffering** spinner + "Fetching recording…" (≤ a few sec), then plays; scrubber advances; tapping the scrubber **seeks** without re-downloading; time readout `m:ss / m:ss`.
3. Transcript present/pending/absent render per the row's data.
4. Force a 502 on the audio endpoint (stop the gvbridge media path) → transport replaced with "Couldn't load this recording." + Retry; Retry re-attempts.
5. Opening a voicemail flips it **heard** (dot gone, dimmer), decrements the Voicemail segment count + rail + topbar badge. A **hard reload** re-derives unread from `isRead` (UI-local caveat — locally-heard item may reappear unread; expected v1).
6. Push a new voicemail (or trigger `VoicemailReceived`): row animates in at top, **calm Info toast** "New voicemail · {caller} · {duration}", badge ++, **music does not pause**, **no modal**. If the player for that VM is already open, no disruptive re-render.
7. Stop the gvbridge: reconnecting banner shows (PR1), voicemail list keeps its last good content (no blank), refresh failures show the warning toast not an error wipe.

**Self-review checklist (Planner ran):**
- Audio src absolute (never relative DTO field).
- Mark-read is a flagged no-op seam, wired into the heard path (decision 4) — never throws.
- No Call back / Text back buttons (decision 3); fast-follow marker present.
- New-arrival never steals screen / never pauses audio (hard rule).
- All literal markup emitted (the `RenderFeed` interleave note names the `FeedItem` projection to implement — an instruction, not a code TBD).
