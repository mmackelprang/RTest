# `AUD-89` — the Vinyl device port the System Config page saves is not the one the API reads

[← Builder Queue index](../BUILDER_QUEUE.md)

🔵 **P3.** Filed 2026-09-30 from the `AUD-16` pre-merge review. Both defects are pre-existing and out of
`AUD-16`'s scope. They are code-read findings: nothing has been changed on the box to reproduce them.

## (a) A UI change to the Vinyl port is shadowed by the stale capitalised row

- The Web client saves the Devices tab with `PostAsJsonAsync` (`ConfigurationApiService.cs:70`), which
  uses `JsonSerializerDefaults.Web`. The body is camelCase, so the store key it writes is `devices:vinyl`.
- `DeviceOptionsResolver` reads `devices:Vinyl` first. It falls back to the lowercase key only when that
  one is empty (`DeviceOptionsResolver.cs:59`, `:120-127`).
- **The box has both rows.** `GET /api/configuration/devices` on `radio` (commit `a86349f`,
  2026-09-30) returned
  `{"radio":{…},"vinyl":{"usbPort":"USB Microphone"},"cast":{…},"Radio":{…},"Vinyl":{"usbPort":"USB Microphone"},"Cast":{…}}`.
  A Vinyl change saved from the UI would update `devices:vinyl` while the resolver kept returning
  `devices:Vinyl`.
- **The UI would also snap back.** The Web client deserializes case-insensitively, so the last duplicate
  wins. In the box's order that is `"Vinyl"`, the stale row.
- **No harm today**, because both rows hold `USB Microphone`.

## (b) A failed load saves `/dev/ttyUSB1` over the stored port

`VinylDeviceOptionsDto.USBPort` defaults to `/dev/ttyUSB1` in both copies (`src/Radio.Web/Models/ApiModels.cs`
and the unused `src/Radio.API/Models/ConfigurationModels.cs`). When `GetConfigurationAsync` returns
`default` (on a 404 or any exception, `ConfigurationApiService.cs:48-63`), `SystemConfigPage` falls back
to `new DeviceOptionsDto()` and shows that value. Pressing Save then writes it over the real port. A
serial-device path never substring-matches a capture device name, so Vinyl would stop resolving, or land
on the first-jack fallback (`AUD-13`).

## Suggested fix (plan TBD)

- Make the resolver prefer the key the UI writes (lowercase), or have the controller normalise section
  keys to one casing. Merge the box's existing pair once.
- Default both DTOs to `""`, and correct the `/dev/ttyUSB1` default in `design/SYSTEMCONFIGURATION.md`.
- Delete the unused API-side `DeviceOptionsDto` / `VinylDeviceOptionsDto`: nothing in `src` references them.

⚠ Touches the deployed store. Back up `configuration.db` before merging keys.
