# Radio UI Planning Guide

A **Material 3-compliant**, touchscreen interface for an embedded music controller with a wide-format display (12.5" × 3.75") that manages multiple audio sources, visualizations, and system configuration through a .NET Core REST API.

**Experience Qualities**:
1. **Professional** - Industrial-grade interface with retro-LED aesthetics that communicates reliability and precision
2. **Efficient** - Icon-driven navigation optimized for touch interactions with minimal text, enabling quick access to all features  
3. **Contextual** - Adaptive interface that transforms based on selected audio input, showing only relevant controls
4. **Touch-Optimized** - All controls meet Material 3 touch target minimums (48px minimum, 60px preferred for primary actions)

**Complexity Level**: Complex Application (advanced functionality, accounts)
  - Multiple integrated subsystems (audio management, system configuration, visualization, playlist management)
  - Real-time data synchronization with backend API via REST and SignalR
  - Context-sensitive UI that adapts to input device selection
  - Comprehensive configuration management with persistence
  - Material 3 design language with touch-friendly components

## Essential Features

### Main Navigation Bar
- **Functionality**: Persistent top bar with date/time, system stats (CPU, RAM, threads), and navigation icons
- **Purpose**: Provides constant awareness of system state and quick access to main sections
- **Trigger**: Always visible, icons clickable to navigate between views
- **Progression**: Click icon → Component slides in/replaces current view → Breadcrumb updates
- **Success criteria**: All system stats update in real-time, navigation feels instantaneous

### Global Music Controls (Material 3, Touch-Optimized)
- **Functionality**: Central audio control with Material 3-compliant, touch-friendly transport controls
- **Controls** (conditional based on source capabilities):
  - **Shuffle On/Off**: Only visible when source `SupportsShuffle` is true
  - **Previous**: Only enabled when source `SupportsPrevious` is true
  - **Play/Pause**: Always available
  - **Next**: Only enabled when source `SupportsNext` is true
  - **Repeat**: Only visible when source `SupportsRepeat` is true (modes: Off, One, All)
  - **Song Duration/Position**: Displayed when source provides `Duration` (with seekable progress bar if source `IsSeekable` is true)
- **Touch Targets**: Minimum 48px, preferred 60px for primary actions (play/pause)
- **Purpose**: Primary transport controls that adapt to current audio source capabilities
- **Success criteria**: Controls are conditionally shown based on source, all interactions feel responsive, settings persist between sessions

### Input/Output Configuration
- **Functionality**: Context-sensitive configuration dialogs based on selected audio source
- **Purpose**: Enables device-specific setup (USB port selection, hidden device management)
- **Trigger**: Click configuration icon next to input/output dropdowns
- **Progression**: Click config icon → Detect input/output type → Show appropriate dialog → User configures → Save to API → Close dialog
- **Success criteria**: Each input type shows correct configuration options, settings persist, validation prevents invalid entries

### Now Playing Display
- **Functionality**: Large, easy-to-read display of current track information (read-only, no touch interactions)
- **Layout**: Larger than typical "now playing" displays to accommodate 12.5" × 3.75" form factor and touch screen viewing distances
- **Content**:
  - **Album Art**: Large display, or generic music icon when no track
  - **Title**: Song title, or "No Track" when empty
  - **Artist**: Artist name, or "--" (dashes) when empty
  - **Album**: Album name, or "--" when empty
  - **Additional Info**: May include genre, year, or source-specific information
- **No Track State**: Shows generic music icon with dashes for artist and song (never blank)
- **Purpose**: Prominent visual feedback of what's currently playing
- **Trigger**: Automatically updates when track changes via API/SignalR
- **Success criteria**: Always displays valid content (never null/blank), album art loads quickly, updates smoothly, readable from typical viewing distance

### Playlist Queue (For Queue-Supporting Sources)
- **Functionality**: Scrollable grid showing queued tracks (visible only for sources with `SupportsQueue` = true, currently Spotify and Audio File Player)
- **Columns**:
  - **Title**: Song title (may be filename for file player)
  - **Artist**: Artist name
  - **Album**: Album name (may be empty if metadata unavailable)
  - **Duration**: Track length (may be estimated for some sources)
  - **Note**: "Date Added" column from examples is NOT needed
- **Interactions**:
  - Click/tap track to jump to that position in queue
  - Drag to reorder (if source supports reordering)
  - Visual indicator for currently playing track
- **Purpose**: Visual queue management and track selection
- **Trigger**: Accessible when queue-supporting source is active, auto-updates as playlist changes
- **Success criteria**: Smooth scrolling, responsive selection, real-time updates via SignalR, handles missing metadata gracefully

### Spotify Music Selection
- **Functionality**: Search and browse interface for Spotify content when Spotify is the active source
- **Layout**:
  - **Search Bar**: Text input that brings up on-screen keyboard for query entry
  - **Browse Button**: Icon button next to search bar to access Spotify's browse categories/playlists
  - **Filter Pills**: Toggleable pill buttons for search filters (Material 3 chip design):
    * All (default)
    * Music (tracks)
    * Playlists
    * Podcasts
    * Albums
    * Artists
    * Audiobooks
- **Search Flow**:
  1. User taps search bar → On-screen keyboard appears
  2. User types query and submits
  3. Results displayed filtered by selected pills
  4. Tap result to play immediately or add to queue
- **Browse Flow**:
  1. User taps Browse icon
  2. Categories displayed in grid/list
  3. Tap category to see playlists
  4. Tap playlist to view tracks
  5. Tap track to play or add to queue
- **Purpose**: Enable discovery and selection of Spotify content
- **Success criteria**: Search is fast, filter pills work, browse hierarchy navigable, selected content plays correctly

### Radio Display (LED Aesthetic, Read-Only)
- **Functionality**: Large LED-style display showing current radio state (visible when Radio is active source)
- **Display Components**:
  - **Frequency**: Large LED display using DSEG14Classic-Bold font, 48px, orange or legacy green color
  - **Band**: AM/FM indicator using DSEG14Classic-Regular font
  - **Signal Strength**: Visual meter (bars or percentage)
  - **Stereo Indicator**: Illuminated "STEREO" text when FM stereo signal detected
  - **Sub-Band Step**: Current frequency step size (e.g., "0.1 MHz" or "0.2 MHz")
  - **EQ Mode**: Current equalizer setting (Off, Rock, Pop, Jazz, Classical, Speech)
