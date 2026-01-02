# Product Requirement Document: Radio Console TypeScript Web UI

## 1. Executive Summary
**Project Name:** Radio Console Web UI
**Version:** 1.0
**Status:** Draft

The **Radio Console** project is a modern audio command center hosted on a Raspberry Pi 5, encased in a vintage console radio cabinet. This document outlines the requirements for developing a **TypeScript-based Single Page Application (SPA)** to serve as the primary touch interface for the device. This UI will replace or serve as an alternative to the originally planned Blazor interface, communicating with the backend via the existing REST API and SignalR Hub.

## 2. Target Audience & Use Case
*   **Primary User:** Homeowner utilizing the vintage radio console.
*   **Hardware Environment:** Raspberry Pi 5 connected to an embedded touchscreen display.
    *   **Display Dimensions:** 12.2" W × 4.7" H (wide, short form factor)
    *   **Resolution:** 1920×720 pixels
    *   **Aspect Ratio:** ~4:1 (ultra-wide landscape)
    *   **Resizing Support:** UI should be responsive to some degree but optimized for this specific aspect ratio and resolution
*   **Input Method:** Primarily Touch (Tap, Swipe, Long-Press). Mouse/Keyboard usage is secondary/debugging only.
*   **Environment:** Low ambient light (living room), requiring a dark-themed, high-contrast UI with LED-style displays for key readings.

## 3. Technical Stack Requirements

### 3.1 Core Technologies
*   **Language:** TypeScript 5+ (Strict Mode enabled).
*   **Framework:** React 18+ (Recommended) or Vue 3.
*   **Build Tool:** Vite (for fast HMR and optimized builds).
*   **State Management:** 
    *   Server State: TanStack Query (React Query) or SWR.
    *   UI State: Zustand, Redux Toolkit, or Context API.
*   **Real-time Communication:** `@microsoft/signalr` client.
*   **HTTP Client:** Axios or Fetch API wrapper.

### 3.2 Design System
*   **Base Library:** Material UI (MUI) v5+ or Tailwind CSS with Material 3 design tokens.
*   **Aesthetic:** "Modern Retro" – Industrial audio engineering workstation with retro-digital LED aesthetics.
    *   Think professional recording studio meets vintage hi-fi equipment
    *   Minimal text, maximum iconography
    *   Interface should feel precise, responsive, and purpose-built
*   **Color Palette (Triadic Scheme):**
    *   **Primary Background:** Deep Charcoal `oklch(0.2 0.01 240)` - Reduces eye strain in low light
    *   **Secondary Surfaces:** Dark Slate `oklch(0.25 0.02 240)` for cards and elevated surfaces
    *   **Accent Color:** Electric Cyan `oklch(0.75 0.15 195)` for active states, focus indicators, primary actions
    *   **LED Display Colors:**
        *   Time/Date: Amber `oklch(0.8 0.18 75)` - Classic LED watch aesthetic
        *   System Stats: Cyan `oklch(0.7 0.15 195)`
        *   Radio Frequency: Bright Amber `oklch(0.85 0.2 75)` OR Legacy Green `oklch(0.75 0.18 140)` (configurable)
*   **Typography:** 
    *   **LED Displays:** DSEG14Classic font family (seven-segment LED display font)
        *   DSEG14Classic-Bold for frequency, time displays (32-48px)
        *   DSEG14Classic-Regular for band indicators, system stats (18-24px)
    *   **UI Text:** Inter or Roboto (high-readability sans-serif)
        *   Headers: Inter SemiBold 20-32px
        *   Body: Inter Regular 16px
        *   Captions: Inter Regular 14px
        *   Buttons: Inter Medium 16px
*   **LED Font Assets:** Available at `/assets/fonts/DSEG14Classic-*` (ttf, woff, woff2 formats)

## 4. Architecture & Integration

The frontend will run as a static SPA (served via Nginx or Kestrel) and communicate with the .NET 8 Backend.

