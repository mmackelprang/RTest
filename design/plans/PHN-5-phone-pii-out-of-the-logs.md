# PLAN — `PHN-5` · Phone numbers and contact names stop being written to disk

> **Row:** `PHN-5`, [`BUILDER_QUEUE_ARCHIVE.md`](../../docs/BUILDER_QUEUE_ARCHIVE.md). 🟠 **P1.** No punch-list row — it was minted straight
> into the queue by `PHN-1b` (punch list `:1441` records that).
> **Branch:** `fix/phn-5-phone-pii-out-of-the-logs`
> **Estimate:** **1 d**, as the owner scoped it. §0.5 says why one day survives a widening from four
> sites to eleven — and what would push it to two.
> **Planned against** `main` at **`6c220461`**. Every line number below was read out of the tree at
> that commit. Where a line is likely to move it is quoted as well as numbered.
> **Widened by owner decision, 2026-09-05:** the row as filed covers four lines in one file. The
> owner ruled it covers **every site that logs a phone number or a real contact name**. That is
> eleven sites across six files and three assemblies.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`TTS-11` (#569) established that the console must not keep a plaintext copy of what it says out
loud, shipped `LogSafeText` to do it, and then **deliberately did not fix the phone-number sites** —
its §6.1 lists them, argues they are a different data class needing a different conversation, and
files them for an owner. This is that row. Eleven log statements across six files write a raw phone
number, a real contact's display name, or both, into a sink that persists. Two of them reach
`journalctl` **today, on a stock box**, and that is not what anybody believed when the row was filed
(**C-93**, **C-94**). The fix is the one `TTS-11` established and this row extends by one label:
**keep the event, drop the identifier, replace it with a token that correlates better than the
identifier did.**

### 0.2 ⚠ The row was filed for four sites. The owner widened it to ten. There are eleven.

The queue row names four raw-number lines plus the one it calls masked, all in
`PhoneContactLookupService.cs`. The owner's widening supplied a list of ten. **A sweep of every
`Log*` call under `src/` found eleven**, and the extra one is the most interesting line in the set
(**C-95**).

**Full inventory, every line read at `6c220461`.** The `Sink` column is the whole point of this
table and is derived in §0.3 — it is not the same for the two services, and that is the row's
central correction.

| # | Site | Svc | Level | Leaks | Sink today |
|---|---|---|---|---|---|
| `P1` | `src/Radio.Infrastructure/External/PhoneContactLookupService.cs:62` | API | Info | number **+ name** | file |
| `P2` | `…/PhoneContactLookupService.cs:78` | API | Debug | number | **none** |
| `P3` | `…/PhoneContactLookupService.cs:90` | API | Debug | **name** (number already masked) | **none** |
| `P4` | `…/PhoneContactLookupService.cs:96` | API | Debug | number | **none** |
| `P5` | `…/PhoneContactLookupService.cs:102` | API | **Warning** | number, **+ `ex`** | file **+ journald** |
| `P6` | `src/Radio.Infrastructure/External/PhoneCallClient.cs:128` | API | Info | number **+ name** | file |
| `P7` | `src/Radio.API/Services/PhoneCallIntegrationService.cs:127` | API | Info | name **or** number | file |
| `P8` | `src/Radio.Web/Services/Hub/PhoneHubService.cs:82` | **Web** | Info | number | file **+ journald** |
| `P9` | `src/Radio.Web/Services/ApiClients/GvTrunkApiService.cs:94` | **Web** | **Error** | number, **+ `ex`** | file **+ journald** |
| `P10` | `src/Radio.Web/Services/ApiClients/PbapApiService.cs:104` | **Web** | Debug | number, **+ `ex`** | **none** |
| `P11` | `src/Radio.Web/Services/ContactResolutionService.cs:173` | **Web** | Debug | number, **+ `ex`** | **none** |

**Verified clean and deliberately untouched:** `PhoneContactLookupService.cs:69`
(`LogWarning(ex, "PBAP contact lookup failed, falling through to REST lookup")`) carries no number
and no name. It is named here so a sweeping fixer does not "fix" it and so its absence from the
task list is not read as an oversight.

**Also swept and clean — the negative results, stated so they are auditable.** No SMS or message
**body** is logged anywhere: `GvBridgeSendService.cs:72` logs `{ThreadId}` only and never its `text`
parameter; every `GvBridgeApiService` SMS failure path logs `{ThreadId}` plus a status; and
`PhoneHubService.cs:98` logs `m.ThreadId` off the received `SmsMessageDto` and never its body. **No
Razor component logs at all.** The dozens of remaining phone-adjacent log lines carry only opaque
ids, device MACs, counts and paths.

### 0.3 ⭐ The sink table, derived — and the row's severity depends on it entirely

The queue row and the owner's brief both reason from `CLAUDE.md`'s rule: *"Since `LOG-11`,
`journalctl` only carries WARNING and above… `Information` lines go to the file sink instead."*

**That rule is true of `Radio.API`. It is false of `Radio.Web`, and nobody had noticed** (**C-93**).

| | `Radio.API` | `Radio.Web` |
|---|---|---|
| Console sink | added **in code**, `Program.cs:48-53`, `restrictedToMinimumLevel: Warning`, `SystemdConsoleFormatter` | declared in `appsettings.json`, **no `restrictedToMinimumLevel` at all** |
| File sink | `./logs/radio-.txt`, 7 files, 50 MB | `logs/web-.txt`, 7 files, 50 MB |
| `MinimumLevel.Default` | `Warning`, with `"Radio": "Information"` override (`appsettings.json:16-18`) | **`Information`** (`appsettings.json`, Serilog block) |
| ⇒ Information reaches | file only | **file AND the console — i.e. `journalctl -u radio-web`** |
| ⇒ Debug reaches | nothing (below the `Radio` override) | nothing (below `Default`) |

Under systemd the console **is** the journal. So `P8` — a **raw phone number at Information** — is
written to `journalctl -u radio-web` on every incoming call, today, on a stock box. The row called
`P9` *"the worst one"* because `Error` clears `LOG-11`'s Warning bar; the conclusion is right and
**the mechanism is wrong** — `P9` reaches the journal because `Radio.Web` has no bar, and by the same
mechanism so does `P8` at a level two steps lower.

⛔ **Do not "fix" this by adding a `restrictedToMinimumLevel` to `Radio.Web`'s console sink.** That
is a `LOG-` row about a second service's sink policy, it changes what every other `Radio.Web` line
does, and it would leave eleven leaks in place behind a level — which is a config value, not a
control (**C-96**). §6.2 files it.

### 0.4 ⚠ The row says the leak is latent. For five of eleven sites that is false.

The queue row's exposure paragraph is careful and, for the `Radio.API` half, correct: the only caller
of `FindCallerNameAsync` is `PhoneCallIntegrationService.cs:121`, whose `ExecuteAsync` returns at
`:44` when `PhoneIntegration:Enabled` is false — and it is false and has never been true. So `P1`–`P7`
are indeed latent.

**`P8`–`P11` are not** (**C-94**). Verified:

- `Radio.Web` **never binds `PhoneIntegrationOptions` at all.** The section does not appear in
  `src/Radio.Web/appsettings.json`, in `src/Radio.Web/appsettings.Production.json`, or in any file
  under `deploy/`. `Radio.Web` reads an unrelated `RotaryPhone` section.
- `PhoneHubService` is started **unconditionally**: `src/Radio.Web/Program.cs:652-653`,
  `var phoneHub = app.Services.GetRequiredService<PhoneHubService>(); _ = phoneHub.StartAsync();`,
  under a comment that reads *"Start RotaryPhone hub connections (non-blocking — logs warning if
  unavailable)"*. It connects to `RotaryPhone:HubUrl`, which ships as `http://radio:5004/hub` — the
  address of the RotaryPhone service that actually runs on the box.
