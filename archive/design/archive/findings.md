# Findings: Radio.Fingerprinting Extraction

## Architecture Analysis

### FingerprintDbContext Problem
- Manages 12 SQLite tables, only 3 are fingerprinting-specific (FingerprintCache, TrackMetadata, PlayHistory)
- Non-FP tables: RadioPresets, AudioFiles, CastDeviceCache, Playlists, PlaylistItems, TTSVoiceCache, TTSVoiceFavorites
- Cannot extract DbContext — too many non-FP consumers depend on it
- Solution: IFingerprintDataConnection interface abstracts the connection

### Consumer Impact Analysis
- `Radio.Core.Models.Audio` imported by 75 files — too many to migrate
- `Radio.Core.Interfaces.Audio` imported by 133 files — domain-wide
- `Radio.Core.Events` imported by 12 files — all fingerprinting events
- `FingerprintingOptions` referenced by 28 files (18 code, 10 docs)
- `Radio.Infrastructure.Audio.Fingerprinting` namespace — ~15 implementation consumers

### PlaySource/MetadataSource Enums
- Used by IAudioSampleProvider, IPlayHistoryRepository, PlayHistoryEntry, BackgroundIdentificationService
- Also used by non-FP code: API controllers, AudioManager, source implementations
- Decision: Keep in Radio.Core, Radio.Fingerprinting depends on Radio.Core

### BackgroundIdentificationService Dependencies (700 LOC)
- IMetricsCollector? (optional, from Radio.Metrics)
- IAudioSampleProvider (Radio.Core interface)
- ISongRecRecognitionService, IMetadataLookupService (Radio.Core interfaces)
- IFingerprintCacheRepository, ITrackMetadataRepository, IPlayHistoryRepository (Radio.Core interfaces)
- FingerprintingOptions via IOptionsMonitor
- TrackIdentifiedEventArgs, SongChangedEventArgs (Radio.Core events)
- ALL dependencies are on interfaces in Radio.Core — fully extractable

### What Radio.Fingerprinting Provides (as NuGet)
1. SongRec (Shazam) audio recognition wrapper
2. MusicBrainz metadata + Cover Art Archive lookup
3. Background identification service with duplicate suppression + song change detection
4. SQLite repositories for fingerprint cache, track metadata, play history
5. FingerprintingOptions configuration
