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
    assert (disabled / "dup-package").is_dir()
