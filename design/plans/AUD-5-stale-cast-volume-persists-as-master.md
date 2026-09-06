# PLAN — `AUD-5` · A superseded Cast connection stops writing (and persisting) the console's master volume

> **Row:** `AUD-5`, [`docs/queue/AUD-5.md`](../../docs/queue/AUD-5.md). No spec doc — the diagnosis is in the row, and the
> row is correct. **Provenance:** PR #473's pre-merge review.
> **Branch:** `fix/cast-initial-volume-sync-generation-check` (as the row names it).
> **Estimate:** **0.5 d.** §0.8 says what would push it to 1 d.
> **Planned against `main` @ `656f58e6`.** Every line number below was read out of the tree at that
> commit. `GoogleCastOutput.cs`, `AudioStateUpdateService.cs` and `AudioManager.cs` are **byte-identical
> between `main` and the `fix/phn-5-…` branch this was planned from** (`git diff main...HEAD` over
> those three paths returns empty), so the anchors are valid on `main`.
> ⚠ **The row's `GoogleCastOutput.cs` anchors are all exact. Its `AudioStateUpdateService.cs` anchors
> are not** — see **`C-116`**.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

Connecting to a Chromecast performs one status **read** to learn the device's current volume. That
read's success path fires a `CastVolumeChanged` event whose sole subscriber writes
`IAudioManager.MasterVolume` — a setter that **persists**. Nothing between the network response and
the event fire re-checks whether the connection that issued the read is still the current one, so a
teardown or a newer connect landing inside that round-trip publishes a superseded connection's
volume as the console's own, and the console keeps it across a restart. The row is right about the
mechanism, right that the obvious fix is wrong, and right that the flag built to distinguish this
case (`IsInitialSync`) has never been branched on. This plan ships **both** halves the row offers,
and is explicit about which one actually removes the harm.

### 0.2 The mechanism, traced — five links, each read at `656f58e6`

**Link 1 — the read.** `ConnectAsync` claims a generation under `_lifecycleLock`
(`src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:536-545`), does its network connect,
and publishes only if nothing superseded it (`:614` → `TryPublishConnectionAsync`, whose generation
check is at `:675`). It then subscribes to receiver status (`:631`) and calls
`SyncInitialVolumeAsync()` (`:634`). **At the moment of that call the connection genuinely is
current** — `TryPublishConnectionAsync` just proved it.

**Link 2 — the window.** `SyncInitialVolumeAsync` (`:1543`) awaits
`receiverChannel.GetChromecastStatusAsync()` at **`:1555`**. That is a network round-trip. Four
sites can bump `_connectionGeneration` while it is in flight, and all four are await-free critical
sections that do not wait for anything:

| Site | Line | What it does |
|---|---|---|
| a newer connect's claim | `:539` | `myGeneration = ++_connectionGeneration` |
| `DisconnectAsync` | `:712` | `_connectionGeneration++`, clears receiver + device |
| `InitializeAsync` | `:248` | `_connectionGeneration++`, installs a fresh client |
| `DisposeAsync` | `:1720` | `_connectionGeneration++`, snapshots the client |

**Nothing between `:1555` and `:1568` reads `_connectionGeneration`.** That is the defect, in one
sentence.

**Link 3 — the fire.** On success the method writes `_lastSetVolume` / `_lastSetMute` (`:1560-1561`),
logs, and fires (`:1568-1573`):

```csharp
          CastVolumeChanged?.Invoke(this, new CastVolumeChangedEventArgs
          {
            Volume = deviceVolume,
            IsMuted = deviceMuted,
            IsInitialSync = true
          });
```

**Link 4 — the sole subscriber.** `CastVolumeChanged` is declared at `:181` and has **one**
subscriber in `src/` and `tests/`: `AudioStateUpdateService.OnCastVolumeChanged`
(`src/Radio.API/Services/AudioStateUpdateService.cs:851`; subscribed `:121`, unsubscribed `:967`).
Its body writes master volume at **`:863`** and mute at **`:871`**. `e.IsInitialSync` is read at
`:866` and `:874` — **as a string-interpolated log argument, and nowhere else in the solution.** An
initial status read is therefore handled identically to the user turning the knob on the Chromecast.

**Link 5 — the persistence, which is what makes this outlive the session.**
`AudioManager.MasterVolume`'s setter (`src/Radio.Infrastructure/Audio/Services/AudioManager.cs:74-82`)
is:

```csharp
  public float MasterVolume
  {
    get => _audioEngine.GetMasterMixer().MasterVolume;
    set
    {
      _audioEngine.GetMasterMixer().MasterVolume = value;
      _preferencePersistence?.ScheduleVolumePersist();
    }
  }
```

`ScheduleVolumePersist()` (`AudioPreferencePersistence.cs:46-62`) arms a 500 ms debounce that writes
`AudioPreferences:MasterVolume` to the config store, and `RestoreVolumePreferencesAsync` (`:69`)
reads it back at startup. **So a stale event does not move a slider; it rewrites saved state.**
`IsMuted` (`AudioManager.cs:85-93`) does the same.

### 0.3 ⚠ The row's "the obvious fix is wrong" warning holds. Verified against the code.

The warning is **accurate, and for the reason it gives.** Two independent checks:

**1. Widening `_lifecycleLock` over `SyncInitialVolumeAsync` would not close the hole.** The harm is
the `Invoke` at `:1568`, which runs *arbitrary subscriber code*. To close the race by locking, the
lock would have to still be held at the `Invoke` — i.e. subscriber code would run under
`_lifecycleLock`. Today's subscriber writes `AudioManager.MasterVolume`, which reaches the SoundFlow
master mixer and a debounce timer; tomorrow's could do anything. Holding a connection-lifecycle lock
across that is a worse defect than the one being fixed. Locking only the *field read* at `:1545`
— the shape someone actually reaches for — leaves `:1555`–`:1568` exactly as exposed as it is now.

**2. It would reintroduce the hang `AUD-3` was built to avoid, and the file says so.** The lock's own
comment at `:49-53` reads:

> *Held ONLY for those swaps — never across a SharpCaster network call. That is deliberate: a connect
> can sit for tens of seconds inside calls that do not observe cancellation, and a teardown that had
> to queue behind it would turn a data race into a hang.*

`GetChromecastStatusAsync()` is exactly such a call. A `DisconnectAsync` triggered from the output
picker takes the same semaphore at `:709`; if a connect held it across the status read, the picker
would block for the duration.

**The row's prescribed shape is also already written into the file.** `:95-104` says, in the
codebase's own words, *"widening this lock would NOT close it (the exposure is the event fire, not
the field read) — a generation re-check before the fire would."* This plan implements that, and
**`C-119` records the one thing that sentence still overstates.**

**Nothing in the row's warning was found inaccurate.** What *was* found inaccurate is elsewhere in
the row: `C-116` (drifted anchors in the second file), `C-117` (a stale forensic instruction) and
`C-118` (one overstated word). None of them changes the diagnosis.

