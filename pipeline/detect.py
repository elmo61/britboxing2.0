"""
detect.py  --  decides whether an RSS item is a boxing fight announcement.

Uses Claude when ANTHROPIC_API_KEY is set (structured JSON output), and falls
back to the regex extractor in feeds.py when it is not. The AI path is the upgrade
the regex can't do: it confirms announcements with no literal "vs" ("Joshua and
Dubois agree terms"), extracts clean fighter names and an explicit event date, and
filters out results / rankings / retirements that aren't announcements. Because it
confirms the item really is a boxing announcement, the caller can then accept a
star-vs-unknown bout and enrich best-effort (sparse on the unknown).
"""

from __future__ import annotations

import json
import os
import sys
from dataclasses import dataclass
from typing import Optional

import feeds

# Cheap, fast model for high-volume headline classification. Bump to
# claude-sonnet-4-6 or claude-opus-4-8 for higher accuracy at higher cost.
DETECT_MODEL = "claude-haiku-4-5"

SYSTEM_PROMPT = (
    "You classify boxing news headlines for a fight-preview site. Decide whether the "
    "item announces a SPECIFIC professional boxing match between two named boxers — a "
    "bout being made, ordered, confirmed, signed, or rescheduled all count. These do "
    "NOT count: results of past fights, rankings, retirements, injuries, purse bids "
    "without a confirmed pairing, and general news. Only boxing — ignore other sports. "
    "If it is an announcement, extract the two boxers' names exactly as a person would "
    "write them (no titles or descriptors), and an explicit event date as ISO "
    "YYYY-MM-DD only when a full date is stated (otherwise null). If it is not an "
    "announcement, set isAnnouncement false and both names null."
)

SCHEMA = {
    "type": "object",
    "properties": {
        "isAnnouncement": {"type": "boolean"},
        "fighterA": {"type": ["string", "null"]},
        "fighterB": {"type": ["string", "null"]},
        "eventDate": {"type": ["string", "null"]},
    },
    "required": ["isAnnouncement", "fighterA", "fighterB", "eventDate"],
    "additionalProperties": False,
}


@dataclass
class Detection:
    is_announcement: bool
    fighter_a: Optional[str]
    fighter_b: Optional[str]
    event_date: Optional[str]
    ai_confirmed: bool   # True only when Claude confirmed it (enables sparse enrichment)


def ai_available() -> bool:
    return bool(os.environ.get("ANTHROPIC_API_KEY"))


def _classify_ai(headline: str, summary: str) -> Detection:
    import anthropic  # imported lazily so the pipeline runs without the SDK key

    client = anthropic.Anthropic()
    resp = client.messages.create(
        model=DETECT_MODEL,
        max_tokens=300,
        system=SYSTEM_PROMPT,
        output_config={"format": {"type": "json_schema", "schema": SCHEMA}},
        messages=[{"role": "user", "content": f"Headline: {headline}\n\nSummary: {summary}"}],
    )
    data = json.loads(next(b.text for b in resp.content if b.type == "text"))
    return Detection(
        is_announcement=bool(data.get("isAnnouncement")),
        fighter_a=data.get("fighterA"),
        fighter_b=data.get("fighterB"),
        event_date=data.get("eventDate"),
        ai_confirmed=True,
    )


def _classify_regex(headline: str) -> Detection:
    pair = feeds.extract_candidate(headline)
    if not pair:
        return Detection(False, None, None, None, False)
    return Detection(True, pair[0], pair[1], None, False)


def classify(headline: str, summary: str = "") -> Detection:
    """Classify one feed item. Tries Claude, falls back to regex on any error."""
    if ai_available():
        try:
            return _classify_ai(headline, summary)
        except Exception as e:  # network, schema, SDK version — degrade gracefully
            sys.stderr.write(f"[warn] AI detection failed ({e}); using regex fallback\n")
    return _classify_regex(headline)


if __name__ == "__main__":
    import feeds as _feeds
    mode = "AI" if ai_available() else "regex"
    print(f"Detection mode: {mode}\n")
    for item in _feeds.poll_items():
        r = classify(item.headline, item.summary)
        if r.is_announcement:
            print(f"[{item.source}] {r.fighter_a} vs {r.fighter_b}"
                  f"{f' ({r.event_date})' if r.event_date else ''}  <- {item.headline}")
