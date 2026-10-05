# HANDOFF — Sleep-mode weather pane: current conditions in the primary block

**Component:** `src/Radio.Web/Components/Shared/SleepForecastPane.razor` (markup + 2 lines of computed-property changes), plus light backend additions in `src/Radio.Core/Models/WeatherForecast.cs`, `src/Radio.Infrastructure/Weather/NwsWeatherService.cs`, and a new internal DTO `src/Radio.Infrastructure/Weather/Dtos/NwsObservationResponse.cs`.
**Surface:** Kiosk sleep screen (route `/sleep`) — same drift cluster, same alternation cadence, same pane bounding box.
**Status:** `[PENDING REVIEW]` — ready for Planner / Builder.
**Supersedes:** None. **Extends** the data contract and service implementation pinned by ADR-022 §2.1 and §2.4; **extends** the visual treatment pinned by `HANDOFF-sleep-weather-visual-redesign.md` §3 / §4 / §6.

**Relationship to existing handoffs:**
- **Follows** `HANDOFF-sleep-weather-visual-redesign.md` (v2) — the 96px LED primary readout, 96px icon, condition text, sub-line, and 3-card forecast row are preserved verbatim. Token vocabulary (`color-mix(in srgb, var(--signal-amber) 35%, #050507)`, `--text-medium`, `--text-low`, `0 0 12px ...` text-shadow) is unchanged. No new tokens.
- **Follows** `HANDOFF-sleep-mode-weather-forecast.md` §3–§12 — alternation cadence, anti-burn-in math, accessibility surface, configuration UI, failure-mode UI all unchanged.
- **Extends** ADR-022 §2.1 — adds an optional sibling `CurrentObservation` record to `WeatherForecast`; existing `WeatherDay[]` field is unchanged. This is the LIGHT architecture decision in this spec.
- **Extends** ADR-022 §2.3 caching strategy — adds two new per-ZIP cache keys (`weather:zip:{zip}:stations`, `weather:zip:{zip}:observation`) with their own TTLs alongside the existing coords/grid/forecast keys.
- **Extends** ADR-022 §2.4 service boundaries — `NwsWeatherService` gains an internal observation-fetch path; one new DTO file. No new public interfaces.
- **Deviates** from `HANDOFF-sleep-weather-visual-redesign.md` §3 State A — the 96px LED primary numeral now represents **current observed temperature**, not today's forecast high. Today's forecast H/L moves down to a smaller supplementary line within the right column. **Driver:** user feedback after PR #415 live review — *"the big number is for what it's doing right now, not what the high will be later today; the H/L still belongs in the pane but as a supporting datum, not as the headline."*

---

## 1. Problem + user feedback

