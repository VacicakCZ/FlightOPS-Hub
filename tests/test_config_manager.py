import json

from backend import config_manager


# --- theme migration ---

def test_canonical_theme_passes_through_known_values():
    assert config_manager._canonical_theme("dark") == "dark"
    assert config_manager._canonical_theme("light") == "light"
    assert config_manager._canonical_theme("system") == "system"


def test_canonical_theme_migrates_legacy_localized_labels():
    assert config_manager._canonical_theme("Tmavý") == "dark"
    assert config_manager._canonical_theme("Dunkel") == "dark"
    assert config_manager._canonical_theme("Světlý") == "light"
    assert config_manager._canonical_theme("Claro") == "light"


def test_canonical_theme_falls_back_to_system_for_unknown_values():
    assert config_manager._canonical_theme("garbage") == "system"
    assert config_manager._canonical_theme(None) == "system"


# --- window geometry migration ---

def test_migrate_geometry_parses_legacy_tk_geometry_string():
    data = {"_window_geometry": "500x860+1174+220"}
    config_manager._migrate_geometry(data)
    assert data["_window_width"] == 500
    assert data["_window_height"] == 860
    assert data["_window_x"] == 1174
    assert data["_window_y"] == 220
    assert "_window_geometry" not in data


def test_migrate_geometry_handles_size_only_string():
    data = {"_window_geometry": "500x860"}
    config_manager._migrate_geometry(data)
    assert data["_window_width"] == 500
    assert data["_window_height"] == 860
    assert "_window_x" not in data


def test_migrate_geometry_defaults_when_missing():
    data = {}
    config_manager._migrate_geometry(data)
    assert data["_window_width"] == config_manager._DEFAULT_WIDTH
    assert data["_window_height"] == config_manager._DEFAULT_HEIGHT


def test_clamp_window_position_drops_bogus_coordinates():
    data = {"_window_x": -50000, "_window_y": 200}
    config_manager._clamp_window_position(data)
    assert "_window_x" not in data
    assert "_window_y" not in data


def test_clamp_window_position_keeps_sane_coordinates():
    data = {"_window_x": 100, "_window_y": 200}
    config_manager._clamp_window_position(data)
    assert data["_window_x"] == 100
    assert data["_window_y"] == 200


# --- stale "translated default" last-profile sentinel self-heal ---

def test_sanitize_last_profile_clears_unknown_value():
    data = {"_profiles": {"Real Profile": []}, "_last_profile": "Vychozi"}
    config_manager._sanitize_last_profile(data, "flight")
    assert data["_last_profile"] is None


def test_sanitize_last_profile_keeps_known_value():
    data = {"_profiles": {"Real Profile": []}, "_last_profile": "Real Profile"}
    config_manager._sanitize_last_profile(data, "flight")
    assert data["_last_profile"] == "Real Profile"


# --- full migrate_config pipeline ---

def test_migrate_config_sets_version_and_is_idempotent():
    data = {"_window_geometry": "500x860+10+20", "_theme": "Tmavý"}
    once = config_manager.migrate_config(data)
    assert once["_config_version"] == config_manager.CURRENT_CONFIG_VERSION
    assert once["_theme"] == "dark"

    twice = config_manager.migrate_config(dict(once))
    assert twice == once


# --- load/save round trip (uses a tmp file, never the real config) ---

def test_save_then_load_round_trips(tmp_path, monkeypatch):
    config_file = tmp_path / "msfs_launcher_config.json"
    monkeypatch.setattr(config_manager, "CONFIG_FILE", str(config_file))

    config_manager.save_config({"_language": "CZ", "_theme": "dark"})
    loaded = config_manager.load_config()

    assert loaded["_language"] == "CZ"
    assert loaded["_theme"] == "dark"
    assert loaded["_config_version"] == config_manager.CURRENT_CONFIG_VERSION


def test_load_config_missing_file_returns_migrated_defaults(tmp_path, monkeypatch):
    monkeypatch.setattr(config_manager, "CONFIG_FILE", str(tmp_path / "does_not_exist.json"))
    loaded = config_manager.load_config()
    assert loaded["_config_version"] == config_manager.CURRENT_CONFIG_VERSION


def test_load_config_corrupt_json_falls_back_to_defaults(tmp_path, monkeypatch):
    config_file = tmp_path / "msfs_launcher_config.json"
    config_file.write_text("{not valid json", encoding="utf-8")
    monkeypatch.setattr(config_manager, "CONFIG_FILE", str(config_file))

    loaded = config_manager.load_config()
    assert loaded["_config_version"] == config_manager.CURRENT_CONFIG_VERSION


def test_save_config_leaves_no_temp_file_behind(tmp_path, monkeypatch):
    config_file = tmp_path / "msfs_launcher_config.json"
    monkeypatch.setattr(config_manager, "CONFIG_FILE", str(config_file))

    config_manager.save_config({"_language": "CZ"})

    assert config_file.exists()
    assert not (tmp_path / "msfs_launcher_config.json.tmp").exists()


def test_save_config_does_not_corrupt_existing_file_on_write_failure(tmp_path, monkeypatch):
    # A crash/kill/power-loss mid-write only ever touches the ".tmp" file -
    # simulate that by making the write itself fail, and confirm the real
    # config file (written successfully earlier) is completely untouched,
    # not truncated/corrupted.
    config_file = tmp_path / "msfs_launcher_config.json"
    monkeypatch.setattr(config_manager, "CONFIG_FILE", str(config_file))
    config_manager.save_config({"_language": "CZ", "_theme": "dark"})
    original_bytes = config_file.read_bytes()

    monkeypatch.setattr(json, "dump", lambda *a, **k: (_ for _ in ()).throw(OSError("disk full")))
    try:
        config_manager.save_config({"_language": "EN"})
    except OSError:
        pass

    assert config_file.read_bytes() == original_bytes
    loaded = config_manager.load_config()
    assert loaded["_language"] == "CZ"
    assert not (tmp_path / "msfs_launcher_config.json.tmp").exists()


def test_load_config_strips_bom(tmp_path, monkeypatch):
    config_file = tmp_path / "msfs_launcher_config.json"
    config_file.write_bytes(b"\xef\xbb\xbf" + json.dumps({"_language": "EN"}).encode("utf-8"))
    monkeypatch.setattr(config_manager, "CONFIG_FILE", str(config_file))

    loaded = config_manager.load_config()
    assert loaded["_language"] == "EN"


# --- language validation (reads the real frontend/locales/languages.json) ---

def test_validate_language_accepts_known_code():
    codes = {entry["code"] for entry in config_manager.available_languages()}
    assert "EN" in codes
    assert config_manager.validate_language("EN") == "EN"


def test_validate_language_falls_back_to_en_for_unknown_code():
    assert config_manager.validate_language("XX") == "EN"
