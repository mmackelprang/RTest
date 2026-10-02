# `AUD-98` — a File Player list must never lose the tracks it has played

[← Builder Queue index](../BUILDER_QUEUE.md)

🚧 **BUILT 2026-10-02, HELD for owner panel UAT — not deployed, not merged.**

## Report

Owner, 2026-10-02: *"When playing a playlist in file mode, how does repeat work?  It seems like as song are played, they're dropped from the playlist?  Is this true?  If so, we need to make sure that repeat actually "works", and not remove from the playlist."*

## Measured on the box

- Coordinator, earlier on 2026-10-02 (box on `c867096`, Repeat All): `GET /api/queue/full` = **39** (3 Played, 1 Current, 35 Upcoming); `GET /api/queue` = **36** (current + upcoming).
- Config store (`Config_sqlite`, read-only, 2026-10-02): `FilePlayerPreferences:QueueItems` = a 3,891-character JSON list, `FilePlayerPreferences:CurrentQueueIndex` = `0`, `Shuffle` = `True`, `Repeat` = `All` (and lowercase `fileplayerpreferences:shuffle`/`repeat` copies, both the same values). **No `OriginalOrder` row and no lowercase `queueitems` row.** `queue.state:count` = 35.
- After the box was restarted onto a later build (`552396d`, built 14:13Z): `GET /api/queue/full` = **32**, all of them Current or Upcoming — **0 Played**.

## Cause

Within a session the normal Next / Repeat All path drops nothing. `NextAsync` moves the finished track into `_playedHistory`, and Repeat All rebuilds the list from `_originalOrder` when the upcoming queue runs out.

**Persistence was the problem.** `SaveQueueStateToPreferences` saved `GetAllTracksInOrder()`, which is the current track plus the upcoming ones. `InitializeAsync` then restored **both** `_playlist` **and** `_originalOrder` from that list. So every restart (every deploy) deleted the played tracks for good, and from then on Repeat All repeated only what had been left. With Shuffle on, the unshuffled order was lost as well.

Found on the way. Items 1, 2 and 5 lost or duplicated a track within a session; items 3 and 4 are persistence defects:

1. **`PreviousAsync` with Repeat All, at the first track**, set the current track to `_originalOrder[^1]` and changed nothing else. The track that had been current vanished, and the last track was then listed twice.
2. **`NextAsync` past the end with Repeat Off** left the last track in `_playedHistory` while it stayed the current track. The full list showed it twice, and Previous then put a copy of it in front of itself.
3. **`PreviousAsync` never saved.** It raises no `QueueChanged`, so a restart after a Previous restored the list as it had been before the Previous.
4. **The store write did not update `_preferences.CurrentValue`.** `PreferencesPersistenceService` writes `CurrentValue` back to the store every 30 seconds. Whenever no configuration reload had happened in between, that save wrote the queue from the last reload over the newer one.
5. **`SetShuffleAsync(false)`** rebuilt the upcoming tracks as `_originalOrder` from the current track onward and ignored the history. Played tracks came back as upcoming, and tracks before the current one in the original order that had not been played left the list. *(Found by the pre-merge review.)* It was already wrong within a session. Before this change a restart emptied the history and reset the unshuffled order to the play order, which hid it. With both now restored, it would have hit after every restart that had Shuffle on.
6. **The queue panel's Remove** sent a row's full-list index to `DELETE /api/queue/{index}`, which counts from the current track, so it removed the track as many rows further down as there were played tracks. *(Found by the pre-merge review.)* Before this change it went wrong only after the first Next in a session; with played tracks surviving a restart, it went wrong from the first tap.

## Fix — the persisted shape

| Key | Before | Now |
|---|---|---|
| `FilePlayerPreferences:QueueItems` | current + upcoming | **played + current + upcoming**, in play order |
| `FilePlayerPreferences:CurrentQueueIndex` | `_currentIndex`: 0 in practice, stale after a queue move | the current track's index in `QueueItems`, which is the played count; `-1` when the list is empty |
| `FilePlayerPreferences:OriginalOrder` | — | **new**: the unshuffled order (what Repeat All and turning Shuffle off rebuild from) |

