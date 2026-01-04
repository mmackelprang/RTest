# UAT Debug & Fix Plan for Radio Console

This document provides a structured approach to debug, verify, and fix the issues discovered during UAT testing of the Radio Console application. Each phase includes detailed context, specific tasks, and Copilot-ready prompts for implementation.

## Issue Summary

1. **Now Playing & Controls** - Missing global controls on all pages
2. **Music Queue Visibility** - Queue not visible in navbar for all sources
3. **Now Playing Metadata** - Current song metadata not displaying
4. **Fingerprinting Debug** - No visible fingerprinting activity
5. **RTL-SDR Audio Output** - Radio doesn't supply audio to SoundFlow
6. **Queue Page Enhancements** - Multi-select, deduplication, auto-advance
7. **Visualizer Graphics** - VU Meter colors, waveform height, FPS metrics
8. **Home Page Controls** - Media controls not affecting audio
9. **File Browser** - Need network/drive browsing capability
10. **Spotify Loopback** - Implement librespot-based audio source
11. **Queue Page Touch UX** - Improve touch-friendliness
12. **Overall Material 3 Design** - Enhance touchscreen support

---

## Phase 1: Global Now Playing & Music Controls

### Context
The Web UI needs a persistent "Now Playing" widget and music controls visible on all pages. Currently, controls only exist on the Home page. Per `design/WEBUI.md`, Material 3 touch targets (48px minimum, 60px preferred) should be used.

### Issues to Fix
- No global now playing display visible across all pages
- Music controls not persistent in navbar/layout
- Controls should be source-aware (show/hide based on capabilities)

### Tasks

#### 1.1 Add Now Playing Widget to MainLayout
Create a compact now playing widget in the navbar that shows current track info and basic controls.

**Copilot Prompt:**
```
Add a global "Now Playing" widget to the MainLayout.razor navbar that displays:
1. Album art (thumbnail, ~40px) or music icon when no track
2. Track title, artist (truncated with ellipsis if too long)
3. Basic transport controls: Previous, Play/Pause, Next (48px touch targets)
4. Volume slider (compact, expandable on hover/tap)
5. The widget should appear between system stats and navigation icons
6. Use AudioStateHubService to get real-time updates
7. Style should match Material 3 dark theme
8. Controls should be disabled/hidden based on current source capabilities
9. Clicking the now playing area should navigate to home page
10. Ensure responsive layout doesn't break on 1920x576 display

Reference: design/WEBUI.md for Material 3 guidelines
Current files: src/Radio.Web/Components/Layout/MainLayout.razor
```

#### 1.2 Add Shuffle & Repeat Controls
Extend the now playing widget with shuffle and repeat controls.

**Copilot Prompt:**
```
Add shuffle and repeat controls to the global now playing widget in MainLayout.razor:
1. Add shuffle button (48px touch target) that highlights when active
2. Add repeat button that cycles: Off → All → One (with visual indicator)
3. Both should only be visible when current source supports them
4. Use AudioApiService to get/set shuffle and repeat state
5. Add tooltips showing current state
6. Persist state changes via ConfigurationApiService
7. Ensure controls update when source changes

Reference: src/Radio.Web/Components/Pages/Home.razor for control patterns
```

### Verification Steps
1. Start the web app and navigate to different pages
2. Verify now playing widget is visible on all pages
3. Play a track and verify metadata updates in real-time
4. Test all transport controls work correctly
5. Switch between sources and verify controls show/hide appropriately
6. Verify touch targets are at least 48px (preferably 60px for primary actions)

---

## Phase 2: Music Queue Navbar Visibility

### Context
The queue navigation icon currently only shows conditionally. Per `design/WEBUI.md`, the queue should be visible in the navbar regardless of audio source, as it's a core music management feature.

### Issues to Fix
- Queue nav icon hidden when source doesn't explicitly support queue
- No visual indicator of queue status (empty, items count)

### Tasks

#### 2.1 Make Queue Always Visible
Update MainLayout to always show queue navigation.

**Copilot Prompt:**
```
Update MainLayout.razor to make the queue navigation always visible:
1. Remove the conditional `@if (_showQueueNav)` wrapper around the queue icon button
2. Add a badge to the queue icon showing the number of items (e.g., "5")
3. Badge should be hidden when queue is empty
4. Subscribe to AudioStateHubService.QueueChanged event to update badge count
5. Add tooltip: "Queue (N items)" or "Queue (empty)"
6. Ensure icon is accessible on all pages

Current file: src/Radio.Web/Components/Layout/MainLayout.razor
Reference: src/Radio.Web/Services/Hub/AudioStateHubService.cs
```

### Verification Steps
1. Start the web app
2. Verify queue icon is visible regardless of selected audio source
3. Add items to queue and verify badge updates
4. Clear queue and verify badge disappears
5. Switch audio sources and verify queue remains visible

---

## Phase 3: Now Playing Metadata Display

### Context
The Now Playing display on the home page and global widget should show real-time metadata for the currently playing track. This requires proper data flow from AudioStateHub.

### Issues to Fix
- Metadata not updating when tracks change
- Album art not loading
- Source information not displaying

### Tasks

#### 3.1 Debug Metadata Flow
Investigate why metadata isn't flowing from backend to UI.

**Copilot Prompt:**
```
Debug and fix the metadata flow for now playing display:
1. Add debug logging in AudioStateUpdateService to log when metadata changes
2. Verify AudioStateHub is broadcasting metadata updates
3. Check AudioStateHubService is receiving and processing updates
4. Add console.log in browser to track SignalR messages
5. Verify Home.razor is subscribed to metadata change events
6. Check if metadata dictionary contains expected keys (Title, Artist, Album, AlbumArt)
7. Add null/empty checks and default values in display logic

Files to check:
- src/Radio.API/Services/AudioStateUpdateService.cs
- src/Radio.API/Hubs/AudioStateHub.cs
- src/Radio.Web/Services/Hub/AudioStateHubService.cs
- src/Radio.Web/Components/Pages/Home.razor
```

#### 3.2 Implement Album Art Loading
Ensure album art loads correctly from various sources.

