"""Builds the plain-text bundle for the Settings tab's "Export diagnostics"
button - app/OS info, the current config, and a tail of the runtime log,
all in one file meant to be attached directly to a GitHub bug report. Pure
text building only; the Api layer handles the file dialog, OS info lookup,
and log reading (see app_logging.read_recent_lines).
"""
import json
import time

from .version import APP_VERSION


def build_report(config, os_info, log_tail):
    lines = [
        f"FlightOps Hub diagnostics - {time.strftime('%Y-%m-%d %H:%M:%S')}",
        f"App version: {APP_VERSION}",
        f"OS: {os_info}",
        "",
        "--- Config ---",
        json.dumps(config, indent=2, ensure_ascii=False, sort_keys=True),
        "",
        "--- Recent log ---",
        log_tail or "(no log file yet)",
    ]
    return "\n".join(lines)


def default_filename():
    return f"flightops_hub_diagnostics_{time.strftime('%Y-%m-%d_%H%M%S')}.txt"
