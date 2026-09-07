"""Applies user-supplied corrections on top of scenery_data.scan_aircraft_and_liveries'
best-effort classification (manifest content_type for aircraft-vs-livery,
base_container matching for a livery's parent aircraft) - both are
heuristics and can be wrong (a payware "livery" manifested as AIRCRAFT, or
a livery whose base_container doesn't resolve), so the user can override
either per package. Pure logic, no UI/Tk dependency, same spirit as
scenery_data.py itself (which stays unmodified).
"""


def apply_overrides(aircraft, liveries, type_overrides, parent_overrides):
    """type_overrides: {folder_name: "aircraft"|"livery"} - reclassifies a
    package regardless of what its own manifest said.
    parent_overrides: {folder_name: parent_folder_name} where "" means
    "explicitly unassigned" and an absent key means "use the heuristic
    (base_container) result".

    Returns (aircraft, liveries) - new lists, inputs are not mutated.
    """
    combined = {r["folder_name"]: r for r in aircraft + liveries}
    original_aircraft_folders = {r["folder_name"] for r in aircraft}

    def is_aircraft(folder_name):
        override = type_overrides.get(folder_name)
        if override == "aircraft":
            return True
        if override == "livery":
            return False
        return folder_name in original_aircraft_folders

    final_aircraft_folders = {fn for fn in combined if is_aircraft(fn)}

    result_aircraft = []
    result_liveries = []
    for folder_name, record in combined.items():
        record = dict(record)
        if is_aircraft(folder_name):
            record.pop("parent_aircraft", None)
            result_aircraft.append(record)
        else:
            if folder_name in parent_overrides:
                parent = parent_overrides[folder_name] or None
            else:
                parent = record.get("parent_aircraft")
            if parent not in final_aircraft_folders:
                parent = None
            record["parent_aircraft"] = parent
            result_liveries.append(record)

    def _sort_key(r):
        return (r["developer"] or "￿", r["display_name"].lower())

    result_aircraft.sort(key=_sort_key)
    result_liveries.sort(key=_sort_key)
    return result_aircraft, result_liveries