**Copilot Prompt:**
```
Implement robust album art loading in the UI:
1. Support both URL-based album art (Spotify) and embedded art (file player)
2. Add loading indicator while fetching album art
3. Fall back to generic music icon if art unavailable or fails to load
4. Cache loaded images to prevent re-fetching
5. Handle CORS issues for external image URLs
6. Add error handling for image load failures
7. Ensure art updates when track changes

Files to update:
- src/Radio.Web/Components/Pages/Home.razor
- src/Radio.Web/Components/Layout/MainLayout.razor (for global widget)
```

### Verification Steps
1. Play a track from Spotify and verify metadata displays
2. Play a local file and verify metadata displays
3. Play an internet radio stream and verify station info displays
4. Verify album art loads (or fallback icon shows)
5. Switch tracks and verify metadata updates in real-time
6. Check browser console for any errors

---

## Phase 4: Fingerprinting Debug & Instrumentation

### Context
Audio fingerprinting should identify unknown tracks playing from radio/USB sources. The BackgroundIdentificationService exists but activity isn't visible. Need to verify it's working and add UI feedback.

### Issues to Fix
- No visible indication that fingerprinting is running
- No logs showing identification attempts
- Unknown if service is actually capturing audio
- No UI to show identification results

### Tasks

#### 4.1 Add Fingerprinting Instrumentation
Add comprehensive logging to track fingerprinting activity.

**Copilot Prompt:**
```
Add instrumentation to the fingerprinting system:
1. Add detailed logging in BackgroundIdentificationService:
   - When service starts/stops
   - When identification cycle begins
   - Audio capture attempts and results
   - API calls to AcoustID
   - Identification results (match/no match)
2. Add logging in SoundFlowAudioTap:
   - When audio capture is requested
   - How many samples captured
   - Any errors during capture
3. Add metrics collection for:
   - Total identification attempts
   - Successful identifications
   - Failed identifications
   - Average identification time
4. Log configuration on startup (enabled, interval, sample duration)

Files to update:
- src/Radio.Infrastructure/Audio/Fingerprinting/BackgroundIdentificationService.cs
- src/Radio.Infrastructure/Audio/Fingerprinting/SoundFlowAudioTap.cs
- src/Radio.Infrastructure/Audio/Fingerprinting/AcoustIdClient.cs
```

#### 4.2 Add Fingerprinting UI Indicator
Show fingerprinting status in the UI.

**Copilot Prompt:**
```
Add fingerprinting activity indicator to the Web UI:
1. Create a new component: Components/Shared/FingerprintingIndicator.razor
2. Show a subtle animated icon when fingerprinting is active
3. Display last identification result (track name if found, "Unknown" if not)
4. Show identification timestamp
5. Add to MainLayout near system stats
6. Use SignalR or polling to get real-time status
7. Consider adding a badge/notification when a track is identified
8. Make it dismissible but persistent across navigation

New file: src/Radio.Web/Components/Shared/FingerprintingIndicator.razor
Update: src/Radio.Web/Components/Layout/MainLayout.razor
Reference: src/Radio.Core/Events/TrackIdentifiedEventArgs.cs
```

#### 4.3 Verify Audio Capture Pipeline
Ensure SoundFlowAudioTap is correctly wired into the audio engine.

**Copilot Prompt:**
```
Debug and verify the audio capture pipeline for fingerprinting:
1. Check SoundFlowAudioEngine.GetMixedOutputStream() returns a valid stream
2. Verify the TappedOutputStream is properly configured
3. Add unit test for SoundFlowAudioTap.CaptureAsync
4. Test with mock audio data first
5. Verify IAudioSampleProvider is registered in DI
6. Check FingerprintingOptions are loaded correctly from configuration
7. Add integration test that captures real audio from a playing source

Files to check/update:
- src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs
- src/Radio.Infrastructure/Audio/Fingerprinting/SoundFlowAudioTap.cs
- tests/Radio.Infrastructure.Tests/Audio/Fingerprinting/ (create tests)
```

### Verification Steps
1. Enable fingerprinting in configuration (`Audio:Fingerprinting:Enabled = true`)
2. Start the application and check logs for fingerprinting service startup
3. Play a radio stream or vinyl source
4. Watch logs for identification attempts every N seconds
5. Verify audio samples are being captured
6. Check if AcoustID API calls are being made
7. Verify identification results appear in logs
8. Check UI indicator shows fingerprinting activity
9. Review metrics dashboard for fingerprinting statistics

---

## Phase 5: RTL-SDR Radio Audio Output

### Context
The SDRRadioAudioSource wraps RTLSDRCore.RadioReceiver but may not be properly feeding audio to SoundFlow's mixer. Need to verify the SDRAudioDataProvider is correctly wired.

### Issues to Fix
- RTL-SDR radio audio not reaching SoundFlow output
- No audio heard when SDR radio is active source
- Possible issue with SDRAudioDataProvider integration

### Tasks

#### 5.1 Debug SDR Audio Pipeline
Investigate the audio flow from RadioReceiver to SoundFlow.

**Copilot Prompt:**
```
Debug the RTL-SDR audio pipeline to identify why audio isn't flowing:
1. Add logging in SDRRadioAudioSource:
   - When GetSoundComponent() is called
   - When SDRAudioDataProvider is created
   - When RadioReceiver.AudioDataAvailable fires
2. Add logging in SDRAudioDataProvider (if it exists):
   - When audio data is received from RadioReceiver
   - When audio data is passed to SoundFlow
   - Buffer sizes and sample rates
3. Verify RadioReceiver is properly initialized and started
4. Check if RadioReceiver.AudioDataAvailable event has subscribers
5. Verify the audio format matches SoundFlow expectations (sample rate, channels, format)
6. Add unit tests for SDRAudioDataProvider

Files to check:
- src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs
- Look for SDRAudioDataProvider.cs (may need to be created)
- src/RTLSDRCore/RadioReceiver.cs (in submodule)
```

#### 5.2 Implement/Fix SDRAudioDataProvider
Ensure proper adapter between RTLSDRCore and SoundFlow.