### 4.1 Backend Endpoints
*   **Base URL:** `https://<host>/api`
*   **Swagger Documentation:** `https://<host>/swagger/index.html`

### 4.2 Real-time Updates (SignalR)
*   **Hub URL:** `https://<host>/hubs/visualization`
*   **Methods to Subscribe:**
    *   `SubscribeToAll`
    *   `ReceiveSpectrum` (FFT Data)
    *   `ReceiveLevels` (VU Meter Data)
    *   `ReceiveWaveform` (Oscilloscope Data)

### 4.3 Audio Stream (Optional)
*   **Stream URL:** `https://<host>/stream/audio` (PCM Audio for local browser preview if needed).

## 5. Functional Requirements

### 5.1 Main Navigation Bar (Persistent Top Bar)
*   **Layout:** Fixed at top, always visible, height ~60px
*   **Left Section:** Date/Time display
    *   LED-style DSEG14Classic-Bold font (32px)
    *   Amber color `oklch(0.8 0.18 75)`
    *   Updates every second
*   **Center Section:** System stats (CPU %, RAM %, Thread count)
    *   LED-style DSEG14Classic-Regular font (18px)
    *   Cyan color `oklch(0.7 0.15 195)`
    *   Updates in real-time
*   **Right Section:** Navigation icons (48px touch targets, gap-6 spacing)
    *   Home / Now Playing
    *   Queue (conditional: only when source supports queue)
    *   Spotify Search (conditional: only when Spotify active)
    *   Radio Controls (conditional: only when Radio active)
    *   Visualizer
    *   System Configuration
*   **Behavior:** Icon navigation triggers horizontal slide transitions between views

### 5.2 Global Playback Controls (Persistent Footer/Bottom Bar)
*   **Transport Controls:** Material 3 buttons with 60px preferred touch targets
    *   **Shuffle:** Toggle button (only visible when source `SupportsShuffle`)
    *   **Previous:** Icon button (only enabled when source `SupportsPrevious`)
    *   **Play/Pause:** Large primary FAB-style button, toggles state
    *   **Next:** Icon button (only enabled when source `SupportsNext`)
    *   **Repeat:** Cycle button - Off/One/All (only visible when source `SupportsRepeat`)
*   **Volume Control:** Horizontal slider (0-100%) with mute toggle
    *   Material 3 Slider component with custom thumb (44px minimum)
    *   *API:* `POST /api/audio/volume/{value}`, `POST /api/audio/mute`
*   **Balance Control:** Horizontal slider (-100 to +100, center = 0)
*   **Progress Bar:** (when Duration available)
    *   Displays Position / Duration in format "2:34 / 4:15"
    *   Seekable if source `IsSeekable` (tap/drag to seek)
    *   Material 3 LinearProgress component
    *   Updates in real-time via SignalR
*   **Metadata Display:** Current Track Title, Artist (inline in footer)
    *   Inter SemiBold 16-18px for title
    *   Inter Regular 14px for artist
    *   *API:* `GET /api/audio` or SignalR updates

### 5.3 Dashboard (Home Screen)
*   **Layout:** Two-section horizontal split optimized for ultra-wide display
    *   **Left Section (40%):** Now Playing display
        *   Large album art (400×400px) or generic music icon
        *   Full track details:
            *   Title: Inter SemiBold 32px
            *   Artist: Inter Regular 24px
            *   Album: Inter Regular 20px
            *   Additional info: Genre, Year, Source indicator (Spotify/Radio/Vinyl/Files)
        *   **Empty State:** Generic music icon, Title="No Track", Artist="--", Album="--"
    *   **Right Section (60%):** Real-time audio visualization
        *   Prominent display of spectrum analyzer or waveform
        *   Tabs to switch between visualization types (VU Meter, Waveform, Spectrum)
        *   **Requirement:** HTML5 Canvas rendering at 30-60 FPS
        *   Retro LED aesthetic with configurable colors

