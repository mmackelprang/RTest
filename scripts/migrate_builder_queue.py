#!/usr/bin/env python3
"""Restructure docs/BUILDER_QUEUE.md from a 494 KB single-table document into a thin index
plus per-row dossiers plus an archive.

Why this is a script and not a hand edit: the source holds 52 table rows whose Item cells run
to 12,946 characters on one physical line. Hand-transcribing them is how prose gets silently
dropped. Every transformation here is mechanical and every one is reconciled by character
count at the end (see `verify()`), so a lost paragraph is a hard failure rather than a
discovery three weeks later.

Outputs
    docs/BUILDER_QUEUE.md              index: live rows only (📋 🚧 🚫), one line per row
    docs/BUILDER_QUEUE_ARCHIVE.md      shipped rows (prose intact) + narrative + session log
    docs/queue/<ID>.md                 one dossier per live row, prose as ordinary markdown
    docs/queue/ORDERING-NOTES.md       § Dependency / ordering notes  (49 KB — too big to inline)
    docs/queue/CROSS-REPO-HANDOFFS.md  § Cross-repo handoffs          (14 KB)
    docs/queue/FAST-FOLLOWS.md         § Documented fast-follows      (9 KB)

Usage
    python scripts/migrate_builder_queue.py            # verify + write
    python scripts/migrate_builder_queue.py --check    # verify only, write nothing

Run from the repository root.
"""

from __future__ import annotations

import io
import os
import re
import sys
from dataclasses import dataclass, field

if hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

SRC = "docs/BUILDER_QUEUE.md"
INDEX = "docs/BUILDER_QUEUE.md"
ARCHIVE = "docs/BUILDER_QUEUE_ARCHIVE.md"
QUEUE_DIR = "docs/queue"
TODAY = "2026-09-06"
REPO_PR = "https://github.com/mmackelprang/RTest/pull/{}"

# Pipes that are NOT preceded by a backslash. A naive line.split("|") is the bug this migration
# exists to fix: AUD-1 carries `hasIncompleteMetadata \|\| FpOptions...` inside a code span, so a
# naive split hands back "\" as that row's Status.
UNESCAPED_PIPE = re.compile(r"(?<!\\)\|")

# A row's Item cell almost always opens with an optional emoji marker then a bolded title.
LEAD_TITLE = re.compile(r"^((?:[^\w\s*`\[]+\s*)*)\*\*(.+?)\*\*", re.S)

# Beats the prose is split on when a table cell becomes paragraphs. See split_paragraphs().
BEAT_EMOJI = "⚠⭐🔴🟠🟡🔵🟢📌✅🚫📋⛔🚧🔄⬆🆕❌"
BEAT = re.compile(r"[ \t]+(?=(?:\*\*|[" + BEAT_EMOJI + r"]))")

LINK = re.compile(r"\]\(([^)]+)\)")

TITLE_TOO_LONG = 100

# Character-delta buckets. Only PROSE_BUCKETS may change the Item-cell character reconciliation;
# the other two touch text that reconciliation does not count (a dossier's duplicated metadata
# table, and the three companion sections that were never inside a row).
META_BUCKET = "link rewrite in dossier metadata tables (not counted as prose)"
COMPANION_BUCKET = "link rewrite in companion sections (not counted as prose)"
PROSE_BUCKETS = {
    "link rewrite (docs/ -> docs/queue/)",
    "unescape \\| -> |",
    "<br> -> paragraph break",
}


# ----------------------------------------------------------------------------- data


@dataclass
class Row:
    line_no: int
    rid: str
    item: str
    status: str
    plan: str
    spec: str
    depends: str
    branch: str
    title_prefix: str = ""
    title: str = ""
    norm_status: str = ""
    pr: str | None = None
    pr_source: str = ""
    flags: list[str] = field(default_factory=list)

    @property
    def live(self) -> bool:
        return "✅" not in self.norm_status


@dataclass
class Delta:
    """One accounted-for change in non-whitespace character count."""

    reason: str
    chars: int
    count: int = 0


# ----------------------------------------------------------------------------- parsing


def read_source() -> list[str]:
    with open(SRC, encoding="utf-8") as fh:
        return fh.read().split("\n")


def find(lines: list[str], heading: str) -> int:
    for i, l in enumerate(lines):
        if l.strip() == heading:
            return i
    raise SystemExit(f"FATAL: heading not found, refusing to guess: {heading!r}")


