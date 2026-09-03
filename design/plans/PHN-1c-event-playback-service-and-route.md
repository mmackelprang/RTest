# PLAN — `PHN-1c` · ADR-029 PR 3: `EventPlaybackService` and the `/api/audio/events` route family

> **Status:** ready for Builder. Written 2026-09-02/03 against `d1226da`.
> **Punch list:** [`docs/HANDOFF-GA-PUNCH-LIST.md`](../../docs/HANDOFF-GA-PUNCH-LIST.md) §3.5 `PHN-1` (P0), §2 `O6`.
> **Decision of record:** [ADR-029](../decisions/2026-08-03-gv-audio-through-engine.md) — D1, D2, D3, and §14 Q10.
> **Sequencing:** [`design/plans/PHN-arc-pr-breakdown.md`](PHN-arc-pr-breakdown.md) — **this plan is PR 3 of 7.** The
> order is unchanged; nothing here re-sequences the arc.
> **Depends on:** `PHN-1a` ✅ ([#528](https://github.com/mmackelprang/RTest/pull/528)) and
> `PHN-1b` ✅ ([#534](https://github.com/mmackelprang/RTest/pull/534)), both merged.
> **Predecessor plans:** [`PHN-1a`](PHN-1a-event-playback-seam-contracts.md) §0.4 (C-1…C-11) and §5, and
> [`PHN-1b`](PHN-1b-gvmedia-client-cache-and-auth.md) §0.3 (⓵…⓸), §0.4 (C-12…C-20) and §5.
> **Those lists are authority wherever they disagree with the ADR.** Thirteen further contradictions —
> **C-21…C-33** — are resolved in §0.4 below, and **three of them change what PR 3 builds**.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

PR 1 gave the arc a type surface and PR 2 gave it the only thing that talks to another machine. **PR 3 is the
first PR a user can reach.** It implements `IEventPlaybackService` — one in-flight attended playback, a
server-minted `playbackId`, a lifecycle that ends exactly once — and exposes ADR §3.3's six routes under
`/api/audio/events`. Both arms land here: `RemoteMedia` fetches through `GvMediaClient` into the bounded cache
and plays the resulting local file as an ordinary file event; `Speech` synthesises through `ITTSFactory` with
the engine resolved explicitly. Ducking is wired the way `AnnouncementService` wires it, which is the only
in-tree event path that is not defective. **Nothing in `Radio.Web` changes** — `VoicemailPlayer.razor` still
holds its `<audio>` element until PR 6, and no broadcast goes on `/hubs/audio` until PR 5.

### 0.2 The shape is 202 + background acquisition, and almost everything else follows from that

This is the single structural fact Builder needs before reading a task, because at least four decisions below
are consequences of it rather than independent choices.

Two shipped statements fix the shape, and they agree:

- `IEventPlaybackService.StartAsync`'s own XML doc (shipped in PR 1):
  *"Returns as soon as the request is accepted — the returned snapshot is normally `Preparing`, because both
  arms have an acquisition phase (an HTTP fetch, or a TTS synthesis) before any audio exists."*
- ADR §3.3: `POST /api/audio/events` → **`202`**.

So `StartAsync` accepts, mints an id, publishes a `Preparing` snapshot and **returns**; acquisition and
playback happen on a task that outlives the HTTP response. Four consequences, each of which is a task below:

1. **The acquisition token cannot be `HttpContext.RequestAborted`** (C-21). It is a service-owned
   `CancellationTokenSource`, cancelled by `StopAsync`, by a replacing `StartAsync`, and by disposal.
2. **A fetch failure is not an HTTP status code** (C-23). It is `EventPlaybackState.Failed` plus a named
   `FailureReason` on the snapshot. The one failure that *is* knowable before accepting — `GvMedia:Enabled`
   being false — is checked synchronously and answered `409`.
3. **`Preparing` must be bounded**, or a hung synthesis parks the seam forever with no route to clear it
   (C-24). PR 3 bounds its own acquisition phase.
4. **The background task must never let an exception escape.** An unobserved faulted task on this box is a
   process-level hazard; every path ends in a terminal snapshot.

### 0.3 The four items PR 1 and PR 2 carried into PR 3, now settled

**⓵ `MaxSpeechChars` → rejection here, `GvSpeechText.ForMessage` in `Radio.Web`, and neither is PR 3's to
compose.**

`PHN-1b` §0.3 ⓵ settled that the seam **rejects** (`EventPlaybackRejection.TextTooLong` → `400`) and that
composition — including truncation with a spoken tail — belongs to `Radio.Web`. PR 3's obligations are
exactly two, and the second is the one that could still go wrong:

- **PR 3 calls `Validate(options.MaxSpeechChars)` and maps `TextTooLong` to a `400` with that reason.** It
  does **not** truncate, does not clamp, and the word "truncat" appears nowhere in the code this PR adds.
- **`GvSpeechText.ForMessage` is NOT created by PR 3, and PR 3 must not create a server-side twin of it.**
  It lands in `PHN-3` (the SMS speak button), lives in `src/Radio.Web/`, is a pure static over
  `SmsMessageDto`, and is called by the Blazor component *before* it posts — which is the only place a spoken
  tail can be composed in the same voice as the rest of the utterance, and the only place the user can see
  what will be said. **Verified against the tree at `d1226da`: no `GvSpeechText` exists in `src/` today**, so
  there is nothing for PR 3 to call and nothing for it to move.

  The reason this needs saying rather than assuming: PR 3 is the first PR that holds a `Text` string on the
  server, and "normalise it before speaking" is a natural-looking thing to do there. It is the wrong place.
  ADR §4.2's governing sentence is *"Utterance composition belongs to `Radio.Web`"*, and a server that
  normalises is a server that changes what was said without the caller seeing it — the same untruth as
  truncating and returning 200.

**⓶ `IEventAudioSource.SeekAsync` stays `Task`. Not reopened.**

`PHN-1b` §0.3 ⓶ closed this as **no**, and PR 2 corrected `IEventPlaybackService.SeekAsync`'s remark in the
same PR so `main` stopped carrying a statement it knew to be false. **PR 3 does not reopen it**, and this
plan is not making a case to widen it. Re-read against PR 3's actual needs, the closure is *more* right than
it was when it was made, for a reason PR 2 could not have checked because it had no consumer:

`EventPlaybackService.SeekAsync(playbackId, position, ct)` must return a `bool`, and it can answer honestly
from information it already has — the playback exists, and `source.IsSeekable` is true. That is exactly what
the shipped remark says it means. A `Task<bool>` on `IEventAudioSource` would add the player's verdict, and
PR 3 would have nowhere to put it: the route returns a **snapshot**, not a boolean, and the snapshot's
`PositionAtBroadcast` already reads through to the player. So a refused seek shows up as an anchor that did
not move — in the same response, on the same round trip. The bool would be redundant with the field beside
it.

**One residual, stated rather than hidden:** `TTSEventSource.IsSeekable` is `false`, so
`EventPlaybackService.SeekAsync` on a speech playback returns `false` **without** calling
`IEventAudioSource.SeekAsync` — deliberately, because the base class throws `NotSupportedException` there and
a `501`-shaped exception is not the answer to "can I scrub this". The route answers `409` for a playback that
exists and cannot seek. See Task 8.

**⓷ `Label` gets `MaxLabelChars = 128`, in `Validate`, implemented here.**

`PHN-1b` §0.3 ⓷ decided the where and the why; PR 3 does the work. `Label` flows into
`EventPlaybackSnapshot`, which PR 5 broadcasts to every open client on a box where CPU churn is audible, so
an unbounded string on that wire is a real if small cost — and a log-line and layout hazard besides. It goes
in `Validate` beside `MaxMediaIdChars` and the priority range, not in the controller, because PR 1's own
sentence is *"the rejection enum exists so the controller does not re-derive the rules."*

⚠ **`EventPlaybackRejection.LabelTooLong` is appended at the END of the enum**, after
`MediaIdHasIllegalCharacter`, and so is every other member this PR adds. That is not style: the shipped
comment on `MediaIdHasIllegalCharacter` says it was placed there *"so that no member above it is
renumbered … a rejection reason is the kind of thing that ends up in a log line or on the wire, and inserting
into the middle of the list is how that quietly stops meaning what it used to."* PR 3 is the PR that puts
these names **on the wire** for the first time. Honour it.

**⓸ Masking — the rule is unchanged, and PR 3 adds one surface it does not cover.**

`PHN-1b` §0.3 ⓸'s rule stands verbatim: *a raw media id must never reach a log message, a log argument, or an
exception message*, masked as a hash prefix (`gvm:1a2b3c4d`). `EventPlaybackService` obeys it by never
holding the raw id in a loggable position — it computes `GvMediaCache.MaskFor(mediaId)` once and logs that.

**The surface the rule does not cover, and PR 3 must not widen it: `EventPlaybackRequest.Text`.** For the
`Speech` arm the payload is an SMS body — private content by exactly the standard the media-id rule exists to
protect. Two shipped lines log it in full, at **Information** and at Debug:

- `TTSFactory.CreateAsync` — `LogInformation("Creating TTS audio for text: '{Text}' …")`, first 50 chars.
- `TTSEventSource.InitializeAsync` — `LogInformation("TTS event source initialized: {Text}", _text)`, **the
  whole string**.

Both are **live announcement paths** and are not PR 3's to change — `IAnnouncementService` has flowed through
them since long before this arc. What PR 3 owns is its own logging, and the rule there is: **`EventPlaybackService`
and `EventPlaybackController` never log `request.Text`, not truncated and not hashed** — they log
`Text.Length` and the playbackId. Task 13 records the two live lines; they are not fixed here.

### 0.4 ⚠ Thirteen contradictions found while planning, and how each resolves

Read this before Task 1. **C-21, C-22 and C-26 change what PR 3 builds or ships.**

---

**C-21 — ⚠ THE FIRST ONE THAT CHANGES THE WORK. `GvMediaCache.WriteAsync`'s remark says PR 3 will pass
`HttpContext.RequestAborted`. PR 3 must not, and the remark becomes false the day this lands.**

The shipped remark (`src/Radio.Infrastructure/External/GvMediaCache.cs`, on `WriteAsync`) reads, in the
middle of an otherwise-correct explanation of the staging fix:

> *"It is self-preserving, and PR 3 passes `HttpContext.RequestAborted`, so a kiosk reload is enough to
> create one."*

**The staging fix is right and this plan does not touch it.** What is wrong is the sentence's claim about
PR 3. §0.2 establishes that acquisition is background work that outlives the HTTP response; `RequestAborted`
is scoped to the request, and the `HttpContext` behind it is pooled and reset once the response completes.
Capturing it for work that outlives the response is a documented hazard whose precise symptom depends on the
server — at best the token is cancelled the instant the `202` is written, cancelling **every** fetch; at
worst it is read off a recycled context. Either way it is wrong, and the "at best" case is worse than it
sounds: it would make the `RemoteMedia` arm fail 100% of the time in a way that looks like a network problem.

**Resolution.** Three parts, and the third is the one that keeps this from recurring:

1. `EventPlaybackService` owns a `CancellationTokenSource` per playback (Task 4). It is cancelled by
   `StopAsync`, by a replacing `StartAsync`, and by `Dispose`. The controller's `RequestAborted` is used for
   the **synchronous** part of `StartAsync` only — validation and the enabled check — and is not linked into
   the acquisition token.
2. Task 6 pins it with a test that cancels the token passed to `StartAsync` immediately after it returns and
   asserts the playback still reaches `Playing`. That test fails if anyone links the two.
3. **Task 12 corrects the remark**, in the same PR, following exactly the precedent `PHN-1b` Task 12 set when
   it corrected the `SeekAsync` remark: a comment that asserts a fact about a PR that has landed and does not
   do that thing is the failure class `CLAUDE.md` § Pre-Merge Review exists for, and this repo has now shipped
   six of them.

**What the staging fix is still protecting, so nobody concludes it was unnecessary:** the cancellation PR 3
*actually* introduces. `StopAsync` during `Preparing` cancels mid-`WriteAsync`; so does a second
`StartAsync`; so does container disposal at shutdown. All three land on
`File.WriteAllBytesAsync(staging, …)` and are caught, deleted and rethrown. The fix is load-bearing for a
smaller and more ordinary set of triggers than the remark claims — which is the correction, not a downgrade.

---

**C-22 — ⚠ THE SECOND ONE THAT CHANGES THE WORK, AND THE MOST IMPORTANT FINDING IN THIS PASS. The gvbridge
audio route returns `404` during a Google Voice auth blackout, so `GvMediaFailure.NotFound` is not permanent —
and `GvMediaUnavailableException.IsPermanent` says it is.**

`PHN-1b` §2.2 item 3 carried *"that the gvbridge voicemail route returns what this client expects — status
codes, content type, and whether `Content-Length` is present"* to PR 3 as unverified, needing the live
service. **It does not need the live service. The route's source is on this machine**, at
`D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Api\GvVoicemailController.cs`, and reading it settles
all three questions and turns up a fourth nobody asked.

Verified against that file:

| Question | Answer | Evidence |
|---|---|---|
| Status codes | `200`, `404`, `502` — and `401` once the gate is on | `GetAudio` returns `NotFound` / `StatusCode(502, …)` / a file result; `GvBridgeAuthMiddleware` returns `401` (not `403`) |
| Content type | `audio/mpeg` | `new PhysicalFileResult(path, "audio/mpeg")` |
| `Content-Length` | **Yes**, on the whole-body `200` this client issues | `PhysicalFileResult` with `EnableRangeProcessing = true`; `GvMediaClient` sends no `Range` header, so the response is a plain `200` and ASP.NET sets `Content-Length` from the file |
| Auth | Gate ships **default-off** (`InterServiceAuthKey = ""`), `401` when configured | `GvBridgeAuthMiddleware.InvokeAsync` — pass-through when `!_validator.IsEnabled` |

So PR 2's declared size bound behaves as designed, and `GvMediaFailure.Unauthorized` is a real reachable
state rather than a hypothetical.

**The fourth thing.** `GetAudio` resolves the recording like this:

```csharp
var node = await FindNodeAsync(id, ct);
if (node?.MediaId is null)
    return NotFound(new { error = $"Voicemail {id} has no recording" });
```

and `FindNodeAsync` is:

```csharp
var result = await _voicemailClient.ListVoicemailsAsync(count: 100, pageToken: null, ct);
return result.Items.FirstOrDefault(v => v.MessageId == id);
```

**It never checks `result.Succeeded`.** `GvVoicemailClient.ListVoicemailsAsync` returns
`GvVoicemailListResult.Empty(succeeded: false)` — an **empty item list** — whenever the authenticated list
call fails, which is precisely what happens during the GV auth blackout. `GetList` guards this
(`if (!result.Succeeded) return StatusCode(502, …)`, with a comment saying exactly why); **`GetAudio` does
not.** An empty list means `FirstOrDefault` returns null, which means:

> **During a GV auth blackout — roughly 9 minutes in every 20 — `GET /api/gvbridge/voicemail/{id}/audio`
> returns `404 "Voicemail {id} has no recording"` for a recording that exists and will play fine in a few
> minutes.**

Now put that against what both handoffs tell PR 3 to do. `PHN-1b` §5: *"`NotFound` → 404"*. And
`GvMediaUnavailableException.IsPermanent`, shipped:

```csharp
public bool IsPermanent => Reason is GvMediaFailure.NotFound or GvMediaFailure.Disabled;
```

with the doc *"True when retrying the same request cannot succeed."* Against this upstream that is **false
roughly 45% of the time**, and following the handoff literally would tell the user a voicemail is permanently
gone when the correct answer is "try again in a minute." That is the `GV-6`/`GV-8` failure class — *"maps
every non-2xx to X, destroying the distinction the caller needs"* — arriving through a door nobody was
watching, and it is exactly the class PR 2's taxonomy was built to prevent.

**Resolution — three parts, Task 10:**

1. **`IsPermanent` becomes `Reason is GvMediaFailure.Disabled`**, and its doc says why `NotFound` was removed,
   citing the gvbridge code path by file and method rather than by line (C-19: never trust a line number).
   `Disabled` is the only reason that is permanent *by construction on our side* — retrying with the feature
   off cannot succeed, and no clock changes that.
2. **`NotFound` presents as retryable**, alongside `Upstream`/`Timeout`/`Transport`. Its distinct
   `FailureReason` name survives — the distinction is preserved, it just no longer carries a false claim
   about retrying.
3. **A note in `GvMediaFailure.NotFound`'s own XML doc**, because the enum member currently says
   *"the recording does not exist. Retrying will not help."* — the same false claim in a second place.

⚠ **This is a defect in RotaryPhone's code, and the right long-term fix is theirs**: `FindNodeAsync` should
propagate `Succeeded` so `GetAudio` can answer `502` during a blackout the way `GetList` already does. That
is a **cross-repo ask**, and PR 3 does not make it — see §4 item 6 for why it is filed for the owner rather
than smuggled into a planning PR, and §5 for the exact text to send.

---

**C-23 — the failure taxonomy PR 2 built has no status codes to map onto, because the route returns `202`.**

`PHN-1b` §5 hands PR 3 a table: *"Map `GvMediaFailure` to status codes — `NotFound` → 404, `Unauthorized` →
502 with a distinct reason, `Timeout`/`Upstream`/`Transport` → 503, `Disabled` → 409, `TooLarge` → 502. **Do
not collapse them**."* That table assumes the fetch happens inside the request. Under §0.2's shape it does
not: `StartAsync` has already returned `202` before `GvMediaClient` is called, so there is no response left
to carry a `503`.

**Resolution — the distinction is preserved by NAME, on the snapshot, which is where PR 6 actually needs it.**

| `GvMediaFailure` | Where it surfaces | Retryable |
|---|---|---|
| `Disabled` | **synchronously — `409` from `POST /api/audio/events`**, before anything is accepted | no |
| `NotFound` | `Failed` snapshot, `FailureReason = "MediaNotFound"` | **yes** (C-22) |
| `Unauthorized` | `Failed` snapshot, `FailureReason = "MediaUnauthorized"` | no |
| `Upstream` | `Failed` snapshot, `FailureReason = "MediaUpstream"` | yes |
| `Timeout` | `Failed` snapshot, `FailureReason = "MediaTimeout"` | yes |
| `Transport` | `Failed` snapshot, `FailureReason = "MediaTransport"` | yes |
| `TooLarge` | `Failed` snapshot, `FailureReason = "MediaTooLarge"` | no |

Seven reasons, seven distinct names, nothing collapsed. `Disabled` is checked synchronously **because it is
the one failure knowable without touching the network** — accepting a request we already know will fail, only
to fail it a millisecond later on a channel the caller may not be watching, is worse than refusing it.

This is a better outcome than the handoff's table rather than a lesser one, and the reason is PR 6: a chip
that must render *"couldn't play that — try again"* needs the reason **on the state it is already rendering**,
not in the status line of a `POST` it fired and forgot. `FailureReason` is a `string?` on
`EventPlaybackSnapshot` precisely so it can carry this.

---

**C-24 — `TTSOptions.GenerationTimeoutSeconds` has never had a reader, so TTS synthesis is unbounded — and
PR 3 is what makes an unbounded `Preparing` user-visible.**

Verified repo-wide at `d1226da`. `GenerationTimeoutSeconds` appears in exactly three places:

- `src/Radio.Core/Configuration/TTSOptions.cs:42` — its declaration, default `30`.
- `src/Radio.Web/Models/ApiModels.cs:803` — the Web-side DTO twin.
- `src/Radio.Web/Components/Pages/SystemConfigPage.razor:699` — a `RadzenNumeric` the owner can set,
  `Min="5" Max="120"`.

**Nothing in `src/Radio.Infrastructure` reads it.** `TTSFactory.GenerateESpeakAsync` awaits
`CopyToAsync` and `WaitForExitAsync` on the caller's token and nothing else; the Google and Azure paths use
an `HttpClient` with no timeout configured against that option. So the owner has a knob in the UI that has
never done anything, and a synthesis that hangs hangs forever.

Today that is invisible: `AnnouncementService.AnnounceAsync` is fire-and-forget and nobody watches it. **PR 3
makes it visible and stateful** — a hung synthesis leaves `Current` pinned in `Preparing`, and since PR 3's
own single-slot rule says a new `StartAsync` replaces the old one, the seam is at least self-clearing, but a
`GET /api/audio/events/current` in between reports a playback that will never start.

**Resolution:** PR 3 bounds **its own** acquisition phase rather than fixing `TTSFactory` (a live shared path
with two other callers). `EventPlaybackService` reads `IOptionsMonitor<TTSOptions>.CurrentValue.GenerationTimeoutSeconds`
and applies it as a linked `CancelAfter` around `ITTSFactory.CreateAsync`. That **gives the dead knob its
first real reader** without touching the factory, and it works because the espeak path's two long awaits both
honour the token.

⚠ **Two residuals, stated because a reviewer will find them:** `process.StandardInput.WriteAsync(text)` takes
no token, so a stall writing to espeak's stdin is not interruptible — it is not a realistic hang for a
≤1 000-character string into a pipe, but the bound is best-effort at that one point. And cancelling
`CreateAsync` mid-synthesis does not kill the `espeak-ng` process; `Process.Dispose` does not. The orphan
exits when its stdout pipe breaks. Neither is worth fixing here; both are worth knowing.

The equivalent bound on the `RemoteMedia` arm already exists and needs nothing: `GvMediaServiceExtensions`
sets `HttpClient.Timeout` from `GvMedia:FetchTimeoutSeconds`, and `GvMediaClient` maps the resulting
cancellation to `GvMediaFailure.Timeout`.

---

**C-25 — the `TTSParameters` trap is wider than ADR §9.3 describes, and both handoffs inherited the narrow
version. It pins `Voice`, `Speed` and `Pitch` too, not just `Engine`.**

ADR §9.3 is right about `Engine` and stops there: *"`TTSParameters.Engine` is a non-nullable `TTSEngine` with
an initializer of `TTSEngine.ESpeak` … Passing any non-null `TTSParameters` bypasses the config default
entirely."* Both `PHN-1a` §5 and `PHN-1b` §5 repeat only the `Engine` half.

Read `TTSFactory.CreateAsync` and the type together:

```csharp
// TTSFactory.CreateAsync
var engine = parameters?.Engine ?? ParseEngine(opts.DefaultEngine);
var voice  = parameters?.Voice  ?? opts.DefaultVoice;
var speed  = parameters?.Speed  ?? opts.DefaultSpeed;
var pitch  = parameters?.Pitch  ?? opts.DefaultPitch;
```

```csharp
// ITTSFactory.cs — every one of these is NON-nullable with an initializer
public record TTSParameters
{
  public TTSEngine Engine { get; init; } = TTSEngine.ESpeak;
  public string    Voice  { get; init; } = "en";
  public float     Speed  { get; init; } = 1.0f;
  public float     Pitch  { get; init; } = 1.0f;
}
```

Each `??` is lifted by the **null-conditional on the object**, so all four fire only when `parameters` itself
is null. Supply a `TTSParameters` to set one field and you have silently pinned the other three to the type's
initializers — not to configuration. On this box that is not academic: the deployed block is

```json
"TTS": { "DefaultEngine": "Google", "DefaultVoice": "en-US-Standard-A", "DefaultSpeed": 1.0, "DefaultPitch": 1.0 }
```

so a partially-filled `TTSParameters` would swap a **Google `en-US-Standard-A` voice for espeak's `en`** —
two changes, one of which (`Engine`) the ADR names and one of which (`Voice`) it does not, and which would
independently break Google synthesis because `en` is not a valid Google voice name.

**Resolution:** `EventPlaybackService` fills **all four** fields explicitly from
`IOptionsMonitor<TTSOptions>`, overriding only what the request actually carried (Task 4). It never passes a
partially-filled `TTSParameters`, and it never passes `null` either — `null` would be correct today but stops
being correct the moment `VoiceId` is set, which is the trap re-armed.

Recorded as its own contradiction rather than folded into ⓵ because the narrow version has now been copied
forward through an ADR amendment and two plans, and the next reader would copy it again.

---

**C-26 — ⚠ SECURITY. `TTSFactory.GenerateESpeakAsync` interpolates a caller-supplied voice string into a
process argument line. It is reachable unauthenticated today, and `EventPlaybackRequest.VoiceId` would be a
second door.**

```csharp
// TTSFactory.GenerateESpeakAsync
var arguments = $"-v {voice} -s {espeakSpeed} -p {espeakPitch} --stdout";
```

`voice` reaches this from `TTSParameters.Voice`, and `SourcesController.PlayTTSEvent` sets it from
`request.Voice ?? "en"` — an unvalidated string from an unauthenticated JSON body on port 5000, with
`Engine = engine ?? TTSEngine.ESpeak` when the request names no engine, so the espeak branch is the
**default** one.

**Be precise about the class.** `UseShellExecute = false`, so there is no shell and this is **not** command
injection. It is **argument injection**: `ProcessStartInfo.Arguments` is a single string that is split on
whitespace into `argv` (by `CreateProcess`/MSVCRT rules on Windows, by .NET's own MSVC-style parser on Unix),
so a space in `voice` introduces new flags. `espeak-ng` accepts `-w <file>` to write its WAV output to a
path, which makes `"en -w /opt/radio-console/api/appsettings.Production.json"` an arbitrary file overwrite as
`mmack` — the account that owns `/opt/radio-console`. No credential is disclosed and no shell is reached, but
integrity is.

**This is live today and PR 3 did not introduce it.** It is not this arc's to fix: `SourcesController` is one
of the two ad-hoc event paths every document in this arc says not to touch, and fixing it properly means
either validating in `TTSFactory` (a shared path) or retiring the endpoint (ADR §14 Q6 — *explicitly not in
this arc*).

**What PR 3 owes is that it does not open the second door**, and the fix is the same shape as
`ValidateMediaId`:

- `EventPlaybackRequest.Validate` allow-lists `VoiceId` to `[A-Za-z0-9._~+-]` with `MaxVoiceIdChars = 64`,
  yielding `VoiceIdTooLong` / `VoiceIdHasIllegalCharacter` (Task 1). Whitespace is refused, which is the
  whole injection class rather than the examples — the same argument the shipped `ValidateMediaId` remark
  makes about `':'`.
- ⚠ **Declared assumption, in the shipped file's own style:** every voice id this system uses is inside that
  set — espeak (`en`, `en-us`, `en+f3`), Google (`en-US-Standard-A`, `en-US-Neural2-A`) and Azure
  (`en-US-JennyNeural`) all are. The one known exception is an **mbrola** voice such as `mb/mb-en1`, whose
  `/` this rejects. If someone ever wants one, this refuses it as a loud, named `400` rather than
  misbehaving quietly, and the fix is one line here.

Task 13 files the live defect in `design/FUTURE-WORK.md` with the reproduction and the fix. **It is not
fixed here and it is not added to `docs/BUILDER_QUEUE.md` by this PR** — see §4 item 5.

---

**C-27 — `TTSEventSource.Position` is never overridden, so every `Speech` snapshot reports position zero, for
the whole playback.**

`EventAudioSourceBase.Position` is `public virtual TimeSpan Position => TimeSpan.Zero;` with the remark *"An
implementer that can report a real position overrides this; `TTSEventSource` deliberately does not."*
`AudioFileEventSource` overrides it and reads through to the player; `TTSEventSource` does not.

PR 1 made that choice about **seek**, where it is clearly right (§8.3 — seeking inside a spoken message has
no user value). PR 3 inherits it for **position**, where the reasoning does not transfer: a progress bar over
a 20-second spoken message is ordinary and useful, and `TTSEventSource` uses `Id` as its
`SoundFlowPlaybackService` key, so the override would be the same three lines `AudioFileEventSource` already
has.

**Resolution: PR 3 does NOT change it**, for two reasons — it contradicts a deliberate, documented decision
in a file PR 3 has no other reason to open, and the consequence is invisible until PR 5 puts snapshots on the
wire and PR 6 renders them.

**What PR 3 owes instead is that it does not lie about it.** Specifically: no comment, doc or test in this PR
may state or imply that `PositionAtBroadcast` advances for a `Speech` playback. Task 6 asserts the honest
version — that a speech snapshot's position is zero — so the behaviour is pinned rather than assumed, and §5
hands the decision to PR 5 with the exact override.

---

**C-28 — a stop after natural completion raises `PlaybackCompleted` twice, and PR 3 is the first code that
subscribes to it and holds state.**

Both event sources raise completion from two independent places:

- `AudioFileEventSource.PlayWithSoundFlowAsync` raises `EndOfContent` when `AwaitCompletionAsync` returns;
  `AudioFileEventSource.StopCoreAsync` raises `UserStopped` unconditionally at its end.
- `TTSEventSource.StartPlaybackWithMonitoringAsync` raises `EndOfContent`; `TTSEventSource.StopCoreAsync`
  raises `UserStopped` unconditionally at its end.

And `AudioSourceBase.StopAsync` only short-circuits on `Created` or `Disposed` — **not** on `Stopped`. So
PR 3's own cleanup, which calls `source.StopAsync()` after a natural end exactly as `AnnouncementService`
does, raises a second `PlaybackCompleted`. `AnnouncementService` is immune by accident: it uses a
`TaskCompletionSource` and `TrySetResult`, so the second event is discarded.

PR 3 is not immune by accident, because it holds a state machine. Without a guard the sequence
`EndOfContent → Completed` then `UserStopped → Stopped` would overwrite a correct terminal state with a wrong
one, raise `PlaybackChanged` twice, and — from PR 5 — broadcast a spurious transition to every client.

**Resolution:** terminal transition is **once-only per playbackId**, enforced by an
`Interlocked.CompareExchange` on the playback's completion flag inside `EventPlaybackService` (Task 4), and
pinned by a test that raises both events and asserts one transition (Task 6). Not by "unsubscribe first" —
unsubscribing races the monitor task, and the flag is correct regardless of ordering.

---

**C-29 — `AudioFileEventSourceFactory.CreateFromFileAsync` cannot be used for a cached recording, for two
independent reasons, and the ADR's "play it as an ordinary file event" reads like it can.**

ADR §5.2 is right that a materialised recording is *just another file event*. It is wrong that the existing
factory entry point can create one:

1. **It re-roots relative paths.** `ResolveFilePath` does `Path.IsPathRooted(filePath) ? filePath :
   Path.Combine(_options.CurrentValue.RootDirectory, filePath)`. `GvMedia:CacheDirectory` ships as the
   **relative** `"./data/gvmedia"`, so `GvMediaClient` returns a relative path, and the factory would look for
   the recording under `FilePlayer:RootDirectory` — `media/audio` in the repo, and **`/mnt/nas/music` on the
   appliance**, which is the live per-machine override `PHN-1b` C-14 found in
   `/opt/radio-console/api/appsettings.Production.json`. The recording is not there; the fetch would succeed
   and the play would `FileNotFoundException`.
2. **It estimates the duration it is handed.** `GetAudioDurationAsync` → `EstimateMp3Duration(bytes)` at a
   flat `16000 B/s`, never decoding — which is the exact thing ADR §4.1 calls *"a correctness fix, not
   decoration"* and tells PR 3 to replace with `VoicemailItemDto.DurationSeconds`. There is no overload that
   accepts a duration.

**Resolution (Task 3):** add one method to the factory —
`CreateFromAbsolutePath(string absolutePath, TimeSpan? duration)` — which does not re-root, takes the
authoritative duration, and falls back to the factory's own estimator when the caller passes `null` (the
`DurationSeconds == 0` "unknown" case). Putting it in the factory rather than constructing
`AudioFileEventSource` inline is not tidiness: it keeps the MP3-bytes-per-second constant in exactly one
place, and — see §0.6 — it is what keeps `SoundFlowPlaybackService` out of `EventPlaybackService`'s
constructor, which is what makes PR 3's DI guard buildable at all.

---

**C-30 — the breakdown's PR 3 line names one route; `PHN-1a`'s own test plan already assumes more. PR 3 ships
all six, and that is a scope clarification rather than a re-sequencing.**

The breakdown says *"`EventPlaybackService` + `POST /api/audio/events`. Handle lifecycle and the `playbackId`
mapping."* PR 5 is *"Server-owned playback state + the three stop conditions. Broadcast over the existing
`/hubs/audio`."*

`PHN-1a` §2.2 already wrote the verification recipe for its three carried device checks as:

```bash
curl -s -X POST http://radio:5000/api/audio/events -d '{...}'
curl -s http://radio:5000/api/audio/events/current      # position should advance between calls
```

— *"after PR 3"*, in a plan that shipped. So `GET /current` was already understood to be PR 3's.

**Resolution:** PR 3 ships ADR §3.3's complete route set. All six are thin wrappers over seam methods PR 1
already declared and PR 3 already implements; splitting them across two PRs would ship a seam whose methods
have no caller and defer the only cheap way to exercise it. **PR 5's scope is unchanged** and is not routes:
the `/hubs/audio` broadcast, the max-duration cap, and the last-circuit-closed `CircuitHandler`. Nothing is
reordered, added or removed from the seven-PR breakdown.

---

**C-31 — ADR §14 Q10 is PR 3's to answer. The speech pre-flight is DROPPED; synthesis is the only gate.**

§9.4 offers exactly two acceptable answers and forbids a third: either `TTSFactory` invalidates its cached
`AvailableEngines` on an `IOptionsMonitor<TTSSecrets>` change, or the pre-flight is dropped — *"a cached
pre-flight that blocks a now-working engine until restart is not"* acceptable.

**Dropped.** Three reasons, the third of which is the strongest and is not in the ADR:

1. Invalidating `_cachedEngines` means editing `TTSFactory`, a live shared path with two other callers, in a
   PR that already touches a Core contract, a factory, an exception predicate and a new controller.
2. §9.4 itself says *"The generate path is the authority; the pre-flight only improves the message."*
   `SpeechSynthesisFailed` carries the engine name, so the message stays precise.
3. **On the deployed configuration the pre-flight would not catch the actual common misconfiguration.**
   §9.4 defect (a): `appsettings.json` ships `"GoogleAPIKey": "${secret:tts_google_api_key}"`, and
   `DetectAvailableEngines` tests only `!IsNullOrEmpty(GoogleAPIKey)` — an unsubstituted tag is not empty, so
   **Google advertises as available on a box where the secret was never set**, and then throws at synthesis.
   With `TTS:DefaultEngine = "Google"` as deployed, that is *the* misconfiguration, and the pre-flight passes
   it. A gate that is wrong in the blocking direction for fixed engines and silent for broken ones is worse
   than no gate.

So `EventPlaybackRejection` gains no `SpeechEngineUnavailable`, the snapshot's `FailureReason` for a failed
synthesis is `"SpeechSynthesisFailed"`, and `ITTSFactory.AvailableEngines` is not read by this PR.
`design/FUTURE-WORK.md` records defect (a) and the caching defect (b) together (Task 13), since they are one
row and neither is this arc's.

---

**C-32 — PR 3 adds no configuration key, and that is deliberate given `PHN-1b` C-14's two-files trap.**

Every value PR 3 reads already ships: `GvMedia:Enabled`, `MaxSpeechChars`, `MaxPlaybackSeconds`,
`FetchTimeoutSeconds`, `CacheDirectory` (all added by PR 2 to `src/Radio.API/appsettings.json`, which the
deploy **does** overwrite) and `TTS:DefaultEngine` / `DefaultVoice` / `DefaultSpeed` / `DefaultPitch` /
`GenerationTimeoutSeconds` (all pre-existing; the last is absent from the JSON and takes its `30` from the
class, which is correct and needs no edit).

That matters because of the trap `PHN-1b` C-14 found and the queue then corrected on the box: the per-machine
overlay is **two files** — `/opt/radio-console/api/appsettings.Production.json` (1057 B, mtime 2026-03-05)
and `/opt/radio-console/web/`'s (75 B, mtime 2026-07-31) — long diverged, excluded from `rsync`, and seeded
only when absent. **A config key added by a PR cannot be proved to reach the appliance**, only hand-edited
there. PR 3 sidesteps it by adding none. Task 14 asserts that as a gate: `git diff` must show no change to
any `appsettings*.json`.

---

**C-33 — `GvMediaStartupCheck` still cannot detect the divergence it was written for, and PR 3 leaves it
alone. Here is why that is the right call rather than an omission.**

The check warns only when `GvMedia:AuthKey` is **empty**. Two differing non-empty keys — the state that
actually produces the `401` — pass in silence, and its own remarks say so. Fixing it would require Radio.API
to read Radio.Web's per-machine overlay, which it structurally cannot: they are separate files in separate
directories loaded by separate processes, and nothing copies one into the other.

**Resolution:** leave it. The honest signal for a key mismatch is not available at boot and never will be —
it is available at **first fetch**, as a `401`, which PR 2 already maps to `GvMediaFailure.Unauthorized` and
PR 3 now surfaces as `FailureReason = "MediaUnauthorized"` on a `Failed` snapshot the UI renders. That is a
better diagnosis than a boot warning would have been: it fires on the box, in the moment, attached to the
thing that failed. Task 13 records the limitation in `design/INTEGRATIONS.md` next to the runbook line PR 2
wrote, so the operator who sees `MediaUnauthorized` knows it means *"the two keys differ"* and knows there
are two files to edit.

⚠ **What PR 3 must NOT do**, because it is the obvious-looking improvement: make the check compare
`GvMedia:AuthKey` against `RotaryPhone:Gv:AuthKey` and warn on inequality. On Radio.API those two keys are
read from the **same** configuration, so on the appliance the second is almost always absent and the
comparison would fire a false warning on every boot of a correctly-configured box.

### 0.5 What this row is NOT

1. ⛔ **No `Radio.Web` change of any kind.** No `EventPlaybackApiService`, no component, no `<audio>` removal,
   no `GvBridgeApiService.GetVoicemailAudioUrl` deletion. All **PR 6**. Removing the builder before its
   replacement is wired would break voicemail playback for three PRs.
2. ⛔ **No `/hubs/audio` broadcast, no `AudioStateHub` change, no `CircuitHandler`, no max-duration cap.**
   All **PR 5**. `EventPlaybackService` raises `PlaybackChanged` (PR 1's contract requires it) and nothing
   subscribes yet — that is PR 5's connection to make, and it is the only reason the event exists now.
3. ⛔ **No `DuckingService` change, and do not touch `DuckingServiceCharacterizationTests`.** PR 4's tripwire.
   PR 3 *consumes* ducking exactly as `AnnouncementService` does and changes none of its behaviour. All four
   characterization tests must still pass, untouched.
4. ⛔ **No preemption rule.** *"A source of priority ≥ 8 stops attended playback"* is **PR 4** (D5, and
   `GvMedia:PreemptAtPriority` exists but is not read here). PR 3 implements only the **single-slot** rule —
   one attended playback at a time, a new `StartAsync` replaces the old — which is D6/§8.1's consequence of
   there being one set of speakers, not D5's priority arbitration. §0.7 states the boundary.
5. ⛔ **Do NOT use `POST /api/sources/events/{tts,file}` as a template.** Re-verified at `d1226da`, all three
   defects are still live: `_duckingService` taken at `:44` and never read (those events do not duck);
   `mixer.AddSource(ttsSource)` at `:651` with no reachable `RemoveSource` or `Dispose`; `PlayFileEvent`
   calling `PlayFileAsync` at `:719` then `fileSource.PlayAsync` at `:732`, which re-enters `PlayFileAsync`
   under a different key. **`AnnouncementService` is the template** — §0.6.
6. ⛔ **No `mixer.AddSource`.** See §0.6; this is the single most copy-able mistake in the arc.
7. ⛔ **No fix to `SourcesController`'s eSpeak argument injection** (C-26), to `TTSFactory`'s engine cache
   (C-31), to `TTSEventSource.Position` (C-27), or to `PhoneContactLookupService` (`PHN-5`). All recorded,
   none touched.
8. ⛔ **No `appsettings*.json` edit** (C-32).
9. ⛔ **No `TTSParameters.Engine` nullability change.** ADR §9.3 flags it as *optional cleanup* and
   `PHN-1a` §4 item 2 routed it here — **declined**, with the reason: it changes a public record in
   `Radio.Core` consumed by `TTSFactory`, `SourcesController` and `AnnouncementService`, and C-25 shows the
   trap is four fields wide, so a nullable `Engine` alone would fix a quarter of it while looking like it
   fixed all of it. Filed in Task 13 as one row covering all four fields.

### 0.6 `AnnouncementService` is the template, and the one thing it does that PR 3 must not copy

Two event paths exist in this tree. Every document in this arc says which one is defective; none of them says
plainly what the **good** one does, and the difference is one line that decides whether PR 3 leaks.

`AnnouncementService.AnnounceAsync` is the working shape, and PR 3's playback loop is it with a state machine
bolted on:

```csharp
ttsSource = await _ttsFactory.CreateAsync(message, cancellationToken: cancellationToken);
_duckingService.SetPriority(ttsSource, priority);
await _duckingService.StartDuckingAsync(ttsSource, cancellationToken);
ttsSource.PlaybackCompleted += (_, _) => completionTcs.TrySetResult(true);
await ttsSource.PlayAsync(cancellationToken);
// … then, in finally: StopDuckingAsync, StopAsync, DisposeAsync
```

⚠ **It never calls `mixer.AddSource`, and that is correct.** Audio reaches the speakers because
`SoundFlowPlaybackService.PlayFileAsync` / `PlayStreamAsync` call
`playbackDevice.MasterMixer.AddComponent(soundPlayer)` themselves, and `StopAsync` calls the matching
`RemoveComponent`. `SoundFlowMasterMixer.AddSource` mutates a `List<IAudioSource>` for bookkeeping and does
**not** route audio — which is the mismatch `CLAUDE.md` § Pre-Merge Review's first worked example describes.
`SourcesController` calls it and never removes, which is its leak. **PR 3 calls neither**, and Task 14 greps
to prove it.

`IDuckingService` keys on `IAudioSource.Id` and is independent of the mixer, so ducking works without it.

**The one thing not to copy:** `AnnounceAsync` **awaits playback to completion** inside the call, which is
why `NotificationsController.Announce` blocks for the length of the announcement and why its "fire-and-forget"
comment is wrong about the HTTP layer. PR 3 returns at `Preparing` (§0.2) and drives the rest from the
`PlaybackCompleted` handler.

**And one thing to notice about how that endpoint's tests pass**, because it is this repo's own instance of
the hazard the brief names: `NotificationsControllerTests.Announce_WithValidMessage_ReturnsOk` asserts a
success status against a host where TTS cannot possibly work. It passes because `AnnounceAsync` catches every
exception internally and returns normally, so the endpoint answers `200` whether the announcement played or
failed at synthesis. **A green test there proves the route exists and nothing else.** PR 3's route tests are
built not to have that property — §2.1.

### 0.7 Three identities, one map — stated once so Tasks 4 and 8 stay short

The identity hazard ADR §3.3 names is real and PR 3 is where it would bite. There are **three** id spaces,
not two:

| Id | Minted by | Shape | Used as the key for |
|---|---|---|---|
| `IAudioSource.Id` | `AudioSourceBase.Id` — `$"{Type}-{Guid:N}"` | `AudioFileEvent-…` / `TTS-…` | `IDuckingService`'s `_activeEvents`, and — for `TTSEventSource` only — `SoundFlowPlaybackService` |
| `AudioFileEventSource._playbackId` | `InitializeAsync` | `audio-event-…` | `SoundFlowPlaybackService`, for the file arm only. **Private; PR 3 cannot see it and must not try.** |
| `EventPlaybackSnapshot.Id` | **`EventPlaybackService`, this PR** | `evp-…` | the HTTP routes, `Current`, `PlaybackChanged` |

**The rule: PR 3 owns `evp-…` and never derives it from, compares it to, or parses either of the others.** It
holds one record mapping `evp-…` → the `IEventAudioSource` instance, and every seam method resolves through
that map and then calls **interface methods on the instance**. Because it never addresses
`SoundFlowPlaybackService` directly, the `Id`/`_playbackId` divergence is invisible to it — which is the
point, and is why the prefix is deliberately a third one rather than a reuse: a log line saying `evp-…` can
only have come from this seam.

The single-slot rule means the map holds at most one entry, so it is a field under a lock rather than a
dictionary. Written as a dictionary it would invite a second concurrent playback that D6 §8.1 forbids.

---

## 1. Tasks

Thirteen tasks. Tasks 1-2 are Core-only. Task 3 is a one-method addition to a shared factory. Tasks 4-7 are
the service and its two guards. Tasks 8-9 are the route family and its end-to-end test. Tasks 10-11 correct
two statements that this PR makes false. Tasks 12-13 are docs and the gate.

⚠ **Every line number in this plan, in `PHN-1a`, in `PHN-1b` and in ADR-029 may have drifted (C-19).
Grep for the symbol, never `sed -n '<n>p'`.** Every code block below is literal and complete unless it says
otherwise.

---

### Task 1 — `Validate` learns three more bounds, and the seam gains its rejection exception

**File:** `src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs` (edit)

Three additions to `EventPlaybackRequest`, one to `EventPlaybackRejection`, and one new exception type.

**1a. Two new caps, beside `MaxMediaIdChars`.** Add after the existing `MaxMediaIdChars` declaration at the
end of the record:

```csharp
  /// <summary>
  /// Upper bound on <see cref="Label"/>. Generous — it holds "Voicemail from Jane Smith (555) 123-4567"
  /// four times over — and the point is that it is bounded. Label flows into
  /// <see cref="EventPlaybackSnapshot"/>, which is broadcast to every open client, so an unbounded
  /// string here is a real if small cost on the wire, a log-line hazard, and a layout hazard.
  /// </summary>
  public const int MaxLabelChars = 128;

  /// <summary>
  /// Upper bound on <see cref="VoiceId"/>. Short deliberately: every voice id this system uses is well
  /// under it — espeak's "en" / "en-us" / "en+f3", Google's "en-US-Neural2-A", Azure's "en-US-JennyNeural".
  /// </summary>
  public const int MaxVoiceIdChars = 64;
```

**1b. A `ValidateVoiceId` helper**, modelled on `ValidateMediaId` and carrying its own reason. Add it as a
private static beside `ValidateMediaId`:

```csharp
  /// <summary>
  /// Bounds and allow-lists a per-request voice override.
  ///
  /// ⚠ This is not cosmetic validation. <c>TTSFactory.GenerateESpeakAsync</c> builds its process
  /// argument line by string interpolation — <c>$"-v {voice} -s {speed} -p {pitch} --stdout"</c> — and
  /// <c>ProcessStartInfo.Arguments</c> is split on whitespace into argv. There is no shell, so this is
  /// argument injection rather than command injection, but a space in a voice id introduces new espeak
  /// flags, and espeak-ng's "-w &lt;file&gt;" writes its output to a path. The allow-list refuses
  /// whitespace outright, which is the whole class rather than the examples — the same argument
  /// <see cref="ValidateMediaId"/> makes about ':'.
  ///
  /// The rule is the RFC 3986 unreserved set plus '+', which espeak uses for variants ("en+f3").
  ///
  /// ⚠ Declared assumption: every voice id this system uses is inside that set. The one known exception
  /// is an mbrola voice such as "mb/mb-en1", whose '/' this refuses. If one is ever wanted, this rejects
  /// it as a loud, named 400 rather than misbehaving quietly, and the fix is one line here.
  ///
  /// ⚠ What this does NOT do: it does not fix the live injection surface. SourcesController's
  /// POST /api/sources/events/tts reaches the same interpolation with an unvalidated voice and no
  /// auth. That is a different, pre-existing path; see design/FUTURE-WORK.md.
  /// </summary>
  private static EventPlaybackRejection ValidateVoiceId(string? voiceId)
  {
    if (voiceId is null)
    {
      return EventPlaybackRejection.None;
    }
    if (voiceId.Length is 0 or > MaxVoiceIdChars)
    {
      return EventPlaybackRejection.VoiceIdTooLong;
    }
    foreach (var c in voiceId)
    {
      if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '~' or '+'))
      {
        return EventPlaybackRejection.VoiceIdHasIllegalCharacter;
      }
    }
    return EventPlaybackRejection.None;
  }
```

⚠ `voiceId.Length is 0` reports `VoiceIdTooLong` for an **empty** string, which reads oddly. It is
deliberate and the alternative is worse: a distinct `VoiceIdEmpty` member buys nothing, and letting `""`
through would pass an empty `-v` argument to espeak. Builder must not "improve" this into
`string.IsNullOrEmpty(voiceId) ? None : …` — that would silently accept `""`. The XML doc above says the
cap is a bound; this line's own comment in the file should say *"empty is refused here too; an empty -v is
not a voice."*

**1c. `Validate` gains three checks.** The `Label` and engine checks apply to **both** arms, so they go
before the `switch`, next to the priority check. The voice check is Speech-only and goes inside that arm.

Replace the opening of `Validate` — from `if (Priority is < 1 or > 10)` through the `switch (Kind)` line —
with:

```csharp
    if (Priority is < 1 or > 10)
    {
      return EventPlaybackRejection.PriorityOutOfRange;
    }

    // Both arms: Label is presentation-only but it reaches the snapshot, the wire and the logs.
    if (Label is not null && Label.Length > MaxLabelChars)
    {
      return EventPlaybackRejection.LabelTooLong;
    }

    switch (Kind)
```

and, inside `case EventPlaybackKind.Speech:`, replace the final `return Text.Length > maxSpeechChars …`
expression with:

```csharp
        if (Text.Length > maxSpeechChars)
        {
          return EventPlaybackRejection.TextTooLong;
        }
        // An unparseable engine is refused rather than silently resolved. TTSFactory.ParseEngine falls
        // back to ESpeak on garbage, so accepting it here would ignore an override the caller stated
        // explicitly — and would do it by selecting a DIFFERENT engine, which for a private message body
        // is the substitution ADR-029 §9.4 says must never happen silently.
        if (Engine is not null && !Enum.TryParse<TTSEngine>(Engine, ignoreCase: true, out _))
        {
          return EventPlaybackRejection.UnknownEngine;
        }
        return ValidateVoiceId(VoiceId);
```

⚠ `TTSEngine` lives in `Radio.Core.Interfaces.Audio` — the **same namespace and assembly** as this file
(`ITTSFactory.cs`). No new `using`, no new project reference, and the precedent for validating a closed set
here is `Enum.IsDefined(MediaKind.Value)` three lines below. If Builder finds it needs a `using`, something
has moved and the change should stop until that is understood.

⚠ **Do not also apply the engine check to the `RemoteMedia` arm.** That arm already returns `ArmMismatch`
when `Engine is not null`, so an engine check there would be unreachable — and unreachable validation is how
a later refactor gets a false sense of coverage.

**1d. Four new rejection members, appended at the END.** After `MediaIdHasIllegalCharacter`:

```csharp
  /// <summary>
  /// <see cref="EventPlaybackRequest.Label"/> exceeds
  /// <see cref="EventPlaybackRequest.MaxLabelChars"/>.
  /// </summary>
  LabelTooLong,

  /// <summary>
  /// <see cref="EventPlaybackRequest.VoiceId"/> is empty or exceeds
  /// <see cref="EventPlaybackRequest.MaxVoiceIdChars"/>.
  /// </summary>
  VoiceIdTooLong,

  /// <summary>
  /// <see cref="EventPlaybackRequest.VoiceId"/> carries a character outside the allow-list
  /// <c>[A-Za-z0-9._~+-]</c>. See the security note on the validator.
  /// </summary>
  VoiceIdHasIllegalCharacter,

  /// <summary>
  /// <see cref="EventPlaybackRequest.Engine"/> does not name a defined
  /// <see cref="TTSEngine"/> member.
  /// </summary>
  UnknownEngine
```

⚠ **Appended, never inserted.** The shipped comment on `MediaIdHasIllegalCharacter` explains why, and PR 3
is the PR that puts these names on the wire for the first time — so from this PR forward the numeric values
have a consumer's memory attached to them.

**1e. `EventPlaybackRejectedException`**, at the bottom of the same file, after `EventPlaybackSnapshot`:

```csharp
/// <summary>
/// Thrown by <see cref="IEventPlaybackService.StartAsync"/> when the request is not acceptable.
/// </summary>
/// <remarks>
/// The seam validates rather than trusting its caller, and the controller validates too — deliberately,
/// and it is not redundant. <see cref="EventPlaybackRequest.Validate"/> holds the rules; both are
/// callers of it, and neither re-derives anything. The controller's call produces a clean 400 without
/// an exception on a path a user can hit by typing; this one protects every non-HTTP caller the arc has
/// not written yet, on a type whose whole posture is defence in depth against a caller-chosen string.
/// </remarks>
public sealed class EventPlaybackRejectedException : Exception
{
  /// <summary>Creates the exception carrying the reason the request was refused.</summary>
  /// <param name="reason">Why the request was refused. Never <see cref="EventPlaybackRejection.None"/>.</param>
  public EventPlaybackRejectedException(EventPlaybackRejection reason)
    : base($"Event playback request refused: {reason}.")
  {
    Reason = reason;
  }

  /// <summary>Why the request was refused.</summary>
  public EventPlaybackRejection Reason { get; }
}
```

⚠ The message interpolates the **reason name**, never any field of the request. A rejection message that
echoed `MediaId` back would put a raw media id in an exception message, which is exactly what
`PHN-1b` §0.3 ⓸'s rule forbids — and it forbids it *because* every caller's catch block logs the exception.

**1f. Add the `<exception>` tag to `StartAsync`'s doc**, since PR 3 is the implementation that introduces it:

```csharp
  /// <exception cref="EventPlaybackRejectedException">
  /// The request did not pass <see cref="EventPlaybackRequest.Validate"/>.
  /// </exception>
```

---

### Task 2 — Core tests for the three new bounds

**File:** `tests/Radio.Core.Tests/EventPlaybackRequestTests.cs` (edit — append; change nothing existing)

⚠ **Do not modify any existing test in this file.** All 22 must still pass unchanged; `Validate`'s new
checks are additive and the existing fixtures (`Speech()`, `Voicemail()`) set neither `Label`, `VoiceId` nor
`Engine`, so none of them can be perturbed. Task 13's gate re-reads that.

```csharp
  // ── Label cap (PHN-1b §0.3 ⓷) ───────────────────────────────────────────

  [Fact]
  public void Validate_AcceptsALabelAtTheCap()
  {
    var request = Speech() with { Label = new string('a', EventPlaybackRequest.MaxLabelChars) };

    Assert.Equal(EventPlaybackRejection.None, request.Validate());
  }

  [Fact]
  public void Validate_RejectsALabelOverTheCap()
  {
    var request = Speech() with { Label = new string('a', EventPlaybackRequest.MaxLabelChars + 1) };

    Assert.Equal(EventPlaybackRejection.LabelTooLong, request.Validate());
  }

  [Fact]
  public void Validate_CapsTheLabelOnBothArms()
  {
    // The cap lives before the arm switch on purpose: a voicemail label reaches the same snapshot,
    // the same wire and the same log lines a speech label does.
    var request = Voicemail() with { Label = new string('a', EventPlaybackRequest.MaxLabelChars + 1) };

    Assert.Equal(EventPlaybackRejection.LabelTooLong, request.Validate());
  }

  // ── VoiceId allow-list — the eSpeak argument-injection pin (§0.4 C-26) ──
  //
  // TTSFactory.GenerateESpeakAsync interpolates the voice into a process argument line that is split
  // on whitespace into argv. A space introduces new espeak flags, and "-w <file>" writes to a path.
  // These cases are the injection shapes, not decorative bad input.

  [Theory]
  [InlineData("en -w /tmp/pwned.wav")]
  [InlineData("en --stdout -w /opt/radio-console/api/appsettings.Production.json")]
  [InlineData("en\t-w x")]
  [InlineData("en\nquiet")]
  public void Validate_RejectsAVoiceIdThatCouldInjectAnEspeakArgument(string voiceId)
  {
    var request = Speech() with { VoiceId = voiceId };

    Assert.Equal(EventPlaybackRejection.VoiceIdHasIllegalCharacter, request.Validate());
  }

  [Theory]
  [InlineData("en")]
  [InlineData("en-us")]
  [InlineData("en+f3")]
  [InlineData("en-US-Neural2-A")]
  [InlineData("en-US-JennyNeural")]
  [InlineData("en_US.utf~8")]
  public void Validate_AcceptsTheVoiceIdsThisSystemActuallyUses(string voiceId)
  {
    var request = Speech() with { VoiceId = voiceId };

    Assert.Equal(EventPlaybackRejection.None, request.Validate());
  }

  [Fact]
  public void Validate_RejectsAnMbrolaStyleVoiceId_WhichIsTheDeclaredAssumption()
  {
    // Pins the assumption rather than hiding it: a '/'-bearing mbrola voice is refused. If one is ever
    // wanted, THIS test is what fails and points at the one line to change.
    var request = Speech() with { VoiceId = "mb/mb-en1" };

    Assert.Equal(EventPlaybackRejection.VoiceIdHasIllegalCharacter, request.Validate());
  }

  [Theory]
  [InlineData("")]
  [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]  // 65
  public void Validate_RejectsAnEmptyOrOverlongVoiceId(string voiceId)
  {
    var request = Speech() with { VoiceId = voiceId };

    Assert.Equal(EventPlaybackRejection.VoiceIdTooLong, request.Validate());
  }

  [Fact]
  public void Validate_AcceptsANullVoiceId_MeaningTheConfiguredDefault()
  {
    Assert.Null(Speech().VoiceId);
    Assert.Equal(EventPlaybackRejection.None, Speech().Validate());
  }

  // ── Engine ──────────────────────────────────────────────────────────────

  [Theory]
  [InlineData("ESpeak")]
  [InlineData("espeak")]
  [InlineData("Google")]
  [InlineData("Azure")]
  public void Validate_AcceptsEveryDefinedEngineName_CaseInsensitively(string engine)
  {
    var request = Speech() with { Engine = engine };

    Assert.Equal(EventPlaybackRejection.None, request.Validate());
  }

  [Fact]
  public void Validate_RejectsAnUnknownEngineRatherThanFallingBackToESpeak()
  {
    // TTSFactory.ParseEngine falls back to ESpeak on garbage. Accepting an unparseable engine here
    // would honour the request by silently choosing a different engine — for a private message body,
    // exactly the substitution ADR-029 §9.4 says must never be silent.
    var request = Speech() with { Engine = "Whisper" };

    Assert.Equal(EventPlaybackRejection.UnknownEngine, request.Validate());
  }

  [Fact]
  public void Validate_ReportsArmMismatchRatherThanUnknownEngine_ForARemoteMediaRequest()
  {
    // Pins that the engine check did NOT get added to the RemoteMedia arm, where it would be
    // unreachable: ArmMismatch fires first and always.
    var request = Voicemail() with { Engine = "Whisper" };

    Assert.Equal(EventPlaybackRejection.ArmMismatch, request.Validate());
  }

  // ── Enum stability (the appended-at-the-end rule) ───────────────────────

  [Fact]
  public void EventPlaybackRejection_KeepsTheNumericValuesShippedBeforeThisPr()
  {
    // PR 3 is the first PR that puts these names on the wire, so from here the numbering has a
    // consumer's memory attached to it. New members go at the END; this fails if one is inserted.
    Assert.Equal(0, (int)EventPlaybackRejection.None);
    Assert.Equal(1, (int)EventPlaybackRejection.UnknownKind);
    Assert.Equal(2, (int)EventPlaybackRejection.ArmMismatch);
    Assert.Equal(14, (int)EventPlaybackRejection.MediaIdHasIllegalCharacter);
    Assert.Equal(15, (int)EventPlaybackRejection.LabelTooLong);
  }
```

⚠ **Builder must verify `14` before trusting it.** Count the members in the shipped enum
(`None`=0 … `MediaIdHasIllegalCharacter`) rather than taking this plan's word for it — this plan is exactly
the kind of document C-19 says not to trust on a number. If the count differs, fix the assertion, not the
enum.

---

### Task 3 — the factory learns to take an absolute path and an authoritative duration

**File:** `src/Radio.Infrastructure/Audio/Services/AudioFileEventSourceFactory.cs` (edit — one new method)

Add after `CreateFromFileAsync`:

```csharp
  /// <summary>
  /// Creates an event source over a file the caller has already located, with a duration the caller
  /// already knows.
  /// </summary>
  /// <remarks>
  /// ⚠ Deliberately NOT a variant of <see cref="CreateFromFileAsync"/>, and both differences are
  /// load-bearing for ADR-029's RemoteMedia arm.
  ///
  /// It does not re-root. <see cref="CreateFromFileAsync"/> sends a relative path through
  /// ResolveFilePath, which combines it with FilePlayer:RootDirectory — "media/audio" in the repo and
  /// "/mnt/nas/music" on the appliance. GvMedia:CacheDirectory ships as the RELATIVE "./data/gvmedia",
  /// so a fetched recording would be looked for under the music root and the play would fail with a
  /// FileNotFoundException after a successful fetch.
  ///
  /// And it accepts the duration rather than estimating it. ADR-029 §4.1 calls the passthrough of
  /// VoicemailItemDto.DurationSeconds "a correctness fix, not decoration": AudioFileEventSource detects
  /// completion from this value, so a wrong duration ends playback early or leaves it hanging. A null
  /// duration means the provider reported 0 — "unknown" per ADR-022 §4.2 — and only then does this fall
  /// back to the same size-based estimate CreateFromFileAsync uses.
  /// </remarks>
  /// <param name="absolutePath">A rooted path to an existing audio file. Not resolved against any root.</param>
  /// <param name="duration">The authoritative duration, or null to estimate it from the file size.</param>
  /// <returns>An audio file event source.</returns>
  /// <exception cref="ArgumentException">The path is not rooted.</exception>
  /// <exception cref="FileNotFoundException">The file does not exist.</exception>
  public async Task<IEventAudioSource> CreateFromAbsolutePathAsync(
    string absolutePath,
    TimeSpan? duration,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

    if (!Path.IsPathRooted(absolutePath))
    {
      throw new ArgumentException(
        "CreateFromAbsolutePathAsync requires a rooted path; it deliberately does not resolve against "
        + "FilePlayer:RootDirectory.", nameof(absolutePath));
    }

    if (!File.Exists(absolutePath))
    {
      throw new FileNotFoundException($"Audio file not found: {absolutePath}");
    }

    var effective = duration ?? await GetAudioDurationAsync(absolutePath, cancellationToken);

    _logger.LogDebug(
      "Creating audio file event source from an absolute path, duration {Duration} ({Source})",
      effective, duration is null ? "estimated" : "authoritative");

    return new AudioFileEventSource(absolutePath, effective, _sourceLogger, _playbackService);
  }
```

⚠ **The log line does not carry the path**, unlike `CreateFromFileAsync`'s
`LogInformation("Creating audio file event source: {FilePath}", fullPath)`. The path here is a cache
filename derived from a voicemail id; logging it would put the hash on a line at Debug alongside — which is
harmless in itself, but the rule in `PHN-1b` §0.3 ⓸ is that the id's derived forms appear only as
`GvMediaCache.MaskFor` produces them, and `EventPlaybackService` already logs that mask. One correlating
token, not two.

⚠ `_playbackService` is the factory's own **optional** `SoundFlowPlaybackService?`. If it is null the
resulting source falls back to `PlaybackLoopAsync` — a silent `Task.Delay` that produces **no audio at all**
while reporting a clean completion. That is the arc's worst failure shape, and it is why Task 9's route test
does not assert on `Completed`. In production it is non-null; nothing here changes that.

**Test** — append to `tests/Radio.Infrastructure.Tests/Audio/Events/` in a new file
`AudioFileEventSourceFactoryAbsolutePathTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Events;

/// <summary>
/// Pins the two properties CreateFromAbsolutePathAsync exists for: it does not re-root, and it honours a
/// caller-supplied duration. Both are ADR-029 §4.1/§5.2 requirements for the RemoteMedia arm, and both
/// are things CreateFromFileAsync does the other way round.
/// </summary>
public class AudioFileEventSourceFactoryAbsolutePathTests : IDisposable
{
  private readonly string _dir =
    Path.Combine(Path.GetTempPath(), "radio-abs-" + Guid.NewGuid().ToString("N"));

  private AudioFileEventSourceFactory CreateFactory(string rootDirectory)
  {
    var options = new FilePlayerOptions { RootDirectory = rootDirectory };
    return new AudioFileEventSourceFactory(
      NullLogger<AudioFileEventSourceFactory>.Instance,
      NullLogger<Radio.Infrastructure.Audio.Sources.Events.AudioFileEventSource>.Instance,
      new StaticOptionsMonitor<FilePlayerOptions>(options));
  }

  public AudioFileEventSourceFactoryAbsolutePathTests() => Directory.CreateDirectory(_dir);

  public void Dispose()
  {
    try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    GC.SuppressFinalize(this);
  }

  private string WriteFile(string name, int bytes)
  {
    var path = Path.Combine(_dir, name);
    File.WriteAllBytes(path, new byte[bytes]);
    return path;
  }

  [Fact]
  public async Task ItDoesNotResolveAgainstFilePlayerRootDirectory()
  {
    // The root points somewhere that does not exist. CreateFromFileAsync would combine against it;
    // this must not, so an absolute path under a different tree still resolves.
    var factory = CreateFactory(Path.Combine(_dir, "a-root-that-is-not-where-the-file-is"));
    var file = WriteFile("recording.mp3", 32_000);

    var source = await factory.CreateFromAbsolutePathAsync(file, TimeSpan.FromSeconds(9));

    Assert.Equal(TimeSpan.FromSeconds(9), source.Duration);
    await source.DisposeAsync();
  }

  [Fact]
  public async Task ItHonoursTheAuthoritativeDurationRatherThanEstimating()
  {
    var factory = CreateFactory(_dir);
    // 32 000 bytes estimates to 2s at the factory's flat 16 000 B/s. The authoritative value is 47s,
    // and it is the one that must win — completion is driven by it.
    var file = WriteFile("recording.mp3", 32_000);

    var source = await factory.CreateFromAbsolutePathAsync(file, TimeSpan.FromSeconds(47));

    Assert.Equal(TimeSpan.FromSeconds(47), source.Duration);
    Assert.NotEqual(TimeSpan.FromSeconds(2), source.Duration);
    await source.DisposeAsync();
  }

  [Fact]
  public async Task ANullDurationFallsBackToTheSameEstimateCreateFromFileAsyncWouldUse()
  {
    // DurationSeconds == 0 means UNKNOWN (ADR-022 §4.2). The fallback must be the factory's own
    // estimator so this arc carries no second bytes-per-second constant.
    var factory = CreateFactory(_dir);
    var file = WriteFile("recording.mp3", 32_000);

    var source = await factory.CreateFromAbsolutePathAsync(file, duration: null);

    Assert.Equal(TimeSpan.FromSeconds(2), source.Duration);
    await source.DisposeAsync();
  }

  [Fact]
  public async Task ARelativePathIsRefusedLoudly()
  {
    var factory = CreateFactory(_dir);

    await Assert.ThrowsAsync<ArgumentException>(
      () => factory.CreateFromAbsolutePathAsync("recording.mp3", TimeSpan.FromSeconds(1)));
  }

  [Fact]
  public async Task AMissingFileIsRefusedLoudly()
  {
    var factory = CreateFactory(_dir);

    await Assert.ThrowsAsync<FileNotFoundException>(
      () => factory.CreateFromAbsolutePathAsync(
        Path.Combine(_dir, "absent.mp3"), TimeSpan.FromSeconds(1)));
  }
}
```

⚠ `StaticOptionsMonitor<T>` is a test helper. **Grep for it before writing one** — several test projects
already carry an equivalent. If none exists in `Radio.Infrastructure.Tests`, add the obvious four-line
implementation (`CurrentValue`, `Get`, `OnChange` returning null) in
`tests/Radio.Infrastructure.Tests/TestSupport/`; do not add a mocking framework for it.

---

### Task 4 — `EventPlaybackService`

**File:** `src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs` (new)

This is the row. Read §0.2, §0.6 and §0.7 first.

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// The one attended playback (ADR-029 D1, D2, D3).
///
/// <para>
/// Shaped after <see cref="AnnouncementService"/>, which is the only non-defective event path in this
/// tree, with a state machine added. Like it, this NEVER calls IMasterMixer.AddSource: audio reaches the
/// speakers because SoundFlowPlaybackService adds a component to the playback device's mixer itself, and
/// AddSource only mutates a bookkeeping list. SourcesController calls it and never removes, which is
/// where its per-play leak comes from.
/// </para>
///
/// <para>
/// ⚠ Unlike AnnouncementService, this does NOT await playback inside the call. StartAsync accepts, mints
/// an id, publishes a Preparing snapshot and returns; acquisition and playback run on a task that
/// outlives the HTTP response (ADR-029 §3.3 specifies 202). Everything about the cancellation model
/// follows from that — see StartAsync.
/// </para>
/// </summary>
public sealed class EventPlaybackService : IEventPlaybackService, IDisposable
{
  /// <summary>
  /// Prefix for the id this service mints. Deliberately a THIRD id space, not a reuse of either
  /// existing one: AudioFileEventSource carries IAudioSource.Id ("AudioFileEvent-…") AND a private
  /// _playbackId ("audio-event-…") that are not equal, while TTSEventSource uses Id for both. A
  /// cancel-by-id built on either would silently fail for one arm (ADR-029 §3.3). This service owns
  /// "evp-…", resolves it to a source instance, and then only ever calls interface methods on that
  /// instance — so the divergence is invisible here, and a log line carrying "evp-" can only have come
  /// from this seam.
  /// </summary>
  private const string PlaybackIdPrefix = "evp-";

  private readonly ILogger<EventPlaybackService> _logger;
  private readonly IOptionsMonitor<GvMediaOptions> _gvMediaOptions;
  private readonly IOptionsMonitor<TTSOptions> _ttsOptions;
  private readonly ITTSFactory _ttsFactory;
  private readonly AudioFileEventSourceFactory _fileFactory;
  private readonly IDuckingService _duckingService;
  private readonly GvMediaClient _gvMediaClient;

  // Serialises the transitions that install or tear down a playback. Async because teardown awaits
  // StopDuckingAsync / StopAsync / DisposeAsync.
  //
  // ⚠ The PlaybackCompleted handler must NEVER wait on this. That event is raised from inside
  // StopCoreAsync, which this service calls while holding the gate — so a handler that waited here
  // would deadlock on a non-reentrant semaphore. The handler instead claims the terminal flag and
  // returns; see OnSourceCompleted.
  private readonly SemaphoreSlim _gate = new(1, 1);

  // Guards the two fields below only. Never held across an await.
  private readonly object _stateLock = new();
  private Playback? _current;
  private EventPlaybackSnapshot? _snapshot;

  private bool _disposed;

  /// <summary>Creates the service.</summary>
  public EventPlaybackService(
    ILogger<EventPlaybackService> logger,
    IOptionsMonitor<GvMediaOptions> gvMediaOptions,
    IOptionsMonitor<TTSOptions> ttsOptions,
    ITTSFactory ttsFactory,
    AudioFileEventSourceFactory fileFactory,
    IDuckingService duckingService,
    GvMediaClient gvMediaClient)
  {
    _logger = logger;
    _gvMediaOptions = gvMediaOptions;
    _ttsOptions = ttsOptions;
    _ttsFactory = ttsFactory;
    _fileFactory = fileFactory;
    _duckingService = duckingService;
    _gvMediaClient = gvMediaClient;
  }

  /// <inheritdoc />
  public EventPlaybackSnapshot? Current
  {
    get
    {
      lock (_stateLock)
      {
        return _snapshot;
      }
    }
  }

  /// <inheritdoc />
  public event EventHandler<EventPlaybackSnapshot>? PlaybackChanged;

  /// <inheritdoc />
  public async Task<EventPlaybackSnapshot> StartAsync(
    EventPlaybackRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ObjectDisposedException.ThrowIf(_disposed, this);

    var gv = _gvMediaOptions.CurrentValue;

    var rejection = request.Validate(gv.MaxSpeechChars);
    if (rejection != EventPlaybackRejection.None)
    {
      throw new EventPlaybackRejectedException(rejection);
    }

    // The ONE failure knowable without touching the network, so it is answered synchronously rather
    // than accepted and then failed on a channel the caller may not be watching. Every other
    // GvMediaFailure becomes a FailureReason on a Failed snapshot — see AcquireRemoteMediaAsync.
    if (request.Kind == EventPlaybackKind.RemoteMedia && !gv.Enabled)
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Disabled, "GvMedia is disabled; refusing to accept a RemoteMedia playback.");
    }

    var playback = new Playback(
      PlaybackIdPrefix + Guid.NewGuid().ToString("N"), request.Kind, request.Label);

    await _gate.WaitAsync(cancellationToken);
    try
    {
      // One audio engine, one set of speakers, so one attended playback (ADR-029 D6 §8.1). This is
      // NOT D5's priority rule — "a source of priority >= 8 preempts attended playback" is PR 4 and
      // nothing here reads GvMedia:PreemptAtPriority.
      var replaced = _current;
      if (replaced is not null && replaced.ClaimTerminal())
      {
        _logger.LogInformation(
          "Attended playback {NewId} replaces {OldId}", playback.Id, replaced.Id);
        await TearDownAsync(replaced);
        Publish(SnapshotOf(replaced, EventPlaybackState.Stopped, failureReason: null));
      }

      var accepted = SnapshotOf(playback, EventPlaybackState.Preparing, failureReason: null);
      lock (_stateLock)
      {
        _current = playback;
        _snapshot = accepted;
      }

      // ⚠ playback.Token, NOT cancellationToken. cancellationToken is the CONTROLLER's, which on the
      // HTTP path is HttpContext.RequestAborted — scoped to the request, on a context that is pooled
      // and reset once the response completes. Acquisition outlives the 202 response by design, so
      // linking them would cancel every fetch the instant it was accepted. The cancellation that
      // actually exists here is StopAsync, a replacing StartAsync, and Dispose. EventPlaybackServiceTests
      // .AcquisitionSurvivesCancellationOfTheStartToken is what keeps this true.
      _ = Task.Run(
        () => AcquireAndPlayAsync(playback, request, playback.Token), CancellationToken.None);

      Publish(accepted);
      return accepted;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> StopAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    await _gate.WaitAsync(cancellationToken);
    try
    {
      var playback = _current;
      if (playback is null || playback.Id != playbackId || !playback.ClaimTerminal())
      {
        return false;
      }

      await TearDownAsync(playback);
      lock (_stateLock)
      {
        _current = null;
      }
      Publish(SnapshotOf(playback, EventPlaybackState.Stopped, failureReason: null));
      return true;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> SeekAsync(
    string playbackId, TimeSpan position, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    var playback = Resolve(playbackId);
    if (playback?.Source is not { } source || !source.IsSeekable)
    {
      // Reported as false rather than by letting EventAudioSourceBase.SeekAsync throw
      // NotSupportedException: "this cannot scrub" is an ordinary answer, not an exception. The
      // return is narrower than "the audio moved" and the interface's remarks say exactly why.
      return false;
    }

    await source.SeekAsync(position, cancellationToken);
    PublishCurrentState(playback);
    return true;
  }

  /// <inheritdoc />
  public async Task<bool> PauseAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    var playback = Resolve(playbackId);
    if (playback?.Source is not { } source || source.State != AudioSourceState.Playing)
    {
      return false;
    }

    await source.PauseAsync(cancellationToken);
    Publish(SnapshotOf(playback, EventPlaybackState.Paused, failureReason: null));
    return true;
  }

  /// <inheritdoc />
  public async Task<bool> ResumeAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    var playback = Resolve(playbackId);
    if (playback?.Source is not { } source || source.State != AudioSourceState.Paused)
    {
      return false;
    }

    await source.ResumeAsync(cancellationToken);
    Publish(SnapshotOf(playback, EventPlaybackState.Playing, failureReason: null));
    return true;
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    Playback? playback;
    lock (_stateLock)
    {
      playback = _current;
      _current = null;
    }

    // Cancel rather than tear down: Dispose is synchronous, teardown is not, and the acquisition
    // task's own catch handles the cancellation. What this guarantees is that no fetch or synthesis
    // keeps running after the container has gone.
    playback?.Cancel();
    _gate.Dispose();
  }

  // ── acquisition ─────────────────────────────────────────────────────────

  private async Task AcquireAndPlayAsync(
    Playback playback, EventPlaybackRequest request, CancellationToken token)
  {
    try
    {
      IEventAudioSource source;
      switch (request.Kind)
      {
        case EventPlaybackKind.RemoteMedia:
          source = await AcquireRemoteMediaAsync(playback, request, token);
          break;
        case EventPlaybackKind.Speech:
          source = await AcquireSpeechAsync(playback, request, token);
          break;
        default:
          // Unreachable: Validate rejected every other value before the playback was minted.
          throw new InvalidOperationException($"Unhandled kind {request.Kind}.");
      }

      token.ThrowIfCancellationRequested();

      playback.Source = source;
      source.PlaybackCompleted += (_, e) => OnSourceCompleted(playback, e);

      _duckingService.SetPriority(source, request.Priority);
      await _duckingService.StartDuckingAsync(source, token);

      await source.PlayAsync(token);

      Publish(SnapshotOf(playback, EventPlaybackState.Playing, failureReason: null));
    }
    catch (OperationCanceledException)
    {
      // Stop, replacement or shutdown. The transition was already published by whoever cancelled,
      // or is about to be; claiming the flag here only stops a late failure from overwriting it.
      playback.ClaimTerminal();
      _logger.LogDebug("Attended playback {Id} cancelled during acquisition", playback.Id);
    }
    catch (GvMediaUnavailableException ex)
    {
      await FailAsync(playback, "Media" + ex.Reason, ex);
    }
    catch (Exception ex)
    {
      await FailAsync(playback, FailureReasonFor(request.Kind), ex);
    }
  }

  private async Task<IEventAudioSource> AcquireRemoteMediaAsync(
    Playback playback, EventPlaybackRequest request, CancellationToken token)
  {
    // Validate guaranteed both of these on the RemoteMedia arm.
    var mediaId = request.MediaId!;
    var masked = GvMediaCache.MaskFor(mediaId);

    _logger.LogInformation(
      "Attended playback {Id}: acquiring {MaskedId}", playback.Id, masked);

    var path = await _gvMediaClient.GetVoicemailFileAsync(mediaId, token);

    // DurationSeconds == 0 means UNKNOWN (ADR-022 §4.2, ADR-029 §4.1). The SOURCE still needs a
    // number — its completion is driven by one — so the factory estimates in that case; the
    // SNAPSHOT reports null, so the UI renders an indeterminate bar rather than a confident lie.
    var authoritative = request.DurationSeconds is > 0
      ? TimeSpan.FromSeconds(request.DurationSeconds.Value)
      : (TimeSpan?)null;
    playback.ReportedDuration = authoritative;

    // GetFullPath because GvMedia:CacheDirectory ships as the relative "./data/gvmedia": an absolute
    // path is what CreateFromAbsolutePathAsync requires, and it keeps the path unambiguous in a log.
    return await _fileFactory.CreateFromAbsolutePathAsync(
      Path.GetFullPath(path), authoritative, token);
  }

  private async Task<IEventAudioSource> AcquireSpeechAsync(
    Playback playback, EventPlaybackRequest request, CancellationToken token)
  {
    var tts = _ttsOptions.CurrentValue;

    // ⚠ ALL FOUR fields, filled explicitly. ADR-029 §9.3 names only Engine, but Voice, Speed and
    // Pitch are non-nullable with initializers too — so TTSFactory's four "parameters?.X ?? opts.X"
    // fallbacks are ALL lifted by the null-conditional on the object, and any non-null
    // TTSParameters silently pins every field the caller did not set to the TYPE's default rather
    // than to configuration. On this box that would swap Google's "en-US-Standard-A" for espeak's
    // "en" — which is also not a valid Google voice, so it would fail rather than merely differ.
    // Passing null would be correct today and stops being correct the moment VoiceId is set, which
    // is the trap re-armed; so it is never passed.
    var parameters = new TTSParameters
    {
      Engine = ResolveEngine(request.Engine, tts.DefaultEngine),
      Voice = request.VoiceId ?? tts.DefaultVoice,
      Speed = tts.DefaultSpeed,
      Pitch = tts.DefaultPitch
    };

    // ⚠ Never log request.Text. For the Speech arm it is an SMS body — private content by exactly
    // the standard PHN-1b §0.3 ⓸'s masking rule protects. Length and engine, nothing else.
    _logger.LogInformation(
      "Attended playback {Id}: synthesising {Chars} characters with {Engine}",
      playback.Id, request.Text!.Length, parameters.Engine);

    // ⚠ The first reader TTSOptions.GenerationTimeoutSeconds has ever had. Nothing in
    // src/Radio.Infrastructure reads it: TTSFactory awaits its espeak subprocess and its cloud calls
    // on the caller's token and no other bound, so an unbounded synthesis would park this seam in
    // Preparing with no route that clears it. Bounding it HERE rather than in TTSFactory keeps a
    // live shared path with two other callers out of this PR.
    //
    // Best-effort at one point, stated rather than hidden: process.StandardInput.WriteAsync takes no
    // token, so a stall writing to espeak's stdin is not interruptible. And cancelling does not kill
    // the espeak process — it exits when its stdout pipe breaks.
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, tts.GenerationTimeoutSeconds)));

    try
    {
      return await _ttsFactory.CreateAsync(request.Text!, parameters, timeout.Token);
    }
    catch (OperationCanceledException) when (!token.IsCancellationRequested)
    {
      throw new TimeoutException(
        $"TTS synthesis exceeded TTS:GenerationTimeoutSeconds ({tts.GenerationTimeoutSeconds}s).");
    }
  }

  /// <summary>
  /// Parses an engine name exactly as TTSFactory.ParseEngine does — Enum.TryParse ignoring case,
  /// ESpeak on garbage — so announcements and attended speech agree by construction.
  /// </summary>
  /// <remarks>
  /// The ESpeak fallback is only ever reached for a garbage TTS:DefaultEngine, never for a garbage
  /// request override: EventPlaybackRequest.Validate refuses an unparseable Engine with
  /// EventPlaybackRejection.UnknownEngine before a playback is minted.
  /// </remarks>
  internal static TTSEngine ResolveEngine(string? requested, string configuredDefault)
  {
    var name = string.IsNullOrWhiteSpace(requested) ? configuredDefault : requested;
    return Enum.TryParse<TTSEngine>(name, ignoreCase: true, out var engine)
      ? engine
      : TTSEngine.ESpeak;
  }

  private static string FailureReasonFor(EventPlaybackKind kind) =>
    kind == EventPlaybackKind.Speech ? "SpeechSynthesisFailed" : "MediaAcquisitionFailed";

  // ── completion and teardown ─────────────────────────────────────────────

  /// <summary>
  /// Handles PlaybackCompleted from the source.
  /// </summary>
  /// <remarks>
  /// ⚠ This must never wait on _gate. It is raised from inside StopCoreAsync, which TearDownAsync
  /// calls while holding the gate, so waiting here would deadlock a non-reentrant semaphore.
  ///
  /// ⚠ And it must be once-only. BOTH event sources raise completion from two independent places —
  /// EndOfContent from their monitor and UserStopped from StopCoreAsync — and AudioSourceBase.StopAsync
  /// short-circuits only on Created or Disposed, never on Stopped. So teardown after a natural end
  /// raises a SECOND event. AnnouncementService is immune by accident (TrySetResult discards it); this
  /// holds a state machine and is not, so an unguarded handler would overwrite Completed with Stopped
  /// and — from PR 5 — broadcast a transition that did not happen.
  /// </remarks>
  private void OnSourceCompleted(Playback playback, AudioSourceCompletedEventArgs e)
  {
    if (!playback.ClaimTerminal())
    {
      return;
    }

    var state = e.Reason switch
    {
      PlaybackCompletionReason.EndOfContent => EventPlaybackState.Completed,
      PlaybackCompletionReason.Error => EventPlaybackState.Failed,
      _ => EventPlaybackState.Stopped
    };
    var reason = e.Reason == PlaybackCompletionReason.Error ? "PlaybackError" : null;

    _ = Task.Run(async () =>
    {
      try
      {
        await _gate.WaitAsync();
        try
        {
          await TearDownAsync(playback);
          lock (_stateLock)
          {
            if (ReferenceEquals(_current, playback))
            {
              _current = null;
            }
          }
          Publish(SnapshotOf(playback, state, reason));
        }
        finally
        {
          _gate.Release();
        }
      }
      catch (ObjectDisposedException)
      {
        // The container went away underneath us. Nothing to publish to.
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error finalising attended playback {Id}", playback.Id);
      }
    }, CancellationToken.None);
  }

  private async Task FailAsync(Playback playback, string failureReason, Exception ex)
  {
    if (!playback.ClaimTerminal())
    {
      return;
    }

    // Warning, not Error: since LOG-11 the journal carries Warning and above, and a failed voicemail
    // is exactly what an operator diagnoses from the box. The exception is logged, so the rule that
    // no raw media id may reach an exception MESSAGE is what keeps this line clean — GvMediaClient
    // and EventPlaybackRejectedException both hold to it.
    _logger.LogWarning(ex, "Attended playback {Id} failed: {Reason}", playback.Id, failureReason);

    try
    {
      await _gate.WaitAsync();
      try
      {
        await TearDownAsync(playback);
        lock (_stateLock)
        {
          if (ReferenceEquals(_current, playback))
          {
            _current = null;
          }
        }
        Publish(SnapshotOf(playback, EventPlaybackState.Failed, failureReason));
      }
      finally
      {
        _gate.Release();
      }
    }
    catch (ObjectDisposedException)
    {
      // Disposed mid-failure. Nothing to publish to.
    }
  }

  /// <summary>
  /// Stops ducking, stops the source and disposes it. Every step is independently guarded: this runs
  /// on the failure path too, where any of them may already be in a bad state, and a throw here would
  /// leave the seam holding a playback it can never clear.
  /// </summary>
  /// <remarks>
  /// The caller must have claimed the terminal flag first. That is what keeps source.StopAsync's
  /// UserStopped event — raised synchronously from inside this call — from re-entering _gate.
  /// </remarks>
  private async Task TearDownAsync(Playback playback)
  {
    playback.Cancel();

    if (playback.Source is not { } source)
    {
      return;
    }

    try { await _duckingService.StopDuckingAsync(source); }
    catch (Exception ex) { _logger.LogWarning(ex, "Error stopping ducking for {Id}", playback.Id); }

    try { await source.StopAsync(); }
    catch (Exception ex) { _logger.LogWarning(ex, "Error stopping source for {Id}", playback.Id); }

    // Disposal releases the FileStream AudioFileEventSource opened over the cached recording, which
    // is what lets GvMediaCache evict it later. On Linux an unlink would succeed regardless; on
    // Windows the FileShare.Read handle would make File.Delete throw, and the evictor logs and
    // continues — so a leaked handle there costs cap accuracy, not correctness.
    try { await source.DisposeAsync(); }
    catch (Exception ex) { _logger.LogWarning(ex, "Error disposing source for {Id}", playback.Id); }
  }

  // ── snapshots ───────────────────────────────────────────────────────────

  private Playback? Resolve(string playbackId)
  {
    lock (_stateLock)
    {
      return _current is { } p && p.Id == playbackId ? p : null;
    }
  }

  /// <summary>
  /// Mints a snapshot. Position and Duration are read from the source at the instant of minting —
  /// the snapshot is an ANCHOR, not a tick (ADR-029 §8.2).
  /// </summary>
  /// <remarks>
  /// ⚠ For a Speech playback PositionAtBroadcast is ALWAYS TimeSpan.Zero, for the whole playback.
  /// EventAudioSourceBase.Position defaults to zero and TTSEventSource deliberately does not override
  /// it, whereas AudioFileEventSource does. That is not a defect this snapshot hides — it is what the
  /// source reports — but nothing in this PR may claim otherwise. See §0.4 C-27 and the handoff to PR 5.
  ///
  /// Duration differs by arm and both are honest about what they are. RemoteMedia reports the
  /// provider's authoritative value, or NULL when the provider said 0 (unknown). Speech reports the
  /// source's own duration, which is TTSFactory's size-based ESTIMATE of the synthesised audio — the
  /// only value that exists, and the one the source's completion is driven by.
  /// </remarks>
  private EventPlaybackSnapshot SnapshotOf(
    Playback playback, EventPlaybackState state, string? failureReason)
  {
    var source = playback.Source;
    var duration = state == EventPlaybackState.Preparing
      ? null
      : playback.Kind == EventPlaybackKind.Speech
        ? source?.Duration
        : playback.ReportedDuration;

    return new EventPlaybackSnapshot(
      playback.Id,
      playback.Kind,
      playback.Label,
      state,
      duration,
      source?.Position ?? TimeSpan.Zero,
      DateTimeOffset.UtcNow,
      failureReason);
  }

  private void PublishCurrentState(Playback playback)
  {
    var state = playback.Source?.State == AudioSourceState.Paused
      ? EventPlaybackState.Paused
      : EventPlaybackState.Playing;
    Publish(SnapshotOf(playback, state, failureReason: null));
  }

  /// <summary>
  /// Stores the snapshot and raises PlaybackChanged.
  /// </summary>
  /// <remarks>
  /// ⚠ A late snapshot must not resurrect a replaced playback. Only the CURRENT playback's snapshot
  /// is stored; a snapshot for one that has already been replaced is raised (so a subscriber sees the
  /// stop) but not retained (so Current keeps describing the playback that is actually in flight).
  ///
  /// Nothing subscribes to PlaybackChanged in this PR — PR 5 is what connects it to /hubs/audio. It is
  /// raised now because PR 1's contract requires it and because a broadcast bolted on later would be
  /// a second place transitions are decided.
  /// </remarks>
  private void Publish(EventPlaybackSnapshot snapshot)
  {
    lock (_stateLock)
    {
      if (_current is null || _current.Id == snapshot.Id)
      {
        _snapshot = snapshot;
      }
    }

    try
    {
      PlaybackChanged?.Invoke(this, snapshot);
    }
    catch (Exception ex)
    {
      // A subscriber that throws must not take the playback down with it.
      _logger.LogWarning(ex, "A PlaybackChanged subscriber threw for {Id}", snapshot.Id);
    }
  }

  /// <summary>One in-flight attended playback. At most one exists at a time (ADR-029 D6 §8.1).</summary>
  private sealed class Playback
  {
    private readonly CancellationTokenSource _cts = new();
    private int _terminal;

    public Playback(string id, EventPlaybackKind kind, string? label)
    {
      Id = id;
      Kind = kind;
      Label = label;
    }

    public string Id { get; }
    public EventPlaybackKind Kind { get; }
    public string? Label { get; }
    public IEventAudioSource? Source { get; set; }
    public TimeSpan? ReportedDuration { get; set; }
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// True for the FIRST caller only. Every terminal transition — natural completion, user stop,
    /// replacement, failure — goes through this, so a playback ends exactly once no matter how many
    /// PlaybackCompleted events its source raises (§0.4 C-28).
    /// </summary>
    public bool ClaimTerminal() => Interlocked.CompareExchange(ref _terminal, 1, 0) == 0;

    public void Cancel()
    {
      try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already cancelled and disposed */ }
    }
  }
}
```

⚠ **Two things a reviewer will reasonably challenge, answered here so the answers are on the record:**

1. *"`Publish` is called from inside `_gate` in some paths and outside in others."* Correct, and
   deliberate: `Publish` takes only `_stateLock` and raises an event. It never awaits, so it cannot
   deadlock against `_gate`, and holding `_gate` while a subscriber runs is a hazard PR 5 would inherit.
   The `try/catch` around the invocation is what makes that safe rather than hopeful.
2. *"`_current` is written in three places."* Yes — installed in `StartAsync`, cleared in `StopAsync`, and
   cleared under a `ReferenceEquals` check on the two completion paths. The `ReferenceEquals` is the
   load-bearing part: without it, a completion arriving after a replacement would clear the **new**
   playback's slot.

---

### Task 5 — `AddEventPlayback`, and one line in `Program.cs`

**File:** `src/Radio.Infrastructure/DependencyInjection/EventPlaybackServiceExtensions.cs` (new)

```csharp
using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.DependencyInjection;