### 0.4 ⭐ The decision the row asked for: both halves, and which one is load-bearing

The row offers three options and says *"Decide deliberately and say which way in the plan."*

**Ship both. They are not redundant, and they are not equal.**

| | What it is | What it removes | Timing-dependent? |
|---|---|---|---|
| **A — the generation re-check** (Task 2) | the producer refuses to publish a reading on behalf of a connection that is no longer current | the **race** — the network round-trip leaves the window | **yes**, and it does not reach zero (`C-119`) |
| **B — the subscriber ignores `IsInitialSync`** (Task 3) | an initial status read is not treated as a user command | the **harm** — an initial read can no longer move or persist master volume, current connection or not | **no** |

**B is the load-bearing half.** With B alone, today's harm is gone, deterministically, with no
residual window. That must be stated plainly rather than buried, because the comments this plan
writes will be read by someone deciding whether to keep A.

**A is still worth shipping, for four reasons that do not depend on B:**

1. `CastVolumeChanged` is a **`public` event on a `public` class**. A second subscriber is one PR
   away, and it would inherit the hole rather than the fix.
2. The producer publishing on behalf of a superseded connection is wrong independently of what any
   subscriber does with it — the same argument that justifies `TryPublishConnectionAsync`.
3. The in-tree comment at `:95-104` currently documents this hole as open. Shipping only B means
   rewriting that comment to say *"still open, but nothing listens"* — which is a worse artefact
   than closing it.
4. It costs a parameter, a nine-line helper, and one `if`.

**What B changes for the user, stated so it can be vetoed in review:** connecting to a Chromecast no
longer snaps the console's volume slider to the Chromecast's own volume. Everything else about
bidirectional sync survives — `SetCastVolumeAsync` (`:1630`) still pushes console volume to the
device while streaming, and a genuine external change made on the Chromecast still reaches master
volume through `OnReceiverStatusChanged` (`:1587`). **Only the connect-time adoption goes**, and it
is the only part of the loop that fires when the user did nothing.

### 0.5 ⚠ What `C-125` and `C-126` mean for this row's boundary

Two adjacent defects were found while tracing. **Neither is fixed here**, both are filed in §4, and
both are named now so a reviewer does not read their absence as an oversight:

- **The fallback path is applied where the direct read is not** (`C-125`). `:1579` logs *"Could not
  read initial Cast device volume — will sync on first status update"*, and that fallback arrives as
  `IsInitialSync = false`, so B does not stop it.
- **`C-123` is unverified and decides whether a follow-up row exists at all.** If SharpCaster raises
  `ReceiverStatusChanged` for the `GET_STATUS` response or on connect, the same connect-time volume
  also arrives labelled `IsInitialSync = false` and B is defeated within milliseconds. **§3.6
  answers this empirically on the box; it cannot be answered by reading this repo.**

### 0.6 Anchor audit — which of the row's citations survived

**`GoogleCastOutput.cs`: every one is exact.** Verified individually at `656f58e6`: `:49-53`,
`:32-104`, `:63-81`, `:120`, `:248`, `:539`, `:614`, `:634`, `:675`, `:712`, `:1543`, `:1555`,
`:1568-1573`, `:1720`, `:1829`. The row's warning that #473 moved everything below `:32` by +66 is
correct and its own citations are already post-#473.

**`AudioStateUpdateService.cs`: every one has drifted** (`C-116`). Corrected table:

| Row says | Actually at `656f58e6` | What it is |
|---|---|---|
| `:118` | **`:121`** | `_castOutput.CastVolumeChanged += OnCastVolumeChanged` |
| `:819` | **`:851`** | `private void OnCastVolumeChanged(...)` |
| `:831` | **`:863`** | `_audioManager.MasterVolume = e.Volume` |
| `:832-834` | **`:864-866`** | `"Synced volume from Cast device: {Volume:P0} (initial: {IsInitial})"` |
| `:839` | **`:871`** | `_audioManager.IsMuted = e.IsMuted` |
| `:935` | **`:967`** | `_castOutput.CastVolumeChanged -= OnCastVolumeChanged` |

`AudioManager.cs:74-82` and `:80` are exact. `AudioPreferencePersistence.cs:46-62` is exact (full
path `src/Radio.Infrastructure/Audio/Services/AudioPreferencePersistence.cs`; the row gives the
bare filename).

### 0.7 ⚠ Eleven constraints found while planning — numbering continues from `C-115` (`PHN-3`)

> ⚠ `design/plans/PHN-3-the-sms-speak-button.md` was **modified in the working tree** when this plan
> was written. If `PHN-3` grew past `C-115` before this lands, renumber from wherever it ends.

**`C-119`, `C-120` and `C-122` change the code this plan writes.** **`C-121` changes the order the
work must be done in.** **`C-123` is the one thing that could not be verified and it is the one that
decides whether a follow-up row exists.**

---

**`C-116` — the row's `AudioStateUpdateService.cs` anchors have drifted (+3 at the subscribe, +32
everywhere below).** §0.6 has the corrected table. The row carefully warns that #473 shifted
`GoogleCastOutput.cs` by +66; it does not warn about its own drift in the second file, which is the
one a fixer will open second and trust more.

**`C-117` — ⚠ CHANGES A ROW INSTRUCTION. The forensic grep target is the file sink, not journald.**
The row (2026-08-11) says *"Grep journald for that line with `initial: True`."* Since `LOG-11`
(2026-09-02) `Radio.API`'s console sink is restricted to Warning in code —
`src/Radio.API/Program.cs:48-53`, `restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning` —
and under systemd the console **is** the journal. `"Synced volume from Cast device"` is logged at
**Information** (`AudioStateUpdateService.cs:864`) from a `Radio.*` namespace, which
`appsettings.json`'s `MinimumLevel.Override` admits at Information. **So it reaches
`/opt/radio-console/logs/radio-*.txt` and does not reach `journalctl -u radio-api`.** Task 1 uses
the file sink.

**`C-118` — the row's word "unconditionally" is wrong; its conclusion is not.** `OnCastVolumeChanged`
guards the write at `:861`:

```csharp
      if (Math.Abs(_audioManager.MasterVolume - e.Volume) > 0.01f)
```

That is a **no-op-avoidance** guard, not a validity guard — it checks nothing about the connection.
Every reading more than 1% away from the current master volume is written and persisted, which is
what the row means. ⚠ **It matters for the tests:** a test asserting "master volume was never set"
must use a value **more than 0.01 away** from the mock's `MasterVolume` getter, or it passes
vacuously against unfixed code. §3.3 pins `0.75f` against `0.08f`.