### 5.4 Source Selection
*   **Feature:** Grid or horizontal scrollable list of available audio sources
*   **Source Types:** Spotify, Radio, Vinyl, Audio File Player, AUX
*   **Visual Design:** Material 3 Cards with:
    *   Source icon (60×60px, Material Icons)
    *   Source name (Inter Medium 18px)
    *   Active state: Cyan accent border `oklch(0.75 0.15 195)`
    *   Touch target: Entire card (minimum 120×120px)
*   **Behavior:** Tap to switch active input on backend
*   **Data:**
    *   List Sources: `GET /api/sources`
    *   Select Source: `POST /api/sources` (Body: `SelectSourceRequest`)

### 5.5 Spotify Integration UI

#### 5.5.1 Search Interface
*   **Search Bar:** Material 3 TextField with search icon
    *   Placeholder: "Search Spotify..."
    *   Opens on-screen keyboard on focus
    *   Search icon or Enter to submit
    *   Debounced input (300ms) to prevent excessive API calls
*   **Browse Button:** Icon button (48px) next to search bar
    *   Opens browse categories dialog
*   **Filter Pills:** Toggleable Material 3 Chips (minimum 32px height)
    *   Options: All (default), Music, Playlists, Podcasts, Albums, Artists, Audiobooks
    *   Multiple selection allowed
    *   Active state: Cyan accent with selection indicator
    *   Touch-friendly spacing (gap-2)

#### 5.5.2 Search Results Display
*   **Layout:** Grouped by type (Tracks, Albums, Playlists, Artists)
*   **Result Cards:** Material 3 Card elevation style
    *   Album/Track/Playlist image (100×100px)
    *   Title (Inter SemiBold 16px)
    *   Subtitle (Artist/Owner, Inter Regular 14px)
    *   Play button overlay (appears on hover/tap)
*   **Grid Layout:** 5-6 items per row (optimized for 1920px width)
*   **Actions:**
    *   Tap card or play button: Play immediately
    *   Long-press or secondary button: Add to queue
    *   *API:* `POST /api/spotify/play` with URI

#### 5.5.3 Browse Categories Dialog
*   **Material 3 Dialog:** Slide-up animation, full-width
*   **Categories List:** Grid of category cards
*   **Navigation Flow:**
    1. Tap category → Load playlists in that category
    2. Tap playlist → Load tracks
    3. Breadcrumb navigation at top
    4. Play button for playlists (plays first track, loads rest to queue)
*   **Data:**
    *   Categories: `GET /api/spotify/browse/categories`
    *   Playlists: `GET /api/spotify/browse/categories/{id}/playlists`

### 5.6 Radio Interface UI

#### 5.6.1 Radio Display (LED Aesthetic)
*   **Frequency Display:** Large LED-style reading
    *   Font: DSEG14Classic-Bold, 48px
    *   Color: Bright Amber `oklch(0.85 0.2 75)` OR Legacy Green `oklch(0.75 0.18 140)` (user-configurable)
    *   Format: "101.5" for FM, "1010" for AM (4 digits, leading zeros)
    *   Glow effect for authentic LED look
*   **Band Indicator:** AM/FM label
    *   Font: DSEG14Classic-Regular, 24px
    *   Same color as frequency
*   **Signal Strength Meter:** 5-bar visual indicator
    *   Bars fill based on strength (0-100%)
    *   Animated during scanning
    *   LED color theme
*   **Stereo Indicator:** "STEREO" text (FM only)
    *   Illuminated when stereo signal detected
    *   Inter Medium 16px
    *   LED color when active, dim gray when inactive
*   **Sub-Band Step:** Current frequency step size
    *   Display: "0.1 MHz", "0.2 MHz", "9 kHz", "10 kHz"
    *   Inter Medium 14px
*   **EQ Mode:** Current equalizer setting
    *   Display: Off, Rock, Pop, Jazz, Classical, Speech
    *   Inter Medium 14px
*   **Data:** Real-time updates via SignalR `OnRadioStateChanged`

