from backend import folder_size


def _make_file(path, size_bytes):
    path.write_bytes(b"x" * size_bytes)


def test_scan_folder_usage_sums_per_package_and_total(tmp_path):
    pkg_a = tmp_path / "pkg-a"
    pkg_a.mkdir()
    _make_file(pkg_a / "one.bgl", 100)
    _make_file(pkg_a / "two.bgl", 50)

    pkg_b = tmp_path / "pkg-b"
    pkg_b.mkdir()
    _make_file(pkg_b / "big.bgl", 1000)

    result = folder_size.scan_folder_usage(str(tmp_path))

    assert result["total_bytes"] == 1150
    assert result["packages"] == [
        {"folder_name": "pkg-b", "bytes": 1000},
        {"folder_name": "pkg-a", "bytes": 150},
    ]


def test_scan_folder_usage_ignores_loose_files_at_top_level(tmp_path):
    (tmp_path / "readme.txt").write_text("not a package", encoding="utf-8")
    pkg = tmp_path / "pkg"
    pkg.mkdir()
    _make_file(pkg / "file.bgl", 10)

    result = folder_size.scan_folder_usage(str(tmp_path))

    assert result["packages"] == [{"folder_name": "pkg", "bytes": 10}]


def test_scan_folder_usage_sums_nested_subfolders(tmp_path):
    pkg = tmp_path / "pkg"
    (pkg / "nested" / "deeper").mkdir(parents=True)
    _make_file(pkg / "top.bgl", 10)
    _make_file(pkg / "nested" / "mid.bgl", 20)
    _make_file(pkg / "nested" / "deeper" / "bottom.bgl", 30)

    result = folder_size.scan_folder_usage(str(tmp_path))

    assert result["packages"] == [{"folder_name": "pkg", "bytes": 60}]


def test_scan_folder_usage_missing_path_returns_empty(tmp_path):
    result = folder_size.scan_folder_usage(str(tmp_path / "does_not_exist"))
    assert result == {"total_bytes": 0, "packages": []}


def test_scan_folder_usage_empty_folder(tmp_path):
    result = folder_size.scan_folder_usage(str(tmp_path))
    assert result == {"total_bytes": 0, "packages": []}
