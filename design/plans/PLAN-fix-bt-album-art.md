# Fix: BT album art does not display in UI (cache the AVRCP URL + remove the MusicBrainz fallback)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Branch:** `fix/bt-album-art` (off `main`, PR back to `main`)

**Goal:** When the audio source is Bluetooth, the UI must display album art whenever it can be obtained — either from AVRCP (rare; mostly local/desktop players) or from SongRec identification (common; streaming services strip art from AVRCP). Eliminate the silent failure mode where the AVRCP `ArtUrl` is stored as a raw `file:///data/data/com.android.../cache/art.jpg` URI and sent to the browser, which cannot fetch it. Remove the deprecated MusicBrainz Cover Art Archive text-search fallback path entirely (project policy: MB is deprecated in favor of SongRec).

---

## 1. Problem statement

When a phone streams audio over A2DP, the Radio Console UI shows the fallback album art icon instead of cover art. The Radio, File, and USB sources all display cover art correctly. The bug reproduces 100% with mainstream phone players (Spotify, YouTube Music, Apple Music) and intermittently with other players.

## 2. Root cause

Two independent failure paths, confirmed by parallel debug investigation (file:line evidence below).

### Bug A — AVRCP fast path bypasses the album-art cache (HIGH confidence)

[`BluetoothAudioSource.OnMetadataChanged` at lines 736–743](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs):

```csharp
// Propagate album art URL from AVRCP if available.
if (!string.IsNullOrEmpty(e.AlbumArtUrl))
{
  MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = e.AlbumArtUrl;
}
```

`e.AlbumArtUrl` originates from BlueZ MPRIS `Track.ArtUrl` ([LinuxBluetoothService.cs:2611](../../src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs)), which is typically a `file:///data/data/com.android.<player>/cache/art.jpg` URI pointing into the **phone's** local filesystem. The URL is stored raw in the metadata bag and broadcast verbatim through SignalR to the browser. The browser cannot fetch `file://` URLs over the network — `img.onerror` fires and the UI swaps to the default-art icon.

