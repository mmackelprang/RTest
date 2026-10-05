# `AUD-37` — a Cast speaker that drops mid-stream is reconnected when it returns

[← Builder Queue index](../../docs/BUILDER_QUEUE.md) · punch-list row: [`HANDOFF-GA-PUNCH-LIST.md`](../handoffs/HANDOFF-GA-PUNCH-LIST.md) §4.2

✅ **MERGED 2026-10-02 on the owner's validation — PR [#751](https://github.com/mmackelprang/RTest/pull/751).** Owner, 2026-10-02, on the owner script below: *"#1 - I validated this yestarday. This passes."* ⚠ That validation **could not have exercised this code**: the box ran `6ab5b74` from 2026-10-01 13:05 EDT, which does not contain `AUD-37`, so the row was merged without a test of the merged code. The coordinator then deployed `main` `75c9a39` (which contains `d5ea6ed`; both services SHA-verified, kiosk live) and the owner re-ran the unplug / replug test on it — ✅ **owner, 2026-10-02: *"AUD-37 passed."*** That is the validation of record. Before the merge, `origin/main` `3f97d19` was merged into the branch and the full gates were re-run (see § Reviews and gates). The HttpMp3 stalled-write gap stays open on the punch-list row.

_(Was: 🔬 **BUILT 2026-10-01 (batch D), HELD for the owner: PR #751 is open and NOT merged.**)_ The owner script below needs a speaker physically unplugged and replugged. Nothing in this row was run on the box. Firewall changes are ruled out, so there is no agent-side way to drop a speaker.

## Scope

`AUD-84` (#736) made the console notice a dropped speaker and fall back to the local output. This row adds the second half: when the same speaker answers again, the console reconnects and switches the output back to Cast, unless someone picked another output in the meantime. The HttpMp3 stalled-write gap from the punch-list row is **not** in this PR.

## As built

**The watcher.** A drop starts one watcher for that speaker. Only one watcher runs at a time; a replacement waits for the one it replaces.
- It probes the speaker's Cast port with backoff: 5 s, doubling, capped at 60 s. A 30 min window bounds the whole effort.
- A speaker counts as back only after two consecutive answers and a 5 s confirm.
- It then reconnects and switches the output back through the output gate. The switch is conditional on the output not having changed since the drop.
- Option `GoogleCast.AutoReconnect` turns it off.

**It yields to the user.** `DevicesController` cancels the watcher first in `SetOutputDevice`, `ConnectToCastDevice` and `DisconnectFromCastDevice` (`ICastReconnectControl.CancelCastReconnectAsync`, bounded at 3 s). After that bound the user's action proceeds. The watcher then acts only on a connection it published itself (it checks ownership after connect, before confirm and before start). It removes that connection, **unless** the user's action made Cast the active output, in which case it keeps it. A pick of another output while the speaker is away logs `Cast: no longer trying to reconnect to "<name>" — stopped by a user output or Cast action`.
Since `AUD-85`, `ConnectToCastDevice` (`cast/connect`) uses its own pick-aware cancel with an explicit same-device keep and a 15 s wait, so the "keeps it only when Cast is already the active output" rule above applies to `SetOutputDevice`, `DisconnectFromCastDevice`, and a `cast/connect` pick of a different device. See [`AUD-85`](AUD-85.md) § As built.

**A start overtaken by a teardown abandons itself.** `GoogleCastOutput.StartAsync` re-checks its connection's generation three times (after the launch, before the DirectChannel send loop, and before `Streaming`). If a user's teardown replaced the connection mid-start, it stops what it created instead of streaming into a closed socket.

**It never takes a receiver from another sender.**
- A watcher connect is *held*: no `AUD-80` volume restore is sent and no status reaches the console until the speaker's running applications have been read.
- The speaker counts as free only if it runs nothing, our application, or the Backdrop (`E8C28D3C`).
- A speaker running anything else is left alone. The channel is closed, no command is sent, and the outcome is `SpeakerInUse`, which ends that window.
- A status that cannot be read counts as a failed attempt and is retried; nothing is sent to the speaker.

**It is bounded for a flapping speaker.**
- A drop within `AutoReconnectStabilitySeconds` (120) of a watcher reconnect continues the same episode, with the same window and backoff.
- `AutoReconnectMaxReconnectsPerHour` (6) caps watcher reconnects per speaker per hour.
- Any user action resets both.

**It is quiet in journald.** Failed automatic attempts log at Debug, and the watcher logs **one** Warning per episode. User-initiated connects still log Error on failure.

**`AUD-81` (merged first) is respected.**
- A console muted at reconnect mutes the speaker at stream start.
- A speaker the console had muted before the loss is recalled at confirmation (`AUD-81` F11). If the console was unmuted meanwhile, the switch back unmutes it.
- The application-list read is never taken as an external volume or mute change.

## Owner script (by ear, at the console, with audio playing)

Read the deployed SHA first: `curl -s http://radio:5000/api/health/version` must show the merged commit. Read the lines below from the file sink (`/opt/radio-console/logs/radio-YYYYMMDD.txt`). They are logged under `Radio.API.Services` at Information, which is visible by default.

1. **Unplug and replug.** Cast to the Office speaker with audio playing.
   - Unplug the speaker. The console falls back to the local speakers within about 5 s. That is `AUD-84`, already shipped.
   - Expect `Cast: will try to reconnect to "Office speaker" when it returns …`.
   - Plug it back in. Within about 5–60 s of the speaker answering (it takes its own boot time first), audio is back on the Office speaker.
   - Expect `Cast: "Office speaker" is back — reconnected and switched the output back to Cast`.
2. **Unplug, then choose another output.** Cast again, unplug, and while it is away pick another output on the console (for example the Soundbar).
   - Replug it. There is no switch back.
   - Expect `Cast: no longer trying to reconnect to "Office speaker" — stopped by a user output or Cast action`. If the watcher observes the change itself instead, the line is `Cast: no longer trying to reconnect to "Office speaker" — the output was changed to …`.

⛔ Do not use iptables or any firewall change on the box to simulate a drop.

## Reviews and gates

- **Original review:** H1 and M1–M5. They were re-reviewed and confirmed after their fix commits. The M3 and M5 partials were completed.
- **Second review:** one HIGH. A console-muted speaker lost and then auto-reconnected after the console was unmuted stayed silent. That was fixed in `AUD-81` (F11, #749) and is covered here by a test that runs on the device model.
- **Merge with `AUD-81`:** `origin/main` was merged in (`5eee4e87`, 6 conflict hunks in `GoogleCastOutput.cs`), followed by an integration commit and a fourth review. Its M1–M4 were fixed:
  - false comments about `DisconnectAsync`;
  - ownership re-checks after connect;
  - keeping the connection for a Cast pick;
  - an unreadable status no longer ends the episode.
- **Deferred LOWs** (in the PR): the free-check under the default app id `CC1AD845`; a `_diagnosticReadsPending` leak on a never-completing read (shared with `AUD-81`); the blocking `Wait()` in `ConfirmReceiverAvailable`; the hourly cap counting refused switch-backs; "busy" being final for the window; residual per-attempt Warnings (about 30 per 30 min worst case); brief double audio between Streaming and the switch; the gap between the app read and the launch.
- **Gates on `7e9fb782`:** `dotnet build -c Release --no-incremental` gives 46 warnings / 0 errors on Windows. The full `dotnet test` run is green except the six known `SrcVariableResamplerTests`: Infrastructure 2194, API 593, Web 1391.
- **Gates after merging `origin/main` `3f97d19` (2026-10-02, clean merge, no conflicts):** `--no-incremental` Release build 46 warnings / 0 errors on Windows. Full `dotnet test`: every project green except Infrastructure's six known `SrcVariableResamplerTests` — Infrastructure 2214 passed / 6 failed, API 619, Web 1595, Web.E2E 28, RTLSDRCore 280, Core 186, Configuration 115, Fingerprinting 112, AudioAnalysis 35, Integration 31, Metrics 28.
- **Deferred LOWs added by the last two reviews:** an `OperationCanceledException` that did not come from our token is treated as "superseded"; `DevicesController` promotes Cast without re-checking `Streaming` after the start; and a start-time mute that lands after a teardown leaves a mark on a dead generation. That mark self-heals at the next connect to the same speaker (F11).

## Merging with `AUD-85` (local-only branch, held for the owner)

`AUD-85` has not been merged into this branch.
- Expect textual conflicts in `DevicesController.ConnectToCastDevice`. Keep `await CancelCastReconnectAsync()` first.
- Expect `CS0419` ambiguous-cref warnings on `<see cref="ConnectAsync"/>` (there is now an overload). Use the full signature.
