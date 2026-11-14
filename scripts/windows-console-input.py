"""Send test keys to one specified Code Link console, never the active desktop window."""
import ctypes, sys, time
from ctypes import wintypes as w
class Key(ctypes.Structure):
    _fields_=[('down',w.BOOL),('repeat',w.WORD),('vk',w.WORD),('scan',w.WORD),('char',w.WCHAR),('ctrl',w.DWORD)]
class Event(ctypes.Union):
    _fields_=[('key',Key),('padding',ctypes.c_byte*16)]
class Record(ctypes.Structure):
    _fields_=[('type',w.WORD),('event',Event)]
k=ctypes.WinDLL('kernel32',use_last_error=True)
k.CreateFileW.restype=w.HANDLE
k.CreateFileW.argtypes=[w.LPCWSTR,w.DWORD,w.DWORD,w.LPVOID,w.DWORD,w.DWORD,w.HANDLE]
k.WriteConsoleInputW.argtypes=[w.HANDLE,ctypes.POINTER(Record),w.DWORD,ctypes.POINTER(w.DWORD)]
k.CloseHandle.argtypes=[w.HANDLE]
pid=int(sys.argv[1])
k.FreeConsole()
if not k.AttachConsole(pid):raise ctypes.WinError(ctypes.get_last_error())
h=k.CreateFileW('CONIN$',0x40000000,3,None,3,0,None)
text=sys.argv[2]
for c in text:
    event=Record();event.type=1;event.event.key=Key(True,1,13 if c=='\r' else 0,0,c,0)
    count=w.DWORD()
    if not k.WriteConsoleInputW(h,ctypes.byref(event),1,ctypes.byref(count)):raise ctypes.WinError(ctypes.get_last_error())
    time.sleep(0.005)
k.CloseHandle(h);k.FreeConsole()
