"""Disposable full-screen terminal: real VT mouse reports drive its viewport."""
import ctypes, json, msvcrt, os, re, sys
from pathlib import Path
k=ctypes.windll.kernel32
k.SetConsoleTitleW('CODELINK_SCROLL_ALT')
stdin=k.GetStdHandle(-10);stdout=k.GetStdHandle(-11)
old_in=ctypes.c_ulong();old_out=ctypes.c_ulong()
k.GetConsoleMode(stdin,ctypes.byref(old_in));k.GetConsoleMode(stdout,ctypes.byref(old_out))
k.SetConsoleMode(stdout,old_out.value|4)
k.SetConsoleMode(stdin,(old_in.value|0x200)&~(2|4|0x40))
msvcrt.setmode(sys.stdin.fileno(),os.O_BINARY)
position=200;events=[];pending='';received=''
state=Path(__file__).resolve().parents[1]/'.local/scroll-alt-state.json'
def draw():
    state.write_text(json.dumps({'position':position,'events':events[-20:],'raw':received[-200:]}),encoding='utf-8')
    print('\x1b[H\x1b[2J'+''.join(f'ALT_LINE_{i:04d}  full-screen mouse wheel test\r\n' for i in range(position,position+24)),end='',flush=True)
try:
    print('\x1b[?1049h\x1b[?1000h\x1b[?1006h',end='',flush=True)
    draw()
    while True:
        ch=os.read(sys.stdin.fileno(),1).decode('utf-8',errors='replace')
        if ch=='q':break
        received+=ch
        pending=(pending+ch)[-100:]
        match=re.search(r'\x1b\[<(\d+);\d+;\d+M$',pending)
        if match:
            button=int(match[1]);events.append(button)
            if button in (64,65):position=max(1,min(200,position+(-3 if button==64 else 3)))
            pending='';draw()
finally:
    print('\x1b[?1006l\x1b[?1000l\x1b[?1049l',end='',flush=True)
    k.SetConsoleMode(stdin,old_in.value);k.SetConsoleMode(stdout,old_out.value)
