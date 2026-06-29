"""
pipeline.py  --  POC content pipeline (the plan's PipelineJob, run on demand).

Chain: discover/seed a bout -> snapshot both fighters -> register them in the
fighters DB -> build Claude prompt -> generate article (Claude API if key set).

Writes the JSON "database" the Nuxt site reads, under ../data:
    data/packages/<slug>.json   bout + both fighter snapshots + the exact prompt
    data/prompts/<slug>.txt      human-readable prompt
    data/articles/<slug>.json    the generated article
    data/fighters/<id>.json      canonical fighter record + bout backlinks

A bout's slug is <fighterAId>-vs-<fighterBId>, so the same matchup is always the
same file. `discover` skips matchups already on disk and advances to the next.

Commands
  python pipeline.py discover        # poll feeds, process the first NEW valid bout
  python pipeline.py samples [--force]  # rebuild the illustrative sample matchups
  python pipeline.py lint            # flag "AI tell" patterns across all articles
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import article as article_mod
import detect
import feeds
import fighters
import snapshots
import style_check

ROOT = Path(__file__).resolve().parent
DATA_DIR = ROOT.parent / "data"


def _maybe_generate(slug: str, bout: dict, snap_a: dict, snap_b: dict) -> bool:
    article = article_mod.generate_article(bout, snap_a, snap_b)
    if article is None:
        return False
    article.setdefault("ai_generated", True)
    article_mod.save_article(DATA_DIR, slug, article)
    return True


def _commit(snap_a: dict, snap_b: dict, *, headline: str, source: str,
            link: str, published, event_date, force: bool = False):
    """Persist a bout: fighter ids, package, fighter records, article. Returns
    (slug, created) where created is False if it already existed and was skipped."""
    fid_a = fighters.fighter_id(DATA_DIR, snap_a["_meta"]["name"], snap_a["physical"].get("dob"))
    fid_b = fighters.fighter_id(DATA_DIR, snap_b["_meta"]["name"], snap_b["physical"].get("dob"))
    slug = f"{fid_a}-vs-{fid_b}"

    if (DATA_DIR / "packages" / f"{slug}.json").exists() and not force:
        return slug, False

    bout = {
        "fighter_a": snap_a["_meta"]["name"],
        "fighter_b": snap_b["_meta"]["name"],
        "fighterAId": fid_a,
        "fighterBId": fid_b,
        "weightClass": snapshots.bout_weight_class(snap_a, snap_b),
        "eventDate": event_date,        # None / "TBC" / ISO date
        "announcedAt": published,        # RSS publish date
        "headline": headline,
        "source": source,
        "link": link,
    }
    article_mod.save_package(DATA_DIR, slug, article_mod.make_package(bout, snap_a, snap_b))
    fighters.upsert(DATA_DIR, snap_a, slug)
    fighters.upsert(DATA_DIR, snap_b, slug)
    _maybe_generate(slug, bout, snap_a, snap_b)
    return slug, True


def discover() -> int:
    print("Polling boxing RSS feeds...")
    items = feeds.poll_items()
    if not items:
        print("No feed items. Nothing to do.")
        return 0

    print(f"Classifying {len(items)} headline(s) "
          f"({'AI' if detect.ai_available() else 'regex fallback'})...")
    for item in items:
        result = detect.classify(item.headline, item.summary)
        if not result.is_announcement or not (result.fighter_a and result.fighter_b):
            continue

        # AI-confirmed announcements may enrich best-effort (sparse on missing wiki);
        # the regex fallback stays strict (both must resolve) to avoid noise.
        allow_sparse = result.ai_confirmed
        snap_a = snapshots.build_snapshot(result.fighter_a, allow_sparse=allow_sparse)
        snap_b = snapshots.build_snapshot(result.fighter_b, allow_sparse=allow_sparse)
        if snap_a is None or snap_b is None:
            print(f"  x could not resolve fighters for {result.fighter_a} vs {result.fighter_b}")
            continue
        if not allow_sparse and not (snapshots.has_record(snap_a) and snapshots.has_record(snap_b)):
            continue

        slug, created = _commit(
            snap_a, snap_b,
            headline=item.headline, source=item.source, link=item.link,
            published=item.published, event_date=result.event_date,
        )
        if not created:
            print(f"  - already have {slug}, skipping")
            continue
        kind = "generated article" if (DATA_DIR / "articles" / f"{slug}.json").exists() else "prompt saved"
        print(f"  OK NEW BOUT {slug} ({kind})")
        return 0

    print("No new boxing announcement found in the feeds.")
    return 0


def samples(force: bool = True) -> int:
    data = json.loads((ROOT / "sample_matchups.json").read_text(encoding="utf-8"))
    pairs = data["matchups"]
    print(f"Building {len(pairs)} illustrative sample matchup(s)...\n")
    need_authoring = []
    for a, b in pairs:
        snap_a, snap_b = snapshots.build_snapshot(a), snapshots.build_snapshot(b)
        if not (snapshots.has_record(snap_a) and snapshots.has_record(snap_b)):
            print(f"  x skipped {a} vs {b}, one did not resolve to a boxer")
            continue
        slug, _ = _commit(
            snap_a, snap_b,
            headline=f"{snap_a['_meta']['name']} vs {snap_b['_meta']['name']}",
            source="Sample matchup (illustrative, not an announced fight)",
            link="", published=None, event_date=None, force=force,
        )
        has_article = (DATA_DIR / "articles" / f"{slug}.json").exists()
        if not has_article:
            need_authoring.append(slug)
        print(f"  OK {slug}  [{'has article' if has_article else 'prompt only'}]")
    if need_authoring:
        print(f"\n{len(need_authoring)} need article JSON (no API key). Supply each "
              f"data/articles/<slug>.json:")
        for s in need_authoring:
            print(f"  · {s}")
    return 0


def lint() -> int:
    art_dir = DATA_DIR / "articles"
    slugs = [p.stem for p in sorted(art_dir.glob("*.json"))] if art_dir.exists() else []
    if not slugs:
        print("No articles to lint.")
        return 0
    hard_total = 0
    for slug in slugs:
        hits = style_check.lint_article(article_mod.load_article(DATA_DIR, slug))
        hard = sum(n for _, n, is_hard in hits if is_hard)
        hard_total += hard
        flag = "x" if hard else (":" if hits else "OK")
        print(f"  {flag} {slug}")
        for label, n, is_hard in hits:
            print(f"      {'[hard] ' if is_hard else '       '}{label}: {n}")
    print(f"\nHard violations (should be 0): {hard_total}")
    return 1 if hard_total else 0


def main(argv: list[str]) -> int:
    cmd = argv[1] if len(argv) > 1 else "discover"
    if cmd == "discover":
        return discover()
    if cmd == "samples":
        return samples(force="--force" in argv or True)
    if cmd == "lint":
        return lint()
    print(__doc__)
    return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
