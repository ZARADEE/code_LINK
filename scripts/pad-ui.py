"""Limited real-device UI smoke helpers for the Code Link application."""
from pathlib import Path
import subprocess, xml.etree.ElementTree as ET, re, sys, time
root=Path(__file__).resolve().parent.parent
exe=str(root/'.tools/platform-tools/adb.exe')
port=next((port for port in ('5038','5037') if subprocess.run([exe,'-P',port,'-s','HA20KTKJ','get-state'],capture_output=True).stdout.strip()==b'device'),None)
if port is None:raise RuntimeError('The test Pad is not connected through ADB')
adb=[exe,'-P',port,'-s','HA20KTKJ']
def call(*args):
    return subprocess.run(adb+list(args),check=True,capture_output=True,timeout=20).stdout.decode('utf-8',errors='replace')
def tree():
    # uiautomator can exit 0 with an idle-timeout error. Never read the old dump
    # in that case: it can describe a different tab or a previous app version.
    for _ in range(3):
        result=call('shell','uiautomator','dump','--compressed','/sdcard/codelink-test.xml')
        if 'dumped to' in result:
            return ET.fromstring(call('shell','cat','/sdcard/codelink-test.xml'))
        time.sleep(.3)
    raise RuntimeError('UI hierarchy dump did not produce a fresh snapshot: '+result)
def find(desc):
    for _ in range(3):
        for n in tree().iter('node'):
            if n.get('content-desc')==desc or n.get('text')==desc:
                coords=list(map(int,re.findall(r'\d+',n.get('bounds'))))
                return n,[(coords[0]+coords[2])//2,(coords[1]+coords[3])//2]
        time.sleep(.5)
    raise RuntimeError('UI element absent: '+desc)
def tap(desc):
    n,p=find(desc)
    if n.get('enabled')!='true':raise RuntimeError('UI element disabled: '+desc)
    call('shell','input','tap',*map(str,p))
if __name__=='__main__' and len(sys.argv)>1 and sys.argv[1]=='send':
    tap('command-input')
    call('shell','input','text',sys.argv[2].replace(' ','%s'))
    call('shell','input','keyevent','4')
    time.sleep(.3)
    tap('send-command')
elif __name__=='__main__' and len(sys.argv)>1 and sys.argv[1]=='tap':tap(sys.argv[2])
elif __name__=='__main__' and len(sys.argv)>1 and sys.argv[1]=='dump':
    for n in tree().iter('node'):
        if n.get('text') or n.get('content-desc'):print(n.attrib)
elif __name__=='__main__' and len(sys.argv)>1 and sys.argv[1]=='capture':
    call('shell','screencap','-p','/sdcard/codelink-test.png')
    call('pull','/sdcard/codelink-test.png',sys.argv[2])
