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
