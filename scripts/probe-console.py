import ctypes,sys,json
from ctypes import wintypes as w
k=ctypes.WinDLL('kernel32',use_last_error=True)
k.GetConsoleWindow.restype=w.HWND
results=[]
for value in sys.argv[1:]:
 k.FreeConsole()
 if not k.AttachConsole(int(value)):continue
 b=ctypes.create_unicode_buffer(4096);k.GetConsoleTitleW(b,len(b))
 results.append({'pid':int(value),'title':b.value,'consoleWindow':k.GetConsoleWindow()})
k.FreeConsole()
print(json.dumps(results,ensure_ascii=False))