- `GvTrunkApiService`, `PbapApiService` and `ContactResolutionService` are registered unconditionally
  (`Radio.Web/Program.cs:387`, `:293`, `:485`) and read no gate.

**So `P8` is live and `P9` is live.** The row's *"latent today and becomes live the moment that flag
flips"* holds for the API half and understates the Web half by a whole service. That raises this
row's tier argument; it does not change the fix.

### 0.5 The estimate

The owner scoped **1 d** for ten sites across two assemblies. `TTS-11` was **1.5 d** for twelve.
One day survives the difference for three reasons, and they are worth naming because they are also
the reasons a Builder should not re-derive any of it:

1. **The helper already exists.** `TTS-11` paid for `LogSafeText`, its tests, its threat-model
   documentation and the argument for hash-plus-prefix. This row adds **one method** to that class
   and reuses the rest.
2. **The harness already exists.** `CapturingLoggerProvider` and eight `*LogSafetyTests` classes
   across three test assemblies are shipped and are the pattern to copy.
3. **Nine of eleven edits are one-argument substitutions.** Only `P1`, `P6` and `P7` restructure a
   message template.

⚠ **What would push it to 2 d:** Task 8. If `LogSafetyLintTests`'s `phoneNumber` rule turns out to
need per-file exemptions after all, that is an afternoon of tuning — and a lint tuned until it is
green is worthless (the file says so itself at `:35`). **If the rule cannot be made global and
clean, drop it and say so in the PR body.** Do not ship an allowlist.

### 0.6 ⚠ Twelve constraints found while planning — numbering continues from `C-92` (`TTS-11`)

**`C-93` and `C-94` change the row's stated stakes** (§0.3, §0.4). **`C-95` adds a site.** **`C-97`
reverses a merged plan's stated expectation.** **`C-98` and `C-99` change the work.** **`C-100` is a
live test defect on `main` that will bite this row's own Builder.**

---

**`C-93` — ⚠ CHANGES THE STAKES. `LOG-11`'s "the journal carries Warning and above" is a statement
about `Radio.API` only. `Radio.Web`'s console sink has no level restriction, so its Information lines
go to journald.** §0.3 has the derivation. `CLAUDE.md` § *Deployment* states the rule without naming
a service and is the reason this was missed twice — by the row, and by `TTS-11` `C-92`, which found
the three `Radio.Web` sites but classified them by level rather than by sink. ⛔ **Do not edit
`CLAUDE.md` in this row** — §6.4 files the correction with the reason.

---

**`C-94` — ⚠ CHANGES THE STAKES. Five of the eleven sites are live on a stock box, not latent.**
§0.4. `Radio.Web` does not bind `PhoneIntegration` at all, and `PhoneHubService.StartAsync()` is
called unconditionally at `Radio.Web/Program.cs:652-653`.

---

**`C-95` — ⚠ AN ELEVENTH SITE NOBODY FILED, and it is this plan's own thesis in four lines.**

`src/Radio.API/Services/PhoneCallIntegrationService.cs:126-127`:

```csharp
    var announcement = $"Incoming call from {callerName}";
    _logger.LogInformation("Phone ringing: {Announcement}", announcement);
```

`callerName` is `FindCallerNameAsync`'s result, which **falls back to the raw phone number**
(`PhoneContactLookupService.cs:106` returns `phoneNumber`). So this one line prints either a real
contact's name or a raw number, at Information, to the file sink.

⭐ **And the identical string is masked nine lines later by the callee.** `:135-136` passes that same
`announcement` to `AnnouncementService.PlaySoundWithAnnouncementAsync`, which logs it as
`LogSafeText.For(message)` at `AnnouncementService.cs:97-98`. **The same characters are hashed at the
callee and printed in clear at the caller.** That is not a coincidence — it is what happens when a
masking rule is applied per-row instead of per-data-class, and it is the strongest single argument
for doing all eleven at once.

---

**`C-96` — the four Debug sites emit nothing today, in either service, and that is not a reason to
skip them.**

`P2`, `P3`, `P4` are Debug under `Radio.API`'s `"Radio": "Information"` override; `P10` and `P11` are
Debug under `Radio.Web`'s `Default: "Information"`. Neither service ships an
`appsettings.Development.json` — **verified: the file does not exist in either project** — so nothing
lowers the floor today.

They are in scope anyway, for the reason `TTS-11` §0.2 gave for its own two Debug sites: they are one
`Serilog:MinimumLevel` edit from live, the test harness observes them (`CapturingLoggerProvider`'s
`IsEnabled` returns `true` at every level), and **a log level is a volume control, not a privacy
control.** Leaving a known leak in place because a config value currently suppresses it is how the
next row gets filed.

---

**`C-97` — ⚠ THIS PLAN REVERSES A MERGED PLAN'S STATED EXPECTATION, deliberately, and the reversal is
the row's central decision.**

`TTS-11` §6.1 item 2 says, of these exact sites:

> **The fix is a different idiom.** `PhoneContactLookupService.cs:87-90` **already masks** —
> `$"***{phoneNumber[^4..]}"` — three lines below `:78`, which does not. So the fix there is *"apply
> the local idiom the file already contains, consistently"*, not *"adopt `LogSafeText`"*. Folding it
> in would put two competing masking schemes in one PR.

**§1 takes the opposite decision: the `***1234` form is deleted and `LogSafeText` is extended.** The
full argument is in §1.2, and the short version is that `TTS-11` was right that two schemes must not
coexist and wrong about which one survives. It was reasoning about *scope* — correctly, since folding
phone numbers into a TTS row would have been two conversations in one PR — not about which mask is
better, which is a question it never had to answer. This row has to.

⛔ **This is a reversal of a recorded position and it must be visible in the PR body**, not left for a
reviewer to notice that a merged plan says something else.

---

**`C-98` — ⚠ CHANGES THE WORK. Hashing the RAW number destroys the correlation the mask exists to
buy. Normalise first.**

The same subscriber is carried through this subsystem in at least three spellings. `PhoneHubService`
receives whatever the RotaryPhone hub sends; `PhoneContactLookupService.cs:58` normalises before its
PBAP lookup; `ContactResolutionService.cs:144-145` carries **both** a normalised `key` and the raw
`number` and logs the raw one. So `+1 (555) 123-4567`, `15551234567` and `5551234567` are one caller
and would hash to **three different tokens** — leaving an operator worse off than the `***4567` form,
which at least collapses all three.

⛔ **Therefore `ForPhone` normalises internally, and every call site passes whatever it has.**
`PhoneNumberNormalizer.Normalize` (`src/Radio.Core/Utilities/PhoneNumberNormalizer.cs:5`) already
exists, is already the subsystem's key function, and is **idempotent** — it strips non-digits, then
strips a leading `1` only when the result is 11 digits, so a second application is a no-op. That
matters because `ForPhone` will be handed already-normalised values at some sites and raw ones at
others. `ForPhone_IsIdempotentUnderNormalization` (§4.1) pins it.

⚠ **One consequence to document rather than discover:** a non-empty input with no digits
(`"unknown"`, `"Anonymous"`, `"(withheld)"`) normalises to `""` and therefore returns `phn:empty`,
the same token as `null`. That is the right answer — both mean *"no usable number"* — but it must be
in the XML doc, or someone will file it as a bug.

---

**`C-99` — ⚠ CHANGES THE WORK. `LogSafetyLintTests`'s `SafeCall` regex does not match `ForPhone`, so
turning on a `phoneNumber` rule without widening it flags every site this row FIXES.**

