"""Builds marker data for the Scenery tab's map view: combines already-scanned
scenery records with coordinates from airports_data. Records without a
resolvable ICAO/coordinate (freeware landmarks, POI packs, mesh-only add-ons
etc.) are simply omitted - there's nothing to plot them at."""
from . import airports_data
from .scenery_simbrief_match import icao_from_display_name


def build_markers(records):
    markers = []
    for record in records:
        icao = icao_from_display_name(record["display_name"])
        airport = airports_data.lookup(icao) if icao else None
        if airport is None:
            continue
        markers.append({
            "folder_name": record["folder_name"],
            "display_name": record["display_name"],
            "icao": icao,
            "enabled": record["enabled"],
            "lat": airport["lat"],
            "lon": airport["lon"],
        })
    return markers
