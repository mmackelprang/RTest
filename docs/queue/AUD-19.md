# `AUD-19` — the History panel will not follow the per-field metadata rule, and that will read as a failed fix

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-09. ⚠ **This row was referenced before it existed.** `docs/queue/AUD-1.md:113`
has said *"filed as `AUD-19`, documented in…"* since 2026-09-08 — **it was neither**, and `AUD-1`'s
Builder is told to cite it in its PR body before merge. Caught by `UI-12`'s Builder while enumerating
adjacent rows; the citation was a coordinator error, corrected here and in `AUD-1`.

## The divergence

The owner's decision on `AUD-1` (2026-09-08):

> *"When metadata is available from the audio source, use the source metadata (song name, album name,
> album art). When one or more of these is missing, use fingerprinting to augment the missing data."*

`AUD-1` delivers that on the **now-playing** path. **It does not reach History.**

`PlayHistoryTracker` re-points its rows at the fingerprint record **regardless of which branch the
source took** — it sets `MetadataSource = Fingerprinting` on *any* landed identification
(`:563-573`, `:631-641`) without consulting whether the source's own metadata won per field.

**So after `AUD-1` ships, the now-playing display obeys the rule and History does not.** Same track,
two surfaces, two answers — and the History one will show SongRec's guess where the source supplied a
name.

## ⚠ Why this needs to be visible before `AUD-1` merges, not after

**Left undocumented, it reads as a failed fix.** The owner will change a phone's metadata behaviour,
look at History, and see the old wrong titles still there. `AUD-1`'s PR body must name this row
explicitly so the divergence is a known, scoped follow-up rather than a bug report.

⭐ That is the whole reason this is a separate row rather than folded in: **different file, different
lifecycle, and folding it in roughly doubles the blast radius of a change already on the live audio
path.**

## Scope questions for the plan

1. **Is History even wrong?** ⚠ **Ask before assuming.** There is a real argument that history is a
   record of *what fingerprinting identified*, and that recording SongRec's answer is correct there
   even when the display prefers the source's. **If so this row closes as "correct, documented" and
   that is a legitimate outcome.** Do not treat the divergence as self-evidently a defect.
2. **If it is wrong, where does the rule live?** `AUD-1` will have built a shared per-field precedence
   helper. History should call the same one rather than re-implement it — two implementations of one
   rule is how the rule stops being one rule.
3. **What about rows already written?** The DB holds history under the old behaviour. Say plainly
   whether existing rows are migrated, left, or re-derived — and if left, that History will show two
   eras with different semantics.
4. ⚠ **`MetadataSource` records provenance and is load-bearing elsewhere.** `AUD-17` established that
   `Source` on `TrackMetadata` records the **title's** provenance, not the art's, and that misreading
   it produced a retracted row. **Do not change what the column means without checking who reads it.**

## Verification

Unit-testable: land an identification where the source supplied a title, and assert the history row
keeps the **source's** title rather than the fingerprint's. **Must fail first** — today it does not,
so confirm RED against `main` rather than assuming.

⚠ **Beware a vacuous UAT here.** `AUD-12`'s stall gates fingerprinting off entirely and `AUD-18`'s tap
can return zero bytes for hours — with either live, *no* identification lands and a broken build looks
correct. Check the tap is alive first, as `AUD-1`'s §4.4 does.

## Depends on

**`AUD-1`** — this row has no meaning until the per-field rule exists to diverge from. ⛔ **Do not claim
it first.**