`tests/Radio.Core.Tests/LogSafetyLintTests.cs:78`:

```csharp
  private static readonly Regex SafeCall = new(@"\bLogSafeText\s*\.\s*For\s*\(", RegexOptions.Compiled);
```

`For\s*\(` requires an open paren immediately after `For`. `LogSafeText.ForPhone(phoneNumber)`
supplies `P`, so `RemoveSafeCalls` does **not** strip it, `phoneNumber` stays visible in the scrubbed
argument text, and a new rule over `phoneNumber` reports a violation at every corrected line. The
lint would fail *because* the row succeeded.

**Fix, in Task 8, and it is one character class:**

```csharp
  private static readonly Regex SafeCall = new(
    @"\bLogSafeText\s*\.\s*For(?:Phone)?\s*\(", RegexOptions.Compiled);
```

---

**`C-100` — ⚠⚠ `LogSafetyLintTests` FAILS IN ANY CHECKOUT WHOSE PATH CONTAINS A `worktrees` SEGMENT.
This is a live defect on `main`, it is not caused by this row, and this row's own Builder will hit
it. MEASURED, not reasoned.**

Run at `6c220461` from `D:\prj\RTest\worktrees\wt-phn5-phn3-plans`:

```
Failed Radio.Core.Tests.LogSafetyLintTests.NoLogCallInTheSolutionPassesAKnownUserTextArgument [3 ms]
  LogSafetyLintTests could not settle on a repository root by walking up from
  'D:\prj\RTest\worktrees\wt-phn5-phn3-plans\tests\Radio.Core.Tests\bin\Release\net10.0\'.
  … Solution files found: D:\prj\RTest\worktrees\wt-phn5-phn3-plans.
```

The mechanism: `FindRepositoryRoot` (`:218-247`) collects every ancestor holding a
`RadioConsole.sln`, then takes `candidates.LastOrDefault(c => !IsInsideAWorktree(c))`.
`IsInsideAWorktree` (`:258-265`) is true for **any path with a `worktrees` segment**. A worktree at
`D:/prj/RTest/worktrees/…` is its own only candidate *and* matches the exclusion, so the filter
empties the list and the assertion fires.

⚠ **The file's own comment says this case is handled and it is wrong** (`:213-216`): *"A worktree
checked out somewhere else entirely is its own outermost match and is scanned normally, which is
correct."* That holds only when the path does not contain the word — and `D:/prj/RTest/worktrees/` is
the convention this repository already uses (`wt-phn3plan` is sitting there now). This is the
comment-accuracy class `CLAUDE.md` § *Pre-Merge Review* exists for: a remark asserting a property the
code does not have.

**Fixed in Task 8 as one line**, because this row edits that file's rules anyway and leaving a
known-red test in a file you are already editing is worse than fixing it:

```csharp
    // Prefer a root that is not a nested checkout; but if EVERY candidate looks like one, the
    // outermost is still the right answer and is certainly better than no root at all. Before
    // PHN-5 this was LastOrDefault(...) with no fallback, so a worktree parked under a directory
    // literally named "worktrees" — the convention this repo uses — filtered out its own only
    // candidate and the lint failed with "could not settle on a repository root". Measured.
    var root = candidates.LastOrDefault(c => !IsInsideAWorktree(c)) ?? candidates.LastOrDefault();
```

The original intent survives: with two candidates (a nested checkout inside a real one) the
non-worktree ancestor still wins, which is the case the guard was written for.

⚠ **This is worth its own row if the owner would rather keep it out of a PII fix.** §6.5 states the
recommendation and the cost either way. **It must not be fixed silently** — the PR body has to say a
test defect unrelated to PII was repaired, or the diff reads as scope creep.

---

**`C-101` — the `PhoneIntegration:Enabled` asymmetry is REAL, is worse than the prior session
claimed, and this row deliberately does NOT fix it.**

The claim under test was *"the API side is gated and the Web side is ungated, with no
repository-level `Enabled` check in `PhoneHubService`."* **Verified, and understated.**
`PhoneIntegrationOptions.Enabled` (`src/Radio.Core/Configuration/PhoneIntegrationOptions.cs:13`,
default `false`) has **exactly one reader in all of `src/`**:

```csharp
// src/Radio.API/Services/PhoneCallIntegrationService.cs:43-48
    var opts = _options.Value;
    if (!opts.Enabled)
    {
      _logger.LogInformation("Phone call integration is disabled");
      return;
    }
```

`PhoneContactLookupService` and `PhoneCallClient` both *inject*
`IOptionsMonitor<PhoneIntegrationOptions>` and read only `ContactsApiBaseUrl` / `HubUrl` and the
reconnect delays. The four `Radio.Web` classes inject nothing — the section is not bound in that
service (**C-94**).

**Not fixed here, and the reason is dependency direction rather than appetite.** Making the Web side
respect a gate means one of: binding a second options section into `Radio.Web`; or minting a new
`RotaryPhone:Enabled` key; or gating `Program.cs:652-655`'s unconditional hub start. All three are
**behavioural** — they change whether `radio-web` connects to the RotaryPhone hub at boot — with UAT
consequences on a live surface, and the choice between them is a config-surface decision belonging to
the owner, not to a logging fix.

⭐ **And the order is right this way round.** Once nothing leaks, whether an ungated path runs is a
*functionality* question rather than a *privacy* one. Fixing the leak first strictly reduces what the
gating decision is carrying. §6.1 files it.

---

**`C-102` — `GET /api/integrations/phone/status` reports `Enabled = true` on a box where it is
false.** `src/Radio.API/Controllers/IntegrationsController.cs:215-223` hard-codes `Enabled = true`
whenever `IPhoneIntegrationService` resolves from DI — which it always does, because
`AddPhoneIntegration` (`AudioServiceExtensions.cs:257`) is registered unconditionally. It fetches
`IOptions<PhoneIntegrationOptions>` at `:212` and uses it only for `HubUrl`. Found while verifying
`C-101`. **Not this row** — it is a wrong-answer defect in a status endpoint, not a PII leak. §6.1
files it with `C-101`.

---

**`C-103` — `P5`, `P9`, `P10` and `P11` log an exception, and the request URL embeds the number.
Masking the argument does not, by itself, prove the number is gone.**

`PhoneContactLookupService.cs:76` builds
`$"{baseUrl}/api/contacts/lookup?phone={Uri.EscapeDataString(phoneNumber)}"` and `:102` logs the
resulting exception. .NET's `HttpRequestException` message does not normally carry the request URI —
but the message varies by platform and failure mode, inner exceptions are chained, and **this plan
did not exhaustively verify that no failure mode includes it.**

⛔ **Do not resolve this by dropping `ex`.** The stack trace is the entire diagnostic value of a
Warning line, and removing it to defend against an unmeasured possibility trades a real capability
for a guess.

**Resolve it by measuring instead:** §4.3's tests drive a real failure and assert the sentinel number
is absent from the captured output **including `exception.ToString()`**. ⚠ That last clause is the
trap — a capturing logger that records only the formatted message will pass while the exception
leaks. `CapturingLoggerProvider` must be checked on this point before the test is trusted.

---

**`C-104` — ⭐ this row is what makes `LogSafetyLintTests`'s `phoneNumber` rule possible, and the
lint says so in its own words.**

`LogSafetyLintTests.cs:62-67`:

> **`phoneNumber` is NOT enforced, deliberately.** Every occurrence in the tree is a real leak of a
> real phone number, and every one belongs to the separate, deliberately-unfixed row the `TTS-11`
> plan files at §6.1 — so a rule over it would be all exemption and no coverage. An allowlist naming
> every file that already leaks is not a lint; it is a place for the next person to add a file
> instead of fixing a leak.