def parse_rows(lines: list[str], start: int, end: int) -> list[Row]:
    """Rows between the table separator and the blank line that ends the table."""
    rows: list[Row] = []
    for i in range(start, end):
        l = lines[i]
        if not l.startswith("|"):
            continue
        parts = UNESCAPED_PIPE.split(l)
        first = parts[1].strip() if len(parts) > 1 else ""
        if first == "#" or set(first) <= {"-", ":"}:
            continue  # the table's header row and its separator
        if len(parts) != 9:
            raise SystemExit(
                f"FATAL: line {i + 1} split into {len(parts)} fields, expected 9. "
                "The escape-aware parse is wrong for this row; fix it rather than dropping it."
            )
        rows.append(
            Row(
                line_no=i + 1,
                rid=parts[1].strip(),
                # parts is ['', id, item, status, plan, spec, depends, branch, ''] — the Item cell
                # is parts[2:-6]. (The join is defensive: an unescaped pipe would widen it, and
                # parse_rows already refuses any row that does not split into exactly 9 fields.)
                item="|".join(parts[2:-6]).strip(),
                status=parts[-6].strip(),
                plan=parts[-5].strip(),
                spec=parts[-4].strip(),
                depends=parts[-3].strip(),
                branch=parts[-2].strip(),
            )
        )
    return rows


# ----------------------------------------------------------------------------- normalization


PR_IN_STATUS = re.compile(r"#(\d{3,4})\b")
# The only two structured phrasings that record a row's own PR outside the Status/Branch cells.
PR_IN_PLAN = re.compile(r"(?:shipped as|as filed —)\s*\[#(\d{3,4})\]")


def pr_number(row: Row) -> tuple[str | None, str]:
    """Recover a shipped row's PR number from STRUCTURED cells only.

    Deliberately never scraped from the Item prose. Proof that would be wrong: OPS-9's prose
    cites `#570`, which is OPS-7's PR, and ENC-20's cites `#509` and `#555`. A scrape would have
    mislabelled both.
    """
    m = PR_IN_STATUS.search(row.status)
    if m:
        return m.group(1), "status cell"
    m = PR_IN_STATUS.search(row.branch)
    if m:
        return m.group(1), "branch cell"
    m = PR_IN_PLAN.search(row.plan)
    if m:
        return m.group(1), "plan cell"
    return None, ""


def normalize_status(row: Row) -> None:
    s = row.status
    if "✅" in s:
        num, src = pr_number(row)
        row.pr, row.pr_source = num, src
        if num:
            row.norm_status = f"✅ [#{num}]({REPO_PR.format(num)})"
        else:
            row.norm_status = "✅"
            row.flags.append("shipped, but no PR number in any structured cell — left bare")
    elif "🚫" in s:
        row.norm_status = "🚫"
    elif "🚧" in s:
        row.norm_status = "🚧"
    elif "📋" in s:
        row.norm_status = "📋"
    elif "🔒" in s or "⛔" in s:
        # Neither appears in the current table, but the legend defines them. Fail loudly rather
        # than silently promoting a blocked row to claimable.
        raise SystemExit(f"FATAL: {row.rid} carries a blocked marker {s!r} with no normalized form.")
    else:
        raise SystemExit(f"FATAL: {row.rid} has an unrecognized status cell {s!r}")


def extract_title(row: Row) -> None:
    m = LEAD_TITLE.match(row.item)
    if not m:
        row.title = ""
        row.flags.append("NO LEADING BOLD TITLE — needs a hand-written index summary")
        return
    row.title_prefix = m.group(1).strip()
    row.title = m.group(2).replace("\n", " ").strip()
    # Flag only a genuine DISAGREEMENT: a marker in the prose that contradicts the row's status.
    # GV-5 opens with 🚫 and is 🚫 — that agrees, and is not worth anyone's attention.
    conflicting = [e for e in "✅🚫🚧" if e in row.title_prefix and e not in row.norm_status]
    if conflicting and row.live:
        # e.g. GV-6 opens "✅ **ASSESSED AGAINST D31 ... claim it as written.**" while its status
        # is 📋. Faithful to the source, but it reads as shipped in a one-line index cell.
        row.flags.append(
            f"prose opens with {''.join(conflicting)!r} but the row's status is {row.norm_status} — "
            "left verbatim; reads as shipped in the index"
        )
    if len(row.title) > TITLE_TOO_LONG:
        row.flags.append(f"leading bold is {len(row.title)} chars (> {TITLE_TOO_LONG}) — emitted "
                         "verbatim, NOT truncated; wants a hand-written short summary")