The MusicBrainz path ([:925–936](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) and the SongRec / fingerprint path ([:803](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) both correctly route through `_albumArtCache.SaveFromUrlAsync` — which downloads the bytes, content-addresses them on disk, and returns the relative `/api/albumart/{hash}.{ext}` URL the browser can fetch. The AVRCP fast path is the only divergent code.

### Bug B — MusicBrainz fallback path is the deprecated approach (MEDIUM confidence)

[`BluetoothAudioSource.OnMetadataChanged` at lines 760–769](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs):

```csharp
else if (string.IsNullOrEmpty(e.AlbumArtUrl) && _serviceScopeFactory != null)
{
  // AVRCP rarely provides album art — look it up via MusicBrainz text search
  var lookupKey = $"{e.Title}|{e.Artist}";
  if (lookupKey != _lastCoverArtLookupKey && !_failedArtLookups.Contains(lookupKey))
  {
    _lastCoverArtLookupKey = lookupKey;
    _ = LookupCoverArtAsync(e.Title, e.Artist, e.Album);
  }
}
```

`LookupCoverArtAsync` ([:841–880](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) calls `IMetadataLookupService.SearchCoverArtByTextAsync` ([IMetadataLookupService.cs:19–21](../../src/Radio.Core/Interfaces/Audio/IMetadataLookupService.cs)) — the MusicBrainz Cover Art Archive text search. Per user policy, **MusicBrainz has been effectively deprecated in this project and replaced with SongRec**. The remaining MB-path failure modes (empty `ContactEmail` blocking the User-Agent, sticky `_failedArtLookups` cache without TTL, title-only retry that loses album disambiguation) are documented elsewhere but become moot once the path is removed.

The replacement strategy for the "AVRCP has no art" case is:

1. **AVRCP fast path** (with Bug A fixed — cache the URL through `_albumArtCache.SaveFromUrlAsync`).
2. **SongRec identification** via `SoundFlowAudioTap` → `BackgroundIdentificationService.TrackIdentified` → `OnTrackIdentified` ([BluetoothAudioSource.cs:801–803, 818–826](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) — already wired, already routes through `CacheAndSetCoverArtAsync`.
3. **If neither produces art: "no album art" is the accepted UX.** Per user, explicitly confirmed.

---

## 3. Design decisions

### Decision 1: Cache the AVRCP URL even if it's `file://` (let `SaveFromUrlAsync` fail)

`AlbumArtCacheService.SaveFromUrlAsync` ([AlbumArtCacheService.cs:109–137](../../src/Radio.Infrastructure/Audio/AlbumArtCacheService.cs)) uses `HttpClient.GetAsync(url)`. For a `file://` URL this throws `NotSupportedException` (no `file://` handler on `HttpClient`), which the existing `catch` block swallows and returns `null`. We propagate that: when `SaveFromUrlAsync` returns `null`, we **remove** `StandardMetadataKeys.AlbumArtUrl` from the metadata bag (do NOT store the raw `file://` URL). The UI then falls back gracefully and SongRec, if it identifies the track, will later populate the art via `OnTrackIdentified`.

Rationale for routing `file://` through the cache rather than filtering it out earlier: future-proofing. Some BlueZ MPRIS players (and the iOS BT stack on some firmwares) embed art as `http://localhost:<port>/...` or embedded `data:` URIs; the cache already handles those correctly. A scheme-based filter would have to enumerate every supported scheme. Letting the cache decide via its existing HTTP-fetch behavior is simpler and self-contained.

### Decision 2: Scoped removal of MB code from `BluetoothAudioSource` only

The user authorized in-place MB cleanup "if trivial". The MB removal in `BluetoothAudioSource` is mechanical:

- Delete the `else if (string.IsNullOrEmpty(e.AlbumArtUrl) && _serviceScopeFactory != null)` branch in `OnMetadataChanged` (lines 760–769).
- Delete the `else if (!string.IsNullOrEmpty(e.Track.MusicBrainzReleaseId))` branch in `OnTrackIdentified` (lines 823–827) and the `else if (!string.IsNullOrEmpty(e.Track.Title) && !string.IsNullOrEmpty(e.Track.Artist))` text-search retry branch (lines 828–836). What remains in `OnTrackIdentified` is the `CacheAndSetCoverArtAsync` call for the SongRec `e.Track.CoverArtUrl` (the only path we want to keep).
- Delete the now-orphaned helper methods `LookupCoverArtAsync` ([:841–880](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) and `LookupCoverArtByReleaseIdAsync` ([:894–923](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)).
- Delete the now-dead state fields `_failedArtLookups` ([:36](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) and `_lastCoverArtLookupKey` ([:38](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)), and the two reset sites that touch them ([:663, :747](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)).

**What stays:**

- `CacheAndSetCoverArtAsync` and `CacheAndSetCoverArtUrlAsync` ([:882–941](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) — these are the SongRec path's cache plumbing, also used by the new AVRCP cache call.
- `UpdateRecentPlayHistoryCoverArtAsync` ([:947–989](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) — back-fills the play history row with the resolved art URL; still needed for both the AVRCP and SongRec paths.

**What's out of scope for THIS PR:**

- `IMetadataLookupService` interface itself + `MetadataLookupService` implementation in `Radio.Fingerprinting`. Still used by DI registration ([FingerprintingServiceExtensions.cs](../../src/Radio.Infrastructure/DependencyInjection/FingerprintingServiceExtensions.cs)) and potentially by other sources we have not audited in scope. Deleting the interface is a wider blast-radius cleanup that deserves its own PR.

### Decision 3: Verify SongRec wiring is intact (read-only audit; no code change expected)

The Coordinator's H-A investigation confirmed that `_identificationService.TrackIdentified += OnTrackIdentified` is wired in the constructor ([:94](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)), `NeedsFingerprintingLookup` is set when AVRCP metadata is incomplete ([:753–754](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)), `RequestImmediateIdentification` is invoked ([:758](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)), and `OnTrackIdentified` routes through `CacheAndSetCoverArtAsync` for `e.Track.CoverArtUrl` ([:801–803](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)). Task 4 is a read-only audit + manual UAT verification; only if a wiring gap is found does it become a code change (in which case Builder pauses for Planner re-scoping).

---

## 4. Bite-sized tasks

Each task is one commit. Ordered so the highest-impact fix (Task 2) lands first, so the bug is partially mitigated even mid-PR. The MB removal (Task 3) is purely subtractive and lands after — making the test in Task 1 the only thing in flight that depends on the new behavior.

### Task 1: Create the feature branch

**Step 1:**

```bash
git switch -c fix/bt-album-art
```

**Step 2:** Confirm clean tree (untracked `.claude/plugins/`, `.claude/worktrees/`, `scripts/research/__pycache__/` are expected and unrelated).

```bash
git status
```

No commit on this task — branch creation only.

---

### Task 2: Cache the AVRCP `ArtUrl` through `_albumArtCache` (Bug A — the primary fix)

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`

**Step 1:** Replace the AVRCP fast path in `OnMetadataChanged` ([lines 736–748](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)).

**Before:**
```csharp
// Propagate album art URL from AVRCP if available.
// Always clear stale art from the previous song — PlayHistoryTracker reads
// AlbumArtUrl from source metadata when creating entries, so leftover art
// from the previous song would leak into the new song's history entry.
if (!string.IsNullOrEmpty(e.AlbumArtUrl))
{
  MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = e.AlbumArtUrl;
}
else
{
  MetadataInternal.Remove(StandardMetadataKeys.AlbumArtUrl);
  _lastCoverArtLookupKey = null;
}
```

**After:**
```csharp
// Propagate album art URL from AVRCP if available.
// Always clear stale art from the previous song — PlayHistoryTracker reads
// AlbumArtUrl from source metadata when creating entries, so leftover art
// from the previous song would leak into the new song's history entry.
//
// AVRCP ArtUrl is usually a file:///data/data/com.android.<player>/cache/...
// URI on the PHONE'S local filesystem, which the browser cannot fetch. Route
// every URL through the album-art cache: it downloads the bytes (for http://)
// or returns null (for file:// — HttpClient throws NotSupportedException,
// caught inside SaveFromUrlAsync). On null we leave AlbumArtUrl absent so the
// UI shows the fallback icon; SongRec, if it later identifies the track via
// OnTrackIdentified, will populate the art then.
MetadataInternal.Remove(StandardMetadataKeys.AlbumArtUrl);
if (!string.IsNullOrEmpty(e.AlbumArtUrl) && _albumArtCache != null && _serviceScopeFactory != null)
{
  _ = CacheAvrcpArtAsync(e.AlbumArtUrl, e.Title, e.Artist);
}
```

**Step 2:** Add a small helper next to `CacheAndSetCoverArtAsync` ([lines 882–892](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)). It's a focused wrapper around `CacheAndSetCoverArtUrlAsync` that logs the AVRCP-specific context and tolerates failure (cache returns null):

```csharp
/// <summary>
/// Downloads the AVRCP-supplied album art URL into the local cache and
/// sets <see cref="StandardMetadataKeys.AlbumArtUrl"/> to the resulting
/// /api/albumart/... path. If the cache returns null (file:// URLs,
/// network errors, etc.) metadata is left without AlbumArtUrl and the
/// UI falls back to the default icon.
/// </summary>
private async Task CacheAvrcpArtAsync(string artUrl, string title, string artist)
{
  try
  {
    var localUrl = await _albumArtCache!.SaveFromUrlAsync(artUrl);
    if (string.IsNullOrEmpty(localUrl))
    {
      Logger.LogDebug(
        "AVRCP art URL not cacheable for '{Title}' by '{Artist}' (URL: {Url}); waiting for SongRec",
        title, artist, artUrl);
      return;
    }

    MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = localUrl;
    Logger.LogInformation(
      "Cached AVRCP album art for '{Title}' by '{Artist}': {LocalUrl}",
      title, artist, localUrl);

    await UpdateRecentPlayHistoryCoverArtAsync(localUrl, title, artist);
  }
  catch (Exception ex)
  {
    Logger.LogWarning(ex, "Failed to cache AVRCP album art for '{Title}' by '{Artist}'", title, artist);
  }
}
```

**Step 3: Build + run all tests.** (Bug B branch still present — that's Task 3.)

```bash
dotnet build --configuration Release
dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothAudioSourceTests" --configuration Release -v n
```

Expected: 0 warnings, existing tests still pass. The existing "MetadataChanged_NewSongWithoutArt_ClearsPreviousAlbumArt" test still passes because we still `Remove` at the top of the block.

**Step 4: Commit**

```bash
git add src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs
git commit -m "$(cat <<'EOF'
fix(bt): route AVRCP album-art URL through cache (avoid raw file:// to browser)

AVRCP Track.ArtUrl is usually a file:///data/data/com.android.<player>/cache
URI on the phone's local filesystem; sending it raw to the browser produced
img.onerror and the default-art fallback. Route every URL through
AlbumArtCacheService.SaveFromUrlAsync, which downloads http:// URLs and
returns null for file:// (HttpClient throws NotSupportedException, caught
internally). On null we leave AlbumArtUrl absent so the UI gracefully shows
the fallback icon; SongRec, if it identifies the track, populates art via
OnTrackIdentified.
EOF
)"
```

> **Bug is partially fixed at this commit** — the AVRCP fast path now never leaks `file://` URLs to the browser. The MB fallback (Task 3) is still active but harmless; removing it is a code-hygiene improvement, not a bug fix.

---

### Task 3: Remove the deprecated MusicBrainz fallback paths from `BluetoothAudioSource`

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`

**Step 1:** Delete the MB text-search fallback branch in `OnMetadataChanged` ([lines 755–769](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)).

**Before** (the `if (NeedsFingerprintingLookup) ... else if (... MB fallback ...)` block):
```csharp
if (NeedsFingerprintingLookup)
{
  _identificationService?.RequestImmediateIdentification();
}
else if (string.IsNullOrEmpty(e.AlbumArtUrl) && _serviceScopeFactory != null)
{
  // AVRCP rarely provides album art — look it up via MusicBrainz text search
  var lookupKey = $"{e.Title}|{e.Artist}";
  if (lookupKey != _lastCoverArtLookupKey && !_failedArtLookups.Contains(lookupKey))
  {
    _lastCoverArtLookupKey = lookupKey;
    _ = LookupCoverArtAsync(e.Title, e.Artist, e.Album);
  }
}
```

**After:**
```csharp
// If incomplete or Shazam-for-all is on, request a fingerprint identification.
// SongRec is the only cover-art fallback for BT (MusicBrainz has been removed
// project-wide); when AVRCP has no art and SongRec doesn't identify the track,
// the UI shows the fallback icon — accepted UX.
if (NeedsFingerprintingLookup)
{
  _identificationService?.RequestImmediateIdentification();
}
```

**Step 2:** Delete the MB branches in `OnTrackIdentified` ([lines 823–837](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)).

**Before** (the three-branch `if/else if/else if` for art resolution after fingerprinting):
```csharp
if (!hasArt && _serviceScopeFactory != null)
{
  if (!string.IsNullOrEmpty(e.Track.CoverArtUrl))
  {
    // Fingerprint pipeline already found cover art — cache it locally
    _ = CacheAndSetCoverArtAsync(e.Track.CoverArtUrl, e.Track.Title, e.Track.Artist);
  }
  else if (!string.IsNullOrEmpty(e.Track.MusicBrainzReleaseId))
  {
    // Have a release ID but no art yet — query Cover Art Archive directly
    _ = LookupCoverArtByReleaseIdAsync(e.Track.MusicBrainzReleaseId, e.Track.Title, e.Track.Artist);
  }
  else if (!string.IsNullOrEmpty(e.Track.Title) && !string.IsNullOrEmpty(e.Track.Artist))
  {
    // Fingerprint gave us better metadata — retry text search with it
    var lookupKey = $"{e.Track.Title}|{e.Track.Artist}";
    if (lookupKey != _lastCoverArtLookupKey && !_failedArtLookups.Contains(lookupKey))
    {
      _lastCoverArtLookupKey = lookupKey;
      _ = LookupCoverArtAsync(e.Track.Title, e.Track.Artist, e.Track.Album);
    }
  }
}
```

**After:**
```csharp
// Use fingerprint-identified cover art if we don't already have art.
// SongRec provides Apple Music CDN URLs which are HTTPS and cache cleanly.
// If SongRec didn't provide a CoverArtUrl, leave art absent (UI fallback);
// MusicBrainz CAA / release-ID lookup is no longer used for BT (deprecated
// project-wide in favor of SongRec).
if (!hasArt && !string.IsNullOrEmpty(e.Track.CoverArtUrl) && _serviceScopeFactory != null)
{
  _ = CacheAndSetCoverArtAsync(e.Track.CoverArtUrl, e.Track.Title, e.Track.Artist);
}
```

**Step 3:** Delete the now-orphaned helper methods `LookupCoverArtAsync` ([lines 841–880](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)) and `LookupCoverArtByReleaseIdAsync` ([lines 894–923](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)). Confirm with grep that nothing else in `BluetoothAudioSource.cs` references either name — both are private, so grep within the file is sufficient.

**Step 4:** Delete the now-dead state fields and the two reset sites that touch them.

- Field `_failedArtLookups` at [line 36](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs): `private readonly HashSet<string> _failedArtLookups = new();`
- Field `_lastCoverArtLookupKey` at [line 38](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs): `private string? _lastCoverArtLookupKey;`
- Reset in `OnDeviceDisconnected` at [line 663](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs): `_lastCoverArtLookupKey = null;`
- Reset in `OnMetadataChanged` (already removed in Task 2 along with the surrounding `else` block — verify no stragglers).

**Step 5:** Audit `using` directives at the top of the file. The MB removal does NOT eliminate the `using Microsoft.Extensions.DependencyInjection;` directive (still needed for `IServiceScopeFactory` and `CreateScope`), but verify there are no now-unused namespace imports.

**Step 6: Build + run all tests.**

```bash
dotnet build --configuration Release
dotnet test --configuration Release -v n
```

Expected: 0 warnings, all ~1,697 tests still pass. If a test in `Radio.Fingerprinting.Tests` was indirectly verifying the BT MB call path through DI, update it to not assert on that wiring (unlikely — the Fingerprinting tests target the service directly, not its BT consumer).

**Step 7: Commit**

```bash
git add src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs
git commit -m "$(cat <<'EOF'
refactor(bt): remove deprecated MusicBrainz cover-art fallback paths

MusicBrainz has been deprecated project-wide in favor of SongRec. Remove
the BT-specific MB text-search fallback (OnMetadataChanged) and the
release-ID + text-search retry branches (OnTrackIdentified), the two
helper methods LookupCoverArtAsync and LookupCoverArtByReleaseIdAsync,
and the dead _failedArtLookups + _lastCoverArtLookupKey state fields.
The remaining BT album-art sources are AVRCP (cached via SaveFromUrlAsync,
fixed in the previous commit) and SongRec.CoverArtUrl (Apple Music CDN,
cached via CacheAndSetCoverArtAsync). When neither produces art, the UI
shows the fallback icon — accepted UX.

IMetadataLookupService and Radio.Fingerprinting.MetadataLookupService
remain in place; their broader removal is out of scope here.
EOF
)"
```

---

### Task 4: Audit SongRec → BT album-art wiring (read-only)

**Files:** none changed — this task is a read-only audit checklist. If a gap is found, Builder pauses and reports back to Planner.

**Step 1:** Verify the event subscription is intact. Confirm at [`BluetoothAudioSource.cs:94`](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs):

```csharp
if (_identificationService != null)
{
  _identificationService.TrackIdentified += OnTrackIdentified;
}
```

**Step 2:** Verify `NeedsFingerprintingLookup` is set and `RequestImmediateIdentification` is called when AVRCP metadata is incomplete OR Shazam-for-all is enabled. Confirm at [`BluetoothAudioSource.cs:753–759`](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs):

```csharp
var hasIncompleteMetadata = string.IsNullOrEmpty(e.Title) || string.IsNullOrEmpty(e.Artist);
NeedsFingerprintingLookup = hasIncompleteMetadata || FpOptions.UseShazamForAllSources;

if (NeedsFingerprintingLookup)
{
  _identificationService?.RequestImmediateIdentification();
}
```

**Step 3:** Verify `OnTrackIdentified` routes `e.Track.CoverArtUrl` through `CacheAndSetCoverArtAsync` (post-Task-3). Confirm at the new (post-Task-3) `OnTrackIdentified` body:

```csharp
if (!hasArt && !string.IsNullOrEmpty(e.Track.CoverArtUrl) && _serviceScopeFactory != null)
{
  _ = CacheAndSetCoverArtAsync(e.Track.CoverArtUrl, e.Track.Title, e.Track.Artist);
}
```

**Step 4:** Verify `BackgroundIdentificationService` is DI-registered AND injected into `BluetoothAudioSource` in the production code path. From `src/Radio.Infrastructure/DependencyInjection/`:

```bash
grep -rn "BackgroundIdentificationService" src/Radio.Infrastructure/DependencyInjection/
grep -rn "AddSingleton.*BackgroundIdentificationService\|AddHostedService.*BackgroundIdentificationService" src/
```

Confirm at least one DI registration AND that `BluetoothAudioSource`'s ctor parameter `BackgroundIdentificationService? identificationService = null` is not actually null in production (a `null` here would silently disable the SongRec path).

**Step 5:** Verify `SoundFlowAudioTap` (the modifier that produces the fingerprint window) is added to the master mixer when BT is active. The audit only needs to confirm the modifier is present somewhere in the pipeline (per [project memory](../../CLAUDE.md): "FingerprintTap" is part of the chain `Sources → MasterMixer → Modifiers (Balance → Limiter → FingerprintTap → VisualizationTap)`).

```bash
grep -rn "SoundFlowAudioTap\|AddModifier" src/Radio.Infrastructure/Audio/SoundFlow/ src/Radio.Infrastructure/Audio/Fingerprinting/
```

**Step 6:** No code change, no commit if all checks pass. If any check FAILS, Builder stops, captures the gap (file:line of the missing wiring), and reports back to Planner before continuing. Planner will then re-scope.

---

### Task 5: Test — AVRCP `file://` URL handling (Bug A regression)

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs`
- Modify: `src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs` (extend `SimulateMetadataChange` to accept an optional `albumArtUrl`)

**Step 1:** Extend the mock to forward `AlbumArtUrl` so tests can simulate it. In `MockBluetoothService.cs`, replace the existing `SimulateMetadataChange` ([lines 143–151](../../src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs)):

**Before:**
```csharp
public void SimulateMetadataChange(string title, string artist)
{
  MetadataChanged?.Invoke(this, new BluetoothPlaybackMetadata
  {
    Title = title,
    Artist = artist,
    Album = "Mock Album"
  });
}
```

**After:**
```csharp
public void SimulateMetadataChange(string title, string artist, string? albumArtUrl = null)
{
  MetadataChanged?.Invoke(this, new BluetoothPlaybackMetadata
  {
    Title = title,
    Artist = artist,
    Album = "Mock Album",
    AlbumArtUrl = albumArtUrl
  });
}
```

The default `null` preserves every existing caller. `BluetoothPlaybackMetadata.AlbumArtUrl` exists at [IBluetoothService.cs:27](../../src/Radio.Core/Interfaces/Audio/IBluetoothService.cs).

**Step 2:** The new tests need a real `AlbumArtCacheService` instance (concrete type, not interface — see [`BluetoothAudioSource.cs:31`](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs)). The cache writes under `./data/albumart`, which is fine for tests as long as each test uses a fresh directory. Add a small per-test temp-dir helper. Place the new tests at the bottom of `BluetoothAudioSourceTests.cs`:

```csharp
[Fact]
public async Task MetadataChanged_WithFileSchemeArtUrl_DoesNotStoreRawUrlInMetadata()
{
  // Arrange — wire a real AlbumArtCacheService rooted at a temp dir
  // so SaveFromUrlAsync returns null for file:// (HttpClient throws
  // NotSupportedException, caught inside the cache).
  using var tempDir = new TempCacheDir();
  var cache = new AlbumArtCacheService(NullLogger<AlbumArtCacheService>.Instance);

  await _source.DisposeAsync();
  _source = new BluetoothAudioSource(
    _loggerMock.Object,
    _deviceManagerMock.Object,
    _mockBluetooth,
    _options,
    identificationService: null,
    metricsCollector: _metricsMock.Object,
    serviceScopeFactory: BuildScopeFactory(),
    albumArtCache: cache);

  // Act — simulate AVRCP metadata with a phone-local file:// URI
  _mockBluetooth.SimulateMetadataChange(
    "Song", "Artist",
    albumArtUrl: "file:///data/data/com.android.spotify/cache/art.jpg");

  // Allow the fire-and-forget CacheAvrcpArtAsync task to complete
  await Task.Delay(200);

  // Assert — AlbumArtUrl must NOT be set to the raw file:// URL
  var hasArt = _source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art);
  Assert.False(
    hasArt && art is string s && s.StartsWith("file://"),
    "Raw file:// AVRCP URL must not be propagated to metadata");
}
```

**Step 3:** Add helper types at the bottom of the test class. `BuildScopeFactory()` returns a minimal `IServiceScopeFactory` that produces a scope without `IPlayHistoryRepository` / `ITrackMetadataRepository` (so `UpdateRecentPlayHistoryCoverArtAsync` no-ops cleanly via its `null` guards):

```csharp
private static IServiceScopeFactory BuildScopeFactory()
{
  var services = new ServiceCollection();
  return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
}

private sealed class TempCacheDir : IDisposable
{
  private readonly string _originalCwd = Environment.CurrentDirectory;
  private readonly string _temp = Path.Combine(
    Path.GetTempPath(), $"radio-art-test-{Guid.NewGuid():N}");

  public TempCacheDir()
  {
    Directory.CreateDirectory(_temp);
    Environment.CurrentDirectory = _temp; // AlbumArtCacheService roots at ./data/albumart
  }

  public void Dispose()
  {
    Environment.CurrentDirectory = _originalCwd;
    try { Directory.Delete(_temp, recursive: true); } catch { /* best effort */ }
  }
}
```

Add these `using` directives if not already present:
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Radio.Infrastructure.Audio;
```

> **Note on `Environment.CurrentDirectory`:** changing cwd is a process-global side effect; xUnit runs tests in parallel by default within a collection. To avoid test cross-talk, put the new tests in a non-parallel collection. Add `[Collection("AlbumArtCacheTests")]` at the class level OR mark the new tests `[Fact, Trait("Category", "SerialAlbumArt")]` and configure `xunit.runner.json` to disable parallelism for that trait. **Simplest:** add to the test class:
> ```csharp
> [CollectionDefinition("BluetoothAudioSourceSerial", DisableParallelization = true)]
> public class BluetoothAudioSourceSerialCollection { }
>
> [Collection("BluetoothAudioSourceSerial")]
> public class BluetoothAudioSourceTests : IAsyncDisposable { /* ... existing ... */ }
> ```
> Builder may instead refactor `BluetoothAudioSource` to take `IAlbumArtCacheService` (interface) so the cache can be mocked without touching the filesystem. That's a strictly better refactor (the field is already declared on the ctor; only the interface change ripples to DI). If Builder chooses that route, the field type at [`BluetoothAudioSource.cs:31`](../../src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs) becomes `IAlbumArtCacheService?` and the ctor param becomes the interface — the DI registration at [`AudioServiceExtensions.cs:188–189`](../../src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs) already exposes both. **Builder is empowered to make that switch** and skip the `TempCacheDir` helper.

**Step 4: Run the test.**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothAudioSourceTests.MetadataChanged_WithFileSchemeArtUrl" --configuration Release -v n
```

Expected: 1 PASS.

**Step 5: Commit**

```bash
git add src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs \
        tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs
git commit -m "test(bt): file:// AVRCP URL must not be propagated to metadata"
```

---

### Task 6: Test — AVRCP `https://` URL handling (cache hit path)

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs`

**Step 1:** Add a test that exercises the success path. The real `AlbumArtCacheService` issues an outbound HTTP request — for a unit test we want to avoid the network. Two options:

- **Option A (preferred):** Builder refactored to `IAlbumArtCacheService?` in Task 5 → mock the interface; `SaveFromUrlAsync` returns `"/api/albumart/abc123.jpg"` directly.
- **Option B (if Builder did not refactor):** point the URL at a tiny local HTTP listener that serves a JPEG byte string. Adds complexity; defer to Option A.

Assuming Option A:

```csharp
[Fact]
public async Task MetadataChanged_WithHttpsArtUrl_StoresCachedRelativeUrl()
{
  // Arrange
  var cacheMock = new Mock<IAlbumArtCacheService>();
  cacheMock
    .Setup(c => c.SaveFromUrlAsync("https://example.com/art.jpg"))
    .ReturnsAsync("/api/albumart/abc123.jpg");

  await _source.DisposeAsync();
  _source = new BluetoothAudioSource(
    _loggerMock.Object,
    _deviceManagerMock.Object,
    _mockBluetooth,
    _options,
    identificationService: null,
    metricsCollector: _metricsMock.Object,
    serviceScopeFactory: BuildScopeFactory(),
    albumArtCache: cacheMock.Object);   // requires Task-5 interface refactor

  // Act
  _mockBluetooth.SimulateMetadataChange(
    "Song", "Artist",
    albumArtUrl: "https://example.com/art.jpg");
  await Task.Delay(200);  // let the fire-and-forget task complete

  // Assert
  Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art));
  Assert.Equal("/api/albumart/abc123.jpg", art);
  cacheMock.Verify(c => c.SaveFromUrlAsync("https://example.com/art.jpg"), Times.Once);
}
```

**Step 2: Run.**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothAudioSourceTests.MetadataChanged_WithHttpsArtUrl" --configuration Release -v n
```

Expected: 1 PASS.

**Step 3: Commit**

```bash
git add tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs
git commit -m "test(bt): https:// AVRCP URL goes through cache and stores relative URL"
```

---

### Task 7: Test — SongRec identifies after empty AVRCP → art comes from SongRec path

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs`

**Step 1:** This test does NOT need a real `BackgroundIdentificationService`. It directly invokes the `OnTrackIdentified` handler by raising the `TrackIdentified` event on a `BackgroundIdentificationService`-stand-in. The simplest path: make `OnTrackIdentified` callable from the test via the event-args type.

Inspect [`BackgroundIdentificationService`](../../src/Radio.Fingerprinting/Services/BackgroundIdentificationService.cs) to confirm `TrackIdentified` is `event EventHandler<TrackIdentifiedEventArgs>?` and that `TrackIdentifiedEventArgs.Track` is a constructable model.

```csharp
[Fact]
public async Task TrackIdentified_AfterEmptyAvrcp_CachesSongRecCoverArtUrl()
{
  // Arrange — cache mock returns a /api/albumart URL when called with the SongRec CDN URL
  var cacheMock = new Mock<IAlbumArtCacheService>();
  cacheMock
    .Setup(c => c.SaveFromUrlAsync("https://itunes.apple.com/.../art.jpg"))
    .ReturnsAsync("/api/albumart/songrec-abc.jpg");

  // Wire a real BackgroundIdentificationService stub (or a Moq derivative) so
  // we can raise TrackIdentified from the test. Simpler: extract the raise via
  // a test-only helper on the source, OR derive a TestableBackgroundIdentificationService
  // that exposes a public RaiseTrackIdentified(track) helper.
  var identificationServiceStub = new TestableBackgroundIdentificationService();

  await _source.DisposeAsync();
  _source = new BluetoothAudioSource(
    _loggerMock.Object,
    _deviceManagerMock.Object,
    _mockBluetooth,
    _options,
    identificationService: identificationServiceStub,
    metricsCollector: _metricsMock.Object,
    serviceScopeFactory: BuildScopeFactory(),
    albumArtCache: cacheMock.Object);

  // AVRCP delivers only Title (Spotify/YouTube-style) — Artist empty triggers fingerprinting
  _mockBluetooth.SimulateMetadataChange("Some Song", "", albumArtUrl: null);
  Assert.True(_source.NeedsFingerprintingLookup);

  // Act — SongRec identifies the track later
  identificationServiceStub.RaiseTrackIdentified(new TrackIdentifiedEventArgs
  {
    Track = new IdentifiedTrack
    {
      Title = "Some Song",
      Artist = "Real Artist",
      CoverArtUrl = "https://itunes.apple.com/.../art.jpg"
    }
  });
  await Task.Delay(200);

  // Assert
  Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art));
  Assert.Equal("/api/albumart/songrec-abc.jpg", art);
}
```

**Step 2:** Add the `TestableBackgroundIdentificationService` stub at the bottom of the test file (or in a shared `tests/Radio.Infrastructure.Tests/Audio/Fakes/` directory). It must subclass or stand-in for `BackgroundIdentificationService` such that:

- The ctor param of `BluetoothAudioSource` accepts it (the param type is `BackgroundIdentificationService?`).
- It exposes a public method to raise `TrackIdentified`.

If `BackgroundIdentificationService` is sealed or its ctor is too heavy to call, **Builder makes the class non-sealed** AND adds an internal `protected virtual void RaiseTrackIdentified(TrackIdentifiedEventArgs)` for test use — a tiny change but a real one. Document that change in the same commit. Alternatively, if `BackgroundIdentificationService.TrackIdentified` is a public event the test can `Raise` directly via Moq if there's an interface to mock; the production code references the concrete class so a real-or-derived instance is required.

Inspect the class first. If extending it for testability is not trivial (e.g., heavy ctor), defer this test to the next sprint by marking it `[Fact(Skip = "Requires BackgroundIdentificationService test-extensibility refactor — tracked in FUTURE-WORK")]` and proceed. Logging the skip and the rationale is preferable to spending a day refactoring a service for one test.

**Step 3: Run / skip.**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothAudioSourceTests.TrackIdentified_AfterEmptyAvrcp" --configuration Release -v n
```

Expected: 1 PASS, or 1 SKIP with the documented reason.

**Step 4: Commit**

```bash
git add tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs \
        $(if changed: src/Radio.Fingerprinting/Services/BackgroundIdentificationService.cs)
git commit -m "test(bt): SongRec identification caches CoverArtUrl when AVRCP empty"
```

---

### Task 8: Test — empty AVRCP + no SongRec result → no broken `<img>` (fallback)

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs`

**Step 1:** This test verifies the "no art" terminal case. AVRCP has no `ArtUrl`; SongRec returns a track with **no** `CoverArtUrl`; the metadata bag must end with `AlbumArtUrl` absent (or empty), NOT set to a broken URL.

```csharp
[Fact]
public async Task TrackIdentified_WithNoCoverArtUrl_LeavesAlbumArtUrlAbsent()
{
  // Arrange — cache mock should NEVER be called (no URL to download)
  var cacheMock = new Mock<IAlbumArtCacheService>(MockBehavior.Strict);

  var identificationServiceStub = new TestableBackgroundIdentificationService();

  await _source.DisposeAsync();
  _source = new BluetoothAudioSource(
    _loggerMock.Object,
    _deviceManagerMock.Object,
    _mockBluetooth,
    _options,
    identificationService: identificationServiceStub,
    metricsCollector: _metricsMock.Object,
    serviceScopeFactory: BuildScopeFactory(),
    albumArtCache: cacheMock.Object);

  _mockBluetooth.SimulateMetadataChange("Mystery Song", "", albumArtUrl: null);

  // Act — SongRec identifies but has no cover art
  identificationServiceStub.RaiseTrackIdentified(new TrackIdentifiedEventArgs
  {
    Track = new IdentifiedTrack
    {
      Title = "Mystery Song",
      Artist = "Mystery Artist",
      CoverArtUrl = null
    }
  });
  await Task.Delay(200);

  // Assert — AlbumArtUrl must be absent (or empty string)
  var hasArt = _source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art);
  Assert.False(
    hasArt && art is string s && !string.IsNullOrEmpty(s),
    "AlbumArtUrl must remain absent when neither AVRCP nor SongRec provides art");

  // And the cache was never touched
  cacheMock.VerifyNoOtherCalls();
}
```

**Step 2: Run.**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "BluetoothAudioSourceTests.TrackIdentified_WithNoCoverArtUrl" --configuration Release -v n
```