Once Tasks 1–7 land, the exemption argument evaporates: there are no remaining occurrences to
exempt. **Turning that rule on is Task 8 and it is the row's durable deliverable** — the eleven fixes
are this year's leak, and the rule is what catches next year's.

### 0.7 Things Builder must NOT do

- ⛔ **Do not add a `restrictedToMinimumLevel` to `Radio.Web`'s console sink.** `C-93`, §6.2.
- ⛔ **Do not gate anything on `PhoneIntegration:Enabled`.** `C-101`, §6.1. It is a behavioural change
  wearing a logging fix's clothes — the same shape `TTS-11` §0.4 refused for `TTSEventSource.Name`.
- ⛔ **Do not touch `PhoneNumberNormalizer`.** It is a key function used for cache lookups
  (`ContactResolutionService.cs:57`, `:81`, `:100`, `:116`; `PhoneContactLookupService.cs:58`). It
  returns usable PII **by design** and must keep doing so. `ForPhone` *calls* it; it does not become
  a masking helper.
- ⛔ **Do not mask `PbapApiService.cs:52`/`:66` or the `PbapSyncService` lines.** Those carry the
  paired device's Bluetooth **MAC**, which is hardware identity, not subscriber identity, and it is
  the field every BT diagnosis in `CLAUDE.md` starts from.
- ⛔ **Do not edit `docs/HANDOFF-GA-PUNCH-LIST.md` beyond what §5 of this plan specifies**, and do
  not edit `CLAUDE.md` at all (`C-93`, §6.4).
- ⛔ **Do not drop `ex` from any site.** `C-103`.

---

## 1. Decision — two data classes, two answers, one algorithm

### 1.1 The shape, stated first

**A phone number gets a hash token. A contact name gets nothing.** Those are different answers
because the two values differ on both axes that matter — how enumerable they are, and what an
operator can do with them.

| | Phone number | Contact name |
|---|---|---|
| Entropy | ~33 bits (NANP), realistically a household's contact list | a household's contact list, and often a first name |
| What it answers for an operator | *"is this the same caller across these lines?"* — the only question these lines are used for | nothing the number does not already answer |
| Useful masked form | yes — a stable token preserves the correlation exactly | **no** — a name has no tail to keep and no shape worth preserving |
| **Verdict** | **`LogSafeText.ForPhone(...)`** | **delete the argument** |

### 1.2 ⚠ Why the hash and not `***1234` — the decision `C-97` reverses

Four options were considered.

| Option | Verdict |
|---|---|
| **Keep `***{last4}`** — the idiom the file already has at `:87-89`, and what `TTS-11` §6.1 expected | **Rejected.** Reasons below. |
| **Delete the number entirely** | **Rejected.** It destroys the only diagnosis these lines support — joining a lookup failure to the call that caused it. `TTS-11` §1 rejected deletion for the same reason and the reason is the same here. |
| **A new `LogSafePhone` class** | **Rejected.** A second class is a second place not to look. The whole defect being fixed is that a reader found one masking form and not the other. |
| **`LogSafeText.ForPhone` → `phn:{8 hex}`** ✅ | **Taken.** |

**Why the hash beats `***1234`, on the threat model this repository has actually written down.**
`LogSafeText`'s own remarks name the exposure: *"a person READING the log — the family member or
technician who opens Settings → Logs."* Against that reader:

- `***4417` identifies Jane instantly to anyone who knows Jane's number. Zero effort.
- `phn:9f2ab41c` identifies nobody without deliberately hashing a candidate list.

Both are recoverable by an adversary who enumerates. **Only one is recoverable by a person glancing
at a screen**, and that person is the threat. The operator's question — *"same caller?"* — is served
identically by both.

**Why this is not "a second masking idiom for one class of PII".** It is the **third label on the one
idiom already in the tree**. `GvMediaCache.MaskFor` (`GvMediaCache.cs:79-83`) emits
`"gvm:" + 8 hex`; `LogSafeText.For` emits `"txt:" + 8 hex + "/" + length`. The house pattern is
already *one algorithm, one prefix per data class, a length only where a length is diagnostic.*
`phn:` follows it. What this row **deletes** is the genuine second idiom: the inline `***{last4}` at
`:87-89`, which today makes one line in one file mask differently from every other.

**Why no length suffix.** A phone number's length is near-constant, so it answers no triage question,
and it would distinguish national from E.164 format for free. `MaskFor` is the in-tree precedent for
the no-length variant and it is the right one to follow. (`For`'s length is kept for text because
*"was this empty or absurd"* is a real question about an utterance.)

⚠ **The honest limitation, and it goes in the XML doc verbatim.** A NANP number is ten digits: the
entire space can be hashed in minutes on any modern machine. **`ForPhone` is a correlation token and
not a confidentiality boundary** — the same sentence `LogSafeText.For` already carries, and for a
stronger reason. It defends against reading, not against enumeration. Anyone describing it as
anonymisation is wrong, and `CLAUDE.md` § *Pre-Merge Review* is about exactly that class of
overclaim.

### 1.3 Why a contact name is deleted rather than tokenised

`LogSafeText`'s own remarks give the rule: *"do not reach for it in a context where enumeration is
the threat: there, log nothing."* A household's contact list is on the order of a hundred names, and
the PBAP database holding them sits on the same box as the log. Tokenising a name buys correlation
nobody needs — the masked **number** already correlates the same lines — and spends it on a value
whose candidate space is small enough to defeat by hand.

**What the diagnosis loses, stated so the trade is visible.** *"The console announced the wrong
caller name"* currently reads off the log directly. Afterwards it is one query against the PBAP store
keyed by the number the operator already holds. That is a slower path to the same answer, and it is
the correct trade for a value with no other use.

**What replaces it where the presence of a name is the diagnosis.** At `P6` and `P7` the useful fact
is not *which* name resolved but *whether one did* — that is what distinguishes "PBAP is working"
from "PBAP returned nothing and we announced a phone number". Those sites gain a boolean.

---

## 2. Tasks

### Task 1 — `LogSafeText.ForPhone`, and one algorithm proved by construction

**File:** `src/Radio.Core/Utilities/LogSafeText.cs`

Extract the shared core, then add the phone entry point. ⚠ **Extracting the core edits a shipped,
tested method** — that is safe here and only here because
`For_IsStableAcrossProcesses_OverUtf8Bytes` pins `For("héllo")` to the exact literal
`"txt:3c48591d/5"`, so any change in encoding, byte count or format fails immediately. **Run that
test before and after the extraction and say so in the PR body.**

Replace the body of `For` and append:

```csharp
  /// <summary>The token for a null, empty, or digitless phone number.</summary>
  public const string EmptyPhone = "phn:empty";

  /// <summary>
  /// Returns <c>phn:{8 hex}</c> for <paramref name="phoneNumber"/>, or <see cref="EmptyPhone"/>
  /// when it is null, empty, or contains no digits.
  /// </summary>
  /// <remarks>
  /// ⚠ THE NUMBER IS NORMALISED BEFORE HASHING, and that is the whole reason this is a method
  /// rather than a call to <see cref="For"/>. The same subscriber reaches these log lines in at
  /// least three spellings — "+1 (555) 123-4567" from the hub, "15551234567" from PBAP,
  /// "5551234567" after normalisation — and hashing the raw string would give one caller three
  /// different tokens, which is strictly worse than the "***4567" form this replaced. Normalising
  /// first is what makes the token answer "is this the same caller?", which is the only question
  /// these lines are ever used for. See plan PHN-5 C-98.
  ///
  /// ⚠ NO LENGTH SUFFIX, unlike <see cref="For"/>, and the omission is deliberate. A phone
  /// number's length is near-constant so it answers no triage question, and it would separate
  /// national from E.164 format for free. GvMediaCache.MaskFor is the in-tree precedent for the
  /// no-length variant; For's length is kept because "was this empty or absurd" is a real question
  /// about an utterance and not about a number.
  ///
  /// ⚠ A non-empty input with no digits — "unknown", "Anonymous", "(withheld)" — normalises to
  /// empty and returns EmptyPhone, the same token as null. That is the intended answer: both mean
  /// "no usable number". It is documented because it is otherwise indistinguishable from a bug.
  ///
  /// ⚠ THIS IS A CORRELATION TOKEN, NOT A CONFIDENTIALITY BOUNDARY, and the caveat is STRONGER
  /// here than on For. A NANP number is ten digits: the entire candidate space can be hashed in
  /// minutes. What this defends against is a person READING the log — the family member or
  /// technician who opens Settings → Logs — which is the exposure PHN-5 was filed for. It is not
  /// anonymisation and must never be described as such.
  /// </remarks>
  public static string ForPhone(string? phoneNumber)
  {
    var normalized = PhoneNumberNormalizer.Normalize(phoneNumber ?? string.Empty);
    return normalized.Length == 0 ? EmptyPhone : Token("phn:", normalized, withLength: false);
  }

  /// <summary>
  /// The one hash the tokens in this class are built from: SHA-256 over the UTF-8 bytes, first
  /// four bytes as lowercase hex.
  /// </summary>
  /// <remarks>
  /// ⚠ Extracted so there is provably ONE algorithm behind every prefix this class emits, which is
  /// the property PHN-5 §1.2 argues for. The shape it produces — a short literal prefix plus 8 hex
  /// — is also GvMediaCache.MaskFor's, so a reader who knows one form recognises the others.
  /// ⚠ Encoding.UTF8 and SHA256 are both load-bearing and both pinned by exact-value tests. Do not
  /// substitute string.GetHashCode(): .NET randomises it per process, and TTSFactory already
  /// builds a cache key with it, so the "consistency" edit is one keystroke away.
  /// </remarks>
  private static string Token(string prefix, string value, bool withLength)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    var hex = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    return withLength
      ? string.Concat(prefix, hex, "/", value.Length.ToString())
      : string.Concat(prefix, hex);
  }
```

and rewrite `For`'s body to route through it, leaving its signature, its `<summary>` and its
`<remarks>` **untouched**:

```csharp
  public static string For(string? text)
  {
    if (string.IsNullOrEmpty(text))
    {
      return Empty;
    }

    return Token("txt:", text, withLength: true);
  }
```

Add `using Radio.Core.Utilities;` — not needed, `PhoneNumberNormalizer` is in the same namespace.
**Verify that** rather than assuming it: both files are `namespace Radio.Core.Utilities;`.

⚠ **Widen the class `<summary>`.** It currently reads *"Renders user-supplied text as a log-safe
token"* and the class now also renders phone numbers. One line: *"Renders a user-supplied value —
free text, or a phone number — as a log-safe token."* This is the `CLAUDE.md` § *Pre-Merge Review*
discipline applied to the change that falsifies the sentence, in the same PR.

---

### Task 2 — `PhoneContactLookupService` (`P1`–`P5`), and the deletion of the second idiom

**File:** `src/Radio.Infrastructure/External/PhoneContactLookupService.cs`

`using Radio.Core.Utilities;` is **already present** at `:7`.

**`:62` (`P1`)** — the name goes; the arrow's meaning survives in the verb:

```csharp
            _logger.LogInformation("PBAP contact resolved for {Number}",
              LogSafeText.ForPhone(phoneNumber));
```

**`:69`** — ⛔ **unchanged.** Carries no number and no name.

**`:78` (`P2`)**:

```csharp
      _logger.LogDebug("Looking up contact for {PhoneNumber}", LogSafeText.ForPhone(phoneNumber));
```

**`:87-90` (`P3`)** — ⭐ **this is the second-idiom deletion.** The three-line inline mask goes with
it:

```csharp
        if (!string.IsNullOrWhiteSpace(contact?.Name))
        {
          // ⚠ The inline "***{last4}" mask that used to be computed here is GONE, and its removal
          // is the point of PHN-5 rather than a side effect. It was the file's own local idiom,
          // applied on exactly one of six lines, and it left contact.Name in clear on the one line
          // it masked. One mask, one shape, every line — see plan PHN-5 §1.2.
          _logger.LogDebug("Contact lookup resolved {PhoneNumber}", LogSafeText.ForPhone(phoneNumber));
          return contact.Name;
        }
```

⚠ **`return contact.Name;` is untouched.** The name is the method's *return value* and the feature;
only the log argument is in scope.

**`:96-97` (`P4`)**:

```csharp
        _logger.LogDebug("Contact lookup returned {StatusCode} for {PhoneNumber}",
          response.StatusCode, LogSafeText.ForPhone(phoneNumber));
```

**`:102` (`P5`)** — `ex` stays (`C-103`):

```csharp
      _logger.LogWarning(ex, "Contact lookup failed for {PhoneNumber}",
        LogSafeText.ForPhone(phoneNumber));
```

---

### Task 3 — `PhoneCallClient` (`P6`)

**File:** `src/Radio.Infrastructure/External/PhoneCallClient.cs:128-129`

Number masked, name replaced by whether one resolved (§1.3):

```csharp
    _logger.LogInformation("Phone call state: {State}, Number: {Number}, NameResolved: {NameResolved}",
      parsedState, LogSafeText.ForPhone(phoneNumber), !string.IsNullOrWhiteSpace(callerName));
```

Add `using Radio.Core.Utilities;`.

⚠ **`:124-126` are untouched** — `_currentState` / `_callerNumber` / `_callerName` are the cached
state the `CallStateChanged` event carries to the UI, which is the feature. Only the log line moves.

---

### Task 4 — `PhoneCallIntegrationService` (`P7`), and the join it creates

**File:** `src/Radio.API/Services/PhoneCallIntegrationService.cs:126-127`

```csharp
    var announcement = $"Incoming call from {callerName}";

    // ⭐ TWO tokens, and each of them joins this line to something it could not reach before.
    // {Number} is the same phn: token PhoneContactLookupService prints on the lookup lines that
    // produced callerName, so a failed resolution and the announcement it degraded into are now
    // one traceable chain. {Announcement} is the SAME txt: token AnnouncementService prints nine
    // lines below at :97-98 for this identical string — before PHN-5 the caller printed it in
    // clear and the callee hashed it, which is what a per-row masking rule produces (plan PHN-5
    // C-95).
    _logger.LogInformation("Phone ringing: announcing to {Number}, announcement {Announcement}",
      LogSafeText.ForPhone(e.PhoneNumber), LogSafeText.For(announcement));
```

Add `using Radio.Core.Utilities;`.

⛔ **`announcement` itself is not changed.** It is the string that gets spoken; this row masks how it
is logged and nothing else.

---

### Task 5 — the three `Radio.Web` client services (`P9`, `P10`, `P11`)

All three keep `ex` (`C-103`) and all three need `using Radio.Core.Utilities;`. `Radio.Web`
references `Radio.Core` — verified via the existing `PhoneNumberNormalizer` use at
`PhoneTextsPanel.razor:413-430`.

**`src/Radio.Web/Services/ApiClients/GvTrunkApiService.cs:94` (`P9`)** — the Error-level site:

```csharp
      _logger.LogError(ex, "Failed to dial {Number} via GV Trunk", LogSafeText.ForPhone(number));
```

**`src/Radio.Web/Services/ApiClients/PbapApiService.cs:104` (`P10`)**:

```csharp
      _logger.LogDebug(ex, "PBAP number lookup failed for {Number}", LogSafeText.ForPhone(phoneNumber));
```

**`src/Radio.Web/Services/ContactResolutionService.cs:173` (`P11`)**:

```csharp
      _logger.LogDebug(ex, "Contact resolution failed for {Number}", LogSafeText.ForPhone(number));
```

⭐ `ContactResolutionService` holds both a normalised `key` and a raw `number` and logs the raw one.
Because `ForPhone` normalises internally (`C-98`), `ForPhone(number)` and `ForPhone(key)` produce the
**same** token — so it does not matter which is passed, and passing the one already in the template
keeps the diff to one argument.

---

### Task 6 — `PhoneHubService` (`P8`), the live-to-journald site

**File:** `src/Radio.Web/Services/Hub/PhoneHubService.cs:82`

```csharp
        _logger.LogInformation("Incoming call from {PhoneNumber}", LogSafeText.ForPhone(phoneNumber));
```

Add `using Radio.Core.Utilities;`.

⚠ **This is the single highest-value line in the row** and the reason is `C-93`, not its level:
`Radio.Web`'s console sink has no minimum, so this Information line is written to
`journalctl -u radio-web` on every incoming call, on a stock box, today. It is inside the
`_hubConnection.On<string, string>("IncomingCall", …)` lambda at `:80-84`, so there is no method to
rename and no signature to change.

---

### Task 7 — the two service-level sink facts, written down where they will be read

**File:** `src/Radio.Web/appsettings.json`, the `Serilog` block's existing `"//"` comment.

Append one sentence to it:

```
⚠ Unlike Radio.API, this Console sink has NO restrictedToMinimumLevel, so under systemd every
Information line here reaches `journalctl -u radio-web` as well as the file. LOG-11 restricted the
API's console sink in code (Radio.API/Program.cs:48-53) and did not touch this one. Anything logged
at Information in Radio.Web is therefore in the journal — see plan PHN-5 C-93 before adding one.
```

**File:** `design/FUTURE-WORK.md` — the § *Phone-number logging* item added by `TTS-11` Task 8 is
**closed** by this row. Mark it so, correct its count (it records the sites `TTS-11` found; there are
eleven), and add the two items §6.1 files.

---

### Task 8 — turn the lint's `phoneNumber` rule on, and repair two defects in the same file

**File:** `tests/Radio.Core.Tests/LogSafetyLintTests.cs`

**8a. Widen `SafeCall` so `ForPhone` counts as safe (`C-99`)** — `:78`:

```csharp
  /// <summary>Matches the helpers whose whole job is to make a forbidden argument safe.</summary>
  private static readonly Regex SafeCall = new(
    @"\bLogSafeText\s*\.\s*For(?:Phone)?\s*\(", RegexOptions.Compiled);
```

⛔ **Do 8a before 8b.** In the other order every line Tasks 2–6 fixed reports as a violation and the
obvious reaction is to weaken the rule.

**8b. Add the rule the file deliberately left out (`C-104`)** — in `Forbidden`:

```csharp
    // P1-P11 — PHN-5. Enforced GLOBALLY and with no per-file exemption, which is only possible
    // because PHN-5 fixed every occurrence in the tree. LogSafetyLintTests' own remarks explain
    // why the rule was left out before that: "a rule over it would be all exemption and no
    // coverage… an allowlist naming every file that already leaks is not a lint; it is a place for
    // the next person to add a file instead of fixing a leak." There is now nothing to exempt.
    Of(@"\bphoneNumber\b" + NotASizeRead, "phoneNumber"),
    // The other spellings the same value travels under in this subsystem. `number` is bare and
    // therefore scoped, like `message` and `text` above: it is an ordinary parameter name.
    Of(@"\bcallerNumber\b" + NotASizeRead, "callerNumber"),
    Of(@"(?<![\w.])number\b" + NotASizeRead, "bare number", "GvTrunkApiService.cs"),
    Of(@"(?<![\w.])number\b" + NotASizeRead, "bare number", "ContactResolutionService.cs"),
```

⚠ **Update the class remarks.** The bullet at `:62-67` says `phoneNumber` *"is NOT enforced,
deliberately"* and this task falsifies it. Rewrite it to record that `PHN-5` removed the exemption
argument by removing the exemptions, and keep the reasoning — it is the argument for never adding an
allowlist later.

**8c. Fix the worktree root defect (`C-100`)** — `:236`, one line, per the code in `C-100`. Correct
the `<remarks>` at `:213-216`, whose claim that a relocated worktree *"is scanned normally, which is
correct"* is false whenever the path contains the segment.

⚠ **Say in the PR body that 8c is an unrelated test-infrastructure repair**, with the measured
failure. A silent fix inside a PII row reads as scope creep; a named one reads as a Builder who
noticed.

---

## 3. Ordering

Task 1 first — everything depends on `ForPhone`. Tasks 2–6 are independent of each other and of
their own order. Task 7 any time. **Task 8 last, and 8a before 8b**, because 8b is red until 2–6
land and 8a is what keeps it green afterwards.

**One PR, not several.** The deliverable is a *property* — no raw phone number and no contact name in
any log line these components write — and §4.4's lint asserts it across three assemblies. A property
test cannot pass until every site is fixed, so splitting means early PRs ship with the assertion
absent, which is how a partial fix gets recorded as a complete one. `TTS-11` §3 made the identical
argument for the identical reason; this row is its sibling and the reasoning transfers whole.

---

## 4. Test plan

> ⚠ **This repository has repeatedly found tests that passed against a deliberately broken
> implementation** — `TTS-11` counted six consecutive cycles, three in `PHN-2` alone. Every pin below
> names the mutation that must make it fail, and **Builder must run each mutation and record the
> result in the PR body**, not reason about it. Where a test cannot falsify something, that is
> stated.

Every test uses a distinctive sentinel number — **`5550137424`** — chosen so `DoesNotContain` cannot
pass by accident against fixture data, and so its last four digits (`7424`) are searchable on their
own. **Assert on both the whole number and on `7424`**: a regression that reinstates `***{last4}`
would pass a test that only looked for the full string.

⚠ **Every test must also assert the log is non-empty.** Without it the whole suite passes vacuously
against a component that logs nothing. `TTS-11`'s tests carry that guard for the same reason.

### 4.1 `T1` — `ForPhone` itself

**File:** `tests/Radio.Core.Tests/Utilities/LogSafeTextTests.cs` (extend; the class already holds the
`Sentinel` convention and the exact-value discipline to copy).

| Test | Pins | Falsifying mutation |
|---|---|---|
| `ForPhone_SameNumberInDifferentFormats_ProducesSameToken` | ⭐ `C-98`. `"+1 (555) 013-7424"`, `"15550137424"`, `"5550137424"` → one token | drop the `Normalize` call → fails |
| `ForPhone_IsIdempotentUnderNormalization` | `ForPhone(x) == ForPhone(Normalize(x))` — the property that lets call sites pass either | as above |
| `ForPhone_DifferentNumbers_ProduceDifferentTokens` | the hash is present at all. ⚠ **Use two numbers of equal digit length** | replace the hash with a constant → fails |
| `ForPhone_NullEmptyOrDigitless_ReturnsEmptyPhone` | `[InlineData(null)] [InlineData("")] [InlineData("unknown")] [InlineData("(withheld)")]` | — |
| `ForPhone_TokenCarriesNoDigitsOfTheInput` | `DoesNotContain("5550137424")` **and** `DoesNotContain("7424")`; `NotEqual(EmptyPhone, token)` | return the input → fails |
| `ForPhone_IsStableAcrossProcesses` | the encoding and the algorithm | `Encoding.Unicode` → fails; `GetHashCode()` → fails |
| `For_IsUnchangedByTheTokenExtraction` | ⭐ Task 1's safety net — the **existing** `For("héllo") == "txt:3c48591d/5"` | any change to `Token`'s text arm → fails |

