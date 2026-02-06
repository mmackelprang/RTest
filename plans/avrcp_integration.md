# AVRCP Metadata Integration Plan

## Objective
Enable retrieval of audio metadata (Artist, Title, Album, Duration, Status) from Bluetooth audio sources using AVRCP.

## 1. Clean Architecture Updates (`IBluetoothService`)

We need to expand the `IBluetoothService` interface to include events for metadata changes.

```csharp
// src/Radio.Core/Interfaces/Audio/IBluetoothService.cs

public enum BluetoothPlaybackStatus
{
    Stopped,
    Playing,
    Paused,
    ForwardSeek,
    ReverseSeek,
    Error
}

public class BluetoothPlaybackMetadata
{
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
    // TrackNumber, TotalTracks could be added if needed
}

public interface IBluetoothService : IAsyncDisposable
{
    // ... existing ...

    event EventHandler<BluetoothPlaybackMetadata> MetadataChanged;
    event EventHandler<BluetoothPlaybackStatus> PlaybackStatusChanged;
    event EventHandler<TimeSpan> PositionChanged; // Optional, might be too chatty
}
```

## 2. Infrastructure Layer: Linux (BlueZ)

We need to map BlueZ `org.bluez.MediaPlayer1` interface to our service.

### 2.1 DBus Interfaces
Update `BluezInterfaces.cs`:
- Add `IMediaPlayer1` interface.
- Properties: `Status` (string), `Position` (uint32), `Track` (dictionary).

```csharp
[DBusInterface("org.bluez.MediaPlayer1")]
public interface IMediaPlayer1 : IDBusObject
{
    Task PlayAsync();
    Task PauseAsync();
    Task StopAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
    // ... GetAsync/SetAsync standard methods
}
```

### 2.2 `LinuxBluetoothService` Implementation
- When a device connects, or on startup, check for `MediaPlayer1` interfaces associated with the device path.
- Subscribe to `PropertiesChanged` on the Media Player.
- **Mapping:**
    - `Status` -> `BluetoothPlaybackStatus`
        - "playing" -> Playing
        - "paused" -> Paused
        - "stopped" -> Stopped
    - `Track` (Dictionary) -> `BluetoothPlaybackMetadata`
        - "Title" -> Title
        - "Artist" -> Artist
        - "Album" -> Album
        - "Duration" (uint32 ms) -> Duration

## 3. Infrastructure Layer: Windows (`WindowsBluetoothService`)

For `net8.0-windows` target, we can potentially use `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`. 
However, since we are using `32feet.NET` (InTheHand) which focuses on RFCOMM/Sockets, getting AVRCP metadata might require Windows 10/11 specific APIs (`Windows.SDK`).

*Constraint:* If we cannot easily add `Windows.SDK` references or if it conflicts, we will **stub** this feature for Windows in this pass, or provide a "best effort" using just the device name.

**Plan for Windows:** Implement stubs to satisfy the interface. Log "AVRCP not implemented on Windows" for now, or use a basic "Unknown" metadata state.

## 4. Domain Logic (`BluetoothAudioSource`)

Wire up the source to the service events.

```csharp
// src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs

public override async Task InitializeAsync(...) {
    // ...
    _bluetoothService.MetadataChanged += OnMetadataChanged;
    _bluetoothService.PlaybackStatusChanged += OnPlaybackStatusChanged;
    // ...
}

private void OnMetadataChanged(object? sender, BluetoothPlaybackMetadata meta) {
    MetadataInternal[StandardMetadataKeys.Title] = meta.Title;
    MetadataInternal[StandardMetadataKeys.Artist] = meta.Artist;
    MetadataInternal[StandardMetadataKeys.Album] = meta.Album;
    
    // If duration > 0, we might expose it, but BluetoothAudioSource is a live stream.
    // We can perhaps start updating a "Now Playing" abstract property if we have one.
}
```

## 5. Documentation
- Update `design/AUDIO.md` to include AVRCP in the Bluetooth section.

## Execution Order
1. Modify `IBluetoothService.cs` (Core).
2. Modify `BluezInterfaces.cs` (Infrastructure/Linux).
3. Modify `LinuxBluetoothService.cs` (Infrastructure/Linux).
4. Modify `WindowsBluetoothService.cs` (Infrastructure/Windows) - Stubs.
5. Modify `BluetoothAudioSource.cs` (Infrastructure/AudioSource).
6. Verify clean build.
