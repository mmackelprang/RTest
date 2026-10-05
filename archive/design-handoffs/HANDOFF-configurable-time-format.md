# HANDOFF — Configurable clock time format (12h / 24h)

**Surface:** All wall-clock displays in `Radio.Web` (sleep screen, topbar Time cluster, queue "ends ~" prediction) + new "Display" section in System Configuration page.
**Status:** `[PENDING REVIEW]` — ready for Planner / Builder
**Relationship to existing handoffs:**
- **Follows** `docs/design-handoffs/design_handoff_radio_console/` (LED display font stack, `--font-led` token, Radzen Material-3 form vocabulary in System Config).
- **Follows** `docs/design-handoffs/HANDOFF-stop-casting-menu-item.md` for the canonical handoff structure used here.
- **Extends** `SystemConfigPage.razor` with a brand-new "Display" tab under the Configuration node. No prior handoff governs that surface — visual language is borrowed wholesale from the existing sibling tabs (Audio, Visualizer, Output, etc.).
- **Deviates** from nothing. No new tokens, no new chrome.

---

## 1. Problem + context

The wall-clock string is hardcoded as `DateTime.Now.ToString("HH:mm")` in three component files. The user (whose locale norm is 12-hour with AM/PM) currently sees `15:45` on a kiosk that lives in their living room — the spec output disagrees with every other clock in the house. The fix is a single user-facing preference, `Display:TimeFormat`, that switches every "what time is it right now" display between `3:45 PM` (12-hour with AM/PM suffix) and `15:45` (24-hour, current behavior). Persisted to the SQLite config store so it survives restarts, hot-reloaded via `IOptionsMonitor<DisplayOptions>` + `ConfigStoreChangeNotifier` so toggling it on the Settings page repaints the topbar within the next 1-second tick **without** a circuit restart.

---

## 2. Setting model

### Config section (new)

| Key | Type | Allowed values | Default | Notes |
|---|---|---|---|---|
| `Display:TimeFormat` | `string` | `"12h"` \| `"24h"` | `"24h"` | String, not enum, for forward-compat (`"12h"` and `"24h"` are the only two values we ship; future additions e.g. `"12h-no-suffix"` would go here). |
| `Display:ShowSeconds` | `bool` | `true` / `false` | `false` | When `true`, the wall clocks render `:ss`. Sleep clock and topbar clock both honor it. |

### Defaults justification

- `TimeFormat = "24h"` — **preserves current behavior**. `Sleep.razor:154`, `MainLayout.razor:220/292/386`, and `QueueHistoryPanel.razor:309` all currently emit `HH:mm`. Shipping `12h` as the default would change every existing kiosk on first deploy without consent. Users who want 12h must opt in — surfaced via the same Settings tab.
- `ShowSeconds = false` — matches the present design. The sleep-screen LED clock is intentionally calm; seconds tick is visually busy and would defeat the "low-stimulus glance display" intent in `Sleep.razor`'s header comment.

### Section registration

The section binds to a new `DisplayOptions` POCO at `src/Radio.Web/Models/DisplayOptions.cs` (mirrors `DevicesOptions.cs`):

```csharp
namespace Radio.Web.Models;

public class DisplayOptions
{
  public const string SectionName = "Display";
  public string TimeFormat { get; set; } = "24h";   // "12h" | "24h"
  public bool ShowSeconds { get; set; } = false;
}
```

Registered in `Radio.Web/Program.cs` alongside the existing `Configure<DevicesOptions>` call near `Program.cs:364`:

```csharp
builder.Services.Configure<DisplayOptions>(builder.Configuration.GetSection(DisplayOptions.SectionName));
```

The SQLite config bridge (`Radio.Configuration/Bridge/SqliteConfigurationProvider`, already wired in `Radio.Web/Program.cs`) automatically flattens `Display:TimeFormat` and `Display:ShowSeconds` from the SQLite `Config_sqlite` table into the .NET configuration tree, so no separate persistence wiring is needed.

### UI location

System Configuration page → Configuration tab → **new** "Display" sub-tab, inserted between **Devices** and **Audio Engine** in the existing inner `RadzenTabs` block at `SystemConfigPage.razor` (current order: Audio, Visualizer, Output, Devices, Audio Engine, Radio, Bluetooth, File Player, TTS, Fingerprinting, Metrics). Display sits with Devices because both control kiosk presentation, not audio behavior.

---

## 3. Visual mockup (ASCII)

