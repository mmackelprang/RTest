# PLAN — `PHN-1b` · ADR-029 PR 2: `GvMediaClient`, the bounded cache, and API-side auth

> **Status:** ready for Builder. Written 2026-09-02 against `c830fb8`.
> **Punch list:** [`docs/HANDOFF-GA-PUNCH-LIST.md`](../../docs/HANDOFF-GA-PUNCH-LIST.md) §3.5 `PHN-1` (P0), §2 `O6`.
> **Decision of record:** [ADR-029](../decisions/2026-08-03-gv-audio-through-engine.md) — D3, D8.
> **Sequencing:** [`design/plans/PHN-arc-pr-breakdown.md`](PHN-arc-pr-breakdown.md) — **this plan is PR 2 of 7.**
> **Depends on:** `PHN-1a` ✅ ([#528](https://github.com/mmackelprang/RTest/pull/528)), merged. Nothing else.
> **Predecessor plan:** [`PHN-1a`](PHN-1a-event-playback-seam-contracts.md) — **its §0.4 contradiction list and
> §5 handoff are authority wherever they disagree with the ADR.** Four items it handed forward are
> settled in §0.3 below.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

PR 1 gave the arc a type surface. PR 2 gives it the **only thing in the arc that talks to another
machine**: a `GvMediaClient` that fetches a voicemail recording from the gvbridge host, server-side,
into a size-bounded on-disk cache, over an `HttpClient` that can carry the `X-RotaryPhone-Auth`
header a browser `<audio>` element never could. **There is still no endpoint, no
`EventPlaybackService`, and nothing that plays a sound** — PR 3 is the first PR a user can reach.
What this PR buys is that the fetch, the cache, the auth and the failure taxonomy are settled and
tested before anything depends on them, and that `Radio.API` gains its `GvMedia` config block once
rather than three times.

### 0.2 Why the cache is the load-bearing part, and not the client

The `HttpClient` half of this row is ninety lines and has an obvious shape. The cache is where the
row can go quietly wrong, for a reason that has nothing to do with performance:

> **GV auth is dead roughly 9 minutes in every 20** (`XR-3`; `docs/HANDOFF-GA-PUNCH-LIST.md`
> §3.5 `XR-3`). Their PSIDTS is good ~11 min, their refresh fires every ~20, and their
> `/api/gvbridge/status` reports `available:true, degraded:false` **during** a blackout. A user who
> plays a voicemail and replays it thirty seconds later has roughly a **45% chance** the second
> fetch would 502 if it went back to the network.

So the cache is not an optimisation and must not be reviewed as one. It is the difference between
"replay always works" and "playback fails at random on a wall clock the user cannot see" — the exact
symptom `design/INTEGRATIONS.md` warns makes test results look random. Its correctness properties
are therefore: **a hit never touches the network**, **the cap is real and eviction actually
deletes**, and **`CacheMaxMegabytes = 0` is a no-cache path rather than an infinitely-evicting one**
(ADR ⟨A1·2⟩).

The cost, stated plainly and owner-accepted at ⟨A1·2⟩: **private voicemail audio now sits at rest on
the box's disk**, where previously it only ever streamed through a browser.

### 0.3 The four items `PHN-1a` handed forward, now decided

`PHN-1a` §5 and its review left four questions explicitly to Planner. All four are **settled here**,
so they cannot drift a third time. Two of them change PR 2's code; two of them bind later PRs.

---

**⓵ `MaxSpeechChars` — the arc settles on REJECTION, not truncation. PR 1's shipped behaviour
stands and the ADR's is overridden.**

ADR §4.2 says the cap is *"truncated with a spoken tail"*, and §10.2's config table repeats it
(*"speech truncation (§4.2)"*). PR 1 shipped `EventPlaybackRejection.TextTooLong` instead. Two PRs
currently imply different answers; this is the answer.

Three reasons, in order of weight:

1. **§4.2's own governing sentence forbids server-side truncation.** The section is titled
   *"Utterance composition belongs to `Radio.Web`"*, and it assigns normalisation — URLs to "a
   link", emoji dropped, whitespace collapsed — to *"a small **pure static helper in `Radio.Web`**"*,
   `GvSpeechText.ForMessage`. Truncating and appending a spoken tail **is composition**: it changes
   what is said and adds words the caller did not write. Doing it in Radio.API contradicts the
   section it is written in.
2. **A truncating server that returns 200 is the same lie PR 1 already refused.** ADR §2 D4
   prescribed that `TTSEventSource` should *no-op* `SeekAsync`; PR 1 overrode it to **throw**,
   because a no-op reporting success is precisely the failure class `CLAUDE.md` § Pre-Merge Review
   exists to catch. A caller that posts 8 000 characters, gets `200`, and hears 1 000 has been told
   the same kind of untruth. Consistency is not the argument here — the argument is that the
   untruth is the same untruth.
3. **`Validate` would have to become a mutator.** It is a pure method on a `sealed record`, callable
   from tests with no configuration object, and the rejection enum exists — in PR 1's own words —
   *"so the controller does not re-derive the rules"*. Truncation cannot be expressed as a
   rejection reason.

**What this binds:**
- **PR 2** writes `GvMediaOptions.MaxSpeechChars`' XML doc to say *reject*, and the word
  "truncation" does not appear anywhere in this arc's code again (Task 1).
- **PR 3** maps `TextTooLong` → `400` with the named reason, like every other rejection.
- **`PHN-3`** (the SMS speak button, which is where `GvSpeechText.ForMessage` actually lands) does
  the truncation **client-side, visibly**, before it posts — which is also the only place a "spoken
  tail" can be composed in the same voice as the rest of the utterance.

⚠ **PR 3 must not silently re-add truncation** on the grounds that the ADR says so. If a reviewer
raises it, this section is the answer.

---

**⓶ `IEventAudioSource.SeekAsync` stays `Task`. Not deferred again — closed for the arc.**

PR 1 called this a Planner call and its XML doc currently describes widening to `Task<bool>` as
*"an open question for PR 3"*. It is now closed as **no**.

- **Widening breaks D4's only justification.** D4's entire argument is that the five signatures are
  copied *verbatim* from `IPrimaryAudioSource`. Widen one and either the codebase carries two seek
  shapes — the thing D4 exists to prevent — or `IPrimaryAudioSource.SeekAsync` changes too, which
  drags `FilePlayerAudioSource` in. That file is a **live primary-source path** with a persisted
  resume position hanging off the same field, is explicitly out of scope, and is logged in
  `design/FUTURE-WORK.md` §14a with its own UAT requirement.
- **The information is not actually lost, it just arrives by a different route.**
  `AudioFileEventSource.SeekCoreAsync` already logs a warning when the player refuses, and
  `Position` reads through to the player rather than tracking a field — so a refused seek shows up
  as **an anchor that did not move** in the next `EventPlaybackSnapshot`. The scrubber snaps back,
  which is the correct user-visible behaviour for a refused seek, and it arrives through the
  broadcast mechanism that already exists rather than through a new bool nobody would render.
- **The cost of deciding it now is zero.** PR 2 does not touch that seam at all. The cost of
  deciding it *by changing it* is a primary-source path with its own UAT.

**What this binds:** `IEventPlaybackService.SeekAsync`'s remark currently asserts an open question
that is closed. That is a false comment, and a false comment is the failure class this repo has
shipped five times. **PR 2 corrects it** (Task 12) — doc-only, no behaviour, in a file PR 2
otherwise does not touch, because leaving it for PR 3 means main carries a statement this plan
already knows to be untrue.

---

**⓷ `Label` gets a cap of 128 characters, enforced in `Validate`, implemented by PR 3 — not by the
controller and not by PR 2.**

PR 1 said the cap *"belongs with the controller"*. That contradicts PR 1's own handoff sentence to
PR 3 — *"the rejection enum exists so the controller does not re-derive the rules"* — and it would
make `Label` the only bound in the request that does not live beside `MaxMediaIdChars`, the priority
range and the text cap.

- **Where:** `EventPlaybackRequest.Validate`, as `public const int MaxLabelChars = 128;` plus a new
  `EventPlaybackRejection.LabelTooLong`.
- **Why 128:** generous — it holds *"Voicemail from Jane Smith (555) 123-4567"* four times over —
  and the point is that it is bounded, matching `MaxMediaIdChars`' stated posture. `Label` flows
  into `EventPlaybackSnapshot`, which PR 5 broadcasts over `/hubs/audio` to every open client on a
  box where CPU churn is audible; an unbounded string on that wire is a real if small cost, and a
  log-line and layout hazard besides.
- **Why PR 3 and not PR 2:** PR 2 ships no validation path and no route, so a `LabelTooLong` member
  added here would be **unreachable code shipped a PR early**. That is the same test applied to ⓶
  and reaching the opposite answer for a principled reason: ⓶ corrects a statement that is *false
  today*; ⓷ would add behaviour that is *unreachable today*.

---

**⓸ Masking: mask on **every** line — and `PHN-1a` C-8 undercounted the example it warned about.**

ADR §5.1 tells this PR to follow `PhoneContactLookupService`'s *"log-masking discipline (it masks
numbers to `***1234`)"*. PR 1's C-8 correctly refused that, noting the file masks on one line and
logs raw on two. **Re-verified against `c830fb8`, it is worse than that — four raw sites against one
masked, and the masked one leaks too:**

| `PhoneContactLookupService.cs` | What it logs |
|---|---|
| `:62` | `LogInformation("PBAP contact found: {Number} → {Name}", phoneNumber, contact.DisplayName)` — **raw number *and* the contact's real name** |
| `:78` | `LogDebug("Looking up contact for {PhoneNumber}", phoneNumber)` — raw |
| `:87-90` | the one masked line — but it still logs `contact.Name` **in full**, and ADR §5.1 asks for *"voicemail ids **and caller identity**"* to be masked |
| `:96-97` | `LogDebug("Contact lookup returned {StatusCode} for {PhoneNumber}", ..., phoneNumber)` — raw |
| `:102` | `LogWarning(ex, "Contact lookup failed for {PhoneNumber}", phoneNumber)` — raw, **and at Warning**, so since `LOG-11` this is one of the few that still reaches `journalctl` |

**A fifth leak the rule as written does not cover: exception messages.** `:102` logs `ex`. Any
exception whose `Message` carries a raw identifier leaks through **every** caller's catch block,
including callers this arc has not written yet. So the rule for PR 2 is stronger than "mask on every
log line":

> **A raw media id must never reach a log message, a log argument, or an exception message.**
> `GvMediaClient` computes its masked form once at method entry and the raw id is used for exactly
> two things thereafter: building the URL, and hashing the cache filename.

**And the mask itself changes shape, deliberately.** `***1234` is right for a phone number because a
human recognises a phone number by its last four digits. **Nobody recognises a voicemail id**, so a
suffix leaks four characters of a secret for zero operator benefit. PR 2 masks media ids as a
**hash prefix** — `gvm:1a2b3c4d`, the first 8 hex of the same SHA-256 that names the cache file — so
log lines correlate with each other and with the file on disk, and leak nothing. This is a declared
deviation from ADR §5.1's literal `***1234`, in service of the property §5.1 was asking for.

Task 9 pins all of this with a test that captures every log line at every level across success,
404, 502, timeout and cache-hit, and asserts the raw id appears in **none** of them.

### 0.4 ⚠ Nine contradictions found while planning, and how each resolves

Read this before Task 1. Three of them change what PR 2 builds.

**C-12 — ⚠ THE ONE THAT CHANGES THE RISK RATING. ADR §5.1 and §10.1 claim Radio.API has "no
`AddHttpClient`, no `IHttpClientFactory`, and no `DelegatingHandler` infrastructure at all", and the
breakdown concludes "the handler infrastructure is genuinely net-new". The grep is right about the
directory and the conclusion is wrong about the container.**

`src/Radio.API/` does contain zero `AddHttpClient` lines — confirmed. But the API's composition root
reaches three of them from two lines of its own `Program.cs`:

- `src/Radio.API/Program.cs:102` → `AddSoundFlowAudio` → `AudioServiceExtensions.cs:257`
  `services.AddPhoneIntegration(configuration)` — **unconditional**, no `Enabled` gate at the call
  site — → `AudioServiceExtensions.cs:469` `services.AddHttpClient<PhoneContactLookupService>();`
- `src/Radio.API/Program.cs:107` → `AddRadioWeather` → `WeatherServiceExtensions.cs:38` and `:55`,
  two named clients (`"nws"`, `"weather-zippopotam"`).

So **`IHttpClientFactory` resolves in Radio.API today**, and `Microsoft.Extensions.Http` 10.0.0 is
already a `PackageReference` of `Radio.Infrastructure` (`Radio.Infrastructure.csproj:45`).

**Resolution:** the shape of the work is unchanged, but its framing is. PR 2 is *adding a typed
client next to three existing ones*, not *standing up HTTP infrastructure*. What is genuinely
net-new is only the `DelegatingHandler` **type** on the API side — and even that has a working
31-line template in `Radio.Web` with an existing test. The breakdown rates PR 2 **Medium** partly on
this claim; the honest rating is **Medium for the cache, Low for the client and the handler**.

**C-13 — ⚠ ADR §5's registration instruction walks straight into the DI hazard this PR exists to
guard against.** §5 says `GvMediaClient` is *"registered beside its sibling in
`AudioServiceExtensions.cs`"*. Its sibling `PhoneContactLookupService` is registered **inside
`AddPhoneIntegration`** (`AudioServiceExtensions.cs:456-469`), which is reached only from within
`AddSoundFlowAudio`. Registering there would:

- **bury the feature inside the audio graph**, so any test that resolves it initialises real audio
  hardware — which is exactly why `ActiveSourceAccessorRegistrationTests` had to fall back to
  descriptor inspection instead of building a provider, and therefore exactly why no guard in this
  repo would catch a missing registration today;
- **couple a feature with its own `GvMedia:Enabled` flag** to `AddSoundFlowAudio`'s lifecycle; and
- grow a method that is already ~430 lines.

**Resolution:** PR 2 adds `src/Radio.Infrastructure/DependencyInjection/GvMediaServiceExtensions.cs`
with `AddGvMedia(this IServiceCollection, IConfiguration)`, called from `src/Radio.API/Program.cs`
directly — following the **`WeatherServiceExtensions` / `AddRadioWeather` precedent**, which is a
self-contained feature extension invoked from `Program.cs` and is the closest in-tree match. This is
a **scoping correction, not a re-decision**: the client still lives in
`src/Radio.Infrastructure/External/` exactly as §5 says. It is also what makes Task 11's real
build-and-resolve guard possible at all.

**C-14 — ⚠ ADR §10.2 prescribes setting `GvMedia:AuthKey` in `appsettings.Production.json`, and for
Radio.API that file does not exist — and the deploy will not deliver an edit to the one that does.**
Three separate facts, all verified:

1. **`src/Radio.API/appsettings.Production.json` does not exist.** The solution has exactly three
   appsettings files: `src/Radio.API/appsettings.json`, `src/Radio.Web/appsettings.json`,
   `src/Radio.Web/appsettings.Production.json`.
2. **The per-machine file that actually ships comes from `deploy/`, not from `src/`.**
   `Deploy-ToLinux.ps1:220` reads `deploy/<configDir>/appsettings.Production.json` and `:226` copies
   **one file to both** `api/` and `web/`. So the ADR's *"real cost … two copies of one secret in two
   services' configuration"* is smaller than stated — it is two **keys** in one **file**, and the
   ADR's proposed mitigation (*"`deploy/` should write both from one source value"*) is already
   half-built.
3. ⚠ **The copy is guarded by `test -f` (`Deploy-ToLinux.ps1:222`) and only runs when the file is
   absent on the box**, and `rsync --delete --exclude='appsettings.Production.json'` (`:217`)
   deliberately preserves it. `radio` already has one — it carries the live
   `FilePlayer:RootDirectory=/mnt/nas/music`. **So editing the repo's deploy seed does not reach the
   box.**

**Resolution:** PR 2 puts the whole `GvMedia` block, with its defaults, in
`src/Radio.API/appsettings.json` — which **is** overwritten on every deploy and is therefore the
right home for non-secret defaults. It does **not** touch `deploy/*/appsettings.Production.json`: an
`AuthKey: ""` there is byte-for-byte identical to the class default, so it would be noise on a fresh
install and would not land on an existing one. Instead PR 2 writes the runbook line (Task 13) —
flipping the key on a live box is a hand edit of `/opt/radio-console/api/appsettings.Production.json`
plus `systemctl restart radio-api`, and nothing in the deploy will do it for you. `ASPNETCORE_ENVIRONMENT=Production`
is set on both units (`deploy/common/radio-api.service:101`, `deploy/common/radio-web.service:59`),
so the overlay does load once it is there.

**C-15 — the ADR's `GetVoicemailFileAsync` signature cannot express what happens to the file when
caching is OFF, and no document notices.** §5 specifies `Task<string>` returning *"a local cached
path"*. With `CacheMaxMegabytes = 0` there is no cache, but playback still needs a **path** — and
nothing says who deletes it. Left unresolved, PR 3 either leaks a file per play or the cache
"disables" by deleting every other file, which ⟨A1·2⟩ explicitly forbids (*"a `0` cap must be a
no-cache path, not an infinitely-evicting one"*).

**Resolution, and it is a design decision the ADR left open rather than a correction:** one
directory, one naming scheme, one code path, and `CacheMaxMegabytes` selects **retention**, not
destination.

| | `CacheMaxMegabytes > 0` (default 50) | `CacheMaxMegabytes == 0` |
|---|---|---|
| Where the file is written | `GvMedia:CacheDirectory` | the same directory |
| Is a later fetch served from disk | **yes** — `TryGetPath` returns a hit | **no, never** — always refetches, so the 9-in-20 blackout exposure is real and intended |
| Reclamation | LRU evict-to-cap after each write, oldest `LastWriteTimeUtc` first | short-TTL sweep after each write: anything older than `max(60s, MaxPlaybackSeconds × 2)` is deleted |

Nothing is evicted per-write in the `0` case, so it is not "infinitely evicting"; it is a
short-retention sweep. **The residual, stated rather than hidden: with caching off, at most one
recording lingers on disk until the next fetch.** That is bounded, and it is the honest price of a
`Task<string>` return.

**C-16 — LRU cannot be built on access time on this box.** The obvious implementation of
"least-recently-*used*" is `File.GetLastAccessTimeUtc`. Linux mounts default to `relatime`, which
updates atime at most once a day for a file read repeatedly — so an atime-based LRU on `radio`
would silently degrade to something between LRU and FIFO, and the degradation would be invisible in
tests on a Windows dev machine where atime behaves differently again.

**Resolution:** recency is `LastWriteTimeUtc`, and `TryGetPath` **touches it on every hit**
(`File.SetLastWriteTimeUtc(path, DateTime.UtcNow)`). That is what makes it LRU rather than FIFO, it
is the same field `AlbumArtCacheService.CleanupExpired` already sweeps on, and it behaves
identically on both platforms. Task 4's code comments must say *why*, because "we used mtime for
LRU" reads like a mistake until you know atime was the mistake.

**C-17 — the only in-tree disk cache is an anti-precedent in three specific ways, and the ADR points
at it.** §5.3 calls `./data/gvmedia/` a *"sibling of the existing `./data/albumart/`"*, which is true
of the location and misleading about the implementation.

- **`AlbumArtCacheService` has no size or count bound at all.** Its only reclamation is a 7-day TTL
  sweep on a 6-hour `Timer` (`AlbumArtCacheService.cs:20-21, 160-201`). A repo-wide grep for
  `MaxCacheBytes|MaxSizeBytes|LRU|MaxCacheSize` across `src/` returns **zero** hits. There is no
  bounded cache in this codebase to copy.
- **Its cache directory is hardcoded in the constructor** (`:27`, `Path.Combine(".", "data", "albumart")`)
  with no injection point — so `AlbumArtCacheServiceTests` creates a temp dir the service never
  uses, and its `Dispose` scrubs the **real** `./data/albumart` by deleting anything written in the
  last minute, inside a bare `catch { }`. **Do not repeat this.** `GvMediaCache` takes its directory
  from options, and its tests are hermetic.
- **It constructs `new HttpClient()` directly** (`:30`) rather than using the factory.

**Resolution:** the eviction shape to copy is **`DiagnosticCaptureService.PruneCaptureDirectory`**
(`src/Radio.Infrastructure/Audio/Diagnostics/DiagnosticCaptureService.cs:254`) — `internal static`,
takes the directory and the caps as parameters explicitly *"so it can be unit-tested against a temp
dir"* (`:252`), applies its caps in order, and swallows per-file failures. `Radio.Infrastructure`
already grants `InternalsVisibleTo` to `Radio.Infrastructure.Tests`
(`Radio.Infrastructure.csproj:15`).

**C-18 — mapping every non-2xx to one exception would re-file a bug the queue already carries
twice.** ADR §5 says `GetVoicemailFileAsync` *"throws `GvMediaUnavailableException` on 5xx/timeout"*
and says nothing about 404 or 401. `GV-6` and `GV-8` are both open rows whose shared root shape is,
verbatim from the queue, *"`GvBridgeApiService` maps every non-2xx to `null`, destroying the
distinction the caller needs."* A 404 (this recording is gone — permanent) and a 502 (GV auth is in
its blackout — retry in a few minutes) demand opposite responses from PR 3's UI, and a 401 means the
`AuthKey` in §10.2's table is wrong, which is the single failure mode the ADR names for D8.

**Resolution:** `GvMediaUnavailableException` carries a `GvMediaFailure` reason
(`Disabled`, `NotFound`, `Unauthorized`, `Upstream`, `Timeout`, `Transport`, `TooLarge`) and an
`IsPermanent` predicate. This is an addition to the ADR's sketch, not a contradiction of it.

**C-19 — line citations have drifted again, in both directions.** PR 1's C-9 corrected ADR §5's
`AudioServiceExtensions.cs:435` to `:450`; against `c830fb8` it is **`:469`**. Similarly
`VoicemailItemDto.DurationSeconds` is cited as `ApiModels.cs:1128` by ADR §4.1 and `:1127` by PR 1's
self-review. **Builder must grep for the symbol, never trust a line number in any of these
documents**, this plan included. Content behind every citation used here was re-verified.

**C-20 — "ADR §17" does not exist.** The `MaxSpeechChars` truncation instruction has been cited as
"§4.2/§17". ADR-029's last numbered section is **§15**, followed by an unnumbered *Handoff*. The two
real sites are **§4.2** (line 217) and **§10.2**'s config table (line 602, *"speech truncation
(§4.2)"*). Recorded so a reader who greps for §17, finds nothing, and concludes the truncation
instruction was withdrawn does not do so — ⓵ above overrides it deliberately, not by accident.

### 0.5 What this row is NOT

1. ⛔ **No endpoint.** `POST /api/audio/events` is **PR 3**. Nothing in this PR is reachable over
   HTTP from outside the box.
2. ⛔ **No `EventPlaybackService`, no `playbackId` mapping, no TTS.** All **PR 3**.
3. ⛔ **No `DuckingService` change**, and **do not touch `DuckingServiceCharacterizationTests`** —
   PR 1 added those four tests specifically so PR 4's behavioural change is a visible diff rather
   than a silent shift inside a shared audio service. **PR 4 updates them; nobody deletes them.**
   All four pass as written, so PR 4's premise is intact.
4. ⛔ **Do NOT use `POST /api/sources/events/{tts,file}` as a template for anything.** Re-verified
   against `c830fb8`, all three defects are still live at the cited lines: `SourcesController.cs`
   declares `_duckingService` at `:29`, takes it at `:44`, assigns it at `:55` and **never reads it
   again** (three occurrences in the file — those events do not duck); `mixer.AddSource(ttsSource)`
   at `:651` has no reachable `RemoveSource` or `Dispose` (the file's only `RemoveSource` is `:727`,
   in the **file** branch — it leaks per play); and `PlayFileEvent` calls `PlayFileAsync` at `:719`
   and then `fileSource.PlayAsync` at `:732`, which re-enters `PlayFileAsync` **under a different
   key** — it double-plays.
5. ⛔ **No `Label` cap, no `LabelTooLong`.** §0.3 ⓷ — PR 3.
6. ⛔ **No change to `EventPlaybackRequest.Validate`.** Task 12 is a doc-comment correction on
   `IEventPlaybackService.SeekAsync` and touches nothing else in that file.
7. ⛔ **No fix to `PhoneContactLookupService`'s masking.** It is a live path with its own callers and
   is not this arc's; §0.3 ⓸ documents it, Task 13 logs it, and PR 2 does not edit it.
8. ⛔ **No `deploy/*/appsettings.Production.json` edit.** C-14.

### 0.6 ⚠ The DI hazard lands here, and nothing in this repo would catch it

PR 1 was exempt because it registered nothing. **PR 2 is the first PR in the arc that registers a
service**, and the failure mode is a service that will not start on an appliance in a cabinet.

The state of the guards, re-verified against `c830fb8`:

- `RotaryEncoderRegistrationTests` builds a **real** `ServiceProvider` and resolves from it — but
  only over `AddLogging()` + `AddRotaryEncoders(new ConfigurationBuilder().Build())`.
- `ActiveSourceAccessorRegistrationTests` covers `AddSoundFlowAudio` by **descriptor inspection
  only**, resolving from a hand-rolled minimal container, deliberately avoiding real audio hardware.
- **`ValidateOnBuild` and `ValidateScopes` appear nowhere in `src/` or `tests/`** — zero matches for
  either.

Task 11 adds the first. It is scoped to `AddLogging()` + `AddGvMedia(...)` and turns **both** flags
on, which is strictly stronger than a `GetRequiredService` probe. It is deliberately **not** applied
to the whole graph: that would fail on pre-existing hardware-touching registrations, which is a
separate row and not this one's to open. C-13's separate extension method is what makes the scoping
possible.

---

## 1. Tasks

Fourteen tasks. Tasks 1-3 are types and config; Tasks 4-9 are the cache, the handler and the client
with their tests; Tasks 10-11 are wiring and the guard; Tasks 12-14 are the doc correction, the docs
and the gate.

---

### Task 1 — `GvMediaOptions`

**File (new):** `src/Radio.Core/Configuration/GvMediaOptions.cs`

Convention note, verified: **`Radio.Infrastructure` contains zero options classes** — it owns the
binding, never the type. Twenty of them live in `src/Radio.Core/Configuration/`, and all 28 in the
solution declare `public const string SectionName`. This one goes there, beside
`PhoneIntegrationOptions`.

Every key from ADR §10.2 except the deleted `SpeechEngine` (⟨A1·1⟩, §9.5). Four of the nine are not
read by PR 2 — that is deliberate: the breakdown assigns *"Radio.API gains its own `GvMedia` config
block"* to this PR, and shipping the block once beats editing `appsettings.json` in three separate
PRs. **Each key's XML doc names its consuming PR**, so an unread key is not mistaken for a dead one.

```csharp
namespace Radio.Core.Configuration;

/// <summary>
/// Server-side GV media fetch, caching and event-playback limits (ADR-029 D8, §10.2).
///
/// <para>
/// This section deliberately does NOT reuse <c>PhoneIntegration:ContactsApiBaseUrl</c>, even though
/// both point at the same host today: that key means "where the contacts API is", and overloading
/// it would couple two features that can be deployed and disabled independently
/// (<c>PhoneIntegration:Enabled</c> is <c>false</c> and has never been true).
/// </para>
/// </summary>
public sealed class GvMediaOptions
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "GvMedia";

  /// <summary>
  /// Master gate for the RemoteMedia arm. Consumed by PR 3's EventPlaybackService and, in this PR,
  /// by GvMediaClient, which refuses to fetch when false.
  /// </summary>
  public bool Enabled { get; set; } = false;

  /// <summary>Base URL of the gvbridge host. Consumed by GvMediaClient.</summary>
  public string BaseUrl { get; set; } = "http://radio:5004";

  /// <summary>
  /// Value for the X-RotaryPhone-Auth header. Empty means no header is sent, which matches the
  /// current LAN-only posture; set it when RotaryPhone's gate ships (ADR-022 §8.1, ADR-029 §10.1).
  /// This is the API-side twin of Radio.Web's RotaryPhone:Gv:AuthKey — two keys in one shared
  /// per-machine file, and a mismatch fails only as a 401 on voicemail playback, which is why
  /// GvMediaStartupCheck warns about it at boot.
  /// </summary>
  public string AuthKey { get; set; } = "";

  /// <summary>Where fetched recordings are written. Consumed by GvMediaCache.</summary>
  public string CacheDirectory { get; set; } = "./data/gvmedia";

  /// <summary>
  /// Cache cap in megabytes. 50 holds roughly 35-100 recordings — comfortably the whole visible
  /// list. 0 is the supported escape hatch and means NO CACHE: recordings are still written (a
  /// local path is what playback needs) but no fetch is ever served from disk, and a short-TTL
  /// sweep reclaims them. Choosing 0 re-exposes replay to the ~9-in-20-minute GV auth blackout
  /// (ADR-029 §5.3, ⟨A1·2⟩).
  /// </summary>
  public int CacheMaxMegabytes { get; set; } = 50;

  /// <summary>
  /// Hard cap on one attended playback. Consumed by PR 5 (ADR-029 D7 §7.1). In this PR it is used
  /// only to bound the download size and the no-cache sweep window.
  /// </summary>
  public int MaxPlaybackSeconds { get; set; } = 300;

  /// <summary>
  /// Cap on EventPlaybackRequest.Text, passed to Validate by PR 3's controller.
  /// ⚠ The behaviour is REJECTION, not truncation: over-length text is refused as
  /// EventPlaybackRejection.TextTooLong and mapped to a 400 with that reason. ADR-029 §4.2 says
  /// "truncated with a spoken tail"; that is overridden, because §4.2's own rule is that utterance
  /// composition belongs to Radio.Web, and a server that silently speaks less than it was asked to
  /// while returning 200 is the same untruth PR 1 refused when it made a non-seekable SeekAsync
  /// throw rather than no-op. Radio.Web truncates visibly before sending (PHN-3).
  /// </summary>
  public int MaxSpeechChars { get; set; } = 1000;

  /// <summary>
  /// Priority at or above which a starting source preempts attended playback. Consumed by PR 4
  /// (ADR-029 D5 §6.1). Not read by this PR.
  /// </summary>
  public int PreemptAtPriority { get; set; } = 8;

  /// <summary>HTTP timeout for one media fetch. Consumed by GvMediaClient.</summary>
  public int FetchTimeoutSeconds { get; set; } = 15;
}
```

**Acceptance:** compiles; `Radio.Core` gains no new package reference (it already has
`Microsoft.Extensions.Options`, and this type needs nothing beyond a POCO).

---

### Task 2 — the `GvMedia` block in `src/Radio.API/appsettings.json`

**File:** `src/Radio.API/appsettings.json`

House style, verified: 2-space indent, PascalCase keys, no blank lines between sections, integration
sections at the tail. `PhoneIntegration` (`:258-267`) is currently the last block. Append after it.

Replace the file's final two lines:

```json
    "ReconnectMaxDelayMs": 30000
  }
}
```

with:

```json
    "ReconnectMaxDelayMs": 30000
  },
  "GvMedia": {
    "Enabled": false,
    "BaseUrl": "http://radio:5004",
    "AuthKey": "",
    "CacheDirectory": "./data/gvmedia",
    "CacheMaxMegabytes": 50,
    "MaxPlaybackSeconds": 300,
    "MaxSpeechChars": 1000,
    "PreemptAtPriority": 8,
    "FetchTimeoutSeconds": 15
  }
}
```

⚠ **Assert the comma** on the `PhoneIntegration` closing brace — that is the one edit here that can
break startup, and JSON gives no warning at build time.

**Acceptance:** `dotnet build` succeeds and the file parses. `./data/gvmedia` matches the `./data/…`
form used by `Database:RootPath`, `Metrics:DatabasePath` and `Fingerprinting:DatabasePath`, and the
`Path.Combine(".", "data", …)` form `AlbumArtCacheService.cs:27` builds.

⛔ **Do not touch `deploy/debian-x64/appsettings.Production.json` or the Pi one.** C-14.

---

### Task 3 — the failure taxonomy

**File (new):** `src/Radio.Infrastructure/External/GvMediaUnavailableException.cs`

Both types in one file, matching the repo's habit of keeping an enum beside the single type that
gives it meaning.

```csharp
namespace Radio.Infrastructure.External;

/// <summary>
/// Why a GV media fetch could not produce a local file.
///
/// <para>
/// This enum exists because collapsing every failure into one exception is a bug this repo already
/// carries twice: GV-6 and GV-8 are both open rows whose shared root shape is "maps every non-2xx
/// to null, destroying the distinction the caller needs." A 404 (the recording is gone) and a 502
/// (GV auth is inside its ~9-minutes-in-20 blackout) demand opposite responses from the UI.
/// </para>
/// </summary>
public enum GvMediaFailure
{
  /// <summary>No reason was supplied. Never thrown deliberately.</summary>
  Unknown = 0,

  /// <summary>GvMedia:Enabled is false. No request was made.</summary>
  Disabled,

  /// <summary>The provider returned 404 — the recording does not exist. Retrying will not help.</summary>
  NotFound,

  /// <summary>
  /// The provider returned 401 or 403. On this box that most likely means GvMedia:AuthKey and
  /// RotaryPhone's expected key have diverged — see GvMediaStartupCheck.
  /// </summary>
  Unauthorized,

  /// <summary>Any other non-success status, 5xx included. Usually the GV auth blackout; retryable.</summary>
  Upstream,

  /// <summary>The fetch exceeded GvMedia:FetchTimeoutSeconds. Retryable.</summary>
  Timeout,

  /// <summary>DNS, connection or TLS failure below HTTP. Retryable.</summary>
  Transport,

  /// <summary>The response exceeded the size bound derived from GvMedia:MaxPlaybackSeconds.</summary>
  TooLarge
}

/// <summary>
/// Thrown by <see cref="GvMediaClient"/> when it cannot produce a local file for a recording.
/// </summary>
/// <remarks>
/// ⚠ <see cref="Exception.Message"/> is masked: it carries the hashed id form, never the raw
/// media id. Callers log exceptions, and an unmasked message would leak through every catch block
/// in the arc — including ones not written yet.
/// </remarks>
public sealed class GvMediaUnavailableException : Exception
{
  public GvMediaUnavailableException(GvMediaFailure reason, string message, Exception? innerException = null)
    : base(message, innerException)
  {
    Reason = reason;
  }

  /// <summary>Why the fetch failed.</summary>
  public GvMediaFailure Reason { get; }

  /// <summary>
  /// True when retrying the same request cannot succeed. Consumed by PR 3 to choose between a
  /// retryable error and a terminal one; false for the whole blackout class, which is the case the
  /// cache exists to mitigate.
  /// </summary>
  public bool IsPermanent => Reason is GvMediaFailure.NotFound or GvMediaFailure.Disabled;
}
```

**Acceptance:** compiles; nothing references it yet.

---

### Task 4 — `GvMediaCache`: the bounded LRU that actually deletes

**File (new):** `src/Radio.Infrastructure/External/GvMediaCache.cs`

This is the largest task and the one to review hardest. Read §0.2, C-15, C-16 and C-17 first.

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.External;

/// <summary>
/// A size-bounded, least-recently-used disk cache for fetched GV recordings (ADR-029 D3 §5.3).
///
/// <para>
/// ⚠ This is blackout mitigation, not an optimisation. GV auth is dead roughly 9 minutes in every
/// 20 (punch list XR-3), so a replay 30 seconds later has roughly a 45% chance of 502ing if it goes
/// back to the network. A hit here never touches the network, which is the property that makes
/// replay reliable on a wall clock the user cannot see.
/// </para>
///
/// <para>
/// The cost, owner-accepted at ADR-029 ⟨A1·2⟩: private voicemail audio now sits at rest on disk,
/// where previously it only streamed through a browser. That is why the cap is real, the directory
/// lives under ./data/, and eviction deletes rather than marks.
/// </para>
///
/// <para>
/// Not modelled on <c>AlbumArtCacheService</c>, deliberately: that cache has no size or count bound
/// at all, and hardcodes its directory in the constructor, which is why its own tests scrub the
/// live ./data/albumart directory. The directory here comes from options so tests are hermetic, and
/// the evictor follows <c>DiagnosticCaptureService.PruneCaptureDirectory</c>'s internal-static shape.
/// </para>
/// </summary>
public sealed class GvMediaCache
{
  private readonly ILogger<GvMediaCache> _logger;
  private readonly IOptionsMonitor<GvMediaOptions> _options;
  private readonly SemaphoreSlim _writeLock = new(1, 1);

  public GvMediaCache(ILogger<GvMediaCache> logger, IOptionsMonitor<GvMediaOptions> options)
  {
    _logger = logger;
    _options = options;
  }

  /// <summary>
  /// True when a fetch may be served from disk. False at CacheMaxMegabytes = 0, where recordings
  /// are still written — playback needs a path — but never read back (ADR-029 ⟨A1·2⟩: a 0 cap is a
  /// no-cache path, not an infinitely-evicting one).
  /// </summary>
  public bool RetainsEntries => _options.CurrentValue.CacheMaxMegabytes > 0;

  /// <summary>
  /// The cache filename for a media id: 32 hex characters of SHA-256, plus ".mp3".
  /// </summary>
  /// <remarks>
  /// Hashed rather than used raw, even though EventPlaybackRequest.ValidateMediaId already
  /// allow-lists the id to [A-Za-z0-9._~-]. Three reasons the allow-list does not cover:
  /// Windows reserved device names (CON, NUL, PRN, AUX, COM1, ...) are allow-list-clean and are not
  /// creatable as files; a case-insensitive filesystem would collide two ids differing only in
  /// case; and this stays correct if the allow-list is ever loosened for a real GV id that needs it.
  /// The same hash's first 8 characters are the log mask, so a log line and a file on disk
  /// correlate without either carrying the id.
  /// </remarks>
  internal static string FileNameFor(string mediaId)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(mediaId));
    return string.Concat(Convert.ToHexString(hash, 0, 16).ToLowerInvariant(), ".mp3");
  }

  /// <summary>The 8-character log mask for a media id. Same hash as <see cref="FileNameFor"/>.</summary>
  internal static string MaskFor(string mediaId)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(mediaId));
    return string.Concat("gvm:", Convert.ToHexString(hash, 0, 4).ToLowerInvariant());
  }

  /// <summary>
  /// Returns the path of a cached recording, or null when there is no usable hit.
  /// </summary>
  /// <remarks>
  /// Always null when CacheMaxMegabytes is 0 — that is what "no cache" means here.
  ///
  /// On a hit the file's LastWriteTimeUtc is touched, which is what makes eviction LRU rather than
  /// FIFO. ⚠ Access time is NOT used and must not be substituted: Linux mounts default to relatime,
  /// which updates atime at most once a day for a repeatedly-read file, so an atime-based LRU would
  /// silently degrade on the appliance while behaving differently again on a Windows dev machine.
  /// A failed touch is not a failed hit — the entry is still served, it just keeps its old
  /// eviction rank.
  /// </remarks>
  public string? TryGetPath(string mediaId)
  {
    var options = _options.CurrentValue;
    if (options.CacheMaxMegabytes <= 0)
    {
      return null;
    }

    var path = Path.Combine(options.CacheDirectory, FileNameFor(mediaId));
    if (!File.Exists(path))
    {
      return null;
    }

    try
    {
      File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not touch cache entry {MaskedId}; it keeps its old eviction rank",
        MaskFor(mediaId));
    }

    return path;
  }

  /// <summary>
  /// Writes a fetched recording and returns its path. Reclamation runs after the write, so a fetch
  /// never fails because reclamation did.
  /// </summary>
  public async Task<string> WriteAsync(string mediaId, byte[] content, CancellationToken cancellationToken)
  {
    var options = _options.CurrentValue;
    var directory = options.CacheDirectory;
    var path = Path.Combine(directory, FileNameFor(mediaId));

    await _writeLock.WaitAsync(cancellationToken);
    try
    {
      Directory.CreateDirectory(directory);
      await File.WriteAllBytesAsync(path, content, cancellationToken);

      if (options.CacheMaxMegabytes > 0)
      {
        EvictToCap(directory, (long)options.CacheMaxMegabytes * 1024L * 1024L, path, _logger);
      }
      else
      {
        var window = TimeSpan.FromSeconds(Math.Max(60, options.MaxPlaybackSeconds * 2));
        SweepOlderThan(directory, window, path, _logger);
      }
    }
    finally
    {
      _writeLock.Release();
    }

    return path;
  }

  /// <summary>
  /// Deletes least-recently-used entries until the directory fits inside <paramref name="maxBytes"/>.
  /// </summary>
  /// <remarks>
  /// ⚠ <paramref name="protectedPath"/> is never deleted. It is the file the caller is about to
  /// play, and deleting it here would make a successful fetch unplayable. The consequence is stated
  /// rather than hidden: when the protected file ALONE exceeds the cap, this method leaves the cap
  /// violated by that one file, logs it, and the overage is corrected on the next write, when the
  /// file is no longer protected. The overage is bounded by one recording — with
  /// MaxPlaybackSeconds = 300 the download bound is ~9.6 MB against a 50 MB cap.
  ///
  /// Recency is LastWriteTimeUtc, touched on read by TryGetPath. See that method for why not atime.
  ///
  /// internal static, taking its directory and cap as parameters, so it can be unit-tested against
  /// a temp dir — the shape of DiagnosticCaptureService.PruneCaptureDirectory.
  /// </remarks>
  internal static void EvictToCap(string directory, long maxBytes, string? protectedPath, ILogger logger)
  {
    if (!Directory.Exists(directory))
    {
      return;
    }

    var entries = new List<FileInfo>();
    foreach (var file in Directory.EnumerateFiles(directory))
    {
      try
      {
        entries.Add(new FileInfo(file));
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "Could not stat cache entry {File}", file);
      }
    }

    var total = entries.Sum(e => e.Length);
    if (total <= maxBytes)
    {
      return;
    }

    var removed = 0;
    foreach (var entry in entries.OrderBy(e => e.LastWriteTimeUtc))
    {
      if (total <= maxBytes)
      {
        break;
      }
      if (protectedPath is not null
          && string.Equals(entry.FullName, Path.GetFullPath(protectedPath), StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      var length = entry.Length;
      try
      {
        entry.Delete();
        total -= length;
        removed++;
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "Could not evict cache entry {File}", entry.Name);
      }
    }

    if (removed > 0)
    {
      logger.LogInformation(
        "GV media cache: evicted {Count} entries, now {Bytes} bytes against a {Cap} byte cap",
        removed, total, maxBytes);
    }

    if (total > maxBytes)
    {
      logger.LogWarning(
        "GV media cache is {Bytes} bytes against a {Cap} byte cap; the entry in flight is exempt "
        + "from eviction and the overage is corrected on the next write",
        total, maxBytes);
    }
  }

  /// <summary>
  /// The CacheMaxMegabytes = 0 reclamation: deletes entries older than <paramref name="window"/>.
  /// Nothing is evicted per write, so this is a short-retention sweep rather than the
  /// "infinitely-evicting" behaviour ADR-029 ⟨A1·2⟩ forbids.
  /// </summary>
  internal static void SweepOlderThan(string directory, TimeSpan window, string? protectedPath, ILogger logger)
  {
    if (!Directory.Exists(directory))
    {
      return;
    }

    var cutoff = DateTime.UtcNow - window;
    var removed = 0;

    foreach (var file in Directory.EnumerateFiles(directory))
    {
      if (protectedPath is not null
          && string.Equals(file, Path.GetFullPath(protectedPath), StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      try
      {
        var info = new FileInfo(file);
        if (info.LastWriteTimeUtc < cutoff)
        {
          info.Delete();
          removed++;
        }
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "Could not sweep cache entry {File}", file);
      }
    }

    if (removed > 0)
    {
      logger.LogDebug("GV media cache (no-cache mode): swept {Count} expired entries", removed);
    }
  }
}
```

⚠ **Builder: `Path.GetFullPath(protectedPath)` is compared against `entry.FullName` and against the
`Directory.EnumerateFiles` result. Confirm both sides are absolute before trusting the comparison —
`EnumerateFiles` returns paths rooted the same way its argument was, so a relative
`CacheDirectory` (which is the default, `./data/gvmedia`) yields relative entries.** If that
comparison ever fails to match, the protected file gets deleted and a successful fetch becomes
unplayable — a silent failure. Normalise both sides with `Path.GetFullPath` and **add a test that
exercises the protection with a relative directory**, not only an absolute one. This is the single
most likely defect in this task.

**Acceptance:** compiles; no DI registration yet.

---

### Task 5 — `GvMediaCache` tests

**File (new):** `tests/Radio.Infrastructure.Tests/External/GvMediaCacheTests.cs`

Hermetic: every test gets its own temp directory, and **nothing writes to `./data/gvmedia`**. The
class implements `IDisposable` and deletes its own directory — contrast `AlbumArtCacheServiceTests`,
which scrubs the live cache in a bare `catch { }` (C-17).

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

/// <summary>
/// Cache behaviour for ADR-029 D3 §5.3. These are not performance tests: the cache is blackout
/// mitigation, so "a hit never touches the network" and "the cap really deletes" are correctness.
/// </summary>
public sealed class GvMediaCacheTests : IDisposable
{
  private readonly string _dir = Path.Combine(
    Path.GetTempPath(), "gvmedia-tests-" + Guid.NewGuid().ToString("n"));

  private GvMediaCache CreateCache(int capMegabytes, int maxPlaybackSeconds = 300)
  {
    var options = new GvMediaOptions
    {
      CacheDirectory = _dir,
      CacheMaxMegabytes = capMegabytes,
      MaxPlaybackSeconds = maxPlaybackSeconds
    };
    return new GvMediaCache(
      NullLogger<GvMediaCache>.Instance,
      new StaticOptionsMonitor<GvMediaOptions>(options));
  }

  public void Dispose()
  {
    if (Directory.Exists(_dir))
    {
      Directory.Delete(_dir, recursive: true);
    }
  }

  [Fact]
  public void FileNameFor_HashesRatherThanUsingTheIdVerbatim()
  {
    // A Windows reserved device name is allow-list-clean under ValidateMediaId but is not a
    // creatable filename. Hashing is what makes that irrelevant.
    var name = GvMediaCache.FileNameFor("CON");

    Assert.DoesNotContain("CON", name, StringComparison.OrdinalIgnoreCase);
    Assert.EndsWith(".mp3", name, StringComparison.Ordinal);
    Assert.Equal(36, name.Length); // 32 hex + ".mp3"
  }

  [Fact]
  public void FileNameFor_DistinguishesIdsDifferingOnlyByCase()
  {
    Assert.NotEqual(GvMediaCache.FileNameFor("abc"), GvMediaCache.FileNameFor("ABC"));
  }

  [Fact]
  public async Task WriteThenTryGet_ReturnsTheSamePath()
  {
    var cache = CreateCache(capMegabytes: 50);

    var written = await cache.WriteAsync("vm-1", new byte[1024], CancellationToken.None);
    var found = cache.TryGetPath("vm-1");

    Assert.NotNull(found);
    Assert.Equal(Path.GetFullPath(written), Path.GetFullPath(found!));
  }

  [Fact]
  public async Task TryGetPath_AlwaysMisses_WhenTheCapIsZero()
  {
    // ADR-029 ⟨A1·2⟩: 0 is a no-cache path. The file is still written — playback needs a path —
    // but it is never served back, so replay goes to the network and is exposed to the blackout.
    var cache = CreateCache(capMegabytes: 0);

    var written = await cache.WriteAsync("vm-1", new byte[1024], CancellationToken.None);

    Assert.True(File.Exists(written));
    Assert.Null(cache.TryGetPath("vm-1"));
  }

  [Fact]
  public void EvictToCap_DeletesOldestFirstUntilItFits()
  {
    Directory.CreateDirectory(_dir);
    var oldest = WriteFile("a.mp3", 400, DateTime.UtcNow.AddMinutes(-30));
    var middle = WriteFile("b.mp3", 400, DateTime.UtcNow.AddMinutes(-20));
    var newest = WriteFile("c.mp3", 400, DateTime.UtcNow.AddMinutes(-10));

    GvMediaCache.EvictToCap(_dir, maxBytes: 900, protectedPath: null, NullLogger.Instance);

    Assert.False(File.Exists(oldest));
    Assert.True(File.Exists(middle));
    Assert.True(File.Exists(newest));
  }

  [Fact]
  public void EvictToCap_ActuallyDeletesFromDisk()
  {
    // ADR-029 §5.3 is explicit that eviction must really delete, because the cost being accepted is
    // private audio at rest.
    Directory.CreateDirectory(_dir);
    WriteFile("a.mp3", 4096, DateTime.UtcNow.AddHours(-1));

    GvMediaCache.EvictToCap(_dir, maxBytes: 1, protectedPath: null, NullLogger.Instance);

    Assert.Empty(Directory.EnumerateFiles(_dir));
  }

  [Fact]
  public void EvictToCap_NeverDeletesTheProtectedEntry_EvenWhenItAloneExceedsTheCap()
  {
    // The stated, bounded cap violation. Deleting the file the caller is about to play would turn a
    // successful fetch into an unplayable one.
    Directory.CreateDirectory(_dir);
    var inFlight = WriteFile("new.mp3", 4096, DateTime.UtcNow);

    GvMediaCache.EvictToCap(_dir, maxBytes: 1, protectedPath: inFlight, NullLogger.Instance);

    Assert.True(File.Exists(inFlight));
  }

  [Fact]
  public async Task WriteAsync_ProtectsTheNewEntry_WhenCacheDirectoryIsRelative()
  {
    // Task 4's most likely defect: CacheDirectory defaults to a RELATIVE path, and the protection
    // check compares against Path.GetFullPath. If the two sides are rooted differently the file
    // just written is evicted and the fetch silently becomes unplayable.
    var relative = Path.Combine(".", "gvmedia-rel-" + Guid.NewGuid().ToString("n"));
    try
    {
      var options = new GvMediaOptions { CacheDirectory = relative, CacheMaxMegabytes = 1 };
      var cache = new GvMediaCache(
        NullLogger<GvMediaCache>.Instance, new StaticOptionsMonitor<GvMediaOptions>(options));

      Directory.CreateDirectory(relative);
      File.WriteAllBytes(Path.Combine(relative, "old.mp3"), new byte[900 * 1024]);
      File.SetLastWriteTimeUtc(Path.Combine(relative, "old.mp3"), DateTime.UtcNow.AddHours(-1));

      var written = await cache.WriteAsync("vm-1", new byte[900 * 1024], CancellationToken.None);

      Assert.True(File.Exists(written));
    }
    finally
    {
      if (Directory.Exists(relative))
      {
        Directory.Delete(relative, recursive: true);
      }
    }
  }

  [Fact]
  public void SweepOlderThan_RemovesExpired_AndKeepsRecentAndProtected()
  {
    Directory.CreateDirectory(_dir);
    var stale = WriteFile("stale.mp3", 100, DateTime.UtcNow.AddHours(-2));
    var fresh = WriteFile("fresh.mp3", 100, DateTime.UtcNow);
    var inFlight = WriteFile("inflight.mp3", 100, DateTime.UtcNow.AddHours(-2));

    GvMediaCache.SweepOlderThan(_dir, TimeSpan.FromMinutes(10), inFlight, NullLogger.Instance);

    Assert.False(File.Exists(stale));
    Assert.True(File.Exists(fresh));
    Assert.True(File.Exists(inFlight));
  }

  private string WriteFile(string name, int bytes, DateTime lastWriteUtc)
  {
    var path = Path.Combine(_dir, name);
    File.WriteAllBytes(path, new byte[bytes]);
    File.SetLastWriteTimeUtc(path, lastWriteUtc);
    return Path.GetFullPath(path);
  }
}

/// <summary>
/// Minimal IOptionsMonitor over a fixed value. The repo has no shared helper for this — every test
/// that needs one builds it inline (see ActiveSourceAccessorRegistrationTests' BuildOptionsMonitor).
/// </summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
  public StaticOptionsMonitor(T value) => CurrentValue = value;

  public T CurrentValue { get; }

  public T Get(string? name) => CurrentValue;

  public IDisposable? OnChange(Action<T, string?> listener) => null;
}
```