Expected: 1 PASS, or 1 SKIP (if Task 7 was skipped — same root cause).

**Step 3: Commit**

```bash
git add tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs
git commit -m "test(bt): no AVRCP + no SongRec art -> AlbumArtUrl stays absent (UI fallback)"
```

---

### Task 9 (Nice-to-have): Log when DI degrades the cache path

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`

**Step 1:** When the AVRCP fast path receives a non-empty URL but `_albumArtCache == null` or `_serviceScopeFactory == null` (rare; means DI didn't wire optional deps), the metadata stays empty silently. Log it so the next debug session doesn't get fooled into thinking AVRCP didn't deliver art. Inside `OnMetadataChanged` (Task 2's new branch):

```csharp
if (!string.IsNullOrEmpty(e.AlbumArtUrl))
{
  if (_albumArtCache != null && _serviceScopeFactory != null)
  {
    _ = CacheAvrcpArtAsync(e.AlbumArtUrl, e.Title, e.Artist);
  }
  else
  {
    Logger.LogWarning(
      "AVRCP delivered ArtUrl '{Url}' but album-art cache or scope factory is null — " +
      "BT album art will not appear. Check DI registration of AlbumArtCacheService.",
      e.AlbumArtUrl);
  }
}
```

**Step 2: Commit**

```bash
git add src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs
git commit -m "feat(bt): warn when AVRCP art arrives but album-art cache deps are null"
```

> Skip this task if Tasks 5–8 covered the case via cache-mock-strict assertions and the log line feels redundant. It's the cheapest possible observability win, ~5 LOC, and worth keeping.

---

### Task 10: Full build + test gate

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```

