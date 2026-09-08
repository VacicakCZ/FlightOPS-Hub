"""Builds the plain-text bundle for the Settings tab's "Export diagnostics"
button - app/OS info, the current config, and a tail of the runtime log,
all in one file meant to be attached directly to a GitHub bug report. Pure
text building only; the Api layer handles the file dialog, OS info lookup,
and log reading (see app_logging.read_recent_lines).
"""
import json
import time

from .version import APP_VERSION

# Fields stripped from the config before it goes into a diagnostics file -
# this is meant to be attached directly to a *public* GitHub issue, unlike
# the Backup & Restore export (backup.py), which keeps everything since
# that one is for the user's own restore. A SimBrief username is often
# tied to the person's real name/VATSIM identity and is essentially never
# needed to diagnose a bug - whether it's set at all is enough signal.
_REDACTED_KEYS = {"_simbrief_username"}


def _redact(config):
    redacted = dict(config)
    for key in _REDACTED_KEYS:
        if redacted.get(key):
            redacted[key] = "<redacted>"
    return redacted


def build_report(config, os_info, log_tail):
    lines = [
        f"FlightOps Hub diagnostics - {time.strftime('%Y-%m-%d %H:%M:%S')}",
        f"App version: {APP_VERSION}",
        f"OS: {os_info}",
        "",
        "--- Config ---",
        json.dumps(_redact(config), indent=2, ensure_ascii=False, sort_keys=True),
        "",
        "--- Recent log ---",
        log_tail or "(no log file yet)",
    ]
    return "\n".join(lines)


def default_filename():
    return f"flightops_hub_diagnostics_{time.strftime('%Y-%m-%d_%H%M%S')}.txt"
