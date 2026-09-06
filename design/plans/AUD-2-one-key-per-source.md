# PLAN — `AUD-2` · One key per source

> **Row:** `AUD-2`, `docs/BUILDER_QUEUE.md:129` on `main` (`:131` in this working tree — the
> `fix/phn-5-…` branch adds two lines above it).
> **Branch:** `fix/sdr-playback-id-ducking-gain` (the row names it). ⚠ The branch name says `sdr`;
> the scope is **four source types across three files**. §0.5.
> **Estimate:** **1 day.** §7 says how it splits and what would push it out.
> **Planned against `main` @ `656f58e6`.** This checkout is on `fix/phn-5-phone-pii-out-of-the-logs`
> @ `35e4ed5a`, so every anchor below was checked against `main` before it was written down:
>
> ```
> $ git diff --stat main..HEAD -- \
>     src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs \
>     src/Radio.Infrastructure/Audio/Services/AudioManager.cs \
>     src/Radio.Infrastructure/Audio/Sources/AudioSourceBase.cs \
>     src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs \
>     src/Radio.Infrastructure/Audio/Sources/Primary/USBAudioSourceBase.cs \
>     src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs \
>     src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs \
>     docs/HANDOFF-GA-PUNCH-LIST.md \
>     tests/Radio.Infrastructure.Tests/Audio/Services/AudioManagerTests.cs
> (empty)
> ```
>
> **Every file this row touches is byte-identical between `main` and this checkout**, so the working-tree
> line numbers below are valid for `main`. The one file that is *not* identical is
> `docs/BUILDER_QUEUE.md` (6 lines), which is why the row's own line number is given twice above.
>
> **The verdict is settled and this plan does not re-derive it.** A `team-debugger` pass on 2026-09-05
> confirmed `AUD-2` at ~90% confidence. What follows re-verifies the anchors (§0.4 — five of the row's
> had drifted) and turns the verdict into work.

---

## 0. Read this before Task 1

### 0.1 The defect, in one paragraph

Four of six primary sources register their SoundFlow component under a **minted** key, while
`AudioManager` addresses them by the source's `IAudioSource.Id`. `SoundFlowPlaybackService`'s
dictionaries are declared without a `StringComparer` (`SoundFlowPlaybackService.cs:24-28`), so lookups
are **ordinal and case-sensitive**; the two keys are never equal, and every `TryGetValue` misses. The
miss is silent — `ApplyEffectiveVolume` (`:710-726`) has two `TryGetValue` calls and no `else`. The
consequence is that **per-source gain and event-audio ducking are dead for those four sources**: the
value is written into `_gainOffsets` / `_duckingMultipliers` under a key nothing will ever read, and
the caller logs success.

### 0.2 The affected matrix, re-derived at `main` @ `656f58e6`

| Source | Registration literal | Site | `Id` (from `AudioSourceBase.cs:28`) | Match |
|---|---|---|---|---|
| `SDRRadioAudioSource` | `$"sdr-radio-{Guid.NewGuid():N}"` | `SDRRadioAudioSource.cs:915` | `Radio-<32 hex>` | ✗ |
| `RadioAudioSource` (via base) | `$"usb-capture-{Id:N}"` | `USBAudioSourceBase.cs:317` | `Radio-<32 hex>` | ✗ |
| `VinylAudioSource` (via base) | `$"usb-capture-{Id:N}"` | `USBAudioSourceBase.cs:317` | `Vinyl-<32 hex>` | ✗ |
| `GenericUSBAudioSource` (via base) | `$"usb-capture-{Id:N}"` | `USBAudioSourceBase.cs:317` | `GenericUSB-<32 hex>` | ✗ |
| `FilePlayerAudioSource` | `$"file-player-{Guid.NewGuid():N}"` | `FilePlayerAudioSource.cs:727` | `FilePlayer-<32 hex>` | ✗ |
| `BluetoothAudioSource` | `Id` | `BluetoothAudioSource.cs:561`, `:580` | `Bluetooth-<32 hex>` | ✓ fixed by `2bbd0eb5` |
| `TestToneAudioSource` | `Id`, passed inline | `TestToneAudioSource.cs:73` | `TestTone-<32 hex>` | ✓ |

⚠ **`TestToneAudioSource` reaches the right answer by a different route, and §4.2's lint depends on
knowing that.** It has **no `_playbackId` field at all** — it passes `Id` straight into the call:

```csharp
    await _playbackService.PlayComponentAsync(Id, _generator, Volume, cancellationToken);
```

and symmetrically `StopAsync(Id, …)` at `:94` and `:107`. So a lint that only inspects `_playbackId`
assignments cannot see this source *or* a future one written in its shape. §4.2 carries a second rule
for exactly that reason.

Two further properties of the table are worth stating because they are each load-bearing:

- **`AudioSourceBase.cs:28` is the ONLY definition of `Id` on the audio-source hierarchy**, and nothing
  overrides it. Verified by regex over `src/`: the other `string Id` members are API/Web DTOs,
  `AudioOutputBase.cs:33` (an *output*, not a source) and a private class in
  `EventPlaybackService.cs:1638`. So `Id` really is `$"{Type}-{Guid.NewGuid():N}"` for all seven rows,
  cached in `_id` on first read (`AudioSourceBase.cs:16`).
- **`$"usb-capture-{Id:N}"` is not a near-miss, it is a miss.** `Id` is a `string`, and `string` is
  not `IFormattable`, so the `:N` format specifier is **ignored** rather than applied. The result
  *contains* the Id as a substring but is not *equal* to it, and ordinal dictionary lookup wants
  equality. Anyone reading that line quickly will assume it round-trips; it does not.

### 0.3 The precedent this plan follows

Commit **`2bbd0eb5`** (2026-03-02, *"fix: Use source ID as playback ID so BT auto-gain applies to
visualization"*) fixed this exact shape for Bluetooth. Its message describes the identical mechanism —
a random `bt-capture-{guid}` key against `AudioManager.SetSourceGain`'s use of `source.Id`, with the
`+28dB` auto-gain silently never applied. The fix was two lines, `_playbackId = Id`, and it left its
rationale in place at `BluetoothAudioSource.cs:559-561`:

```csharp
        // Use the audio source ID so AudioManager.SetSourceGain can find
        // this component and apply the gain offset (e.g., auto-gain +28dB).
        _playbackId = Id;
```

**Task 1 is that fix, three more times.** Same shape, same rationale comment, same direction.

### 0.4 ⚠ Anchor corrections — five of the row's had drifted

The row closes with: *"Every anchor in this row re-verified 2026-08-11 against `main` @ `8b1ce0a` and
all are byte-exact and unchanged."* That was true on 2026-08-11 and is **not** true now. Re-derived at
`main` @ `656f58e6`:

| Row's anchor | Actual at `656f58e6` | |
|---|---|---|
| `SDRRadioAudioSource.cs:908` | **`:915`** | ✗ drifted |
| `SDRRadioAudioSource.cs:956-960` | **`:963-967`** | ✗ drifted |
| `SoundFlowPlaybackService.cs:620` (`SetGainOffset`) | **`:656`** | ✗ drifted |
| `SoundFlowPlaybackService.cs:643` (`SetDuckingMultiplier`) | **`:679`** | ✗ drifted |
| `AudioManager.cs:508` (`ClearDuckingMultiplier`) | **`:554`** | ✗ drifted |
| `SoundFlowPlaybackService.cs:734`/`:752` (diagnostics) | **`:766-772`** (`GetDiagnostics`) | ✗ drifted, and see below |
| `SDRRadioAudioSource.cs:204` (`Type => Radio`) | `:204` | ✓ |
| `SoundFlowPlaybackService.cs:25` (dict) | `:25` | ✓ |
| `SoundFlowPlaybackService.cs:423` (registration) | `:423` | ✓ |
| `AudioManager.cs:121`/`:240`/`:247`/`:292`/`:479` | unchanged | ✓ |
| `AudioSourceBase.cs:28` | `:28` | ✓ |

⚠ **One row anchor is worse than drifted — the corroboration step it proposes does not work.** The row
suggests *"dumping the live keys (`SoundFlowPlaybackService` already exposes `_activePlayers.Keys` /
component counts around `:734` and `:752`)"*. The method is `GetDiagnostics()` at `:766-772`, and it
returns `(_activePlayers.Count, _activeComponents.Count, _activePlayers.Keys.ToArray())` — **player
keys only**. Every source in §0.2 except `FilePlayer` registers as a *component*
(`PlayComponentAsync`), so for exactly the sources this row is about, `PlayerIds` comes back **empty**
and proves nothing. Do not use it as the confirmation instrument; use §6's UAT instead.

