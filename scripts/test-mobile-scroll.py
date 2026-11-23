"""Real PAD swipes against disposable Windows Terminal fixtures (no ADB forwarding)."""
import json, re, runpy, subprocess, sys, time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
ui=runpy.run_path(str(ROOT/'scripts/pad-ui.py'))
def run(*args):
    return subprocess.check_output(list(args),cwd=ROOT,text=True,encoding='utf-8').strip()
def state():return json.loads(run('node','scripts/scroll-acceptance.cjs','state'))
def window():return json.loads(run('powershell','-NoProfile','-File','scripts/read-scroll-window.ps1'))
def wait(read,check):
    end=time.monotonic()+20
    while time.monotonic()<end:
        value=read()
        if check(value):return value
        time.sleep(.3)
    raise AssertionError('Expected scroll state did not arrive: '+str(value))
def swipe(direction):
    n=next(n for n in ui['tree']().iter('node') if n.get('resource-id')=='windowText')
    left,top,right,bottom=map(int,re.findall(r'\d+',n.get('bounds')))
    x=(left+right)//2;y1=top+(bottom-top)//4;y2=bottom-(bottom-top)//4
    if direction=='up':y1,y2=y2,y1
    ui['call']('shell','input','swipe',str(x),str(y1),str(x),str(y2),'650')
def select(alt):
    run('node','scripts/scroll-acceptance.cjs','setup',*(['--alt'] if alt else []))
    config=json.loads((ROOT/'.local/scroll-test-session.json').read_text())
    ui['tap']('session-card-'+config['sessionId'])
    mode,_=ui['find']('terminal-scroll-mode')
    if mode.get('text')=='滑动：本地查看':ui['tap']('terminal-scroll-mode')
    return config
result={'date':'2026-11-23','device':'Lenovo TB371FC / Android 14','transport':'Remote',
        'network':'PAD on phone hotspot; PC on campus Wi-Fi; USB for ADB gestures only'}
select(True)
alt=lambda:json.loads((ROOT/'.local/scroll-alt-state.json').read_text())
start=alt()['position'];swipe('down');earlier=wait(alt,lambda s:s['position']<start)
view=wait(state,lambda s:f"ALT_LINE_{earlier['position']:04d}" in s['viewport'])
assert view['remotePath']=='relay'
swipe('up');later=wait(alt,lambda s:s['position']>earlier['position'])
result['fullScreen']={'before':start,'afterDown':earlier['position'],'afterUp':later['position'],
                      'receivedRealMouseReports':64 in earlier['events'] and 65 in later['events'],
                      'viewportSynced':True}
assert result['fullScreen']['receivedRealMouseReports']
select(False)
ui['tap']('terminal-scroll-latest');start=wait(window,lambda s:s['value']==s['maximum'])
swipe('down');earlier=wait(window,lambda s:s['value']<start['value'])
wait(state,lambda s:earlier['visibleFirstLine'].strip() in s['viewport'])
swipe('up');later=wait(window,lambda s:s['value']>earlier['value'])
ui['tap']('terminal-scroll-mode');ui['tap']('terminal-scroll-latest');beforeLocal=window()['value'];swipe('down');time.sleep(1)
assert window()['value']==beforeLocal,'Local browsing moved the PC window'
if '--capture' in sys.argv:
    ui['call']('shell','screencap','-p','/sdcard/codelink-scroll.png')
    ui['call']('pull','/sdcard/codelink-scroll.png',str(ROOT/'.local/scroll-local.png'))
ui['tap']('terminal-scroll-mode');ui['tap']('terminal-scroll-latest')
latest=wait(window,lambda s:s['value']==s['maximum'])
result['normalHistory']={'before':start['value'],'afterDown':earlier['value'],'afterUp':later['value'],
                         'latest':latest['value'],'localModeLeavesDesktopUnchanged':True,'viewportSynced':True}
result['actualPath']=view['remotePath'];result['passed']=True
(ROOT/'research/scroll-acceptance.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
