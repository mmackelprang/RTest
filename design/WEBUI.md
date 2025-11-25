# Radio UI Planning Guide

A touchscreen interface for an embedded music controller with a wide-format display (12.5" × 3.75") that manages multiple audio sources, visualizations, and system configuration through a .NET Core REST API.

**Experience Qualities**:
1. **Professional** - Industrial-grade interface with retro-LED aesthetics that communicates reliability and precision
2. **Efficient** - Icon-driven navigation optimized for touch interactions with minimal text, enabling quick access to all features
3. **Contextual** - Adaptive interface that transforms based on selected audio input, showing only relevant controls

**Complexity Level**: Complex Application (advanced functionality, accounts)
  - Multiple integrated subsystems (audio management, system configuration, visualization, playlist management)
  - Real-time data synchronization with backend API
  - Context-sensitive UI that adapts to input device selection
  - Comprehensive configuration management with persistence

## Essential Features

### Main Navigation Bar
- **Functionality**: Persistent top bar with date/time, system stats (CPU, RAM, threads), and navigation icons
- **Purpose**: Provides constant awareness of system state and quick access to main sections
- **Trigger**: Always visible, icons clickable to navigate between views
- **Progression**: Click icon → Component slides in/replaces current view → Breadcrumb updates
- **Success criteria**: All system stats update in real-time, navigation feels instantaneous

### Audio Setup Component
- **Functionality**: Central audio control with global volume, balance, transport controls (play/pause/skip), input/output selection
- **Purpose**: Primary interface for all audio playback operations
- **Trigger**: Default view on startup, accessible via main nav
- **Progression**: Load config from API → Display current settings → User adjusts → Save to API immediately
- **Success criteria**: All controls responsive, settings persist between sessions, dropdown shows only non-hidden devices

### Input/Output Configuration
- **Functionality**: Context-sensitive configuration dialogs based on selected audio source
- **Purpose**: Enables device-specific setup (USB port selection, hidden device management)
- **Trigger**: Click configuration icon next to input/output dropdowns
- **Progression**: Click config icon → Detect input/output type → Show appropriate dialog → User configures → Save to API → Close dialog
- **Success criteria**: Each input type shows correct configuration options, settings persist, validation prevents invalid entries

### Now Playing Views
- **Functionality**: Dynamic view that changes layout/controls based on active input (Spotify, Radio, Vinyl, File Player)
- **Purpose**: Show relevant metadata and controls for current audio source
- **Trigger**: Automatically updates when input changes or metadata arrives from API
- **Progression**: Input selected → API queried for metadata → Appropriate template rendered → Controls functional
- **Success criteria**: Each input type displays correctly, album art loads, LED fonts render properly, controls affect playback

### Playlist Grid
- **Functionality**: Scrollable grid showing queued tracks with song name, artist, timestamp
- **Purpose**: Visual queue management and track selection
- **Trigger**: Accessible via nav icon, auto-updates as playlist changes
- **Progression**: Click playlist icon → Grid loads from API → User scrolls/selects → Track plays on selection
- **Success criteria**: Smooth scrolling, responsive selection, real-time updates

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
- Radio Frequency: Bright amber `oklch(0.85 0.2 75)` - High visibility for primary reading

## Font Selection

Fonts should balance retro-digital LED aesthetics for numeric displays with clean modern sans-serif for UI labels and metadata. Use DSEG14 or similar seven-segment display font for time, frequency, and system stats to create authentic embedded device character. Inter or Roboto for general UI text ensures legibility at small sizes on the touchscreen.

- **Typographic Hierarchy**:
  - H1 (Date/Time Display): DSEG14 Classic Bold/32px/tight tracking - dominant retro-digital presence
  - H2 (System Stats): DSEG14 Classic Regular/18px/tight tracking - secondary LED readings
  - H3 (Radio Frequency): DSEG14 Classic Bold/48px/tight tracking - primary reading on radio view
  - H4 (Song Titles): Inter SemiBold/20px/normal tracking - metadata hierarchy
  - Body (Artists, Labels): Inter Regular/16px/normal tracking - supporting information
  - Caption (Timestamps): Inter Regular/14px/normal tracking - tertiary information
  - Button Text: Inter Medium/16px/slight tracking - minimal text on controls

## Animations

Animations should feel mechanical and precise—think professional audio equipment with satisfying physical feedback. Button presses should have subtle compression effects. View transitions should slide horizontally (matching the wide format). VU meters and spectrum analyzers should move fluidly without lag. All animations should be snappy (150-250ms) to maintain the responsive feel critical for a professional tool.

- **Purposeful Meaning**: Mechanical precision with tactile feedback—every interaction should feel like operating physical hardware with definitive clicks and smooth analog movements
- **Hierarchy of Movement**: Primary: transport controls (play/pause immediate response), Secondary: view transitions (smooth horizontal slides), Tertiary: real-time visualizations (fluid 60fps)

## Component Selection

- **Components**: 
  - Button (primary controls - play, save, etc.) with custom pressed states
  - Select (input/output dropdowns) with large touch-friendly options
  - Slider (volume, balance) with custom thumb design for precise control
  - Dialog (all configuration modals) with slide-up animation
  - Card (now playing, playlist items) with elevated appearance
  - Scroll Area (playlist grid, config grid) with custom scrollbar
  - Separator (section dividers) in subtle accent color
  - Progress (playback position) with custom styling
  - Badge (status indicators like band, signal strength)
  - Input (configuration values, text entry)
  - Checkbox (multi-select in hide/unhide dialog)
  - Table (configuration grid in system config)
  - Tabs (switching between visualization modes)
  
- **Customizations**:
  - Custom numeric keypad component with large touch targets (min 60px)
  - Custom on-screen keyboard component optimized for landscape layout
  - Custom multi-select list box with checkboxes and large rows
  - LED-style text component wrapping DSEG14 font with glow effect
  - Canvas-based visualization components (VU meter, waveform, spectrum)
  - Custom dropdown that filters hidden devices
  
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
