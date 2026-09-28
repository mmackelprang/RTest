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