⚠ **`EvictToCap`'s ordering test depends on `LastWriteTimeUtc` being distinguishable.** The three
files are stamped 10 minutes apart, so filesystem timestamp granularity is not a factor. Do not
rewrite it to rely on write order.

**Acceptance:** all facts pass; `./data/gvmedia` does not exist after the run.

---

### Task 6 — `GvMediaAuthHandler`

**File (new):** `src/Radio.Infrastructure/External/GvMediaAuthHandler.cs`

ADR §10.1 offers *"extract the handler to a shared location or add a small copy"*. **A copy, and
here is the trade:** `Radio.Web`'s handler reads a different key (`RotaryPhone:Gv:AuthKey`) from raw
`IConfiguration` and lives in a Web namespace. Extracting 31 lines to a shared project to serve two
consumers that read different keys buys nothing and couples two services' configuration shapes. The
copy differs in one deliberate way: it reads `IOptionsMonitor<GvMediaOptions>` rather than raw
`IConfiguration`, matching the options convention every other consumer in this arc uses, while
keeping the same per-request read that makes the key flippable without a restart.

```csharp
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.External;

/// <summary>
/// Adds X-RotaryPhone-Auth to outbound GV media requests when GvMedia:AuthKey is non-empty
/// (ADR-029 D8 §10.1). Empty today, which matches the current LAN-only posture.
///
/// <para>
/// This handler is the mechanism that closes carried risk #3. GvBridgeApiService.GetVoicemailAudioUrl
/// only ever BUILDS a string that the browser then fetches, so no DelegatingHandler can touch it —
/// which is why browser-side voicemail playback would break the moment RotaryPhone's gate flips on.
/// Once Radio.API fetches the audio itself, through this handler, the constraint dissolves.
/// </para>
///
/// <para>
/// A copy of Radio.Web's RotaryPhoneAuthHandler rather than a shared extraction: the two read
/// different configuration keys, and 31 shared lines are not worth coupling the two services'
/// configuration shapes. It reads IOptionsMonitor rather than raw IConfiguration, per request, so
/// the key stays flippable without a restart.
/// </para>
/// </summary>
public sealed class GvMediaAuthHandler : DelegatingHandler
{
  private const string HeaderName = "X-RotaryPhone-Auth";
  private readonly IOptionsMonitor<GvMediaOptions> _options;

  public GvMediaAuthHandler(IOptionsMonitor<GvMediaOptions> options)
  {
    _options = options;
  }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var key = _options.CurrentValue.AuthKey;
    if (!string.IsNullOrEmpty(key) && !request.Headers.Contains(HeaderName))
    {
      request.Headers.Add(HeaderName, key);
    }
    return base.SendAsync(request, cancellationToken);
  }
}
```

