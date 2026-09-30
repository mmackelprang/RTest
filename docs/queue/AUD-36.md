# `AUD-36` — `IdentificationIntervalSeconds` is a dead setting that the UI still offers

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3.** Filed 2026-09-26 from `AUD-35`'s adversarial review.

`FingerprintingOptions.IdentificationIntervalSeconds` (default 15) is read only by the start-up log line
in `BackgroundIdentificationService.ExecuteAsync` (*"interval: 15s"*), which is therefore false. The loop's
pacing comes from the capture length (`SampleDurationSeconds`), the SongRec back-off table, and — since
`AUD-35` — `IdlePollIntervalMs`. The System Config page still offers it as *"Time between identification
attempts"* (`SystemConfigPage.razor:736-737`), so an operator changing it changes nothing.

**Decide:** remove it (and its UI control and log text), or make it mean something (a minimum gap between
captures). ⚠ Before removing, check the appliance's SQLite config store for a
`fingerprinting:identificationIntervalSeconds` row — the `fpcalcPath` precedent in `AUD-1` shows an
orphaned row is harmless but confusing.

## ✅ 2026-09-30 — decided: remove it (Builder, Phase 2i)

**Removed end to end**, not given a meaning. The loop already has three honest pacing mechanisms (capture
length plus SongRec latency, the SongRec failure back-off, `IdlePollIntervalMs`); a fourth "minimum gap"
would be new behaviour nobody asked for, and the greenfield rule owes the old key nothing.

- **Gone:** `FingerprintingOptions.IdentificationIntervalSeconds`; the Web `FingerprintingConfigDto`
  property and its System Config field (*"Time between identification attempts"*); the key in
  `src/Radio.API/appsettings.json`, the integration-test config, the AudioUAT tool config, and the design
  docs' samples and tables.
- **The start-up line is now true:** *"Background identification service started (sample duration: 15s,
  idle poll: 1000ms)"* — the two values the loop actually uses (`Math.Max(100, IdlePollIntervalMs)`
  exactly as the loop computes it). Two other stale "interval" wordings in the same file were corrected.

### The appliance's stored row: an orphan, and harmless — measured, not assumed

Read-only on `radio` 2026-09-30 (`sqlite3 -readonly …/configuration.db`, table `Config_sqlite`):
`fingerprinting:identificationIntervalSeconds|30`, written 2026-03-05 by the System Config page. It
**stays** after this change — `POST /api/configuration/{section}` only upserts the keys it is sent and
never deletes one — and sits beside the older orphan `fingerprinting:fpcalcPath`. The deployed
`appsettings.Production.json` does not carry the key; the deployed `appsettings.json` did, and the deploy
overwrites that file. It was deliberately **not** deleted from the store: that is a hand edit to
production config for zero functional gain.

**Why it is harmless rather than assumed harmless:**

- **API side:** options binding uses the default binder (no `ErrorOnUnknownConfiguration`,
  `ValidateOnStart` or DataAnnotations anywhere in `src/`), which ignores a key with no property.
- **Web side — the one that would have hurt:** `ConfigurationApiService.GetConfigurationAsync<T>` returns
  `default` on *any* deserialization failure and the page then falls back to `new()`, whose
  `UseShazamForAllSources` is `false`. Had the orphan made that read fail, saving the page would have
  written `false` over the box's `true` and killed Bluetooth album art (`AUD-1`). It does not fail:
  System.Text.Json skips unmapped members under the client's options.
  `tests/Radio.Web.Tests/Services/ApiClients/FingerprintingConfigOrphanKeyTests.cs` pins both halves
  against the section JSON captured verbatim from the box — the load keeps the stored values, and a save
  no longer writes the key. **Mutation-checked:** adding `UnmappedMemberHandling = Disallow` to the
  client's options fails the load test; re-adding the DTO property fails the save test; each on its own.