# ----------------------------------------------------------------------------- text transforms


def rewrite_links(text: str, deltas: dict[str, Delta], bucket: str = "link rewrite (docs/ -> docs/queue/)") -> str:
    """Re-root docs/-relative links for a file that now lives in docs/queue/.

    `../design/x` resolved from docs/ must become `../../design/x` from docs/queue/;
    a bare `uat/REPORT.md` must become `../uat/REPORT.md`.

    `bucket` separates rewrites made inside row PROSE (which the reconciliation counts) from
    rewrites made in a dossier's duplicated metadata table (which it must not).
    """
    d = deltas.setdefault(bucket, Delta(bucket, 0))

    def sub(m: re.Match) -> str:
        t = m.group(1)
        if t.startswith(("http://", "https://", "mailto:", "file:", "/", "#")):
            return m.group(0)
        new = "../" + t
        d.chars += 3
        d.count += 1
        return "](" + new + ")"

    return LINK.sub(sub, text)


def to_markdown(text: str, deltas: dict[str, Delta]) -> str:
    """Turn a one-line table cell into ordinary markdown paragraphs.

    Only two things remove characters, and both are accounted for:
      * `\\|` -> `|`   (the table escape is wrong outside a table: inside a code span it would
                        render the backslash literally)
      * `<br>` -> paragraph break
    Everything else is a whitespace-only change, so the non-whitespace character count of the
    result is exactly the source count minus those two accounted deltas.
    """
    d_esc = deltas.setdefault("unescape \\| -> |", Delta("unescape \\| -> |", 0))
    n = text.count("\\|")
    if n:
        d_esc.chars -= n
        d_esc.count += n
        text = text.replace("\\|", "|")

    d_br = deltas.setdefault("<br> -> paragraph break", Delta("<br> -> paragraph break", 0))
    n = len(re.findall(r"<br\s*/?>", text))
    if n:
        d_br.chars -= sum(len(m) for m in re.findall(r"<br\s*/?>", text))
        d_br.count += n
        text = re.sub(r"<br\s*/?>\s*", "\n\n", text)

    return split_paragraphs(text)


def split_paragraphs(text: str) -> str:
    """Insert paragraph breaks at authorial beats. Whitespace-only: no character is added or
    removed, so the reconciliation stays exact.

    A break goes in only where all three hold:
      1. the left side closes a sentence (allowing trailing ** ` * ) " _ closers),
      2. the right side opens a new beat (a bold run or a marker emoji),
      3. the offset is outside every bold run and every code span — otherwise the break would
         sever the run and change how the text renders.
    """
    out = []
    for block in text.split("\n\n"):
        pieces = []
        last = 0
        for m in BEAT.finditer(block):
            pos = m.start()
            left = block[:pos].rstrip()
            left = left.rstrip("*`_\")]}")
            if not left.endswith((".", "!", "?")):
                continue
            if block.count("**", 0, pos) % 2 or block.count("`", 0, pos) % 2:
                continue  # inside a bold run or a code span
            if pos - last < 80:
                continue  # keep paragraphs substantial rather than one per sentence
            pieces.append(block[last:pos].strip())
            last = m.end()
        pieces.append(block[last:].strip())
        out.append("\n\n".join(p for p in pieces if p))
    return "\n\n".join(out)


def nonws(s: str) -> int:
    return len("".join(s.split()))


# ----------------------------------------------------------------------------- emission


def meta_table(row: Row, rewrite: bool, deltas: dict[str, Delta]) -> str:
    cells = [row.plan, row.spec, row.depends, row.branch]
    if rewrite:
        cells = [rewrite_links(c, deltas, META_BUCKET) for c in cells]
    plan, spec, depends, branch = cells
    return (
        "| Field | Value |\n"
        "|---|---|\n"
        f"| Status | {row.norm_status} |\n"
        f"| Plan | {plan} |\n"
        f"| Spec / handoff | {spec} |\n"
        f"| Depends on | {depends} |\n"
        f"| Branch | {branch} |\n"
    )


def heading_title(row: Row) -> str:
    return row.title if row.title else "(no title in source — see the row body)"


def dossier(row: Row, deltas: dict[str, Delta]) -> str:
    body = to_markdown(rewrite_links(row.item, deltas), deltas)
    return (
        f"# {row.rid} — {heading_title(row)}\n\n"
        f"> Queue dossier for row **`{row.rid}`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).\n"
        f"> The detail below was moved verbatim out of that row's Item cell on {TODAY}; only\n"
        f"> whitespace, the table's `\\|` escapes and docs-relative link prefixes changed.\n\n"
        f"{meta_table(row, True, deltas)}\n"
        f"## Detail\n\n"
        f"{body}\n"
    )