**`C-119` — ⭐ THE GENERATION RE-CHECK CANNOT BE MADE AIRTIGHT, AND THE COMMENT MUST SAY SO.** The
check must release `_lifecycleLock` **before** the `Invoke`, because subscriber code must never run
under it (§0.3). So a generation bump landing between the release and the `Invoke` is still
published. The check reduces the window **from a network round-trip to a handful of instructions**;
it does not empty it. This file has shipped three comments that claimed more than the code enforced
(`CLAUDE.md` § *Pre-Merge Review*), one of whose *corrections* overclaimed in turn. **Task 4's
comment says "removes the round-trip", never "closes".**

**`C-120` — ⭐ `_lastSetVolume` / `_lastSetMute` must be primed EVEN when the reading is discarded.**
The obvious tidy-up — move `:1560-1561` behind the new generation check so a stale reading touches
nothing — is wrong. `_lastSetVolume` initialises to the **`-1f` sentinel** (`:151`), and
`OnReceiverStatusChanged` compares against it at `:1607`:

```csharp
    var volumeChanged = Math.Abs(deviceVolume - _lastSetVolume) > 0.01f;
```

Against `-1f` that is true for every real volume, so the **next** status event would be reported as
an **external** change — `IsInitialSync = false` — and written straight to master volume. Skipping
the priming to be "safe" converts a suppressed initial sync into a spurious user-authored one: the
same write, through the other door. **Prime unconditionally; gate only the fire.** This also keeps
the header comment at `:96-97` (*"its SUCCESS path writes `_lastSetVolume`/`_lastSetMute` and fires
`CastVolumeChanged`"*) true about the writes.

**`C-121` — ⚠ ORDERING. The forensic read must happen BEFORE Task 3 lands.** Task 3 replaces the
message `"Synced volume from Cast device: … (initial: True)"` with a different one. After it ships,
that string can never be emitted again, and the row's hypothesis check becomes unrunnable against
future data. Task 1 is therefore first and is **not optional**.

**`C-122` — the header comment's inventory of critical sections becomes stale.** `:37-44` enumerates
*"six await-free critical sections"* by name. `IsCurrentGenerationAsync` is a seventh. Task 4.1
updates the count and the list. Missing this is precisely the failure mode `CLAUDE.md`
§ *Pre-Merge Review* documents for this file.

**`C-123` — ⚠ UNVERIFIED, AND IT DECIDES WHETHER A FOLLOW-UP ROW EXISTS.** Does SharpCaster 3.0.0's
`ReceiverChannel` raise `ReceiverStatusChanged` for the response to `GetChromecastStatusAsync()`,
and/or for the unsolicited `RECEIVER_STATUS` a Chromecast pushes on connect? If **yes**, the same
connect-time volume also arrives at `OnReceiverStatusChanged` (`:1587`) with `IsInitialSync = false`
milliseconds later, Task 3 does not stop it, and the user-visible behaviour is unchanged. SharpCaster
ships to `~/.nuget/packages/sharpcaster/3.0.0/lib/` as a **compiled assembly only** — this is not
decidable by reading this repository. §3.6's on-box check answers it in one connect.

**`C-124` — `SyncInitialVolumeAsync`'s catch-all swallowing `ObjectDisposedException` is the desired
behaviour, not a leak.** The new `IsCurrentGenerationAsync` takes `_lifecycleLock`, which
`DisposeAsync` disposes at `:1737`. A disposal racing the status read therefore surfaces as
`ObjectDisposedException` inside the method's `catch (Exception ex)` at `:1577`, logged at Debug, no
event fired — exactly right for a disposed output. Named so nobody "hardens" it into a rethrow:
`ConnectAsync`'s own comment at `:523-528` warns that an exception escaping this region leaves
`State = Connecting`, which **wedges Cast until the process restarts**.

**`C-125` — after Task 3, the read-failure fallback is applied where the successful read is not.**
`:1579` logs *"Could not read initial Cast device volume — will sync on first status update"*, and
that fallback sync arrives as `IsInitialSync = false`. So a Cast device whose status read **fails**
still moves master volume on connect, and one whose read **succeeds** no longer does. The sentence
at `:1579` stays literally true, which is why this is a design inconsistency rather than a comment
bug. §4.2 files it.

**`C-126` — `SubscribeToReceiverStatus` / `UnsubscribeFromReceiverStatus` are not ordered against
each other.** The connect subscribes at `:631`, **after** `TryPublishConnectionAsync` at `:614`; a
concurrent `DisconnectAsync` unsubscribes at `:734`, after its own generation bump at `:712`. If the
teardown's unsubscribe runs before the connect's subscribe, the handler stays attached to a client
the teardown has already disconnected. Bounded (the socket is torn down; the `:1607` dedupe absorbs
a repeat; the `:861` guard absorbs a no-op) and **out of scope** — §4.3 files it.

### 0.8 The estimate

**0.5 d.** Four small edits across two production files, two comment blocks, four tests across two
existing test classes, one forensic read and one on-box confirmation. The row calls it *"cheap to
fix"* and it is. For calibration: `PHN-5` was 1 d for eleven sites, a new helper, its tests and a
lint rule; this is materially less than half of that, and unlike `PHN-5` the diagnosis arrived
complete.

⚠ **What would push it to 1 d:**

- **`C-123` comes back positive.** Triaging the `IsInitialSync = false` duplicate and writing it up
  as a follow-up row is an afternoon. ⛔ **It does not get fixed inside this PR** — that is a
  redesign of bidirectional volume sync and the row forbids unpicking #468's work.
- **No Chromecast is available for §3.6.** Then the on-box half of the verification cannot run;
  say so in the PR body rather than claiming a check that did not happen.

### 0.9 Things Builder must NOT do

- ⛔ **Do not widen `_lifecycleLock` over `SyncInitialVolumeAsync`.** §0.3. It does not close the
  hole and it reintroduces the picker hang.
- ⛔ **Do not delete the `CastVolumeChanged` fire from `SyncInitialVolumeAsync`,** and do not remove
  `IsInitialSync` from `CastVolumeChangedEventArgs`. The event is the only forensic record that the
  read happened, and it is what §3.6 and any future "my volume changed by itself" report are checked
  against.
- ⛔ **Do not touch `OnReceiverStatusChanged`, the `-1f` sentinel, or `SetCastVolumeAsync` /
  `SetCastMuteAsync`.** §4 explains each.
- ⛔ **Do not "fix" the three unsynchronized `_client` check-then-dereference reads.** PR #473 already
  assessed them and wrote the reasoning into `:63-81`. Task 2 removes one of them only as a *side
  effect* of passing the client in — and Task 4.1 updates that comment to match.
- ⛔ **Do not re-brainstorm the row.** The diagnosis is complete and was re-verified here.
- ⛔ **Do not unpick PR #468.** It created the generation counter this fix uses; the hole predates it
  by six months (`b420edc`, 2026-02-11).

---

## 1. Tasks

### Task 1 — the forensic read, which must happen before any code lands

⚠ **`C-121`: run this first.** Task 3 retires the message this looks for.

⚠ **`C-117`: the file sink, not journald.** And per `CLAUDE.md` § *Deployment*, keep every query
bounded — log volume on this box correlates with audible audio distortion.

```bash
ssh mmack@radio 'grep -h "Synced volume from Cast device" /opt/radio-console/logs/radio-*.txt | tail -50'
ssh mmack@radio 'grep -hc "initial: True" /opt/radio-console/logs/radio-*.txt'
ssh mmack@radio 'grep -h "Cast device initial volume" /opt/radio-console/logs/radio-*.txt | tail -50'
```

Correlate any `initial: True` hit against nearby `"Connecting to Chromecast"` / `"Disconnecting from
Chromecast"` / `"was superseded while connecting"` lines. Record the counts in the PR body.

⚠ **A negative result does not close this row, and the row says so explicitly.** The mechanism is
real whether or not it has fired in production. ⚠ **And do not cite the 2026-08-10 journal's
`0.03` / `0.02` / `0.01` / `6.85E-09` ratchet as a repro** — the row attributes those to a slider
drag during the crash investigation.

---

### Task 2 — `SyncInitialVolumeAsync` takes the connection it is syncing for, and re-checks it before the fire

**File:** `src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs`

#### 2.1 The fourth test seam, declared beside the other two

Insert after `ConnectTransportOverrideForTests` (`:130`):

```csharp
  /// <summary>
  /// Test seam: substitutes the Cast status read inside
  /// <see cref="SyncInitialVolumeAsync"/>. Without it the volume-sync race is
  /// unreachable offline for the same reason
  /// <see cref="ConnectTransportOverrideForTests"/> exists — no fake socket can
  /// answer a Cast GET_STATUS, so the read always throws and the method diverts
  /// into its catch before the generation check is ever evaluated. Awaiting inside
  /// this delegate is also what lets a test interleave a teardown at exactly the
  /// point the network round-trip occupies in production.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<Task<(float Volume, bool Muted)?>>? CastStatusReadOverrideForTests { get; set; }
```

`Radio.Infrastructure.csproj:15` already carries `<InternalsVisibleTo Include="Radio.Infrastructure.Tests" />`
— **no project-file change is needed.**

#### 2.2 The call site

`:634`, inside `ConnectAsync`. Both arguments are already in scope: `myGeneration` is assigned at
`:539` and `client` at `:540`/`:577`, and `client` is proven non-null by the throw at `:593-596`
and is not reassigned after it.

```csharp
      // Read initial device volume. The generation goes with it: the read is a
      // network round-trip, and this connection can be superseded inside it.
      await SyncInitialVolumeAsync(client, myGeneration).ConfigureAwait(false);
```

#### 2.3 The method, replacing `:1540-1581` whole

```csharp
  /// <summary>
  /// Reads the initial device volume after connecting and syncs our local state.
  /// </summary>
  /// <param name="client">
  /// The client to read from, passed explicitly rather than read from <c>_client</c>
  /// so the caller's snapshot is used — the same reason
  /// <see cref="SubscribeToReceiverStatus"/> takes one: a concurrent connect or
  /// teardown may already have swapped the field.
  /// </param>
  /// <param name="generation">
  /// The connection generation the caller claimed. Re-checked after the network read
  /// and before the event fire; the comment on that check states exactly what it does
  /// and does not guarantee.
  /// </param>
  private async Task SyncInitialVolumeAsync(ChromecastClient client, int generation)
  {
    try
    {
      var reading = await ReadInitialCastVolumeAsync(client).ConfigureAwait(false);
      if (reading == null)
      {
        return;
      }

      var (deviceVolume, deviceMuted) = reading.Value;

      // Primed BEFORE the currency check, and therefore primed even for a reading
      // that is about to be discarded. That is deliberate. These two fields are the
      // echo filter's baseline, not connection state: _lastSetVolume starts at the
      // -1f sentinel, and OnReceiverStatusChanged reports any status event arriving
      // while it is still -1f as an EXTERNAL change. Skipping the priming here would
      // convert a suppressed initial sync into a spurious user-authored one — the
      // same write to master volume, through the other door.
      _lastSetVolume = deviceVolume;
      _lastSetMute = deviceMuted;

      _logger.LogInformation(
        "Cast device initial volume: {Volume:P0}, Muted: {Muted}",
        deviceVolume, deviceMuted);

      // AUD-5. The read above is a network round-trip, and four sites bump the
      // generation while it is in flight: InitializeAsync, a newer connect's claim,
      // DisconnectAsync and DisposeAsync. Publishing this reading for a connection
      // that has since been superseded is what the defect was — the subscriber writes
      // AudioManager.MasterVolume, whose setter schedules a persist.
      //
      // ⚠ What this check does and does not do, stated precisely because this file
      // has shipped comments that claimed more than the code enforced:
      //   IT DOES remove the network round-trip from the window. A supersede landing
      //     any time between the claim and the status response is now caught here.
      //   IT DOES NOT make the window empty. The lock is released before the Invoke —
      //     subscriber code must never run under _lifecycleLock — so a bump landing in
      //     the few instructions between the release and the Invoke is still published.
      //   IT IS NOT what stops an initial read moving master volume today. That is
      //     AudioStateUpdateService.OnCastVolumeChanged, which ignores IsInitialSync
      //     events outright and has no timing dependence at all. This check is the
      //     producer honouring its own contract for whatever subscribes next.
      if (!await IsCurrentGenerationAsync(generation).ConfigureAwait(false))
      {
        _logger.LogInformation(
          "Cast initial volume read belongs to a superseded connection (generation {Generation}) — not published",
          generation);
        return;
      }

      // Fire event so subscribers can observe the device's actual volume
      CastVolumeChanged?.Invoke(this, new CastVolumeChangedEventArgs
      {
        Volume = deviceVolume,
        IsMuted = deviceMuted,
        IsInitialSync = true
      });
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not read initial Cast device volume — will sync on first status update");
    }
  }

  /// <summary>
  /// Performs the Cast status read behind <see cref="SyncInitialVolumeAsync"/>, or
  /// the test substitute for it. Returns null when the client exposes no receiver
  /// channel or the status carries no volume.
  /// </summary>
  private async Task<(float Volume, bool Muted)?> ReadInitialCastVolumeAsync(ChromecastClient client)
  {
    if (CastStatusReadOverrideForTests != null)
    {
      return await CastStatusReadOverrideForTests().ConfigureAwait(false);
    }

    var receiverChannel = client.GetChannel<ReceiverChannel>();
    if (receiverChannel == null)
    {
      return null;
    }

    var status = await receiverChannel.GetChromecastStatusAsync().ConfigureAwait(false);
    if (status?.Volume?.Level == null)
    {
      return null;
    }

    return ((float)status.Volume.Level.Value, status.Volume.Muted ?? false);
  }

  /// <summary>
  /// True when <paramref name="generation"/> is still the current connection
  /// generation. Await-free inside the lock, like every other critical section here.
  /// </summary>
  /// <remarks>
  /// Deliberately takes no <see cref="CancellationToken"/>. Callers must sit inside a
  /// catch that tolerates <see cref="ObjectDisposedException"/>: <c>DisposeAsync</c>
  /// disposes <c>_lifecycleLock</c>, and a disposed output must not publish anything
  /// anyway, so the throw is the right outcome rather than a case to handle.
  /// </remarks>
  private async Task<bool> IsCurrentGenerationAsync(int generation)
  {
    await _lifecycleLock.WaitAsync().ConfigureAwait(false);
    try
    {
      return _connectionGeneration == generation;
    }
    finally
    {
      _lifecycleLock.Release();
    }
  }
```

**Two behavioural notes for the reviewer.**

- The old body read `_client` twice (`:1545` null-check, `:1552` dereference). It now reads it
  **zero** times. That removes one of the three check-then-dereference sites the header comment
  names — hence Task 4.1.
- `.ConfigureAwait(false)` is added to the status read, which `:1555` did not have. Neutral in this
  host and consistent with the surrounding connect path; called out so it is not mistaken for an
  accident.

---

### Task 3 — an initial status read is not a user command

**File:** `src/Radio.API/Services/AudioStateUpdateService.cs`

Insert immediately after the `_audioManager == null` guard, i.e. between `:856` and `:858`. The
existing `try` block and everything in it is **unchanged**.

```csharp
    // AUD-5. An initial sync is this application READING the Cast device's status
    // right after connecting — not the user turning a knob on the Chromecast. Applying
    // it rewrites the console's own master mixer volume, which is not the Cast device's
    // volume, from a value set on a different device; and IAudioManager.MasterVolume's
    // setter schedules a debounced write to the config store (AudioPreferencePersistence),
    // so the rewrite outlives both the Cast session and the process.
    //
    // The event is still logged. It is the only record that the read happened, it is
    // what a "my volume changed by itself" report gets checked against, and the
    // "initial:" token is what that check greps for.
    if (e.IsInitialSync)
    {
      _logger.LogInformation(
        "Cast device initial volume read: {Volume:P0}, Muted: {Muted} (initial: {IsInitial}) — not applied to master volume",
        e.Volume, e.IsMuted, e.IsInitialSync);
      return;
    }
```

⚠ **Keep `{IsInitial}` structured rather than hard-coding `True`.** It is constant inside this branch,
but the property is what a structured sink indexes, and §3.6 and the tests both look for
`initial: True`.

⚠ **Level stays Information.** `Radio.API`'s console sink is Warning-restricted
(`Program.cs:48-53`), so this goes to the file sink only — no journald volume added to a box where
log volume is an audio problem (`C-117`).

---

### Task 4 — the header comments stop describing a hole that is now closed, without overclaiming about what replaced it

**File:** `src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs`

#### 4.1 `:37-44` — six critical sections become seven (`C-122`), and `:55-78` loses a site

Replace `:37-44`:

```csharp
  // WHAT IT SERIALIZES: seven await-free critical sections, each touching
  // _connectionGeneration and/or those three fields as one consistent unit:
  //     InitializeAsync         bump, install a fresh client, clear receiver+device
  //     ConnectAsync's claim    bump, snapshot _client
  //     TryPublishConnection    generation check, then publish client+receiver+device
  //     IsCurrentGeneration     generation check only — mutates nothing
  //     DisconnectAsync         bump, snapshot, clear receiver+device
  //     DisposeAsync            bump, snapshot _client (deliberately NOT clearing it)
  //     StopAsync               snapshot _client only — touches no generation
```

Replace `:55-59` (the enumeration of unlocked check-then-dereference sites):

```csharp
  // READS ARE DELIBERATELY UNSYNCHRONIZED. Most reads of these fields — across
  // the start/stream/volume/teardown paths — take no lock at all. Two of them
  // null-check a field and then dereference it on a second, separate read:
  // SetCastVolumeAsync and SetCastMuteAsync, as do the `_client!` dereferences in
  // the Start/stream helpers. (SyncInitialVolumeAsync was a third until AUD-5; it
  // now takes its client as a parameter and does not read the field at all.)
  // Reference assignment is
```

Replace `:76-78` — **one word, `three` → `two`.** Quoted with its lead-in line so the anchor is
unambiguous:

```csharp
  //     What the precondition buys them is that their guard stays valid across
  //     the awaits that follow it. The two check-then-dereference sites named
  //     above rely on it directly.
```

⚠ **`:77-78` is the sentence PR #473's own first draft got wrong** (`CLAUDE.md` § *Pre-Merge Review*,
case 3). Change **only** the count — the surrounding precondition argument is correct and re-verified:
`_client` is assigned in exactly two places (`InitializeAsync:250`, `TryPublishConnectionAsync:680`),
both non-null, and neither `DisconnectAsync` nor `DisposeAsync` clears it.

