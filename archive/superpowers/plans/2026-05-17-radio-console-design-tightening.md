# Radio Console — Design Tightening Arc

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement each PR task-by-task. Each PR in this arc is a standalone unit — pick one PR section and ship it end-to-end. Do NOT bundle multiple PRs into a single branch.

## Arc overview

This arc converts the approved Radio Console design-tightening handoff (`docs/handoffs/design_handoff_radio_console/`) into shippable work. The handoff defines 12 change items (P0·0–P2·2) plus a cross-cutting nice-to-haves section. The visual + behaviour spec lives in `Handoff Canvas.html` and `IMPLEMENTATION.md` inside that folder — this plan does NOT redesign anything; it slices the approved script into reviewable PR-sized units.

The arc is multi-PR because:

1. **P0·0 (Formatting layer)** is a foundation that P0·2, P0·3, and P0·4 all consume. It must land first.
2. **The topbar redesign (P0·1)** touches `MainLayout.razor` heavily and is large enough to deserve its own review window, especially with the new `SourceBubble.razor` extraction.
3. **Display-name and timestamp fixes (P0·2 + P0·3)** touch the same panels (`NowPlayingPanel`, `QueueHistoryPanel`) and naturally group.
4. **Metrics, File Browser, Now Playing dock, source-pill semantics, Visualizer, Queue split, Skeletons, Sleep, Dev tray** each touch distinct surfaces and are best landed as small focused PRs.

We collapse the 12 items into **6 sequenced PRs**:

| PR | Items landed | Theme |
|---|---|---|
| PR 1 | P0·0 | Formatting layer foundation |
| PR 2 | P0·1 + cross-cutting nice-to-haves | Topbar redesign + a11y/legibility polish |
| PR 3 | P0·2 + P0·3 + P0·5 | Display names, durations/timestamps, File Browser chips |
| PR 4 | P0·4 + P1·3 + P1·5 | Metrics tiles + Visualizer mode picker + Skeleton states |
| PR 5 | P1·1 + P1·2 + P1·4 | Now Playing dock + source-pill semantics + Queue split |
| PR 6 | P2·1 + P2·2 | Sleep screen + Dev tray gesture |

**Branch lineage rules (apply to every PR):**

- Branch from `main`, NOT from `feature/rotaryphone-sip-integration` (that branch has unrelated SIP/PhonePage WIP that must not ride along).
- Name pattern: `feat/web-design-<short-theme>` (e.g. `feat/web-design-formatting-layer`).
- Each PR opens against `main` and is squash-merged after review.
- Do NOT chain PRs off each other unless a hard code dependency requires it — when PR N's files only *consume* PR N-1's new APIs, rebase onto main after the prior PR merges rather than stacking.

**Tech stack:** .NET 10, Blazor Server (Radzen + MudBlazor), `src/Radio.Web/`, design tokens in `src/Radio.Web/wwwroot/css/design-system.css`. Tests in `tests/Radio.Web.Tests/`. Target viewport: **1920×720 kiosk**.

**Build/test cadence (every PR):**

```bash
dotnet build --configuration Release        # 0 warnings expected
dotnet test  --configuration Release        # ~1,697 tests across 10 projects
```

UAT happens on the Ubuntu kiosk:

```powershell
.\deploy\Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
```

Then browse `http://radio:5002/` at 1920×720.

---

## PR 1 — `feat(web): formatting layer for durations, timestamps, display names, and units`

**Handoff items landed:** P0·0

**Why standalone:** This is the foundation that PRs 3 and 4 consume. Landing it alone keeps the diff tiny and reviewable (~250 LOC including tests), and lets reviewers focus on the formatting contract rather than its consumers.

**Branch:** `feat/web-design-formatting-layer` off `main`.

### Files

**New:**
- `src/Radio.Web/Formatting/Durations.cs`
- `src/Radio.Web/Formatting/Timestamps.cs`
- `src/Radio.Web/Formatting/DisplayNames.cs`
- `src/Radio.Web/Formatting/Units.cs`
- `tests/Radio.Web.Tests/Formatting/DurationsTests.cs`
- `tests/Radio.Web.Tests/Formatting/TimestampsTests.cs`
- `tests/Radio.Web.Tests/Formatting/DisplayNamesTests.cs`
- `tests/Radio.Web.Tests/Formatting/UnitsTests.cs`

**Modified:**
- `src/Radio.Web/Components/_Imports.razor` — add `@using Radio.Web.Formatting`
- `src/Radio.Web/appsettings.json` — add an empty `Devices:Aliases` object so PR 3 can populate without touching schema (optional; PR 3 can add it too — surface as open question below).

### Implementation notes (verbatim from handoff §P0·0, condensed)

1. Flat `static class` helpers under `Radio.Web.Formatting`. No DI, no state.

2. `Durations.FormatTrack(TimeSpan t)`:
   - `t < 1h` → `"{m}:{ss:00}"` (e.g. `3:00`)
   - `t >= 1h` → `"{h}:{mm:00}:{ss:00}"` (e.g. `1:02:14`)
   - `t.TotalSeconds < 1 || t == TimeSpan.Zero` → `"—"`
   - `TimeSpan?` overload returns `"—"` on null.

3. `Durations.FormatLong(TimeSpan t)` for queue totals: always `h:mm:ss`.

4. `Timestamps.FormatRelative(DateTime local)`:
   - Same calendar day → `"Today {HH:mm}"`
   - Yesterday → `"Yesterday {HH:mm}"`
   - Older → `"{MMM d} · {HH:mm}"`
   - Caller converts UTC → local before calling.

5. `DisplayNames.Source(AudioSourceDto s)`:
   - Strip `-{guid}` suffix from `Id` when `Name` is missing.
   - Return `Name` if set, else humanize `Type` (`"FilePlayer"` → `"File Player"`).
   - **Never** return raw `Id`.

6. `DisplayNames.Device(AudioDeviceDto d, IDictionary<string,string>? aliasMap = null)`:
   - Apply `aliasMap` first.
   - Strip trailing parenthesized hardware-driver suffixes when head is sufficiently descriptive (define threshold: head length ≥ 4 chars AND contains a space, OR explicit allow list — Builder decides during impl, document in code).
   - Strip leading `"N - "` enumeration prefixes.
   - Cap at 40 chars + ellipsis.