#### 5.6.2 Radio Controls (Touch-Friendly Buttons)
*   **Frequency Control Row:** Horizontal button layout (60px touch targets each)
    *   **Down Arrow (◄):** Step frequency down
        *   Single tap: Decrease by step size
        *   Long-press (500ms): Start scan downward
        *   Visual feedback during scan
        *   *API:* `POST /api/radio/frequency/down`, `POST /api/radio/scan/start?direction=down`
    *   **SET Button:** Opens numeric keypad dialog (see 5.6.3)
    *   **Up Arrow (►):** Step frequency up
        *   Single tap: Increase by step size
        *   Long-press (500ms): Start scan upward
        *   *API:* `POST /api/radio/frequency/up`, `POST /api/radio/scan/start?direction=up`
*   **Function Button Row:**
    *   **Sub Band:** Cycles frequency step size
        *   FM: 0.1 MHz ↔ 0.2 MHz
        *   AM: 9 kHz ↔ 10 kHz
        *   *API:* `POST /api/radio/step`
    *   **EQ:** Cycles equalizer modes (Off → Rock → Pop → Jazz → Classical → Speech → Off)
        *   **Note:** Device-specific EQ on RF320 hardware, not global audio system EQ
        *   *API:* `POST /api/radio/equalizer`
    *   **Volume -:** Decrease radio device volume
        *   **Note:** Separate from master volume, adjusts RF320 hardware volume
        *   *API:* `POST /api/radio/volume?delta=-5`
    *   **Volume +:** Increase radio device volume
        *   *API:* `POST /api/radio/volume?delta=5`

#### 5.6.3 Frequency Keypad Dialog
*   **Material 3 Dialog:** Custom numeric keypad
*   **Layout:** 
    *   Current frequency display at top (read-only, LED font)
    *   3×4 numeric keypad (60×60px keys)
        *   Digits 0-9
        *   Decimal point (.) for FM
        *   Backspace key
        *   Clear key
    *   Confirm and Cancel buttons (48px height)
*   **Validation:**
    *   FM: 87.5-108.0 MHz
    *   AM: 520-1710 kHz
    *   Show error message for invalid input (red border with shake animation)
*   **Behavior:**
    *   Opens with current frequency
    *   First keypress clears display
    *   Confirm calls `POST /api/radio/frequency?value={frequency}`

### 5.7 Audio File Player UI

#### 5.7.1 File Browser
*   **Layout:** Material 3 Data Table or List with file system navigation
*   **Root Directory:** Configurable via `RootDir` setting (e.g., `/media/music`)
*   **Columns:**
    *   Icon (file type indicator)
    *   Filename/Title (from metadata if available)
    *   Artist (from metadata)
    *   Duration (from metadata)
    *   File size (optional)
*   **Navigation:**
    *   Breadcrumb trail at top showing current path
    *   Folder icon (48px) to navigate into directories
    *   Up/back button to navigate to parent directory
*   **File Selection:**
    *   Single tap: Add to queue
    *   Double tap: Play immediately
    *   Multi-select mode: Checkbox column (40px touch targets)
        *   Select multiple files
        *   Bulk actions: Add all to queue, Play all
*   **File Type Support:** MP3, FLAC, WAV, M4A, OGG (display appropriate icons)
*   **Empty State:** "No audio files found" with folder icon when directory is empty
*   **Data:**
    *   Browse: `GET /api/files/browse?path={path}`
    *   Play: `POST /api/audio/play` with file path

#### 5.7.2 Playlist View (Queue)
*   **Conditional Visibility:** Only visible when Audio File Player is active source
*   **Layout:** Scrollable Material 3 Data Grid (72px row height)
*   **Columns:**
    *   Title (may be filename if metadata unavailable)
    *   Artist (empty if unavailable)
    *   Album (empty if unavailable)
    *   Duration
*   **Interactions:**
    *   Tap row: Jump to that track
    *   Drag handle: Reorder tracks (if source supports)
    *   Delete button: Remove from queue
*   **Current Track Indicator:** Cyan accent border on currently playing item
*   **Real-time Updates:** Via SignalR `OnQueueChanged`

