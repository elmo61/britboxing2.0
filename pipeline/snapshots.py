"""
snapshots.py  --  POC equivalent of the plan's FighterEnrichmentService.

Reuses the proven low-level helpers from the existing britboxing_wikifetch.py
prototype (search_title / fetch_wikitext / parse_infobox / _clean / _int /
_stance) but owns the field-level parsing, so this module is the single,
improved source of enrichment logic to port into FighterEnrichmentService.cs.

Improvements over the prototype's parsers (all were returning null/garbage on
real articles):

  * AGE  -- case-insensitive "{{Birth date and age|...}}" and tolerant of the
           |df=y| flag appearing before OR after the Y|M|D digits.
  * REACH/HEIGHT -- handles bare inches ("72 in"), mixed fractions ("70+1/2 in")
           and decimals ("5 ft 7.5 in"), in addition to ft/in, {{convert}} and cm.
  * WEIGHT -- extracts the {{plainlist|*[[Welterweight]]...}} into a clean array
           of divisions, plus a heaviest-shared helper to pick the bout division.
  * FORM   -- scans every wikitable under "Professional record" and keeps the one
           with the most result rows; matches the modern "{{yes2}}Win"/"{{no2}}Loss"
           cell format the prototype missed. Still flagged _unverified.
"""

from __future__ import annotations

import datetime as dt
import re
import sys
from pathlib import Path
from typing import Optional

# Reuse the prototype's low-level helpers (one directory up).
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import britboxing_wikifetch as wf  # noqa: E402


# --------------------------------------------------------------------------- #
# Numeric helpers
# --------------------------------------------------------------------------- #
def _fraction(value: str) -> float:
    """'7' -> 7.0, '7.5' -> 7.5, '7+1/2' -> 7.5."""
    value = value.strip()
    m = re.match(r"(\d+)\+(\d+)/(\d+)$", value)
    if m:
        return int(m.group(1)) + int(m.group(2)) / int(m.group(3))
    return float(value)


# --------------------------------------------------------------------------- #
# Field parsers
# --------------------------------------------------------------------------- #
def parse_age(raw: str, today: Optional[dt.date] = None) -> Optional[int]:
    """Age from a {{Birth date and age|Y|M|D}} / {{bda|...}} template.
    Tolerant of case and of named flags (df=y) anywhere in the args."""
    today = today or dt.date.today()
    m = re.search(r"\b(?:birth date and age|bda|birth year and age)\b([^}]*)", raw, re.I)
    if not m:
        return None
    nums = [int(x) for x in re.split(r"\|", m.group(1)) if x.strip().isdigit()]
    if len(nums) >= 3:
        y, mo, d = nums[0], nums[1], nums[2]
        return today.year - y - ((today.month, today.day) < (mo, d))
    if nums:
        return today.year - nums[0]
    return None


def parse_dob(raw: str) -> Optional[str]:
    """Birth date as ISO 'YYYY-MM-DD' from a {{Birth date and age|Y|M|D}} template,
    or None. DOB is stored for stable fighter IDs (more durable than age)."""
    m = re.search(r"\b(?:birth date and age|bda|birth date)\b([^}]*)", raw, re.I)
    if not m:
        return None
    nums = [int(x) for x in re.split(r"\|", m.group(1)) if x.strip().isdigit()]
    if len(nums) >= 3:
        y, mo, d = nums[0], nums[1], nums[2]
        try:
            return dt.date(y, mo, d).isoformat()
        except ValueError:
            return None
    return None


def parse_length_inches(raw: str) -> Optional[int]:
    """Length in whole inches from the many formats Wikipedia uses."""
    if not raw:
        return None
    raw = re.sub(r"<ref.*?(/>|</ref>)", "", raw, flags=re.S).replace("&nbsp;", " ")
    # 5 ft 7.5 in  /  5 ft 8+1/2 in
    m = re.search(r"(\d+)\s*ft\s*(\d+(?:\.\d+)?(?:\+\d+/\d+)?)\s*in", raw, re.I)
    if m:
        return round(int(m.group(1)) * 12 + _fraction(m.group(2)))
    # {{convert|5|ft|8|in}}
    m = re.search(r"\{\{convert\|(\d+)\|ft\|(\d+)\|in", raw, re.I)
    if m:
        return int(m.group(1)) * 12 + int(m.group(2))
    # bare inches: 72 in  /  70+1/2 in
    m = re.search(r"(\d+(?:\.\d+)?(?:\+\d+/\d+)?)\s*in\b", raw, re.I)
    if m:
        return round(_fraction(m.group(1)))
    # {{convert|180|cm}}  /  180 cm
    m = re.search(r"\{\{convert\|(\d+(?:\.\d+)?)\|cm", raw) or re.search(r"(\d+(?:\.\d+)?)\s*cm", raw)
    if m:
        return round(float(m.group(1)) / 2.54)
    return None