def archive_section(row: Row, deltas: dict[str, Delta]) -> str:
    body = to_markdown(row.item, deltas)  # archive sits in docs/, so links need no rewrite
    return (
        f"### {row.rid} — {heading_title(row)}\n\n"
        f"{meta_table(row, False, deltas)}\n"
        f"{body}\n"
    )


def build_index(rows: list[Row], legend: str, risks: str, sizes: dict[str, int]) -> str:
    live = [r for r in rows if r.live]
    shipped = [r for r in rows if not r.live]
    counts = {}
    for r in rows:
        counts[r.norm_status[0]] = counts.get(r.norm_status[0], 0) + 1

    out = ["# Builder Queue", ""]
    out += [
        "> Work items queued by **Planner** for **Builder** to clear one PR per cycle.",
        "> Planner appends rows + spec/plan links; Builder claims a 📋 row whose dependencies are all met, ships it as a PR, then marks it ✅.",
        ">",
        f"> **Last updated:** {TODAY} (Builder) — **the queue was restructured; no row's meaning changed.** "
        f"This file is now an index of **live rows only** ({len(live)} of {len(rows)}). Each live row's full prose "
        f"moved verbatim to a dossier under [`queue/`](queue/), and the {len(shipped)} shipped rows moved, prose intact, "
        f"to [`BUILDER_QUEUE_ARCHIVE.md`](BUILDER_QUEUE_ARCHIVE.md) — which also holds this file's former "
        "`Last updated` session log and its resolved-blocker narrative.",
        ">",
        "> ⚠ **Planner: append new rows here as one line.** The Item cell is a short title plus a link to "
        "`queue/<ID>.md`; the detail belongs in the dossier. The old shape put 12,946 characters on one "
        "physical line, which put the file past what any agent could read in a single pass.",
        "",
        "---",
        "",
    ]

    out += [legend.rstrip(), "", "---", "", "## Queue", ""]
    out += [
        "| # | Item | Status | Plan | Spec / handoff | Depends on | Branch |",
        "|---|------|--------|------|----------------|-----------|--------|",
    ]
    for r in live:
        prefix = (r.title_prefix + " ") if r.title_prefix else ""
        title = r.title if r.title else f"see the dossier"
        cell = f"{prefix}**{title}** — [detail](queue/{r.rid}.md)".replace("|", "\\|")
        out.append(
            f"| {r.rid} | {cell} | {r.norm_status} | {r.plan} | {r.spec} | {r.depends} | {r.branch} |"
        )
    out += [
        "",
        f"**Shipped rows are not listed here.** All {len(shipped)} of them, with their prose intact, are in "
        f"[`BUILDER_QUEUE_ARCHIVE.md`](BUILDER_QUEUE_ARCHIVE.md).",
        "",
        "---",
        "",
    ]

    out += [risks.rstrip(), "", "---", "", "## Companion notes (moved out of this file)", ""]
    out += [
        "These three sections were measured against the index's size budget and moved rather than inlined. "
        "Nothing in them was edited; each file opens with the section verbatim.",
        "",
        "| Section | Size | Now at |",
        "|---|---|---|",
        f"| Dependency / ordering notes — claim order, and why each ordering is load-bearing | {sizes['ordering'] // 1024} KB | [`queue/ORDERING-NOTES.md`](queue/ORDERING-NOTES.md) |",
        f"| Cross-repo handoffs (RotaryPhone — NOT claimable here) | {sizes['crossrepo'] // 1024} KB | [`queue/CROSS-REPO-HANDOFFS.md`](queue/CROSS-REPO-HANDOFFS.md) |",
        f"| Documented fast-follows (NOT in these PRs) | {sizes['fastfollows'] // 1024} KB | [`queue/FAST-FOLLOWS.md`](queue/FAST-FOLLOWS.md) |",
        "",
        "⚠ **Read `ORDERING-NOTES.md` before claiming.** Several rows have a mandatory claim order "
        "(`AUD-2` before `AUD-4`, `AUD-6` before `AUD-7`) that this index does not restate.",
        "",
    ]
    return "\n".join(out) + "\n"


