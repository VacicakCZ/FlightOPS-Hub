import logging

from backend import app_logging


def test_read_recent_lines_returns_empty_when_file_missing(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(app_logging, "LOG_FILE", "does_not_exist.log")
    assert app_logging.read_recent_lines() == ""


def test_read_recent_lines_tails_the_file(tmp_path, monkeypatch):
    log_path = tmp_path / "test.log"
    log_path.write_text("\n".join(f"line {i}" for i in range(10)) + "\n", encoding="utf-8")
    monkeypatch.setattr(app_logging, "LOG_FILE", str(log_path))

    result = app_logging.read_recent_lines(max_lines=3)

    assert result == "line 7\nline 8\nline 9\n"


def _close_and_clear_handlers():
    logger = logging.getLogger(app_logging.LOGGER_NAME)
    for handler in logger.handlers[:]:
        handler.close()
    logger.handlers.clear()


def test_setup_is_idempotent(tmp_path, monkeypatch):
    # Real setup() writes a log file next to the CWD - point it at a tmp_path
    # so the test suite never leaves a stray flightops_hub.log in the repo,
    # and close handlers after so Windows doesn't keep the file locked
    # (which would break tmp_path's own cleanup).
    monkeypatch.chdir(tmp_path)
    _close_and_clear_handlers()

    logger1 = app_logging.setup()
    handler_count = len(logger1.handlers)
    logger2 = app_logging.setup()

    assert logger1 is logger2
    assert len(logger2.handlers) == handler_count  # no duplicate handlers stacked

    _close_and_clear_handlers()


def test_get_logger_child_uses_dotted_name():
    logger = app_logging.get_logger("scenery")
    assert logger.name == "flightops_hub.scenery"
