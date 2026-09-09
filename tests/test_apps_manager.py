import json

from backend import apps_manager


def test_save_apps_leaves_no_temp_file_behind(tmp_path, monkeypatch):
    apps_file = tmp_path / "msfs_apps.json"
    monkeypatch.setattr(apps_manager, "APPS_FILE", str(apps_file))

    apps_manager.save_apps({"REX Atmos": {"path": "C:\\rex.exe", "delay": 150, "admin": False, "launch_mode": "timer"}})

    assert apps_file.exists()
    assert not (tmp_path / "msfs_apps.json.tmp").exists()


def test_save_apps_does_not_corrupt_existing_file_on_write_failure(tmp_path, monkeypatch):
    apps_file = tmp_path / "msfs_apps.json"
    monkeypatch.setattr(apps_manager, "APPS_FILE", str(apps_file))
    apps_manager.save_apps({"App A": {"path": "C:\\a.exe", "delay": 0, "admin": False, "launch_mode": "immediate"}})
    original_bytes = apps_file.read_bytes()

    monkeypatch.setattr(json, "dump", lambda *a, **k: (_ for _ in ()).throw(OSError("disk full")))
    try:
        apps_manager.save_apps({"App B": {"path": "C:\\b.exe", "delay": 0, "admin": False, "launch_mode": "immediate"}})
    except OSError:
        pass

    assert apps_file.read_bytes() == original_bytes
    assert not (tmp_path / "msfs_apps.json.tmp").exists()
