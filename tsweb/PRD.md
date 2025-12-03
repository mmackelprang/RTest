# Planning Guide

A modern TypeScript web interface for a vintage radio console housed in a Raspberry Pi 5, designed for a 12.2"×4.7" ultra-wide touchscreen display (1920×720px), serving as a sophisticated audio command center with Spotify integration, FM/AM radio, file playback, and real-time audio visualization.

**Experience Qualities**:
1. **Professional** - Industrial audio engineering aesthetics with precise, purpose-built controls that feel like high-end studio equipment
2. **Tactile** - Touch-optimized interface with immediate visual feedback, LED-style displays, and satisfying interaction states
3. **Immersive** - Real-time audio visualizations with retro-digital LED aesthetics that create an engaging listening experience

**Complexity Level**: Complex Application (advanced functionality, accounts)
  - Multiple integrated audio sources (Spotify, Radio, Files, Vinyl, AUX), real-time SignalR communication, advanced visualizations, system configuration, and persistent state management across sessions

## Essential Features

### Audio Playback Control
- **Functionality**: Transport controls (play/pause/skip), volume/balance sliders, progress bar with seek capability
- **Purpose**: Universal playback control regardless of active audio source
- **Trigger**: Persistent footer bar always visible at bottom of screen
- **Progression**: User taps play button → backend starts playback → SignalR updates position in real-time → user adjusts volume via slider → backend applies change instantly
- **Success criteria**: All controls respond within 100ms, volume changes are smooth, progress bar updates at least 10 times per second

### Multi-Source Audio Selection
- **Functionality**: Switch between Spotify, FM/AM Radio, Audio Files, Vinyl, and AUX input
- **Purpose**: Unified interface for all audio sources in the vintage console
- **Trigger**: Source selection icons in navigation bar or dedicated source picker
- **Progression**: User taps source icon → backend switches audio input → UI updates to show source-specific controls → playback continues or pauses based on source availability
- **Success criteria**: Source switching completes within 500ms, UI shows appropriate controls per source (Spotify search only for Spotify, frequency controls only for Radio)

### FM/AM Radio Tuner
- **Functionality**: LED-style frequency display, step up/down, scan, band switching, EQ modes
- **Purpose**: Digital recreation of vintage radio tuning with modern precision
- **Trigger**: User selects Radio source from navigation
- **Progression**: User taps frequency up arrow → frequency increments by step size (0.1 or 0.2 MHz) → LED display updates → signal strength meter animates → long-press on arrow → starts scan → stops on strong signal → user taps SET → numeric keypad appears → enters 101.5 → confirms → tunes to station
- **Success criteria**: Frequency changes are instant (<50ms), LED display has authentic glow effect, scan finds stations reliably, numeric keypad validates FM (87.5-108 MHz) and AM (520-1710 kHz) ranges

### Spotify Integration
- **Functionality**: Search with type filters (tracks/albums/playlists/artists), browse categories, play immediately or add to queue
- **Purpose**: Modern streaming integration for the vintage console experience
- **Trigger**: User taps Spotify search icon when Spotify is active source
- **Progression**: User taps search bar → on-screen keyboard appears → types "pink floyd" → debounced search triggers after 300ms → results grouped by type display → user taps track card → track begins playing → metadata updates in footer → album art shows in dashboard
- **Success criteria**: Search returns results within 1 second, on-screen keyboard is optimized for ultra-wide display, playback starts within 500ms of selection

### Real-Time Audio Visualization
- **Functionality**: VU meters, waveform oscilloscope, spectrum analyzer with LED aesthetics
- **Purpose**: Visual feedback that enhances the audio experience with retro-digital style
- **Trigger**: Dashboard always shows visualization; dedicated visualizer page for full-screen
- **Progression**: Audio plays → SignalR streams FFT/waveform/level data → Canvas renders at 60fps → user swipes to change visualization type → smooth transition to new visualization mode
- **Success criteria**: Consistent 60fps rendering, no dropped frames, LED color themes match display aesthetic (amber/cyan/green), visualizations react in real-time to audio with no perceptible lag