- **Font Selection**:
  - Frequency: DSEG14Classic-Bold (primary LED reading)
  - Band/Indicators: DSEG14Classic-Regular (secondary LED readings)
  - Other text: Consistent modern sans-serif matching global UI font
- **Color**: Orange (primary option) or legacy green (alternative), applied to LED segments
- **Purpose**: Authentic radio receiver display with retro LED aesthetic
- **Trigger**: Automatically updates when radio state changes via SignalR
- **Success criteria**: LED fonts render correctly, color theme consistent, all state components update in real-time, readable from viewing distance

### Radio Controls (Touch-Friendly, Source-Specific)
- **Functionality**: Touch-optimized controls for radio when Radio is the active source
- **Button Layout**:
  - **Frequency Controls**:
    * Down Arrow button (◄): Step frequency down, long-press to scan down
    * **Set button**: Opens numeric keypad dialog for direct frequency entry
    * Up Arrow button (►): Step frequency up, long-press to scan up
  - **Sub Band button**: Cycles through frequency step sizes (FM: 0.1/0.2 MHz, AM: 9/10 kHz)
  - **EQ button**: Cycles through equalizer modes (device-specific, not global EQ)
  - **Volume Up/Down buttons**: Adjust radio device volume (separate from master volume)
- **Long-Press Behavior**:
  - Hold Up/Down arrow: Initiates scan in that direction
  - Scan continues until strong signal found or user releases/taps
  - Visual feedback during scan (frequency updates, scanning indicator)
- **Keypad Dialog** (triggered by Set button):
  - Large touch-friendly numeric keypad
  - Displays current frequency, allows direct entry
  - Validates frequency range based on current band (FM: 87.5-108 MHz, AM: 520-1710 kHz)
  - Cancel/Confirm buttons
- **Note on Volume**: Radio device volume is separate from master/mixer volume - adjusts hardware volume on RF320 device
- **Note on EQ**: EQ setting is device-specific (RF320 hardware EQ), not the global audio system EQ
- **Purpose**: Full control of radio receiver hardware functions
- **Success criteria**: All buttons responsive with appropriate touch feedback, long-press scan works reliably, keypad validation prevents invalid frequencies, device volume and EQ changes apply to hardware

### System Configuration Manager
- **Functionality**: Advanced settings with configuration/preferences/secrets grid, backup/restore, shutdown, view system status (memory / cpu / processes / disk space, network info)
- **Purpose**: Complete system administration and configuration management
- **Trigger**: Click system config icon in main nav
- **Progression**: Open dialog → Select component → Load config grid → Edit values → Add/delete rows → Save → Persist to API
- **Success criteria**: All config editable, backup/restore works, shutdown initiates gracefully

### Debug Management
- **Functionality**: CRUD interface for TTS and audio file prompts, View recent LOGs from API or Web
- **Purpose**: Manage system audio notifications and voice prompts
- **Trigger**: Navigate to Prompt Management section
- **Progression**: View list → Click add/edit → Enter details → Save → API persists
- **Success criteria**: Prompts play correctly, TTS vs File types handled appropriately

### Audio Visualization
- **Functionality**: Real-time visual representation of audio (VU meter, waveform, spectrum analyzer)
- **Purpose**: Visual feedback for audio levels and frequency content
- **Trigger**: Click visualization icon in nav
- **Progression**: Select visualization type → Canvas renders → Updates in real-time with audio
- **Success criteria**: Smooth 60fps rendering, accurate audio representation

### Reusable Touch Dialogs
- **Functionality**: Multi-select list, numeric keypad (with range validation), keyboard dialog
- **Purpose**: Touch-optimized input for various configuration needs
- **Trigger**: Various configuration actions throughout app
- **Progression**: Trigger action → Dialog slides up → User interacts → Confirm/Cancel → Dialog dismisses → Value returned
- **Success criteria**: Large touch targets, responsive feedback, validation works

## Edge Case Handling

- **No API Connection**: Display error toast, show last cached config, attempt reconnection
- **Missing Configuration Keys**: Use sensible defaults, log missing keys for API team
- **Invalid USB Devices**: Filter out non-audio devices, show empty state if none found
- **Long Song/Artist Names**: Truncate with ellipsis, show full text on hover/long-press
- **Zero Playlist Items**: Show empty state with icon and "No tracks in queue" message
- **Rapid Setting Changes**: Debounce API calls to prevent overwhelming backend
- **Spotify Auth Failure**: Show error in config dialog with clear next steps
- **Hidden All Outputs**: Prevent hiding GoogleCast and DefaultDeviceAudio as specified
- **Malformed Config Data**: Graceful fallback, log error, show user-friendly message

## Design Direction

The design should evoke an industrial audio engineering workstation with retro-digital aesthetics—think professional recording studio meets vintage hi-fi equipment. The wide, short form factor demands horizontal information architecture. Use bold LED-style typography for key readings (time, frequency, levels) to create nostalgic charm while maintaining modern touch interaction standards. Minimal text, maximum iconography. The interface should feel precise, responsive, and purpose-built.

## Color Selection

**Triadic** - Professional studio aesthetic with retro-digital character using cyan, amber, and crimson accents against dark surfaces.

- **Primary Color**: Deep charcoal `oklch(0.2 0.01 240)` - Professional equipment background that reduces eye strain
- **Secondary Colors**: 
  - Dark slate `oklch(0.25 0.02 240)` for cards and elevated surfaces
  - Medium gray `oklch(0.35 0.01 240)` for secondary controls
- **Accent Color**: Electric cyan `oklch(0.75 0.15 195)` for active states, focus indicators, and primary actions (play, save) - evokes digital precision
- **Foreground/Background Pairings**:
  - Background (Deep Charcoal `oklch(0.2 0.01 240)`): Cyan accent text `oklch(0.75 0.15 195)` - Ratio 6.2:1 ✓
  - Background (Deep Charcoal `oklch(0.2 0.01 240)`): White primary text `oklch(0.95 0 0)` - Ratio 11.8:1 ✓
  - Card (Dark Slate `oklch(0.25 0.02 240)`): White text `oklch(0.95 0 0)` - Ratio 9.8:1 ✓
  - Primary (Electric Cyan `oklch(0.75 0.15 195)`): Black text `oklch(0.15 0 0)` - Ratio 10.2:1 ✓
  - Secondary (Medium Gray `oklch(0.35 0.01 240)`): White text `oklch(0.95 0 0)` - Ratio 6.5:1 ✓
  - Accent (Amber LED `oklch(0.8 0.18 75)`): Black text `oklch(0.15 0 0)` - Ratio 11.5:1 ✓
  - Muted (Dark Gray `oklch(0.3 0.01 240)`): Light Gray text `oklch(0.7 0.02 240)` - Ratio 4.8:1 ✓