#### 4.2 `:95-104` — the paragraph that documents the hole

Replace it whole:

```csharp
  // SyncInitialVolumeAsync WAS the exception to that reassurance, and AUD-5 addressed
  // it. It is a read rather than a command, and its success path fires
  // CastVolumeChanged — whose subscriber writes AudioManager.MasterVolume, a setter
  // that schedules a persist. Nothing re-checked _connectionGeneration between the
  // status response and the event fire, so a teardown landing in that window published
  // the volume of a connection that was no longer current, and the console kept it
  // across a restart. Two things changed, and which one does what matters:
  //   - The method now takes its client AND its generation as parameters, and
  //     re-checks the generation immediately before the fire. That removes the network
  //     round-trip from the window. It does NOT make the window empty: the lock is
  //     released before the Invoke, because subscriber code must never run under it.
  //   - AudioStateUpdateService.OnCastVolumeChanged now ignores IsInitialSync events
  //     outright. THAT is what makes an initial read unable to move master volume, and
  //     it has no timing dependence at all.
  // Widening this lock was and remains the wrong fix: the exposure is the event fire,
  // not the field read, and holding it across a SharpCaster call is the hang described
  // above.
```

---

## 2. Ordering

**Task 1 first and separately** (`C-121`) — it reads production logs and must run before Task 3
retires the message it looks for. Record its output in the PR body before writing code.

