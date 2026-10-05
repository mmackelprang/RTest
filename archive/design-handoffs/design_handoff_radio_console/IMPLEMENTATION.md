# Implementation Script — Radio Console Design Tightening

> **For the implementing agent (Claude Code or human):**
> This script maps one canvas artboard → one set of file changes. Land changes in the order given. Do not start any section whose status is not `[APPROVED]`.
>
> Every section uses the same shape:
> 1. **Reference** — which artboard / which finding
> 2. **Files** — exact paths to touch
> 3. **Steps** — numbered, concrete
> 4. **Acceptance** — checkable criteria
> 5. **Notes** — gotchas, dependencies, what NOT to change
>
> Tokens to use are in `src/Radio.Web/wwwroot/css/design-system.css`. Do not invent new colors, type, or spacing — extend the token block first if you genuinely need a new value.

---

## P0 — Fix before anything else

These are bugs, data hygiene problems, and production-hygiene issues that the design system can't paper over.

---

### P0·0 — Formatting layer (foundation)

**Status:** `[PENDING REVIEW]`
**Reference:** Underpins P0·2, P0·3, P1·3 and the Metrics work in P0·4.
**Files:**
- `src/Radio.Web/Formatting/Durations.cs` (new)
- `src/Radio.Web/Formatting/Timestamps.cs` (new)
- `src/Radio.Web/Formatting/DisplayNames.cs` (new)
- `src/Radio.Web/Formatting/Units.cs` (new)
- `src/Radio.Web/_Imports.razor` (add `@using Radio.Web.Formatting`)

**Steps:**

1. Create the `Radio.Web.Formatting` namespace as a flat folder of `static class` helpers. No DI, no state.

2. `Durations.FormatTrack(TimeSpan t)`:
   - `t < 1 hour` → `"{m}:{ss:00}"` (e.g. `3:00`)
   - `t >= 1 hour` → `"{h}:{mm:00}:{ss:00}"` (e.g. `1:02:14`)
   - `t.TotalSeconds < 1 || t == TimeSpan.Zero` → `"—"`
   - `null` → `"—"`

3. `Durations.FormatLong(TimeSpan t)` for queue totals: always `h:mm:ss` form (e.g. `2:48:15`).

4. `Timestamps.FormatRelative(DateTime utc)`:
   - Same calendar day → `"Today {HH:mm}"`
   - Yesterday → `"Yesterday {HH:mm}"`
   - Older than 48h → `"{MMM d} · {HH:mm}"`
   - Caller is responsible for converting from UTC to local before display.

5. `DisplayNames.Source(AudioSourceDto s)`:
   - Strip any `-{guid}` suffix from `Id` if `Name` is missing.
   - Return `Name` if set, else humanized `Type` (`"FilePlayer"` → `"File Player"`).
   - **Never** return the raw `Id`.

6. `DisplayNames.Device(AudioDeviceDto d, IDictionary<string,string>? aliasMap = null)`:
   - Apply `aliasMap` first (e.g. `"CABLE Input (VB-Audio Virtual Cable)" → "VB-Audio Cable"`). The map should live in `appsettings.json` under `Devices:Aliases`.
   - Strip trailing parenthesized hardware-driver suffixes when the head is sufficiently descriptive (`"LG TV SSCR2 (AMD High Definition Audio Device)"` → `"LG TV"`).
   - Strip leading `"N - "` enumeration prefixes.
   - Cap at 40 chars with ellipsis.

7. `DisplayNames.Track(NowPlayingDto np)`:
   - Prefer `np.Title` if non-empty and not literally `"Track {n}"` filename-derived.
   - When `Title` looks generic, attempt to parse the source `FilePath`: strip extension, strip leading `^\d+[\s\-_]+` (track number), title-case if all-lowercase.
   - Subtitle is `np.Artist ?? "—"`.

8. `Units` enum and `Units.Format(double value, Units unit)`:
   ```csharp
   public enum Units {
     Percent,       // 35.7%
     Megabytes,     // 850 MB
     Milliseconds,  // 215 ms (>=1000 → "1.2 s")
     Count,         // 135,725
     PerMinute,     // 12.4/min
     Frequency,     // 88.5 MHz (uses existing FrequencyFormatter)
     Decibels,      // -3 dB
     Bare           // 65
   }
   ```
   - Always thousands-separated for `Count`.
   - One decimal for `Percent` if value < 10, else integer.