7. `DisplayNames.Track(NowPlayingDto np)`:
   - Prefer `np.Title` if non-empty and not literally `"Track {n}"` filename-derived.
   - When generic, parse `np.FilePath`: strip extension, strip `^\d+[\s\-_]+`, title-case if all-lowercase.
   - Subtitle: `np.Artist ?? "—"`.

8. `Units` enum + `Units.Format(double value, Units unit)`:
   ```csharp
   public enum Units {
     Percent, Megabytes, Milliseconds, Count, PerMinute, Frequency, Decibels, Bare
   }
   ```
   - Thousands-separated for `Count`.
   - One decimal for `Percent` if value < 10, else integer.
   - `Milliseconds` auto-converts to `"{value/1000:0.0} s"` when ≥ 1000.
   - `Frequency` delegates to existing `FrequencyFormatter`.

### Tests (xUnit)

- `DurationsTests` — round-trip the acceptance cases: 180.66s → `3:00`; 0s → `—`; 3742s → `1:02:22`; `null` → `—`. ~10 cases.
- `TimestampsTests` — freeze `now`, exercise same-day / yesterday / older branches. Use a `Func<DateTime>` clock hook or pass `now` as a second arg for testability (Builder picks one approach; document choice).
- `DisplayNamesTests`:
  - GUID-suffix stripping cases.
  - `Track 8` + `FilePath = ".../01 - opening.mp3"` → `"Opening"`.
  - Device alias map applied; long names ellipsised; `N - ` prefix stripped.
  - Regex assertion: result never matches `[0-9a-f]{8}-[0-9a-f]{4}`.
- `UnitsTests` — one assertion per enum variant for boundary values.

### Acceptance