Expected: 0 warnings; all ~1,697 tests pass.

---

### Task 11: Deploy + UAT on `radio` Ubuntu host

```bash
pwsh.exe -NoProfile -Command "& './deploy/Deploy-ToLinux.ps1' -TargetHost radio -Runtime linux-x64"
```

Then execute the Manual UAT plan (§6 below). **Builder pauses for Mark's UAT sign-off before opening the PR.**

---

### Task 12: Open PR

```bash
git push -u origin fix/bt-album-art

gh pr create --title "fix(bt): display album art for BT source (cache AVRCP URL, drop MB)" --body "$(cat <<'EOF'
## Summary

Fixes BT album art not appearing in the UI. Two changes:

1. **AVRCP fast path now caches the URL** via `AlbumArtCacheService.SaveFromUrlAsync` instead of storing the raw URL in metadata. AVRCP `Track.ArtUrl` is usually a `file:///data/data/com.android.<player>/cache/art.jpg` URI on the phone's local filesystem — sending it raw to the browser produced `img.onerror` and the fallback icon. The cache downloads HTTP(S) URLs and returns null for `file://` (HttpClient throws `NotSupportedException`, caught internally); on null we leave `AlbumArtUrl` absent and SongRec (if it identifies the track) populates it via `OnTrackIdentified`.
2. **Removed deprecated MusicBrainz cover-art fallback paths** from `BluetoothAudioSource` per project policy (MB deprecated in favor of SongRec). Deleted the MB text-search branch in `OnMetadataChanged`, the MB release-ID and text-search retry branches in `OnTrackIdentified`, the two helper methods (`LookupCoverArtAsync`, `LookupCoverArtByReleaseIdAsync`), and the dead state fields (`_failedArtLookups`, `_lastCoverArtLookupKey`). `IMetadataLookupService` + `MetadataLookupService` left in place; their broader removal is a separate PR.

