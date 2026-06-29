"""
style_check.py  --  flags common "AI tell" patterns in article copy.

Two uses:
  * a guardrail in the human review step (run `python pipeline.py lint`);
  * a check we can assert on in future (zero em dashes, etc.).

It only REPORTS — it does not rewrite. Some hits are legitimate; the reviewer
decides. The prompt house-style block in article.py is what prevents most of
these being produced in the first place.
"""

from __future__ import annotations

import re
from html import unescape

# (label, compiled pattern, whether it's a hard "should never appear")
_PATTERNS: list[tuple[str, re.Pattern, bool]] = [
    ("em dash (—)", re.compile(r"—"), True),
    ("en dash (–)", re.compile(r"–"), True),
    ("spaced-hyphen dash ( - )", re.compile(r"\S \- \S"), True),
    ("curly quote/apostrophe", re.compile(r"[‘’“”]"), False),
    ("semicolon (antithesis)", re.compile(r";"), False),
    ("'not just X, (but) Y'", re.compile(r"\bnot just\b", re.I), False),
    ("'isn't/it's not … it's'", re.compile(r"\b(?:isn't|is not|it's not)\b[^.]{0,40}\bit'?s\b", re.I), False),
    ("'not … so much as'", re.compile(r"\bnot\b[^.]{0,30}\bso much as\b", re.I), False),
    ("rhetorical 'the question is whether'", re.compile(r"\bquestion[^.]{0,30}\bwhether\b", re.I), False),
    ("'comes down to'", re.compile(r"\bcomes down to\b", re.I), False),
    ("filler opener (That said/Ultimately/…)",
     re.compile(r"(?:^|>|\.\s)(?:That said|Ultimately|It'?s worth noting|When it comes to|In a world)\b", re.I), False),
    ("marketing word",
     re.compile(r"\b(testament|stark reminder|delve|tapestry|landscape|realm|navigate|"
                r"underscore[sd]?|boasts?|showcase[sd]?|leverage[sd]?|crucial|pivotal|robust)\b", re.I), False),
]


def strip_html(html_text: str) -> str:
    return unescape(re.sub(r"<[^>]+>", " ", html_text))


def lint_text(text: str) -> list[tuple[str, int, bool]]:
    """Return [(label, count, is_hard)] for every pattern that matched."""
    out = []
    for label, pat, hard in _PATTERNS:
        n = len(pat.findall(text))
        if n:
            out.append((label, n, hard))
    return out


def lint_article(article: dict) -> list[tuple[str, int, bool]]:
    blob = " ".join(str(article.get(k, "")) for k in ("title", "summary"))
    blob += " " + strip_html(article.get("body", ""))
    return lint_text(blob)
