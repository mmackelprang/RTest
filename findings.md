# Findings & Decisions

## Current State of TTS Voice System

### Google Voices — Hardcoded (17 voices)
- `TTSFactory.GetGoogleVoicesAsync()` (line 491) returns a static `List<TTSVoiceInfo>` with 10 US + 7 UK voices
- No API call to Google Cloud TTS voice listing endpoint
- Google Cloud TTS has a REST endpoint: `GET /v1/voices?key={key}&languageCode=en`

### Azure Voices — Dynamic with In-Memory Cache (24h)
- `TTSFactory.GetAzureVoicesAsync()` (line 527) calls Azure REST API
- Uses `SemaphoreSlim` + `_cachedAzureVoices` field + `_azureVoiceCacheExpiry`
- Filters to `en-*` locales
- Falls back to 5 default voices on error

### eSpeak Voices — Dynamic via Process
- `TTSFactory.GetESpeakVoicesAsync()` (line 429) shells out to `espeak-ng --voices`
- No caching — runs process each time
- Falls back to 3 default voices on error

### Web UI Voice Selection
- Google engine: shows `MudSelect` dropdown populated from API
- Other engines: shows plain text input for manual voice name entry
- No favorites mechanism exists

### API Endpoints
- `GET /api/sources/events/tts/voices?engine=Google` — returns `List<TTSVoiceInfoDto>`
- `GET /api/sources/events/tts/engines` — returns `List<TTSEngineInfoDto>`

### TTSVoiceInfo Record (Core)
```csharp
public record TTSVoiceInfo
{
  public string Id { get; init; } = "";
  public string Name { get; init; } = "";
  public string Language { get; init; } = "";
  public TTSVoiceGender Gender { get; init; }
}
```

### Database Pattern
- FingerprintDbContext: Singleton, manages shared SQLite connection to fingerprints.db
- Repositories: Scoped, get connection via `_dbContext.GetConnectionAsync()`
- Tables created in `CreateTablesAsync()` with `CREATE TABLE IF NOT EXISTS`
- UPSERT via `ON CONFLICT(...) DO UPDATE SET`
- Data mapping via `SqliteDataReader` with `GetOrdinal()`