**Acceptance:** compiles.

---

### Task 7 — `GvMediaAuthHandler` tests

**File (new):** `tests/Radio.Infrastructure.Tests/External/GvMediaAuthHandlerTests.cs`

Mirrors `tests/Radio.Web.Tests/Services/RotaryPhoneAuthHandlerTests.cs` — three facts over a
capturing inner handler, constructing the handler directly rather than through DI.

```csharp
using System.Net;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

public class GvMediaAuthHandlerTests
{
  private const string HeaderName = "X-RotaryPhone-Auth";

  private sealed class CapturingHandler : HttpMessageHandler
  {
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      LastRequest = request;
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
  }

  private static async Task<HttpRequestMessage> SendThrough(string authKey)
  {
    var inner = new CapturingHandler();
    var handler = new GvMediaAuthHandler(
      new StaticOptionsMonitor<GvMediaOptions>(new GvMediaOptions { AuthKey = authKey }))
    {
      InnerHandler = inner
    };

    using var client = new HttpClient(handler);
    await client.GetAsync("http://radio:5004/api/gvbridge/voicemail/abc/audio");

    Assert.NotNull(inner.LastRequest);
    return inner.LastRequest!;
  }

  [Fact]
  public async Task NoHeader_WhenKeyIsEmpty()
  {
    // The shipping default. A header sent against a service that does not expect one is not
    // harmless: it is the kind of difference that makes a cross-repo bug report ambiguous.
    var request = await SendThrough("");

    Assert.False(request.Headers.Contains(HeaderName));
  }

  [Fact]
  public async Task AddsHeader_WhenKeyIsSet()
  {
    var request = await SendThrough("s3cret");

    Assert.True(request.Headers.Contains(HeaderName));
    Assert.Equal("s3cret", Assert.Single(request.Headers.GetValues(HeaderName)));
  }

  [Fact]
  public async Task DoesNotDuplicate_WhenTheHeaderIsAlreadyPresent()
  {
    var inner = new CapturingHandler();
    var handler = new GvMediaAuthHandler(
      new StaticOptionsMonitor<GvMediaOptions>(new GvMediaOptions { AuthKey = "s3cret" }))
    {
      InnerHandler = inner
    };

    using var client = new HttpClient(handler);
    using var message = new HttpRequestMessage(HttpMethod.Get, "http://radio:5004/x");
    message.Headers.Add(HeaderName, "already-there");
    await client.SendAsync(message);

    Assert.Equal("already-there", Assert.Single(inner.LastRequest!.Headers.GetValues(HeaderName)));
  }
}
```

