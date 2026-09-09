#!/usr/bin/env bash
# test-classify-voice.sh — the VOICE row's predicate, driven with canned status payloads.
#
#   ./deploy/debian-x64/kiosk/tests/test-classify-voice.sh
#
# Exits 0 when every case passes, 1 otherwise. No network, no systemd, no display: it sources
# ../bin/radio-console-open for its functions (the seam at the bottom of that file stops the
# sourced copy before anything acts), stubs the one probe that touches the box, and hands
# classify_voice() a literal body in GV_BODY.
#
# WHY THIS FILE EXISTS, AND WHY IT LIVES HERE
# -------------------------------------------
# KIOSK-3: the launcher derived VOICE from `psidtsAgeSeconds`, which RotaryPhone's PR #79
# DELETES. An absent field made classify_voice()'s guard fire every time, so VOICE would have
# read "needs sign-in" permanently, on every launch, whatever Google Voice was actually doing —
# a wrong indicator, which is how an owner learns to ignore an indicator.
#
# Nothing in this repo could have caught that. `radio-console-open` is a shell script with no
# test project; the whole reason the defect got as far as it did is that a `grep` for consumers
# of the field was scoped to `src/`. So the harness lives NEXT TO THE SCRIPT IT TESTS rather than
# under `scripts/` (which holds `dotnet test` runners and needs hardware) or in a test project
# (there is no shell one). setup-kiosk.sh installs named files out of `bin/`, `icons/` and
# `gtk-touch/` — it never copies a `tests/` sibling, so this cannot reach the box.
#
# ⚠ ON CLOCKS — read CLAUDE.md § Test Timing before adding a case.
# The staleness cases use wall-clock timestamps, and the direction they fail in is stated rather
# than implied. `fresh_ts` is 30 s old against a 120 s gate (90 s of margin) and the stale case
# is a VERBATIM capture from the box at 2026-09-09T14:30:50Z, which only gets older.
# ⚠ NOT "old by construction" — it is old because time has passed since it was captured, and the
# distinction is real: that case would have FAILED had this suite been run before ~14:32:50Z on
# 2026-09-09 itself. From any later run it is safe, and it is now well past that.
# A starved runner can only make a "fresh" reading older, i.e. move it TOWARD the gate — it would
# have to stall for 90 seconds inside one `classify_voice` call to flip an assertion. There is no
# case here whose PASS depends on something happening quickly.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAUNCHER="$SCRIPT_DIR/../bin/radio-console-open"

[ -r "$LAUNCHER" ] || { echo "cannot read $LAUNCHER" >&2; exit 2; }

# ⛔ GNU date is a PRECONDITION, not an optional nicety, and without this check the suite goes
# quietly vacuous instead of failing honestly. On a BSD/macOS `date`, `date -d '30 seconds ago'`
# fails, `fresh_ts` returns empty, every fixture gets `"lastApiSuccessAt":""`, the staleness term
# degrades to no-signal — and EIGHT of the nine `expect online` cases then pass for entirely the
# wrong reason. The run would still go red (on §4's stale case alone), but it would name the
# wrong thing and bury it under eight meaningless PASS lines.
date -d '30 seconds ago' >/dev/null 2>&1 || {
  echo "this harness needs GNU date (coreutils) for -d; found something else" >&2; exit 2; }

# source-path=SCRIPTDIR is what lets shellcheck resolve the relative path from THIS file's
# directory rather than from the caller's cwd. Without it the source is unfollowed, and the
# knock-on is a false SC2034 on GV_BODY — which is read by the sourced classify_voice().
# shellcheck source-path=SCRIPTDIR
# shellcheck source=../bin/radio-console-open
. "$LAUNCHER"

# The ONLY stub. classify_voice's first act is the bridge-Chrome process check, which is a real
# `pgrep` against a profile path that does not exist on a dev machine. Everything after it is the
# logic under test and is NOT stubbed. Overridden AFTER the source, or the source would win.
probe_voice_process() { return "${STUB_VOICE_PROCESS_RC:-0}"; }

# ---------------------------------------------------------------------------------------------
# Payload fixtures
# ---------------------------------------------------------------------------------------------

# VERBATIM from `curl localhost:5004/api/gvbridge/status` on `radio`, 2026-09-09T14:30Z, on the
# build that is live TODAY (pre-#78, pre-#79). Not retyped and not tidied — the field order, the
# nulls and the 7-digit fractional seconds are all as served.
#
# ⚠ The row and its dossier both quoted this timestamp as `"2026-09-09T14:12:40Z"`. The real
# field carries a 7-digit fraction, and any date parsing has to survive it. Verified on the box:
# `date -d "2026-09-09T14:30:50.7537656Z" +%s` → 1788964250.
LIVE_CAPTURE='{"available":true,"activeMode":"GVApi","sipRegistered":true,"wsConnected":true,"lastConnectedAt":"2026-09-09T14:21:33.8016403Z","cookiesValid":true,"psidtsAgeSeconds":219,"degraded":false,"lastHealthyAt":"2026-09-09T14:01:17.8461169Z","throttledUntil":null,"throttleReason":null,"authBlackout":false,"lastApiSuccessAt":"2026-09-09T14:30:50.7537656Z","lastApiAuthFailureAt":"2026-09-09T14:20:44.0208539Z"}'