**Copilot Prompt:**
```
Create or fix SDRAudioDataProvider to bridge RTLSDRCore and SoundFlow:
1. Implement IDataProvider or appropriate SoundFlow interface
2. Subscribe to RadioReceiver.AudioDataAvailable event
3. Convert RTLSDRCore audio format to SoundFlow format if needed
4. Handle sample rate conversion if necessary (RTL-SDR likely outputs different rate)
5. Implement proper buffering to prevent underruns
6. Add error handling and logging
7. Ensure proper disposal of resources
8. Test with live RTL-SDR device or mock audio data

Create/update: src/Radio.Infrastructure/Audio/Sources/Primary/SDRAudioDataProvider.cs
Reference: SoundFlow documentation for IDataProvider interface
Reference: src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs
```

#### 5.3 Test SDR Audio End-to-End
Create tests and manual verification steps.

**Copilot Prompt:**
```
Add tests and verification for SDR radio audio:
1. Create integration test that:
   - Initializes SDRRadioAudioSource
   - Starts playback
   - Verifies GetSoundComponent() returns valid object
   - Mocks RadioReceiver to emit test audio
   - Verifies audio flows through pipeline
2. Add metrics for SDR audio (samples processed, underruns, overruns)
3. Add UI indication when SDR is active (frequency, signal strength)
4. Test with actual hardware if available
5. Document any RTL-SDR specific setup requirements

New test file: tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/SDRRadioAudioSourceTests.cs
```

### Verification Steps
1. Connect RTL-SDR device
2. Select "SDR Radio" as audio source in UI
3. Tune to a known FM station
4. Verify audio is heard through speakers
5. Check logs for audio data flow
6. Monitor metrics for sample processing
7. Verify signal strength and frequency are updating
8. Test changing frequency and verify audio updates

---

## Phase 6: Queue Page Enhancements

### Context
The queue page needs several UX improvements: multi-select file dialog, duplicate prevention, auto-advance when tracks finish, and support for multiple source types.

### Issues to Fix
- File dialog doesn't support multi-select
- Can't add files directly from dialog to queue
- Same file can be added multiple times
- Queue doesn't auto-advance when track finishes
- Only FilePlayer songs supported (need Spotify too)
- Queue should be persisted on shutdown and restored on startup.
- The last search performed in Spotify should persist the search and all found items should be displayed the next time Spotify search is displayed.
- The last selected audio input, audio output, volume, etc. should be persisted when selected by the user and should be restored next time the Web UI is started.

### Tasks

#### 6.1 Multi-Select File Dialog
Update AudioFileSelectionDialog to support multiple file selection.

**Copilot Prompt:**
```
Update the file selection dialog to support multi-select:
1. Modify AudioFileSelectionDialog.razor to allow multi-file selection
2. Change file input to accept multiple files: <input type="file" multiple>
3. Show list of selected files before adding to queue
4. Add "Select All" / "Deselect All" options if browsing folder
5. Add preview of total duration for selected files
6. Update dialog result to return List<string> paths
7. Ensure touch-friendly checkbox selection (48px targets)
8. Add loading indicator while processing multiple files

File to update: src/Radio.Web/Components/Dialogs/AudioFileSelectionDialog.razor
Reference: design/WEBUI.md for touch target guidelines
```

#### 6.2 Add to Queue from Dialog
Add ability to queue files directly from the dialog without closing it.

**Copilot Prompt:**
```
Add "Add to Queue" functionality within AudioFileSelectionDialog:
1. Add "Add to Queue" button alongside "Select" button
2. Allow adding files without closing dialog (for batch operations)
3. Show confirmation when files are added (toast notification)
4. Keep dialog open for continued browsing
5. Add "Add All & Close" option
6. Update queue count badge in real-time as files are added
7. Disable adding files already in queue (show visual indicator)

File to update: src/Radio.Web/Components/Dialogs/AudioFileSelectionDialog.razor
Reference: src/Radio.Web/Services/ApiClients/QueueApiService.cs
```

#### 6.3 Duplicate Prevention
Prevent adding the same track to queue multiple times.

**Copilot Prompt:**
```
Implement duplicate detection and prevention for queue:
1. Add method to QueueApiService to check if track already exists
2. Use file path or unique identifier for comparison
3. Show visual indicator in file browser if file is already queued
4. Add option in settings to allow/disallow duplicates
5. If duplicate found, show dialog: "Already in queue. Add anyway?"
6. For Spotify tracks, compare track URI
7. Consider case where same track from different sources is valid

Files to update:
- src/Radio.Web/Services/ApiClients/QueueApiService.cs
- src/Radio.API/Controllers/QueueController.cs (may need new endpoint)
- src/Radio.Web/Components/Pages/QueuePage.razor
```

#### 6.4 Auto-Advance on Track End
Implement auto-advance to next track in queue.

**Copilot Prompt:**
```
Implement auto-advance functionality when a queued track finishes:
1. Subscribe to AudioStateHub.TrackEnded event
2. Check if shuffle is enabled:
   - If yes: select random track from remaining queue items
   - If no: advance to next sequential track
3. Handle edge cases:
   - Last track in queue (stop or restart based on repeat mode)
   - Repeat One mode (replay same track)
   - Repeat All mode (loop back to start)
4. Respect user preferences for auto-advance (add config option)
5. Add visual feedback when auto-advancing
6. Log auto-advance actions for debugging

Files to update:
- src/Radio.Web/Components/Pages/QueuePage.razor
- src/Radio.API/Services/AudioStateUpdateService.cs (may need new event)
- src/Radio.Core/Configuration/ (add AutoAdvanceEnabled option)
```

#### 6.5 Spotify Queue Support
Enable adding Spotify tracks to queue.

**Copilot Prompt:**
```
Add Spotify track support to the queue system:
1. Update queue to handle different source types (FilePlayer, Spotify)
2. Add source type indicator in queue display (icon or badge)
3. Allow adding Spotify tracks from SpotifyPage to queue
4. Update QueueItemDto to include source type and source-specific data
5. Handle playback transition between FilePlayer and Spotify sources
6. Store Spotify track URIs in queue
7. Ensure Spotify authentication state is checked before adding

Files to update:
- src/Radio.API/Models/AudioSourceDtos.cs (QueueItemDto)
- src/Radio.Web/Components/Pages/SpotifyPage.razor
- src/Radio.Web/Components/Pages/QueuePage.razor
- src/Radio.API/Controllers/QueueController.cs
```

