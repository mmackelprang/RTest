# PLAN — `PHN-1a` · ADR-029 PR 1: the event-playback contracts

> **Status:** ready for Builder. Written 2026-09-02 against `4f84b4d`.
> **Punch list:** [`docs/HANDOFF-GA-PUNCH-LIST.md`](../../docs/HANDOFF-GA-PUNCH-LIST.md) §3.5 `PHN-1` (P0), §2 `O6`.
> **Decision of record:** [ADR-029](../decisions/2026-08-03-gv-audio-through-engine.md) — D1, D2, D4.
> **Sequencing:** [`design/plans/PHN-arc-pr-breakdown.md`](PHN-arc-pr-breakdown.md) — **this plan is PR 1 of 7.**
> **Depends on:** nothing. It is the head of the arc.
> **Follows the ADR**, with one declared extension (§0.4 C-1/C-2 — PR 1 must *build* the seek
> primitive the ADR believed already existed) and eleven contradictions resolved in §0.4.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

ADR-029 says voicemail-through-the-engine and speak-a-text are **one mechanism, not two features**,
and PR 1 is that mechanism's type surface: the `IEventPlaybackService` seam, the closed
discriminated `EventPlaybackRequest`, the `EventPlaybackSnapshot` it hands back, and the five
transport members `IEventAudioSource` is missing. **Nothing in this PR plays a sound differently
than it does today.** No controller, no HTTP route, no `GvMediaClient`, no DI registration, no
ducking change. What it buys is that PRs 2-5 all build against one settled contract instead of
inventing three.

### 0.2 Why `IEventPlaybackService` sits BESIDE `IAnnouncementService`, not inside it

This is load-bearing and Builder should be able to defend it, because *"just add an overload to
`AnnounceAsync`"* is the obvious-looking shortcut and it is wrong for a mechanical reason rather
than a stylistic one.

`IAnnouncementService` is **fire-and-forget by signature**. `AnnounceAsync(message, priority, ct)`
returns a bare `Task`, hands back no identity, and `StopAsync()` stops *everything*. There is no
per-event addressability anywhere in the type. It has exactly two injection sites in the tree —
`NotificationsController.cs:16,23` and `PhoneCallIntegrationService.cs:21,29` — and both depend on
exactly that shape: post it, forget it.

Adding a handle, a position, a pause and a state broadcast to it would not extend that contract, it
would **replace** it, while both existing callers keep needing the old one. The result is one
interface with two personalities and a stop method whose blast radius depends on which personality
the caller used.

The real distinction is **attended vs unattended**, and it is behavioural, not cosmetic:

| | `IAnnouncementService` (unattended) | `IEventPlaybackService` (attended) |
|---|---|---|
| Who started it | a doorbell, a laundry machine, an incoming call | a person, with a finger, one second ago |
| Is anyone listening on purpose | no | yes — that is the whole event |
| Transport controls | none, and none wanted | play / pause / seek / stop |
| Ducking policy | mixes over the primary | preempted at priority ≥ 8 (PR 4) |
| Stop semantics | global | by `playbackId` |

Those differences are exactly what PRs 4 and 5 branch on. Collapsing the two interfaces would
collapse the distinction the later PRs need.

### 0.3 What this row is NOT

1. ⛔ **No controller and no route.** `POST /api/audio/events` is **PR 3**. Adding it here would
   drag `EventPlaybackService`, the `playbackId` mapping and the ducking subscription in with it.
2. ⛔ **No `GvMediaClient`, no cache, no `GvMedia` config block.** All **PR 2**.
3. ⛔ **No `DuckingService` change.** Making `StartDuckingAsync` raise on every call is **PR 4**,
   the sharpest change in the arc. Task 12 here only *pins today's behaviour* so PR 4's diff is
   visible instead of silent.
4. ⛔ **No DI registration of anything.** See §0.6 — this is what keeps PR 1's risk at zero.
5. ⛔ **No fix to `FilePlayerAudioSource`.** §0.4 C-1 finds a real defect there. It is a live
   primary-source path with its own UAT; it is logged (Task 13), not smuggled in.
6. ⛔ **Do NOT use `POST /api/sources/events/{tts,file}` as a template for anything.** §0.4 C-10 —
   all three of its defects are still live at the lines the ADR cites.

### 0.4 ⚠ Eleven contradictions found while planning, and how each resolves

Read this before Task 1. Two of them change what PR 1 has to build.

**C-1 — ⚠ THE ONE THAT CHANGES THE WORK. ADR-029 §8.3 says `FilePlayerAudioSource` "already
implements seeking over a local file through `SoundFlowPlaybackService`", and concludes that D4 is
"a lift rather than an invention". Both halves are false.**

- `SoundFlowPlaybackService` has **no seek method of any kind.** Its complete public surface is
  `PlayFileAsync`, `PlayStreamAsync`, `PlayDataProviderAsync`, `PlayComponentAsync`, `StopAsync`,
  `Pause`, `Resume`, `SetVolume`, `SetGainOffset`, `SetDuckingMultiplier`,
  `ClearDuckingMultiplier`, `IsPlaying`, `GetPosition`, `GetDiagnostics`, `StopAll`, `Dispose`.
- `FilePlayerAudioSource.SeekCoreAsync`
  (`src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs:910-922`) range-checks
  its argument and then executes `_position = position;`. **It assigns a field.** No audio is
  repositioned — and `_position` is the very field `Position` reads back, so a seek reports
  success, moves the readout, and changes nothing audible.
- `FilePlayerAudioSource.cs:119` therefore declares `public override bool IsSeekable => true;`
  over an implementation that cannot seek. That is the **`CLAUDE.md` § Pre-Merge Review failure
  class** — a fourth instance beyond the three that section already enumerates.

**Resolution:** D4's *shape* is unchanged; the five member signatures are still copied verbatim from
`IPrimaryAudioSource`. What changes is that **PR 1 must build the primitive rather than lift one**
(Task 4). That is one extra task; the breakdown's 1-2 d estimate for PR 1 still holds.
`FilePlayerAudioSource` is **not** fixed here (§0.3 item 5) and is logged in Task 13.

**C-2 — ADR-029 §14 Q3 is a Planner verification item, and it is now answered: seek is available,
just not where the ADR looked.** `SoundFlowPlaybackService._activePlayers` is
`Dictionary<string, SoundPlayer>` (`SoundFlowPlaybackService.cs:24`), and `SoundFlow` 1.4.1's
`SoundPlayerBase` exposes — verified by reflecting the restored assembly, not inferred from
documentation:

```
Boolean Seek(System.TimeSpan time, System.IO.SeekOrigin seekOrigin = Begin)
Boolean Seek(Single timeInSeconds)
Boolean Seek(Int32 sampleOffset)
Single  Time        // current playback time, in SECONDS
Single  Duration    // total duration, in SECONDS
```

So `SeekAsync` does **not** have to degrade to Q3's stop-and-restart-at-offset fallback. Two things
Builder must carry forward: `Seek` returns **`bool`** — propagate it, do not swallow it — and
`Time`/`Duration` are **`float` seconds**, so every crossing into `TimeSpan` goes through
`TimeSpan.FromSeconds(...)`.

**C-3 — `SoundFlowPlaybackService.GetPosition` carries a comment that is false; it is the fifth
instance of the same class.** `SoundFlowPlaybackService.cs:714-725` finds the player and then
returns `TimeSpan.Zero`, annotated `// Position tracking not available in current SoundFlow API`.
`ISoundPlayer.Time` exists in the referenced package (`<PackageReference Include="SoundFlow"
Version="1.*" />`, restored 1.4.1). **`GetPosition` has zero callers repo-wide**, so correcting it
in PR 1 has no behavioural blast radius — stated explicitly, because *"fix a stub inside a shared
audio service"* otherwise reads as a risk it is not.

**C-4 — ADR §8.3 says D4's "blast radius is two files".** It is six production files plus two test
files: `IEventPlaybackService.cs` (new), `IEventAudioSource.cs`, `EventAudioSourceBase.cs`,
`AudioFileEventSource.cs`, `TTSEventSource.cs`, `SoundFlowPlaybackService.cs`. Not a design change —
a scoping correction, so "two files" is not read as the size of the work.

**C-5 — the punch list names the members `Seek` / `Pause` / `Resume`; ADR §2 D4 names them
`SeekAsync` / `PauseAsync` / `ResumeAsync`.** **The ADR governs.** D4's entire argument is that the
signatures are copied *verbatim* from `IPrimaryAudioSource`, which declares `PauseAsync` (`:47`),
`ResumeAsync` (`:54`) and `SeekAsync(TimeSpan, CancellationToken)` (`:71`).

**C-6 — ⚠ pausing a `TTSEventSource` would make it report itself finished, and no document mentions
it.** `TTSEventSource.StartPlaybackWithMonitoringAsync` treats `!_playbackService.IsPlaying(Id)` as
natural completion (`TTSEventSource.cs:169-175`), and `IsPlaying` is
`player.State == PlaybackState.Playing` (`SoundFlowPlaybackService.cs:697-707`) — **a paused player
fails that test.** So the moment PR 1 makes `PauseAsync` reachable on a TTS source, a pause raises
`PlaybackCompleted(EndOfContent)` and drives the state to `Stopped`. The ADR says only that pausing
a spoken message *"has no user value"* (§8.3, §12 item 1), which is a UX claim, not this defect.
**Resolution:** Task 9 makes the monitor loop pause-aware. The new branch is unreachable until
something calls `PauseAsync`, and nothing does before PR 3 — so it is additive, not a change to the
live announcement path.

**C-7 — `AudioFileEventSource`'s completion timer is unaware of transport, and the ADR catches only
half of it.** §14 Q4 flags the seek half — *"a seek mid-playback must re-arm that timer or
completion will fire early"* — and hands it to Planner. It misses the pause half:
`await Task.Delay(_duration, cancellationToken)` (`AudioFileEventSource.cs:205`) keeps counting
while the audio is paused, so a pause longer than the remaining audio raises `EndOfContent` on a
source that is silent and unfinished. §0.5 and Task 8 fix both with one mechanism.