def parse_weight_classes(raw: str) -> Optional[list[str]]:
    """The list of divisions from a {{plainlist|*[[X]]*[[Y]]}} weight/division param."""
    if not raw:
        return None
    links = re.findall(r"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]", raw)
    if links:
        return [wf._clean(l) for l in links]
    parts = [p.strip() for p in re.split(r"[\*\n]+", raw)
             if p.strip() and "plainlist" not in p.lower()]
    return [wf._clean(p) for p in parts] or None


# Divisions light -> heavy; synonyms normalise to one key for comparison.
_DIVISION_ORDER = [
    "minimumweight", "light flyweight", "flyweight", "super flyweight",
    "bantamweight", "super bantamweight", "featherweight", "super featherweight",
    "lightweight", "super lightweight", "welterweight", "super welterweight",
    "middleweight", "super middleweight", "light heavyweight", "cruiserweight",
    "heavyweight",
]
_DIVISION_SYNONYMS = {
    "strawweight": "minimumweight", "junior flyweight": "light flyweight",
    "junior bantamweight": "super flyweight", "junior featherweight": "super bantamweight",
    "junior lightweight": "super featherweight",
    "junior welterweight": "super lightweight", "light welterweight": "super lightweight",
    "junior middleweight": "super welterweight", "light middleweight": "super welterweight",
}


def _normalise_division(name: str) -> Optional[str]:
    key = name.lower()
    key = re.sub(r"\([^)]*\)", " ", key)   # drop "(boxing)" etc.
    key = key.replace("-", " ")            # "light-heavyweight" -> "light heavyweight"
    key = re.sub(r"\s+", " ", key).strip()
    key = _DIVISION_SYNONYMS.get(key, key)
    return key if key in _DIVISION_ORDER else None


def bout_weight_class(snap_a: dict, snap_b: dict) -> Optional[str]:
    """Heaviest division both fighters share — the likely division for the bout."""
    a = {_normalise_division(d) for d in (snap_a["_meta"].get("weightClasses") or [])}
    b = {_normalise_division(d) for d in (snap_b["_meta"].get("weightClasses") or [])}
    shared = [d for d in (a & b) if d]
    if not shared:
        return None
    heaviest = max(shared, key=_DIVISION_ORDER.index)
    return heaviest.title().replace("Super ", "Super ")  # title-case display


# --------------------------------------------------------------------------- #
# Form (recent record) -- still best-effort / unverified
# --------------------------------------------------------------------------- #
_RESULT_RE = re.compile(r"\b(Win|Loss|Draw|NC)\b")  # case-sensitive: avoids notes' "Won"/"Lost"
_METHOD_RE = re.compile(r"\b(KO|TKO|UD|SD|MD|RTD|DQ|PTS)\b")
_RESULT_LETTER = {"Win": "W", "Loss": "L", "Draw": "D", "NC": "N"}


def parse_recent_form(wikitext: str, n: int = 5) -> Optional[dict]:
    heading = re.search(r"==+\s*Professional (?:boxing )?record\s*==+", wikitext, flags=re.I)
    if not heading:
        return None
    tail = wikitext[heading.end():]
    best: list[tuple[str, str]] = []
    for table in re.findall(r"\{\|.*?\|\}", tail, flags=re.S):
        rows: list[tuple[str, str]] = []
        for row in re.split(r"\n\|-", table):
            rm = _RESULT_RE.search(row)
            if not rm:
                continue
            mm = _METHOD_RE.search(row)
            rows.append((_RESULT_LETTER[rm.group(1)], mm.group(1) if mm else ""))
        if len(rows) > len(best):
            best = rows
    if not best:
        return None

    recent = best[:n]  # Wikipedia lists most-recent first
    last5 = []
    for letter, method in recent:
        if letter == "W":
            last5.append("WKO" if method in ("KO", "TKO") else "WDEC")
        elif letter == "L":
            last5.append("LKO" if method in ("KO", "TKO") else "LDEC")
        elif letter == "D":
            last5.append("DRAW")
        else:
            last5.append("NC")
    first = recent[0][0]
    streak = 0
    for letter, _ in recent:
        if letter == first:
            streak += 1
        else:
            break
    on_win_streak = first == "W"
    return {
        "currentStreak": streak if on_win_streak else 0,
        "streakType": (
            "KO" if any(m in ("KO", "TKO") for _, m in recent[:streak]) else "Decision"
        ) if on_win_streak else None,
        "last5": last5,
        "kosInLastFive": sum(1 for s in last5 if s == "WKO"),
        "_unverified": True,
    }


