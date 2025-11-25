# Radio Console

Grandpa Anderson's Console Radio Remade - A modern audio command center hosted on a Raspberry Pi 5, encased in a vintage console radio cabinet.

## Overview

This project restores the original function (Radio/Vinyl) while adding modern capabilities (Spotify, Streaming, Smart Home Events, and Chromecast Audio).

## Technical Architecture

| Component | Technology |
|-----------|------------|
| Hardware | Raspberry Pi 5 (Raspberry Pi OS / Linux) |
| Framework | .NET 8+ (C#) |
| Audio Engine | [SoundFlow](https://github.com/lsxprime/SoundFlow) |
| UI | Blazor Server |
| API | ASP.NET Core Web API |
| Real-time | SignalR |
| Database | Repository Pattern (SQLite / JSON) |
| Logging | Serilog |
| Testing | xUnit |

## Project Structure

```
RadioConsole/
├── src/
│   ├── Radio.Core/          # Core interfaces, models, and domain logic
│   ├── Radio.Infrastructure/ # Audio management, configuration, external integrations
│   ├── Radio.API/           # REST API and SignalR hubs
│   └── Radio.Web/           # Blazor Server UI
├── tests/
│   ├── Radio.Core.Tests/
│   ├── Radio.Infrastructure.Tests/
│   └── Radio.API.Tests/
├── design/                   # Design documents
└── scripts/                  # Deployment and utility scripts
```

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- For Raspberry Pi deployment: Raspberry Pi OS with .NET runtime

### Building

```bash
dotnet restore
dotnet build --configuration Release
```

### Running Tests

```bash
dotnet test --configuration Release
```

### Running the Applications

```bash
# Run the API
dotnet run --project src/Radio.API

# Run the Web UI
dotnet run --project src/Radio.Web
```

## Design Documents

- [Project Plan](PROJECTPLAN.md) - High-level project overview
- [Development Plan](PLAN.md) - Detailed development phases
- [Audio Architecture](design/AUDIO.md) - Audio system design
- [Configuration](design/CONFIGURATION.md) - Configuration infrastructure
- [Web UI](design/WEBUI.md) - UI design specifications

## License

See [LICENSE](LICENSE) file for details.

