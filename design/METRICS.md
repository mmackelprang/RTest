# Metrics System

The Radio Console metrics system provides end-to-end observability: backend collection with 3-tier SQLite rollup, a REST API for querying, and a Blazor dashboard with real-time charts.

## Architecture Overview

```
Backend (Radio.API)                          Frontend (Radio.Web)
┌─────────────────────────────┐              ┌──────────────────────────────┐
│ IMetricsCollector            │              │ MetricsDashboardPage.razor   │
│   .Increment(key, value)     │  REST API   │   Hero stat cards            │
│   .Gauge(key, value)         │ ◄──────────►│   Canvas time-series chart   │
│                              │             │   Collapsible categories     │
│ SQLite 3-tier rollup:        │             │   Auto-refresh (10s)         │
│   Minute → 2h retention      │             │                              │
│   Hour   → 48h retention     │             │ MetricsApiService.cs         │
│   Day    → 365d retention    │             │   (API client)               │
└─────────────────────────────┘              └──────────────────────────────┘
```

**Key files:**

| Purpose | File |
|---------|------|
| Dashboard UI | `src/Radio.Web/Components/Pages/MetricsDashboardPage.razor` |
| Chart renderer | `src/Radio.Web/wwwroot/js/metricsChart.js` |
| Dashboard CSS | `src/Radio.Web/wwwroot/css/design-system.css` (§18) |
| API client | `src/Radio.Web/Services/ApiClients/MetricsApiService.cs` |
| Web DTOs | `src/Radio.Web/Models/ApiModels.cs` (`MetricHistoryDto`, `MetricAggregateDto`) |
| API controller | `src/Radio.API/Controllers/MetricsController.cs` |
| Core models | `src/Radio.Core/Metrics/` |
| SQLite storage | `src/Radio.Infrastructure/Metrics/` |
| Configuration | `appsettings.json` → `Metrics` section |

---

## Dashboard Layout

The dashboard (`/metrics`) is a full-width single-panel layout for a 1920x720 kiosk viewport:

```
┌──────────────────────────────────────────────────────────────────────────┐
│ METRICS    [5m] [1h] [24h] [7d] [30d]                      [↻ Refresh] │
├──────────────────────────────────────────────────────────────────────────┤
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐                    │
│  │ CPU Temp │  │ Memory  │  │ Buffer  │  │ Underrun│  ← Hero stat cards │
│  │  52.3°C  │  │ 68.2 MB │  │ 94.1%   │  │  0      │                    │
│  │ ~~spark~~│  │ ~~spark~~│  │ ~~spark~~│  │ ~~spark~~│                    │
│  └─────────┘  └─────────┘  └─────────┘  └─────────┘                    │
├──────────────────────────────────────────────────────────────────────────┤
│ ┌────────────────────────────────────────────────────────────────────┐  │
│ │                    Canvas Time-Series Chart                        │  │
│ │  Area fill, min/max band, hover tooltip, threshold lines           │  │
│ │  Stats bar: Count | Avg | Min | Max | StdDev                       │  │
│ └────────────────────────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────────────────────────┤
│ ▾ Audio (12)         ← Collapsible category sections                    │
│   audio.songs_played_total    42     ~~spark~~  ▲                       │
│   audio.playback_errors        0     ~~spark~~  —                       │
│ ▸ System (8)                                                            │
│ ▸ Streaming (5)      ← Click any row → loads into chart above          │
└──────────────────────────────────────────────────────────────────────────┘
```

### Time Ranges and Resolution

| Button | Window | Resolution | Data Source |
|--------|--------|------------|-------------|
| **5m** | Last 5 minutes | Minute | Minute tier (2h retention) |
| **1h** | Last 1 hour | Minute | Minute tier |
| **24h** | Last 24 hours | Hour | Hour tier (48h retention) |
| **7d** | Last 7 days | Hour | Hour tier |
| **30d** | Last 30 days | Day | Day tier (365d retention) |

The selected time range is persisted in the `ui.metrics` configuration key and restored on page load.

---

## Hero Stat Cards

Hero cards are the large pinned cards at the top of the dashboard. They provide at-a-glance status for the most important metrics.

### How Hero Cards Are Selected

Hero cards are auto-selected by matching metric keys against a pattern list (line 144 of `MetricsDashboardPage.razor`):

