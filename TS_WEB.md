# Product Requirement Document: Radio Console TypeScript Web UI

## 1. Executive Summary
**Project Name:** Radio Console Web UI
**Version:** 1.0
**Status:** Draft

The **Radio Console** project is a modern audio command center hosted on a Raspberry Pi 5, encased in a vintage console radio cabinet. This document outlines the requirements for developing a **TypeScript-based Single Page Application (SPA)** to serve as the primary touch interface for the device. This UI will replace or serve as an alternative to the originally planned Blazor interface, communicating with the backend via the existing REST API and SignalR Hub.

## 2. Target Audience & Use Case
*   **Primary User:** Homeowner utilizing the vintage radio console.
*   **Hardware Environment:** Raspberry Pi 5 connected to a 7-inch or 10-inch touchscreen display.
*   **Input Method:** Primarily Touch (Tap, Swipe). Mouse/Keyboard usage is secondary/debugging only.
*   **Environment:** Low ambient light (living room), requiring a dark-themed, high-contrast UI.

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
*   **Base Library:** Material UI (MUI) v5+ or Tailwind CSS.
*   **Aesthetic:** "Modern Retro" – Combining modern usability with vintage aesthetics.
    *   **Backgrounds:** Deep Charcoal / OLED Black.
    *   **Accents:** Amber / Gold (mimicking vacuum tube glow).
    *   **Typography:** 
        *   Headers: Monospace or Retro-styled (e.g., "Share Tech Mono").
        *   Body: High-readability sans-serif (Roboto, Inter).

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

### 5.1 Global Playback Controls (Persistent Footer/Sidebar)
*   **Transport Controls:** Play, Pause, Stop.
*   **Volume Control:** Slider (0-100%) with Mute toggle.
    *   *API:* `POST /api/audio/volume/{value}`, `POST /api/audio/mute`
*   **Metadata Display:** Current Track Title, Artist, Album Art (Thumbnail).
    *   *API:* Poll `GET /api/audio` or receive updates.

### 5.2 Dashboard (Home Screen)
*   **Now Playing View:** Large Album Art, Full Track Details.
*   **Visualizer Integration:** Prominent display of real-time audio visualization (Spectrum/Waveform).
    *   *Requirement:* Must render efficiently (HTML5 Canvas) at ~30-60 FPS.

### 5.3 Source Selection
*   **Feature:** Grid or List of available audio sources (Spotify, Radio, Vinyl, File, AUX).
*   **Behavior:** Tapping a source switches the active input on the backend.
*   **Data:**
    *   List Sources: `GET /api/sources`
    *   Select Source: `POST /api/sources` (Body: `SelectSourceRequest`)

### 5.4 Play History
*   **Feature:** Scrollable list of recently played tracks.
*   **Data:** `GET /api/playhistory`
*   **Details:** Show Time, Source (Icon), Title, Artist.

### 5.5 Device Configuration
*   **Feature:** Manage Audio Outputs (Switch between Local Speakers, Chromecast, HTTP Stream).
*   **Data:**
    *   Get Outputs: `GET /api/devices/output`
    *   Set Output: `POST /api/devices/output`

### 5.6 Settings
*   **Configuration Editor:** Form to view/edit system settings (Ducking, Visualizer styles).
    *   *API:* `GET /api/configuration`, `POST /api/configuration`

## 6. UI/UX Specifications

### 6.1 Touch Optimization
*   **Hit Targets:** Minimum 48x48px for all interactive elements.
*   **Gestures:** Swipe left/right for track navigation (where applicable).
*   **Feedback:** Immediate visual feedback on touch (ripple effect or state change).

### 6.2 Responsive Layout
*   **Orientation:** Landscape primary.
*   **Resolutions:**
    *   Target: 1024x600 (7" Pi Display).
    *   Fallback: Responsive scaling for mobile/desktop browsers.

## 7. Deliverables
1.  **Source Code:** Complete TypeScript codebase in `src/Radio.Web.TS` (suggested path).
2.  **Docker Support:** `Dockerfile` for building the static assets and serving them via Nginx.
3.  **Documentation:** `README.md` with setup instructions (`npm install`, `npm run dev`).

## 8. Future Considerations
*   **Virtual Keyboard:** If the OS does not provide a usable on-screen keyboard, a custom implementation may be required for search fields.
*   **Offline Mode:** UI should gracefully handle backend disconnection (e.g., "Reconnecting..." overlay).