**C-8 — ADR §5.1 tells PR 2 to follow `PhoneContactLookupService`'s *"log-masking discipline (it
masks numbers to `***1234`)"*. Following that file verbatim would leak.** It masks in exactly one
place, the success-path debug line (`PhoneContactLookupService.cs:87-90`), and logs the **raw**
number at `:78` (`"Looking up contact for {PhoneNumber}"`) and again in the REST catch at `:100`.
PR 2's instruction must be *"mask on every line"*, not *"do what that file does"*. Not PR 1's work;
recorded here so PR 2 does not inherit it.

**C-9 — three ADR line citations have drifted; the content behind all three is confirmed
unchanged.** `PhoneContactLookupService` is registered at `AudioServiceExtensions.cs:450` (ADR §5
says `:435`); `RotaryPhoneAuthHandler` is attached at `Radio.Web/Program.cs:327,342,360,377,393`
(ADR §1.2 says `:323,338,356,373,389`); `INTEGRATIONS.md`'s corrected ducking claim is at `:566`
(ADR ⟨A1·6⟩ says `:464`). Cosmetic — recorded so a reader who greps a cited line, finds nothing,
and concludes the finding was withdrawn does not do so.

**C-10 — the three `SourcesController` defects every document warns about are still live, at the
exact lines cited.** Re-verified against the current tree:

- `_duckingService` is declared `:29`, taken as a constructor parameter `:44`, assigned `:55`, and
  **never read again** — three occurrences in the whole file. Those events do not duck.
- `mixer.AddSource(ttsSource)` at `:651`, inside `PlayTTSEvent` (`:605-663`), has no
  `RemoveSource`, no `Dispose` and no `PlaybackCompleted` subscription reachable from it. The
  file's only `RemoveSource` is `:727`, in the *file* action's failure branch. It leaks per play.
- `PlayFileEvent` calls `_playbackService.PlayFileAsync` at `:719`, then `fileSource.PlayAsync` at
  `:732`, which runs `AudioSourceBase.PlayAsync` → `AudioFileEventSource.PlayCoreAsync` →
  `PlayWithSoundFlowAsync` → **the same `PlayFileAsync` again, under a different key**
  (`_playbackId` vs the `fileSource.Id` the controller passed). It double-plays.

**Nothing has been fixed since the ADR was written.** The warning stands exactly as issued.

**C-11 — do NOT also align `Duration`.** `IEventAudioSource.Duration` is `TimeSpan`;
`IPrimaryAudioSource.Duration` is `TimeSpan?`. D4 lists five members and `Duration` is not one of
them. The nullability the ADR needs for *"duration unknown"* (§4.1, `DurationSeconds == 0`) lives on
`EventPlaybackSnapshot.Duration`, which is `TimeSpan?` already. Changing the source-level `Duration`
would touch every event source for no gain. Recorded because *"copy the transport surface from
`IPrimaryAudioSource`"* reads like an instruction to align it too.

### 0.5 The completion problem, stated once so Tasks 8 and 9 stay short

Both event sources decide *"playback finished"* from a clock rather than from the audio, and PR 1 is
the first change that can move the audio out from under that clock.

- `AudioFileEventSource` waits one fixed `Task.Delay(_duration)` (`:205`). A **seek forward** makes
  it complete late, a **seek backward** makes it complete early, and a **pause** makes it complete
  while paused.
- `TTSEventSource` polls `IsPlaying(Id)` every 100 ms (`:163-193`) — transport-aware for stop, but
  per C-6 it reads a **pause** as completion.

**The rule for both: a wait is re-armed by any transport event, and a paused source has no
deadline.** The mechanism is a `CancellationTokenSource` that `SeekCoreAsync` / `PauseCoreAsync` /
`ResumeCoreAsync` swap and cancel; the waiter catches that cancellation, recomputes, and waits
again. **No poll and no timer is added** — ADR §1.3's ban on ticks and polls is respected, and
`AudioFileEventSource` gains no periodic work at all.

The degradation is deliberately benign: if `GetPosition` ever returns `null` or `Zero`, `remaining`
falls back to `_duration` and the behaviour is **exactly today's single `Task.Delay(_duration)`**.

### 0.6 The DI-guard question, answered — and why PR 1 is exempt

A sibling planner found that adding a constructor dependency to a registered service can turn a
passing DI guard into a service-start failure on the appliance. The state of the guards here:

- `tests/Radio.Infrastructure.Tests/DependencyInjection/RotaryEncoderRegistrationTests.cs` builds a
  **real** `ServiceProvider` and resolves from it — but only over `AddRotaryEncoders`.
- `tests/Radio.Infrastructure.Tests/DependencyInjection/ActiveSourceAccessorRegistrationTests.cs`
  covers `AddSoundFlowAudio` by **descriptor inspection only**, and resolves from a hand-rolled
  minimal container, deliberately avoiding real audio hardware.
- **No test anywhere calls `BuildServiceProvider` with `ValidateOnBuild` or `ValidateScopes`** —
  zero matches across `tests/`.

So a new service registered in `AudioServiceExtensions.cs` would be caught by **nothing** today.

**PR 1 is exempt because it registers nothing and changes no constructor.**
`IEventPlaybackService` ships with no implementation and no registration;
`AudioFileEventSource`, `TTSEventSource`, `AudioFileEventSourceFactory` and
`SoundFlowPlaybackService` all keep their current constructor signatures. Task 14 asserts that.
**The obligation lands on PR 2 and PR 3**, which do register new services — §5 carries it forward.

---

## 1. Tasks

Fourteen tasks. Tasks 1-3 are Core-only and independent of the rest; Tasks 4-11 are the transport
lift; Tasks 12-14 are the pin, the docs and the gate.

---

### Task 1 — `IEventPlaybackService`, the request, the snapshot and the three enums

**File (new):** `src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs`

Convention note: the interface, its enums and its records go in **one file**, mirroring
`ITTSFactory.cs` (which holds `ITTSFactory`, `TTSEngine` and `TTSParameters` together) and
`IAudioSource.cs` (interface + three enums + an EventArgs). Do not scatter them across
`Models/Audio/`.

