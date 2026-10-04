# `PHN-14` — a caller's name is looked up in both contact sources

[← Builder Queue index](../BUILDER_QUEUE.md)

🚧 **BUILT 2026-10-03, HELD for the owner's real-call UAT** — branch `feat/phn-13-14-ignore-and-lookup`, one PR with
[`PHN-13`](PHN-13.md). **Not deployed, not merged.**

🟡 **Owner request, 2026-10-03.**

> *"Name appears now. Make sure we look both places and dispatch the ignore fix for the button"*

## What the coordinator measured (2026-10-03, read-only)

- `GET /api/bluetooth/pbap/lookup?phoneNumber=…` (`PbapController.cs:47`) answered `404 "No device currently
  connected"` whenever no phone was connected. It answered that for every format of a number that IS in the
  stored synced contacts.
- `GET /api/bluetooth/pbap/status` shows three cached devices, all `isStale`: the Pixel 10 Pro XL with 949
  contacts, and 833 and 672 from older phones.
- The owner's name appeared only after a manual entry was added to RotaryPhone's `/api/contacts`, i.e. the Web
  fallback in the `PHN-11` banner (`IncomingCallBannerService`) works.

## Found while building (read-only, RotaryPhone `main`)

⚠ **The API announcement's RotaryPhone fallback has never answered.** `PhoneContactLookupService` asked for
`GET {ContactsApiBaseUrl}/api/contacts/lookup?phone=`, a route RotaryPhone has never had: its `ContactsController`
maps `GET /api/contacts/{id}`, so `lookup` was read as a contact id and every request 404'd. The design notes had
recorded that contract as "assumed" (`design/FUTURE-WORK.md`). The Web banner's fallback worked because it has
always read the list, `GET /api/contacts`. So until this row the spoken announcement used **neither** source with
no phone connected.

## What the lookup does now

| | |
|---|---|
| **Sources, in order** | 1. The stored synced phone books (PBAP), on **every** phone ever synced. 2. RotaryPhone's contacts list, `GET /api/contacts`, matched locally. |
| **Precedence when both match** | The synced phone book wins (as before, and as recommended). |
| **Which phone books, in which order** | The **connected** phone first (it is the phone in hand, and it was the only one searched before), then the **most recently synced**, then the rest by address. Chosen because the newest sync is the most likely to be current — a contact renamed on the new phone should beat the old phone's spelling — while older phones still fill gaps (the box's three books overlap but are not identical). |
| **Matching** | One rule everywhere (`PhoneNumberNormalizer.FindMatch`; the same tiers in the repository's SQL): an exact match on the digits, with the North American `1` dropped from an 11-digit number — so `+19193718044`, `19193718044` and `9193718044` are one number — then the last seven digits. **An exact match on any phone beats a last-seven match on any phone**, so an old phone's exact entry wins over a connected phone's lookalike. |
| **Consumers** | The banner (Web: `pbap/lookup`, then the list) and the spoken announcement (API: the repository, then the list). Both now use both sources, in the same order, with the same rule. |
| **Logging** | No number or name at Information. The PBAP hit line moved from Information to Debug (it carried only the masked token) and now names the device and match kind. |

Also on the Web side: the Messages feed's local index now prefers a synced contact over a RotaryPhone contact for
the same number (it was alphabetical), and the banner no longer trusts a circuit-cached "no such contact" — the
kiosk circuit lives for days, and before this row every lookup with no phone connected was cached as a miss.

## Owner question (not changed)

The last-seven-digit tier also matches two different numbers that share their last seven digits (different area
codes). It is the repository's long-standing, tested behaviour (`FindByPhoneNumber_Last7Fallback_ShouldMatch`),
carried to every source unchanged rather than tightened unilaterally. With three phone books (~2,400 numbers) now
searched, a stranger could occasionally be announced as a contact. Options: keep it; or match last-seven only when
one side is a 7-digit local number.

## Tests

`PbapContactRepositoryTests` (`AnyDevice_*`, over a fake clock so the sync order is deterministic and the address
order is the reverse of it), `PbapControllerLookupTests` (the real repository on a temp file; every format with no
phone connected), `PhoneContactLookupServiceTests` (both sources, precedence, country codes, the list route),
`PhoneNumberNormalizerTests` (`FindMatch_*`), `ContactResolutionServiceTests` (PBAP-first priming,
`retryCachedMiss`), `IncomingCallBannerServiceTests` (precedence, country codes, the cached miss asked again).

## Pre-merge review (session model) and what was done

No HIGH. **M1** (the banner consulted the circuit's local contact index first — one phone book's first numbers plus
every RotaryPhone contact — so it could show a RotaryPhone name where the announcement spoke the synced one): fixed,
the banner asks the API's lookup directly (`ResolveAsync(skipLocalIndex: true)`), pinned by
`ThePhonePagesLocalIndex_DoesNotOutrankTheApisSyncedPhoneBookAnswer`. **M2** (the hero's 5 s deadline started at
the `200`, the banner's at the tap): fixed, armed at the tap; pinned. **M3** (precedence is source first, then
match tier, so a last-seven synced match beats an exact RotaryPhone match): **not changed — owner question**; it
meets the stated "synced phone contacts first", and the alternative (synced-exact → RotaryPhone-exact →
synced-last-7 → RotaryPhone-last-7) changes that rule. LOWs fixed: `DeclineCallResult.Failed` is 0 (a default
outcome fails closed); no timer after the hero is disposed; the `409` close drops an attempt check that guarded
nothing; the stale `Program.cs` cross-process reload comment and the hero's phone-id comment corrected; a remark
narrowed ("does not parse"); a PhonePage test now waits for the `200` to be handled. Left: the Debug line naming the
device MAC (acceptable — MACs are already logged at Information by `PbapSyncService`).

## Mutation checks

**51 mutants over two rounds, 51 killed** (literal edits in a separate worktree outside the repo, targeted tests
run, file restored from git). Round 1, 30: every ranking step in the repository, the controller's old no-device
404 and connected-phone argument, the lookup service's PBAP skip / connected phone / old `/lookup` route, both
`FindMatch` tiers and the country-code strip, the `409` read as failure / its state dropped / InCall closing as
Ended, the cached miss, the RotaryPhone source, the flag read once, PBAP-first priming, `retryCachedMiss`, and the
hero's 409 / failure / deadline / reset / double-tap / enable, the page and panel wiring. Round 2, after review, 21:
the two new mutants (local index consulted; `skipLocalIndex` ignored) and the Web round re-run against the
refactored code (M24 re-formed: its anchor moved with the tap-armed timer). Not covered by a test: the
`Dispose` attempt bump in the hero, and the enum order.
