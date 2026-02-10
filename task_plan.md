# Task Plan: Dynamic TTS Voice Retrieval, Caching & Favorites

## Goal
Replace hardcoded TTS voice lists with on-demand API enumeration for Google and Azure engines. Cache voices in the fingerprinting SQLite database. Add a "favorite voices" feature persisted to the same database. Update the Web UI to show voice dropdowns for all cloud engines with favorites at the top. Prioritize low-cost and US/UK English voices in sort order.

## Current Phase
Phase 1: Requirements & Discovery

## Phases

### Phase 1: Requirements & Discovery
- [x] Explore TTSFactory voice methods (Google hardcoded, Azure already dynamic)
- [x] Explore Web UI voice selection (SystemConfigPage.razor)
- [x] Explore SQLite repository patterns (FingerprintDbContext, SqliteRadioPresetRepository)
- [x] Explore API endpoints (SourcesController TTS routes)
- [x] Research Google/Azure voice API response formats and pricing tiers
- [x] Document findings and design approach
- **Status:** complete

### Phase 2: Database & Repository Layer
- [ ] Add `TTSVoiceCache` and `TTSVoiceFavorites` tables to FingerprintDbContext
- [ ] Create `ITTSVoiceRepository` interface in Core
- [ ] Create `SqliteTTSVoiceRepository` implementation in Infrastructure
- [ ] Register in DI (FingerprintingServiceExtensions)
- [ ] Write unit tests for repository
- **Status:** pending

### Phase 3: TTSFactory Voice Enumeration
- [ ] Add `RefreshVoicesAsync(engine)` method — fetches from API, stores in DB cache
- [ ] Replace `GetGoogleVoicesAsync()` hardcoded list with DB cache read (no auto-fetch)
- [ ] Update `GetAzureVoicesAsync()` to use DB cache instead of in-memory 24h cache
- [ ] Add favorite management methods to ITTSFactory
- [ ] Wire favorites + price tier sorting into voice listing
- [ ] Write unit tests for voice enumeration
- **Status:** pending

### Phase 4: API & Web UI
- [ ] Add `POST /api/sources/events/tts/voices/refresh?engine=Google` endpoint
- [ ] Add API endpoints for favorites CRUD
- [ ] Update SystemConfigPage.razor: voice dropdown for all cloud engines
- [ ] Add "Scan for Voices" button per engine
- [ ] Add favorite toggle (star icon) on each voice
- [ ] Show favorites at top, then Standard/free tier, then premium
- [ ] Write bUnit tests
- **Status:** pending

### Phase 5: Testing & Verification
- [ ] `dotnet build --configuration Release` — 0 warnings, 0 errors
- [ ] All existing tests pass
- [ ] New tests pass
- **Status:** pending

### Phase 6: Delivery
- [ ] Commit when user requests
- **Status:** pending

## Design Decisions

### Database: Use FingerprintDbContext (fingerprints.db)
- Already has the DbContext + Repository pattern for similar data
- Repositories registered as scoped, DbContext as singleton
- Two new tables: `TTSVoiceCache`, `TTSVoiceFavorites`

### Voice Cache Table Schema
```sql
CREATE TABLE IF NOT EXISTS TTSVoiceCache (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Engine TEXT NOT NULL,        -- "Google" or "Azure"
  VoiceId TEXT NOT NULL,       -- e.g. "en-US-Standard-A"
  Name TEXT NOT NULL,          -- e.g. "US Standard A (Male)"
  Language TEXT NOT NULL,      -- e.g. "en-US"
  Gender TEXT NOT NULL,        -- "Male", "Female", "Neutral"
  PriceTier TEXT NOT NULL,     -- "Standard", "WaveNet", "Neural2", "Studio", "Neural", etc.
  LastUpdated TEXT NOT NULL,   -- ISO 8601
  UNIQUE(Engine, VoiceId)
);
```

### Voice Favorites Table Schema
```sql
CREATE TABLE IF NOT EXISTS TTSVoiceFavorites (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Engine TEXT NOT NULL,
  VoiceId TEXT NOT NULL,
  AddedAt TEXT NOT NULL,
  UNIQUE(Engine, VoiceId)
);
```

### Google Cloud TTS Voice API
- `GET https://texttospeech.googleapis.com/v1/voices?key={apiKey}&languageCode=en`
- Response: `{ voices: [{ languageCodes: ["en-US"], name: "en-US-Standard-A", ssmlGender: "MALE", naturalSampleRateHertz: 24000 }] }`
- Filter to `en-*` languages
- **Price tier inferred from voice name**: `{locale}-{Tier}-{Letter}`
  - `Standard` — cheapest ($4/M chars, first 4M free/month)
  - `WaveNet` — mid ($16/M chars, first 1M free)
  - `Neural2` — mid ($16/M chars, first 1M free)
  - `Studio` — premium ($100/M chars)
  - `Journey`, `Casual`, `Polyglot` — premium ($16-100/M chars)

