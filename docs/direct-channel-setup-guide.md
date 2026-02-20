# Direct Cast Channel — Setup Guide

Step-by-step instructions for enabling the experimental Direct Cast Channel
streaming mode, which sends audio directly over the Cast protocol instead of
HTTP. This reduces end-to-end latency from 4-10 seconds to potentially
under 1 second.

> **Prerequisite knowledge:** You should be comfortable editing JSON config
> files and hosting a static HTML file on a web server.

---

## Table of Contents

1. [Overview — What You're Setting Up](#1-overview)
2. [Host the Custom Receiver HTML](#2-host-the-custom-receiver-html)
3. [Register the Receiver on Google Cast Developer Console](#3-register-the-receiver)
4. [Register Your Cast Devices for Development](#4-register-your-cast-devices)
5. [Configure Radio Console](#5-configure-radio-console)
6. [Deploy and Verify](#6-deploy-and-verify)
7. [Troubleshooting](#7-troubleshooting)
8. [Switching Back to HTTP Mode](#8-switching-back)

---

## 1. Overview

The Direct Channel mode replaces this pipeline:

```
Audio Engine → MP3 Encode → HTTP Server → Cast fetches HTTP → MP3 Decode → Play
                                (4-10s latency)
```

With this:

```
Audio Engine → WAV Chunk → Base64 → Cast Protocol Message → Web Audio API → Play
                                (target: <1s latency)
```

To make this work, the Cast device needs a **custom receiver application** — a
small HTML page that knows how to listen for audio chunks on a custom message
bus and play them via the Web Audio API. Google's Default Media Receiver
(CC1AD845) doesn't understand our custom messages, so we must register our own.

**You will need:**
- A Google account
- A way to host a single static HTML file over HTTPS (GitHub Pages, any web
  server, Cloudflare Pages, Netlify, etc.)
- Access to at least one Cast device on your network
- ~15 minutes

---

## 2. Host the Custom Receiver HTML

The receiver is a single file: `docs/receiver-direct-channel.html`

It must be served over **HTTPS** at a publicly accessible URL. The Cast device
fetches this URL when the application launches — it does NOT need to be on
your local network, but it must be reachable from the internet (Google's Cast
infrastructure loads it).

### Option A: GitHub Pages (Recommended — Free, Zero Maintenance)

If this repo is on GitHub:

1. Push the `docs/` folder to a branch (e.g., `main` or `gh-pages`).

2. Go to your repo's **Settings → Pages**.

3. Under **Source**, select the branch and set the folder to `/docs`.

4. Click **Save**. GitHub will publish the site.

5. Your receiver URL will be:
   ```
   https://<username>.github.io/<repo>/receiver-direct-channel.html
   ```

6. Wait a minute for the deploy, then verify the URL loads in a browser.
   You should see "Radio Console — Direct Channel Receiver" and
   "Waiting for audio..."

### Option B: Any Static Web Host

Copy `docs/receiver-direct-channel.html` to any HTTPS-capable web server:

- **Cloudflare Pages / Netlify / Vercel:** Drag and drop the file.
- **nginx / Apache:** Place the file in your web root.
- **Python one-liner** (for testing, not production):
  ```bash
  # Only works if your machine has a public HTTPS URL or you use a tunnel
  cd docs && python3 -m http.server 8443
  ```

The only requirement is that the final URL is **HTTPS** and publicly
accessible. Save this URL — you'll need it in the next step.

### Option C: Local Network (Advanced)

For testing on a local network without public hosting, you can use a tool
like [ngrok](https://ngrok.com/) or [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
to expose a local HTTP server with an HTTPS URL:

```bash
# Terminal 1: serve the file
cd docs && python3 -m http.server 8080

# Terminal 2: expose via ngrok
ngrok http 8080
```

ngrok will give you an HTTPS URL like `https://abc123.ngrok-free.app` — use
`https://abc123.ngrok-free.app/receiver-direct-channel.html` as your
receiver URL.

---

## 3. Register the Receiver

### 3a. Access the Google Cast SDK Developer Console

1. Go to **https://cast.google.com/publish/**

2. Sign in with your Google account.

3. If this is your first time, you'll need to pay a **one-time $5 registration
   fee** and accept the developer agreement. This is a Google requirement for
   all Cast developer accounts.

### 3b. Create a New Application

1. Click **"Add New Application"**.

2. Select **"Custom Receiver"** as the application type.

3. Fill in the form:

   | Field | Value |
   |-------|-------|
   | **Name** | `Radio Console Direct Channel` (or any name you like) |
   | **Receiver Application URL** | The HTTPS URL from Step 2 (e.g., `https://yourusername.github.io/RTest/receiver-direct-channel.html`) |

4. Click **"Save"**.

5. You'll be taken back to the application list. Find your new application and
   note the **Application ID** — it looks like a hex string, e.g., `A1B2C3D4`.

   **This is the critical value you'll configure in Radio Console.**

> **Important:** After creating the application, it can take **up to 15 minutes**
> for the registration to propagate to Cast devices. If your device shows
> "Application not found" errors, wait and try again.

---

## 4. Register Your Cast Devices for Development

While your app is unpublished (development mode), only **registered devices**
can run it. You must add your Cast devices to the developer console.

### 4a. Find Your Device Serial Number

1. In the **Google Cast SDK Developer Console**, click **"Add New Device"**.

2. You need the device's **serial number**. Find it using one of these methods:

   - **Google Home app:** Open the Google Home app → tap the device →
     Settings (gear icon) → scroll to "Cast firmware" → the serial number
     is listed.
   - **Physical device:** For Chromecast dongles, the serial number is
     printed on the device itself.
   - **Chromecast Audio:** Check the bottom of the device.
   - **Smart speakers/displays:** Google Home app is the easiest way.

### 4b. Register the Device

1. Enter the **serial number** and a **description** (e.g., "Living Room
   Chromecast Audio").

2. Click **"Register"**.

3. **Reboot the Cast device** — it must be rebooted after registration for
   the change to take effect. You can reboot from the Google Home app
   (device Settings → three-dot menu → Reboot) or by unplugging and
   replugging the device.

4. Wait **~5 minutes** after the reboot for the device to re-register with
   Google's servers and pick up its developer status.

> **Tip:** You can register multiple devices. Each one needs its own serial
> number entry and reboot.

---

## 5. Configure Radio Console

Edit `src/Radio.API/appsettings.json` (or the deployed copy at
`/opt/radio-console/api/appsettings.json`):

```json
{
  "AudioOutput": {
    "GoogleCast": {
      "Enabled": true,
      "ApplicationId": "A1B2C3D4",
      "StreamingMode": "DirectChannel",
      "DirectChannelChunkSizeMs": 100,
      "DirectChannelNamespace": "urn:x-cast:com.radioconsole.audio",
      "DiscoveryTimeoutSeconds": 10,
      "PreferredDeviceName": "AudioCast1",
      "DefaultVolume": 0.7,
      "AutoReconnect": true,
      "ReconnectDelaySeconds": 5
    }
  }
}
```

### Configuration Reference

| Setting | Value | Notes |
|---------|-------|-------|
| `ApplicationId` | Your app ID from Step 3 | **Required change** — replace `CC1AD845` |
| `StreamingMode` | `"DirectChannel"` | **Required change** — enables the new pipeline |
| `DirectChannelChunkSizeMs` | `100` | Optional. Range: 50-200ms. Lower = less latency, more messages/sec |
| `DirectChannelNamespace` | `"urn:x-cast:com.radioconsole.audio"` | Only change if you modified the receiver HTML to use a different namespace |

### Chunk Size Tuning

| Chunk Size | Latency | Messages/sec | Base64 per message | CPU overhead |
|------------|---------|--------------|--------------------|----|
| 50ms | Lowest | 20 | ~12.8KB | Highest |
| 100ms | Balanced | 10 | ~25.6KB | Moderate |
| 200ms | Highest | 5 | ~51.2KB | Lowest |

Start with 100ms (the default). If audio glitches, try 200ms. If you want
minimum latency and your network is solid, try 50ms.

---

## 6. Deploy and Verify

### 6a. Deploy

From your Windows development machine:

```powershell
# Raspberry Pi
./deploy/Deploy-ToPi.ps1

# Ubuntu x64
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
```

### 6b. Verify Startup Logs

Check that DirectChannel mode activated correctly:

```bash
# On the target machine
sudo journalctl -u radio-api.service -n 50 --no-pager
```

You should see these key log messages:

```
DirectChannel mode: skipping HTTP stream activation
Cast: Audio engine set for DirectChannel streaming
DirectChannel mode: audio engine wired to Cast output
```

If you see `HTTP Stream output activated on startup` instead, the config
change didn't take effect — double-check `appsettings.json`.

### 6c. Connect to a Cast Device

Use the Web UI at `http://<host>:5002` and select the Google Cast output,
then choose your registered Cast device.

Or use the API directly:

```bash
# Discover devices
curl http://<host>:5000/api/devices/cast

# Connect (replace with your device's details)
curl -X POST http://<host>:5000/api/devices/cast/connect \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "https://192.168.86.52/",
    "name": "AudioCast1",
    "ipAddress": "192.168.86.52",
    "port": 443,
    "model": "Chromecast Audio"
  }'
```

### 6d. Verify Streaming

Check the logs for the full DirectChannel startup sequence:

```
Starting Google Cast output (mode: DirectChannel)
Cast: Receiver launched on <DeviceName>
Cast: Starting DirectChannel streaming — transport: <id>, namespace: urn:x-cast:com.radioconsole.audio, chunk: 100ms
DirectCast: Streaming loop started — 100ms chunks = 19200 bytes PCM, 10 msgs/sec target
Google Cast output started streaming to <DeviceName> (mode: DirectChannel)
```

Verify data flow via the diagnostics endpoint:

```bash
curl http://<host>:5000/api/devices/cast/diagnostics
```

Check that:
- `outputTap.activeReaderCount` is `1` (the DirectChannel reader)
- `cast.state` is `"Streaming"`
- `httpStream.state` is `"Created"` (not started — correct for DirectChannel)

### 6e. Start Playing Audio

Select a file or source in the Web UI. Audio should now play through the Cast
device with significantly less latency than the HTTP MP3 path.

---

## 7. Troubleshooting

### "Application not found" or receiver doesn't load

| Cause | Fix |
|-------|-----|
| App ID is wrong | Double-check the Application ID in appsettings.json matches the Cast Developer Console |
| Registration hasn't propagated | Wait 15 minutes after creating the app, then try again |
| Device not registered for dev | Add the device serial number in the Developer Console and reboot it |
| Device not rebooted | Reboot the Cast device after registering it |
| Receiver URL not HTTPS | The URL must be HTTPS — Cast devices refuse HTTP receiver URLs |
| Receiver URL not accessible | Open the URL in a browser and confirm it loads |

### Streaming starts but no audio plays

| Cause | Fix |
|-------|-----|
| Default Media Receiver in use | Make sure `ApplicationId` is your custom app ID, not `CC1AD845` |
| Namespace mismatch | Verify `DirectChannelNamespace` in appsettings.json matches the `NAMESPACE` constant in receiver-direct-channel.html |
| AudioContext suspended | The receiver auto-resumes; check Chrome DevTools on the Cast device (see below) |
| No audio source active | Start playing a file or other audio source in Radio Console |

### How to debug the receiver

You can inspect the Cast receiver's console output via Chrome DevTools:

1. On a computer on the same network as the Cast device, open Chrome.
2. Navigate to `chrome://inspect/#devices`.
3. Wait for your Cast device to appear under "Remote Target".
4. Click **"inspect"** next to the receiver page.
5. The DevTools console will show chunk decode times, scheduling info, and
   any errors.

### Audio glitches or dropouts

| Cause | Fix |
|-------|-----|
| Wi-Fi congestion | Move the Cast device closer to the router, or reduce chunk rate by increasing `DirectChannelChunkSizeMs` to 200 |
| CPU overload on Cast device | Increase chunk size to reduce decode frequency |
| Buffer underrun | The receiver auto-recovers with a 50ms cushion; persistent underruns suggest network issues |

### "Cast: Could not get transport ID" — falls back to HttpMp3

This means `LaunchApplicationAsync()` succeeded but didn't return application
status with a transport ID. This can happen if:
- The Cast device is slow to respond
- The app ID is invalid
- Network timeout during app launch

Try: increase the post-launch delay, verify the app ID, check network
connectivity to the Cast device.

### Logs show send errors

The streaming service logs send errors at Warning level. Common causes:
- Cast device disconnected mid-stream
- Network interruption
- Cast protocol message size exceeded (shouldn't happen with default settings)

The service automatically backs off on errors (100ms delay) and continues
retrying. If errors are persistent, the Cast connection may need to be
re-established.

---

## 8. Switching Back to HTTP Mode

To revert to the standard HTTP MP3 streaming:

1. Edit `appsettings.json`:
   ```json
   "StreamingMode": "HttpMp3",
   "ApplicationId": "CC1AD845"
   ```

2. Redeploy:
   ```powershell
   ./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
   ```

3. Verify by checking logs for:
   ```
   HTTP Stream output activated on startup
   ```

The `ApplicationId` can be either `CC1AD845` (Google's Default Media Receiver)
or your custom app ID — the Default Media Receiver works fine for HTTP MP3
streaming. But if you want now-playing metadata displayed on Google Home
displays, keep using the custom receiver.

---

## Quick Reference

```
Receiver HTML:     docs/receiver-direct-channel.html
Architecture doc:  design/direct-cast-channel.md
Config file:       src/Radio.API/appsettings.json → AudioOutput.GoogleCast
Cast Dev Console:  https://cast.google.com/publish/
Diagnostics API:   GET /api/devices/cast/diagnostics
Cast connect API:  POST /api/devices/cast/connect
```
