# Output picker UI — replace the `/devices`-navigation stub

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Replace the placeholder "Out" topbar pill (which currently just navigates to the `/devices` page) with a proper popover-style output picker that lets the user pick between local audio outputs (soundbar, USB audio device, etc.) without leaving the current page. Mirrors the existing `CastDeviceDropdown.razor` pattern for visual consistency. Resolves Mark's UX complaint: "there's no obvious/simple way to choose the single audio output like there used to be."

**Source research**: [`docs/research/2026-05-22-bt-dual-routing-investigation.md`](../research/2026-05-22-bt-dual-routing-investigation.md) §"For the UI complaint" — the "Out" pill at `src/Radio.Web/Components/Layout/MainLayout.razor:636-641` was explicitly stubbed by an earlier "PR 3 will own this" comment that never materialized.

**Architecture**:

Today's state at `MainLayout.razor:88-94`:
```razor
<button type="button" class="nav-pill" @onclick="ToggleOutputPicker" ...>
  <RadzenIcon Icon="speaker" />
  <span class="nav-pill-label">Out</span>
</button>

private void ToggleOutputPicker() => NavigationManager.NavigateTo("/devices");
```

Target state:
```razor
<button type="button" class="nav-pill" @onclick="ToggleOutputPicker" ...>
  <RadzenIcon Icon="speaker" />
  <span class="nav-pill-label">Out</span>
</button>
<OutputPickerDropdown IsOpen="_showOutputDropdown"
                     IsOpenChanged="@((bool v) => { _showOutputDropdown = v; StateHasChanged(); })"
                     OnOutputSelected="OnLocalOutputSelectedAsync"
                     CurrentOutputId="@_selectedOutputId"
                     AvailableOutputs="_availableOutputs" />
```

The dropdown lists all non-Cast outputs (built-in audio, USB audio devices, headphones). Selecting one calls the existing `DevicesController.SetOutputDeviceAsync` endpoint, which mutes Cast paths if a Cast is connected (`SetLocalOutputMuted(false)`-style logic already in `DevicesController.cs`).

**Tech Stack**: Blazor Server, Radzen components for consistency with `CastDeviceDropdown.razor`. No new dependencies.

**Addresses**: UI gap explicitly called out by Mark on 2026-05-22 — "from the UI there's no obvious/simple way to choose the single audio output like there used to be. This needs a cleaner UI."

---

## Task 1: Author `OutputPickerDropdown.razor`

**Files:**
- Create: `src/Radio.Web/Components/Shared/OutputPickerDropdown.razor`