def build_archive(rows: list[Row], banner: str, narrative: str, deltas: dict[str, Delta]) -> str:
    shipped = [r for r in rows if not r.live]
    out = [
        "# Builder Queue — Archive",
        "",
        f"> Shipped rows and historical narrative moved out of [`BUILDER_QUEUE.md`](BUILDER_QUEUE.md) on {TODAY}.",
        "> **Nothing here was edited.** Each row's prose is the text that was in its Item cell, re-homed from a",
        "> table cell into ordinary markdown; only whitespace changed. Status cells were normalized to the",
        "> four-mark vocabulary and nothing else was.",
        ">",
        "> This file is an archive, not a queue. Nothing in it is claimable.",
        "",
        "---",
        "",
        f"## Shipped rows ({len(shipped)})",
        "",
    ]
    for r in shipped:
        out.append(archive_section(r, deltas))
        out.append("---\n")
    out += ["", "## Historical narrative", "",
            "> Moved verbatim from `BUILDER_QUEUE.md`. These sections documented a resolved blocker and the",
            "> reasoning around it; they are kept for the record.", "", narrative.rstrip(), "",
            "---", "", "## Session log", "",
            "> The `Last updated` / `Prior entry` blockquote stack that used to sit in the index header.",
            "> 44 entries, ~157 KB — it was the single largest block in the file and none of it was queue state.",
            "", banner.rstrip(), ""]
    return "\n".join(out) + "\n"


def build_companion(title: str, body: str, blurb: str, deltas: dict[str, Delta]) -> str:
    # The body opens with its own "## <title>" heading. Promote it to the file's H1 rather than
    # emitting the same heading twice; the text is identical, so nothing is lost.
    first, _, rest = body.partition("\n")
    if first.strip() != f"## {title}":
        raise SystemExit(f"FATAL: companion section did not open with '## {title}': {first!r}")
    return (
        f"# {title}\n\n"
        f"> Moved verbatim from [`../BUILDER_QUEUE.md`](../BUILDER_QUEUE.md) on {TODAY}. {blurb}\n"
        f"> Nothing below was edited; this file's H1 is the section's own heading, promoted.\n\n"
        f"{rewrite_links(rest, deltas, COMPANION_BUCKET).strip()}\n"
    )


# ----------------------------------------------------------------------------- verification


