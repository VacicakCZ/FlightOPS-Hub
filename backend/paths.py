"""Path resolution helpers, aware of running from source vs. a PyInstaller one-file build."""
import os
import sys


def resource_dir():
    """Base directory for bundled, read-only assets (frontend/, OIG3.ico).

    Inside a PyInstaller one-file build these are unpacked to sys._MEIPASS;
    running from source they live next to this package.
    """
    if getattr(sys, "frozen", False) and hasattr(sys, "_MEIPASS"):
        return sys._MEIPASS
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def resource_path(*parts):
    return os.path.join(resource_dir(), *parts)
