# Progress Log

## Session: 2026-02-09

### Phase 1: Requirements & Discovery
- **Status:** complete
- Explored TTSFactory voice methods, Web UI, API endpoints, SQLite patterns
- Google voices are hardcoded (17 en-US/en-GB), Azure already calls API with 24h in-memory cache
- Designed: 2 new SQLite tables (TTSVoiceCache, TTSVoiceFavorites) in fingerprints.db
- Plan written in task_plan.md

### Phase 2: SQLite Persistence Layer
- **Status:** complete
- Added TTSVoiceCache + TTSVoiceFavorites tables to FingerprintDbContext
- Created ITTSVoiceRepository interface (Core) + SqliteTTSVoiceRepository (Infrastructure)
- Registered as singleton in DI (must match TTSFactory singleton lifetime)

### Phase 3: TTSFactory Refactoring
- **Status:** complete
- Added ITTSVoiceRepository dependency, new interface methods (RefreshVoices, Favorites)
- Replaced hardcoded GetGoogleVoicesAsync with FetchGoogleVoicesAsync (calls Google Cloud TTS API)
- Replaced in-memory cached GetAzureVoicesAsync with FetchAzureVoicesAsync (calls Azure API)
- Added SortVoices: favorites first, then by price tier (Standard cheapest), then by language (US > UK > other)
- Removed old Azure cache fields and GetDefaultAzureVoices
- Added PriceTier extraction from Google voice names and Azure VoiceType

### Phase 4: API Endpoints & Web UI
- **Status:** complete
- Added 3 new API endpoints: POST refresh, POST/DELETE favorite
- Updated TTSVoiceInfoDto with IsFavorite + PriceTier in both API and Web DTOs
- Added RefreshTTSVoicesAsync, Set/RemoveTTSVoiceFavoriteAsync to SourcesApiService
- Updated SystemConfigPage: voice dropdown for all cloud engines, "Scan for Voices" button, star toggle

### Phase 5: Testing & Verification
- **Status:** complete
- Build: 0 warnings, 0 errors
- Updated 2 TTSFactory tests (hardcoded → cache-based behavior)
- Fixed DI lifetime mismatch (ITTSVoiceRepository scoped → singleton)
- All tests pass: Core 35, RTLSDRCore 6, Infra 655, API 198, Web 130, Integration 82+3skip+1 flaky

---
*Update after completing each phase or encountering errors*