```csharp
private static readonly string[] _heroPatterns =
  ["cpu", "memory", "buffer", "underrun", "error", "active"];
```

The dashboard scans all available metric keys and picks those containing any of these substrings. Results are ordered by the pattern array order (cpu first, active last) and capped at **6 cards**.

**To change which metrics appear as hero cards**, edit the `_heroPatterns` array. Each entry is a case-insensitive substring match against the metric key.

Examples:
- `"cpu"` matches `system.cpu_temp_celsius` and `system.cpu_usage_percent`
- `"buffer"` matches `audio.buffer_fill_percent`
- `"error"` matches `audio.playback_errors`, `api.errors_total`, etc.

**To pin a specific metric** that doesn't match existing patterns, add a unique fragment of its key. For example, to pin `tts.latency_ms`, add `"tts_latency"` or just `"latency"` (but note that broad patterns may match more metrics than intended).

**To remove a hero card**, remove the matching pattern from the array.

**To reorder hero cards**, reorder the patterns — the first pattern appears leftmost.

### Threshold Coloring

Each hero card has a colored left border indicating health status:
- **Green** (`--signal-green`): Normal range
- **Amber** (`--signal-amber`): Warning threshold crossed
- **Red** (`--signal-red`): Critical threshold crossed
- **Accent blue** (`--accent-primary`): No threshold defined for this metric

Thresholds are defined in a static dictionary (line 148):

```csharp
private static readonly Dictionary<string, (double warn, double crit, bool invertAbove)> _thresholds = new()
{
  ["cpu_temp"]       = (70, 85, true),     // ≥70 amber, ≥85 red
  ["cpu_usage"]      = (80, 95, true),     // ≥80 amber, ≥95 red
  ["memory_percent"] = (80, 95, true),     // ≥80 amber, ≥95 red
  ["buffer_fill"]    = (50, 20, false),    // ≤50 amber, ≤20 red (inverted)
  ["buffer_percent"] = (50, 20, false),    // ≤50 amber, ≤20 red (inverted)
  ["underrun"]       = (1, 10, true),      // ≥1 amber, ≥10 red
  ["error"]          = (1, 10, true),      // ≥1 amber, ≥10 red
};
```

The `invertAbove` flag controls the comparison direction:
- **`true`** (normal): Higher values are worse. Value ≥ warn → amber, value ≥ crit → red.
- **`false`** (inverted): Lower values are worse. Value ≤ warn → amber, value ≤ crit → red. Used for "fill" metrics where dropping below a threshold is bad.

**To add a threshold for a new metric**, add an entry to the `_thresholds` dictionary. The key is a substring match against the metric key (same as hero patterns). If a metric matches multiple threshold entries, the first match wins.

### Sparklines

Each hero card includes a 200x50px SVG sparkline showing the trend over the selected time range. Sparklines are rendered server-side as inline SVG with area fill. The sparkline color matches the threshold color.

---

## Time-Series Chart

The main chart panel uses a `<canvas>` element rendered via JavaScript interop (`metricsChart.js`). It follows the same ES module pattern as the visualizer.

### Features
- **Area fill**: Translucent gradient below the data line
- **Min/Max band**: Shaded region between min and max values per data bucket
- **Threshold lines**: Horizontal dashed lines for warning/critical thresholds
- **Hover tooltip**: Crosshair + tooltip showing timestamp and value on mouse/touch
- **Gridlines**: Horizontal gridlines with auto-scaled Y-axis labels
- **X-axis timestamps**: Evenly spaced time labels formatted as HH:mm

### Selecting a Metric
- Click any **hero card** to display its data in the chart
- Click any **metric row** in the category sections below
- The first hero metric is auto-selected on page load
- The stats bar below the chart shows: Count, Avg, Min, Max, StdDev

### Chart Colors
The chart line and fill color reflects the selected metric's threshold status:
- `#5CD4E8` (accent blue) — normal / no threshold
- `#F0A830` (amber) — warning
- `#F87171` (red) — critical

---

## Category Sections

Metrics are grouped into collapsible categories based on the key prefix (everything before the first `.`). For example:
- `system.cpu_temp_celsius` → **System** category
- `audio.songs_played_total` → **Audio** category
- `tts.latency_ms` → **Tts** category

