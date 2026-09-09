import subprocess

from backend import process_utils


def test_terminate_pid_calls_taskkill_with_list_args_not_a_shell_string(monkeypatch):
    # A shell-string command (os.system(f"taskkill /PID {pid} ...")) would
    # let a crafted pid value inject extra shell commands - list args passed
    # straight to the process (no shell) can't be reinterpreted that way.
    calls = []
    monkeypatch.setattr(subprocess, "run", lambda *a, **k: calls.append((a, k)))

    process_utils.terminate_pid(4242)

    assert len(calls) == 1
    args, kwargs = calls[0]
    assert args[0] == ["taskkill", "/PID", "4242", "/F", "/T"]
    assert kwargs.get("stdout") == subprocess.DEVNULL
    assert kwargs.get("stderr") == subprocess.DEVNULL


def test_terminate_pid_swallows_errors(monkeypatch):
    monkeypatch.setattr(subprocess, "run", lambda *a, **k: (_ for _ in ()).throw(OSError("no such process")))

    process_utils.terminate_pid(4242)  # must not raise