fresh_ts() { date -u -d '30 seconds ago' +%Y-%m-%dT%H:%M:%S.0000000Z; }

# The live capture with its success timestamp refreshed — i.e. what the box serves RIGHT NOW,
# rather than what it served at 14:30Z.
live_fresh() {
  printf '%s' "$LIVE_CAPTURE" \
    | sed -E "s/\"lastApiSuccessAt\":\"[^\"]*\"/\"lastApiSuccessAt\":\"$(fresh_ts)\"/"
}

# ⭐ THE CASE THIS ROW EXISTS FOR: the same healthy box AFTER RotaryPhone's #79, i.e. with
# `psidtsAgeSeconds` ABSENT rather than null. Built by deleting the field from the live capture,
# so it cannot drift from the real shape.
live_fresh_post79() {
  live_fresh | sed -E 's/"psidtsAgeSeconds":[0-9]+,//'
}

# ---------------------------------------------------------------------------------------------
# Assertions
# ---------------------------------------------------------------------------------------------

PASS=0
FAIL=0

run_case() {   # $1 = body -> echoes the verdict
  GV_BODY="$1"
  classify_voice
}

expect() {   # $1 = expected verdict, $2 = case name, $3 = body
  local got; got="$(run_case "$3")"
  if [ "$got" = "$1" ]; then
    printf '  PASS  %-56s -> %s\n' "$2" "$got"; PASS=$(( PASS + 1 ))
  else
    printf '  FAIL  %-56s -> %s (expected %s)\n' "$2" "$got" "$1"; FAIL=$(( FAIL + 1 ))
  fi
}

# Pins the PROPERTY rather than today's answer, for the two safety rules the row states in words.
# A future change to the pill vocabulary should not be able to satisfy these by accident.
expect_not() {   # $1 = forbidden verdict, $2 = case name, $3 = body
  local got; got="$(run_case "$3")"
  if [ "$got" != "$1" ]; then
    printf '  PASS  %-56s -> %s (not %s)\n' "$2" "$got" "$1"; PASS=$(( PASS + 1 ))
  else
    printf '  FAIL  %-56s -> %s (must NOT be %s)\n' "$2" "$got" "$1"; FAIL=$(( FAIL + 1 ))
  fi
}

echo "classify_voice() — $LAUNCHER"
echo ""

# ── 1. The post-#79 shape. THE ROW. ──────────────────────────────────────────────────────────
echo "post-#79 payload (psidtsAgeSeconds REMOVED, honest fields healthy)"
expect_not needsignin "  must not report a healthy bridge as needs-sign-in" "$(live_fresh_post79)"
expect     online     "  reads online"                                      "$(live_fresh_post79)"
echo ""

# ── 2. Today's shape must not regress while #79 is unmerged. ─────────────────────────────────
echo "pre-#79 payload (psidtsAgeSeconds present, honest fields healthy)"
expect online "  reads online" "$(live_fresh)"
echo ""

# ── 3. Genuinely unhealthy — one case per PRESENT-AND-BAD term. ──────────────────────────────
echo "present-and-bad terms"
expect needsignin "  available:false" \
  "$(live_fresh | sed -E 's/"available":true/"available":false/')"
expect needsignin "  cookiesValid:false" \
  "$(live_fresh | sed -E 's/"cookiesValid":true/"cookiesValid":false/')"
expect needsignin "  degraded:true" \
  "$(live_fresh | sed -E 's/"degraded":false/"degraded":true/')"
expect needsignin "  authBlackout:true" \
  "$(live_fresh | sed -E 's/"authBlackout":false/"authBlackout":true/')"
echo ""

# ── 4. Staleness. The capture is deterministic and hours old. ────────────────────────────────
echo "lastApiSuccessAt staleness (gate = ${GV_STALE_AFTER:-?}s)"
expect needsignin "  the 2026-09-09T14:30Z capture, replayed (stale by elapsed time)" \
  "$LIVE_CAPTURE"
expect online     "  59s old — inside the gate, one measured cadence tick" \
  "$(live_fresh | sed -E "s/\"lastApiSuccessAt\":\"[^\"]*\"/\"lastApiSuccessAt\":\"$(date -u -d '59 seconds ago' +%Y-%m-%dT%H:%M:%S.0000000Z)\"/")"