**LED Display Colors**:
- Time/Date: Amber `oklch(0.8 0.18 75)` - Classic LED watch aesthetic
- System Stats: Cyan `oklch(0.7 0.15 195)` - Differentiates from primary time display
- **Radio Frequency**: Bright amber `oklch(0.85 0.2 75)` OR legacy green `oklch(0.75 0.18 140)` - **High visibility for primary reading, configurable**
- **Radio Indicators**: Same color as frequency (orange or green theme consistency)

## Font Selection

Fonts balance retro-digital LED aesthetics for numeric displays with clean modern sans-serif for UI labels and metadata. DSEG14Classic (seven-segment LED display font) is used for radio frequency, time, and system stats to create authentic embedded device character. Inter or Roboto for general UI text ensures legibility at small sizes on the touchscreen.

- **Typographic Hierarchy**:
  - H1 (Date/Time Display): DSEG14Classic-Bold/32px/tight tracking - dominant retro-digital presence
  - H2 (System Stats): DSEG14Classic-Regular/18px/tight tracking - secondary LED readings
  - **H3 (Radio Frequency)**: DSEG14Classic-Bold/48px/tight tracking - **primary reading on radio view (SPECIFIED)**
  - **Radio Band Display**: DSEG14Classic-Regular/24px/tight tracking - **band indicator (SPECIFIED)**
  - **Radio Indicators**: Inter Medium/16px - **for stereo, signal strength, EQ, other radio state text**
  - H4 (Song Titles): Inter SemiBold/20px/normal tracking - metadata hierarchy
  - Body (Artists, Labels): Inter Regular/16px/normal tracking - supporting information
  - Caption (Timestamps): Inter Regular/14px/normal tracking - tertiary information
  - Button Text: Inter Medium/16px/slight tracking - minimal text on controls

**LED Font Files Available**: Located in `/assets/fonts/DSEG14Classic-*`:
  - DSEG14Classic-Bold (ttf, woff, woff2) - Use for frequency display
  - DSEG14Classic-Regular (ttf, woff, woff2) - Use for band display
  - DSEG14Classic-Light (ttf, woff, woff2) - Optional for less prominent LED readings
  - Each available in normal and italic variants

## Animations

Animations should feel mechanical and precise—think professional audio equipment with satisfying physical feedback. Button presses should have subtle compression effects. View transitions should slide horizontally (matching the wide format). VU meters and spectrum analyzers should move fluidly without lag. All animations should be snappy (150-250ms) to maintain the responsive feel critical for a professional tool.

- **Purposeful Meaning**: Mechanical precision with tactile feedback—every interaction should feel like operating physical hardware with definitive clicks and smooth analog movements
- **Hierarchy of Movement**: Primary: transport controls (play/pause immediate response), Secondary: view transitions (smooth horizontal slides), Tertiary: real-time visualizations (fluid 60fps)

## Component Selection

**Material 3 Components** - All components follow Material 3 design guidelines with touch-optimized sizing:

- **Components**: 
  - **Button** (primary controls - play, save, etc.) with custom pressed states, minimum 48px height
  - **IconButton** (navigation, radio controls) with 48px touch target, optional badges
  - **Chip** (Spotify filter pills) - Filter chips with selection state, minimum 32px height, touch-friendly spacing
  - **FAB (Floating Action Button)** for primary actions like "Add to Queue" when applicable
  - **Select/Dropdown** (input/output dropdowns) with large touch-friendly options, minimum 48px per option
  - **Slider** (volume, balance) with custom thumb design (minimum 44px) for precise control
  - **Dialog** (all configuration modals) with slide-up animation, Material 3 elevated styling
  - **Card** (now playing, playlist items, browse results) with elevated appearance per Material 3 spec
  - **List/ListItem** (queue, browse results) with minimum 56px height for one-line, 72px for two-line
  - **Scroll Area/ScrollBar** (playlist grid, config grid) with custom scrollbar, touch drag support
  - **Divider/Separator** (section dividers) in subtle accent color
  - **LinearProgress/CircularProgress** (playback position, loading states) with custom styling
  - **Badge** (status indicators like band, signal strength, notification counts)
  - **TextField/Input** (configuration values, text entry) with Material 3 filled or outlined style
  - **Checkbox** (multi-select in hide/unhide dialog) minimum 40px touch target
  - **Radio Button** (exclusive selections) minimum 40px touch target
  - **Switch** (toggle settings) minimum 52px wide touch target
  - **DataTable/Grid** (configuration grid in system config) with sortable columns, touch-scroll
  - **Tabs/TabBar** (switching between visualization modes) with indicator, minimum 48px touch target
  - **BottomSheet** for contextual actions and settings
  - **Snackbar/Toast** for notifications and confirmations
  
- **Customizations**:
  - **Radio Frequency Keypad**: Custom numeric keypad dialog with large touch targets (min 60px per key)
    * Decimal point key for FM frequencies (e.g., 101.5)
    * Validates frequency ranges: FM 87.5-108 MHz, AM 520-1710 kHz
    * Shows current frequency, clears on first key press
    * Cancel/Confirm buttons
  - **On-Screen Keyboard**: Custom QWERTY keyboard optimized for landscape 12.5" × 3.75" layout
    * Used for Spotify search, configuration values
    * Adaptive to input type (numeric, email, text)
  - **Multi-Select Dialog**: Custom list with large checkboxes (40px touch targets) and readable rows
    * For device hiding, filter selection
  - **LED-Style Text Component**: Wraps DSEG14Classic font family with optional glow effect
    * Used for frequency, time, band displays
    * Configurable color (orange/green for radio)
  - **Canvas Visualizations**: Real-time audio visualization components
    * VU meter (analog-style with LED segments)
    * Waveform analyzer (oscilloscope style)
    * Spectrum analyzer (frequency bars)
    * All target 60fps updates
  - **Smart Dropdown**: Filters hidden devices automatically, shows only relevant options for context
  