### Verification Steps
1. Open file selection dialog and select multiple files
2. Verify all selected files are shown before adding
3. Add files to queue and verify they appear
4. Try adding the same file again and verify duplicate warning
5. Play through queue and verify auto-advance to next track
6. Enable shuffle and verify random selection
7. Test repeat modes (Off, One, All)
8. Add Spotify tracks to queue and verify mixed queue works
9. Verify queue persists across app restarts

---

## Phase 7: Visualizer Graphics Improvements

### Context
The visualizer needs several visual enhancements: color-coded VU meters, expanded waveform height, and FPS/updates-per-second metrics.

### Issues to Fix
- VU Meter lacks color bands (should transition blue→green→yellow→red)
- VU Meter max height should auto-adjust based on recent average
- Waveform is vertically compressed
- Only shows "Last Update" timestamp, not updates per second
- Updates/sec should be tracked for all visualization modes

### Tasks

#### 7.1 Enhance VU Meter Colors
Implement rainbow color gradient for VU meters.

**Copilot Prompt:**
```
Update VU meter visualization to use rainbow color gradient:
1. Modify drawMeter() in visualizer.js
2. Implement color bands by height:
   - 0-20%: Blue (#0000FF to #0080FF)
   - 20-40%: Cyan (#0080FF to #00FFFF)
   - 40-60%: Green (#00FF00)
   - 60-80%: Yellow (#FFFF00)
   - 80-95%: Orange (#FF8000)
   - 95-100%: Red (#FF0000)
3. Use smooth gradient transitions between bands
4. Apply to both peak and RMS bars
5. Maintain existing peak hold indicator (white/red)

File to update: src/Radio.Web/wwwroot/js/visualizer.js (drawMeter function)
Current code: Lines 75-150 approximately
```

#### 7.2 Dynamic VU Meter Scaling
Implement auto-adjusting max height for VU meters.

**Copilot Prompt:**
```
Add dynamic scaling to VU meter max height:
1. Track recent peak values in a rolling buffer (last 5-10 seconds)
2. Calculate average maximum peak over that window
3. Scale the meter so average max reaches ~80% of display height
4. Implement smooth transitions when scaling changes (ease-in-out)
5. Still show values above scaled max (clipping indicator)
6. Add visual indicator showing current scale factor (e.g., "×1.5")
7. Reset scale when audio stops or is very quiet for extended period

File to update: src/Radio.Web/wwwroot/js/visualizer.js
Add new properties to canvasData for tracking: recentPeaks[], scaleFactor, lastScaleUpdate
```

#### 7.3 Expand Waveform Height
Increase vertical amplitude of waveform visualization.

**Copilot Prompt:**
```
Increase the vertical scale of waveform visualization:
1. Modify drawWaveform() in visualizer.js
2. Change amplitude multiplier from current value to 2.5x or 3x
3. Add optional auto-scaling based on peak amplitude in waveform data
4. Ensure waveform doesn't clip at top/bottom of canvas
5. Center waveform vertically in canvas
6. Consider adding subtle grid lines for reference
7. Add option to manually adjust waveform scale (user preference)

File to update: src/Radio.Web/wwwroot/js/visualizer.js (drawWaveform function)
Current code: Check around lines 150-200
```

#### 7.4 Add Updates Per Second Metric
Replace timestamp with FPS/updates-per-second display.

**Copilot Prompt:**
```
Add updates-per-second (UPS) metric to all visualizations:
1. Track timestamps of last N updates (circular buffer)
2. Calculate UPS by dividing N by time span
3. Update UPS display every second (not every frame)
4. Show as "Updates: XX/sec" in bottom-left corner
5. Color-code based on performance:
   - Green: >30 UPS
   - Yellow: 15-30 UPS
   - Red: <15 UPS
6. Add to all three visualization modes (VU, Waveform, Spectrum)
7. Persist UPS history for performance trending

Files to update:
- src/Radio.Web/Components/Pages/VisualizerPage.razor (add UPS display)
- src/Radio.Web/wwwroot/js/visualizer.js (add UPS calculation)
```

### Verification Steps
1. Open visualizer page and select VU Meter mode
2. Play audio and verify color gradient (blue at low levels, red at peaks)
3. Play quiet then loud audio, verify meter scale adjusts
4. Switch to Waveform mode
5. Verify waveform has increased height and isn't compressed
6. Check bottom-left corner shows "Updates: XX/sec" instead of timestamp
7. Switch to Spectrum mode and verify UPS display there too
8. Monitor UPS values during normal playback (should be stable)
9. Check performance on Raspberry Pi (target >20 UPS)

---

## Phase 8: Home Page Media Controls

### Context
The media control buttons on the Home page don't affect audio playback. Controls should always operate on the currently playing audio source.

### Issues to Fix
- Transport controls (play/pause/next/previous) not triggering API calls
- Volume/balance sliders not updating audio
- Controls not reflecting current audio state
- Missing error handling for control operations

### Tasks

#### 8.1 Debug Control Event Handlers
Verify event handlers are properly wired and calling API.

**Copilot Prompt:**
```
Debug and fix the event handlers for media controls on Home page:
1. Add logging to all control methods:
   - HandlePlayPauseAsync()
   - HandleNextAsync()
   - HandlePreviousAsync()
   - HandleShuffleAsync()
   - HandleRepeatAsync()
   - OnVolumeChanged()
   - OnBalanceChanged()
2. Verify each method calls appropriate AudioApiService method
3. Add error handling with user-visible error messages (snackbar)
4. Ensure controls are enabled/disabled based on audio state
5. Add debouncing for slider controls (volume, balance)
6. Verify API calls complete successfully (check response status)
7. Update UI state immediately for responsive feel (optimistic updates)

File to update: src/Radio.Web/Components/Pages/Home.razor
Reference: src/Radio.Web/Services/ApiClients/AudioApiService.cs
```

#### 8.2 Sync Control State with Audio State
Ensure controls reflect actual audio state at all times.