**Acceptance:**

- [ ] All four files compile, exist under `Radio.Web.Formatting`, are referenced from at least one Razor file by end of P0·1.
- [ ] `Durations.FormatTrack(TimeSpan.FromSeconds(180.6628))` → `"3:00"`.
- [ ] `DisplayNames.Source` never returns a string containing a GUID character span (regex: `[0-9a-f]{8}-[0-9a-f]{4}`).
- [ ] No `.ToString()` on a `TimeSpan` remains anywhere in `Components/` after P0·3 lands.

**Notes:**

This file is small but it is the single biggest leverage in the package — three later P0s depend on it. Land this first, even if it goes out behind a feature flag.

---

### P0·1 — Top bar redesign

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P0 — fix before anything else` → **"Top bar redesign"**
**Files:**
- `src/Radio.Web/Components/Layout/MainLayout.razor`
- `src/Radio.Web/wwwroot/css/design-system.css` (`§5 Top Bar`, `§6 Route Toggle`, `§7 Navigation Icons`)

**Steps:**

1. **Bump topbar height** from `80px` → `120px` in `design-system.css §2`:
   - `--topbar-height: 120px;`
   - `--content-height: 600px;`
   - Verify nothing else hard-codes 720/640 split (search the project for `640px` and `80px`).

2. **Restructure the topbar** in `MainLayout.razor` from one flex row into a two-row stack inside `.topbar`:
   - Row 1 (`.topbar-primary`, 64px) — Time cluster | In cluster | `→` glyph | Out cluster | nav pill strip.
   - Row 2 (`.topbar-sources`, 56px) — Source bubble strip, left-aligned.

3. **Add `.cluster` styles** in `design-system.css §5`:
   - `.cluster-label` — uses `var(--font-mono)`, `10px`, `0.16em` letter-spacing, `var(--text-low)`, uppercase.
   - `.cluster-value` — `15px`, `var(--text-high)`, `font-weight: 500`. May contain an inline `--font-led` numeric span for frequency / clock time.
   - `.cluster-swatch` — `8×8` square, `2px` radius, used to show the source / output color dot inline with the value.

4. **Replace the nine circular `.nav-icon` buttons** with rectangular `.nav-pill`:
   - `height: 56px; padding: 0 16px; border-radius: 10px;`
   - Contains label text (Inter, `12px`, `0.10em` letter-spacing, uppercase, 500 weight) — no more icon-only navigation in the top row.
   - Active state: `background: var(--accent-dim); color: var(--accent);`
   - Badge variant for Queue count: `<span class="nav-badge">45</span>` styled as amber pill, 10px font.

5. **Source bubbles** — extract into `Components/Shared/SourceBubble.razor`:
   - Props: `Icon`, `Label`, `Sub`, `Accent` (CSS variable name), `IsActive`, `IsDisabled`, `HasDetail`, `OnSwitch`, `OnOpenDetail`.
   - Render: pill shape (`height: 48px; border-radius: 24px`), 28×28 round icon chip on the left, label + optional sub.
   - When `IsActive`, use `background: {accent}14; border: 1px solid {accent}55; color: {accent};`.
   - When `IsActive && HasDetail`, render a trailing `›` glyph as a separate focusable element bound to `OnOpenDetail`.
   - When `IsDisabled`, `opacity: 0.4; pointer-events: none;` and append " · offline" to the sub.

6. **Remove the inline `RadzenButton` debug distortion marker** from `MainLayout.razor` (lines ~48–55). Move it to the dev tray in P2·2. Until P2·2 ships, gate it behind `#if DEBUG` so it never appears in release builds.

7. **Sticky/fixed positioning:** the existing `position: fixed` on `.topbar` is fine; just verify the content offset (`margin-top` on `.content-area`) tracks `--topbar-height` not a hard 80.

**Acceptance:**

- [ ] Top bar matches the "After" artboard within ±2px on a 1920-wide preview.
- [ ] No circular pill remains anywhere in the top bar.
- [ ] Active source AND active output both show their human name, not just a colored icon.
- [ ] Debug button is gone from release builds.
- [ ] Content pages still vertically fit (600px content area) without scrollbars on Home/Devices/Queue.

