# Testing Guide

This document describes the testing infrastructure and practices for the Radio Console project.

## Test Projects

| Project | Tests | Purpose |
|---------|-------|---------|
| `tests/Radio.Core.Tests` | 35 | Unit tests for core domain models and interfaces |
| `tests/Radio.Infrastructure.Tests` | 817 | Unit tests for infrastructure implementations (audio, BT, config, fingerprinting) |
| `tests/Radio.API.Tests` | 211 | Unit tests for API controllers, hubs, and middleware |
| `tests/Radio.Web.Tests` | 116 | Unit tests for Blazor components and Web services |
| `tests/Radio.Web.E2ETests` | 28 | End-to-end Playwright browser tests (excluded from CI) |
| `tests/RTLSDRCore.Tests` | 155 | Unit tests for RTL-SDR signal processing library |
| `tests/Radio.IntegrationTests` | 75 | Integration tests (SignalR, secrets, fingerprinting, play history) |
| **Total** | **1,437** | |

## Running Tests

```bash
# Run all tests
dotnet test --configuration Release --verbosity normal

# Run single test project
dotnet test tests/Radio.IntegrationTests

# Run specific test class
dotnet test --filter "FullyQualifiedName~SecretsConfigurationIntegrationTests"

# Run specific test method
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Skip hardware-dependent tests (useful for CI)
dotnet test --filter "Category!=RequiresAudioDevice&Category!=RequiresRTLSDR"
```

---

## Integration Tests

The `Radio.IntegrationTests` project provides comprehensive end-to-end testing for critical systems including secrets configuration, audio data flow, play history recording, fingerprinting, and SignalR hubs.

### Test Results Summary

- **Total Tests:** 75
- **Passed:** 72
- **Skipped:** 3 (require external APIs / audio device)

### Test Files

| File | Tests | Description |
|------|-------|-------------|
| `Secrets/SecretsConfigurationIntegrationTests.cs` | 18 | Secret tag parsing, JSON/SQLite providers, encryption/decryption |
| `PlayHistory/PlayHistoryRecordingIntegrationTests.cs` | 8 | Play history recording, updates, search, statistics |
| `Audio/SoundFlowAudioDataIntegrationTests.cs` | 13 | Mock audio capture, sample generation, WAV file creation |
| `Fingerprinting/FingerprintingConfigurationIntegrationTests.cs` | 10 | Database tables, indexes, repository operations |
| `Fingerprinting/EndToEndFingerprintingIntegrationTests.cs` | 8 | Full fingerprinting flow with play history updates |
| `SignalR/AudioStateHubIntegrationTests.cs` | 9 | Hub connection, subscriptions, group management |

### Test Infrastructure

#### IntegrationTestWebApplicationFactory

Extended `WebApplicationFactory<Program>` providing:
- Isolated temp directory per test instance (GUID-based)
- Configurable options: `EnableBackgroundServices`, `EnableRealAudioEngine`, `UseMockFingerprinting`
- Automatic database path override to temp directory
- SQLite connection pool cleanup on disposal
- Service replacement with mocks

```csharp
public class MyIntegrationTests : IClassFixture<IntegrationTestWebApplicationFactory>
{
  private readonly IntegrationTestWebApplicationFactory _factory;

  public MyIntegrationTests(IntegrationTestWebApplicationFactory factory)
  {
    _factory = factory;
  }

  [Fact]
  public async Task MyTest()
  {
    var client = _factory.CreateClient();
    // ... test code
  }
}
```

#### MockAudioSampleProvider

Mock implementation of `IAudioSampleProvider` for testing without real audio hardware:

```csharp
var provider = new MockAudioSampleProvider();
provider.SetActive(true, "TestFile.mp3", PlaySource.File);

// Capture simulated audio samples
var samples = await provider.CaptureAsync(TimeSpan.FromSeconds(10));

// Use custom sample generator
provider.SetSampleGenerator(duration =>
  MockAudioSampleProvider.GenerateSineWave(duration, frequency: 440.0));
```

Static generators available:
- `GenerateSineWave(duration, sampleRate, channels, frequency)`
- `GenerateSilence(duration, sampleRate, channels)`
- `GenerateWhiteNoise(duration, sampleRate, channels)`

#### MockMetadataLookupService

Mock implementation of `IMetadataLookupService` for testing fingerprint identification:

```csharp
var lookupService = new MockMetadataLookupService();

// Set default metadata for matches
lookupService.SetDefaultMetadata(new TrackMetadata
{
  Title = "Test Track",
  Artist = "Test Artist",
  Source = MetadataSource.SongRec
});

// Configure specific result for a fingerprint ID
lookupService.ConfigureResult("fp-123", new MetadataLookupResult
{
  IsMatch = true,
  Confidence = 0.99,
  Metadata = metadata
});

// Check lookup history
Assert.Equal(3, lookupService.LookupHistory.Count);
```

#### TestAudioFileGenerator

Creates valid WAV files programmatically for testing:

