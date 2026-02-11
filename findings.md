# Findings & Decisions

## UI Review — Material 3 Compliance Issues

### Home Page (Now Playing + Queue/History + Visualizer)
- **Balance slider** takes permanent space in NowPlayingPanel even though we'll always center it — REMOVE
- **Dark theme is all black/dark gray** — M3 prefers surface tonal elevation (slightly tinted dark surfaces, not pure black). Current `#1a1a1a` backgrounds lack M3 surface tint.
- **Accent color is cyan (#00BCD4)** — not a standard M3 tonal palette. M3 uses a primary/secondary/tertiary color system derived from a seed color.
- **Source chip** (e.g., "SDR Radio (RTL-SDR)") uses a bright green pill — should use M3 tonal chip style
- **Transport controls** are bare icons with no touch target padding — M3 minimum 48dp touch targets
- **Volume slider** has no visible track fill color differentiation — M3 sliders show filled portion in primary color
- **Three-panel layout** doesn't adapt to narrow screens — no responsive breakpoints

### Title Bar Mini-Player
- Contains album art (40x40), track title/artist, transport controls (prev/pause/next), AND volume — too crowded
- User requests: **ONLY mute + volume control** in the title bar
- Current title bar height is 60px — tight for touch. M3 top app bar standard is 64dp
- Navigation icons are icon-only with no labels — fine for desktop but M3 recommends bottom navigation on mobile/touch

### Play History (QueueHistoryPanel)
- **Bluetooth entries show stream URL** (`192.168.86.55:8080/stream/audio/mp3`) instead of song name — data issue from play history recording
- Some entries show "Bluetooth Device" or "Pixel 8 Pro" as title — should show actual song name
- **Source color mapping is outdated**: still has "Spotify", uses "FilePlayer" instead of "File", missing "Bluetooth"
- **No duration shown** — user wants Song Name, Artist, Song Length
- **No date grouping** — entries just listed chronologically with HH:mm time only

### Queue Page
- Empty state is good (centered icon + helpful text)
- **No file browser integration** — user must go to separate Files page. User wants this merged.
- Buttons use MudBlazor default styling — could better match M3 filled/tonal button variants

### Metrics Page
- **No component filtering** — all metrics shown in a flat grid with no way to filter by category
- **No sparklines** — just current snapshot values, no trend visualization
- **"Memory Usage Mb" shows "254.6%"** — formatting bug (applies % format to MB value)
- Category labels (API, AUDIO, BLUETOOTH, etc.) are tiny uppercase — hard to scan
- Cards are all same visual weight — no hierarchy between important and secondary metrics

### Devices Page
- Default device persistence exists for output and Cast
- **Input devices have no "Set as Default" action** — read-only, should allow selection
- Cast device table is functional but action buttons could be more M3 (filled tonal vs text buttons)

### System Config Page
- 7 tabs, very long page (~2857 lines) — functional but dense
- **Store Management** (Tab 7) has JSON export, DB backup, import — needs verification with new databases (secrets.db, fingerprints.db, albumart/)

### Global Material 3 Observations
1. **Color system**: Current cyan accent on pure black is high-contrast but not M3-aligned. M3 uses surface-tint (primary color blended into surfaces at different elevations).
2. **Typography**: MudBlazor defaults to Roboto which is fine for M3, but heading scales don't match M3 type scale.
3. **Touch targets**: Many buttons/icons are 40px or smaller — M3 minimum is 48dp.
4. **Elevation**: M3 dark theme uses tonal surface elevation (lighter = higher), not uniform dark backgrounds.
5. **Button variants**: Many actions use text buttons where M3 would use filled-tonal or outlined.
6. **No bottom navigation**: Touch-first interfaces (Pi with touchscreen) need bottom nav bar, not top icon row.

## Visualization Tap Logging
- `Console.WriteLine` in VisualizationTapModifier.cs line 68 is **already commented out** but the dead code (timing variables, commented line) should be fully removed.

## Volume/Balance/EQ Persistence
- **Volume**: Already persisted via debounced `ScheduleVolumePersist()` in AudioManager (500ms debounce, stores 0-100 int)
- **Balance**: Already persisted in same mechanism
- **EQ**: `RadioPreferences.LastEqualizerMode` schema exists but implementation is stub-only. No SoundFlow ParametricEqualizer wired yet.

## Deployment
- **No deployment scripts** exist for Raspberry Pi or Debian
- Only existing scripts are MSIX sparse package registration (Windows A2DP)
- CI/CD pipeline exists for Ubuntu but no ARM64/RPi config
- README mentions libmp3lame-dev requirement but no detailed guide
