# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Cross-Service Boundary (IMPORTANT)

This service shares the Ubuntu box (`radio`) with RotaryPhone. **Read before any BT/audio work:**

**`D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`** — Defines which BT adapter, profiles, and WirePlumber configs each service owns. Violating these boundaries will break the other service's audio.

Key rules:
- Radio Console owns **TP-Link UB500** (`hci0`, `78:20:51:F5:FB:A7`) for music/A2DP
- RotaryPhone owns **Intel AX201** (`hci1`, `10:91:D1:FE:00:46`) for voice/HFP
- Radio Console manages all `/etc/wireplumber/bluetooth.lua.d/` configs
- Always `bluetoothctl select 78:20:51:F5:FB:A7` before any bluetoothctl commands
- If you need to change any boundary, update the boundary doc first

To request changes from the RotaryPhone session, update the boundary doc's Change Log and optionally create a prompt file at `D:\prj\RotaryPhone\docs\prompts/`. See the boundary doc's "Passing Work Between Sessions" section for the full protocol.

## Project Overview

**Grandpa Anderson's Console Radio Remade** - A modern audio command center restoring vintage console radio functionality with modern capabilities (Bluetooth A2DP, streaming, smart home events, Chromecast audio).

**Target Platform:** Raspberry Pi 5 (Linux) with Windows development support
**Stack:** .NET 10, ASP.NET Core, Blazor Server, SoundFlow audio engine, SQLite/JSON config

## Build & Test Commands

```bash
# Build
dotnet build --configuration Release

# Run all tests
dotnet test --configuration Release --verbosity normal

# Run single test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Run API server (Swagger at http://localhost:5000/swagger)
dotnet run --project src/Radio.API

# Run Web UI (http://localhost:5002)
dotnet run --project src/Radio.Web

# Run audio UAT tool
dotnet run --project tools/Radio.Tools.AudioUAT

# Deploy to Pi (from Windows)
./deploy/Deploy-ToPi.ps1 -PiHost piradio -PiUser radio
```

### ⚠ Never pipe `dotnet test` into `tail` — it swallows the exit code

**Measured 2026-09-05: `dotnet test ... | tail` exited `0` while five tests were failing.** In a
pipeline the shell reports the exit status of the *last* command, and `tail` succeeds at tailing
whatever it was handed — including the output of a failed run. The same is true of `head`, `grep`,
and `cat`. Anything that reads a gate's result from `$?` or `$LASTEXITCODE` after a pipe is reading
`tail`'s opinion, not the suite's.

This matters more here than it looks, because the suite is large enough that piping to `tail` is the
natural reflex: a full run prints thousands of lines and only the last few are the summary.

```bash
# WRONG — always "passes"
dotnet test RadioConsole.sln -c Release | tail -30

# Right — keep the exit code, then read the file
dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log
```

Read the **per-project summary lines** (`Passed! - Failed: 0, Passed: 141, ...`), one per test
project — a single missed `Failed: 4` is the whole point of the gate. Known-failing on Windows and
not a regression: four `SrcVariableResamplerTests` (`libsamplerate.so.0`, `TEST-5`),
`NwsObservationIntegrationTests.RealNwsCall_*` (live network, `Category=Integration`, CI-excluded),
and `CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording` (added
2026-09-06 — same shape: `Category=Integration`, live Cover Art Archive API, fails on a 15 s HTTP
timeout, passes on re-run, and excluded by `build.yml:58`'s `Category!=Integration` filter, so CI
never runs it).

⚠ **The Release build baseline is 47 warnings, 0 errors** — measured on `main` 2026-09-06 while
shipping `OPS-2`, with an identical warning histogram (all `IDE0011`) before and after the change.
Earlier notes citing **53** are stale. The number matters because the gate is *equality with the
baseline*, not an absolute: a Builder holding the wrong figure either waves through four new
warnings or chases four that were never there.

## Solution Structure

