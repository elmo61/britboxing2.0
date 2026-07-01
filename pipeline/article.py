"""
article.py  --  POC equivalent of the plan's ArticleGenerationService.

Builds the Claude prompt dynamically from the two JSONB snapshots, following the
"Article Generation Prompt Structure" in BOXING_SITE_PLAN.md. The prompt is the
real one production will use; generate_article() will call the Claude API the
moment an ANTHROPIC_API_KEY is present.

For this proof of concept the key is not set, so the pipeline saves the assembled
prompt + bout package to disk and the article JSON is supplied out-of-band
(written this once by Claude Code), then fed to render.py. Wiring the live API
later is a one-line path: set the key.
"""

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Optional

MODEL = "claude-sonnet-5"  # per the plan's tech-stack table

SYSTEM_PROMPT = (
    "You are a British boxing journalist writing fight-preview articles for "
    "BritBoxing (britboxing.co.uk). Tone: UK broadsheet boxing desk — factual, "
    "measured, no hype clichés ('all-action war', 'fireworks'). "
    "PURPOSE: help the reader understand the two boxers and how the fight shapes "
    "up. This is editorial about the fighters, NOT a betting tip — do not discuss "
    "odds, who is the favourite, or give a win/method prediction. "
    "WRITE FOR VARIETY — this is important. Every preview must feel written from "
    "scratch for THIS fight, not poured into a template. Before writing, find the "
    "single most interesting angle this matchup's data suggests — a huge "
    "experience gap, two unbeaten punchers, a reach mismatch, a veteran slowing "
    "down, a prospect's step up — and build the piece around THAT. Vary your "
    "opening every time (never default to 'When a fight involves…'); vary the "
    "order you introduce the fighters; vary your section headings and how many "
    "sections you use; vary article length to fit how much the data supports. The "
    "structure below is a checklist of what to COVER, not a running order to "
    "follow. Two previews placed side by side should read as clearly different pieces. "
    "EMPHASIS: where one fighter is far more famous, weight the piece toward the "
    "lesser-known opponent — readers arrive knowing the big name. Where both are "
    "comparably known, treat them evenly. "
    "DATA DISCIPLINE: use ONLY the statistics provided in the prompt. Never invent "
    "records, rankings, dates, venues, quotes or biographical detail. If a stat is "
    "null/unknown, simply leave it out — never state that a figure is missing or "
    "unavailable. Use a stat only when it is genuinely informative (e.g. a clear "
    "reach or experience advantage); skip differences that are marginal. Form data "
    "marked unverified may be described softly ('recent outings suggest') or omitted. "
    "Do not refer to yourself, to AI, or to how the article was produced. "
    "HOUSE STYLE — write like a person, not a model. Hard rules: NEVER use an em "
    "dash (—) or en dash (–); use commas, full stops or brackets and recast the "
    "sentence. Use straight quotes and apostrophes, not curly ones. Avoid these "
    "tell-tale constructions: 'not just X but Y' and 'it's not X, it's Y'; "
    "'not a … so much as …'; semicolon antithesis ('one has X; the other has Y'); "
    "rule-of-three lists for rhythm; and ending on a rhetorical 'the question is "
    "whether…'. Vary how each article closes. Avoid filler/marketing words "
    "(testament, stark reminder, delve, tapestry, landscape, realm, navigate, "
    "underscore, boasts, showcase, leverage, crucial, pivotal, robust) and hedging "
    "openers (That said, Ultimately, It's worth noting, When it comes to, In a "
    "world where). Vary sentence length; let some sentences be short and plain. "
    "Return ONLY a JSON object with keys: title, slug, body, summary, tags. "
    "'body' is HTML (<p>, <h2>, <ul>) — no <html>/<head>. 'tags' is an array of strings."
)


