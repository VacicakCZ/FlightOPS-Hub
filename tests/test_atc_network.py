from backend import atc_network


def test_icao_from_callsign_extracts_prefix():
    assert atc_network._icao_from_callsign("VHHH_TWR") == "VHHH"
    assert atc_network._icao_from_callsign("KOGD_ATIS") == "KOGD"


def test_icao_from_callsign_rejects_short_shorthand():
    # VATSIM occasionally uses a shortened local code instead of full
    # ICAO (e.g. "SY_TWR" for Sydney) - must not be mistaken for a real
    # 4-letter ICAO code.
    assert atc_network._icao_from_callsign("SY_TWR") is None


def test_icao_from_callsign_rejects_pilot_style_callsign_with_no_underscore():
    assert atc_network._icao_from_callsign("DLH456A") is None


def test_icao_from_callsign_handles_none_and_empty():
    assert atc_network._icao_from_callsign(None) is None
    assert atc_network._icao_from_callsign("") is None


def test_extract_airports_dedupes_and_uppercases():
    result = atc_network._extract_airports(["vhhh_TWR", "VHHH_APP", "KOGD_ATIS", "SY_TWR", None])
    assert result == {"VHHH", "KOGD"}


def test_fetch_online_atc_airports_returns_empty_set_for_off():
    assert atc_network.fetch_online_atc_airports("off") == set()
    assert atc_network.fetch_online_atc_airports(None) == set()
    assert atc_network.fetch_online_atc_airports("something-else") == set()
