# `OPS-14` — the API docs (OpenAPI + Scalar) are served on the box, and the DevTray says where

[← Builder Queue index](../BUILDER_QUEUE.md)

✅ **SHIPPED 2026-10-02.**

## Request

Owner, 2026-10-02: *"Side question: is the radio api swagger doc up to date?  Is it currently running on the radio box?"* The answer: there is no Swagger any more. The API serves the built-in OpenAPI document (`/openapi/v1.json`) and a Scalar UI (`/scalar/v1`), generated from the live controllers, but both were mapped only under `IsDevelopment()`, so the box (Production) answered **404** for both. `CLAUDE.md` and `README.md` still pointed at `/swagger`. Offered: (1) serve them on the box, or (2) keep them development-only and fix the docs. Owner: *"1"*.

Then: *"put a note in the triple-tap debug dialog about where to find the swagger docs - don't actually open them on the console theough."*

## As built

- `src/Radio.API/Program.cs`: `MapOpenApi()` and `MapScalarApiReference()` are mapped in every environment. Nothing new is exposed — every documented endpoint is already reachable, unauthenticated, on the LAN. `Scalar.AspNetCore` 2.17.2 embeds its UI script (no CDN reference in the assembly), so the page works offline.
- DevTray: a reading row **API docs · `http://radio:5000/scalar/v1` · open on another device**. Plain text, never an `<a>` — a page outside the app strands the kiosk (`UI-26`). The address is this machine's name plus the configured API port, because `radio-web` itself reaches the API at `localhost`. `MainLayout` computes it (`DevTray.BuildApiDocsUrl`) and passes it in.
- `CLAUDE.md` and `README.md` name the real URLs.

## Tests

- `ApiTests.OpenApiDocument_IsServedOutsideDevelopment` / `ScalarReference_IsServedOutsideDevelopment` (host in the `Testing` environment). Mutant: the `IsDevelopment()` guard put back → both fail.
- `DevTrayTests`: `BuildApiDocsUrl` cases, note is plain text with no link or button, hidden without an address. Mutants: the note as an `<a>` → 1 fails; the API host instead of the machine name → 2 fail.

## Verification

`curl http://radio:5000/openapi/v1.json` and `/scalar/v1` answer 200 from the LAN; the DevTray shows the note.
