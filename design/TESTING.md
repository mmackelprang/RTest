# Testing Guide

This document describes the testing infrastructure and practices for the Radio Console project.

## Test Projects

| Project | Tests | Purpose |
|---------|-------|---------|
| `tests/Radio.Metrics.Tests` | 17 | Unit tests for Radio.Metrics NuGet package |
| `tests/Radio.Configuration.Tests` | 115 | Unit tests for Radio.Configuration NuGet package |
| `tests/Radio.Fingerprinting.Tests` | 95 | Unit + integration tests for Radio.Fingerprinting NuGet package |
| `tests/RTLSDRCore.Tests` | 155 | Unit tests for RTLSDRCore NuGet package |
| `tests/Radio.AudioAnalysis.Tests` | 35 | Unit tests for Radio.AudioAnalysis NuGet package |
| `tests/Radio.Core.Tests` | 23 | Unit tests for core domain models and interfaces |
| `tests/Radio.Infrastructure.Tests` | 840 | Unit tests for infrastructure implementations (audio, BT, platform) |
| `tests/Radio.API.Tests` | 223 | Unit tests for API controllers, hubs, and middleware |
| `tests/Radio.Web.Tests` | 116 | Unit tests for Blazor components and Web services |
| `tests/Radio.IntegrationTests` | 50 | Cross-cutting integration tests (SignalR, audio data flow) |
| `tests/Radio.Web.E2ETests` | 28 | End-to-end Playwright browser tests (excluded from CI) |
| **Total** | **~1,697** | |

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

- **Total Tests:** 50
- **Passed:** 48
- **Skipped:** 2 (require external APIs / audio device)

Note: Secrets, fingerprinting, and play history integration tests have been moved to their respective package test projects (`Radio.Configuration.Tests`, `Radio.Fingerprinting.Tests`).

### Test Files

| File | Tests | Description |
|------|-------|-------------|
| `Audio/SoundFlowAudioDataIntegrationTests.cs` | 13 | Mock audio capture, sample generation, WAV file creation |
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

## Test Seams — when `internal` + `InternalsVisibleTo` is acceptable

A **test seam** is any member made more visible, or any branch added to production code, whose
justification is a test. This repository has a dozen-odd and will grow more. They are not banned —
some reach a path that hardware or a network genuinely makes unreachable — but an unlabelled seam is
indistinguishable from coverage, and that is how a gap hides.

**The rule is one sentence: a seam must say what it displaces.**

### The four kinds

Classify a seam before adding one. The kind determines what you owe the reader.

| Kind | Definition | What you owe |
|---|---|---|
| **A · Visibility** | Widens access (`private` → `internal`) to a value or method the production path already computes and reaches unchanged. | One line saying it is test-only. |
| **B · Injection** | A test-only method that writes state the test could not otherwise establish. Production path unchanged. | One line, plus why the state is otherwise unreachable. |
| **C · Substitution** | A field or property that production code **reads on every run** and that, when set, replaces a real collaborator. | The full label below. It adds a live branch to shipped code. |
| **D · Entry-point** | A real production method made `internal` so a test can call it **directly**, bypassing the dispatch that normally selects it. | The full label below. **This is the deceptive one.** |

**Kinds A and B are cheap and need no ceremony.** They change nothing about what runs in production;
the seam is an observation post. Prefer them — if a Kind-C or Kind-D seam can be restated as an A or B,
restate it.

**Kinds C and D are debt and must be labelled.** Kind C puts a branch in shipped code that exists only
for tests. Kind D is worse in one specific way: **a test entering through a Kind-D seam looks like
coverage in every coverage report** — the method really executes, the assertion is real — while the
dispatch that would have selected it is untested. Nothing distinguishes the two from outside. The
label is the only thing that does.

### The label

Every Kind-C and Kind-D seam carries this block in its XML doc, verbatim in shape:

```csharp
/// <para>
/// <b>Test seam (kind D — entry point).</b> Reached directly from
/// <c>SomeTests.SomeTest</c> via <c>InternalsVisibleTo</c>.
/// <b>Why the real path is unreachable:</b> &lt;the concrete blocker — a device, a
/// socket, a handshake; not "it is hard"&gt;.
/// <b>NOT covered by this seam:</b> &lt;the specific dispatch, branch or collaborator
/// the seam bypasses, with file:line&gt;.
/// </para>
```

Three parts, and the third earns the label:

- **kind** — `A`, `B`, `C` or `D`, with its word. An author who cannot pick a kind usually has two seams.
- **Why the real path is unreachable** — a *mechanism*, not a difficulty. "A fake socket cannot
  complete a Cast handshake" is a mechanism. "Hard to set up" is not, and is a sign the seam is
  unnecessary.
- **NOT covered by this seam** — the gap, in the file, beside the thing that caused it. This is the
  clause the convention exists for. If the answer is "nothing — it only observes", the seam is Kind A
  or B and does not need this block.

### Rules

1. **Prefer no seam. Check whether the real path is reachable before assuming it is not.** It very
   often is, and this repository has already been wrong about it once, expensively:
   - `Mock<T>` can subclass an abstract SoundFlow type **with a null engine** — both `SoundComponent`
     and `AudioCaptureDevice` store the engine reference without dereferencing it. Only the *engine*
     is native.
   - A `private` method reached by an interface event is drivable by raising that event on the mock
     (`IBluetoothService.DeviceConnected`).
   - A collaborator held in an **optional nullable constructor parameter** is often absent in tests,
     making the code it guards a safe no-op.
2. **Prefer Kind A or B over C or D.** Observe rather than substitute wherever the assertion allows.
3. **A Kind-C seam must be inert in production** — null-checked, defaulting to the real collaborator,
   never settable from production code.
4. **A Kind-D seam does not close a branch-dispatch gap**, and a test using one must not be described
   as if it did. Pin the dispatch separately, or record that it is unpinned.
5. **Never close a coverage gap with a second seam that re-asserts the first from another angle.** That
   adds a test without adding coverage and makes the gap harder to see.
6. **A labelled seam is a standing invitation to delete it.** When the blocker goes away, the seam and
   its label go with it. `ApplyDeferredCaptureState` was a Kind-D seam from PR #469 until `TEST-2`
   showed the blocker never existed; it was retired rather than labelled.

### ⚠ The reason rule 1 leads

`BluetoothAudioSource.ApplyDeferredCaptureState` was made `internal` in PR #469 with a doc comment
asserting that the real call sites *"require a native SoundFlow `AudioEngine` … and cannot be exercised
directly in a unit test."* **That was never true.** A test mocking `SoundComponent` with a null engine
was already green in CI at the time. The comment was then cited as authority by queue row `TEST-2`,
which sat open for thirteen months waiting for a native harness nobody needed.

The lesson is not "seams are bad". It is that **the sentence justifying a seam is a technical claim and
gets checked like one** — see `CLAUDE.md` § *Pre-Merge Review*.

### Enforcement

`TestSeamLabelLintTests` (`tests/Radio.Core.Tests/TestSeamLabelLintTests.cs`) asserts that every Kind-C
and Kind-D seam carries a complete label. It is a **regression lint over the seams that exist**, not a
proof that no unlabelled seam can be added — a new seam in a shape it does not recognise passes. Read
its class remarks before trusting a green run.

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