**Copilot Prompt:**
```
Implement proper state synchronization between UI controls and audio engine:
1. Subscribe to AudioStateHub events in Home.razor:
   - PlayStateChanged (playing/paused/stopped)
   - VolumeChanged
   - ShuffleChanged
   - RepeatModeChanged
2. Update local state variables when events fire
3. Call StateHasChanged() to refresh UI
4. Handle race conditions (local change vs. remote change)
5. Add visual feedback when control operation is in progress (spinner)
6. Implement reconnection logic if SignalR connection drops
7. Load initial state on component initialization

File to update: src/Radio.Web/Components/Pages/Home.razor
Reference: src/Radio.Web/Services/Hub/AudioStateHubService.cs
```

#### 8.3 Add Control Response Feedback
Provide immediate visual/haptic feedback for control interactions.

**Copilot Prompt:**
```
Add visual feedback for control interactions:
1. Add subtle animation when buttons are clicked (ripple effect)
2. Show loading spinner on button while API call is in progress
3. Use MudBlazor's button loading state
4. Add haptic feedback simulation (visual pulse) for touch screens
5. Show snackbar notification for important state changes
6. Add transition animations for slider changes
7. Ensure 60px touch targets for primary controls (play/pause)
8. Add keyboard shortcuts for common controls (space for play/pause)

File to update: src/Radio.Web/Components/Pages/Home.razor
Use: MudButton Loading property, MudBlazor animations
```

### Verification Steps
1. Open Home page
2. Click Play and verify audio starts
3. Click Pause and verify audio pauses
4. Adjust volume slider and verify volume changes
5. Adjust balance slider and verify L/R balance changes
6. Click Next/Previous and verify track changes (if source supports it)
7. Toggle Shuffle and verify state persists
8. Cycle Repeat mode and verify it works
9. Check browser console for any errors
10. Open developer tools network tab and verify API calls are made
11. Test on touch screen for responsiveness

---

## Phase 9: File Browser Network & Drive Access

### Context
The file browser currently only shows local filesystem. Need to add support for browsing network shares, different drives, and other storage locations. May need virtual keyboard for touchscreen.

### Issues to Fix
- Can't browse network shares or UNC paths
- Limited to single drive on Windows
- No way to enter custom paths on touch screen
- No virtual keyboard for path entry

### Tasks

#### 9.1 Add Drive/Share Selection
Implement drive selection and network path browsing.

**Copilot Prompt:**
```
Add multi-drive and network share support to file browser:
1. Add drive selector dropdown at top of FileBrowserPage
2. On Windows: List all available drives (C:, D:, etc.)
3. On Linux: Show mounted filesystems from /etc/mtab or /proc/mounts
4. Add "Network" option to browse UNC paths (Windows) or mounted network shares (Linux)
5. Show drive/share info (total space, free space, type)
6. Add breadcrumb navigation for path traversal
7. Handle permission errors gracefully (show "Access Denied" message)
8. Remember last browsed location per drive

File to update: src/Radio.Web/Components/Pages/FileBrowserPage.razor
New API endpoints may be needed in FileController for drive enumeration
Reference: System.IO.DriveInfo for Windows, /proc/mounts for Linux
```

#### 9.2 Custom Path Entry
Add text input for entering custom paths (UNC, URLs, etc.).

**Copilot Prompt:**
```
Add custom path entry to file browser:
1. Add text input field for manual path entry at top of page
2. Support UNC paths (\\\\server\\share)
3. Support URLs for network streams (http://, ftp://)
4. Add "Go" button (60px touch target) next to input
5. Show recent paths in dropdown for quick access
6. Validate path format before navigating
7. Handle various path separators (/, \\)
8. Add "Add to Favorites" option for frequently used paths
9. Style with Material 3 text field component

File to update: src/Radio.Web/Components/Pages/FileBrowserPage.razor
```

#### 9.3 Virtual Keyboard Integration
Add touch-friendly virtual keyboard for path entry.

**Copilot Prompt:**
```
Implement virtual keyboard for touchscreen path entry:
1. Research available JavaScript virtual keyboard libraries:
   - simple-keyboard (https://github.com/hodgef/simple-keyboard)
   - react-simple-keyboard (if applicable)
2. Add virtual keyboard component that appears when text input is focused
3. Position keyboard overlay at bottom of screen
4. Include common path characters: / \\ : . @ 
5. Add number row and special characters
6. Implement "Close" button to dismiss keyboard
7. Make keyboard responsive to screen size
8. Add configuration option to disable if physical keyboard is available
9. Test on touch screen for usability

New component: src/Radio.Web/Components/Shared/VirtualKeyboard.razor
Add JavaScript interop: src/Radio.Web/wwwroot/js/virtual-keyboard.js
May need npm package: simple-keyboard
```

#### 9.4 Network Discovery (Advanced)
Implement network device/share discovery for easier browsing.

**Copilot Prompt:**
```
Add network discovery for SMB/CIFS shares (optional, stretch goal):
1. Add "Browse Network" button that scans for available shares
2. Use Samba client libraries on Linux (libsmbclient)
3. Use Windows networking APIs on Windows
4. Show list of discovered servers and shares
5. Handle authentication for protected shares (dialog for credentials)
6. Cache discovered shares for performance
7. Add loading indicator during scan (can take 10-30 seconds)
8. Show icons for different device types (NAS, PC, etc.)

This is complex and may require platform-specific code
Consider deferring if time-constrained
Reference: Samba client libraries, Windows NetAPI
```

### Verification Steps
1. Open file browser page
2. Verify drive selector shows all available drives
3. Switch between drives and verify navigation works
4. Enter a UNC path manually and verify it loads
5. Test virtual keyboard on touch screen
6. Enter various path formats and verify validation
7. Add path to favorites and verify quick access
8. Test on both Windows and Linux (if applicable)
9. Try browsing a network share with authentication
10. Verify error handling for inaccessible paths

---

## Phase 10: Spotify Loopback Implementation

### Context
The current SpotifyAudioSource may not be working correctly. The `/SpotifyLoopback` folder contains examples for using `librespot` as an audio source. Need to implement a new SpotifyPrimaryAudioSource that integrates librespot with SoundFlow.

### Issues to Fix
- Spotify in loopback mode not feeding audio to SoundFlow
- Need proper integration of librespot process
- Missing lifecycle management for Spotify device

### Tasks

#### 10.1 Review Loopback Architecture
Understand the existing SpotifyLoopback code and design.

