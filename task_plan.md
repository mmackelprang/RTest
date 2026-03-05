# Task Plan: Audio Distortion Debugging & Instrumentation

## Goal
Investigate BT audio distortion events (slow/underwater/clipped sound) tagged by user over 90 minutes. Determine root cause before attempting fixes. Design instrumentation to make these issues diagnosable in the future.

## Current Phase
Phase 1 — Analysis & Research (complete)

## Phases

| # | Phase | Status | Notes |
|---|-------|--------|-------|
| 1 | Analysis & Research | complete | Extracted 151 markers, correlated with logs, read pipeline code |
| 2 | Instrumentation Design | in_progress | What metrics/logging to add |
| 3 | Implementation | pending | Add the instrumentation |
| 4 | Deploy & Collect Data | pending | Run with instrumentation, reproduce distortion, analyze |

## Key Findings

1. **NOT clipping** — all markers show isClipping=False, peaks at -6 to -11 dBFS
2. **No app-level errors** during distortion — no buffer drops, underruns, or compensations
3. **PipeWire-pulse overruns** logged at 14:01 — MiniAudio output not consuming fast enough
4. **Buffer grows monotonically** — BT clock ~2.1s/hour faster than ALSA. received > output. No DropOldest or drift compensation triggered during the distortion window.
5. **Lock contention possible** — both PipeWire thread (AddSamples) and MiniAudio thread (GenerateAudio) compete for `_bufferLock`
6. **Source state anomaly** — 49% of markers show state="Ready" while audio is clearly playing
7. **BT format S24LE** → our stream requests S16LE (PipeWire converts). Sample rates match at 48kHz.

## Proposed Instrumentation

### A. GenerateAudio Callback Timing
- Measure interval between successive GenerateAudio calls (should be ~10.67ms for 512 samples at 48kHz)
- Log when interval exceeds 2x expected (missed deadline)
- Track min/max/avg over 10s windows

### B. Lock Contention Timing
- Time how long GenerateAudio waits for `_bufferLock` (use Stopwatch around lock acquisition)
- Time how long AddSamples waits for `_bufferLock`
- Log when wait exceeds 1ms (concerning for real-time audio)

### C. Buffer Level Sampling
- Record buffer level (as % of capacity) at every GenerateAudio call
- Log periodic stats: min/max/avg level over 10s windows
- Emit metric for buffer fill percentage

### D. Audio Discontinuity Detection
- In GenerateAudio, track the last N samples read
- Detect discontinuities (sudden jumps in sample values) that indicate glitches
- Count and log discontinuities per interval

### E. PipeWire OnProcess Timing
- Measure interval between OnProcess calls in PipeWireNativeStream
- Log burst delivery (multiple callbacks within 1ms)
- Track delivery jitter

### F. GC Pause Monitoring
- Use `GC.RegisterForFullGCNotification()` or monitor `GC.GetGCMemoryInfo()`
- Log Gen2 collections with duration
- Correlate with audio callback timing

### G. Metrics Integration
- Buffer fill % → metrics collector (for dashboard visualization)
- Callback interval stats → metrics
- Lock wait times → metrics
- Discontinuity count → metrics

## Research Items

### R1: Unify Config Systems (SQLite vs appsettings.json)

**Priority:** High — has caused multiple bugs
**Status:** Research needed

**Problem:** The app has two independent config systems:
1. `.NET IConfiguration` pipeline — reads from `appsettings.json` at startup, feeds `IOptions<T>`
2. `IConfigurationManager` (SQLite store) — UI config pages read/write here at runtime

When the UI saves a config change (e.g. `UseShazamForAllSources` toggle), it writes to SQLite but `IOptions<T>` still holds the stale `appsettings.json` value. This has caused:
- BT fingerprinting not running despite user enabling the toggle
- Likely other settings silently ignoring UI changes until restart

**Options to research:**
1. **Drop SQLite, use JSON config only** — simpler, `IOptionsMonitor<T>` auto-reloads on file change. Downside: harder to manage via API/UI (must write JSON files).
2. **Bridge SQLite → IConfiguration** — implement `IConfigurationProvider` backed by SQLite. Config changes flow to `IOptionsMonitor<T>` via change tokens. Architectural but most correct.
3. **Drop IOptions<T>, use IConfigurationManager everywhere** — all config reads go through the store. Downside: lose type-safe options binding, need to convert from key-value strings.
4. **Hybrid: Save to both stores simultaneously** — API controller saves to SQLite AND updates `IConfiguration`/JSON. `IOptionsMonitor<T>` picks up changes. Fragile synchronization.

**Current workaround:** `AudioSourceFactory.SyncFingerprintingOptionsFromStore()` manually reads from SQLite and patches the `IOptions<T>` value on source creation. This is a band-aid.

## Key Decisions
| Decision | Rationale | Date |
|----------|-----------|------|
| Research first, instrument second | User wants to understand before fixing — previous attempts at blind fixes didn't resolve | 2026-03-05 |
| Focus on callback timing + lock contention first | PipeWire-pulse overruns + lock architecture are the most likely root causes | 2026-03-05 |
| Don't fix the buffer growing issue yet | Buffer hasn't actually overflowed in these sessions; fixing it won't address current distortion | 2026-03-05 |
| Config unification research needed | SQLite vs appsettings.json duplication has caused multiple bugs — needs architectural decision | 2026-03-05 |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| (none yet) | | |
