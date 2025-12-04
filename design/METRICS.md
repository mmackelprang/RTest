# Metrics Infrastructure Design

## Overview
This document outlines the design for a lightweight, portable, and performant metrics system for the Radio project. The system is designed to run on limited hardware (Raspberry Pi) using SQLite, minimizing disk I/O while providing historical data aggregation.

## Architectural Goals
1.  **Performance:** Minimal impact on the audio playback loop. Writes are buffered in memory and flushed to disk in batches.
2.  **Storage Efficiency:** Data is stored at high resolution (1-minute) for a short time, then rolled up to lower resolutions (1-hour, 1-day) for long-term storage.
3.  **Portability:** The core logic is decoupled from the specific application, making it transferrable to future projects via a shared library.
4.  **Clean Architecture:** Interfaces defined in `Core`, implementation in `Infrastructure`.

## Data Model

### Metric Types
*   **Counter:** Monotonically increasing values (e.g., `SongsPlayed`, `CharactersTTSed`). Stored as deltas (change per bucket).
*   **Gauge:** Variable values (e.g., `DiskUsage`, `MemoryUsage`). Stored as snapshots (Min/Max/Avg/Last) per bucket.

### Database Schema (SQLite)

**1. MetricDefinitions**
Stores metadata to avoid repeating strings in data tables.
| Column | Type | Description |
| :--- | :--- | :--- |
| `Id` | INTEGER PK | Auto-increment ID |
| `Key` | TEXT | Unique key (e.g., "audio.songs_played") |
| `Type` | INTEGER | 0=Counter, 1=Gauge |
| `Unit` | TEXT | Display unit (e.g., "Count", "MB") |
|
**2. MetricData_{Resolution}**
Three separate tables: `MetricData_Minute`, `MetricData_Hour`, `MetricData_Day`.
| Column | Type | Description |
| :--- | :--- | :--- |
| `MetricId` | INTEGER | FK to MetricDefinitions |
| `Timestamp` | INTEGER | Unix Timestamp (start of bucket) |
| `ValueSum` | REAL | Total delta (Counter) or Sum for Avg (Gauge) |
| `ValueCount` | INTEGER | Number of samples in this bucket |
| `ValueMin` | REAL | Min value in bucket (Gauge only) |
| `ValueMax` | REAL | Max value in bucket (Gauge only) |

---

## Implementation Phases

### Phase 1: Core Domain Definitions
Define the interfaces and types in the Core layer. This establishes the contract that the rest of the application will use.

**Files:**
*   `src/RadioConsole.Core/Metrics/MetricType.cs`
*   `src/RadioConsole.Core/Metrics/MetricPoint.cs` (DTO for reading)
*   `src/RadioConsole.Core/Interfaces/IMetricsCollector.cs`
*   `src/RadioConsole.Core/Interfaces/IMetricsReader.cs`

**Coding Assistant Prompt:**
> "I need to implement the Core metrics domain. Please create an Enum `MetricType` (Counter, Gauge), and an interface `IMetricsCollector` with methods `Increment(key, value, tags)` and `Gauge(key, value, tags)`. Also create `IMetricsReader` for retrieving history. Place these in `RadioConsole.Core/Metrics` and `RadioConsole.Core/Interfaces`."

### Phase 2: SQLite Infrastructure
Implement the persistence layer. This involves creating the SQLite tables and the repository to read/write them.

**Files:**
*   `src/RadioConsole.Infrastructure/Data/MetricsDbContext.cs` (or existing context)
*   `src/RadioConsole.Infrastructure/Repositories/SqliteMetricsRepository.cs`

**Coding Assistant Prompt:**
> "Implement `SqliteMetricsRepository` in the Infrastructure layer. It needs to manage tables for `MetricDefinitions` and `MetricData_Minute/Hour/Day`. Implement a method `SaveBucketsAsync` that takes a batch of in-memory buckets and upserts them into the `MetricData_Minute` table. Ensure you handle the foreign key relationship with `MetricDefinitions` efficiently (cache the definitions)."

### Phase 3: Buffered Collector Service
Implement the logic that buffers metrics in memory to prevent disk thrashing. This service will flush to the database every 60 seconds.

**Files:**
*   `src/RadioConsole.Infrastructure/Services/BufferedMetricsCollector.cs`

**Coding Assistant Prompt:**
> "Create a class `BufferedMetricsCollector` that implements `IMetricsCollector` and `IHostedService`. It should use a `ConcurrentDictionary` to aggregate metrics in memory. Use a `System.Threading.Timer` to flush this buffer to the `SqliteMetricsRepository` every 60 seconds. Ensure thread safety when swapping the buffer during a flush."

### Phase 4: Rollup & Pruning Service
Implement the background service that aggregates data from Minute -> Hour -> Day and deletes old data.

**Logic:**
*   **Hourly:** Aggregate `MetricData_Minute` > 2 hours old into `MetricData_Hour`. Delete processed minute rows.
*   **Daily:** Aggregate `MetricData_Hour` > 48 hours old into `MetricData_Day`. Delete processed hour rows.

