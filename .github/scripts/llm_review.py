#!/usr/bin/env python3
"""Ask a local ollama model to review a PR diff, and print its findings.

Lives in a script rather than inline in the workflow on purpose: the first draft
embedded a Python heredoc inside a YAML block scalar, which broke both the YAML
(prompt lines at column 0 ended the scalar) and the shell (a `<<'PY'` terminator
cannot be indented). A file is also editable and diffable on its own, which
matters because the PROMPT is the part most likely to need tuning.

Reads:  the diff file named on argv[1]
Env:    OLLAMA_URL   base url, e.g. http://172.17.0.1:11434
        OLLAMA_MODEL defaults to qwen2.5-coder:14b
Writes: the model's findings to stdout. Never raises for a model-side problem —
        this is an advisory job and must not fail a PR.
"""
import json
import os
import sys
import urllib.error
import urllib.request

# The model is small (14b). It is given a narrow, checkable job rather than
# "review this diff", because that is where small models are strongest and
# where this repo's real defects cluster (CLAUDE.md § Pre-Merge Review).
PROMPT = """You are reviewing a C#/Blazor pull request for a .NET audio appliance.

Report ONLY concrete, checkable problems. If you find none, reply exactly: NO FINDINGS.
Do not praise, summarise, or restate the diff. Do not suggest stylistic changes.

Look specifically for these, which are the failure modes this codebase actually ships:

1. A comment, log message, or XML doc that asserts MORE than the code does.
   This repo shipped a method that logged "Removed audio source from mixer" while
   only removing an item from a List. Where a comment gives a REASON something is
   safe, the reason is the claim to check, not the conclusion.
2. A branch that is statically unreachable, or a guard whose condition can never be
   true given an assignment above it.
3. A method that reports success on a path where it did nothing.
4. A test that races a wall clock: asserting on something that must happen inside a
   Task.Delay or sleep, instead of synchronizing on the observation itself.
5. A lookup by key or id that can silently miss, with the miss unhandled.

For each finding give: file:line, one sentence on what is wrong, and one sentence on
how it fails. Maximum 6 findings, most serious first.

DIFF:
"""


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: llm_review.py <diff-file>", file=sys.stderr)
        return 2

    try:
        with open(sys.argv[1], encoding="utf-8", errors="replace") as fh:
            diff = fh.read()
    except OSError as exc:
        print(f"(could not read diff: {exc})")
        return 0

    if not diff.strip():
        print("NO FINDINGS")
        return 0

    base = os.environ.get("OLLAMA_URL", "").rstrip("/")
    if not base:
        print("(OLLAMA_URL not set — skipped)")
        return 0

    payload = json.dumps({
        "model": os.environ.get("OLLAMA_MODEL", "qwen2.5-coder:14b"),
        "prompt": PROMPT + diff,
        "stream": False,
        # Low temperature: we want the same diff to produce the same findings,
        # so a re-run is a check rather than a fresh roll of the dice.
        "options": {"temperature": 0.1, "num_ctx": 32768},
    }).encode("utf-8")

    req = urllib.request.Request(
        f"{base}/api/generate",
        data=payload,
        headers={"Content-Type": "application/json"},
    )

    try:
        # Generous: a 14b model over a real diff on CPU is minutes, not seconds.
        with urllib.request.urlopen(req, timeout=1200) as resp:
            body = json.load(resp)
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
        # An advisory job that fails loudly is worse than one that says nothing:
        # a red X trains people to ignore it. Report and succeed.
        print(f"(local review unavailable: {type(exc).__name__}: {exc})")
        return 0

    text = body.get("response", "(model returned no response field)").strip()

    # Observed 2026-09-07 while validating against a diff with three planted
    # defects: the model listed all three correctly and THEN appended a trailing
    # "NO FINDINGS". Left in place that reads as though the findings above were
    # retracted. Strip it only when there is other content — a genuine, lone
    # "NO FINDINGS" must survive untouched.
    lines = [ln for ln in text.splitlines()]
    while lines and lines[-1].strip().rstrip(".").upper() in ("NO FINDINGS", ""):
        if any(ln.strip() for ln in lines[:-1]):
            lines.pop()
        else:
            break  # nothing else in the response: this IS the answer
    print("\n".join(lines).strip() or "NO FINDINGS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