### 0.5 Scope: three files, four source types — **not** SDR-only

The row's title and its branch name (`fix/sdr-playback-id-ducking-gain`) both say SDR. The row's body
only ever analyses `SDRRadioAudioSource`. **That framing is too narrow and must not shrink the fix**:
`USBAudioSourceBase.cs:317` is a single site inherited by **three** concrete sources
(`RadioAudioSource`, `VinylAudioSource`, `GenericUSBAudioSource`), and `FilePlayerAudioSource.cs:727`
is a fourth. Keep the branch name — the row names it, and renaming it costs a queue edit for nothing —
but the PR title and body must say four source types.

**Explicitly out of scope, and not an oversight:** `AudioFileEventSource.cs:146` mints
`$"audio-event-{Guid.NewGuid():N}"` and has the same *shape*. It is an **event** source, and
`AudioManager` only ever addresses `_activeSource` / `source`, which are primary sources — so the
divergence reaches no gain or ducking path. It is already documented as deliberate at
`EventPlaybackService.cs:32-41`, which describes a third id space (`"evp-"`) built precisely because
`AudioFileEventSource` carries `Id` and `_playbackId` unequal while `TTSEventSource` uses `Id` for
both. **Leave it alone**, and say so in the PR so a reviewer does not read it as a missed site.

### 0.6 ⚠ The trap: do not add a translation layer

The row's own note is correct and is repeated here because it is the one way this fix can go wrong:
**do not "fix" this by teaching `AudioManager` to translate `Id` → `sdr-radio-*`**, and do not add an
alias map inside `SoundFlowPlaybackService`. Either would make the symptom go away while leaving two
key spaces in the tree, and the next source added would reintroduce the bug. The answer is **one key
per source, agreed by both layers** — which is what `2bbd0eb5` did and what Task 1 does.

Nothing parses the current prefixes, so removing them is safe. Verified by regex over all `*.cs`:
`usb-capture` / `sdr-radio` / `file-player` appear **only** at the three assignment sites in §0.2.
There is no `StartsWith`, no `Split`, no substring test anywhere on those strings — they are
write-only.

### 0.7 Where these log lines land — read before choosing a level

Per `CLAUDE.md` § *Deployment*, `Radio.Infrastructure` has no sink of its own; its lines land in
whichever host loads it. Both `AudioManager` and `SoundFlowPlaybackService` are loaded by
**`Radio.API`**, so `src/Radio.API/appsettings.json` governs, and it was read for this plan:

- `Serilog.MinimumLevel.Default` = **`Warning`**, with `Override` `"Radio"` = **`Information"`**.
- The only sink is `Async` → `File` at `./logs/radio-.txt`. **There is no Console sink in the file at
  all**, which together with `LOG-11` is why `journalctl -u radio-api` carries Warning and above.

Three consequences that drive Task 2's levels:

1. **`Debug` and `Trace` are written nowhere on the box.** The `Radio` override floors at
   `Information`. So `SoundFlowPlaybackService.cs:666` (`LogDebug`, *"Applied gain offset …"*) and
   `AudioManager.cs:481` (`LogDebug`, *"Ducking level: …"*) are invisible in **both** the journal and
   the file sink on a stock box. Correcting their *wording* alone would change nothing an operator can
   see — which is a large part of why this survived. Task 2 therefore puts the new signal at
   **`Warning`**, deliberately.
2. **A `Warning` reaches journald.** That is the point: this condition is a defect, is rare, and
   should be loud.
3. ⚠ **But a `Warning` on the ducking path can become a storm, and that would be its own bug.**
   `DuckingService.CalculateFadeParameters` (`DuckingService.cs:491-495`) gives `FadeSmooth`
   `Math.Max(5, requestedDurationMs / 16)` steps — ~18 for a 300 ms fade — and each step raises
   `DuckingLevelChanged` → `AudioManager.OnDuckingLevelChanged` → `SetDuckingMultiplier`. Attack plus
   release is ~36 calls per duck cycle. An ungated warning would emit dozens of journald lines per TTS
   announcement **on a box where log volume correlates with audible audio distortion** (`CLAUDE.md`
   § *Deployment*, and the memory note on SSH/journald contention). Task 2c gates it on
   `TransitionComplete`. That gate is load-bearing, not cosmetic.

### 0.8 ⚠ A miss is not always a defect — this shapes the whole of Task 2

The obvious implementation ("warn whenever the lookup misses") would be **wrong**, and would ship the
same class of false claim in the opposite direction. `AudioManager.cs:288-295` applies the stored gain
offset on **every** source switch, and `FilePlayerAudioSource` has `canAutoPlay = false`
(`AudioManager.cs:261`) — so switching to FilePlayer legitimately stores a gain offset while nothing
is registered. That is a correct, expected miss, and the offset is picked up later:
`PlayFileAsync`/`PlayComponentAsync` read `_gainOffsets.GetValueOrDefault(sourceId, 1.0f)` at
registration time (`SoundFlowPlaybackService.cs:407`, `:343`).

**`SoundFlowPlaybackService` cannot tell the two apart** — it knows its dictionaries, not whether the
source is supposed to be live. **`AudioManager` can**: it holds the `IAudioSource` and its `State`.
Hence the split in Task 2: the service *reports* whether it applied anything, and `AudioManager`
decides whether that is news. The predicate for "this is a defect" is
**`!applied && source.State == AudioSourceState.Playing`** (`IAudioSource.cs:108`).

---

## 1. Task 1 — the fix: `_playbackId = Id` at three sites

Three one-line changes. Each keeps a rationale comment, following `2bbd0eb5`'s example, because the
next person to read a bare `_playbackId = Id` has no way to know it is load-bearing.

### 1.1 `src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs:914-915`

Replace:

```csharp
    // Generate a playback ID for this session
    _playbackId = $"sdr-radio-{Guid.NewGuid():N}";
```

with:

```csharp
    // Register under the source's own Id — NOT a minted key. AudioManager addresses this source's
    // gain and ducking by IAudioSource.Id (AudioManager.cs:121/:292/:479/:554), and
    // SoundFlowPlaybackService's dictionaries are ordinal (no StringComparer, :24-28), so a minted
    // key misses every lookup silently. Same fix and same reason as BluetoothAudioSource (2bbd0eb5).
    _playbackId = Id;
```

### 1.2 `src/Radio.Infrastructure/Audio/Sources/Primary/USBAudioSourceBase.cs:317`

Replace:

```csharp
        _playbackId = $"usb-capture-{Id:N}";
```

with:

```csharp
        // Id itself, not a prefixed derivative. `$"usb-capture-{Id:N}"` CONTAINS the Id but is not
        // EQUAL to it — Id is a string, so the `:N` specifier was silently ignored — and
        // SoundFlowPlaybackService keys on ordinal equality. This one line covers three concrete
        // sources: RadioAudioSource, VinylAudioSource and GenericUSBAudioSource.
        _playbackId = Id;
```

### 1.3 `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs:726-727`

Replace:

```csharp
    // Generate playback ID for this session
    _playbackId = $"file-player-{Guid.NewGuid():N}";
```

with:

```csharp
    // The source's own Id, which is stable for the lifetime of the source instance. A per-session
    // GUID was wrong twice over: it missed AudioManager's gain/ducking lookups, and because
    // StopAsync deliberately KEEPS _gainOffsets (:512), every track change also left one entry
    // there that nothing would ever read or remove.
    _playbackId = Id;
```

### 1.4 Ordering is already correct — no call-site reordering is needed

Worth checking explicitly, because if `SetGainOffset` ran before registration the fix would appear not
to work. It does not: `AudioManager.SwitchSourceAsync` calls `newPrimary.PlayAsync(...)` at `:278` and
only then applies the gain offset at `:288-295`. And for the paths where the offset genuinely does
precede registration (FilePlayer, §0.8), the registration path re-reads `_gainOffsets` anyway. **No
change required here** — this note exists so the Builder does not go looking for one.

---

## 2. Task 2 — make a miss say so

