"""Rotating runtime log (flightops_hub.log, next to the exe/config - same
CWD-relative, portable-app reasoning as config_manager.CONFIG_FILE).

Separate from flightops_hub.pyw's own crash_log.txt, which is a
last-resort handler for the narrow window before this module has even
been imported/configured (a startup crash before logging exists at all).
This one runs for the app's whole lifetime and is meant to answer "what
was the user doing right before it broke" for a bug report - not just
catch fatal crashes.
"""
import logging
import logging.handlers
import platform

from .version import APP_VERSION

LOG_FILE = "flightops_hub.log"
LOGGER_NAME = "flightops_hub"

_MAX_BYTES = 2 * 1024 * 1024
_BACKUP_COUNT = 1


def setup():
    """Idempotent - safe to call more than once (e.g. from a test harness),
    won't stack duplicate handlers."""
    logger = logging.getLogger(LOGGER_NAME)
    if logger.handlers:
        return logger

    logger.setLevel(logging.INFO)
    handler = logging.handlers.RotatingFileHandler(
        LOG_FILE, maxBytes=_MAX_BYTES, backupCount=_BACKUP_COUNT, encoding="utf-8"
    )
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(name)s: %(message)s"))
    logger.addHandler(handler)
    logger.propagate = False

    logger.info("FlightOps Hub %s starting - %s", APP_VERSION, platform.platform())
    return logger


def get_logger(suffix=None):
    """suffix: dotted sub-name, e.g. get_logger('scenery') -> logger named
    'flightops_hub.scenery' - shares the parent's file handler via normal
    logging hierarchy propagation, no extra setup needed."""
    name = LOGGER_NAME if not suffix else f"{LOGGER_NAME}.{suffix}"
    return logging.getLogger(name)


def read_recent_lines(max_lines=200):
    """Best-effort tail of the current log file, for bundling into a
    diagnostics export - never raises, returns "" if the file can't be read
    (e.g. logging was never set up, or this is a fresh install)."""
    try:
        with open(LOG_FILE, "r", encoding="utf-8", errors="replace") as f:
            lines = f.readlines()
        return "".join(lines[-max_lines:])
    except OSError:
        return ""