**Copilot Prompt:**
```
Review the Spotify Loopback implementation approach:
1. Read all files in /SpotifyLoopback folder:
   - README.md
   - LibrespotManager.cs
   - AudioDeviceManager.cs
   - SmartSpotifyDevice.cs
   - Program.cs
2. Review design/SPOTIFY_LOOPBACK_IMPLEMENTATION.md
3. Review existing SPOTIFY_LOOPBACK_*.md files in root
4. Understand the lifecycle management pattern
5. Identify how librespot process is started/stopped
6. Understand audio routing (librespot -> ALSA/PulseAudio -> SoundFlow)
7. Document findings and create integration plan

Output: Add section to this document or create SPOTIFY_INTEGRATION_PLAN.md
```

#### 10.2 Implement LibrespotAudioSource
Create new audio source that wraps librespot.

**Copilot Prompt:**
```
Implement LibrespotAudioSource as a PrimaryAudioSourceBase:
1. Create new class: src/Radio.Infrastructure/Audio/Sources/Primary/LibrespotAudioSource.cs
2. Implement IAudioSource interface
3. Use LibrespotManager from /SpotifyLoopback as reference
4. Integrate with existing SpotifyAudioSource or replace it
5. Start librespot process on Enable()
6. Stop librespot process on Disable()
7. Capture audio output via ALSA loopback or PulseAudio monitoring
8. Feed captured audio to SoundFlow
9. Implement metadata retrieval from Spotify Connect protocol
10. Handle authentication (credentials from configuration)
11. Add comprehensive logging
12. Implement proper disposal pattern

New file: src/Radio.Infrastructure/Audio/Sources/Primary/LibrespotAudioSource.cs
Reference: /SpotifyLoopback/LibrespotManager.cs
Reference: design/SPOTIFY_LOOPBACK_IMPLEMENTATION.md
```

#### 10.3 Audio Capture from Librespot
Implement audio capture from librespot output.

**Copilot Prompt:**
```
Implement audio capture from librespot to SoundFlow:
1. Determine audio routing strategy:
   Option A: Use ALSA loopback device
   Option B: Use PulseAudio monitor source
   Option C: Pipe audio directly (librespot --pipe)
2. Implement chosen strategy in LibrespotAudioDataProvider
3. Convert captured PCM audio to SoundFlow format
4. Handle sample rate matching (librespot outputs 44.1kHz)
5. Implement buffering to prevent underruns
6. Add latency measurement and compensation
7. Handle case where librespot process dies unexpectedly
8. Test audio quality and synchronization

New file: src/Radio.Infrastructure/Audio/Sources/Primary/LibrespotAudioDataProvider.cs
Reference: SoundFlow documentation for audio input requirements
```

#### 10.4 Spotify Device Lifecycle Management
Integrate SmartSpotifyDevice for automatic lifecycle management.

**Copilot Prompt:**
```
Integrate SmartSpotifyDevice for Spotify device lifecycle:
1. Add SmartSpotifyDevice.cs from /SpotifyLoopback to Infrastructure
2. Integrate with LibrespotAudioSource
3. Implement auto-start when Spotify source is selected
4. Implement auto-stop after idle timeout (configurable)
5. Add health monitoring (restart if crashed)
6. Show device status in UI (running, stopped, error)
7. Add manual start/stop controls in settings
8. Handle credentials securely (use SecretsProvider)
9. Add metrics for device uptime, restarts, errors

New file: src/Radio.Infrastructure/Audio/Spotify/SmartSpotifyDevice.cs
Update: src/Radio.Infrastructure/Audio/Sources/Primary/LibrespotAudioSource.cs
Reference: /SpotifyLoopback/SmartSpotifyDevice.cs
```

#### 10.5 UI Integration and Testing
Add UI controls and test Spotify functionality.

**Copilot Prompt:**
```
Add UI controls for Spotify device management and test:
1. Add Spotify device status indicator to MainLayout or settings page
2. Show device name, status (online/offline), IP address
3. Add manual start/stop buttons
4. Show current playback info from Spotify (track, artist, album)
5. Add troubleshooting info (logs, connection status)
6. Create integration test:
   - Start LibrespotAudioSource
   - Connect from Spotify app
   - Play a track
   - Verify audio flows to SoundFlow
   - Verify metadata is captured
7. Document setup requirements in README

Files to update:
- src/Radio.Web/Components/Pages/SpotifyPage.razor (add device status)
- Update README.md with Spotify setup instructions
- Create: docs/SPOTIFY_SETUP.md with detailed guide
```

### Verification Steps
1. Install librespot binary on target system
2. Configure Spotify credentials in app settings
3. Start LibrespotAudioSource via UI
4. Verify librespot process is running (ps aux | grep librespot)
5. Open Spotify app on phone or desktop
6. Look for Radio Console device in Spotify Connect list
7. Select device and play a track
8. Verify audio is heard through Radio Console speakers
9. Verify track metadata appears in Now Playing
10. Test pause/play/skip from Spotify app
11. Verify device appears/disappears when started/stopped
12. Check logs for any errors
13. Test auto-stop after idle period

---

## Phase 11: Queue Page Touch UX Improvements

### Context
The queue page needs better touch support. When only one item is in queue, it should be clearly visible. Touch interactions need larger targets and clearer affordances.

### Issues to Fix
- Single queue item not visible or hard to see
- Touch targets too small for reliable interaction
- Drag-and-drop not intuitive on touch screen
- Missing touch-specific gestures (swipe to delete, etc.)

### Tasks

#### 11.1 Improve Single Item Display
Ensure single queue items are prominently displayed.

**Copilot Prompt:**
```
Fix queue display when only one item exists:
1. Remove minimum row requirements for MudTable display
2. Ensure single item is shown with full height (not compressed)
3. Add visual emphasis to single item (larger, centered?)
4. Show helpful text: "1 track in queue" above the item
5. Ensure touch targets are at least 60px height
6. Make entire row clickable to play
7. Add larger delete button (60px touch target)
8. Style to make it clear this is an interactive element

File to update: src/Radio.Web/Components/Pages/QueuePage.razor
Check: Empty state handling, table rendering with single row
```

#### 11.2 Enhance Touch Targets
Increase size of all interactive elements.

