# PLAN — `TTS-11` · The console keeps a plaintext copy of everything it says out loud

> **Row:** `TTS-11`, P1, `docs/HANDOFF-GA-PUNCH-LIST.md:1076`.
> **Branch:** `fix/tts-11-no-user-text-in-logs`
> **Estimate:** **1.5 d**, not the 0.5 h on the punch list. §0.2 says why the estimate moved.
> **Planned against** `main` at `603207df`. Every line number below was read out of the tree at that
> commit, not copied from a report. Where a line is likely to move, the statement is quoted.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

Since `LOG-11` the API's console sink carries Warning and above, and under systemd the console *is*
the journal — so `Information` lines went to the **file sink** instead. `src/Radio.API/appsettings.json:17-18`
sets `"Override": { "Radio": "Information" }`, so every `Radio.*` Information line is written to
`./logs/radio-*.txt` and kept for the rolling retention window. Eleven log statements across five
files write the text of a spoken utterance into that sink. On the appliance that means a plaintext
copy of every SMS body the console reads aloud is **at rest on a machine in a family home**, and
`GET /api/system/logs` renders it in Settings → Logs with `Info` one dropdown away.

The fix is not "stop logging". It is: **keep the event, drop the payload, and replace it with
something that correlates better than the payload did.**

### 0.2 ⚠ The row was filed for three sites. There are eleven, and two of them are worse than any of the three.

The punch-list row names three paths and estimates 0.5 h. That estimate was honest for three lines.
A sweep of every `Log*` call in the solution found **eleven** utterance-bearing sites — and separately
four phone-number sites, which §6.1 files and deliberately does not fix.

The two that matter most were not in the filing:

- **`AnnouncementService.cs:40` logs the message untruncated.** Every other site clips to 47 or 50
  characters. This one does not, and it is the shared entry point for `NotificationsController` and
  for phone-call announcements. It is the single worst line in the set and nobody had found it.
- **`SoundFlowMasterMixer.cs:101-103` logs `source.Name` from generic mixer code** that has no idea
  it is holding speech. `TTSEventSource` is the only `IAudioSource` implementation whose `Name`
  embeds user text, so a completely domain-agnostic bookkeeping line leaks 47 characters of it.

That second one is the reason the "three independent paths" framing understated the problem. The
defect is not three lines that each decided to log text. It is **one property — `TTSEventSource.Name`
is user content wearing a display-name's clothes — plus a habit of logging payloads.** Any code that
logs an `IAudioSource`'s name is a leak, present and future.

**Full inventory, all read at `603207df`:**

| # | Site | Level | What reaches the sink |
|---|---|---|---|
| `L1` | `src/Radio.Infrastructure/Audio/Services/TTSFactory.cs:99-100` | Info | first 50 chars of the utterance |
| `L2` | `src/Radio.Infrastructure/Audio/Sources/Events/TTSEventSource.cs:109` | Info | **the whole utterance** |
| `L3` | `src/Radio.Infrastructure/Audio/Sources/Events/TTSEventSource.cs:124` | Debug | **the whole utterance** |
| `L4` | `src/Radio.Infrastructure/Audio/Services/AudioManager.cs:513-515` | Info | `TriggeringSource.Name` → `"TTS: "` + 47 chars |
| `L5` | `src/Radio.Infrastructure/Audio/Services/AudioManager.cs:523-525` | Debug | same argument, other transition arm |
| `L6` | `src/Radio.API/Controllers/NotificationsController.cs:48-49` | Info | **the whole** `request.Message` |
| `L7` | `src/Radio.Infrastructure/Audio/Services/AnnouncementService.cs:40` | Info | **the whole message, untruncated** |
| `L8` | `src/Radio.Infrastructure/Audio/Services/AnnouncementService.cs:91-92` | Info | **the whole message, untruncated** |
| `L9` | `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs:101-103` | Info | `source.Name` → 47 chars |
| `L10` | `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs:117-119` | Info | same, on remove |
| `L11` | `src/Radio.API/Controllers/SourcesController.cs:645-647` | Info | first 50 chars |