/// <summary>
/// Registers attended event playback (ADR-029 D1).
/// </summary>
/// <remarks>
/// A standalone extension, following AddGvMedia and AddRadioWeather rather than being folded into
/// AddSoundFlowAudio — for the reason GvMediaServiceExtensions' remarks give, plus one specific to this
/// service: EventPlaybackService is the seam a controller injects, and burying its registration inside
/// a 400-line method that also wires audio hardware is how a missing registration becomes a service
/// that will not start on an appliance in a cabinet.
///
/// ⚠ It DEPENDS on AddSoundFlowAudio having been called — ITTSFactory, IDuckingService and
/// AudioFileEventSourceFactory all come from there — and on AddGvMedia for GvMediaClient. Registration
/// order in an IServiceCollection does not matter (resolution is lazy), so this is a dependency on the
/// calls happening, not on their sequence.
/// </remarks>
public static class EventPlaybackServiceExtensions
{
  /// <summary>Registers <see cref="EventPlaybackService"/> as the one attended-playback seam.</summary>
  public static IServiceCollection AddEventPlayback(this IServiceCollection services)
  {
    // Singleton because the state is global: one audio engine, one set of speakers, one in-flight
    // attended playback (ADR-029 D6 §8.1). Registered concretely and then aliased, following the
    // AddSingleton<IDuckingService>(sp => sp.GetRequiredService<DuckingService>()) pattern, so both
    // resolve to ONE instance — two would be two Current properties and two single slots.
    services.AddSingleton<EventPlaybackService>();
    services.AddSingleton<IEventPlaybackService>(sp => sp.GetRequiredService<EventPlaybackService>());

    return services;
  }
}
```

**File:** `src/Radio.API/Program.cs` (edit — one line)

Immediately after the existing `builder.Services.AddGvMedia(builder.Configuration);`:

```csharp
// ADR-029 D1. Standalone for the same reason AddGvMedia is; depends on AddSoundFlowAudio and
// AddGvMedia above for ITTSFactory / IDuckingService / AudioFileEventSourceFactory / GvMediaClient.
builder.Services.AddEventPlayback();
```

⚠ **Grep for `AddGvMedia(builder.Configuration)` to find the site.** Do not use a line number.

⚠ **Nothing is registered in `Radio.Web`.** §0.5 item 1.

---

### Task 6 — `EventPlaybackService` tests

**File:** `tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs` (new)

These are the tests that have to be able to fail. The service is built over real collaborators wherever
that is cheap — a real `GvMediaClient` over a stub `HttpMessageHandler` and a real `GvMediaCache` over a
temp directory, per `PHN-1b` §4 item 1 — and over minimal hand-written fakes for `ITTSFactory` and
`IDuckingService`. **No mocking framework**; this repo does not use one in these projects.

Fakes (top of the file):

```csharp
/// <summary>An ITTSFactory that hands back a source the test controls, or throws on demand.</summary>
private sealed class FakeTtsFactory : ITTSFactory
{
  public Func<string, TTSParameters?, CancellationToken, Task<IEventAudioSource>>? OnCreate { get; set; }
  public TTSParameters? LastParameters { get; private set; }