### 5.8 Play History
*   **Feature:** Scrollable list of recently played tracks (last 50-100 items)
*   **Layout:** Material 3 List with 64px row height
*   **Display Fields:**
    *   Timestamp (Inter Regular 14px, amber LED style optional)
    *   Source icon (Spotify/Radio/Vinyl/Files, 24×24px)
    *   Track title (Inter SemiBold 16px)
    *   Artist (Inter Regular 14px)
*   **Interaction:** Tap to replay (if source allows)
*   **Data:** `GET /api/playhistory`

### 5.9 Device Configuration
*   **Feature:** Manage Audio Outputs
*   **Layout:** Material 3 Select dropdown or dialog with output options
*   **Output Types:**
    *   Local Speakers (Default ALSA device)
    *   Chromecast (with device discovery)
    *   HTTP Stream (for external clients)
*   **Chromecast Discovery:**
    *   Button to scan for devices (48px)
    *   Loading indicator during scan
    *   List of discovered devices with connect button
    *   Connection status indicator
*   **Data:**
    *   Get Outputs: `GET /api/devices/output`
    *   Set Output: `POST /api/devices/output`
    *   Discover Chromecast: `GET /api/devices/chromecast/discover`

### 5.10 Settings & Configuration
*   **Layout:** Material 3 Dialog or dedicated page with tabbed sections
*   **Sections:**
    *   **Audio Settings:** Ducking configuration (percentage, attack/release times, policy)
    *   **Visualizer Settings:** FFT size, smoothing, peak hold time, window function
    *   **Display Settings:** LED color theme (Amber/Green toggle), brightness
    *   **System Settings:** API base URL, polling intervals, log level
*   **Configuration Editor:** Form with Material 3 inputs
    *   TextField for text values
    *   Slider for numeric ranges
    *   Switch for boolean toggles
    *   Select for enumerations
*   **Validation:** Real-time validation with error messages
*   **Actions:**
    *   Save button (48px height, primary action)
    *   Reset to defaults button (secondary action)
    *   *API:* `GET /api/configuration`, `POST /api/configuration`

### 5.11 System Configuration Manager
*   **Advanced Section:** For administrative tasks
*   **Features:**
    *   **Configuration Grid:** View/edit all configuration keys (key, value, description, last modified)
    *   **Secrets Management:** View secret tags (masked values), add/update secrets
    *   **Backup/Restore:** 
        *   Backup: Download .radiobak file
        *   Restore: Upload file with confirmation dialog
        *   List previous backups with timestamps
    *   **System Status:**
        *   CPU usage chart (real-time)
        *   Memory usage chart (real-time)
        *   Disk space
        *   Network info (IP, connectivity)
        *   Running processes (filtered, audio-related)
    *   **Power Actions:**
        *   Shutdown button (with confirmation and countdown)
        *   Restart button (with confirmation)
*   **Data:**
    *   Configuration: `GET /api/configuration/all`, `POST /api/configuration/update`
    *   System status: `GET /api/system/status`
    *   Power: `POST /api/system/shutdown`, `POST /api/system/restart`

## 6. UI/UX Specifications

### 6.1 Touch Optimization
*   **Hit Targets:** 
    *   Minimum: 48×48px for all interactive elements (Material 3 standard)
    *   Preferred: 60×60px for primary actions (play, pause, frequency controls)
*   **Gestures:** 
    *   Tap: Primary interaction for all buttons and selectable items
    *   Long-press: Secondary actions (e.g., radio frequency scan, context menus)
    *   Swipe left/right: Track navigation in visualizer or queue (where applicable)
    *   Drag: Reorder queue items, seek in progress bar
*   **Feedback:** 
    *   Immediate visual feedback on touch (Material 3 ripple effect)
    *   State changes (pressed/active states with color shift)
    *   Haptic feedback via Vibration API (if device supports)
    *   Audio feedback for critical actions (optional)

