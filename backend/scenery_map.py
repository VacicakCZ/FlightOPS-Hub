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
            "airport_name": airport["name"],
            "gsx_installed": record.get("gsx_installed", False),
        })
    return markers


def attach_airport_names(records):
    """Adds an airport_name field (None if unresolvable) to each record in
    place, for the plain list view - same lookup as build_markers, minus the
    lat/lon it doesn't need there."""
    for record in records:
        icao = icao_from_display_name(record["display_name"])
        airport = airports_data.lookup(icao) if icao else None
        record["airport_name"] = airport["name"] if airport else None
    return records