- **States**:
  - All buttons: default (no border), hover (glow effect), active (pressed/darkened), disabled (reduced opacity)
  - Sliders: dragging state with value tooltip
  - Inputs: focused (cyan border glow), error (red border with shake), success (green flash)
  - Cards: default (elevated), selected (cyan border), active (highlighted)
  
- **Icon Selection**:
  - Play/Pause/Skip: Standard media control icons (Play, Pause, SkipBack, SkipForward)
  - Shuffle: Shuffle icon
  - Volume: SpeakerHigh/SpeakerLow
  - Config/Settings: Gear or Wrench
  - Playlist: MusicNotes or ListMusic
  - Visualization: Waveform or ChartBar
  - System: Desktop or Cpu
  - Save: FloppyDisk
  - Add: Plus
  - Delete: Trash
  - Backup: DownloadSimple
  - Restore: UploadSimple
  - Power: Power
  - Radio: Radio
  - Vinyl: Disc
  - USB: UsbPlug
  - Spotify: MusicNote with custom styling
  - Tune: ArrowUp/ArrowDown
  - Scan: MagnifyingGlass with arrows
  
- **Spacing**:
  - Main nav bar: py-3 px-4 with gap-6 between sections
  - Control groups: gap-3 for related controls, gap-6 between groups
  - Cards: p-4 for content, gap-4 between elements
  - Dialogs: p-6 with gap-4 for form fields
  - Touch targets: Minimum 48px height, 60px preferred for primary actions
  - Grid items: gap-2 for dense lists, gap-4 for cards
  
- **Mobile**: *NOT A DESIGN CONCERN*
  - Layout already optimized for fixed landscape display (12.5" × 3.75")
  - All touch targets 48px minimum (60px preferred)
  - Horizontal scrolling for playlist/config grids
  - Dialogs use full-width on this small display
  - Font sizes larger than typical mobile (primary content 16-20px)
  - No responsive breakpoints needed - fixed dimensions

---

## UI Implementation Plan

This section provides a phased development plan for implementing the Blazor Web UI. Each phase includes detailed Copilot prompts that can be used by coding agents to build the UI components.

**Prerequisites:**
- Complete all phases in `/RTest/UIPREPARATION.md` first (API endpoints and core functionality must be ready)
- Blazor Server project exists at `/RTest/src/Radio.Web`
- Material 3 component library installed (e.g., MudBlazor or custom Material 3 implementation)

**Technology Stack:**
- **Framework**: Blazor Server (.NET 8+)
- **UI Library**: Material 3 components (MudBlazor recommended for Material 3 support)
- **Real-time**: SignalR (built into Blazor Server)
- **HTTP Client**: Typed HttpClient for API communication
- **State Management**: Blazor component state + SignalR for real-time updates

**Estimated Total Effort:** 20-28 development days (after backend phases complete)

---

### Phase 1: Project Setup and Infrastructure (2-3 days)

#### Task 1.1: Install Material 3 Component Library and Configure Project

**Prompt for Copilot Agent:**
```
Set up the Blazor Server project with Material 3 components and configure for 12.5" × 3.75" fixed landscape display.

Project location: /RTest/src/Radio.Web

Requirements:

1. Install MudBlazor package (latest version):
   - Add PackageReference to Radio.Web.csproj
   - Version: 7.x or later (based on Material Design 2; Material 3-inspired aesthetics require custom theming)
     - Note: MudBlazor 7.x does not provide full Material 3 compliance out-of-the-box. Custom theming is required to approximate Material 3 design language.

2. Configure MudBlazor in Program.cs:
   - Add MudBlazor services
   - Configure MudBlazor with custom theme for dark mode
   - Set up SignalR hub connection to /hubs/audio

3. Update _Imports.razor:
   - Add @using MudBlazor
   - Add @using Radio.Web.Services
   - Add @using Radio.Web.Models
   - Add @using Radio.API.Models

4. Create custom Material 3 theme in wwwroot/css/custom-theme.css:
   - Primary color: Electric cyan oklch(0.75 0.15 195)
   - Secondary: Dark slate oklch(0.25 0.02 240)
   - Background: Deep charcoal oklch(0.2 0.01 240)
   - LED colors: Amber oklch(0.8 0.18 75) and Cyan oklch(0.7 0.15 195)
   - Touch target sizes: 48px minimum, 60px preferred

5. Add DSEG14Classic LED fonts to wwwroot/fonts/:
   - Copy from /RTest/assets/fonts/DSEG14Classic-*
   - Create @font-face declarations in custom-theme.css

6. Configure fixed viewport for 12.5" × 3.75" (1920×576 pixels typical):
   - Set viewport meta tag
   - Disable zoom and scaling
   - Configure for landscape orientation only

7. Update MainLayout.razor:
   - Use MudThemeProvider and MudDialogProvider
   - Set dark mode by default
   - Configure for fixed dimensions (no responsive breakpoints)

Success Criteria:
- MudBlazor components render correctly
- Dark theme applied with custom colors
- LED fonts load and display properly
- Layout fixed to landscape dimensions
- No console errors
- Build succeeds
```

#### Task 1.2: Create API Client Services

