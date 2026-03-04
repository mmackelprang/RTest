# Progress Log

## Session: 2026-03-04

- Explored current fingerprinting architecture (ChromaprintFingerprintService, BackgroundIdentificationService, MetadataLookupService, SoundFlowAudioTap)
- Confirmed: File fingerprinting works well. Live sources (vinyl, radio) struggle with AcoustID
- Observed vinyl low-frequency rumble on spectrum analyzer (strong sub-100Hz even during silence) — will add high-pass filter before fpcalc
- User confirmed: direct-drive turntable, no wow/flutter — pitch correction unnecessary
- Documented alternative metadata sources: RDS (FM), ACRCloud (cloud, 5K/month), SongRec (Shazam, unlimited)
- Created 6-phase plan: preprocessing → SongRec → ACRCloud → RDS → strategy architecture → UI
- Ready to begin Phase 1 (audio preprocessing)
- Implemented 2nd-order Butterworth high-pass filter (80Hz cutoff) in ChromaprintFingerprintService
- Filter applied to all live-source fingerprints (vinyl, radio, USB) before normalization + fpcalc
- File-based fingerprinting (GenerateFingerprintFromFileAsync) is unaffected
- Updated pipeline integration tests: assertions now check hash validity + similarity (common prefix) instead of exact match, since filter intentionally modifies audio
- All 1,391 tests pass
- Ready to deploy and test with vinyl
- Tested vinyl with Cars Greatest Hits — 786-char hash (good) but AcoustID returned no results across all duration fallbacks
- Confirmed: high-pass filter alone insufficient for vinyl — Phase 2 (SongRec) needed

## Session: 2026-03-04 (continued)

- Phase 2: SongRec (Shazam) integration implemented
- Created ISongRecRecognitionService interface in Radio.Core
- Created SongRecRecognitionService: writes temp WAV, invokes `songrec audio-file-to-recognized-song`, parses Shazam JSON
- Added SongRecOptions config (Enabled, SongRecPath, TimeoutSeconds)
- Added MetadataSource.Shazam and LookupSource.SongRec enum values
- Wired into BackgroundIdentificationService as AcoustID fallback for live sources
- Registered in DI (FingerprintingServiceExtensions)
- Added 9 unit tests for SongRec service (parsing, availability, edge cases)
- Fixed Now Playing panel stuck on previous source (subscribed to SourceChanged event)
- All 1,400 tests pass (9 new SongRec tests)