- [ ] All four formatter files compile under `Radio.Web.Formatting`.
- [ ] `Durations.FormatTrack(TimeSpan.FromSeconds(180.6628))` → `"3:00"`.
- [ ] `DisplayNames.Source` results never contain a GUID character span (regex test in unit tests).
- [ ] Unit test coverage ≥ 90% on each formatter class (verify with `dotnet test --collect:"XPlat Code Coverage"` — soft target, don't block PR on it).
- [ ] `dotnet build` clean, 0 warnings.
- [ ] `_Imports.razor` updated so Razor files can call formatters without explicit `@using` per file.

### UAT

No UI surface yet — formatter library is dead code until PR 2/3 consume it. The acceptance gate is the test suite.

### Dependencies

None. This is the foundation PR.

### LOC estimate

~250–350 (production) + ~250 (tests). Single PR, easily reviewable.

---

## PR 2 — `feat(web): topbar redesign with source bubbles and a11y polish`

**Handoff items landed:** P0·1 + cross-cutting nice-to-haves (scrollbars, `--text-medium` bump, `aria-label`, focus rings)

**Why standalone:** P0·1 is the single largest change in the package — it restructures `MainLayout.razor`, extracts a new `SourceBubble.razor` component, and reworks four CSS sections. Folding it together with display-name fixes (P0·2) would create a monster PR. The cross-cutting nice-to-haves naturally belong here because they primarily touch `design-system.css` and `MainLayout.razor` chrome.

**Branch:** `feat/web-design-topbar` off `main`. Rebase onto main after PR 1 merges (no code dependency on PR 1 — topbar wiring to display names happens in PR 3).

### Files

**New:**
- `src/Radio.Web/Components/Shared/SourceBubble.razor`
- `src/Radio.Web/Components/Shared/SourceBubble.razor.css` (scoped CSS optional — Builder may inline into design-system.css §N Source bubble)
- `tests/Radio.Web.Tests/Components/Shared/SourceBubbleTests.cs`

**Modified:**
- `src/Radio.Web/Components/Layout/MainLayout.razor` — restructure topbar; remove `route-toggle` markup; remove debug button (gate behind `#if DEBUG` until P2·2 lands).
- `src/Radio.Web/wwwroot/css/design-system.css`:
  - `§2 CSS Custom Properties` — `--topbar-height: 120px`, `--content-height: 600px`, bump `--text-medium` from `#9CA3AF` to `#B5BCC9`.
  - `§5 Top Bar` — `.topbar-primary` (64px), `.topbar-sources` (56px), `.cluster`, `.cluster-label`, `.cluster-value`, `.cluster-swatch`.
  - `§6 Route Toggle` — delete `.route-toggle`/`.route-active` after migration confirms no other references.
  - `§7 Navigation Icons` → rename to `§7 Navigation Pills`, replace `.nav-icon` with `.nav-pill` (56px tall, 16px h-padding, 10px radius), `.nav-pill.active`, `.nav-badge`.
  - `§19 Scrollbars` — replace blanket-hide with thin (3px) visible thumb on `.panel-body` and `.queue-list-wrapper`.
  - New `§N Focus rings` — `:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }` while preserving `-webkit-tap-highlight-color: transparent` for touch.

### Implementation notes (verbatim from handoff §P0·1, condensed)

1. Bump topbar from 80px → 120px; verify nothing else hard-codes `640px`/`80px` (grep both).
2. Two-row topbar inside `.topbar`:
   - **Row 1 `.topbar-primary` (64px):** Time cluster | In cluster | `→` glyph | Out cluster | nav pill strip.
   - **Row 2 `.topbar-sources` (56px):** Left-aligned source bubble strip.
3. Cluster atoms: `.cluster-label` (mono 10px, 0.16em letter-spacing, uppercase, `--text-low`), `.cluster-value` (15px, `--text-high`, 500 weight, may contain inline `--font-led` span), `.cluster-swatch` (8×8, 2px radius).
4. Replace nine `.nav-icon` circles with `.nav-pill` rectangles (56px tall, 16px padding, 10px radius, Inter 12px uppercase 0.10em letter-spacing). Active: `background: var(--accent-dim); color: var(--accent);`. Queue badge variant: amber pill, 10px font.
5. `SourceBubble.razor` props:
   ```
   [Parameter] public string Icon
   [Parameter] public string Label
   [Parameter] public string? Sub
   [Parameter] public string Accent       // CSS variable name, e.g. "--source-radio"
   [Parameter] public bool IsActive
   [Parameter] public bool IsDisabled
   [Parameter] public bool HasDetail
   [Parameter] public EventCallback OnSwitch
   [Parameter] public EventCallback OnOpenDetail
   ```
   - Pill shape `height: 48px; border-radius: 24px`, 28×28 round icon chip on left, label + optional sub.
   - Active: `background: {accent}14; border: 1px solid {accent}55; color: {accent};`.
   - `HasDetail && IsActive` → trailing `›` glyph as separate focusable element bound to `OnOpenDetail`, with `@onclick:stopPropagation`.
   - `IsDisabled` → `opacity: 0.4; pointer-events: none;` and append ` · offline` to `Sub`.
   - **Keep the `data-source` attribute** on the root element so existing tests / CSS hooks don't break.
6. Remove the inline `RadzenButton` debug distortion marker at lines ~76–83 of `MainLayout.razor`. Gate behind `#if DEBUG` (lower priority — full removal happens in P2·2 / PR 6).
7. Verify `.content-area` margin tracks `--topbar-height` and not a hard `80`.
8. **Cross-cutting:**
   - Add `aria-label` to every icon-only button in `MainLayout` (Sleep, Queue badge, debug button if it stays for DEBUG builds).
   - Scrollbar CSS as in handoff:
     ```css
     .panel-body { scrollbar-width: thin; scrollbar-color: var(--text-low) transparent; }
     .panel-body::-webkit-scrollbar { display: block; width: 3px; }
     .panel-body::-webkit-scrollbar-thumb { background: var(--text-low); border-radius: 2px; }
     ```
   - Same rules for `.queue-list-wrapper`.

### Tests

- `SourceBubbleTests` (bUnit, MudBlazor + Radzen with `JSInterop.Mode = JSRuntimeMode.Loose`):
  - Renders label + sub.
  - `IsActive` applies the accent class and shows the chevron only when `HasDetail`.
  - Clicking the body fires `OnSwitch`; clicking the chevron fires `OnOpenDetail` but NOT `OnSwitch`.
  - `IsDisabled` suppresses click and appends "offline" sub text.
- Update or smoke-check `MainLayout` rendering tests if any exist that assert `.route-toggle`. Add a smoke test that renders `MainLayout` and asserts: zero `.route-toggle` elements; one `.topbar-primary`; one `.topbar-sources`; nav pills present.

### Acceptance (from handoff)

- [ ] Top bar matches the "After" artboard within ±2px on a 1920-wide preview.
- [ ] No circular pill remains anywhere in the top bar.
- [ ] Active source AND active output both show their human name (not just a colored icon).
- [ ] Debug button is gone from release builds.
- [ ] Content pages still vertically fit in 600px without scrollbars on Home/Devices/Queue.
- [ ] Thin scrollbar thumb appears on overflowing `.panel-body`.
- [ ] `--text-medium` is `#B5BCC9`.
- [ ] Tabbing through the topbar shows visible focus rings on every focusable element.

### UAT (1920×720 kiosk)

- [ ] Deploy and open Home — topbar is 120px, two rows, sources show as labelled pills.
- [ ] Active source pill has correct per-source accent color.
- [ ] Queue badge appears as an amber pill on the Queue nav button.
- [ ] Tab key cycles focus rings cleanly across topbar elements.
- [ ] No black or shifted layout on Home/Devices/Queue/Metrics — all pages still fit 600px content area.
- [ ] Screenshot diff against `screenshots/home.png` matches handoff "After" artboard.

### Dependencies

None hard. Cosmetic only — PR 1 not strictly required, but rebase onto main after PR 1 merges to avoid conflicts in `_Imports.razor`.

### LOC estimate

~500–700 (MainLayout + SourceBubble + CSS + tests).

---

## PR 3 — `feat(web): clean display names, duration/timestamp formatting, and File Browser chips`

**Handoff items landed:** P0·2 + P0·3 + P0·5

**Why grouped:** P0·2 (display names) and P0·3 (durations) both touch `NowPlayingPanel.razor`, `QueueHistoryPanel.razor`, and `DeviceManagementPage.razor` — natural co-edit. P0·5 (File Browser chip filter) is isolated to `FileBrowserDialog.razor` but is a small P0 fix that fits the "data-hygiene polish" theme and keeps PR 4 focused on dashboards/visualizer.

**Branch:** `feat/web-design-display-hygiene` off `main`. **Hard dependency on PR 1** (consumes the formatters). Rebase onto main after PR 1 merges.

### Files

**Modified:**
- `src/Radio.Web/Components/Layout/MainLayout.razor` — source bubble labels via `DisplayNames.Source`.
- `src/Radio.Web/Components/Shared/NowPlayingPanel.razor` — `DisplayNames.Track`, `Durations.FormatTrack`.
- `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor` — `Durations.FormatTrack`, `Durations.FormatLong`, currently-playing row treatment.
- `src/Radio.Web/Components/Pages/DeviceManagementPage.razor` — `DisplayNames.Device` with alias map.
- `src/Radio.Web/Components/Dialogs/FileBrowserDialog.razor` — chip-based filter; fix breadcrumb/drive selector binding.
- `src/Radio.Web/Components/Pages/PlayHistoryPage.razor` — `Timestamps.FormatRelative` for Date/Time column.
- `src/Radio.Web/appsettings.json` — populate `Devices:Aliases` map.
- `src/Radio.Web/Models/DevicesOptions.cs` (new, or wherever options types live — check `src/Radio.Web/Models/`) — typed binding for `Devices:Aliases`.
- `src/Radio.Web/Program.cs` — bind `DevicesOptions` to configuration.

**Tests (modified or added):**
- `tests/Radio.Web.Tests/Components/Shared/QueueHistoryPanelTests.cs` — assert currently-playing row has amber border + `▶`.
- `tests/Radio.Web.Tests/Components/Shared/NowPlayingPanelTests.cs` — assert no `TimeSpan.ToString` artefacts.
- `tests/Radio.Web.Tests/Components/Dialogs/FileBrowserDialogTests.cs` — chip add/remove, persistence round-trip without double-escaping.

### Implementation notes (verbatim from handoff §P0·2, §P0·3, §P0·5, condensed)

**P0·2 (display names):**
1. Add `Devices:Aliases` to `appsettings.json`:
   ```json
   "Devices": {
     "Aliases": {
       "CABLE Input (VB-Audio Virtual Cable)": "VB-Audio Cable In",
       "CABLE In 16ch (VB-Audio Virtual Cable)": "VB-Audio Cable 16ch",
       "Realtek Digital Output (Realtek USB Audio)": "Realtek USB · S/PDIF"
     }
   }
   ```
2. Bind a typed `DevicesOptions` in `Program.cs` and inject `IOptions<DevicesOptions>` where needed.
3. Replace every raw-DTO-field binding with the appropriate `DisplayNames.*` call.
4. Add `title="@rawName"` HTML tooltip on every display-name element for debug/a11y reach to the raw value.
5. **Do NOT change DTOs server-side** — only display projection changes.

**P0·3 (durations + timestamps):**
1. Replace every `@track.Duration.ToString()` with `@Durations.FormatTrack(track.Duration)`.
2. Replace progress display `@_elapsed.ToString(@"mm\:ss")` with `@Durations.FormatTrack(_elapsed)` to handle >1h case.
3. PlayHistory: `@Timestamps.FormatRelative(row.PlayedAtUtc.ToLocalTime())` for Date/Time column.
4. Add `font-variant-numeric: tabular-nums;` to every duration element so columns align.
5. Right-align duration columns.
6. Currently-playing row in queue: `border-left: 3px solid var(--signal-amber);` + replace index cell with `▶`.

**P0·5 (File Browser chips):**
1. Find filter persistence boundary — likely `JsonSerializer.Serialize` calls referencing a filter string or `LocalStorage`-backed filter.
2. Replace persisted `string` with `List<string> _filterExtensions`. Add one-time repair pass for double-escaped legacy values: parse repeatedly until you get a `string[]`, then take it.
3. Render row of `RadzenChip` with remove `×`. Append dashed `＋ add type` chip → popover with `.mp3 .flac .wav .m4a .aac .ogg .opus .wma`.
4. Wire filter into file list query as case-insensitive `EndsWith` match.
5. Fix breadcrumb/drive selector mismatch — both bind to `_currentPath`.

### Acceptance (from handoff)

P0·2:
- [ ] No GUID character span (`[0-9a-f]{8}-[0-9a-f]{4}`) appears in any user-visible text on Home, Devices, Queue, History, File Browser.
- [ ] `Track 8` / `Track 9` cases display parsed file names.
- [ ] Long device strings ≤ 40 chars + ellipsis; full name in `title` tooltip.

P0·3:
- [ ] No `00:03:00.6628571`-style duration appears anywhere.
- [ ] Queue rows show amber-left-border + `▶` indicator for currently-playing.
- [ ] History Date/Time column reads `Today 08:40`, `Yesterday 14:22`, or `Feb 6 · 11:05` per age.

P0·5:
- [ ] No escaped JSON appears in filter UI.
- [ ] Chips reorder/remove cleanly; persistence round-trips without re-escaping.
- [ ] Adding `.mp3` filters file list immediately (no Apply button).
- [ ] Breadcrumb and drive selector stay in sync.

### UAT (1920×720 kiosk)

- [ ] Switch between Bluetooth / File Player / Radio sources — topbar source bubble label is human-readable in every case, no GUIDs.
- [ ] Devices page — paired BT device shows clean name; hover reveals raw name.
- [ ] Queue with a file playing — currently-playing row has amber-left + `▶` glyph; duration column reads `3:42`, not `00:03:42.000…`.
- [ ] Queue total tile reads `2:48:15`, not `02:48:15.4567890`.
- [ ] Play history shows `Today 14:22`, `Yesterday 09:15`, `Mar 6 · 11:05` mix.
- [ ] File Browser: open, see chips for current filter, remove `.flac` and watch list update without Apply; persistence survives dialog close/reopen.
- [ ] File Browser: breadcrumb and drive selector show the same drive after navigation.

### Dependencies

**PR 1 must be merged first** (consumes all four formatters).

### LOC estimate

~400–600 across 7 files + tests.

---

## PR 4 — `feat(web): metrics dashboard, visualizer mode picker, and skeleton loading states`

**Handoff items landed:** P0·4 + P1·3 + P1·5

**Why grouped:** All three are about how *information surfaces inside panels* — metric tiles, visualizer mode segments, and skeleton placeholders. Skeleton is naturally introduced alongside the metric tile redesign (which has visible loading states) and the visualizer rework (same). Grouping keeps the new `Skeleton.razor` and `Sparkline.razor` components reviewed together and prevents two PRs both touching the loading-state branches across the same files.

**Branch:** `feat/web-design-data-surfaces` off `main`. Hard dependency on PR 1 (for `Units` enum).

### Files

**New:**
- `src/Radio.Web/Components/Shared/MetricTile.razor`
- `src/Radio.Web/Components/Shared/Sparkline.razor`
- `src/Radio.Web/Components/Shared/Skeleton.razor`
- `tests/Radio.Web.Tests/Components/Shared/MetricTileTests.cs`
- `tests/Radio.Web.Tests/Components/Shared/SparklineTests.cs`
- `tests/Radio.Web.Tests/Components/Shared/SkeletonTests.cs`

**Modified:**
- `src/Radio.Web/Components/Pages/MetricsDashboardPage.razor` — group by category, swap layout, use `MetricTile`.
- `src/Radio.Web/Components/Shared/VisualizerPanel.razor` — 3-segment header, full-bleed canvas, connection dot, remove burned-in telemetry.
- Loading branches across:
  - `src/Radio.Web/Components/Shared/NowPlayingPanel.razor`
  - `src/Radio.Web/Components/Shared/RadioControlPanel.razor`
  - `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor`
  - `src/Radio.Web/Components/Shared/VisualizerPanel.razor`
  - `src/Radio.Web/Components/Pages/DeviceManagementPage.razor`
  - `src/Radio.Web/Components/Pages/PlayHistoryPage.razor`
- Server-side metric descriptor — add `Unit` field. Located in `src/Radio.Metrics/` (standalone NuGet package). **Check whether this requires the package to be re-versioned + re-published locally.** Surface as open question.

### Implementation notes (handoff §P0·4, §P1·3, §P1·5, condensed)

**P0·4 (metrics):**
1. Add `Unit` enum to each metric descriptor on server side. Defaults to `Bare`. Mirrors `Radio.Web.Formatting.Units`.
2. `MetricTile.razor` parameters: `Category`, `Name`, `Value (double)`, `Unit`, `Series (IReadOnlyList<double>?)`, `Thresholds (double? warn, double? critical)`.
   - Top: category in mono, 10px, uppercase, low contrast.
   - Middle: name in body, 13px, medium contrast.
   - Value: `--font-mono`, 30px, color from thresholds (green/amber/red) or `--text-high`.
   - Sparkline: if `Series` provided, render `<Sparkline />` matched to value color.
3. `Sparkline.razor` — pure inline SVG, `viewBox="0 0 120 28" preserveAspectRatio="none"`, takes `IReadOnlyList<double>` + `Stroke` parameter. Filled area 10% opacity below line.
4. Group tiles by category with sub-header (mono 11px uppercase low contrast, 14px bottom margin). 2- or 4-column grid.
5. Specific fixes from screenshots:
   - `Memory Usage Mb` → Unit `Megabytes` → "850 MB" (NOT "850.4%").
   - `Signal Strength` → Unit `Percent` → "65%".
   - `Frequency Changes` → Unit `Count` → thousands-separated.
   - `Latency Ms` → Unit `Milliseconds` → auto-converts to `s` above 1000.

**P1·3 (visualizer):**
1. Replace corner chip group with full-width 3-segment header (VU / WAVE / SPECTRUM), grid 3-col, 44px tall, mono 12px uppercase 0.10em letter-spacing.
   - Active: `background: var(--accent-dim); color: var(--accent);` with 2px accent underline.
   - Inactive: `color: var(--text-medium);` separated by 1px `var(--surface-separator)` borders.
2. Canvas fills remaining vertical space. Remove current padding.
3. Move "Connected" pill to single 8px dot at top-right of header — `var(--signal-green)` when connected, `var(--signal-red)` when not. No text.
4. Remove burned-in `"Updates: 22/sec"` overlay from the canvas — moves to dev tray in PR 6.
5. Frequency-band axis labels (`375Hz / 750Hz / 1.1kHz / 1.5kHz`) under canvas in 16px strip, mono 10px, low contrast.

**P1·5 (skeletons):**
1. `Skeleton.razor`:
   ```csharp
   public enum Shape { NowPlaying, Radio, ListRow, DeviceRow, MetricTile, Visualizer }
   [Parameter] public Shape Shape { get; set; }
   ```
   Each shape arranges existing `.skeleton-*` divs from `design-system.css §17` to match real component layout.
2. Replace every `RadzenProgressBarCircular` used as generic loading indicator with `<Skeleton Shape="..." />`.
3. Keep `RadzenProgressBarCircular` only for modal/determinate progress (e.g. file scan bar).

### Tests

- `MetricTileTests` — value color matches threshold band; sparkline rendered only when `Series` provided.
- `SparklineTests` — SVG path d-attribute computed correctly for known input; min/max normalization.
- `SkeletonTests` — each `Shape` value produces a distinct DOM arrangement (basic structural assertions).
- Update `MetricsDashboardPageTests` (if it exists) — assertion that "Memory Usage" tile shows MB not %.
- Update `VisualizerPanelTests` (if it exists) — 3-segment header present, no burned-in "Updates" text.

### Acceptance (from handoff)

P0·4:
- [ ] Memory tile reads `850 MB`, never `%`.
- [ ] Each tile shows a sparkline when time-series data exists in the selected window.
- [ ] Category sub-headers replace per-tile category prefix.
- [ ] Tile value color matches threshold semantics (green/amber/red) — not all cyan.

P1·3:
- [ ] Mode picker is obvious primary control of panel.
- [ ] Canvas fills available height/width, no debug telemetry overlaid.
- [ ] Connection state is small dot, not pill.

P1·5:
- [ ] No centered spinner in any panel-body loading branch in `Components/`.
- [ ] First-load on Radio shows freq-well + band-button + meter outlines, then animates to real content.

### UAT (1920×720 kiosk)

- [ ] Metrics page — category sub-headers visible; Memory tile reads "850 MB"; sparklines render under tiles with time-series data; color matches threshold.
- [ ] Home → Visualizer panel — VU/WAVE/SPECTRUM 3-segment header at top, canvas fills below; no "Updates: NN/sec" overlay; connection dot top-right.
- [ ] Hard-refresh Home — skeleton outlines flash before real content, no spinner.
- [ ] Hard-refresh Devices page — device-row skeletons flash, then real rows.
- [ ] Cold-load Radio page — band-button + freq-well skeletons appear.

### Dependencies

**PR 1 must be merged** (`Units` enum). If `Radio.Metrics` package needs a version bump for the new `Unit` field, surface that as an open question — Builder may need to coordinate with the local NuGet feed (`pack-local.ps1`).

### LOC estimate

~600–800 (three new components + dashboard refactor + visualizer rework + skeleton swaps).

---

## PR 5 — `feat(web): persistent Now Playing dock, source-pill semantics, and queue split layout`

**Handoff items landed:** P1·1 + P1·2 + P1·4

**Why grouped:** All three are P1 "shape of the product" changes that interact:
- P1·1 (dock) needs the source bubble + accent treatment from PR 2.
- P1·2 (source-pill semantics) directly modifies the `SourceBubble.razor` and `MainLayout.HandleSourceToggleAsync` from PR 2.
- P1·4 (queue split) re-shapes `QueueHistoryPanel.razor` which PR 3 already touched for duration formatting — landing them together avoids a double-edit churn on that file.

**Branch:** `feat/web-design-queue-and-dock` off `main`. Hard dependencies on PR 2 (SourceBubble) and PR 3 (duration formatters in QueueHistoryPanel).

### Files

**New:**
- `src/Radio.Web/Components/Shared/NowPlayingDock.razor`
- `tests/Radio.Web.Tests/Components/Shared/NowPlayingDockTests.cs`

**Modified:**
- `src/Radio.Web/Components/Layout/MainLayout.razor`:
  - Mount `NowPlayingDock` inside `.content-area`, conditional on non-Home routes.
  - Rewrite `HandleSourceToggleAsync` — tap always switches source, never opens/closes panel.
  - Audit & remove tap-driven `RadioPanelToggleService.ToggleRadioPanelAsync()` calls.
- `src/Radio.Web/Components/Shared/SourceBubble.razor` — wire `HasDetail` chevron's `OnOpenDetail` to route or panel-show call sites.
- `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor` — split into list (flex 1.6) + context (flex 1); tab strip; right-column tiles (Queue Total, Up Next, Save as playlist); move ADD FILES / CLEAR ALL into kebab.
- `src/Radio.Web/wwwroot/css/design-system.css` — new `§N Dock` section.
- `src/Radio.Web/Services/RadioPanelToggleService.cs` — verify `ShowRadioPanelAsync` exists (add if only `ToggleRadioPanelAsync` does).

### Implementation notes (handoff §P1·1, §P1·2, §P1·4, condensed)

**P1·1 (Now Playing dock):**
1. `NowPlayingDock.razor`:
   - 64px tall, bottom edge of content area.
   - Left→right: art thumb 48×48 → title + artist → 3-bar EQ animation (`.now-playing-bars`) → elapsed `mm:ss` → progress bar (3px, `--accent`) → total `mm:ss` → ⏮ play/pause ⏭.
   - Subscribes to `AudioStateHubService.NowPlayingChanged` + `PlaybackStateChanged` only — no extra polling.
   - Click on title/art → `NavigationManager.NavigateTo("/")`.
   - Backdrop: `background: var(--surface-overlay); backdrop-filter: blur(20px); border-top: 1px solid var(--surface-separator);`.
2. Mount in `MainLayout.razor` inside `.content-area` as sibling of `.page-transition`, absolute-positioned to bottom. Render only when route is NOT `/` or empty.
3. When dock is rendered, reduce `.page-transition` effective height by 64px.
4. 8×8 source color dot next to artist line using `--source-*` variables.

**P1·2 (source-pill semantics):**
1. Delete second-tap-toggles-panel and second-tap-navigates branches from `HandleSourceToggleAsync`. Tap body **always** switches source.
2. `SourceBubble.razor` already renders chevron when `HasDetail && IsActive` (from PR 2). Confirm it's a separate focusable element with `stopPropagation`.
3. `HasDetail = true` for `Radio`, `RTLSDRCore`, `RF320`, `Bluetooth`. Not for `File`, `USB`.
4. Chevron-tap dispatch:
   - Radio family → `NavigateTo("/")` + `RadioPanelToggleService.ShowRadioPanelAsync()`.
   - Bluetooth → `NavigateTo("/bluetooth")`.
5. Audit `RadioPanelToggleService` callers — `ToggleRadioPanelAsync()` is only invoked by explicit chevron tap or in-panel close, never tap-driven.

**P1·4 (queue split):**
1. Split `QueueHistoryPanel.razor` columns: `flex: 1.6` (list) | `flex: 1` (context). 12px gap.
2. Tab strip at top of list column: `Queue · N` / `History` / `Radio` (third only when active source is a radio type). Active uses `--accent-dim` background, mono 12px uppercase 0.10em letter-spacing.
3. Right context — three stacked tiles:
   - **Queue Total** — `--font-led`, 30px amber, `Durations.FormatLong(totalRuntime)` + sub `{count} tracks · ends ~{nowPlus(total):HH:mm}`.
   - **Up next** — three 32×32 album thumbs + title/artist, dimmed progressively (text-high → text-medium → text-low).
   - **Save as playlist** — accent-bordered CTA `＋ Save as playlist`.
4. ADD FILES / CLEAR ALL header buttons move into kebab menu in list-column header.

### Tests

- `NowPlayingDockTests` — renders given a `NowPlayingDto`; click on title triggers navigation to `/`; respects `PlaybackState`.
- `QueueHistoryPanelTests`:
  - Tab strip switches list contents without remounting right column.
  - Right column shows Queue Total LED, Up Next thumbs, Save CTA.
  - Kebab opens menu containing ADD FILES + CLEAR ALL.
- Update `MainLayoutTests` (if exists) — assert `NowPlayingDock` mounts on `/devices` but not on `/`.
- `SourceBubbleTests` (from PR 2) — second tap on already-active body does NOT fire `OnOpenDetail`; chevron tap fires only `OnOpenDetail`.

### Acceptance (from handoff)

P1·1:
- [ ] Navigating `/` → `/devices` reveals dock; back to `/` removes it.
- [ ] Dock shows same track/state as home within one SignalR round-trip.
- [ ] Tapping title navigates home.
- [ ] No layout shift on track change (`min-width: 200px` on metadata block).

P1·2:
- [ ] Tapping a source pill body switches source exactly once, never opens a panel.
- [ ] Active source pill shows chevron only when detail surface exists.
- [ ] Tapping chevron opens detail; pill body does not.

P1·4:
- [ ] Content area fills when queue has ≥1 track (no large empty void below).
- [ ] Tapping History tab swaps list without remounting right column; right column shows history stats (total plays, top track, top artist).
- [ ] Save-as-playlist tile triggers existing save dialog.

### UAT (1920×720 kiosk)

- [ ] Open `/` — no dock; switch to `/devices` — dock appears at bottom with current track; click title — returns to `/`.
- [ ] Cycle through sources via topbar — every tap switches source, never toggles panel.
- [ ] Active Radio source — chevron visible on pill; tap chevron → Radio panel opens.
- [ ] Active File source — no chevron (no detail surface).
- [ ] Open Queue page with files queued — list on left, right column shows Queue Total LED, Up Next thumbs, Save CTA. No empty void.
- [ ] Tap History tab — right column transitions to history stats; list reloads.
- [ ] Kebab in list header shows ADD FILES and CLEAR ALL options.

### Dependencies

**PR 2 (SourceBubble + topbar) + PR 3 (Durations.FormatLong) must be merged.**

### LOC estimate

~700–900 (dock + queue split + source-pill semantics + tests).

---

## PR 6 — `feat(web): sleep screen and dev tray gesture`

**Handoff items landed:** P2·1 + P2·2

**Why grouped:** Both are P2 system-polish items, both introduce new routes/components, and P2·2 is the natural home for the dev artefacts removed in PRs 2 and 4 (distortion marker, visualizer "Updates: NN/sec"). Landing them together ties off the dev-UI loose ends in one place.

**Branch:** `feat/web-design-sleep-and-dev-tray` off `main`. Soft dependency on PR 2 (distortion button removal) and PR 4 (visualizer telemetry removal). If those have merged, this PR has clean places to wire them in; if not, surface a conflict warning.

### Files

**New:**
- `src/Radio.Web/Components/Pages/Sleep.razor` (route `/sleep`, `@layout EmptyLayout`)
- `src/Radio.Web/Components/Shared/DevTray.razor`
- `src/Radio.Web/wwwroot/js/dev-gesture.js`
- `tests/Radio.Web.Tests/Components/Pages/SleepTests.cs`
- `tests/Radio.Web.Tests/Components/Shared/DevTrayTests.cs`

**Modified:**
- `src/Radio.Web/Components/Layout/MainLayout.razor`:
  - `HandleSleepButtonAsync` → `SystemApi.SetSleepAsync(true)` + `NavigationManager.NavigateTo("/sleep")`.
  - Add 48×48 invisible hit area top-right of topbar for dev gesture (`DotNetObjectReference`).
  - Mount `<DevTray>` conditionally on `_isDevTrayOpen`.
  - **Final removal** of distortion-marker button (was DEBUG-gated in PR 2).
- `src/Radio.Web/wwwroot/js/idle-dimmer.js` — navigate to `/sleep` on idle instead of overlaying.
- `src/Radio.Web/Components/Shared/VisualizerPanel.razor` — expose visualizer "Updates: NN/sec" as a service-level reading consumed by `DevTray` (the burned-in label was removed in PR 4; this PR wires the data through).

### Implementation notes (handoff §P2·1, §P2·2, condensed)

**P2·1 (Sleep as a screen):**
1. `/sleep` page with `@layout EmptyLayout`:
   - Background `#050507`, subtle radial amber glow at center.
   - 160×160 faint album art block (40% opacity).
   - Clock: `--font-led`, **96px**, amber with strong glow shadow.
   - Track title + artist at 18px in very dim color.
   - Hint at bottom: `tap anywhere to wake`, mono 11px, very dim, uppercase, letter-spaced.
2. `HandleSleepButtonAsync` calls `SystemApi.SetSleepAsync(true)` then navigates.
3. `Sleep.razor` full-screen `@onclick` → `SystemApi.SetSleepAsync(false)` + `NavigationManager.NavigateTo("/")`.
4. `idle-dimmer.js` navigates instead of overlaying.
5. Server-pushed `SleepStateChanged` event: if server reports wake while on `/sleep`, navigate home.

**P2·2 (Dev tray):**
1. 48×48 invisible hit area top-right of topbar. `dev-gesture.js` counts taps client-side; 3 taps within 1.5s call into Blazor via `DotNetObjectReference` → toggle `_isDevTrayOpen`.
2. `DevTray.razor` positioned-fixed top-right, slides down with drop shadow:
   - Header: `● Dev Tray · unlocked · auto-lock 0:30`.
   - 2×3 grid of action cards:
     - **Mark distortion** — `AudioApi.ReportDistortionAsync()`.
     - **Updates: NN/sec** — live visualizer telemetry.
     - **Dump audio frame** — last 5s buffer dump.
     - **Download logs** — last hour, zip.
     - **Fingerprint events** — opens existing fingerprint detail panel.
     - **Engine state** — readable `AudioStateHubService` state string.
   - Auto-locks after 30s no interaction (timer in `DevTray.razor`).
3. Final removal of distortion-marker button from `MainLayout.razor`.

### Tests

- `SleepTests` — renders clock + title + hint; click anywhere fires wake handler.
- `DevTrayTests` — opens via JS interop bridge (mock `DotNetObjectReference`); auto-locks after timer; "Mark distortion" wires to API.
- E2E: idle-timer triggers `/sleep` navigation (manual UAT only — hard to automate at 1920×720 kiosk).

### Acceptance (from handoff)

P2·1:
- [ ] Sleep button takes user to `/sleep` with fade-in.
- [ ] Tap anywhere on `/sleep` wakes and returns to last route.
- [ ] No black overlay hack remains in JS.

P2·2:
- [ ] No dev-only UI is visible in production chrome without the gesture.
- [ ] Gesture works at native 1920×720 touch resolution.
- [ ] Tray auto-locks after 30s.

### UAT (1920×720 kiosk)

- [ ] Tap sleep button — `/sleep` page fades in with 96px amber clock + faint art.
- [ ] Wait 5s, tap screen — returns to last route with current playback intact.
- [ ] Idle for configured idle period — auto-navigates to `/sleep` (no overlay).
- [ ] 3-tap top-right invisible hit area within 1.5s — DevTray slides down.
- [ ] All 6 dev cards present; "Mark distortion" actually marks; visualizer telemetry updates live.
- [ ] Wait 30s without interacting — tray auto-locks.
- [ ] No distortion-marker button anywhere in topbar.

### Dependencies

**Soft on PR 2 (distortion-marker DEBUG gating) and PR 4 (visualizer telemetry decoupling).** Land last in the arc.

### LOC estimate

~500–700 (Sleep page + DevTray + JS gesture + MainLayout wiring + tests).

---

## Open questions for the user (surface before Builder starts)

These are spec ambiguities or coordination items that should be answered before Builder picks up the corresponding PR. None block PR 1 — they affect PR 3 and onward.

1. **`Devices:Aliases` location.** The handoff puts the alias map in `appsettings.json`, but the project's existing pattern (per project memory) is to use `appsettings.Production.json` for per-machine overrides because deploy overwrites `appsettings.json`. **Question:** put the default aliases (the VB-Audio + Realtek entries) in `appsettings.json` baseline OR in `appsettings.Production.json` only? Recommend: baseline goes in `appsettings.json`, per-host extensions go in `appsettings.Production.json`.

2. **`Radio.Metrics` package versioning (PR 4).** Adding a `Unit` field to `MetricDescriptor` is a breaking API change for the standalone NuGet package. **Question:** bump the package minor version + republish to local feed via `pack-local.ps1` as part of the PR, OR keep the field internal to the consuming side until the package is re-released? Recommend: bump minor version, run `pack-local.ps1`, commit updated `nuget.config` references.

3. **Device-name head-length threshold (PR 1 / `DisplayNames.Device`).** The handoff says "strip trailing parenthesized hardware-driver suffixes when the head is sufficiently descriptive." That's a judgement call. **Question:** define "sufficient" as (a) head length ≥ 4 chars AND contains a space, (b) explicit allow list of known driver suffixes, or (c) both? Recommend: (c) — start with explicit allow list (`AMD High Definition Audio Device`, `Realtek USB Audio`, `VB-Audio Virtual Cable`, …) and fall back to heuristic only when no match.

4. **Skeleton replacement scope (PR 4 / P1·5).** The handoff says "No centered spinner in any panel-body loading branch in `Components/`." There may be deliberate spinners in modal/determinate contexts that are NOT panel-body loading. **Question:** Builder will distinguish "panel-body initial-load" (replace with Skeleton) from "modal/determinate progress" (keep `RadzenProgressBarCircular`). Confirm this is the user's read of the intent — there's no risk of removing legitimate spinners.

5. **Visualizer telemetry data path (PR 6).** The "Updates: NN/sec" reading needs to move from a burned-in canvas label to a service-level value consumed by `DevTray`. **Question:** add a new `VisualizerTelemetryService` singleton, OR push it through an existing service like `AudioStateHubService`? Recommend: a small new singleton — keeps the visualizer self-contained.

6. **`HasDetail` matrix (PR 5 / P1·2).** Handoff lists `Radio`, `RTLSDRCore`, `RF320`, `Bluetooth` as having details, and `File`, `USB` as not. **Question:** what about future sources (Vinyl, Phono, TTS, Spotify)? Builder will mark only the four enumerated source types as `HasDetail=true` and leave others false; new sources must be explicitly added. Confirm.

7. **PR 6 timing vs. PR 2/4 conflicts.** PR 6 finalizes removal of the distortion-marker button (DEBUG-gated in PR 2) and consumes the visualizer telemetry value (decoupled in PR 4). If for any reason PR 6 ships before PR 2 or PR 4 merge, the conflicts will need a manual rebase. Builder should land 1 → 2 → 3 → 4 → 5 → 6 strictly in order; if any PR slips, sequence the remainder accordingly.

---

## Risks

| Risk | Mitigation |
|---|---|
| Topbar height bump from 80→120 breaks an unrelated page that hard-codes 640 content area. | PR 2 includes a `grep` for `640px` and `80px` across the project (handoff explicitly calls this out). Add a 600px content-area assertion test to `MainLayoutTests` if feasible. |
| Display-name regex matches a legitimate non-GUID hex sequence. | Test set in PR 1 includes both positive (GUID present) and negative (hex but not GUID-shaped) cases. |
| Skeleton swap inadvertently removes a determinate progress bar (e.g. file scan). | PR 4 keeps `RadzenProgressBarCircular` for modal/determinate cases. UAT explicitly exercises file scan to confirm progress still shows. |
| `Radio.Metrics` package version bump conflicts with another in-flight PR consuming the old shape. | Check `git log` for other branches touching `Radio.Metrics`; coordinate sequencing. |
| Dev gesture (3-tap top-right) overlaps with an existing gesture or button hit area. | The 48×48 hit area sits in unused topbar corner; PR 6 UAT checks no overlap. If overlap found, narrow the area or pick a different gesture (e.g. four-finger touch). |
| Queue split layout changes break drag-reorder behaviour. | PR 5 must preserve `RadzenDataList` drag-reorder events; UAT exercises reorder + verify persistence. |

---

## What this arc explicitly does NOT change

(Copied from handoff "What NOT to change" — keep these locked.)

- DSEG amber LED treatment for time/frequency on `.display-frequency`, `.topbar-clock`, sleep screen, queue total. Do not extend to regular UI text.
- Per-source color mapping (`--source-vinyl/-file/-radio/-bluetooth/-usb`). Already correct.
- Three-column home page split (520 / fill / 710). Proportions work.
- SignalR push patterns. They are the right architecture for this app.

Also:

- **`feature/rotaryphone-sip-integration` WIP does not ride along.** Every PR branches from `main`, not from that branch.
- **No new design tokens** unless a value is genuinely missing — extend `design-system.css §2` only when forced.
- **DTOs server-side stay as-is** — only display projection changes (P0·2).

---

## Build / verify checklist (per PR)

- [ ] `dotnet build --configuration Release` — 0 warnings.
- [ ] `dotnet test --configuration Release --verbosity normal` — green.
- [ ] Eyeball affected pages in dev build at native 1920×720 — content fits, no scrollbars except intended, no debug UI visible.
- [ ] Take fresh screenshot into `screenshots/{page}.png` (overwrite current) so next design pass starts from current baseline.
- [ ] Open PR against `main` with body that lists handoff items landed and pastes acceptance checkbox list.
- [ ] Run automated code review agent + address any high-priority feedback before merge.

---

## Out of scope

- Anything marked `[PARKED]` in `IMPLEMENTATION.md` (none in the current package — all 12 items are in scope per Coordinator's instruction).
- Server-side audio engine changes.
- New design tokens beyond `--text-medium` bump.
- Re-platforming SignalR or hub patterns.
- Folding in the `feature/rotaryphone-sip-integration` WIP.
