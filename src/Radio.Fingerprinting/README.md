# Radio.Fingerprinting

Audio fingerprinting and recognition library providing:

- **SongRec (Shazam) integration** — wraps the SongRec binary for audio recognition
- **MusicBrainz metadata lookup** — artist, album, cover art via MusicBrainz + Cover Art Archive APIs
- **Background identification service** — hosted service with duplicate suppression and song change detection
- **SQLite persistence** — fingerprint cache, track metadata, and play history repositories

## Dependencies

- `Radio.Core` — domain interfaces and models
- `Radio.Metrics` — optional metrics collection
- `Microsoft.Data.Sqlite` — database access
- `Microsoft.Extensions.Hosting.Abstractions` — hosted service support

## Usage

Register services via DI in the consuming application. Implement `IFingerprintDataConnection` to provide a SQLite connection, or use the built-in `FingerprintDbContext` from `Radio.Infrastructure`.