### 3.1 Settings UI — new "Display" sub-tab inside Configuration

```
┌─ System Configuration ──────────────────────────────────────────────────┐
│ ▸ System Stats                                                          │
│ ▾ Configuration                                                         │
│   ┌──────────────────────────────────────────────────────────────────┐  │
│   │ Audio │ Visualizer │ Output │ Devices │ ▌Display│ Audio Engine …│  │
│   ├──────────────────────────────────────────────────────────────────┤  │
│   │                                                                  │  │
│   │ Display Settings                                                 │  │
│   │ Controls how the kiosk renders the wall clock on the topbar     │  │
│   │ and sleep screen.                                                │  │
│   │                                                                  │  │
│   │ Time Format                                                      │  │
│   │  ┌──────────────────────────────┐                                │  │
│   │  │ 24-hour (15:45)            ▾ │   ← RadzenDropDown            │  │
│   │  └──────────────────────────────┘                                │  │
│   │  Options:  12-hour (3:45 PM)                                     │  │
│   │            24-hour (15:45)                                       │  │
│   │  Affects topbar clock, sleep screen, and queue end-time          │  │
│   │  predictions.                                                    │  │
│   │                                                                  │  │
│   │ ☐ Show seconds                                                   │  │
│   │  Adds :ss to wall clocks (e.g. 15:45:22 / 3:45:22 PM).           │  │
│   │  Off by default — the sleep screen is designed to be calm.       │  │
│   │                                                                  │  │
│   │ ┌──────────────────────┐                                         │  │
│   │ │ 💾  Save Display     │                                         │  │
│   │ └──────────────────────┘                                         │  │
│   └──────────────────────────────────────────────────────────────────┘  │
│ ▸ Logs                                                                  │
│ ▸ Event Sources                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Sleep screen clock — before / after

Sleep clock is rendered by `Sleep.razor:56` inside `.sleep-screen-clock`, using `--font-led` (Orbitron, bold). The drift container shifts the whole cluster ±20% of the 1920×720 viewport every 60s for burn-in protection (Sleep.razor:96-99) — that wrapping behavior does NOT change.

**Default — `TimeFormat = "24h"`, `ShowSeconds = false` (current behavior, preserved):**

```
                  ░░░░░░░░░░░░░░░░░░░░░░░░
                  ░                      ░
                  ░     ┌────────────┐   ░
                  ░     │            │   ░    ← faint album art
                  ░     │  [art]     │   ░       (when present)
                  ░     │            │   ░
                  ░     └────────────┘   ░
                  ░                      ░
                  ░     ╔══════════╗     ░
                  ░     ║  15:45   ║     ░    ← Orbitron 96px,
                  ░     ╚══════════╝     ░       --font-led
                  ░                      ░
                  ░       Lateralus      ░    ← title
                  ░         Tool         ░    ← artist
                  ░                      ░
                  ░░░░░░░░░░░░░░░░░░░░░░░░
                       tap anywhere to wake
```

**After — `TimeFormat = "12h"`, `ShowSeconds = false`:**

```
                  ░░░░░░░░░░░░░░░░░░░░░░░░░░░
                  ░                         ░
                  ░     ╔═══════════════╗   ░
                  ░     ║  3:45 PM      ║   ░   ← AM/PM glyphs use
                  ░     ╚═══════════════╝   ░     the same Orbitron
                  ░                         ░     run; no separate
                  ░       Lateralus         ░     font-size tier
                  ░         Tool            ░
                  ░░░░░░░░░░░░░░░░░░░░░░░░░░░
```

**After — `TimeFormat = "12h"`, `ShowSeconds = true`:**

```
                  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
                  ░                           ░
                  ░     ╔══════════════════╗  ░
                  ░     ║  3:45:22 PM      ║  ░   ← total string is
                  ░     ╚══════════════════╝  ░     ~11 glyphs; still
                  ░                           ░     fits inside the
                  ░         Lateralus         ░     ±384px drift
                  ░           Tool            ░     safe-area at 96px
                  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░       (`Sleep.razor:90-94`)
