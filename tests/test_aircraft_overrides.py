from backend import aircraft_overrides


def _aircraft(folder_name, developer="Dev", suspected_livery=False):
    return {
        "folder_name": folder_name,
        "display_name": folder_name,
        "developer": developer,
        "enabled": True,
        "parent_aircraft": None,
        "suspected_livery": suspected_livery,
    }


def _livery(folder_name, developer="Dev", parent_aircraft=None):
    return {
        "folder_name": folder_name,
        "display_name": folder_name,
        "developer": developer,
        "enabled": True,
        "parent_aircraft": parent_aircraft,
        "suspected_livery": False,
    }


def test_type_override_moves_aircraft_to_liveries():
    aircraft = [_aircraft("pkg-a")]
    result_aircraft, result_liveries = aircraft_overrides.apply_overrides(
        aircraft, [], {"pkg-a": "livery"}, {}
    )
    assert result_aircraft == []
    assert [r["folder_name"] for r in result_liveries] == ["pkg-a"]


def test_type_override_moves_livery_to_aircraft():
    liveries = [_livery("pkg-b")]
    result_aircraft, result_liveries = aircraft_overrides.apply_overrides(
        [], liveries, {"pkg-b": "aircraft"}, {}
    )
    assert [r["folder_name"] for r in result_aircraft] == ["pkg-b"]
    assert result_liveries == []
    assert "parent_aircraft" not in result_aircraft[0]


def test_parent_override_forces_a_specific_parent():
    aircraft = [_aircraft("plane-a"), _aircraft("plane-b")]
    liveries = [_livery("liv-a", parent_aircraft=None)]
    _, result_liveries = aircraft_overrides.apply_overrides(
        aircraft, liveries, {}, {"liv-a": "plane-b"}
    )
    assert result_liveries[0]["parent_aircraft"] == "plane-b"


def test_parent_override_of_empty_string_forces_unassigned():
    aircraft = [_aircraft("plane-a")]
    liveries = [_livery("liv-a", parent_aircraft="plane-a")]
    _, result_liveries = aircraft_overrides.apply_overrides(
        aircraft, liveries, {}, {"liv-a": ""}
    )
    assert result_liveries[0]["parent_aircraft"] is None


def test_parent_pointing_at_a_folder_that_is_no_longer_aircraft_falls_back_to_unassigned():
    # e.g. the parent got type-overridden to livery itself, or was removed -
    # never point at something that isn't in the final aircraft list.
    aircraft = [_aircraft("plane-a")]
    liveries = [_livery("liv-a", parent_aircraft="plane-a")]
    _, result_liveries = aircraft_overrides.apply_overrides(
        aircraft, liveries, {"plane-a": "livery"}, {}
    )
    assert result_liveries[0]["parent_aircraft"] is None


def test_suspected_livery_flag_survives_untouched_by_default():
    aircraft = [_aircraft("pkg-a", suspected_livery=True)]
    result_aircraft, _ = aircraft_overrides.apply_overrides(aircraft, [], {}, {})
    assert result_aircraft[0]["suspected_livery"] is True


def test_suspected_livery_flag_suppressed_once_type_overridden():
    # Explicitly confirming "yes this really is an aircraft" should stop
    # the hint from nagging again.
    aircraft = [_aircraft("pkg-a", suspected_livery=True)]
    result_aircraft, _ = aircraft_overrides.apply_overrides(
        aircraft, [], {"pkg-a": "aircraft"}, {}
    )
    assert result_aircraft[0]["suspected_livery"] is False


def test_suspected_livery_flag_suppressed_once_dismissed():
    aircraft = [_aircraft("pkg-a", suspected_livery=True)]
    result_aircraft, _ = aircraft_overrides.apply_overrides(
        aircraft, [], {}, {}, dismissed_suggestions={"pkg-a"}
    )
    assert result_aircraft[0]["suspected_livery"] is False


def test_suspected_livery_flag_only_suppressed_for_the_matching_folder():
    aircraft = [_aircraft("pkg-a", suspected_livery=True), _aircraft("pkg-b", suspected_livery=True)]
    result_aircraft, _ = aircraft_overrides.apply_overrides(
        aircraft, [], {}, {}, dismissed_suggestions={"pkg-a"}
    )
    by_folder = {r["folder_name"]: r for r in result_aircraft}
    assert by_folder["pkg-a"]["suspected_livery"] is False
    assert by_folder["pkg-b"]["suspected_livery"] is True