**Copilot Prompt:**
```
Improve touch targets throughout queue page:
1. Increase row height to minimum 60px
2. Make action buttons at least 48px (preferably 60px)
3. Increase drag handle size and visibility
4. Add padding around clickable areas
5. Implement ripple effect on tap for feedback
6. Increase spacing between rows to prevent mis-taps
7. Make track titles larger and easier to read
8. Add visual hover state for touch preview (on supported devices)

File to update: src/Radio.Web/Components/Pages/QueuePage.razor
Reference: design/WEBUI.md for Material 3 touch guidelines
```

#### 11.3 Add Swipe Gestures
Implement touch gestures for common actions.

**Copilot Prompt:**
```
Add swipe gesture support to queue items:
1. Implement swipe-to-delete gesture (swipe left to reveal delete button)
2. Add swipe-right for quick actions (add to favorites, etc.)
3. Use Hammer.js or similar touch library
4. Show visual indication of swipe direction and action
5. Add "undo" snackbar after delete
6. Make swipe threshold configurable
7. Add animations for smooth swipe transitions
8. Test on actual touch screen

File to update: src/Radio.Web/Components/Pages/QueuePage.razor
New JS file: src/Radio.Web/wwwroot/js/touch-gestures.js
Consider: Hammer.js or use native Touch events API
```

#### 11.4 Improve Drag Reordering for Touch
Make drag-and-drop more intuitive on touch screens.

**Copilot Prompt:**
```
Improve drag-and-drop for touch screens:
1. Increase drag handle size and make it more obvious
2. Add visual feedback when drag starts (item lifts with shadow)
3. Show drop zones more clearly
4. Implement haptic feedback (visual pulse) when hovering over drop zone
5. Auto-scroll when dragging near top/bottom edges
6. Add "long-press to drag" mode for better touch control
7. Show item preview while dragging
8. Consider sortable.js library for better touch support

File to update: src/Radio.Web/Components/Pages/QueuePage.razor
Consider library: SortableJS for touch-optimized drag-and-drop
```

### Verification Steps
1. Add one item to queue
2. Verify it's clearly visible and not hidden
3. Verify touch targets are large enough
4. Add multiple items and test scrolling
5. Test drag-and-drop reordering on touch screen
6. Test swipe-to-delete gesture
7. Verify visual feedback for all interactions
8. Test on actual touch device (not just mouse)
9. Verify no mis-taps or difficulty selecting items
10. Check accessibility for keyboard navigation too

---

## Phase 12: Overall Material 3 Design & Touch Optimization

### Context
The entire Web UI should follow Material 3 design principles and be optimized for the 12.5" × 3.75" touchscreen display (1920x576 resolution). Need comprehensive review and polish.

### Issues to Fix
- Inconsistent design language across pages
- Some pages not optimized for touch
- Navigation not consistent
- Color scheme and typography need review
- Animations and transitions need polish

### Tasks

#### 12.1 Material 3 Design Audit
Review all pages for Material 3 compliance.

**Copilot Prompt:**
```
Conduct comprehensive Material 3 design audit:
1. Check each page for compliance with Material 3 guidelines:
   - Home.razor
   - QueuePage.razor
   - SpotifyPage.razor
   - FileBrowserPage.razor
   - VisualizerPage.razor
   - RadioPage.razor
   - SystemConfigPage.razor
   - MetricsDashboardPage.razor
2. Document issues in DESIGN_AUDIT.md:
   - Touch target sizes (<48px)
   - Color contrast issues
   - Spacing and density problems
   - Typography inconsistencies
   - Missing or incorrect component usage
3. Prioritize issues by severity (critical, important, nice-to-have)
4. Create remediation plan with specific changes

Output: Create DESIGN_AUDIT.md with findings
Reference: design/WEBUI.md for guidelines
Reference: Material 3 design guidelines (material.io)
```

#### 12.2 Touch Target Standardization
Ensure all interactive elements meet touch size requirements.

**Copilot Prompt:**
```
Standardize touch targets across entire application:
1. Create CSS utility classes for standard touch sizes:
   .touch-target-small (48px)
   .touch-target-medium (60px)  
   .touch-target-large (72px)
2. Apply classes to all buttons, links, and interactive elements
3. Use MudBlazor Size property: Size.Large for primary actions
4. Add padding/margin to prevent accidental taps
5. Ensure icon buttons use IconSize.Large where appropriate
6. Test on actual touch device
7. Document standard sizes in style guide

File to create: src/Radio.Web/wwwroot/css/touch-targets.css
Files to update: All .razor pages (apply classes)
```

#### 12.3 Color & Typography Refinement
Standardize colors and typography per Material 3.

**Copilot Prompt:**
```
Refine color scheme and typography:
1. Review and update _customTheme in MainLayout.razor
2. Ensure color contrast meets WCAG AA standards (4.5:1)
3. Define semantic colors:
   - Primary action (play button, confirm)
   - Secondary action (cancel, back)
   - Error/warning/success states
   - Surface and background variants
4. Standardize font sizes and weights:
   - Headings: h4, h5, h6 (Typo.h4, h5, h6)
   - Body: body1, body2
   - Captions for metadata
5. Ensure readable text on 1920x576 display from ~2 feet away
6. Add theme toggle (light/dark) if needed
7. Document theme in style guide

File to update: src/Radio.Web/Components/Layout/MainLayout.razor
Reference: Material 3 color system, dynamic color
```

#### 12.4 Animation & Transition Polish
Add smooth animations for better UX.

**Copilot Prompt:**
```
Add polish animations and transitions:
1. Page transitions (slide in/out, fade)
2. Button press animations (ripple, scale)
3. Loading states (skeleton screens, spinners)
4. Success/error feedback (check mark, shake)
5. List item animations (add/remove from queue)
6. Drawer/dialog enter/exit animations
7. Use easing functions for natural motion
8. Keep animations fast (<300ms for most)
9. Add prefers-reduced-motion support for accessibility

Files to update: 
- src/Radio.Web/wwwroot/css/animations.css (create if needed)
- Various .razor pages
Reference: Material 3 motion guidelines
```

#### 12.5 Accessibility Review
Ensure app is accessible via keyboard and screen readers.