When neither AVRCP nor SongRec produces art, the UI shows the fallback icon — accepted UX per user.

## Test plan

- [x] AVRCP `file://` URL → metadata `AlbumArtUrl` stays absent (not raw `file://` value)
- [x] AVRCP `https://` URL → cache hit → metadata holds `/api/albumart/{hash}.jpg`
- [x] AVRCP empty + SongRec identifies with CoverArtUrl → metadata holds `/api/albumart/{hash}.jpg`
- [x] AVRCP empty + SongRec without CoverArtUrl → metadata `AlbumArtUrl` stays absent
- [x] Existing `BluetoothAudioSourceTests` regression suite passes
- [x] Manual UAT on `radio` Ubuntu host (see UAT scenarios in PR description)

## UAT (manual, on `radio`)

1. Pair phone via Spotify (typically AVRCP delivers only title) → confirm art appears within ~5 s from SongRec OR fallback icon if SongRec doesn't match
2. Play track on phone in a local-music player that ships `Track.ArtUrl` → confirm art appears immediately (cached AVRCP path)
3. Browser DevTools → Network: confirm `<img src>` is `/api/albumart/{hash}.jpg`, never `file://...`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Merge once Mark approves.

---

## 5. Test plan summary

| Coverage | File | Tests |
|---|---|---|
| AVRCP `file://` URL → no raw propagation | `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs` (Task 5) | 1 |
| AVRCP `https://` URL → cache hit + relative URL stored | `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs` (Task 6) | 1 |
| SongRec identifies after empty AVRCP → SongRec art cached | `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs` (Task 7) | 1 |
| Empty AVRCP + empty SongRec → fallback (no broken `<img>`) | `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs` (Task 8) | 1 |
| Mock extension | `src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs` (Task 5) | n/a (test infra) |
| Regression | full `dotnet test --configuration Release` | ~1,697 tests, 0 regressions |

