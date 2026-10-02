# `AUD-96` — queue rows are re-read from the NAS on every request, and the 500 ms poller does it continuously

[← Builder Queue index](../BUILDER_QUEUE.md)

🚧 **BUILT 2026-10-02, HELD for owner panel UAT** — PR on branch `perf/aud-96-ui-35-snappy-switch`, one PR
with [`UI-35`](UI-35.md). Not deployed, not merged.

🟠 **P1.** Filed 2026-10-02 by the Builder from the owner's report and a read-only investigation on the box
the same day.

> *"One more thing I notice that switching between audio sources takes quite a while to update the UI. Has
> something changed to make this feel sluggish? It used to feel quite a bit snappier."* — owner, 2026-10-02

## What was measured (read-only, on `radio`, 2026-10-01/02)

- **Server switch latency is unchanged** (0.03–0.7 s). What changed on 2026-09-29: the play queue now holds
  **47 tracks on the NAS** (`/mnt/nas_media`, CIFS over WiFi) after `AUD-75`, and `UI-17` (#715) made the
  centre panel wait on the queue (that half is [`UI-35`](UI-35.md)).
- **`GET /api/queue/full` 1.4–4.7 s and `GET /api/queue` 1.9–4.7 s.** Every call rebuilt every row:
  `FilePlayerAudioSource.CreateQueueItemWithState` / `CreateQueueItem` → `AudioTagReader.Read` (SoundFlow
  metadata, then `AccurateDurationReader` via TagLib) + `TryGetEmbeddedAlbumArtUrl` (TagLib again, and a
  re-hash of the picture) ≈ **140 CIFS opens per call**, no cache.
- **The 500 ms poller did the same read on every pass.** `AudioStateUpdateService.CheckQueueAsync` called
  `GetFullPlaylistAsync()` on every loop pass while a queue source was active, in one sequential loop with
  `CheckSourceChangedAsync`, so a slow queue read delayed the `SourceChanged` broadcast — **measured lags
  1.5–6.4 s on 10-01.** With File Player playing: **406 CIFS opens / 10 s, 99.8 MB read from the NAS / 10 s,
  WiFi rx 10 MB/s** — on the box's only management link, where I/O load correlates with audible distortion.

## What changed

- **`QueueItemMetadataCache`** (`src/Radio.Infrastructure/Audio/Sources/Primary/QueueItemMetadataCache.cs`):
  per-path rows — title, artist, album, duration, art URL — keyed by full path. **A miss never touches the
  file**: it returns the row the old code showed for an unreadable file (file name, `--`, `--`, no art) and
  queues the path for **one sequential background reader** (deliberately not parallel: the point is to take
  load off the share). When stored rows change, the cache's `Version` advances — **once per 16 changed rows
  and once when the reader goes idle**, not per row, because every step is a `QueueChanged` broadcast and a
  queue re-read by every open panel (a 47-track warm-up is three version steps — at 16, 32 and idle — not 47).
- **Staleness: size + last-write time.** Each entry carries the file's stamp. A path is re-validated — one
  `stat` on the reader, a re-read only if the stamp changed — when it is **enqueued** (load file / directory /
  playlist, add to queue, restore at startup) and when it **becomes the current track**. Nothing re-validates
  on a timer: a file re-tagged in place while it sits in the queue keeps its old row until it is enqueued again
  or played. Two kinds of row are re-read even with an unchanged stamp, the next time they are asked for: **an
  unverified row** once it is 2 minutes old, and **any row** once it is a day old. ⚠ The second is load-bearing:
  `AlbumArtCacheService` deletes art it has not been asked to save for 7 days, and the old per-request read
  re-saved every queued track's art continuously — an unstated side effect the cache would otherwise have
  removed.
- **A failure never downgrades a good row.** A row is unverified when its last read or stat failed. On the
  appliance the share sits behind WiFi and `FileInfo.Exists` answers `false` on any I/O error, so neither "read
  as nothing" nor "missing" can be told from a hiccup: the last good metadata is kept (unverified, so it is
  retried), and only a path that never read successfully shows its placeholder. A file that genuinely cannot be
  read costs one attempt per 2 minutes, and only while its queue is being looked at. The cache drops rows for
  paths no longer queued once it exceeds 2,048 entries.