  public IReadOnlyList<TTSEngineInfo> AvailableEngines => Array.Empty<TTSEngineInfo>();

  public Task<IEventAudioSource> CreateAsync(
    string text, TTSParameters? parameters = null, CancellationToken cancellationToken = default)
  {
    LastParameters = parameters;
    return OnCreate is null
      ? Task.FromResult<IEventAudioSource>(new FakeEventSource())
      : OnCreate(text, parameters, cancellationToken);
  }

  public Task<IReadOnlyList<TTSVoiceInfo>> GetVoicesAsync(TTSEngine e, CancellationToken ct = default)
    => Task.FromResult<IReadOnlyList<TTSVoiceInfo>>(Array.Empty<TTSVoiceInfo>());
  public Task<int> RefreshVoicesAsync(TTSEngine e, CancellationToken ct = default) => Task.FromResult(0);
  public Task SetVoiceFavoriteAsync(TTSEngine e, string v, CancellationToken ct = default) => Task.CompletedTask;
  public Task RemoveVoiceFavoriteAsync(TTSEngine e, string v, CancellationToken ct = default) => Task.CompletedTask;
}
```

```csharp
/// <summary>
/// A minimal IEventAudioSource the test drives directly.
/// </summary>
/// <remarks>
/// ⚠ RaiseCompleted is deliberately callable more than once, because that is what the real sources do:
/// both raise EndOfContent from their monitor AND UserStopped from StopCoreAsync, and
/// AudioSourceBase.StopAsync does not short-circuit on Stopped. A fake that could only complete once
/// would make ATerminalTransitionHappensExactlyOnce vacuous — it would be asserting a property of the
/// fake rather than of the service (§0.4 C-28).
/// </remarks>
private sealed class FakeEventSource : IEventAudioSource
{
  public string Id { get; } = "AudioFileEvent-" + Guid.NewGuid().ToString("N");
  public string Name => "fake";
  public AudioSourceType Type => AudioSourceType.AudioFileEvent;
  public AudioSourceCategory Category => AudioSourceCategory.Event;
  public AudioSourceState State { get; set; } = AudioSourceState.Ready;
  public float Volume { get; set; } = 1.0f;