**Tasks 2, 3 and 4 in one PR.** They are one property — *a Cast status read cannot become the
console's persisted master volume* — and the comment in Task 4.2 describes both halves, so it is
wrong at any point where only one has landed. Task 4 must be in the same commit as Tasks 2 and 3 for
the same reason.

---

## 3. Test plan

> ⚠ **This repository has repeatedly found tests that passed against a deliberately broken
> implementation.** Every pin below names the mutation that must make it fail, and **Builder runs
> each mutation and records the result in the PR body** rather than reasoning about it. Where
> something cannot be pinned, §3.4 says so.

> ⚠ **`CLAUDE.md` § *Test Timing*.** Every test here is **fully deterministic and contains no
> `Task.Delay`, no timer, and no wall-clock comparison.** The rendezvous in §3.1/§3.2 is the seam
> delegate being invoked — the teardown runs *inside* the awaited read, so the interleaving is
> caused, not hoped for. §3.3's tests are synchronous end to end.

### 3.1 `T1` — the generation re-check, driven through the real connect path

**File:** `tests/Radio.Infrastructure.Tests/Audio/Outputs/GoogleCastOutputConcurrencyTests.cs`
(**extend**, do not create a new file — `StartLoopbackListener`, `Device` and `BuildOutput` already
live there at `:145`, `:173` and `:182`, and duplicating them would be the third copy of the same
Cast-race scaffolding).