## Edge Case Handling
- **Network Disconnection**: Show reconnecting overlay with automatic retry using exponential backoff; cache last known state to display stale data gracefully
- **Missing Metadata**: Display filename or "Unknown Artist/Album" with generic music icon placeholder; truncate long filenames with ellipsis
- **Invalid Frequency Entry**: Red border with shake animation, clear error message ("FM range: 87.5-108.0 MHz"), prevent confirmation until valid
- **Spotify Auth Failure**: Clear error banner with "Reconnect Spotify" action button, disable Spotify-specific UI elements
- **Empty Queue/Results**: Contextual empty states with helpful iconography ("No tracks in queue", "No search results found") and suggested actions
- **Rapid Control Changes**: Debounce API calls (300ms) and show loading states to prevent overwhelming backend or causing UI lag
- **SignalR Disconnection**: Graceful degradation to polling fallback, persistent "Reconnecting..." indicator, restore subscriptions on reconnect

## Design Direction
The interface should evoke a professional recording studio meets vintage hi-fi equipment—think industrial audio engineering workstation with retro-digital LED aesthetics. It should feel precise, responsive, and purpose-built, prioritizing iconography over text. The design should be minimal yet rich where it matters (visualizations, LED displays), with LED-style displays for critical readings creating an authentic vintage-modern fusion. A dark, high-contrast interface optimized for low ambient light conditions.

## Color Selection
Triadic color scheme with deep charcoals, electric cyan accents, and warm amber LED displays to create a sophisticated retro-digital aesthetic.

- **Primary Color**: Deep Charcoal `oklch(0.2 0.01 240)` - Main background that reduces eye strain in low-light living room environments, creates canvas for LED elements to shine
- **Secondary Colors**: 
  - Dark Slate `oklch(0.25 0.02 240)` for elevated surfaces (cards, dialogs) - subtle distinction from background while maintaining cohesion
  - Electric Cyan `oklch(0.75 0.15 195)` for interactive elements, focus states, and modern accents - provides vibrant contrast against dark backgrounds
- **Accent Color**: Electric Cyan `oklch(0.75 0.15 195)` - Active states, focus indicators, primary action buttons, progress bars, creating visual hierarchy and drawing attention to interactive elements
- **Foreground/Background Pairings**:
  - Background (Deep Charcoal `oklch(0.2 0.01 240)`): White text `oklch(0.98 0 0)` - Ratio 15.2:1 ✓
  - Card (Dark Slate `oklch(0.25 0.02 240)`): White text `oklch(0.98 0 0)` - Ratio 13.8:1 ✓
  - Primary/Accent (Electric Cyan `oklch(0.75 0.15 195)`): Deep Charcoal text `oklch(0.2 0.01 240)` - Ratio 8.1:1 ✓
  - LED Display Amber `oklch(0.8 0.18 75)`: Deep Charcoal background - Ratio 9.5:1 ✓
  - LED Display Cyan `oklch(0.7 0.15 195)`: Deep Charcoal background - Ratio 6.2:1 ✓
  - Muted elements `oklch(0.5 0.02 240)`: Deep Charcoal background - Ratio 3.8:1 ✓ (for large text/icons)

## Font Selection
LED displays require authentic seven-segment font (DSEG14Classic) to evoke vintage digital equipment, while UI text uses modern sans-serif (Inter) for maximum readability and professional appearance.

- **Typographic Hierarchy**:
  - LED Frequency Display: DSEG14Classic-Bold/48px/tight spacing - dominant focal point
  - LED Time Display: DSEG14Classic-Bold/32px/tight spacing - persistent, always visible
  - LED Stats/Indicators: DSEG14Classic-Regular/18-24px/tight spacing - system information
  - Page Headings: Inter SemiBold/28-32px/normal spacing - section titles
  - Track Title (Now Playing): Inter SemiBold/32px/normal spacing - primary content
  - Artist/Album: Inter Regular/20-24px/relaxed spacing - secondary content
  - Body/Controls: Inter Medium/16px/normal spacing - interactive elements
  - Captions/Metadata: Inter Regular/14px/normal spacing - supporting information

