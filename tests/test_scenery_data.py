import json
import os

import scenery_data


# --- ICAO/country/continent detection ---

def test_find_icao_code_from_folder_name_skips_developer_prefix():
    # The first "-"-separated segment is the developer slug and must be
    # skipped, or a developer code like "ORBX"/"FSDG" could be mistaken
    # for a real ICAO prefix.
    assert scenery_data.find_icao_code("orbx-airport-eddf-frankfurt", "") == "EDDF"


def test_find_icao_code_from_title_requires_uppercase_token():
    assert scenery_data.find_icao_code("some-folder", "Welcome to KJFK New York") == "KJFK"
    assert scenery_data.find_icao_code("some-folder", "Welcome to Jfk New York") is None


def test_find_icao_code_returns_none_when_nothing_recognizable():
    assert scenery_data.find_icao_code("some-random-package", "Just A Title") is None


def test_categorize_scenery_uses_icao_prefix_when_available():
    country, continent = scenery_data.categorize_scenery("fsdg-airport-lkpr-prague", "")
    assert country == "Czech Republic"
    assert continent == "europe"


def test_categorize_scenery_falls_back_to_keyword_match():
    country, continent = scenery_data.categorize_scenery("some-generic-package", "Tokyo Japan City Scenery")
    assert country == "Japan"
    assert continent == "asia"


def test_categorize_scenery_unknown_defaults_to_other():
    country, continent = scenery_data.categorize_scenery("totally-unrecognizable", "Nothing Here")
    assert country is None
    assert continent == "other"


# --- developer key heuristic (shared with gsx_profiles.py's own copy) ---

def test_developer_key_takes_first_segment():
    assert scenery_data._developer_key("fnx-aircraft-320") == "fnx"
    assert scenery_data._developer_key("bksq_a339x") == "bksq"


def test_prettify_dev_key_capitalizes_first_letter_only():
    assert scenery_data._prettify_dev_key("aerosoft") == "Aerosoft"
    assert scenery_data._prettify_dev_key("") == ""


# --- resolve_disabled_locations ---

def test_resolve_disabled_locations_creates_default_sibling_folder(tmp_path):
    community = tmp_path / "Community"
    community.mkdir()

    locations = scenery_data.resolve_disabled_locations(str(community), "")

    assert len(locations) == 1
    assert locations[0] == scenery_data.get_default_disabled_path(str(community))
    assert (tmp_path / "Community_disabled_by_FlightOpsHub").is_dir()


def test_resolve_disabled_locations_prefers_custom_path_and_keeps_old_default_visible(tmp_path):
    community = tmp_path / "Community"
    community.mkdir()
    default_disabled = tmp_path / "Community_disabled_by_FlightOpsHub"
    default_disabled.mkdir()
    custom = tmp_path / "MyDisabledStuff"

    locations = scenery_data.resolve_disabled_locations(str(community), str(custom))

    assert locations[0] == str(custom)
    assert custom.is_dir()
    assert str(default_disabled) in locations


# --- scan_scenery_packages (synthetic tmp-dir fixtures, never real MSFS data) ---