`L3` and `L5` are Debug and therefore **not** written under today's config. They are in scope anyway:
they are one `appsettings.json` edit from being written, the test harness observes them (§4 — the
capturing logger's `IsEnabled` returns `true` at every level), and leaving two known leaks in place
because a config value currently suppresses them is how the next `LOG-` row gets filed.

### 0.3 Corrections to the record made while planning

Numbered continuing from `C-81`.

- **`C-82` — the ducking line number in the row is wrong.** `docs/HANDOFF-GA-PUNCH-LIST.md:1076` says
  *"the call opens at `:499` and the offending argument is at `:501`"*. At `603207df`, `AudioManager.cs`
  lines 499 and 501 are **comment prose inside the `<remarks>` block**. The statement is at
  **`:513-515`**. `PHN-1f` and `PHN-2` both edited this method after the row was filed. The row hedged
  correctly — it said *"quoted rather than trusted to the number"* — and the quote is what let this be
  re-found. ⛔ **Do not edit the punch list to fix this**; that file is held by another pass. It is
  recorded here.
- **`C-83` — there is a second `AudioManager` site nobody filed.** `:523-525` logs the identical
  `e.TriggeringSource?.Name ?? "unknown"` argument at Debug, in the `else` arm added by `PHN-1f`.
  A fixer who reads only the row fixes one of two.
- **`C-84` — `TTSEventSource:92` is wrong in two places.** The row says `:92` for the whole-string
  line; the test comment at `tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs:1218-1220`
  says `:92` and `:107`. Actual: **`:109`** and **`:124`**. Task 7 fixes the test comment, which this
  plan may touch.
- **`C-85` — the row's proposed fix does not cover its own third path.** The row says *"log the source
  **type** and a character count, never the name"*. That prescribes the fix for `L4`/`L5`/`L9`/`L10`
  (the `.Name` sites) and says nothing about `L1`/`L2`/`L6`/`L7`, which log a **string argument**, not
  a name. §2 splits the fix in two for that reason.
- **`C-90` — ⚠ ADDED AT MERGE, 2026-09-05. Two of this plan's own claims were falsified during the
  build, and both are corrected in the shipped code rather than here alone.**
  1. **§1.3 and §1.4's *"`Id` joins a ducking line to its mixer line"* is FALSE.** An adversarial
     reviewer enumerated all six `AddSource`/`RemoveSource` callers against all four
     `StartDuckingAsync` callers: **no path emits both lines for the same source.**
     `AnnouncementService` and `EventPlaybackService` duck but never call `AddSource`;
     `SourcesController.PlayTTSEvent` calls `AddSource` but never ducks. `EventPlaybackService.cs:14`
     already said so in the tree. The `Id` field was **kept** — it identifies the source without
     naming it, which is independently worth having — but the justification was wrong and is removed
     from the code comments.
  2. **§1.3's *"`Name` is redundant there today"* is FALSE for one pair.** `AudioSourceBase.cs:28`
     defines `Id => $"{Type}-{guid}"`, so `{SourceType}` is fully contained in `{SourceId}` on the
     same line; and `RadioAudioSource` and `SDRRadioAudioSource` **both** return
     `AudioSourceType.Radio` while their `Name`s differ (`"Radio (RF320)"` vs `"SDR Radio (RTL-SDR)"`).
     So the mixer line did lose the only field distinguishing the two radio backends. The change
     stands (it is required for the privacy property) and the loss is bounded: `AudioManager.cs:230`
     still logs `source.Name` on the primary path and is deliberately untouched.
- **`C-91` — the set is TWELVE, not eleven.** `src/Radio.API/Controllers/AudioController.cs:413`
  logs `GetActiveSources().Select(s => s.Name)` **at `Warning`**, so it reaches journald *as well as*
  the file sink — a strictly larger exposure than any of `L1`–`L11`. It is reachable because
  `SourcesController.cs:655` adds a TTS source to the mixer and nothing ever removes it. Found by an
  adversarial reviewer during the build, not by the planning sweep, and fixed in this row as `L12`.
  **It is the plan's own thesis coming true inside the plan's own cycle:** any code that logs an
  `IAudioSource`'s `Name` is a leak, present or future.
- **`C-92` — §6.1's phone inventory is INCOMPLETE.** It lists five sites in two files; there are
  **seven across five**. `PhoneCallClient.cs:128-129` (Info, number **and** caller name),
  `Radio.Web/Services/Hub/PhoneHubService.cs:82` (Info) and
  `Radio.Web/Services/ApiClients/PbapApiService.cs:104` (Debug) were missed because the sweep was
  scoped to `Radio.API`'s logging config and **`Radio.Web` is a separate service with its own sink.**
  Recorded in `design/FUTURE-WORK.md` § *Phone-number logging* with the tier argument.
- **`C-86` — the estimate.** 0.5 h was scoped to three lines. Eleven sites across five files, plus a
  new helper with its own tests, plus five test classes, plus the mutation runs §4 requires, is **1.5 d**.

### 0.4 Things Builder must NOT do

- ⛔ **Do not change `TTSEventSource.Name`.** It is tempting — it is the root of `L4`/`L5`/`L9`/`L10` —
  and it is out of scope. `Name` is a display property; `src/Radio.Web/Components/Shared/NowPlayingPanel.razor:713`
  consumes a `SourceName` off the now-playing DTO, and this plan has **not** traced whether a TTS
  source's name can reach it. Changing `Name` would be a UI change wearing a logging fix's clothes.
  Fix the log lines; leave the property alone. If a later row wants `Name` to stop carrying text, that
  is a UX decision and belongs to the owner.
- ⛔ **Do not widen `NeitherTheTextNorTheRawMediaIdReachesAnyLogLineThisSeamWrites`.** §4.6 is the whole
  argument. Read it before touching that file.
- ⛔ **Do not fix the phone-number sites.** §6.1. They are real, one is at Warning, and they are not
  this row.
- ⛔ **Do not edit `docs/HANDOFF-GA-PUNCH-LIST.md` or `docs/HANDOFF-NEXT-SESSION.md`.** Both are held.

---

## 1. Decision — what replaces the content

Four candidates were considered. The row proposes the fourth; this plan takes it and adds a hash.

| Option | What an operator keeps | Why not / why |
|---|---|---|
| **Delete the line** | nothing | Rejected for all eleven. `L2` is the only evidence the audio stream was materialised; without it, *"the console said nothing and there is nothing in the log"* becomes indistinguishable from *"the console was never asked to say anything"*. That is a real diagnosis, lost. |
| **Length only** (`chars=142`) | plausibility | Answers "was the string empty / absurd", which is genuinely useful. But it cannot join two lines: `L1`, `L2`, `L9` and `L4` all fire for the same utterance and a bare length is the same for every 142-character message. |
| **Category only** (`speech`) | the event | Weakest. The lines already say which subsystem they are; a category adds nothing the template does not. |
| **Hash prefix + length** ✅ | correlation **and** plausibility | Taken. |

### 1.1 The chosen shape, and the idiom it copies

`src/Radio.Infrastructure/External/GvMediaCache.cs:79-83` already establishes the house form for this
exact problem — `"gvm:"` plus the first 8 hex characters of a SHA-256, so *"a log line and a file on
disk correlate without either carrying the id"*. This row extends the same shape to text:

```
txt:9f2ab41c/142
```

Prefix, 8 hex of SHA-256 over the UTF-8 bytes, then the character count.

**What this buys that the truncated text never did.** Today `L1` clips at 50, `TTSEventSource.Name`
clips at 47, and `L2` clips at nothing — so three lines about *the same utterance* print three
different strings and an operator cannot reliably tell they are the same event. A stable token makes
`L1 → L2 → L9 → L4` a single traceable chain for the first time. **The privacy fix makes the
diagnosis better, not worse**, which is the bar §0.4 of the row was really asking for.

### 1.2 ⚠ The honest limitation, and it goes in the XML doc

**A hash of a short utterance is reversible by enumeration.** *"Yes"*, *"OK"*, *"Dinner's ready"*,
*"The front door is open"* — the candidate space for a smart-home announcement is small, and anyone
holding the log file can hash a word list and match. `MaskFor` has the same property in principle;
it gets away with it because a GV media id is high-entropy, and an utterance is not.

So the helper's documentation must say what it is: **a correlation token, not a confidentiality
boundary.** It defends against a person reading the log — which is the actual threat here, a family
member or a repair tech opening Settings → Logs — and not against an adversary with a candidate list.

This matters beyond politeness. `CLAUDE.md` § *Pre-Merge Review* exists because this repo has shipped
three comments that asserted more than the code enforced. A helper called `Mask` whose doc implies
irreversibility would be the fourth, and the reviewer is briefed to falsify exactly that claim.

### 1.3 Two fixes, not one — the `.Name` sites do not need the helper

- **String-argument sites (`L1`, `L2`, `L3`, `L6`, `L7`, `L8`, `L11`)** → replace the argument with
  `LogSafeText.For(text)`.
- **`.Name` sites (`L4`, `L5`, `L9`, `L10`)** → **drop `Name` entirely and log `Type` and `Id`.**
  No helper, no hash. `SoundFlowMasterMixer` **already logs `source.Id`** on both lines, so `Name` is
  redundant there today; the ducking lines gain `Id`, which lets an operator join a ducking line to
  the mixer line — something `"TTS: Dinner's rea..."` never allowed.

### 1.4 What each operator-facing line still diagnoses afterwards

The row's own warning: do not blind the operator to fix the leak. Line by line, for the two lines
someone actually uses to diagnose a silent console:

| Line | Before | After | Still supports |
|---|---|---|---|
| `L4` ducking started (Info) | `source=TTS: Dinner's rea...` | `source=TTS id=evt-7c1f duckLevel=30% activeEvents=1` | *"is the radio ducked, by what kind of thing, how far, and is it the same event the mixer logged?"* — **more** than before, because `id` joins to `L9`. |
| `L6`/`L11` announce route (Info) | `'Front door is open' (priority 8)` | `txt:9f2ab41c/20 (priority 8)` | *"did the request arrive, at what priority, and was the body sane?"* Priority is the field that decides preemption and it is untouched. Length catches an empty or truncated body. |
| `L1` TTS create (Info) | first 50 chars + engine | `txt:9f2ab41c/20` + engine | *"which engine was asked, for how much text"* — and the token now joins forward to `L2`. |
| `L2` source initialized (Info) | whole utterance | `txt:9f2ab41c/20` | *"the stream was materialised for the utterance `L1` created"* — the join is new. |

Nothing an operator uses to find a silent console is lost. The only thing lost is the ability to read
the message off the screen, which is the defect.

---

## 2. Tasks

### Task 1 — the helper

**New file:** `src/Radio.Core/Utilities/LogSafeText.cs`.

`Radio.Core` rather than Infrastructure, because `L6` and `L11` are in `Radio.API` and `L1`–`L10` are
in `Radio.Infrastructure`; both reference Core, and neither references the other. `Radio.Core` has no
dependencies (`CLAUDE.md` § *Solution Structure*), and this needs only `System.Security.Cryptography`
and `System.Text`.

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Radio.Core.Utilities;

/// <summary>
/// Renders user-supplied text as a log-safe token: a stable hash prefix plus a character count.
/// </summary>
/// <remarks>
/// The shape deliberately mirrors <c>GvMediaCache.MaskFor</c> — a short literal prefix plus the
/// first 8 hex characters of a SHA-256 — so that a reader who knows one form recognises the other.
///
/// ⚠ THIS IS A CORRELATION TOKEN, NOT A CONFIDENTIALITY BOUNDARY, and the difference is not
/// pedantic. Announcement text is drawn from a small candidate space ("Yes", "The front door is
/// open"), so anyone holding both the log file and a word list can recover a short utterance by
/// hashing candidates. What this defends against is a person READING the log — the family member or
/// technician who opens Settings → Logs, which is the actual exposure TTS-11 was filed for. It does
/// not defend against an adversary who can enumerate. Do not describe it as anonymised, and do not
/// reach for it in a context where enumeration is the threat: there, log nothing.
///
/// The character count is deliberately exact rather than bucketed. It is the field that answers
/// "was the string empty, or absurdly long" — the two questions an operator actually asks about a
/// TTS payload — and an exact count over a space an attacker can already enumerate leaks nothing
/// the hash has not already leaked.
/// </remarks>
public static class LogSafeText
{
  /// <summary>The token for null or empty text. Distinguishable from a hash at a glance.</summary>
  public const string Empty = "txt:empty";

  /// <summary>
  /// Returns <c>txt:{8 hex}/{length}</c> for <paramref name="text"/>, or <see cref="Empty"/>
  /// when it is null or empty.
  /// </summary>
  public static string For(string? text)
  {
    if (string.IsNullOrEmpty(text))
    {
      return Empty;
    }

    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
    return string.Concat(
      "txt:", Convert.ToHexString(hash, 0, 4).ToLowerInvariant(), "/", text.Length.ToString());
  }
}
```

**Note the `Length` semantics.** `text.Length` counts UTF-16 code units, so an emoji counts 2 and a
combining sequence counts more than its glyphs. That is fine for the diagnostic question ("empty?
absurd?") and it is what every existing truncation site already used. It is called out so a reviewer
does not file it as a bug.

**Tests:** `tests/Radio.Core.Tests/Utilities/LogSafeTextTests.cs`
- same input → same token (that is the entire point of choosing a hash over a counter)
- different input → different token
- `null` and `""` → `Empty`
- the token contains none of the input, asserted against a distinctive sentinel
- `For("héllo")` is stable across runs — pins the UTF-8-not-UTF-16 encoding choice

> **Falsifying mutation:** change `Encoding.UTF8` to `Encoding.Unicode` → the stability test fails.
> Change `HashData` to `text.GetHashCode()` → the cross-run stability test fails (`GetHashCode` on
> `string` is randomised per process since .NET Core 3.0). ⚠ **Run both.** The second is the one
> that matters: `TTSFactory.cs:107` already builds a cache key with `text.GetHashCode()`, so a
> plausible "consistency" edit is one keystroke away.

### Task 2 — `TTSEventSource` (`L2`, `L3`)

`src/Radio.Infrastructure/Audio/Sources/Events/TTSEventSource.cs`

```csharp
// :109
Logger.LogInformation("TTS event source initialized: {Text}", LogSafeText.For(_text));
// :124
Logger.LogDebug("Playing TTS audio: {Text}", LogSafeText.For(_text));
```

Keep the `{Text}` placeholder name — it still names what the token stands for, and renaming it would
break any log query the owner has. Add `using Radio.Core.Utilities;`.

⚠ **Leave the constructor's `_name` construction at `:47-49` alone** (§0.4).

### Task 3 — `TTSFactory` (`L1`)

`src/Radio.Infrastructure/Audio/Services/TTSFactory.cs:99-100`

```csharp
_logger.LogInformation("Creating TTS audio for text: {Text} with engine {Engine}",
  LogSafeText.For(text), engine);
```

The single quotes around `'{Text}'` go: they framed a human-readable string and the token is not one.

⚠ **Do not touch `:107`** — `var cacheKey = $"{engine}_{voice}_{text.GetHashCode()}";` is a cache key,
not a log line, and it never reaches a sink. It is mentioned only so a fixer sweeping for `text` does
not "fix" it and silently change cache behaviour.

### Task 4 — `AnnouncementService` (`L7`, `L8`)

`src/Radio.Infrastructure/Audio/Services/AnnouncementService.cs`

```csharp
// :40
_logger.LogInformation("Announcing: {Message} (priority {Priority})",
  LogSafeText.For(message), priority);

// :91-92
_logger.LogInformation("Playing sound {Sound} then announcing: {Message} (priority {Priority})",
  soundPath, LogSafeText.For(message), priority);
```

`soundPath` stays as-is — it is a server-side file path chosen by config, not user text.

### Task 5 — the `.Name` sites (`L4`, `L5`, `L9`, `L10`)

**`src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs`** — `Id` is already there, so
`Name` simply goes and `Type` takes its place:

```csharp
// :101-103
_logger.LogInformation(
  "Added audio source {SourceId} ({SourceType}) to mixer",
  source.Id, source.Type);

// :117-119
_logger.LogInformation(
  "Removed audio source {SourceId} ({SourceType}) from mixer",
  source.Id, source.Type);
```

⚠ `SoundFlowMasterMixer.cs:117-119`'s message says *"Removed audio source … from mixer"* while the
method mutates `_sources` only. `CLAUDE.md` § *Pre-Merge Review* names this exact line as failure
mode 1 of three, and a fix that *"landed one layer too high and silently did nothing for months"*
trusted that wording. **This row does not fix that** — it changes only the arguments. Do not
opportunistically reword the message; that is a behavioural claim needing its own row.

**`src/Radio.Infrastructure/Audio/Services/AudioManager.cs`** — both arms:

```csharp
// :513-515
_logger.LogInformation(
  "Ducking started: source={TriggerSource} id={TriggerId}, duckLevel={DuckLevel:F0}%, activeEvents={EventCount}",
  e.TriggeringSource?.Type.ToString() ?? "unknown", e.TriggeringSource?.Id ?? "unknown",
  e.DuckLevel, e.ActiveEventCount);

// :523-525
_logger.LogDebug(
  "Ducking continues: source={TriggerSource} id={TriggerId} left, activeEvents={EventCount}",
  e.TriggeringSource?.Type.ToString() ?? "unknown", e.TriggeringSource?.Id ?? "unknown",
  e.ActiveEventCount);
```

⚠ **`:481-483` logs `_activeSource.Name` and must NOT be changed.** Verified safe:
`SwitchSourceAsync` throws `"Only primary sources can be switched to"` before assigning
`_activeSource` (`AudioManager.cs:180`), and every primary implementation returns a constant `Name`
(`"Bluetooth Audio"`, `"File Player"`, `"Radio (RF320)"`, `"SDR Radio (RTL-SDR)"`, `"Test Tone"`,
`"Vinyl Turntable"`, `"Generic USB Audio"`). Same for `:124`, `:188-190`, `:203-205`, `:230`, `:592`.
Changing them would delete a genuinely useful operator field for no privacy gain. **This is the one
place in the row where the right answer is "leave it", and it is stated so a sweeping fixer does not
over-apply the rule.**

### Task 6 — the two controllers (`L6`, `L11`)

**`src/Radio.API/Controllers/NotificationsController.cs:48-49`**

```csharp
_logger.LogInformation("Notification announce request: {Message} (priority {Priority})",
  LogSafeText.For(request.Message), priority);
```

**`src/Radio.API/Controllers/SourcesController.cs:645-647`**

```csharp
_logger.LogInformation("Playing TTS event: {Text} with engine {Engine}",
  LogSafeText.For(request.Text),
  engine?.ToString() ?? "(configured default)");
```

### Task 7 — correct the stale comment in `EventPlaybackServiceTests`

`tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs:1216-1220` cites
`TTSFactory.cs:99`, `TTSEventSource.cs:92` and `:107`. Two of three are wrong (`C-84`), and after
Tasks 2–3 **all three describe a defect that no longer exists**. Rewrite that comment block to say
the leak was closed by `TTS-11`, keep the explanation of *why the name says "this seam writes"*
(which is still true and still load-bearing — §4.6), and cross-reference the new tests.

⛔ **The test's name does not change.** §4.6.

### Task 8 — docs

- `design/FUTURE-WORK.md` § *TTS seam* item 5 — mark closed, and record that the set was eleven, not
  three. Add the phone-number inventory from §6.1 as a new item **in the same pass**, so it is filed
  somewhere durable rather than living only in this plan.
- `docs/BUILDER_QUEUE.md` — Builder marks the row ✅ at merge.

---

## 3. Ordering

Task 1 first (everything depends on the helper). Tasks 2–6 are independent of each other and can be
done in any order or in parallel. Task 7 after 2–3. Task 8 last.

**One PR, not several.** The deliverable of this row is the *property* — no user text in any log line
these components write — and §4's tests assert it across components in three assemblies. A test for a
global property cannot pass until every site is fixed, so splitting means the early PRs ship with the
test disabled or absent, which is how a partial fix gets recorded as a complete one. The diff is
eleven argument edits, one 30-line helper and five test classes across three assemblies — moderate,
and reviewable in one sitting. The phone-number sites are a separate row precisely because they do
**not** share this property or this test harness (§6.1).

---

## 4. Test plan

> ⚠ **Six consecutive cycles have found a test that passed against a deliberately broken
> implementation — three in `PHN-2` alone.** Every pin below names the mutation that must make it
> fail, and **Builder must actually run each mutation and record the result**, not reason about it.
> Where a test cannot falsify something, that is stated rather than implied.

### 4.1 The harness

Reuse `CapturingLoggerProvider` — `tests/Radio.Infrastructure.Tests/External/GvMediaClientTests.cs:480-512`.
It is `internal`, so it is visible across the `Radio.Infrastructure.Tests` assembly without moving it,
and its `IsEnabled` returns `true` at **every** level, which is why `L3` and `L5` (Debug) are
observable. `Radio.Core.Tests` and `Radio.API.Tests` are separate assemblies and need their own copy
or a small shared equivalent — Builder's call; a 30-line duplicate is acceptable and preferable to a
new shared test package for this.

Every test below uses a distinctive sentinel — `"Marmalade sentinel four seven"` — so a
`DoesNotContain` cannot pass by accident against generic fixture text.

⚠ **Every test must also assert `Assert.NotEmpty(logs.Messages)`.** Without it the whole thing passes
vacuously against a component that logs nothing at all. This is not hypothetical: the existing
`…ThisSeamWrites` test carries that exact guard at `:1262-1263` with a comment saying why.

### 4.2 `T1` — the real `TTSEventSource`, with no fake anywhere in the chain

`tests/Radio.Infrastructure.Tests/Audio/Sources/Events/TTSEventSourceLogSafetyTests.cs`

`src/Radio.Infrastructure/Radio.Infrastructure.csproj:15` grants
`InternalsVisibleTo Radio.Infrastructure.Tests`, and `TTSEventSourceTests.cs:27` already constructs
the type directly — `new TTSEventSource(text, parms, stream, dur, logger)`. So the real type can be
driven with a `MemoryStream` and **no HTTP and no fake**.

Construct with the sentinel, run `InitializeAsync` then `PlayAsync`, then assert:
- the sentinel appears in **zero** captured messages
- `LogSafeText.For(sentinel)` appears in **at least one** — so "no text" is not achieved by logging
  nothing, the same guard `…ThisSeamWrites:1272` uses for the media-id mask

> **Falsifying mutations, both to be run:** restore `_text` at `:109` → must fail. Restore `_text` at
> `:124` → must fail. **Run the second one specifically.** It is a `LogDebug`, and if the capturing
> logger were ever changed to honour a minimum level, this test would silently stop covering `L3`
> while still passing. The mutation is the only thing that proves it currently does.

> **What `T1` cannot falsify:** a leak on a path `PlayAsync` does not reach — the error branch at
> `:149`, the stop path at `:293`, disposal at `:338`. Those were read and log no text today, but the
> test does not pin them. Name the test for what it drives.

### 4.3 `T2` — the real `TTSFactory`

`tests/Radio.Infrastructure.Tests/Audio/Services/TTSFactoryLogSafetyTests.cs`

`L1` at `:99` fires **before** any network call (the engine calls are at `:306` and `:394`), so a
factory with a voice configured and no credentials will log `:99` and then fail. Assert the captured
messages contain the token and not the sentinel.

⚠ **UNVERIFIED ASSUMPTION, and Builder must check it first.** This plan verified that `:99` precedes
the HTTP calls textually. It did **not** verify that no earlier guard throws before reaching `:99`
for a credential-less configuration. `:82-88` throws when `voice` is empty — so the test **must** set
`TTSOptions.DefaultVoice` — but whether a missing secret throws before or after `:99` was not traced.
**If it throws first, `T2` is unreachable as written**; fall back to asserting via
`TTSFactoryTests`' existing setup, or drop `T2` and rely on `T5`'s lint for `L1`. Do not paper over
it — if `T2` cannot observe `:99`, say so in the test file.

> **Falsifying mutation:** restore the 50-char prefix at `:99` → must fail.

> **What `T2` cannot falsify:** anything after the engine call. `TTSFactory` news up its `HttpClient`
> inline (`:306`, `:394`, `:525`, `:655`) rather than taking one by injection, so a successful
> synthesis cannot be simulated offline. `:373` and `:414` log byte counts and durations — read, and
> they carry no text — but no test in this row pins them. **That is a real hole and it is named here
> rather than hidden behind a confident test name.**

### 4.4 `T3` — the mixer, holding a real TTS source

`tests/Radio.Infrastructure.Tests/Audio/SoundFlow/SoundFlowMasterMixerLogSafetyTests.cs`
(or extend `SoundFlowMasterMixerTests.cs`)

Add a real `TTSEventSource` built from the sentinel to a real `SoundFlowMasterMixer`, then remove it.
Assert neither the sentinel nor `"TTS: "` appears.

> **Falsifying mutation:** restore `source.Name` at `:101-103` → must fail. Restore at `:117-119` →
> must fail. **Both.** `L10` is currently latent (no caller passes a TTS source to `RemoveSource`),
> so only the mutation proves the line is covered at all.

This is the most valuable test in the set, because it pins the *generic* leak. A future
`IAudioSource` whose `Name` embeds user text is caught here and nowhere else.

### 4.5 `T4` — both ducking arms

`tests/Radio.Infrastructure.Tests/Audio/Services/AudioManagerDuckingLogTests.cs`

Raise `DuckingStateChangedEventArgs` with `TriggeringSource` set to a real `TTSEventSource` built from
the sentinel, **once with `Transition = Started` and once without**, and assert on both.

> **Falsifying mutation:** restore `?.Name` at `:513-515` → must fail. Restore at `:523-525` → must
> fail. ⚠ **A test that drives only the `Started` arm leaves `:525` completely unpinned — which is
> exactly how `:525` escaped the original filing.** If only one mutation fails, the test is half a
> test; fix the test, not the record.

### 4.6 ⚠ `T5` — what happens to `…ThisSeamWrites`, and why the answer is "nothing"

`tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs:1204`

`PHN-1c` shipped a test whose name claimed *"neither the text nor the raw media id reaches any log
line"*, and it did not hold that property: the Speech arm ran on `FakeTtsFactory`, so the real
`TTSFactory` and `TTSEventSource` were never in the chain. It was renamed to end in `…ThisSeamWrites`,
and its comment at `:1212-1228` says so explicitly and ends *"Do NOT widen the name back without
first widening the chain to a real ITTSFactory."*

**The chain still cannot be widened, and Tasks 2–3 do not change that.** `TTSFactory` constructs its
own `HttpClient` (§4.3), so a real `ITTSFactory` in `EventPlaybackServiceTests` would need live
credentials and a network. **The honest name stays.** Task 7 corrects its stale line numbers and
records that the leak it described is now closed — and leaves both the name and the reasoning intact.

The property the name promised is delivered instead by `T1`–`T4`, which drive the real types
directly. **That is the actual lesson from the `PHN-1c` trap: when a harness cannot observe a
property, the fix is a harness that can, not a name that sounds like one.** Renaming
`…ThisSeamWrites` back to something broad because "the leak is fixed now" would re-commit the
original error with a fresh justification.

### 4.7 `T6` — the API-side sites

`tests/Radio.API.Tests/Controllers/NotificationsControllerLogSafetyTests.cs` and the `SourcesController`
equivalent. Post the sentinel, assert it is absent from the captured log.

> **Falsifying mutation:** restore `request.Message` / `request.Text` → must fail.

### 4.8 `T7` — a regression lint, shipped with its limitation written on it

`tests/Radio.Core.Tests/LogSafetyLintTests.cs`

Scan `src/**/*.cs` and fail if any `Log(Information|Warning|Error|Debug|Trace)` call passes an
argument matching a small forbidden set — `_text`, `request.Text`, `request.Message`, bare `message`,
`.Name` on an identifier named `source`/`ttsSource`/`TriggeringSource`, `phoneNumber`.

> **Falsifying mutation:** re-add any one of `L1`–`L11` → must fail. Run it for at least three of
> them, from three different files.

> ⚠ **What it cannot falsify, and this belongs in the test's own comment:** a new leak through a
> differently-named variable. It is a **regression lint over eleven known shapes, not a proof of the
> property.** Anyone reading a green run as "no user text is logged anywhere" is reading it wrong.
> If that sentence cannot be written honestly in the file, do not ship `T7`.

Recommended anyway: it is the only check that fires when someone re-adds one of these lines in six
months, and the eleven shapes it knows are the eleven that actually occurred.

### 4.9 Gates

- `dotnet build --configuration Release` — 0 warnings (warnings are errors in Release)
- `dotnet test --configuration Release` — full suite green
- Every mutation in §4.2–§4.8 run and its result recorded in the PR body. **A mutation that does not
  make its test fail is a finding, not a formality** — it means the test does not test what its name says.

### 4.10 On-box verification (optional, and it is a *check*, not a gate)

After deploy, speak a distinctive phrase through Settings → TTS preview or
`POST /api/notifications/announce`, then:

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "Marmalade" $F; grep -o "txt:[0-9a-f]\{8\}/[0-9]*" $F | tail -5'
```

Expect `0` for the phrase and the token present. ⚠ Keep it short — `CLAUDE.md` notes log reads on
this box correlate with audible audio distortion.

---

## 5. Self-review

- **Spec coverage.** All three filed paths are covered (`L1`/`L2`/`L3` → Task 2–3, `L4` → Task 5,
  `L6` → Task 6). The row's own proposed fix — *"log the source type and a character count"* — is
  implemented, with a hash added and §1.1 giving the reason.
- **Placeholders.** None. Every task has literal code.
- **Line numbers.** Every one read at `603207df`. `C-82`/`C-84` correct two that were wrong in the
  record. The four sites likeliest to move (`AudioManager`, twice) are quoted as well as numbered.
- **Type consistency.** `LogSafeText.For` takes `string?` and returns `string`, so it is safe at
  `L6`'s `request.Message` (nullable per the `IsNullOrWhiteSpace` guard at `NotificationsController.cs:41`)
  and at `L1`'s non-null `text`. `IAudioSource.Type` is an enum — `.ToString()` is explicit at the
  `AudioManager` sites because the null-coalescing operand is a `string`.
- **Unverified, listed rather than assumed:**
  1. Whether `TTSFactory.CreateAsync` reaches `:99` with no credentials configured (§4.3). **`T2`
     depends on this and Builder must check it first.**
  2. Whether a `TTSEventSource`'s `Name` can reach `NowPlayingPanel.razor:713`'s `SourceName`. Not
     traced; it is why §0.4 forbids touching `Name` rather than a reason to touch it.
  3. Whether `AnnouncementService` and `EventPlaybackService` add their TTS source to the mixer.
     `NoMixerSourceIsEverAdded` (`EventPlaybackServiceTests.cs:1171`) proves `EventPlaybackService`
     does **not**; `SourcesController.cs:654` proves that route does. `AnnouncementService`'s
     `SetActiveSource` was not traced. This changes **which** routes reach `L9`, not whether `L9`
     leaks — Task 5 and `T3` are correct either way.
  4. The exact rolling-retention window of the file sink. `appsettings.json` was read for the level
     override, not the retention policy; "7 days" appears in a sweep report and is **not** confirmed
     here. It affects how long a leaked copy persists, not whether it exists.

---

## 6. Deliberately not done

### 6.1 ⚠ Four phone-number sites — real, one at Warning, and NOT this row

Found by the same sweep, in the same sink, and **needing an owner decision before anyone files them**:

| Site | Level | Leaks |
|---|---|---|
| `src/Radio.Infrastructure/External/PhoneContactLookupService.cs:62` | **Info** | `LogInformation("PBAP contact found: {Number} → {Name}", phoneNumber, contact.DisplayName)` — full unmasked number **and** the contact's name |
| `src/Radio.Infrastructure/External/PhoneContactLookupService.cs:102` | **Warning** | `LogWarning(ex, "Contact lookup failed for {PhoneNumber}", phoneNumber)` — full number |
| `src/Radio.Infrastructure/External/PhoneContactLookupService.cs:78, 96` | Debug | full number |
| `src/Radio.API/Services/PhoneCallIntegrationService.cs:127` | Info | `"Phone ringing: {Announcement}"` where the announcement is built at `:126` as `$"Incoming call from {callerName}"`, and `callerName` falls back to the **raw phone number** (`PhoneContactLookupService.cs:106`) |

**Why they are not folded in.**
1. **Different data class.** Phone numbers and contact names are PII about third parties; utterance
   text is content the household authored. Different fix (mask the number, keep the shape) and a
   different conversation about what an operator needs.
2. **The fix is a different idiom.** `PhoneContactLookupService.cs:87-90` **already masks** —
   `$"***{phoneNumber[^4..]}"` — three lines below `:78`, which does not. So the fix there is *"apply
   the local idiom the file already contains, consistently"*, not *"adopt `LogSafeText`"*. Folding it
   in would put two competing masking schemes in one PR.
3. **`:102` is at Warning**, so it survives every log-level tightening including `LOG-11`'s. That is a
   genuinely different exposure profile and deserves its own tiering argument, not a footnote in this one.
4. `PhoneIntegration:Enabled` is false on a stock box, so nothing here is live today — the same
   argument the row makes for `L6` being a smaller exposure.

⛔ **This plan does not file a row for these.** `docs/BUILDER_QUEUE.md` was opened for `TTS-11` and
`OPS-7` only, and `docs/HANDOFF-GA-PUNCH-LIST.md` is held by another pass. Task 8 adds them to
`design/FUTURE-WORK.md` so they exist somewhere durable. **Whether they become a row is the owner's
call**, and the honest recommendation is yes — `:102` at Warning is the strongest single argument.

### 6.2 `TTSEventSource.Name` itself

§0.4. It is the root cause of four of the eleven sites and changing it is a UI decision.

### 6.3 The `SoundFlowMasterMixer` "Removed … from mixer" wording

§ Task 5. `CLAUDE.md` names it as a known comment-accuracy defect that already cost a real bug. It
needs a row about mixer detach semantics, not an opportunistic reword inside a logging fix.

### 6.4 Making `TTSFactory`'s `HttpClient` injectable

It would let `T2` cover the whole factory instead of one line, and let `…ThisSeamWrites` be widened
for real (§4.6). It is also a DI change to a live shared service with four `new HttpClient()` sites
(`:306`, `:394`, `:525`, `:655`), and it is not a logging fix. **Filed as the thing that would make
§4.3's named hole closable** — worth a row of its own, out of scope for this one.
