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
# is a VERBATIM capture from the box on 2026-09-09, hours old by construction and getting older.
# A starved runner can only make a "fresh" reading older, i.e. move it TOWARD the gate — it would
# have to stall for 90 seconds inside one `classify_voice` call to flip an assertion. There is no
# case here whose PASS depends on something happening quickly.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAUNCHER="$SCRIPT_DIR/../bin/radio-console-open"

[ -r "$LAUNCHER" ] || { echo "cannot read $LAUNCHER" >&2; exit 2; }

# shellcheck source=../bin/radio-console-open
# shellcheck disable=SC1090
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
expect needsignin "  the 2026-09-09T14:30Z capture, replayed (stale by construction)" \
  "$LIVE_CAPTURE"
expect online     "  59s old — inside the gate, one measured cadence tick" \
  "$(live_fresh | sed -E "s/\"lastApiSuccessAt\":\"[^\"]*\"/\"lastApiSuccessAt\":\"$(date -u -d '59 seconds ago' +%Y-%m-%dT%H:%M:%S.0000000Z)\"/")"
echo ""

# ── 5. ABSENT CONTRIBUTES NOTHING (ADR-032). ─────────────────────────────────────────────────
# The half of the asymmetry that makes this predicate survive a field being deleted. If any of
# these ever reads needsignin, the predicate has acquired a term that can never clear, and the
# VOICE row is pinned again — which is this row's entire defect, wearing a different field name.
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

# ── 6. An uninterpretable timestamp is NO SIGNAL, not a verdict. ─────────────────────────────
# Documented in the launcher next to the code. Stated here so the choice is visible as a choice.
echo "uninterpretable lastApiSuccessAt"
expect online "  naive, no zone — refused rather than guessed" \
  "$(live_fresh | sed -E 's/"lastApiSuccessAt":"[^"]*"/"lastApiSuccessAt":"2026-09-09T14:30:50"/')"
expect online "  not a date at all" \
  "$(live_fresh | sed -E 's/"lastApiSuccessAt":"[^"]*"/"lastApiSuccessAt":"soon"/')"
echo ""

# ── 7. The safety property the old guard had, preserved through the change. ──────────────────
# `:118-123` of the launcher wrote a guard specifically so an unreadable value could not report a
# dead session as Online. The input changed; the property must not.
echo "malformed and empty bodies must never read as online"
expect_not online "  empty body"          ""
expect_not online "  not JSON"            "<html>502 Bad Gateway</html>"
expect_not online "  JSON, but not this"  '{"error":"nope"}'
expect_not online "  available absent"    "$(live_fresh | sed -E 's/"available":true,//')"
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

printf '%d passed, %d failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