- **The read itself is unchanged** — it is the old per-row code moved onto the reader
  (`FilePlayerAudioSource.ReadQueueItemMetadata`): `AudioTagReader.Read` (so `AUD-32`'s SoundFlow → TagLib
  fallback stands) and `TryGetEmbeddedAlbumArtUrl` (embedded art, content-addressed cache). `AUD-1`'s per-field
  precedence lives in the current-track path (`UpdateMetadataFromFile` / `OnTrackIdentified`), which this does
  not touch; queue rows never carried fingerprint data.
- **`IPlayQueue.QueueVersion`** — a new cheap change signal. `FilePlayerAudioSource` returns a 64-bit FNV-1a hash
  of the played / current / upcoming paths in order and their error flags, mixed with the cache's `Version`.
  Hashing rather than counting mutations is deliberate: the class changes its queue from more than a dozen
  places, and a counter would be right only while every one remembered to bump it. **It touches no file.**
- **`CheckQueueAsync` reads `QueueVersion` every pass and the full playlist only when it moved.** Its snapshot
  now includes each row's metadata, because a row filled in by the reader changes no id, index or state and must
  still be broadcast. Version and snapshot both advance **after** the send (`UI-13` ordering).
- **Saving the queue as a playlist waits for unread rows** (new `IPlayQueue.WaitForQueueMetadataAsync`, bounded
  at 15 s in `PlaylistsController.Create`). The playlist stores title / artist / album / duration for good, so a
  save made right after loading a large folder would otherwise store file names. The wait reports unsettled
  while any queued row's read has failed, too, not only while the reader is busy; either way the save goes ahead
  with what it has and logs a Warning. A save the client abandons during the wait returns 499 rather than
  logging an Error.
- **`QueueVersion` copies the lists with `ToArray`.** Several pre-existing writers change them without
  `_playlistLock`. Enumeration would throw on that; a copy does not, but can pick up a slot a concurrent `Clear`
  or `Dequeue` has just nulled, so a null entry is hashed as a marker. If anything still throws, a fallback value
  practically certain to be new is returned, so the poller re-reads rather than skips. It is seeded per instance
  with a random 64-bit value, so a re-created File Player is practically certain not to repeat an old one's.
- **`radio-web`'s "Queue state saved" line is now Debug.** It ran on every `QueueChanged` in every open panel,
  and `radio-web`'s console sink reaches journald (see `CLAUDE.md`).
- **Restore (`InitializeAsync`) warms the cache** in the background. ⚠ **The `File.Exists` filter at restore was
  considered and left synchronous**: it runs once per process, on the first creation of the File Player (one
  `stat` per saved path, not three opens), and bounding it would change which saved tracks survive a restart on a
  slow mount — a behaviour change this row does not need. Noted rather than done.

## Tests

- `QueueItemMetadataCacheTests` (17, `FakeTimeProvider` where time matters): a miss returns the placeholder at
  once and is read once however often it is asked for; requests while a read is queued are served by that one
  queued pass (the reader parked on a gate); an unchanged file is stat'ed but not re-read and the version stays;
  a changed stamp re-reads and advances the version (and does not when the tags came back identical); a missing
  file shows its placeholder without a read and is stat'ed again only after the retry interval; a throwing
  reader is retried only after the retry interval; a failed read is read again at the next revalidation, and when
  asked for after the retry interval but not before; a failed refresh read and a failed stat both keep the last
  good row; a row past `MaxAge` is re-read when next asked for, and not before; the version is published per
  batch of 16 (reader parked until every path is queued) and when idle; the trim bound; dispose abandons the
  backlog after the read in progress; nothing scheduled after dispose.