def build_prompt(bout: dict, snap_a: dict, snap_b: dict) -> str:
    """Assemble the user prompt from bout context + both snapshots."""
    a_name = snap_a["_meta"]["name"]
    b_name = snap_b["_meta"]["name"]
    return f"""Write a fight-preview article for the following bout.

## Bout
- Matchup: {a_name} vs {b_name}
- Announced via: {bout.get('source')} — headline: "{bout.get('headline')}"
- Source link: {bout.get('link')}
- Event date: {bout.get('eventDate') or 'to be confirmed'}
- Weight class (heaviest division both fighters share): {bout.get('weightClass') or 'unknown'}

## Fighter A — {a_name}
{json.dumps(snap_a, indent=2, ensure_ascii=False)}

## Fighter B — {b_name}
{json.dumps(snap_b, indent=2, ensure_ascii=False)}

## What to cover (a checklist, NOT a running order — arrange it your own way for this fight)
- A sense of each fighter: record, how they win (KO vs decision), recent outings.
- The genuinely informative contrasts (a clear experience, finishing-rate, reach
  or age gap). Skip stats that are missing or marginal.
- The stylistic questions the numbers raise — what each fighter must do.
Lead with whatever is most interesting about THIS matchup; vary your structure,
headings and opening from other previews. No odds, no favourite, no prediction.

Remember: invent nothing; use only the JSON above. Omit anything you don't have
data for — never mention that a figure is missing. Return the JSON object only.
"""


def generate_article(bout: dict, snap_a: dict, snap_b: dict) -> Optional[dict]:
    """Call the Claude API if a key is configured; else return None so the
    caller can fall back to the save-prompt-for-review path."""
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        return None

    import anthropic  # imported lazily so the POC runs without the key

    client = anthropic.Anthropic(api_key=api_key)
    msg = client.messages.create(
        model=MODEL,
        max_tokens=2000,
        system=SYSTEM_PROMPT,
        messages=[{"role": "user", "content": build_prompt(bout, snap_a, snap_b)}],
    )
    text = msg.content[0].text.strip()
    text = text.removeprefix("```json").removeprefix("```").removesuffix("```").strip()
    return json.loads(text)


# --------------------------------------------------------------------------- #
# IO helpers — per-slug layout so many articles coexist:
#   <out>/packages/<slug>.json   bout + both snapshots + exact prompt
#   <out>/prompts/<slug>.txt      human-readable prompt
#   <out>/articles/<slug>.json    the generated article
#   <out>/<slug>.html             the rendered page
# --------------------------------------------------------------------------- #
def make_package(bout: dict, snap_a: dict, snap_b: dict) -> dict:
    return {
        "bout": bout,
        "fighter_a_snapshot": snap_a,
        "fighter_b_snapshot": snap_b,
        "system_prompt": SYSTEM_PROMPT,
        "user_prompt": build_prompt(bout, snap_a, snap_b),
        "model": MODEL,
    }


def save_package(out_dir: Path, slug: str, package: dict) -> Path:
    (out_dir / "packages").mkdir(parents=True, exist_ok=True)
    (out_dir / "prompts").mkdir(parents=True, exist_ok=True)
    pkg_path = out_dir / "packages" / f"{slug}.json"
    pkg_path.write_text(json.dumps(package, indent=2, ensure_ascii=False), encoding="utf-8")
    (out_dir / "prompts" / f"{slug}.txt").write_text(
        package["system_prompt"] + "\n\n---\n\n" + package["user_prompt"], encoding="utf-8"
    )
    return pkg_path


def load_package(out_dir: Path, slug: str) -> dict:
    return json.loads((out_dir / "packages" / f"{slug}.json").read_text(encoding="utf-8"))


def load_article(out_dir: Path, slug: str) -> dict:
    return json.loads((out_dir / "articles" / f"{slug}.json").read_text(encoding="utf-8"))


def save_article(out_dir: Path, slug: str, article: dict) -> Path:
    (out_dir / "articles").mkdir(parents=True, exist_ok=True)
    path = out_dir / "articles" / f"{slug}.json"
    path.write_text(json.dumps(article, indent=2, ensure_ascii=False), encoding="utf-8")
    return path


def list_ready_slugs(out_dir: Path) -> list[str]:
    """Slugs that have BOTH a package and an article — ready to render."""
    pkg_dir, art_dir = out_dir / "packages", out_dir / "articles"
    if not pkg_dir.exists():
        return []
    out = []
    for p in sorted(pkg_dir.glob("*.json")):
        if (art_dir / p.name).exists():
            out.append(p.stem)
    return out