**Step 1:** Mirror the structure of `src/Radio.Web/Components/Shared/CastDeviceDropdown.razor`. Read that file first to understand:
- The popover-vs-MudDialog choice (the lightweight popover per MEMORY)
- The click-away handling (`@onclick:stopPropagation="true"` per MEMORY note)
- The event-callback signatures
- The visual styling (Radzen + project's design-system.css tokens)

**Step 2:** Outline the new component:

```razor
@inject Radio.Web.Services.ApiClients.DevicesApiService DevicesApi
@inject Microsoft.Extensions.Logging.ILogger<OutputPickerDropdown> Logger

@if (IsOpen)
{
  <div class="output-picker-overlay" @onclick="HandleClickAway"></div>
  <div class="output-picker-popover" @onclick:stopPropagation="true">
    <div class="output-picker-header">
      <RadzenIcon Icon="speaker" />
      <span>Select Output</span>
    </div>

    @foreach (var device in AvailableOutputs.Where(d => !IsCastOutput(d)))
    {
      <button type="button"
              class="output-picker-row @(device.Id == CurrentOutputId ? "is-active" : "")"
              @onclick="@(() => HandleSelectAsync(device))">
        <RadzenIcon Icon="@GetIconForDevice(device)" />
        <span class="output-picker-name">@DisplayNames.Device(device)</span>
        @if (device.Id == CurrentOutputId)
        {
          <RadzenIcon Icon="check" Class="output-picker-checkmark" />
        }
      </button>
    }

    @if (!AvailableOutputs.Any(d => !IsCastOutput(d)))
    {
      <div class="output-picker-empty">No local outputs detected.</div>
    }
  </div>
}

@code {
  [Parameter] public bool IsOpen { get; set; }
  [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }
  [Parameter] public EventCallback<AudioDeviceDto> OnOutputSelected { get; set; }
  [Parameter] public string? CurrentOutputId { get; set; }
  [Parameter] public List<AudioDeviceDto> AvailableOutputs { get; set; } = new();

  private async Task HandleSelectAsync(AudioDeviceDto device)
  {
    await OnOutputSelected.InvokeAsync(device);
    await IsOpenChanged.InvokeAsync(false);
  }

  private async Task HandleClickAway()
  {
    await IsOpenChanged.InvokeAsync(false);
  }

  private static bool IsCastOutput(AudioDeviceDto d) =>
    d.Id.Equals("google-cast", StringComparison.OrdinalIgnoreCase);

  private static string GetIconForDevice(AudioDeviceDto d)
  {
    // Best-effort icon mapping based on device name heuristics
    var name = (d.Name ?? "").ToLowerInvariant();
    if (name.Contains("usb")) return "usb";
    if (name.Contains("hdmi")) return "tv";
    if (name.Contains("headphone") || name.Contains("phone")) return "headphones";
    return "speaker";
  }
}
```

**Step 3:** CSS — add styles to `src/Radio.Web/wwwroot/css/components/output-picker.css` (or extend `cast-dropdown.css` and rename to a shared file). The visual should be near-identical to `CastDeviceDropdown` for consistency.

**Step 4:** Build verification:

```bash
dotnet build src/Radio.Web/Radio.Web.csproj --configuration Release
```
Expected: 0 errors, only pre-existing IDE0011 warnings.

**Step 5:** Commit:

```bash
git add src/Radio.Web/Components/Shared/OutputPickerDropdown.razor src/Radio.Web/wwwroot/css/components/output-picker.css
git commit -m "feat(web): OutputPickerDropdown popover (mirrors CastDeviceDropdown shape)"
```

---

## Task 2: Wire into MainLayout

**Files:**
- Modify: `src/Radio.Web/Components/Layout/MainLayout.razor`

**Step 1:** Replace the stub `ToggleOutputPicker` body. Current:

```csharp
private void ToggleOutputPicker()
{
  NavigationManager.NavigateTo("/devices");
}
```

New:

```csharp
private bool _showOutputDropdown;

private void ToggleOutputPicker()
{
  _showOutputDropdown = !_showOutputDropdown;
  // Close Cast dropdown if it was open — they're mutually exclusive popovers
  if (_showOutputDropdown && _showCastDropdown)
  {
    _showCastDropdown = false;
  }
  StateHasChanged();
}

private async Task OnLocalOutputSelectedAsync(AudioDeviceDto device)
{
  if (string.IsNullOrEmpty(device?.Id) || device.Id == _selectedOutputId) return;

  // If Cast is currently selected: disconnect (un-mute will fire via DevicesController on output switch)
  if (_selectedOutputId == "google-cast")
  {
    // The output switch below will handle Cast teardown via DevicesController
  }

  try
  {
    var success = await DevicesApi.SetOutputDeviceAsync(device.Id);
    if (success)
    {
      _selectedOutputId = device.Id;
      Logger.LogInformation("Switched to local output: {Name} ({Id})", device.Name, device.Id);
    }
  }
  catch (Exception ex)
  {
    Logger.LogError(ex, "Failed to switch output");
  }
}
```

**Step 2:** Add the dropdown component immediately after the "Out" pill in the markup:

```razor
<!-- Output picker entry -->
<button type="button" class="nav-pill" @onclick="ToggleOutputPicker"
        aria-label="Output device picker"
        title="Select audio output">
  <RadzenIcon Icon="speaker" />
  <span class="nav-pill-label">Out</span>
</button>

<OutputPickerDropdown IsOpen="_showOutputDropdown"
                     IsOpenChanged="@((bool v) => { _showOutputDropdown = v; StateHasChanged(); })"
                     OnOutputSelected="OnLocalOutputSelectedAsync"
                     CurrentOutputId="@_selectedOutputId"
                     AvailableOutputs="_availableOutputs" />
```

**Step 3:** Build + commit:

```bash
dotnet build src/Radio.Web/Radio.Web.csproj --configuration Release
git add src/Radio.Web/Components/Layout/MainLayout.razor
git commit -m "feat(web): wire OutputPickerDropdown into MainLayout (replaces /devices nav stub)"
```

---

## Task 3: Mutual-exclusivity behavior on output switch

**Files:**
- Modify: `src/Radio.API/Controllers/DevicesController.cs` (verify, may already do this)
- Maybe modify: `src/Radio.Infrastructure/Audio/Engine/...` for any source-of-truth on "single active output"

**Step 1:** Audit current behavior. In `DevicesController.cs`, find `SetOutputDeviceAsync` (the endpoint that the UI calls). Read it to understand the existing logic around:
- When local output is selected: does it auto-disconnect Cast?
- When Cast is selected: does it auto-mute local? (yes — verified earlier at lines 205, 214, 1341)
- Is there a "single active output" invariant?

**Step 2:** If selecting a local output doesn't auto-disconnect Cast: add a call to `_castOutput.DisconnectAsync()` or equivalent at the start of the local-output-switch branch. Document the invariant: at any time, exactly one of {local output, cast output} is active.

**Step 3:** Verify by integration test (or note for Mark UAT): selecting a local output via the new picker should silence Cast within 1-2 seconds.

**Step 4:** Build + commit if changes needed:

```bash
git add src/Radio.API/Controllers/DevicesController.cs
git commit -m "feat(api): enforce mutual exclusivity between local + Cast output paths"
```

---

## Task 4: Tests

**Files:**
- Create: `tests/Radio.Web.Tests/Components/Shared/OutputPickerDropdownTests.cs`

**Step 1:** Add bUnit tests mirroring `CastDeviceDropdownTests.cs` if it exists, or write fresh:

```csharp
public class OutputPickerDropdownTests : TestContext
{
  [Fact]
  public void Closed_RendersNothing()
  {
    var cut = RenderComponent<OutputPickerDropdown>(p => p.Add(c => c.IsOpen, false));
    Assert.Empty(cut.FindAll(".output-picker-popover"));
  }

  [Fact]
  public void Open_ListsNonCastOutputsOnly()
  {
    var outputs = new List<AudioDeviceDto> {
      new() { Id = "local-1", Name = "Built-in Audio" },
      new() { Id = "usb-1", Name = "USB Audio Device" },
      new() { Id = "google-cast", Name = "Google Cast" },  // should NOT appear
    };
    var cut = RenderComponent<OutputPickerDropdown>(p => p
      .Add(c => c.IsOpen, true)
      .Add(c => c.AvailableOutputs, outputs));
    var rows = cut.FindAll(".output-picker-row");
    Assert.Equal(2, rows.Count);  // local + usb, not cast
  }

  [Fact]
  public void CurrentSelection_GetsActiveClassAndCheckmark()
  { /* ... */ }

  [Fact]
  public void OutputSelected_InvokesCallback()
  { /* ... */ }

  [Fact]
  public void ClickAway_ClosesPopover()
  { /* ... */ }
}
```

**Step 2:** Run + commit:

```bash
dotnet test tests/Radio.Web.Tests/Radio.Web.Tests.csproj --configuration Release --filter "OutputPickerDropdownTests"
git add tests/Radio.Web.Tests/Components/Shared/OutputPickerDropdownTests.cs
git commit -m "test(web): OutputPickerDropdown component tests"
```

---

## Task 5: Build + open PR + auto-merge

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
git push -u origin feat/output-picker-ui
gh pr create --title "feat(web): output picker popover (replaces /devices nav stub)" --body "<see PR template below>"
```

PR template:

```markdown
## Summary

Replaces the placeholder "Out" topbar pill (which previously navigated to /devices) with a proper popover-style output picker. Mirrors the existing CastDeviceDropdown visual + interaction pattern.

Resolves Mark's UX complaint from 2026-05-22: "from the UI there's no obvious/simple way to choose the single audio output like there used to be. This needs a cleaner UI."

## Behavior

- Click "Out" in topbar → popover opens listing all non-Cast local outputs (soundbar, USB, etc.)
- Select an output → DevicesApi.SetOutputDeviceAsync fires → exclusive selection (auto-disconnects Cast if it was active)
- Click outside the popover → closes
- Active output gets is-active class + checkmark

## Test plan

- [x] OutputPickerDropdown bUnit tests (5 cases)
- [x] Build clean
- [ ] Mark UAT: open the dropdown, switch between local outputs, confirm Cast disconnects when switching to local
```

Auto-merge after CI green per the current tranche's authorization.

---

## Task 6: Deploy + UAT (operator action — Mark, post-merge)

```bash
pwsh.exe -NoProfile -Command "& './deploy/Deploy-ToLinux.ps1' -TargetHost radio -Runtime linux-x64"
```

Then open `http://radio:5002`, click "Out" in topbar, verify the popover appears + lists local outputs. Select one. Confirm Cast disconnects if it was active.

---

## Out of scope

- **Combining Cast + local in a single picker.** Cast picker stays separate (it has its own scan/connect lifecycle and a different visual treatment). Two pills, two popovers — by design.
- **Output picker on `/devices` page** (the explicit-management page). That page stays as the full management surface; this plan only adds the quick-switch popover in the topbar.
- **Headphone/USB hotplug.** The popover renders whatever `_availableOutputs` contains at render time. If a USB device is plugged in mid-session, the list refreshes via the same mechanism the page already uses (SignalR audio-state events).
- **Visual design refinement.** First-pass visual matches CastDeviceDropdown for consistency. Mark may want polish later.
- **Path B (PipeWire dual-routing).** Separate plan at `docs/plans/2026-05-22-wp-bt-route-exclusivity.md`.
