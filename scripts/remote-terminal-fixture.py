"""Finite acceptance terminal; no shell commands or user's files are executed."""
import sys
import time
from pathlib import Path

if len(sys.argv)>1 and sys.argv[1]=='--gate':
    gate=Path(sys.argv[2])
    print('REMOTE BACKGROUND RUNNING',flush=True)
    while not gate.exists():
        time.sleep(.5)
    print('REMOTE BACKGROUND COMPLETED',flush=True)
    raise SystemExit(0)

print("REMOTE READY",flush=True)
while True:
    value=sys.stdin.readline()
    if not value:
        break
    value=value.rstrip("\r\n")
    if value=="EXIT":
        print("REMOTE COMPLETED",flush=True)
        break
    print("RECEIVED:"+value,flush=True)