```csharp
// Create a 1-second sine wave at 440Hz
var path = TestAudioFileGenerator.CreateSineWaveFile(
  directory: tempDir,
  fileName: "test.wav",
  duration: TimeSpan.FromSeconds(1),
  sampleRate: 48000,
  channels: 2,
  frequency: 440.0);

// Create silence
var silencePath = TestAudioFileGenerator.CreateSilenceFile(
  tempDir, "silence.wav", TimeSpan.FromSeconds(0.5));

// Create frequency sweep (chirp)
var chirpPath = TestAudioFileGenerator.CreateChirpFile(
  tempDir, "chirp.wav", TimeSpan.FromSeconds(1),
  startFrequency: 100, endFrequency: 5000);
```

### Test Categories (Traits)

Tests can be filtered by category using traits:

```csharp
[Trait("Category", "RequiresAudioDevice")]  // Needs audio output device
[Trait("Category", "RequiresRTLSDR")]       // Needs RTLSDR hardware
[Trait("Category", "RequiresNetwork")]      // Needs external API access
[Trait("Category", "LongRunning")]          // Takes > 30 seconds
```

Tests with hardware dependencies should check availability and skip gracefully:

```csharp
[Fact(Skip = "Requires real audio device - run manually")]
[Trait("Category", "RequiresAudioDevice")]
public async Task RealAudioDevice_Test()
{
  // Test implementation
}
```

### Secrets Configuration Tests

Tests the `${secret:identifier}` pattern parsing and encryption:

```csharp
// Tag parsing
SecretTag.TryParse("${secret:my-api-key}", out var tag);
Assert.Equal("my-api-key", tag.Identifier);

// Store and retrieve encrypted secrets
await provider.SetSecretAsync("api-key", "secret-value");
var value = await provider.GetSecretAsync("api-key");

// Resolve tags in connection strings
var resolved = await provider.ResolveTagsAsync(
  "Server=${secret:host};Password=${secret:pass}");
```

### Play History Tests

Tests the complete play history recording flow:

```csharp
// Record a play
var entry = new PlayHistoryEntry
{
  Id = Guid.NewGuid().ToString(),
  PlayedAt = DateTime.UtcNow,
  Source = PlaySource.Radio,
  WasIdentified = false
};
await repository.RecordPlayAsync(entry);

// Update with fingerprinting results
var updated = entry with
{
  WasIdentified = true,
  MetadataSource = MetadataSource.Fingerprinting,
  IdentificationConfidence = 0.95
};
await repository.UpdateAsync(updated);
```

### SignalR Hub Tests

Tests real-time communication via SignalR:

```csharp
// Connect to hub
var connection = new HubConnectionBuilder()
  .WithUrl(new Uri(server.BaseAddress, "/hubs/audio"),
    options => options.HttpMessageHandlerFactory = _ => server.CreateHandler())
  .Build();

await connection.StartAsync();
Assert.Equal(HubConnectionState.Connected, connection.State);

// Subscribe to updates
await connection.InvokeAsync("SubscribeToQueue");
await connection.InvokeAsync("SubscribeToRadioState");
```

### Running Integration Tests

```bash
# All integration tests
dotnet test tests/Radio.IntegrationTests

# Skip hardware-dependent tests (for CI)
dotnet test tests/Radio.IntegrationTests --filter "Category!=RequiresAudioDevice&Category!=RequiresRTLSDR"

# Specific test class
dotnet test tests/Radio.IntegrationTests --filter "FullyQualifiedName~SecretsConfigurationIntegrationTests"

# Specific test area
dotnet test tests/Radio.IntegrationTests --filter "FullyQualifiedName~Fingerprinting"
```

---

## Unit Tests

Unit tests are located in the respective test projects and use xUnit with Moq for mocking.

### Common Patterns

```csharp
// Arrange
var mockLogger = new Mock<ILogger<MyService>>();
var mockRepository = new Mock<IRepository>();
var service = new MyService(mockLogger.Object, mockRepository.Object);

// Act
var result = await service.DoSomethingAsync();

// Assert
Assert.NotNull(result);
mockRepository.Verify(r => r.SaveAsync(It.IsAny<Entity>()), Times.Once);
```

---

## Test Configuration

### appsettings.IntegrationTests.json

Integration tests use a dedicated configuration file:

```json
{
  "Database": {
    "RootPath": "./test-data",
    "ConfigurationSubdirectory": "config",
    "MetricsSubdirectory": "metrics",
    "FingerprintingSubdirectory": "fingerprints"
  },
  "Fingerprinting": {
    "Enabled": true,
    "IdentificationIntervalSeconds": 30,
    "SampleDurationSeconds": 10
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
```

### Test Isolation

Each integration test instance:
1. Creates a unique temp directory using a GUID
2. Configures all database paths to use the temp directory
3. Clears SQLite connection pools on disposal
4. Deletes the temp directory after test completion

This ensures tests don't interfere with each other or with development data.
