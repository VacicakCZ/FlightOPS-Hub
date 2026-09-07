"""Win32 ctypes helpers with no dependency on any UI toolkit.

Kept separate from pywebview/tkinter so the single-instance check and the
"already running" message box can run before any window exists.
"""
import ctypes

_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
_user32 = ctypes.WinDLL("user32", use_last_error=True)

_ERROR_ALREADY_EXISTS = 183
_SINGLE_INSTANCE_MUTEX_NAME = "Global\\FlightOpsHub_SingleInstance_Mutex"

MB_OK = 0x0
MB_ICONWARNING = 0x30
MB_ICONERROR = 0x10


def acquire_single_instance_lock():
    """Creates a named mutex and returns (handle, already_running).

    The handle must be kept alive for the app's lifetime (store it in a
    module/global variable) - if it gets garbage collected the mutex is
    released and a second instance could start despite the first still
    running.
    """
    handle = _kernel32.CreateMutexW(None, False, _SINGLE_INSTANCE_MUTEX_NAME)
    already_running = ctypes.get_last_error() == _ERROR_ALREADY_EXISTS
    return handle, already_running


def message_box(title, text, icon=MB_ICONWARNING):
    _user32.MessageBoxW(None, text, title, icon | MB_OK)


# --- ShellExecuteExW plumbing for "run as admin" launches (used by the M2 launch orchestrator) ---
SEE_MASK_NOCLOSEPROCESS = 0x00000040
SW_SHOWNORMAL = 1


class SHELLEXECUTEINFOW(ctypes.Structure):
    _fields_ = [
        ("cbSize", ctypes.c_ulong),
        ("fMask", ctypes.c_ulong),
        ("hwnd", ctypes.c_void_p),
        ("lpVerb", ctypes.c_wchar_p),
        ("lpFile", ctypes.c_wchar_p),
        ("lpParameters", ctypes.c_wchar_p),
        ("lpDirectory", ctypes.c_wchar_p),
        ("nShow", ctypes.c_int),
        ("hInstApp", ctypes.c_void_p),
        ("lpIDList", ctypes.c_void_p),
        ("lpClass", ctypes.c_wchar_p),
        ("hKeyClass", ctypes.c_void_p),
        ("dwHotKey", ctypes.c_ulong),
        ("hIcon", ctypes.c_void_p),
        ("hProcess", ctypes.c_void_p),
    ]


_kernel32.GetProcessId.argtypes = [ctypes.c_void_p]
_kernel32.GetProcessId.restype = ctypes.c_ulong
_kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
_kernel32.CloseHandle.restype = ctypes.c_int


def run_as_admin(path, cwd=None):
    """Launches `path` elevated via the 'runas' verb. Returns the new PID, or None on failure/cancel."""
    info = SHELLEXECUTEINFOW()
    info.cbSize = ctypes.sizeof(SHELLEXECUTEINFOW)
    info.fMask = SEE_MASK_NOCLOSEPROCESS
    info.lpVerb = "runas"
    info.lpFile = path
    info.lpDirectory = cwd
    info.nShow = SW_SHOWNORMAL

    if not ctypes.windll.shell32.ShellExecuteExW(ctypes.byref(info)):
        return None

    if not info.hProcess:
        return None

    pid = _kernel32.GetProcessId(info.hProcess)
    _kernel32.CloseHandle(info.hProcess)
    return pid
