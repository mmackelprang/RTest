# `AUD-92` — DirectChannel `pong` replies never reach the console: `LastRttMs` is always 0

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3, diagnostics only.** Filed 2026-10-01 by the `AUD-54` Builder. **Read from the decompiled library, not measured on the box.**

## Provenance

`AUD-54` defect (2) said that after a Stop/Start, SharpCaster routed inbound DirectChannel messages to a stale, re-registered channel. `AUD-54` fixed the re-registration: there is now one channel per namespace. Then `AUD-84`'s Builder (follow-up 6) reported that SharpCaster drops unknown message types. The `AUD-54` implementer decompiled `~/.nuget/packages/sharpcaster/3.0.0/lib/net9.0/Sharpcaster.dll` with `ilspycmd` to check that report, and the pre-merge reviewer confirmed what it found.

## What happens (FROM THE DECOMPILED SOURCE)

- `ChromecastClient.Receive` chooses the channel with `Channels.FirstOrDefault(c => c.Namespace == castMessage.Namespace)`.
- It dispatches a message only if `MessageTypes.TryGetValue(message.Type, …)` succeeds. `MessageTypes` is built in `InitializeClient` with `ToDictionary`, using the default comparer, which is case-sensitive.
- SharpCaster's own `PongMessage` has the type `"PONG"`. Our receiver (`docs/receiver-direct-channel.html:316`) replies with `type: 'pong'`.
- That lookup misses, so SharpCaster takes its else branch. It logs a conversion error, but our client has no logger. It then calls `Debugger.Break()`. The message is never delivered.
- One side effect does happen: `HeartbeatChannel.RestartTimeoutTimer()` runs before the type check.

**Effect:** `DirectCastAudioChannel.OnMessageReceived` never runs for a pong. `DirectCastStreamingService.LastRttMs` stays 0. `POST /api/devices/cast/ping` and `GET /api/devices/cast/diagnostics` report no round-trip time. The audio itself is not affected.

## Fix options (for the plan)

1. **Receiver:** send a type SharpCaster knows, or the exact `"PONG"`. Check how the receiver page is deployed (GitHub Pages) and whether the Cast app id caches it.
2. **Sender:** on registration, replace the private `MessageTypes` with a copy that adds `"pong" → typeof(PongMessage)`, as one reference swap. This reflects into private SharpCaster state, the same way `RegisterCustomChannel` already does.

Also find out what `Debugger.Break()` does in a Release build on Linux with no debugger attached. It appears to be harmless, because audio has been flowing for months, but nobody has verified that.

## Verification

With the fix deployed and casting in DirectChannel mode, `POST /api/devices/cast/ping` returns a non-zero `lastRttMs`.