**Prompt for Copilot Agent:**
```
Create typed HttpClient services for communicating with the REST API.

Location: /RTest/src/Radio.Web/Services/

Requirements:

1. Create AudioApiService.cs:
   - Constructor with HttpClient and IConfiguration
   - Base URL from appsettings.json
   - Methods for all /api/audio endpoints:
     * GetPlaybackStateAsync()
     * UpdatePlaybackStateAsync(UpdatePlaybackRequest)
     * PlayAsync(), PauseAsync(), StopAsync()
     * NextAsync(), PreviousAsync()
     * SetShuffleAsync(bool enabled)
     * SetRepeatModeAsync(RepeatMode mode)
     * SetVolumeAsync(float volume)
     * SetBalanceAsync(float balance)
     * GetNowPlayingAsync()

2. Create QueueApiService.cs:
   - Methods for /api/queue endpoints:
     * GetQueueAsync()
     * AddToQueueAsync(string trackId, int? position)
     * RemoveFromQueueAsync(int index)
     * MoveQueueItemAsync(int from, int to)
     * JumpToIndexAsync(int index)
     * ClearQueueAsync()

3. Create SourcesApiService.cs:
   - Methods for /api/sources endpoints:
     * GetSourcesAsync()
     * GetActiveSourcesAsync()
     * GetPrimarySourceAsync()
     * SelectSourceAsync(string sourceType, Dictionary<string, string> config)

4. Create SpotifyApiService.cs:
   - Methods for /api/spotify endpoints:
     * SearchAsync(string query, List<string> types)
     * GetBrowseCategoriesAsync()
     * GetCategoryPlaylistsAsync(string categoryId)
     * GetUserPlaylistsAsync()
     * PlayUriAsync(string uri, string contextUri)
     * GetAuthUrlAsync()
     * GetAuthStatusAsync()

5. Create RadioApiService.cs:
   - Methods for /api/radio endpoints:
     * GetRadioStateAsync()
     * SetFrequencyAsync(double frequency)
     * FrequencyUpAsync(), FrequencyDownAsync()
     * StartScanAsync(string direction)
     * StopScanAsync()
     * SetBandAsync(string band)
     * SetStepAsync(double step)
     * SetEqualizerAsync(string mode)
     * SetDeviceVolumeAsync(int volume)

6. Register all services in Program.cs:
   - AddHttpClient for each service
   - Configure base address from appsettings
   - Add retry policies using Polly (optional but recommended)

7. Create DTOs in /RTest/src/Radio.Web/Models/:
   - Mirror API DTOs from Radio.API.Models
   - Or reference Radio.API project directly

Success Criteria:
- All API services compile
- Services registered in DI container
- Can make API calls successfully
- Error handling implemented
- Async/await used throughout
```

#### Task 1.3: Create SignalR Hub Connection Service

**Prompt for Copilot Agent:**
```
Create SignalR service for real-time audio state updates.

Location: /RTest/src/Radio.Web/Services/AudioStateHubService.cs

Requirements:

1. Create AudioStateHubService:
   - IAsyncDisposable implementation
   - HubConnection to /hubs/audio
   - Automatic reconnection logic
   - Event handlers for all hub messages

2. Hub events to handle:
   - PlaybackStateChanged(PlaybackStateDto)
   - NowPlayingChanged(NowPlayingDto)
   - QueueChanged(List<QueueItemDto>)
   - RadioStateChanged(RadioStateDto)
   - VolumeChanged(VolumeDto)

3. Public events for components to subscribe:
   - event EventHandler<PlaybackStateDto>? OnPlaybackStateChanged
   - event EventHandler<NowPlayingDto>? OnNowPlayingChanged
   - event EventHandler<List<QueueItemDto>>? OnQueueChanged
   - event EventHandler<RadioStateDto>? OnRadioStateChanged
   - event EventHandler<VolumeDto>? OnVolumeChanged

4. Methods for selective subscriptions:
   - Task StartAsync()
   - Task StopAsync()
   - Task SubscribeToQueueAsync()
   - Task UnsubscribeFromQueueAsync()
   - Task SubscribeToRadioStateAsync()
   - Task UnsubscribeFromRadioStateAsync()

5. Connection state management:
   - Track connection state (Connected, Reconnecting, Disconnected)
   - Expose ConnectionState property
   - Fire events on state changes
   - Automatic reconnection with exponential backoff

6. Register as singleton in Program.cs

Success Criteria:
- Connects to SignalR hub successfully
- Receives real-time updates
- Reconnects automatically on disconnect
- Components can subscribe to events
- No memory leaks (proper disposal)
```

---

### Phase 2: Core Layout and Navigation (3-4 days)

#### Task 2.1: Create Main Layout with Navigation Bar

**Prompt for Copilot Agent:**
```
Create the main layout with persistent navigation bar for 12.5" × 3.75" landscape display.

Location: /RTest/src/Radio.Web/Components/Layout/MainLayout.razor

Requirements:

1. Update MainLayout.razor:
   - Fixed height 576px (3.75")
   - Fixed width 1920px (12.5")
   - Dark background: oklch(0.2 0.01 240)
   - Horizontal layout (row)

2. Top navigation bar (always visible):
   - Height: 60px
   - Contains:
     * Date/Time display (left) - DSEG14Classic-Bold, amber LED color
     * System stats (CPU, RAM, Threads) - DSEG14Classic-Regular, cyan LED color
     * Navigation icons (right) - 48px touch targets
   - Update every second via JavaScript interop

3. Navigation icons:
   - Home / Now Playing
   - Queue (only visible when SupportsQueue)
   - Spotify Search (only when Spotify active)
   - Radio Controls (only when Radio active)
   - Visualizer
   - System Config
   - Spacing: gap-6 between icons

4. Main content area:
   - Below nav bar
   - Height: 516px (remaining space)
   - Horizontal slide transitions when navigating
   - RenderFragment for @Body

5. Create NavigationService.cs in Services/:
   - Track current page
   - Fire events on navigation
   - Expose CurrentPage property

6. Create SystemStatsService.cs:
   - Use System.Diagnostics.Process for CPU/RAM
   - Update every second
   - Thread count from ThreadPool
   - Expose as observable properties

Success Criteria:
- Navigation bar always visible
- Date/time updates every second
- System stats update in real-time
- Navigation icons clickable
- LED fonts render correctly
- Touch targets 48px minimum
- Layout fits 1920×576 exactly
```

#### Task 2.2: Create Home/Now Playing Page