```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Attended event playback — ADR-029 D1.
///
/// Sits BESIDE <see cref="IAnnouncementService"/>, deliberately, and does not replace it.
/// <see cref="IAnnouncementService"/> serves UNATTENDED announcements: fire-and-forget, no
/// identity, one global stop. This serves ATTENDED playback: a user pressed a button, is
/// listening on purpose, and expects transport controls and a handle to address.
///
/// Both arms of <see cref="EventPlaybackRequest"/> share one lifecycle, one state model, one
/// stop path and one broadcast; they differ only in how the audio is acquired, and that
/// difference lives inside the implementation rather than at this contract.
/// </summary>
public interface IEventPlaybackService
{
  /// <summary>
  /// Starts an attended playback. Returns as soon as the request is accepted — the returned
  /// snapshot is normally <see cref="EventPlaybackState.Preparing"/>, because both arms have an
  /// acquisition phase (an HTTP fetch, or a TTS synthesis) before any audio exists.
  /// </summary>
  Task<EventPlaybackSnapshot> StartAsync(
    EventPlaybackRequest request,
    CancellationToken cancellationToken = default);

  /// <summary>Stops the playback with this id. False when no such playback is in flight.</summary>
  Task<bool> StopAsync(string playbackId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Seeks the playback with this id. False when there is no such playback, or when the
  /// underlying source is not seekable.
  /// </summary>
  Task<bool> SeekAsync(
    string playbackId,
    TimeSpan position,
    CancellationToken cancellationToken = default);

  /// <summary>Pauses the playback with this id. False when no such playback is in flight.</summary>
  Task<bool> PauseAsync(string playbackId, CancellationToken cancellationToken = default);

  /// <summary>Resumes the playback with this id. False when no such playback is paused.</summary>
  Task<bool> ResumeAsync(string playbackId, CancellationToken cancellationToken = default);

  /// <summary>
  /// The one in-flight attended playback, or null. There is one audio engine and one set of
  /// speakers, so this state is global rather than per-caller (ADR-029 D6).
  /// </summary>
  EventPlaybackSnapshot? Current { get; }

  /// <summary>
  /// Raised on every state transition. Deliberately NOT raised periodically: the snapshot
  /// carries a position anchor and clients interpolate locally (ADR-029 §8.2).
  /// </summary>
  event EventHandler<EventPlaybackSnapshot>? PlaybackChanged;
}

/// <summary>Which arm of <see cref="EventPlaybackRequest"/> is populated.</summary>
public enum EventPlaybackKind
{
  /// <summary>Speak a literal string through the currently selected TTS engine.</summary>
  Speech = 0,

  /// <summary>Play a remote recording, addressed by identifier — never by URL.</summary>
  RemoteMedia = 1
}

/// <summary>
/// The closed set of remote media the server knows how to resolve. One member today.
/// Adding a member means adding a URL template in the server's own configuration — which is
/// the whole point: the caller never supplies a URL (ADR-029 D2).
/// </summary>
public enum RemoteMediaKind
{
  /// <summary>A Google Voice voicemail recording, fetched from the configured gvbridge host.</summary>
  GvVoicemail = 0
}

/// <summary>Lifecycle of one attended playback.</summary>
public enum EventPlaybackState
{
  /// <summary>Accepted; audio is being acquired (fetch or synthesis). No sound yet.</summary>
  Preparing = 0,
  Playing = 1,
  Paused = 2,
  /// <summary>Reached the end of the content.</summary>
  Completed = 3,
  /// <summary>Ended before the end of the content — user stop, preemption, or the duration cap.</summary>
  Stopped = 4,
  /// <summary>Never produced sound. <see cref="EventPlaybackSnapshot.FailureReason"/> says why.</summary>
  Failed = 5
}

/// <summary>
/// A closed discriminated request with deliberately ASYMMETRIC arms (ADR-029 D2).
///
/// Speech carries the literal utterance, because the text is already in the caller's hands, is
/// small, and the server has no business acquiring SMS content. Remote media carries a
/// (kind, id, duration) REFERENCE, because the recording is large, remote, and in nobody's hands
/// yet — so the fetch happens once, server-side, where it can be cached and authenticated.
///
/// ⚠ There is deliberately NO url/uri field on this type, and there must never be one. An
/// endpoint that fetches a caller-supplied URL is a server-side-request-forgery primitive, and
/// "it is a LAN kiosk" is not a defence. The server maps <see cref="RemoteMediaKind.GvVoicemail"/>
/// to a URL built from ITS OWN configuration. <see cref="Validate"/> pins this, and
/// EventPlaybackRequestTests pins that this type declares no URL-shaped property.
/// </summary>
public sealed record EventPlaybackRequest
{
  /// <summary>Which arm is populated. Every other field is validated against this.</summary>
  public required EventPlaybackKind Kind { get; init; }

  // ── Kind == Speech ────────────────────────────────────────────
  /// <summary>The literal utterance. Composed by the caller (ADR-029 §4.2).</summary>
  public string? Text { get; init; }

  /// <summary>Per-request voice override. Null means TTSOptions.DefaultVoice.</summary>
  public string? VoiceId { get; init; }

  /// <summary>
  /// Per-request engine override. Null means the currently selected engine, TTS:DefaultEngine
  /// (ADR-029 D10). Radio.Web sends null; this exists so one utterance can diverge without
  /// creating a second persistent place where engine selection lives.
  /// </summary>
  public string? Engine { get; init; }

  // ── Kind == RemoteMedia ───────────────────────────────────────
  /// <summary>Which closed-set media resolver to use.</summary>
  public RemoteMediaKind? MediaKind { get; init; }

  /// <summary>
  /// The provider's identifier for the recording — for GvVoicemail, VoicemailItemDto.Id.
  /// ⚠ NEVER a URL, and never VoicemailItemDto.AudioUrl. See the type remarks.
  /// </summary>
  public string? MediaId { get; init; }

  /// <summary>
  /// Authoritative duration from the provider's DTO. Per ADR-022 §4.2, 0 means UNKNOWN.
  /// This is a correctness fix, not decoration: AudioFileEventSource detects completion from
  /// this value, and AudioFileEventSourceFactory would otherwise estimate it from file size
  /// (MP3 at a flat 16000 B/s) and never decode.
  /// </summary>
  public int? DurationSeconds { get; init; }

  // ── Both arms ─────────────────────────────────────────────────
  /// <summary>Display label, e.g. "Voicemail from Jane". Presentation only.</summary>
  public string? Label { get; init; }

  /// <summary>
  /// Ducking priority. 6 is the attended-playback class (ADR-029 §6.1) — below the 8 that this
  /// system uses for "an event that did not state its importance", so anything that did not
  /// claim a rank still outranks a user listening to a recording.
  /// </summary>
  public int Priority { get; init; } = 6;

  /// <summary>
  /// Validates the closed set and its asymmetric arms.
  /// Returns <see cref="EventPlaybackRejection.None"/> when the request is acceptable.
  /// </summary>
  /// <param name="maxSpeechChars">
  /// Cap on <see cref="Text"/>, GvMedia:MaxSpeechChars. The default matches the ADR's shipping
  /// value so this method is usable from tests without a configuration object.
  /// </param>
  public EventPlaybackRejection Validate(int maxSpeechChars = 1000)
  {
    if (Priority is < 1 or > 10)
    {
      return EventPlaybackRejection.PriorityOutOfRange;
    }

    switch (Kind)
    {
      case EventPlaybackKind.Speech:
        // The arms are closed, not merely optional: a Speech request carrying media fields is
        // a caller confusion, and accepting it would let a future refactor read the wrong arm.
        if (MediaKind is not null || MediaId is not null || DurationSeconds is not null)
        {
          return EventPlaybackRejection.ArmMismatch;
        }
        if (string.IsNullOrWhiteSpace(Text))
        {
          return EventPlaybackRejection.MissingText;
        }
        return Text.Length > maxSpeechChars
          ? EventPlaybackRejection.TextTooLong
          : EventPlaybackRejection.None;

      case EventPlaybackKind.RemoteMedia:
        if (Text is not null || VoiceId is not null || Engine is not null)
        {
          return EventPlaybackRejection.ArmMismatch;
        }
        if (MediaKind is null)
        {
          return EventPlaybackRejection.MissingMediaKind;
        }
        if (!Enum.IsDefined(MediaKind.Value))
        {
          return EventPlaybackRejection.UnknownMediaKind;
        }
        if (DurationSeconds is < 0)
        {
          return EventPlaybackRejection.NegativeDuration;
        }
        return ValidateMediaId(MediaId);

      default:
        return EventPlaybackRejection.UnknownKind;
    }
  }

  /// <summary>
  /// Defence in depth for the SSRF property. The primary defence is structural — this type has
  /// no URL field, and the server builds the URL from its own configuration — but the id still
  /// becomes a URL path segment and a cache key downstream, so it is constrained here too.
  ///
  /// ⚠ Declared assumption: a Google Voice voicemail id contains no '/' or '\'. If one ever
  /// does, this rejects it as MediaIdHasPathSeparator — a loud, named 400 rather than a silent
  /// misbehaviour — and the fix is one line here.
  /// </summary>
  private static EventPlaybackRejection ValidateMediaId(string? mediaId)
  {
    if (string.IsNullOrWhiteSpace(mediaId))
    {
      return EventPlaybackRejection.MissingMediaId;
    }
    if (mediaId.Length > MaxMediaIdChars)
    {
      return EventPlaybackRejection.MediaIdTooLong;
    }
    // Checked before the separator rule so a pasted URL gets the precise reason.
    if (mediaId.Contains("://", StringComparison.Ordinal)
        || mediaId.StartsWith("//", StringComparison.Ordinal))
    {
      return EventPlaybackRejection.MediaIdLooksLikeUrl;
    }
    if (mediaId.Contains('/') || mediaId.Contains('\\'))
    {
      return EventPlaybackRejection.MediaIdHasPathSeparator;
    }
    if (mediaId is "." or "..")
    {
      return EventPlaybackRejection.MediaIdHasPathSeparator;
    }
    foreach (var c in mediaId)
    {
      if (char.IsControl(c) || char.IsWhiteSpace(c))
      {
        return EventPlaybackRejection.MediaIdHasControlCharacter;
      }
    }
    return EventPlaybackRejection.None;
  }

  /// <summary>Upper bound on a media identifier. Generous; the point is that it is bounded.</summary>
  public const int MaxMediaIdChars = 256;
}

/// <summary>Why a request was refused. None means it was acceptable.</summary>
public enum EventPlaybackRejection
{
  None = 0,
  UnknownKind,
  ArmMismatch,
  PriorityOutOfRange,
  MissingText,
  TextTooLong,
  MissingMediaKind,
  UnknownMediaKind,
  MissingMediaId,
  MediaIdTooLong,
  MediaIdLooksLikeUrl,
  MediaIdHasPathSeparator,
  MediaIdHasControlCharacter,
  NegativeDuration
}

/// <summary>
/// The state of the one attended playback, as an ANCHOR rather than a tick (ADR-029 §8.2).
///
/// <see cref="PositionAtBroadcast"/> plus <see cref="BroadcastAtUtc"/> plus <see cref="State"/>
/// is enough for a client to interpolate its own progress bar locally, which is why there is
/// deliberately no periodic position broadcast: a tick would put a timer on the server and a
/// message on the wire for every open client, continuously, on a box where CPU churn is audible.
/// </summary>
/// <param name="Id">The server-minted playbackId. See the identity note in Task 8.</param>
/// <param name="Duration">
/// Null while Preparing, and null when the provider reported duration 0 (unknown) — so the UI
/// renders an indeterminate bar rather than a confident lie.
/// </param>
public sealed record EventPlaybackSnapshot(
  string Id,
  EventPlaybackKind Kind,
  string? Label,
  EventPlaybackState State,
  TimeSpan? Duration,
  TimeSpan PositionAtBroadcast,
  DateTimeOffset BroadcastAtUtc,
  string? FailureReason);
```

**Acceptance:** the file compiles; nothing references it yet; no DI registration is added anywhere.

---

### Task 2 — nothing to build

*(Intentionally absent — validation ships inside Task 1's record. Numbering is kept so the task
ids in the queue row, the PR body and the review notes do not shift if validation is later split
out.)*

---

### Task 3 — Core tests for the contract, including the two SSRF pins

**File (new):** `tests/Radio.Core.Tests/EventPlaybackRequestTests.cs`

