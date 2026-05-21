# Cast stutter comparison — RTest vs custom-CAF-receiver reference apps

**Status**: research framework — *empty cells, structure only*. Each cell is filled in during the research execution pass.

**Author**: Mark + Claude (brainstorming pass 2026-05-21)

**Motivation**: RTest's Google Cast output produces periodic audible stutters on the kiosk. The user reports never having heard the same behavior from reference apps (TuneIn, Plex) using *custom CAF receiver pages* for continuous-audio cast. Anecdotal — but a strong-enough signal to investigate the architectural differences. Goal: understand what those reference apps do differently, so the cost of any future RTest change is informed rather than guessed.

**Explicit non-goal**: no RTest implementation work falls out of this document. The "things RTest could try" section is *research output*, not a plan or commitment. A separate plan would consume that list later if and when the team chose to act on any item.

---

## 1. Scope

### In scope
- Custom-CAF-receiver casting of *continuous* audio (long-form playback — internet radio, full-album streams, queued playlists).
- Stutter-avoidance strategies — what each system does so the receiver's playback buffer never runs empty.
- Both RTest cast modes:
  - **HttpMp3** — `ApplicationId = "CC1AD845"` (Google's Default Media Receiver). Radio.API serves `/stream/audio/mp3` (LAME-encoded, `StreamType.Live`). Receiver pulls.
  - **DirectChannel** — `ApplicationId = "567E3DBA"` (custom). Base64-encoded WAV chunks (~100 ms each) pushed over the Cast custom message bus to `receiver.html`. Receiver plays via Web Audio API.

### Out of scope
- Spotify Connect (different architecture — Chromecast runs a native Spotify app and pulls from Spotify's CDN; no sender-side audio pipeline exists, so the comparison axis isn't meaningful).
- Video casting (different decoder paths, different framing constraints).
- Cast SDK *API surface* review (we care about what gets transmitted and how it's consumed, not "which SDK method to call").
- Sender-side audio production upstream of `IAudioOutput` (SoundFlow, PipeWire, sources) — already studied in prior debug sessions, not relitigated here unless evidence implicates it.

---

## 2. Reference systems

Four columns in every matrix in this document:

| Column key | System | Why included |
|---|---|---|
| **RTest-HM** | RTest HttpMp3 mode | RTest's default — closer to "how most Cast audio works" |
| **RTest-DC** | RTest DirectChannel mode | RTest's experimental mode — pure custom-receiver, push protocol |
| **TuneIn** | TuneIn Radio Cast app | Closest real-world analog: continuous internet radio with custom CAF receiver |
| **Plex** | Plex (audio cast via Plex receiver) | Public engineering blog detail, custom CAF receiver, mixed continuous + queued |

---

## 3. Data collection methodology

Each filled cell carries an evidence tag so the reader knows what they're looking at:

| Tag | Means | How obtained |
|---|---|---|
| `[source-walked]` | We read the code | RTest: open in tree. TuneIn / Plex: `chrome://inspect` while cast is active → attach DevTools to the live receiver page → inspect the minified-but-readable receiver JS, watch `MediaSource` / `AudioContext` state, breakpoint on chunk handlers. |
| `[doc-cited]` | We have a public reference | Google CAF receiver docs (StreamManager, MediaInformation, segment loaders), Plex engineering blog posts, public Cast SDK reference, Web Audio API + MSE specs. Cite the URL in the cell. |
| `[inferred-from-behavior]` | We're reasoning from observable signals | Wall-clock chunk-arrival cadence, MSE buffered-range readout via DevTools, audio output spectral analysis under known-good vs stutter conditions. Lower confidence; explicit. |

Findings without an evidence tag should not appear in the filled doc.

### Tools needed for the research execution pass
- A Chromecast on the LAN (we have at least one — `_defaultCastDevice` in RTest's config).
- TuneIn + Plex installed on a controller device (phone or tablet) able to initiate a cast session.
- Chrome with developer tools on a workstation that can reach the Chromecast's debug port (Chromecast exposes a port on the local network when "Send a Bug Report" diagnostic mode is enabled, or — easier — use the Chrome `chrome://inspect` Devices tab with the Chromecast's IP).
- Optional: Wireshark with mDNS + Cast protocol decoders, for the inferred-from-behavior tier.

### Counter-discipline
Receivers can change between releases. Date-stamp every receiver-walked finding (`[source-walked, TuneIn receiver build 2026-05-21]`) so a future reader knows whether the evidence is still current.

---

## 4. Failure-mode catalog (the diagnostic spine)

Seven modes, each independently capable of producing the *click-pause-resume* the user hears. The matrix below is filled per system × mode during the research pass.

### Modes

| # | Mode | Mechanism | Audible signature |
|---|------|-----------|---|
| **FM1** | Receiver jitter-buffer underrun | Audio leaves the receiver's playback queue faster than new chunks arrive; buffer hits zero | Brief silences (50–300 ms). Cadenced. Most common Cast stutter. |
| **FM2** | Sender pipeline jitter | Sender's encode → transmit pipeline blocks momentarily (GC, lock contention, PipeWire quantum stall, slow `HttpResponse.WriteAsync`) | Same audible result as FM1 but root cause is upstream; correlates with sender-side load |
| **FM3** | Clock drift / resampling | Sender's nominal sample rate doesn't match receiver's; buffer slowly drains or overflows over minutes/hours | Drift — stutter rate increases with session length. Sometimes micro pitch artifacts |
| **FM4** | Receiver scheduling slips | Receiver JS schedules audio via `AudioBufferSourceNode.start(when)` or MSE append; main thread is busy at that moment (GC, large message decode, DOM, image load) | Sharp clicks at scheduled-buffer boundaries; often correlates with track-change / metadata events |
| **FM5** | Network / transport jitter | TCP head-of-line on Cast bus, WiFi retransmits, mDNS contention | Bursty stutters, sometimes 1+ second; correlates with other LAN activity |
| **FM6** | Codec / format boundary glitches | A chunk straddles an MP3 frame, AAC ADTS sync, or PCM block boundary in a way the decoder can't seamlessly join | Periodic pops at the chunk interval (every 100 ms in DC mode; every ~46 ms per MP3 frame in HM mode) |
| **FM7** | Receiver app lifecycle event | Chromecast OS evicts/restarts the receiver under memory pressure or backgrounding; custom-HTML receivers especially vulnerable | Long gap (1+ s) followed by playback resuming. Rare but real. |

### Failure-mode matrix (to be filled)

For each cell:

- **Exposure** — Y/N + one-line "this system is vulnerable because…"
- **Mitigation** — concrete: chunk size, buffer depth, codec, scheduling approach
- **Evidence** — `[source-walked]`, `[doc-cited]`, or `[inferred-from-behavior]`

| Mode | RTest-HM | RTest-DC | TuneIn | Plex |
|---|---|---|---|---|
| FM1 — Receiver underrun | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| FM2 — Sender pipeline jitter | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| FM3 — Clock drift / resampling | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| FM4 — Receiver scheduling slips | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| FM5 — Network / transport jitter | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| FM6 — Codec / boundary glitches | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| FM7 — Receiver lifecycle | _to fill_ | _to fill_ | _to fill_ | _to fill_ |

---

## 5. Pipeline table (the apples-to-apples reference)

Ten rows × the same four columns. Each cell with an evidence tag.

| Row | What it captures | RTest-HM | RTest-DC | TuneIn | Plex |
|---|---|---|---|---|---|
| Source format | Samples the sender starts with (PCM s16le 48 kHz stereo? 24-bit float?) | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Codec / container | MP3 / AAC / Opus / WAV / raw PCM; bitrate; CBR vs VBR | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Chunk size & cadence | "100 ms WAV every 100 ms" vs "2 s OGG every 2 s" vs "no chunking — open HTTP stream" | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Transport | HTTP byte stream / Cast message bus / MSE-fed Range requests / HLS playlist polling | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Receiver decoder API | `<audio>` element / Media Source Extensions (MSE) / Web Audio API (`AudioBufferSourceNode`) / native CAF `PlayerManager` | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Buffer target depth | Seconds the receiver tries to keep queued before playing | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Adaptive behavior | Does buffer grow under jitter? Bitrate switch on bandwidth drop? | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Clock sync model | Sender-clock master / receiver-clock master / NTP-aligned timestamps / no sync | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Backpressure | Receiver tells sender to slow down? Or sender just blasts? | _to fill_ | _to fill_ | _to fill_ | _to fill_ |
| Metadata channel | Separate from audio path or interleaved? Frequency? | _to fill_ | _to fill_ | _to fill_ | _to fill_ |

---

## 6. Findings synthesis (to be written after the matrices are filled)

A short prose section (~500 words) that reads across rows and columns of the two matrices and pulls out 3–5 *patterns* — not prescriptions. Examples of the *kind* of finding expected (not actual content, just shape):

- "Every reference system uses MSE rather than Web Audio API for continuous-audio cast; only RTest DirectChannel uses Web Audio with manual scheduling."
- "Buffer depths cluster around 4–8 seconds across reference systems; RTest's 100 ms chunk cadence with no jitter buffer is an outlier by an order of magnitude."
- "Reference systems all separate the metadata channel from the audio channel (so a 200 KB album-art payload can't head-of-line-block 100 ms of audio); RTest DirectChannel interleaves them on the same custom message bus."

Patterns. Not prescriptions. The "what to do about it" goes in §7.

---

## 7. Speculative — things RTest could try (research output, not a roadmap)

Five to eight entries, each in this shape:

> **Idea — \<short name\>**
> **Addresses**: FM1, FM5 (the failure modes this would mitigate)
> **What changes in RTest**: brief, concrete; sender? receiver? both? what code area?
> **Scope**: rough sense of how much code (e.g., "~50 LOC in receiver.html, no protocol change")
> **Risk / trade-off**: what we'd give up to get the mitigation (latency, complexity, etc.)
> **Confidence**: how strongly the reference systems' behavior suggests this is the right move (high / medium / low)

Each idea explicitly **is not a commitment**. A future plan would consume any one of these and turn it into real work via the normal Builder/queue flow.

---

## 8. Out-of-band notes

- TuneIn's receiver build number / Plex's receiver build number will be captured the moment we attach to each. Receiver code changes; this doc's findings are dated.
- If the inspection effort reveals that TuneIn or Plex has switched to a *native* CAF target (no HTML receiver), the column is dropped and replaced with the next-best candidate (Pandora, SoundCloud, NPR One).
- If RTest's stutter is reproducible *only on HttpMp3 mode* or *only on DirectChannel mode*, the failure-mode matrix for the un-affected mode is filled as "not applicable to symptom" rather than left blank — useful negative evidence.

---

## Execution checklist (for the research pass that fills this doc)

- [ ] Verify `chrome://inspect` reach to the Chromecast on the LAN
- [ ] Cast from TuneIn, attach DevTools, capture receiver build number + URL
- [ ] Walk TuneIn receiver: decoder, buffer depth, chunk handling — fill TuneIn column of both matrices
- [ ] Cast from Plex, repeat
- [ ] Walk RTest-HM (`/stream/audio/mp3` server + Default Media Receiver behavior via DevTools)
- [ ] Walk RTest-DC (`receiver.html` source + `DirectCastStreamingService.cs` sender)
- [ ] Fill failure-mode matrix
- [ ] Fill pipeline table
- [ ] Write §6 synthesis
- [ ] Draft §7 speculative ideas
- [ ] Spec self-review (placeholders / contradictions / scope) — fix inline
- [ ] Surface to user for review
