"""
supabase_db.py  --  write pipeline output into Supabase (Postgres).

Writes bypass the public read-only RLS, so they use the SECRET key (service
role), never the publishable one. Credentials are read from environment /
a gitignored .env at the repo root:

    SUPABASE_URL=https://<ref>.supabase.co
    SUPABASE_SECRET_KEY=sb_secret_...        # Project Settings -> API -> secret key

If the secret key is absent, db writes are skipped (the pipeline still writes
the JSON files, which stay the source of truth). Rows mirror db/schema.sql and
upsert via PostgREST (Prefer: resolution=merge-duplicates), the REST equivalent
of the ON CONFLICT in db/seed.sql.
"""

from __future__ import annotations

import json
import os
import re
from pathlib import Path
from typing import Optional

import requests

_ROOT = Path(__file__).resolve().parent.parent
_ISO_DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def _load_dotenv() -> None:
    for env_path in (_ROOT / ".env", Path(__file__).resolve().parent / ".env"):
        if not env_path.exists():
            continue
        for line in env_path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, _, v = line.partition("=")
            os.environ.setdefault(k.strip(), v.strip())


_load_dotenv()
URL = os.environ.get("SUPABASE_URL", "https://sgpjwpnbmpepxqjhzqpo.supabase.co").rstrip("/")
SECRET = os.environ.get("SUPABASE_SECRET_KEY")

_SESSION = requests.Session()


def enabled() -> bool:
    return bool(URL and SECRET)


def _iso_date(v) -> Optional[str]:
    return v if (v and _ISO_DATE.match(str(v))) else None


def _drop_none(d: dict) -> dict:
    return {k: v for k, v in d.items() if v is not None}


# --------------------------------------------------------------------------- #
# Row shapers (mirror db/schema.sql columns)
# --------------------------------------------------------------------------- #
def fighter_row(f: dict) -> dict:
    snap = f.get("latest") or {}
    rec, phys, meta = snap.get("record", {}), snap.get("physical", {}), snap.get("_meta", {})
    return _drop_none({
        "id": f.get("id"), "name": f.get("name"), "dob": _iso_date(f.get("dob")),
        "wikipedia_title": f.get("wikipediaTitle"), "has_wikipedia": bool(f.get("hasWikipedia")),
        "wins": rec.get("wins"), "losses": rec.get("losses"), "draws": rec.get("draws"),
        "wins_ko": rec.get("winsKo"), "wins_dec": rec.get("winsDec"), "no_contests": rec.get("noContests"),
        "age": phys.get("age"), "stance": phys.get("stance"),
        "height_inches": phys.get("heightInches"), "reach_inches": phys.get("reachInches"),
        "weight_classes": meta.get("weightClasses"), "latest_snapshot": snap,
        "updated_at": f.get("updatedAt"),
    })


def bout_row(slug: str, pkg: dict) -> dict:
    b = pkg.get("bout", {})
    prompt = {"system": pkg.get("system_prompt"), "user": pkg.get("user_prompt"), "model": pkg.get("model")}
    return _drop_none({
        "slug": slug, "fighter_a_id": b.get("fighterAId"), "fighter_b_id": b.get("fighterBId"),
        "weight_class": b.get("weightClass"), "event_date": _iso_date(b.get("eventDate")),
        "announced_at": b.get("announcedAt"), "headline": b.get("headline"),
        "source": b.get("source"), "source_url": b.get("link"),
        "fighter_a_snapshot": pkg.get("fighter_a_snapshot"),
        "fighter_b_snapshot": pkg.get("fighter_b_snapshot"), "prompt": prompt,
    })


def article_row(slug: str, art: dict) -> dict:
    return _drop_none({
        "slug": art.get("slug", slug), "title": art.get("title"), "summary": art.get("summary"),
        "body": art.get("body"), "tags": art.get("tags"), "ai_generated": bool(art.get("ai_generated", True)),
    })


# --------------------------------------------------------------------------- #
# REST upsert
# --------------------------------------------------------------------------- #
def _upsert(table: str, rows: list[dict], on_conflict: str) -> None:
    if not rows:
        return
    # PostgREST requires every row in a bulk insert to have the same keys, but
    # the row shapers drop nulls per-row. Normalise to the union of keys.
    keys = sorted(set().union(*(r.keys() for r in rows)))
    rows = [{k: r.get(k) for k in keys} for r in rows]
    r = _SESSION.post(
        f"{URL}/rest/v1/{table}",
        params={"on_conflict": on_conflict},
        headers={
            "apikey": SECRET, "Authorization": f"Bearer {SECRET}",
            "Content-Type": "application/json",
            "Prefer": "resolution=merge-duplicates,return=minimal",
        },
        data=json.dumps(rows, ensure_ascii=False).encode("utf-8"),
        timeout=30,
    )
    if r.status_code >= 300:
        raise RuntimeError(f"supabase {table} upsert failed [{r.status_code}]: {r.text[:300]}")


def _read(path: Path) -> Optional[dict]:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else None


def push_slug(data_dir: Path, slug: str) -> None:
    """Upsert one bout and its fighters + article (FK-safe order)."""
    pkg = _read(data_dir / "packages" / f"{slug}.json")
    if not pkg:
        return
    bout = pkg.get("bout", {})
    fids = [bout.get("fighterAId"), bout.get("fighterBId")]
    fighters = [fighter_row(_read(data_dir / "fighters" / f"{fid}.json"))
                for fid in fids if fid and (data_dir / "fighters" / f"{fid}.json").exists()]
    _upsert("fighters", fighters, "id")
    _upsert("bouts", [bout_row(slug, pkg)], "slug")
    art = _read(data_dir / "articles" / f"{slug}.json")
    if art:
        _upsert("articles", [article_row(slug, art)], "slug")


def push_all(data_dir: Path) -> dict:
    """Bulk-sync everything in data/ to Supabase. Returns counts."""
    fighters = [fighter_row(json.loads(p.read_text(encoding="utf-8")))
                for p in sorted((data_dir / "fighters").glob("*.json"))]
    _upsert("fighters", fighters, "id")

    bouts, slugs_with_bout = [], set()
    for p in sorted((data_dir / "packages").glob("*.json")):
        pkg = json.loads(p.read_text(encoding="utf-8"))
        if pkg.get("bout", {}).get("fighterAId") and pkg["bout"].get("fighterBId"):
            bouts.append(bout_row(p.stem, pkg))
            slugs_with_bout.add(p.stem)
    _upsert("bouts", bouts, "slug")

    articles = []
    for p in sorted((data_dir / "articles").glob("*.json")):
        if p.stem in slugs_with_bout:
            articles.append(article_row(p.stem, json.loads(p.read_text(encoding="utf-8"))))
    _upsert("articles", articles, "slug")

    return {"fighters": len(fighters), "bouts": len(bouts), "articles": len(articles)}