⚠ **`ForPhone_IsStableAcrossProcesses` needs a literal digest, and this plan does not invent one.**
Every other code block here was read out of the tree; a hash cannot be. **Builder computes it once
and pastes the output:**

```bash
python3 -c "import hashlib;print('phn:'+hashlib.sha256('5550137424'.encode()).hexdigest()[:8])"
```

Then hard-codes that literal, exactly as `For_IsStableAcrossProcesses_OverUtf8Bytes` hard-codes
`"txt:3c48591d/5"`. ⛔ **Do not write the test as `Assert.Equal(ForPhone(x), ForPhone(x))`** — that
passes against every implementation including a constant, and it is not a cross-process pin.

### 4.2 `T2` — the two Infrastructure services, driven for real

**Files:** `tests/Radio.Infrastructure.Tests/External/PhoneContactLookupServiceLogSafetyTests.cs`
and `…/PhoneCallClientLogSafetyTests.cs`.

Reuse `CapturingLoggerProvider` (`tests/Radio.Infrastructure.Tests/External/GvMediaClientTests.cs:480-512`)
— it is `internal` and visible across that assembly, and its `IsEnabled` returns `true` at **every**
level, which is what makes the Debug sites `P2`/`P3`/`P4` observable at all.

`PhoneContactLookupService` takes an `HttpClient`, so a stub handler drives every arm offline:

- **404 / non-success** → exercises `P2` and `P4`.
- **200 with a `Name`** → exercises `P3`, and asserts `contact.Name` is **absent** from the log while
  still being the method's return value.
- **a throwing handler** → exercises `P5` **including the exception** (`C-103`).
- **a `_pbapRepo` returning a contact** → exercises `P1`, and asserts the `DisplayName` is absent.

Assert in every arm: the sentinel is absent, `7424` is absent, `LogSafeText.ForPhone(sentinel)` is
**present in at least one message**, and the message list is non-empty.

> **Falsifying mutations, all five to be run:** restore the raw argument at `:62`, `:78`, `:90`,
> `:96`, `:102` — each must fail its own arm. ⚠ **Run `:90`'s specifically.** It is the line that was
> already "masked", and a test that only checked the *number* would pass against the pre-fix code
> while `contact.Name` leaked. The name assertion is the one that catches it.

> **What `T2` cannot falsify:** `PhoneCallClient`'s SignalR handler is reached through
> `_hubConnection.On<…>` and this plan did **not** verify that the registration at `:64-65` can be
> driven without a live hub. If it cannot, call `OnCallStateChangedWithName` directly — it is
> `private`, so this needs either `InternalsVisibleTo` (already granted for
> `Radio.Infrastructure.Tests`, `Radio.Infrastructure.csproj:15`, but that does not reach `private`)
> or a small visibility change. **Builder decides and says which**; if `P6` ends up pinned only by
> the lint, that is acceptable and must be written in the test file rather than implied.

### 4.3 `T3` — the four `Radio.Web` services

**File:** `tests/Radio.Web.Tests/Services/PhonePiiLogSafetyTests.cs`

`Radio.Web.Tests` is a separate assembly and needs its own capturing logger. **A 30-line duplicate is
correct here** and preferable to a shared test package — `TTS-11` §4.1 made the same call.

`GvTrunkApiService`, `PbapApiService` and `ContactResolutionService` all take an `HttpClient`; a
throwing stub handler reaches `P9`, `P10` and `P11` directly. `PhoneHubService` (`P8`) is a SignalR
lambda and has the same reachability question as `P6` — same instruction: drive it if you can, and if
not, say so in the file.

⚠⚠ **The capturing logger must record `exception?.ToString()`, not only the formatted message**
(`C-103`). Three of these four sites log an exception, and a harness that ignores it would report
green while the number leaked through the stack trace. **Verify the harness on this point first** —
write one test that deliberately throws with the sentinel *in the exception message* and confirm the
harness sees it. If it does not, the rest of `T3` proves nothing about exceptions.

> **Falsifying mutations:** restore the raw argument at each of `:82`, `:94`, `:104`, `:173`.

### 4.4 `T4` — the lint, which is the durable deliverable

Task 8's rule is itself the regression pin (`C-104`).

> **Falsifying mutations — run at least three, from three different files:** re-add `phoneNumber` at
> `PhoneContactLookupService.cs:78`; re-add `number` at `GvTrunkApiService.cs:94`; re-add
> `phoneNumber` at `PbapApiService.cs:104`. Each must make `NoLogCallInTheSolutionPassesAKnownUserTextArgument`
> fail, and the failure must name the right file and line.

> ⚠ **What it cannot falsify, and this belongs in the file's own comment:** a new leak through a
> differently-named variable, or a contact **name**, for which there is no rule at all and no
> plausible one — `Name` is far too common an identifier. **The lint covers numbers, not names.** The
> name property is pinned only by `T2`'s assertions. Anyone reading a green lint as *"no phone PII is
> logged anywhere"* is reading it wrong, and the sentence saying so must be in the file or the rule
> should not ship.

### 4.5 Gates

- `dotnet build --configuration Release` — 0 warnings (warnings are errors in Release).
- `dotnet test --configuration Release` — full suite green.
  ⛔ **Never pipe it to `tail`** (`CLAUDE.md`): redirect, echo `$?`, then grep the file. Read the
  **per-project** summary lines.
  Known-failing on Windows and not regressions: four `SrcVariableResamplerTests`
  (`libsamplerate.so.0`) and `NwsObservationIntegrationTests.RealNwsCall_*` (live network).
- ⚠ **If the suite is run from a git worktree, `LogSafetyLintTests` is red before Task 8c and green
  after** (`C-100`). Do not mistake the pre-fix failure for a finding about this row.
- Every mutation in §4.1–§4.4 run, with its result in the PR body. **A mutation that does not make
  its test fail is a finding, not a formality.**

### 4.6 On-box verification — a check, not a gate

After deploy, place a call to the console and then:

```bash
ssh mmack@radio "journalctl -u radio-web --since '-10min' --no-pager | grep -cE '[0-9]{7}'"
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/web-*.txt | head -1); grep -oE "phn:[0-9a-f]{8}" $F | tail -5'
```

Expect `0` for the digit run and `phn:` tokens present. ⚠ **Keep it short and bounded** — `CLAUDE.md`
records that heavy log reads on this box correlate with audible audio distortion, and the `--since`
bound is not optional.

⚠ **This check is weaker than it looks and the PR body should say so.** `PhoneIntegration:Enabled` is
`false`, so the `Radio.API` half (`P1`–`P7`) cannot be exercised on the box at all without flipping a
flag this row deliberately does not touch. **Only the `Radio.Web` half is observable in production**,
which is `C-94` cutting the other way.

---

## 5. Docs and queue