```csharp
using System.Reflection;
using Radio.Core.Interfaces.Audio;

namespace Radio.Core.Tests;

/// <summary>
/// Contract tests for ADR-029 D2 — the closed discriminated request set.
/// Two of these are security pins rather than behaviour tests; they are labelled as such.
/// </summary>
public class EventPlaybackRequestTests
{
  private static EventPlaybackRequest Speech(string text = "hello") =>
    new() { Kind = EventPlaybackKind.Speech, Text = text };

  private static EventPlaybackRequest Voicemail(string mediaId = "vm-abc123") =>
    new()
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaKind = RemoteMediaKind.GvVoicemail,
      MediaId = mediaId,
      DurationSeconds = 42
    };

  [Fact]
  public void Validate_AcceptsAWellFormedSpeechRequest()
  {
    Assert.Equal(EventPlaybackRejection.None, Speech().Validate());
  }

  [Fact]
  public void Validate_AcceptsAWellFormedVoicemailRequest()
  {
    Assert.Equal(EventPlaybackRejection.None, Voicemail().Validate());
  }

  [Fact]
  public void Validate_DefaultsPriorityToTheAttendedClass()
  {
    // ADR-029 §6.1 — 6, deliberately below the 8 this system uses for "did not state a rank".
    Assert.Equal(6, Speech().Priority);
  }

  // ── SSRF pin 1: a URL-bearing request is REFUSED ────────────────────────
  // ADR-029 D2 / §13: an endpoint that fetches a caller-supplied URL is an SSRF primitive.
  // The structural defence is that no URL field exists (pin 2); this is defence in depth for
  // the one string a caller does control.
  [Theory]
  [InlineData("http://169.254.169.254/latest/meta-data/")]
  [InlineData("https://evil.example/payload.mp3")]
  [InlineData("file:///etc/shadow")]
  [InlineData("//evil.example/payload.mp3")]
  public void Validate_RejectsAUrlBearingMediaId(string mediaId)
  {
    Assert.Equal(EventPlaybackRejection.MediaIdLooksLikeUrl, Voicemail(mediaId).Validate());
  }

  [Theory]
  [InlineData("../../etc/passwd")]
  [InlineData("a/b")]
  [InlineData("a\\b")]
  [InlineData(".")]
  [InlineData("..")]
  public void Validate_RejectsAMediaIdCarryingAPathSeparator(string mediaId)
  {
    Assert.Equal(EventPlaybackRejection.MediaIdHasPathSeparator, Voicemail(mediaId).Validate());
  }

  [Theory]
  [InlineData("vm abc")]
  [InlineData("vm\nabc")]
  [InlineData("vm\tabc")]
  [InlineData("vm\0abc")]
  public void Validate_RejectsAMediaIdCarryingWhitespaceOrControlCharacters(string mediaId)
  {
    Assert.Equal(EventPlaybackRejection.MediaIdHasControlCharacter, Voicemail(mediaId).Validate());
  }

  [Fact]
  public void Validate_RejectsAnOverlongMediaId()
  {
    var tooLong = new string('a', EventPlaybackRequest.MaxMediaIdChars + 1);
    Assert.Equal(EventPlaybackRejection.MediaIdTooLong, Voicemail(tooLong).Validate());
  }

  // ── SSRF pin 2: the type cannot carry a URL at all ──────────────────────
  // This is the structural property. If someone later adds AudioUrl to the request because
  // VoicemailItemDto has one, this test fails and says why.
  [Fact]
  public void EventPlaybackRequest_DeclaresNoUrlShapedProperty()
  {
    var offenders = typeof(EventPlaybackRequest)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.Name.Contains("Url", StringComparison.OrdinalIgnoreCase)
                  || p.Name.Contains("Uri", StringComparison.OrdinalIgnoreCase)
                  || p.PropertyType == typeof(Uri))
      .Select(p => p.Name)
      .ToList();

    Assert.True(
      offenders.Count == 0,
      "EventPlaybackRequest must never carry a URL (ADR-029 D2 - an endpoint that fetches a "
      + "caller-supplied URL is an SSRF primitive). Offending properties: "
      + string.Join(", ", offenders));
  }

  // ── The closed set ──────────────────────────────────────────────────────
  [Fact]
  public void Validate_RejectsASpeechRequestCarryingMediaFields()
  {
    var mixed = new EventPlaybackRequest
    {
      Kind = EventPlaybackKind.Speech,
      Text = "hello",
      MediaKind = RemoteMediaKind.GvVoicemail,
      MediaId = "vm-abc123"
    };

    Assert.Equal(EventPlaybackRejection.ArmMismatch, mixed.Validate());
  }

  [Fact]
  public void Validate_RejectsARemoteMediaRequestCarryingSpeechFields()
  {
    var mixed = new EventPlaybackRequest
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaKind = RemoteMediaKind.GvVoicemail,
      MediaId = "vm-abc123",
      Text = "hello"
    };

    Assert.Equal(EventPlaybackRejection.ArmMismatch, mixed.Validate());
  }

  [Fact]
  public void Validate_RejectsAnUndefinedKind()
  {
    var bogus = new EventPlaybackRequest { Kind = (EventPlaybackKind)99 };

    Assert.Equal(EventPlaybackRejection.UnknownKind, bogus.Validate());
  }

  [Fact]
  public void Validate_RejectsAnUndefinedMediaKind()
  {
    var bogus = new EventPlaybackRequest
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaKind = (RemoteMediaKind)99,
      MediaId = "vm-abc123"
    };

    Assert.Equal(EventPlaybackRejection.UnknownMediaKind, bogus.Validate());
  }

  [Fact]
  public void Validate_RejectsMissingMediaKind()
  {
    var noKind = new EventPlaybackRequest
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaId = "vm-abc123"
    };

    Assert.Equal(EventPlaybackRejection.MissingMediaKind, noKind.Validate());
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void Validate_RejectsEmptySpeech(string text)
  {
    var empty = new EventPlaybackRequest { Kind = EventPlaybackKind.Speech, Text = text };

    Assert.Equal(EventPlaybackRejection.MissingText, empty.Validate());
  }

  [Fact]
  public void Validate_RejectsSpeechOverTheCharacterCap()
  {
    var longText = new string('a', 1001);

    Assert.Equal(EventPlaybackRejection.TextTooLong, Speech(longText).Validate(maxSpeechChars: 1000));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(11)]
  public void Validate_RejectsAPriorityOutsideOneToTen(int priority)
  {
    var request = Speech() with { Priority = priority };

    Assert.Equal(EventPlaybackRejection.PriorityOutOfRange, request.Validate());
  }

  [Fact]
  public void Validate_AcceptsDurationZeroAsUnknown()
  {
    // ADR-022 §4.2 / ADR-029 §4.1 — 0 means UNKNOWN, not invalid.
    var unknownDuration = Voicemail() with { DurationSeconds = 0 };

    Assert.Equal(EventPlaybackRejection.None, unknownDuration.Validate());
  }

  [Fact]
  public void Validate_RejectsANegativeDuration()
  {
    var negative = Voicemail() with { DurationSeconds = -1 };

    Assert.Equal(EventPlaybackRejection.NegativeDuration, negative.Validate());
  }

  [Fact]
  public void RemoteMediaKind_IsAClosedSetOfOne()
  {
    // If this fails, someone added a media kind. That is fine - but the server must also have
    // gained a URL template for it in its OWN configuration (ADR-029 D2).
    Assert.Single(Enum.GetValues<RemoteMediaKind>());
    Assert.Equal(RemoteMediaKind.GvVoicemail, Enum.GetValues<RemoteMediaKind>()[0]);
  }

  [Fact]
  public void EventPlaybackState_CarriesTheSixLifecycleStates()
  {
    var states = Enum.GetValues<EventPlaybackState>();

    Assert.Contains(EventPlaybackState.Preparing, states);
    Assert.Contains(EventPlaybackState.Playing, states);
    Assert.Contains(EventPlaybackState.Paused, states);
    Assert.Contains(EventPlaybackState.Completed, states);
    Assert.Contains(EventPlaybackState.Stopped, states);
    Assert.Contains(EventPlaybackState.Failed, states);
  }
}
```

**Acceptance:** all tests pass. `dotnet test --filter "FullyQualifiedName~EventPlaybackRequestTests"`.

---

### Task 4 — give `SoundFlowPlaybackService` a real seek, and make `GetPosition` tell the truth

**File:** `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs`

This is the task §0.4 C-1 added. `_activePlayers` is `Dictionary<string, SoundPlayer>`, and
`SoundPlayer` inherits `SoundPlayerBase`, which has the members quoted in C-2.

**4a — replace the whole `GetPosition` method.** Find it at `:714-725` and replace it in full:

```csharp
  /// <summary>
  /// Gets the current playback position for a source.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <returns>The current position, or null when no player is registered under this id.</returns>
  public TimeSpan? GetPosition(string sourceId)
  {
    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        // SoundPlayerBase.Time is a float, in SECONDS.
        return TimeSpan.FromSeconds(player.Time);
      }
    }
    return null;
  }
```

⚠ The comment being deleted here — `// Position tracking not available in current SoundFlow API` —
was **false** for the referenced package (§0.4 C-3). Do not preserve it. The method has zero
callers today, so this changes no existing behaviour.

**4b — add `Seek` immediately after `Resume` (`:585-596`), beside its siblings.**

```csharp
  /// <summary>
  /// Seeks a source to an absolute position from the start of its content.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="position">The position to seek to, from the beginning of the content.</param>
  /// <returns>
  /// True when the player repositioned. False when no player is registered under this id, OR
  /// when the player's data provider refused the seek — SoundPlayerBase.Seek returns a bool and
  /// this method propagates it rather than reporting an unconditional success. A caller's
  /// IsSeekable contract depends on that difference being visible.
  /// </returns>
  public bool Seek(string sourceId, TimeSpan position)
  {
    ThrowIfDisposed();

    if (position < TimeSpan.Zero)
    {
      return false;
    }

    lock (_playersLock)
    {
      if (!_activePlayers.TryGetValue(sourceId, out var player))
      {
        return false;
      }

      // SoundPlayerBase.Seek(TimeSpan, SeekOrigin = Begin).
      var moved = player.Seek(position);
      _logger.LogDebug(
        "Seek for source {SourceId} to {Position} returned {Moved}", sourceId, position, moved);
      return moved;
    }
  }
```

**Acceptance:** the solution builds with zero warnings in Release.

---

### Task 5 — the tests Task 4 can actually carry, and an honest note about the ones it cannot

**File (new):** `tests/Radio.Infrastructure.Tests/Audio/SoundFlow/SoundFlowPlaybackServiceTransportTests.cs`

⚠ **Read this before writing the file.** `SoundFlowPlaybackService` takes a concrete
`SoundFlowAudioEngine`, and a registered `SoundPlayer` only exists after a real `PlayFileAsync`
against a real device. **The populated-dictionary paths are not unit-testable on a build agent** —
they are covered by PR 6's UAT on the box, and §2.2 says so plainly. What *is* testable is the
unregistered-id contract, which is the half that a caller branches on.

If `SoundFlowPlaybackService` cannot be constructed in this test project without touching audio
hardware, **do not fake it and do not weaken it** — delete this task's file, and record in the PR
body that Task 4 is covered by build plus PR 6's UAT only. Half a test that constructs a fake
engine is worse than an honest gap.

```csharp
using Microsoft.Extensions.Logging;
using Moq;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Covers the no-player-registered contract of the transport methods added by ADR-029 PR 1.
/// The populated-dictionary paths need a real device and are exercised by UAT (see the plan
/// Test Plan §2.2), not here.
/// </summary>
public class SoundFlowPlaybackServiceTransportTests
{
  [Fact]
  public void GetPosition_ReturnsNull_WhenNoPlayerIsRegistered()
  {
    var service = CreateService();

    Assert.Null(service.GetPosition("no-such-source"));
  }

  [Fact]
  public void Seek_ReturnsFalse_WhenNoPlayerIsRegistered()
  {
    var service = CreateService();

    Assert.False(service.Seek("no-such-source", TimeSpan.FromSeconds(5)));
  }

  [Fact]
  public void Seek_ReturnsFalse_ForANegativePosition()
  {
    var service = CreateService();

    Assert.False(service.Seek("no-such-source", TimeSpan.FromSeconds(-1)));
  }

  private static SoundFlowPlaybackService CreateService()
  {
    // The engine is never started; these three assertions never reach a device.
    var engine = new SoundFlowAudioEngine(
      Mock.Of<ILogger<SoundFlowAudioEngine>>(),
      /* fill in the remaining constructor arguments from the current signature */);

    return new SoundFlowPlaybackService(
      Mock.Of<ILogger<SoundFlowPlaybackService>>(),
      engine);
  }
}
```