  public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(30);
  public TimeSpan Position { get; set; } = TimeSpan.Zero;
  public bool IsSeekable { get; set; } = true;

  public int PlayCalls { get; private set; }
  public int StopCalls { get; private set; }
  public int DisposeCalls { get; private set; }
  public TimeSpan? SoughtTo { get; private set; }

  public event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged;
  public event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;

  public object GetSoundComponent() => this;
  public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

  public Task PlayAsync(CancellationToken ct = default)
  {
    PlayCalls++;
    State = AudioSourceState.Playing;
    return Task.CompletedTask;
  }

  public Task PauseAsync(CancellationToken ct = default)
  {
    State = AudioSourceState.Paused;
    return Task.CompletedTask;
  }

  public Task ResumeAsync(CancellationToken ct = default)
  {
    State = AudioSourceState.Playing;
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken ct = default)
  {
    StopCalls++;
    State = AudioSourceState.Stopped;
    return Task.CompletedTask;
  }

  public Task SeekAsync(TimeSpan position, CancellationToken ct = default)
  {
    SoughtTo = position;
    return Task.CompletedTask;
  }

  public ValueTask DisposeAsync()
  {
    DisposeCalls++;
    return ValueTask.CompletedTask;
  }

  public void RaiseCompleted(PlaybackCompletionReason reason, Exception? error = null) =>
    PlaybackCompleted?.Invoke(this, new AudioSourceCompletedEventArgs
    {
      SourceId = Id, Reason = reason, Error = error
    });

