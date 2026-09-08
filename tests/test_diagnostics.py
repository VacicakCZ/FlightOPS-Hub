from backend import diagnostics


def test_build_report_includes_all_sections():
    report = diagnostics.build_report({"_theme": "dark"}, "Windows-11", "some log line\n")

    assert "App version:" in report
    assert "OS: Windows-11" in report
    assert '"_theme": "dark"' in report
    assert "some log line" in report


def test_build_report_handles_empty_log():
    report = diagnostics.build_report({}, "Windows-11", "")
    assert "(no log file yet)" in report


def test_build_report_redacts_simbrief_username():
    # This file is meant to be attached directly to a public GitHub issue -
    # the real username must never appear in it, just whether it is set.
    report = diagnostics.build_report({"_simbrief_username": "RealPersonName"}, "Windows-11", "")
    assert "RealPersonName" not in report
    assert "<redacted>" in report


def test_build_report_leaves_empty_simbrief_username_alone():
    report = diagnostics.build_report({"_simbrief_username": ""}, "Windows-11", "")
    assert "<redacted>" not in report


def test_default_filename_looks_like_a_txt_report():
    name = diagnostics.default_filename()
    assert name.startswith("flightops_hub_diagnostics_")
    assert name.endswith(".txt")
