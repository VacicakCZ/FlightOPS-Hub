import os

from backend import community_detect


def test_default_community_path_msfs2020_steam():
    path = community_detect.default_community_path("MSFS 2020", "Steam")
    assert path.endswith(os.path.join("Microsoft Flight Simulator", "Packages", "Community"))


def test_default_community_path_msfs2020_msstore():
    path = community_detect.default_community_path("MSFS 2020", "MS Store")
    assert "Microsoft.FlightSimulator_8wekyb3d8bbwe" in path
    assert path.endswith(os.path.join("LocalCache", "Packages", "Community"))


def test_default_community_path_msfs2024_steam():
    path = community_detect.default_community_path("MSFS 2024", "Steam")
    assert path.endswith(os.path.join("Microsoft Flight Simulator 2024", "Packages", "Community"))


def test_default_community_path_msfs2024_msstore():
    path = community_detect.default_community_path("MSFS 2024", "MS Store")
    assert "Microsoft.FlightSimulator2024_8wekyb3d8bbwe" in path
    assert path.endswith(os.path.join("LocalCache", "Packages", "Community"))


def test_detect_community_path_returns_none_when_missing(tmp_path, monkeypatch):
    monkeypatch.setenv("APPDATA", str(tmp_path))
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    assert community_detect.detect_community_path("MSFS 2024", "Steam") is None


def test_detect_community_path_returns_path_when_present(tmp_path, monkeypatch):
    monkeypatch.setenv("APPDATA", str(tmp_path))
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    expected = community_detect.default_community_path("MSFS 2024", "Steam")
    os.makedirs(expected)
    assert community_detect.detect_community_path("MSFS 2024", "Steam") == expected
