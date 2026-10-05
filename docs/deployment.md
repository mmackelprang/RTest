# Deployment

This page is a condensed overview of how Radio Console is deployed. The full operational notes, including the
history behind each rule, are in [`CLAUDE.md`](../CLAUDE.md) § Deployment. The step-by-step guide is
[`deploy/DEPLOYMENT.md`](../deploy/DEPLOYMENT.md). Rebuilding a bare box is covered by
[`deploy/provision/README.md`](../deploy/provision/README.md).

## The appliance

| | |
|---|---|
| Machine | Intel N100 mini-PC, `x86_64`, Ubuntu with GNOME 46 on Wayland (GDM auto-login) |
| Hostname | `radio` (SSH as `mmack@radio`; the host-specific SSH key is bound to the name, not the IP) |
| Display | 1920x720 touch panel running a kiosk Chrome |
| Runtime | `linux-x64`. A Raspberry Pi 5 (`linux-arm64`, through `Deploy-ToPi.ps1`) is a build target that has not been tested on hardware; see [known issues](known-issues-and-future-work.md) |
| Audio | PipeWire. Music Bluetooth runs on a TP-Link UB500 (`hci0`); voice Bluetooth on the Intel AX201 (`hci1`) belongs to RotaryPhone |

The box has limited resources, and heavy log reads correlate with audible distortion. Keep `journalctl` queries
bounded (`--since '-30min'`). WiFi (`wlp0s20f3`) is the only management link. `enp1s0` is a point-to-point
cable to the HT801 phone adapter; plug nothing else into it.

## Services

| Unit | Port | Role |
|---|---|---|
| `radio-api.service` | 5000 | Radio.API: the audio engine, all hardware, REST and SignalR |
| `radio-web.service` | 5002 | Radio.Web: the Blazor UI (depends on the API) |
| `radio-kiosk` (transient **user** unit) | — | The kiosk Chrome, started by `/usr/local/bin/radio-kiosk-launch` |

Both services share `/opt/radio-console/{api,web,data,logs}`. Per-machine settings go in
`appsettings.Production.json`. The deploy leaves an installed copy alone, so values in
`deploy/*/appsettings.Production.json` seed a new box but do not reach an existing one. The SQLite configuration
store outranks both JSON layers.

The kiosk's command line is defined in one place:
[`deploy/debian-x64/kiosk/bin/radio-kiosk-launch`](../deploy/debian-x64/kiosk/bin/radio-kiosk-launch), installed by
`setup-kiosk.sh`. It runs on a dedicated profile (`~/.config/radio-kiosk-chrome`) with
`--password-store=basic`, and exposes CDP on `:9223`. Never use `pkill -f chrome`: the Google Voice bridge
Chrome on the same box must survive. `radio-kiosk-exit` matches on the kiosk profile path for that reason.

## Deploying

From a dev host with PowerShell 7:

```powershell
./deploy/Deploy-ToLinux.ps1               # defaults: -TargetHost radio -Runtime linux-x64
./deploy/Deploy-ToPi.ps1                  # untested Raspberry Pi target: -TargetHost piradio, linux-arm64
./deploy/Deploy-ToLinux.ps1 -VerifyOnly   # box-side SHA check only; no build, stop or sync
```

The script builds, proves the transport (`-Transport rsync|scp|auto`) before stopping anything, stops the
kiosk and services, syncs, restarts, and then verifies on the box:
- **Both services report the expected git SHA** at `/api/health/version`. A mismatch exits 1 and leaves the
  kiosk down deliberately.
- **The kiosk is live:** it has established connections to `:5002`. A warning here means the binaries landed
  but the browser did not come back. Recover with `ssh mmack@radio '/usr/local/bin/radio-kiosk-launch'`.

Read the verdict line rather than the exit code alone. A red exit does not imply a dark panel, and a live
panel does not imply a verified deploy.

## Verifying

```bash
curl -s http://radio:5000/api/health/version   # API: gitSha
curl -s http://radio:5002/api/health/version   # Web: gitSha
git log --oneline -1 origin/main               # these must match before anyone runs UAT
curl -sI http://radio:5002/css/design-system.css | grep -i cache-control   # no-cache
```

**Merged is not deployed.** Check the deployed SHA before a person runs acceptance tests.

## Logs

- `journalctl -u radio-api` carries **Warning and above only**. Information-level lines go to the file sink
  under `/opt/radio-console/logs/`.
- `radio-web`'s console sink is not level-restricted, so treat anything it logs at Information as persistent.
- Log levels on `radio-api` can be changed at runtime without a restart (the change is not persisted):

```bash
curl -s http://radio:5000/api/system/logging/levels
curl -s -X PUT http://radio:5000/api/system/logging/levels/Radio.Infrastructure.Audio \
  -H 'Content-Type: application/json' -d '{"level":"Information"}'
curl -s -X POST http://radio:5000/api/system/logging/levels/reset
```

## Bluetooth and audio boundary

Radio Console and RotaryPhone share the box. Before you change anything in Bluetooth, PipeWire or WirePlumber,
read the cross-service boundary document referenced at the top of [`CLAUDE.md`](../CLAUDE.md). It records which
adapter, profiles and WirePlumber configs each service owns. Those configs are kept in
[`deploy/common/`](../deploy/common/).
