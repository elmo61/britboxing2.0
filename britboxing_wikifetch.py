#!/usr/bin/env python3
"""
britboxing_wikifetch.py
-----------------------
Pulls a fighter's professional boxing record + bio from English Wikipedia
(MediaWiki API) and emits the JSONB snapshot shape from BOXING_SITE_PLAN.md.

WHY THIS SOURCE
  Wikipedia content is licensed CC BY-SA 4.0 and the MediaWiki API is a
  public, sanctioned interface. Facts (a win/loss count) aren't copyrightable,
  but when you display derived text you should attribute Wikipedia. Wikidata
  IDs/structured data are CC0.

HOW IT FITS YOUR PLAN
  This is a PROTOTYPE / enrichment helper meant to run at match-card creation
  time (human in the loop -- you eyeball the output before it's frozen into a
  snapshot). Once you're happy with the data quality, the same logic ports
  cleanly into FighterEnrichmentService.cs.

WIKIMEDIA ETIQUETTE (please respect)
  * A descriptive User-Agent with contact info is REQUIRED by policy --
    edit USER_AGENT below before running.
  * Keep volume low. This does ~2 requests per fighter; that's fine.
  * The script sends maxlag=5 and backs off, per the API guidelines.

USAGE
  pip install requests mwparserfromhell
  python britboxing_wikifetch.py "Daniel Dubois" "Anthony Joshua"
  python britboxing_wikifetch.py --title "Nick Ball (boxer)" --records
  python britboxing_wikifetch.py "Conor Benn" --out conor.json

OUTPUT
  A list of snapshot objects (or a single object) printed as JSON, or written
  to --out. Every object carries a `_meta` block with the source URL, license
  note, and any fields that need manual verification.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
import time
from typing import Optional

import requests

try:
    import mwparserfromhell  # robust wikitext template parsing
    HAVE_MWPARSER = True
except ImportError:
    HAVE_MWPARSER = False

API = "https://en.wikipedia.org/w/api.php"

# >>> EDIT THIS before running. Wikimedia requires a real contact. <<<
USER_AGENT = "BritBoxingDataBot/0.1 (https://britboxing.co.uk; contact@britboxing.co.uk)"

SESSION = requests.Session()
SESSION.headers.update({"User-Agent": USER_AGENT})


# --------------------------------------------------------------------------- #
# Low-level API helper
# --------------------------------------------------------------------------- #
def _get(params: dict) -> dict:
    params = {**params, "format": "json", "maxlag": "5"}
    last_exc = None
    for attempt in range(4):
        try:
            r = SESSION.get(API, params=params, timeout=20)
        except requests.RequestException as e:
            last_exc = e
            time.sleep(1.5 * (attempt + 1))
            continue
        if r.status_code == 200:
            data = r.json()
            if isinstance(data, dict) and data.get("error", {}).get("code") == "maxlag":
                time.sleep(2 * (attempt + 1))
                continue
            return data
        time.sleep(1.5 * (attempt + 1))
    if last_exc:
        raise last_exc
    return {}


def search_title(query: str) -> Optional[str]:
    """Resolve a fighter name to the best-matching Wikipedia article title."""
    data = _get({
        "action": "query",
        "list": "search",
        "srsearch": f"{query} boxer",
        "srlimit": 1,
    })
    hits = data.get("query", {}).get("search", [])
    return hits[0]["title"] if hits else None


def fetch_wikitext(title: str) -> Optional[str]:
    data = _get({
        "action": "query",
        "prop": "revisions",
        "titles": title,
        "rvprop": "content",
        "rvslots": "main",
        "redirects": 1,
    })
    pages = data.get("query", {}).get("pages", {})
    for _, page in pages.items():
        if "missing" in page:
            return None
        revs = page.get("revisions", [])
        if revs:
            return revs[0]["slots"]["main"]["*"]
    return None


# --------------------------------------------------------------------------- #
# Parsing helpers
# --------------------------------------------------------------------------- #
def _clean(val: str) -> str:
    """Strip refs, comments, links and bold/italic markup from a param value."""
    val = re.sub(r"<ref[^>]*?/>", "", val)
    val = re.sub(r"<ref[^>]*?>.*?</ref>", "", val, flags=re.S)
    val = re.sub(r"<!--.*?-->", "", val, flags=re.S)
    val = re.sub(r"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", r"\1", val)  # [[link|text]] -> text
    val = re.sub(r"'''?", "", val)
    return val.strip()


def parse_infobox(wikitext: str) -> dict:
    """Return the Infobox boxer params as a {lowercased_key: raw_value} dict."""
    if HAVE_MWPARSER:
        code = mwparserfromhell.parse(wikitext)
        for tmpl in code.filter_templates():
            name = str(tmpl.name).strip().lower()
            if name.startswith("infobox") and ("box" in name or "sportsperson" in name):
                return {
                    str(p.name).strip().lower(): str(p.value).strip()
                    for p in tmpl.params
                }
        return {}
    # Degraded fallback (brace-balanced scan) if mwparserfromhell isn't installed.
    m = re.search(r"\{\{\s*infobox boxer", wikitext, flags=re.I)
    if not m:
        return {}
    i, depth, start = m.start(), 0, m.start()
    while i < len(wikitext):
        if wikitext[i:i + 2] == "{{":
            depth += 1; i += 2; continue
        if wikitext[i:i + 2] == "}}":
            depth -= 1; i += 2
            if depth == 0:
                break
            continue
        i += 1
    block = wikitext[start:i]
    params: dict = {}
    for part in re.split(r"\n\s*\|", block):
        if "=" in part:
            k, _, v = part.partition("=")
            params[k.strip().lower()] = v.strip()
    return params


def _int(params: dict, *keys) -> Optional[int]:
    for k in keys:
        if k in params and params[k].strip():
            digits = re.sub(r"[^\d]", "", _clean(params[k]))
            if digits:
                return int(digits)
    return None


def _parse_length_inches(raw: str) -> Optional[int]:
    if not raw:
        return None
    raw = raw.replace("&nbsp;", " ")
    m = re.search(r"ft\s*=\s*(\d+).*?in\s*=\s*(\d+)", raw, flags=re.S)
    if m:
        return int(m.group(1)) * 12 + int(m.group(2))
    m = re.search(r"(\d+)\s*ft\s*(\d+)", raw)
    if m:
        return int(m.group(1)) * 12 + int(m.group(2))
    m = re.search(r"\{\{convert\|(\d+(?:\.\d+)?)\|in", raw)
    if m:
        return round(float(m.group(1)))
    m = re.search(r"\{\{convert\|(\d+(?:\.\d+)?)\|cm", raw) or re.search(r"(\d+(?:\.\d+)?)\s*cm", raw)
    if m:
        return round(float(m.group(1)) / 2.54)
    return None


def _parse_age(raw: str) -> Optional[int]:
    m = re.search(r"birth date and age\|(\d{4})\|(\d{1,2})\|(\d{1,2})", raw)
    if not m:
        m = re.search(r"bda\|(\d{4})\|(\d{1,2})\|(\d{1,2})", raw)
    if m:
        y, mo, d = map(int, m.groups())
        today = dt.date.today()
        return today.year - y - ((today.month, today.day) < (mo, d))
    return None


def _stance(raw: str) -> Optional[str]:
    if not raw:
        return None
    v = _clean(raw).lower()
    if "southpaw" in v:
        return "Southpaw"
    if "orthodox" in v:
        return "Orthodox"
    return _clean(raw) or None


def parse_record_table(wikitext: str, n: int = 5) -> Optional[dict]:
    """
    BEST-EFFORT: derive last-N form from the 'Professional boxing record'
    wikitable. Wikipedia's record tables vary in layout, so TREAT THIS AS
    UNVERIFIED -- it's a convenience, not a source of truth. Returns None if
    it can't find a confident answer.
    """
    idx = re.search(r"==+\s*Professional (?:boxing )?record\s*==+", wikitext, flags=re.I)
    if not idx:
        return None
    tail = wikitext[idx.end():]
    table = re.search(r"\{\|.*?\|\}", tail, flags=re.S)
    if not table:
        return None
    rows = re.split(r"\n\|-", table.group(0))
    results = []  # list of (result_letter, method)
    for row in rows:
        rl = None
        if re.search(r"\{\{\s*(won|win)\b", row, flags=re.I) or re.search(r"\|\s*Win\b", row):
            rl = "W"
        elif re.search(r"\{\{\s*(lost|loss)\b", row, flags=re.I) or re.search(r"\|\s*Loss\b", row):
            rl = "L"
        elif re.search(r"\{\{\s*draw\b", row, flags=re.I) or re.search(r"\|\s*Draw\b", row):
            rl = "D"
        if rl is None:
            continue
        method = ""
        mm = re.search(r"\b(KO|TKO|UD|SD|MD|RTD|DQ|PTS)\b", row)
        if mm:
            method = mm.group(1)
        results.append((rl, method))
    if not results:
        return None
    # Wikipedia lists most-recent first; take the leading N.
    recent = results[:n]
    last5 = []
    for rl, method in recent:
        if rl == "W":
            last5.append("WKO" if method in ("KO", "TKO") else "WDEC")
        elif rl == "L":
            last5.append("LKO" if method in ("KO", "TKO") else "LDEC")
        else:
            last5.append("DRAW")
    # current streak from the most recent results
    streak, first = 0, recent[0][0]
    for rl, _ in recent:
        if rl == first:
            streak += 1
        else:
            break
    streak_type = "KO" if any(m in ("KO", "TKO") for _, m in recent[:streak]) else "Decision"
    kos_in_last_five = sum(1 for s in last5 if s == "WKO")
    return {
        "currentStreak": streak if first == "W" else 0,
        "streakType": streak_type if first == "W" else None,
        "last5": last5,
        "kosInLastFive": kos_in_last_five,
        "_unverified": True,
    }


# --------------------------------------------------------------------------- #
# Snapshot assembly
# --------------------------------------------------------------------------- #
def build_snapshot(title: str, wikitext: str, want_records: bool) -> dict:
    p = parse_infobox(wikitext)
    needs_check = []

    wins = _int(p, "wins")
    ko = _int(p, "ko")            # 'KO=' param -> wins by knockout
    losses = _int(p, "losses")
    draws = _int(p, "draws")
    total = _int(p, "total")
    no_contests = _int(p, "no_contests", "no contests")

    wins_dec = (wins - ko) if (wins is not None and ko is not None) else None
    if total is not None and None not in (wins, losses, draws):
        computed = wins + losses + draws + (no_contests or 0)
        if computed != total:
            needs_check.append(f"total ({total}) != wins+losses+draws+NC ({computed})")

    age = _parse_age(p.get("birth_date", "") + p.get("birth date", ""))
    height_in = _parse_length_inches(p.get("height", ""))
    reach_in = _parse_length_inches(p.get("reach", ""))
    stance = _stance(p.get("stance", "") or p.get("style", ""))
    weight_class = _clean(p.get("weight", "") or p.get("division", "")) or None
    nationality = _clean(p.get("nationality", "")) or None
    realname = _clean(p.get("realname", "") or p.get("real_name", "")) or None

    form = parse_record_table(wikitext, 5) if want_records else None

    snap = {
        "capturedAt": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
        "version": 1,
        "record": {
            "wins": wins,
            "losses": losses,
            "draws": draws,
            "winsKo": ko,
            "winsDec": wins_dec,
            "noContests": no_contests,
        },
        "form": form if form else {
            "currentStreak": None,
            "streakType": None,
            "last5": [],
            "kosInLastFive": None,
            "avgRoundsLastFive": None,
        },
        "physical": {
            "age": age,
            "heightInches": height_in,
            "reachInches": reach_in,
            "stance": stance,
        },
        "standing": {
            # Wikipedia infoboxes don't reliably carry sanctioning rankings.
            # Pull these from the WBC/WBO/IBF/WBA ratings pages instead.
            "titlesHeld": None,
            "rankings": {"wbc": None, "wbo": None, "ibf": None, "wba": None},
        },
        "_meta": {
            "name": title,
            "realName": realname,
            "nationality": nationality,
            "weightClassRaw": weight_class,
            "source": f"https://en.wikipedia.org/wiki/{title.replace(' ', '_')}",
            "license": "Text CC BY-SA 4.0 — attribute Wikipedia on display.",
            "needsVerification": needs_check or None,
            "formIsUnverified": bool(form),
        },
    }
    return snap


def fighter_snapshot(name: str, title: Optional[str], want_records: bool) -> Optional[dict]:
    resolved = title or search_title(name)
    if not resolved:
        sys.stderr.write(f"[warn] no Wikipedia article found for '{name}'\n")
        return None
    wikitext = fetch_wikitext(resolved)
    if not wikitext:
        sys.stderr.write(f"[warn] could not fetch wikitext for '{resolved}'\n")
        return None
    if "{{infobox boxer" not in wikitext.lower():
        sys.stderr.write(f"[warn] '{resolved}' has no boxer infobox — wrong article?\n")
    return build_snapshot(resolved, wikitext, want_records)


# --------------------------------------------------------------------------- #
# CLI
# --------------------------------------------------------------------------- #
def main() -> int:
    ap = argparse.ArgumentParser(description="Fetch boxer records from Wikipedia.")
    ap.add_argument("names", nargs="*", help="Fighter name(s) to look up.")
    ap.add_argument("--title", help="Exact Wikipedia article title (skips search).")
    ap.add_argument("--records", action="store_true",
                    help="Also derive best-effort last-5 form from the record table (UNVERIFIED).")
    ap.add_argument("--out", help="Write JSON to this file instead of stdout.")
    args = ap.parse_args()

    if not HAVE_MWPARSER:
        sys.stderr.write("[note] mwparserfromhell not installed — using degraded "
                         "parser. Run: pip install mwparserfromhell\n")

    targets = list(args.names)
    if args.title:
        targets = targets or [args.title]

    if not targets:
        ap.print_help()
        return 1

    out = []
    for i, name in enumerate(targets):
        snap = fighter_snapshot(name, args.title if len(targets) == 1 else None, args.records)
        if snap:
            out.append(snap)
        if i < len(targets) - 1:
            time.sleep(0.5)  # be polite between requests

    result = out[0] if len(out) == 1 else out
    text = json.dumps(result, indent=2, ensure_ascii=False)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            f.write(text)
        sys.stderr.write(f"[ok] wrote {args.out}\n")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
