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

