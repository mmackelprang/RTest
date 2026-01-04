# Key Benefits of This Approach

* Complete Control: Start/stop Spotify device whenever you want
* Clean Integration: Fits naturally into your app's lifecycle
* Resource Efficient: Only runs when needed
* Graceful Shutdown: Proper cleanup on exit
* Error Recovery: Can restart on failures
* State Management: Track device state and react accordingly
* Auto-Shutdown: Can implement idle timeout to save resources
## Usage Patterns
###Pattern 1: Always-On (Traditional)
```
// Start on app launch
await audioManager.EnableSpotifyAsync(...);

// Stop on app exit
await audioManager.DisableSpotifyAsync();
```
###Pattern 2: User-Controlled
```
// User toggles Spotify in settings
if (userWantsSpotify)
    await audioManager.EnableSpotifyAsync(...);
else
    await audioManager.DisableSpotifyAsync();
```
###Pattern 3: On-Demand
```
// Start only when user plays Spotify content
if (currentTrack.Source == AudioSource.Spotify)
    await audioManager.EnableSpotifyAsync(...);
```
###Pattern 4: Auto-Idle Shutdown
```
// Automatically shutdown after 10 minutes of inactivity
var smartDevice = new SmartSpotifyDevice(librespotPath);
await smartDevice.EnsureDeviceRunningAsync(); // Starts if needed
// ... automatically shuts down when idle
```

This gives you complete control over the Spotify device lifecycle, making it a first-class citizen in your audio management application!
