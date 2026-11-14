"""Check a test terminal stays discoverable while minimized; restore its placement."""
import ctypes
import ctypes.wintypes as w
import json
import pathlib
import subprocess
import sys
import time

root = pathlib.Path(__file__).resolve().parents[1]
user32 = ctypes.WinDLL('user32', use_last_error=True)
class Placement(ctypes.Structure):
    _fields_ = [('length', w.UINT), ('flags', w.UINT), ('show', w.UINT), ('minimum', w.POINT), ('maximum', w.POINT), ('normal', w.RECT)]
user32.GetWindowPlacement.argtypes = [w.HWND, ctypes.POINTER(Placement)]
user32.SetWindowPlacement.argtypes = [w.HWND, ctypes.POINTER(Placement)]
user32.ShowWindowAsync.argtypes = [w.HWND, ctypes.c_int]
user32.IsIconic.argtypes = [w.HWND]
handle = int(sys.argv[1])
placement = Placement(length=ctypes.sizeof(Placement))
if not user32.GetWindowPlacement(handle, ctypes.byref(placement)):
    raise ctypes.WinError(ctypes.get_last_error())
try:
    user32.ShowWindowAsync(handle, 6)
    time.sleep(.25)
    assert user32.IsIconic(handle), 'The test window was not minimized'
    run = subprocess.run([str(root/'dist/desktop-verified/clink.exe'), 'scan'], capture_output=True, text=True, encoding='utf-8', check=True)
    result = json.loads(run.stdout)
    assert any(p['windowHandle'] == handle for p in result), 'Minimized terminal disappeared from scan'
    print(json.dumps({'passed': True, 'minimizedWindowFound': True, 'windowHandle': handle}))
finally:
    user32.SetWindowPlacement(handle, ctypes.byref(placement))