**Notes:**

The current `route-toggle` CSS class is referenced from `MainLayout.razor` only. Safe to delete after the migration. The `data-source` attribute mechanism (per-source color) moves onto `SourceBubble` — keep the attribute name so any existing tests don't break.

---

### P0·2 — Surface clean display names

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P0` → **"Source & device names"**
**Files:**
- `src/Radio.Web/Components/Layout/MainLayout.razor`
- `src/Radio.Web/Components/Shared/NowPlayingPanel.razor`
- `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor`
- `src/Radio.Web/Components/Pages/DeviceManagementPage.razor`
- `src/Radio.Web/Components/Dialogs/FileBrowserDialog.razor`
- `src/Radio.Web/appsettings.json` (`Devices:Aliases` map)

**Steps:**

1. Add `Devices:Aliases` to `appsettings.json`:
   ```json
   "Devices": {
     "Aliases": {
       "CABLE Input (VB-Audio Virtual Cable)": "VB-Audio Cable In",
       "CABLE In 16ch (VB-Audio Virtual Cable)":  "VB-Audio Cable 16ch",
       "Realtek Digital Output (Realtek USB Audio)": "Realtek USB · S/PDIF"
     }
   }
   ```
   Bind into a typed `DevicesOptions` and inject where needed.

2. In every place that currently binds a raw DTO field to a string template, replace with the appropriate `DisplayNames.*` call from P0·0.
   - In MainLayout: source bubble `Label` = `DisplayNames.Source(source)`.
   - In NowPlayingPanel: title/subtitle = `DisplayNames.Track(np)`.
   - In Device tables: name = `DisplayNames.Device(d, aliasMap)`.

3. Add a `title` attribute (HTML tooltip) on every display-name element so the **raw** name is reachable on hover — useful for debugging and accessibility.

**Acceptance:**

- [ ] No GUID character span (`[0-9a-f]{8}-[0-9a-f]{4}`) appears in any user-visible text on Home, Devices, Queue, History, File Browser.
- [ ] `Track 8` / `Track 9` from `Cary High Chorus / Fall Concert 2006` displays as the parsed file name when available.
- [ ] Long device strings are capped at 40 chars + ellipsis; full name on hover.

**Notes:**

Don't change the DTOs themselves — keep the raw fields available server-side. Only the *display projection* changes.

---

### P0·3 — Duration & timestamp formatting

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P0` → **"Queue rows & duration format"**
**Files:**
- `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor`
- `src/Radio.Web/Components/Shared/NowPlayingPanel.razor`
- `src/Radio.Web/Components/Pages/PlayHistoryPage.razor`

**Steps:**

1. Replace every `@track.Duration.ToString()` with `@Durations.FormatTrack(track.Duration)`.
2. Replace every progress-display `@_elapsed.ToString(@"mm\:ss")` with `@Durations.FormatTrack(_elapsed)` to handle the `>1h` long-form case.
3. PlayHistory: replace the Date/Time column format with `@Timestamps.FormatRelative(row.PlayedAtUtc.ToLocalTime())`.
4. Add `font-variant-numeric: tabular-nums;` to any element that displays a duration so columns line up.
5. Right-align duration columns in tables.
6. Add the now-playing accent treatment to the queue list:
   - Currently-playing row gets `border-left: 3px solid var(--signal-amber);` and the number cell shows `▶` instead of its index.

**Acceptance:**

- [ ] No `00:03:00.6628571`-style duration appears anywhere.
- [ ] Queue rows have a clear "this is what's playing" indicator (amber left border + ▶ glyph).
- [ ] History "Date/Time" column reads as `Today 08:40`, `Yesterday 14:22`, or `Feb 6 · 11:05` depending on age.

---

