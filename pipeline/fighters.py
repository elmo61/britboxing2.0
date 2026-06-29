"""
fighters.py  --  the canonical fighter "table" (data/fighters/<id>.json).

Each fighter has a STABLE id derived from their name, disambiguated by birth year
when two different known fighters would collide. The record stores their latest
snapshot plus a `bouts` backlink list, which is what powers internal linking
(a fighter page lists every fight they appear in; fight pages link to both).

This is the dedup anchor too: a bout's identity is its two fighter ids, so the
same matchup always produces the same bout slug.

Note on the plan's snapshot model: the fighter record here is the LATEST view of
a boxer; the snapshot frozen inside each bout package stays fixed at announcement
time. Updating a fighter never rewrites old bout pages.
"""

from __future__ import annotations

import datetime as dt
import json
import re
import unicodedata
from pathlib import Path
from typing import Optional


def _slug(name: str) -> str:
    raw = re.sub(r"\([^)]*\)", "", name)  # drop "(boxer)" disambiguation
    norm = unicodedata.normalize("NFKD", raw).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "-", norm.lower()).strip("-")


def _fighters_dir(data_dir: Path) -> Path:
    d = data_dir / "fighters"
    d.mkdir(parents=True, exist_ok=True)
    return d


def _load(path: Path) -> Optional[dict]:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else None


def fighter_id(data_dir: Path, name: str, dob: Optional[str]) -> str:
    """Return the stable id for a fighter, allocating a disambiguated one only if
    an existing fighter of the same name has a *different* known DOB."""
    base = _slug(name)
    existing = _load(_fighters_dir(data_dir) / f"{base}.json")
    if existing and dob and existing.get("dob") and existing["dob"] != dob:
        # genuine name clash between two different people -> disambiguate by year
        return f"{base}-{dob[:4]}"
    return base


def upsert(data_dir: Path, snapshot: dict, bout_slug: str, dob: Optional[str] = None) -> str:
    """Create or update the fighter for this snapshot, append the bout backlink,
    and return the fighter id. Refreshes the stored `latest` snapshot only when we
    have real Wikipedia data (a sparse snapshot never overwrites a richer one)."""
    name = snapshot["_meta"]["name"]
    dob = dob or snapshot.get("physical", {}).get("dob")
    fid = fighter_id(data_dir, name, dob)
    path = _fighters_dir(data_dir) / f"{fid}.json"
    rec = _load(path) or {
        "id": fid, "name": name, "dob": dob,
        "wikipediaTitle": None, "hasWikipedia": False,
        "latest": None, "bouts": [],
    }

    has_wiki = bool(snapshot["_meta"].get("hasWikipedia"))
    # Keep the richest data we've seen: only overwrite `latest` with real data.
    if has_wiki or rec["latest"] is None:
        rec["latest"] = snapshot
        rec["hasWikipedia"] = rec["hasWikipedia"] or has_wiki
        if has_wiki:
            rec["wikipediaTitle"] = snapshot["_meta"].get("source", "").rsplit("/", 1)[-1] or None
    if dob and not rec.get("dob"):
        rec["dob"] = dob
    if bout_slug not in rec["bouts"]:
        rec["bouts"].append(bout_slug)
    rec["updatedAt"] = dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")

    path.write_text(json.dumps(rec, indent=2, ensure_ascii=False), encoding="utf-8")
    return fid