echo ""

# ── 5. ABSENT CONTRIBUTES NOTHING — for these four fields (ADR-032). ─────────────────────────
# The half of the asymmetry that makes this predicate survive a field being deleted. If any of
# these ever reads needsignin, the predicate has acquired a term that can never clear, and the
# VOICE row is pinned again — which is this row's entire defect, wearing a different field name.
#
# ⛔ Before adding a case here for a NEW field, check that the field actually obeys this rule.
# It is a property of cookiesValid / degraded / authBlackout / lastApiSuccessAt, NOT of the
# payload. `available` is the anchor and must be present (§7 below). `psidtsMintedAtUtc`, which
# RotaryPhone add in the same release that removes psidtsAgeSeconds, goes the other way again:
# its `null` means UNKNOWN and is explicitly NOT healthy. This file does not read it, and a case
# asserting `online` for its absence would be asserting the opposite of their contract.
echo "absent / null optional terms contribute nothing"
expect online "  lastApiSuccessAt absent" \
  "$(live_fresh | sed -E 's/,"lastApiSuccessAt":"[^"]*"//')"
expect online "  lastApiSuccessAt null" \
  "$(live_fresh | sed -E 's/"lastApiSuccessAt":"[^"]*"/"lastApiSuccessAt":null/')"
expect online "  cookiesValid null" \
  "$(live_fresh | sed -E 's/"cookiesValid":true/"cookiesValid":null/')"
expect online "  degraded and authBlackout both absent" \
  "$(live_fresh | sed -E 's/"degraded":false,//; s/"authBlackout":false,//')"
expect online "  every optional term absent — degrades to available alone" \
  "{\"available\":true,\"activeMode\":\"GVApi\"}"
echo ""

# ── 5b. psidtsMintedAtUtc is NOT a term here, and this pins that. ─────────────────────────────
# RotaryPhone add this field in the same release that removes psidtsAgeSeconds. classify_voice
# deliberately does not read it: GV-12's predicate does not name it, and adopting a field that
# GvBridgeHealth.IsHealthy does not carry would put the launcher and the console into
# disagreement about the same box.
#
# ⚠ This case asserts "we do not read this field", NOT "null there is healthy" — RotaryPhone are
# explicit that its null means UNKNOWN and is not healthy. If someone later adds it as a term
# they must treat null as "cannot assert healthy", and this case going RED is how they find out
# they are changing a decision rather than filling a gap.
#
# ⚠ The shape below is UNVERIFIED: it comes from a contract document our lane held as a
# superseded draft, and the live box still serves the pre-#78 build, so it could not be checked
# against the wire. The §1 fixture — the live capture with the field simply deleted — is the
# minimal claim and does not depend on this being right.
echo "psidtsMintedAtUtc is not read (announced post-#79 additions, shape unverified)"
expect online "  present and null — ignored, not consulted" \
  "$(live_fresh_post79 | sed -E 's/\{/{"psidtsMintedAtUtc":null,"browserSessionStale":false,/')"
echo ""

# ── 6. An uninterpretable timestamp is NO SIGNAL, not a verdict. ─────────────────────────────
# Documented in the launcher next to the code. Stated here so the choice is visible as a choice.
# ── 6b. Pre-epoch instants are MAXIMALLY STALE, not uninterpretable. ─────────────────────────
# Caught in pre-merge review. `date -d` prints a NEGATIVE epoch for a pre-1970 instant, and a
# digits-only guard read that leading `-` as a parse failure — routing the most stale reading
# obtainable onto the no-signal arm and reporting a dead bridge as Online. `0001-01-01T00:00:00Z`
# is exactly what .NET's DateTime.MinValue serialises to, i.e. what a bridge that has NEVER had a
# successful API call would report, and GvBridgeHealth.IsHealthy calls that body unhealthy.
echo "pre-epoch timestamps"
expect needsignin "  0001-01-01Z — .NET DateTime.MinValue, 'never succeeded'" \
  '{"available":true,"lastApiSuccessAt":"0001-01-01T00:00:00Z"}'
expect needsignin "  1960-01-01Z — pre-epoch, passes the zone check" \
  '{"available":true,"lastApiSuccessAt":"1960-01-01T00:00:00Z"}'
expect needsignin "  1970-01-01Z — the epoch itself, no sign involved" \
  '{"available":true,"lastApiSuccessAt":"1970-01-01T00:00:00Z"}'
echo ""

# ── 6c. Known divergences from System.Text.Json, pinned so a change is deliberate. ───────────
# ⚠ These assert what this parser DOES, not what is correct. GvBridgeHealth reading the same two
# bodies answers unhealthy for both: System.Text.Json takes the LAST duplicate key and ignores a
# nested object. Accepted because closing either needs a real JSON parser, which the launcher
# deliberately does not depend on. Neither shape occurs in any observed payload.
echo "known parser divergences from System.Text.Json (accepted, not correct)"
expect online "  duplicate key: first wins here, last wins in JSON" \
  '{"available":true,"available":false}'
expect needsignin "  nested degraded is read as top-level; JSON would ignore it" \
  '{"available":true,"upstream":{"degraded":true}}'
echo ""

echo "uninterpretable lastApiSuccessAt"
expect online "  naive, no zone — refused rather than guessed" \
  "$(live_fresh | sed -E 's/"lastApiSuccessAt":"[^"]*"/"lastApiSuccessAt":"2026-09-09T14:30:50"/')"
expect online "  not a date at all" \
  "$(live_fresh | sed -E 's/"lastApiSuccessAt":"[^"]*"/"lastApiSuccessAt":"soon"/')"
echo ""

# ── 7. The safety property the old guard had, preserved through the change. ──────────────────
# `:118-123` of the launcher wrote a guard specifically so an unreadable value could not report a
# dead session as Online. The input changed; the property must not.
# ⚠ SCOPE: this is a property of classify_voice, NOT of the launcher. When :5004 is unreachable
# the body is empty, but run_probes never calls this function — PHONE goes `down` and VOICE falls
# back to the bare process check, which reports Online on the strength of a Chrome process
# existing. That fallback is pre-existing and deliberate (§5.3's last table row). The heading
# below says "must never read as online" about the FUNCTION, and that is all it can say.
echo "malformed and empty bodies must never read as online (within classify_voice)"
expect_not online "  empty body"          ""
expect_not online "  not JSON"            "<html>502 Bad Gateway</html>"
expect_not online "  JSON, but not this"  '{"error":"nope"}'
expect_not online "  available absent"    "$(live_fresh | sed -E 's/"available":true,//')"
expect_not online "  available is a STRING, not a bool"  '{"available":"true"}'
expect_not online "  truncated mid-token"                '{"available":tru'
echo ""

# ── 7b. Parser shape robustness. ─────────────────────────────────────────────────────────────
# The grep patterns anchor on the quote-name-quote-colon shape, which is what stops a key's text
# inside a string VALUE from being read as the key. In VALID JSON an inner `"` is always escaped,
# so the backslash always intervenes — that is why this holds rather than luck.
#
# ⚠ KNOWN AND ACCEPTED: a NESTED object carrying one of these key names would be read as though
# it were top-level. The payload is flat today (verified against the live body), the pre-existing
# psidts parser had the identical property, and the failure direction is safe — it can only make
# the row amber, never falsely Online. Documented rather than fixed, because fixing it means a
# real JSON parser and `:106-108` explains why this file does not take that dependency.
echo "parser shape"
expect online     "  whitespace around every colon still parses" \
  '{ "available" : true , "degraded" : false }'
expect needsignin "  a key's text inside a string value is not read as the key" \
  '{"note":"\"available\": true","available":false}'
echo ""

# ── 8. The process arm still short-circuits everything above it. ─────────────────────────────
echo "bridge Chrome not running"
# Set and unset explicitly rather than as a `VAR=x func` prefix: bash keeps such an assignment
# after a FUNCTION call (unlike a command), so the prefix form would silently leak into any case
# added below it.
STUB_VOICE_PROCESS_RC=1
expect offline "  offline regardless of a healthy body" "$(live_fresh)"
unset STUB_VOICE_PROCESS_RC
echo ""

# ── 9. The sourcing seam must NOT fire when the launcher is EXECUTED. ─────────────────────────
# Everything above this point proves the SOURCED direction, because it sources. Nothing proved
# the executed one — and that is the direction whose failure mode is the worst in this file: the
# desktop icon silently becomes a no-op, on a panel with no keyboard, with nothing logged. So run
# it for real with a bad argument, which reaches the dispatch BELOW the seam and must print usage
# and exit 2. If the seam ever fired on execution this returns 0 and prints nothing.
echo "sourcing seam does not fire when executed"
seam_out="$(bash "$LAUNCHER" --a-deliberately-bad-argument 2>&1)"; seam_rc=$?
if [ "$seam_rc" = 2 ] && printf '%s' "$seam_out" | grep -q '^usage: radio-console-open'; then
  printf '  PASS  %-56s -> rc=2, usage printed\n' "  executed path still reaches the dispatch"
  PASS=$(( PASS + 1 ))
else
  printf '  FAIL  %-56s -> rc=%s, output %s\n' \
    "  executed path still reaches the dispatch" "$seam_rc" "${seam_out:-(empty)}"
  FAIL=$(( FAIL + 1 ))
fi
echo ""

printf '%d passed, %d failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