### P0·4 — Metrics dashboard — units, grouping, sparklines

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P0` → **"Metrics tiles"**
**Files:**
- `src/Radio.Web/Components/Pages/MetricsDashboardPage.razor`
- `src/Radio.Web/Components/Shared/MetricTile.razor` (new)
- `src/Radio.Web/Components/Shared/Sparkline.razor` (new)
- API/model: wherever `MetricDescriptor` is declared (likely `Radio.Metrics`) — add a `Unit` field.

**Steps:**

1. **Add a `Unit` enum** to each metric descriptor on the server side. Defaults to `Bare` when unknown. Match the enum in `Radio.Web.Formatting.Units`.

2. **`MetricTile.razor`** — parameters:
   ```
   Category (string, e.g. "System · Memory")
   Name (string, e.g. "Heap in use")
   Value (double)
   Unit (Units)
   Series (IReadOnlyList<double>?, last N samples) — optional
   Thresholds (double? warn, double? critical) — optional
   ```
   Render:
   - Top: category in mono, 10px, uppercase, low contrast.
   - Middle: name in body, 13px, medium contrast.
   - Big value: `--font-mono`, 30px, color computed from thresholds (green / amber / red), or `--text-high` when no threshold.
   - Sparkline: if `Series` provided, render `<Sparkline />` matched to value color.

3. **`Sparkline.razor`** — pure inline SVG, `viewBox="0 0 120 28" preserveAspectRatio="none"`, takes a `IReadOnlyList<double>` and a `Stroke` parameter. Filled area at 10% opacity below the line.

4. **Restructure `MetricsDashboardPage.razor`**:
   - Group tiles by category. Render each group with a sub-header (mono, 11px, uppercase, low contrast, 14px bottom margin).
   - 2- or 4-column grid depending on density target.
   - Remove the wall-of-tiles layout.

5. **Specific fixes from the current screenshots:**
   - `Memory Usage Mb` value is in MB not %. Unit = `Megabytes`. Tile shows `850 MB`, not `850.4%`.
   - `Signal Strength` value is bare. Unit = `Percent`. Tile shows `65%`.
   - `Frequency Changes` is a count — Unit = `Count`, format with thousands separator.
   - `Latency Ms` — Unit = `Milliseconds`, formatter auto-converts to `s` above 1000.

**Acceptance:**

- [ ] Memory usage tile reads `850 MB` (or current value with unit `MB`) — never `%`.
- [ ] Each tile shows a sparkline when time-series data is available for that metric in the selected window.
- [ ] Category headers replace the per-tile category prefix.
- [ ] Tile value color matches threshold semantics (green/amber/red) — not every tile is cyan.

---

### P0·5 — File Browser filter — chips, no string

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P0` → **"File browser filter"**
**Files:**
- `src/Radio.Web/Components/Dialogs/FileBrowserDialog.razor`

**Steps:**

1. Locate the current filter state. It is almost certainly a `string` bound to a `RadzenDropDown` whose value is being JSON-serialized for persistence and re-bound on load without deserializing. Find the persistence boundary (look for `JsonSerializer.Serialize` calls referencing a filter or for `LocalStorage`-backed filter persistence).

2. Replace with:
   - `List<string> _filterExtensions = new();`
   - On load, deserialize from persistence into the list directly. If the persisted blob is the double-escaped form, run a one-time repair pass (parse repeatedly until you get a `string[]`, then take it).

3. Render as a row of `RadzenChip` elements, each with a remove `×`. Append a dashed `＋ add type` chip that opens a small popover with a curated set of common extensions (`.mp3 .flac .wav .m4a .aac .ogg .opus .wma`).

4. Wire the filter into the file list query: case-insensitive `EndsWith` match across the list.

**Acceptance:**

- [ ] No escaped JSON ever appears in the filter UI.
- [ ] Chips reorder and remove cleanly; persistence round-trips without re-escaping.
- [ ] Adding `.mp3` filters the file list immediately, no Apply button.

**Notes:**

Also fix the breadcrumb / drive selector mismatch noted in the audit — the screenshot shows drive `C:\` selected while the breadcrumb reads `D:\`. They should bind to the same source of truth (`_currentPath`).

---

## P1 — Big quality wins

These don't fix bugs, they fix the *shape* of the product.

---

### P1·1 — Persistent Now Playing dock

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P1 — big quality wins` → **"Persistent Now Playing dock"**
**Files:**
- `src/Radio.Web/Components/Shared/NowPlayingDock.razor` (new)
- `src/Radio.Web/Components/Layout/MainLayout.razor`
- `src/Radio.Web/wwwroot/css/design-system.css` (new `§N Dock`)

**Steps:**