```
RadioConsole.sln
├── src/Radio.Core              # Domain interfaces, models, events (no dependencies)
├── src/Radio.Infrastructure    # Audio engine, BT, Cast, sources, outputs, DI wiring
│   ├── Audio/SoundFlow/        # Engine, mixer, device manager, tapped output stream
│   ├── Audio/Sources/          # Primary (Radio, BT, File, Vinyl, USB) + Event (TTS, AudioFile)
│   ├── Audio/Outputs/          # Local, GoogleCast, HttpStream
│   ├── Audio/Fingerprinting/   # SoundFlowAudioTap, FingerprintDbContext (12-table SQLite)
│   ├── Platform/Bluetooth/     # Linux (BlueZ D-Bus) + Windows (WinRT)
│   └── Configuration/          # DeviceOptionsResolver, PreferencesPersistence (Radio-specific)
├── src/Radio.Configuration     # Standalone NuGet: JSON/SQLite stores, secrets, backup, bridge
├── src/Radio.Fingerprinting    # Standalone NuGet: SongRec, MusicBrainz, background ID, repos
├── src/Radio.Metrics           # Standalone NuGet: time-series metrics collection + SQLite storage
├── src/Radio.AudioAnalysis     # Standalone NuGet: waveform comparison, THD, silence detection
├── src/RTLSDRCore              # Standalone NuGet: RTL-SDR software-defined radio library
├── src/Radio.API               # REST controllers, SignalR hubs, middleware
├── src/Radio.Web               # Blazor Server UI (Radzen.Blazor)
├── tests/                      # 10 xUnit test projects (~1,416 tests)
│   ├── Radio.Metrics.Tests         # Metrics package tests
│   ├── Radio.Configuration.Tests   # Configuration package tests
│   ├── Radio.Fingerprinting.Tests  # Fingerprinting package tests
│   ├── RTLSDRCore.Tests            # RTL-SDR package tests
│   ├── Radio.AudioAnalysis.Tests   # Audio analysis package tests
│   ├── Radio.Core.Tests            # Core domain tests
│   ├── Radio.Infrastructure.Tests  # Infrastructure integration tests
│   ├── Radio.API.Tests             # API controller tests
│   ├── Radio.Web.Tests             # Web UI component tests
│   └── Radio.IntegrationTests      # Cross-cutting integration tests
├── tools/                      # AudioUAT, ConfigurationManager CLIs
├── deploy/                     # Pi deployment scripts, systemd services
├── design/                     # Architecture docs, decision log, work log
└── RaddyRF320BT/               # Git submodule for vintage radio protocol
```

## Architecture

**Layered Architecture:**
- **Core** - Pure domain (interfaces: IAudioEngine, IAudioSource, IConfigurationStore, IBluetoothService, etc.)
- **Extracted Libraries** - Standalone NuGet packages: Configuration, Fingerprinting, Metrics, AudioAnalysis, RTLSDRCore
- **Infrastructure** - SoundFlow wrapper, device management, outputs (Local/Cast/HTTP), sources (Radio/SDR/File/BT/TTS), Bluetooth (Linux BlueZ + Windows WinRT), DI wiring
- **API** - REST endpoints under `/api/*`, SignalR hubs at `/hubs/visualization` and `/hubs/audio`
- **Web** - Blazor Server UI, 12 pages, shared components, SignalR client

**Key Patterns:**
- Constructor-based dependency injection
- Dual config stores (SQLite/JSON) switchable via appsettings.json
- Encrypted secrets with tag substitution: `${secret:identifier}`
- Audio ducking with priority system (1-10 scale)
- Multi-target framework: `net10.0` (Linux) + `net10.0-windows10.0.19041.0` (WinRT BT)
- Extracted libraries packable as NuGet (`pack-local.ps1`)

**Audio Pipeline:**
```
Sources (Radio/SDR/File/BT/TTS) → Master Mixer → Modifiers (Balance, FingerprintTap, Viz)
                                                ↓
                                     Playback Device (local speakers)
                                     TappedOutputStream → HTTP Stream → Google Cast
                                     TappedOutputStream → Visualization (FFT/Levels/Waveform)
```

## API Endpoints