### Azure TTS Voice API (already partially implemented)
- `GET https://{region}.tts.speech.microsoft.com/cognitiveservices/voices/list`
- Response includes `VoiceType` field: `"Standard"` (cheap) vs `"Neural"` (premium)
- Filter to `en-*` locales

### Cache Strategy — On-Demand Only
- **No automatic fetching** — voices are only fetched when the user clicks "Scan for Voices"
- If DB cache has voices for the engine, serve from cache
- If DB cache is empty for the engine, the voice dropdown shows "No voices cached — click Scan"
- Stale cache is fine — user refreshes manually when they want new voices
- No cache expiration (user controls refresh)

### Voice Ordering (within each engine)
1. **Favorites** (sorted by name) — visual separator below
2. **Standard/free tier** + US English (`en-US-Standard-*`)
3. **Standard/free tier** + UK English (`en-GB-Standard-*`)
4. **Standard/free tier** + other English
5. **WaveNet/Neural2 tier** + US English
6. **WaveNet/Neural2 tier** + UK English
7. **WaveNet/Neural2 tier** + other English
8. **Premium tier** (Studio, Journey, etc.) + US English
9. **Premium tier** + UK/other English

**Simplified sort key**: `(isFavorite DESC, priceTierRank ASC, languageRank ASC, name ASC)`
- `priceTierRank`: Standard=0, WaveNet/Neural2=1, Studio/Journey/Casual/Polyglot=2, Neural(Azure)=1
- `languageRank`: en-US=0, en-GB=1, other en-*=2

### Price Tier Extraction
**Google**: Parse from voice name — split on `-`, tier is the 3rd segment:
  `en-US-Standard-A` → tier = "Standard"
  `en-US-Neural2-C` → tier = "Neural2"

**Azure**: Use `VoiceType` field from API response directly

### ITTSFactory Extensions
```csharp
// Existing
Task<IReadOnlyList<TTSVoiceInfo>> GetVoicesAsync(TTSEngine engine, CancellationToken ct = default);

// New
Task<int> RefreshVoicesAsync(TTSEngine engine, CancellationToken ct = default);
Task SetVoiceFavoriteAsync(TTSEngine engine, string voiceId, CancellationToken ct = default);
Task RemoveVoiceFavoriteAsync(TTSEngine engine, string voiceId, CancellationToken ct = default);
```

### TTSVoiceInfo Extension
Add two properties to TTSVoiceInfo record:
```csharp
public bool IsFavorite { get; init; }
public string PriceTier { get; init; } = "Standard";
```

### API Endpoints
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/sources/events/tts/voices?engine=Google` | Returns cached voices with favorites+pricing sort |
| POST | `/api/sources/events/tts/voices/refresh?engine=Google` | Fetches from cloud API, stores in DB, returns count |
| POST | `/api/sources/events/tts/voices/favorites` | Add favorite `{ engine, voiceId }` |
| DELETE | `/api/sources/events/tts/voices/favorites` | Remove favorite `{ engine, voiceId }` |

### Web UI Changes
- Voice dropdown for **all cloud engines** (not just Google)
- "Scan for Voices" button with refresh icon next to engine selector
  - Shows spinner during scan, then "Found N voices" snackbar
  - Button disabled if engine has no API key configured
- Each voice item in dropdown shows:
  - Star icon (filled=favorite, click to toggle)
  - Voice name
  - Price tier badge (e.g. "Standard", "Neural2")
- Favorites section at top with visual divider
- If no cached voices: message "No voices cached. Click Scan to discover available voices."

## Files Summary (Estimated)

| Action | File |
|--------|------|
| Modify | `src/Radio.Infrastructure/Audio/Fingerprinting/Data/FingerprintDbContext.cs` |
| Create | `src/Radio.Core/Interfaces/Audio/ITTSVoiceRepository.cs` |
| Create | `src/Radio.Infrastructure/Audio/TTS/Data/SqliteTTSVoiceRepository.cs` |
| Modify | `src/Radio.Infrastructure/DependencyInjection/FingerprintingServiceExtensions.cs` |
| Modify | `src/Radio.Core/Interfaces/Audio/ITTSFactory.cs` |
| Modify | `src/Radio.Infrastructure/Audio/Services/TTSFactory.cs` |
| Modify | `src/Radio.API/Controllers/SourcesController.cs` |
| Modify | `src/Radio.Web/Models/ApiModels.cs` |
| Modify | `src/Radio.Web/Services/ApiClients/SourcesApiService.cs` |
| Modify | `src/Radio.Web/Components/Pages/SystemConfigPage.razor` |
| Create | `tests/Radio.Infrastructure.Tests/Audio/TTS/SqliteTTSVoiceRepositoryTests.cs` |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