Builder should not skip the test commits; each one gates the next step (Task 5 establishes the mock extension Tasks 6–8 reuse).

---

## 6. Manual UAT plan (Mark, on `radio` Ubuntu host)

After Builder runs `Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`:

### Scenario A — Phone player that does NOT ship AVRCP art (Spotify / YouTube Music / Apple Music)

1. Make sure the phone is paired to the Radio Console via Bluetooth (the TP-Link UB500 adapter — see [project memory](../../CLAUDE.md) BT boundary section).
2. Open Spotify (or YouTube Music) on the phone. Start playing a popular, easily-identifiable track (e.g., a Top-40 single — SongRec needs ~5 s of audio to identify).
3. Activate the Bluetooth source in the Radio Console UI at `http://radio:5002`.
4. **Within ~10 s after playback starts**, confirm one of two acceptable outcomes:
   - **SongRec identifies:** Album art appears in the now-playing card, sourced from `/api/albumart/{hash}.jpg`.
   - **SongRec does NOT identify** (rare for popular tracks, common for podcasts / obscure tracks): the now-playing card shows the default-album-art icon. **No broken-image icon, no flickering, no console errors in DevTools.**
5. Open browser DevTools → Network panel. Filter on `img`. Verify the art `<img src>` is either `/api/albumart/{hash}.jpg` OR `/images/default-album-art.png`. **It must NEVER be `file:///data/data/...`.**
6. Tail journalctl during the test to confirm the cache path is exercised:
   ```bash
   ssh mmack@radio "journalctl -u radio-api -n 200 --no-pager | grep -E 'Cached AVRCP album art|Cover art found|cover art|AVRCP art URL not cacheable'"
   ```
   - Spotify path: expect `AVRCP art URL not cacheable for '<title>' by '<artist>' (URL: file:///...); waiting for SongRec` followed by `Cover art found for '<title>' by '<artist>': /api/albumart/...` (when SongRec resolves).

