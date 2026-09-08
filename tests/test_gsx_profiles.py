from backend import gsx_profiles


# --- scan_profiles: filename -> ICAO extraction ---

def test_scan_profiles_extracts_icao_from_various_separators(tmp_path):
    for name in ["LKPR-darriancze-gsxvdgs.ini", "ebbr_aerosoft_v2.ini", "DTMB - Nizar.ini"]:
        (tmp_path / name).write_text("", encoding="utf-8")

    profiles = gsx_profiles.scan_profiles(str(tmp_path))

    assert profiles["LKPR"] == ["LKPR-darriancze-gsxvdgs.ini"]
    assert profiles["EBBR"] == ["ebbr_aerosoft_v2.ini"]
    assert profiles["DTMB"] == ["DTMB - Nizar.ini"]


def test_scan_profiles_ignores_files_without_a_recognizable_icao_prefix(tmp_path):
    (tmp_path / "readme.txt").write_text("", encoding="utf-8")
    (tmp_path / "notanicao-file.ini").write_text("", encoding="utf-8")

    profiles = gsx_profiles.scan_profiles(str(tmp_path))

    assert "READ" not in profiles
    # "notanicao" starts with 4 letters immediately followed by more letters
    # (no separator right after), so the prefix regex should not match it.
    assert profiles == {}


def test_scan_profiles_ignores_subdirectories(tmp_path):
    (tmp_path / "LKPR-sub").mkdir()

    profiles = gsx_profiles.scan_profiles(str(tmp_path))

    assert profiles == {}


def test_scan_profiles_missing_or_empty_path_returns_empty_dict(tmp_path):
    assert gsx_profiles.scan_profiles("") == {}
    assert gsx_profiles.scan_profiles(str(tmp_path / "does_not_exist")) == {}


# --- developer-match heuristic ---

def test_status_for_missing_when_no_profiles():
    assert gsx_profiles._status_for("aerosoft", []) == "missing"


def test_status_for_matches_developer_key_in_filename():
    assert gsx_profiles._status_for("aerosoft", ["ebbr_aerosoft_v2.ini"]) == "installed_match"


def test_status_for_unmatched_when_developer_not_in_filename():
    assert gsx_profiles._status_for("flightbeam", ["lfbo-snekye.ini"]) == "installed_unmatched"


# --- attach_gsx_status: full record integration ---

def _record(folder_name, display_name):
    return {"folder_name": folder_name, "display_name": display_name}


def test_attach_gsx_status_full_three_states(tmp_path):
    (tmp_path / "ebbr_aerosoft_v2.ini").write_text("", encoding="utf-8")
    (tmp_path / "lfbo-snekye.ini").write_text("", encoding="utf-8")

    records = [
        _record("aerosoft-airport-ebbr-brussels", "[EBBR] Brussels Airport"),
        _record("flightbeam-lfbo-toulouse", "[LFBO] Toulouse-Blagnac"),
        _record("orbx-airport-eddf-frankfurt", "[EDDF] Frankfurt Airport"),
    ]

    gsx_profiles.attach_gsx_status(records, str(tmp_path))

    by_folder = {r["folder_name"]: r for r in records}
    assert by_folder["aerosoft-airport-ebbr-brussels"]["gsx_status"] == "installed_match"
    assert by_folder["flightbeam-lfbo-toulouse"]["gsx_status"] == "installed_unmatched"
    assert by_folder["orbx-airport-eddf-frankfurt"]["gsx_status"] == "missing"


def test_attach_gsx_status_none_for_record_without_icao():
    records = [_record("some-random-scenery", "Some Scenery With No Code")]

    gsx_profiles.attach_gsx_status(records, "")

    assert records[0]["gsx_status"] is None
    assert records[0]["icao"] is None


def test_attach_gsx_status_attaches_prettified_developer_name():
    records = [_record("aerosoft-airport-ebbr-brussels", "[EBBR] Brussels Airport")]

    gsx_profiles.attach_gsx_status(records, "")

    assert records[0]["developer"] == "Aerosoft"