  public void RaiseStateChanged() =>
    StateChanged?.Invoke(this, new AudioSourceStateChangedEventArgs
    {
      SourceId = Id, PreviousState = State, NewState = State
    });
}

/// <summary>Records what the seam asked of ducking. Asserts nothing on its own.</summary>
private sealed class FakeDuckingService : IDuckingService
{
  public List<(string Id, int Priority)> Priorities { get; } = new();
  public List<string> Started { get; } = new();
  public List<string> Stopped { get; } = new();

  public float CurrentDuckLevel => 100f;
  public bool IsDucking => Started.Count > Stopped.Count;
  public int ActiveEventCount => Started.Count - Stopped.Count;

  public event EventHandler<DuckingStateChangedEventArgs>? DuckingStateChanged;
  public event EventHandler<DuckingLevelChangedEventArgs>? DuckingLevelChanged;

  public Task StartDuckingAsync(IEventAudioSource s, CancellationToken ct = default)
  {
    Started.Add(s.Id);
    DuckingStateChanged?.Invoke(this, new DuckingStateChangedEventArgs { IsDucking = true });
    return Task.CompletedTask;
  }

  public Task StopDuckingAsync(IEventAudioSource s, CancellationToken ct = default)
  {
    Stopped.Add(s.Id);
    DuckingLevelChanged?.Invoke(this, new DuckingLevelChangedEventArgs { TransitionComplete = true });
    return Task.CompletedTask;
  }

  public Task StopAllDuckingAsync(CancellationToken ct = default) => Task.CompletedTask;
  public int GetPriority(IAudioSource s) =>
    Priorities.LastOrDefault(p => p.Id == s.Id) is { Priority: var v and > 0 } ? v : 8;
  public void SetPriority(IAudioSource s, int priority) => Priorities.Add((s.Id, priority));
  public IReadOnlyList<IEventAudioSource> GetActiveEventsByPriority() => Array.Empty<IEventAudioSource>();
  public void Dispose() { }
}
```

⚠ **`FakeDuckingService` raises both its events**, even though nothing in PR 3 subscribes. That is
deliberate: PR 4 is the PR that subscribes to `DuckingStateChanged`, and a fake that never raised it would
let PR 4 add a subscription that deadlocks or re-enters without any existing test noticing.

⚠ **Builder must check `IEventAudioSource`'s and `IDuckingService`'s members against the tree before
compiling these.** They are transcribed from `IEventAudioSource.cs`, `IAudioSource.cs` and
`IDuckingService.cs` at `d1226da`; if a member has moved, fix the fake, not the interface.

**The load-bearing facts, written out.** These four are the ones that can catch a dead or mis-wired
surface; Builder should write them first and confirm each **fails** against a deliberately broken
implementation before writing the rest.

```csharp
  [Fact]
  public async Task StartAsync_ReturnsPreparing_BeforeAnyAudioExists()
  {
    // ADR-029 §3.3 specifies 202, and the shipped IEventPlaybackService doc says the snapshot is
    // "normally Preparing, because both arms have an acquisition phase". Everything about the
    // cancellation model below follows from this being true.
    var tts = new FakeTtsFactory();
    var gate = new TaskCompletionSource();
    tts.OnCreate = async (_, _, ct) =>
    {
      await gate.Task.WaitAsync(ct);
      return new FakeEventSource();
    };
    using var service = CreateService(ttsFactory: tts);

    var snapshot = await service.StartAsync(SpeechRequest());

    Assert.Equal(EventPlaybackState.Preparing, snapshot.State);
    Assert.StartsWith("evp-", snapshot.Id);
    Assert.Null(snapshot.Duration);              // no audio exists yet, so no duration is known
    Assert.Equal(TimeSpan.Zero, snapshot.PositionAtBroadcast);
    Assert.Equal(snapshot.Id, service.Current?.Id);

    gate.SetResult();
  }

  [Fact]
  public async Task AcquisitionSurvivesCancellationOfTheStartToken()
  {
    // ⚠ THE C-21 PIN. On the HTTP path the token passed to StartAsync is HttpContext.RequestAborted,
    // which is scoped to a request the acquisition deliberately outlives — so linking the two would
    // cancel every fetch the instant it was accepted, and the RemoteMedia arm would fail 100% of the
    // time in a way that looks like a network problem. This fails if anyone links them.
    var tts = new FakeTtsFactory();
    var released = new TaskCompletionSource();
    var source = new FakeEventSource();
    tts.OnCreate = async (_, _, ct) =>
    {
      await released.Task.WaitAsync(ct);
      return source;
    };
    using var service = CreateService(ttsFactory: tts);
    using var caller = new CancellationTokenSource();

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest(), caller.Token);

    // The caller goes away the instant it has its 202 — exactly what a kiosk reload does.
    await caller.CancelAsync();
    released.SetResult();

    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(EventPlaybackState.Playing, final.State);
    Assert.Equal(1, source.PlayCalls);
  }

  [Fact]
  public async Task ATerminalTransitionHappensExactlyOnce_EvenWhenTheSourceRaisesCompletionTwice()
  {
    // C-28. Both shipped sources raise EndOfContent from their monitor AND UserStopped from
    // StopCoreAsync, and AudioSourceBase.StopAsync does not short-circuit on Stopped — so teardown
    // after a natural end raises a second event. AnnouncementService is immune by accident
    // (TrySetResult discards it); a state machine is not.
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts);

    var terminals = new List<EventPlaybackSnapshot>();
    service.PlaybackChanged += (_, s) =>
    {
      if (s.State is EventPlaybackState.Completed or EventPlaybackState.Stopped
          or EventPlaybackState.Failed)
      {
        lock (terminals) { terminals.Add(s); }
      }
    };

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    source.RaiseCompleted(PlaybackCompletionReason.EndOfContent);
    source.RaiseCompleted(PlaybackCompletionReason.UserStopped);

    await WaitUntilAsync(() => service.Current is null, TimeSpan.FromSeconds(5));

    lock (terminals)
    {
      Assert.Single(terminals);
      // The FIRST one wins. A guard that let the last write through would report Stopped for a
      // playback that ran to the end.
      Assert.Equal(EventPlaybackState.Completed, terminals[0].State);
    }
  }

  [Theory]
  [InlineData(HttpStatusCode.NotFound, "MediaNotFound")]
  [InlineData(HttpStatusCode.Unauthorized, "MediaUnauthorized")]
  [InlineData(HttpStatusCode.Forbidden, "MediaUnauthorized")]
  [InlineData(HttpStatusCode.BadGateway, "MediaUpstream")]
  [InlineData(HttpStatusCode.InternalServerError, "MediaUpstream")]
  public async Task EveryGvMediaFailureReachesTheSnapshotUnderItsOwnName(
    HttpStatusCode status, string expectedReason)
  {
    // C-23. The 202 shape means these never become status codes, so FailureReason is the ONLY place
    // the distinction survives — and GV-6 / GV-8 are open rows for exactly the collapse this
    // prevents. Driven through the REAL GvMediaClient over a stub handler, so the real exception is
    // produced rather than a test constructing one.
    using var service = CreateService(
      gvMedia: new GvMediaOptions { Enabled = true, CacheDirectory = _cacheDir },
      httpHandler: new StubHandler(_ => new HttpResponseMessage(status)));

    var failed = NextSnapshotWith(service, EventPlaybackState.Failed);
    await service.StartAsync(VoicemailRequest());

    var final = await failed.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(expectedReason, final.FailureReason);
    Assert.Null(service.Current);
  }
```

Three helpers those four rely on, and they carry a rule of their own:

```csharp
  /// <summary>
  /// Completes on the first snapshot in the given state. Subscribed BEFORE the action that causes it,
  /// so there is no window in which the transition can be missed.
  /// </summary>
  /// <remarks>
  /// ⚠ Every asynchronous assertion in this file goes through this or WaitUntilAsync, never through a
  /// fixed Task.Delay. TEST-4 is the row about a wall-clock test window racing a wall-clock loop, and
  /// TEST-1 is the row about not writing the next one.
  /// </remarks>
  private static Task<EventPlaybackSnapshot> NextSnapshotWith(
    EventPlaybackService service, EventPlaybackState state)
  {
    var tcs = new TaskCompletionSource<EventPlaybackSnapshot>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    service.PlaybackChanged += (_, s) =>
    {
      if (s.State == state)
      {
        tcs.TrySetResult(s);
      }
    };
    return tcs.Task;
  }

  private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (condition())
      {
        return;
      }
      await Task.Delay(10);   // a poll INSIDE a bounded wait, not a sleep before an assertion
    }
    Assert.Fail($"Condition was not met within {timeout}.");
  }

  /// <summary>An HttpMessageHandler that answers from a function. No network, no timing.</summary>
  private sealed class StubHandler : HttpMessageHandler
  {
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public int Calls { get; private set; }

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Calls++;
      return Task.FromResult(_respond(request));
    }
  }
```

⚠ **`CreateService`, `SpeechRequest()` and `VoicemailRequest()` are the file's own fixtures** and Builder
writes them: `CreateService` news up `EventPlaybackService` over `NullLogger`, `StaticOptionsMonitor`s for
`GvMediaOptions` and `TTSOptions`, the fakes above, a real `AudioFileEventSourceFactory` and a real
`GvMediaClient` over a `StubHandler` and a real `GvMediaCache` pointed at a per-test temp directory
(`_cacheDir`, deleted in `Dispose`). Defaults: `TTSOptions { DefaultEngine = "Google",
DefaultVoice = "en-US-Standard-A", DefaultSpeed = 1.0f, DefaultPitch = 1.0f }`, matching what the box
actually ships, so `SpeechFillsAllFourTtsParametersFromConfiguration` asserts against real values rather
than invented ones.

**The remaining facts.** Each states its assertion precisely; the bodies follow the four above
mechanically. They are listed rather than written out because their shape is the same and the value is in
the assertion, not the plumbing:

```csharp
  [Fact]
  public async Task StopAsync_CancelsAnAcquisitionStillInFlight()
  {
    // The cancellation that DOES exist. Start with a fetch that blocks, stop by id, assert the fetch's
    // token is cancelled and the snapshot goes to Stopped — which is also the cancellation
    // GvMediaCache's staging write is protecting against, now that C-21 has removed the one its
    // remark named.
  }

  [Fact]
  public async Task ASecondStartReplacesTheFirst_AndTheFirstIsTornDown()
  {
    // ADR-029 D6 §8.1 — one set of speakers. Assert the first source saw StopAsync and DisposeAsync,
    // that StopDuckingAsync was called for it, that Current describes the SECOND playback, and that
    // the ids differ.
  }

  [Fact]
  public async Task ALateCompletionFromAReplacedPlaybackDoesNotClearTheCurrentOne()
  {
    // The ReferenceEquals guard in OnSourceCompleted. Start A, start B, then raise completion on A's
    // source; Current must still describe B.
  }

  [Fact]
  public async Task SpeechFillsAllFourTtsParametersFromConfiguration()
  {
    // ⚠ THE C-25 PIN, and it must assert all four. TTSParameters' four fields are non-nullable with
    // initializers, so a partially-filled instance silently pins Voice, Speed and Pitch to the TYPE's
    // defaults as well as Engine. Configure TTS as the box has it — Google / en-US-Standard-A — pass a
    // request with no overrides, and assert LastParameters carries Google, "en-US-Standard-A", and the
    // configured speed and pitch. Asserting Engine alone would pass while Voice was silently "en".
  }

  [Fact]
  public async Task ARequestVoiceOverridesTheConfiguredVoice_AndLeavesTheEngineOnTheConfiguredOne()
  {
    // The exact shape ADR-029 §9.3 warns about: the moment a voice is attached, a null-parameters
    // call is no longer available and the default silently becomes ESpeak unless it is set explicitly.
  }

  [Theory]
  [InlineData(null, "Google", TTSEngine.Google)]
  [InlineData("Azure", "Google", TTSEngine.Azure)]
  [InlineData(null, "not-an-engine", TTSEngine.ESpeak)]
  public void ResolveEngine_MatchesTTSFactoryParseEngine(string? requested, string configured, TTSEngine expected)
  {
    Assert.Equal(expected, EventPlaybackService.ResolveEngine(requested, configured));
  }

  [Fact]
  public async Task SynthesisIsBoundedByGenerationTimeoutSeconds()
  {
    // C-24. TTSOptions.GenerationTimeoutSeconds has never had a reader; this is its first. Configure
    // it to 1, make CreateAsync block on its token, and assert the snapshot reaches Failed with
    // "SpeechSynthesisFailed" rather than sitting in Preparing forever.
  }

  [Fact]
  public async Task StartAsync_ThrowsEventPlaybackRejected_ForAnInvalidRequest()
  {
    // The seam validates as well as the controller. Both call Validate; neither re-derives a rule.
  }

  [Fact]
  public async Task StartAsync_ThrowsGvMediaUnavailableDisabled_WithoutMintingAPlayback()
  {
    // C-23: the one failure knowable without the network is answered synchronously. Assert Current is
    // still null afterwards — a refused request must leave no state behind.
  }

  [Fact]
  public async Task TheTransportAndTooLargeFailuresAlsoReachTheSnapshotUnderTheirOwnNames()
  {
    // The two the status-code Theory above cannot reach: Transport needs the handler to THROW an
    // HttpRequestException, and TooLarge needs a body over MaxPlaybackSeconds × 32 000 B/s. Expect
    // "MediaTransport" and "MediaTooLarge". Timeout is covered by the handler blocking past
    // FetchTimeoutSeconds, with that option set to 1 so the test is quick.
  }

  [Fact]
  public async Task AVoicemailWithDurationZeroReportsANullSnapshotDuration()
  {
    // ADR-029 §4.1: 0 means UNKNOWN, and the UI must render an indeterminate bar rather than a
    // confident lie. The SOURCE still gets an estimate — its completion needs a number — so this also
    // asserts the source's Duration is non-zero while the snapshot's is null. Those two being
    // different is the whole point and would otherwise look like a bug.
  }

  [Fact]
  public async Task AVoicemailWithAReportedDurationUsesItRatherThanTheSizeEstimate()
  {
    // Write a cache file whose size estimates to something else, and assert the snapshot and the
    // source both carry the provider's value.
  }

  [Fact]
  public async Task ASpeechSnapshotReportsPositionZeroForItsWholeLife()
  {
    // ⚠ THE C-27 HONESTY PIN, and it asserts the CURRENT behaviour rather than the desirable one.
    // EventAudioSourceBase.Position defaults to zero and TTSEventSource deliberately does not
    // override it. Nothing in this PR may claim a speech scrubber advances. When PR 5 adds the
    // override, THIS test is what fails, which is how it should be found.
  }

  [Fact]
  public async Task SeekIsRefusedForANonSeekableSource_WithoutThrowing()
  {
    // EventAudioSourceBase.SeekAsync throws NotSupportedException when IsSeekable is false. "This
    // cannot scrub" is an ordinary answer, so the seam pre-checks and returns false instead.
  }

  [Fact]
  public async Task PauseAndResumeAreRefusedFromTheWrongState()
  {
    // EventAudioSourceBase already no-ops these with a warning; the seam must not report success for
    // a no-op — that is the untruth PR 1 refused when it made a non-seekable SeekAsync throw.
  }

  [Fact]
  public async Task NoMixerSourceIsEverAdded()
  {
    // §0.6 — the copy-able mistake. A recording IMasterMixer asserts AddSource is never called;
    // SoundFlowMasterMixer.AddSource does not route audio, and SourcesController's failure to remove
    // it is where its per-play leak comes from. Task 13's grep is the second half of this.
  }

  [Fact]
  public async Task NeitherTheTextNorTheRawMediaIdReachesAnyLogLine()
  {
    // PHN-1b §0.3 ⓸'s rule, extended to Text. Capture every log line at every level across a
    // successful speech playback, a successful voicemail playback and a failed one, and assert
    // neither the utterance nor the raw media id appears in any message or argument — with
    // Assert.NotEmpty on the captured lines so it cannot pass vacuously, which is how PHN-1b's
    // equivalent test was written.
  }

  [Fact]
  public void Dispose_CancelsAnAcquisitionStillInFlight()
  {
    // The third cancellation that actually exists. The container disposes singletons at shutdown.
  }