**Acceptance:** three facts pass. `StaticOptionsMonitor<T>` comes from Task 5's file — both test
files are in `namespace Radio.Infrastructure.Tests.External`, so no `using` is needed.

---

### Task 8 — `GvMediaClient`

**File (new):** `src/Radio.Infrastructure/External/GvMediaClient.cs`

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.External;

/// <summary>
/// Fetches GV recordings server-side into a local file (ADR-029 D3 §5).
///
/// <para>
/// Shaped after PhoneContactLookupService — same folder, same host, typed HttpClient, options via
/// IOptionsMonitor — with two deliberate differences. It does NOT degrade silently to a fallback
/// value: a caller that asked for audio and got none needs to know why, so failures throw a
/// GvMediaUnavailableException carrying a reason. And its masking is stricter: see MaskedId.
/// </para>
/// </summary>
public sealed class GvMediaClient
{
  /// <summary>
  /// Bytes per second assumed when bounding a download. GV voicemail is MP3 at roughly 64 kbps
  /// (~8 000 B/s); 32 000 leaves four times that headroom while still bounding the read, so a
  /// misbehaving or hostile upstream cannot exhaust memory on an N100 that already correlates
  /// CPU pressure with audible distortion.
  /// </summary>
  private const int AssumedMaxBytesPerSecond = 32_000;