**This half is not in the row, and it matters as much as the fix.** `SoundFlowPlaybackService.cs:666`
currently logs *"Applied gain offset {Gain:F2} to source (SourceId={SourceId})"* **unconditionally**,
immediately after a lookup that may have matched nothing. That is the failure class `CLAUDE.md`
§ *Pre-Merge Review* names — *"a log message describing an action stronger than what actually
occurred"* — and it is why `AUD-2` survived for months: the only instrument anyone had said the thing
worked. **Do this even in the counterfactual where the keys had matched.**

### 2.1 `ApplyEffectiveVolume` reports whether it reached anything

`src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs:706-726` becomes:

```csharp
  /// <summary>
  /// Recalculates and applies the effective volume for a source.
  /// Must be called under _playersLock.
  /// </summary>
  /// <returns>
  /// True if a live player or component was registered under <paramref name="sourceId"/> and its
  /// volume was set; false if the id matched nothing.
  /// <para>
  /// ⚠ A false return is NOT by itself a defect, and no caller may treat it as one. A source can
  /// legitimately carry a stored gain offset while stopped — AudioManager applies the stored offset
  /// on every source switch (AudioManager.cs:288-295) and FilePlayer does not auto-play
  /// (AudioManager.cs:261) — and the offset is picked up at registration time by the
  /// _gainOffsets.GetValueOrDefault reads in PlayComponentAsync (:407) and PlayFileAsync (:343).
  /// This class cannot distinguish that from a key mismatch, because it knows its dictionaries and
  /// not whether the source was supposed to be live. AudioManager can, and does. See AUD-2 §0.8.
  /// </para>
  /// </returns>
  private bool ApplyEffectiveVolume(string sourceId)
  {
    var baseVol = _baseVolumes.GetValueOrDefault(sourceId, 1.0f);
    var gainOffset = _gainOffsets.GetValueOrDefault(sourceId, 1.0f);
    var duckMult = _duckingMultipliers.GetValueOrDefault(sourceId, 1.0f);
    var effective = Math.Clamp(baseVol * gainOffset * duckMult,
      AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

    var applied = false;

    if (_activePlayers.TryGetValue(sourceId, out var player))
    {
      player.Volume = effective;
      applied = true;
    }
    if (_activeComponents.TryGetValue(sourceId, out var component))
    {
      component.Volume = effective;
      applied = true;
    }

    return applied;
  }
```

### 2.2 The four setters return it

All four change `void` → `bool`. That is source-compatible: none of them is on an interface
(`SoundFlowPlaybackService` is a concrete class with no `ISoundFlowPlaybackService` in the tree —
see §4.1), and every existing caller ignores the value. `SetVolume` is included for uniformity even
though it is not implicated, because a sibling that alone returns nothing invites the question later.

`:640-649`:

```csharp
  /// <returns>True if a live player or component received the new volume. See ApplyEffectiveVolume.</returns>
  public bool SetVolume(string sourceId, float volume)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      _baseVolumes[sourceId] = Math.Clamp(volume, 0f, 1f);
      return ApplyEffectiveVolume(sourceId);
    }
  }
```

`:656-669` — the line the row calls out, and the reason for this task:

```csharp
  /// <returns>True if a live player or component received the new gain. See ApplyEffectiveVolume.</returns>
  public bool SetGainOffset(string sourceId, float gainOffset)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      gainOffset = Math.Clamp(gainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);
      _gainOffsets[sourceId] = gainOffset;
      var applied = ApplyEffectiveVolume(sourceId);

      // ⚠ TWO OUTCOMES, TWO MESSAGES. The single line this replaces — "Applied gain offset {Gain:F2}
      // to source (SourceId={SourceId})" — was printed on BOTH paths, including the one where the
      // dictionary lookup matched nothing and no volume moved. It asserted a success it never
      // checked, which is the class CLAUDE.md § Pre-Merge Review names, and it is what made AUD-2
      // invisible for months: the only instrument anyone had reported success either way.
      //
      // Both arms stay at Debug, and that is deliberate rather than an oversight: neither is an
      // error at THIS layer (§0.8), and Radio.API's Serilog floor for "Radio" is Information, so
      // neither reaches the box at all. The operator-visible signal is AudioManager's warning in
      // §2.3, which is the only layer that can tell a stored offset from a broken key.
      if (applied)
      {
        _logger.LogDebug(
          "Applied gain offset {Gain:F2} to live playback (SourceId={SourceId})",
          gainOffset, sourceId);
      }
      else
      {
        _logger.LogDebug(
          "Stored gain offset {Gain:F2} for SourceId={SourceId}; nothing is registered under that " +
          "key, so no volume changed. It applies when the source next starts.",
          gainOffset, sourceId);
      }

      return applied;
    }
  }
```

`:679-689` and `:695-704`:

```csharp
  /// <returns>True if a live player or component received the multiplier. See ApplyEffectiveVolume.</returns>
  public bool SetDuckingMultiplier(string sourceId, float multiplier)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      multiplier = Math.Clamp(multiplier, 0f, 1f);
      _duckingMultipliers[sourceId] = multiplier;
      return ApplyEffectiveVolume(sourceId);
    }
  }
```

```csharp
  /// <returns>
  /// True if a live player or component had its volume restored. False means nothing was registered
  /// under this id — which on the ducking-ended path means no volume was restored, and the caller
  /// must not claim otherwise. See AudioManager.OnDuckingStateChanged.
  /// </returns>
  public bool ClearDuckingMultiplier(string sourceId)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      _duckingMultipliers.Remove(sourceId);
      return ApplyEffectiveVolume(sourceId);
    }
  }
```

Neither gains a log line here: they have none today, and the operator-visible signal belongs one
layer up.

### 2.3 `AudioManager` turns "didn't apply" into "shouldn't have failed"

Four call sites. The predicate is `!applied && <source>.State == AudioSourceState.Playing` — §0.8.

**`AudioManager.cs:112-126`** (`SetSourceGain`, the live-slider path):

```csharp
  /// <inheritdoc/>
  public void SetSourceGain(AudioSourceType sourceType, float gain)
  {
    gain = Math.Clamp(gain, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);
    _preferencePersistence?.SetSourceGain(sourceType, gain);

    // If this source is currently active, update the live playback component gain
    if (_activeSource != null && _activeSource.Type == sourceType && _playbackService != null)
    {
      var applied = _playbackService.SetGainOffset(_activeSource.Id, gain);

      if (applied)
      {
        _logger.LogInformation(
          "Applied live gain offset {Gain:F2} to active source {SourceName}",
          gain, _activeSource.Name);
      }
      else if (_activeSource.State == AudioSourceState.Playing)
      {
        // ⚠ THE AUD-2 SIGNATURE. The source reports Playing, yet nothing is registered under its
        // Id — the two layers disagree about this source's key, and the user's gain slider is
        // moving nothing. Warning, not Information: since LOG-11 radio-api's journal carries
        // Warning and above, and Radio.API's file sink floors at Information, so Debug would be
        // written nowhere at all (CLAUDE.md § Deployment). This is rare and user-visible; it should
        // be loud.
        _logger.LogWarning(
          "Gain offset {Gain:F2} did NOT reach {SourceName}: no player or component is registered " +
          "under SourceId={SourceId} while the source reports Playing. The source's playback key " +
          "and its IAudioSource.Id have diverged.",
          gain, _activeSource.Name, _activeSource.Id);
      }
      else
      {
        _logger.LogDebug(
          "Stored gain offset {Gain:F2} for {SourceName} (State={State}); it applies when playback starts.",
          gain, _activeSource.Name, _activeSource.State);
      }
    }
  }
```

**`AudioManager.cs:288-295`** (switch-time gain):

```csharp
      // Apply per-source gain offset
      if (_playbackService != null && _preferencePersistence != null)
      {
        var gain = _preferencePersistence.GetSourceGain(source.Type);
        var applied = _playbackService.SetGainOffset(source.Id, gain);

        if (!applied && source.State == AudioSourceState.Playing)
        {
          _logger.LogWarning(
            "Gain offset {Gain:F2} did NOT reach {SourceName} ({SourceType}): nothing is registered " +
            "under SourceId={SourceId} while the source reports Playing.",
            gain, source.Name, source.Type, source.Id);
        }
        else
        {
          // applied=false with a non-Playing source is the NORMAL case here, not a failure:
          // FilePlayer has canAutoPlay=false (:261), so a switch to it stores the offset with
          // nothing yet registered, and PlayFileAsync picks it up at :343. Reporting `applied`
          // rather than asserting success is the whole point of this task.
          _logger.LogDebug(
            "Gain offset {Gain:F2} for source {SourceName} ({SourceType}), applied={Applied}",
            gain, source.Name, source.Type, applied);
        }
      }
```