**Copilot Prompt:**
```
Conduct accessibility (a11y) review:
1. Ensure all interactive elements are keyboard accessible
2. Add proper ARIA labels and roles
3. Test tab order is logical
4. Add skip navigation links
5. Ensure focus indicators are visible
6. Add alt text to all images
7. Ensure color isn't the only indicator of state
8. Test with screen reader (if possible)
9. Add keyboard shortcuts for common actions
10. Document keyboard shortcuts in help dialog

Files to update: All .razor pages
Create: src/Radio.Web/Components/Dialogs/KeyboardShortcutsDialog.razor
Reference: WCAG 2.1 AA guidelines
```

### Verification Steps
1. Review DESIGN_AUDIT.md for all identified issues
2. Verify touch targets meet 48px minimum (60px preferred)
3. Check color contrast with online tools
4. Test typography is readable from 2 feet away
5. Navigate with keyboard only (no mouse/touch)
6. Test with screen reader if possible
7. Verify all animations are smooth on Raspberry Pi
8. Test on actual 1920x576 touch display
9. Get user feedback from UAT testers
10. Create punch list for any remaining issues

---

## Testing & Validation

### Manual Testing Checklist
After completing all phases, perform end-to-end testing:

1. **Audio Playback**
   - [ ] All audio sources work (Spotify, FilePlayer, Radio, Vinyl, USB)
   - [ ] Audio quality is good (no distortion, dropouts)
   - [ ] Volume and balance controls work
   - [ ] Transport controls work (play/pause/next/previous)

2. **Metadata & Display**
   - [ ] Now playing metadata shows correct info for all sources
   - [ ] Album art loads (or fallback icon shows)
   - [ ] Global widget updates in real-time
   - [ ] Queue displays correctly with 0, 1, or many items

3. **Queue Functionality**
   - [ ] Multi-select file dialog works
   - [ ] Duplicate prevention works
   - [ ] Auto-advance to next track works
   - [ ] Shuffle and repeat modes work
   - [ ] Drag-and-drop reordering works (if supported)

4. **Visualizations**
   - [ ] VU meter shows rainbow colors
   - [ ] VU meter auto-scales
   - [ ] Waveform has good vertical height
   - [ ] Spectrum analyzer works
   - [ ] Updates/sec metric shows for all modes

5. **Fingerprinting**
   - [ ] Service is running (check logs)
   - [ ] Audio samples are being captured
   - [ ] Identification attempts are logged
   - [ ] Results appear in UI (if found)

6. **User Interface**
   - [ ] All pages load without errors
   - [ ] Touch targets are adequate size
   - [ ] Navigation is smooth and consistent
   - [ ] Animations are smooth (even on Raspberry Pi)
   - [ ] No layout shifts or glitches
   - [ ] Responsive to 1920x576 display

7. **Networking**
   - [ ] File browser can access network shares
   - [ ] Drive selection works
   - [ ] Virtual keyboard works on touch screen
   - [ ] Custom path entry works

8. **Spotify**
   - [ ] Librespot device appears in Spotify app
   - [ ] Can play tracks from Spotify app
   - [ ] Audio quality is good
   - [ ] Metadata appears in Now Playing
   - [ ] Controls work (play/pause/skip)

### Automated Testing
Consider adding:
- Unit tests for critical business logic
- Integration tests for audio pipeline
- E2E tests with Playwright for UI flows
- Performance tests for Raspberry Pi

### Performance Metrics
Monitor these metrics on Raspberry Pi:
- CPU usage (should stay below 50% during normal playback)
- RAM usage (should stay below 1GB)
- Thread count (should be reasonable, <100)
- Visualization FPS/UPS (target >20 UPS)
- Audio latency (should be <100ms)
- UI responsiveness (interactions should feel instant)

---

## Implementation Order Recommendation

Based on dependencies and impact, here's a suggested implementation order:

1. **Phase 3** - Now Playing Metadata (foundational for other features)
2. **Phase 1** - Global Now Playing Widget (high visibility)
3. **Phase 2** - Queue Navbar Visibility (quick win)
4. **Phase 8** - Home Page Controls (critical functionality)
5. **Phase 7** - Visualizer Improvements (good visual impact)
6. **Phase 4** - Fingerprinting Debug (diagnostic foundation)
7. **Phase 5** - RTL-SDR Audio (hardware-specific debug)
8. **Phase 6** - Queue Enhancements (multiple sub-tasks)
9. **Phase 11** - Queue Touch UX (builds on Phase 6)
10. **Phase 9** - File Browser Network Access (standalone feature)
11. **Phase 10** - Spotify Loopback (complex, can be later)
12. **Phase 12** - Material 3 Polish (final refinement)

---

## Notes & Considerations

### Platform Differences
- Windows vs. Linux file path handling
- ALSA vs. PulseAudio for audio routing
- Drive enumeration differs by platform
- Consider Docker for consistent environment

### Raspberry Pi Constraints
- Limited CPU for real-time audio processing
- Keep visualizations optimized
- Test on actual hardware regularly
- Monitor temperature and throttling

### Security
- Sanitize file paths to prevent directory traversal
- Validate network paths before accessing
- Store Spotify credentials securely
- Use HTTPS for any remote API calls

### Configuration
- All user preferences should use ConfigurationApiService
- Settings should persist across restarts
- Provide sensible defaults
- Allow reset to defaults option

---

## Completion Criteria

This plan is complete when:
1. All 12 issues listed in the problem statement are addressed
2. Manual testing checklist is 100% passed
3. Application runs smoothly on Raspberry Pi 5
4. No critical bugs or errors in logs
5. UI follows Material 3 guidelines consistently
6. Touch screen interaction is smooth and intuitive
7. Audio quality is excellent across all sources
8. User documentation is updated

---

## References

- `/design/WEBUI.md` - Web UI design specifications
- `/design/AUDIO.md` - Audio system architecture
- `/design/CONFIGURATION.md` - Configuration infrastructure
- `/SpotifyLoopback/` - Spotify integration examples
- `archive/PROJECTPLAN.md` - Project overview
- Material 3 Guidelines: https://m3.material.io/
- SoundFlow Documentation: https://lsxprime.github.io/soundflow-docs/

---

**Document Version:** 1.0  
**Created:** 2026-01-04  
**Last Updated:** 2026-01-04  
**Status:** Ready for Implementation