  private readonly ILogger<GvMediaClient> _logger;
  private readonly IOptionsMonitor<GvMediaOptions> _options;
  private readonly HttpClient _httpClient;
  private readonly GvMediaCache _cache;

  public GvMediaClient(
    ILogger<GvMediaClient> logger,
    IOptionsMonitor<GvMediaOptions> options,
    HttpClient httpClient,
    GvMediaCache cache)
  {
    _logger = logger;
    _options = options;
    _httpClient = httpClient;
    _cache = cache;
  }

  /// <summary>
  /// Returns a local path for a voicemail recording, fetching on a cache miss.
  /// </summary>
  /// <exception cref="GvMediaUnavailableException">
  /// The recording could not be produced. <see cref="GvMediaUnavailableException.Reason"/> says why.
  /// </exception>
  public async Task<string> GetVoicemailFileAsync(
    string voicemailId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(voicemailId);

    var options = _options.CurrentValue;
    var masked = GvMediaCache.MaskFor(voicemailId);

    if (!options.Enabled)
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Disabled, $"GvMedia is disabled; refusing to fetch {masked}.");
    }

    var cached = _cache.TryGetPath(voicemailId);
    if (cached is not null)
    {
      _logger.LogDebug("GV voicemail {MaskedId} served from cache", masked);
      return cached;
    }

    var uri = BuildVoicemailUri(options.BaseUrl, voicemailId, masked);
    var content = await FetchAsync(uri, options, masked, cancellationToken);

    var path = await _cache.WriteAsync(voicemailId, content, cancellationToken);
    _logger.LogInformation(
      "GV voicemail {MaskedId} fetched ({Bytes} bytes) and materialised", masked, content.Length);
    return path;
  }

  /// <summary>
  /// Builds the absolute fetch URI from the server's OWN configuration plus a media id.
  /// </summary>
  /// <remarks>
  /// ⚠ Deliberately NOT <c>new Uri(baseUri, mediaId)</c>, and deliberately not a relative request
  /// against HttpClient.BaseAddress. PR 1's review found that under RFC 3986 §4.2 a relative
  /// reference carrying a scheme is not relative at all — it resolves as ABSOLUTE — so
  /// "http:evil.example" would have escaped the configured host through exactly that call.
  /// EventPlaybackRequest.ValidateMediaId now allow-lists the id to [A-Za-z0-9._~-], which refuses
  /// ':' outright and closes that class. This method does not rely on that: it places the id in a
  /// path segment via UriBuilder, which cannot alter scheme or authority, and then COMPARES scheme
  /// and authority against the base rather than asserting that they cannot have changed.
  ///
  /// Uri.EscapeDataString is a no-op over the allow-listed set, and is applied anyway so this stays
  /// correct if the allow-list is ever loosened. It introduces no '%' for the allow-listed set, so
  /// UriBuilder.Path cannot double-escape.
  /// </remarks>
  internal static Uri BuildVoicemailUri(string baseUrl, string mediaId, string maskedId)
  {
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
        || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Transport, "GvMedia:BaseUrl is not an absolute http(s) URI.");
    }

    var builder = new UriBuilder(baseUri)
    {
      Path = $"/api/gvbridge/voicemail/{Uri.EscapeDataString(mediaId)}/audio",
      Query = string.Empty,
      Fragment = string.Empty
    };
    var candidate = builder.Uri;

    if (!string.Equals(candidate.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(candidate.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase))
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Transport,
        $"Refusing to fetch {maskedId} outside the configured GvMedia host.");
    }

    return candidate;
  }

  private async Task<byte[]> FetchAsync(
    Uri uri, GvMediaOptions options, string masked, CancellationToken cancellationToken)
  {
    HttpResponseMessage response;
    try
    {
      response = await _httpClient.GetAsync(
        uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
      // HttpClient surfaces its own timeout as a cancellation that the caller did not request.
      _logger.LogWarning(
        "GV voicemail {MaskedId} fetch timed out after {Seconds}s", masked, options.FetchTimeoutSeconds);
      throw new GvMediaUnavailableException(
        GvMediaFailure.Timeout, $"Timed out fetching {masked}.", ex);
    }
    catch (HttpRequestException ex)
    {
      _logger.LogWarning("GV voicemail {MaskedId} fetch failed below HTTP", masked);
      throw new GvMediaUnavailableException(
        GvMediaFailure.Transport, $"Transport failure fetching {masked}.", ex);
    }

    using (response)
    {
      if (!response.IsSuccessStatusCode)
      {
        var reason = (int)response.StatusCode switch
        {
          404 => GvMediaFailure.NotFound,
          401 or 403 => GvMediaFailure.Unauthorized,
          _ => GvMediaFailure.Upstream
        };

        // Warning rather than Debug: since LOG-11 the journal carries Warning and above, and a 502
        // here is the GV auth blackout, which is the thing an operator is most often diagnosing.
        _logger.LogWarning(
          "GV voicemail {MaskedId} fetch returned {StatusCode} ({Reason})",
          masked, (int)response.StatusCode, reason);

        throw new GvMediaUnavailableException(
          reason, $"Fetch of {masked} returned {(int)response.StatusCode}.");
      }

      var maxBytes = (long)Math.Max(1, options.MaxPlaybackSeconds) * AssumedMaxBytesPerSecond;

      if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
      {
        _logger.LogWarning(
          "GV voicemail {MaskedId} declared {Declared} bytes, over the {Max} byte bound",
          masked, declared, maxBytes);
        throw new GvMediaUnavailableException(
          GvMediaFailure.TooLarge, $"{masked} declared {declared} bytes, over the fetch bound.");
      }

      // Read with an explicit bound rather than ReadAsByteArrayAsync: Content-Length is advisory
      // and may be absent, so the bound has to hold while streaming too.
      await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var buffer = new MemoryStream();
      var chunk = new byte[81920];
      int read;
      while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
      {
        if (buffer.Length + read > maxBytes)
        {
          _logger.LogWarning(
            "GV voicemail {MaskedId} exceeded the {Max} byte bound while streaming", masked, maxBytes);
          throw new GvMediaUnavailableException(
            GvMediaFailure.TooLarge, $"{masked} exceeded the fetch bound while streaming.");
        }
        buffer.Write(chunk, 0, read);
      }

      if (buffer.Length == 0)
      {
        _logger.LogWarning("GV voicemail {MaskedId} fetch returned an empty body", masked);
        throw new GvMediaUnavailableException(
          GvMediaFailure.Upstream, $"Fetch of {masked} returned an empty body.");
      }

      return buffer.ToArray();
    }
  }
}
```

⚠ **Two comment-accuracy items for the reviewer, per `CLAUDE.md` § Pre-Merge Review:**

1. The `OperationCanceledException` filter claims it distinguishes an `HttpClient` timeout from a
   caller cancellation. **Verify that against the code, not the comment** — the filter is
   `!cancellationToken.IsCancellationRequested`, and a caller token cancelled *between* the throw
   and the filter would be misclassified. That is an accepted, narrow race; the comment must not
   claim it cannot happen.
2. `BuildVoicemailUri`'s remark says `UriBuilder` *"cannot alter scheme or authority"* and then
   compares them anyway. **That is deliberate belt-and-braces, not redundancy** — do not let a
   reviewer "simplify" the comparison away on the strength of the sentence above it.

**Acceptance:** compiles; no DI registration yet.

---

### Task 9 — `GvMediaClient` tests, including the masking pin

**File (new):** `tests/Radio.Infrastructure.Tests/External/GvMediaClientTests.cs`

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Configuration;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

public sealed class GvMediaClientTests : IDisposable
{
  private const string RawId = "vm-secret-identifier-9876";

  private readonly string _dir = Path.Combine(
    Path.GetTempPath(), "gvmedia-client-tests-" + Guid.NewGuid().ToString("n"));

  private readonly CapturingLoggerProvider _logs = new();

  public void Dispose()
  {
    if (Directory.Exists(_dir))
    {
      Directory.Delete(_dir, recursive: true);
    }
  }

  private sealed class StubHandler : HttpMessageHandler
  {
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public int Calls { get; private set; }

    public Uri? LastUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Calls++;
      LastUri = request.RequestUri;
      return Task.FromResult(_respond(request));
    }
  }

  private (GvMediaClient Client, StubHandler Handler) CreateClient(
    Func<HttpRequestMessage, HttpResponseMessage> respond,
    int capMegabytes = 50,
    bool enabled = true)
  {
    var options = new GvMediaOptions
    {
      Enabled = enabled,
      BaseUrl = "http://radio:5004",
      CacheDirectory = _dir,
      CacheMaxMegabytes = capMegabytes
    };
    var monitor = new StaticOptionsMonitor<GvMediaOptions>(options);
    var handler = new StubHandler(respond);
    var http = new HttpClient(handler);
    var cache = new GvMediaCache(NullLogger<GvMediaCache>.Instance, monitor);
    var client = new GvMediaClient(
      _logs.CreateLogger<GvMediaClient>(), monitor, http, cache);
    return (client, handler);
  }

  private static HttpResponseMessage Audio(byte[] bytes) =>
    new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

  [Fact]
  public async Task Fetch_WritesTheRecordingAndReturnsItsPath()
  {
    var (client, _) = CreateClient(_ => Audio(new byte[2048]));

    var path = await client.GetVoicemailFileAsync(RawId);

    Assert.True(File.Exists(path));
    Assert.Equal(2048, new FileInfo(path).Length);
  }

  [Fact]
  public async Task ASecondCall_IsServedFromCacheAndNeverTouchesTheNetwork()
  {
    // The blackout property (ADR-029 §5.3): GV auth is dead ~9 minutes in every 20, so a replay
    // that went back to the network would 502 roughly 45% of the time.
    var (client, handler) = CreateClient(_ => Audio(new byte[1024]));

    await client.GetVoicemailFileAsync(RawId);
    await client.GetVoicemailFileAsync(RawId);

    Assert.Equal(1, handler.Calls);
  }

  [Fact]
  public async Task ASecondCall_RefetchesWhenTheCacheIsDisabled()
  {
    var (client, handler) = CreateClient(_ => Audio(new byte[1024]), capMegabytes: 0);

    await client.GetVoicemailFileAsync(RawId);
    await client.GetVoicemailFileAsync(RawId);

    Assert.Equal(2, handler.Calls);
  }

  [Fact]
  public async Task Fetch_IsRefusedWhenGvMediaIsDisabled_WithoutAnyRequest()
  {
    var (client, handler) = CreateClient(_ => Audio(new byte[16]), enabled: false);

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.Disabled, ex.Reason);
    Assert.True(ex.IsPermanent);
    Assert.Equal(0, handler.Calls);
  }

  [Theory]
  [InlineData(HttpStatusCode.NotFound, GvMediaFailure.NotFound, true)]
  [InlineData(HttpStatusCode.Unauthorized, GvMediaFailure.Unauthorized, false)]
  [InlineData(HttpStatusCode.Forbidden, GvMediaFailure.Unauthorized, false)]
  [InlineData(HttpStatusCode.BadGateway, GvMediaFailure.Upstream, false)]
  [InlineData(HttpStatusCode.ServiceUnavailable, GvMediaFailure.Upstream, false)]
  public async Task StatusCodesMapToDistinctReasons(
    HttpStatusCode status, GvMediaFailure expected, bool permanent)
  {
    // GV-6 and GV-8 are both open rows for collapsing this distinction. A 404 and a 502 need
    // opposite responses from the UI.
    var (client, _) = CreateClient(_ => new HttpResponseMessage(status));

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(expected, ex.Reason);
    Assert.Equal(permanent, ex.IsPermanent);
  }

  [Fact]
  public async Task AnOversizeBodyIsRefusedRatherThanBuffered()
  {
    var oversize = new byte[300 * 32_000 + 1];
    var (client, _) = CreateClient(_ => Audio(oversize));

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.TooLarge, ex.Reason);
  }

  // ── The masking pin ───────────────────────────────────────────────────────
  // ADR-029 §5.1 asks GvMediaClient to follow PhoneContactLookupService's masking discipline.
  // That file masks on ONE line and logs the raw number on four others, one of them at Warning.
  // The rule here is stronger and this test is what enforces it, on every path and every level —
  // and on exception messages too, because callers log exceptions.
  [Theory]
  [InlineData(HttpStatusCode.OK)]
  [InlineData(HttpStatusCode.NotFound)]
  [InlineData(HttpStatusCode.BadGateway)]
  public async Task TheRawMediaIdNeverReachesALogLineOrAnExceptionMessage(HttpStatusCode status)
  {
    var (client, _) = CreateClient(_ => status == HttpStatusCode.OK
      ? Audio(new byte[512])
      : new HttpResponseMessage(status));

    try
    {
      await client.GetVoicemailFileAsync(RawId);
      // Second call exercises the cache-hit path, which has its own log line.
      await client.GetVoicemailFileAsync(RawId);
    }
    catch (GvMediaUnavailableException ex)
    {
      Assert.DoesNotContain(RawId, ex.Message, StringComparison.Ordinal);
    }

    Assert.NotEmpty(_logs.Messages);
    Assert.All(_logs.Messages, m => Assert.DoesNotContain(RawId, m, StringComparison.Ordinal));
  }

  [Fact]
  public void TheMaskIsAHashPrefix_NotASuffixOfTheId()
  {
    // ***1234 is right for a phone number, because a human recognises one by its last four digits.
    // Nobody recognises a voicemail id, so a suffix would leak four characters for no benefit.
    var mask = GvMediaCache.MaskFor(RawId);

    Assert.StartsWith("gvm:", mask, StringComparison.Ordinal);
    Assert.DoesNotContain(RawId[^4..], mask, StringComparison.Ordinal);
  }

  // ── The SSRF pins ─────────────────────────────────────────────────────────
  [Fact]
  public async Task TheFetchedUriAlwaysStaysOnTheConfiguredHost()
  {
    var (client, handler) = CreateClient(_ => Audio(new byte[64]));

    await client.GetVoicemailFileAsync(RawId);

    Assert.NotNull(handler.LastUri);
    Assert.Equal("radio:5004", handler.LastUri!.Authority);
    Assert.Equal("http", handler.LastUri.Scheme);
    Assert.Equal($"/api/gvbridge/voicemail/{RawId}/audio", handler.LastUri.AbsolutePath);
  }

  [Theory]
  [InlineData("http:evil.example")]
  [InlineData("https://evil.example/payload.mp3")]
  [InlineData("//evil.example/payload.mp3")]
  [InlineData("../../etc/passwd")]
  public void ASchemeOrPathBearingIdCannotMoveTheFetchOffTheConfiguredHost(string hostileId)
  {
    // PR 1's review found the deny-list defeated by a scheme-bearing id: under RFC 3986 §4.2 a
    // relative reference carrying a scheme resolves as ABSOLUTE, so new Uri(base, id) escaped the
    // base. EventPlaybackRequest now allow-lists the id; this pins that GvMediaClient does not
    // reintroduce the hole even if it is handed an id that never went through that validator.
    var uri = GvMediaClient.BuildVoicemailUri("http://radio:5004", hostileId, "gvm:test");

    Assert.Equal("radio:5004", uri.Authority);
    Assert.Equal("http", uri.Scheme);
    Assert.StartsWith("/api/gvbridge/voicemail/", uri.AbsolutePath, StringComparison.Ordinal);
  }
}

/// <summary>Captures every formatted log message, at every level, for the masking pin.</summary>
internal sealed class CapturingLoggerProvider
{
  public List<string> Messages { get; } = [];

  public ILogger<T> CreateLogger<T>() => new CapturingLogger<T>(Messages);

  private sealed class CapturingLogger<T>(List<string> sink) : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      sink.Add(formatter(state, exception));
      if (exception is not null)
      {
        sink.Add(exception.ToString());
      }
    }
  }
}
```