Each category header shows the metric count. Inside, each row displays:
- **Label**: Metric key with prefix stripped, underscores replaced with spaces, title-cased
- **Current value**: Formatted with automatic unit detection
- **Sparkline**: 150x20px inline SVG
- **Trend indicator**: ▲ (up >5%), ▼ (down >5%), or — (flat), comparing the average of the 3 most recent points to the 3 oldest

All categories are expanded by default. Click a category header to collapse/expand.

---

## Adding a New Metric

### Step 1: Record the metric in the backend

Inject `IMetricsCollector` and call `Increment()` (counter) or `Gauge()` (point-in-time):

```csharp
// Counter: accumulates over time (songs played, errors, bytes sent)
_metricsCollector.Increment("audio.songs_played_total", 1.0);

// Gauge: instantaneous value (temperature, percentage, count of active items)
_metricsCollector.Gauge("system.cpu_temp_celsius", 52.3);

// With tags (stored in metric history, available in hover tooltips)
_metricsCollector.Increment("api.requests_total", 1.0,
    new Dictionary<string, string> { ["endpoint"] = "/api/audio" });
```

### Step 2: Key naming convention

Use dot-separated names: `{category}.{metric_name}`

- The **category** (prefix before first `.`) determines the collapsible section in the dashboard
- Use **snake_case** for the metric name
- Include the **unit suffix** when applicable: `_ms`, `_seconds`, `_percent`, `_mb`, `_celsius`