### Scenario B — Phone player that DOES ship AVRCP `http://` art (local music player, some podcast apps)

1. On the phone, install a local music player that exposes album art via MPRIS (e.g., "Music" by Sony, "Phonograph", VLC for Android). Load a track with embedded cover art.
2. Play the track. Verify in the Radio Console UI that album art appears **immediately** (within ~1 s — no SongRec wait).
3. journalctl should show `Cached AVRCP album art for '<title>' by '<artist>': /api/albumart/{hash}.jpg`.

### Scenario C — Track change leaks no art from previous track

1. With BT source active and Spotify playing a track WITH SongRec-resolved art (Scenario A), skip to the next track via phone.
2. **Immediately after the skip**, verify the now-playing card does NOT show the previous track's art. Acceptable behavior: brief fallback icon → resolves to new art within ~5 s, OR stays on fallback icon if SongRec doesn't match the new track.

### Scenario D — Source switch away from BT and back

1. From BT (Scenario A) → switch to Radio source → switch back to BT (same phone, same track).
2. Album art should re-resolve. (No regression in this scenario; just a sanity check that the metadata bag isn't sticky across source switches.)

### Pass criteria

All four scenarios pass. DevTools Network shows **zero** `file://` URLs for album art across the whole session. journalctl shows no `ERROR` lines mentioning `AlbumArtCache` or `LookupCoverArt`.

If any scenario fails, capture journalctl (`journalctl -u radio-api -n 500 --no-pager > /tmp/uat-fail.log`) and DevTools Network HAR, feed back to Planner.

---

## 7. Risk notes

1. **SongRec timing on BT audio.** SongRec needs roughly 5–10 s of audio buffered in the `SoundFlowAudioTap` window before it can identify a track. If the user switches tracks rapidly on the phone (e.g., shuffling through songs), the previous track's identification result may arrive AFTER the new AVRCP `Title`/`Artist` has already updated. The existing `OnTrackIdentified` handler guards against this via `MetadataInternal.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out _)` — but the title-mismatch case (SongRec identifies the previous track AFTER the new track's AVRCP arrived) currently overwrites the new track's art with the old track's art. **Not introduced by this PR** (pre-existing); flagged here because UAT may surface it. If observed, follow-up plan: track a `_currentTrackKey = $"{title}|{artist}"` in `OnMetadataChanged` and reject `OnTrackIdentified` results whose `e.Track.Title|e.Track.Artist` don't match.

