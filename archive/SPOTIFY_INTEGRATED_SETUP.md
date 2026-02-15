# Spotify Integrated Mode Setup Guide

This guide explains how to set up and configure Spotify in **Integrated Mode** for the Radio Console application. Integrated mode uses a managed `librespot` process to stream Spotify audio directly through the application, eliminating the need for external audio loopback devices like VB-Cable or Stereo Mix.

---

## Overview

### Spotify Integration Modes

The Radio Console supports two Spotify integration modes:

| Mode | Audio Flow | Visualization | Setup Complexity | Best For |
|------|-----------|---------------|------------------|----------|
| **RemoteControl** | External device | ❌ No | Low | Simple playback control |
| **Integrated** | Managed librespot pipe | ✅ Yes | Medium | Linux/Raspberry Pi |

**Integrated Mode** is recommended for:
- **Raspberry Pi** and Linux systems
- Avoiding virtual audio device configuration
- Clean, self-contained Spotify integration
- Full visualization support

---

## Prerequisites

### 1. Spotify Premium Account

Spotify Premium is **required** for librespot to function. Free accounts are not supported.

### 2. Spotify Developer Application

You need to create a Spotify Developer application to obtain credentials:

1. Go to [Spotify Developer Dashboard](https://developer.spotify.com/dashboard)
2. Click **"Create an App"**
3. Fill in the app name and description (e.g., "Radio Console")
4. Note down:
   - **Client ID**
   - **Client Secret**

### 3. Spotify Refresh Token

You need to obtain a refresh token using the OAuth 2.0 Authorization Code Flow:

#### Option A: Use Spotify Auth Helper Tool

If you have a helper script:
```bash
./scripts/spotify-auth-helper.sh
```

#### Option B: Manual OAuth Flow

1. Construct the authorization URL:
   ```
   https://accounts.spotify.com/authorize?client_id=YOUR_CLIENT_ID&response_type=code&redirect_uri=http://127.0.0.1:8888/callback&scope=user-read-playback-state%20user-modify-playback-state%20user-read-currently-playing
   ```

2. Open the URL in a browser and authorize the application

3. Copy the **authorization code** from the redirect URL

4. Exchange the code for a refresh token using curl:
   ```bash
   curl -X POST https://accounts.spotify.com/api/token \
     -H "Content-Type: application/x-www-form-urlencoded" \
     -d "grant_type=authorization_code" \
     -d "code=YOUR_AUTH_CODE" \
     -d "redirect_uri=http://127.0.0.1:8888/callback" \
     -d "client_id=YOUR_CLIENT_ID" \
     -d "client_secret=YOUR_CLIENT_SECRET"
   ```

5. Save the **refresh_token** from the response

### 4. Librespot Executable

Librespot is the Spotify client that runs as a child process. You need to install or build it.

---

## Installing Librespot

### Option A: Pre-built Binaries (Recommended)

#### Linux/Raspberry Pi

1. Download the latest release from [librespot releases](https://github.com/librespot-org/librespot/releases)

   **Important Security Note:** Always verify the integrity of downloaded binaries before installation to prevent supply-chain attacks.

   ```bash
   # For ARM (Raspberry Pi)
   # Download the binary and its checksum file
   wget https://github.com/librespot-org/librespot/releases/download/v0.4.2/librespot-linux-armhf-v0.4.2.tar.gz
   wget https://github.com/librespot-org/librespot/releases/download/v0.4.2/SHA256SUMS
   
   # Verify the checksum before extracting
   sha256sum -c SHA256SUMS --ignore-missing
   # If the checksum matches, you'll see: librespot-linux-armhf-v0.4.2.tar.gz: OK
   
   # Extract and install only if checksum verification succeeds
   tar -xzvf librespot-linux-armhf-v0.4.2.tar.gz
   sudo mv librespot /usr/local/bin/
   sudo chmod +x /usr/local/bin/librespot
   
   # For x86_64 Linux
   wget https://github.com/librespot-org/librespot/releases/download/v0.4.2/librespot-linux-x86_64-v0.4.2.tar.gz
   wget https://github.com/librespot-org/librespot/releases/download/v0.4.2/SHA256SUMS
   
   # Verify the checksum
   sha256sum -c SHA256SUMS --ignore-missing
   
   # Extract and install only if checksum verification succeeds
   tar -xzvf librespot-linux-x86_64-v0.4.2.tar.gz
   sudo mv librespot /usr/local/bin/
   sudo chmod +x /usr/local/bin/librespot
   ```

2. Verify installation:
   ```bash
   librespot --version
   ```

#### Windows

1. Download `librespot.exe` from [librespot releases](https://github.com/librespot-org/librespot/releases)

   **Important Security Note:** Verify the file hash before executing. Download the SHA256SUMS file from the release page and compare hashes using PowerShell:
   
   ```powershell
   # Calculate the hash of the downloaded file
   Get-FileHash librespot.exe -Algorithm SHA256
   # Compare with the hash in SHA256SUMS file
   ```

2. Extract to a permanent location (e.g., `C:\librespot\librespot.exe`)

3. Verify installation:
   ```cmd
   C:\librespot\librespot.exe --version
   ```

### Option B: Build from Source

If pre-built binaries are not available for your platform:

#### Prerequisites
- Rust toolchain (install via [rustup](https://rustup.rs/))
- Build dependencies:
  ```bash
  # Ubuntu/Debian/Raspberry Pi
  sudo apt install build-essential libasound2-dev
  
  # Fedora/RHEL
  sudo dnf install gcc alsa-lib-devel
  ```

#### Build Steps

1. Clone the repository:
   ```bash
   git clone https://github.com/librespot-org/librespot.git
   cd librespot
   ```

2. Build with pipe backend:
   ```bash
   cargo build --release --no-default-features --features "pipe-backend"
   ```

3. Install the binary:
   ```bash
   # Linux
   sudo cp target/release/librespot /usr/local/bin/
   sudo chmod +x /usr/local/bin/librespot
   
   # Or keep it in a custom location
   cp target/release/librespot ~/librespot/librespot
   ```

4. Verify:
   ```bash
   librespot --version
   ```

---

## Configuration

### 1. Configure Spotify Secrets

You must provide your Spotify credentials via the configuration system.

#### Method A: Using Configuration API (Recommended)

Use the Radio Console Web UI at **http://localhost:5001/system** → **Configuration** tab → **Secrets** section to add:

- `spotify_clientid` = Your Client ID
- `spotify_clientsecret` = Your Client Secret  
- `spotify_refreshtoken` = Your Refresh Token

#### Method B: Direct Configuration File

Edit `src/Radio.API/appsettings.json`:

```json
{
  "Spotify": {
    "ClientID": "${secret:spotify_clientid}",
    "ClientSecret": "${secret:spotify_clientsecret}",
    "RefreshToken": "${secret:spotify_refreshtoken}"
  }
}
```

Then create secrets in the configuration database or secrets file.

#### Method C: Environment Variables

For production deployments:

```bash
export SPOTIFY__CLIENTID="your_client_id"
export SPOTIFY__CLIENTSECRET="your_client_secret"
export SPOTIFY__REFRESHTOKEN="your_refresh_token"
```

### 2. Configure Device Options

Configure Spotify mode and librespot path:

#### Via Web UI (Recommended)

Navigate to **http://localhost:5001/system** → **Configuration** → **Devices** tab:

1. Set **Spotify Mode** to `Integrated`
2. Set **Librespot Path** to the location of your librespot executable:
   - Linux: `/usr/local/bin/librespot` or `/usr/bin/librespot`
   - Raspberry Pi: `/usr/local/bin/librespot`
   - Windows: `C:\librespot\librespot.exe`
3. Click **Save Device Settings**

#### Via Configuration File

Edit `src/Radio.API/appsettings.json`:

```json
{
  "Devices": {
    "Spotify": {
      "Mode": "Integrated",
      "LibrespotPath": "/usr/local/bin/librespot"
    }
  }
}
```

**Note:** Use absolute paths for best reliability.

---

## Verification

### 1. Start the Radio Console

```bash
# Start the API
dotnet run --project src/Radio.API

# In another terminal, start the Web UI
dotnet run --project src/Radio.Web
```

### 2. Check Logs

Look for log entries indicating successful initialization:

```
[INF] Initializing Spotify in Integrated mode
[INF] Librespot process started (PID: 12345)
[INF] Spotify device 'Radio Console' started successfully
[INF] Spotify integrated mode initialized successfully
```

### 3. Test Playback

1. Open the Web UI at http://localhost:5001
2. Navigate to the **Spotify** page
3. Select a playlist or track
4. Start playback
5. Verify that:
   - Audio plays through the Radio Console
   - The visualizer shows audio activity
   - Metadata (track name, artist) appears

---

## Troubleshooting

### Librespot Not Found

**Error:** `Librespot executable not found at: /usr/local/bin/librespot`

**Solutions:**
1. Verify the path is correct:
   ```bash
   which librespot
   ```
2. Update the `LibrespotPath` in configuration
3. Ensure the file has execute permissions:
   ```bash
   chmod +x /usr/local/bin/librespot
   ```

### Authentication Failed

**Error:** `Failed to start device: Spotify credentials not configured`

**Solutions:**
1. Verify your Client ID, Client Secret, and Refresh Token are correctly configured
2. Check that secrets are properly resolved (check logs for `${secret:...}` placeholders)
3. Regenerate the refresh token if it has expired

### Librespot Process Exits Immediately

**Error:** `Librespot exited with code 1`

**Solutions:**
1. Check librespot logs in the Radio Console logs
2. Test librespot manually:
   ```bash
   librespot --name "Test" --backend pipe --device - --access-token "test"
   ```
3. Ensure Spotify Premium account is active
4. Verify the access token is valid (check Spotify API authentication)

### No Audio Output

**Issue:** Librespot starts but no audio plays

**Solutions:**
1. Verify the integrated source is active in the audio mixer
2. Check that the source is not muted
3. Ensure the visualizer shows audio levels
4. Check Radio Console audio engine logs for errors

### Token Refresh Errors

**Error:** `Token refresh failed`

**Solutions:**
1. Verify your Client Secret is correct
2. Check network connectivity to Spotify API
3. Regenerate the refresh token if it's invalid
4. Token refresh happens every 50 minutes - check logs around that time

---

## Advanced Configuration

### Custom Librespot Options

The Radio Console currently uses these librespot settings:

```bash
librespot \
  --name "Radio Console" \
  --backend pipe \
  --device - \
  --access-token "<token>" \
  --bitrate 320 \
  --enable-volume-normalisation \
  --initial-volume 100 \
  --cache-size-limit 1024
```

To customize these, you would need to modify `LibrespotManager.cs` in the source code.

### Running as a System Service

For production Raspberry Pi deployments, configure the Radio Console as a systemd service (see `/design/SYSTEMCONFIGURATION.md`). The librespot process will be automatically managed by the Radio Console service.

---

## Security Notes

1. **Store Secrets Securely**: Use the encrypted secrets storage or environment variables
2. **Refresh Token**: Treat this like a password - it provides long-term access to your Spotify account
3. **Access Scope**: The application only requests playback control permissions
4. **Token Rotation**: Consider regenerating tokens periodically for security

---

## References

- [Librespot GitHub Repository](https://github.com/librespot-org/librespot)
- [Spotify Web API Documentation](https://developer.spotify.com/documentation/web-api/)
- [Radio Console Configuration Guide](/design/SYSTEMCONFIGURATION.md)

---

**Last Updated:** 2026-01-05  
**Version:** 1.0
