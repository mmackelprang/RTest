# `AUD-17` — AVRCP album art stopped working around 2026-07-19, and a false premise hid it

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-08 by the `AUD-1` Planner, which found it while checking — rather than
assuming — a claim that row had been asserting for a month.

## What was found

The fingerprint DB on the appliance holds **66 successfully-cached AVRCP-sourced album arts between
2026-03-05 and 2026-07-19**, with real `/api/albumart/<hash>` paths — not rejected `file://` URLs,
not placeholders. **Since 2026-08-01 the count is 0 of 31.** Shazam over the same later window is
9,517 of 9,577.

So AVRCP art worked on this box for four and a half months and then stopped.

## ⚠ Why nobody noticed — this is the part worth reading

`AUD-1`'s row asserts, and `docs/ROADMAP.md:133` repeats, that **BlueZ 5.72 ships no BIP/cover-art
implementation, so AVRCP can never supply album art on this box.** Under that premise a zero is not
a symptom — it is the expected reading. The measurement that should have raised an alarm was
interpreted as confirmation.

**The premise is false**, and the 66 cached arts are the disproof. `AUD-1`'s *count* was honest for
the 7-day window it sampled; the *mechanism* it inferred from that count was not.

⚠ **Correct both documents as part of this row** — `docs/queue/AUD-1.md:26` and
`docs/ROADMAP.md:133`. A false claim that makes a regression look like a design limit is worse than
no claim, and this one has been shaping decisions: it is the stated reason `UseShazamForAllSources`
is `true` in production, which is in turn the cause of the metadata-overwrite defect `AUD-1` exists
to fix.

## Deliberately not assumed

- **Not established: what changed.** The date range brackets it but nothing here names a cause. A
  BlueZ or WirePlumber package upgrade, a deploy, the dual-adapter split, or a phone-side change are
  all candidates. **Find out before proposing a fix.**
- **Not established: that it is ours.** It may be an upstream regression, in which case the honest
  close is a documented finding plus whatever detection prevents the next silent stop.
- **Not the same as `AUD-1`.** That row splits one flag into two decisions and is unaffected by
  this: its design holds either way. This row is about art that used to arrive and no longer does.
- **Not the same as `AUD-15` or `AUD-12`.** Different subsystems; do not conflate.

## Scope questions for the plan

1. **When exactly, and against what?** Correlate the last successful AVRCP art with `git log`,
   the deploy history, and `/var/log/apt/history.log` on the box. The 2026-07-19 boundary is precise
   enough to be checkable.
2. **Is the code path still reached at all**, or is it failing further down? Distinguish "BlueZ
   never offers art" from "we ask and discard the answer" — they look identical in the DB.
3. **What would have caught this?** A silent drop from 66 to 0 over six weeks, on a metric already
   being written to a database, is a monitoring gap as much as a defect.

## Verification

The DB query that found it is the regression test: AVRCP-sourced art count over a window must be
non-zero while a phone is connected and playing tagged media. ⚠ Note the trap — **`AUD-12`'s `Ready`
stall gates fingerprinting off entirely**, and `AUD-1`'s overwrite can replace AVRCP art with
Shazam art, so both can make this row's measurement read zero for reasons that are not this bug.
Establish which sources a given row came from, not just that a row exists.

⚠ Live audio path and needs the owner's phone. **Not auto-mergeable.**