def _make_scenery_package(base_dir, folder_name, title, content_type="SCENERY"):
    pkg_dir = base_dir / folder_name
    pkg_dir.mkdir(parents=True)
    manifest = {"content_type": content_type, "title": title}
    (pkg_dir / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")


def test_scan_scenery_packages_reads_enabled_and_disabled_locations(tmp_path):
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    community.mkdir()
    disabled.mkdir()

    _make_scenery_package(community, "orbx-airport-eddf-frankfurt", "Frankfurt Airport")
    _make_scenery_package(disabled, "fsdg-airport-lkpr-prague", "Prague Airport")

    records = scenery_data.scan_scenery_packages(str(community), [str(disabled)])

    by_folder = {r["folder_name"]: r for r in records}
    assert by_folder["orbx-airport-eddf-frankfurt"]["enabled"] is True
    assert by_folder["fsdg-airport-lkpr-prague"]["enabled"] is False


def test_scan_scenery_packages_ignores_non_scenery_content_type(tmp_path):
    community = tmp_path / "Community"
    community.mkdir()
    _make_scenery_package(community, "some-aircraft", "Some Aircraft", content_type="SIMOBJECT")

    records = scenery_data.scan_scenery_packages(str(community), [])

    assert records == []


def test_scan_scenery_packages_ignores_folders_without_manifest(tmp_path):
    community = tmp_path / "Community"
    community.mkdir()
    (community / "not-a-package").mkdir()

    records = scenery_data.scan_scenery_packages(str(community), [])

    assert records == []


def test_scan_scenery_packages_missing_community_path_returns_empty(tmp_path):
    assert scenery_data.scan_scenery_packages(str(tmp_path / "nope"), []) == []


def test_scan_scenery_packages_community_wins_on_name_conflict(tmp_path):
    # Should never legitimately happen (a folder can't be enabled and
    # disabled at once), but the documented tie-break is "enabled" wins,
    # so verify that stays true rather than accidentally regressing.
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    community.mkdir()
    disabled.mkdir()
    _make_scenery_package(community, "same-name-airport", "Same Name")
    _make_scenery_package(disabled, "same-name-airport", "Same Name")

    records = scenery_data.scan_scenery_packages(str(community), [str(disabled)])

    assert len(records) == 1
    assert records[0]["enabled"] is True


# --- apply_package_changes (move logic, synthetic tmp dirs) ---

def test_apply_package_changes_moves_enabled_package_to_disabled(tmp_path):
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    community.mkdir()
    disabled.mkdir()
    _make_scenery_package(community, "orbx-airport-eddf-frankfurt", "Frankfurt Airport")

    results = scenery_data.apply_package_changes(
        str(community), [str(disabled)], {"orbx-airport-eddf-frankfurt": False}
    )

    assert results == [("orbx-airport-eddf-frankfurt", True, None)]
    assert not (community / "orbx-airport-eddf-frankfurt").exists()
    assert (disabled / "orbx-airport-eddf-frankfurt").is_dir()


def test_apply_package_changes_moves_disabled_package_back_to_community(tmp_path):
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    community.mkdir()
    disabled.mkdir()
    _make_scenery_package(disabled, "orbx-airport-eddf-frankfurt", "Frankfurt Airport")

    results = scenery_data.apply_package_changes(
        str(community), [str(disabled)], {"orbx-airport-eddf-frankfurt": True}
    )

    assert results == [("orbx-airport-eddf-frankfurt", True, None)]
    assert (community / "orbx-airport-eddf-frankfurt").is_dir()
    assert not (disabled / "orbx-airport-eddf-frankfurt").exists()


def test_apply_package_changes_is_noop_when_already_in_desired_state(tmp_path):
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    community.mkdir()
    disabled.mkdir()
    _make_scenery_package(community, "already-enabled", "Already Enabled")

    results = scenery_data.apply_package_changes(
        str(community), [str(disabled)], {"already-enabled": True}
    )

    assert results == []
    assert (community / "already-enabled").is_dir()


def test_apply_package_changes_reports_missing_source(tmp_path):
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    community.mkdir()
    disabled.mkdir()

    results = scenery_data.apply_package_changes(
        str(community), [str(disabled)], {"never-existed": False}
    )

    assert results == [("never-existed", False, "source not found")]


def test_apply_package_changes_never_overwrites_existing_destination(tmp_path, monkeypatch):
    # Normally a destination that already exists is always caught earlier
    # (currently_in_community / current_disabled_path), so the in-loop
    # "destination already exists" guard only fires if the filesystem
    # changes between that initial scan and the actual move (e.g. a
    # concurrent apply) - simulate that race by hiding the destination from
    # the initial scan only, then verify the guard still refuses to move
    # into it or delete either copy.
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    community.mkdir()
    disabled.mkdir()
    _make_scenery_package(community, "dup-package", "Dup")
    _make_scenery_package(disabled, "dup-package", "Dup (stale copy)")

    real_isdir = os.path.isdir
    dst_normalized = os.path.normpath(str(disabled / "dup-package"))

    def fake_isdir(path):
        if os.path.normpath(path) == dst_normalized:
            return False
        return real_isdir(path)

    monkeypatch.setattr(os.path, "isdir", fake_isdir)

    results = scenery_data.apply_package_changes(
        str(community), [str(disabled)], {"dup-package": False}
    )

    assert results == [("dup-package", False, "destination already exists")]
    # Neither copy should have been touched/deleted.
    assert (community / "dup-package").is_dir()


# --- scan_aircraft_and_liveries: developer lookup + base_container-based
# reclassification (manifest content_type is self-reported and often wrong
# for registration/repaint packs - see scenery_data.py's _is_actually_a_livery) ---

def _make_aircraft_package(base_dir, folder_name, title, content_type="AIRCRAFT", creator="", simobjects=None):
    """simobjects: {simobject_folder_name: base_container_value_or_None}."""
    pkg_dir = base_dir / folder_name
    pkg_dir.mkdir(parents=True)
    manifest = {"content_type": content_type, "title": title}
    if creator:
        manifest["creator"] = creator
    (pkg_dir / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    for simobj_name, base_container in (simobjects or {}).items():
        simobj_dir = pkg_dir / "SimObjects" / "Airplanes" / simobj_name
        simobj_dir.mkdir(parents=True)
        cfg_lines = ["[VARIATION]"]
        if base_container is not None:
            cfg_lines.append(f'base_container = "{base_container}"')
        (simobj_dir / "aircraft.cfg").write_text("\n".join(cfg_lines), encoding="utf-8")


def test_developer_lookup_prefers_curated_list_over_creator_field(tmp_path):
    community = tmp_path / "Community"
    community.mkdir()
    _make_aircraft_package(
        community, "pmdg-aircraft-77w", "PMDG 777-300ER", creator="Some Random Creator Name",
        simobjects={"PMDG 777-300ER": None},
    )

    aircraft, _ = scenery_data.scan_aircraft_and_liveries(str(community), [])

    assert aircraft[0]["developer"] == "PMDG"


def test_developer_lookup_falls_back_to_creator_consensus_for_unknown_prefix(tmp_path):
    community = tmp_path / "Community"
    community.mkdir()
    _make_aircraft_package(
        community, "somestudio-aircraft-a", "Aircraft A", creator="Some Studio Inc.",
        simobjects={"Aircraft A": None},
    )
    _make_aircraft_package(
        community, "somestudio-aircraft-b", "Aircraft B", creator="Some Studio Inc.",
        simobjects={"Aircraft B": None},
    )

    aircraft, _ = scenery_data.scan_aircraft_and_liveries(str(community), [])

    assert {r["developer"] for r in aircraft} == {"Some Studio Inc."}


def test_aircraft_declared_package_with_external_base_container_reclassified_as_livery(tmp_path):
    # Mirrors a real-world pattern: a registration/repaint pack whose
    # manifest.json wrongly says content_type "AIRCRAFT", but whose own
    # aircraft.cfg base_container points at a *different* package's
    # SimObjects folder - structurally a livery no matter what the
    # manifest claims.
    community = tmp_path / "Community"
    community.mkdir()
    _make_aircraft_package(
        community, "base-aircraft-320", "Base Airbus A320", content_type="AIRCRAFT",
        simobjects={"Base_A320": None},
    )
    _make_aircraft_package(
        community, "livery-a320-fake-reg", "A320 Fake Registration", content_type="AIRCRAFT",
        simobjects={"livery-a320-fake-reg": "Base_A320"},
    )

    aircraft, liveries = scenery_data.scan_aircraft_and_liveries(str(community), [])

    assert [r["folder_name"] for r in aircraft] == ["base-aircraft-320"]
    assert len(liveries) == 1
    assert liveries[0]["folder_name"] == "livery-a320-fake-reg"
    assert liveries[0]["parent_aircraft"] == "base-aircraft-320"


def test_aircraft_with_base_container_pointing_to_its_own_variant_stays_aircraft(tmp_path):
    # A package can legitimately bundle multiple related SimObjects where
    # one variant's aircraft.cfg derives from a sibling within the *same*
    # package (e.g. a shortened-fuselage variant based on the main model) -
    # that must NOT be mistaken for an external dependency.
    community = tmp_path / "Community"
    community.mkdir()
    _make_aircraft_package(
        community, "fnx-aircraft-319-321", "Fenix A319 & A321", content_type="AIRCRAFT",
        simobjects={
            "FNX321": None,
            "FNX319": "FNX321",
        },
    )

    aircraft, liveries = scenery_data.scan_aircraft_and_liveries(str(community), [])

    assert [r["folder_name"] for r in aircraft] == ["fnx-aircraft-319-321"]
    assert liveries == []


def test_livery_declared_package_is_unaffected_by_reclassification_check(tmp_path):
    community = tmp_path / "Community"
    community.mkdir()
    _make_aircraft_package(
        community, "pmdg-aircraft-77w", "PMDG 777-300ER", content_type="AIRCRAFT",
        simobjects={"PMDG 777-300ER": None},
    )
    _make_aircraft_package(
        community, "pmdg-aircraft-77w-liveries", "Liveries", content_type="LIVERY",
        simobjects={"pmdg-aircraft-77w-liveries": "PMDG 777-300ER"},
    )

    aircraft, liveries = scenery_data.scan_aircraft_and_liveries(str(community), [])

    assert [r["folder_name"] for r in aircraft] == ["pmdg-aircraft-77w"]
    assert liveries[0]["parent_aircraft"] == "pmdg-aircraft-77w"
