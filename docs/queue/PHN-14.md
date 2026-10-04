# `PHN-14` — a caller's name is looked up in both contact sources

[← Builder Queue index](../BUILDER_QUEUE.md)

🚀 **IN FLIGHT 2026-10-03** — branch `feat/phn-13-14-ignore-and-lookup`, one PR with [`PHN-13`](PHN-13.md).

🟡 **Owner request, 2026-10-03.**

> *"Name appears now. Make sure we look both places and dispatch the ignore fix for the button"*

## What the coordinator measured (2026-10-03, read-only)

- `GET /api/bluetooth/pbap/lookup?phoneNumber=…` (`PbapController.cs:47`) answers `404 "No device currently
  connected"` whenever no phone is connected. It answered that for every format of a number that IS in the
  stored synced contacts.
- `GET /api/bluetooth/pbap/status` shows three cached devices, all `isStale`: the Pixel 10 Pro XL with 949
  contacts, and 833 and 672 from older phones.
- The owner's name appeared only after a manual entry was added to RotaryPhone's `/api/contacts`, i.e. the Web
  fallback in the `PHN-11` banner (`IncomingCallBannerService`) works.

## Required

- The PBAP lookup falls back to the stored contacts when no phone is connected.
- Every caller-name consumer consults both sources: the API's stored synced contacts and RotaryPhone's
  contacts — the banner (Web) and the API's spoken announcement (`PhoneCallIntegrationService`).
- Digits-only matching tolerates a leading country code: `+19193718044`, `19193718044` and `9193718044` all
  match.
- No number in Information logs (`PHN-5`).
