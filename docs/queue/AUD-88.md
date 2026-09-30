# `AUD-88` — Windows Bluetooth album art from the media session (SMTC) is silently dropped

[← Builder Queue index](../BUILDER_QUEUE.md)

🔵 **P3.** Filed 2026-09-30 by the `AUD-17` Builder, from a code read. **Windows dev host only — the
appliance is unaffected** (its Bluetooth service supplies no art at all; see [`AUD-17`](AUD-17.md)).

## The defect

1. `WindowsMediaSessionWatcher.ReadCurrentMediaPropertiesAsync` reads the SMTC thumbnail and
   `SaveThumbnailAsync` stores it with `AlbumArtCacheService.SaveAsync`. That returns an
   **already-local** `/api/albumart/<hash>.<ext>` path, or a `data:` URI when no cache is available.
   The watcher publishes this as `BluetoothPlaybackMetadata.AlbumArtUrl`.
2. `BluetoothAudioSource.OnMetadataChanged` sends any supplied art to `CacheSourceSuppliedArtAsync`
   (named `CacheAvrcpArtAsync` before `AUD-17`). That method calls
   `IAlbumArtCacheService.SaveFromUrlAsync(url)`.
3. `AlbumArtCacheService` builds its `HttpClient` with no `BaseAddress`
   (`new HttpClient { Timeout = … }`). A relative path therefore throws inside `GetAsync`, and so
   does a `data:` URI (unsupported scheme). `SaveFromUrlAsync` catches the exception and returns
   null. The source logs *"Source-supplied art URL not cacheable … waiting for SongRec"* at Debug,
   and the art never reaches metadata.

So on Windows, SMTC art is fetched, cached to disk, and then thrown away.

## Fix sketch

The SongRec path already handles this case: `CacheAndSetCoverArtUrlAsync` downloads only when
`IsRemoteUrl(url)` is true and otherwise uses the URL as it is. The source-supplied path should do the
same. It also needs a test through the mock service with a `/api/albumart/...` URL, which would fail
today.

## Verification

This is a unit test only. The Windows path cannot be exercised on the appliance.