- `/api/audio` - Volume, mute, playback state, now playing
- `/api/sources` - Switch audio sources, TTS engines/voices
- `/api/devices` - Enumerate input/output devices, Cast discovery/connection
- `/api/radio` - Tuner controls, presets, band selection
- `/api/bluetooth` - BT pairing, discovery, AVRCP controls
- `/api/queue` - Queue management, reordering
- `/api/files` - File browsing, playback
- `/api/playhistory` - Play history with search
- `/api/configuration` - Config CRUD, import/export
- `/stream/audio` - Raw PCM audio stream (16-bit, stereo, 48kHz)
- `/stream/audio/mp3` - MP3 stream (for Google Cast)

## Deployment

### Reaching the box

**Use `mmack@radio` for SSH from WSL. Do NOT use the bare IP.**

```bash
ssh mmack@radio
```

`radio` resolves fine from WSL and is the working form (verified 2026-08-10) — `radio` →
`radio.lan` → `192.168.86.50`. The bare IP **fails** — `mmack@192.168.86.50` gives
`Permission denied (publickey,password)`. The login user is **`mmack`**, not `radio`.

*Why*, so this stops getting rediscovered: `~/.ssh/config` has a `Host radio radio.local` block
that supplies `IdentityFile ~/.ssh/id_ed25519_radio` together with `IdentitiesOnly yes`.
Connecting by IP does not match that block, so the correct key is never offered and
`IdentitiesOnly` suppresses every other key — hence the instant rejection. It is the *SSH
identity* that is hostname-bound, not DNS. The IP (`192.168.86.50`) is still accurate as
*reference* information (`curl`, browser, and `ssh -i ~/.ssh/id_ed25519_radio
mmack@192.168.86.50` all work), it just must not be the default form for SSH.

> An earlier revision of this note claimed the opposite — that `radio`/`piradio` do not resolve
> from WSL and the IP must be used. That was wrong and cost several sessions time. Six
> independent checks confirm `mmack@radio` works.

The in-app service URLs (`http://radio:5004`, etc.) resolve *on* the box and should not be changed.

### What the box actually is

**An Intel N100, `x86_64`, running Ubuntu + GNOME 46 on Wayland** (GDM3 with auto-login; `loginctl`
session 1, seat0). Not a Raspberry Pi — the project *targets* Pi/ARM64 as well, but the deployed
appliance is x64, and several traps below follow from that.

✅ **Fixed by `OPS-1` (2026-09-01).** `Deploy-ToLinux.ps1` now defaults to `-Runtime linux-x64` and
`-TargetHost radio`, so the documented invocation targets this box correctly and no longer needs the
flags spelled out. **`Deploy-ToPi.ps1` now passes `-TargetHost piradio` explicitly** — it always passed
`-Runtime linux-arm64` but never a host, so flipping the shared default without fixing the wrapper
would have started shipping ARM64 binaries to this x64 box, which is worse than the bug it replaced.
*Previously:* the default was `linux-arm64` against an x86_64 appliance, so the literal documented
invocation shipped ARM binaries here.

⚠ **The box is resource-constrained and on WiFi.** Heavy `journalctl` reads correlate with audio
distortion — always bound queries (`--since '-30min'`) and never tail. `enp1s0` is unavailable, so
WiFi is the only link; do not restart anything that could drop it while nobody is physically present.

### Services

- `radio-api.service` — Radio.API on port 5000 (audio engine, BT, all hardware)
- `radio-web.service` — Radio.Web on port 5002 (Blazor UI, depends on API)
- Shared: `/opt/radio-console/{api,web,data,logs}`

**`radio-kiosk.service` is a *transient user* unit — there is no unit file to find.** It is created
on the fly by `systemd-run --user --collect --unit=radio-kiosk` inside
`/usr/local/bin/radio-kiosk-launch`, so it lives only while the kiosk Chrome does. Query it with
`systemctl --user status radio-kiosk` (note `--user`; `sudo systemctl status radio-kiosk` will
report it does not exist, which is correct and not a fault). Starting it that way is the point:
the transient unit is created by the *graphical session's own* service manager and therefore
inherits that session's `WAYLAND_DISPLAY` / `DBUS_SESSION_BUS_ADDRESS` / `XDG_RUNTIME_DIR`, which
a detached SSH shell does not have.

