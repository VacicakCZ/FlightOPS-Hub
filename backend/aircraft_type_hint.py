"""Best-effort aircraft model tag (A320, 737-800, CRJ900, ...) extracted from
a package's display name via regex - a pure display hint, never used for
grouping/classification (unlike the developer key, which stays the reliable
top-level group per scenery_data.py's own reasoning: the "manufacturer"
manifest field is inconsistent, so nothing load-bearing hangs off this).

Not exhaustive - covers the aircraft families that actually show up
repeatedly in the MSFS addon ecosystem. Returns None rather than guessing
when nothing recognizable is found; that's fine here since a missing badge
is low-stakes (a wrong content_type/country would not be).
"""
import re

_PATTERNS = [
    re.compile(r"\bA3(\d{2})(?:neo|ceo)?\b", re.IGNORECASE),
    re.compile(r"\bA220[-\s]?(?:100|300)?\b", re.IGNORECASE),
    re.compile(r"\b7[0-8]7(?:[-\s]?\d{1,3}[A-Z]{0,3})?\b"),
    re.compile(r"\bCRJ[-\s]?\d{3,4}\b", re.IGNORECASE),
    re.compile(r"\bERJ[-\s]?\d{3}\b", re.IGNORECASE),
    re.compile(r"\bE[-\s]?(?:170|175|190|195)\b"),
    re.compile(r"\bATR[-\s]?(?:42|72)\b", re.IGNORECASE),
    re.compile(r"\bCessna\s?(?:150|152|162|172|182|206|208|210|340|414|421)\b", re.IGNORECASE),
    re.compile(r"\bC[-\s]?(?:150|152|162|172|182|206|208|210|340|414|421)\b"),
    re.compile(r"\bTBM[-\s]?(?:700|850|900|930|940)\b", re.IGNORECASE),
    re.compile(r"\bPC[-\s]?(?:6|12|24)\b", re.IGNORECASE),
    re.compile(r"\bKing\s?Air(?:\s?\d{3})?\b", re.IGNORECASE),
    re.compile(r"\bMD[-\s]?(?:11|8[0-8])\b", re.IGNORECASE),
    re.compile(r"\bDHC[-\s]?[268]\b", re.IGNORECASE),
    re.compile(r"\bDash\s?8\b", re.IGNORECASE),
    re.compile(r"\bDC[-\s]?(?:3|6|9|10)\b", re.IGNORECASE),
    re.compile(r"\bLearjet\s?\d{2}\b", re.IGNORECASE),
    re.compile(r"\bCitation\s?\w*\b", re.IGNORECASE),
    re.compile(r"\bConcorde\b", re.IGNORECASE),
    re.compile(r"\bSpitfire\b", re.IGNORECASE),
]


def extract_type_hint(display_name):
    if not display_name:
        return None
    for pattern in _PATTERNS:
        match = pattern.search(display_name)
        if match:
            return match.group(0).strip()
    return None
