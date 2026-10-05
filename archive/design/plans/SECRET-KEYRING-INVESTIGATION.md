# DataProtection Key-Ring Failure — Investigation & Remediation

**Date:** 2026-07-17
**Box:** `radio` (Ubuntu N100, x64) — `radio-api.service`
**Status:** ✅ **RESOLVED — verified on the box 2026-09-02.** Both the code fix and the owner
re-entry are done: the key rings persist (`data/keys` from Jul 17, `data/keys-web` from Aug 18, both
surviving every deploy since), there are zero decrypt failures in the log, and a live announcement
produced *"Creating TTS audio ... with engine Google"* with ducking and a clean teardown. The branch
referenced below merged as `fix/web-dataprotection-keyring`. ⚠ One residual, filed as `SEC-2`: the
Azure key slot appears to hold a Google key (`AIza...` prefix, identical to the Google slot) — latent
while `tts:defaultEngine` is `Google`.

*Original status:* Root cause confirmed. Code/config fix implemented on branch
`fix/dataprotection-keyring-persist-path` (awaiting review). Owner action required to
re-enter secrets.

**See also:** the [2026-08-18 addendum](#addendum-2026-08-18--the-same-defect-in-radioweb)
at the end of this document — the same class of defect in `Radio.Web`, which the
original text of this investigation explicitly (and wrongly) ruled out.

---

## Symptom

`radio-api` logs, clustered around Cast/device operations (observed 15:19–15:22):

```
Failed to decrypt secret (key ring mismatch? secrets may need to be re-entered)
CryptographicException: The key {ca0ed7d8-6666-4685-9886-c90f60be8393}
was not found in the key ring
```

The live key ring at `/opt/radio-console/.aspnet/DataProtection-Keys/` holds three keys —
`6ddfce58…` (created May 18), `b4d1194b…` (Feb 19), `cfc4777a…` (Feb 13) — none of which is
`ca0ed7d8…`. A stored secret was encrypted with a key that has since been lost.

---

## Finding 1 — Which secret(s) + feature impact

The secrets store is a SQLite DB at `/opt/radio-console/data/secrets/secrets.db` (table
`Secrets`). It holds **four** secrets, and **all four were encrypted with the lost key
`ca0ed7d8`** — the entire store is currently undecryptable. Confirmed by decoding only the
DataProtection key-id header embedded in each ciphertext (a non-sensitive GUID; secret
plaintext was never touched):

| Tag (identifier)      | Created (UTC)        | Key-id in ciphertext | Feature |
|-----------------------|----------------------|----------------------|---------|
| `tts_google_api_key`  | 2026-02-12 18:09:51  | `ca0ed7d8…` (lost)   | **Google Cloud TTS** |
| `tts_azure_api_key`   | 2026-02-12 18:09:52  | `ca0ed7d8…` (lost)   | **Azure TTS** |
| `tts_azure_region`    | 2026-02-12 18:09:52  | `ca0ed7d8…` (lost)   | **Azure TTS region** |
| `acoustid_api_key`    | 2026-02-12 18:10:09  | `ca0ed7d8…` (lost)   | AcoustID (vestigial) |

**Live impact — TTS is dark (primary impact):**
- `appsettings.json` binds `TTS:GoogleAPIKey = ${secret:tts_google_api_key}` and
  `TTS:AzureAPIKey = ${secret:tts_azure_api_key}`. These `${secret:…}` tags are resolved by
  `AddSecretResolution<TTSSecrets>()` (see `AudioServiceExtensions.AddEventAudioSources`).
- Resolution calls `SecretsProviderBase.Decrypt`; the lost key makes `Unprotect` throw, which
  is swallowed and returns `null` (`SecretsProviderBase.cs:103–114`), so
  `ResolveTagsAsync` leaves the tag **unreplaced**.
- `TTSFactory` then sees the value still contains `"${secret:"` and throws
  *"Google TTS API key is not configured. Set it via the System Configuration → Secrets → TTS
  Services page."* (`TTSFactory.cs:383`, and the Azure equivalent at `:593`).
- `TTS:DefaultEngine = "Google"`, so the primary TTS path (event/announcement audio) is
  broken. Every enumeration of TTS engines/voices — which the Sources/Devices UI performs —
  re-resolves the secrets and re-throws the decrypt warning, which is why the errors cluster
  around device/Cast operations.

**AcoustID — effectively no impact (vestigial):** the current fingerprinting pipeline uses
SongRec (Shazam) + MusicBrainz (`Fingerprinting` section in `appsettings.json`); there is no
active consumer of an AcoustID web-service API key in code, and `appsettings.json` has no
`${secret:acoustid_api_key}` reference. The secret is a leftover from the pre-`fpcalc`
AcoustID pipeline (last accessed 2026-03-05). Re-entry is optional.

---

## Finding 2 — Why the key was lost (root cause)

**The app never configured an explicit key-storage path.** `AddManagedConfiguration`
(`ConfigurationServiceExtensions.cs`) called only:

```csharp
services.AddDataProtection().SetApplicationName("Radio.Configuration");
```

With no `PersistKeysToFileSystem`, ASP.NET Core falls back to the **ambient
`$HOME/.aspnet/DataProtection-Keys`**. That makes the key-ring location a function of the
process's `HOME` — and `HOME` changed between runs:

| Date | Event | Key ring location |
|------|-------|-------------------|
| **2026-02-12** | Four secrets encrypted (key `ca0ed7d8` in ring) | pre-`HOME` default location (service had **no** `HOME` set) |
| **2026-02-13** | Commit `e07c96c` "Dual-service deployment" adds `Environment=HOME=/opt/radio-console` to `radio-api.service` | `/opt/radio-console/.aspnet/DataProtection-Keys/` |

The evidence lines up exactly: the oldest surviving key `cfc4777a` was created **Feb 13
16:06** — i.e. DataProtection generated a *fresh* key the first time it ran under the new
`HOME`, in the new location. The old key `ca0ed7d8`, written the day before to the
pre-`HOME` location, was never migrated and is orphaned. Because all four secrets were
entered on Feb 12, they were all encrypted under `ca0ed7d8`, so all four broke at once.

`PersistKeysToFileSystem` has **never** appeared anywhere in the git history — confirming the
key ring always depended on ambient `HOME`.

(Note: the later `User=radio → User=mmack` switch on 2026-05-22, commit `9ef7582`, did **not**
move the ring, because `HOME` is pinned explicitly in the unit and the deploy chowns
`/opt/radio-console` recursively to the run-user. The ring files are owned by `mmack`.)

---

## Finding 3 — Is the key ring deploy-safe now? **Yes.**

- **Deploy wipe scope:** `Deploy-ToLinux.ps1` runs `rsync -a --delete` into `…/api/` and
  `…/web/` **only**. The key ring at `/opt/radio-console/.aspnet/…` is at the `HOME` root, one
  level above `api/`, so `--delete` never touches it.
- **`HOME` is stable:** `Environment=HOME=/opt/radio-console` has been in the unit since
  2026-02-13 and is unchanged since; the ring has accumulated normal 90-day rotations in place
  (Feb 13 → Feb 19 → May 18).

**Conclusion:** this is a **one-time legacy artifact** — secrets encrypted on Feb 12 with a
key that the Feb-13 `HOME` change orphaned. It will **not** recur on its own. The remaining
risk is latent: the ring's safety still rides on the ambient `HOME` default rather than an
explicit path, so a *future* `HOME`/home-dir change could move it again. Finding-2 fix closes
that.

---

## Remediation

### (a) Re-enter the affected secret(s) — owner supplies the values

The actual key values are the owner's to provide; nothing in this investigation printed or
stored them. Re-entry re-encrypts them under the *current* key ring, fixing the decrypt errors.

**Recommended order:** deploy the Finding-2 fix **first** (it relocates the ring to an
explicit path), then re-enter — so the re-encrypted secrets land under the robust path.

**TTS (Google + Azure) — required to restore TTS:**
- **UI:** System Configuration → **Secrets** → **TTS Services** — enter Google API key,
  Azure API key, Azure region → Save. (This is exactly what the `TTSFactory` error message
  directs to.)
- **Equivalent API** (from the box, values redacted):
  ```bash
  curl -X POST http://localhost:5000/api/secrets/tts \
    -H 'Content-Type: application/json' \
    -d '{"GoogleAPIKey":"<google-key>","AzureAPIKey":"<azure-key>","AzureRegion":"eastus"}'
  ```
  Routes through `SecretsController` → `ISecretsProvider.SetSecretAsync` →
  `tts_google_api_key` / `tts_azure_api_key` / `tts_azure_region`.

**AcoustID — optional (no active consumer today):** re-enter only if the AcoustID web-service
lookup is reintroduced; otherwise the stale row can be left or cleared. It does not affect
current fingerprinting (SongRec + MusicBrainz).

> Tip: the four undecryptable rows can be left as-is — `SetSecretAsync` upserts and overwrites
> them on re-entry. No manual DB surgery is needed.

### (b) Code/config fix — make the key ring robust (implemented)

Branch **`fix/dataprotection-keyring-persist-path`**:

1. **`src/Radio.Configuration/ConfigurationServiceExtensions.cs`** — `AddManagedConfiguration`
   now persists the key ring to an **explicit, `HOME`-independent** path and keeps
   `SetApplicationName`:
   ```csharp
   services.AddDataProtection()
     .SetApplicationName("Radio.Configuration")
     .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
   ```
   `keysPath` resolves as: `DataProtection:KeysPath` → else `<Database:RootPath>/keys` → else
   `./data/keys`, then `Path.GetFullPath` + `Directory.CreateDirectory`. On the box this is
   `/opt/radio-console/data/keys`.
2. **`src/Radio.API/appsettings.json`** — adds an explicit `DataProtection:KeysPath =
   "./data/keys"` (with an inline note).

**Why `data/keys`:** it lives under the persistent data root alongside `secrets.db` /
`configuration.db`, which the deploy preserves (`rsync --delete` only wipes `api/`/`web/`), and
it is derived independently of `HOME`, so no future user/home change can silently move it again.

**Behavioural note (safe):** after this deploys, DataProtection will create a fresh key under
`data/keys` and ignore the old `.aspnet` ring. That loses nothing — every current secret is
already undecryptable — and the owner's re-entry (step a) encrypts under the new ring. This
ring is the API's alone: `AddManagedConfiguration` is called only from `Radio.API`, so no
other process reads or writes `data/keys`.

> ### ⚠ Correction (2026-08-18)
>
> The sentence above originally ended *"…so nothing else is affected."* **That was false**,
> and the error cost a two-day, every-page-500 outage of the Blazor UI — see the
> [addendum](#addendum-2026-08-18--the-same-defect-in-radioweb).
>
> `Radio.Web` not calling `AddManagedConfiguration` means it does not share *this* key ring.
> It does **not** mean `Radio.Web` is free of DataProtection. Blazor Server protects the
> serialized marker it emits for every interactive component, so `Radio.Web` had a key ring
> of its own the whole time — the ambient `$HOME/.aspnet/DataProtection-Keys` one, i.e.
> exactly the fragile default this investigation set out to eliminate. The remediation
> above removed the trap from the API and left it in place for the UI.

**Validation:** `Radio.Configuration` builds 0-warning; `Radio.API` (net10.0) builds 0-error;
`Radio.Configuration.Tests` 115/115 pass.

---

---

## Addendum (2026-08-18) — the same defect in `Radio.Web`

**Status:** root cause confirmed live on the box. Fix on branch
`fix/web-dataprotection-keyring`.

### Symptom

Every page at `http://radio.local:5002/` returned HTTP 500 from 2026-08-16 onward.
`MainLayout` still painted — it is static-rendered — while the page body was ASP.NET
Core's error page, which is why the console looked half-alive rather than dead.

### Causal chain (each step observed, not inferred)

1. `deploy/common/radio-web.service` is hardened with `ProtectSystem=strict` and
   `ProtectHome=true`, with
   `ReadWritePaths=/opt/radio-console/logs /opt/radio-console/web /opt/radio-console/data`.
2. Reading `/proc/<radio-web PID>/mounts` **inside the unit's own mount namespace**
   shows `/home` masked as a read-only empty tmpfs
   (`tmpfs /home tmpfs ro,nosuid,nodev,noexec,relatime,…`), while the process
   environment still carries `HOME=/home/mmack`.
3. `src/Radio.Web/Program.cs` configured no DataProtection, and `Radio.Web` never
   calls `AddManagedConfiguration`, so the key ring fell back to the ambient
   `$HOME/.aspnet/DataProtection-Keys` — inside that namespace, both invisible and
   unwritable.
4. Minting a new key therefore failed with
   `System.IO.IOException: Read-only file system`.
5. Blazor Server calls `Protect()` to serialize the interactive component marker.
   Captured stack: `SSRRenderModeBoundary.ToMarker` →
   `ServerComponentSerializer.CreateSerializedServerComponent` →
   `TimeLimitedDataProtector.Protect` → `KeyRingProvider.CreateCacheableKeyRingCore`
   → `FileSystemXmlRepository.StoreElementCore` → `File.Move` → `IOException`.
   `src/Radio.Web/Components/App.razor` uses
   `new InteractiveServerRenderMode(prerender: false)` on both `HeadOutlet` and
   `Routes`; the failure happens anyway, because the marker is emitted regardless of
   prerendering.
6. Timeline: zero such errors on Aug 15; they begin immediately after the Sun
   2026-08-16 03:00:47 restart. The deployed unit file had been rewritten with the
   hardening on Aug 10 16:56, but the running process kept its old mount namespace
   until that restart applied it — which is why the outage and the config change are
   six days apart.

### Why the hardening, not the app, changed the outcome

`Radio.Web` had depended on ambient `$HOME` since it was written. It worked only
because `HOME` happened to name a writable directory. `ProtectHome=true` removed that
accident. Nothing in the application changed on Aug 16.

### Fix

1. **`src/Radio.Web/Configuration/DataProtectionSetup.cs`** (+ the call from
   `src/Radio.Web/Program.cs`) — persists the ring to an explicit, `HOME`-independent
   path using the same resolution order as `AddManagedConfiguration`:
   `DataProtection:KeysPath` → `<Database:RootPath>/keys-web` → `./data/keys-web`,
   then `Path.GetFullPath` against the process working directory +
   `Directory.CreateDirectory`. `SetApplicationName("Radio.Web")` — a different
   purpose discriminator from `"Radio.Configuration"` on purpose: `Radio.Web` has no
   reason to be able to unprotect API secrets.
2. **`src/Radio.Web/appsettings.json`** — `DataProtection:KeysPath = "./data/keys-web"`,
   with an inline note. A **separate directory** from the API's `./data/keys` so the
   secrets ring keeps holding only API-created keys; key files carry no app name, so a
   shared directory would leave a future secrets investigation unable to attribute
   them. It is not a security boundary — both services run as `mmack`.
3. **`deploy/common/radio-web.service`** — `Environment=HOME=/opt/radio-console/data`.
   Defense in depth for any *other* `$HOME` consumer, not the fix itself.
   `/opt/radio-console/data` rather than `/opt/radio-console`, because
   `ProtectSystem=strict` leaves only the `ReadWritePaths` entries writable and the
   parent is not one of them. Folded into the canonical unit; a matching fallback
   drop-in lives at
   `deploy/provision/systemd/radio-web.service.d/10-dataprotection-home.conf` for a
   box whose deployed unit predates the fold.

### Generalised lesson

*"Service X does not use the secrets store"* does not imply *"service X does not use
DataProtection"*. ASP.NET Core takes the key ring for antiforgery, for Blazor Server
component markers, and for anything else built on `IDataProtector`, whether or not the
app asked for it. When a unit gains `ProtectHome=`/`ProtectSystem=` hardening, the
writable-path audit has to cover what the *framework* writes, not only what the
application's own code writes.

---

## One-line owner summary

TTS (Google + Azure) is silently off because all stored secrets were encrypted on 2026-02-12
with a DataProtection key that the 2026-02-13 `HOME=/opt/radio-console` change orphaned.
**Re-enter the TTS keys** at *System Configuration → Secrets → TTS Services* (AcoustID is
optional/vestigial). Merge `fix/dataprotection-keyring-persist-path` and deploy first so the
key ring is pinned to an explicit, deploy-safe path and can't drift again.