**The kiosk command line has exactly one definition:** `deploy/debian-x64/kiosk/bin/radio-kiosk-launch`,
installed to `/usr/local/bin/` by `deploy/debian-x64/kiosk/setup-kiosk.sh`. The autostart entry and
`Deploy-ToLinux.ps1` both call it. Change flags there and nowhere else — three callers carrying
three different flag sets is how the box drifted in the first place. The `~/Desktop` entries and
the helper scripts also come from that directory: **do not hand-edit `~/Desktop`**, re-run
`setup-kiosk.sh` from a checkout instead. Since 2026-08-18 that directory is also the source of
truth for the desktop icon assets, the dialogs' GTK touch override, and four helper scripts —
`radio-kiosk-launch`, `radio-kiosk-exit`, `radio-console-open` (the Radio Console icon: probe,
start what is down, then open the kiosk) and `radio-shutdown-confirm`.

### ⚠ Kiosk Chrome must pass `--password-store=basic`

GDM auto-login never unlocks the login keyring. Without this flag Chrome asks gnome-keyring for it and
gnome-shell raises a modal **"Authentication required"** dialog that **grabs input and covers the
panel**. On 2026-08-02 this blocked the kiosk for ~33 hours, and the failure was worse than a dialog on
top of the UI — Chrome never reached navigation at all (0 connections to `:5002`, 0 renderers) while
`radio-web` returned 200 the whole time. Every launch path carries the flag; keep it that way. Since
2026-08-18 the boot path and the deploy relaunch both get it from the single definition in
`radio-kiosk-launch`, so there is one place to keep right rather than three.

**Do not apply the same flag to a browser whose profile already holds `v11` cookies.** `v11` cookies are
encrypted with the keyring-derived key, and `basic` makes that key unobtainable, so Chrome **discards
them** — measured live at 45 `v11` → 16 `v10`, destroying the Google Voice session. It is only safe on a
profile that was `basic` from first run, or paired with a planned re-login.

The kiosk is now the worked example of the safe case. Since 2026-08-18 it runs on its own
`--user-data-dir=~/.config/radio-kiosk-chrome`, created `basic` from first run and only ever
pointed at `localhost:5002` — it holds no Google session to lose. The **Google Voice bridge**
Chrome (`~/.config/gv-bridge-chrome`) is the profile the warning above is about: it holds the only
authenticated Google session on the box. Never point `--password-store=basic` at it, and never
widen a kill to `pkill -f chrome` — `radio-kiosk-exit` matches on the kiosk profile path precisely
so the bridge survives.

**The real fix is PAM auto-unlock or an empty-password login keyring** (needs physical access) — that
resolves every browser at once and costs no session. Full write-up:
`docs/uat/2026-08-03-osk-wayland-viability/REPORT.md`.

### Remote UI driving: CDP is back, AT-SPI works, screen capture is not

**Kiosk CDP on `:9223` works again as of 2026-08-18.** Chrome ≥136 silently ignores
`--remote-debugging-port` on the *default* user-data-dir, and this box runs Chrome 151 — so the flag
was inert for as long as the kiosk shared that profile. The kiosk now launches with
`--user-data-dir=~/.config/radio-kiosk-chrome`, which is the documented fix, and
`curl -sf http://localhost:9223/json/version` returns the browser version JSON. The Google Voice
bridge keeps its own CDP on **`:9224`**; the two ports are deliberately distinct, and driving the
bridge is RotaryPhone's business, not this repo's.

**`radio-refresh-browser` is still broken, and the dedicated profile does not fix it.** It fails for
a different reason: it drives `xdotool`, which is X11-only and cannot see a native Wayland window.
Nothing calls it — the deploy stops and relaunches the kiosk itself. Re-implementing it over the
restored CDP would work, but nothing has done that yet.

For screen capture, Shell's screenshot API and `GetWindows` are `AccessDenied` on GNOME 46, and
`gnome-screenshot`/`grim`/`scrot` are absent. The working route is
`org.gnome.Mutter.ScreenCast` → `RecordMonitor` → PipeWire → `gst-launch-1.0 pipewiresrc`. **Mutter only
emits buffers on damage**, so a static screen starves the stream — force damage, and validate the
instrument before trusting a "nothing changed" result.