## Animations
Animations should be purposeful and snappy, reinforcing the industrial precision of audio equipment while adding moments of delight—transitions should feel like high-end hardware responding instantly, not arbitrary software effects.

- **Purposeful Meaning**: Motion communicates state changes (play button to pause with subtle rotation), navigation (horizontal slides matching ultra-wide format), and system feedback (ripples on touch, scale-down on press). LED displays glow and pulse subtly when active to mimic real vintage equipment warmth.
- **Hierarchy of Movement**: 
  - Critical feedback (button press, touch ripple): 150ms - immediate, snappy
  - State transitions (play/pause icon morph): 200ms - noticeable but quick
  - Content changes (page slides, dialog open): 250ms - smooth without feeling slow
  - Visualizations (VU meters, spectrum): 16ms (60fps) - fluid, no easing, direct value mapping
  - Background effects (LED glow pulse): 2000ms - subtle, atmospheric, doesn't distract

## Component Selection
- **Components**: 
  - Navigation: Fixed top bar with icon buttons (Phosphor Icons, 48px touch targets) for Home, Queue, Radio, Visualizer, Settings
  - Playback Footer: Material 3 Slider for volume/balance, FAB for play/pause (68px), IconButtons for skip/shuffle/repeat (60px)
  - LED Displays: Custom component using DSEG14Classic font with configurable glow effect (CSS text-shadow), used for frequency, time, system stats
  - Source Cards: Material 3 elevated Cards (120×120px minimum) with icons, titles, and cyan accent border for active state
  - Spotify Search: Material 3 TextField with search icon, horizontal scrolling Chip filters, Card grid results (5-6 per row)
  - Radio Controls: Large IconButtons (60×60px) for frequency up/down with long-press scan, custom numeric keypad Dialog
  - File Browser: Material 3 List with folder/file icons, breadcrumb navigation, multi-select checkboxes (40×40px)
  - Visualizers: HTML5 Canvas with 60fps rendering, tabs to switch between modes (VU/Waveform/Spectrum)
  - Settings: Material 3 Dialog with tabbed sections, form inputs (TextField, Slider, Switch, Select)
- **Customizations**: 
  - Custom LED Display component with DSEG14Classic font and glow effects
  - Custom Numeric Keypad Dialog (3×4 grid, 60×60px keys) for frequency/numeric entry
  - Custom On-Screen Keyboard Dialog (full QWERTY optimized for 1920px landscape)
  - Custom Canvas-based visualizers with LED color themes
  - Custom signal strength meter (5-bar LED indicator)
- **States**: 
  - Buttons: Default (rest), Hover (subtle glow), Active/Pressed (scale 0.95, deeper color), Focused (cyan ring), Disabled (50% opacity, no interaction)
  - Inputs: Default (muted border), Focused (cyan accent glow), Error (red border with shake animation), Filled (content present)
  - Cards: Default (elevated), Hover (subtle lift), Pressed (slight scale down), Selected (cyan accent border)
  - LED Displays: Inactive (dim gray), Active (full brightness with glow), Flashing (pulse animation for alerts)
- **Icon Selection**: Phosphor Icons for consistent, modern aesthetic with retro industrial feel
  - Media: Play, Pause, SkipBack, SkipForward, Shuffle, Repeat (24-32px weight regular)
  - Audio: SpeakerHigh, SpeakerX, Waveform (24-32px)
  - Navigation: House, Queue, Radio, ChartBar, Gear (32px, bold weight for nav bar)
  - Radio: CaretUp, CaretDown, Hash, SignalMedium (24px)
  - Actions: MagnifyingGlass, Plus, Trash, FloppyDisk, Power (20-24px)
- **Spacing**: 8px grid system - gap-2 (8px) for tight groups, gap-3 (12px) for related controls, gap-6 (24px) for distinct sections, 16-24px card padding, 24px dialog padding
- **Mobile**: While primary target is fixed 1920×720 touchscreen, responsive down to 1600×600 with:
  - Horizontal scrolling for lists/grids instead of wrapping
  - Collapsible navigation to icon-only mode
  - Stacked footer controls if width constrained
  - Minimum font sizes maintained for touch readability