The v2 redesign (PR landing after this spec) made the sleep weather pane a single glanceable primary readout — but the 96 px LED numeral in that primary block is bound to `WeatherDay.HighF` (today's forecast HIGH). At 7 PM that reads "60°" because today's high was 60° hours ago, even when the current observed temperature is 48° and falling. The user's mental model on a glance at the kiosk is *"what is it doing right now?"*, not *"what was the peak temperature predicted for today?"* The forecast H/L still has value as supporting context (it lets the glance answer "is it getting colder?"), but it is not the headline.

This iteration rebinds the big number to current observations from NWS's `/stations/{id}/observations/latest` endpoint, keeps today's H/L visible as a smaller supplementary indicator inside the same right column, and degrades gracefully back to v2 behavior (today's H/L as the headline) when the observation fetch fails or no observation is available.

---

## 2. Data source confirmation

NWS provides current observations through a four-call chain that branches off the existing points lookup. The first two calls (ZIP → coords → grid) are already done as part of the forecast fetch in `NwsWeatherService.GetForecastAsync`. The new work is the third and fourth call:

```
[existing]  ZIP            → coords           (cached forever per ZIP)
[existing]  coords         → points/grid      (cached 30 days per ZIP)
[existing]  points/grid    → forecast URL     → forecast periods (cached fresh-TTL per ZIP)
                              │
                              │  points response also has properties.observationStations URL
                              │
[NEW]                      → observationStations URL
                              GET <observationStations>
                              → list of nearby station IDs, ordered by distance
                              (cached forever per ZIP — stations don't move)
[NEW]                      → /stations/{closestStationId}/observations/latest
                              → properties.temperature.value (°C — needs conversion to °F)
                              → properties.textDescription   ("Partly Cloudy")
                              → properties.icon              (URL — reuse NwsIconMapper)
                              → properties.timestamp         (ISO 8601 observation time)
                              (cached 30 min fresh + 24 h stale-serve per ZIP)
```

References:
- NWS API: <https://www.weather.gov/documentation/services-web-api>
- Observation endpoint: <https://api.weather.gov/openapi.json> (paths `/gridpoints/{wfo}/{x},{y}/stations` and `/stations/{stationId}/observations/latest`)
- Observation properties documented under `ObservationCollectionGeoJson` and `ObservationGeoJson` schemas in the OpenAPI spec — `properties.temperature` is a `QuantitativeValue` with `unitCode` typically `wmoUnit:degC`; `properties.textDescription` is a short human label; `properties.icon` follows the same URL shape `NwsIconMapper` already handles for forecast periods.
- Station refresh cadence: METAR stations report roughly hourly (some report more frequently). The 30-min fresh-TTL gives sub-hour freshness without hammering the endpoint.

We deliberately do **not** use `/gridpoints/{wfo}/{x},{y}/observations` (an alternate "all observations near the grid point" endpoint) — it returns N observations from N stations interleaved and forces us to do client-side station selection on every poll. The two-step (stations list → latest from closest) chain is cleaner and reuses the existing per-ZIP cache vocabulary.

---

## 3. Data contract additions (the light architecture decision)

### 3.1 New record: `CurrentObservation`

Add to `src/Radio.Core/Models/WeatherForecast.cs`:

```csharp
namespace Radio.Core.Models;

/// <summary>
/// A snapshot of currently-observed weather conditions from the nearest reporting
/// station, paired with the broader <see cref="WeatherForecast"/>. The sleep
/// screen renders this as the headline numeral (the "what is it doing right
/// now" answer) while the per-day forecast handles the outlook.
///
/// Always nullable on the parent forecast — if the observation chain fails
/// (no nearby station, station returned no data, network failure), the
/// forecast still renders with today's high as the headline (v2 fallback).
/// </summary>
/// <param name="TempF">Observed temperature, rounded to nearest int Fahrenheit.</param>
/// <param name="TempC">Observed temperature, rounded to nearest int Celsius.</param>
/// <param name="ConditionShort">Short condition label from
/// <c>properties.textDescription</c> (e.g. <c>"Partly Cloudy"</c>). NWS labels
/// are already capitalized.</param>
/// <param name="IconKey">Stable icon key mapped from
/// <c>properties.icon</c> via <see cref="NwsIconMapper.MapToIconKey"/>. Same
/// vocabulary as <see cref="WeatherDay.IconKey"/>.</param>
/// <param name="ObservedAtUtc">When the observation was taken by the station
/// (<c>properties.timestamp</c>). Used both for the staleness rule and
/// (optionally, future) an "observed at HH:mm" affordance.</param>
/// <param name="IsStale"><c>true</c> when the observation is older than 2
/// hours from now (regardless of whether the cache or the network supplied
/// it) OR when the observation API call failed and we are serving a cached
/// value. UI applies the same opacity-0.7 + sync_problem affordance the
/// parent forecast already uses.</param>
public sealed record CurrentObservation(
  int TempF,
  int TempC,
  string ConditionShort,
  string IconKey,
  DateTimeOffset ObservedAtUtc,
  bool IsStale);
```

### 3.2 Extend `WeatherForecast`

Add a sibling field on the existing record. Keep the field **nullable** — that's the load-bearing graceful-degradation contract.

```csharp
public sealed record WeatherForecast(
  string Zip,
  string LocationName,
  DateTimeOffset GeneratedAtUtc,
  DateTimeOffset FetchedAtUtc,
  bool IsStale,
  IReadOnlyList<WeatherDay> Days,
  CurrentObservation? Current);   // NEW — nullable; UI must tolerate null
```

`Current` is the LAST positional parameter so callers constructing forecasts without observations (test code, the v1 service before the new path is wired) continue to compile after a simple `Current: null` addition. **Greenfield project convention from MEMORY.md applies** — no backward-compat shim, no extension method, no second constructor; just add the parameter.

### 3.3 What the API surface returns

`GET /api/weather/forecast` continues to return `WeatherForecast` as the JSON payload. The new field appears as a sibling JSON object:

```json
{
  "zip": "27312",
  "locationName": "Pittsboro, NC",
  "generatedAtUtc": "2026-05-24T18:00:00Z",
  "fetchedAtUtc":   "2026-05-24T18:01:23Z",
  "isStale": false,
  "days": [ /* 3 WeatherDay records */ ],
  "current": {
    "tempF": 48,
    "tempC": 9,
    "conditionShort": "Partly Cloudy",
    "iconKey": "partly-cloudy-night",
    "observedAtUtc": "2026-05-24T17:53:00Z",
    "isStale": false
  }
}
```

When the observation chain fails, `"current": null` appears in the payload. **The Web layer's typed deserializer must handle the null** — `WeatherApiService` already deserializes into the record; the new nullable property auto-handles via `System.Text.Json` defaults.

### 3.4 What this is NOT

- Not a new endpoint. `GET /api/weather/observation` would force a second round-trip from the kiosk for the same conceptual fetch. Bundling current + forecast in one payload is cheaper and matches the UI's single-render contract.
- Not a separate `IWeatherService` method. The interface stays `Task<WeatherForecast?> GetForecastAsync(string, CancellationToken)`. The observation fetch is an internal step of `FetchForecastAsync` — callers don't know it exists.
- Not a SignalR push. The observation refreshes every 30 min at most; polled HTTP fits the existing sleep-screen refresh cadence.

---

## 4. Service layer changes (`NwsWeatherService`)

### 4.1 New private methods

Add to `NwsWeatherService.cs`:

```csharp
private async Task<CurrentObservation?> GetOrFillObservationAsync(
    string zip, GridInfo grid, CancellationToken ct);

private async Task<string?> GetOrFillClosestStationAsync(
    string zip, GridInfo grid, CancellationToken ct);

private async Task<CurrentObservation?> FetchStationsAndLatestObservationAsync(
    string observationStationsUrl, CancellationToken ct);
```

Plus a new internal helper for the observation DTO → record conversion (handles °C-to-°F via the existing service formula, rounds with `MidpointRounding.AwayFromZero` to match `AggregateToDays`, calls `NwsIconMapper.MapToIconKey(props.Icon)`).

### 4.2 Where it slots into `FetchForecastAsync`

The new fetch runs **after** the grid step (which is what supplies `observationStations`). It runs **in parallel** with `FetchForecastPeriodsAsync` since both are independent of each other once the grid is known.

**Decision: use `Task.WhenAll`.** Rationale:

- The two calls are completely independent given the grid info. Sequencing them would add ~200–400 ms (typical NWS response time) to every cold-cache forecast fetch, doubling the perceived latency of the sleep screen's first paint.
- Parallel keeps the cold-start budget for the full chain at ~500 ms (the existing ADR §4 latency target) instead of ~700 ms.
- Failure isolation is preserved: `Task.WhenAll` re-throws the first exception, but we wrap each call's body in its own try/catch so an observation failure can't poison the forecast fetch. The pattern is:

```csharp
// Inside FetchForecastAsync, after grid is resolved:
var forecastTask = FetchForecastPeriodsAsync(gridInfo.ForecastUrl, ct);
var observationTask = TryFetchObservationAsync(zip, gridInfo, ct);
//   ^ wraps GetOrFillObservationAsync in try/catch, returns null on any
//     non-cancellation exception, logs Warning. NEVER throws.

await Task.WhenAll(forecastTask, observationTask).ConfigureAwait(false);

var nwsForecast = await forecastTask;
var observation = await observationTask;   // null on failure — that's fine
```

If `observationTask` returns null (failure or no station), the resulting `WeatherForecast.Current` is null and the UI falls back to v2 behavior. The forecast itself is unaffected.

### 4.3 New cache keys + TTLs

Two new entries join the existing three per ZIP:

| Cache key                                | TTL                          | Stampede protection |
|------------------------------------------|------------------------------|---------------------|
| `weather:zip:{zip}:coords`               | Process lifetime (existing)  | `_coordsLocks` (existing) |
| `weather:zip:{zip}:grid`                 | 30 days (existing)           | `_gridLocks` (existing) |
| `weather:zip:{zip}:forecast`             | Fresh-TTL + 24 h stale-serve (existing) | None (per existing rationale) |
| **`weather:zip:{zip}:stations`** *(NEW)* | Process lifetime (no expiry, `Priority = NeverRemove`) | New `_stationsLocks` per-ZIP semaphore |
| **`weather:zip:{zip}:observation`** *(NEW)* | 30 min fresh + 24 h stale-serve | None (same rationale as forecast — duplicate refresh at the boundary is cheap) |

Cache key constants follow the existing prefix pattern (`StationsKeyPrefix + zip + StationsKeySuffix`).

#### Why stations is cached forever

The list of nearby weather reporting stations for a given grid point essentially never changes within a process lifetime. Stations come online or go offline on the scale of years, not hours. Caching forever matches the rationale ADR-022 §2.2 used for ZIP centroids.

We also do **not** persist the stations list across process restarts (consistent with the existing forecast cache). Re-fetching the stations list on cold start is one extra HTTP call per ZIP per process — negligible.

#### Why observation has its own fresh-TTL (30 min, not the configured forecast interval)

NWS stations report on ~hourly cadence (some more often). The forecast `RefreshIntervalMinutes` is configurable in 15–360 (default 60) — using the same TTL would either over-fetch (15-min config wastes calls) or under-fetch (360-min config means a 6-hour-old "current" temp). 30 min is a fixed compromise that doesn't need a config knob.

If the user ever wants to tune observation freshness separately, it becomes a new option key (`Display:Weather:ObservationRefreshIntervalMinutes`) and a one-line change. Not needed for v1 of this feature.

#### Stale-while-revalidate for observation

Same pattern as the existing forecast cache:

- Fresh hit (cached < 30 min old AND observation timestamp < 2h old) → return as-is, `IsStale=false`.
- Stale entry + refresh succeeds → return new value with `IsStale` recomputed from the new timestamp.
- Stale entry + refresh fails AND cached entry < 24 h old → return cached with `IsStale=true`, log Warning.
- No cache + refresh fails → return null (parent `WeatherForecast.Current = null`).

#### Stampede protection

Add a new static dictionary:

```csharp
private static readonly ConcurrentDictionary<string, SemaphoreSlim> _stationsLocks =
  new(StringComparer.Ordinal);
```

Used in `GetOrFillClosestStationAsync` with the same double-checked locking pattern as `GetOrFillCoordsAsync` / `GetOrFillGridAsync` (the PR #415 review-fix pattern documented in lines 200–246 of the existing service). This is the load-bearing concurrency rule for cold-cache fills.

The observation cache itself is **not** lock-guarded — same rationale as the forecast cache: stale-while-revalidate tolerates a duplicate refresh at the TTL boundary, and adding a third semaphore tier serializes requests behind a slow upstream call.

### 4.4 Station selection

The stations endpoint returns `features[]` ordered by distance from the grid point (per NWS docs). We pick `features[0].properties.stationIdentifier`. **No multi-station fallback** — if the closest station's `observations/latest` returns 404 or returns a feature with `properties.temperature.value == null`, the entire observation fetch is treated as a failure and `Current` becomes null. Listed explicitly in §10 below.

Rationale: walking the station list adds branching complexity (which stations are "close enough"? do we cache per-station observation entries? what if 5 stations all 404?) for marginal UX benefit. A single station that's stale-but-cached + the v2 fallback to forecast-high is the simpler degradation path. Revisit only if telemetry shows the closest station fails frequently.

### 4.5 Temperature unit conversion

NWS observations come back in Celsius (`unitCode: "wmoUnit:degC"`). Convert with the same formula `AggregateToDays` already uses for the forecast path:

```csharp
int tempF = (int)Math.Round(tempC * 9.0 / 5.0 + 32, MidpointRounding.AwayFromZero);
```

Round each to int separately so the displayed integer Fahrenheit and Celsius are independently accurate to the original Celsius value — no compounded rounding error.

**Sanity guard:** if `properties.temperature.value` is null OR outside `[-60, 60]` °C (catches sensor glitches), treat the observation as a failure (return null from the fetch — same as a 404).

### 4.6 Existing methods to keep unchanged

- `GetForecastAsync` outer contract (caching, fresh/stale, null-on-failure) — unchanged.
- `GetOrFillCoordsAsync`, `GetOrFillGridAsync`, `FetchPointsAsync` — unchanged.
- `FetchForecastPeriodsAsync` — unchanged.
- `AggregateToDays`, `ComputeDayName` — unchanged.
- `SetForecastCacheEntryForTesting` — unchanged.

`FetchForecastAsync` is the only existing method whose body changes — it gains the parallel observation fetch and the new `Current` field in the constructed `WeatherForecast`.

### 4.7 New DTOs (internal-only)

Add `src/Radio.Infrastructure/Weather/Dtos/NwsStationsResponse.cs`:

```csharp
internal sealed class NwsStationsResponse {
  [JsonPropertyName("features")] public List<NwsStationFeature>? Features { get; set; }
}
internal sealed class NwsStationFeature {
  [JsonPropertyName("properties")] public NwsStationProperties? Properties { get; set; }
}
internal sealed class NwsStationProperties {
  [JsonPropertyName("stationIdentifier")] public string? StationIdentifier { get; set; }
}
```

Add `src/Radio.Infrastructure/Weather/Dtos/NwsObservationResponse.cs`:

```csharp
internal sealed class NwsObservationResponse {
  [JsonPropertyName("properties")] public NwsObservationProperties? Properties { get; set; }
}
internal sealed class NwsObservationProperties {
  [JsonPropertyName("timestamp")]       public DateTimeOffset? Timestamp { get; set; }
  [JsonPropertyName("textDescription")] public string? TextDescription { get; set; }
  [JsonPropertyName("icon")]            public string? Icon { get; set; }
  [JsonPropertyName("temperature")]     public NwsObservationValue? Temperature { get; set; }
}
internal sealed class NwsObservationValue {
  [JsonPropertyName("value")]    public double? Value { get; set; }
  [JsonPropertyName("unitCode")] public string? UnitCode { get; set; }
}
```

`NwsPointsResponse` (existing) needs a one-field extension to expose `observationStations`:

```csharp
internal sealed class NwsPointsProperties {
  // ...existing fields (Forecast, RelativeLocation)...
  [JsonPropertyName("observationStations")] public string? ObservationStations { get; set; }
}
```

The existing `GridInfo` record gains a sibling field:

```csharp
private sealed record GridInfo(
  string ForecastUrl,
  string City,
  string State,
  string ObservationStationsUrl);   // NEW
```

(`ObservationStationsUrl` is empty when NWS omits the field; the observation fetch treats empty as "no observation available" and returns null.)

---

## 5. UI changes (`SleepForecastPane.razor`)

### 5.1 Primary block redesign

The right column of Region 1 grows from two lines (condition + H/L) to three lines (condition + supplementary "Today: H°/L°" + …well, two lines if we collapse the H/L treatment). The 96 px LED numeral on the left changes its data source from `Today.HighF` to `Current.TempF`. The 96 px icon similarly switches from `Today.IconKey` to `Current.IconKey`.

#### State G — Current observation available (the new default)

```
                         ────────── 880 px ──────────
                        ┌───────────────────────────┐
                        │                           │
                        │   🌤            °  F·C    │   ← Region 1 (180 px tall)
                        │  ╔══╗   48  ┌──┐──────────│
                        │  ║  ║   °°  │  │ Partly   │
                        │  ╚══╝       └──┘ Cloudy   │   ← icon = CURRENT condition
                        │                  ─────────│   ← (no visible rule — whitespace gap 6 px)
                        │                  Today    │   ← supplementary label 12 px mono
                        │                  60°/42°  │   ← supplementary H/L 16 px LED
                        │                           │
                        │     Pittsboro · Sat 7:18 PM   ← sub-line unchanged (14 px mono)
                        │                           │
                        │ ┌────────┬────────┬────────┐ ← Region 2 unchanged
                        │ │   ☀    │   ☁    │   🌧   │
                        │ │  SAT   │  SUN   │  MON   │
                        │ │  60°   │  64°   │  58°   │
                        │ │  42°   │  48°   │  44°   │
                        │ └────────┴────────┴────────┘
                        │                           │
                        └───────────────────────────┘
                            tap anywhere to wake
```

Right column composition (top to bottom inside its 200 px slot):

```
   ┌── 200 px ──┐
   │ Partly     │   ← condition 24 px mono --text-medium (unchanged)
   │ Cloudy     │
   │            │
   │ ─ gap 8 px ─
   │            │
   │ Today      │   ← supplementary label 12 px mono uppercase --text-low
   │ 60°/42°    │   ← supplementary H/L 16 px LED dim amber (not full text-shadow recipe)
   └────────────┘
```

The right column total height grows from ~70 px (v2) to ~88 px. It stays vertically centered on the temp baseline; the Region 1 box height stays at 180 px (the existing 84 px of breathing room absorbs the extra 18 px).

#### State H — Current observation unavailable (`Forecast.Current == null`)

Falls back to v2 visual behavior verbatim:

```
                        │   🌤            °  F·C    │
                        │  ╔══╗   60  ┌──┐──────────│   ← icon + temp = today's FORECAST
                        │  ║  ║   °°  │  │ Partly   │
                        │  ╚══╝       └──┘ sunny    │
                        │                  77/66    │   ← v2 "today H/L" line REAPPEARS
                        │                           │   ← in its original v2 position
                        │                           │   ← (NO supplementary "Today:" label —
                        │     Pittsboro · Sat 7:18 PM    primary IS today's forecast)
```

When `Current == null`, the supplementary "Today: …" rows are **hidden entirely** (their slab would be empty / duplicative). The v2 28 px LED H/L line returns in its original position. Effectively the right column reverts to its v2 two-line structure (condition + H/L).

##### Sub-line qualifier for fallback

When `Current == null`, append a subtle qualifier to the sub-line so the operator can tell at a glance why the headline reads "today's high" instead of a current observation:

```
   Pittsboro · Sat 7:18 PM · forecast only
   ───────────────────────────────────────
                              ↑
                       added when Current == null
                       12 px mono italic --text-low
                       preceded by " · " separator
                       (NO icon — keeps emissive budget tight)
```

The 12 px italic is the same metrics as the "yesterday at HH:mm" qualifier the existing sub-line already uses for stale-data — Designer is keeping the affordance vocabulary consistent. **No new tokens.**

#### State G + IsStale (observation present but stale)

`Current.IsStale = true` follows the existing stale-pane convention:

- The whole pane gets `opacity: 0.7` via `.sleep-forecast-pane.is-stale` (already wired in v2).
- The `sync_problem` glyph appears at the start of the sub-line (already wired in v2).
- **No additional visual change.** The big number still shows the observed temperature — it's just dimmed and flagged.

The `WeatherForecast.IsStale` (forecast staleness) and `CurrentObservation.IsStale` (observation staleness) are now independent booleans. The pane goes to `.is-stale` when **either** is true. The sub-line qualifier hierarchy:

| Forecast stale | Current stale (or null) | Sub-line                                           |
|----------------|-------------------------|----------------------------------------------------|
| false          | false                   | `Pittsboro · Sat 7:18 PM`                          |
| false          | null                    | `Pittsboro · Sat 7:18 PM · forecast only`          |
| false          | stale                   | `⟳! Pittsboro · Sat 7:18 PM`                       |
| stale          | (any)                   | `⟳! Pittsboro · yesterday at 3:00 PM` (forecast takes precedence on the relative time) |

#### State I — Both forecast Days.Count < 2 AND Current present

The 1-day fallback path from v2 (Region 2 omitted) is unchanged. The primary block still uses `Current.TempF` / `Current.IconKey` per State G. The supplementary "Today: H°/L°" line still draws from `Today.HighF/LowF`. The sub-line and Region-2-omission rules are unchanged.

### 5.2 Typography for the new supplementary lines

| Element                                  | Font              | Size  | Weight | Color             | Other |
|------------------------------------------|-------------------|-------|--------|-------------------|-------|
| **Supplementary label `Today`**          | `var(--font-mono)`| 12 px | 400    | `var(--text-low)` | `text-transform: uppercase; letter-spacing: 0.12em; line-height: 1;` |
| **Supplementary H/L numerals**           | `var(--font-led)` | 16 px | 700    | `color-mix(in srgb, var(--signal-amber) 35%, #050507)` | `tabular-nums; letter-spacing: 0.02em; line-height: 1;` — **NO text-shadow** (16 px LED is small enough that the glow muddies the glyph). |
| **Supplementary `/` separator**          | `var(--font-led)` | 16 px | 400    | `var(--text-low)` | Same rule as the v2 28 px H/L `/` — separator is a tier quieter than the numerals. |
| **Sub-line qualifier ` · forecast only`**| `var(--font-mono)`| 12 px | 400 italic | `var(--text-low)` | Italic to differentiate from the relative-time stale qualifier ("yesterday at HH:mm") which is upright — gives the operator a visual cue that "this is a fallback state, not a stale state." |

All sizes are absolute pixels (fixed 1920×720 kiosk panel).

All other typography from v2 §4 is unchanged:

- Region 1 big temp: still 96 px LED, byte-identical to `.sleep-screen-clock` recipe. **Source data changes from `Today.HighF` to `Current.TempF` (or `Current.TempC` per unit setting); the visual recipe does not.**
- Region 1 condition text: still 24 px mono `--text-medium`. **Source data changes from `Today.ConditionShort` to `Current.ConditionShort`; size/treatment unchanged.**
- Region 1 big icon: still 96 px Material Symbol, dim amber, no text-shadow. **Source key changes from `Today.IconKey` to `Current.IconKey`; size/treatment unchanged.**
- Region 1 unit indicator: unchanged (still display-only F·C).
- Sub-line: unchanged.
- Region 2 forecast cards: **completely unchanged** (still 48 px icon + 14 px mono day label + 28 px LED high + 20 px LED low). Today's card in Region 2 continues to use `Today.IconKey` and `Today.HighF/LowF` (FORECAST data) — that's intentional: the forecast row shows the daily outlook, and today's card mirrors the supplementary "Today: H/L" line in the primary block. **Region 2 is a separate visual story from the primary's now-current block.**

The new supplementary "Today" / H/L stack sits **inside** `.sleep-forecast-primary-right`, **below** the existing condition slot, separated by an 8 px gap.

### 5.3 Color budget

| Element                                                | Emissive (dim amber)? | Notes |
|--------------------------------------------------------|-----------------------|-------|
| Region 1 big temp (NEW: from `Current.TempF`)          | Yes                   | Same 5 glyphs as v2, just rebound source data. |
| Region 1 big icon (NEW: from `Current.IconKey`)        | Yes                   | Same 1 silhouette as v2, just rebound source data. |
| Region 1 condition (NEW: from `Current.ConditionShort`)| No (`--text-medium`)  | Same area as v2, rebound source data. |
| Region 1 supplementary H/L (NEW)                       | Yes (small)           | ~5 glyphs at 16 px LED — adds ~30% emissive area to the right column vs. v2. |
| Region 1 supplementary "Today" label (NEW)             | No (`--text-low`)     | Passive. |
| Region 1 v2 H/L (28 px LED)                            | n/a — **REMOVED** in State G; reappears in State H |

Net emissive change in State G (the common case): the v2 `Today.HighF/LowF` at 28 px LED (~5 glyphs) is **replaced** by the supplementary H/L at 16 px LED (~5 glyphs). The glyph count is the same; the luminous area shrinks (16² vs. 28² ≈ 33% the area). State G has a **smaller** emissive footprint in the right column than v2.

State H (fallback) is visually identical to v2 → same emissive footprint as v2.

**Zero new tokens** — every color and font already exists. The supplementary H/L explicitly does **not** get the `text-shadow` glow recipe; 16 px LED with glow reads as smudge, not as instrument. This is the same rationale §5 of the v2 spec used for keeping glow off the 96 px icons.

### 5.4 Code-behind changes

Add to the existing `@code` block:

```csharp
private CurrentObservation? Current => Forecast.Current;
private bool HasCurrent => Current is not null;

private int PrimaryTempDisplay =>
  HasCurrent
    ? (IsCelsius ? Current!.TempC : Current.TempF)
    : (IsCelsius ? Today.HighC : Today.HighF);

private string PrimaryConditionDisplay =>
  HasCurrent ? Current!.ConditionShort : Today.ConditionShort;

private string PrimaryIconKey =>
  HasCurrent ? Current!.IconKey : Today.IconKey;
```

The existing `CurrentTempDisplay` computed property is renamed to `PrimaryTempDisplay` (semantic — "the numeral shown in the primary block") and gains the conditional. Existing references in the markup change accordingly.

The supplementary "Today" stack is rendered inside `.sleep-forecast-primary-right` with a guard:

```razor
<div class="sleep-forecast-primary-right">
  <div class="sleep-forecast-primary-condition">@PrimaryConditionDisplay</div>

  @if (HasCurrent)
  {
    @* Current observation is the headline — show today's forecast H/L as
       supplementary context. *@
    <div class="sleep-forecast-primary-supplementary">
      <div class="sleep-forecast-supplementary-label">Today</div>
      <div class="sleep-forecast-supplementary-hl">
        <span class="sleep-forecast-supplementary-high">@TodayHigh</span><span class="sleep-forecast-supplementary-hl-sep">°/</span><span class="sleep-forecast-supplementary-low">@TodayLow</span><span class="sleep-forecast-supplementary-deg">°</span>
      </div>
    </div>
  }
  else
  {
    @* Fallback: primary block IS today's forecast, so the v2 H/L line takes
       its original position (no supplementary label — would be redundant). *@
    <div class="sleep-forecast-primary-hl">
      <span class="sleep-forecast-primary-high">@TodayHigh</span><span class="sleep-forecast-primary-hl-sep">/</span><span class="sleep-forecast-primary-low">@TodayLow</span>
    </div>
  }
</div>
```

Sub-line code-behind: extend the existing `_subLineText` composition to append ` · forecast only` (lowercase, no degree, no icon) when `Current is null`:

```csharp
if (!HasCurrent)
{
  _subLineText += " · forecast only";   // wrap span in mono-italic --text-low in markup
}
```

Easier: render the qualifier as a separate span (`<span class="sleep-forecast-subline-fallback"> · forecast only</span>`) conditionally in the markup, so the italic + color rules don't have to be embedded inline.

### 5.5 `BuildAriaLabel` updates

When `Current is not null`, the SR string leads with the current observation followed by the forecast outlook:

> *"Currently 48 degrees Fahrenheit, partly cloudy in Pittsboro. Today's high 60, low 42. Saturday partly cloudy, high 60, low 42. Sunday cloudy, high 64, low 48. Monday rainy, high 58, low 44."*

When `Current is null`, the SR string keeps the v2 lead (current = today's forecast high):

> *"Currently 60 degrees Fahrenheit, partly sunny in Pittsboro. Today partly sunny, high 60, low 42. …"*

(The word "Currently" is preserved in both — to a screen-reader user, the distinction "observed vs. forecast" isn't actionable; what matters is "what should I expect right now if I walk outside.")

The `_lastAnnouncedSignature` field gains `Current?.TempF` and `Current?.ObservedAtUtc.Ticks` and `Current?.IsStale` so an updated observation retriggers the SR announcement.

### 5.6 Files changed (UI side)

| File                                                                  | Change                                                                                                       |
|-----------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------|
| `src/Radio.Web/Components/Shared/SleepForecastPane.razor`             | Markup + computed-property additions per §5.4–§5.5.                                                          |
| `src/Radio.Web/wwwroot/css/design-system.css` §P·6                    | Add rules for `.sleep-forecast-primary-supplementary`, `.sleep-forecast-supplementary-label`, `.sleep-forecast-supplementary-hl`, `.sleep-forecast-subline-fallback`. Existing v2 rules unchanged. |

**Out of scope:** `Sleep.razor` (no fetch-cadence changes), `WeatherApiService.cs` (auto-handles the new nullable field via `System.Text.Json`), `SystemConfigPage.razor` (no new config), the Region 2 card markup (untouched).

---

## 6. Failure modes

The whole feature degrades to v2 behavior on any observation-path failure. The decision matrix:

| Situation                                                  | Service returns                              | UI behavior |
|------------------------------------------------------------|----------------------------------------------|-------------|
| Observation chain succeeds, fresh                          | `WeatherForecast` with `Current` populated, `Current.IsStale = false` | State G (current = headline, today H/L = supplementary) |
| Observation chain succeeds, observation > 2 h old          | `WeatherForecast` with `Current` populated, `Current.IsStale = true`  | State G + pane opacity 0.7 + sync_problem on sub-line |
| Observation cache stale, refresh fails, cached < 24 h      | `WeatherForecast` with cached `Current`, `IsStale = true`             | State G + stale affordance |
| Observation cache stale, refresh fails, cached > 24 h      | `WeatherForecast` with `Current = null`                                | State H (fallback) + ` · forecast only` qualifier |
| Observation chain fails (no stations, station 404, sensor null) | `WeatherForecast` with `Current = null`                          | State H + ` · forecast only` qualifier |
| Forecast chain fails AND observation chain also fails      | `null` (unchanged from v2)                                            | Pane does not render (Sleep.razor guard) |
| Forecast chain succeeds, observation chain fails           | `WeatherForecast` with `Current = null`                                | State H — pane renders normally with today's H/L as headline |

The load-bearing contract: **the observation chain failing must NEVER cause the forecast chain to fail or the pane to disappear.** `TryFetchObservationAsync` is the firewall — it catches everything except `OperationCanceledException` and returns null.

---

## 7. Stale semantics summary

`CurrentObservation.IsStale = true` when **any** of:

1. `ObservedAtUtc < (now - 2 hours)`.
2. The observation fetch failed and we are serving a cached value within the 24 h stale-serve horizon.

`CurrentObservation = null` when:

1. No observation could be obtained (chain failure, sensor null, out-of-range guard tripped).
2. Cached value is older than 24 h stale-serve horizon AND refresh failed.

The 2-hour threshold is generous — METAR stations report at most hourly, so anything within 2 h is a single missed report's worth of staleness, which the user shouldn't worry about. Beyond 2 h, the data is two reports stale and the operator should know.

The 24 h stale-serve horizon mirrors the existing forecast cache horizon, for consistency. Both expire at the same upper bound under sustained NWS outage.

---

## 8. Configuration (unchanged)

**No new config keys.** The feature reuses the existing PR #415 keys:

- `Display:Weather:Enabled` — global on/off (already gates the whole forecast fetch)
- `Display:Weather:Zip` — used by the observation chain identically to the forecast chain
- `Display:Weather:TemperatureUnit` — `F` / `C` / `both` — drives `IsCelsius` / `IsBoth` exactly as it does for the forecast cards
- `Display:Weather:ContactEmail` — used in the NWS `User-Agent` header (same `HttpClient("nws")` instance)

When `Display:Weather:Enabled = false`, `GetForecastAsync` already returns null before any fetch — no observation calls are made. No change.

`TemperatureUnit = "both"` behavior: the primary block still falls back to Fahrenheit for the big numeral (per v2 §3 State F rationale — 96 px LED `"48°F · 9°C"` would blow the column budget). The supplementary "Today" line in `both` mode shows `60°F·42°F · 16°C·6°C` style or similar — Builder calls. **Decision:** in `both` mode, the supplementary line stays Fahrenheit-only to avoid stacking four numerals in a 16 px row. Reviewable if it looks crowded in implementation.

---

## 9. Tests required

### 9.1 Unit tests — `tests/Radio.Infrastructure.Tests/Weather/NwsWeatherServiceTests.cs`

Add tests (existing tests are not changed beyond updating `WeatherForecast` constructor calls to pass `Current: null` where the test doesn't care):

| Test name | What it asserts |
|-----------|-----------------|
| `GetForecastAsync_StationsChainSucceeds_PopulatesCurrent` | Canned points response with `observationStations` URL → canned stations response with one feature → canned observation response with 9.4°C / "Partly Cloudy" / partly_cloudy_night icon → `Current.TempF == 49`, `Current.TempC == 9`, `Current.ConditionShort == "Partly Cloudy"`, `Current.IconKey == "partly-cloudy-night"`. |
| `GetForecastAsync_NoObservationStationsUrl_LeavesCurrentNull` | Points response with `observationStations = null` → `Current is null`, forecast still populated. |
| `GetForecastAsync_StationsListEmpty_LeavesCurrentNull` | Stations response with `features = []` → `Current is null`. |
| `GetForecastAsync_ObservationReturns404_LeavesCurrentNull` | Stations returns one feature, observations/latest returns 404 → `Current is null`, forecast still populated. |
| `GetForecastAsync_ObservationSensorValueNull_LeavesCurrentNull` | Observation response with `properties.temperature.value = null` → `Current is null`. |
| `GetForecastAsync_ObservationOutOfRange_LeavesCurrentNull` | `temperature.value = 99` (°C) → guard trips, `Current is null`. |
| `GetForecastAsync_ObservationOlderThanTwoHours_MarksStale` | Observation timestamp is `now - 3h` → `Current.IsStale == true`. |
| `GetForecastAsync_ObservationCacheHit_DoesNotCallHandler` | Two back-to-back calls, second one's stations and observations endpoints not invoked. |
| `GetForecastAsync_StationsCacheStampede_OnlyOneFetch` | Five parallel cold-cache calls → handler sees one stations request (semaphore guard). Mirrors existing `_coordsLocks` test pattern. |
| `GetForecastAsync_ObservationStaleFetchFails_ReturnsStaleCurrent` | Inject observation cache entry with `ObservedAtUtc = now - 90min` and fetched 35 min ago (stale by both rules) → handler throws on refresh → `Current.IsStale == true`, value from cache. |
| `GetForecastAsync_ObservationStaleFetchFailsBeyondHorizon_ReturnsNullCurrent` | Cache entry > 24 h, refresh fails → `Current is null`, forecast still populated. |
| `GetForecastAsync_ForecastFails_ObservationSucceeds_StillReturnsNull` | Forecast endpoint throws → existing behavior: outer fetch fails. Confirms observation-side success doesn't rescue a broken forecast. |
| `GetForecastAsync_ParallelTiming_BothCallsFire` | Verify the parallel `Task.WhenAll` path — both forecast and observation handlers are invoked within ~50 ms of each other (loose timing check on a `DelayingHandler` test double). |

New fixture files under `tests/Radio.Infrastructure.Tests/Weather/fixtures/`:

- `points-with-observation-stations.json` — points response that includes `observationStations`
- `stations-pittsboro.json` — stations list response (3 features, ordered by distance)
- `observation-fresh.json` — observation response with valid sensor data
- `observation-no-temp.json` — observation response with `temperature.value = null`
- `observation-out-of-range.json` — observation response with `temperature.value = 99` (°C)

### 9.2 Unit test additions — `NwsIconMapperTests`

No changes — the icon mapper already handles the observation icon URL format (same URL shape as forecast period icons). Add **one** smoke test:

| Test name | What it asserts |
|-----------|-----------------|
| `MapToIconKey_NwsObservationIcons_ReturnsExpectedKeys` | Table-driven test with a dozen real observation icon URLs (`/icons/land/day/few?size=medium`, `/icons/land/night/rain`, etc.) → asserts each maps to the expected `IconKey`. |

### 9.3 bUnit tests — `tests/Radio.Web.Tests/Components/Shared/SleepForecastPaneCurrentObservationTests.cs`

(New test file — keeps the existing `SleepForecastPaneTests` clean.)

| Test name | What it asserts |
|-----------|-----------------|
| `Renders_State_G_When_Current_Is_Available` | Forecast with `Current` populated → big temp = `Current.TempF`, big icon = `Current.IconKey` Material Symbol, supplementary "Today" label visible, supplementary H/L = `Today.HighF/LowF`. |
| `Renders_State_H_When_Current_Is_Null` | Forecast with `Current = null` → big temp = `Today.HighF` (v2 behavior), supplementary section not rendered, sub-line ends in ` · forecast only`. |
| `Sub_Line_Has_Fallback_Qualifier_When_Current_Is_Null` | Markup contains the `<span class="sleep-forecast-subline-fallback">` element only when `Current is null`. |
| `Pane_Has_Stale_Class_When_Current_Is_Stale` | Forecast not stale, `Current.IsStale = true` → root has `is-stale` class, sync_problem glyph in sub-line. |
| `Pane_Has_Stale_Class_When_Forecast_Is_Stale_And_Current_Null` | Forecast stale, `Current = null` → root has `is-stale` class, sub-line shows relative time qualifier ("yesterday at HH:mm") AND ` · forecast only`. (Order: `⟳! Pittsboro · yesterday at 3:00 PM · forecast only`.) |
| `Aria_Label_Leads_With_Current_When_Available` | Aria label string starts with `"Currently 48 degrees Fahrenheit, partly cloudy in Pittsboro. Today's high 60, low 42."` |
| `Aria_Label_Leads_With_Today_When_Current_Null` | Aria label string starts with `"Currently 60 degrees Fahrenheit, partly sunny in Pittsboro. Today partly sunny, high 60, low 42."` (preserves v2 contract). |
| `Switches_Big_Number_To_Celsius_When_Unit_C_And_Current_Present` | `TemperatureUnit = "C"` with `Current.TempC = 9` → big numeral renders `9`, supplementary H/L renders `Today.HighC/LowC`. |
| `Big_Number_Falls_Back_To_Fahrenheit_In_Both_Mode_With_Current_Present` | `TemperatureUnit = "both"`, `Current.TempF = 48`, `Current.TempC = 9` → big numeral renders `48` (per v2 §3 State F rationale extended to current obs). Unit indicator shows both letters active. |
| `Renders_Without_Region_2_When_Single_Day_And_Current_Present` | Forecast with 1 day + `Current` populated → primary block renders State G, Region 2 omitted. Sub-line unchanged. |

### 9.4 Web service test — `WeatherApiServiceTests`

| Test name | What it asserts |
|-----------|-----------------|
| `DeserializesForecast_WithCurrentField` | Canned API response JSON with `current` object → `Forecast.Current` populated correctly. |
| `DeserializesForecast_WithCurrentNull` | Canned API response JSON with `current: null` → `Forecast.Current is null`. |

(Existing API serialization tests get updated to add the new field; no contract break to assert.)

### 9.5 Integration test — `tests/Radio.IntegrationTests/Weather/`

One additional `[Category("Integration")]` test:

| Test name | What it asserts |
|-----------|-----------------|
| `RealNwsCall_ReturnsForecast_WithCurrentObservation` | Real call to NWS with default ZIP (27312) → forecast has 3 days AND `Current` is non-null AND `Current.TempF` is in `[-40, 130]` AND `Current.IconKey` is not `"unknown"`. Excluded from default CI (matches existing convention). |

---

## 10. Out of scope

These are explicitly NOT in this iteration:

1. **Humidity display.** `properties.relativeHumidity.value` is available from the same observation; not surfaced. Future small addition: a third supplementary line `RH 73%` in the right column.
2. **Wind speed / direction.** `properties.windSpeed.value` + `properties.windDirection.value` are available; not surfaced. Future iteration could add a small wind glyph + value near the sub-line.
3. **Sunrise / sunset / moon phase.** Not in NWS; would require a second provider. Out of scope.
4. **Hourly forecast.** Same as ADR-022 §3 — not surfaced.
5. **Severe weather alerts.** Same as ADR-022 §3 — its own ADR if needed.
6. **Multi-station fallback.** If the closest station fails, the entire observation fetch is treated as a failure (Current = null). No walking the station list. (Documented in §4.4.)
7. **Per-station observation cache.** Cache key is per-ZIP, not per-station — if the closest station changes between fetches (rare, but possible if NWS reorders), the cached observation is invalidated naturally because the new station's `observations/latest` URL is different from the cached entry's source. We don't cache by station ID.
8. **Observation refresh interval config knob.** `Display:Weather:ObservationRefreshIntervalMinutes` is not added. Hardcoded 30 min fresh-TTL per §4.3 rationale.
9. **Backend hosted service to warm the observation cache.** Same rationale as ADR-022 §2.4 — lazy refresh on demand wins.
10. **Animated weather icons.** Pure static rendering per v2.
11. **"Feels like" temperature** (`apparentTemperature`). Available in some observation responses; not surfaced. Single number is the requested headline.
12. **`current.observedAt` as a visible affordance** (e.g. "observed 14 min ago"). Not surfaced — the sub-line already carries one timestamp (the forecast's), adding a second risks visual clutter. The staleness affordance (opacity + sync_problem) already communicates "this is old data" without a numeric duration.
13. **International (non-US) locations.** Same as ADR-022 §3 — out of scope.
14. **Display unit handling for the supplementary H/L in `both` mode** beyond "stays Fahrenheit-only." Tunable later if it looks crowded.

---

## Hand-off summary for Planner / Builder

Add an optional nullable sibling `CurrentObservation? Current` to `WeatherForecast` (rounded F/C ints, condition short, icon key via existing `NwsIconMapper`, observed timestamp, `IsStale`). Extend `NwsWeatherService` with two private fetch methods (`GetOrFillClosestStationAsync`, `GetOrFillObservationAsync`) running off the existing `GridInfo`'s new `ObservationStationsUrl` field, fired in parallel with the existing forecast fetch via `Task.WhenAll`. Two new per-ZIP cache keys: `weather:zip:{zip}:stations` (process lifetime, semaphore-guarded) and `weather:zip:{zip}:observation` (30 min fresh + 24 h stale-serve, no semaphore). All observation-side failures are firewalled — a broken observation chain produces `Current = null`, never breaks the forecast chain or the pane. Three new internal DTOs (`NwsStationsResponse`, `NwsObservationResponse`, plus one field addition to `NwsPointsProperties`). Sanity guards: null sensor value and temp outside `[-60, 60]` °C both mean "no observation." Pick the closest station only (no multi-station fallback).

UI side: `SleepForecastPane.razor` rebinds the 96 px LED big number, the 96 px icon, and the 24 px condition text to `Current.*` when present; falls back to `Today.HighF` / `Today.IconKey` / `Today.ConditionShort` when `Current is null` (State H = v2 behavior). The v2 28 px LED "today H/L" line is replaced (in State G) by a smaller supplementary "Today" label (12 px mono uppercase `--text-low`) + 16 px LED dim-amber H/L (no text-shadow) below the condition text. When `Current is null`, the v2 H/L returns in its original position AND the sub-line gains a ` · forecast only` qualifier (12 px mono italic `--text-low`). Stale handling: `Current.IsStale = true` follows the existing `.is-stale` pane convention (opacity 0.7 + sync_problem). Region 2 (3-card forecast row) is completely unchanged — it continues to use forecast `Days[]` data. No new tokens. No new config keys. No new endpoints. Configuration UI unchanged. Tests required: 13 new service tests + 1 icon mapper test + 10 bUnit tests + 2 web service tests + 1 integration test.

Files changed (5 modified, 2 added):

- **Modified:** `src/Radio.Core/Models/WeatherForecast.cs` (add record + field)
- **Modified:** `src/Radio.Infrastructure/Weather/NwsWeatherService.cs` (parallel observation fetch, new caches, new private methods)
- **Modified:** `src/Radio.Infrastructure/Weather/Dtos/NwsPointsResponse.cs` (one new field on `NwsPointsProperties`)
- **Modified:** `src/Radio.Web/Components/Shared/SleepForecastPane.razor` (markup + computed properties + aria label)
- **Modified:** `src/Radio.Web/wwwroot/css/design-system.css` §P·6 (4 new rules for supplementary stack + fallback qualifier)
- **Added:** `src/Radio.Infrastructure/Weather/Dtos/NwsStationsResponse.cs`
- **Added:** `src/Radio.Infrastructure/Weather/Dtos/NwsObservationResponse.cs`

Plus the test fixtures and test files enumerated in §9.