**But a screenshot is often not what you needed. AT-SPI works here, and it answers most UI questions
directly** (established 2026-08-18 while verifying `KIOSK-2`). `python3-gi` + `Atspi` are installed,
and the accessibility bus exposes, for any GTK app and for Chrome:

- the **widget tree** — roles, names, and nesting;
- the **rendered text** of every label, which is how dialog copy gets checked without seeing it;
- **screen extents in pixels**, which is how the kiosk dialogs' 58 px buttons were measured;
- a working **`click` action**, so buttons can actually be pressed — that is how `Try again` /
  `Open anyway` / `Cancel` were exercised end to end;
- window **states**, including `ACTIVE`, which is a usable proxy for stacking: a dialog mapping over
  the fullscreen kiosk takes `ACTIVE` off it.

Chrome exposes its tree because the kiosk flag set includes `--force-renderer-accessibility`. Prefer
this over the ScreenCast route whenever the question is *"what does it say / how big is it / does the
button work"* rather than *"what does it look like"*. It needs the graphical session's environment —
`eval "$(systemctl --user show-environment | grep -E 'WAYLAND_DISPLAY|XDG_RUNTIME_DIR|DBUS_SESSION_BUS_ADDRESS' | sed 's/^/export /')"`.

**`--window-position` is a no-op under Wayland.** Windows described elsewhere as "off-screen" are not;
what makes one visible is stacking order, i.e. whichever browser restarted most recently.

**⚠ Since `LOG-11` (2026-09-02), `journalctl -u radio-api` only carries WARNING and above. ⚠ This is
true of `radio-api` ONLY — read the `radio-web` note below before concluding an `Information` line is
safe from the journal.** The API's console sink is level-restricted, and under systemd the console *is*
the journal — so its `Information` lines no longer appear there. They go to the file sink instead. This
changes how the box gets triaged: a startup sequence you expect to see in `journalctl -u radio-api` will
look like it never happened.

⚠ **`radio-web` did NOT get the same treatment, and the asymmetry is documented nowhere else.**
`src/Radio.Web/appsettings.json`'s `Serilog` block sets `MinimumLevel.Default: "Information"`, and its
Console sink carries **no `restrictedToMinimumLevel`** — that string appears nowhere in the file. So
**every `Information` line in `Radio.Web` is a journald line**, on a box where log volume correlates with
audible audio distortion. Measured 2026-09-05 while planning `PHN-5`, the row that found it:
`src/Radio.Web/Services/Hub/PhoneHubService.cs:82` logs
`LogInformation("Incoming call from {PhoneNumber}", …)`, so a raw phone number reaches
`journalctl -u radio-web` on every incoming call, on a stock box. **Treat any `Information` line you add
to `Radio.Web` as public and persistent until that sink is restricted.**

```bash
# Warnings and errors — journald
ssh mmack@radio "journalctl -u radio-api --since '-30min' --no-pager | tail -50"

# Information detail — the file sink, which is where startup/device/source lines now live
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); tail -100 $F'
```

The split is deliberate: every line used to be stored twice, once in the journal and once in the file, on
a box where log volume correlates with audible audio distortion.

**Verifying a deploy actually landed.** ✅ **Closed by `OPS-1` (2026-09-01) — both services are now
verified by SHA.** `Radio.Web` serves `/api/health/version` on **port 5002**, the twin of the API's on
5000, and `Deploy-ToLinux.ps1` polls both and `exit 1`s on a mismatch. The SHA parsing behind both is
one implementation, `Radio.Core.Utilities.AssemblyBuildInfo` — two copies would be two chances for the
services to derive a version differently and quietly pass a check that should fail.

```bash
curl -s http://radio:5000/api/health/version   # API  — gitSha, assemblyName "Radio.API"
curl -s http://radio:5002/api/health/version   # Web  — gitSha, assemblyName "Radio.Web"
```

