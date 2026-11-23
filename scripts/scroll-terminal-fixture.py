"""Disposable terminal with numbered history for swipe acceptance."""
import ctypes
import sys
import time
ctypes.windll.kernel32.SetConsoleTitleW('CODELINK_SCROLL_TEST')
for line in range(1,241):print(f'SCROLL_LINE_{line:04d}  terminal history for swipe test',flush=True)
print('SCROLL_READY',flush=True)
while True:
    value=sys.stdin.readline()
    if not value:time.sleep(.2);continue
    if value.strip()=='EXIT':break
    print('SCROLL_INPUT:'+value.strip(),flush=True)