```

**Note:** AM/PM is rendered with a single trailing space + uppercase (`tt` format specifier in invariant culture). It uses the same Orbitron weight as the digits — no smaller `<sup>`-style treatment. Rationale: the sleep clock is the "wall clock" of the kiosk; commercial LED wall clocks render AM/PM as full-height glyphs.

### 3.3 Topbar Time cluster — before / after

The topbar clock lives at `MainLayout.razor:33-35` inside `.cluster-value > .font-led` (Orbitron, weight 700, color `var(--signal-amber)`, sized via `.topbar-clock` rule at `design-system.css:209`). It coexists with the static label `"Time"` and a vertical separator.

**Default — `24h`, no seconds (preserved):**

```
┌─────────────────────────────────────────────────────────────────┐
│ Time         │ In   ● Radio  →  Out  ● Soundbar   [Cast] [Out]  │  ← Row 1
│ 15:45        │                                                  │
├──────────────┴──────────────────────────────────────────────────┤
│ [Radio] [Bluetooth] [File] [TTS] [USB] [Vinyl] [SDR]            │  ← Row 2
└─────────────────────────────────────────────────────────────────┘
```

**After — `12h`, no seconds:**

```
┌─────────────────────────────────────────────────────────────────┐
│ Time         │ In   ● Radio  →  Out  ● Soundbar   [Cast] [Out]  │
│ 3:45 PM      │                                                  │
└──────────────┴──────────────────────────────────────────────────┘
```

**After — `12h`, show seconds:**

```
┌─────────────────────────────────────────────────────────────────┐
│ Time         │ In   ● Radio  →  Out  ● Soundbar   [Cast] [Out]  │
│ 3:45:22 PM   │                                                  │
└──────────────┴──────────────────────────────────────────────────┘
```

The widest case (`12:34:56 PM` = 11 glyphs) is ~10% wider than the current `15:45` (5 glyphs). The Time cluster has flex space before the `topbar-separator` — verified by inspection of `design-system.css:209-213` (no fixed-width constraint on the cluster). No layout overflow risk on the 1920px kiosk panel.

### 3.4 Queue "ends ~" prediction — before / after

Rendered in the queue-total-tile subline at `QueueHistoryPanel.razor:309`:

```
┌────────────────────────────────────┐
│        ┌────────────┐              │
│        │  1:23:45   │  ← total LED │
│        └────────────┘              │
│   3 tracks · ends ~15:45     ← 24h │ (current)
│   3 tracks · ends ~3:45 PM   ← 12h │ (after)
└────────────────────────────────────┘
```

Same format helper; seconds suppressed on this surface regardless of `ShowSeconds` (it's a forward-looking estimate, not a wall clock — :ss precision is meaningless here).

---

## 4. Interaction — dropdown vs. radio vs. toggle

**Pick: Dropdown (RadzenDropDown).** Justification:

- **Two values today, but explicit forward-compat for more.** A segmented toggle / two-radio works perfectly for binary, but it locks the visual to two cells. Adding `"12h-no-suffix"` later (a future request that's plausible — some users want `3:45` without the PM glyph) would force a chrome rework. A dropdown absorbs it for free.
- **Consistent with sibling tabs.** Every existing setting in `SystemConfigPage.razor` that's "pick one of N enumerated values" uses `RadzenDropDown` with `_options` data (Default Source, Ducking Policy, FFT Size, Audio Quality, Default Engine, etc.). A radio group or segmented control would be the only setting of its kind in the page — design-system drift for the sake of saving one click.
- **Show-seconds is a separate concern (boolean), so it gets its own checkbox.** Sibling tabs use `RadzenCheckBox` inline-flex with a `<span>` label (see Audio Engine "Enable Hot Plug Detection" at `SystemConfigPage.razor:334` for the canonical pattern). Same pattern here.

The "Save Display" button mirrors every other tab's save affordance — `RadzenButton Variant="Variant.Filled" ButtonStyle="ButtonStyle.Primary" Icon="save"`. Save commits both `Display:TimeFormat` and `Display:ShowSeconds` in one round trip via `ConfigurationApiService.SetValueAsync` (or whatever the page already uses for sibling tabs — Planner picks the existing pathway).

---

## 5. Affected components

### 5.1 Components that read the new setting (hot-reload consumers)

| File | Line(s) | Current code | Action |
|---|---|---|---|
| `src/Radio.Web/Components/Layout/MainLayout.razor` | 220, 292, 386 | `DateTime.Now.ToString("HH:mm")` | Inject `IOptionsMonitor<DisplayOptions>`; replace literal with helper call. |
| `src/Radio.Web/Components/Pages/Sleep.razor` | 154 | `DateTime.Now.ToString("HH:mm")` (in `UpdateClock()`) | Inject `IOptionsMonitor<DisplayOptions>`; replace literal with helper call. |
| `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor` | 309 | `DateTime.Now.Add(_totalRuntime).ToString("HH:mm")` | Inject `IOptionsMonitor<DisplayOptions>`; replace literal with helper call (with seconds suppressed). |

### 5.2 New file

| File | Purpose |
|---|---|
| `src/Radio.Web/Models/DisplayOptions.cs` | POCO for `Display:*` section (mirror `DevicesOptions.cs`). |
| `src/Radio.Web/Formatting/Clocks.cs` | Static helper centralising format-string selection. Single source of truth so the three consumers stay in lock-step. Suggested API: `Clocks.FormatWallClock(DateTime local, DisplayOptions opts, bool allowSeconds = true)`. The `allowSeconds` parameter lets `QueueHistoryPanel` suppress seconds even when the global setting is on. |

### 5.3 New tab in System Config

| File | Action |
|---|---|
| `src/Radio.Web/Components/Pages/SystemConfigPage.razor` | Add new `<RadzenTabsItem Text="Display" Icon="schedule">` block inside the inner `RadzenTabs` (the Configuration sub-tabs). Suggested insertion: between Devices and Audio Engine. Add corresponding `_displayConfig` field, `LoadDisplayConfigAsync`, `SaveDisplayConfigAsync` following the **Audio** tab's pattern (`SystemConfigPage.razor:96-133`). |

### 5.4 Files explicitly NOT touched (out of scope — see §9)

| File | Line | Why not |
|---|---|---|
| `src/Radio.Web/Formatting/Timestamps.cs` | 37 | This is a calendar-anchored relative helper ("Today HH:mm", "Yesterday HH:mm"). The date prefix already disambiguates the time domain — flipping just the time portion to 12h would read inconsistently ("Today 3:45 PM" is fine, but "Mar 8 · 3:45 PM" is mixing en-dash separator with PM glyph). Could be done in a follow-up after we see how the user reacts to the wall-clock change. |
| `src/Radio.Web/Components/Pages/MetricsDashboardPage.razor` | 573 | Chart x-axis labels; dense renderings benefit from the compact 24h form regardless of user wall-clock preference. |
| `src/Radio.Web/Components/Pages/SystemConfigPage.razor` | 724, 2214 | Log table — ISO-style `yyyy-MM-dd HH:mm:ss` is a developer-facing timestamp; preserve invariant format. |
| `src/Radio.Web/Components/Pages/PlayHistoryPage.razor` | 153, 253 | Tooltip and drawer use ISO format for unambiguous archival timestamps. |
| `src/Radio.Web/Components/Pages/Diagnostic.razor`, `Bare.razor`, `Minimal.razor` | various | Dev-only diagnostic pages — not user-facing. |
| `src/Radio.Web/Program.cs` | 42 | Serilog format string for log files. Invariant format is correct for log tooling. |

---

## 6. Hot-reload mechanism

The propagation chain — verified against the existing pattern used by `IOptionsMonitor<DevicesOptions>` consumers (`MainLayout.razor:16`, `DeviceManagementPage.razor:10`, `OutputPickerDropdown.razor:130`) and the SQLite bridge wired in `Radio.Configuration/Bridge/ConfigStoreChangeNotifier.cs`:

```
1. User opens Settings → Configuration → Display tab.
2. User changes "Time Format" dropdown from "24-hour" to "12-hour", clicks Save.
3. SystemConfigPage.SaveDisplayConfigAsync calls ConfigApi.SetValueAsync("Display:TimeFormat", "12h")
   and ConfigApi.SetValueAsync("Display:ShowSeconds", false).