*Previously:* `radio-web` was checked only with `systemctl is-active`, which is exactly as true of a
stale binary as a fresh one, and the interim gate was grepping the deployed binary for a branch-only
symbol (`grep -ac <symbol> /opt/radio-console/web/Radio.Web`). That workaround is no longer needed.

**That SHA guarantee covered the binaries and, until `OPS-5` (2026-09-02), nothing else.** It reports
which assembly is running, which is a true statement that says nothing about the bytes a browser is
painting. `Radio.Web` sent `ETag` and `Last-Modified` on every static asset but **no `Cache-Control`
at all**, so browsers applied heuristic freshness and reused `css/`, `js/`, fonts and the Radzen theme
without revalidating. A CSS-only change could land, pass the SHA check on both services, and still not
be on the panel — measured on 2026-09-02, when the kiosk painted a `design-system.css` that predated a
deploy earlier the same day. Restarting the browser is not itself a fix: Chrome's HTTP cache lives in
the profile directory and outlives the process. The deploy separately deletes that directory for the
kiosk profile (since 2026-08-18) — which is one profile on one box, does nothing for a laptop or tablet
on the LAN, and nothing at all under `-NoRestart`. It is still there; it is just no longer the thing
correctness depends on.
`OPS-5` set `Cache-Control: no-cache` on everything `UseStaticFiles` serves — revalidate before reuse,
which the existing `ETag` answers with a `304` and no body. **Deployed and displayed are now the same
thing**, and a styling change that looks like it "didn't work" is once again a reason to suspect the
CSS rather than the cache. `Radio.API` serves no static files, so it needed no equivalent.

```bash
curl -sI http://radio:5002/css/design-system.css | grep -i cache-control   # cache-control: no-cache
```

**The deploy now also verifies the kiosk itself, and it checks liveness rather than existence.**
After relaunching, `Deploy-ToLinux.ps1` polls for up to 20s and prints either
`Kiosk is live (N established connections to :5002)` in green, or
`WARNING: 0 established connections to :5002 - the kiosk did not reach the UI.` in red. The warning
means the **binaries landed and verified fine** — what failed is the browser coming back; the deploy
does not exit non-zero for it. Established connections are the check because process existence is
not: during the 2026-08-02 outage Chrome was running and `radio-web` returned 200 for ~33 hours
while the connection count was **0**. Recover with
`ssh mmack@radio '/usr/local/bin/radio-kiosk-launch'`, and diagnose with
`ssh mmack@radio 'systemctl --user status radio-kiosk.service'`.

**One-time cutover, already done on `radio` 2026-08-18 but needed on any box still running the
old kiosk.** `radio-kiosk-exit` matches the kiosk *by profile path*, so it deliberately does not
match a kiosk started the old way on the default profile — that Chrome must be stopped once by
hand (`kill` its PID; **never** `pkill -f chrome`, which takes the Google Voice bridge with it).
Until it is, a deploy will relaunch on the new profile and leave two kiosks stacked on screen.

**`Deploy-ToLinux.ps1` calls `/usr/local/bin/radio-kiosk-exit` and `/usr/local/bin/radio-kiosk-launch`,
which `setup-kiosk.sh` installs.** On a box that has never run that installer the deploy prints a
`WARNING: ... is missing` line and skips the kiosk stop/relaunch rather than falling back to a
broader `pkill` — a wider match would take the Google Voice bridge with it.

## Cross-Platform Requirements

Code must run on Raspberry Pi (Linux). Avoid:
- Windows-only APIs (WPF, WinForms) outside `#if WINDOWS_TARGET` guards
- Platform-specific paths without abstraction
- Libraries without Linux/ARM64 support

Use: System.Device.Gpio, SoundFlow (MiniAudio), cross-platform .NET APIs
Exception: WinRT BT APIs and NAudio WASAPI are Windows-only behind conditional compilation.

## Code Style

- 2-space indentation (EditorConfig enforced)
- File-scoped namespaces
- Nullable reference types enabled
- Warnings as errors in Release builds
- Comment internal logic, edge cases, protocol details
- Explicit type annotations preferred

## Test Timing — never race a wall clock against a wall clock

