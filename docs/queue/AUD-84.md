# `AUD-84` — a Cast speaker that drops mid-stream crashes `radio-api`, restarting the whole console

[← Builder Queue index](../BUILDER_QUEUE.md)

🔴 **P0 — GA-blocking.** Filed 2026-09-30 from the owner's casting baseline run. **MEASURED on the box, not code-read.**

## Provenance

The owner ran a casting baseline at the console on 2026-09-30, box on `b64c8cd` (= `main`, both services SHA-verified that day). Record: [`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) § Casting baseline. Evidence below is from `journalctl -u radio-api` and `systemctl show` on `radio`, read the same afternoon.

Related punch-list row: **`AUD-37`** ([`HANDOFF-GA-PUNCH-LIST.md`](../HANDOFF-GA-PUNCH-LIST.md) §4.2), filed 2026-09-27 from a code review, which predicted that a Cast device dropping mid-stream would be *"never noticed"*. ⛔ **The measurement shows the prediction was wrong in the worse direction: the process dies instead.** `AUD-37`'s "notice and fall back" half remains open and is this row's natural sibling.

## What the owner saw

Owner, verbatim: *"The console did **not** notice when the speaker disconnected, but it did auto-reconnect and the previous volume. The console UI did appear to crash here, though- was there a depoyment? After restarting the UI, changing the volume on the console didn't affect the office speaker. Switching between speakers seems to work as expected."*

**There was no deployment.** The "UI crash" was `radio-api` crashing and taking `radio-web` with it.

## What happened (MEASURED, 2026-09-30, times EDT)

The Office speaker (a Google Home Mini) was casting Bluetooth audio in `DirectChannel` mode (the box's production mode, `deploy/debian-x64/appsettings.Production.json`). The owner unplugged it.

| Time | Event |
|---|---|
| 16:36:36 | `Unhandled exception. System.IO.IOException: Unable to write data to the transport connection: Broken pipe.` → `System.Net.Sockets.SocketException (32): Broken pipe` |
| | Stack: `Sharpcaster.ChromecastClient.SendAsync` ← `Sharpcaster.Channels.ChromecastChannel.SendAsync` ← `Sharpcaster.Channels.HeartbeatChannel.TimerElapsed(Object sender, ElapsedEventArgs e)` ← `Task.<>c.<ThrowAsync>` |
| 16:36:41 | `radio-api.service: Main process exited, code=dumped, status=6/ABRT` · `Failed with result 'core-dump'` |
| 16:36:51 | systemd `Scheduled restart job, restart counter is at 1` → `radio-api` restarted; `radio-web` restarted in the same second (dependency), so the kiosk's Blazor circuit dropped |
| 16:38:33 | Owner relaunched the kiosk |

The phone also had to reconnect: `Device "Pixel 10 Pro XL" did not reconnect within 5s`.

**The `Task.<>c.<ThrowAsync>` frame is the signature of an exception escaping an `async void` method** — here SharpCaster's `HeartbeatChannel.TimerElapsed`, a `System.Timers.Timer` handler. An exception from an `async void` is re-thrown on the thread pool, where nothing can catch it, and the runtime terminates the process.

⚠ **The "auto-reconnect at the previous volume" was NOT a reconnect.** It was the restarted process restoring the saved Cast output, with `AUD-80`'s remembered per-device volume. Nothing in the running process noticed the drop; the process simply died and a new one started with the saved preferences.

## Blast radius

Everything the console does, not only Cast:

- **Every audio source stops** — `radio-api` owns the audio engine, so Radio, Vinyl, file playback and Bluetooth all go silent for the restart (15 s from the exception at 16:36:36 to the restart at 16:36:51, plus start-up).
- **Bluetooth drops** — the phone disconnects and must reconnect.
- **The UI goes away** — `radio-web` restarts with it and the kiosk circuit is lost; on the box the owner had to relaunch the kiosk by hand.
- **systemd's restart budget is spent** — each drop is one restart; enough of them in a window and systemd stops restarting the unit (check `StartLimitBurst` / `StartLimitIntervalSec` for `radio-api` before reasoning about repeated drops).

A speaker losing power or Wi-Fi is ordinary household behaviour (a Chromecast auto-update, someone unplugging a speaker in another room), so this is reachable in normal use, with no operator error.

## Why the fix has to be on our side, and what cannot work

- **SharpCaster 3.0.0** (pinned at `src/Radio.Infrastructure/Radio.Infrastructure.csproj:33`) **is the latest version on NuGet.** No upgrade fixes it.
- ⛔ **A `try`/`catch` in our code cannot catch this.** The exception is thrown from an `async void` timer callback *inside the library*, on a thread-pool thread with no caller of ours on the stack. Wrapping our calls to SharpCaster changes nothing. `AppDomain.UnhandledException` observes the crash but cannot prevent termination.
- **The plan must choose a mechanism that keeps the heartbeat from ever sending on a dead socket, or keeps the library's heartbeat from running at all.** Candidate shapes, for the plan to evaluate rather than a decision here: own or replace the heartbeat (disable SharpCaster's timer and ping from our own loop, where failures are caught); or detect the dead connection first and tear the client down (stopping its heartbeat timer) before the next tick fires. Whether SharpCaster's timer can be stopped or replaced from outside must be established from the 3.0.0 source, not assumed.

## How to reproduce

1. Cast any source to a Cast speaker (the Office speaker in `DirectChannel` mode reproduced it).
2. Unplug the speaker's power.
3. Within about 5 s (as measured in the baseline run; the heartbeat interval was not separately measured), `radio-api` aborts. Confirm with `ssh mmack@radio "systemctl show radio-api -p NRestarts"` before and after — the count goes up by one — and `journalctl -u radio-api --since '-5min' --no-pager | grep -E 'Unhandled exception|HeartbeatChannel|core-dump'`.

⚠ This drops Bluetooth and the kiosk on the box. Do it with the owner present, not during someone's listening.

## Verification for a fix

⛔ **NOT auto-mergeable** — live audio path, and the only proof is the owner re-running the unplug with the real speaker.

1. **Unit / integration:** whatever replaces or guards the heartbeat must have a test that drives a send failure on a dead connection and asserts the process-level outcome (no unobserved exception escapes), not only that a method returned.
2. **On the box, with the owner:** read `systemctl show radio-api -p NRestarts`; cast; unplug the speaker; wait at least 30 s; read `NRestarts` again. **Pass = unchanged**, no `Unhandled exception` in the journal, Bluetooth still connected, kiosk still live (`Deploy-ToLinux.ps1 -VerifyOnly` connection count).
3. **The console notices and falls back or reconnects** — that is `AUD-37`'s behaviour (punch list §4.2): the local speakers unmute or the output returns to a local device, and the UI shows the Cast output as disconnected. If this row ships without `AUD-37`, say so explicitly: "does not crash" and "notices" are different claims, and only the first is this row's.
4. Plug the speaker back in and reconnect from the console; audio resumes at the remembered volume (`AUD-80`).

## Related

- `AUD-37` (punch list §4.2) — the "notice and fall back" half; do them together or in sequence.
- `AUD-54` (punch list §5) — Cast lifecycle defects in the same client-lifetime code; the 2026-09-30 baseline also recorded a connect race there.
- `AUD-81` — the console volume does not reach the speaker; reconfirmed in the same run.
- `AUD-80` ✅ ([#725](https://github.com/mmackelprang/RTest/pull/725)) — the remembered per-device volume that made the restart look like a reconnect.
- `OPS-3` — `BindsTo=` for `radio-web`; relevant to how the UI behaves when `radio-api` dies.