⚠ **`BuildVoicemailUri` is `internal static`, reachable from the test project via
`Radio.Infrastructure.csproj:15`'s `InternalsVisibleTo`.** If Builder finds
`ASchemeOrPathBearingIdCannotMoveTheFetchOffTheConfiguredHost` fails for `"../../etc/passwd"`
because `UriBuilder` normalises the dot segments away, **that is a pass, not a failure** — assert
what actually happens and say so in the PR body rather than weakening the assertion. The property
being pinned is *the authority never changes*, not the exact path text.

**Acceptance:** all facts pass; the masking theory fails loudly if any log line or exception message
carries `RawId`.

---

### Task 10 — registration, wiring, and the boot warning D8 asks for

**File (new):** `src/Radio.Infrastructure/DependencyInjection/GvMediaServiceExtensions.cs`

Per C-13, a self-contained feature extension called from `Program.cs`, following
`WeatherServiceExtensions` / `AddRadioWeather` — **not** appended to `AddSoundFlowAudio`.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.DependencyInjection;

/// <summary>
/// Registers server-side GV media fetch and caching (ADR-029 D3, D8).
/// </summary>
/// <remarks>
/// A standalone extension rather than an addition to AddSoundFlowAudio, deliberately. ADR-029 §5
/// says "beside its sibling in AudioServiceExtensions.cs", but that sibling is registered inside
/// AddPhoneIntegration, which is only reachable through AddSoundFlowAudio — so registering there
/// would bury a feature with its own Enabled flag inside the audio graph, and any test that
/// resolved it would initialise real audio hardware. That is exactly why
/// ActiveSourceAccessorRegistrationTests can only inspect descriptors, and therefore exactly why no
/// guard in this repo would catch a missing registration today. Keeping this separate is what makes
/// GvMediaRegistrationTests a real build-and-resolve guard.
/// </remarks>
public static class GvMediaServiceExtensions
{
  public static IServiceCollection AddGvMedia(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    services.Configure<GvMediaOptions>(configuration.GetSection(GvMediaOptions.SectionName));

    services.AddTransient<GvMediaAuthHandler>();
    services.AddSingleton<GvMediaCache>();

    var options = configuration.GetSection(GvMediaOptions.SectionName).Get<GvMediaOptions>()
      ?? new GvMediaOptions();

    services
      .AddHttpClient<GvMediaClient>(client =>
      {
        // BaseAddress is deliberately NOT set. GvMediaClient builds one absolute, validated URI in
        // exactly one place; a BaseAddress would create a second, implicit resolution site with the
        // RFC 3986 relative-reference hazard PR 1's review found.
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.FetchTimeoutSeconds));
      })
      .AddHttpMessageHandler<GvMediaAuthHandler>();

    services.AddHostedService<GvMediaStartupCheck>();

    return services;
  }
}

/// <summary>
/// Logs the one boot warning ADR-029 §10.2 requires: GvMedia:Enabled true with an empty AuthKey.
/// </summary>
/// <remarks>
/// It also warns on the specific divergence §10.2 names as the real cost of D8 — the same secret
/// living under two keys, where a mismatch surfaces only as a 401 on voicemail playback. Both
/// services load the same per-machine appsettings.Production.json on this box, so the two keys are
/// in one file and comparing them at boot is cheap.
/// </remarks>
internal sealed class GvMediaStartupCheck : IHostedService
{
  private readonly ILogger<GvMediaStartupCheck> _logger;
  private readonly IOptionsMonitor<GvMediaOptions> _options;
  private readonly IConfiguration _configuration;

