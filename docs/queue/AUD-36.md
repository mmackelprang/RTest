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
