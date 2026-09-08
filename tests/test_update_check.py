from backend import update_check


def test_is_newer_detects_a_real_bump():
    assert update_check.is_newer("v2.1", "v2.0") is True
    assert update_check.is_newer("2.1.0", "2.0.9") is True


def test_is_newer_false_when_same_or_older():
    assert update_check.is_newer("v2.0", "v2.0") is False
    assert update_check.is_newer("v1.9", "v2.0") is False


def test_is_newer_handles_missing_patch_component():
    # "v2.0" vs "v2.0.1" - the tuple comparison must not blow up just
    # because one side has fewer dotted components than the other.
    assert update_check.is_newer("v2.0.1", "v2.0") is True
    assert update_check.is_newer("v2.0", "v2.0.1") is False


def test_is_newer_garbage_text_never_beats_a_real_version():
    assert update_check.is_newer("not-a-version", "v2.0") is False
    assert update_check.is_newer("", "v2.0") is False


def test_parse_version_ignores_prerelease_suffix():
    assert update_check._parse_version("v2.1-beta") == (2, 1)
    assert update_check._parse_version("2.1.3-rc1") == (2, 1, 3)


def test_find_asset_download_url_matches_exact_name():
    data = {
        "assets": [
            {"name": "source.zip", "browser_download_url": "https://example.com/source.zip"},
            {"name": "flightops_hub.exe", "browser_download_url": "https://example.com/flightops_hub.exe"},
        ]
    }
    assert update_check._find_asset_download_url(data) == "https://example.com/flightops_hub.exe"


def test_find_asset_download_url_none_when_missing():
    assert update_check._find_asset_download_url({"assets": [{"name": "other.zip"}]}) is None
    assert update_check._find_asset_download_url({}) is None