⚠ `CreateService` is the one place in this plan with a deliberate hole: `SoundFlowAudioEngine`'s
constructor changes more often than anything else in this area, so Builder fills it from the
current signature. **If it cannot be built without starting a device, apply the delete-the-file
instruction above.** Do not invent a shim.

**Acceptance:** either three passing tests, or the file is absent and the PR body says why.

---

### Task 6 — extend `IEventAudioSource` with the five transport members

**File:** `src/Radio.Core/Interfaces/Audio/IEventAudioSource.cs`

Replace the interface body. The five new members are copied **verbatim** from
`IPrimaryAudioSource` (`:19`, `:24`, `:47`, `:54`, `:71`) — same names, same signatures, same
defaults — because D4 is a lift, not a design. **`Duration` stays `TimeSpan`** (§0.4 C-11).

```csharp
public interface IEventAudioSource : IAudioSource
{
  /// <summary>
  /// Gets the duration of the event audio.
  /// Non-nullable, unlike IPrimaryAudioSource.Duration: an event always has a length, and the
  /// "unknown duration" case lives on EventPlaybackSnapshot.Duration instead (ADR-029 §4.1).
  /// </summary>
  TimeSpan Duration { get; }

  /// <summary>
  /// Gets the current playback position.
  /// </summary>
  TimeSpan Position { get; }

  /// <summary>
  /// Gets whether seeking is supported for this source.
  /// </summary>
  bool IsSeekable { get; }

  /// <summary>
  /// Starts playback of the event audio.
  /// </summary>
  Task PlayAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Pauses playback while maintaining the current position.
  /// </summary>
  Task PauseAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Resumes playback from the paused position.
  /// </summary>
  Task ResumeAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops playback of the event audio.
  /// </summary>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Seeks to a specific position in the audio content.
  /// Only valid if <see cref="IsSeekable"/> is true.
  /// </summary>
  Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

  /// <summary>
  /// Raised when playback completes.
  /// </summary>
  event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;
}
```

**Acceptance:** `Radio.Core` compiles. `Radio.Infrastructure` will not compile until Task 7 —
that is expected, and Tasks 6 and 7 land in one commit.

---

### Task 7 — `EventAudioSourceBase` gains the template methods

**File:** `src/Radio.Infrastructure/Audio/Sources/Events/EventAudioSourceBase.cs`

Mirror `PrimaryAudioSourceBase` exactly (`:109-146` and its `*CoreAsync` hooks) so the two
hierarchies read the same way. `ThrowIfDisposed()`, `Logger` and the protected `State` setter are
all on `AudioSourceBase` (`:231`, `:239`, `:39-53`).

Add after the existing `public abstract TimeSpan Duration { get; }`:

```csharp
  /// <inheritdoc/>
  /// <remarks>
  /// Defaults to zero. An implementer that can report a real position overrides this;
  /// TTSEventSource deliberately does not.
  /// </remarks>
  public virtual TimeSpan Position => TimeSpan.Zero;

  /// <inheritdoc/>
  /// <remarks>
  /// Defaults to FALSE, and the default is the honest one: SeekAsync throws NotSupportedException
  /// unless an implementer both overrides this to true AND overrides SeekCoreAsync. A source that
  /// claims IsSeekable without repositioning any audio is the exact defect CLAUDE.md's pre-merge
  /// rule exists for.
  /// </remarks>
  public virtual bool IsSeekable => false;

  /// <inheritdoc/>
  public virtual async Task PauseAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State != AudioSourceState.Playing)
    {
      Logger.LogWarning("Cannot pause {SourceId} - not playing (state: {State})", Id, State);
      return;
    }

    await PauseCoreAsync(cancellationToken);
    State = AudioSourceState.Paused;
  }

  /// <inheritdoc/>
  public virtual async Task ResumeAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State != AudioSourceState.Paused)
    {
      Logger.LogWarning("Cannot resume {SourceId} - not paused (state: {State})", Id, State);
      return;
    }

    await ResumeCoreAsync(cancellationToken);
    State = AudioSourceState.Playing;
  }

  /// <inheritdoc/>
  public virtual async Task SeekAsync(
    TimeSpan position,
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!IsSeekable)
    {
      throw new NotSupportedException($"Audio source {Id} does not support seeking.");
    }

    await SeekCoreAsync(position, cancellationToken);
  }

  /// <summary>
  /// Implementation hook for <see cref="PauseAsync"/>. Called only when the source is Playing.
  /// </summary>
  protected virtual Task PauseCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  /// <summary>
  /// Implementation hook for <see cref="ResumeAsync"/>. Called only when the source is Paused.
  /// </summary>
  protected virtual Task ResumeCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  /// <summary>
  /// Implementation hook for <see cref="SeekAsync"/>. Called only when <see cref="IsSeekable"/>.
  /// </summary>
  protected virtual Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
    => throw new NotSupportedException($"Audio source {Id} does not support seeking.");
```

**Acceptance:** `Radio.Infrastructure` compiles; both event sources inherit the defaults; no
existing behaviour changes because nothing calls the new members yet.

---

### Task 8 — `AudioFileEventSource`: real position, real seek, real pause, and a completion that survives all three

**File:** `src/Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs`

This is the largest task. Four edits.

**8a — add two fields** beside the existing ones (`:13-20`):

```csharp
  private CancellationTokenSource _transportCts = new();
  private readonly object _transportLock = new();
```

**8b — add the two properties**, immediately after `public override TimeSpan Duration => _duration;`
(`:71`):

```csharp
  /// <inheritdoc/>
  /// <remarks>
  /// Read from the player rather than tracked here, so it stays correct across a seek. Falls back
  /// to zero when there is no player — which is also the state before playback starts.
  /// </remarks>
  public override TimeSpan Position =>
    _playbackService is not null && _playbackId is not null
      ? _playbackService.GetPosition(_playbackId) ?? TimeSpan.Zero
      : TimeSpan.Zero;

  /// <inheritdoc/>
  /// <remarks>
  /// True only on the file-path arm with a live playback service. The stream constructor is
  /// excluded deliberately: SoundFlow's StreamDataProvider is built over whatever stream it is
  /// handed, and a non-seekable stream would make Seek report false at runtime. Claiming
  /// IsSeekable and then failing is worse than reporting false.
  /// </remarks>
  public override bool IsSeekable =>
    _playbackService is not null
    && _playbackId is not null
    && !string.IsNullOrEmpty(_filePath);
```

**8c — add the three `*CoreAsync` overrides.** Put them immediately before `StopCoreAsync`
(`:245`):

```csharp
  /// <inheritdoc/>
  protected override Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
  {
    if (position < TimeSpan.Zero || position > _duration)
    {
      throw new ArgumentOutOfRangeException(nameof(position), "Seek position out of range");
    }

    var moved = _playbackService!.Seek(_playbackId!, position);
    if (!moved)
    {
      Logger.LogWarning(
        "Seek to {Position} was refused by the player for {Name}", position, _name);
    }

    // Re-arm the completion wait either way: on a refusal nothing moved, so recomputing simply
    // restores the same deadline.
    SignalTransportChange();
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    if (_playbackService is not null && _playbackId is not null)
    {
      _playbackService.Pause(_playbackId);
    }

    // The completion wait must stop counting: a paused source consumes no audio, so it has no
    // deadline. Without this, a pause longer than the remaining audio fires EndOfContent on a
    // source that is silent and unfinished.
    SignalTransportChange();
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    if (_playbackService is not null && _playbackId is not null)
    {
      _playbackService.Resume(_playbackId);
    }

    SignalTransportChange();
    return Task.CompletedTask;
  }

  /// <summary>
  /// Cancels the current completion wait so it recomputes its deadline from the player's real
  /// position. Called by every transport override. Not a timer and not a poll - it fires only
  /// on a user action.
  /// </summary>
  private void SignalTransportChange()
  {
    CancellationTokenSource previous;
    lock (_transportLock)
    {
      previous = _transportCts;
      _transportCts = new CancellationTokenSource();
    }

    previous.Cancel();
    previous.Dispose();
  }

  /// <summary>
  /// Waits until the content is finished, re-arming whenever transport moves.
  ///
  /// Replaces a single wall-clock Task.Delay(_duration): that delay was correct only for a
  /// playback that is never sought and never paused, which is exactly what ADR-029 stops being
  /// true. If GetPosition yields nothing, remaining falls back to the full duration and the
  /// behaviour is identical to the delay it replaces.
  /// </summary>
  private async Task AwaitCompletionAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      CancellationTokenSource transport;
      lock (_transportLock)
      {
        transport = _transportCts;
      }

      TimeSpan remaining;
      if (State == AudioSourceState.Paused)
      {
        // No deadline while paused - wait for the next transport event instead.
        remaining = Timeout.InfiniteTimeSpan;
      }
      else
      {
        var position = _playbackService?.GetPosition(_playbackId!) ?? TimeSpan.Zero;
        remaining = _duration - position;
        if (remaining <= TimeSpan.Zero)
        {
          return;
        }
      }

      using var linked =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, transport.Token);
      try
      {
        await Task.Delay(remaining, linked.Token);
        return;
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        // A transport event landed. Recompute and wait again.
      }
    }
  }
```

**8d — use it.** In `PlayWithSoundFlowAsync`, replace these three lines (`:203-205`):

```csharp
      // Wait for playback to complete (based on duration)
      // In a full implementation, we would listen for playback end events from SoundFlow
      await Task.Delay(_duration, cancellationToken);
```

with:

```csharp
      // Wait for the content to finish. Position-driven and re-armed by transport, so a seek or
      // a pause does not make completion fire at the wrong time (ADR-029 §14 Q4).
      await AwaitCompletionAsync(cancellationToken);
```