**A test must not depend on production code's own timer firing inside a `Task.Delay` the test
performs.** There is no rendezvous between those two clocks, so the assertion holds only while the
machine is fast enough — which is why this shape passes locally and fails on CI, where the runner is
`[self-hosted, linux, x64, appserver]` and shared.

**The rule:** if an assertion depends on the component under test having *observed* something,
synchronize on the observation, not on elapsed time. Count events; don't time them.

`BluetoothCaptureWatchdogTests` is the worked example (`TEST-4`). It set a value, slept 60 ms, and
assumed the watchdog's 20 ms poll loop had seen it. Under load one tick stretched past the entire
window, the value was never observed, and the test failed — **13/200 under CPU saturation**, and once
on `main` at `2a81f56`. The fix was **test-only**: the fake source now parks the watchdog on entry to
every poll until the test grants it, so polls are counted rather than timed and every assertion runs
while the component is frozen. That took the same 200 iterations at the same load to **200/200**. Read
that file's class-level `<remarks>` before writing a similar test.

**Prefer determinism over patience.** Raising a timeout or adding a sleep converts a flaky test into a
*slow* flaky test; the failure rate drops but never reaches zero, and the suite gets slower for it.
Where production code has no seam to drive it deterministically, the house idiom is an injectable
`TimeProvider` defaulting to `TimeProvider.System` (see `EncoderHudService`, and the note in
`AudioServiceExtensions.cs`) with `FakeTimeProvider` in the test. Note that advancing a fake clock is
only half the job when the callback is `async` — you still need a completion rendezvous before
asserting on what it wrote.

**Know which direction a timing dependency fails in.** It is dangerous only when starvation can flip
the assertion to *fail*. A bounded negative check that starvation can merely weaken — "no event
arrived within 200 ms" — is safe, and sometimes unavoidable. Say which one a test is, as
`DisabledByZeroThreshold_DoesNotRaise` does, rather than implying a determinism it does not have.

## Pre-Merge Review

Checks the reviewer runs on every PR, on top of the generic pass. Short list — these are the
failure modes this repo actually ships, not a general-purpose rubric.

- **Do comments, log messages, and XML docs assert only what the code actually does?** Flag any
  comment claiming an invariant the code does not enforce — *"this lock guards every access"*,
  *"X is the only code that does Y"*, *"if the state is already Z"* — and any log message
  describing an action stronger than what actually occurred. Where a comment states a
  **precondition** that makes unsynchronized or unguarded access safe, verify that precondition is
  still true *in this diff*.

  *Why, so this stops getting rediscovered:* the repo has shipped three such mismatches, two of
  which caused real bugs.
  1. `SoundFlowMasterMixer` logs *"Removed audio source … from mixer"* while only mutating a
     `List<IAudioSource>` — the real detach lives elsewhere. A later fix (`03a6fea`) trusted the
     wording, landed one layer too high, and silently did nothing for months.
  2. `BluetoothAudioSource` carried *"If source is already Playing … route to mixer now"* two
     lines below an assignment of `State = Ready`, making the `Playing` branch statically
     unreachable — BT song recognition was silently disabled (fixed in #469).
  3. `GoogleCastOutput._lifecycleLock` was documented as guarding *every* read/write of
     `_client` / `_connectedReceiver`; most reads were always outside it. Caught before it cost
     anything — but note *how*. The corrected comment's own first draft overclaimed in turn
     (*"never a null one"* — the field is in fact null before the first `InitializeAsync`), and
     only a reviewer briefed to actively **falsify** it caught that. Reviewing a comment for
     plausibility is not the same as checking it against the code.

  A wrong comment is worse than no comment: it survives the code it described, and the next
  engineer debugs the description instead of the behavior. When a comment offers a reason a thing
  is safe, the reason is the claim to check — not the conclusion.

## Database Paths

Configured via `appsettings.json` Database section:
- Configuration: `./data/config/configuration.db`
- Metrics: `./data/metrics/metrics.db`
- Fingerprints: `./data/fingerprints/fingerprints.db`
- Backups: `./data/backups/`
- Logs: `./logs/`
- Album art cache: `./data/albumart/`