**`AudioManager.cs:470-484`** (`OnDuckingLevelChanged`, the hot path):

```csharp
  private void OnDuckingLevelChanged(object? sender, DuckingLevelChangedEventArgs e)
  {
    if (_playbackService == null || _activeSource == null)
    {
      return;
    }

    // Convert duck level percentage (0-100) to multiplier (0.0-1.0)
    var multiplier = e.NewLevel / 100f;
    var applied = _playbackService.SetDuckingMultiplier(_activeSource.Id, multiplier);

    _logger.LogDebug(
      "Ducking level: {PrevLevel:F0}% -> {NewLevel:F0}% (multiplier={Mult:F2}, source={Source}, applied={Applied})",
      e.PreviousLevel, e.NewLevel, multiplier, _activeSource.Name, applied);

    // ⚠ THE TransitionComplete GATE IS LOAD-BEARING AND MUST NOT BE REMOVED AS REDUNDANT.
    // This handler runs once per fade STEP, and DuckingService.CalculateFadeParameters gives
    // FadeSmooth Math.Max(5, requestedDurationMs / 16) steps (DuckingService.cs:491-495) — ~18 for a
    // 300 ms fade, and attack plus release is ~36 calls per duck cycle. Warning on every step would
    // put dozens of lines into journald per TTS announcement, on a box where log volume correlates
    // with audible audio distortion (CLAUDE.md § Deployment). TransitionComplete
    // (IDuckingService.cs:187) is raised once per transition, which bounds this at two lines per
    // duck cycle while still catching any persistent divergence — the condition is not transient,
    // so sampling it at the end of each transition loses nothing.
    if (!applied && e.TransitionComplete && _activeSource.State == AudioSourceState.Playing)
    {
      _logger.LogWarning(
        "Ducking multiplier {Mult:F2} did NOT reach {SourceName}: nothing is registered under " +
        "SourceId={SourceId} while the source reports Playing. Event audio is not ducking this source.",
        multiplier, _activeSource.Name, _activeSource.Id);
    }
  }
```

**`AudioManager.cs:546-559`** (ducking ended). ⚠ **This block carries a second unconditional
overclaim, and it is the one the punch list cites.** The existing line reads *"Ducking ended: volume
restored, activeEvents={EventCount}"* and prints whether or not anything was restored — including when
`_activeSource` is null and there is nothing to restore at all:

```csharp
    // Ducking ended — clear all ducking multipliers to restore full volume.
    // ⚠ SEMANTICALLY unchanged from before PHN-1f, deliberately: the edge is still literally
    // `!e.IsDucking`, and nothing about when this block runs has moved. The BYTES did move — the block
    // came out of an `else` and now sits behind an early `return`, two spaces to the left — and an
    // earlier revision of this comment said "byte for byte", which is the kind of claim a diff
    // falsifies at a glance.
    var restored = false;
    if (_activeSource != null)
    {
      restored = _playbackService.ClearDuckingMultiplier(_activeSource.Id);

      if (!restored && _activeSource.State == AudioSourceState.Playing)
      {
        _logger.LogWarning(
          "Ducking release did NOT reach {SourceName}: nothing is registered under SourceId={SourceId} " +
          "while the source reports Playing. The source may be left at its ducked volume.",
          _activeSource.Name, _activeSource.Id);
      }
    }

    // ⚠ "volumeRestored" is now REPORTED, not ASSERTED. The wording this replaces — "Ducking ended:
    // volume restored, activeEvents={EventCount}" — was printed unconditionally: with a null
    // _activeSource (nothing to restore) and with a missed key (nothing restored) it said exactly
    // what it says on success. docs/HANDOFF-GA-PUNCH-LIST.md:918-919 cites this family of lines as
    // evidence that ducking works end to end. It never was such evidence, and §5 corrects that entry.
    _logger.LogInformation(
      "Ducking ended: activeEvents={EventCount}, volumeRestored={Restored}",
      e.ActiveEventCount, restored);
```

⚠ **Do not restructure `OnDuckingStateChanged`'s branching while doing this.** Its `<remarks>` at
`AudioManager.cs:489-501` records, in detail, why the outer branch is keyed on `IsDucking` and not on
`Transition`, and `AudioManagerTests.cs:446-496` pins it with a named mutation. Task 2 adds a local
and reworks two log statements inside the existing block; it moves no branch.

---

## 3. Task 3 — a stop/start invariant that is currently false

`SoundFlowPlaybackService.cs:512` reads:

```csharp
      // Keep _gainOffsets — they persist across stop/start for the same source
```

**That claim is conditional on something the code does not currently provide.** It holds only if the
key is stable across a stop/start cycle. `SDRRadioAudioSource` and `FilePlayerAudioSource` mint a
fresh GUID on **every** `PlayCoreAsync` (`:915`, `:727`), so for them the comment is false in both
directions: the retained offset is never found again, *and* every cycle leaves behind an entry that
nothing will ever read or remove. (`USBAudioSourceBase` is the milder case — its key derives from the
stable `Id`, so it is constant across cycles, just never equal to the key `AudioManager` uses.)

Task 1 is what makes the comment true. Replace `:510-512` with:

```csharp
      _baseVolumes.Remove(sourceId);
      _duckingMultipliers.Remove(sourceId);
      // Keep _gainOffsets: they persist across a stop/start cycle for the same source.
      //
      // ⚠ This is true only because the playback key is the source's IAudioSource.Id, which
      // AudioSourceBase caches in _id on first read (AudioSourceBase.cs:28) and is therefore stable
      // for the lifetime of the source instance. Before AUD-2 the claim was FALSE for SDR and
      // FilePlayer, which minted a fresh GUID on every PlayCoreAsync: the retained offset was never
      // found again after a restart, AND every cycle leaked one entry here that nothing would ever
      // read or remove. If a future source registers under anything other than Id, this line stops
      // being true again — PlaybackKeyLintTests (§4.2) is what keeps that from happening quietly.
```

⚠ **Check the wording against the post-fix reality rather than leaving an overclaim in place.** The
retention is per **source instance**, not per source *type*: `_id` is an instance field, so disposing
and recreating a source (a device re-enumeration, say) produces a new `Id` and the entry from the old
instance is orphaned. The comment above therefore says *"for the lifetime of the source instance"* and
not *"for the same source"*, which would be the easy overclaim to write here. **The per-instance
orphan is pre-existing, is bounded by the number of source instances ever created, and is explicitly
NOT in this row's scope** — do not fix it here, and do not claim it is fixed.

---

## 4. Task 4 — tests

### 4.1 The honest verdict: today, nothing covers this

A regex for `SetDuckingMultiplier|SetGainOffset|_playbackId|ClearDuckingMultiplier` across `tests/`
returns **no behavioural coverage**. The only hits are prose in comments
(`AudioManagerTests.cs:386`/`:452`/`:456`/`:459`/`:462`/`:490`/`:508`,
`DuckingServiceTests.cs:289`) and a *different* `GetDiagnostics` — the one on
`BufferedSoundGenerator`, in `FmAudioDropoutDiagnosticTests.cs`. Nothing calls these three setters.

`AudioManagerTests.cs:384-392` says why, and the constraint is real:

> ⚠ THE PLAYBACK SERVICE IS REAL, NOT MOCKED, AND THAT IS FORCED RATHER THAN CHOSEN.
> AudioManager's constructor takes the CONCRETE SoundFlowPlaybackService — there is no
> ISoundFlowPlaybackService in the tree — and ClearDuckingMultiplier is a non-virtual public
> method, so Moq can neither substitute it nor record the call.

**What that constraint does and does not block, restated for this row.** It blocks *recording* a call
on a mock. It does **not** block driving the real service: `AudioManagerTests.CreatePlaybackService()`
(`:416-444`) and `SoundFlowPlaybackServiceTransportTests.CreateService()` (`:44-80`) both construct a
real `SoundFlowPlaybackService` over an uninitialised `SoundFlowAudioEngine`, reaching no hardware —
`GetUnderlyingEngine()` returns the null `_engine` (`SoundFlowAudioEngine.cs:769`) and
`GetPlaybackDevice()` returns null without attempting recovery, because its recovery arm is gated on
`_engine != null` (`:776-791`).

