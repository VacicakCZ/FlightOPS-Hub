"""Validation for the "disabled addons holding path" setting - pure logic,
ported from the old browse_disabled_path handler.
"""
import os


def validate_disabled_path(disabled_path, community_path):
    """Returns {"ok": bool, "error": "nested"|None, "warning": "cross_drive"|None}.

    "nested" is a hard error (same folder as Community, or one contains the
    other) - the caller should not persist the path. "cross_drive" is only a
    warning - the path is still valid and should be persisted, but moving
    packages there will copy instead of rename.
    """
    if not community_path:
        return {"ok": True, "error": None, "warning": None}

    community_norm = os.path.normpath(community_path)
    a = os.path.normcase(os.path.normpath(disabled_path))
    b = os.path.normcase(community_norm)
    nested = a == b or a.startswith(b + os.sep) or b.startswith(a + os.sep)
    if nested:
        return {"ok": False, "error": "nested", "warning": None}

    cross_drive = os.path.splitdrive(a)[0] != os.path.splitdrive(b)[0]
    return {"ok": True, "error": None, "warning": "cross_drive" if cross_drive else None}
