# Radio Console Project - Development Plan

## Project: Grandpa Anderson's Console Radio Remade

A modern audio command center hosted on a Raspberry Pi 5, encased in a vintage console radio cabinet. The software restores the original function (Radio/Vinyl) while adding modern capabilities (Spotify, Streaming, Smart Home Events, and Chromecast Audio).

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Technical Architecture](#technical-architecture)
3. [Phase Summary](#phase-summary)
4. [Phase 0: Project Setup](#phase-0-project-setup--repository-structure)
5. [Phase 1: Configuration Infrastructure](#phase-1-configuration-infrastructure)
6. [Phase 2: Core Audio Engine](#phase-2-core-audio-engine-soundflow)
7. [Phase 3: Primary Audio Sources](#phase-3-primary-audio-sources)
8. [Phase 4: Event Audio Sources](#phase-4-event-audio-sources)
9. [Phase 5: Ducking & Priority System](#phase-5-ducking--priority-system)
10. [Phase 6: Audio Outputs](#phase-6-audio-outputs)
11. [Phase 7: Visualization & Monitoring](#phase-7-visualization--monitoring)
12. [Phase 8: API & SignalR Integration](#phase-8-api--signalr-integration)
13. [Phase 9: Blazor UI Components](#phase-9-blazor-ui-components)
14. [Phase 10: Testing & Quality Assurance](#phase-10-testing--quality-assurance)
15. [Phase 11: Documentation](#phase-11-documentation)
16. [Phase 12: Deployment & Optimization](#phase-12-deployment--optimization)
17. [Progress Tracking](#progress-tracking)

---

## Project Overview

### Reference Documentation
- `/PROJECTPLAN.md` - High-level project overview
- `/design/CONFIGURATION.md` - Configuration infrastructure design
- `/design/AUDIO.md` - Audio implementation specification
- `/design/AUDIO_ARCHITECTURE.md` - SoundFlow audio architecture
- `/design/WEBUI.md` - UI planning guide

### Technical Stack

| Component | Technology |
|-----------|------------|
| Hardware | Raspberry Pi 5 (Raspberry Pi OS / Linux) |
| Framework | .NET 8+ (C#) |
| Audio Engine | [SoundFlow](https://github.com/lsxprime/soundflow-docs/) |
| UI | Blazor Server (Material 3) |
| API | ASP.NET Core Web API |
| Real-time | SignalR |
| Streaming | HTTP Audio Stream Server |
| Database | Repository Pattern (SQLite / JSON) |
| Logging | Serilog (Console + File) |
| Testing | xUnit |

---

## Technical Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Single ASP.NET Core Host                              │
├─────────────────────────────────────────────────────────────────────────────┤
│  Blazor Server  │  Web API  │  SignalR Hub  │  Local Stream Server          │
├─────────────────────────────────────────────────────────────────────────────┤
│                          Radio.Core                                          │
│              (Interfaces, Entities, Domain Logic, Enums)                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                       Radio.Infrastructure                                   │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                    Audio Management (SoundFlow)                        │  │
│  │  ┌─────────────┬─────────────┬──────────────┬──────────────────────┐  │  │
│  │  │AudioManager │DuckingService│VisualizerSvc │ StreamOutputService  │  │  │
│  │  │(MixerNode)  │(Priority)    │(FFT/Levels)  │ (HTTP/Cast)          │  │  │
│  │  └─────────────┴─────────────┴──────────────┴──────────────────────┘  │  │
│  ├───────────────────────────────────────────────────────────────────────┤  │
│  │                    Configuration Infrastructure                        │  │
│  │           (From design/CONFIGURATION.md - SQLite/JSON)                 │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Phase Summary

| Phase | Name | Duration | Risk | Priority | Dependencies |
|-------|------|----------|------|----------|--------------|
| 0 | Project Setup | 1-2 days | Low | Critical | None |
| 1 | Configuration Infrastructure | 3-5 days | Low | Critical | Phase 0 |
| 2 | Core Audio Engine | 5-7 days | Low | Critical | Phase 1 |
| 3 | Primary Audio Sources | 7-10 days | Medium | High | Phase 2 |
| 4 | Event Audio Sources | 4-5 days | Low | High | Phase 2 |
| 5 | Ducking & Priority | 3-4 days | Low | High | Phase 3, 4 |
| 6 | Audio Outputs | 4-5 days | Medium | High | Phase 5 |
| 7 | Visualization & Monitoring | 3-4 days | Low | Medium | Phase 2 |
| 8 | API & SignalR Integration | 4-5 days | Low | High | Phase 6, 7 |
| 9 | Blazor UI Components | 7-10 days | Medium | High | Phase 8 |
| 10 | Testing & Quality | 5-7 days | Low | Critical | All Previous |
| 11 | Documentation | 3-4 days | Low | Medium | All Previous |
| 12 | Deployment & Optimization | 3-5 days | Medium | High | All Previous |

**Estimated Total Duration:** 48-68 days

---

## Phase 0: Project Setup & Repository Structure

**Duration:** 1-2 days  
**Risk Level:** Low  
**Priority:** Critical  
**Status:** 🟢 Completed

### Objectives
1. Create project structure following Clean Architecture
2. Set up solution with proper project references
3. Configure CI/CD pipeline
4. Establish configuration infrastructure integration

### Deliverables
- [x] Solution file with all projects
- [x] Project references configured
- [x] Directory.Build.props for shared settings
- [x] GitHub Actions workflow for build and test
- [x] .editorconfig for code style
- [x] Initial README.md updated

### Repository Structure

```
RadioConsole/
├── .github/
│   ├── copilot-instructions.md
│   └── workflows/
│       ├── build.yml
│       └── test.yml
├── src/
│   ├── Radio.Core/
│   │   ├── Radio.Core.csproj
│   │   ├── Interfaces/
│   │   │   ├── Audio/
│   │   │   ├── Services/
│   │   │   └── Repositories/
│   │   ├── Models/
│   │   ├── Enums/
│   │   ├── Events/
│   │   └── Exceptions/
│   ├── Radio.Infrastructure/
│   │   ├── Radio.Infrastructure.csproj
│   │   ├── Audio/
│   │   ├── Configuration/
│   │   ├── External/
│   │   └── DependencyInjection/
│   ├── Radio.API/
│   │   ├── Radio.API.csproj
│   │   ├── Controllers/
│   │   ├── Hubs/
│   │   ├── Streaming/
│   │   └── Program.cs
│   └── Radio.Web/
│       ├── Radio.Web.csproj
│       ├── Components/
│       └── wwwroot/
├── tests/
│   ├── Radio.Core.Tests/
│   ├── Radio.Infrastructure.Tests/
│   └── Radio.API.Tests/
├── design/
├── scripts/
├── RadioConsole.sln
├── Directory.Build.props
├── Directory.Packages.props
└── README.md
```

### Coding Assistant Prompt

```markdown
## Task: Initialize Radio Console SoundFlow Project

Create a new .NET 8 solution for the Radio Console project with SoundFlow integration. This will be a clean implementation following Clean Architecture principles.

### Context
Reference the following design documents:
- `/PROJECTPLAN.md` - Project overview
- `/design/CONFIGURATION.md` - Configuration infrastructure
- `/design/AUDIO_ARCHITECTURE.md` - Audio system architecture

### Requirements

#### 1. Create Solution Structure
Create the following projects with proper references:

**Radio.Core.csproj** (Class Library):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
  </ItemGroup>
</Project>
```

**Radio.Infrastructure.csproj** (Class Library):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SoundFlow" Version="1.*" />
    <PackageReference Include="SoundFlow.Backends.MiniAudio" Version="1.*" />
    <PackageReference Include="SpotifyAPI.Web" Version="7.*" />
    <PackageReference Include="Serilog" Version="4.*" />
    <PackageReference Include="Serilog.Extensions.Logging" Version="8.*" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Radio.Core\Radio.Core.csproj" />
  </ItemGroup>
</Project>
```

**Radio.API.csproj** (Web Project):
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.*" />
    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Radio.Core\Radio.Core.csproj" />
    <ProjectReference Include="..\Radio.Infrastructure\Radio.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

**Radio.Web.csproj** (Blazor Web App):
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Radio.Core\Radio.Core.csproj" />
    <ProjectReference Include="..\Radio.Infrastructure\Radio.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

#### 2. Create Directory.Build.props
```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

#### 3. Create GitHub Actions Workflow (.github/workflows/build.yml)
```yaml
name: Build and Test

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore --configuration Release
      - name: Test
        run: dotnet test --no-build --configuration Release --verbosity normal
```

#### 4. Create .editorconfig
```ini
root = true

[*]
indent_style = space
indent_size = 2
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
csharp_style_namespace_declarations = file_scoped:warning
csharp_prefer_braces = true:warning
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
```

#### 5. Create Placeholder Interfaces in Radio.Core

Create these placeholder interfaces for future implementation:
- `src/Radio.Core/Interfaces/Audio/IAudioManager.cs`
- `src/Radio.Core/Interfaces/Audio/IAudioSource.cs`
- `src/Radio.Core/Interfaces/Audio/IAudioEngine.cs`
- `src/Radio.Core/Interfaces/Audio/IAudioDeviceManager.cs`

### Success Criteria
- [ ] Solution builds without errors on both Windows and Linux
- [ ] All projects properly reference each other
- [ ] CI/CD pipeline runs successfully
- [ ] Code style enforced via .editorconfig
- [ ] All placeholder interfaces created with XML documentation
```

### Testing Requirements
- Verify solution builds with `dotnet build`
- Verify tests run with `dotnet test`
- Verify CI/CD pipeline passes

---

## Phase 1: Configuration Infrastructure

**Duration:** 3-5 days  
**Risk Level:** Low  
**Priority:** Critical  
**Status:** 🟢 Completed  
**Dependencies:** Phase 0

### Objectives
1. Implement configuration infrastructure from `/design/CONFIGURATION.md`
2. Create dual backing stores (SQLite and JSON)
3. Implement secrets management with tag-based substitution
4. Integrate with Microsoft.Extensions.Configuration and IOptions pattern
5. Create backup/restore capabilities

### Deliverables
- [x] `IConfigurationStore` interface and implementations
- [x] `ISecretsProvider` with JSON and SQLite providers
- [x] `IConfigurationManager` for high-level access
- [x] `IConfigurationBackupService` for backup/restore
- [x] Configuration models and options classes
- [x] Unit tests for all configuration components

### Coding Assistant Prompt

```markdown
## Task: Implement Configuration Infrastructure

Implement the complete configuration infrastructure as specified in `/design/CONFIGURATION.md`. This system provides unified configuration management with:
- Microsoft.Extensions.Configuration integration
- Microsoft.Extensions.Options for user preferences
- Secrets management with tag-based substitution (${secret:identifier})
- Dual backing stores: SQLite and JSON files
- Full CRUD operations on configuration files and entries
- Backup/Restore capabilities
- Raw vs Resolved reading modes for UI management

### Reference
Review `/design/CONFIGURATION.md` for complete specifications including:
- Class diagrams and architecture
- Data flow diagrams for secret resolution
- State transition diagrams
- Complete interface definitions

### Files to Create

#### 1. Models (`src/Radio.Infrastructure/Configuration/Models/`)

**ConfigurationEntry.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Represents a single configuration key-value pair with metadata.
/// </summary>
public sealed record ConfigurationEntry
{
  /// <summary>The configuration key (supports section notation with ':').</summary>
  public required string Key { get; init; }
  
  /// <summary>The configuration value (resolved if secrets were substituted).</summary>
  public required string Value { get; init; }
  
  /// <summary>Original value with secret tags intact (null if same as Value).</summary>
  public string? RawValue { get; init; }
  
  /// <summary>Indicates whether this entry contains or contained a secret tag.</summary>
  public bool ContainsSecret { get; init; }
  
  /// <summary>When this entry was last modified.</summary>
  public DateTimeOffset? LastModified { get; init; }
  
  /// <summary>Optional description for documentation purposes.</summary>
  public string? Description { get; init; }
}
```

**ConfigurationReadMode.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Specifies how configuration values should be read.
/// </summary>
public enum ConfigurationReadMode
{
  /// <summary>Returns values with secret tags resolved to actual values.</summary>
  Resolved = 0,
  
  /// <summary>Returns raw values with secret tags intact (for UI management).</summary>
  Raw = 1
}
```

**ConfigurationStoreType.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Specifies the backing store type for configuration data.
/// </summary>
public enum ConfigurationStoreType
{
  Json = 0,
  Sqlite = 1
}
```

**SecretTag.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Models;

using System.Text.RegularExpressions;

/// <summary>
/// Represents a secret tag reference in configuration values.
/// Format: ${secret:identifier}
/// </summary>
public sealed partial record SecretTag
{
  public const string TagPrefix = "${secret:";
  public const string TagSuffix = "}";
  
  [GeneratedRegex(@"\$\{secret:([a-zA-Z0-9_-]+)\}", RegexOptions.Compiled)]
  private static partial Regex TagPatternRegex();
  
  public required string Tag { get; init; }
  public required string Identifier { get; init; }
  
  public static SecretTag Create(string identifier) => new()
  {
    Tag = $"{TagPrefix}{identifier}{TagSuffix}",
    Identifier = identifier
  };
  
  public static bool TryParse(string value, out SecretTag? tag)
  {
    tag = null;
    var match = TagPatternRegex().Match(value);
    if (!match.Success) return false;
    
    tag = new SecretTag
    {
      Tag = match.Value,
      Identifier = match.Groups[1].Value
    };
    return true;
  }
  
  public static IEnumerable<SecretTag> ExtractAll(string value)
  {
    if (string.IsNullOrEmpty(value)) yield break;
    
    foreach (Match match in TagPatternRegex().Matches(value))
    {
      yield return new SecretTag
      {
        Tag = match.Value,
        Identifier = match.Groups[1].Value
      };
    }
  }
  
  public static bool ContainsTag(string? value) =>
    !string.IsNullOrEmpty(value) && TagPatternRegex().IsMatch(value);
}
```

**ConfigurationOptions.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Configuration options for the managed configuration system.
/// </summary>
public sealed class ConfigurationOptions
{
  public const string SectionName = "ManagedConfiguration";
  
  public ConfigurationStoreType DefaultStoreType { get; set; } = ConfigurationStoreType.Json;
  public string BasePath { get; set; } = "./config";
  public string JsonExtension { get; set; } = ".json";
  public string SqliteFileName { get; set; } = "configuration.db";
  public string SecretsFileName { get; set; } = "secrets";
  public string BackupPath { get; set; } = "./config/backups";
  public bool AutoSave { get; set; } = true;
  public int BackupRetentionDays { get; set; } = 30;
  public int AutoSaveDebounceMs { get; set; } = 5000;
}
```

#### 2. Abstractions (`src/Radio.Infrastructure/Configuration/Abstractions/`)

**IConfigurationStore.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Abstractions;

/// <summary>
/// Represents a backing store for configuration data.
/// </summary>
public interface IConfigurationStore
{
  string StoreId { get; }
  ConfigurationStoreType StoreType { get; }
  
  Task<ConfigurationEntry?> GetEntryAsync(string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);
  Task<IReadOnlyList<ConfigurationEntry>> GetAllEntriesAsync(ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);
  Task<IReadOnlyList<ConfigurationEntry>> GetEntriesBySectionAsync(string sectionPrefix, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);
  Task SetEntryAsync(string key, string value, CancellationToken ct = default);
  Task SetEntriesAsync(IEnumerable<ConfigurationEntry> entries, CancellationToken ct = default);
  Task<bool> DeleteEntryAsync(string key, CancellationToken ct = default);
  Task<bool> ExistsAsync(string key, CancellationToken ct = default);
  Task<bool> SaveAsync(CancellationToken ct = default);
  Task ReloadAsync(CancellationToken ct = default);
}
```

**ISecretsProvider.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Abstractions;

/// <summary>
/// Provides secrets storage and resolution for tag-based substitution.
/// </summary>
public interface ISecretsProvider
{
  Task<string?> GetSecretAsync(string tag, CancellationToken ct = default);
  Task<string> SetSecretAsync(string tag, string value, CancellationToken ct = default);
  string GenerateTag(string? hint = null);
  Task<bool> DeleteSecretAsync(string tag, CancellationToken ct = default);
  Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct = default);
  bool ContainsSecretTag(string value);
  Task<string> ResolveTagsAsync(string value, CancellationToken ct = default);
}
```

**IConfigurationManager.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Abstractions;

/// <summary>
/// High-level configuration management interface.
/// </summary>
public interface IConfigurationManager
{
  Task<IConfigurationStore> GetStoreAsync(string storeId, CancellationToken ct = default);
  Task<IConfigurationStore> CreateStoreAsync(string storeId, CancellationToken ct = default);
  Task<IReadOnlyList<ConfigurationFile>> ListStoresAsync(CancellationToken ct = default);
  Task<bool> DeleteStoreAsync(string storeId, CancellationToken ct = default);
  
  Task<T?> GetValueAsync<T>(string storeId, string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);
  Task SetValueAsync<T>(string storeId, string key, T value, CancellationToken ct = default);
  Task<bool> DeleteValueAsync(string storeId, string key, CancellationToken ct = default);
  
  Task<string> CreateSecretAsync(string storeId, string key, string secretValue, CancellationToken ct = default);
  Task<bool> UpdateSecretAsync(string tag, string newValue, CancellationToken ct = default);
  
  IConfigurationBackupService Backup { get; }
  ConfigurationStoreType CurrentStoreType { get; }
}
```

**IConfigurationBackupService.cs:**
```csharp
namespace Radio.Infrastructure.Configuration.Abstractions;

/// <summary>
/// Provides backup and restore capabilities for configuration stores.
/// </summary>
public interface IConfigurationBackupService
{
  Task<BackupMetadata> CreateBackupAsync(string storeId, ConfigurationStoreType storeType, string? description = null, CancellationToken ct = default);
  Task<IReadOnlyList<BackupMetadata>> CreateFullBackupAsync(string? description = null, CancellationToken ct = default);
  Task RestoreBackupAsync(string backupId, bool overwrite = false, CancellationToken ct = default);
  Task<IReadOnlyList<BackupMetadata>> ListBackupsAsync(string? storeId = null, CancellationToken ct = default);
  Task<bool> DeleteBackupAsync(string backupId, CancellationToken ct = default);
  Task ExportBackupAsync(string backupId, Stream destination, CancellationToken ct = default);
  Task<BackupMetadata> ImportBackupAsync(Stream source, CancellationToken ct = default);
}
```

#### 3. Store Implementations

**JsonConfigurationStore.cs** - JSON file-based implementation
**SqliteConfigurationStore.cs** - SQLite database implementation
**ConfigurationStoreFactory.cs** - Factory for creating stores

#### 4. Secrets Implementations

**JsonSecretsProvider.cs** - JSON-based secrets with Data Protection encryption
**SqliteSecretsProvider.cs** - SQLite-based secrets storage
**SecretTagProcessor.cs** - Tag detection and substitution logic

#### 5. Services

**ConfigurationManager.cs** - High-level orchestration
**ConfigurationBackupService.cs** - Backup/restore implementation
**UserPreferencesService.cs** - IOptions integration for user preferences
**LastRunStateService.cs** - Application state persistence with auto-save

#### 6. DI Extensions

**ConfigurationServiceExtensions.cs:**
```csharp
namespace Radio.Infrastructure.DependencyInjection;

public static class ConfigurationServiceExtensions
{
  public static IServiceCollection AddManagedConfiguration(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    services.Configure<ConfigurationOptions>(
      configuration.GetSection(ConfigurationOptions.SectionName));
    
    services.AddDataProtection();
    
    services.AddSingleton<ISecretsProvider, JsonSecretsProvider>();
    services.AddSingleton<IConfigurationStoreFactory, ConfigurationStoreFactory>();
    services.AddSingleton<IConfigurationManager, ConfigurationManager>();
    services.AddSingleton<IConfigurationBackupService, ConfigurationBackupService>();
    
    return services;
  }
}
```

### Unit Tests Required

Create in `tests/Radio.Infrastructure.Tests/Configuration/`:

1. **SecretTagTests.cs**
   - Test TryParse with valid/invalid inputs
   - Test ExtractAll with multiple tags
   - Test ContainsTag detection

2. **ConfigurationStoreTests.cs**
   - Test CRUD operations for both JSON and SQLite
   - Test Raw vs Resolved read modes
   - Test section filtering

3. **SecretsProviderTests.cs**
   - Test secret storage and retrieval
   - Test tag generation
   - Test tag resolution in values

4. **ConfigurationBackupTests.cs**
   - Test backup creation
   - Test restore operations
   - Test retention policy

### Success Criteria
- [ ] All interfaces compile without errors
- [ ] Both JSON and SQLite stores work correctly
- [ ] Secret tags resolve properly
- [ ] Backup/restore functionality works
- [ ] Unit tests pass with >80% coverage
- [ ] Hot-swap between store types works
```

### Testing Requirements
- Run `dotnet test` for unit tests
- Manual testing of configuration CRUD operations
- Verify secret resolution works correctly
- Test backup and restore functionality

---

## Phase 2: Core Audio Engine (SoundFlow)

**Duration:** 5-7 days  
**Risk Level:** Low  
**Priority:** Critical  
**Status:** ⚪ Not Started  
**Dependencies:** Phase 1

### Objectives
1. Initialize SoundFlow AudioEngine with MiniAudio backend
2. Implement device enumeration for ALSA/USB devices
3. Create master output node with mixed audio stream
4. Support hot-plug detection for USB audio devices

### Deliverables
- [ ] `IAudioEngine` interface and SoundFlow implementation
- [ ] `IAudioDeviceManager` for device enumeration
- [ ] `IMasterMixer` for audio mixing
- [ ] Hot-plug detection system
- [ ] Tapped output stream for Chromecast/streaming
- [ ] Unit tests for audio engine

### Coding Assistant Prompt

```markdown
## Task: Implement Core SoundFlow Audio Engine

Implement the foundation audio engine using the SoundFlow library. Reference `/design/AUDIO_ARCHITECTURE.md` for the complete specification.

### Reference
- SoundFlow Documentation: https://github.com/lsxprime/soundflow-docs/
- Uses MiniAudio backend for cross-platform audio

### Files to Create

#### 1. Core Interfaces (`src/Radio.Core/Interfaces/Audio/`)

**IAudioEngine.cs:**
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Core audio engine interface wrapping SoundFlow functionality.
/// </summary>
public interface IAudioEngine : IAsyncDisposable
{
  Task InitializeAsync(CancellationToken ct = default);
  Task StartAsync(CancellationToken ct = default);
  Task StopAsync(CancellationToken ct = default);
  
  IMasterMixer GetMasterMixer();
  Stream GetMixedOutputStream();
  
  AudioEngineState State { get; }
  bool IsReady { get; }
  
  event EventHandler<AudioEngineStateChangedEventArgs>? StateChanged;
  event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;
}

public enum AudioEngineState
{
  Uninitialized,
  Initializing,
  Ready,
  Running,
  Stopping,
  Error,
  Disposed
}
```

**IAudioDeviceManager.cs:**
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Manages audio device enumeration and USB port reservations.
/// </summary>
public interface IAudioDeviceManager
{
  Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync(CancellationToken ct = default);
  Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync(CancellationToken ct = default);
  Task<AudioDeviceInfo?> GetDefaultOutputDeviceAsync(CancellationToken ct = default);
  Task SetOutputDeviceAsync(string deviceId, CancellationToken ct = default);
  
  bool IsUSBPortInUse(string usbPort);
  void ReserveUSBPort(string usbPort, string sourceId);
  void ReleaseUSBPort(string usbPort);
  
  Task RefreshDevicesAsync(CancellationToken ct = default);
  event EventHandler<AudioDeviceChangedEventArgs>? DevicesChanged;
}

public record AudioDeviceInfo
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public required AudioDeviceType Type { get; init; }
  public bool IsDefault { get; init; }
  public int MaxChannels { get; init; }
  public int[] SupportedSampleRates { get; init; } = [];
  public string? AlsaDeviceId { get; init; }
  public string? USBPort { get; init; }
  public bool IsUSBDevice { get; init; }
}

public enum AudioDeviceType { Output, Input, Duplex }
```

**IMasterMixer.cs:**
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Master audio mixer that combines all audio sources.
/// </summary>
public interface IMasterMixer
{
  float MasterVolume { get; set; }
  float Balance { get; set; }
  bool IsMuted { get; set; }
  
  void AddSource(IAudioSource source);
  void RemoveSource(IAudioSource source);
  IReadOnlyList<IAudioSource> GetActiveSources();
}
```

#### 2. SoundFlow Implementation (`src/Radio.Infrastructure/Audio/SoundFlow/`)

**SoundFlowAudioEngine.cs:**
```csharp
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// SoundFlow audio engine implementation.
/// </summary>
public class SoundFlowAudioEngine : IAudioEngine
{
  private readonly ILogger<SoundFlowAudioEngine> _logger;
  private readonly IOptions<AudioOptions> _options;
  
  private AudioEngine? _engine;
  private TappedOutputStream? _outputTap;
  private Timer? _hotPlugTimer;
  private AudioEngineState _state = AudioEngineState.Uninitialized;

  public async Task InitializeAsync(CancellationToken ct = default)
  {
    _state = AudioEngineState.Initializing;
    
    try
    {
      // Initialize MiniAudio backend
      _engine = new MiniAudioEngine(
        sampleRate: 48000,
        channels: 2,
        bufferSize: 1024);
      
      // Create output tap for streaming
      _outputTap = new TappedOutputStream(48000, 2, 5);
      
      // Set up hot-plug detection timer
      _hotPlugTimer = new Timer(CheckForDeviceChanges, null, 
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
      
      _state = AudioEngineState.Ready;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize audio engine");
      _state = AudioEngineState.Error;
      throw;
    }
  }
  
  public Stream GetMixedOutputStream() => _outputTap 
    ?? throw new InvalidOperationException("Engine not initialized");
    
  // Additional implementation...
}
```

**SoundFlowDeviceManager.cs:**
```csharp
namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Device manager with USB port conflict detection.
/// </summary>
public class SoundFlowDeviceManager : IAudioDeviceManager
{
  private readonly Dictionary<string, string> _usbPortReservations = new();
  private readonly object _reservationLock = new();

  public bool IsUSBPortInUse(string usbPort)
  {
    lock (_reservationLock)
    {
      return _usbPortReservations.ContainsKey(usbPort);
    }
  }

  public void ReserveUSBPort(string usbPort, string sourceId)
  {
    lock (_reservationLock)
    {
      if (_usbPortReservations.ContainsKey(usbPort))
      {
        throw new AudioDeviceConflictException(
          $"USB port '{usbPort}' already in use by '{_usbPortReservations[usbPort]}'");
      }
      _usbPortReservations[usbPort] = sourceId;
    }
  }

  public void ReleaseUSBPort(string usbPort)
  {
    lock (_reservationLock)
    {
      _usbPortReservations.Remove(usbPort);
    }
  }
}
```

**TappedOutputStream.cs:**
```csharp
namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Stream that captures mixed audio output for HTTP streaming.
/// Uses a lock-free ring buffer for real-time capture.
/// </summary>
internal class TappedOutputStream : Stream
{
  private readonly byte[] _buffer;
  private readonly int _bufferSize;
  private int _readPosition;
  private int _writePosition;
  private readonly object _lock = new();

  public TappedOutputStream(int sampleRate = 48000, int channels = 2, int bufferSizeSeconds = 5)
  {
    _bufferSize = sampleRate * channels * 2 * bufferSizeSeconds; // 16-bit PCM
    _buffer = new byte[_bufferSize];
  }

  public void WriteFromEngine(float[] samples)
  {
    // Convert float samples to 16-bit PCM and write to ring buffer
    lock (_lock)
    {
      foreach (var sample in samples)
      {
        var pcm = (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
        _buffer[_writePosition++] = (byte)(pcm & 0xFF);
        _buffer[_writePosition++] = (byte)((pcm >> 8) & 0xFF);
        if (_writePosition >= _bufferSize) _writePosition = 0;
      }
    }
  }

  public override int Read(byte[] buffer, int offset, int count)
  {
    lock (_lock)
    {
      var available = (_writePosition - _readPosition + _bufferSize) % _bufferSize;
      var toRead = Math.Min(count, available);
      
      for (int i = 0; i < toRead; i++)
      {
        buffer[offset + i] = _buffer[_readPosition++];
        if (_readPosition >= _bufferSize) _readPosition = 0;
      }
      
      return toRead;
    }
  }

  // Implement other Stream members...
  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length => throw new NotSupportedException();
  public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
  public override void Flush() { }
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
```

#### 3. Exceptions (`src/Radio.Core/Exceptions/`)

**AudioDeviceConflictException.cs:**
```csharp
namespace Radio.Core.Exceptions;

public class AudioDeviceConflictException : Exception
{
  public string? DeviceId { get; }
  public string? ConflictingSourceId { get; }

  public AudioDeviceConflictException(string message) : base(message) { }
  
  public AudioDeviceConflictException(string message, string deviceId, string conflictingSourceId) 
    : base(message)
  {
    DeviceId = deviceId;
    ConflictingSourceId = conflictingSourceId;
  }
}
```

### Unit Tests Required

1. **SoundFlowAudioEngineTests.cs**
   - Test initialization and state transitions
   - Test start/stop operations
   - Test mixed output stream produces data

2. **SoundFlowDeviceManagerTests.cs**
   - Test USB port reservation/release
   - Test conflict detection
   - Test device enumeration

3. **TappedOutputStreamTests.cs**
   - Test write and read operations
   - Test ring buffer wraparound
   - Test thread safety

### Success Criteria
- [ ] Audio engine initializes on both Windows and Linux
- [ ] Device enumeration works for ALSA/USB devices
- [ ] Mixed output stream captures real audio data
- [ ] Hot-plug detection works for USB devices
- [ ] USB port conflict detection works
- [ ] All unit tests pass
```

### Testing Requirements
- Test on both Windows and Raspberry Pi (Linux)
- Verify USB audio device detection
- Test hot-plug scenarios
- Verify stream output contains valid audio data

---

## Phase 3: Primary Audio Sources

**Duration:** 7-10 days  
**Risk Level:** Medium  
**Priority:** High  
**Status:** ⚪ Not Started  
**Dependencies:** Phase 2

### Objectives
1. Implement base primary audio source interface
2. Create Spotify integration with SpotifyAPI-NET
3. Implement USB audio sources (Radio, Vinyl, Generic)
4. Create File Player with directory support

### Deliverables
- [ ] `IAudioSource` and `IPrimaryAudioSource` interfaces
- [ ] Spotify audio source with authentication
- [ ] Radio USB audio source (Raddy RF320)
- [ ] Vinyl USB audio source
- [ ] File Player with playlist support
- [ ] Generic USB audio source
- [ ] Unit tests for all sources

### Audio Sources Overview

| Source | Type | Seekable | USB Port | Configuration |
|--------|------|----------|----------|---------------|
| Spotify | Stream | Yes | No | Secrets (API keys) |
| Radio (RF320) | USB Input | No | Yes | DeviceOptions |
| Vinyl | USB Input | No | Yes | DeviceOptions |
| File Player | File | Yes | No | FilePlayerOptions |
| Generic USB | USB Input | No | Yes | User-selected |

### Coding Assistant Prompt

```markdown
## Task: Implement Primary Audio Sources

Create the primary audio source implementations. Only one primary source can be active at a time. Reference `/design/AUDIO_ARCHITECTURE.md` for specifications.

### Files to Create

#### 1. Base Interfaces (`src/Radio.Core/Interfaces/Audio/`)

**IAudioSource.cs:**
```csharp
namespace Radio.Core.Interfaces.Audio;

public interface IAudioSource : IAsyncDisposable
{
  string Id { get; }
  string Name { get; }
  AudioSourceType Type { get; }
  AudioSourceCategory Category { get; }
  AudioSourceState State { get; }
  float Volume { get; set; }
  
  object GetSoundComponent();
  event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged;
}

public enum AudioSourceType
{
  Spotify, Radio, Vinyl, FilePlayer, GenericUSB, TTS, AudioFileEvent
}

public enum AudioSourceCategory { Primary, Event }

public enum AudioSourceState
{
  Created, Initializing, Ready, Playing, Paused, Stopped, Error, Disposed
}
```

**IPrimaryAudioSource.cs:**
```csharp
namespace Radio.Core.Interfaces.Audio;

public interface IPrimaryAudioSource : IAudioSource
{
  TimeSpan? Duration { get; }
  TimeSpan Position { get; }
  bool IsSeekable { get; }
  
  Task PlayAsync(CancellationToken ct = default);
  Task PauseAsync(CancellationToken ct = default);
  Task ResumeAsync(CancellationToken ct = default);
  Task StopAsync(CancellationToken ct = default);
  Task SeekAsync(TimeSpan position, CancellationToken ct = default);
  
  event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;
  IReadOnlyDictionary<string, string> Metadata { get; }
}
```

#### 2. Source Implementations (`src/Radio.Infrastructure/Audio/Sources/Primary/`)

**SpotifyAudioSource.cs** - Spotify Connect integration
**RadioAudioSource.cs** - Raddy RF320 USB input
**VinylAudioSource.cs** - Turntable USB input  
**FilePlayerAudioSource.cs** - Local file/directory playback
**GenericUSBAudioSource.cs** - User-selected USB device

#### 3. Configuration Models (`src/Radio.Core/Configuration/`)

**AudioOptions.cs:**
```csharp
namespace Radio.Core.Configuration;

public class AudioOptions
{
  public const string SectionName = "Audio";
  public string DefaultSource { get; set; } = "Spotify";
  public int DuckingPercentage { get; set; } = 20;
  public DuckingPolicy DuckingPolicy { get; set; } = DuckingPolicy.FadeSmooth;
  public int DuckingAttackMs { get; set; } = 100;
  public int DuckingReleaseMs { get; set; } = 500;
}

public enum DuckingPolicy { FadeSmooth, FadeQuick, Instant }
```

**DeviceOptions.cs:**
```csharp
namespace Radio.Core.Configuration;

public class DeviceOptions
{
  public const string SectionName = "Devices";
  public RadioDeviceOptions Radio { get; set; } = new();
  public VinylDeviceOptions Vinyl { get; set; } = new();
  public CastDeviceOptions Cast { get; set; } = new();
}

public class RadioDeviceOptions
{
  public string USBPort { get; set; } = "/dev/ttyUSB0";
}

public class VinylDeviceOptions
{
  public string USBPort { get; set; } = "/dev/ttyUSB1";
}
```

**SpotifySecrets.cs:**
```csharp
namespace Radio.Core.Configuration;

public class SpotifySecrets
{
  public const string SectionName = "Spotify";
  public string ClientID { get; set; } = "";
  public string ClientSecret { get; set; } = "";
  public string RefreshToken { get; set; } = "";
}
```

### Implementation Notes

1. **USB Port Conflict Prevention:**
   - Check `IAudioDeviceManager.IsUSBPortInUse()` before initializing
   - Reserve port with `ReserveUSBPort()` on init
   - Release with `ReleaseUSBPort()` on dispose

2. **Preference Auto-Save:**
   - Save LastSongPlayed, SongPosition on state changes
   - Restore on source initialization

3. **File Player Features:**
   - Support: .mp3, .flac, .wav, .ogg, .aac, .m4a, .wma
   - Shuffle mode
   - Repeat modes: Off, One, All
   - Directory scanning with recursive option

### Success Criteria
- [ ] All sources initialize correctly
- [ ] USB port conflicts detected and reported
- [ ] Preferences auto-save on state changes
- [ ] Only one primary source active at a time
- [ ] File player supports all audio formats
```

### Testing Requirements
- Test each source type independently
- Test USB port conflict scenarios
- Test preference persistence
- Test Spotify authentication flow

---

## Phase 4: Event Audio Sources

**Duration:** 4-5 days  
**Risk Level:** Low  
**Priority:** High  
**Status:** ⚪ Not Started  
**Dependencies:** Phase 2

### Objectives
1. Implement event audio source interface
2. Create TTS Event source with multiple engines (eSpeak, Google, Azure)
3. Implement Audio File Event source for notifications

### Deliverables
- [ ] `IEventAudioSource` interface
- [ ] `ITTSFactory` with engine support
- [ ] TTS Event source implementation
- [ ] Audio File Event source
- [ ] Unit tests

### Coding Assistant Prompt

```markdown
## Task: Implement Event Audio Sources

Create ephemeral audio sources for events. These trigger ducking of primary sources and auto-dispose when complete.

### Key Interfaces

**IEventAudioSource.cs:**
- Duration property
- PlayAsync (one-shot)
- StopAsync
- PlaybackCompleted event

**ITTSFactory.cs:**
- AvailableEngines property
- CreateAsync(text, parameters)
- GetVoicesAsync(engine)

### TTS Engines
1. **eSpeak** - Offline, uses espeak-ng command
2. **Google Cloud TTS** - Requires API key from secrets
3. **Azure TTS** - Requires API key from secrets

### Implementation Notes
- Events are ephemeral: create, play once, dispose
- Trigger ducking via DuckingService
- Auto-cleanup when PlaybackCompleted fires
```

---

## Phase 5: Ducking & Priority System

**Duration:** 3-4 days  
**Risk Level:** Low  
**Priority:** High  
**Status:** ⚪ Not Started  
**Dependencies:** Phase 3, Phase 4

### Objectives
1. Implement ducking service for volume management
2. Create priority system for event sources
3. Support configurable fade policies

### Deliverables
- [ ] `IDuckingService` interface and implementation
- [ ] Fade policies (Smooth, Quick, Instant)
- [ ] Priority queue for events
- [ ] Unit tests

### Coding Assistant Prompt

```markdown
## Task: Implement Ducking & Priority System

When event audio plays, duck (reduce volume) of primary source.

### Configuration
- DuckingPercentage: 20 (volume % when ducked)
- DuckingAttackMs: 100 (fade down time)
- DuckingReleaseMs: 500 (fade up time)
- DuckingPolicy: FadeSmooth | FadeQuick | Instant

### IDuckingService Interface
- StartDucking(eventSource)
- StopDucking(eventSource)
- CurrentDuckLevel property
- IsDucking property
```

---

## Phase 6: Audio Outputs

**Duration:** 4-5 days  
**Risk Level:** Medium  
**Priority:** High  
**Status:** ⚪ Not Started  
**Dependencies:** Phase 5

### Objectives
1. Implement local audio output (ALSA/default device)
2. Create Google Cast output for Chromecast
3. Support multiple simultaneous outputs

### Deliverables
- [ ] `IAudioOutput` interface
- [ ] Local audio output implementation
- [ ] Google Cast output using SharpCaster
- [ ] HTTP stream server for Cast
- [ ] Unit tests

### Coding Assistant Prompt

```markdown
## Task: Implement Audio Outputs

Create audio output implementations for local speakers and Chromecast.

### IAudioOutput Interface
- Initialize, Start, Stop, Dispose
- Volume, Mute controls
- State and events

### Local Output
- Use SoundFlow's default output device
- Support device selection from DeviceManager

### Google Cast Output
- Use SharpCaster library
- HTTP stream endpoint for audio data
- Device discovery and selection
```

---

## Phase 7: Visualization & Monitoring

**Duration:** 3-4 days  
**Risk Level:** Low  
**Priority:** Medium  
**Status:** ⚪ Not Started  
**Dependencies:** Phase 2

### Objectives
1. Implement spectrum analyzer using FFT
2. Create level meter (VU meter)
3. Build waveform analyzer

### Deliverables
- [ ] `IVisualizerService` interface
- [ ] Spectrum analyzer (FFT-based)
- [ ] Level meter
- [ ] Waveform analyzer
- [ ] Unit tests

### Coding Assistant Prompt

```markdown
## Task: Implement Audio Visualization

Create real-time audio visualization components.

### IVisualizerService
- GetSpectrumData() - FFT frequency bins
- GetLevelData() - Peak/RMS levels
- GetWaveformData() - Time-domain samples
- SampleRate, FFTSize configuration

### Implementation
- Tap into SoundFlow audio stream
- Process with FFT for spectrum
- Calculate peak/RMS for levels
- Buffer samples for waveform
```

---

## Phase 8: API & SignalR Integration

**Duration:** 4-5 days  
**Risk Level:** Low  
**Priority:** High  
**Status:** ⚪ Not Started  
**Dependencies:** Phase 6, Phase 7

### Objectives
1. Create REST API controllers
2. Implement SignalR hub for real-time data
3. Set up audio streaming endpoint

### Deliverables
- [ ] AudioController (playback control)
- [ ] SourcesController (source management)
- [ ] DevicesController (device enumeration)
- [ ] ConfigurationController (settings)
- [ ] AudioVisualizationHub (SignalR)
- [ ] Audio stream middleware
- [ ] API documentation (Swagger)

### Coding Assistant Prompt

```markdown
## Task: Implement API & SignalR

Create REST API and real-time SignalR integration.

### REST Endpoints
- GET/POST /api/audio - Playback state
- GET/POST /api/sources - Source selection
- GET /api/devices - Device enumeration
- GET/POST /api/configuration - Settings

### SignalR Hub
- AudioVisualizationHub
- Methods: GetSpectrum, GetLevels, GetWaveform
- Broadcasts at 30-60fps

### Audio Stream
- /stream/audio - HTTP audio stream
- PCM format for Chromecast
```

---

## Phase 9: Blazor UI Components

**Duration:** 7-10 days  
**Risk Level:** Medium  
**Priority:** High  
**Status:** ⚪ Not Started  
**Dependencies:** Phase 8

### Objectives
1. Create main navigation and layout
2. Build audio control components
3. Implement visualization displays
4. Create configuration management UI

### Deliverables
- [ ] Main layout with navigation bar
- [ ] Source selector component
- [ ] Volume/transport controls
- [ ] Now playing display
- [ ] Spectrum visualizer (Canvas)
- [ ] VU meter display
- [ ] Playlist grid
- [ ] Configuration manager
- [ ] Touch-friendly dialogs

### Coding Assistant Prompt

```markdown
## Task: Implement Blazor UI

Create Blazor Server components for the touchscreen interface. Reference `/design/WEBUI.md` for design specifications.

### Design Requirements
- 12.5" × 3.75" landscape display
- LED-style fonts (DSEG14) for time/frequency
- Industrial/retro aesthetic
- Touch-optimized (48-60px targets)

### Color Palette
- Background: Deep charcoal oklch(0.2 0.01 240)
- Accent: Electric cyan oklch(0.75 0.15 195)
- LED: Amber oklch(0.8 0.18 75)

### Key Components
1. SourceSelector.razor - Input dropdown
2. VolumeControl.razor - Slider with mute
3. TransportControls.razor - Play/pause/skip
4. NowPlaying.razor - Current track info
5. SpectrumVisualizer.razor - Canvas FFT display
6. ConfigurationManager.razor - Settings grid
```

---

## Phase 10: Testing & Quality Assurance

**Duration:** 5-7 days  
**Risk Level:** Low  
**Priority:** Critical  
**Status:** ⚪ Not Started  
**Dependencies:** All Previous Phases

### Objectives
1. Achieve >80% unit test coverage
2. Create integration tests
3. Perform end-to-end testing
4. Security and performance testing

### Deliverables
- [ ] Unit tests for all components
- [ ] Integration tests for API
- [ ] E2E tests for UI
- [ ] Performance benchmarks
- [ ] Security audit results
- [ ] Test coverage report

### Testing Categories

| Category | Scope | Tools |
|----------|-------|-------|
| Unit | Individual classes | xUnit, Moq |
| Integration | API endpoints | WebApplicationFactory |
| E2E | Full UI flows | Playwright |
| Performance | Audio latency | Custom benchmarks |

### Coding Assistant Prompt

```markdown
## Task: Implement Comprehensive Testing

Create tests for all components with >80% coverage goal.

### Unit Test Structure
tests/
├── Radio.Core.Tests/
│   ├── Models/
│   └── Services/
├── Radio.Infrastructure.Tests/
│   ├── Configuration/
│   ├── Audio/
│   └── External/
└── Radio.API.Tests/
    ├── Controllers/
    └── Hubs/

### Key Test Areas
1. Configuration store CRUD
2. Secret tag resolution
3. Audio engine state management
4. Source lifecycle
5. Ducking transitions
6. API request/response
```

---

## Phase 11: Documentation

**Duration:** 3-4 days  
**Risk Level:** Low  
**Priority:** Medium  
**Status:** ⚪ Not Started  
**Dependencies:** All Previous Phases

### Objectives
1. Complete API documentation
2. Create user guide
3. Write deployment guide
4. Document architecture decisions

### Deliverables
- [ ] API documentation (OpenAPI/Swagger)
- [ ] User manual
- [ ] Deployment guide
- [ ] Architecture decision records
- [ ] Configuration reference
- [ ] Troubleshooting guide

### Coding Assistant Prompt

```markdown
## Task: Create Project Documentation

Write comprehensive documentation for the Radio Console project.

### Documentation Structure
docs/
├── api/
│   └── openapi.yaml
├── user-guide/
│   ├── getting-started.md
│   ├── configuration.md
│   └── troubleshooting.md
├── deployment/
│   ├── raspberry-pi-setup.md
│   └── docker-deployment.md
└── architecture/
    ├── decisions/
    └── diagrams/
```

---

## Phase 12: Deployment & Optimization

**Duration:** 3-5 days  
**Risk Level:** Medium  
**Priority:** High  
**Status:** ⚪ Not Started  
**Dependencies:** All Previous Phases

### Objectives
1. Create deployment scripts for Raspberry Pi
2. Optimize for ARM64 performance
3. Set up systemd service
4. Configure production logging

### Deliverables
- [ ] setup-pi.sh deployment script
- [ ] systemd service file
- [ ] Production appsettings.json
- [ ] Performance optimizations
- [ ] Monitoring setup

### Coding Assistant Prompt

```markdown
## Task: Deploy to Raspberry Pi

Create deployment infrastructure for Raspberry Pi 5.

### Deployment Script (scripts/setup-pi.sh)
1. Install .NET 8 runtime
2. Install audio dependencies (ALSA, PulseAudio)
3. Configure audio permissions
4. Set up systemd service
5. Configure firewall

### Systemd Service
- Auto-start on boot
- Restart on failure
- Logging to journald

### Performance Optimizations
- AOT compilation where possible
- Memory limits
- Audio buffer tuning
```

---

## Progress Tracking

### Current Status

| Phase | Status | Started | Completed | Notes |
|-------|--------|---------|-----------|-------|
| 0 | 🟢 Completed | 2025-11-25 | 2025-11-25 | Solution structure, CI/CD, placeholder interfaces created |
| 1 | 🟢 Completed | 2025-11-25 | 2025-11-25 | Configuration infrastructure with JSON/SQLite stores, secrets management, backup/restore |
| 2 | ⚪ Not Started | - | - | |
| 3 | ⚪ Not Started | - | - | |
| 4 | ⚪ Not Started | - | - | |
| 5 | ⚪ Not Started | - | - | |
| 6 | ⚪ Not Started | - | - | |
| 7 | ⚪ Not Started | - | - | |
| 8 | ⚪ Not Started | - | - | |
| 9 | ⚪ Not Started | - | - | |
| 10 | ⚪ Not Started | - | - | |
| 11 | ⚪ Not Started | - | - | |
| 12 | ⚪ Not Started | - | - | |

### Status Legend
- ⚪ Not Started
- 🟡 In Progress
- 🟢 Completed
- 🔴 Blocked

---

## Updating This Plan

As each phase is completed:
1. Update the phase status in the Progress Tracking table
2. Add completion date
3. Note any deviations or learnings
4. Update dependencies if needed
5. Adjust estimates for remaining phases based on actual progress

---

*Last Updated: 2025-11-25*
*Next Review: After Phase 1 Completion*