**Prompt for Copilot Agent:**
```
Create the main home page with Now Playing display and global music controls.

Location: /RTest/src/Radio.Web/Components/Pages/Home.razor

Requirements:

1. Layout:
   - Two-column layout: Now Playing (left 60%) | Controls (right 40%)
   - Or single column with Now Playing above controls for better fit

2. Now Playing Display (read-only):
   - Large album art or generic music icon (400×400px)
   - Title: Inter SemiBold 32px
   - Artist: Inter Regular 24px
   - Album: Inter Regular 20px
   - When empty: generic icon, Title="No Track", Artist="--", Album="--"
   - Source indicator (Spotify/Radio/Vinyl/Files)

3. Global Music Controls:
   - Arranged horizontally: Shuffle | Previous | Play/Pause | Next | Repeat
   - Each button: 60px × 60px (preferred touch target)
   - Icons from Material Icons
   - Conditional visibility based on source capabilities:
     * Shuffle: visible if source.CanShuffle
     * Previous: enabled if source.CanPrevious
     * Next: enabled if source.CanNext
     * Repeat: visible if source.CanRepeat
   - Play/Pause toggles based on isPlaying state

4. Progress bar (when Duration available):
   - Shows Position / Duration
   - Seekable if source.CanSeek (click to seek)
   - Updates in real-time via SignalR
   - Format: "2:34 / 4:15"

5. Volume and Balance controls:
   - Volume slider: 0-100, vertical or horizontal
   - Balance slider: -100 (L) to 100 (R)
   - Mute button
   - Use MudSlider components

6. Data binding:
   - Inject AudioApiService and AudioStateHubService
   - Subscribe to OnPlaybackStateChanged and OnNowPlayingChanged
   - Call API methods on button clicks
   - Update UI on SignalR events

7. State management:
   - Track current playback state
   - Track source capabilities
   - Handle loading states
   - Handle errors gracefully

Success Criteria:
- Now Playing displays current track or empty state
- Controls conditional based on source
- Play/Pause/Next/Previous work
- Shuffle/Repeat toggles work
- Volume/Balance sliders work
- Progress bar updates in real-time
- Seek works when supported
- UI updates via SignalR without page refresh
```

---

### Phase 3: Queue Management UI (2-3 days)

#### Task 3.1: Create Queue Display Component

**Prompt for Copilot Agent:**
```
Create the music queue display with reordering support.

Location: /RTest/src/Radio.Web/Components/Pages/QueuePage.razor

Requirements:

1. Layout:
   - Full width/height of main content area
   - Header: "Queue" title + count + Clear All button
   - Scrollable list of queue items

2. Queue item display:
   - MudDataGrid or custom list
   - Columns: Title (flexible) | Artist (250px) | Album (200px) | Duration (80px) | Actions (80px)
   - Current playing item highlighted (cyan accent border)
   - Row height: 72px (two-line Material 3)
   - Touch target for entire row: jump to that item

3. Queue operations:
   - Click row to jump to that track
   - Drag handle on left for reordering (if source supports)
   - Delete button (X) on right to remove
   - Clear All button in header

4. Data binding:
   - Inject QueueApiService and AudioStateHubService
   - Load queue on page load
   - Subscribe to OnQueueChanged for real-time updates
   - Call API methods for operations

5. Drag-and-drop reordering:
   - Use MudDropZone or HTML5 drag API
   - Visual feedback during drag
   - Call MoveQueueItemAsync on drop
   - Only enable if source.CanReorderQueue

6. Empty state:
   - Show when queue is empty
   - Generic icon + "No tracks in queue" message
   - Maybe "Add from [Source]" button

7. Conditional visibility:
   - Only show Queue page when source.SupportsQueue
   - Hide navigation icon when not applicable

Success Criteria:
- Queue displays all items
- Click item to jump/play
- Drag to reorder works (when supported)
- Delete removes item
- Clear All empties queue
- Real-time updates via SignalR
- Smooth scrolling for long queues
- Current item visually highlighted
```

---

### Phase 4: Spotify Integration UI (3-4 days)

#### Task 4.1: Create Spotify Search and Browse UI

**Prompt for Copilot Agent:**
```
Create Spotify search interface with filter pills and browse functionality.

Location: /RTest/src/Radio.Web/Components/Pages/SpotifyPage.razor

Requirements:

1. Layout:
   - Header section with search bar and browse button
   - Filter pills row below header
   - Results area (scrollable)

2. Search bar:
   - MudTextField with search icon
   - Placeholder: "Search Spotify..."
   - Opens on-screen keyboard on focus (native browser behavior)
   - Search button or Enter to submit

3. Browse button:
   - Icon button next to search bar
   - Opens browse categories dialog
   - 48px touch target

4. Filter pills (MudChip):
   - Toggleable chips: All, Music, Playlists, Podcasts, Albums, Artists, Audiobooks
   - "All" selected by default
   - Multiple selection allowed
   - Minimum 32px height, touch-friendly spacing
   - Active state: cyan accent border

5. Search results display:
   - Grouped by type: Tracks, Albums, Playlists, Artists
   - Each result as MudCard:
     * Image/icon (100×100)
     * Title (bold)
     * Subtitle (artist/owner)
     * Play button overlay on hover/tap
   - Grid layout: 4-5 items per row
   - Touch target for entire card

6. Browse dialog:
   - MudDialog with categories list
   - Click category to see playlists
   - Click playlist to see tracks
   - Breadcrumb navigation
   - Play button for playlists

7. Play actions:
   - Click track: play immediately
   - Click album/playlist: play first track, load rest to queue
   - Add to Queue button (secondary action)

8. Data binding:
   - Inject SpotifyApiService
   - Search on submit
   - Filter results based on selected pills
   - Load browse categories on dialog open

9. Conditional visibility:
   - Only show Spotify page when Spotify is active source
   - Show auth prompt if not authenticated

Success Criteria:
- Search bar functional
- Filter pills toggle
- Results display correctly
- Browse categories accessible
- Play actions work (starts playback)
- Add to queue works
- Images load
- Touch-friendly interactions
- Graceful error handling
```

---

### Phase 5: Radio Controls UI (3-4 days)

#### Task 5.1: Create Radio Display Component

**Prompt for Copilot Agent:**
```
Create LED-style radio display showing frequency, band, and status.

Location: /RTest/src/Radio.Web/Components/Shared/RadioDisplay.razor

Requirements:

1. Display layout:
   - Large frequency display: DSEG14Classic-Bold, 72px
   - Band indicator (AM/FM): DSEG14Classic-Regular, 36px
   - Signal strength meter: Visual bars (5 bars)
   - Stereo indicator: "STEREO" text when applicable
   - Sub-band step: "0.1 MHz" or "0.2 MHz"
   - EQ mode: "Rock", "Pop", etc.

2. LED color theme:
   - Orange: oklch(0.85 0.2 75) - primary option
   - OR Legacy green: oklch(0.75 0.18 140) - alternative
   - Toggle via config or user preference
   - Glow effect for LED segments

3. Frequency display:
   - Format: "101.5" for FM, "1010" for AM
   - Decimal aligned
   - Leading zeros for AM (4 digits)

4. Signal strength:
   - 5 bars, fill based on strength (0-100%)
   - Animated when scanning
   - Color: same as LED theme

5. Stereo indicator:
   - Only for FM
   - Illuminated when stereo signal detected
   - LED color when active, dim when not

6. Data binding:
   - Inject RadioApiService and AudioStateHubService
   - Subscribe to OnRadioStateChanged
   - Update display in real-time

7. Conditional visibility:
   - Only show when Radio is active source

Success Criteria:
- LED fonts render correctly
- Frequency displays with proper format
- Signal strength updates in real-time
- Stereo indicator works
- Color theme toggleable (orange/green)
- Authentic LED look with glow
- Updates via SignalR
```