Add a paragraph to the class `<summary>` noting it now also pins the volume-sync window, then:

```csharp
  [Fact]
  public async Task InitialVolumeReadForASupersededConnection_IsNotPublished()
  {
    // AUD-5. The teardown lands INSIDE the status read — the window the network
    // round-trip occupies in production — and the device then "answers" normally.
    // The reading must be discarded rather than published, because its sole
    // subscriber writes AudioManager.MasterVolume and that setter persists.
    using var listener = StartLoopbackListener(out var port);
    await using var output = BuildOutput();
    await output.InitializeAsync();

    var published = new List<CastVolumeChangedEventArgs>();
    output.CastVolumeChanged += (_, e) => { lock (published) { published.Add(e); } };

    var readRan = false;

    // The connect "succeeds" on the wire, as in ConnectThatSucceedsButLostTheRace.
    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;

    output.CastStatusReadOverrideForTests = async () =>
    {
      readRan = true;

      // ⚠ The catch is load-bearing, not defensive tidiness. DisconnectAsync
      // rethrows (GoogleCastOutput.cs:755), and an exception escaping this delegate
      // would be swallowed by SyncInitialVolumeAsync's own catch — which would make
      // this test pass for the wrong reason and survive the mutation below.
      try { await output.DisconnectAsync(); }
      catch { /* tearing down a client that never really connected may throw */ }

      return (0.08f, false);
    };

    await output.ConnectAsync(Device("cast-a", port));

    Assert.True(readRan, "the status-read seam never fired — the test never reached the window");
    Assert.Empty(published);
  }
```

**Falsifying mutation:** delete the `if (!await IsCurrentGenerationAsync(generation))` block →
`published` holds one entry → fails.

⚠ **The teardown's generation bump happens under the lock at `:712`, before any of `DisconnectAsync`'s
network work**, so the bump is guaranteed even though the rest of the teardown is best-effort. That
is what makes this deterministic rather than lucky.

### 3.2 `T2` — the anti-vacuity twin

Without this, `IsCurrentGenerationAsync` mutated to `return false;` passes `T1`.

```csharp
  [Fact]
  public async Task InitialVolumeReadForTheCurrentConnection_IsPublishedAsAnInitialSync()
  {
    using var listener = StartLoopbackListener(out var port);
    await using var output = BuildOutput();
    await output.InitializeAsync();

    var published = new List<CastVolumeChangedEventArgs>();
    output.CastVolumeChanged += (_, e) => { lock (published) { published.Add(e); } };

    output.ConnectTransportOverrideForTests = _ => Task.CompletedTask;
    output.CastStatusReadOverrideForTests =
      () => Task.FromResult<(float Volume, bool Muted)?>((0.42f, true));

    await output.ConnectAsync(Device("cast-a", port));

    var e = Assert.Single(published);
    Assert.Equal(0.42f, e.Volume, 3);
    Assert.True(e.IsMuted);

    // Pins the flag Task 3's branch depends on. If this ever goes false, the
    // subscriber-side guard silently stops matching and the harm returns.
    Assert.True(e.IsInitialSync);
  }
```

**Falsifying mutations:** `IsCurrentGenerationAsync` → `return false;` → fails. `IsInitialSync = false`
in the event initialiser → fails.

### 3.3 `T3` — the subscriber, which is the load-bearing half

**File:** `tests/Radio.API.Tests/Services/AudioStateUpdateServiceCastVolumeTests.cs` (**new**).

Reuse the assembly's existing scaffolding rather than inventing any:
`CapturingLoggerProvider` (`tests/Radio.API.Tests/TestSupport/CapturingLoggerProvider.cs:25` —
`internal`, same assembly, and its `IsEnabled` returns true at **every** level, which is what makes
an Information line observable), and the reflection-invoke pattern from
`AudioStateUpdateServiceTests.InvokeUpdateCurrentMatchAnchor` (`:59-68`).

⚠ **No `internal` change is needed in `Radio.API`.** The handler stays `private`; the existing test
class already reaches private members of this type by reflection, and matching that is cheaper than
widening visibility.

```csharp
  private const float ExistingMasterVolume = 0.75f;

  // ⚠ C-118: 0.08f is more than 0.01 away from ExistingMasterVolume. The production
  // guard is `Math.Abs(_audioManager.MasterVolume - e.Volume) > 0.01f`, so a value
  // inside that band would make BOTH tests below pass against unfixed code.
  private const float ArrivingVolume = 0.08f;

  [Fact]
  public void AnInitialCastVolumeSync_IsLoggedButNeverAppliedToMasterVolume()
  {
    var audio = new Mock<IAudioManager>();
    audio.SetupGet(a => a.MasterVolume).Returns(ExistingMasterVolume);
    audio.SetupGet(a => a.IsMuted).Returns(false);

    var logs = new CapturingLoggerProvider();
    var svc = CreateServiceWithAudioManager(audio.Object, logs);

    RaiseCastVolumeChanged(svc, ArrivingVolume, muted: true, initial: true);

    audio.VerifySet(a => a.MasterVolume = It.IsAny<float>(), Times.Never);
    audio.VerifySet(a => a.IsMuted = It.IsAny<bool>(), Times.Never);

    // The forensic record must survive the fix — it is what AUD-5's hypothesis pass
    // greps for, and what a future "my volume changed by itself" report is checked
    // against. Also guards against the whole handler being short-circuited to a
    // bare `return`, which would satisfy the VerifySet assertions vacuously.
    Assert.Contains(logs.Messages, m => m.Contains("initial: True"));
  }

  [Fact]
  public void AnExternalCastVolumeChange_IsStillAppliedToMasterVolume()
  {
    // The anti-vacuity twin: without it, `if (true) return;` at the top of the
    // handler passes the test above.
    var audio = new Mock<IAudioManager>();
    audio.SetupGet(a => a.MasterVolume).Returns(ExistingMasterVolume);
    audio.SetupGet(a => a.IsMuted).Returns(false);

    var svc = CreateServiceWithAudioManager(audio.Object, new CapturingLoggerProvider());

    RaiseCastVolumeChanged(svc, ArrivingVolume, muted: true, initial: false);

    audio.VerifySet(a => a.MasterVolume = ArrivingVolume, Times.Once);
    audio.VerifySet(a => a.IsMuted = true, Times.Once);
  }

  // --- helpers ---

  private static void RaiseCastVolumeChanged(
    AudioStateUpdateService svc, float volume, bool muted, bool initial)
  {
    var method = typeof(AudioStateUpdateService).GetMethod(
      "OnCastVolumeChanged",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(method);
    method!.Invoke(svc, new object?[]
    {
      null,
      new CastVolumeChangedEventArgs { Volume = volume, IsMuted = muted, IsInitialSync = initial }
    });
  }

  /// <summary>
  /// ⚠ The audio manager goes in through a POPULATED ServiceCollection, not a
  /// constructor parameter: AudioStateUpdateService resolves it with
  /// IServiceProvider.GetService (AudioStateUpdateService.cs:86). Same reason
  /// AudioStateUpdateServiceTests.CreateServiceWith exists.
  /// </summary>
  private static AudioStateUpdateService CreateServiceWithAudioManager(
    IAudioManager audioManager, CapturingLoggerProvider logs)
  {
    var hubContextMock = new Mock<IHubContext<AudioStateHub>>();
    var clientsMock = new Mock<IHubClients>();
    var allClientsMock = new Mock<IClientProxy>();
    allClientsMock
      .Setup(c => c.SendCoreAsync(
        It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);
    clientsMock.SetupGet(c => c.All).Returns(allClientsMock.Object);
    hubContextMock.SetupGet(h => h.Clients).Returns(clientsMock.Object);

    var services = new ServiceCollection();
    services.AddSingleton(audioManager);

    return new AudioStateUpdateService(
      logs.CreateLogger<AudioStateUpdateService>(),
      hubContextMock.Object,
      services.BuildServiceProvider(),
      new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
  }
```

