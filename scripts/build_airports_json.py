"""One-off/offline generator for data/airports.json from the OurAirports CSV
export (https://davidmegginson.github.io/ourairports-data/airports.csv).

Not run at app startup or packaged into the exe - this is a build-time step
whose output (data/airports.json) is what actually ships. Re-run manually
if the airport dataset ever needs refreshing.
"""
import csv
import json
import os
import re
import sys

ICAO_RE = re.compile(r"^[A-Z]{4}$")


def build(csv_path, out_path):
    airports = {}
    with open(csv_path, "r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            code = (row.get("icao_code") or "").strip().upper()
            if not ICAO_RE.match(code):
                code = (row.get("ident") or "").strip().upper()
            if not ICAO_RE.match(code):
                continue
            try:
                lat = round(float(row["latitude_deg"]), 4)
                lon = round(float(row["longitude_deg"]), 4)
            except (KeyError, ValueError):
                continue
            name = (row.get("name") or "").strip()
            municipality = (row.get("municipality") or "").strip()
            # Prefer real airports over duplicate heliport/balloonport entries
            # that happen to share a 4-letter code collision (rare but seen).
            existing = airports.get(code)
            airport_type = row.get("type") or ""
            if existing and existing.get("_rank", 0) >= _rank(airport_type):
                continue
            airports[code] = {"lat": lat, "lon": lon, "name": name, "municipality": municipality, "_rank": _rank(airport_type)}

    for entry in airports.values():
        entry.pop("_rank", None)

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(airports, f, ensure_ascii=False, separators=(",", ":"))

    return len(airports)


_TYPE_RANK = {
    "large_airport": 4,
    "medium_airport": 3,
    "small_airport": 2,
    "seaplane_base": 1,
    "heliport": 0,
    "closed": 0,
    "balloonport": 0,
}


def _rank(airport_type):
    return _TYPE_RANK.get(airport_type, 0)


if __name__ == "__main__":
    csv_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ.get("TEMP", "."), "airports_raw.csv")
    out_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data", "airports.json")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    count = build(csv_path, out_path)
    print(f"Wrote {count} airports to {out_path}")