⚠ **Leave `PlaybackLoopAsync` (`:227-242`) exactly as it is.** It is the no-playback-service
simulation path; there is no player to ask for a position and nothing to seek.

> **Identity note, carried from ADR-029 §3.3 — do not act on it here.** This source mints **two**
> ids: the public `IAudioSource.Id` (`AudioSourceBase.cs:28`, `"{Type}-{guid}"`) and the internal
> `_playbackId` (`:112`, `"audio-event-{guid}"`) used as the `SoundFlowPlaybackService` key. They
> are **not equal**, whereas `TTSEventSource` uses `Id` directly as its playback key. Everything
> added above correctly uses `_playbackId`. **PR 3's `EventPlaybackService` must mint and own its
> own `playbackId` and map it to the source** rather than assuming the two coincide — a
> cancel-by-id API built on the wrong one fails for exactly one of the two arms.

**Acceptance:** builds clean; Task 10's tests pass.

---

### Task 9 — `TTSEventSource`: pause without a false completion

**File:** `src/Radio.Infrastructure/Audio/Sources/Events/TTSEventSource.cs`

Per §0.4 C-6 this source **must not** simply inherit the base pause: its monitor loop reads a
paused player as a finished one. Three edits.

**9a — add one field** beside the existing ones:

```csharp
  private volatile bool _isPaused;
```

**9b — add the two overrides.** Seek is deliberately not overridden: `IsSeekable` stays false from
the base, so `SeekAsync` throws `NotSupportedException` — seeking inside a spoken message has no
user value (ADR-029 §8.3), and a no-op that reported success would be a lie.

```csharp
  /// <inheritdoc/>
  /// <remarks>
  /// The _isPaused flag is not decoration. StartPlaybackWithMonitoringAsync treats
  /// !IsPlaying(Id) as natural completion, and SoundFlowPlaybackService.IsPlaying is
  /// player.State == PlaybackState.Playing - which a PAUSED player fails. Without this flag a
  /// pause would raise PlaybackCompleted(EndOfContent) and drive the source to Stopped.
  /// </remarks>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    _isPaused = true;
    _playbackService?.Pause(Id);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    _isPaused = false;
    _playbackService?.Resume(Id);
    return Task.CompletedTask;
  }
```

**9c — make the monitor loop pause-aware.** Inside `StartPlaybackWithMonitoringAsync`, replace the
body of the `while (!cancellationToken.IsCancellationRequested)` loop's first two blocks
(`:167-186`) so the completion check and the safety timeout both skip while paused:

```csharp
      while (!cancellationToken.IsCancellationRequested)
      {
        if (_isPaused)
        {
          // A paused player reports IsPlaying == false and accrues no audio. Neither the
          // completion check nor the duration safety net applies while paused.
          await Task.Delay(checkInterval, cancellationToken);
          continue;
        }

        // Check if playback is still active
        if (!_playbackService.IsPlaying(Id))
        {
          // Playback finished naturally
          Logger.LogDebug("TTS playback completed naturally after {Elapsed}", elapsed);
          State = AudioSourceState.Stopped;
          OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
          return;
        }

        // Safety check: if we've exceeded expected duration by 50%, stop
        if (elapsed > _duration + TimeSpan.FromSeconds(_duration.TotalSeconds * 0.5 + 1))
        {
          Logger.LogWarning("TTS playback exceeded expected duration, stopping");
          await _playbackService.StopAsync(Id, cancellationToken);
          State = AudioSourceState.Stopped;
          OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
          return;
        }

        await Task.Delay(checkInterval, cancellationToken);
        elapsed += checkInterval;
      }
```

⚠ Note what did **not** change: the loop's cadence, its cancellation behaviour, and every existing
branch. The `_isPaused` branch is **unreachable until something calls `PauseAsync`, and nothing
does before PR 3** — so this is additive to the live announcement path, not a change to it. Say so
in the PR body; a reviewer looking at a diff inside the announcement path is right to be careful.

**Acceptance:** builds clean; Task 11's tests pass; existing `TTSEventSourceTests` still pass
unchanged.

---

### Task 10 — `AudioFileEventSource` transport tests

**File:** `tests/Radio.Infrastructure.Tests/Audio/Events/AudioFileEventSourceTests.cs` (extend)

The existing helpers `CreateSource` and `CreateSourceFromStream` construct the source with **no**
playback service, which is exactly the shape needed for the not-seekable assertions. Append:

```csharp
  [Fact]
  public void Position_IsZero_WhenThereIsNoPlaybackService()
  {
    var source = CreateSource();

    Assert.Equal(TimeSpan.Zero, source.Position);
  }

  [Fact]
  public void IsSeekable_IsFalse_WhenThereIsNoPlaybackService()
  {
    var source = CreateSource();

    Assert.False(source.IsSeekable);
  }

  [Fact]
  public void IsSeekable_IsFalse_ForTheStreamConstructor()
  {
    // The stream arm is excluded deliberately - SoundFlow's StreamDataProvider is built over
    // whatever stream it is given, and a non-seekable stream would make Seek fail at runtime.
    var source = CreateSourceFromStream();

    Assert.False(source.IsSeekable);
  }

  [Fact]
  public async Task SeekAsync_Throws_WhenTheSourceIsNotSeekable()
  {
    var source = CreateSource();

    await Assert.ThrowsAsync<NotSupportedException>(
      () => source.SeekAsync(TimeSpan.FromSeconds(1)));
  }

  [Fact]
  public async Task PauseAsync_IsANoOp_WhenTheSourceIsNotPlaying()
  {
    var source = CreateSource();

    await source.PauseAsync();

    Assert.NotEqual(AudioSourceState.Paused, source.State);
  }

  [Fact]
  public async Task ResumeAsync_IsANoOp_WhenTheSourceIsNotPaused()
  {
    var source = CreateSource();

    await source.ResumeAsync();

    Assert.NotEqual(AudioSourceState.Playing, source.State);
  }
```

⚠ **What these cannot cover, and why that is stated rather than faked:** the seeking arm needs a
registered `SoundPlayer`, which needs a real device. `IsSeekable == true`, a successful
`SeekCoreAsync`, and the re-armed completion wait are **not** unit-testable here. §2.2 records
them as UAT items on PR 6. Do not add a fake `SoundFlowPlaybackService` to reach them — it is a
concrete class, and a fake would test the fake.

**Acceptance:** the six new tests pass and the existing file still passes.

---

### Task 11 — `TTSEventSource` transport tests

**File:** `tests/Radio.Infrastructure.Tests/Audio/Events/TTSEventSourceTests.cs` (extend)

Reuse whatever construction helper the file already has. Append:

```csharp
  [Fact]
  public void IsSeekable_IsFalse()
  {
    // Seeking inside a spoken message has no user value (ADR-029 §8.3). False here is the
    // honest answer, and it is what makes SeekAsync throw rather than silently no-op.
    var source = CreateSource();

    Assert.False(source.IsSeekable);
  }

  [Fact]
  public async Task SeekAsync_Throws()
  {
    var source = CreateSource();

    await Assert.ThrowsAsync<NotSupportedException>(
      () => source.SeekAsync(TimeSpan.FromSeconds(1)));
  }

  [Fact]
  public void Position_IsZero()
  {
    var source = CreateSource();

    Assert.Equal(TimeSpan.Zero, source.Position);
  }

  [Fact]
  public async Task PauseAsync_IsANoOp_WhenTheSourceIsNotPlaying()
  {
    var source = CreateSource();

    await source.PauseAsync();

    Assert.NotEqual(AudioSourceState.Paused, source.State);
  }
```

⚠ The C-6 defect itself — that a pause would otherwise be read as completion — needs a live
player, so it is **not** provable here. It is provable on the box, and §2.2 lists it as the one
PR 6 UAT step that exists specifically for this task.

**Acceptance:** the four new tests pass and the existing file still passes.

---

### Task 12 — pin today's ducking behaviour, so PR 4's change is a visible diff

**File (new):** `tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs`

This task exists for **PR 4**, the arc's sharpest change. PR 4 is the first load-bearing use of a
subsystem that is currently decorative, and today's behaviour is written down in prose in three
documents but pinned by no test. These tests assert what the code does **now**. PR 4 will change
them — deliberately, in its own diff, where a reviewer can see it.

**Verified before writing this task, against the current tree:**

- `DuckingService.StartDuckingAsync` (`:96-144`) computes `needsTransition = !_isDucking` at `:108`
  and raises `DuckingStateChanged` only inside `if (needsTransition)` (`:125-137`, raise at
  `:136`). A second concurrent event reaches only a `LogDebug` at `:138-143`.
- `GetActiveEventsByPriority()` has **zero non-test callers** — `DuckingService.cs:262`
  (definition), `IDuckingService.cs:70` (declaration), `DuckingServiceTests.cs:375,392` (tests).
- `StopAllDuckingAsync()` has **zero non-test callers** — `:197`, `IDuckingService.cs:49`,
  `DuckingServiceTests.cs:255,266,489,496`.
- `GetPriority` has exactly **one** production read site: `DuckingService.cs:267`, inside
  `GetActiveEventsByPriority`, as `.OrderByDescending(e => GetPriority(e))`.
- The only non-test subscriber to `DuckingStateChanged` is `AudioManager` (`:63` subscribe,
  `:490-515` handler, `:533` unsubscribe), and the handler acts **only** on `!e.IsDucking`
  (`ClearDuckingMultiplier` at `:508`).
- `design/INTEGRATIONS.md:566` **already strikes** the false claim in place and records the
  mechanism. **No doc correction is owed by this PR** — the ADR's ⟨A1·6⟩ note pointed at `:464`,
  which is line drift, not a missing fix (§0.4 C-9).

Write the tests against `DuckingServiceTests`' existing construction helper in the same folder.