**Migration.** No backward-compatibility code was needed. The old shape is a special case of the new one: an index of 0 means nothing has been played. A box holding the old rows therefore starts exactly as before, with the first saved track current, nothing played, and the unshuffled order taken from the saved list. From the first queue change after that, it is in the new shape. **The tracks the old shape already dropped cannot be recovered.** Reloading the folder or the saved playlist brings them back.

**Restore** is the pure `BuildRestoredQueue`:
- A missing file is skipped without moving the played/current boundary.
- If the current track is missing, the next existing track becomes current.
- If nothing exists at or after the index, the last played track becomes current.
- The unshuffled order is the saved one, completed with any restored track it lacks.
- Each path is checked with `File.Exists` once.

**Save:**
- **Every case variant of each key is written.** This is the `UI-28` trap. It matters more for a list than for a scalar: the bridge flattens a JSON array into `:0`, `:1`, … with case-insensitive keys, so a stale lowercase row holding a longer list would add its extra entries to the restored list.
- **Writes are serialised, and a snapshot older than the newest is skipped**, so two quick changes cannot land in the wrong order.
- **`CurrentValue` is updated after the store write.** Races remain, each needing a periodic save or a reload to overlap the write: a periodic save that serialised before the update, a reload already in flight, and a periodic save pairing the new list with the old index (which moves the boundary but drops no track). The next queue change writes again; until then a restart restores the earlier list.
- **The write runs on the thread pool** (`Task.Run`), not on the playback monitor or the HTTP request. **The store is scanned for case variants once per source**, not on every save.

**Defects 1–6 above are fixed.**
- The Repeat All wrap keeps every track. With Shuffle on it now goes to the **last track in shuffled order** rather than to `_originalOrder[^1]`. With Shuffle off and no queue edits, these are the same track.
- With Repeat Off, Previous after the end now steps back one track. Before, it re-selected the last track.
- Turning Shuffle off reorders exactly the current and upcoming tracks by the unshuffled order, with the current track first. Played tracks stay played, and no unplayed track leaves.
- The panel converts the row to the current-relative index (`QueueHistoryPanel.ToQueueIndex`), anchored on the Current row.

## `GetQueueAsync` consumers

| Consumer | Disposition | Why |
|---|---|---|
| `PlaylistsController.Create` (Save as playlist) | **→ `GetFullPlaylistAsync`** | The owner sees the whole list. **Chosen order: as the panel shows it** — played, current, upcoming, in play order (shuffled order if Shuffle is on). |
| `QueueController.ContainsTrack` (`/api/queue/contains`) | **→ `GetFullPlaylistAsync`** | A played track is still in the list, and Repeat All plays it again. Nothing in the Web UI calls this endpoint today. |
| `QueueController.GetQueue`, `AddToQueue`, `RemoveFromQueue`, `MoveQueueItem` | kept | They return the list whose indexes `RemoveFromQueueAsync` / `MoveQueueItemAsync` / `JumpToIndexAsync` / `AddToQueueAsync(position)` take. Returning the full list would hand callers indexes that point at different tracks. |
| Web `QueueHistoryPanel` Remove button → `DELETE /api/queue/{index}` | **fixed in the caller** | It already showed the full list but passed the full-list index (defect 6). It now passes `ToQueueIndex`. |
| Web `QueuePersistenceService.SaveQueueStateAsync` (`queue.state`) | kept | **Write-only**: `RestoreQueueStateAsync` and `ClearQueueStateAsync` have no callers, so nothing restores from `queue.state`. What it holds changes nothing. Deleting it is a candidate follow-up; it was left alone here because `AUD-96` edits the same file. |
| Web `MainLayout.RefreshQueueCountAsync` (nav badge) | kept | The badge counts what is left to play (current + upcoming); played tracks show dimmed in the panel. |
| Web `QueueHistoryPanel.RefreshQueueAsync` fallback | kept | Runs only when `/api/queue/full` fails. It is a degraded display, not a source of truth. |
| API `AudioStateUpdateService` | already `GetFullPlaylistAsync` | — |

