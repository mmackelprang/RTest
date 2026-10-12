# `TEST-12` — `VoicemailRowTests.ZeroDuration_RendersEmDash` fails during the minute 10:00 (local) on the dev host's clock

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

🟢 **P3. Filed 2026-10-12 by the `AUD-101` Builder**, from a full-suite run that failed on it.

## Measured

- The full CI-filter run on `feat/aud-101-am-dsp-chain` (`cc5190b`) at about 22:00 EDT failed one `Radio.Web.Tests`
  test: `ZeroDuration_RendersEmDash`, at `Assert.DoesNotContain("0:00", cut.Markup)`
  (`tests/Radio.Web.Tests/Components/VoicemailRowTests.cs:71`). The markup it found contained
  `list-item-meta">10:00 PM</span>`.
- Re-run alone at 22:03 EDT: 4/4 `VoicemailRowTests` pass. The branch changes nothing under `src/Radio.Web` or
  `tests/Radio.Web.Tests`.

## Cause (read from the test)

`Vm(...)` builds its item with `DateTime.UtcNow` (`:20`), and the row renders that time as a local clock time.
When the rendered time is `10:00` (AM or PM), it contains the substring `0:00`, which the test forbids in order to
prove that a zero duration renders as an em dash. So the test fails whenever it runs during the minute 10:00 local
time. This is the same class as `CLAUDE.md` § Test Timing: an assertion that depends on the wall clock.

## Fix (for the Builder who claims it)

Use a fixed timestamp in `Vm(...)` (for example `new DateTime(2026, 1, 1, 9, 13, 0, DateTimeKind.Utc)`), and/or make
the assertion target the duration element rather than the whole markup. Test-only.

## Related

None. Pre-existing; unrelated to `AUD-101`.

## Shipped 2026-10-12 — [#805](https://github.com/mmackelprang/RTest/pull/805)

- `VoicemailRow` takes `[Parameter] TimeProvider Clock`, defaulting to `TimeProvider.System`. It uses the clock for
  "now", for the 30-minute recency check, and for the local zone.
- The tests use a `FakeTimeProvider` and a fixed item time.
- They assert on `.vm-row-duration` instead of the whole markup.
- They pin the 10:00 case, the zone (UTC+05:30) and the pending window.
- Mutation checks: 4 of 4 killed.
