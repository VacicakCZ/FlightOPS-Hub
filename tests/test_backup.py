import pytest

from backend import backup


def test_build_bundle_shape():
    config = {"_theme": "dark"}
    apps = {"SimBrief": {"path": "C:\\x.exe"}}
    bundle = backup.build_bundle(config, apps)

    assert bundle["bundle_version"] == backup.BUNDLE_VERSION
    assert bundle["config"] == config
    assert bundle["apps"] == apps
    assert "app_version" in bundle
    assert isinstance(bundle["exported_at"], int)


def test_default_filename_looks_like_a_json_backup():
    name = backup.default_filename()
    assert name.startswith("flightops_hub_backup_")
    assert name.endswith(".json")


def test_parse_bundle_round_trip():
    config = {"_theme": "dark", "_simbrief_username": "Vacicak"}
    apps = {"SimBrief": {"path": "C:\\x.exe"}}
    bundle = backup.build_bundle(config, apps)

    parsed_config, parsed_apps = backup.parse_bundle(bundle)

    assert parsed_config == config
    assert parsed_apps == apps


def test_parse_bundle_rejects_non_dict():
    with pytest.raises(ValueError):
        backup.parse_bundle(["not", "a", "bundle"])


def test_parse_bundle_rejects_missing_config_or_apps():
    with pytest.raises(ValueError):
        backup.parse_bundle({"apps": {}})
    with pytest.raises(ValueError):
        backup.parse_bundle({"config": {}})


def test_parse_bundle_rejects_wrong_types():
    with pytest.raises(ValueError):
        backup.parse_bundle({"config": "not a dict", "apps": {}})
    with pytest.raises(ValueError):
        backup.parse_bundle({"config": {}, "apps": "not a dict"})
