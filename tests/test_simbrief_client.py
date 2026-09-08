import pytest

from backend import simbrief_client


def _ofp(**overrides):
    base = {
        "origin": {"icao_code": "LKPR"},
        "destination": {"icao_code": "LZIB"},
        "alternate": {"icao_code": "LZKZ"},
        "times": {"est_time_enroute": "2745"},
        "aircraft": {"name": "Citation X"},
        "params": {"time_generated": "1788813731"},
    }
    base.update(overrides)
    return base


def test_parses_legs_and_summary_fields():
    result = simbrief_client._parse_ofp(_ofp())

    assert result["origin"] == "LKPR"
    assert result["destination"] == "LZIB"
    assert result["alternate"] == "LZKZ"
    assert result["duration_minutes"] == 46  # 2745s rounds to 46 min
    assert result["aircraft_name"] == "Citation X"
    assert result["planned_at"] == 1788813731


def test_missing_alternate_is_none():
    result = simbrief_client._parse_ofp(_ofp(alternate={}))
    assert result["alternate"] is None


def test_missing_summary_fields_are_none_not_fatal():
    result = simbrief_client._parse_ofp(_ofp(times={}, aircraft={}, params={}))
    assert result["duration_minutes"] is None
    assert result["aircraft_name"] is None
    assert result["planned_at"] is None


def test_missing_origin_raises():
    # origin/destination are load-bearing for scenery matching - the caller
    # (fetch_latest_ofp) turns this into an error response, not a silent None.
    with pytest.raises(KeyError):
        simbrief_client._parse_ofp(_ofp(origin={}))


def test_garbage_est_time_enroute_does_not_crash():
    result = simbrief_client._parse_ofp(_ofp(times={"est_time_enroute": "not-a-number"}))
    assert result["duration_minutes"] is None