def verify(rows: list[Row], written: dict[str, str], deltas: dict[str, Delta], src_lines: list[str]) -> bool:
    ok = True
    print()
    print("=" * 78)
    print("VERIFICATION")
    print("=" * 78)

    # 1. every row id appears exactly once across index + archive
    print("\n1. ROW RECONCILIATION")
    queue_table = written[INDEX].split("## Queue\n\n", 1)[1].split("\n\n", 1)[0]
    index_ids = re.findall(r"^\| ([A-Z][A-Za-z0-9-]*) \|", queue_table, re.M)
    # Scope to the shipped-rows section: the narrative that follows carries its own "### B1 — ",
    # "### B2 — " headings, which the row pattern would otherwise pick up as rows.
    shipped_region = written[ARCHIVE].split("## Shipped rows", 1)[1].split("\n## Historical narrative", 1)[0]
    archive_ids = re.findall(r"^### ([A-Z][A-Za-z0-9-]*) — ", shipped_region, re.M)
    all_ids = index_ids + archive_ids
    src_ids = [r.rid for r in rows]
    print(f"   source rows              : {len(src_ids)}")
    print(f"   index rows (live)        : {len(index_ids)}")
    print(f"   archive sections (shipped): {len(archive_ids)}")
    print(f"   total emitted            : {len(all_ids)}")
    dupes = [i for i in set(all_ids) if all_ids.count(i) > 1]
    missing = [i for i in src_ids if i not in all_ids]
    extra = [i for i in all_ids if i not in src_ids]
    if len(all_ids) != len(src_ids) or dupes or missing or extra:
        ok = False
        print(f"   FAIL  missing={missing} extra={extra} duplicated={dupes}")
    else:
        print("   PASS  every source row id appears exactly once in the output set")

    # 2. prose reconciliation
    print("\n2. PROSE RECONCILIATION (non-whitespace characters of Item cells)")
    src_chars = sum(nonws(r.item) for r in rows)
    out_chars = 0
    for r in rows:
        if r.live:
            body = written[f"{QUEUE_DIR}/{r.rid}.md"].split("## Detail\n\n", 1)[1]
        else:
            sec = written[ARCHIVE].split(f"\n### {r.rid} — ", 1)[1]
            body = sec.split("| Branch |", 1)[1].split(" |\n", 1)[1]
            body = body.split("\n---\n", 1)[0]
        out_chars += nonws(body)
    prose = [d for d in deltas.values() if d.reason in PROSE_BUCKETS]
    expected = sum(d.chars for d in prose)
    actual = out_chars - src_chars
    print(f"   source Item cells        : {src_chars:,} non-whitespace chars")
    print(f"   emitted dossiers+archive : {out_chars:,} non-whitespace chars")
    print(f"   delta                    : {actual:+,}  ({abs(actual) / src_chars * 100:.4f}%)")
    print("   accounted for by:")
    for d in sorted(prose, key=lambda x: x.reason):
        print(f"     {d.chars:+6d} chars   {d.count:4d} x  {d.reason}")
    for d in sorted(deltas.values(), key=lambda x: x.reason):
        if d.reason not in PROSE_BUCKETS:
            print(f"     (excluded)      {d.count:4d} x  {d.reason}")
    if actual == expected:
        print(f"   PASS  delta exactly matches the {len(prose)} declared prose transformations "
              f"({expected:+,}) — no prose lost")
    else:
        ok = False
        print(f"   FAIL  delta {actual:+,} != declared {expected:+,}; {abs(actual - expected)} chars unaccounted")

    # 2b. sequence identity, which a character COUNT alone would not catch: matching totals are
    # consistent with scrambled text. Re-derive each row's expected body from the source and
    # compare the whitespace-stripped character sequences.
    print("\n2b. PROSE IS VERBATIM (whitespace-stripped sequence identity, per row)")
    mismatched = []
    for r in rows:
        throwaway: dict[str, Delta] = {}
        src = rewrite_links(r.item, throwaway) if r.live else r.item
        expected_body = to_markdown(src, throwaway)
        if r.live:
            got = written[f"{QUEUE_DIR}/{r.rid}.md"].split("## Detail\n\n", 1)[1]
        else:
            sec = written[ARCHIVE].split(f"\n### {r.rid} — ", 1)[1]
            got = sec.split("| Branch |", 1)[1].split(" |\n", 1)[1].split("\n---\n", 1)[0]
        if "".join(expected_body.split()) != "".join(got.split()):
            mismatched.append(r.rid)
    if mismatched:
        ok = False
        print(f"   FAIL  {len(mismatched)} row(s) differ from their source text: {mismatched}")
    else:
        print(f"   PASS  all {len(rows)} row bodies are character-for-character the source Item")
        print("         cell, modulo whitespace and the 3 declared transformations")

    # 3. the index table parses
    print("\n3. INDEX QUEUE TABLE PARSES (the specific bug being fixed)")
    region = written[INDEX].split("## Queue\n\n", 1)[1].split("\n\n", 1)[0]
    tbl = [l for l in region.split("\n") if l.startswith("|")]
    naive = sorted({len(l.split("|")) for l in tbl})
    aware = sorted({len(UNESCAPED_PIPE.split(l)) for l in tbl})
    print(f"   table lines (incl. header + separator): {len(tbl)}")
    for l in tbl:
        n_naive, n_aware = len(l.split("|")), len(UNESCAPED_PIPE.split(l))
        rid = l.split("|")[1].strip()
        mark = "ok " if n_naive == n_aware == 9 else "BAD"
        print(f"     {mark} {rid:12s} naive={n_naive:2d} escape-aware={n_aware:2d}")
    if naive == aware == [9]:
        print("   PASS  every line splits into exactly 9 fields (7 columns) under BOTH a naive")
        print("         split('|') and an escape-aware split — no embedded pipes remain")
    else:
        ok = False
        print(f"   FAIL  naive={naive} escape-aware={aware}")

    # 4. status vocabulary
    print("\n4. STATUS VOCABULARY")
    allowed = re.compile(r"^(📋|🚧|🚫|✅|✅ \[#\d{3,4}\]\(https://github\.com/mmackelprang/RTest/pull/\d{3,4}\))$")
    bad = [(r.rid, r.norm_status) for r in rows if not allowed.match(r.norm_status)]
    tally: dict[str, int] = {}
    for r in rows:
        k = r.norm_status[0] if not r.norm_status.startswith("✅") else "✅"
        tally[k] = tally.get(k, 0) + 1
    print(f"   tally: " + "  ".join(f"{k} {v}" for k, v in sorted(tally.items())))
    if bad:
        ok = False
        print(f"   FAIL  non-conforming: {bad}")
    else:
        print("   PASS  every status is one of the four normalized forms")
    kept = [(r.rid, r.pr, r.pr_source) for r in rows if r.pr]
    print(f"   PR numbers preserved: {len(kept)}")
    for rid, pr, src in kept:
        print(f"     {rid:12s} #{pr}  (from {src})")
    bare = [r.rid for r in rows if not r.live and not r.pr]
    print(f"   shipped rows with NO PR number in any structured cell: {len(bare)}")
    print(f"     {', '.join(bare)}")
    print("     (never had one; not invented — see the report)")

    # 5. links resolve
    print("\n5. INTERNAL LINKS RESOLVE")
    total = 0
    broken = []
    for path, text in written.items():
        base = os.path.dirname(path)
        for t in LINK.findall(text):
            if t.startswith(("http://", "https://", "mailto:", "file:", "#")):
                continue
            total += 1
            rel = t.split("#")[0]
            target = os.path.normpath(os.path.join(base, rel))
            norm = target.replace("\\", "/")
            resolved = (
                os.path.exists(target)                                   # already on disk
                or norm in written                                       # a file this run writes
                or any(w.startswith(norm + "/") for w in written)        # a directory this run creates
            )
            if not resolved:
                broken.append((path, t, target))
    print(f"   relative links checked: {total}")
    if broken:
        ok = False
        for p, t, target in broken[:40]:
            print(f"   FAIL  {p}: {t} -> {target}")
    else:
        print("   PASS  every relative link resolves to a file on disk or a file this run wrote")

    # 6. plan links on shipped rows
    print("\n6. SHIPPED ROWS WITH A design/plans/*.md PLAN LINK")
    n, ok6 = 0, True
    for r in rows:
        if r.live:
            continue
        for t in LINK.findall(r.plan):
            if "design/plans/" in t:
                target = os.path.normpath(os.path.join("docs", t.split("#")[0]))
                state = "ok" if os.path.exists(target) else "MISSING"
                if state == "MISSING":
                    ok = ok6 = False
                n += 1
                print(f"   {state:8s} {r.rid:10s} -> {target}")
    print(f"   PASS  {n} design/plans links on shipped rows, all pointing at files that exist"
          if ok6 else "   FAIL  a shipped row's plan link does not resolve")

    # 7. paragraph splitting never severed a bold run or a code span
    print("\n7. PARAGRAPH SPLITS DID NOT SEVER A BOLD RUN OR CODE SPAN")
    # A paragraph with an odd count is only MY fault if the row's whole body is balanced — i.e. a
    # split fell inside a run. Where the source text was already unbalanced the imbalance is
    # pre-existing and out of scope to fix (ground rule: re-home text, do not edit it).
    severed, preexisting, checked = [], [], 0
    for r in rows:
        if r.live:
            body = written[f"{QUEUE_DIR}/{r.rid}.md"].split("## Detail\n\n", 1)[1]
        else:
            sec = written[ARCHIVE].split(f"\n### {r.rid} — ", 1)[1]
            body = sec.split("| Branch |", 1)[1].split(" |\n", 1)[1].split("\n---\n", 1)[0]
        for para in body.split("\n\n"):
            checked += 1
            odd_b, odd_c = para.count("**") % 2, para.count("`") % 2
            if not (odd_b or odd_c):
                continue
            whole_ok = not (body.count("**") % 2 or body.count("`") % 2)
            (severed if whole_ok else preexisting).append((r.rid, para.strip()[:88]))
    print(f"   paragraphs checked: {checked} (row bodies only)")
    if severed:
        ok = False
        for rid, s in severed[:20]:
            print(f"   FAIL  {rid}: a split severed a run — {s}")
    else:
        print("   PASS  no split severed a bold run or code span")
    if preexisting:
        print(f"   NOTE  {len(preexisting)} paragraph(s) carry an odd marker count that is PRE-EXISTING")
        print("         in the source row (unbalanced ** or ` in the original prose). Not introduced")
        print("         here and deliberately not fixed — that is a content edit:")
        for rid, s in preexisting[:10]:
            print(f"           {rid}: {s}")

    print()
    print("=" * 78)
    print("RESULT:", "ALL CHECKS PASS" if ok else "*** FAILURES ABOVE ***")
    print("=" * 78)
    return ok


