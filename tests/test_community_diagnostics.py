import json

from backend import community_diagnostics


def _write_manifest(folder_path, title, content_type="SCENERY"):
    folder_path.mkdir(parents=True, exist_ok=True)
    (folder_path / "manifest.json").write_text(
        json.dumps({"title": title, "content_type": content_type}), encoding="utf-8"
    )


# --- find_misplaced_packages ---

def test_finds_a_manifest_nested_one_level_too_deep(tmp_path):
    _write_manifest(tmp_path / "some-airport" / "some-airport", "Some Airport")

    result = community_diagnostics.find_misplaced_packages(str(tmp_path))

    assert result == [{"folder_name": "some-airport", "nested_folder": "some-airport"}]


def test_correctly_placed_package_is_not_flagged(tmp_path):
    _write_manifest(tmp_path / "some-airport", "Some Airport")

    result = community_diagnostics.find_misplaced_packages(str(tmp_path))

    assert result == []


def test_folder_with_no_manifest_anywhere_is_not_flagged(tmp_path):
    (tmp_path / "not-a-package" / "random-stuff").mkdir(parents=True)

    result = community_diagnostics.find_misplaced_packages(str(tmp_path))

    assert result == []


def test_manifest_nested_two_levels_deep_is_not_flagged(tmp_path):
    # Only the specific "exactly one level too deep" mistake is detected -
    # anything deeper is a different (or non-) problem, not this heuristic's job.
    _write_manifest(tmp_path / "wrapper" / "inner" / "actual-package", "Deeply Nested")

    result = community_diagnostics.find_misplaced_packages(str(tmp_path))

    assert result == []


def test_missing_community_path_returns_empty(tmp_path):
    result = community_diagnostics.find_misplaced_packages(str(tmp_path / "does_not_exist"))
    assert result == []


# --- find_duplicate_packages ---

def test_same_title_under_two_folder_names_is_a_duplicate(tmp_path):
    _write_manifest(tmp_path / "aerosoft-ebbr-v1", "Brussels Airport")
    _write_manifest(tmp_path / "aerosoft-ebbr-v2", "Brussels Airport")

    result = community_diagnostics.find_duplicate_packages([str(tmp_path)])

    assert result == [{"title": "Brussels Airport", "folders": ["aerosoft-ebbr-v1", "aerosoft-ebbr-v2"]}]


def test_unique_titles_are_not_duplicates(tmp_path):
    _write_manifest(tmp_path / "pkg-a", "Airport A")
    _write_manifest(tmp_path / "pkg-b", "Airport B")

    result = community_diagnostics.find_duplicate_packages([str(tmp_path)])

    assert result == []


def test_duplicate_detected_across_multiple_locations(tmp_path):
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    _write_manifest(community / "pkg-enabled", "Some Scenery")
    _write_manifest(disabled / "pkg-disabled-copy", "Some Scenery")

    result = community_diagnostics.find_duplicate_packages([str(community), str(disabled)])

    assert result == [{"title": "Some Scenery", "folders": ["pkg-disabled-copy", "pkg-enabled"]}]


def test_same_folder_name_in_two_locations_only_counts_once(tmp_path):
    # Same folder name appearing in both a Community and a disabled scan
    # (e.g. a stale duplicate scan of the same physical folder) should not
    # be reported as "two different installs".
    community = tmp_path / "Community"
    disabled = tmp_path / "Disabled"
    _write_manifest(community / "same-name", "A Package")
    _write_manifest(disabled / "same-name", "A Package")

    result = community_diagnostics.find_duplicate_packages([str(community), str(disabled)])

    assert result == []


def test_folders_without_manifest_are_ignored(tmp_path):
    (tmp_path / "empty-folder").mkdir()

    result = community_diagnostics.find_duplicate_packages([str(tmp_path)])

    assert result == []
