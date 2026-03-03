# Task Plan

## Objective
Fix metrics dashboard so switching the timeframe updates card values, not just sparklines.

## Phases

### Phase 1: Fix auto-refresh timer
- [x] Identify root cause: timer calls `LoadSnapshotsAsync()` which overwrites time-scoped values
- [ ] Fix timer callback to re-apply `UpdateCardValuesFromSparklines()` after loading snapshots
- [ ] Optionally refresh sparkline data on timer tick too

### Phase 2: Testing & Validation
- [ ] Build with 0 warnings
- [ ] All tests pass
- [ ] Deploy to Ubuntu and verify timeframe switching updates card values

## Decisions Log
| # | Decision | Rationale | Date |
|---|----------|-----------|------|
| 1 | Timer should reload sparklines + re-apply values | Keeps card values in sync with selected timeframe even on auto-refresh | 2026-03-02 |

## Current Status
**Phase:** 1
**Blocked:** No