#### Task 5.2: Create Radio Control Buttons

**Prompt for Copilot Agent:**
```
Create touch-friendly radio control buttons for frequency, scan, EQ, and volume.

Location: /RTest/src/Radio.Web/Components/Shared/RadioControls.razor

Requirements:

1. Button layout (horizontal row):
   - Frequency Down (◄)
   - **SET button** (numeric keypad)
   - Frequency Up (►)
   - Sub Band
   - EQ
   - Volume Down (-)
   - Volume Up (+)

2. Frequency Up/Down buttons:
   - IconButton, 60px touch target
   - Single click: step frequency
   - Long press: start scan
   - Hold for continuous scan
   - Visual feedback during press
   - Call FrequencyUpAsync() or FrequencyDownAsync()

3. SET button (new):
   - Between Up/Down arrows
   - Opens frequency entry dialog
   - Numeric keypad layout
   - Shows current frequency
   - Validate frequency range on confirm
   - Call SetFrequencyAsync(frequency)

4. Long-press detection:
   - Use @onpointerdown / @onpointerup events
   - Start scan after 500ms hold
   - Continue scanning until release
   - Visual indicator (scanning icon/text)
   - Call StartScanAsync(direction)
   - Call StopScanAsync() on release

5. Sub Band button:
   - Cycles through step sizes
   - Shows current step on button or nearby
   - Call SetStepAsync(step)

6. EQ button:
   - Cycles through EQ modes
   - Shows current mode on button or nearby
   - Call SetEqualizerAsync(mode)

7. Device Volume buttons:
   - +/- buttons, 48px touch targets
   - Adjust radio device volume (not master)
   - Range: 0-100
   - Show current volume nearby
   - Call SetDeviceVolumeAsync(volume)

8. Frequency keypad dialog:
   - MudDialog with numeric keys
   - 60px per key (touch-friendly)
   - Decimal point for FM
   - Clear, Backspace, Confirm, Cancel
   - Validation:
     * FM: 87.5 - 108.0 MHz
     * AM: 520 - 1710 kHz
   - Error message for invalid input

9. Data binding:
   - Inject RadioApiService and AudioStateHubService
   - Track radio state
   - Disable buttons when not radio source

Success Criteria:
- All buttons work with single tap
- Long press triggers scan
- SET button opens keypad
- Keypad validates frequency
- Sub Band cycles steps
- EQ cycles modes
- Device volume adjusts
- Visual feedback on all interactions
- Touch targets 48-60px
```

---

### Phase 6: System Configuration UI (2-3 days)

#### Task 6.1: Create Configuration Management UI

**Prompt for Copilot Agent:**
```
Create system configuration management interface.

Location: /RTest/src/Radio.Web/Components/Pages/ConfigPage.razor

Requirements:

1. Layout:
   - Tabs for: Configuration, Preferences, Secrets
   - Each tab shows data grid
   - Toolbar: Add, Delete, Backup, Restore, Save

2. Configuration grid:
   - MudDataGrid with columns: Key, Value, Description, LastModified
   - Inline editing
   - Search/filter
   - Sortable columns
   - Touch-friendly row height (56px)

3. CRUD operations:
   - Add: opens dialog with key/value/description fields
   - Edit: inline or dialog
   - Delete: confirmation dialog
   - Save: calls configuration API

4. Secrets grid:
   - Key, Tag (masked), LastModified
   - No raw values displayed
   - Update: opens dialog to enter new secret
   - Generates new tag on update

5. Backup/Restore:
   - Backup: downloads .radiobak file
   - Restore: file upload, confirmation dialog
   - Show backup list with dates

6. System status section:
   - CPU usage chart
   - Memory usage chart
   - Disk space
   - Network info
   - Running processes (filtered)
   - Refresh button

7. Shutdown/Restart buttons:
   - Prominent warning
   - Confirmation dialog
   - Countdown before action

8. Data binding:
   - Create ConfigurationApiService for /api/configuration
   - Load on page load
   - Save on changes

Success Criteria:
- Can view all config/preferences/secrets
- CRUD operations work
- Backup creates file
- Restore loads file
- System status displays
- Shutdown/restart work (with confirmation)
- Touch-friendly grid
```

---

### Phase 7: Audio Visualization (2-3 days)

#### Task 7.1: Create Visualization Components

**Prompt for Copilot Agent:**
```
Create real-time audio visualization components.

Location: /RTest/src/Radio.Web/Components/Shared/Visualizations/

Requirements:

1. Create VisualizerPage.razor:
   - Tabs to switch between: VU Meter, Waveform, Spectrum
   - Full content area for visualization
   - 60fps target

2. VU Meter Component (VUMeter.razor):
   - Analog-style meter
   - Two meters: Left and Right channels
   - LED segments (green/yellow/red)
   - Peak hold indicators
   - Canvas-based rendering

3. Waveform Component (Waveform.razor):
   - Oscilloscope-style display
   - Scrolling left-to-right
   - Cyan line color
   - Canvas-based rendering

4. Spectrum Analyzer Component (SpectrumAnalyzer.razor):
   - Vertical bars for frequency bands
   - 20 bands (20Hz - 20kHz)
   - LED color scheme
   - Canvas-based rendering

5. Data binding:
   - Connect to /api/visualization endpoint via SignalR
   - Receive audio samples at ~60fps
   - Buffer samples for smooth animation
   - Use JavaScript interop for Canvas drawing

6. JavaScript interop:
   - Create visualizer.js in wwwroot/js/
   - Canvas drawing functions
   - RequestAnimationFrame loop
   - Optimized for performance

7. Performance considerations:
   - Use OffscreenCanvas if available
   - Throttle updates if FPS drops
   - Efficient data transfer (TypedArrays)

Success Criteria:
- All three visualizations render
- Smooth 60fps animation
- Accurate audio representation
- No performance issues
- Touch to switch between modes
- Responsive to audio changes
```

