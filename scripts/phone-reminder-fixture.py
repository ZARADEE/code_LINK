"""Disposable terminal for PC-resolved authorization acceptance; executes no commands."""
import ctypes,time
from pathlib import Path
k=ctypes.windll.kernel32;k.SetConsoleTitleW('CODELINK_PHONE_UI_TEST')
out=k.GetStdHandle(-11);mode=ctypes.c_ulong();k.GetConsoleMode(out,ctypes.byref(mode));k.SetConsoleMode(out,mode.value|4)
control=Path(__file__).resolve().parents[1]/'.local/phone-reminder-mode.txt'
prior=None
while True:
    current=control.read_text().strip() if control.exists() else 'running'
    if current=='stop':break
    if current!=prior:
        text=('Would you like to run the following command?\r\n1. Yes\r\n2. No, and tell Codex\r\nesc to cancel' if current=='approval' else '• Working (1s · esc to interrupt)\r\nPC handled the test prompt; task is running.')
        print('\x1b[H\x1b[2J'+text,end='',flush=True);prior=current
    time.sleep(.2)