  public GvMediaStartupCheck(
    ILogger<GvMediaStartupCheck> logger,
    IOptionsMonitor<GvMediaOptions> options,
    IConfiguration configuration)
  {
    _logger = logger;
    _options = options;
    _configuration = configuration;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    var options = _options.CurrentValue;
    if (!options.Enabled)
    {
      return Task.CompletedTask;
    }

    if (string.IsNullOrEmpty(options.AuthKey))
    {
      var webKey = _configuration.GetValue<string>("RotaryPhone:Gv:AuthKey");
      if (!string.IsNullOrEmpty(webKey))
      {
        _logger.LogWarning(
          "GvMedia:Enabled is true and GvMedia:AuthKey is empty, but RotaryPhone:Gv:AuthKey is set. "
          + "These are the same secret under two keys; voicemail fetches will fail with 401 until "
          + "GvMedia:AuthKey matches it in appsettings.Production.json.");
      }
      else
      {
        _logger.LogWarning(
          "GvMedia:Enabled is true and GvMedia:AuthKey is empty. This is correct only while "
          + "RotaryPhone's /api/gvbridge/* auth gate is off; set the key when it ships.");
      }
    }

    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

**File:** `src/Radio.API/Program.cs`

Insert one line immediately after the `AddRadioWeather` call (currently `:107`, **grep for
`AddRadioWeather` rather than trusting the number** — C-19):

```csharp
// GV media fetch + bounded on-disk cache + the API-side X-RotaryPhone-Auth handler (ADR-029 D3/D8).
// Standalone rather than folded into AddSoundFlowAudio; see GvMediaServiceExtensions' remarks.
builder.Services.AddGvMedia(builder.Configuration);
```

`using Radio.Infrastructure.DependencyInjection;` is already present at `Program.cs:9`, so
`AddGvMedia` needs no new `using` and must **not** be fully qualified — `AddSoundFlowAudio` and
`AddRadioWeather` beside it are called unqualified.

**Acceptance:** `dotnet build --configuration Release` clean; `radio-api` still starts locally.

---

### Task 11 — the DI guard this arc has been missing

**File (new):** `tests/Radio.Infrastructure.Tests/DependencyInjection/GvMediaRegistrationTests.cs`

Follows `RotaryEncoderRegistrationTests`' build-and-resolve shape and goes one step further, because
§0.6 established that nothing in this repo validates a container eagerly.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.DependencyInjection;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// The first real container guard in this repository.
///
/// <para>
/// PR 1 registered nothing and was exempt. PR 2 is the first PR in the ADR-029 arc that registers
/// services, and the failure mode is a service that will not start — on an appliance, in a cabinet.
/// Nothing existing would catch it: RotaryEncoderRegistrationTests covers only AddRotaryEncoders,
/// ActiveSourceAccessorRegistrationTests inspects descriptors rather than resolving, and neither
/// ValidateOnBuild nor ValidateScopes appears anywhere in src/ or tests/.
/// </para>
///
/// <para>
/// Validation is scoped to AddGvMedia on purpose. Turning it on over the whole graph would fail on
/// pre-existing hardware-touching registrations, which is a different row and not this one's to
/// open.
/// </para>
/// </summary>
public class GvMediaRegistrationTests
{
  private static ServiceProvider BuildProvider(IConfiguration? configuration = null)
  {
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddGvMedia(configuration ?? new ConfigurationBuilder().Build());

    return services.BuildServiceProvider(new ServiceProviderOptions
    {
      ValidateOnBuild = true,
      ValidateScopes = true
    });
  }

  [Fact]
  public void AddGvMedia_BuildsAndResolvesTheClient()
  {
    // An empty configuration on purpose: this also proves the defaults in GvMediaOptions are
    // sufficient to construct everything, which is what an appliance with no GvMedia block gets.
    using var provider = BuildProvider();

    var client = provider.GetRequiredService<GvMediaClient>();

    Assert.NotNull(client);
  }

  [Fact]
  public void AddGvMedia_ResolvesTheAuthHandlerAndTheCache()
  {
    using var provider = BuildProvider();

    Assert.NotNull(provider.GetRequiredService<GvMediaAuthHandler>());
    Assert.NotNull(provider.GetRequiredService<GvMediaCache>());
  }

  [Fact]
  public void TheCacheIsASingleton_SoTheWriteLockIsProcessWide()
  {
    // Two instances would be two write locks over one directory: concurrent evictions racing each
    // other's deletes, failing quietly rather than loudly.
    using var provider = BuildProvider();

    Assert.Same(
      provider.GetRequiredService<GvMediaCache>(),
      provider.GetRequiredService<GvMediaCache>());
  }

  [Fact]
  public void OptionsBindFromTheGvMediaSection()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GvMedia:Enabled"] = "true",
        ["GvMedia:CacheMaxMegabytes"] = "7",
        ["GvMedia:BaseUrl"] = "http://example.invalid:1234"
      })
      .Build();

    using var provider = BuildProvider(configuration);

    var options = provider.GetRequiredService<IOptionsMonitor<GvMediaOptions>>().CurrentValue;

    Assert.True(options.Enabled);
    Assert.Equal(7, options.CacheMaxMegabytes);
    Assert.Equal("http://example.invalid:1234", options.BaseUrl);
  }
}
```

⚠ **If `ValidateOnBuild` throws on a descriptor `AddHttpClient` registered rather than one PR 2
added**, do **not** silently delete the flag. Keep `ValidateScopes`, drop `ValidateOnBuild`, and
paste the exact exception into the PR body with a one-line note. A weakened guard that nobody knows
is weakened is worse than a guard that was never added.

**Acceptance:** four facts pass, and the provider is built with both validations on (or the
documented fallback).

---

### Task 12 — close the `SeekAsync` "open question", because it is now false

**File:** `src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs`

Doc-comment only. **Change nothing else in this file** — no new member, no validation change, no
`Label` cap (§0.3 ⓷).

Find the `<remarks>` on `IEventPlaybackService.SeekAsync` — currently **`:46-48`**, verified against
`c830fb8`, but grep for `Widening IEventAudioSource` rather than trusting the number — and replace
this paragraph:

```
  /// Widening IEventAudioSource.SeekAsync to Task&lt;bool&gt; would close the gap and is an open
  /// question for PR 3, not something to settle here: ADR-029 D4 copies those signatures
  /// verbatim from IPrimaryAudioSource, so changing one changes both.
```

with:

```
  /// Widening IEventAudioSource.SeekAsync to Task&lt;bool&gt; would close the gap, and was CLOSED as
  /// "no" by the PHN-1b plan (§0.3 ⓶). ADR-029 D4's only justification is that these signatures are
  /// copied verbatim from IPrimaryAudioSource, so widening one either leaves the codebase with two
  /// seek shapes or changes IPrimaryAudioSource too — which pulls in FilePlayerAudioSource, a live
  /// primary-source path with a persisted resume position and its own UAT (FUTURE-WORK §14a).
  /// A refused seek is still observable: Position reads through to the player, so the next
  /// snapshot's anchor simply does not move, and the scrubber snaps back.
```

⚠ **Assert the replacement, and use a phrase that is actually on one line.** The obvious check —
`grep -c "open question for PR 3"` — returns `0` **before** the edit as well, because the phrase
wraps between `:46` and `:47`; it would pass vacuously and prove nothing. Use:

```bash
# Must print 1 BEFORE the edit and 0 AFTER. Run it both times.
grep -c "question for PR 3" src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs
# Must print 1 AFTER.
grep -c "CLOSED as" src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs
```

A replacement assertion that cannot fail is not an assertion. Paste both before-and-after values
into the PR body.

**Acceptance:** builds; no test changes; `git diff` on this file is comment-only.

---

### Task 13 — docs

Three edits, all small.

**13a — `design/INTEGRATIONS.md`.** This arc's client is an integration service, and project memory
requires this file stay current for integration work. Add a `GvMedia` subsection under the phone /
GV material covering: the config keys and their defaults; that the fetch is server-side and
therefore carries `X-RotaryPhone-Auth` where the browser `<audio>` element could not; the cache
location, cap and the `0` escape hatch; and — the operational part — **that flipping `AuthKey` on a
live box is a hand edit of `/opt/radio-console/api/appsettings.Production.json` followed by
`systemctl restart radio-api`, because `Deploy-ToLinux.ps1:217` excludes that file from rsync and
`:222` only seeds it when it is absent** (C-14).

⚠ **Do not touch `INTEGRATIONS.md:566`.** That is the corrected ducking claim, and it is **PR 4's**
to update on the day the correction stops being true.

**13b — `design/FUTURE-WORK.md`.** Append one entry, in the file's existing shape:

- **Title:** `PhoneContactLookupService logs raw phone numbers on four lines and the contact's full name on the masked one`
- **What exists:** `:62` (Information, raw number + display name), `:78` (Debug, raw), `:96-97`
  (Debug, raw), `:102` (Warning, raw — since `LOG-11` this is one of the few that still reaches the
  journal). The single masked line, `:87-90`, still logs `contact.Name` in full, and ADR-029 §5.1
  asks for caller identity to be masked too.
- **What is needed:** compute the masked form once at method entry and use only it; mask
  `contact.Name` the same way; roughly fifteen lines.
- **Gotchas:** ADR-029 §5.1 points at this file as the masking *example*, so it is actively
  propagating. `PHN-1b` did not fix it — `PhoneIntegration:Enabled` is `false` and has never been
  true, so this is not a live leak today, but it becomes one the moment that flag flips.
- **Priority:** medium.

**13c — `design/DECISION-LOG.md`.** One line recording that PR 2 settled `MaxSpeechChars` as
rejection rather than truncation (§0.3 ⓵), overriding ADR-029 §4.2, and that
`IEventAudioSource.SeekAsync` stays `Task` (§0.3 ⓶). Both are arc-level decisions, not PR-local
ones, and the decision log is where a future reader will look for them rather than in a plan file.

**Acceptance:** all three exist and name real file:line references.

---

### Task 14 — build, test and the scope gate

Run and paste the output into the PR body. **Do not claim any of this without the output.**

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```

Then assert each of the following explicitly in the PR body:

1. **Release build: zero warnings.** Warnings are errors in Release. The likeliest failures are a
   nullable warning in `TryGetPath`, and `CA`/`IDE` analysers on the `MemoryStream` usage in
   `FetchAsync`.
2. **No endpoint was added.** `git diff --stat` shows **no** change under
   `src/Radio.API/Controllers/`.
3. **`DuckingServiceCharacterizationTests` is untouched**, and all four of its tests still pass.
   That file is PR 4's tripwire; a diff on it here would destroy the signal.
4. **`EventPlaybackRequest.Validate` is unchanged.** The only diff in
   `src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs` is Task 12's comment.
   `git diff -- src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs` contains no line that is
   not inside a `///` comment.
5. **No deploy file changed.** `git diff --stat` shows nothing under `deploy/`.
6. **The masking pin passes and is not vacuous.** Confirm `_logs.Messages` is non-empty in the
   theory — a test that asserts "the id is in none of zero log lines" proves nothing. The
   `Assert.NotEmpty` is there for that reason; say in the PR body that it fires.
7. **The pre-merge comment-accuracy check, run against this diff specifically.** `CLAUDE.md` is
   explicit that where a comment states a reason a thing is safe, **the reason is the claim to
   check**. Verify by reading the code, not the comment:
   - `EvictToCap`'s remark says the protected entry is never deleted **and** that the cap can
     therefore be left violated by one file. Both halves must be true of the code — if the second
     sentence were dropped the comment would overclaim.
   - `TryGetPath`'s remark says a failed touch is not a failed hit. Confirm the `catch` returns the
     path rather than null.
   - `BuildVoicemailUri`'s remark says `UriBuilder` cannot alter scheme or authority **and** that
     the comparison is belt-and-braces rather than redundant.
   - `GvMediaServiceExtensions`' remark says a `BaseAddress` would create a second resolution site.
     Confirm no `BaseAddress` is set anywhere on this client.
   - The `OperationCanceledException` filter: confirm the comment does not claim the
     timeout/cancellation distinction is exact. It is not.

---

## 2. Test Plan

### 2.1 What the automated tests actually prove

| Claim | Proved by |
|---|---|
| A second play never touches the network | `GvMediaClientTests.ASecondCall_IsServedFromCacheAndNeverTouchesTheNetwork` |
| `CacheMaxMegabytes = 0` is a no-cache path, not an infinitely-evicting one | `TryGetPath_AlwaysMisses_WhenTheCapIsZero`, `ASecondCall_RefetchesWhenTheCacheIsDisabled`, `SweepOlderThan_RemovesExpired_AndKeepsRecentAndProtected` |
| Eviction is LRU, and actually deletes | `EvictToCap_DeletesOldestFirstUntilItFits`, `EvictToCap_ActuallyDeletesFromDisk` |
| A successful fetch is never made unplayable by its own eviction | `EvictToCap_NeverDeletesTheProtectedEntry_...`, `WriteAsync_ProtectsTheNewEntry_WhenCacheDirectoryIsRelative` |
| The cache filename cannot be a Windows device name or collide on case | `FileNameFor_HashesRatherThanUsingTheIdVerbatim`, `FileNameFor_DistinguishesIdsDifferingOnlyByCase` |
| 404, 401/403 and 5xx stay distinguishable | `StatusCodesMapToDistinctReasons` (5 cases) |
| A disabled feature makes no request at all | `Fetch_IsRefusedWhenGvMediaIsDisabled_WithoutAnyRequest` |
| An oversize body cannot exhaust memory | `AnOversizeBodyIsRefusedRatherThanBuffered` |
| **The raw media id reaches no log line and no exception message** | `TheRawMediaIdNeverReachesALogLineOrAnExceptionMessage` (3 cases, all levels) |
| The mask leaks no part of the id | `TheMaskIsAHashPrefix_NotASuffixOfTheId` |
| A hostile id cannot move the fetch off the configured host | `ASchemeOrPathBearingIdCannotMoveTheFetchOffTheConfiguredHost` (4 cases), `TheFetchedUriAlwaysStaysOnTheConfiguredHost` |
| The auth header is sent only when the key is set, and never duplicated | `GvMediaAuthHandlerTests`, 3 facts |
| **The container actually builds and resolves** | `GvMediaRegistrationTests`, 4 facts, `ValidateOnBuild` + `ValidateScopes` |
| Nothing else regressed | the full suite, ~1,700 tests |

### 2.2 What tests cannot prove

PR 2 ships **no user-visible surface**: no route, no UI, no sound. There is no browser UAT to run
and no screenshot to take. That is not the same as "fully verified."

**Needs the running app (not a device) — deferrable to PR 3, when there is a route to drive:**

1. **That `radio-api` still starts with the new registration.** The DI guard (Task 11) proves the
   scoped graph resolves; it does not prove the *whole* API container still builds, because nothing
   in this repo validates that container. A local `dotnet run --project src/Radio.API` that reaches
   "Now listening on" is the cheap check, and Builder should run it and say so.
2. **That `GvMediaStartupCheck`'s warning actually appears.** Set `GvMedia:Enabled=true` with an
   empty `AuthKey` locally and confirm one Warning line. Worth doing here rather than deferring —
   it is thirty seconds and it is the only output this PR produces at runtime.

**Genuinely needs the box or the live gvbridge — carried forward, not claimed here:**