---

### Phase 8: Reusable Dialogs and Polish (2-3 days)

#### Task 8.1: Create Touch-Optimized Dialog Components

**Prompt for Copilot Agent:**
```
Create reusable touch-friendly dialog components.

Location: /RTest/src/Radio.Web/Components/Shared/Dialogs/

Requirements:

1. Numeric Keypad Dialog (NumericKeypadDialog.razor):
   - 3×4 grid of number buttons (0-9, decimal, clear)
   - Each button: 60px × 60px
   - Current value display
   - Backspace button
   - Confirm/Cancel buttons
   - Optional: Min/Max validation
   - Optional: Decimal support toggle

2. On-Screen Keyboard Dialog (KeyboardDialog.razor):
   - QWERTY layout optimized for landscape 12.5" × 3.75"
   - Keys: 48px × 48px
   - Shift key for uppercase
   - Special chars keyboard toggle
   - Return/Enter button
   - Backspace button
   - Space bar (2x width)
   - Confirm/Cancel buttons

3. Multi-Select Dialog (MultiSelectDialog.razor):
   - List of items with checkboxes
   - Search/filter bar
   - Select All / Deselect All
   - Large checkboxes: 40px touch target
   - Row height: 56px
   - Scrollable list
   - Confirm/Cancel buttons

4. Confirmation Dialog (ConfirmDialog.razor):
   - Title, message, icon
   - Two-button: Confirm/Cancel (or Yes/No)
   - Optional: danger styling for destructive actions
   - 48px button height
   - Auto-focus on safe action

5. Loading Dialog (LoadingDialog.razor):
   - Spinner/progress indicator
   - Message
   - Optional: cancel button
   - Modal (blocks interaction)

6. Usage:
   - Register dialogs globally in App.razor or MainLayout
   - Use MudBlazor's IDialogService
   - Pass parameters via DialogParameters
   - Return results via DialogResult

Success Criteria:
- All dialogs render correctly
- Touch targets 48px minimum
- Keyboard functional
- Keypad validates input
- Multi-select works
- Confirmation returns result
- Dialogs accessible via service
- Smooth animations
```

#### Task 8.2: UI Polish and Responsive Touches

**Prompt for Copilot Agent:**
```
Add final polish, animations, and ensure touch-friendly interactions.

Locations: Various components across /RTest/src/Radio.Web/

Requirements:

1. Add page transitions:
   - Horizontal slide animation between pages
   - 250ms duration
   - Smooth easing
   - Use CSS transitions or Blazor animation library

2. Add touch feedback:
   - Ripple effect on all buttons (Material Design)
   - MudBlazor has built-in, ensure enabled
   - Visual press state (scale down slightly)
   - Haptic feedback via JavaScript interop (if device supports)

3. Add loading states:
   - Skeleton screens while loading data
   - Loading spinners for actions
   - Disable buttons during operations
   - Show progress for long operations

4. Add error handling:
   - Toast notifications for errors
   - Graceful degradation on API failures
   - Retry buttons
   - Clear error messages

5. Add empty states:
   - For queue, search results, etc.
   - Icon + message + action button
   - Consistent styling

6. Optimize performance:
   - Virtualization for long lists (MudVirtualize)
   - Debounce search input
   - Cancel pending requests on page leave
   - Lazy load images

7. Add accessibility:
   - ARIA labels for icon buttons
   - Keyboard navigation support
   - Focus indicators (cyan accent)
   - Screen reader support (where applicable)

8. Test on target hardware:
   - Deploy to Raspberry Pi 5
   - Test on 12.5" × 3.75" touchscreen
   - Verify touch targets
   - Verify font sizes
   - Verify colors
   - Verify performance (60fps visualizations)

Success Criteria:
- Smooth page transitions
- Touch feedback on all interactions
- Loading states show appropriately
- Errors handled gracefully
- Empty states informative
- Performance acceptable (60fps where needed)
- No layout issues on target display
- Touch targets all 48px+
```

---

## Implementation Notes

### Development Order
Follow this sequence for optimal dependency management:

1. **Phase 1 (Setup)** → Required first, establishes foundation
2. **Phase 2 (Layout/Nav)** → Required for all other UI
3. **Phase 3 (Queue)** + **Phase 4 (Spotify)** + **Phase 5 (Radio)** → Can be done in parallel or any order based on priority
4. **Phase 6 (Config)** → Can be done anytime after Phase 1
5. **Phase 7 (Visualizations)** → Can be done anytime after Phase 1
6. **Phase 8 (Polish)** → Final phase after all features complete

### Testing Strategy
- **Unit Tests**: Test services and state management
- **Integration Tests**: Test API client services
- **Manual Testing**: Primary testing method for UI
  - Test on Windows during development
  - Test on Raspberry Pi 5 before finalizing
  - Test all touch interactions
  - Test long-press behaviors
  - Test real-time updates

### Common Pitfalls
- **SignalR reconnection**: Ensure automatic reconnection implemented
- **Memory leaks**: Dispose of SignalR subscriptions properly
- **Touch targets**: Verify all interactive elements meet 48px minimum
- **Performance**: Test visualizations on actual hardware, may need optimization
- **API timeouts**: Implement retry logic and timeout handling
- **State synchronization**: Ensure UI updates when SignalR messages received

### Deployment
- Build in Release mode for production
- Deploy to Raspberry Pi 5 via SSH/SCP or CI/CD
- Configure systemd service for auto-start
- Set up reverse proxy (nginx) if needed
- Configure HTTPS with Let's Encrypt

---

## Summary

This phased plan provides comprehensive Copilot prompts for building the entire Blazor UI. Each phase is designed to be independently executable, with clear requirements, success criteria, and implementation guidance.

**Total estimated effort: 20-28 days** (assumes backend phases from UIPREPARATION.md are complete)

The UI will be fully functional, touch-optimized, Material 3 compliant, and optimized for the 12.5" × 3.75" landscape display format.