`IPlayQueue.GetQueueAsync`'s doc said *"upcoming items only"*. It returns the current track too, and never the played ones. It now says so, and names the operations that use its indexes.

## Not changed — seen, left for a row

`RebuildQueueFromList` drops every track before the current one. It is reached when `AddToQueueAsync(position: 0)` inserts in front of the current track, or when `MoveQueueItemAsync` moves an upcoming track in front of it: the inserted or moved track disappears, and `_currentIndex` goes stale. Those are unplayed tracks, and the change has a separate design question (should a track inserted before the current one count as played?), so it is reported rather than fixed here.

`RemoveFromQueueAsync` never removes the track from `_originalOrder`, so a removed track comes back at the next Repeat All wrap. That was already true within a session. Now that the unshuffled order is persisted, it is also true after a restart. *(Pre-merge review, LOW.)*

`QueueApiService.AddToQueueAsync` (Web) posts to `/api/queue`, but the route is `POST /api/queue/add`. It has no callers. *(Pre-merge review, pre-existing.)*

## Tests

- `FilePlayerQueueRestoreTests` (22): restart round trip with states, Repeat All after a restart (Shuffle off and on), a Shuffle restart keeping both the play order and the unshuffled order, Shuffle off after a restart and within a session, old shape at index 0 and at a non-zero index, Previous and jump-into-played after a restart, the Repeat Off end, the Repeat All Previous wrap, an emptied list, and `BuildRestoredQueue` edge cases.
- `FilePlayerQueueRestoreSqliteTests` (4), on the real store and bridge: a round trip, a stale lowercase row holding a longer list, the box's old-shape rows, and `CurrentValue` before any reload.
- `QueueWholeListConsumerTests` (2): Save as playlist, and `contains`.
- `QueueHistoryPanelRemoveIndexTests` (4): the panel's Remove index with and without played tracks, with an Error row, and with no current row.

**Mutation checks.** Each mutant was applied to committed code, run against these tests, then reverted. All 14 were killed:

| Mutant | Killed by |
|---|---|
| save without played | 8 tests |
| restore ignoring `OriginalOrder` | 2 |
| no case variants | the stale-row test |
| no `CurrentValue` update | the `CurrentValue` test |
| Previous not persisting | the Previous test |
| Repeat Off end duplicate kept | the end-of-list test |
| old Previous wrap | the wrap test |
| Save as playlist via `GetQueueAsync` | the playlist test |
| `contains` via `GetQueueAsync` | the `contains` test |
| missing files moving the boundary | the missing-files test |
| restore discarding played | the round-trip tests |
| **the original defect** (no played tracks saved AND no `OriginalOrder`) | both Repeat All after-restart tests (Shuffle off and on) |
| Shuffle off back to `_originalOrder.Skip(current)` | both Shuffle-off tests |
| panel passes the full-list index | 3 of the 4 Remove-index tests (the no-played case is the same index either way) |

Saving without played tracks alone does **not** fail the Repeat All tests. That is because the persisted `OriginalOrder` still holds every track, so Repeat All still wraps to the whole list; the round-trip tests catch that mutant instead.

## Owner check

1. **On the deployed build** (check `curl -s http://radio:5000/api/health/version` against the branch head first), with **Repeat All** on, play a few tracks in File mode (skip ahead 3–4 times).
2. **Deploy or restart.** The full list comes back: the tracks already played are shown dimmed above the current one, nothing is missing, and the count is the same as before the restart.
3. **Let it reach the last track (or skip to it) and go past it.** It wraps to the first track of the **whole** list, not to the track that was current at the restart.
4. **Mid-list, Save as playlist.** The saved playlist's track count equals the whole list, played tracks included. Load it to confirm.
5. *(Shuffle)* With Shuffle on, repeat 1–2. The order after the restart is the same shuffled order. Turning Shuffle off keeps the played tracks dimmed where they are; the upcoming tracks are every unplayed track, in folder order, with none repeated.
6. **After the restart, with played tracks showing, tap ✕ on an upcoming track.** That track is the one removed, not one further down.
