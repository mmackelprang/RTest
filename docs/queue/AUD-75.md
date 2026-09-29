# `AUD-75` — the file player's media root on the box is a development checkout path

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-28 by the Phase 2d Builder while measuring log volume (`LOG-12`). **Filed, not fixed.** The fix is configuration on the appliance, so it is the owner's call.

## The evidence

On 2026-09-27, radio-api's file sink has 48 `WRN` lines from `FileBrowser`, all in one 11-second burst (08:24:02–08:24:13). No other day this week has any. For example:

```
Path traversal attempt blocked: /opt/radio-console/media/audio/08-I'm Not In Love.mp3 resolved to
/opt/radio-console/media/audio/08-I'm Not In Love.mp3 (outside /home/mmack/RTest/src/Radio.API/media/audio)
```

Real media files in the appliance's own media directory are rejected as traversal attempts.

## Owner ruling 2026-09-29 — the library is `nas.local`'s `multimedia` share, at `/mnt/nas_media`

The owner: *"/mnt/nas_media should point to the nas server nas.local to the multimedia share there. That is where the bulk of my MP3 files are stored."*

**The mount is fixed on the box (done 2026-09-29, this session).** Why `/mnt/nas/music` "listed nothing": its fstab line was `//nas/multimedia/music`, and the bare name `nas` does not resolve on the box (`mount error: could not resolve address for nas`), so `mnt-nas-music.mount` failed at every boot. `nas.local` resolves (`192.168.86.47`, SMB 445 open) and the existing `/etc/smbcredentials` authenticate. Now:

```
//nas.local/multimedia /mnt/nas_media cifs credentials=/etc/smbcredentials,uid=1000,gid=1000,iocharset=utf8,nofail,_netdev,x-systemd.automount,x-systemd.mount-timeout=20 0 0
```

- The old `/mnt/nas/music` line is **commented out**, not deleted; the pre-change file is `/etc/fstab.bak-20260929-110405`.
- `x-systemd.automount` (wanted by `remote-fs.target`) mounts on first access, so boot never waits on the NAS or on mDNS, and `nofail` keeps a NAS outage from failing boot. `uid=1000` is `mmack`, which `radio-api` runs as.
- Verified: `/mnt/nas_media` lists `Music`, `Photos`, `Samples`, `Video`, `@Recycle`; `Music` has 349 artist directories.

**What this changes for the fix below.** The music root is `/mnt/nas_media/Music`, not `/mnt/nas/music`, and `/mnt/nas/music` is now **never** mounted. Every repo reference to it needs to move with fix 1: `src/Radio.API/appsettings.json` (`BookmarkedPaths` → "NAS Music Library"), `deploy/debian-x64/appsettings.Production.json` (`RootDirectory` — seed-only, so the box's installed copy needs the same edit by hand), and the `AudioFileEventSourceFactory.cs:77` doc comment. The box's config-store row still wins over all of them until it is deleted or corrected. Fixes 2 and 3 are unchanged.

### Repo half done 2026-09-29 (owner: *"make the file browser in the radio console default to start looking in `/mnt/nas_media/Music`"*)

- `src/Radio.API/appsettings.json`: the `music`-tagged "NAS Music Library" bookmark is `/mnt/nas_media/Music`. That bookmark is what the queue's **Add Files** dialog opens by default (`QueueHistoryPanel` passes `PreferredBookmarkTag = "music"`; `FileBrowserDialog.FindDefaultBookmark` opens it when accessible). Before this, it named the never-mounted `/mnt/nas/music`, was inaccessible, and the dialog fell through to "Local Audio Files". **Reaches the box on the next deploy** (the API's `appsettings.json` is deployed; no store row overrides the bookmarks).
- `deploy/debian-x64/appsettings.Production.json`: `RootDirectory` `/mnt/nas_media/Music`, `AllowedBrowseDirectories` `[ "/mnt/nas_media" ]`. Seed-only, so this does **not** change the box.
- Placeholder in `SystemConfigPage.razor` and the `AudioFileEventSourceFactory` doc comment updated. The `/mnt/nas/music` strings in `FileBrowserDialogTests` are prefix-logic fixtures, not configuration, and were left.
- Checked: moving the root does not affect the phone ring sound. `phoneintegration:ringSoundPath` is the relative `media/sounds/phone-ring.wav`, which `PhoneCallIntegrationService` tests with `File.Exists` against the working directory `/opt/radio-console`, where it does not exist, so it already plays TTS only (pre-existing, unrelated).

### Box half — NOT done (the session's permission gate refused remote config writes; the owner runs it)