```

⚠ **Two of the four written-out facts are what make this suite non-vacuous**, and Builder should confirm
each fails against a deliberately broken implementation before moving on:

- `AcquisitionSurvivesCancellationOfTheStartToken` — break it by passing `cancellationToken` instead of
  `playback.Token` to `AcquireAndPlayAsync`. It must then time out rather than reach `Playing`.
- `EveryGvMediaFailureReachesTheSnapshotUnderItsOwnName` — break it by replacing the reason string with a
  constant. It must then fail on four of its five cases.

Everything else in this file could pass against a service that never reached its collaborators.

⚠ **Every asynchronous assertion goes through `NextSnapshotWith` or `WaitUntilAsync`, never a bare
`Task.Delay` before an assertion.** `TEST-4` is the open row about a wall-clock test window racing a
wall-clock loop, and `TEST-1` is the row about not writing the next one. Task 13 item 6 greps for it.

---

### Task 7 — the DI guard, and why PR 3's has a different shape from PR 2's

**File:** `tests/Radio.Infrastructure.Tests/DependencyInjection/EventPlaybackRegistrationTests.cs` (new)

`PHN-1b` Task 11 shipped this repo's first `ValidateOnBuild`/`ValidateScopes` guard and its handoff says
to extend the pattern rather than invent another. PR 3 extends it, with one honest difference.

**The difference, and the design decision it forced.** `AddGvMedia`'s graph is closed — everything it needs
it registers. `AddEventPlayback`'s is not: `ITTSFactory`, `IDuckingService` and `AudioFileEventSourceFactory`
come from `AddSoundFlowAudio`, which initialises real audio hardware and is precisely why
`ActiveSourceAccessorRegistrationTests` can only inspect descriptors. So this guard registers **fakes** for
the three, and validates what `AddEventPlayback` itself contributes.

⚠ **That constraint is what shaped `EventPlaybackService`'s constructor, and the causality is worth
recording because it looks like a coincidence.** The naive implementation constructs
`AudioFileEventSource` directly, which needs `SoundFlowPlaybackService` — a concrete class whose
constructor takes the concrete `SoundFlowAudioEngine` **and starts a background monitor task**. It cannot
be faked, and `PHN-1a` Task 5 already hit this ("if constructible"). Routing through
`AudioFileEventSourceFactory.CreateFromAbsolutePathAsync` (Task 3) instead means **no SoundFlow type appears
in `EventPlaybackService`'s constructor at all** — which is what makes this guard possible, and is a better
design for the ordinary reason too.

```csharp
  private static ServiceProvider BuildProvider(IConfiguration? configuration = null)
  {
    var config = configuration ?? new ConfigurationBuilder().Build();

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddOptions();
    services.AddSingleton<IConfiguration>(config);

    // From AddGvMedia — a real registration, closed graph, already guarded by GvMediaRegistrationTests.
    services.AddGvMedia(config);

    // From AddSoundFlowAudio, which cannot be called here: it initialises real audio hardware, which is
    // why ActiveSourceAccessorRegistrationTests inspects descriptors instead of resolving. These three
    // are what EventPlaybackService needs from it, and faking them is what keeps this a REAL
    // build-and-resolve guard rather than a descriptor check.
    services.AddSingleton<ITTSFactory, FakeTtsFactory>();
    services.AddSingleton<IDuckingService, FakeDuckingService>();
    services.AddSingleton<AudioFileEventSourceFactory>();
    services.Configure<FilePlayerOptions>(_ => { });

    services.AddEventPlayback();

    return services.BuildServiceProvider(new ServiceProviderOptions
    {
      ValidateOnBuild = true,
      ValidateScopes = true
    });
  }

  [Fact]
  public void AddEventPlayback_BuildsAndResolvesTheSeam()
  {
    using var provider = BuildProvider();

    Assert.NotNull(provider.GetRequiredService<IEventPlaybackService>());
  }

  [Fact]
  public void TheInterfaceAndTheConcreteTypeResolveToOneInstance()
  {
    // Two instances would be two Current properties and two single slots — two attended playbacks
    // that each believe they are the only one, on one set of speakers.
    using var provider = BuildProvider();

    Assert.Same(
      provider.GetRequiredService<EventPlaybackService>(),
      (EventPlaybackService)provider.GetRequiredService<IEventPlaybackService>());
  }

  [Fact]
  public void ItResolvesWithNoGvMediaSectionAtAll()
  {
    // What an appliance with no GvMedia block gets. Proves the defaults are sufficient to construct
    // everything, which is the property GvMediaRegistrationTests asserts for its own graph.
    using var provider = BuildProvider();

    Assert.NotNull(provider.GetRequiredService<EventPlaybackService>());
  }
```

⚠ **Note what this guard cannot do, so nobody over-trusts it** — the same honesty `PHN-1b` §0.6 applied to
its own. `AddSingleton<EventPlaybackService>()` is a **constructor** registration, so `ValidateOnBuild`
genuinely introspects it and a missing dependency fails the build of the provider. But
`AddSingleton<IEventPlaybackService>(sp => …)` is a **factory**, which `ValidateOnBuild` cannot introspect —
so the `GetRequiredService` in each fact is what actually exercises it, exactly as `PHN-1b` noted for
`AddHttpClient<T>`. And the fakes mean this proves nothing about `AddSoundFlowAudio` still registering
those three. **Task 9's `CustomWebApplicationFactory` tests are what cover that**, because they build the
real API container.

---

### Task 8 — `EventPlaybackController` and its wire DTO

**File:** `src/Radio.API/Models/EventPlaybackModels.cs` (new)

```csharp
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Models;

/// <summary>
/// The wire shape of POST /api/audio/events.
/// </summary>
/// <remarks>
/// ⚠ A separate type from <see cref="EventPlaybackRequest"/> on purpose, and the reason is not layering
/// hygiene. Every field here is nullable and the two enums arrive as STRINGS, so a body with a missing
/// or unrecognised "kind" is answered with a NAMED rejection reason instead of System.Text.Json's
/// required-member or enum-parse exception, which the model binder turns into a generic 400. Keeping
/// EventPlaybackRequest off the wire also keeps the type whose whole posture is "there is no URL field
/// and there never will be one" free of any deserialisation concern.
///
/// The mapping is a TRANSLATION, not a second rule set: an unrecognised enum name becomes an UNDEFINED
/// enum value, and EventPlaybackRequest.Validate then produces UnknownKind / UnknownMediaKind /
/// ArmMismatch by its own rules. The controller decides nothing.
/// </remarks>
public sealed class EventPlaybackRequestDto
{
  /// <summary>"Speech" or "RemoteMedia".</summary>
  public string? Kind { get; set; }

  /// <summary>Speech arm: the literal utterance, composed by the caller (ADR-029 §4.2).</summary>
  public string? Text { get; set; }

  /// <summary>Speech arm: per-request voice override. Null means TTS:DefaultVoice.</summary>
  public string? VoiceId { get; set; }

  /// <summary>Speech arm: per-request engine override. Null means TTS:DefaultEngine.</summary>
  public string? Engine { get; set; }

  /// <summary>RemoteMedia arm: "GvVoicemail".</summary>
  public string? MediaKind { get; set; }

  /// <summary>RemoteMedia arm: the provider's recording id. ⚠ NEVER a URL.</summary>
  public string? MediaId { get; set; }

  /// <summary>RemoteMedia arm: the provider's duration. 0 means unknown (ADR-022 §4.2).</summary>
  public int? DurationSeconds { get; set; }

  /// <summary>Display label. Presentation only.</summary>
  public string? Label { get; set; }

  /// <summary>Ducking priority 1-10. Null takes EventPlaybackRequest's own default.</summary>
  public int? Priority { get; set; }
}

/// <summary>The wire shape of POST /api/audio/events/{id}/seek.</summary>
public sealed class EventPlaybackSeekDto
{
  /// <summary>Target position from the start of the content, in seconds.</summary>
  public double PositionSeconds { get; set; }
}
```

**File:** `src/Radio.API/Controllers/EventPlaybackController.cs` (new)

⚠ **A new controller, not a method on `AudioController`.** Two reasons, the second load-bearing:
`AudioController` is already ~700 lines with seven constructor parameters; and adding a **required**
constructor dependency to a controller that several tests construct directly would break them, which is
exactly the class of breakage this arc has been guarding against since `PHN-1a` §0.6.

```csharp
using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.External;

namespace Radio.API.Controllers;

/// <summary>
/// Attended event playback — voicemail recordings and spoken messages (ADR-029 §3.3).
/// </summary>
/// <remarks>
/// One mechanism, not two features: both arms share this route family, one lifecycle, one stop path and
/// one state model, differing only in how the audio is acquired.
///
/// ⚠ POST returns 202, not 200. Both arms have an acquisition phase — an HTTP fetch or a TTS synthesis —
/// before any audio exists, so the response describes an ACCEPTED playback in Preparing. Acquisition
/// failures therefore arrive as EventPlaybackState.Failed with a named FailureReason on a later snapshot,
/// NOT as a status code on this response; GET current is how a caller that cares reads them (and from
/// PR 5, /hubs/audio pushes them). The one exception is GvMedia:Enabled being false, which is knowable
/// without touching the network and is answered here as a 409.
/// </remarks>
[ApiController]
[Route("api/audio/events")]
[Produces("application/json")]
public class EventPlaybackController : ControllerBase
{
  private readonly ILogger<EventPlaybackController> _logger;
  private readonly IEventPlaybackService _playback;

  /// <summary>Initializes a new instance of the EventPlaybackController.</summary>
  public EventPlaybackController(
    ILogger<EventPlaybackController> logger, IEventPlaybackService playback)
  {
    _logger = logger;
    _playback = playback;
  }

  /// <summary>Starts an attended playback.</summary>
  [HttpPost]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status202Accepted)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<EventPlaybackSnapshot>> Start(
    [FromBody] EventPlaybackRequestDto dto, CancellationToken cancellationToken)
  {
    var request = Map(dto);

    // The controller validates so a bad request gets a clean 400 without an exception; the seam
    // validates too, for callers that are not this one. Both call Validate — the rules live there and
    // neither re-derives them.
    var rejection = request.Validate(MaxSpeechCharsFor(request));
    if (rejection != EventPlaybackRejection.None)
    {
      // ⚠ The reason NAME only. Echoing MediaId or Text back would put a raw media id, or a private
      // message body, into a response and into whatever logs it (PHN-1b §0.3 ⓸).
      return BadRequest(new { error = "Event playback request refused", reason = rejection.ToString() });
    }

    try
    {
      var snapshot = await _playback.StartAsync(request, cancellationToken);
      return Accepted(snapshot);
    }
    catch (EventPlaybackRejectedException ex)
    {
      // Unreachable while the check above matches, and kept anyway: the seam is the authority, and a
      // controller that silently disagreed with it would be worse than a duplicated 400.
      return BadRequest(new { error = "Event playback request refused", reason = ex.Reason.ToString() });
    }
    catch (GvMediaUnavailableException ex) when (ex.Reason == GvMediaFailure.Disabled)
    {
      return Conflict(new
      {
        error = "Remote media playback is disabled; set GvMedia:Enabled.",
        reason = ex.Reason.ToString()
      });
    }
  }

  /// <summary>The one in-flight attended playback, or 204 when there is none.</summary>
  [HttpGet("current")]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public ActionResult<EventPlaybackSnapshot> GetCurrent()
  {
    var snapshot = _playback.Current;
    return snapshot is null ? NoContent() : Ok(snapshot);
  }

  /// <summary>Stops the playback with this id.</summary>
  [HttpDelete("{id}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Stop(string id, CancellationToken cancellationToken)
    => await _playback.StopAsync(id, cancellationToken)
      ? NoContent()
      : NotFound(new { error = "No such playback", reason = "UnknownPlaybackId" });

  /// <summary>Seeks the playback with this id.</summary>
  [HttpPost("{id}/seek")]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<EventPlaybackSnapshot>> Seek(
    string id, [FromBody] EventPlaybackSeekDto dto, CancellationToken cancellationToken)
  {
    if (dto.PositionSeconds < 0 || double.IsNaN(dto.PositionSeconds) || double.IsInfinity(dto.PositionSeconds))
    {
      return BadRequest(new { error = "positionSeconds must be a finite, non-negative number", reason = "BadPosition" });
    }

    var moved = await _playback.SeekAsync(
      id, TimeSpan.FromSeconds(dto.PositionSeconds), cancellationToken);
    return Transport(id, moved, "NotSeekable");
  }

  /// <summary>Pauses the playback with this id.</summary>
  [HttpPost("{id}/pause")]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<EventPlaybackSnapshot>> Pause(string id, CancellationToken cancellationToken)
    => Transport(id, await _playback.PauseAsync(id, cancellationToken), "NotPlaying");

  /// <summary>Resumes the playback with this id.</summary>
  [HttpPost("{id}/resume")]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<EventPlaybackSnapshot>> Resume(string id, CancellationToken cancellationToken)
    => Transport(id, await _playback.ResumeAsync(id, cancellationToken), "NotPaused");

  /// <summary>
  /// Turns a transport method's single bool into 200 / 404 / 409.
  /// </summary>
  /// <remarks>
  /// The seam returns one bool for two different situations — no such playback, and a playback that
  /// cannot do this — because IEventPlaybackService says so. Current is what separates them, and it is a
  /// shipped seam member rather than a widening of the contract.
  ///
  /// ⚠ Honest about the race, because a reviewer will find it: Current is read AFTER the call, so a
  /// playback replaced in between reports 404 rather than 409. Both are refusals, the window is
  /// microseconds, and there is one user in front of one console — the alternative is a lock across an
  /// HTTP handler, which is worse than the imprecision it would buy.
  /// </remarks>
  private ActionResult<EventPlaybackSnapshot> Transport(string id, bool succeeded, string refusalReason)
  {
    var current = _playback.Current;
    if (succeeded)
    {
      return current is null ? NoContent() : Ok(current);
    }
    return current is null || current.Id != id
      ? NotFound(new { error = "No such playback", reason = "UnknownPlaybackId" })
      : Conflict(new { error = "The playback cannot do that right now", reason = refusalReason });
  }

  /// <summary>
  /// Translates the wire shape into the Core request. Decides nothing.
  /// </summary>
  /// <remarks>
  /// An absent or unrecognised enum name becomes an UNDEFINED enum value rather than a controller-side
  /// error, so Validate reports it under its own rules — UnknownKind, UnknownMediaKind on the
  /// RemoteMedia arm, and ArmMismatch on the Speech arm, which is where an unparseable mediaKind on a
  /// speech request genuinely belongs.
  ///
  /// Priority is applied with `with` only when the caller sent one, so the default stays a single
  /// constant on EventPlaybackRequest rather than being repeated here.
  /// </remarks>
  private static EventPlaybackRequest Map(EventPlaybackRequestDto dto)
  {
    const int Undefined = -1;

    var kind = Enum.TryParse<EventPlaybackKind>(dto.Kind, ignoreCase: true, out var k)
      && Enum.IsDefined(k) ? k : (EventPlaybackKind)Undefined;

    RemoteMediaKind? mediaKind = dto.MediaKind is null
      ? null
      : Enum.TryParse<RemoteMediaKind>(dto.MediaKind, ignoreCase: true, out var mk) && Enum.IsDefined(mk)
        ? mk
        : (RemoteMediaKind)Undefined;

    var request = new EventPlaybackRequest
    {
      Kind = kind,
      Text = dto.Text,
      VoiceId = dto.VoiceId,
      Engine = dto.Engine,
      MediaKind = mediaKind,
      MediaId = dto.MediaId,
      DurationSeconds = dto.DurationSeconds,
      Label = dto.Label
    };

    return dto.Priority is int priority ? request with { Priority = priority } : request;
  }

  private static int MaxSpeechCharsFor(EventPlaybackRequest request) => GvMediaOptionsSnapshot.MaxSpeechChars;
}
```

⚠ **`MaxSpeechCharsFor` above is the one place this plan hands Builder a hole rather than code, and it
must be closed rather than worked around.** The controller needs `GvMedia:MaxSpeechChars` to call
`Validate`. Inject `IOptionsMonitor<GvMediaOptions>` into the controller and use
`_gvMediaOptions.CurrentValue.MaxSpeechChars`; delete the placeholder method and the
`GvMediaOptionsSnapshot` name, which does not exist. **Do not** hardcode `1000`, and **do not** call
`Validate()` with its parameterless default — the default exists so tests need no configuration object,
and using it in the controller would mean a change to `GvMedia:MaxSpeechChars` silently did nothing at
the only place a user can reach it. Adding the dependency is safe: this controller is new, so nothing
constructs it directly, and Task 9's tests resolve it from the real container.

---

### Task 9 — the route tests, built so they can fail

**File:** `tests/Radio.API.Tests/Controllers/EventPlaybackControllerTests.cs` (new)

⚠ **Read this before writing them.** `AddHermeticTestRig` fails every outbound HTTP call by design, and
`ENC-8` shipped a page that could not deserialize its own API response because a null result was the
expected state. This project's own instance is next door:
`NotificationsControllerTests.Announce_WithValidMessage_ReturnsOk` asserts success against a host where
TTS cannot work, and passes because `AnnounceAsync` swallows every exception — **a green test there proves
the route is mapped and nothing else.**

So these tests are built around one property: **each asserts an outcome that a dead or half-wired surface
could not produce.** A `404` from an unmapped route, a generic model-binding `400`, or a `500` all fail
them.

```csharp
public class EventPlaybackControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;

  public EventPlaybackControllerTests(CustomWebApplicationFactory<Program> factory) => _factory = factory;

  [Fact]
  public async Task Post_RemoteMedia_Returns409_WhenGvMediaIsDisabled()
  {
    // GvMedia:Enabled ships false. A 409 with reason "Disabled" can only come from the controller's
    // own catch — an unmapped route gives 404, a broken DTO gives a generic 400, an unresolvable
    // dependency gives 500. It also proves the whole Radio.API container still builds with
    // AddEventPlayback in it, which closes PHN-1b §2.2 item 1.
    var client = _factory.CreateClient();

    var response = await client.PostAsJsonAsync("/api/audio/events", new
    {
      kind = "RemoteMedia", mediaKind = "GvVoicemail", mediaId = "vm-abc123", durationSeconds = 12
    });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("Disabled", body.GetProperty("reason").GetString());
  }

  [Theory]
  [InlineData("https://evil.example/payload.mp3", "MediaIdLooksLikeUrl")]
  [InlineData("http:evil.example", "MediaIdHasIllegalCharacter")]
  [InlineData("../../etc/shadow", "MediaIdHasPathSeparator")]
  public async Task Post_AUrlBearingMediaId_Returns400_WithTheNamedReason(string mediaId, string reason)
  {
    // The SSRF pin, at the wire rather than in a unit test. "http:evil.example" is the case PR 1's
    // review found: RFC 3986 §4.2 resolves a scheme-bearing relative reference as ABSOLUTE, so a
    // deny-list passes it. This asserts the allow-list is what the ROUTE reaches.
  }

  [Fact]
  public async Task Post_AVoiceIdCarryingASpace_Returns400_VoiceIdHasIllegalCharacter()
  {
    // §0.4 C-26 at the wire. "en -w /tmp/pwned.wav" must never reach TTSFactory's argument line.
  }

  [Fact]
  public async Task Post_AnOverlongLabel_Returns400_LabelTooLong()
  {
    // PHN-1b §0.3 ⓷ — proves Task 1's cap is reachable from the route, which is the whole reason it
    // was deferred from PR 2 rather than shipped there as unreachable code.
  }

  [Fact]
  public async Task Post_ABodyWithNoKind_Returns400_UnknownKind_NotAModelBindingError()
  {
    // The DTO's whole justification. Binding EventPlaybackRequest directly would make this a generic
    // required-member 400 with no named reason.
    var response = await _factory.CreateClient().PostAsJsonAsync("/api/audio/events", new { text = "hi" });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("UnknownKind", body.GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Post_ASpeechRequestCarryingAnUnparseableMediaKind_Returns400_ArmMismatch()
  {
    // Pins that Map translates rather than decides: an unrecognised mediaKind becomes an undefined
    // enum value, and Validate reports ArmMismatch because this is the Speech arm — which is the
    // correct reason, and not the one a controller-side parse error would have given.
  }

  [Fact]
  public async Task Get_Current_Returns204_WhenNothingIsPlaying()
  {
    var response = await _factory.CreateClient().GetAsync("/api/audio/events/current");

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  [Theory]
  [InlineData("DELETE", "/api/audio/events/evp-nope")]
  [InlineData("POST", "/api/audio/events/evp-nope/pause")]
  [InlineData("POST", "/api/audio/events/evp-nope/resume")]
  public async Task TransportOnAnUnknownPlaybackId_Returns404(string method, string path) { /* … */ }

  [Fact]
  public async Task Post_RemoteMedia_ReachesTheClientAndFailsWithANamedMediaReason()
  {
    // ⚠ THE TEST THAT PROVES THE SURFACE IS ALIVE, and the only one here that exercises the whole
    // chain: route → DTO → Validate → StartAsync → 202 → background acquisition → GvMediaClient →
    // the auth handler → the failure taxonomy → a Failed snapshot → GET current.
    //
    // GvMedia:Enabled is turned ON and BaseUrl is pointed at 127.0.0.1:1, where a connection is
    // refused immediately on every platform — no real network, no GV auth clock, no timeout to wait
    // out. The expected end state is Failed with "MediaTransport".
    //
    // What would break it, i.e. what it can actually catch: a 202 that never starts acquisition, an
    // acquisition wired to the request's cancellation token (§0.4 C-21 — it would report Stopped, not
    // Failed), a taxonomy that collapsed the reasons, a snapshot never published, or Current never
    // storing it.
    var client = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
      c.AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GvMedia:Enabled"] = "true",
        ["GvMedia:BaseUrl"] = "http://127.0.0.1:1",
        ["GvMedia:FetchTimeoutSeconds"] = "3"
      }))).CreateClient();

    var accepted = await client.PostAsJsonAsync("/api/audio/events", new
    {
      kind = "RemoteMedia", mediaKind = "GvVoicemail", mediaId = "vm-abc123",
      durationSeconds = 12, label = "Voicemail from Jane"
    });

    Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    var start = await accepted.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("Preparing", start.GetProperty("state").GetString());
    var id = start.GetProperty("id").GetString();
    Assert.StartsWith("evp-", id);
    Assert.Equal("Voicemail from Jane", start.GetProperty("label").GetString());

    var final = await PollUntilTerminalAsync(client, TimeSpan.FromSeconds(15));

    Assert.Equal("Failed", final.GetProperty("state").GetString());
    Assert.Equal("MediaTransport", final.GetProperty("failureReason").GetString());
    Assert.Equal(id, final.GetProperty("id").GetString());
  }
}
```

⚠ **`PollUntilTerminalAsync` polls `GET /api/audio/events/current` until `state` is not `Preparing`, or
the deadline passes — and it FAILS the test on the deadline rather than returning the last snapshot.** A
helper that returns whatever it last saw would let this pass against a service that never left `Preparing`,
which is the exact failure mode it exists to catch.

⚠ **`_factory.CreateClient()` and `_factory.WithWebHostBuilder(...)` build different hosts**, so the
`GvMedia:Enabled=true` test does not leak into the others. That is also why the enabled test asserts on
its own playback's id.

⚠ **Do not add a test that asserts a playback reaches `Completed`.** Nothing in the test host produces
audio — `AddSoundFlowAudio`'s hardware initialisation lives in hosted services that
`CustomWebApplicationFactory` deliberately removes — so a `Completed` assertion would either fail or, worse,
pass through `AudioFileEventSource`'s silent `PlaybackLoopAsync` fallback, which reports a clean completion
having produced no sound at all. **That a voicemail is audible is a box check, and it is PR 6's** (§2.2).

---

### Task 10 — `IsPermanent` stops claiming something the upstream disproves

**File:** `src/Radio.Infrastructure/External/GvMediaUnavailableException.cs` (edit)

§0.4 C-22. Two edits, both doc-heavy because the *reason* is the whole value.

```csharp
  /// <summary>
  /// True when retrying the same request cannot succeed.
  /// </summary>
  /// <remarks>
  /// ⚠ NotFound was removed from this set by PR 3, and the reason is a property of the upstream rather
  /// than of this class. RotaryPhone's GvVoicemailController.GetAudio resolves a recording through
  /// FindNodeAsync, which calls GvVoicemailClient.ListVoicemailsAsync and — unlike the sibling GetList,
  /// which guards it explicitly — does NOT check the result's Succeeded flag. A failed authenticated
  /// list returns GvVoicemailListResult.Empty(succeeded: false), an EMPTY item list, so FirstOrDefault
  /// yields null and the route answers 404 "has no recording".
  ///
  /// That failure is exactly the Google Voice auth blackout — roughly 9 minutes in every 20 (XR-3) — so
  /// a 404 from this upstream means "gone" OR "try again in a few minutes", and nothing in the response
  /// distinguishes them. Reporting it as permanent would tell a user a voicemail no longer exists
  /// roughly 45% of the times it is transient, which is the GV-6 / GV-8 failure class the GvMediaFailure
  /// enum was built to prevent, arriving through a different door.
  ///
  /// The distinction is NOT collapsed: NotFound keeps its own name and reaches the snapshot as
  /// "MediaNotFound", distinct from "MediaUpstream". What it no longer carries is a claim about
  /// retrying that this side cannot support.
  ///
  /// Disabled is the only reason that is permanent by construction on OUR side — retrying with the
  /// feature off cannot succeed, and no clock changes that.
  /// </remarks>
  public bool IsPermanent => Reason is GvMediaFailure.Disabled;
