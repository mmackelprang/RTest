# HANDOFF — Sleep-mode 3-day weather forecast (alternating with the clock)

**Component:** `src/Radio.Web/Components/Pages/Sleep.razor` (existing surface) + a new lightweight forecast sub-component at `src/Radio.Web/Components/Shared/SleepForecastPane.razor`.
**Surface:** Kiosk sleep screen (route `/sleep`) — the drift cluster that today holds the LED clock + faint album art + track metadata.
**Status:** `[PENDING REVIEW]` — ready for Planner / Builder.
**Relationship to existing handoffs:**
- **Follows** `docs/design-handoffs/design_handoff_radio_console/` (token system: `--font-led`, `--font-mono`, `--signal-amber`, `--text-low/medium/high`, the "calm wall-clock" intent of the sleep screen).
- **Follows** `HANDOFF-configurable-time-format.md` (PR #1) — the Display sub-tab pattern in `SystemConfigPage.razor`, the `IOptionsMonitor` + SQLite-bridge hot-reload mechanism, the invariant-culture formatting convention, and the `Clocks.FormatWallClock` helper for any in-pane "as of HH:mm" timestamps.
- **Follows** `HANDOFF-rds-accumulating-scroll.md` (PR #3) — its Radio sub-tab append pattern (sub-heading + 12-column row + Size/SizeMD layout) is the template for this PR's new Weather group inside the Display sub-tab.
- **Extends** `Sleep.razor`'s existing anti-burn-in drift model (60-second `_driftTimer`, ±20% safe area, CSS-eased `--sleep-shift-x/y` transforms) to a second composition (the forecast pane) that swaps into the same drift wrapper rather than parking in a new fixed position.
- **Extends** the new `Display` sub-tab created by PR #1 with a second group (Weather) under the same tab — no new top-level tab. Display tab ordering becomes **Time** (PR #1) → **RDS Ticker** (only if PR #3 moved it here; see §9) → **Weather** (this PR).
- **Deviates** from nothing. No new color tokens, no new fonts, no new chrome.

---

## 1. Problem + context

The sleep screen is parked overnight on a 1920×720 kiosk in the user's living room. Today the drift cluster shows the LED clock plus optional album art and track metadata, repositioned every 60s to prevent OLED-style burn-in. User feedback (2026-05-23): *"When in sleep mode, I'd like to display the 3-day forecast alternating with the time when the time display is moving across the screen."* The fix is a 3-day NWS forecast (data contract pinned by ADR-022) rendered as a second composition that swaps in for the clock at certain drift-cycle boundaries, then swaps back. Failure-mode contract from ADR §2.3 is load-bearing: a null forecast (weather disabled, ZIP unresolved, NWS unreachable on cold start) must render the existing clock-only sleep screen exactly as today — no broken card, no placeholder, no exception.

---

## 2. Visual mockup (ASCII)

The drift container chrome (`<div class="sleep-screen-drift">`) is unchanged. The forecast pane swaps places with the clock cluster on the cadence in §3 below. All measurements assume the 1920×720 kiosk panel with the same ±20% drift safe area the clock uses (±384px horizontal, ±144px vertical).

### State A — Clock (existing, unchanged)

```
                    ░░░░░░░░░░░░░░░░░░░░░░░░░
                    ░                       ░
                    ░    ╔═══════════╗      ░  ← Orbitron 96px,
                    ░    ║   15:45   ║      ░    dim amber (35%),
                    ░    ╚═══════════╝      ░    --font-led
                    ░                       ░
                    ░       Lateralus       ░  ← optional track row
                    ░         Tool          ░    (when present)
                    ░░░░░░░░░░░░░░░░░░░░░░░░░
                       tap anywhere to wake
```

### State B — Forecast (3 days available, F units, fresh data)

```
            ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
            ░                                          ░
            ░     ┌────────────┬────────────┬────────────┐ ░
            ░     │   TODAY    │    MON     │    TUE     │ ░  ← group header row
            ░     │            │            │            │ ░    (uppercase, --font-mono
            ░     │   [icon]   │   [icon]   │   [icon]   │ ░     11px, --text-low)
            ░     │  PARTLY    │   SHOWERS  │   SUNNY    │ ░
            ░     │            │            │            │ ░  ← icon row (40px Material
            ░     │   72°      │   68°      │   78°      │ ░    Symbol, dim amber 35%)
            ░     │   54°      │   52°      │   58°      │ ░
            ░     └────────────┴────────────┴────────────┘ ░
            ░                                          ░
            ░         Pittsboro, NC · as of 3:00 PM        ░  ← footer (--font-mono
            ░                                          ░       11px, --text-low)
            ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
                          tap anywhere to wake
```

Per-day card geometry (left-to-right inside the row):

```
┌──────────────────────┐
│        TODAY         │   day-name strip,  --font-mono 14px, uppercase,
│                      │   color --text-medium, letter-spacing 0.12em
│       ┌────┐         │
│       │ ☀  │         │   icon, Material Symbol 40px, dim amber 35%
│       └────┘         │   (matches sleep clock brightness — single
│                      │    emissive intensity across the pane)
│       PARTLY         │   conditionShort, --font-mono 11px, --text-low,
│                      │   uppercase, max-width truncate w/ ellipsis
│        72°           │   high, --font-led 36px, dim amber 35%
│        54°           │   low,  --font-led 24px, --text-low
└──────────────────────┘
```

Card width: 200px each, three cards = 600px + 2×20px gaps = 640px total pane width. Pane height ≈ 220px (header 24 + icon row 56 + condition 18 + high 36 + low 28 + padding). Fits comfortably inside the ±384/±144 drift safe area at any selected offset (worst case the rightmost card's right edge lands at center+384+320 = 1024px from viewport-center, still ≈ 230px from the bezel on a 1920px panel).

### State C — Forecast with stale-data indicator (`IsStale=true`)

```
            ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
            ░                                          ░
            ░     ┌────────────┬────────────┬────────────┐ ░
            ░     │   TODAY    │    MON     │    TUE     │ ░
            ░     │   [icon]   │   [icon]   │   [icon]   │ ░  (entire pane rendered
            ░     │  PARTLY    │   SHOWERS  │   SUNNY    │ ░   at color-mix 70%
            ░     │   72°      │   68°      │   78°      │ ░   against background —
            ░     │   54°      │   52°      │   58°      │ ░   subtler than fresh,
            ░     └────────────┴────────────┴────────────┘ ░   still readable)
            ░                                          ░
            ░     [sync_problem]  Pittsboro, NC · as of 1:00 PM yesterday   ░
            ░                                          ░
            ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
                          tap anywhere to wake
```

Stale affordance is a **single small Material Symbol** (`sync_problem`, 14px) tucked to the left of the footer location text, **plus** the existing "as of HH:mm" timestamp (rendered with PR #1's `Clocks.FormatWallClock` honoring user TimeFormat). The pane body itself dims to 70% by setting `opacity: 0.7` on the outer `.sleep-forecast-pane.is-stale` rule — no per-element color change, so we avoid introducing a second token. Day-old timestamps add the relative qualifier (`yesterday`, `2 days ago`) — once `GeneratedAtUtc` is older than 12h, the footer reads `as of 1:00 PM yesterday` rather than just `as of 1:00 PM` to avoid the "is this today's 1pm?" confusion.

### State D — Partial days (only 2 days returned)

```
            ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
            ░                                  ░
            ░     ┌────────────┬────────────┐  ░
            ░     │   TODAY    │    MON     │  ░  ← 2 cards centered in pane;
            ░     │   [icon]   │   [icon]   │  ░    same card geometry, no
            ░     │  PARTLY    │   SHOWERS  │  ░    placeholder / "—" card for
            ░     │   72°      │   68°      │  ░    the missing day
            ░     │   54°      │   52°      │  ░
            ░     └────────────┴────────────┘  ░
            ░                                  ░
            ░      Pittsboro, NC · as of 3:00 PM   ░
            ░                                  ░
            ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
                       tap anywhere to wake
```

Single-day case (`days.Length == 1`) renders one centered card with the same chrome. Zero-day case (`days.Length == 0`) is treated identically to `forecast == null` per §6.A — the pane never renders.

### State E — Forecast with `TemperatureUnit="both"`

```
        ┌──────────────────────┐
        │        TODAY         │
        │       ┌────┐         │
        │       │ ☀  │         │
        │       └────┘         │
        │       PARTLY         │
        │     72°F · 22°C      │   ← --font-led 28px (smaller to fit
        │     54°F · 12°C      │     both numerics on one line), the
        └──────────────────────┘     " · " separator in --text-low
```

Card width grows to 220px for the `both` case (660px + gaps = 700px pane width — still safe). The smaller 28/20px LED type for `both` is the one tradeoff users opt into when they pick that unit; `F` and `C` (single-unit) keep the bigger 36/24px sizes.

---

## 3. Alternation timing

**Pick: Option 1 — every other reposition (clock → forecast → clock → forecast → …).**

The clock's existing `_driftTimer` fires every `DriftIntervalSeconds = 60`. On each tick, in addition to picking a new offset, the sleep page flips an `_isShowingForecast` boolean. Result:

```
t=0s     clock at position A    (initial)
t=60s    forecast at position B  ← drift + composition swap
t=120s   clock at position C     ← drift + swap back
t=180s   forecast at position D
t=240s   clock at position E
...
```

### Why this cadence (and not Options 2 or 3)

| Option | Pro | Con | Verdict |
|---|---|---|---|
| **1. Every other reposition (chosen)** | Forecast gets ~60s on screen — long enough for a glance, short enough not to feel parked. Forecast also gets the full anti-burn-in benefit (it's at a fresh offset every appearance). Implementation is one boolean toggle inside the existing `_driftTimer` callback — no new timer, no new mental model. | Forecast and clock each get 50% of screen time — slightly clock-heavy users might want more time on the clock. Mitigation: not in v1, this can become a "Display:Weather:DurationRatio" knob if anyone asks. | ✓ pick |
| **2. Clock for X positions, then forecast for 1** | Tunable bias toward the clock. | Introduces a counter + a configurable ratio just to express "clock more than forecast." Premature flexibility for v1 (user hasn't asked for it). | reject |
| **3. Separate forecast cycle (forecast on its own timer)** | Independent cadences feel "organic." | Means **two** ±20% drifts running on overlapping schedules — the composition could swap mid-drift-transition, which fights the CSS-eased translate. Also adds a second `Timer` to dispose. Extra moving parts for no user-visible win. | reject |

Option 1 also has a natural a11y story: the same 1-Hz `_clockTimer` that ticks the clock string keeps ticking while the forecast is on-screen (we just don't render `_clockText` then), so when the swap happens the clock string is current within 1s — no "stale 15:45 reappearing as 15:47" jump.

### Initial-state choice

On `/sleep` entry, the screen shows the **clock first** (matches today's first impression — the user just dimmed/idled into sleep, the clock is the most-expected sight). The forecast appears 60s later on the first drift fire. If the forecast service returns `null` at that moment, we skip the swap and show the clock again at a new offset (see §6.A).

### Anti-burn-in compatibility (also §7 below)

Both compositions ride inside the same `<div class="sleep-screen-drift" style="@_shiftStyle">` wrapper. The forecast pane therefore inherits the exact same translate / CSS-eased transitions / safe-area math as the clock. Forecast pixels never park — each appearance is at a fresh offset within ±384/±144. The wider forecast pane (640px vs the clock cluster's ~360px) trims the effective horizontal safe area to roughly ±224px from center while the forecast is up; the existing `PickNewDriftOffset()` doesn't need to know — the worst-case 384px offset still leaves 230px of bezel clearance. No code change to drift math required.

---

## 4. Icon mapping refinement

ADR §2.6 references the icon mapping table but defers the concrete list to Designer. Here is the mapping from `WeatherDay.IconKey` (Core / NWS-stable) → Material Symbols name (Web-renderable via the `<RadzenIcon>` component, same as every other icon on the site). Every key in the left column MUST be produced by `NwsIconMapper` (Infrastructure); every key in the right column MUST be renderable in `SleepForecastPane`. The `unknown` fallback is mandatory — `NwsIconMapper` returns it for any unmapped NWS icon URL.

| `IconKey` (Core) | Material Symbol (Web) | NWS source examples |
|---|---|---|
| `sunny` | `sunny` | day/skc, day/few |
| `mostly-sunny` | `partly_cloudy_day` | day/sct |
| `partly-cloudy` | `partly_cloudy_day` | day/bkn (day) |
| `mostly-cloudy` | `cloud` | day/ovc, night/ovc |
| `cloudy` | `cloud` | day/ovc, night/ovc |
| `clear-night` | `clear_night` | night/skc, night/few |
| `partly-cloudy-night` | `partly_cloudy_night` | night/sct, night/bkn |
| `rain` | `rainy` | rain, rain_showers |
| `rain-light` | `rainy_light` | rain_light, drizzle |
| `rain-heavy` | `rainy_heavy` | rain_heavy |
| `thunderstorm` | `thunderstorm` | tsra, tsra_sct |
| `snow` | `weather_snowy` | snow, blowing_snow |
| `sleet` | `weather_mix` | sleet, freezing_rain |
| `fog` | `foggy` | fog, smoke, haze |
| `wind` | `air` | wind (any prefix), tornado |
| `hot` | `device_thermostat` | hot |
| `cold` | `severe_cold` | cold, blizzard (severe) |
| `unknown` | `cloud_off` | anything `NwsIconMapper` can't classify |

### Symbol-set note

All entries above are in the **Material Symbols Rounded** variable font family already loaded by the project (verified via `--font-mono` and existing icon usage in MainLayout and SystemConfigPage). No new icon font dependency. The 40px size in the sleep card is a single CSS override on `.sleep-forecast-icon` — Material Symbols are variable-font and scale cleanly.

### Single-color rule

All icons render at `color: color-mix(in srgb, var(--signal-amber) 35%, #050507)` — the **exact same dim-amber as the sleep clock**. This is a deliberate choice: the sleep screen has **one emissive color** (dim amber) and one passive color (`--text-low` mono). A multi-color weather icon set (sunny=yellow, rain=blue, etc.) would break the "stereo's off-state" feel called out in the sleep-screen CSS comment block (design-system.css:2806-2808). The icon **shape** carries the meaning; the color stays uniform.

---

## 5. Configuration UI

A new **Weather** group inside the Display sub-tab (created by PR #1) of the Configuration tab in `SystemConfigPage.razor`. Group sits **below** the existing Time-Format group, separated by the same sub-heading divider PR #3 uses for "RDS RadioText Ticker" in the Radio tab.

### Layout

```
┌─ System Configuration ─────────────────────────────────────────────────────┐
│ ▾ Configuration                                                            │
│   ┌──────────────────────────────────────────────────────────────────────┐ │
│   │ Audio │ Visualizer │ Output │ Devices │ ▌Display │ Audio Engine │ …  │ │
│   ├──────────────────────────────────────────────────────────────────────┤ │
│   │                                                                      │ │
│   │ TIME FORMAT                                  (PR #1 — existing)      │ │
│   │  ┌───────────────────────────┐                                       │ │
│   │  │ 24-hour (15:45)         ▾ │   ☐ Show seconds                      │ │
│   │  └───────────────────────────┘                                       │ │
│   │                                                                      │ │
│   ├──────────────────────────────────────────────────────────────────────┤ │
│   │                                                                      │ │
│   │ SLEEP-SCREEN WEATHER                          (this PR)              │ │
│   │  Adds a 3-day forecast that alternates with the clock on the         │ │
│   │  sleep screen, using the US National Weather Service. Free, no       │ │
│   │  API key required.                                                   │ │
│   │                                                                      │ │
│   │  ☑ Enable weather on sleep screen                                    │ │
│   │                                                                      │ │
│   │  ┌─────────────────────┐  ┌─────────────────────┐                    │ │
│   │  │ ZIP code            │  │ Refresh interval    │                    │ │
│   │  │ 27312               │  │ 60 minutes      ▴▾  │                    │ │
│   │  │ Pittsboro, NC ✓     │  │  (15 – 360)         │                    │ │
│   │  └─────────────────────┘  └─────────────────────┘                    │ │
│   │                                                                      │ │
│   │  ┌─────────────────────┐  ┌─────────────────────────────────────┐    │ │
│   │  │ Temperature unit    │  │ Contact email (optional)            │    │ │
│   │  │ Fahrenheit (°F)   ▾ │  │ you@example.com                     │    │ │
│   │  └─────────────────────┘  └─────────────────────────────────────┘    │ │
│   │                          Sent to NWS as a User-Agent so they can     │ │
│   │                          contact you if there's an issue.            │ │
│   │                                                                      │ │
│   │ ┌──────────────────────┐                                             │ │
│   │ │ 💾  Save Display     │                                             │ │
│   │ └──────────────────────┘                                             │ │
│   └──────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────┘
```

### Control mapping (Radzen primitives, sibling-tab pattern)

| Field | Control | Validation | Notes |
|---|---|---|---|
| `Display:Weather:Enabled` | `RadzenCheckBox` + inline `<span>` label | n/a | Top of the group; toggling off greys the remaining four controls (`Disabled="@(!_weatherConfig.Enabled)"` on each). |
| `Display:Weather:Zip` | `RadzenTextBox`, `MaxLength="5"` | 5 ASCII digits; reject letters/symbols client-side | On blur, fire a "resolve ZIP" call to `WeatherApiService.ValidateZipAsync(zip)`. On success, show `✓ Pittsboro, NC` in `--text-low` beneath the input. On failure, show `✗ ZIP not recognized` in the error color the page already uses. (Validation is **soft** — the user can still save; the runtime fetch will surface `null` and the pane will hide.) |
| `Display:Weather:RefreshIntervalMinutes` | `RadzenNumeric<int>`, `Min="15"`, `Max="360"`, `Step="15"` | 15–360 (matches ADR §2.5) | Suffix label `minutes` after the field. |
| `Display:Weather:TemperatureUnit` | `RadzenDropDown`, 3 options | One of `"F"`, `"C"`, `"both"` | Option labels: `Fahrenheit (°F)`, `Celsius (°C)`, `Both (°F · °C)`. Mirrors PR #1's "show the format in the option text" convention. |
| `Display:Weather:ContactEmail` | `RadzenTextBox` | Basic email regex client-side; empty allowed | Helper text below explains the NWS User-Agent requirement (taken verbatim from ADR §2.6). |
| Save button | `RadzenButton Variant="Filled" ButtonStyle="Primary" Icon="save"` | n/a | Same "Save Display" button serves **both** the Time group and the Weather group — one round trip writes all six keys (PR #1's two + this PR's five). The button label stays `Save Display` to make it obvious one save covers the whole tab. |

### Why one Save button for the whole Display tab

Every other Configuration sub-tab (Audio, Visualizer, Output, Devices, Audio Engine, Radio) uses a single tab-scoped Save button. Splitting the Display tab into per-group saves would be the only place in `SystemConfigPage.razor` with multiple saves per tab — design-system drift. The `_displayConfig` POCO that PR #1 introduces grows five additional properties (`WeatherEnabled`, `WeatherZip`, `WeatherRefreshMin`, `WeatherTempUnit`, `WeatherContactEmail`) and `SaveDisplayConfigAsync` writes all seven keys in one batch via the existing `ConfigurationApiService.SetValueAsync` loop pattern.

### Hot-reload

Identical mechanism to PR #1 — the SQLite bridge + `ConfigStoreChangeNotifier.NotifyReload()` propagates writes through `IOptionsMonitor<WeatherDisplayOptions>`. The sleep screen reads `WeatherOptionsMonitor.CurrentValue` on the next forecast fetch (lazy, never blocks) and the user sees changes within the next swap cycle (worst case 60s). No service restart, no circuit drop.

---

## 6. Failure-mode UI

These are the load-bearing visuals from ADR §2.3. Builder MUST implement each case explicitly — none of them is a "happy path with degraded styling."

### A. Forecast service returns `null` (cache empty + upstream fails, or `Enabled=false`)

**Visual:** the sleep screen shows the clock at every drift cycle. The alternation logic still runs (the `_isShowingForecast` boolean still flips), but when it would render the forecast pane it instead falls through to the clock cluster again. **Net effect:** the user sees the clock at every offset, same as today's behavior, with no visible artifact of the failed forecast attempt.

**Reason for this choice (vs. "skip the offset entirely"):** if the clock has been parked at offset A for 60s and the forecast was about to show at offset B, *not* repositioning would leave the clock burning at A for another 60s — defeating the anti-burn-in contract. We always reposition; we just sometimes re-render the same composition.

**Logging:** at `Information` level on first `null` per sleep session (Sleep.razor logs to `Logger`), then suppressed for the rest of the session to avoid log spam.

### B. `IsStale = true` (cached data served while refresh failed)

**Visual:** State C in §2 — pane renders normally at 70% opacity with a `sync_problem` icon (Material Symbol, 14px, dim amber) prefixing the location/timestamp footer. Timestamp adds `yesterday` or `N days ago` qualifier if `GeneratedAtUtc` is older than 12 hours.

**Why subtle (not loud):** stale data is still useful data — the user isn't getting an emergency, they're getting an hour-old forecast that almost certainly still describes today's weather. A loud warning would train the user to ignore staleness signals when they actually matter (e.g. a 3-day-old forecast). The 30% opacity drop is the same affordance pattern the existing radio-controller handoff uses for "this thing still works but isn't current."

### C. Partial days (NWS returned fewer than 3 forecast periods)

**Visual:** State D in §2 — render the cards we have, centered in the pane. No `—` placeholder card, no "data missing" copy. The user sees a 1- or 2-card forecast that is fully truthful about what's known. The footer text and stale indicator behave identically to the 3-day case.

**Zero days returned:** treated as null (case A). The pane never renders.

### D. ZIP misconfigured (e.g. user typed `999999` or `abcd`)

**Visual on sleep screen:** clock-only (case A). The pane never appears; the user never sees a stale "no ZIP" placeholder on the kiosk.

**Visual in Settings UI:** the ZIP-validation chip beneath the input shows `✗ ZIP not recognized` in the error color. The Save button still works (user can save an invalid ZIP without being blocked) — the consequence is just that the sleep screen reverts to clock-only until they fix it. A first-time user typing `2731` (4 digits) sees the same chip, can keep typing to fix it without losing focus.

**No toast, no modal, no banner on the Home page.** The Settings page is the right place to surface the ZIP problem; the sleep screen is the wrong place to surface configuration errors.

### E. Weather disabled, then re-enabled mid-session

**Visual:** the next swap cycle (≤60s later) the forecast appears. No need for the user to restart the kiosk or navigate away from `/sleep`. The `IOptionsMonitor` hot-reload from §5 + the lazy-fetch model from ADR §2.3 makes this work for free.

---

## 7. Anti-burn-in compatibility

The forecast pane MUST honor the same anti-burn-in contract as the clock. Concretely:

1. **Same drift wrapper.** The forecast pane renders **inside** `<div class="sleep-screen-drift" style="@_shiftStyle">`, not as a sibling. The existing CSS-eased transform applies to it without any new rule.
2. **No per-element animations.** No icon spin, no "as of HH:mm" tick, no internal animation on the cards. The whole pane is static-content + outer drift, identical motion vocabulary to the clock.
3. **Same dim color budget.** Every emissive element in the pane uses the same `color-mix(in srgb, var(--signal-amber) 35%, #050507)` as `.sleep-screen-clock`. Every passive element uses `var(--text-low)`. No new tokens. No element brighter than the clock.
4. **Reduced-motion respect.** The CSS rule `@media (prefers-reduced-motion: reduce) { .sleep-screen-drift { transition: none; } }` (design-system.css:2862-2864) already covers the drift transition. The swap itself between clock and forecast is an instant DOM swap (no fade), so there's no animation to disable. **Decision:** keep the swap instant in both motion modes. Adding a cross-fade would require a new transition that ignores reduced-motion users — net negative.
5. **First-paint position.** When the forecast first appears at t=60s, it uses the drift offset just selected by `PickNewDriftOffset()` for **this** tick, not the previous one. (The existing `_driftTimer` callback already calls `PickNewDriftOffset()` *before* `StateHasChanged()`, so this falls out for free — no code-order change required.)
6. **Composition geometry stays bounded.** As noted in §2, the worst-case forecast pane (640px wide, with `TemperatureUnit="both"` extending to 700px) at the worst-case drift offset (+384px) leaves ≈ 230px of bezel clearance on a 1920px panel. The existing `Sleep.razor` comment at line 95–98 about the 360px clock fitting in the safe area is still satisfied with margin.

---

## 8. Accessibility

The sleep screen is a kiosk surface — primarily visual, but the wake affordance is already keyboard-accessible (`tabindex="0"` on `.sleep-screen` with any-key wake at Sleep.razor:251-256). The forecast pane inherits that wake behavior automatically; tapping or pressing any key on the forecast wakes the system just like tapping the clock.

### Screen-reader announcements

The wrapping `<div class="sleep-screen" role="button" aria-label="Tap to wake">` already governs the surface semantics. The forecast pane's internal content is **decorative** by default (it's a glance display), but for screen-reader users a polite live region is appropriate:

- The forecast pane has `aria-live="polite"` and `aria-atomic="true"` on its outermost `<div class="sleep-forecast-pane">`.
- The pane's `aria-label` is **regenerated on each swap-in** in the form `"3-day forecast for Pittsboro. Today partly cloudy, high 72, low 54. Monday showers, high 68, low 52. Tuesday sunny, high 78, low 58."` — a single static string the SR can read end-to-end without scrubbing card-by-card. Temperatures use the same unit as the visible UI (the `TemperatureUnit` setting governs both).
- The label is **regenerated** only when the forecast data or unit changes — not on every swap. The SR fires its announcement when `aria-live` content changes, so re-announcing the identical string every 120s would be noise. Implementation: cache the last-rendered label on the component; only push a new one when the underlying data changes.
- For the clock swap-back, the existing clock cluster has no `aria-live` (and we don't add one — the clock string changes every second; live-announcing every 1-Hz tick would be hostile). The SR simply re-reads the wrapping "Tap to wake" affordance when the user lands on the surface.
- Stale state: the label prepends `"Forecast data is stale, last updated yesterday at 1 PM. "` before the day-by-day breakdown. No icon-only signal would be SR-visible otherwise.
- Null forecast: no `aria-live` content at all (the pane doesn't render). The SR experience is identical to today's clock-only sleep screen.

### Color contrast

All dim-amber and `--text-low` elements are already used elsewhere on the sleep screen and pass the kiosk's contrast bar (calm display, not WCAG AAA — the surface is intentionally low-stimulus). Nothing new to verify.

### Touch / mouse / keyboard parity

Same as today — any tap or keypress on the sleep surface wakes the kiosk. The forecast cards are **not** independently interactive (no tap-to-expand, no hover state, no per-card focus ring). Selecting any card or the gaps between them produces the same wake-and-go-home behavior as tapping the clock.

---

## 9. Coordination with PRs #1 and #3

### Display sub-tab ordering

**Pick: `Time` (PR #1) → `Weather` (this PR).**

PR #3 (RDS scroll) places its config under the **Radio** sub-tab, not Display — verified by reading `HANDOFF-rds-accumulating-scroll.md` §5 ("Three new keys live under the existing **Radio** config tab in System Config"). So PR #3 does **not** touch the Display tab at all, and the Display tab's contents are just Time (PR #1) + Weather (this PR).

Ordering rationale: Time is the more universally-used setting (every user has a clock preference; not every user wants weather). Time sits first; Weather sits second. Both groups separated by the sub-heading divider pattern PR #3 established for "RDS RadioText Ticker" in the Radio tab. Same `SaveDisplayConfigAsync` writes all seven keys (two Time + five Weather) in one batch.

### Merge sequence

1. **PR #1** (configurable time format) merges first — establishes the Display sub-tab and the `Clocks.FormatWallClock` helper.
2. **PR #3** (RDS scroll) is independent and can merge in either order with this PR.
3. **PR #2 — this one** (sleep weather) merges after PR #1 and depends on:
   - The Display sub-tab existing.
   - The `Clocks.FormatWallClock(local, opts, allowSeconds)` helper being available for the "as of HH:mm" footer.
   - The `DisplayOptions` POCO being registered (we extend it with the weather properties OR we register a sibling `WeatherDisplayOptions` POCO — Planner's call; ADR §2.5 spec'd the latter, so default to that).
   - The `_displayConfig` model / `SaveDisplayConfigAsync` method existing so we can extend rather than parallel-implement.
4. If PR #1 has not yet merged when Builder starts this work, **rebase against it** before opening this PR. Do not parallel-implement the Display sub-tab.

### Naming alignment

PR #1's `DisplayOptions` POCO lives in `src/Radio.Web/Models/DisplayOptions.cs`. ADR-022 §2.5 spec'd a separate `WeatherDisplayOptions` in `src/Radio.Core/Configuration/`. We keep them separate (per ADR) but bound to the same `Display:*` config root — Web has `DisplayOptions` (Time keys), Core/Web has `WeatherDisplayOptions` (Weather keys). The Settings page's `_displayConfig` is a UI-only composite that flattens both for rendering and re-splits on save.

---

## 10. Answers to ADR-022's three open questions

### Q1 — Built-in ZIP fallback table contents

**Answer:** ship **only `27312`** in the fallback table for v1.

Reasoning: the fallback table exists for the cold-start-without-internet edge case on the user's own kiosk. Any non-default ZIP is by definition typed in by the user — and a user typing a ZIP almost certainly has internet (they're configuring the box). Shipping NYC / LA / Chicago "just in case" would be 5 entries of dead weight for our single-household appliance. If a second household ever uses this, the maintainer adds their ZIP to the table in a one-line PR. The cost of growing the table later is zero; the cost of shipping unused entries now is non-zero (mental overhead, "why is THIS specific list curated?").

### Q2 — Day-name format

**Answer:** `"Today"`, `"Tomorrow"`, then **3-letter weekday abbrev** (`"Mon"`, `"Tue"`, `"Wed"`, `"Thu"`, `"Fri"`, `"Sat"`, `"Sun"`).

Reasoning: The mockup in §2 uses `TODAY` / `MON` / `TUE` (uppercased visually via CSS `text-transform: uppercase`, but the underlying string is title-case so screen-readers don't shout). 3-letter matches the prompt's example, fits the 200px card without wrapping, and is unambiguously English. `"Tomorrow"` is 8 characters — slightly long for the same card slot — but at 14px mono with 0.12em letter-spacing it measures ~120px, still inside the 200px card with comfortable margin. The string comes from the Core data contract (`WeatherDay.DayName` per ADR §2.1) — `NwsWeatherService` is responsible for producing the correct string per the kiosk's local day index:

- Day index 0 (today's date in kiosk local time) → `"Today"`
- Day index 1 → `"Tomorrow"`
- Day index 2+ → 3-letter weekday, English, invariant culture

This is consistent with PR #1's invariant-culture rule (locale-aware day names are explicit non-goal — see PR #1 §9.2).

### Q3 — Contact email fallback policy

**Answer:** hardcoded fallback `radioconsole@localhost.local` so the feature works out of the box, **plus** a one-line helper text in the Settings UI nudging the user to add their real email.

Reasoning: NWS allows anonymous traffic but reserves the right to rate-limit it. For a single-household kiosk hitting the API once per hour, anonymous is fine in practice and we don't want a "weather is broken" failure mode on first deploy. The Settings UI's helper text (verbatim from §5: *"Sent to NWS as a User-Agent so they can contact you if there's an issue."*) gives the user a clear reason to fill it in; no nag toast, no modal — Settings is the right surface for the prompt, the kiosk is not.

`WeatherServiceExtensions.AddRadioWeather()` builds the User-Agent as `RadioConsole/1.0 (+{ContactEmail or fallback})`. The fallback string is `radioconsole@localhost.local` (the `.local` TLD makes it obvious to an NWS sysadmin that it's a placeholder, not a real address). When the user sets a real email, the User-Agent rebuild fires via `IOptionsMonitor<WeatherDisplayOptions>` change events — no service restart.

If down the line we observe rate-limiting against the fallback UA, we can promote the fallback to a hardcoded "RadioConsole anonymous deploy — please contact <maintainer-email>" without UI changes. Out of scope for v1.

---

## 11. Open questions for the user

Designer picked the answers below pragmatically. Confirm or override before Builder ships.

1. **Alternation cadence = 50/50 (every drift cycle).** Picked over biased ratios because nobody has asked for bias and it adds config surface. If you want the clock to dominate (e.g. clock 4 cycles → forecast 1 cycle), say so and we add `Display:Weather:CycleRatio` with default `1`. **Designer recommendation: ship 50/50.**

2. **Default `TemperatureUnit = "F"`.** Matches ADR-022's default and the user's home location (Pittsboro, NC, USA). If you want `"both"` as default to maximize info density, say so — it slightly shrinks the high/low type to fit both numerics. **Designer recommendation: ship `F`; let the user pick `both` if they want it.**

3. **Stale-data threshold for "yesterday" qualifier = 12 hours.** Below 12h, footer reads `as of 3:00 PM`; at or above, reads `as of 1:00 PM yesterday` (or `N days ago`). Could be 6h, 24h, etc. **Designer recommendation: 12h — anything less is fiddly, anything more reads as "today" too long after midnight rolls.**

4. **Footer location text = `City, ST`.** Pulled from `WeatherForecast.LocationName` (already in the data contract). On a long city name (`Winston-Salem, NC` = 17 chars) the footer line stays comfortably under the pane width. If you'd rather drop the location and show only the timestamp, say so. **Designer recommendation: keep the location — confirms to the user which ZIP the forecast is for, especially useful during initial setup.**

5. **No interactive forecast cards.** No per-card tap-to-expand, no per-card hover state. The sleep screen is wake-or-do-nothing. **Designer recommendation: keep cards inert; a future "Wake to Weather" feature is the right place for interactive forecast UI.**

6. **No fade transition between clock ↔ forecast swap.** Instant DOM swap, then the drift wrapper animates the position. A cross-fade would be 200–300ms of overlap, fighting the reduced-motion media query. **Designer recommendation: stay instant — the drift transition is the "motion language" of this screen.**

7. **Display tab icon stays `schedule` (PR #1's choice).** Weather group sits under the same tab; no separate icon. If you want the tab icon to switch to something more generic like `display_settings` now that there are two groups, say so. **Designer recommendation: keep `schedule` — Time is the dominant feature on the tab; the icon should match that.**

8. **First-paint = clock (not forecast).** Sleep entry shows the clock first; forecast appears at t=60s. Reasoning: matches today's first-impression and avoids hitting NWS on a sleep entry that's about to be a 10s tap-to-wake. If you want the forecast to be the very first thing the user sees, we flip the initial `_isShowingForecast` to `true` and the forecast fetches eagerly on mount. **Designer recommendation: clock-first.**

---

## 12. Out of scope

Explicitly NOT in this PR. Flag any of these to the user if scope-creep pressure builds.

1. **Multi-location support.** Single ZIP only. ADR-022 §3 already commits to a future ADR if the user wants multiple locations.
2. **Hourly forecast detail.** The pane shows day-level high/low only. No hourly breakdown, no overnight low/morning high split, no per-hour precipitation timeline.
3. **Severe weather alerts.** No NWS `/alerts/active` integration. Tornado warnings do not override the sleep screen in this PR. (ADR-022 §3 calls this out as a future-ADR-worthy feature.)
4. **Sunrise / sunset / moon phase.** NWS doesn't provide astronomical data. No second-provider integration.
5. **Precipitation amounts / wind speed / humidity.** The card shows icon + condition + high + low only. ADR §2.1 carries `PrecipitationProbabilityPct` in the data contract but we deliberately don't render it on the sleep card — would crowd the 200px card and the iconography already implies precipitation. If you later want a "70% chance of rain" tag, it's a one-line CSS+markup change.
6. **Animated icons.** Pure static Material Symbols. No rotating sun, no animated rain. The sleep screen's motion vocabulary is the drift wrapper; nothing inside it animates.
7. **Per-day tap-to-expand.** Cards are inert. See open question 5.
8. **Forecast on Home page or any other surface.** This PR adds the forecast to the sleep screen only. A future "small forecast widget on Home" is mentioned in ADR-022 §1 as a possible reuse of the same data; not in this PR.
9. **Locale-aware day names or temperature glyphs.** Invariant-culture English (`Today` / `Tomorrow` / `Mon`) and ASCII degree symbol (`72°F`). Same constraint as PR #1.
10. **Background refresher / push.** The forecast refreshes lazily on the next swap that comes due after the cache TTL expires. No `BackgroundService`, no SignalR hub. ADR-022 §2.4 chose this explicitly.

---

## Hand-off summary for Planner / Builder

Add a 3-day NWS forecast pane that swaps in for the LED clock every other 60-second drift cycle on `/sleep`. Pane lives inside the existing `.sleep-screen-drift` wrapper so it inherits the same anti-burn-in transform — no new motion. Three cards (200px each, `--font-mono` headers + 40px dim-amber Material Symbol icon + `--font-led` high/low temps), with explicit visuals for stale (`opacity: 0.7` + `sync_problem` 14px icon), partial (1–2 cards centered), and null (pane never renders, clock continues alone — load-bearing per ADR §2.3). Five new config keys (`Display:Weather:Enabled/Zip/RefreshIntervalMinutes/TemperatureUnit/ContactEmail`) live in a new "Sleep-screen weather" group inside PR #1's Display sub-tab; one shared `Save Display` button writes both Time and Weather keys. Hot-reload via the existing SQLite-bridge + `IOptionsMonitor<WeatherDisplayOptions>` plumbing. Icon mapping table in §4 closes ADR §2.6's open mapping. Single emissive color (dim amber, 35%) preserves the "stereo's off state" sleep aesthetic. No new tokens, no new fonts, no new API endpoints beyond what ADR-022 already specified.
