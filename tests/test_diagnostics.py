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


def test_build_report_redacts_windows_username_in_log_lines():
    # Any log line mentioning a file path (downloads, exports, apply
    # errors, ...) can carry C:\Users\<name>\... - must be scrubbed too,
    # not just the config's own _simbrief_username field.
    log = r"Update download finished: C:\Users\RealPersonName\Downloads\flightops_hub.exe"
    report = diagnostics.build_report({}, "Windows-11", log)
    assert "RealPersonName" not in report
    assert r"C:\Users\<user>\Downloads\flightops_hub.exe" in report


def test_build_report_redacts_windows_username_in_config_paths():
    config = {"_community_path": r"C:\Users\RealPersonName\AppData\Local\Packages\Microsoft.FlightSimulator\Community"}
    report = diagnostics.build_report(config, "Windows-11", "")
    assert "RealPersonName" not in report
    assert "<user>" in report


def test_default_filename_looks_like_a_txt_report():
    name = diagnostics.default_filename()
    assert name.startswith("flightops_hub_diagnostics_")
    assert name.endswith(".txt")