**The pivotal consequence.** With no device, `PlayComponentAsync` and `PlayFileAsync` return early and
**register nothing**. So:

- ✅ **The miss direction is fully testable, with no seam and no hardware.** Every lookup misses by
  construction, which is exactly the condition Task 2's warning fires on.
- ❌ **The hit direction is not.** Proving that a *registered* component's `Volume` actually moves
  requires a real playback device. This is not a new limitation and not this row's to solve —
  `SoundFlowPlaybackServiceTransportTests`' own class summary already states it: *"The populated-dictionary
  paths need a real device and are exercised by UAT."* Same answer here. §6 is where the positive
  direction gets proved.

So: **the new behaviour (Task 2) is well covered by unit tests; the fix itself (Task 1) is covered by
a lint, and its audible effect is UAT-only.** Say exactly that in the PR. Do not let a green suite be
reported as proof that gain and ducking now work.

### 4.2 New — `tests/Radio.Infrastructure.Tests/Audio/Sources/PlaybackKeyLintTests.cs`

**This is what pins Task 1**, and it is the house idiom for exactly this job: `LogSafetyLintTests.cs`
(`tests/Radio.Core.Tests/`) is an existing source-text lint over `src/**/*.cs` that fails when a fixed
log shape is written again. A key lint is strictly better here than the alternatives — it needs no
hardware, no reflection on a private field, and no production seam, and it catches a *future* primary
source that mints its own key, which no runtime test would.

```csharp
using System.Text.RegularExpressions;

namespace Radio.Infrastructure.Tests.Audio.Sources;

/// <summary>
/// A regression lint over <c>src/Radio.Infrastructure/Audio/Sources/Primary/*.cs</c>, in two rules:
/// every assignment to <c>_playbackId</c> must be <c>Id</c> or <c>null</c>, and the key argument of
/// every <c>Play*Async</c> / <c>StopAsync</c> call must be <c>Id</c> or <c>_playbackId</c>.
/// </summary>
/// <remarks>
/// <b>What it pins (AUD-2).</b> Four primary sources registered their SoundFlow component under a
/// minted key — <c>$"sdr-radio-{Guid.NewGuid():N}"</c>, <c>$"usb-capture-{Id:N}"</c>,
/// <c>$"file-player-{Guid.NewGuid():N}"</c> — while AudioManager addressed them by
/// <c>IAudioSource.Id</c>. SoundFlowPlaybackService's dictionaries are ordinal, so every gain and
/// ducking lookup missed, silently, for months. Bluetooth had the identical bug and was fixed in
/// isolation by 2bbd0eb5 (2026-03-02) — this file exists because that fix pinned nothing, so the
/// same shape survived in four more sources for another six months.
///
/// <b>Why TWO rules.</b> The four broken sources all went through a <c>_playbackId</c> field, so
/// rule 1 alone would have caught AUD-2. It would NOT catch a new source written in
/// TestToneAudioSource's shape, which has no such field and passes <c>Id</c> straight into the call
/// (<c>TestToneAudioSource.cs:73</c>, <c>:94</c>, <c>:107</c>). That shape is correct today and is
/// the obvious template for the next source somebody adds — so rule 2 checks the call sites
/// directly, and a source using either idiom is covered.
///
/// ⚠⚠ <b>WHAT THIS TEST CANNOT DO.</b> It is a lint over source TEXT, not a proof of the property.
/// It cannot see a key reaching the call through a local (<c>var key = Mint();
/// PlayComponentAsync(key, …)</c>), through a helper method, or through a differently-named field —
/// rule 2 would simply see an identifier it does not recognise, which is why it FAILS on anything
/// that is not <c>Id</c> or <c>_playbackId</c> rather than trying to evaluate it. That is
/// deliberately strict: a false failure here is a five-second read, and a false pass is another six
/// months of silent no-op. It says nothing about whether the volume actually moves — that needs a
/// registered component, which needs a real playback device, which is UAT (plan §6). Read a green
/// run as "nobody re-typed the shape that broke", not as "gain and ducking work".
///
/// ⚠ <b>Event sources are deliberately out of the sweep.</b> AudioFileEventSource.cs:146 mints
/// <c>"audio-event-…"</c> and is CORRECT to: AudioManager only ever addresses primary sources, so
/// the divergence reaches no gain or ducking path, and EventPlaybackService.cs:32-41 documents that
/// id space on purpose. Widening this lint to <c>Sources/</c> would flag it and the rule would be
/// deleted within a week.
/// </remarks>
public class PlaybackKeyLintTests
{
  [Fact]
  public void EveryPrimarySourceRegistersUnderItsOwnId()
  {
    var dir = Path.Combine(RepoRoot(), "src", "Radio.Infrastructure", "Audio", "Sources", "Primary");
    Assert.True(Directory.Exists(dir), $"Primary source directory not found: {dir}");

    // `_playbackId = <rhs>;` — captures the right-hand side up to the semicolon.
    var assignment = new Regex(@"_playbackId\s*=\s*([^;]+);", RegexOptions.Compiled);
    var violations = new List<string>();

    foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
    {
      var lines = File.ReadAllLines(file);
      for (var i = 0; i < lines.Length; i++)
      {
        var match = assignment.Match(lines[i]);
        if (!match.Success)
        {
          continue;
        }

        var rhs = match.Groups[1].Value.Trim();
        if (rhs is "Id" or "null")
        {
          continue;
        }

        violations.Add($"{Path.GetFileName(file)}:{i + 1} — _playbackId = {rhs}");
      }
    }

    Assert.True(
      violations.Count == 0,
      "A primary audio source must register with SoundFlowPlaybackService under its own " +
      "IAudioSource.Id, so AudioManager's gain and ducking lookups can find it (AUD-2). " +
      "Offending assignments:\n  " + string.Join("\n  ", violations));
  }

  [Fact]
  public void EveryPlaybackServiceCallIsKeyedOnIdOrPlaybackId()
  {
    // Rule 2 — the call sites, for sources that carry no _playbackId field. See the class remarks.
    //
    // ⚠ WHOLE-FILE TEXT, NOT LINE BY LINE, AND THAT IS NOT A STYLE CHOICE. Four of the seven
    // Play*Async call sites in this directory put the key argument on the line AFTER the open paren
    // (SDRRadioAudioSource.cs:963-964, FilePlayerAudioSource.cs:739-740,
    // BluetoothAudioSource.cs:562-563 and :581-582, USBAudioSourceBase.cs:326-327). A per-line match
    // finds no argument on the opening line and SKIPS those calls silently — i.e. it would pass
    // happily on the exact four sources AUD-2 is about. `\s` matches newlines in .NET by default, so
    // matching over the full text handles both shapes with no extra options.
    var dir = Path.Combine(RepoRoot(), "src", "Radio.Infrastructure", "Audio", "Sources", "Primary");

    // First argument of PlayComponentAsync / PlayFileAsync / PlayDataProviderAsync / StopAsync,
    // taken up to the first comma or closing paren.
    var call = new Regex(
      @"_playbackService\s*[?]?\s*\.\s*(PlayComponentAsync|PlayFileAsync|PlayDataProviderAsync|StopAsync)\s*\(\s*([^,)\s]+)",
      RegexOptions.Compiled);
    var violations = new List<string>();
    var inspected = 0;

    foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
    {
      var text = File.ReadAllText(file);
      foreach (Match match in call.Matches(text))
      {
        var key = match.Groups[2].Value.Trim();
        if (key is "Id" or "_playbackId")
        {
          continue;
        }

        // 1-based line number of the call, for a message that can be jumped to.
        var line = text.Take(match.Index).Count(c => c == '\n') + 1;
        violations.Add($"{Path.GetFileName(file)}:{line} — {match.Groups[1].Value}({key}…)");
      }

      inspected += call.Matches(text).Count;
    }

    // ⛔ THE VACUITY GUARD, and it is the most important line in this file. A regex lint that matches
    // NOTHING passes — silently, forever, through every refactor that renames the field or reshapes
    // the call. This assertion fails loudly if the pattern stops seeing the code it is supposed to
    // police. The floor is deliberately under the true count (18 at 656f58e6 — Bluetooth 5,
    // FilePlayer 4, SDR 3, TestTone 3, USBAudioSourceBase 3) so ordinary churn does not trip it,
    // while a pattern that has gone blind still cannot pass.
    Assert.True(
      inspected >= 14,
      $"PlaybackKeyLintTests matched only {inspected} playback-service call sites under Primary/. " +
      "It matched 18 when written. The regex has almost certainly stopped matching the code rather " +
      "than the code having shrunk — fix the pattern, do NOT lower this floor.");

    Assert.True(
      violations.Count == 0,
      "A primary audio source must key SoundFlowPlaybackService calls on its own IAudioSource.Id " +
      "(directly, or via a _playbackId that rule 1 pins to Id) — AUD-2. Offending calls:\n  "
      + string.Join("\n  ", violations));
  }

  private static string RepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
    {
      dir = dir.Parent;
    }

    Assert.NotNull(dir);
    return dir!.FullName;
  }
}
```