### 6.2 Responsive Layout for Ultra-Wide Display
*   **Primary Target:** 1920×720 pixels (12.2" W × 4.7" H, ~4:1 aspect ratio)
*   **Orientation:** Landscape only (ultra-wide format)
*   **Layout Strategy:**
    *   Horizontal information architecture
    *   Multi-column layouts (2-3 columns) to utilize width
    *   Persistent navigation bar at top (60px height)
    *   Persistent playback controls at bottom (80-100px height)
    *   Main content area: 1920×540-560px
*   **Responsive Behavior:**
    *   Graceful scaling for slightly different resolutions (1800-2000px width, 680-760px height)
    *   Minimum supported: 1600×600
    *   Maximum font scaling: 110% for readability at distance
*   **Content Organization:**
    *   Use horizontal space effectively (side-by-side views)
    *   Avoid deep vertical scrolling (limit to ~2 screen heights max)
    *   Horizontal scrolling acceptable for lists/grids (playlists, file browser)

### 6.3 Visual Design Principles
*   **Design Direction:** Industrial audio engineering workstation with retro-digital aesthetics
    *   Professional recording studio meets vintage hi-fi equipment
    *   Minimal text, maximum iconography
    *   Precise, responsive, purpose-built feel
*   **Color Contrast:**
    *   All text meets WCAG AA standards (minimum 4.5:1 for body, 3:1 for large text)
    *   LED displays use high-contrast colors (amber/cyan on dark charcoal)
    *   Active/inactive states clearly distinguished
*   **Spacing & Rhythm:**
    *   Consistent 8px grid system
    *   Gap between related controls: gap-3 (12px)
    *   Gap between control groups: gap-6 (24px)
    *   Card padding: 16-24px
    *   Dialog padding: 24px
*   **Animations:**
    *   Duration: 150-250ms for UI transitions (snappy, responsive)
    *   Easing: Ease-out for entering, ease-in for exiting
    *   Page transitions: Horizontal slide (matching wide format)
    *   Button press: Subtle scale down (0.95)
    *   Loading states: Smooth fade-in/out
    *   VU meters/visualizers: Fluid 60fps animation (no easing, direct value mapping)

### 6.4 Component Library Requirements

#### Material 3 Components
*   **Button:** Primary controls (play, save, confirm)
    *   Filled buttons for primary actions
    *   Outlined buttons for secondary actions
    *   Text buttons for tertiary actions
    *   Custom pressed states with visual feedback
*   **IconButton:** Navigation, tool actions (48px touch target)
    *   Optional badge for notifications
*   **FAB (Floating Action Button):** Main play/pause control
*   **Chip:** Filter pills in Spotify search (minimum 32px height)
    *   Filter chips with selection state
    *   Touch-friendly spacing (gap-2)
*   **Card:** Source selection, search results, file items
    *   Elevated appearance (Material 3 elevation tokens)
    *   Hover/press states
*   **TextField/Input:** Search bars, configuration values
    *   Filled or outlined style
    *   Focus state with cyan accent glow
    *   Error state with red border and shake animation
*   **Select/Dropdown:** Source selection, output device selection
    *   Large touch-friendly options (minimum 48px per option)
*   **Slider:** Volume, balance, configuration ranges
    *   Custom thumb design (minimum 44px)
    *   Value tooltip on drag
*   **Switch:** Boolean toggles in settings
    *   Minimum 52px wide touch target
*   **Checkbox:** Multi-select in file browser (40px touch target)
*   **Radio Button:** Exclusive selections (40px touch target)
*   **Dialog:** All modal interactions
    *   Slide-up animation
    *   Material 3 elevated styling
    *   Scrim (backdrop) with semi-transparent dark overlay
*   **List/ListItem:** File browser, play history, queue
    *   One-line: 56px height
    *   Two-line: 72px height
    *   Three-line: 88px height
*   **DataGrid/Table:** Configuration grid, queue with columns
    *   Sortable columns
    *   Touch-scroll support
    *   Minimum 56px row height
