# Radio Project System Context: Project: Grandpa Anderson's Console Radio Remade

## 1. Project Overview
A modern audio command center hosted on a Raspberry Pi 5, encased in a vintage console radio cabinet. The software restores the original function (Radio/Vinyl) while adding modern capabilities (Spotify, Streaming, Smart Home Events, and Chromecast Audio).

## 2. Technical Architecture & Stack
*   **Hardware:** Raspberry Pi 5 (running Raspberry Pi OS / Linux).
*   **Framework:** .NET 8+ (C#).
*   **Audio Engine:** **SoundFlow** (`https://github.com/lsxprime/soundflow-docs/`).
*   **Hosting:** Single ASP.NET Core Host containing:
    *   **Blazor Server:** For the UI (Direct hardware access required).
    *   **Web API:** REST endpoints for external control.
    *   **SignalR Hub:** For real-time visualization data from SoundFlow library.
    *   **Local Stream Server:** An endpoint to expose raw audio as an HTTP stream (for Chromecast integration) from SoundFlow.
*   **Database:** Repository Pattern supporting hot-swapping between `SQLite` and `JSON` flat files.
*   **Logging:** `Serilog` (Console + File sinks).
*   **Configuration:** Configuration / Secrets / Preferences all handled by the infrastructure defined in `design/CONFIGURATION.md`
    * Other than a main configuration parameter `RootDir`, all preferences, secrets and configuration should come from the configuration subsystem.
	* All file and other resource paths should be referenced from this `RootDir` (except web projects which have their own structure)

## 3. Core Software Modules

### A. Audio Management (The "Kernel") - *ALL* of these components should leverage the SoundFlow library as much as possible.
*   See `/design/AUDIO.md` for the detailed design for all of the audio components.

### B. Audio Outputs - leverage the SoundFlow library as much as possible.
*   See `/design/AUDIO.md` for the detailed design for all of the audio components.

### C. Music Inputs - These are the primary audio sources
*   See `/design/AUDIO.md` for the detailed design for all of the audio components.

### C. Event Inputs - These are ephemeral audio souces that are normally short and indicate some notable event has occurred in the system
*   See `/design/AUDIO.md` for the detailed design for all of the audio components.

### D. **Future** Event Generators
*   See `/design/AUDIO.md` for the detailed design for all of the audio components.

## 4. Blazor User Interface (Web App)
*   See `/design/WEBUI.md` for the detailed design for all of the audio components.