```csharp
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// CHARACTERIZATION tests: these assert what DuckingService does TODAY, not what it should do.
///
/// They exist for ADR-029 D5 / PHN arc PR 4, which makes priority load-bearing for the first
/// time in this system. Today ducking is binary and reference-counted: the first event fades the
/// primary to the fixed global Audio:DuckingPercentage and every subsequent concurrent event
/// changes nothing. PR 4 must change the second assertion below - and when it does, the change
/// appears as an edited test in PR 4's own diff rather than as a silent behavioural shift inside
/// a shared audio service.
///
/// ⚠ If you are reading this in PR 4: update these, do not delete them.
/// </summary>
public class DuckingServiceCharacterizationTests
{
  [Fact]
  public async Task StartDuckingAsync_RaisesDuckingStateChanged_OnTheFirstEvent()
  {
    var service = CreateService();
    var raised = 0;
    service.DuckingStateChanged += (_, _) => raised++;

    await service.StartDuckingAsync(CreateEventSource("event-1"));

    Assert.Equal(1, raised);
  }

  [Fact]
  public async Task StartDuckingAsync_DoesNotRaise_ForASecondConcurrentEvent_TODAY()
  {
    // ⚠ ADR-029 §6.3 requires this to become 2. That is PR 4's change, and this assertion is
    // where it becomes visible. DuckingService.cs:108 computes needsTransition = !_isDucking,
    // and the raise at :136 sits inside if (needsTransition); the second event reaches only the
    // LogDebug at :138-143.
    var service = CreateService();
    await service.StartDuckingAsync(CreateEventSource("event-1"));

    var raisedAfterFirst = 0;
    service.DuckingStateChanged += (_, _) => raisedAfterFirst++;

    await service.StartDuckingAsync(CreateEventSource("event-2"));

    Assert.Equal(0, raisedAfterFirst);
  }

  [Fact]
  public async Task SetPriority_DoesNotChangeTheDuckLevel_TODAY()
  {
    // Priority currently arbitrates nothing: the duck target is the fixed global
    // Audio:DuckingPercentage regardless of the priorities involved. INTEGRATIONS.md:566
    // records the same finding in the doc.
    var service = CreateService();
    var low = CreateEventSource("event-low");
    var high = CreateEventSource("event-high");

    service.SetPriority(low, 2);
    await service.StartDuckingAsync(low);
    var levelWithLowPriority = service.CurrentDuckLevel;

    service.SetPriority(high, 10);
    await service.StartDuckingAsync(high);

    Assert.Equal(levelWithLowPriority, service.CurrentDuckLevel);
  }

  [Fact]
  public async Task ActiveEventCount_IsReferenceCounted()
  {
    var service = CreateService();
    var first = CreateEventSource("event-1");
    var second = CreateEventSource("event-2");

    await service.StartDuckingAsync(first);
    await service.StartDuckingAsync(second);
    Assert.Equal(2, service.ActiveEventCount);

    await service.StopDuckingAsync(first);
    Assert.Equal(1, service.ActiveEventCount);
    Assert.True(service.IsDucking);

    await service.StopDuckingAsync(second);
    Assert.Equal(0, service.ActiveEventCount);
    Assert.False(service.IsDucking);
  }

  // CreateService / CreateEventSource: reuse the helpers already in DuckingServiceTests.cs in
  // this folder. If they are private there, lift them into a shared internal helper rather than
  // duplicating them - two copies of a ducking fixture is how the two get to disagree.
}
```

**Acceptance:** four passing tests. **If any of them fails as written, stop and report it** — it
means the behaviour drifted from what this plan verified on 2026-09-02, and PR 4's premise needs
re-checking before it is planned.

---

### Task 13 — record the two defects this PR deliberately does not fix

**File:** `design/FUTURE-WORK.md` — append a new numbered section, following the file's existing
shape (what exists / what is needed / gotchas / priority).

Content to write:

- **Title:** `FilePlayerAudioSource.IsSeekable claims a seek that does not move any audio`
- **What exists:** `FilePlayerAudioSource.cs:119` declares `IsSeekable => true`;
  `SeekCoreAsync` (`:910-922`) range-checks its argument and then assigns `_position = position`,
  which is the same field `Position` (`:116`) reads back. So `/api/audio` and the UI report a new
  position while the audio keeps playing from where it was. Found while planning `PHN-1a`
  (ADR-029 PR 1); ADR-029 §8.3 asserts the opposite and is wrong.
- **What is needed:** `SoundFlowPlaybackService.Seek(sourceId, position)` — **added by `PHN-1a`
  Task 4** — called from `SeekCoreAsync`, with `_position` becoming a read-through to
  `GetPosition` rather than an independently tracked field. Roughly ten lines.
- **Gotchas:** this is a **live primary-source path** — the `/queue` scrubber and the persisted
  resume position (`StopCoreAsync` writes `_preferences.CurrentValue.SongPositionMs` at `:903`)
  both read that field, so the change needs its own UAT on the box rather than a ride-along.
- **Priority:** medium. Nothing is broken that was working; a capability the UI advertises simply
  does not work, and has not since it shipped.
- **Second entry, same section:** `SoundFlowPlaybackService.GetPosition` returned `TimeSpan.Zero`
  behind the comment *"Position tracking not available in current SoundFlow API"* until `PHN-1a`
  Task 4. The comment was false — `ISoundPlayer.Time` exists in SoundFlow 1.4.x. Recorded as the
  **fifth** instance of the `CLAUDE.md` § Pre-Merge Review failure class, beside the four already
  catalogued, because the pattern is the point.

⚠ Do **not** edit `design/INTEGRATIONS.md` in this PR. Its ducking claim is already corrected in
place at `:566`, and PR 1 changes no integration service.

**Acceptance:** the section exists, names real file:line references, and states priority.

---

### Task 14 — build, test and the no-behaviour-change gate

Run and paste the output into the PR body. **Do not claim any of this without the output.**

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```

Then assert each of the following explicitly in the PR body:

1. **Release build: zero warnings.** Warnings are errors in Release; a nullable warning in the new
   `Validate` switch is the likeliest failure.
2. **No DI registration was added.** `git diff --stat` shows **no** change to
   `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`,
   `src/Radio.API/Program.cs`, or `src/Radio.Web/Program.cs`.
3. **No constructor signature changed.** `AudioFileEventSource`, `TTSEventSource`,
   `AudioFileEventSourceFactory` and `SoundFlowPlaybackService` all keep their current parameter
   lists — this is what makes §0.6's DI hazard inapplicable to PR 1.
4. **No controller changed.** `src/Radio.API/Controllers/` is untouched.
5. **The pre-merge comment-accuracy check, run against this diff specifically.** Every new
   `<remarks>` in Tasks 7, 8 and 9 states a *reason a thing is safe*, and `CLAUDE.md` is explicit
   that the reason is the claim to check. In particular verify, by reading the code rather than by
   reading the comment: `IsSeekable => false` on `TTSEventSource` really does make `SeekAsync`
   throw; the `_isPaused` branch really is unreachable without a `PauseAsync` caller; and
   `AwaitCompletionAsync` really does degrade to `Task.Delay(_duration)` when `GetPosition`
   yields nothing.

---

## 2. Test Plan

### 2.1 What the automated tests actually prove

| Claim | Proved by |
|---|---|
| A URL-bearing voicemail request is refused, with a named reason | `EventPlaybackRequestTests.Validate_RejectsAUrlBearingMediaId` (4 cases) |
| A path-traversal or separator-bearing id is refused | `Validate_RejectsAMediaIdCarryingAPathSeparator` (5 cases) |
| The request type **cannot** carry a URL at all | `EventPlaybackRequest_DeclaresNoUrlShapedProperty` (reflection) |
| The arms are closed, not merely optional | `Validate_RejectsASpeechRequestCarryingMediaFields`, `..._RejectsARemoteMediaRequestCarryingSpeechFields` |
| `RemoteMediaKind` is a closed set of one | `RemoteMediaKind_IsAClosedSetOfOne` |
| Duration 0 means unknown, not invalid | `Validate_AcceptsDurationZeroAsUnknown` |
| Attended playback defaults to priority 6 | `Validate_DefaultsPriorityToTheAttendedClass` |
| A non-seekable event source throws rather than silently no-opping | `AudioFileEventSourceTests.SeekAsync_Throws_WhenTheSourceIsNotSeekable`, `TTSEventSourceTests.SeekAsync_Throws` |
| The stream arm of `AudioFileEventSource` does not claim seekability | `IsSeekable_IsFalse_ForTheStreamConstructor` |
| Pause/resume are no-ops from the wrong state | `PauseAsync_IsANoOp_WhenTheSourceIsNotPlaying` and siblings |
| `Seek`/`GetPosition` return false/null for an unregistered id | `SoundFlowPlaybackServiceTransportTests` (if constructible — see Task 5) |
| Ducking is binary and transition-only **today** | `DuckingServiceCharacterizationTests`, 4 tests |
| Nothing else regressed | the full suite, ~1,700 tests |

### 2.2 What tests cannot prove — and PR 1 is largely backend, so this list is short but real

PR 1 ships **no user-visible surface**, so there is no browser UAT to run and no screenshot to
take. That is not the same as "fully verified by tests." Three things genuinely require a real
audio device, and all three are **carried forward to PR 6's UAT on the box** rather than claimed
here:

1. **That `SoundPlayerBase.Seek` actually repositions a short local MP3.** ADR-029 §14 Q3 asked
   this. Planning proved the *method exists and returns a bool*; only a device proves the audio
   moves. **If it does not, the fallback is unchanged from Q3's** — degrade to
   stop-and-restart-at-offset, which is workable for a ~1 MB local file. Nothing in PR 1's shape
   changes either way.
2. **That `SoundPlayerBase.Time` advances during playback.** `Position` and the re-armed
   completion wait both read it. If it turned out to be pinned at zero, `AwaitCompletionAsync`
   degrades to exactly today's `Task.Delay(_duration)` — by design (§0.5) — so the failure is a
   loss of accuracy, not a hang.
3. **That pausing a TTS source no longer reports completion (§0.4 C-6).** The unit tests can only
   reach the not-playing guard; the defect lives in the live monitor loop.

**How to check all three on the box, once PR 3 gives them a route** — recorded now so PR 6's UAT
does not have to re-derive it:

```bash
# after PR 3, from the box; ids from GET /api/gvbridge/voicemail
curl -s -X POST http://radio:5000/api/audio/events \
  -H 'Content-Type: application/json' \
  -d '{"kind":"RemoteMedia","mediaKind":"GvVoicemail","mediaId":"<id>","durationSeconds":<n>}'