2. **Race: AVRCP fires before SongRec has a result.** Most common path. AVRCP arrives with no art → we cache attempt returns null → `AlbumArtUrl` absent → SongRec finishes later → `OnTrackIdentified` populates art. No race: the second write wins. Verified by reading the handler bodies; no test gap.

3. **Race: AVRCP fires `http://` URL, SongRec also resolves.** AVRCP cache succeeds → `AlbumArtUrl` set to `/api/albumart/A.jpg` → SongRec finishes → `OnTrackIdentified` sees `hasArt == true` and skips its `CacheAndSetCoverArtAsync` call. The AVRCP art wins. **Acceptable** — AVRCP art is what the phone wants displayed; SongRec's Apple Music CDN art is a fallback for when the phone gives us nothing.

4. **`SaveFromUrlAsync` fails for `http://` URL** (network unreachable, slow phone-hosted server, etc.). `CacheAvrcpArtAsync` logs at Warning and leaves `AlbumArtUrl` absent. SongRec, if it identifies the track, will populate it later. No retry — if it fails once, the next AVRCP `MetadataChanged` event (next track) will try again. **Acceptable.**

5. **`MockBluetoothService.SimulateMetadataChange` signature change is source-compatible** (added an optional parameter with default). No existing call site requires updating. Verified by grep of `SimulateMetadataChange` across all test projects.

6. **`TempCacheDir` test helper mutates `Environment.CurrentDirectory`** (process-global, breaks parallel xUnit). Mitigation in Task 5 step 3: put the affected tests in a `[Collection]` with `DisableParallelization = true`. Better long-term fix (recommended in Task 5's note): refactor `BluetoothAudioSource` to take `IAlbumArtCacheService?` so the cache can be mocked instead of constructed.

7. **Behavior change: BT play history now misses MB-resolved art for tracks the user previously got art for via MB.** Some tracks where AVRCP delivered nothing AND SongRec failed to identify previously got art from MB. After this PR they will get the fallback icon. **Per user, this is the explicitly accepted UX** for the deprecated MB-removal direction; not a regression.

8. **`IMetadataLookupService.MetadataLookupService` is no longer called from `BluetoothAudioSource` but remains DI-registered.** Other sources or future callers may still use it. Dead-code analyzer warnings may surface — Builder addresses any that appear in the Release build's 0-warning gate. If the analyzer flags `MetadataLookupService` itself as unused, that's the signal to schedule the broader cleanup PR.

---

## 8. Out of scope (explicit)

- Removal of `IMetadataLookupService` interface or `Radio.Fingerprinting.Services.MetadataLookupService` implementation (separate PR — wider blast radius; needs an audit of all consumers beyond `BluetoothAudioSource`).
- Time-bounding `_failedArtLookups` (becomes irrelevant — the field is deleted in Task 3).
- Adding a `_currentTrackKey` mismatch guard in `OnTrackIdentified` (Risk #1 above — pre-existing; deferred unless UAT surfaces it).
- Web → API album-art proxy changes (debug investigation confirmed it works).
- Changes to `AlbumArtCacheService` itself (TTL, cache size limits, eviction policy).
- Adding `data:` URI scheme handling to the cache (no current player ships these via AVRCP; defer until needed).
- Changes to RotaryPhone or any non-BT-audio surface area.