1. Build `NowPlayingDock.razor`:
   - Height: 64px, sits at the bottom edge of the content area.
   - Layout (left → right): art thumbnail 48×48 → title + artist → 3-bar EQ animation (existing `.now-playing-bars` keyframes) → elapsed `mm:ss` → progress bar (3px, `--accent`) → total `mm:ss` → ⏮ play/pause ⏭.
   - Subscribes to `AudioStateHubService.NowPlayingChanged` and `.PlaybackStateChanged` only — no additional polling.
   - On click anywhere on the title/art block, `NavigationManager.NavigateTo("/")`.
   - Backdrop: `background: var(--surface-overlay); backdrop-filter: blur(20px); border-top: 1px solid var(--surface-separator);`.

2. Mount in `MainLayout.razor`:
   - Place inside `.content-area` as a sibling of `.page-transition`, absolute-positioned to the bottom.
   - Render only when `NavigationManager.ToBaseRelativePath(Uri) is not "" or "/"` — i.e. not on Home (where `NowPlayingPanel` owns the column).
   - When dock is rendered, reduce `.page-transition`'s effective height by 64px so content doesn't slide under it.

3. Add the source color dot next to the artist line (8×8 square, 2px radius) using the existing `--source-*` variables.

**Acceptance:**

- [ ] Navigating from `/` → `/devices` reveals the dock; navigating back to `/` removes it cleanly.
- [ ] The dock shows the same track / state as the home page within one SignalR round-trip.
- [ ] Tapping the title takes the user home.
- [ ] No layout shift when track changes (use `min-width: 200px` on the metadata block).

---

### P1·2 — Source pill semantics

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P1` → **"Source pill semantics"**
**Files:**
- `src/Radio.Web/Components/Layout/MainLayout.razor` (method `HandleSourceToggleAsync`)
- `src/Radio.Web/Components/Shared/SourceBubble.razor` (from P0·1)

**Steps:**

1. Delete the second-tap-toggles-panel and second-tap-navigates branches in `HandleSourceToggleAsync`. Tapping a source's body **always** switches to that source. Period.

2. In `SourceBubble.razor`, render the active pill with a trailing `›` chevron when `HasDetail == true`. The chevron is a separate clickable element with its own `OnOpenDetail` handler. Stop the click event from bubbling to the body.

3. `HasDetail` is true for source types that have a dedicated detail surface: `Radio`, `RTLSDRCore`, `RF320`, `Bluetooth`. Not for `File`, `USB`.

4. When chevron is tapped:
   - Radio family → `NavigateTo("/")` + show Radio control panel via `RadioPanelToggleService.ShowRadioPanelAsync()`.
   - Bluetooth → `NavigateTo("/bluetooth")`.

5. The current `RadioPanelToggleService` should no longer be flipped by source-tap. Only by explicit chevron tap or by an in-panel close. Audit calls to `ToggleRadioPanelAsync()` and remove the tap-driven ones.

**Acceptance:**

- [ ] Tapping any source pill switches to that source exactly once, never opens a panel.
- [ ] The active source pill shows a chevron only when a detail surface exists.
- [ ] Tapping the chevron opens that detail; the rest of the pill does not.

---

### P1·3 — Visualizer panel — promoted mode picker

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P1` → **"Visualizer panel"**
**Files:**
- `src/Radio.Web/Components/Shared/VisualizerPanel.razor`

**Steps:**

1. Replace the corner mode chip group (VU / WAVE / SPECTRUM) with a full-width 3-segment header inside the panel:
   - Grid layout, 3 equal columns, 44px tall.
   - Mono font, 12px, uppercase, `0.10em` letter-spacing.
   - Active segment: `background: var(--accent-dim); color: var(--accent);` with a 2px underline accent.
   - Inactive segments: `color: var(--text-medium);` separated by 1px `var(--surface-separator)` borders.

2. The canvas/SVG visualization fills the remaining vertical space. Remove the current padding around it.

3. Move the "Connected" pill to a single 8px dot at the top-right of the panel header, color `var(--signal-green)` when connected, `var(--signal-red)` when not. No text label — the dot is enough.

4. Remove the burned-in `"Updates: 22/sec"` yellow caption from the canvas. Add it to the dev tray in P2·2.

