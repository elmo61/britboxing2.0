"""
feeds.py  --  POC equivalent of the plan's FeedIngestionService.

Polls a collection of boxing news RSS feeds and extracts candidate bouts
("Fighter A vs Fighter B") from the headlines. This only surfaces *candidates* --
the snapshot step decides whether a candidate is a real bout by checking that
both names resolve to genuine Wikipedia boxer articles with a parseable record.
That validation gate is what lets us point this at general-sport feeds without
generating previews for "Brazil vs Japan".

Porting note: the connector patterns + extraction heuristics below map directly
onto a C# FeedIngestionService using System.ServiceModel.Syndication.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Optional

import feedparser

# Boxing news feeds. BBC + ESPN are boxing-specific and reliable.
# NOTE: the plan listed Sky as https://www.skysports.com/rss/12040 but that feed
# currently returns general Sky Sports content (football etc.), not boxing -- it
# is kept last and the boxer-validation gate filters its noise. Replace with a
# verified Sky boxing feed when one is confirmed.
FEEDS = {
    "BBC": "https://feeds.bbci.co.uk/sport/boxing/rss.xml",
    "ESPN": "https://www.espn.com/espn/rss/boxing/news",
    # Boxing-dedicated feeds (idea taken from the C# BritBoxingFeeds example) —
    # far higher signal for actual fight announcements than general-sport feeds.
    "BoxingScene": "https://www.boxingscene.com/rss",
    "WorldBoxingNews": "https://www.worldboxingnews.net/feed/",
    "Sky": "https://www.skysports.com/rss/12040",
}

# Connectors that signal "these two fighters are matched up".
# Ordered so the first match wins; longer phrases first.
_CONNECTORS = [
    r"set to face", r"set to fight", r"set to meet",
    r"to face", r"to fight", r"to meet", r"to take on", r"takes on",
    r"faces", r"clashes with", r"clash with", r"meets",
    r"vs\.?", r"\bv\b", r"versus",
]
_CONNECTOR_RE = re.compile(r"\s+(?:" + "|".join(_CONNECTORS) + r")\s+", re.I)

# Headlines that mention a matchup but where no preview should be generated.
_NEGATIVE_RE = re.compile(
    r"\b(off|cancell?ed|scrapped|postponed indefinitely|called off|faq|"
    r"rematch postponed|pulls out|withdraws)\b",
    re.I,
)

# A "name-like" run: one or more Capitalised words (handles O'Sullivan, Saint-,
# and single mononyms like "Canelo"). Possessive 's is trimmed later.
_NAME_RUN_RE = re.compile(r"[A-Z][\w.'-]*(?:\s+[A-Z][\w.'-]*)*")

# Descriptor words to drop so "unified middleweight champ Scott" -> "Scott".
_DESCRIPTORS = {
    "Report", "Exclusive", "Breaking", "Official", "Watch", "Live",
    "WBC", "WBO", "IBF", "WBA", "IBO", "Ring",
}


@dataclass
class FeedItem:
    """A raw news item. Detection (regex or AI) happens downstream in detect.py."""
    headline: str
    summary: str
    source: str
    link: str
    published: Optional[str]


def poll_items(limit_per_feed: int = 25) -> list[FeedItem]:
    """Poll every feed and return raw items in feed order (BBC, ESPN, Sky)."""
    items: list[FeedItem] = []
    for source, url in FEEDS.items():
        parsed = feedparser.parse(url)
        for entry in parsed.entries[:limit_per_feed]:
            items.append(FeedItem(
                headline=entry.get("title", ""),
                summary=entry.get("summary", entry.get("description", "")),
                source=source,
                link=entry.get("link", ""),
                published=entry.get("published", entry.get("updated")),
            ))
    return items


def _strip_possessive(name: str) -> str:
    return re.sub(r"'s\b", "", name).strip()


def _name_near_connector(side: str, take: str) -> Optional[str]:
    """Pull the fighter name from one side of the connector.

    take="last"  -> rightmost name run (left side, nearest the connector)
    take="first" -> leftmost name run (right side, nearest the connector)
    Descriptor-only leading tokens (Report:, WBC, ...) are skipped.
    """
    runs = _NAME_RUN_RE.findall(side)
    # Clean each run: drop descriptor tokens, trim possessives.
    cleaned = []
    for run in runs:
        toks = [t for t in run.split() if t not in _DESCRIPTORS]
        if toks:
            cleaned.append(_strip_possessive(" ".join(toks)))
    cleaned = [c for c in cleaned if c]
    if not cleaned:
        return None
    chosen = cleaned[-1] if take == "last" else cleaned[0]
    # Keep at most the last 3 tokens — boxing names are rarely longer and this
    # drops trailing clutter like "title bout".
    return " ".join(chosen.split()[-3:]) if take == "last" else " ".join(chosen.split()[:3])


def extract_candidate(headline: str) -> Optional[tuple[str, str]]:
    """Return (fighter_a, fighter_b) from a headline, or None if not a matchup."""
    if _NEGATIVE_RE.search(headline):
        return None
    m = _CONNECTOR_RE.search(headline)
    if not m:
        return None
    left, right = headline[: m.start()], headline[m.end():]
    a = _name_near_connector(left, take="last")
    b = _name_near_connector(right, take="first")
    if not a or not b or a.lower() == b.lower():
        return None
    return a, b


if __name__ == "__main__":
    for it in poll_items():
        pair = extract_candidate(it.headline)
        mark = f"  -> {pair[0]} vs {pair[1]}" if pair else ""
        print(f"[{it.source}] {it.headline}{mark}")
