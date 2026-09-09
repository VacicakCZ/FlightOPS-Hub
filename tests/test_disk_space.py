import shutil

from backend import disk_space


class _FakeUsage:
    def __init__(self, free):
        self.free = free


def test_check_missing_paths_returns_empty(tmp_path):
    result = disk_space.check(str(tmp_path / "does_not_exist"), "")
    assert result == []


def test_check_reports_free_bytes_and_low_flag(tmp_path, monkeypatch):
    monkeypatch.setattr(shutil, "disk_usage", lambda path: _FakeUsage(1024))

    result = disk_space.check(str(tmp_path), "")

    assert result == [{"label": "community", "free_bytes": 1024, "low": True}]


def test_check_does_not_flag_ample_space(tmp_path, monkeypatch):
    monkeypatch.setattr(shutil, "disk_usage", lambda path: _FakeUsage(50 * 1024 ** 3))

    result = disk_space.check(str(tmp_path), "")

    assert result == [{"label": "community", "free_bytes": 50 * 1024 ** 3, "low": False}]


def test_check_dedupes_paths_on_the_same_drive(tmp_path, monkeypatch):
    # Both are real subfolders of tmp_path, so they genuinely share a drive -
    # the disabled-holding folder should not be reported a second time.
    community = tmp_path / "Community"
    disabled = tmp_path / "disabled-addons"
    community.mkdir()
    disabled.mkdir()
    monkeypatch.setattr(shutil, "disk_usage", lambda path: _FakeUsage(1024))

    result = disk_space.check(str(community), str(disabled))

    assert len(result) == 1
    assert result[0]["label"] == "community"


def test_check_reports_each_drive_separately(tmp_path, monkeypatch):
    community = tmp_path / "Community"
    disabled = tmp_path / "disabled-addons"
    community.mkdir()
    disabled.mkdir()

    fake_devices = {str(community): 1, str(disabled): 2}
    real_stat = __import__("os").stat

    def fake_stat(path):
        if str(path) in fake_devices:
            return type("S", (), {"st_dev": fake_devices[str(path)]})()
        return real_stat(path)

    monkeypatch.setattr("os.stat", fake_stat)
    monkeypatch.setattr(shutil, "disk_usage", lambda path: _FakeUsage(1024))

    result = disk_space.check(str(community), str(disabled))

    assert [e["label"] for e in result] == ["community", "disabled"]


def test_check_ignores_empty_disabled_path(tmp_path, monkeypatch):
    monkeypatch.setattr(shutil, "disk_usage", lambda path: _FakeUsage(50 * 1024 ** 3))

    result = disk_space.check(str(tmp_path), "")

    assert len(result) == 1