**Falsifying mutations:** remove the `if (e.IsInitialSync)` block → test 1 fails. Change it to
`if (!e.IsInitialSync)` → test 2 fails. Replace the block's body with a bare `return` (no log) →
test 1 fails on the `initial: True` assertion.

⚠ **The service is never started.** `AudioStateUpdateService` is a `BackgroundService`; `ExecuteAsync`
is not run, so nothing in these tests involves the 500 ms polling loop or any other timer.

### 3.4 What cannot be pinned, and what that costs

- **`C-120` — that `_lastSetVolume` / `_lastSetMute` are primed even for a discarded reading.** Both
  are `private` fields with no observable consequence reachable from a test: the only reader is
  `OnReceiverStatusChanged`, which is raised by SharpCaster's `ReceiverChannel` and cannot be driven
  offline. Pinning it would need a **fifth** seam whose only purpose is to observe a private field —
  which is exactly the "mock that re-asserts the seam from a different angle" `TEST-2` forbids. It is
  therefore asserted by the comment in Task 2.3 and by review, and this paragraph says so rather than
  implying coverage that does not exist.
- **`C-119`'s residual window** — a generation bump between the lock release and the `Invoke`. Not
  reachable without instrumenting the gap. The comment states the limit; no test claims otherwise.
- **`C-123`** — SharpCaster's event behaviour. Answered on the box (§3.6), not in the suite.

### 3.5 Gates

⚠ **`CLAUDE.md`: never pipe `dotnet test` into `tail`** — the pipeline reports `tail`'s exit code.

```bash
dotnet build RadioConsole.sln -c Release        # 0 warnings; Release treats warnings as errors
dotnet test RadioConsole.sln -c Release > /tmp/aud5-test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/aud5-test.log
```

Read the **per-project summary lines**. Known-failing on Windows and **not** a regression: four
`SrcVariableResamplerTests` (`libsamplerate.so.0`, `TEST-5`) and
`NwsObservationIntegrationTests.RealNwsCall_*` (`Category=Integration`, CI-excluded).

Then the mutation runs from §3.1–§3.3, each recorded in the PR body with its observed failure.

### 3.6 On-box verification — a check, and the answer to `C-123`

```bash
./deploy/Deploy-ToLinux.ps1        # defaults are -TargetHost radio -Runtime linux-x64 since OPS-1
```

Then, from the UI, note the current master volume, connect a Chromecast whose own volume is clearly
different, and:

1. **The console's volume slider must not move.** That is the whole user-visible deliverable.
2. **The file sink must show the new line:**

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -E "Cast device initial volume|initial: True|not applied to master volume|superseded connection" $F | tail -30'
```

3. ⭐ **`C-123`, answered in one connect.** In that same output, check whether a
   `"Cast device volume changed externally"` line appears **immediately after** the initial-volume
   line, carrying the same value. If it does, SharpCaster raises `ReceiverStatusChanged` for the
   status response and/or on connect, the connect-time volume still reaches master volume through
   the `IsInitialSync = false` door, and §4.2's follow-up row becomes real work rather than a
   hypothetical. **Record the answer either way in the PR body** — a negative is as useful as a
   positive and closes a question that cost this plan an unverifiable.

⚠ **If no Chromecast is available, say so in the PR body.** Do not report §3.6 as passed on the
strength of the unit tests; the unit suite cannot see SharpCaster at all, which is the entire reason
`C-123` is open.

---

## 4. Deliberately not done

### 4.1 `OnReceiverStatusChanged` gets no generation check

The external-change path (`:1587`) is not guarded either, and it is not this row. Two reasons.
First, it is subscribed and unsubscribed against a specific client (`:631`, `:734`), so its
staleness question is a *subscription-lifetime* question, not a generation question — a different
mechanism needing a different fix. Second, it carries a genuine user action, so discarding it has a
cost that discarding a status read does not. ⛔ **And do not touch the `-1f` sentinel at `:151`**: it
is load-bearing for the documented read-failure fallback (`:1579`).

### 4.2 The fallback-path inconsistency (`C-125`), and the `C-123` duplicate

After Task 3, a Cast device whose initial status read **fails** still moves master volume — via the
fallback that `:1579` documents — while one whose read **succeeds** no longer does. Resolving that
means deciding whether the console should ever adopt a Cast device's volume at all, which is a
redesign of the bidirectional sync `b420edc` shipped, and the row explicitly forbids unpicking it.
**File as a candidate row** once §3.6 has answered `C-123`, since the two questions have the same
answer: *should a Cast device's volume, read at connect, become the console's persisted master
volume?*

### 4.3 The subscribe/unsubscribe ordering race (`C-126`)

Real, bounded, and a different mechanism. Fixing it means moving `SubscribeToReceiverStatus` /
`UnsubscribeFromReceiverStatus` under `_lifecycleLock` — which they would fit, both being await-free
— but that changes the lock's documented inventory again and widens a row the queue calls small.
File it.

### 4.4 A non-persisting master-volume setter

The tempting narrow fix — apply the Cast device's volume to the mixer but skip the persist — needs a
new member on `IAudioMixerControl` / `IAudioManager`, touches a public interface, and still leaves
the console's own gain stage set from another device's knob for the rest of the session. Rejected as
more machinery for a worse outcome.

### 4.5 The three unsynchronized `_client` reads

PR #473 assessed them and wrote the reasoning into `:63-81`. Task 2 removes one of them incidentally
and Task 4.1 updates the count; nothing else about them changes. ⛔ Do not re-derive that assessment.

### 4.6 The `AUD-5` row's own citation drift and stale forensic instruction

`C-116` and `C-117` are corrections to `docs/BUILDER_QUEUE.md`. **This plan does not edit that file**
— a Builder is working in it concurrently. §5 supplies the wording; the owner or the claiming Builder
applies it.

---

## 5. Docs and queue

- `docs/BUILDER_QUEUE.md` — Plan cell + status, per **§ Queue row wording** below. Not edited here.
- `design/FUTURE-WORK.md` — no entry. Nothing is stubbed; §4's items are candidate rows, not stubs.
- `design/INTEGRATIONS.md` — no entry. This touches neither encoders, phone integration nor
  notifications.
- `CLAUDE.md` — **no edit.** The § *Pre-Merge Review* section already documents this file's comment
  history and Task 4 is an instance of it, not a new rule.
- **PR body must carry:** Task 1's forensic counts; the §3.5 mutation results, one line each; the
  §3.6 answer to `C-123`; and an explicit note that **`C-116`/`C-117` are queue corrections not
  applied by this PR**.

---

## 6. Self-review

### 6.1 Verified first-hand at `656f58e6`

- Every `GoogleCastOutput.cs` anchor in the row (fifteen of them) — all exact.
- Every `AudioStateUpdateService.cs` anchor in the row (six) — all drifted; corrected table in §0.6.
- `CastVolumeChanged` has exactly one subscriber across `src/` and `tests/` (grep over the whole
  repo: `:181` declaration, `:121` subscribe, `:967` unsubscribe, no other handler).
- `IsInitialSync` is read only at `AudioStateUpdateService.cs:866` and `:874`, both as log arguments.
- `SyncInitialVolumeAsync` has exactly one call site, `:634`.
- `MasterVolume` and `IsMuted` setters both call `ScheduleVolumePersist()`; the debounce and its
  config-store write; `RestoreVolumePreferencesAsync` reads it back.
- `Radio.API`'s console sink restriction (`Program.cs:48-53`) and `MinimumLevel` override
  (`appsettings.json`) — the derivation behind `C-117`.
- `Radio.Infrastructure.csproj:15` and `Radio.API.csproj:47` already carry the `InternalsVisibleTo`
  entries the tests need.
- The existing test scaffolding named in §3.1 and §3.3, line by line.
- `git diff main...HEAD` over the three production files: empty, so these anchors hold on `main`.

### 6.2 Not verified, and what it costs

- **`C-123`.** SharpCaster 3.0.0 ships compiled; its `ReceiverChannel` event behaviour is not
  readable from this repo. **Cost:** if it raises on connect, Task 3's user-visible benefit is
  cancelled out and the row's headline race fix (Task 2) is the only surviving improvement. It does
  not change any code in this plan — it changes whether a follow-up row is needed. §3.6 settles it.
- **Whether `initial: True` has ever actually been logged in production.** Task 1 answers it, and per
  the row a negative result does not close the row.

### 6.3 What would falsify this plan's central decision

The decision is *ship both halves, and say that the subscriber-side one is load-bearing.* It is
wrong if either:

- **Someone wants connect-time volume adoption as a feature.** Then Task 3 is a regression, Task 2
  ships alone, and the row's headline race is closed while the persisted-rewrite half stays. The
  owner can make that call from §0.4's table; nothing else in the plan changes.
- **`C-123` comes back positive AND the follow-up is declined.** Then Task 3 removes a log-visible
  behaviour without removing the user-visible one, and shipping it alone would read as a fix that
  did not work. In that case §3.6's result must be stated in the PR body so the next reader knows
  the difference between *fixed* and *fixed on one of two paths*.

---

## Queue row wording

⛔ **This plan does not edit `docs/BUILDER_QUEUE.md`** — a Builder is working in it concurrently. The
strings below are for whoever applies them.

**`AUD-5` · Plan cell** — replaces `_plan TBD (small — a generation parameter, one await-free
re-check, and an explicit decision on `IsInitialSync`; **the diagnosis is complete and must not be
re-brainstormed**)_`:

> [`AUD-5-stale-cast-volume-persists-as-master.md`](../design/plans/AUD-5-stale-cast-volume-persists-as-master.md) · **both halves, and the plan says which is load-bearing:** a generation re-check before the fire (closes the race, narrows but does not empty the window) **and** the subscriber ignoring `IsInitialSync` (removes the harm, no timing dependence). **0.5 d.** ⚠ Task 1 — the forensic log read — must run **before** the code lands: Task 3 retires the `"Synced volume from Cast device … initial: True"` message the row tells you to grep for.

**`AUD-5` · Status cell** — unchanged at 📋 until claimed.

**`AUD-5` · Item cell, two corrections found while planning** (append; do not rewrite the row):

> _**Anchors re-verified 2026-09-05 against `main` @ `656f58e6`. Every `GoogleCastOutput.cs` citation above is byte-exact. Every `AudioStateUpdateService.cs` citation has DRIFTED and is corrected here:** subscribe `:118` → **`:121`**, handler `:819` → **`:851`**, `MasterVolume` write `:831` → **`:863`**, log `:832-834` → **`:864-866`**, `IsMuted` write `:839` → **`:871`**, unsubscribe `:935` → **`:967`**. **And the forensic instruction is stale:** `"Synced volume from Cast device"` is logged at **Information** from `Radio.API`, whose console sink has been Warning-restricted since `LOG-11` (2026-09-02) — so it is in the **file sink**, `/opt/radio-console/logs/radio-*.txt`, and **not** in `journalctl -u radio-api`. Also: the row calls the `MasterVolume` write "unconditional"; it is guarded by `Math.Abs(current - e.Volume) > 0.01f` at `:861`, which checks nothing about the connection — the conclusion stands, the word does not, and a test asserting "never written" must use a value more than 0.01 away or it passes vacuously._

**`TEST-2` · Item cell, seam addendum** (append — `AUD-5` adds a **fourth** `internal` seam and
`TEST-2` owns the register):

> _**⚠ ADDED by `AUD-5`'s plan, 2026-09-05 — a FOURTH seam.** `GoogleCastOutput.CastStatusReadOverrideForTests` substitutes the Cast status read inside `SyncInitialVolumeAsync`, for the same reason `ConnectTransportOverrideForTests` exists: no fake socket can answer a Cast `GET_STATUS`, so the read always throws and the method diverts into its catch before the generation check is ever evaluated. It is not a mock re-asserting a seam — the test drives the real `ConnectAsync`, the real `_connectionGeneration`, the real `DisconnectAsync` and the real re-check, and only the network read is substituted. **Four seams now exist solely because a native/hardware dependency makes the real path untestable**, which strengthens this row's own conclusion: the durable deliverable is the written convention, not a Bluetooth-specific test._