```bash
ssh mmack@radio
T=$(date +%Y%m%d-%H%M%S); P=/opt/radio-console/api/appsettings.Production.json; D=/opt/radio-console/data/config/configuration.db
cp -p $P $P.bak-$T && sqlite3 $D ".backup $D.bak-$T"
# In $P set  "FilePlayer": { "RootDirectory": "/mnt/nas_media/Music", "AllowedBrowseDirectories": [ "/mnt/nas_media" ] }
sqlite3 $D "delete from Config_sqlite where Key='fileplayer:rootDirectory';"
sudo systemctl restart radio-api
```

Until then the file player's root is still the empty dev path, so relative browsing and root-checked playback stay broken; the **Add Files** default above does not depend on it. Fix 2 (bookmark paths rejected by `FileBrowser.GetFullPath`) is still open and is why queued `/opt/radio-console/media/audio/...` files fail.

### Fix 2 done 2026-09-29 — and a correction to what it was for

⛔ **Correction: queued files under `/opt/radio-console/media/audio` were never refused at playback.** This dossier (and the session that shipped #711) said they were. Checked in code: only `FilesController` uses `IFileBrowser`; the queue and `FilePlayerAudioSource.GetFullPath` accept absolute paths without consulting it, and `FilesController.QueueFiles`/`PlayFile` validate absolute paths with `IsPathAllowed`, which already honoured bookmarks. The box's file sink shows no queue-path refusals.

**What fix 2 actually fixes** (`FileBrowser.GetFullPath`, `IsWithinAllowedDirectory`):
- **Browsing a bookmark outside the root.** `FilesController.ListFilesAbsolute` asks `FileBrowser.GetFileInfoAsync` for each file's metadata. For "Local Audio Files" that threw, so every file came back **without artist/album/duration** and logged one `Path traversal attempt blocked` warning — the 48-line burst above. It now accepts the root, `AllowedBrowseDirectories` and `BookmarkedPaths`, the same set as `IsPathAllowed`.
- **A root-prefix hole.** The root check was a bare `StartsWith`, so a root of `/mnt/nas_media/Music` admitted `/mnt/nas_media/MusicX`. Every comparison now carries a trailing separator.
- Tests: six in `FileBrowserTests` (bookmark and allowed directory accepted; root-prefix sibling, bookmark-prefix sibling, unrelated absolute path and `../` escape rejected). Run against the old `GetFullPath`, the two acceptance tests and the root-prefix test fail.

Fix 3 (log once when a configured root is missing or empty) is still open.

## Where the root comes from (measured on the box, read-only)

- `FileBrowser.GetFullPath` (`src/Radio.Infrastructure/Audio/Services/FileBrowser.cs`) uses `FilePlayer:RootDirectory`. If that path is relative, it is combined with `RootDir` or the working directory, which is `/opt/radio-console` on the box.
- `api/appsettings.Production.json` on the box says `"RootDirectory": "/mnt/nas/music"`. **It is overridden.**
- The SQLite config store (`/opt/radio-console/data/config/configuration.db`, table `Config_sqlite`) holds:
  ```
  fileplayer:rootDirectory | /home/mmack/RTest/src/Radio.API/media/audio | 2026-02-12T18:39:11Z
  ```
  `Program.cs` adds the SQLite bridge **after** the appsettings files, so this row wins. It was written in February, most likely when a development-era config was imported or saved. `/home/mmack/RTest/src/Radio.API/media/audio` does exist on the box, **and it is empty**.
- A second problem sits behind the first, and it is a mismatch between two layers rather than a design choice. `FilePlayer:BookmarkedPaths` in the shipped `appsettings.json` lists `/opt/radio-console/media/audio` ("Local Audio Files"), and the persisted queue (`FilePlayerPreferences:QueueItems`) holds absolute paths under it. `FilesController.IsPathAllowed` explicitly allows `AllowedBrowseDirectories` and `BookmarkedPaths`, but `FileBrowser.GetFullPath` ignores them and checks only `RootDirectory`. So even a correct `/mnt/nas/music` root would still reject the "Local Audio Files" bookmark. `GetFullPath` also uses `StartsWith` with no trailing separator (`/mnt/nas` would admit `/mnt/nasty`); the controller already guards against that.
- Order check: `WebApplication.CreateBuilder` loads `appsettings.{Environment}.json`, environment variables and the command line; `Program.cs` adds the SQLite bridge after all of them, so a store row overrides every one.

## Fix shapes (owner decides)

1. Delete (or correct) the `fileplayer:rootDirectory` row in the box's config store. That restores the Production value, `/mnt/nas/music`. Check `ls /mnt/nas/music` first: it listed nothing on 2026-09-28, so it may not be mounted.
2. Make `FileBrowser.GetFullPath` honour the same allow-list as `FilesController.IsPathAllowed` (bookmarks and allowed browse directories), with the trailing-separator check.
3. Consider whether a config-store key that points at a directory that does not exist, or is empty, should be logged once at startup. It was invisible for seven months.

## Verification

Count `Path traversal attempt blocked` in the file sink after queueing a file from "Local Audio Files" (0 expected). Browse both bookmarks from the Files page.