4. Radio.API ConfigurationController writes the values to the SQLite Config_sqlite table
   via ConfigurationManager.SetValueAsync (Radio.Configuration/Services/ConfigurationManager.cs).
5. ConfigurationManager calls ConfigStoreChangeNotifier.NotifyReload()
   (Radio.Configuration/Bridge/ConfigStoreChangeNotifier.cs:26).
6. SqliteConfigurationProvider re-reads the table and fires its IChangeToken.
7. .NET's binding pipeline raises IOptionsMonitor<DisplayOptions>.OnChange.
8. The three consumers (MainLayout, Sleep, QueueHistoryPanel) read DisplayOptionsMonitor.CurrentValue
   on their NEXT tick:
     - MainLayout: 1-second System.Timers.Timer (OnTimerElapsed → MainLayout.razor:384-398).
       Worst-case latency: 1s.
     - Sleep: 1-second System.Threading.Timer (Sleep.razor:108-117).
       Worst-case latency: 1s.
     - QueueHistoryPanel: re-renders whenever queue state changes (or on next StateHasChanged).
       For pure setting changes, ~next user interaction.
9. The clock string repaints with the new format. No service restart, no circuit drop.
```

**Optional refinement (Planner decides):** if the user expects "instant" repaint across all surfaces on save, register an `OnChange` callback in `MainLayout.OnInitializedAsync` that calls `InvokeAsync(StateHasChanged)` so the topbar refreshes within the same Blazor render pass. Sleep / QueueHistoryPanel get the same treatment if responsiveness matters there. This is **NOT** required for correctness — the 1s timer already covers it — but the pattern is two extra lines per consumer and aligns with how `DeviceDisplayStateService.DisplaySettingsChanged` is used in `MainLayout.razor:274`.

---

## 7. Accessibility

### Setting controls

- **Time Format dropdown:**
  - `aria-label="Time format"` on the `RadzenDropDown` (Radzen wires this to the underlying `<select>`).
  - Each option text reads as `"12-hour (3:45 PM)"` / `"24-hour (15:45)"` — the parenthesised exemplar gives screen-reader users an unambiguous read without forcing them to scrub between options.
  - Keyboard: Tab to focus, Enter / Space to open, Up/Down to navigate, Enter to select, Esc to close (Radzen default).
- **Show seconds checkbox:**
  - Standard `RadzenCheckBox` with adjacent `<span>Show seconds</span>` label. Click on label toggles state (Radzen default).
  - `aria-label="Show seconds in wall clocks"` on the checkbox for screen-reader clarity (the inline `<span>` is visual-only).
- **Save button:** identical pattern to every sibling tab's save button — no new a11y story.

### Clock surfaces

- **Sleep screen clock:** the wrapping `<div class="sleep-screen" role="button" tabindex="0" aria-label="Tap to wake">` (Sleep.razor:27-32) already handles the surface semantics. The clock text itself is decorative — no aria-label change needed. Screen-readers announce "Tap to wake, button" + the visible text content (which now reads "3:45 PM" or "15:45" as configured).
- **Topbar Time cluster:** today the structure is `<span class="cluster-label">Time</span><span class="cluster-value"><span class="font-led">@_currentTime</span></span>`. No explicit aria — relies on cluster-label as adjacent context. Add `aria-label="Time @_currentTime"` to the outer cluster div to make the screen-reader read "Time 3:45 PM" as one phrase instead of two whitespace-separated fragments. **Optional** — pattern-consistency with In/Out clusters (which don't have this either) argues for leaving it alone in this PR; revisit in a broader a11y pass.
- **Queue ends-tile:** the `<text>` fragment "ends ~3:45 PM" is already inside a meaningful `.queue-total-tile-sub` span; no separate a11y treatment.

### Reduced motion / locale

- The font (Orbitron) renders AM/PM uppercase glyphs cleanly; no fallback path needed. Tested against the existing `--font-led` stack (`'Orbitron', 'JetBrains Mono', 'Consolas', monospace`).
- We render with **invariant culture** (`CultureInfo.InvariantCulture`) — same as the existing `Timestamps.cs:37` pattern — so the `tt` specifier always emits `AM` / `PM` (not `vorm.` / `nachm.` or other locale forms). Locale-aware formatting is explicit non-goal (§9).

---

## 8. Open questions for the user

These are defaults Designer chose without explicit direction from the request. Confirm or override before Builder ships.

1. **Default = 24h.** Chose this to preserve current behavior on first deploy (every existing kiosk continues to show `15:45` after the PR lands, no surprises). User's stated preference is 12h — if user wants the upgrade to flip everyone to 12h by default and let outliers opt in, we change the default in `DisplayOptions.TimeFormat` and (optionally) seed the SQLite store on first migration. **Designer recommendation: ship 24h default + a one-line release note pointing users to Settings → Display → Time Format. Cheapest, no migration surprises.**

2. **Show seconds: separate setting (chosen) vs. coupled to format choice.** Coupling ("12h with AM/PM, no seconds" as one menu entry, "12h with seconds" as another, etc.) collapses the dropdown to 4 options. Splitting (the chosen design) keeps the dropdown to 2 and isolates the boolean. Splitting wins on extensibility (e.g. adding a future "no-suffix" variant doesn't quadruple the option count) and on a11y (two simpler controls beat one 4-way dropdown).

3. **Apply to queue "ends ~" prediction.** Chose to include it under the "wall clock" umbrella. Argument for excluding: it's a derived future-time estimate, not a real clock — different cognitive category. Argument for including (chosen): the user reads it as "wall time at which the queue will finish," which IS a clock reading. Trivial to flip if user disagrees — remove the helper call at `QueueHistoryPanel.razor:309` and revert to `"HH:mm"`.

4. **Show seconds on sleep screen.** Sleep is meant to be calm; adding ticking seconds might defeat the purpose. Default off is unambiguous. If user wants seconds-on-sleep banned regardless of the setting, we add `allowSeconds: false` to the Sleep call site too. **Designer recommendation: honor the global setting on sleep — if the user explicitly turned seconds on, they want to see them everywhere; second-guessing that on one surface is paternalistic.**

5. **AM/PM glyph styling.** Chose full-height glyphs at the same weight as digits. Alternative: smaller superscript `<sup>` treatment, matching wristwatch convention. **Designer recommendation: full-height — the sleep clock is the kiosk's wall-clock surrogate; commercial LED wall clocks render AM/PM full-height. Superscript would also bump us into a custom-styled span inside the existing single-string `_clockText`, which is more invasive.**

6. **Settings page tab icon.** Chose `schedule` (Material Symbols clock-face glyph). Alternatives: `access_time`, `more_time`, `display_settings`. **Designer recommendation: `schedule` — it's the cleanest clock glyph in Material Symbols and unambiguously signals "time."**

---

## 9. Out of scope

Explicitly NOT in this PR. Flag any of these to the user if scope-creep pressure builds.

1. **Custom format strings.** No free-text "format pattern" input box (e.g. `"HH:mm:ss zzz"`, `"h.mm a"`). Two enumerated values + one boolean; nothing else.
2. **Locale-aware formatting.** We use `CultureInfo.InvariantCulture` everywhere. AM/PM glyphs are always uppercase English, never `vorm.` / `am.` / `下午`. If the user later wants culture-aware rendering, that's a separate spec with locale detection, culture-fallback strategy, and a much bigger test matrix.
3. **ISO 8601 dates / date-only displays.** No "show date below time" toggle in this PR. The sleep screen has art + title + artist below the clock already; adding a date row would crowd the composition. Could be added later as a third `Display:*` boolean without restructuring.
4. **Relative-time formatters** (`Timestamps.FormatRelative`, `FormatRecentRelative`). The "Today HH:mm" / "Xs ago" helpers stay 24h-locked in this PR. Reconsidering them is fine but lives in a follow-up that also needs UX work on "Today 3:45 PM" vs. "Mar 8 · 3:45 PM" consistency (see §5.4).
5. **Chart axis labels and log timestamps.** `MetricsDashboardPage.razor`, `SystemConfigPage.razor` log table, `PlayHistoryPage.razor` tooltips, and `Program.cs` Serilog format string all stay in their current invariant 24h / ISO formats. These are developer- or archival-facing, not wall-clock surfaces.
6. **Per-surface override.** No "make ONLY the sleep screen 12h while the topbar stays 24h" controls. One setting, all surfaces.
7. **Server-side respect.** The Radio.API service emits timestamps in its own logs and DTOs using its own format conventions — this PR does not touch them. The setting is a Web/UI concern only.
8. **Timezone handling.** All clocks display local wall time (server `DateTime.Now`), unchanged. No "show as UTC" option. No "show secondary timezone" feature.

---

## Hand-off summary for Planner / Builder

One new `DisplayOptions` POCO (`Display:TimeFormat` string + `Display:ShowSeconds` bool), bound in `Program.cs` alongside `DevicesOptions`. One new `Clocks.FormatWallClock(local, opts, allowSeconds)` helper in `src/Radio.Web/Formatting/`. Three single-line replacements at `Sleep.razor:154`, `MainLayout.razor:220/292/386`, `QueueHistoryPanel.razor:309` to call the helper instead of the literal `"HH:mm"`. One new "Display" sub-tab inside `SystemConfigPage.razor`'s Configuration block with a dropdown, a checkbox, and a Save button (pattern-copied from sibling tabs). Hot-reload free of charge — the existing SQLite-bridge + `IOptionsMonitor` plumbing already handles it; consumers re-read `DisplayOptionsMonitor.CurrentValue` on their 1s ticks. No new tokens, no new API endpoints, no DB migration.
