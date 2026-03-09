# Radio.Configuration

Managed configuration library with JSON and SQLite backing stores, encrypted secrets, backup/restore, and .NET `IConfiguration` bridge.

## Quick Start

```csharp
services.AddManagedConfiguration(configuration);
```

## Key Types

| Type | Purpose |
|------|---------|
| `IConfigurationManager` | High-level CRUD for config entries and secrets |
| `IConfigurationStore` | Low-level store abstraction (JSON or SQLite) |
| `IConfigurationStoreFactory` | Creates/caches store instances |
| `ISecretsProvider` | Encrypted secret storage with `${secret:tag}` resolution |
| `IConfigurationBackupService` | Store-level backup and restore |
| `ConfigurationOptions` | Paths, store type, auto-save settings |
| `ConfigStoreChangeNotifier` | Bridges config writes to `IOptionsMonitor` change tokens |
| `AddSecretResolution<T>()` | Post-configure secret tag resolution for any options type |

## Architecture

```
ConfigurationManager
  ├── IConfigurationStoreFactory
  │     ├── JsonConfigurationStore   (file-per-store)
  │     └── SqliteConfigurationStore (table-per-store)
  ├── ISecretsProvider (composite)
  │     ├── SqliteSecretsProvider     (primary, encrypted)
  │     └── JsonSecretsProvider       (fallback)
  └── IConfigurationBackupService
        └── Zip archive backup/restore

Bridge (IConfiguration integration):
  SqliteConfigurationProvider → IOptionsMonitor<T> change tokens
```

## Features

- **Dual backing stores**: JSON files or SQLite database, switchable via options
- **Encrypted secrets**: AES via `Microsoft.AspNetCore.DataProtection`, tag-based references (`${secret:identifier}`)
- **IConfiguration bridge**: SQLite values flow into .NET's `IOptions<T>` / `IOptionsMonitor<T>` pipeline
- **Backup/restore**: Zip-based config store snapshots with manifest
- **Secret resolution**: `AddSecretResolution<TOptions>()` auto-resolves `${secret:tag}` in any options type