*   **Tabs/TabBar:** Switching between visualizer modes, configuration sections
    *   Indicator line (cyan accent)
    *   Minimum 48px touch target per tab
*   **LinearProgress:** Playback position, loading states
    *   Custom styling with retro LED colors
*   **CircularProgress:** Loading indicators in buttons/dialogs
*   **Snackbar/Toast:** Notifications and confirmations
    *   Auto-dismiss after 3-5 seconds
    *   Action button optional (e.g., "Undo")
*   **Badge:** Notification counts on navigation icons

#### Custom Components
*   **LED Display Component:** Reusable component for frequency, time, stats
    *   Uses DSEG14Classic font
    *   Configurable color (amber/green/cyan)
    *   Optional glow effect
    *   Props: value, fontSize, color, glow
*   **Numeric Keypad Dialog:** Touch-optimized number entry
    *   3×4 grid, 60×60px keys
    *   Decimal point, backspace, clear
    *   Min/max validation
    *   Used for radio frequency entry, numeric config values
*   **On-Screen Keyboard Dialog:** Full QWERTY keyboard for text entry
    *   Optimized for 1920px width landscape layout
    *   Shift key for uppercase
    *   Special characters toggle
    *   Return, backspace, space bar
    *   Used for Spotify search, text configuration
*   **Multi-Select Dialog:** Checkbox list with search/filter
    *   Large checkboxes (40px)
    *   Select all / deselect all
    *   Scrollable list (56px rows)
    *   Confirm/Cancel buttons
*   **Canvas Visualizers:** Real-time audio visualization
    *   VU Meter: Analog-style meter with LED segments
    *   Waveform: Oscilloscope-style scrolling display
    *   Spectrum Analyzer: Vertical frequency bars (20 bands)
    *   Target: 60fps rendering
    *   LED color scheme (configurable)

### 6.5 Icon Selection (Material Icons or Phosphor Icons)
*   **Media Controls:** Play, Pause, SkipBack, SkipForward, Shuffle, Repeat
*   **Audio:** SpeakerHigh, SpeakerLow, SpeakerX (mute)
*   **Navigation:** Home, List, MusicNote, Radio, Waveform, Gear
*   **Actions:** Plus (add), Trash (delete), MagnifyingGlass (search)
*   **Radio:** ArrowUp, ArrowDown, Hash (SET), Signal
*   **System:** FloppyDisk (save), DownloadSimple (backup), UploadSimple (restore), Power
*   **Sources:** Spotify logo, Disc (vinyl), Folder (files), UsbPlug
*   **File Types:** FileAudio, FileMp3, Folder, FolderOpen

### 6.6 Accessibility Considerations
*   **ARIA Labels:** All icon buttons must have aria-label attributes
*   **Keyboard Navigation:** Support for Tab/Shift+Tab focus navigation (debugging/fallback)
*   **Focus Indicators:** Visible cyan accent focus ring on keyboard-focused elements
*   **Screen Reader Support:** Semantic HTML, proper heading hierarchy, alt text for images
*   **Color Contrast:** All text/UI elements meet WCAG AA standards
*   **Error Messages:** Clear, actionable error messages with visual and text indicators

## 7. Deliverables
1.  **Source Code:** Complete TypeScript codebase in `src/Radio.Web.TS` (suggested path)
    *   Structured with clear separation of concerns (components, services, hooks, utilities)
    *   TypeScript 5+ with strict mode enabled
    *   ESLint and Prettier configuration
    *   All dependencies documented in package.json
2.  **Docker Support:** 
    *   `Dockerfile` for building the static assets
    *   Multi-stage build (node for build, nginx for serving)
    *   Nginx configuration for SPA routing and API proxy
    *   Docker Compose file for local development with backend
3.  **Documentation:** 
    *   `README.md` with setup instructions:
        *   Prerequisites (Node.js version, npm/yarn)
        *   Installation: `npm install`
        *   Development: `npm run dev`
        *   Build: `npm run build`
        *   Linting: `npm run lint`
    *   Architecture overview
    *   Component documentation (key components and their props)
    *   API integration guide
    *   Deployment instructions