The unit suffix drives automatic formatting in the dashboard — see [Value Formatting](#value-formatting) below.

### Step 3: The metric appears automatically

No dashboard code changes are required for basic display. Once the backend records a metric:
1. It appears in the API's `/api/metrics/keys` response
2. The dashboard fetches snapshots for all available keys
3. It shows up in the appropriate category section with auto-formatted values

### Step 4 (optional): Promote to hero card

To make the metric appear as a hero stat card, add a matching pattern to `_heroPatterns`:

```csharp
private static readonly string[] _heroPatterns =
  ["cpu", "memory", "buffer", "underrun", "error", "active", "your_new_pattern"];
```

### Step 5 (optional): Add threshold coloring

To add red/amber/green coloring, add an entry to `_thresholds`:

```csharp
["your_metric_fragment"] = (warningValue, criticalValue, invertAbove: true),
```

Set `invertAbove: true` if higher values are worse (temperature, error counts), or `false` if lower values are worse (buffer fill percentage).

---

## Value Formatting

The dashboard auto-formats values based on patterns in the metric key. This is handled by `FormatMetricValue()` (line 563):

| Key contains | Format | Example |
|-------------|--------|---------|
| `memory`, `bytes`, `_mb`, `file_size` | Bytes (KB/MB/GB) | `168.234 MB` |
| `percent`, `_ratio` | Percentage | `94.1%` |
| `cpu` + `usage` | Percentage | `12.3%` |
| `seconds`, `duration`, `latency`, `_time` | Duration (ms or s) | `42.1 ms` |
| `_ms`, `latency_ms`, `duration_ms` | Milliseconds | `42.1 ms` |
| (everything else) | Scaled number | `42`, `1.234 K`, `2.345 M` |

### Gauge vs Counter Classification

The dashboard classifies metrics to determine how to compute the display value from history (line 296):

**Gauges** (show latest value): Keys containing `percent`, `ratio`, `rate`, `_size`, `fill`, `active`

**Counters** (show accumulated total): Keys containing `count`, `total`, `bytes`, `sent`, `received`, `played`, `requests`, `errors`, `dropped`, `underruns`, `skipped`, `chunks`, `connected`, `disconnected`, `reconnect`, `failures`, `queued`

If a metric doesn't match either pattern, it's treated as a gauge (latest value shown).

**Tip**: When naming new metrics, include one of these keywords so the dashboard classifies it correctly. For example, name an error counter `audio.decode_errors` (contains "errors") rather than `audio.decode_problems`.

---

## Auto-Refresh

The dashboard refreshes automatically every **10 seconds**. On each tick:
1. Fetches fresh snapshots for all metrics
2. Re-fetches sparkline history for up to 20 metrics
3. Recomputes gauge/counter display values
4. Re-renders the canvas chart if a metric is selected

The refresh timer is disposed on page navigation (via `IAsyncDisposable`).

---

## Configuration

Metrics are configured in `appsettings.json` under the `Metrics` section:

```json
{
  "Metrics": {
    "Enabled": true,
    "FlushIntervalSeconds": 60,
    "DatabasePath": "./data/metrics/metrics.db",
    "RetentionMinuteData": 120,
    "RetentionHourData": 48,
    "RetentionDayData": 365,
    "RollupIntervalMinutes": 60
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable/disable metrics collection |
| `FlushIntervalSeconds` | `60` | How often buffered metrics are flushed to SQLite |
| `DatabasePath` | `./data/metrics/metrics.db` | SQLite database location |
| `RetentionMinuteData` | `120` | Minutes of minute-resolution data to keep |
| `RetentionHourData` | `48` | Hours of hour-resolution data to keep |
| `RetentionDayData` | `365` | Days of day-resolution data to keep |
| `RollupIntervalMinutes` | `60` | How often minute data is rolled up to hour/day tiers |

---

## API Endpoints

All metrics API endpoints are under `/api/metrics`:

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/metrics/keys` | List all metric key names |
| `GET` | `/api/metrics/snapshots?keys=a,b,c` | Current values for specified keys |
| `GET` | `/api/metrics/history?key=X&start=...&end=...&resolution=Minute` | Time-series history |
| `GET` | `/api/metrics/aggregate?key=X&start=...&end=...&resolution=Minute` | Aggregate stats (returns raw `double`) |
| `POST` | `/api/metrics/event` | Record a UI event metric from the frontend |

**Note**: The aggregate endpoint returns a raw `double`, not a `MetricAggregateDto`. The dashboard computes aggregate stats (count, avg, min, max, stdDev) client-side from history data.

---

## Proposed Future Expansion

*Analysis Date: 2025-12-05*

This section outlines a comprehensive metrics expansion plan to enhance observability, enable optimization, and support sustainability engineering for the Radio Console application.

### Current Metrics Coverage Summary

| Area | Current Coverage | Gap Assessment |
|------|------------------|----------------|
| **System Health** | ✅ Complete | Memory, CPU temp, disk, DB size |
| **Audio Playback** | 🟡 Partial | Base tracking exists, needs expansion |
| **TTS Services** | 🟡 Partial | Character count, latency, cache tracking |
| **API Layer** | 🟡 Partial | Request counting exists |
| **Real-time (SignalR)** | ❌ Missing | No connection/broadcast metrics |
| **External Services** | ❌ Missing | Spotify API calls untracked |
| **Audio Streaming** | ❌ Missing | HTTP stream clients untracked |
| **Configuration** | ❌ Missing | No backup/restore metrics |

---

### Priority 1: Audio Engine & Playback Metrics (High Impact)

#### Current Implementation
The base audio source class (`PrimaryAudioSourceBase`) has infrastructure for:
- `audio.songs_played_total` (Counter) - tracked on completion
- `audio.songs_skipped` (Counter) - tracked on skip
- `audio.playback_errors` (Counter) - tracked on exception

#### Recommended Additions

| Metric | Type | Location | Purpose |
|--------|------|----------|---------|
| `audio.playback_duration_seconds` | Counter | `PrimaryAudioSourceBase` | Total listening time for sustainability/usage analysis |
| `audio.engine_state_changes` | Counter | `SoundFlowAudioEngine` | Track init/start/stop transitions |
| `audio.buffer_underruns` | Counter | `TappedOutputStream` | Audio quality issues |
| `audio.source_switches` | Counter | `MasterMixer` | How often users change sources |
| `audio.ducking_events` | Counter | `DuckingService` | Event overlay frequency |
| `audio.ducking_duration_ms` | Gauge | `DuckingService` | Average ducking time |

#### Implementation Example

```csharp
// In SoundFlowAudioEngine.cs
public async Task StartAsync(CancellationToken cancellationToken = default)
{
    // ... existing code ...
    _metricsCollector?.Increment("audio.engine_state_changes", 1.0, 
        new Dictionary<string, string> { ["transition"] = "start" });
}
```

**Sustainability Impact**: Understanding playback patterns helps identify power consumption hotspots on Raspberry Pi.

---

### Priority 2: API & HTTP Streaming Metrics (High Impact)

#### Current Implementation
`ApiMetricsMiddleware` exists and tracks `api.requests_total`, but lacks granularity.

#### Recommended Additions

| Metric | Type | Location | Purpose |
|--------|------|----------|---------|
| `api.request_latency_ms` | Gauge | `ApiMetricsMiddleware` | Response time tracking |
| `api.errors_total` | Counter | `ApiMetricsMiddleware` | Error rate monitoring |
| `api.requests_by_endpoint` | Counter | `ApiMetricsMiddleware` | Identify hot endpoints |
| `stream.http_clients_connected` | Gauge | `AudioStreamMiddleware` | Concurrent stream consumers |
| `stream.bytes_sent_total` | Counter | `AudioStreamMiddleware` | Bandwidth utilization |
| `stream.client_connect_duration_seconds` | Gauge | `AudioStreamMiddleware` | Session length analysis |

#### Implementation Example

```csharp
// Enhanced ApiMetricsMiddleware.cs
public async Task InvokeAsync(HttpContext context)
{
    var stopwatch = Stopwatch.StartNew();
    var endpoint = context.Request.Path.Value ?? "unknown";
    
    try
    {
        await _next(context);
    }
    finally
    {
        stopwatch.Stop();
        var tags = new Dictionary<string, string>
        {
            ["endpoint"] = endpoint,
            ["method"] = context.Request.Method,
            ["status"] = context.Response.StatusCode.ToString()
        };
        
        _metricsCollector?.Increment("api.requests_total", 1.0, tags);
        _metricsCollector?.Gauge("api.request_latency_ms", stopwatch.ElapsedMilliseconds, tags);
        
        if (context.Response.StatusCode >= 400)
        {
            _metricsCollector?.Increment("api.errors_total", 1.0, tags);
        }
    }
}
```

**Sustainability Impact**: Identifying slow endpoints helps optimize CPU usage and reduce energy consumption.

---

### Priority 3: SignalR & Real-time Metrics (Medium-High Impact)

#### Current State
The `AudioVisualizationHub` broadcasts at 30fps to subscribed clients but has **no observability**.

#### Recommended Additions

| Metric | Type | Location | Purpose |
|--------|------|----------|---------|
| `websocket.connected_clients` | Gauge | `AudioVisualizationHub` | Active connection count |
| `websocket.subscriptions_by_type` | Gauge | `AudioVisualizationHub` | Spectrum/Level/Waveform counts |
| `websocket.broadcast_latency_ms` | Gauge | `VisualizationBroadcastService` | Broadcasting performance |
| `websocket.messages_sent_total` | Counter | `VisualizationBroadcastService` | Message volume |
| `websocket.connection_duration_seconds` | Gauge | `AudioVisualizationHub` | Session analysis |

#### Implementation Example

```csharp
// In AudioVisualizationHub.cs
private static int _connectedClients = 0;

public override async Task OnConnectedAsync()
{
    Interlocked.Increment(ref _connectedClients);
    _metricsCollector?.Gauge("websocket.connected_clients", _connectedClients);
    await base.OnConnectedAsync();
}

public override async Task OnDisconnectedAsync(Exception? exception)
{
    Interlocked.Decrement(ref _connectedClients);
    _metricsCollector?.Gauge("websocket.connected_clients", _connectedClients);
    await base.OnDisconnectedAsync(exception);
}
```

**Sustainability Impact**: High client counts directly impact CPU/memory; tracking enables scaling decisions.

---

### Priority 4: External Service Metrics (Medium Impact)

#### Spotify Integration
The `SpotifyAudioSource` and `SpotifyAuthService` make external API calls with **no tracking**.

#### Recommended Additions

| Metric | Type | Location | Purpose |
|--------|------|----------|---------|
| `spotify.api_calls_total` | Counter | `SpotifyAudioSource` | API usage tracking |
| `spotify.api_latency_ms` | Gauge | `SpotifyAudioSource` | External service performance |
| `spotify.api_errors_total` | Counter | `SpotifyAudioSource` | Reliability monitoring |
| `spotify.token_refresh_count` | Counter | `SpotifyAuthService` | Auth health |
| `spotify.auth_failures` | Counter | `SpotifyAuthService` | Auth issues |

#### TTS External Calls
Currently tracks characters and cache hits, but **missing latency per provider**.

| Metric | Type | Location | Purpose |
|--------|------|----------|---------|
| `tts.provider_latency_ms` | Gauge | `TTSFactory` | Tag by provider (eSpeak/Google/Azure) |
| `tts.provider_errors` | Counter | `TTSFactory` | Provider reliability |
| `tts.cost_estimate_cents` | Counter | `TTSFactory` | Cloud cost tracking (Google/Azure) |

**Sustainability Impact**: External API calls have network/latency costs; optimizing reduces wait times and power.

---

### Priority 5: Visualization & FFT Metrics (Medium Impact)

#### Current State
The `VisualizerService` performs FFT analysis but **processing cost is invisible**.

#### Recommended Additions

| Metric | Type | Location | Purpose |
|--------|------|----------|---------|
| `visualizer.fft_processing_ms` | Gauge | `SpectrumAnalyzer` | FFT computation time |
| `visualizer.samples_processed_total` | Counter | `VisualizerService` | Processing volume |
| `visualizer.is_active` | Gauge | `VisualizerService` | 1 when active, 0 when idle |
| `visualizer.peak_events` | Counter | `LevelMeter` | Clipping/peak occurrences |

**Sustainability Impact**: FFT is CPU-intensive; tracking enables power-saving when visualization is unused.

---

### Priority 6: Configuration & Database Metrics (Lower Impact)

#### Recommended Additions

| Metric | Type | Location | Purpose |
|--------|------|----------|---------|
| `config.backup_count` | Counter | `UnifiedDatabaseBackupService` | Backup frequency |
| `config.backup_size_mb` | Gauge | `UnifiedDatabaseBackupService` | Storage utilization |
| `config.restore_count` | Counter | `UnifiedDatabaseBackupService` | Recovery events |
| `db.query_duration_ms` | Gauge | `SqliteMetricsRepository` | Database performance |
| `metrics.rollup_duration_ms` | Gauge | `MetricsRollupService` | Aggregation performance |
| `metrics.pruned_rows` | Counter | `MetricsRollupService` | Data lifecycle |

---

### Implementation Roadmap

#### Phase 1: Foundation (Week 1)
- [ ] Enhance `ApiMetricsMiddleware` with latency and error tracking
- [ ] Add SignalR connection metrics to `AudioVisualizationHub`
- [ ] Add HTTP stream client tracking to `AudioStreamMiddleware`

#### Phase 2: Audio Core (Week 2)
- [ ] Add playback duration tracking to `PrimaryAudioSourceBase`
- [ ] Add engine state transition metrics to `SoundFlowAudioEngine`
- [ ] Add ducking metrics to `DuckingService`

#### Phase 3: External Services (Week 3)
- [ ] Add Spotify API call metrics to `SpotifyAudioSource`
- [ ] Enhance TTS metrics with per-provider latency
- [ ] Add authentication metrics to `SpotifyAuthService`

#### Phase 4: Advanced (Week 4)
- [ ] Add visualizer performance metrics
- [ ] Add configuration/backup metrics
- [ ] Add database operation metrics

---

### Dashboard Recommendations

#### System Health Dashboard
```
┌─────────────────────────────────────────────────────────────┐
│ CPU Temp │ Memory MB │ Disk % │ DB Size │ API Latency P99  │
├─────────────────────────────────────────────────────────────┤
│ Chart: System metrics over 24h                              │
│ Chart: API request rate by endpoint                         │
│ Chart: Error rate %                                         │
└─────────────────────────────────────────────────────────────┘
```

#### Audio Performance Dashboard
```
┌─────────────────────────────────────────────────────────────┐
│ Songs Played │ Playback Hours │ Skip Rate │ Error Rate      │
├─────────────────────────────────────────────────────────────┤
│ Chart: Playback by source (Spotify/Radio/Vinyl/File)        │
│ Chart: Ducking events timeline                              │
│ Chart: TTS characters by provider                           │
└─────────────────────────────────────────────────────────────┘
```

#### Real-time Connections Dashboard
```
┌─────────────────────────────────────────────────────────────┐
│ WebSocket Clients │ HTTP Streams │ Broadcast Rate           │
├─────────────────────────────────────────────────────────────┤
│ Chart: Connected clients over time                          │
│ Chart: Bytes streamed per hour                              │
│ Chart: Subscription types (Spectrum/Level/Waveform)         │
└─────────────────────────────────────────────────────────────┘
```

---

### Sustainability Considerations

#### Power Optimization Opportunities
1. **Visualizer Auto-disable**: Disable FFT when no clients subscribed
2. **Adaptive Broadcast Rate**: Reduce from 30fps when on battery/high temp
3. **TTS Caching**: Increase cache hits to reduce cloud API calls
4. **Stream Buffering**: Optimize buffer sizes for power efficiency

#### Metric-Driven Decisions

| Metric Threshold | Action |
|-----------------|--------|
| `system.cpu_temp_celsius` > 75 | Reduce visualization rate |
| `websocket.connected_clients` == 0 | Disable visualizer |
| `stream.http_clients_connected` == 0 | Reduce audio tap buffer |
| `tts.cache_hits` / total < 0.5 | Increase cache TTL |

---

### Complete Proposed Metric Registry

#### System Metrics (Existing)
- `system.disk_usage_percent` (Gauge) ✅
- `system.cpu_temp_celsius` (Gauge) ✅
- `system.memory_usage_mb` (Gauge) ✅
- `db.file_size_mb` (Gauge) ✅

#### Audio Metrics (Partial + Proposed)
- `audio.songs_played_total` (Counter) ✅
- `audio.songs_skipped` (Counter) ✅
- `audio.playback_errors` (Counter) ✅
- `audio.playback_duration_seconds` (Counter) 🆕
- `audio.engine_state_changes` (Counter) 🆕
- `audio.buffer_underruns` (Counter) 🆕
- `audio.source_switches` (Counter) 🆕
- `audio.ducking_events` (Counter) 🆕
- `audio.ducking_duration_ms` (Gauge) 🆕

#### TTS Metrics (Partial + Proposed)
- `tts.requests_total` (Counter) ✅
- `tts.latency_ms` (Gauge) ✅
- `tts.characters_processed` (Counter) ✅
- `tts.cache_hits` (Counter) ✅
- `tts.cache_misses` (Counter) ✅
- `tts.provider_latency_ms` (Gauge) 🆕
- `tts.provider_errors` (Counter) 🆕
- `tts.cost_estimate_cents` (Counter) 🆕

#### API Metrics (Partial + Proposed)
- `api.requests_total` (Counter) ✅
- `api.request_latency_ms` (Gauge) 🆕
- `api.errors_total` (Counter) 🆕
- `api.requests_by_endpoint` (Counter) 🆕

#### Streaming Metrics (All Proposed)
- `stream.http_clients_connected` (Gauge) 🆕
- `stream.bytes_sent_total` (Counter) 🆕
- `stream.client_connect_duration_seconds` (Gauge) 🆕

#### WebSocket Metrics (All Proposed)
- `websocket.connected_clients` (Gauge) 🆕
- `websocket.subscriptions_by_type` (Gauge) 🆕
- `websocket.broadcast_latency_ms` (Gauge) 🆕
- `websocket.messages_sent_total` (Counter) 🆕
- `websocket.connection_duration_seconds` (Gauge) 🆕

#### Spotify Metrics (All Proposed)
- `spotify.api_calls_total` (Counter) 🆕
- `spotify.api_latency_ms` (Gauge) 🆕
- `spotify.api_errors_total` (Counter) 🆕
- `spotify.token_refresh_count` (Counter) 🆕
- `spotify.auth_failures` (Counter) 🆕

#### Visualizer Metrics (All Proposed)
- `visualizer.fft_processing_ms` (Gauge) 🆕
- `visualizer.samples_processed_total` (Counter) 🆕
- `visualizer.is_active` (Gauge) 🆕
- `visualizer.peak_events` (Counter) 🆕

#### Configuration Metrics (All Proposed)
- `config.backup_count` (Counter) 🆕
- `config.backup_size_mb` (Gauge) 🆕
- `config.restore_count` (Counter) 🆕
- `db.query_duration_ms` (Gauge) 🆕
- `metrics.rollup_duration_ms` (Gauge) 🆕
- `metrics.pruned_rows` (Counter) 🆕

---