⚠ **Before writing `RepoRoot()` from scratch, check how `LogSafetyLintTests.cs` locates `src/` and
reuse that**, rather than shipping a second walker that can disagree with the first about where the
repo root is.

**Mutation checks (all three must bite, and must be run — a lint nobody mutated is a lint nobody
knows works):**

1. Revert any one of §1.1–§1.3 → **rule 1** goes red, naming the file and line.
2. Rewrite one source in `TestToneAudioSource`'s shape with a minted key —
   `PlayComponentAsync($"x-{Guid.NewGuid():N}", …)` — → **rule 2** goes red. Rule 1 stays green,
   which is the whole reason rule 2 exists.
3. Break the regex (change `_playbackService` to `_nope`) → the **vacuity guard** goes red at 0.
   Without this check, mutations 1 and 2 can both be defeated by a pattern that matches nothing.

### 4.3 New — `tests/Radio.Infrastructure.Tests/Audio/SoundFlow/SoundFlowPlaybackServiceVolumeReportingTests.cs`

Pins §2.1 and §2.2. Build the service with `SoundFlowPlaybackServiceTransportTests.CreateService()`'s
construction (lift it or duplicate it; that file's comment explains why it reaches no device).

```csharp
  [Fact]
  public void SetGainOffset_ReturnsFalse_WhenNothingIsRegisteredUnderTheKey()
  {
    var service = CreateService();

    Assert.False(service.SetGainOffset("no-such-source", 1.5f));
  }

  [Fact]
  public void SetDuckingMultiplier_ReturnsFalse_WhenNothingIsRegisteredUnderTheKey()
  {
    var service = CreateService();

    Assert.False(service.SetDuckingMultiplier("no-such-source", 0.2f));
  }

  [Fact]
  public void ClearDuckingMultiplier_ReturnsFalse_WhenNothingIsRegisteredUnderTheKey()
  {
    // The ducking-ended path reads this to decide whether it may say "volume restored"
    // (AudioManager.OnDuckingStateChanged). False here is what makes that line honest.
    var service = CreateService();

    Assert.False(service.ClearDuckingMultiplier("no-such-source"));
  }

  [Fact]
  public void SetGainOffset_StillStoresTheOffset_WhenNothingIsRegistered()
  {
    // The false return must NOT be read as "the write was rejected". Storing the offset for a
    // not-yet-started source is the intended behaviour — PlayComponentAsync reads _gainOffsets back
    // at registration time (:407) — and is why the miss is not, on its own, an error (plan §0.8).
    // Observed through the only public surface available: a second call reports the same miss
    // rather than throwing or resetting.
    var service = CreateService();

    Assert.False(service.SetGainOffset("not-started-yet", 1.5f));
    Assert.False(service.SetGainOffset("not-started-yet", 1.5f));
  }
```

⚠ **`SetGainOffset_StillStoresTheOffset_…` is a weak test and must be named and commented as one** —
without a registered component there is no public read-back of `_gainOffsets`, so it pins the absence
of a throw and nothing more. The real proof of the store-then-apply path is UAT §6.3. Do not let it
imply otherwise.

### 4.4 New — `tests/Radio.Infrastructure.Tests/Audio/Services/AudioManagerVolumeMissLogTests.cs`

Pins §2.3, including the storm gate. This is the highest-value new file: it exercises the exact
condition `AUD-2` was in, using the real playback service with nothing registered.

The logger is a `Mock<ILogger<AudioManager>>` (rather than `_loggerMock.Object`) so `Log` can be
verified — the standard Moq shape for `ILogger` already used elsewhere in this suite.

```csharp
  [Fact]
  public async Task ADuckingMissOnAPlayingSourceWarnsOncePerTransition_NotOncePerFadeStep()
  {
    // ⛔ THE STORM PIN. FadeSmooth raises Math.Max(5, durationMs / 16) times per transition
    // (DuckingService.cs:491-495) — ~18 for a 300 ms fade. Warning per step would put dozens of
    // lines into journald per TTS announcement, on a box where log volume correlates with audible
    // audio distortion (CLAUDE.md § Deployment). The gate is e.TransitionComplete.
    //
    // MUTATION: drop `&& e.TransitionComplete` from OnDuckingLevelChanged and this goes red at 21.
    var (manager, ducking, _, source, logger) = await CreateManagerWithDuckingAndLoggerAsync();
    await using (manager)
    {
      source.SetupGet(s => s.State).Returns(AudioSourceState.Playing);

      for (var i = 0; i < 20; i++)
      {
        ducking.Raise(d => d.DuckingLevelChanged += null, ducking.Object,
          new DuckingLevelChangedEventArgs
          {
            PreviousLevel = 100f - i,
            NewLevel = 99f - i,
            TransitionComplete = false
          });
      }

      ducking.Raise(d => d.DuckingLevelChanged += null, ducking.Object,
        new DuckingLevelChangedEventArgs
        {
          PreviousLevel = 80f,
          NewLevel = 20f,
          TransitionComplete = true
        });

      VerifyWarningCount(logger, Times.Once());
    }
  }

  [Fact]
  public async Task ADuckingMissOnANonPlayingSourceDoesNotWarn()
  {
    // The other half of §0.8: applied=false is the NORMAL case for a source that is not playing,
    // and warning on it would ship the same overclaim in the opposite direction.
    var (manager, ducking, _, source, logger) = await CreateManagerWithDuckingAndLoggerAsync();
    await using (manager)
    {
      source.SetupGet(s => s.State).Returns(AudioSourceState.Ready);

      ducking.Raise(d => d.DuckingLevelChanged += null, ducking.Object,
        new DuckingLevelChangedEventArgs
        {
          PreviousLevel = 100f, NewLevel = 20f, TransitionComplete = true
        });

      VerifyWarningCount(logger, Times.Never());
    }
  }

  [Fact]
  public async Task SetSourceGainWarnsWhenTheKeyMissesOnAPlayingSource()
  {
    // The user-facing symptom, at the layer that can recognise it: the slider moves, the source
    // says Playing, and nothing is registered under its Id. This is AUD-2's signature.
    var (manager, _, _, source, logger) = await CreateManagerWithDuckingAndLoggerAsync();
    await using (manager)
    {
      source.SetupGet(s => s.State).Returns(AudioSourceState.Playing);

      manager.SetSourceGain(AudioSourceType.Radio, 1.5f);

      VerifyWarningCount(logger, Times.Once());
    }
  }

  private static void VerifyWarningCount(Mock<ILogger<AudioManager>> logger, Times times) =>
    logger.Verify(
      l => l.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception?>(),
        (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
      times);
```

`CreateManagerWithDuckingAndLoggerAsync` is `CreateManagerWithDuckingAsync` (`AudioManagerTests.cs:392-414`)
with the logger returned instead of discarded and `State` settable on the mock source. ⚠ Check whether
`CreateMockPrimarySource` already stubs `State`; if it does, these tests must override rather than
add a second setup, or Moq will keep the first.

⚠ **`VerifyWarningCount` counts warnings from the whole handler, not from one line.** If `AudioManager`
grows an unrelated warning on these paths, these tests get more brittle, not less correct — prefer
tightening the `It.IsAnyType` matcher to the message text if that happens, rather than raising the
expected count.

### 4.5 The option not taken, priced

**Introduce `ISoundFlowPlaybackService`.** It would let Moq record `SetGainOffset` / `SetDuckingMultiplier`
directly and would let a test assert the *hit* direction without a device — which is the one thing
§4.1 says cannot be covered.

**Cost:** the interface needs roughly 20 public members (`PlayFileAsync`, `PlayComponentAsync`,
`PlayDataProviderAsync`, `PlayStreamAsync`, `StopAsync`, `StopAll`, `SetVolume`, `SetGainOffset`,
`SetDuckingMultiplier`, `ClearDuckingMultiplier`, `IsPlaying`, `GetPosition`, `Seek`, `GetDiagnostics`,
`GetAudioFormat`, `GetUnderlyingEngine`, the `GeneratorStalled` event, …), two of which return
SoundFlow types (`MiniAudioEngine?`, `AudioPlaybackDevice?`) and are `internal` on the engine — so the
interface either leaks the audio library across a boundary that currently contains it, or gets carved
into two. Every construction site and the DI wiring changes. It is a refactor with its own review
surface and its own regression risk, on the hot audio path.

**Verdict: do not take it in this row.** The seam's value is real but it is a `TEST-*` row of its own,
and taking it here would bury a three-line fix under a 20-member interface. §4.2–§4.4 get the coverage
that is available without it, and §6 covers the rest. **If the Builder finds this frustrating, that
frustration is the argument for filing the row, not for widening this one.**

### 4.6 Regression check

`dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"`, then read the
per-project `Passed!`/`Failed!` lines. ⚠ Never pipe into `tail` (`CLAUDE.md` § *Build & Test*).
Known-failing on Windows and **not** regressions: four `SrcVariableResamplerTests`
(`libsamplerate.so.0`, `TEST-5`) and `NwsObservationIntegrationTests.RealNwsCall_*`
(`Category=Integration`, CI-excluded).

---

## 5. Task 5 — correct the punch-list entry

`docs/HANDOFF-GA-PUNCH-LIST.md:918-919` currently records, inside the resolved `TTS` entry:

> Confirmed working end to end from the Serilog file sink: engine `Google`, no
> `Google TTS API error`, ducking engaged at 20% `FadeSmooth` and released to 100%, source removed cleanly.

**The ducking clause is not evidence of working ducking**, and the reasoning must be recorded so it is
not re-asserted from the same log lines later. The lines that produced it are emitted **before the key
is used, or without consulting the result**:

- `DuckingService` computes and raises the level. It never touches `SoundFlowPlaybackService`; it says
  so itself at `DuckingService.cs:521-525` — *"The DuckingService does not directly modify the mixer
  volume. Instead, it emits DuckingLevelChanged events…"*.
- `AudioManager.OnDuckingLevelChanged` logs *"Ducking level: …"* at **Debug** (`:481-483`) — which on
  a stock box is written **nowhere**: `Radio.API`'s Serilog `Radio` override floors at `Information`
  (§0.7). Whatever was read on 2026-08-19, it was not this line.
- `AudioManager.OnDuckingStateChanged` logs *"Ducking ended: volume restored"* at **Information**
  (`:557-559`) — unconditionally, after a `ClearDuckingMultiplier` whose result it does not check, and
  even when `_activeSource` is null. **This is the line the entry was almost certainly read from, and
  it prints identically whether or not the multiplier landed.**

So the entry's *TTS* findings stand — engine `Google`, no API error, source removed cleanly — and its
headline conclusion (the voice fix worked) is untouched. Only the ducking clause is unsupported.
⚠ **Correct it; do not delete the entry, and do not restate it as "ducking was broken."** What is known
is that those log lines cannot distinguish the two cases. Whether ducking was *audibly* working on
2026-08-19 is not knowable from that evidence and is not being claimed either way here. The exact
replacement wording is in **§9**, to be applied as part of this PR's docs commit.

⚠ This correction also has a forward dependency on §2.3: once *"Ducking ended"* reports
`volumeRestored={Restored}`, that line **does** become usable evidence. §9's wording says so, so the
next person knows which side of this PR a given log line came from.

---

## 6. Test plan — UAT on the box

**This is the only place the fix is actually proved.** §4.1 explains why: the hit direction needs a
real playback device. It is also user-facing and audible, so it needs a real listen.

⚠ Deploy first — `CLAUDE.md` and project memory both require it:
`./deploy/Deploy-ToLinux.ps1` (defaults are `-TargetHost radio -Runtime linux-x64` since `OPS-1`).
Confirm both SHAs before testing anything:

```bash
curl -s http://radio:5000/api/health/version
curl -s http://radio:5002/api/health/version
```

### 6.1 The panel test — the predicted-failure baseline

**Run this BEFORE deploying the fix, on the current build.** A UAT that only observes the post-fix
state cannot tell a fix from a thing that was never broken, and the row was explicitly filed as
*confirm-or-close*.

1. Kiosk → Home → select **Radio**. Confirm audio.
2. Sweep the **Radio gain slider** across its full range.
   **Predicted result today: no audible change at any position.**
3. Switch to **Bluetooth**, play from the phone, sweep the **Bluetooth** gain slider.
   **Predicted result today: it works** — Bluetooth was fixed by `2bbd0eb5`. This is the control, and
   it is what makes step 2 a measurement rather than an anecdote: same UI, same API, same service,
   one key that matches and one that does not.
4. Repeat step 2 for **Vinyl** / **Generic USB** / **File player** if hardware allows. All four are
   predicted dead pre-fix.

**Post-deploy, all of steps 2–4 must move the volume audibly.**

### 6.2 The ducking test

1. Play **Radio**.
2. Fire a TTS event (Settings → Event Sources → a TTS announcement, or any notification that speaks).
3. **Pre-fix predicted:** radio volume does **not** drop while the announcement plays.
   **Post-fix required:** radio audibly ducks for the announcement and returns to full afterwards.
4. Repeat on **Bluetooth** as the control — it should duck both before and after.

### 6.3 The stop/start persistence check (Task 3's invariant)

1. Set the Radio gain to a distinctly non-unity value.
2. Switch away to another source and back.
3. **Post-fix required:** the gain is still applied, audibly, without re-touching the slider.
   Pre-fix this could not work for SDR/FilePlayer — the offset was stored under a key discarded at
   the next `PlayCoreAsync`.

### 6.4 Log evidence — read-only, bounded

⚠ **Two of the three patterns in the row's suggested command match nothing on a stock box**, and the
command below is corrected for that. `"Ducking level:"` is `LogDebug` (`AudioManager.cs:481`) and
`"Applied gain offset"` at `SoundFlowPlaybackService.cs:666` is also `LogDebug` — both below the
`Information` floor (§0.7). `AudioManager.cs:122` says *"Applied **live** gain offset"*, which the
substring `"Applied gain offset"` does not match either.

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -E "AUDIO ROUTING: Adding component|Applied live gain offset|Ducking started|Ducking ended|did NOT reach" $F | tail -40'
```

What to look for:

- `🔊 AUDIO ROUTING: Adding component … (SourceId=Radio-<32 hex>, …)` — **`SourceId` must be
  `Radio-<guid>`, not `sdr-radio-<guid>`.** This single line is the most direct confirmation that
  Task 1 landed, and pre-fix it reads `sdr-radio-…`. It is `Information`
  (`SoundFlowPlaybackService.cs:414`) so it is in the **file sink, not the journal**, since `LOG-11`.
- `Applied live gain offset …` on each slider move (`AudioManager.cs:122`, `Information`).
- `Ducking ended: activeEvents=…, volumeRestored=True` — post-fix only; the `volumeRestored` field
  does not exist before this PR (§2.3).
- **`did NOT reach`** — **must not appear at all post-fix.** If it does, the fix is incomplete and the
  message names the source and the `SourceId`.

⚠ Bound every journal query (`--since '-30min'`), never tail, and keep log reads away from the audio
listening windows: log volume on this box correlates with audible distortion (`CLAUDE.md`
§ *Deployment*).

### 6.5 Pre-fix negative control for the warning

Worth one deliberate run, because Task 2's whole value is that it would have caught this: on the
**pre-fix** build with the Task 2 changes applied, `did NOT reach` **should** appear. If the plan is
executed as one commit this cannot be observed directly — in that case rely on §4.4's unit tests,
which assert the same condition, and say so rather than implying the box demonstrated it.

---

## 7. Estimate — 1 day

| | |
|---|---|
| Task 1 (three one-line changes + comments) | ~0.5 h |
| Task 2 (`bool` through 4 methods + 4 `AudioManager` sites + reworded logs) | ~2 h |
| Task 3 (comment) | ~0.25 h |
| Task 4 (lint + two new test files, ~8 tests) | ~2.5 h |
| Task 5 + §9 (docs) | ~0.5 h |
| Deploy + UAT §6 (pre-fix baseline, post-fix, both control arms) | ~2 h |

**The code is the small part.** The estimate is dominated by tests and by a UAT that has to be run
**twice** — once before the fix to establish the predicted-failure baseline (§6.1), once after — and
that requires physical presence at the box for the audible checks.

**What would push it to 1.5–2 d:** if `CreateMockPrimarySource` cannot have `State` overridden
cleanly (§4.4) and the shared helper needs reworking; or if the USB sources cannot be exercised on the
box for lack of a turntable / USB input, in which case the Vinyl and GenericUSB arms of §6.1 go
untested and the PR must say so explicitly rather than implying four-source coverage.

**What would NOT push it out, and must not be allowed to:** discovering that the seam in §4.5 would
make testing easier. That is a separate row.

---

## 8. Queue row wording

⚠ **Not applied — `docs/BUILDER_QUEUE.md` is out of scope for this plan.** Apply as part of the
implementation PR's docs commit. This replaces the `AUD-2` row's **Item**, **Plan** and **Status**
cells; the **Spec / handoff**, **Depends on** and **Branch** cells are unchanged.

**Status:** `📋` → keep `📋` (queued, claimable now — no dependency).

**Plan cell** (replaces `_plan TBD (**investigate first**; scope depends entirely on the answer)_`):

> [`design/plans/AUD-2-one-key-per-source.md`](../design/plans/AUD-2-one-key-per-source.md)

**Item cell** — replace the row's opening framing and its closing anchor paragraph. Keep the body's
mechanism analysis; it is correct. The three edits:

1. **Replace the title and the opening warning.** From *"Confirm-or-close: is SDR gain/ducking silently
   dead…? ⚠ INVESTIGATE FIRST. This is an unverified inference from a code read, not an observation,
   and the row may legitimately close with no code change."* to:

   > **One key per source: four primary sources register under a minted key and are addressed by
   > `IAudioSource.Id`, so gain and ducking miss silently.** **✅ CONFIRMED 2026-09-05 by a
   > `team-debugger` pass (~90% confidence) — the investigation is DONE and must not be re-run.** The
   > row was filed confirm-or-close; it closed as **confirm**. Plan:
   > `design/plans/AUD-2-one-key-per-source.md`.

2. **Replace the scope sentence.** The row analyses only `SDRRadioAudioSource`. Insert after the
   mechanism paragraph:

   > **⚠ SCOPE IS FOUR SOURCE TYPES ACROSS THREE FILES, NOT SDR-ONLY — the title and the branch name
   > both understate it.** `SDRRadioAudioSource.cs:915`, `FilePlayerAudioSource.cs:727`, and
   > `USBAudioSourceBase.cs:317` — the last inherited by `RadioAudioSource`, `VinylAudioSource` and
   > `GenericUSBAudioSource`. `$"usb-capture-{Id:N}"` is a miss, not a near-miss: `Id` is a `string`,
   > so `:N` is ignored, and the result contains the Id without equalling it. `BluetoothAudioSource`
   > and `TestToneAudioSource` already use `Id` and need no change. `AudioFileEventSource.cs:146` is
   > deliberately excluded — it is an **event** source, `AudioManager` addresses only primary sources,
   > and `EventPlaybackService.cs:32-41` documents that id space on purpose.

3. **Replace the closing anchor paragraph in full.** The claim *"Every anchor in this row re-verified
   2026-08-11 against `main` @ `8b1ce0a` and all are byte-exact and unchanged"* is **no longer true**:

   > _**⚠ The 2026-08-11 anchor claim is STALE — five anchors had drifted by `656f58e6`.** Corrected:
   > `SDRRadioAudioSource.cs:908`→**`:915`**, `:956-960`→**`:963-967`**;
   > `SoundFlowPlaybackService.cs:620`→**`:656`**, `:643`→**`:679`**; `AudioManager.cs:508`→**`:554`**.
   > Unchanged and re-verified at `656f58e6`: `SDRRadioAudioSource.cs:204`,
   > `SoundFlowPlaybackService.cs:25`/`:423`, `AudioManager.cs:121`/`:240`/`:247`/`:292`/`:479`,
   > `AudioSourceBase.cs:28`. **And the row's suggested corroboration does not work:**
   > `GetDiagnostics()` (now `SoundFlowPlaybackService.cs:766-772`, not `:734`/`:752`) returns
   > `_activePlayers.Keys` **only** — every source in this row except FilePlayer registers as a
   > *component*, so it comes back empty and proves nothing. Use the plan's UAT §6.4 instead. **The
   > row also under-scopes the fix: a second half, not in this row, corrects the log lines that assert
   > success after a lookup that matched nothing (`SoundFlowPlaybackService.cs:666`,
   > `AudioManager.cs:557-559`) — see the plan §2.**_

---

## 9. Punch list correction

⚠ **Not applied — `docs/HANDOFF-GA-PUNCH-LIST.md` is out of scope for this plan.** Apply as part of the
implementation PR's docs commit. See §5 for the reasoning.

**At `docs/HANDOFF-GA-PUNCH-LIST.md:918-919`**, replace:

```
> section-level write.** Confirmed working end to end from the Serilog file sink: engine `Google`, no
> `Google TTS API error`, ducking engaged at 20% `FadeSmooth` and released to 100%, source removed cleanly.
```

with:

```
> section-level write.** Confirmed working end to end from the Serilog file sink: engine `Google`, no
> `Google TTS API error`, source removed cleanly.
>
> ⚠ **CORRECTED 2026-09-06 (`AUD-2`): the ducking clause was withdrawn. It read "ducking engaged at
> 20% `FadeSmooth` and released to 100%", and those log lines could not support it.** They are emitted
> before the ducking multiplier is applied, or without checking whether it was: `DuckingService` only
> raises events and never touches the mixer (`DuckingService.cs:521-525`); `AudioManager`'s
> "Ducking level:" line is `Debug` (`AudioManager.cs:481`) and `Radio.API`'s Serilog `Radio` override
> floors at `Information`, so it was written to neither sink; and "Ducking ended: volume restored"
> (`AudioManager.cs:557-559`) was `Information` but **unconditional** — printed identically whether the
> multiplier landed, missed, or had no active source at all. So the entry recorded that the lines
> appeared, which they would have either way. **This is NOT a claim that ducking was broken on
> 2026-08-19 — it is a statement that the evidence cited does not decide it**, and the TTS findings
> either side of it are unaffected. `AUD-2` separately found that four primary sources registered
> under a key `AudioManager` never addressed, which is a real mechanism by which ducking could have
> been silently dead here; `Bluetooth` was not affected (`2bbd0eb5`).
>
> **Since `AUD-2` these lines can be trusted again:** "Ducking ended" now reports
> `volumeRestored={Restored}` from `ClearDuckingMultiplier`'s actual result, and a miss on a playing
> source emits a `did NOT reach` **warning**. A log excerpt showing `volumeRestored=True` IS evidence;
> one showing the old unconditional wording is not, and dates it to before this fix.
```

---

## 10. PR checklist

- [ ] Branch `fix/sdr-playback-id-ducking-gain` off `main`.
- [ ] Tasks 1–4 in source; Task 5 + §8 + §9 in a separate docs commit.
- [ ] PR title and body say **four source types across three files**, not SDR (§0.5).
- [ ] PR body states that `AudioFileEventSource` is deliberately untouched, with the reason (§0.5).
- [ ] PR body states plainly that **unit tests cover the miss direction and the lint covers the key;
      the audible fix is proved by UAT only** (§4.1). Do not report a green suite as proof that gain
      and ducking work.
- [ ] Full suite green except the two known Windows failures (§4.6).
- [ ] UAT §6 run **twice** — pre-fix baseline and post-fix — with the Bluetooth control arm both times.
- [ ] `did NOT reach` absent from the post-fix log (§6.4).
- [ ] Pre-merge review specifically re-reads every comment added by Tasks 1–3 against the code, per
      `CLAUDE.md` § *Pre-Merge Review*. **This PR's entire second half is about a comment that
      overclaimed**; shipping a new one would be an unusually poor outcome. The claims to falsify:
      §2.1's *"cannot distinguish"*, §2.3's *"bounded at two lines per duck cycle"*, and §3's
      *"stable for the lifetime of the source instance"*.