5. Frequency-band axis labels (`375Hz / 750Hz / 1.1kHz / 1.5kHz`) should sit *under* the canvas in a thin (16px) strip, mono font, 10px, low contrast — not inside the canvas overlapping the bars.

**Acceptance:**

- [ ] Mode picker is the obvious primary control of the panel.
- [ ] Canvas fills the available height/width with no debug telemetry overlaid.
- [ ] Connection state is a small dot, not a pill.

---

### P1·4 — Queue split layout

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P1` → **"Queue page — split layout"**
**Files:**
- `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor`

**Steps:**

1. Split the panel into two columns: `flex: 1.6` (list) | `flex: 1` (context). 12px gap.

2. Add a tab strip at the top of the list column:
   - Three pills: `Queue · N` / `History` / `Radio` (the third only when the active source is a radio type).
   - Use the existing `--accent-dim` for active background, mono font, 12px uppercase 0.10em letter-spacing.

3. Right context panel — three stacked tiles:
   - **Queue Total** — `--font-led`, 30px, amber, shows `Durations.FormatLong(totalRuntime)` plus a sub-line: `{count} tracks · ends ~{nowPlus(total):HH:mm}`.
   - **Up next** — three 32×32 album-art thumbnails with title + artist, each line dimmed progressively (text-high → text-medium → text-low).
   - **Save as playlist** — accent-bordered CTA tile with `＋ Save as playlist` text.

4. The current ADD FILES / CLEAR ALL header buttons move into a kebab menu in the list-column header. They should not occupy a whole top row.

**Acceptance:**

- [ ] The content area is filled when the queue has 1+ tracks (no large empty void below).
- [ ] Tapping the History tab swaps the list contents without remounting the right column (the right column updates its content to "History stats" — total plays, top track, top artist).
- [ ] Save-as-playlist tile triggers the existing save dialog.

---

### P1·5 — Skeleton loading states

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P1` → **"Skeleton loading states"**
**Files:**
- `src/Radio.Web/Components/Shared/Skeleton.razor` (new)
- All loading branches across `NowPlayingPanel`, `RadioControlPanel`, `QueueHistoryPanel`, `VisualizerPanel`, `DeviceManagementPage`, `PlayHistoryPage`.

**Steps:**

1. Build `Skeleton.razor` with one parameter:
   ```
   public enum Shape { NowPlaying, Radio, ListRow, DeviceRow, MetricTile, Visualizer }
   [Parameter] public Shape Shape { get; set; }
   ```
   Each shape renders a shape-matched arrangement of the existing `.skeleton-*` divs from `design-system.css §17`. Sizes match the real component layout.

2. Replace every `RadzenProgressBarCircular` used as a generic loading indicator with `<Skeleton Shape="..." />`.

3. Keep `RadzenProgressBarCircular` only for *modal / determinate* progress (e.g. file scan progress bar).

**Acceptance:**

- [ ] No centered spinner in a panel-body loading branch anywhere in `Components/`.
- [ ] First-load on Radio shows a freq-well shape + band buttons + meter outlines, then animates to real content.

---

## P2 — System polish

Things that change how the device feels in the room.

---

### P2·1 — Sleep as a screen

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `System polish` → **"Sleep — ambient screen"**
**Files:**
- `src/Radio.Web/Components/Pages/Sleep.razor` (new — route `/sleep`)
- `src/Radio.Web/wwwroot/js/idle-dimmer.js` (existing — modify to navigate, not overlay)
- `src/Radio.Web/Components/Layout/MainLayout.razor` (`HandleSleepButtonAsync`)

**Steps:**

1. Build `/sleep` page with `@layout EmptyLayout` (no top bar):
   - Background `#050507` with a radial amber glow at center, very subtle.
   - 160×160 faint album art block at center, 40% opacity.
   - Large clock below: `--font-led`, **96px**, amber, with a strong glow shadow.
   - Track title + artist line at 18px in a very dim color.
   - Hint text at the bottom: `tap anywhere to wake`, mono 11px, very dim, uppercase, letter-spaced.

2. `HandleSleepButtonAsync` in `MainLayout`: call `SystemApi.SetSleepAsync(true)` then `NavigationManager.NavigateTo("/sleep")`.

3. `Sleep.razor` has a full-screen `@onclick` handler that calls `SystemApi.SetSleepAsync(false)` then `NavigationManager.NavigateTo("/")`.

