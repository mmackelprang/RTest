# Music is not bundled

This folder ships empty. Commercial music is not distributed with this repository.

On the appliance, the music library lives on a NAS mount at `/mnt/nas_media/Music`, which is the
`"NAS Music Library"` bookmark (tag `music`) in `FilePlayer:BookmarkedPaths` in
`src/Radio.API/appsettings.json`. Point that bookmark at your own library, or drop files here for
local testing; nothing in the build, the tests or the deploy depends on this folder having content.

The bundled alarm and alert sounds (`../alarm/`, `../alerts/`) are separate; see
`docs/known-issues-and-future-work.md` for their provenance status.