- `FilePlayerQueueMetadataTests` (10, counting reader and stat seams): each queued path read once across 50 queue
  reads; rows carry the cached fields; **100 reads of `QueueVersion` with an unchanged queue make zero reads and
  zero stats**; the version moves on a fill (and `WaitForQueueMetadataAsync` reports unsettled before it, settled
  after), add, move and remove; the wait stays unsettled while a row's read has failed; `QueueVersion` hashes a
  null slot rather than throwing; a re-added changed file is re-read, an unchanged one is not; two instances with
  the same queue report different versions; a restored queue is warmed.
- `PlaylistsControllerCreateMetadataTests` (3): saving a playlist waits for the queue metadata before reading the
  queue; still saves when the wait times out; returns 499 and saves nothing when the client cancels.
- `AudioStateUpdateServiceQueueVersionTests` (5): an unchanged version skips the full read on every later pass; a
  moved version with identical rows reads once and sends nothing; a filled-in title / album art is broadcast; a
  cancelled send does not advance the version.
- `QueuePollerFileAccessTests` (Radio.IntegrationTests, 2, hermetic — deliberately not `Category=Integration`):
  the real poller check against a real File Player — **20 passes over an unchanged 12-track queue make no read and
  no stat**; a track added while playing is broadcast once as a placeholder and once filled in.
- Existing fixtures (`FilePlayerAudioSourceTests`, `StandardMetadataTests`) now wait for the reader before deleting
  their temp directory, and the six queue-metadata assertions rendezvous on it (`WhenQueueMetadataIdleAsync`).

## Local benchmark (Windows dev box, not the appliance)

`GetFullPlaylistAsync` over 47 real MP3 copies, each tagged with a 48 KB embedded picture, on local NVMe, with the
album-art cache wired — the same scratch harness run on `main` and on this branch:

| | `main` (`e955f63`) | this branch |
|---|---|---|
| per call, steady state (50 calls) | **35.4 ms** | **0.005 ms** |
| first call after loading the playlist | 42.7 ms | 0.4 ms |
| rows fully tagged after the load | at once (the call does the reads) | 14 ms, in the background |

With the share emulated (the reader seam sleeping 30 ms per row, about three CIFS opens), the branch still answers
in 0.4 ms first / 0.005 ms steady, and the background warm-up takes 1.83 s. The old code would pay that warm-up on
**every** call — 47 × 30 ms ≈ 1.4 s, the low end of what the box measured. ⚠ Local disk understates the box by
orders of magnitude; the deploy measurement below is the one that counts.

## Known limits (accepted)

- A row that nothing asks for is not refreshed. After more than about 6 days with nobody reading the queue, a
  queued track's art file can expire; the next read returns the old URL (and schedules the re-read that re-saves
  the same content-addressed file), so one render can show a broken tile. Narrow, and self-healing.
- Two `SourceChanged` events for one switch can each start a background queue read in the panel ([`UI-35`](UI-35.md)).
  Not a regression — the old code awaited two — and the read is now cheap.

## Owner checks (after deploy)

1. File Player queue still shows titles, artists and art for every row (a row may show its file name for a moment
   right after a playlist load, then fill in).
2. Load a playlist, add a track, reorder, skip: the queue view follows each change.

## What the deploy step must measure

With File Player **playing** and the queue unchanged, over 10 s on the box (bounded reads only): CIFS opens
(was **406 / 10 s**) and bytes read from the NAS (was **99.8 MB / 10 s**) should drop to near zero — what remains
is the playing track's own stream; WiFi rx (was **10 MB/s**) to the audio stream's rate.
`GET /api/queue/full` (was 1.4–4.7 s) should return in milliseconds once the queue is warm. The first queue read
after a restart can still show file names for the few seconds the reader takes to warm 47 rows.