4.  **Configuration Files:**
    *   `vite.config.ts` with development server proxy configuration
    *   `.env.example` with required environment variables
    *   TypeScript configuration (`tsconfig.json`)
5.  **Asset Integration:**
    *   LED fonts from `/assets/fonts/DSEG14Classic-*` included in build
    *   Icon library (Material Icons or Phosphor Icons)
    *   Example images for testing (album art placeholders)

## 8. Edge Cases & Error Handling

### 8.1 Network & API Errors
*   **No API Connection:** 
    *   Display persistent error banner at top
    *   Show last cached data (if available)
    *   Attempt automatic reconnection with exponential backoff
    *   "Retry" button for manual reconnection
*   **SignalR Disconnection:**
    *   Show "Reconnecting..." overlay (semi-transparent)
    *   Automatic reconnection attempts
    *   Graceful degradation (polling fallback if SignalR unavailable)
*   **API Timeout/Slow Response:**
    *   Loading indicators for all async operations
    *   Timeout after 10 seconds with error message
    *   Cancel button for long-running operations

### 8.2 Data Validation & Empty States
*   **Missing Configuration Keys:** Use sensible defaults, log missing keys
*   **Invalid USB Devices:** Filter out non-audio devices, show empty state
*   **Zero Playlist Items:** Empty state with icon and "No tracks in queue" message
*   **No Search Results:** Empty state with "No results found" message and clear filters button
*   **Missing Album Art:** Generic music icon placeholder
*   **Missing Metadata:** Display filename or "Unknown" with dashes
*   **Long Names:** Truncate with ellipsis, show full text on hover/long-press (tooltip)

### 8.3 User Input Validation
*   **Invalid Frequency Entry:** Red border, shake animation, error message
*   **Out-of-Range Values:** Clamp to min/max, show warning
*   **Rapid Setting Changes:** Debounce API calls (300ms) to prevent overwhelming backend
*   **Concurrent Operations:** Disable buttons during operations, show loading state

### 8.4 Device-Specific Considerations
*   **Spotify Auth Failure:** Clear error message with "Reconnect Spotify" button
*   **Chromecast Not Found:** Empty state with "Scan Again" button
*   **Radio Device Offline:** Disable radio controls, show status message
*   **File System Access Errors:** Permission denied message, suggest checking directory

### 8.5 Configuration Management
*   **Hidden All Outputs:** Prevent hiding all outputs (keep at least one visible)
*   **Malformed Config Data:** Graceful fallback, log error, show user-friendly message
*   **Secrets Not Found:** Prompt to re-enter secret values
*   **Backup/Restore Failures:** Clear error messages with troubleshooting steps

## 9. Future Considerations
*   **Virtual Keyboard:** 
    *   Custom on-screen keyboard implementation (provided in requirements)
    *   QWERTY layout optimized for 1920×720 landscape
    *   Adaptive to input type (numeric, email, text)
*   **Offline Mode:** 
    *   Service Worker for offline functionality
    *   Cache static assets for faster loading
    *   IndexedDB for caching API responses
    *   UI should gracefully handle backend disconnection with "Reconnecting..." overlay
*   **Multi-language Support:**
    *   i18n framework for internationalization
    *   English as primary language
    *   Structure to support future translations
*   **Themes:**
    *   Light mode option (for bright environments)
    *   Custom color themes beyond amber/green
    *   User preference persistence
*   **Advanced Visualizations:**
    *   3D spectrum analyzer
    *   Customizable visualization plugins
    *   Multiple simultaneous visualizations
*   **Voice Control:**
    *   Integration with voice assistants
    *   Voice commands for playback control
*   **Mobile Companion App:**
    *   Remote control via separate mobile app
    *   Same TypeScript codebase with responsive design
*   **Performance Monitoring:**
    *   FPS counter for visualizations
    *   Network request monitoring
    *   Error tracking integration (Sentry, etc.)
