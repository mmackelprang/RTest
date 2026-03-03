# Findings

## Research Notes

### Bug: Duplicate BT play history entries

**Root cause — two issues in `PlayHistoryTracker.cs`:**

1. **`OnBluetoothMetadataChanged` (line 519)**: When AVRCP reports a NEW song (different title/artist), it overwrites the current entry instead of finalizing it and creating a new entry. This either loses history or causes duplicates depending on timing with fingerprinting's `OnSongChanged`.

2. **`UpsertPlayHistoryAsync` (line 102)**: When BT source fires `StateChanged → Playing`, it creates an entry immediately using `GetSourceMetadata()`. For BT, metadata is often just the device name ("Pixel 8 Pro") at this point — real AVRCP data arrives milliseconds later. This creates placeholder entries ("Pixel 8 Pro / Bluetooth") that may never get properly updated.

**Fix:**
- `OnBluetoothMetadataChanged`: When title/artist CHANGED (new song), finalize old entry + create new entry. When title/artist improved (same song, better metadata), update existing entry. Add dedup check.
- `UpsertPlayHistoryAsync`: For BT sources with placeholder metadata, skip creating the entry. Let `OnBluetoothMetadataChanged` create it when real AVRCP data arrives.

## Key Files
| File | Role |
|------|------|
| `src/Radio.Infrastructure/Audio/Services/PlayHistoryTracker.cs` | Play history recording logic (the bug) |
| `src/Radio.Infrastructure/Audio/Fingerprinting/Data/SqlitePlayHistoryRepository.cs` | SQLite persistence |
| `src/Radio.Core/Interfaces/Audio/IPlayHistoryRepository.cs` | Repository interface |

## Open Questions
- _None_
