"""Builds the plain-text bundle for the Settings tab's "Export diagnostics"
button - app/OS info, the current config, and a tail of the runtime log,
all in one file meant to be attached directly to a GitHub bug report. Pure
text building only; the Api layer handles the file dialog, OS info lookup,
and log reading (see app_logging.read_recent_lines).
"""
import json
import re
import time

from .version import APP_VERSION

# Fields stripped from the config before it goes into a diagnostics file -
# this is meant to be attached directly to a *public* GitHub issue, unlike
# the Backup & Restore export (backup.py), which keeps everything since
# that one is for the user's own restore. A SimBrief username is often
# tied to the person's real name/VATSIM identity and is essentially never
# needed to diagnose a bug - whether it's set at all is enough signal.
_REDACTED_KEYS = {"_simbrief_username"}

# Windows profile paths (C:\Users\<name>\...) show up all over the place
# here - the Community/disabled/GSX/exe.xml paths in the config, and any
# log line that happens to mention a file path (e.g. "Update download
# finished: C:\Users\<name>\Downloads\..."). Rather than track down every
# individual field/log line that could contain one, this blanket-redacts
# the account name segment wherever it appears in the final report text,
# keeping the rest of the path intact since that part is actually useful
# for diagnosis. \\+ (not \\) since backslashes come through doubled once
# the config has gone through json.dumps (a raw log line has single
# backslashes, a JSON-serialized path has "\\\\" per separator).
_WINDOWS_USER_PATH_RE = re.compile(r'([A-Za-z]:\\+Users\\+)([^\\"\r\n]+)')


def _redact_windows_username(text):
    return _WINDOWS_USER_PATH_RE.sub(lambda m: m.group(1) + "<user>", text)


def _redact_config(config):
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
        json.dumps(_redact_config(config), indent=2, ensure_ascii=False, sort_keys=True),
        "",
        "--- Recent log ---",
        log_tail or "(no log file yet)",
    ]
    return _redact_windows_username("\n".join(lines))


def default_filename():
    return f"flightops_hub_diagnostics_{time.strftime('%Y-%m-%d_%H%M%S')}.txt"