| # | Task |
|---|---|
| 1 | `design/FUTURE-WORK.md` — close `TTS-11`'s § *Phone-number logging* item; correct its count to eleven; add §6.1's two filed items. |
| 2 | `src/Radio.Web/appsettings.json` — the sink sentence (Task 7). |
| 3 | `docs/BUILDER_QUEUE.md` — Builder marks `PHN-5` ✅ at merge and adds a cycle banner entry. |
| 4 | ⛔ **`docs/HANDOFF-GA-PUNCH-LIST.md` — nothing.** `PHN-5` has no punch-list row (punch list `:1441`), so there is no tier count to move and no cell to correct. Stated so its absence is not read as an omission. |

---

## 6. Deliberately not done

### 6.1 The `PhoneIntegration:Enabled` asymmetry, and the status endpoint that lies

`C-101` and `C-102`. Both real, both verified first-hand, both out of scope:

1. **Gate the `Radio.Web` phone path, or decide it needs no gate.** Requires choosing between binding
   a second options section into `Radio.Web`, minting a `RotaryPhone:Enabled`, or gating
   `Program.cs:652-655`. All three change whether `radio-web` connects to the RotaryPhone hub at
   boot. **Owner's call, and the honest recommendation is that it wants a row** — `C-94` means five
   log sites, and an unknown amount of other behaviour, run ungated on every box.
2. **`IntegrationsController.cs:215-223` reports `Enabled = true` unconditionally.** Cheap to fix
   (read `IOptions<PhoneIntegrationOptions>.Value.Enabled`, which the method already has in hand at
   `:212`), and a wrong answer in a status endpoint is its own defect class.

⛔ **Neither is fixed here**, and the reason is the one §0.7 gives: they are behavioural changes, and
folding them into a masking row would put a UAT-bearing change inside a diff whose whole value is
that it is mechanical.

### 6.2 `Radio.Web`'s unrestricted console sink

`C-93`. Adding `restrictedToMinimumLevel` there would cut journald volume on a box where log volume
correlates with audible distortion — genuinely worth doing, and a `LOG-` row about a second service's
sink policy. It changes what **every** `Radio.Web` line does, not just these four. ⛔ **And it must
not be mistaken for a fix for this row:** a level is a volume control, not a privacy control
(`C-96`), and a leak suppressed by configuration is still a leak.

### 6.3 `GvMediaCache.MaskFor` folded into `LogSafeText`

It would complete §1.2's thesis — one algorithm, one file, three prefixes — and it is a one-line
delegation. **Not done, for a mechanical reason rather than scope discipline:** `MaskFor` and its
sibling `FileNameFor` (`GvMediaCache.cs:68-72`) take **different byte counts from the same hash** (4
and 16), and the log mask correlating with the on-disk filename is the property that comment is
about. Splitting them across two files is two chances for them to drift apart. It is also `internal`
to `Radio.Infrastructure` and working. **Left alone deliberately**, and named here so *"one
algorithm"* is not read as a claim that there is literally one implementation today. There are two,
and this row does not make it three.

### 6.4 The `CLAUDE.md` correction

`C-93` means `CLAUDE.md` § *Deployment*'s `LOG-11` paragraph is true of `Radio.API` and misleading
about `Radio.Web`. The correction belongs there — it is exactly the kind of box fact that file
exists to stop people rediscovering. ⛔ **Not made here**: `CLAUDE.md` is the repository's shared
context file and editing it inside a feature PR is how two sessions end up disagreeing about it.
Task 7 puts the fact in `src/Radio.Web/appsettings.json`, next to the sink it describes, where the
next person to add a sink will see it. **Recommend to the owner that `CLAUDE.md` follow.**

### 6.5 `C-100` as its own row

Task 8c fixes a live test defect that has nothing to do with PII. The argument for doing it here is
that this row edits that file anyway and a Builder working in a worktree meets the failure on their
first `dotnet test`. The argument against is that it is unrelated, and unrelated fixes inside a
themed PR are how diffs stop being reviewable.

**Taken here, on the grounds that leaving a known-red test in a file you are editing is worse.** If
the owner prefers it split, it is a clean five-line row: the one-line fallback, the comment
correction, and a test that asserts `FindRepositoryRoot` resolves from a path containing a
`worktrees` segment. ⚠ **In that case Task 8's other parts still cannot be verified from a worktree**
— the Builder must run the suite from `D:/prj/RTest/RTest` itself.

---

## 7. Self-review

### 7.1 What was verified first-hand at `6c220461`

- All eleven sites: file, line, exact statement text, level, enclosing method. `P1`–`P5` read
  directly; `P6`–`P11` read and confirmed against the owner's list, which was correct in every
  particular.
- `PhoneContactLookupService.cs:69` carries no PII — the one line in that file **not** in scope.
- **No SMS body is logged anywhere** — `GvBridgeSendService`, `GvBridgeApiService` and
  `PhoneHubService` swept; the negative is recorded in §0.2 so it is auditable.
- `LogSafeText.cs` in full: `For`, `Empty`, the algorithm, and the threat-model remarks §1.2 quotes.
- `GvMediaCache.MaskFor` (`:79-83`) and `FileNameFor` (`:68-72`) — including the shared-hash coupling
  that §6.3 turns on.
- `PhoneNumberNormalizer` in full, including that `Normalize` is idempotent.
- `LogSafeTextTests` in full — the discipline §4.1 copies, and the exact literal `"txt:3c48591d/5"`
  that makes Task 1's extraction safe.
- Both services' Serilog configuration, in `appsettings.json` **and** in `Program.cs`, which is where
  `C-93` actually lives. Neither project has an `appsettings.Development.json`.
- `PhoneIntegrationOptions` in full; **every** read of `.Enabled` in `src/` (there is one); the
  unconditional registrations at `Radio.Web/Program.cs:293`, `:387`, `:485`, `:652-655`.
- `LogSafetyLintTests.cs` in full, including the `phoneNumber` exemption at `:62-67` that `C-104`
  retires and the `SafeCall` regex at `:78` that `C-99` widens.
- **`C-100` was measured, not reasoned** — the lint was executed from this worktree and its failure
  output is quoted verbatim.

### 7.2 What could not be verified, and what it costs

1. **Nothing here was built or run except `LogSafetyLintTests`.** Every code block is written against
   read source and is unexecuted.
2. **Whether `CapturingLoggerProvider` records exception text** (`C-103`, §4.3). **Four sites' tests
   depend on it and Builder must check it first.** If it does not, either extend it or state in the
   test file that the exception channel is unpinned — do not let a green run imply coverage it does
   not have.
3. **Whether `P6` and `P8` can be driven without a live SignalR hub** (§4.2, §4.3). Both are
   registration-time lambdas. If they cannot, they are pinned by the lint alone, which is weaker.
4. **Whether any `HttpRequestException` in this subsystem actually carries the request URI**
   (`C-103`). The URL embeds the number; the exception's contents were not enumerated. §4.3 measures
   it rather than asserting either way.
5. **The `phn:` token's collision behaviour was not analysed.** 4 bytes is 32 bits; over a household
   contact list a collision is negligible, and a collision costs a confused correlation rather than a
   leak. Stated because §1.2 claims the token *identifies* a caller and that claim is probabilistic.
6. **No box was touched.** §4.6 is a post-deploy check and its `Radio.API` half is unreachable while
   `PhoneIntegration:Enabled` is false.

### 7.3 What would falsify this plan's central decision

§1.2 rests on the threat being **a person reading the log**, inherited from `LogSafeText`'s own
documentation. If the owner's threat model is instead *"an operator holding a phone number needs to
find it in the log without tooling"*, `***{last4}` wins and §1.2 is wrong — the fix is then a shared
`LogSafePhone.Last4` helper and the rest of the plan is unchanged in shape. **That is the one
decision in this row worth putting back to the owner**, and `C-97` records that it reverses a merged
plan's stated expectation.