3. **That the gvbridge voicemail route returns what this client expects** — status codes, content
   type, and whether `Content-Length` is present. The size bound behaves differently depending on
   that last one, and only the real service settles it. **Carried to PR 3.**
4. **That the cache directory is writable under the service account.** `radio-api` runs as `mmack`
   with `/opt/radio-console` owned by that user, so this should hold — but `./data/gvmedia` is
   created on first fetch, and first fetch is PR 3. **Carried to PR 3.**
5. **The three device-only checks PR 1 carried forward** — that `SoundPlayerBase.Seek` repositions a
   short local MP3, that `Time` advances, and that pausing a TTS source no longer reports
   completion. **Still PR 6's**, unchanged by this PR.

⚠ **If anyone does exercise this against the live bridge before PR 3, record the wall-clock time.**
GV auth is dead ~9 minutes in every 20 on a cycle independent of the tester, and their
`/api/gvbridge/status` reports healthy during the blackout — an untimed result is noise, and a 502
here is as likely to be the clock as a bug.

### 2.3 Commands

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
dotnet test --filter "FullyQualifiedName~GvMediaCacheTests"
dotnet test --filter "FullyQualifiedName~GvMediaClientTests"
dotnet test --filter "FullyQualifiedName~GvMediaAuthHandlerTests"
dotnet test --filter "FullyQualifiedName~GvMediaRegistrationTests"
dotnet test --filter "FullyQualifiedName~DuckingServiceCharacterizationTests"   # must stay green, untouched
dotnet run --project src/Radio.API                                              # reaches "Now listening on"
```

---

## 3. Self-review

**Spec coverage against ADR-029's PR 2 scope (D3, D8):**

| ADR item | Task | Note |
|---|---|---|
| D3 — `GvMediaClient` in `Radio.Infrastructure/External/` | 8 | folder as specified |
| D3 — modelled on `PhoneContactLookupService` | 8 | shape followed; masking and failure handling deliberately stricter (§0.3 ⓸, C-18) |
| D3 §5.3 — bounded LRU at `./data/gvmedia/`, eviction really deletes | 4, 5 | |
| ⟨A1·2⟩ — `CacheMaxMegabytes = 0` is a no-cache path | 4, 5 | C-15 defines what that means for a `Task<string>` return |
| D8 §10.2 — the `GvMedia` config block | 1, 2 | `SpeechEngine` correctly absent (⟨A1·1⟩) |
| D8 §10.2 — boot warning when Enabled && AuthKey empty | 10 | plus the two-key divergence warning |
| D8 §10.1 — API-side auth handler | 6, 7 | a copy, with the trade stated |
| §5.1 — log masking | 8, 9 | rule strengthened; ADR's own example refuted (§0.3 ⓸) |
| §5 — registration site | 10 | **scoping correction**, C-13 |
| PR 1 §5 — hash the cache key, never use the raw id as a filename | 4 | with three reasons the allow-list does not cover |
| PR 1 §0.6 — PR 2 must add a real DI guard | 11 | first `ValidateOnBuild`/`ValidateScopes` in the repo |

**Placeholders:** none. Every code block is literal. Three places name a value Builder must confirm
rather than invent — the `AddRadioWeather` line number in `Program.cs` (grep for it), the exact
`<remarks>` text in Task 12 (assert the replacement), and whether `ValidateOnBuild` survives (Task
11's fallback). Each says what to do if the check fails.

**Type consistency:** `CacheMaxMegabytes` is `int` and is widened to `long` bytes at exactly one
site (`(long)options.CacheMaxMegabytes * 1024L * 1024L`) — `int` megabytes times an `int` constant
would overflow above 2 048 MB. `MaxPlaybackSeconds` × `AssumedMaxBytesPerSecond` is cast to `long`
for the same reason. `FileNameFor` and `MaskFor` derive from the same `SHA256.HashData` over the
same UTF-8 bytes, so a log line and a filename always correspond. `GvMediaOptions` lives in
`Radio.Core.Configuration`, which `Radio.API` already references.

**Load:** nothing periodic is added. `AlbumArtCacheService`'s 6-hour `Timer` is deliberately **not**
copied — reclamation here happens on the write path, which already holds a lock and is already doing
I/O, so the box gains no background work. ADR §1.3's ban on ticks and polls is respected.

**Scope:** no controller, no route, no `EventPlaybackService`, no ducking change, no
`EventPlaybackRequest` behaviour change, no `deploy/` change, no `PhoneContactLookupService` fix.

**Assertions this PR makes, and where each is checked:**

| Claim in a comment or contract | Checked by |
|---|---|
| *"a hit never touches the network"* | `ASecondCall_IsServedFromCacheAndNeverTouchesTheNetwork` (asserts `handler.Calls == 1`) |
| *"eviction really deletes"* | `EvictToCap_ActuallyDeletesFromDisk` (asserts the directory is empty) |
| *"the protected entry is never deleted"* | `EvictToCap_NeverDeletesTheProtectedEntry_...`, and the relative-path variant |
| *"the cap may be left violated by one file"* | the same two tests — the cap is 1 byte and a 4 KB file survives, which is the violation, asserted rather than described |
| *"the raw id never reaches a log or an exception message"* | `TheRawMediaIdNeverReachesALogLineOrAnExceptionMessage`, with `Assert.NotEmpty` so it cannot pass vacuously |
| *"a hostile id cannot move the fetch off the host"* | `ASchemeOrPathBearingIdCannotMoveTheFetchOffTheConfiguredHost`, including the `http:evil.example` case PR 1's review found |
| *"the container actually resolves"* | `GvMediaRegistrationTests` with both validations on |
| *"`0` is not infinitely evicting"* | `SweepOlderThan_...` keeps a recent entry; nothing is deleted per write |
| *"BaseAddress is not set"* | Task 14 item 7 — read `AddGvMedia`; also implied by `TheFetchedUriAlwaysStaysOnTheConfiguredHost` passing with an absolute URI |
| *"LRU, not FIFO"* | `TryGetPath` touches `LastWriteTimeUtc`; `EvictToCap_DeletesOldestFirstUntilItFits` orders on it |

**Where a comment could still overclaim, flagged for the reviewer rather than defended:** the LRU
claim is only as good as the touch, and the touch is best-effort inside a `try`. If touching fails
persistently on some filesystem, eviction degrades to FIFO **silently**. That degradation is
acceptable — FIFO over a 50 MB cache of ~1.4 MB files is not a user-visible difference — but the
comment must not say the ordering is guaranteed, and it does not.

**Rebase surface.** Small. The only shared files are `src/Radio.API/appsettings.json` (append at the
tail, after `PhoneIntegration`), `src/Radio.API/Program.cs` (one line), `docs/BUILDER_QUEUE.md` (one
row), and three docs. `src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs` is comment-only and
no other open row touches it. Nothing in `src/Radio.Infrastructure/Audio/` is touched at all, which
keeps this clear of `AUD-2`, `AUD-4` and the whole `ENC-*` bundle.

---

## 4. Things this plan deliberately does not do, with the reason

1. **Add an `IGvMediaClient` interface.** ADR §5 sketches a concrete class, and
   `PhoneContactLookupService` — the file it says to model — is injected concretely too. PR 3 can
   test against it with a stub `HttpMessageHandler`, which is what Task 9 does; an interface would
   be a seam nobody needs yet.
2. **Fix `AlbumArtCacheService`.** It has no size bound and an untestable hardcoded directory
   (C-17). Both are real, neither is this arc's, and touching a live album-art path to make a
   voicemail cache tidier is how a Medium PR becomes a risky one. It is not logged as future work
   either — it predates this arc and belongs to whoever files it on its own merits.
3. **Retire `GvBridgeApiService.GetVoicemailAudioUrl`.** It is what `VoicemailPlayer.razor:8` still
   binds. **PR 6** removes both together; removing the builder first would break voicemail playback
   for four PRs.
4. **Withdraw the cross-repo ask in § Cross-repo handoffs #3.** ADR §10.1 makes withdrawing it *"a
   deliverable of this arc"*, and this PR builds the handler that closes it — but the ask is only
   genuinely closed once Radio.API is the thing doing the fetching, which is **PR 6**. Withdrawing
   it now would tell RotaryPhone they may enable the gate while `VoicemailPlayer.razor` is still a
   browser `<audio>` element, which would break voicemail playback on the box.
5. **Add retry or backoff to the fetch.** The 45%-of-the-time blackout is nine minutes long; a retry
   policy that could outlast it would hold a request open for minutes, and one that could not is
   noise. The cache is the mitigation the ADR chose, and adding a second one would make the first
   harder to reason about.
6. **Register anything in `Radio.Web`.** Nothing in Web changes in this arc until PR 6.
7. **Cap `Label` or change `Validate`.** §0.3 ⓷ — PR 3.

---

## 5. Handoff to the rest of the arc

**Do not re-sequence the arc.** The breakdown's order stands; this plan implements PR 2 of it
unchanged.

**To PR 3 (`EventPlaybackService` + `POST /api/audio/events`) — everything PR 1 handed forward, plus
what PR 2 adds:**

- **Carried unchanged from PR 1 §5:** mint and own the `playbackId` — `IAudioSource.Id`
  (`AudioFileEvent-{guid}`) and `AudioFileEventSource._playbackId` (`audio-event-{guid}`) are **not
  equal**, whereas `TTSEventSource` uses `Id` directly, so a cancel-by-id built on the wrong one
  fails for exactly one arm. Call `EventPlaybackRequest.Validate(options.MaxSpeechChars)` and map
  each rejection to a `400` with its reason. Resolve the TTS engine **explicitly** from
  `TTS:DefaultEngine` and set `TTSParameters.Engine` — passing `parameters: null` is not equivalent
  (ADR §9.3). Same DI-guard obligation; Task 11 is now the template.
- **New from PR 2:** add `MaxLabelChars = 128` and `EventPlaybackRejection.LabelTooLong` to
  `Validate` (§0.3 ⓷). Map `GvMediaFailure` to status codes — `NotFound` → 404, `Unauthorized` →
  502 *with a distinct reason* (it is our misconfiguration, not the caller's error), `Timeout` /
  `Upstream` / `Transport` → 503, `Disabled` → 409, `TooLarge` → 502. **Do not collapse them**;
  `GV-6` and `GV-8` are both open rows for exactly that.
- ⚠ **Do not re-add speech truncation** (§0.3 ⓵). `TextTooLong` is a 400.
- Prefer `AudioFileEventSource`'s **path** constructor over the stream one (ADR §5.2) —
  `GvMediaClient` hands you a local path precisely so that seek is implementable.
- Pass `VoicemailItemDto.DurationSeconds` through as `EventPlaybackRequest.DurationSeconds`; `0`
  means unknown, and the snapshot's `Duration` must be null in that case rather than a confident
  lie. **Grep for the symbol** — the citation has been `ApiModels.cs:1127` and `:1128` in different
  documents (C-19).

**To PR 4 (priority becomes load-bearing) — ⚠ THE ONE TO REVIEW HARDEST:**

- **`DuckingServiceCharacterizationTests` is your tripwire, and PR 2 did not touch it.** All four
  still pass, so the premise is intact. Its second test asserts `0` raises for a second concurrent
  event; PR 4 changes that to `1` and says so. **Update those tests, never delete them.**
- When PR 4 lands, `design/INTEGRATIONS.md:566`'s correction must be updated **in the same PR** —
  leaving a doc that says *"this is not true today"* after the day it becomes true is the same
  failure class in reverse. PR 2 deliberately did not touch that line.
- The live consequence to put in front of a reviewer as a deliberate acceptance, not a discovery:
  with `PhoneIntegration:Enabled` false, **a doorbell posted to `/api/notifications/announce` at its
  default priority 8 will stop a voicemail mid-play.** That is the intended design (ADR §6.1).

**To PR 5 (server-owned state):** `Label` is capped at 128 by then (§0.3 ⓷), which bounds what goes
on the `/hubs/audio` wire. `GvMedia:MaxPlaybackSeconds` is already bound and configured — read it
from `IOptionsMonitor<GvMediaOptions>`, do not add a second key.

**To PR 6 (`PHN-2`, retire the `<audio>` element):**

- Remove `VoicemailPlayer.razor:8`'s `<audio>` element **and**
  `GvBridgeApiService.GetVoicemailAudioUrl` together (§4 item 3).
- **Withdraw the cross-repo ask** in [`CROSS-REPO-HANDOFFS.md`](../../docs/queue/CROSS-REPO-HANDOFFS.md) § Cross-repo handoffs #3 in the same
  PR (§4 item 4). ADR §10.1 makes it a deliverable; PR 6 is the first moment it is true.
- Carry PR 1's three device-only checks into the UAT: seek actually repositions; `Time` actually
  advances; pausing a TTS source does not report completion. Add PR 2's two: the gvbridge route's
  actual status codes and `Content-Length` behaviour, and that `./data/gvmedia` is writable under
  the service account.
- The row's own UAT is unchanged and is the thing that settles Feature A: **play a voicemail while
  the radio is on and confirm the radio ducks, that mute silences it, that master volume moves it,
  and that with Cast active it goes to the Cast device rather than the local speakers.**

**To `PHN-3` (the SMS speak button — note it is NOT one of the breakdown's seven PRs):** the
breakdown sequences Features A and C but not B; `PHN-3` remains its own punch-list row, blocked on
`PHN-1` by `O6`. It owns `GvSpeechText.ForMessage` and therefore owns **truncation with a spoken
tail** (§0.3 ⓵) — client-side, visible, before the post.
