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


def test_position_from_callsign_extracts_suffix():
    assert atc_network._position_from_callsign("VHHH_TWR") == "TWR"
    assert atc_network._position_from_callsign("KOGD_ATIS") == "ATIS"


def test_position_from_callsign_falls_back_to_atc_when_no_suffix():
    assert atc_network._position_from_callsign("SY_TWR_APP") == "TWR_APP"
    assert atc_network._position_from_callsign("NOFACILITY") == "ATC"


def test_add_position_groups_by_airport():
    by_airport = {}
    atc_network._add_position(by_airport, "VHHH_TWR", "118.200")
    atc_network._add_position(by_airport, "VHHH_GND", "121.900")
    atc_network._add_position(by_airport, "KOGD_ATIS", "125.550", position_override="ATIS")

    assert by_airport == {
        "VHHH": [{"position": "TWR", "frequency": "118.200"}, {"position": "GND", "frequency": "121.900"}],
        "KOGD": [{"position": "ATIS", "frequency": "125.550"}],
    }


def test_add_position_ignores_unparseable_callsign():
    by_airport = {}
    atc_network._add_position(by_airport, "SY_TWR", "118.200")
    assert by_airport == {}


def test_fetch_online_atc_returns_empty_dict_for_off():
    assert atc_network.fetch_online_atc("off") == {}
    assert atc_network.fetch_online_atc(None) == {}
    assert atc_network.fetch_online_atc("something-else") == {}