**Coding Assistant Prompt:**
> "Create a `MetricsRollupService` (BackgroundService). It should run hourly. It needs to query `MetricData_Minute`, aggregate the rows by Hour (Sum for counters, recalculate Min/Max/Avg for gauges), insert them into `MetricData_Hour`, and then delete the old minute rows. Repeat the pattern for rolling Hour data into Day data."

### Phase 5: Integration (Adding the Metrics)
Inject the collector into existing services and instrument the code.

**Integration Points:**

#### 1. Audio Playback & Library (Core)
*   **Locations:** `AudioPlayerService`, `LibraryManager`
*   **Metrics:**
    *   `radio.songs_played_total` (Counter): Increment when track finishes.
    *   `radio.songs_skipped` (Counter): Increment when track is skipped.
    *   `radio.playback_errors` (Counter): Increment on playback exceptions.
    *   `library.tracks_total` (Gauge): Update after library scan.
    *   `library.scan_duration_ms` (Gauge): Measure duration of scan operation.
    *   `library.new_tracks_added` (Counter): Count of new files found during scan.

#### 2. Text-to-Speech Services (Infrastructure)
*   **Locations:** `TtsService` (and specific providers like `AzureTtsProvider`, `GoogleTtsProvider`)
*   **Metrics:**
    *   `tts.requests_total` (Counter): Increment on request, tag by provider (e.g., `provider=azure`).
    *   `tts.latency_ms` (Gauge): Measure stopwatch time from request to audio ready.
    *   `tts.characters_processed` (Counter): Increment by `text.Length`. Critical for cost tracking.
    *   `tts.cache_hits` / `tts.cache_misses` (Counter): Increment based on whether audio file existed locally.

#### 3. System Health & Hardware (Infrastructure)
*   **Location:** `SystemMonitorService` (New Background Service)
*   **Metrics:**
    *   `system.disk_usage_percent` (Gauge): Check drive available space.
    *   `system.cpu_temp_celsius` (Gauge): Read from Pi sensors (`/sys/class/thermal/thermal_zone0/temp`).
    *   `system.memory_usage_mb` (Gauge): `Process.GetCurrentProcess().WorkingSet64`.
    *   `db.file_size_mb` (Gauge): Check `FileInfo` size of the SQLite db.

#### 4. API & Web Usage (Web/API)
*   **Location:** `RadioController`, `WebSocketHub`
*   **Metrics:**
    *   `api.requests_total` (Counter): Middleware to count HTTP requests.
    *   `websocket.connected_clients` (Gauge): Track active connections in Hub.
    *   `ui.button_clicks` (Counter): Received via API from frontend (e.g., `POST /api/metrics/event`).

**Coding Assistant Prompt:**
> "I need to instrument the application with the `IMetricsCollector`.
> 1. In `AudioPlayerService`, track `radio.songs_played_total` and `radio.playback_errors`.
> 2. In `TtsService`, track `tts.characters_processed` and `tts.latency_ms`.
> 3. Create a `SystemMonitorService` that runs every 5 minutes to report `system.disk_usage_percent` and `system.cpu_temp_celsius`.
> 4. Update `LibraryManager` to report `library.tracks_total` after a scan."

### Phase 6: REST API Layer
Expose the metrics to the frontend for visualization.

**Files:**
*   `src/RadioConsole.Web/Controllers/MetricsController.cs`

**Endpoints:**
1.  **Get History:** `GET /api/metrics/history`
    *   **Params:** `key` (string), `resolution` (Minute/Hour/Day), `start` (DateTime), `end` (DateTime)
    *   **Response:** JSON array of time-series data points.
    *   **Usage:** Charts (e.g., "Songs per Hour").
2.  **Get Snapshot:** `GET /api/metrics/aggregate`
    *   **Params:** `key` (string)
    *   **Response:** Single value (Total for Counters, Last/Avg for Gauges).
    *   **Usage:** Dashboard widgets (e.g., "Total Characters Spoken", "Current CPU Temp").

**Coding Assistant Prompt:**
> "Create a `MetricsController` in `RadioConsole.Web`.
> 1. `GetHistory`: Accepts `key`, `start` (DateTime), `end` (DateTime), and `resolution` (Enum). Calls `IMetricsReader.GetHistoryAsync`. Returns a JSON list of points.
> 2. `GetSnapshot`: Accepts `key`. Calls `IMetricsReader.GetCurrentSnapshotsAsync` to get the latest value (for Gauges) or the total sum (for Counters)."

---

## Usage Examples

**Displaying a Chart (Frontend):**
To display "Songs Played per Hour" for the last 24 hours:
1.  Frontend calls API: `GET /api/metrics/history?key=audio.songs_played&resolution=Hour&start={Now-24h}&end={Now}`
2.  Backend `IMetricsReader` queries `MetricData_Hour` (and recent `MetricData_Minute`).
3.  Returns JSON array: `[{ time: 10:00, value: 12 }, { time: 11:00, value: 14 }...]`

**Calculating Total TTS Characters (Aggregate):**
To display "Total Characters Spoken All Time":
1.  Frontend calls API: `GET /api/metrics/aggregate?key=tts.chars_total`
2.  Backend sums `ValueSum` from `MetricData_Day` + `MetricData_Hour` + `MetricData_Minute`.