```

and, on the enum member, replace *"the recording does not exist. Retrying will not help."*:

```csharp
  /// <summary>
  /// The provider returned 404. ⚠ This does NOT mean the recording is gone. RotaryPhone's audio route
  /// answers 404 both when a recording genuinely has no media and when its authenticated voicemail list
  /// failed — which is what a Google Voice auth blackout looks like from here. See
  /// GvMediaUnavailableException.IsPermanent for the code path. Treat as retryable.
  /// </summary>
  NotFound,
```

**Test** — append to `tests/Radio.Infrastructure.Tests/External/GvMediaClientTests.cs`:

```csharp
  [Theory]
  [InlineData(GvMediaFailure.Disabled, true)]
  [InlineData(GvMediaFailure.NotFound, false)]
  [InlineData(GvMediaFailure.Unauthorized, false)]
  [InlineData(GvMediaFailure.Upstream, false)]
  [InlineData(GvMediaFailure.Timeout, false)]
  [InlineData(GvMediaFailure.Transport, false)]
  [InlineData(GvMediaFailure.TooLarge, false)]
  public void IsPermanentIsTrueOnlyForDisabled(GvMediaFailure reason, bool expected)
  {
    // ⚠ NotFound is false on purpose and this is the assertion that says so. RotaryPhone's
    // GvVoicemailController.GetAudio answers 404 when its voicemail LIST call fails, which is the GV
    // auth blackout — so a 404 here is ambiguous between "gone" and "try again", and this side cannot
    // tell. If RotaryPhone ever propagates Succeeded so the route answers 502 during a blackout, THIS
    // test is what should change, deliberately, alongside the doc that explains it.
    Assert.Equal(expected, new GvMediaUnavailableException(reason, "masked").IsPermanent);
  }