4. `idle-dimmer.js` no longer overlays the page; it navigates to `/sleep` when idle.

5. SignalR `SleepStateChanged` event still drives the page — if the server reports wake-from-elsewhere while we're on `/sleep`, navigate home.

**Acceptance:**

- [ ] Tapping the sleep button takes the user to `/sleep` with a fade-in.
- [ ] Tapping anywhere on `/sleep` wakes and returns to last route.
- [ ] No black overlay hack remains in JS.

---

### P2·2 — Dev tools gesture & tray

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `System polish` → **"Dev tools"**
**Files:**
- `src/Radio.Web/Components/Shared/DevTray.razor` (new)
- `src/Radio.Web/Components/Layout/MainLayout.razor`
- `src/Radio.Web/wwwroot/js/dev-gesture.js` (new)

**Steps:**

1. Add a 48×48 invisible hit area in the top-right corner of the topbar. Counts taps client-side via `dev-gesture.js`. Three taps within 1.5s call into Blazor (`DotNetObjectReference`) → toggle `_isDevTrayOpen`.

2. `DevTray.razor` is positioned-fixed at top-right, slides down with a small drop shadow:
   - Header: `● Dev Tray · unlocked · auto-lock 0:30`.
   - 2×3 grid of action cards:
     - **Mark distortion** — calls `AudioApi.ReportDistortionAsync()` (the action currently on the topbar button).
     - **Updates: NN/sec** — live visualizer telemetry (moved from `VisualizerPanel`).
     - **Dump audio frame** — last 5s buffer dump.
     - **Download logs** — last hour, zip.
     - **Fingerprint events** — opens the existing fingerprint detail panel.
     - **Engine state** — readable string of the current `AudioStateHubService` state.
   - Auto-locks after 30s of no interaction with the tray (a timer driven by `DevTray.razor`).

3. Move the distortion-marker button **out** of `MainLayout.razor` entirely.

**Acceptance:**

- [ ] No dev-only UI is visible in production chrome without the gesture.
- [ ] The gesture works at native 1920×720 touch resolution.
- [ ] Tray auto-locks after 30s.

---

## Cross-cutting nice-to-haves

These don't have their own canvas artboards but should land alongside the P0/P1 work in the same files.

- **Hidden scrollbars** — `design-system.css §19` currently hides every scrollbar. Add a thin (3px) always-visible thumb just on `.panel-body` and `.queue-list-wrapper` so the "more content below" hint exists. CSS:
  ```css
  .panel-body { scrollbar-width: thin; scrollbar-color: var(--text-low) transparent; }
  .panel-body::-webkit-scrollbar { display: block; width: 3px; }
  .panel-body::-webkit-scrollbar-thumb { background: var(--text-low); border-radius: 2px; }
  ```
- **Bump `--text-medium`** from `#9CA3AF` to `#B5BCC9` in `design-system.css §2` for better legibility at touchscreen viewing distance.
- **`aria-label` on every icon-only button** in `MainLayout` (Sleep, Queue badge, debug if it stays).
- **Visible focus rings**: keep `-webkit-tap-highlight-color: transparent` for touch, but add `:focus-visible` outlines with `outline: 2px solid var(--accent); outline-offset: 2px;`.

## What NOT to change

- The DSEG amber LED treatment for time / frequency. It is the strongest visual element in the app — keep it on `.display-frequency`, `.topbar-clock`, sleep screen, queue total. Do not extend it to regular UI text.
- The per-source color mapping (`--source-vinyl / -file / -radio / -bluetooth / -usb`). Already correct.
- The three-column home page split (520 / fill / 710). The proportions work.
- SignalR push patterns. They are the right architecture for this app.

---

## Build / verify checklist

After landing each section:

- [ ] `dotnet build` clean.
- [ ] `dotnet test` green (`tests/Radio.Web.Tests`).
- [ ] Run E2E suite (`scripts/run-e2e-tests.ps1` or `.sh`).
- [ ] Eyeball the affected page in the dev build at native 1920×720 — content fits, no scrollbars except where intended, no debug UI visible.
- [ ] Take a fresh screenshot into `screenshots/{page}.png` (overwrite the current one) so the next design pass starts from a current baseline.