curl -s http://radio:5000/api/audio/events/current      # position should advance between calls
```

### 2.3 Commands

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
dotnet test --filter "FullyQualifiedName~EventPlaybackRequestTests"
dotnet test --filter "FullyQualifiedName~AudioFileEventSourceTests"
dotnet test --filter "FullyQualifiedName~TTSEventSourceTests"
dotnet test --filter "FullyQualifiedName~DuckingServiceCharacterizationTests"
```

⚠ `TEST-1` shipped (#483), so a green run no longer silently depends on whether `radio-api`
happens to be up. Trust the suite.

---

## 3. Self-review

**Spec coverage against ADR-029's PR 1 scope (D1, D2, D4):**

| ADR item | Task |
|---|---|
| D1 — `IEventPlaybackService` in Core, beside `IAnnouncementService` | 1, and §0.2 carries the reasoning |
| D1 — the handle-returning signature set | 1 |
| D2 — closed discriminated set, asymmetric arms | 1, 3 |
| D2 — never a caller-supplied URL | 1 (`Validate`, and the absence of a URL field), 3 (both pins) |
| D4 — `Position`, `IsSeekable`, `SeekAsync`, `PauseAsync`, `ResumeAsync` on `IEventAudioSource` | 6, 7 |
| D4 — implemented on `AudioFileEventSource` | 8 |
| D4 — implemented on `TTSEventSource` | 9 |
| §4.1 — `DurationSeconds` passthrough as a correctness fix | 1 (field + validation); consumed in PR 3 |
| §8.2 — snapshot is an anchor, never a tick | 1 |
| §14 Q3 — verify seek before sequencing | §0.4 C-2 (answered), 4, §2.2 item 1 (device half) |
| §14 Q4 — a seek must re-arm the completion timer | §0.5, 8c, 8d — **and the pause half the ADR missed** |
| §3.3 — the two-id hazard | recorded in Task 8; **acted on in PR 3**, correctly |

**Placeholders:** one, declared and bounded — `CreateService` in Task 5, where
`SoundFlowAudioEngine`'s constructor argument list is filled from the current signature, with an
explicit instruction to delete the file rather than fake the engine. Every other code block is
literal.

**Type consistency:** `SoundPlayerBase.Time`/`Duration` are `float` **seconds** and cross into
`TimeSpan` only through `TimeSpan.FromSeconds`. `SoundPlayerBase.Seek` returns `bool` and that bool
is propagated through `SoundFlowPlaybackService.Seek` to a `LogWarning` in `SeekCoreAsync`.
`IEventAudioSource.Duration` stays `TimeSpan`; `EventPlaybackSnapshot.Duration` is `TimeSpan?`
(§0.4 C-11). `EventPlaybackRequest.DurationSeconds` is `int?` to match `VoicemailItemDto.DurationSeconds`
(`ApiModels.cs:1127`), which is `int`.

**Load:** nothing periodic is added. `AwaitCompletionAsync` waits once and re-waits only on a user
action; `TTSEventSource`'s existing 100 ms poll keeps its existing cadence and gains one branch.
ADR §1.3's disqualification of ticks and polls is respected.

**Scope:** no controller, no DI, no config key, no ducking change, no `FilePlayerAudioSource` fix.

**Assertions this PR makes, and where each is checked:**

| Claim in a comment or contract | Checked by |
|---|---|
| *"EventPlaybackRequest can never carry a URL"* | `EventPlaybackRequest_DeclaresNoUrlShapedProperty` |
| *"the arms are closed"* | the two `ArmMismatch` tests |
| `IsSeekable => false` makes `SeekAsync` throw | `SeekAsync_Throws` on both sources |
| the stream arm is not seekable | `IsSeekable_IsFalse_ForTheStreamConstructor` |
| *"the `_isPaused` branch is unreachable before PR 3"* | Task 14 item 5 — grep for `PauseAsync` callers; there are none |
| *"`AwaitCompletionAsync` degrades to today's delay"* | Task 14 item 5, by reading the `?? TimeSpan.Zero` fallback |
| *"ducking is binary and transition-only today"* | `DuckingServiceCharacterizationTests`, 4 tests |
| *"`GetPosition` has no callers, so fixing it is safe"* | Task 14 item 2 diff review + the repo-wide grep in §0.4 C-3 |

**Rebase surface.** Small and mostly private to this arc. `SoundFlowPlaybackService.cs` is the one
shared file — `AUD-2`/`AUD-4` are queued against source-removal layers and `SoundFlowMasterMixer`,
not this file's transport methods, and `ENC-*` does not touch it. `IEventAudioSource.cs` and
`EventAudioSourceBase.cs` have no other open row against them.

---

## 4. Things this plan deliberately does not do, with the reason

1. **Fix `FilePlayerAudioSource`'s seek.** §0.4 C-1 — a live primary-source path with its own UAT
   and a persisted resume position hanging off the same field. Logged in Task 13.
2. **Make `TTSParameters.Engine` nullable.** ADR §9.3 flags it as the root cause of the
   ESpeak-pinning trap and marks it *optional cleanup, flagged for Planner rather than required*.
   It belongs with **PR 3**, which is the first code that resolves an engine.
3. **Answer ADR §14 Q10** (which gate the speech pre-flight uses). It is a **PR 3** question —
   there is no synthesis path here to gate.
4. **Retire `/api/sources/events/*` or route `IAnnouncementService` through the new seam.**
   ADR §14 Q6 says explicitly *not in this arc*.
5. **Add a `Radio.Core` model folder entry.** The contract lives with its interface, matching
   `ITTSFactory.cs` and `IAudioSource.cs`.
6. **Register `IEventPlaybackService` in DI.** §0.6 — an interface with no implementation
   registered as a service would fail at the first resolve. PR 3 registers it *with* its
   implementation.
7. **Touch `design/INTEGRATIONS.md`.** Already corrected in place at `:566`; PR 1 changes no
   integration service.

---

## 5. Handoff to the rest of the arc

**Do not re-sequence the arc.** The breakdown's order stands; this plan implements PR 1 of it
unchanged. What follows are obligations discovered while planning PR 1 that belong to later PRs.

**To PR 2 (`GvMediaClient` + cache + API-side auth):**

- ⚠ **§0.4 C-8 — do not copy `PhoneContactLookupService`'s masking, copy its *intent*.** That file
  masks on one line and logs the raw number on two others. The rule for `GvMediaClient` is *mask
  on every line*, voicemail ids included.
- ⚠ **§0.6 — PR 2 is the first PR in this arc that registers a service**, so it must add a real
  DI guard test under `tests/Radio.Infrastructure.Tests/DependencyInjection/`, following
  `RotaryEncoderRegistrationTests`' build-and-resolve shape. Nothing existing would catch a
  missing registration, and the failure mode is a service that will not start on the appliance.
- The cache key must not be the raw `MediaId` used as a filename. Task 1's validator rejects
  separators, but hash it anyway — defence in depth is the whole posture here.
- `CacheMaxMegabytes = 0` must be a **no-cache** path, not an infinitely-evicting one (ADR ⟨A1·2⟩).

**To PR 3 (`EventPlaybackService` + `POST /api/audio/events`):**

- Mint and own the `playbackId`; **do not** assume `IAudioSource.Id` and
  `AudioFileEventSource._playbackId` coincide — they do not (Task 8's identity note).
- Call `EventPlaybackRequest.Validate(options.MaxSpeechChars)` and map each
  `EventPlaybackRejection` to a `400` with a stated reason. The rejection enum exists so the
  controller does not re-derive the rules.
- Resolve the TTS engine **explicitly** from `TTS:DefaultEngine` and set `TTSParameters.Engine`;
  do not rely on passing `parameters: null` (ADR §9.3 — the non-nullable `Engine` initializer
  silently selects ESpeak the moment any `TTSParameters` is supplied).
- Same DI-guard obligation as PR 2.

**To PR 4 (priority becomes load-bearing) — ⚠ THE ONE TO REVIEW HARDEST:**

Everything the ADR asserts about this subsystem is **independently confirmed against the current
tree** (the evidence is listed in Task 12), so PR 4's premise is sound. What makes it sharp is the
consequence, not the premise:

- It is the **first load-bearing use** of members that are dead today.
  `GetActiveEventsByPriority` and `StopAllDuckingAsync` have zero non-test callers, and
  `GetPriority` is read in exactly one place — inside the dead one.
- `INTEGRATIONS.md`'s old claim that higher-priority announcements interrupt lower ones is
  **false today**, and the doc already says so at `:566`. PR 4 is what makes it true. **When PR 4
  lands, `:566`'s correction must be updated in the same PR** — leaving a doc that says
  *"this is not true today"* after the day it becomes true is the same failure class in reverse.
- The one required engine change — `StartDuckingAsync` raising `DuckingStateChanged` on **every**
  call — is safe for the single existing subscriber, which acts only on `!e.IsDucking`
  (`AudioManager.cs:490-515`, `ClearDuckingMultiplier` at `:508`). Verify that is still the only
  subscriber at the time; it is today.
- **`DuckingServiceCharacterizationTests` (Task 12) is PR 4's tripwire.** Its second test asserts
  `0` raises for a second concurrent event; PR 4 must change it to `1` and say so. **Update those
  tests, do not delete them** — deleting them makes the behavioural change invisible again, which
  is the entire thing this task was written to prevent.
- The live consequence worth putting in front of a reviewer: with `PhoneIntegration:Enabled` false,
  the only thing on this box that can preempt attended playback is an external announcement posted
  to `/api/notifications/announce` at its default priority 8. **A doorbell will stop a voicemail
  mid-play.** That is the intended design (ADR §6.1), and it should be a deliberate acceptance in
  PR 4's review rather than a discovery after it ships.

**To PR 6 (`PHN-2`, retire the `<audio>` element):**

- Carry §2.2's three device-only checks into the UAT: seek actually repositions; `Time` actually
  advances; pausing a TTS source does not report completion.
- The row's own UAT is unchanged and is the thing that settles Feature A: play a voicemail while
  the radio is on and confirm the radio **ducks**, that **mute** silences it, that **master
  volume** moves it, and that with **Cast active** it goes to the Cast device rather than the
  local speakers.