```

⚠ **Do not "fix" this by changing the mapping so a 404 becomes `Upstream`.** That would collapse the
distinction `GV-6` and `GV-8` are open rows about, and it would be wrong for the genuinely-permanent case
(`node.MediaId is null` — a voicemail with no recording, which really is 404 forever). The name stays; only
the retryability claim goes.

---

### Task 11 — correct the `GvMediaCache` remark this PR makes false

**File:** `src/Radio.Infrastructure/External/GvMediaCache.cs` (edit — one sentence, no behaviour)

§0.4 C-21. `WriteAsync`'s remarks currently contain:

> `It is self-preserving, and PR 3 passes HttpContext.RequestAborted, so a kiosk reload is enough to create one.`

Replace **that sentence only** with:

```
/// It is self-preserving, so a single poisoned entry outlives everything around it. The cancellation
/// that actually reaches this method comes from EventPlaybackService, not from the request: a stop
/// during Preparing, a second StartAsync replacing the first, and container disposal at shutdown all
/// cancel a fetch mid-write. (An earlier revision of this remark said PR 3 would pass
/// HttpContext.RequestAborted; it deliberately does not — acquisition outlives the 202 response, and
/// binding it to a request-scoped token would cancel every fetch the instant it was accepted.)
```

**Assert the replacement**, per the `PHN-1b` Task 12 precedent — this must print `1` before and `0` after:

```bash
grep -c "PR 3 passes HttpContext.RequestAborted" src/Radio.Infrastructure/External/GvMediaCache.cs
```

and this must print `1` after:

```bash
grep -c "acquisition outlives the 202 response" src/Radio.Infrastructure/External/GvMediaCache.cs
```

⚠ **Nothing else in this file changes.** The staging write, the `File.Move`, both reclaimers and every
other remark stay exactly as `PHN-1b` shipped them. This is a doc correction to a sentence that becomes
false the day PR 3 lands, and leaving it would be the sixth instance of the failure class
`CLAUDE.md` § Pre-Merge Review enumerates.

---

### Task 12 — docs

**12a. `design/FUTURE-WORK.md`** — a new numbered section recording the two defects PR 3 found and did not
fix, in the shape §14 and §15 already use (What Exists / What's Needed / Gotchas / Priority). Both are
**live paths with their own callers**, which is why neither is smuggled into a planning PR.

- **The eSpeak argument-injection surface (§0.4 C-26).** `TTSFactory.GenerateESpeakAsync` interpolates
  `voice` into `$"-v {voice} -s … --stdout"`; `SourcesController.PlayTTSEvent` reaches it from an
  unauthenticated body with `Engine = engine ?? TTSEngine.ESpeak`, so the espeak branch is the default.
  Record the reproduction verbatim, the precise class (**argument** injection, not command injection — no
  shell), the impact (`espeak-ng -w <path>` is an arbitrary file write as `mmack`, who owns
  `/opt/radio-console`), and the two candidate fixes: validate in `TTSFactory` for every caller, or move to
  `ArgumentList` so the runtime never re-parses a single string. ⚠ **Mark it the highest-priority item in
  the file and say why it is not fixed here:** `SourcesController` is one of the two ad-hoc event paths
  every document in this arc says not to touch, and ADR §14 Q6 puts its retirement explicitly outside this
  arc.
- **`TTSParameters` pins all four fields, and `AvailableEngines` is cached for the process lifetime**
  (§0.4 C-25, C-31). One entry, because they are one refactor: make `Engine`/`Voice`/`Speed`/`Pitch`
  nullable so "unset" is expressible, which fixes `SourcesController:623` at the same time; and invalidate
  `_cachedEngines` on an `IOptionsMonitor<TTSSecrets>` change, which is ADR §14 Q10's other acceptable
  answer plus §9.4 defect (a).

**12b. `design/INTEGRATIONS.md`** — extend the `GvMedia` runbook `PHN-1b` added, with:

- **The route family**, its statuses, and that `POST` returns **202** — an acquisition failure is a later
  snapshot, not this response.
- **`FailureReason` as the diagnosis table**: what each of the seven names means and what to do. In
  particular `MediaUnauthorized` = *"`GvMedia:AuthKey` and RotaryPhone's `InterServiceAuthKey` differ; there
  are TWO `appsettings.Production.json` files to edit, one under `api/` and one under `web/`, and neither is
  re-seeded by the deploy"* — which is the operator-facing half of §0.4 C-33.
- ⚠ **`MediaNotFound` does not mean the recording is gone** (§0.4 C-22), with the one-line reason.
- The pre-enable check: RotaryPhone's gate ships default-off, so `GvMedia:AuthKey` may stay empty — but
  **verify before flipping `GvMedia:Enabled`**, because a mismatch surfaces only as `MediaUnauthorized`.

⚠ **Do NOT touch `design/INTEGRATIONS.md:566`'s ducking correction.** It says higher-priority announcements
do not interrupt lower ones *today*, which is still true — **PR 4** is what makes it false and must update
it in the same PR.

---

### Task 13 — build, test, and the scope gate

```bash
dotnet build --configuration Release        # 0 warnings; Release treats them as errors
dotnet test  --configuration Release --verbosity normal
```

Then assert each of these, and **paste the output** rather than asserting from memory:

1. **No mixer bookkeeping.** Must return **0**:
   ```bash
   grep -rn "AddSource\|RemoveSource" src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs src/Radio.API/Controllers/EventPlaybackController.cs
   ```
2. **No config change.** Must be empty:
   ```bash
   git diff --name-only origin/main -- '*appsettings*.json' 'deploy/'
   ```
3. **No `Radio.Web` change.** Must be empty:
   ```bash
   git diff --name-only origin/main -- src/Radio.Web/
   ```
4. **The ducking tripwire is untouched and green.**
   ```bash
   git diff --name-only origin/main -- tests/Radio.Infrastructure.Tests/Audio/DuckingServiceCharacterizationTests.cs   # empty
   dotnet test --filter "FullyQualifiedName~DuckingServiceCharacterizationTests"                                       # 4 passed
   ```
5. **No `SourcesController`, `TTSFactory`, `TTSEventSource` or `FilePlayerAudioSource` change.** Must be empty:
   ```bash
   git diff --name-only origin/main -- src/Radio.API/Controllers/SourcesController.cs src/Radio.Infrastructure/Audio/Services/TTSFactory.cs src/Radio.Infrastructure/Audio/Sources/Events/TTSEventSource.cs src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs
   ```
6. **`Task.Delay` is not used as a test synchroniser** in the new test files (`TEST-1`, `TEST-4`):
   ```bash
   grep -rn "Task.Delay" tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs tests/Radio.API.Tests/Controllers/EventPlaybackControllerTests.cs
   ```
   A hit is allowed only inside a *fake collaborator* simulating work — never as `await Task.Delay(…)`
   before an assertion. Read every hit and say which it is.
7. **The two doc corrections landed.** Task 11's two `grep -c` commands, both values.
8. **The existing Core tests were not edited:**
   ```bash
   git diff origin/main -- tests/Radio.Core.Tests/EventPlaybackRequestTests.cs | grep -c "^-[^-]"   # must be 0
   ```
9. **The API host still starts outside the test rig:**
   ```bash
   dotnet run --project src/Radio.API      # must reach "Now listening on"; Ctrl-C
   ```
   Task 9's tests build the same container, but this is the one that matches what systemd does.

---

## 2. Test Plan

### 2.1 What the automated tests actually prove

| Claim | Proved by |
|---|---|
| A `Label` over 128 chars is refused, on both arms | `Validate_RejectsALabelOverTheCap`, `Validate_CapsTheLabelOnBothArms` |
| A `VoiceId` that could inject an espeak argument is refused | `Validate_RejectsAVoiceIdThatCouldInjectAnEspeakArgument` (4 cases), and at the route |
| Every voice id this system really uses is still accepted | `Validate_AcceptsTheVoiceIdsThisSystemActuallyUses` (6 cases) |
| The mbrola limitation is a stated assumption, not an accident | `Validate_RejectsAnMbrolaStyleVoiceId_WhichIsTheDeclaredAssumption` |
| An unknown engine is refused rather than silently resolved to ESpeak | `Validate_RejectsAnUnknownEngineRatherThanFallingBackToESpeak` |
| The rejection enum's numbering did not shift | `EventPlaybackRejection_KeepsTheNumericValuesShippedBeforeThisPr` |
| A cached recording is not re-rooted against the music directory | `ItDoesNotResolveAgainstFilePlayerRootDirectory` |
| The provider's duration wins over the size estimate | `ItHonoursTheAuthoritativeDurationRatherThanEstimating` |
| Duration 0 falls back to the factory's own estimator, not a second constant | `ANullDurationFallsBackToTheSameEstimateCreateFromFileAsyncWouldUse` |
| **Acquisition is not bound to the request's cancellation token** | `AcquisitionSurvivesCancellationOfTheStartToken` — the C-21 pin |
| The cancellation that does exist works | `StopAsync_CancelsAnAcquisitionStillInFlight`, `Dispose_CancelsAnAcquisitionStillInFlight` |
| A playback ends exactly once however many completions its source raises | `ATerminalTransitionHappensExactlyOnce_…` |
| One attended playback at a time, and the replaced one is torn down | `ASecondStartReplacesTheFirst_AndTheFirstIsTornDown` |
| A late completion cannot clear the playback that replaced it | `ALateCompletionFromAReplacedPlaybackDoesNotClearTheCurrentOne` |
| **All four `TTSParameters` fields come from configuration, not the type's defaults** | `SpeechFillsAllFourTtsParametersFromConfiguration` — the C-25 pin |
| Engine resolution matches `TTSFactory.ParseEngine` exactly | `ResolveEngine_MatchesTTSFactoryParseEngine` (3 cases) |
| Synthesis is bounded, so `Preparing` cannot last forever | `SynthesisIsBoundedByGenerationTimeoutSeconds` |
| **Every `GvMediaFailure` reaches the snapshot under its own name** | `EveryGvMediaFailureReachesTheSnapshotUnderItsOwnName` (6 cases) |
| `Disabled` is refused synchronously and leaves no state | `StartAsync_ThrowsGvMediaUnavailableDisabled_WithoutMintingAPlayback` |
| Duration 0 reports a null snapshot duration while the source still has a number | `AVoicemailWithDurationZeroReportsANullSnapshotDuration` |
| A speech snapshot's position is zero — asserted, not assumed | `ASpeechSnapshotReportsPositionZeroForItsWholeLife` — the C-27 honesty pin |
| Transport is refused rather than falsely reported for the wrong state | `SeekIsRefusedForANonSeekableSource_WithoutThrowing`, `PauseAndResumeAreRefusedFromTheWrongState` |
| No mixer source is ever added | `NoMixerSourceIsEverAdded` + Task 13 grep |
| Neither the utterance nor the raw media id reaches a log line | `NeitherTheTextNorTheRawMediaIdReachesAnyLogLine`, with `Assert.NotEmpty` |
| `AddEventPlayback` builds and resolves under `ValidateOnBuild` + `ValidateScopes` | `EventPlaybackRegistrationTests`, 3 facts |
| The interface and the concrete type are one instance | `TheInterfaceAndTheConcreteTypeResolveToOneInstance` |
| **The whole Radio.API container still builds with the new registration** | every `EventPlaybackControllerTests` fact — closes `PHN-1b` §2.2 item 1 |
| The routes exist and answer with named reasons rather than binding errors | `Post_ABodyWithNoKind_Returns400_UnknownKind_NotAModelBindingError` and siblings |
| **The whole chain is alive: route → validate → 202 → background fetch → taxonomy → snapshot** | `Post_RemoteMedia_ReachesTheClientAndFailsWithANamedMediaReason` |
| `IsPermanent` no longer claims a 404 is permanent | `IsPermanentIsTrueOnlyForDisabled` (7 cases) |
| Nothing else regressed | the full suite, ~1,700 tests |

### 2.2 What the tests cannot prove

PR 3 ships **no user-visible surface** — no Blazor component changes, so there is no browser UAT and no
screenshot. That is not the same as verified. The list below is deliberately split by *what it actually
needs*, because `PHN-1a` carried three device-only checks to PR 6 and being vague about which is which is
how they get claimed early.

**Needs the running app, not a device — Builder does these and reports the output:**

1. **That `radio-api` starts outside the test rig.** Task 13 item 9. `CustomWebApplicationFactory` removes
   every `IHostedService`, so it is a weaker container than systemd's; `dotnet run` is the one that matches.
2. **That the `Preparing → Failed` path produces exactly one log line at Warning and none carrying the
   media id.** Run the `127.0.0.1:1` request against a locally-run API and read the console. This is thirty
   seconds and it is the only runtime output the RemoteMedia arm produces without a real bridge.

**Genuinely needs the box or the live gvbridge — carried to PR 6, not claimed here:**

3. **That a fetched voicemail is actually audible, ducks the radio, follows mute and master volume, and
   goes to the Cast device when Cast is active.** This is the row the arc exists for and it is
   unambiguously PR 6's UAT. ⚠ PR 3 must not claim any part of it: nothing in a test host produces sound,
   and `AudioFileEventSource` has a **silent** `PlaybackLoopAsync` fallback that reports a clean completion
   having produced no audio, so a green "it completed" is the *least* trustworthy possible evidence here.
4. **That `./data/gvmedia` is writable under the service account.** `PHN-1b` §2.2 item 4 carried this to
   PR 3 on the assumption PR 3 would do a first fetch on the box. It cannot: `GvMedia:Enabled` ships
   `false` and PR 3 does not flip it, so the first real fetch is still PR 6's. **Re-carried to PR 6**,
   stated rather than silently dropped. (`radio-api` runs as `mmack` and `/opt/radio-console` is owned by
   that user, so it should hold.)
5. **The three device-only checks from `PHN-1a`** — that `SoundPlayerBase.Seek` repositions a short local
   MP3, that `Time` advances, and that pausing a TTS source no longer reports completion. **Still PR 6's**,
   unchanged. PR 3 now supplies the commands for them, which is what `PHN-1a` §2.2 was waiting for:

   ```bash
   ID=$(curl -s -X POST http://radio:5000/api/audio/events -H 'Content-Type: application/json' \
     -d '{"kind":"RemoteMedia","mediaKind":"GvVoicemail","mediaId":"<id>","durationSeconds":<n>}' \
     | python3 -c 'import sys,json;print(json.load(sys.stdin)["id"])')
   curl -s http://radio:5000/api/audio/events/current      # position must advance between calls
   curl -s -X POST http://radio:5000/api/audio/events/$ID/seek -H 'Content-Type: application/json' \
     -d '{"positionSeconds":20}'                           # then re-read current: the anchor must move
   curl -s -X POST http://radio:5000/api/audio/events/$ID/pause
   curl -s -X DELETE -o /dev/null -w '%{http_code}\n' http://radio:5000/api/audio/events/$ID   # 204
   ```

**Two things `PHN-1b` §2.2 item 3 carried here that are now CLOSED, by reading source rather than by
guessing** — see §0.4 C-22 for the evidence:

6. ✅ **The gvbridge route's status codes and `Content-Length`.** `200` (`PhysicalFileResult`,
   `audio/mpeg`, `Content-Length` present on the whole-body GET this client issues), `404`, `502`, and
   `401` once RotaryPhone's gate is configured. Settled from
   `D:\prj\RotaryPhone\src\RotaryPhoneController.GVBridge\Api\GvVoicemailController.cs` and
   `…\RotaryPhoneController.Server\Middleware\GvBridgeAuthMiddleware.cs`. **No live call was made and none
   is needed for this question.**
7. ✅ **What a blackout looks like from here.** A `404`, not a `502` — which is why Task 10 exists.

⚠ **The one thing still worth checking against the live bridge, and only before `GvMedia:Enabled` is
flipped:** whether RotaryPhone's `InterServiceAuthKey` is set on the appliance. It ships default-off, and
while it is off `GvMedia:AuthKey` may stay empty. If it has been set, **every** fetch returns `401` and
surfaces as `MediaUnauthorized` until the two match — across **two** `appsettings.Production.json` files
that the deploy does not re-seed. This is a PR 6 pre-flight, not a PR 3 gate.

⚠ **If anyone does exercise this against the live bridge, record the wall-clock time.** GV auth is dead ~9
minutes in every 20 on a cycle independent of the tester, and their `/api/gvbridge/status` reports healthy
during the blackout — so an untimed result is noise, and now that C-22 is known, **a `404` is as likely to
be the clock as a bad id**.

### 2.3 Commands

```bash
dotnet build --configuration Release
dotnet test  --configuration Release --verbosity normal
dotnet test --filter "FullyQualifiedName~EventPlaybackRequestTests"
dotnet test --filter "FullyQualifiedName~AudioFileEventSourceFactoryAbsolutePathTests"
dotnet test --filter "FullyQualifiedName~EventPlaybackServiceTests"
dotnet test --filter "FullyQualifiedName~EventPlaybackRegistrationTests"
dotnet test --filter "FullyQualifiedName~EventPlaybackControllerTests"
dotnet test --filter "FullyQualifiedName~GvMediaClientTests"
dotnet test --filter "FullyQualifiedName~DuckingServiceCharacterizationTests"   # 4, untouched
dotnet run  --project src/Radio.API                                            # "Now listening on"
```

---

## 3. Self-review

**Spec coverage against ADR-029's PR 3 scope (D1, D2, D3, §14 Q10):**

| ADR item | Task | Note |
|---|---|---|
| D1 — implement `IEventPlaybackService` | 4 | beside `IAnnouncementService`, which is untouched |
| D1/§3.3 — the six routes | 8 | all of them; C-30 explains why not just `POST` |
| D2 — `Validate` is the one rule set | 1, 8 | the controller translates the wire and decides nothing |
| D2 — never a caller-supplied URL | 1, 9 | the allow-list is asserted **at the route**, including `http:evil.example` |
| D3/§5.2 — materialise, then play as an ordinary file event | 3, 4 | via the **path** constructor, per §5.2 |
| D3 — prefer the path constructor so seek is implementable | 3 | the stream arm is never used |
| §3.3 — the two-id hazard | 4, §0.7 | a third id space, and the other two are never touched |
| §4.1 — `DurationSeconds` passthrough as a correctness fix | 3, 4 | source gets a number, snapshot gets null when unknown |
| §4.2 — composition belongs to `Radio.Web` | §0.3 ⓵ | no truncation, no normalisation, no `GvSpeechText` twin |
| §8.2 — the snapshot is an anchor, never a tick | 4 | `PlaybackChanged` on transitions only; nothing periodic |
| §9.2/§9.3 — resolve the engine explicitly | 4 | and all four fields, per C-25 |
| §9.4 — an unavailable engine fails with a stated reason, never a substitution | 4 | `SpeechSynthesisFailed`; `Validate` refuses an unknown engine outright |
| **§14 Q10 — which gate the pre-flight uses** | §0.4 C-31 | **answered: dropped.** Synthesis is the only gate |
| §14 Q4 — a seek must re-arm completion | — | already shipped in `PHN-1a`; PR 3 consumes it |
| `PHN-1b` §0.3 ⓷ — `Label` cap in `Validate` | 1, 2 | |
| `PHN-1a`/`1b` §5 — the DI-guard obligation | 7, 9 | two layers, one scoped and one whole-container |

**Placeholders — stated exactly, because "no placeholders" is a claim this plan can be checked against.**

**Production code: none.** Tasks 1, 3, 4, 5, 8, 10 and 11 are complete literal code, with **one declared
hole**: `MaxSpeechCharsFor` / `GvMediaOptionsSnapshot` in Task 8 names a type that does not exist. Task 8
says exactly what to replace it with (`IOptionsMonitor<GvMediaOptions>` on the controller), why the two
tempting shortcuts are wrong, and why adding the dependency is safe on a brand-new controller. It is
deliberately left as an obvious compile error rather than a plausible-looking line, so it cannot be
skipped.

**Test code: partly literal, and the split is by whether the shape carries information.** Written out in
full: all of Tasks 2, 3 and 7 and 10; Task 6's three fakes, its three helpers, and the four facts whose
mechanics are load-bearing (`StartAsync_ReturnsPreparing…`, `AcquisitionSurvivesCancellationOfTheStartToken`,
`ATerminalTransitionHappensExactlyOnce…`, `EveryGvMediaFailureReachesTheSnapshotUnderItsOwnName`); and in
Task 9, the three whose exact assertions matter most. **Specified assertion-by-assertion but not written
out:** Task 6's remaining thirteen facts and Task 9's remaining six. Each names its subject, its setup and
what it must assert, and each follows the written four mechanically over the same fixtures — there is no
"similar to Task N", no unstated assertion, and nothing whose *shape* is in question. Writing them out
would add roughly six hundred lines of identical plumbing and would not remove an ambiguity. **If Builder
finds one whose intent is not clear from its name plus its comment, that is a defect in this plan — say so
rather than guessing.**

**Three values Builder must confirm rather than trust**, each with what to do if the check fails: the
numeric `14` for `MediaIdHasIllegalCharacter` (Task 2 — count the members), the
`AddGvMedia(builder.Configuration)` site in `Program.cs` (Task 5 — grep it), and whether a
`StaticOptionsMonitor` test helper already exists (Task 3 — grep before writing one). Per C-19, no line
number anywhere in this plan is load-bearing.

**Type consistency.** `EventPlaybackRequest.DurationSeconds` is `int?` and crosses to `TimeSpan` only
through `TimeSpan.FromSeconds`, guarded by `is > 0` so `0` and `null` both become a null snapshot
`Duration`. `EventPlaybackSnapshot.Duration` is `TimeSpan?`; `IEventAudioSource.Duration` stays
non-nullable `TimeSpan` (`PHN-1a` C-11). `EventPlaybackSeekDto.PositionSeconds` is `double` and is
range-checked for NaN/∞ before `TimeSpan.FromSeconds`, which would otherwise throw. `TTSEngine` is parsed
by name, case-insensitively, exactly as `TTSFactory.ParseEngine` does, and `ResolveEngine` is `internal
static` so the test asserts the same code the service runs.

**Load.** Nothing periodic is added. `PlaybackChanged` fires on transitions only; there is no position tick
(ADR §8.2), no timer, and no poll. The one background task per playback exists only while a playback does.
`TTSEventSource`'s pre-existing 100 ms monitor keeps its own cadence and is not touched. ADR §1.3's ban on
ticks and polls is respected. **One new cost, stated:** a `RemoteMedia` playback holds an open `FileStream`
over a cached recording for its duration — that is `AudioFileEventSource`'s existing behaviour, not
something PR 3 adds, but PR 3 is what makes it happen for voicemail.

**Scope.** No `Radio.Web`, no config key, no `deploy/`, no `DuckingService`, no `SourcesController`, no
`TTSFactory`, no `TTSEventSource`, no `FilePlayerAudioSource`, no `/hubs/audio`, no `CircuitHandler`, no
max-duration cap, no preemption rule. Task 13 asserts six of those with `git diff --name-only`.

**Assertions this PR makes, and where each is checked:**

| Claim in a comment or contract | Checked by |
|---|---|
| *"acquisition is not bound to the caller's token"* | `AcquisitionSurvivesCancellationOfTheStartToken` |
| *"a playback ends exactly once"* | `ATerminalTransitionHappensExactlyOnce_…`, raising both events |
| *"the handler never waits on `_gate`"* | code review of `OnSourceCompleted` — it claims and returns; the wait is inside the `Task.Run`. ⚠ **Not covered by a test**, and it is a deadlock rather than a wrong value, so a reviewer must read it |
| *"all four `TTSParameters` fields come from configuration"* | `SpeechFillsAllFourTtsParametersFromConfiguration`, asserting four fields |
| *"the snapshot's `Duration` is null when the provider said 0"* | `AVoicemailWithDurationZeroReportsANullSnapshotDuration`, which also asserts the source's is not |
| *"a speech snapshot's position does not advance"* | `ASpeechSnapshotReportsPositionZeroForItsWholeLife` — asserts the real behaviour, not the desirable one |
| *"no mixer source is ever added"* | `NoMixerSourceIsEverAdded` + Task 13 item 1 |
| *"no raw media id and no utterance in a log"* | `NeitherTheTextNorTheRawMediaIdReachesAnyLogLine`, `Assert.NotEmpty` so it cannot pass vacuously |
| *"the container resolves"* | `EventPlaybackRegistrationTests` with both validations, **and** the whole-container route tests |
| *"`IsPermanent` is true only for `Disabled`"* | `IsPermanentIsTrueOnlyForDisabled`, 7 cases |
| *"the surface is alive end to end"* | `Post_RemoteMedia_ReachesTheClientAndFailsWithANamedMediaReason` |
| *"gvbridge sends `Content-Length` and answers 404/502/401"* | **read from RotaryPhone's source** (§2.2 item 6), not asserted by a test in this repo — and it must not be, since it is another repo's behaviour |

**Where a comment could still overclaim, flagged for the reviewer rather than defended.** Three:

1. **The `_gate` deadlock argument is a reading, not a test.** `OnSourceCompleted`'s claim that it never
   waits on `_gate` is true of the code as written and would stop being true if anyone moved the wait out
   of the `Task.Run`. Nothing fails if they do — it hangs.
2. **`TearDownAsync`'s claim about file handles.** It says disposal releases the `FileStream` "which is
   what lets `GvMediaCache` evict it later." That is true on Linux either way (unlink succeeds on an open
   file); on Windows it is what makes `File.Delete` succeed rather than being logged and skipped. The
   comment says both, and neither is asserted by a test — the cache's evictor already swallows per-file
   failures by design.
3. **`Transport`'s 404-vs-409 race.** Documented in the method's own remarks rather than argued away. Two
   refusals, microseconds apart, one console.

**Rebase surface.** Two shared files and both are small: `src/Radio.API/Program.cs` (one line, at a spot no
other open row touches) and `src/Radio.Infrastructure/Audio/Services/AudioFileEventSourceFactory.cs` (one
new method, appended). `src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs` and the two
`External/GvMedia*.cs` files are this arc's alone. **Nothing under `src/Radio.Infrastructure/Audio/SoundFlow/`
is touched**, which keeps this clear of `AUD-2` and `AUD-4`; nothing in `src/Radio.Web/`, which keeps it
clear of the whole `ENC-*` bundle including the two Builders currently in flight on `ENC-6` and `ENC-7`.
`docs/BUILDER_QUEUE.md` is one appended row.

---

## 4. Things this plan deliberately does not do, with the reason

1. **Override `TTSEventSource.Position`** (C-27). It would be three lines and it contradicts a deliberate,
   documented decision in a file PR 3 has no other reason to open. The consequence — a speech scrubber
   pinned at zero — is invisible until PR 5 broadcasts snapshots and PR 6 renders them, so it is handed to
   PR 5 with the exact change, and PR 3 pins the current behaviour instead of asserting the desirable one.
2. **Fix `TTSFactory`'s eSpeak argument line** (C-26). A live shared path reached by `AnnouncementService`
   and `SourcesController`; fixing it properly means either validating inside the factory for every caller
   or moving to `ArgumentList`, and either belongs in a change that can be reviewed on its own merits.
   PR 3 closes its own door and files the rest. ⚠ **This is a live security defect and it is filed, not
   fixed — see §5.**
3. **Make `TTSParameters`' fields nullable** (C-25). ADR §9.3 flags it as optional cleanup and `PHN-1a` §4
   routed it here; declined because the trap is four fields wide and a nullable `Engine` alone would look
   like a complete fix while being a quarter of one. Filed as one row covering all four.
4. **Invalidate `TTSFactory._cachedEngines`** (C-31). The other acceptable answer to ADR §14 Q10; not
   taken, for the reasons in C-31, and filed with §9.4 defect (a) because they are one change.
5. **Add a queue row for either of the above.** Two Builders are editing `docs/BUILDER_QUEUE.md` right
   now, and this PR was scoped to touch **only its own row**. Both are recorded in
   `design/FUTURE-WORK.md` with reproductions, and the eSpeak one is called out to the owner directly.
   A security row is the owner's to prioritise, not a planning PR's to slip in.
6. **Ask RotaryPhone to fix `FindNodeAsync`** (C-22). The right long-term fix for the 404-during-blackout
   is theirs — propagate `Succeeded` so `GetAudio` answers `502` the way `GetList` already does. PR 3 does
   not make the ask, because a cross-repo ask goes through
   `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`'s protocol and a queue row under
   § Cross-repo handoffs — both outside this PR's scope. The exact text is in §5 so the owner can send it
   in one paste. **This side is correct without it**; the fix would only make `MediaNotFound` mean what it
   says.
7. **Fix `GvMediaStartupCheck`** (C-33). It structurally cannot see Radio.Web's overlay, and the
   plausible-looking improvement would fire a false warning on every boot of a correctly-configured box.
   The real signal is `MediaUnauthorized` at first fetch, which PR 3 surfaces and Task 12b documents.
8. **Retire `/api/sources/events/*` or route `IAnnouncementService` through the new seam.** ADR §14 Q6 —
   explicitly not in this arc.
9. **Add an `IGvMediaClient` interface.** `PHN-1b` §4 item 1 decided against it and gave PR 3 the reason:
   a stub `HttpMessageHandler` is enough to test through the real client, which is what Task 6 does. An
   interface would be a seam nobody needs.
10. **Broadcast anything, cap anything by duration, or count circuits.** All PR 5.

---

## 5. Handoff to the rest of the arc

**Do not re-sequence the arc.** The breakdown's order stands; this plan implements PR 3 of it unchanged.
C-30 clarifies a boundary the breakdown left implicit; it moves nothing between PRs.

**To PR 4 (priority becomes load-bearing) — ⚠ STILL THE ONE TO REVIEW HARDEST:**

- **`DuckingServiceCharacterizationTests` is your tripwire and PR 3 did not touch it.** All four still
  pass. Update them; never delete them.
- **PR 3 implements the single-slot rule and NOT the priority rule, and the difference is where your diff
  goes.** `EventPlaybackService.StartAsync` already tears down a previous attended playback — that is D6
  §8.1's consequence of there being one set of speakers. What PR 4 adds is the *other* direction: an
  **unattended** source starting at priority ≥ `GvMedia:PreemptAtPriority` (8) stops attended playback.
  Nothing in PR 3 reads that key; the subscription to `DuckingStateChanged` is yours to add, and
  `EventPlaybackService.StopAsync(playbackId)` is the method to call — it is idempotent through
  `ClaimTerminal`, so a preemption racing a natural end cannot double-fire.
- **When PR 4 lands, `design/INTEGRATIONS.md:566`'s correction must be updated in the same PR.** PR 3
  deliberately did not touch that line — it is still true today.
- The live consequence to put in front of a reviewer as a deliberate acceptance: with
  `PhoneIntegration:Enabled` false, **a doorbell posted to `/api/notifications/announce` at its default
  priority 8 will stop a voicemail mid-play.** Intended (ADR §6.1).

**To PR 5 (server-owned state and the three stop conditions):**

- **`PlaybackChanged` is already raised on every transition and nothing subscribes.** That is your
  connection to make. `EventPlaybackService.Publish` is the single point where a transition is decided, so
  the hub broadcast belongs downstream of it and nowhere else — two places deciding transitions is how the
  snapshot and the wire drift apart.
- ⚠ **`GET /api/audio/events/current` already exists** and is what ADR §8.1 calls the re-attach path. Seed
  `AudioStateStore` from it. ⚠ And note what the queue recorded when `ENC-12` shipped: **`AudioStateStore`
  had never been constructed in its life** — zero consumers in `src/Radio.Web` — so its hub cache has never
  once run. Anything that plans to "read the cached state" needs a consumer first.
- ⚠ **A speech playback's `PositionAtBroadcast` is always zero** (C-27), so a scrubber rendered from it
  will sit at 0 for the whole message. The fix is an override on `TTSEventSource` mirroring
  `AudioFileEventSource`'s — `_playbackService.GetPosition(Id) ?? TimeSpan.Zero`, and `Id` *is* that
  source's playback key, so it is three lines. **PR 3 pinned the current behaviour in
  `ASpeechSnapshotReportsPositionZeroForItsWholeLife`; that test is what should fail when you make the
  change**, and it should be updated rather than deleted.
- **The max-duration cap has a natural home:** `EventPlaybackService.Playback` already owns a
  `CancellationTokenSource`. `CancelAfter(GvMedia:MaxPlaybackSeconds)` on it, at the point the source
  starts playing, is the whole feature — read the value from `IOptionsMonitor<GvMediaOptions>`; do not add
  a second key.
- **`Label` is capped at 128** (Task 1), which bounds what goes on the wire.

**To PR 6 (`PHN-2` — retire the `<audio>` element):**

- Remove `VoicemailPlayer.razor`'s `<audio>` element, `wwwroot/js/voicemail-player.js` and
  `GvBridgeApiService.GetVoicemailAudioUrl` **together** — removing the builder first would break voicemail
  playback.
- **Withdraw the cross-repo ask** in `docs/BUILDER_QUEUE.md` § Cross-repo handoffs #3 in the same PR. ADR
  §10.1 makes it a deliverable; PR 6 is the first moment it is true.
- ⚠ **Pre-flight before flipping `GvMedia:Enabled` on the box:** check whether RotaryPhone's
  `InterServiceAuthKey` is set. It ships default-off, but if it has been set then **every** fetch returns
  `401` → `MediaUnauthorized` until `GvMedia:AuthKey` matches — and that is **two** hand edits, to
  `/opt/radio-console/api/appsettings.Production.json` and `/opt/radio-console/web/`'s, neither re-seeded
  by the deploy.
- ⚠ **A `MediaNotFound` during UAT is as likely to be the GV blackout as a bad id** (C-22). Record the
  wall-clock time of every failure and retry after five minutes before concluding anything.
- **Carry these UAT items**, which is the full list now: `PHN-1a`'s three device-only checks (seek
  repositions; `Time` advances; pausing a TTS source does not report completion), plus **that
  `./data/gvmedia` is writable under the service account** — re-carried from `PHN-1b` §2.2 item 4, because
  PR 3 does not perform a first fetch on the box — plus the row's own settling check: **play a voicemail
  while the radio is on and confirm the radio ducks, that mute silences it, that master volume moves it,
  and that with Cast active it goes to the Cast device rather than the local speakers.**
- §2.2 has the `curl` recipe for all of it.

**To `PHN-3` (the SMS speak button — NOT one of the breakdown's seven PRs):**

- It owns **`GvSpeechText.ForMessage`**, in `src/Radio.Web/`, as a pure static over `SmsMessageDto`, called
  by the component **before** it posts. It owns truncation with a spoken tail, client-side and visible
  (§0.3 ⓵). PR 3 rejects over-length text with `TextTooLong` → `400`; **do not re-add truncation to the
  server on the grounds that ADR §4.2 says so** — `PHN-1b` §0.3 ⓵ is the answer if a reviewer raises it.
- `VoiceId` is now allow-listed to `[A-Za-z0-9._~+-]`, 64 chars (C-26). Send `null` for both `voiceId` and
  `engine`, which is what ADR §9.5 says `Radio.Web` should do anyway.

**To the owner — two things that are not any PR's, and one is a live security defect:**

1. ⚠ **`TTSFactory.GenerateESpeakAsync` builds its process arguments by string interpolation, and
   `POST /api/sources/events/tts` reaches it unauthenticated.** A `voice` of
   `"en -w /opt/radio-console/api/appsettings.Production.json"` becomes extra `espeak-ng` flags — an
   arbitrary file write as `mmack`. Argument injection, not command injection; no shell is involved and no
   credential is disclosed, but integrity is. Live today, not introduced by this arc, filed in
   `design/FUTURE-WORK.md` with the reproduction. **PR 3 closes its own path; the `SourcesController` path
   stays open until someone takes the row.** This plan did not add that row because two Builders are
   editing the queue and it was scoped to one.
2. **A cross-repo ask for RotaryPhone**, ready to paste (C-22):

   > `GvVoicemailController.GetAudio` resolves a recording through `FindNodeAsync`, which calls
   > `GvVoicemailClient.ListVoicemailsAsync` and does not check the result's `Succeeded` flag — unlike
   > `GetList`, which guards it and returns 502 with a comment saying why. A failed authenticated list
   > returns `Empty(succeeded: false)`, so `FirstOrDefault` yields null and the route answers
   > **404 "has no recording"** during a Google Voice auth blackout, for a recording that exists.
   > Radio Console cannot distinguish that from a genuinely absent recording, so it now treats every 404
   > from this route as retryable. Propagating `Succeeded` so `GetAudio` answers 502 during a blackout —
   > the way `GetList` already does — would make the 404 mean what it says.