# ----------------------------------------------------------------------------- main


def main() -> int:
    check_only = "--check" in sys.argv
    lines = read_source()

    # This script rewrites its own input, so it is NOT idempotent: a second run would parse the
    # already-migrated index (16 rows, no shipped prose) and emit a lint archive over the real one.
    # Refuse rather than destroy. Recover the pre-migration file from git if you need to re-run.
    if any(l.startswith("# Builder Queue") for l in lines[:1]) and not any(
        "Prior entry" in l for l in lines[:200]
    ):
        raise SystemExit(
            "REFUSING TO RUN: docs/BUILDER_QUEUE.md looks already migrated (no session-log banner).\n"
            "This migration is one-shot. To re-run it, restore the pre-migration file first:\n"
            "    git checkout <pre-migration-ref> -- docs/BUILDER_QUEUE.md"
        )

    i_legend = find(lines, "## Status legend")
    i_queue = find(lines, "## Queue")
    i_narr = find(lines, "## ✅ Resolved — `PHN-1e`'s blocker, and what each half cost")
    i_deps = find(lines, "## Dependency / ordering notes")
    i_risks = find(lines, "## Carried risks (baked into the plans as explicit tasks)")
    i_cross = find(lines, "## Cross-repo handoffs (RotaryPhone — NOT claimable here)")
    i_ff = find(lines, "## Documented fast-follows (NOT in these PRs)")

    banner = "\n".join(lines[5:i_legend]).strip()          # the Last updated / Prior entry stack
    legend = "\n".join(lines[i_legend:i_queue]).strip().rstrip("-").strip()
    narrative = "\n".join(lines[i_narr:i_deps]).strip()
    ordering = "\n".join(lines[i_deps:i_risks]).strip()
    risks = "\n".join(lines[i_risks:i_cross]).strip()
    crossrepo = "\n".join(lines[i_cross:i_ff]).strip()
    fastfollows = "\n".join(lines[i_ff:]).strip()

    sizes = {
        "ordering": len(ordering.encode()),
        "crossrepo": len(crossrepo.encode()),
        "fastfollows": len(fastfollows.encode()),
        "banner": len(banner.encode()),
        "narrative": len(narrative.encode()),
    }

    rows = parse_rows(lines, i_queue, i_narr)
    for r in rows:
        normalize_status(r)
        extract_title(r)

    deltas: dict[str, Delta] = {}
    written: dict[str, str] = {}

    for r in rows:
        if r.live:
            written[f"{QUEUE_DIR}/{r.rid}.md"] = dossier(r, deltas)

    written[ARCHIVE] = build_archive(rows, banner, narrative, deltas)
    written[f"{QUEUE_DIR}/ORDERING-NOTES.md"] = build_companion(
        "Dependency / ordering notes", ordering,
        "Claim order for the queue, and why each ordering is load-bearing.", deltas)
    written[f"{QUEUE_DIR}/CROSS-REPO-HANDOFFS.md"] = build_companion(
        "Cross-repo handoffs (RotaryPhone — NOT claimable here)", crossrepo,
        "These live in the RotaryPhone repo and are not Radio Console queue rows.", deltas)
    written[f"{QUEUE_DIR}/FAST-FOLLOWS.md"] = build_companion(
        "Documented fast-follows (NOT in these PRs)", fastfollows,
        "Work deliberately deferred out of shipped PRs.", deltas)
    written[INDEX] = build_index(rows, legend, risks, sizes)

    print(f"source: {SRC}  {os.path.getsize(SRC):,} bytes, {len(lines)} lines, {len(rows)} rows")
    longest = max(rows, key=lambda r: len(r.item))
    print(f"longest Item cell: {longest.rid} at {len(longest.item):,} chars")
    print("\nSECTION SIZES IN SOURCE")
    for k, v in sizes.items():
        print(f"   {k:12s} {v:8,} bytes")

    print("\nFLAGGED ROWS (need a hand-written index summary)")
    any_flag = False
    for r in rows:
        for f in r.flags:
            if r.live or "NO LEADING BOLD" in f:
                any_flag = True
                print(f"   {r.rid:10s} {f}")
                print(f"              title: {r.title[:160]}")
    if not any_flag:
        print("   none")

    ok = verify(rows, written, deltas, lines)

    print("\nOUTPUT SIZES")
    for p in sorted(written):
        print(f"   {len(written[p].encode()):8,} bytes  {p}")

    if check_only:
        print("\n--check: nothing written.")
        return 0 if ok else 1
    if not ok:
        print("\nVerification failed; refusing to write.")
        return 1

    os.makedirs(QUEUE_DIR, exist_ok=True)
    for p, text in written.items():
        with open(p, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
    print(f"\nwrote {len(written)} files.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