# --------------------------------------------------------------------------- #
# Snapshot assembly
# --------------------------------------------------------------------------- #
def _build(title: str, wikitext: str) -> dict:
    p = wf.parse_infobox(wikitext)
    needs_check = []
    # Display name without the "(boxer)" disambiguation; keep `title` for the URL.
    display_name = re.sub(r"\s*\([^)]*\)", "", title).strip()

    wins = wf._int(p, "wins")
    ko = wf._int(p, "ko")
    losses = wf._int(p, "losses")
    draws = wf._int(p, "draws")
    total = wf._int(p, "total")
    no_contests = wf._int(p, "no_contests", "no contests")
    wins_dec = (wins - ko) if (wins is not None and ko is not None) else None
    if total is not None and None not in (wins, losses, draws):
        computed = wins + losses + draws + (no_contests or 0)
        if computed != total:
            needs_check.append(f"total ({total}) != W+L+D+NC ({computed})")

    weight_classes = parse_weight_classes(p.get("weight", "") or p.get("division", ""))

    return {
        "capturedAt": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
        "version": 1,
        "record": {
            "wins": wins, "losses": losses, "draws": draws,
            "winsKo": ko, "winsDec": wins_dec, "noContests": no_contests,
        },
        "form": parse_recent_form(wikitext) or {
            "currentStreak": None, "streakType": None, "last5": [],
            "kosInLastFive": None, "avgRoundsLastFive": None,
        },
        "physical": {
            "age": parse_age(p.get("birth_date", "") + p.get("birth date", "")),
            "dob": parse_dob(p.get("birth_date", "") + p.get("birth date", "")),
            "heightInches": parse_length_inches(p.get("height", "")),
            "reachInches": parse_length_inches(p.get("reach", "")),
            "stance": wf._stance(p.get("stance", "") or p.get("style", "")),
        },
        "standing": {
            # Wikipedia infoboxes don't carry sanctioning rankings -- pull from
            # the WBC/WBO/IBF/WBA ratings pages later.
            "titlesHeld": None,
            "rankings": {"wbc": None, "wbo": None, "ibf": None, "wba": None},
        },
        "_meta": {
            "name": display_name,
            "realName": wf._clean(p.get("realname", "") or p.get("real_name", "")) or None,
            "nationality": wf._clean(p.get("nationality", "")) or None,
            "weightClasses": weight_classes,
            "hasWikipedia": True,
            "source": f"https://en.wikipedia.org/wiki/{title.replace(' ', '_')}",
            "license": "Text CC BY-SA 4.0 — attribute Wikipedia on display.",
            "needsVerification": needs_check or None,
            "formIsUnverified": True,
        },
    }


def _sparse_snapshot(name: str) -> dict:
    """A minimal record for a fighter with no usable Wikipedia article (e.g. a
    little-known opponent of a star). Stats are null; the card/article omit them.
    Flagged hasWikipedia:false so the UI and review step know it is incomplete."""
    return {
        "capturedAt": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
        "version": 1,
        "record": {"wins": None, "losses": None, "draws": None,
                   "winsKo": None, "winsDec": None, "noContests": None},
        "form": {"currentStreak": None, "streakType": None, "last5": [],
                 "kosInLastFive": None, "avgRoundsLastFive": None},
        "physical": {"age": None, "dob": None, "heightInches": None,
                     "reachInches": None, "stance": None},
        "standing": {"titlesHeld": None,
                     "rankings": {"wbc": None, "wbo": None, "ibf": None, "wba": None}},
        "_meta": {
            "name": name,
            "realName": None,
            "nationality": None,
            "weightClasses": None,
            "hasWikipedia": False,
            "source": None,
            "license": None,
            "needsVerification": ["no Wikipedia article found — stats unknown"],
            "formIsUnverified": True,
        },
    }


def build_snapshot(name: str, allow_sparse: bool = False) -> Optional[dict]:
    """Resolve a fighter name to a JSONB snapshot.

    allow_sparse=False (default, used by the regex detector): returns None when
    there is no Wikipedia article, so feed noise is rejected.
    allow_sparse=True (used once AI has confirmed a boxing announcement): returns
    a sparse name-only snapshot instead of None, so a star-vs-unknown bout can
    still be created with the data we do have."""
    title = wf.search_title(name)
    wikitext = wf.fetch_wikitext(title) if title else None
    if not wikitext:
        sys.stderr.write(f"[warn] no usable Wikipedia article for {name!r}\n")
        return _sparse_snapshot(name) if allow_sparse else None
    return _build(title, wikitext)


def has_record(snapshot: Optional[dict]) -> bool:
    """True if the snapshot carries a parseable win count (i.e. real Wikipedia
    data, not sparse/noise)."""
    return bool(snapshot) and snapshot.get("record", {}).get("wins") is not None


# Back-compat alias.
is_real_fighter = has_record


if __name__ == "__main__":
    import json
    for n in sys.argv[1:]:
        snap = build_snapshot(n)
        print(f"\n### {n}  valid={is_real_fighter(snap)}")
        if snap:
            print(json.dumps({k: snap[k] for k in ("record", "form", "physical")}, indent=2))
            print("weightClasses:", snap["_meta"]["weightClasses"])
