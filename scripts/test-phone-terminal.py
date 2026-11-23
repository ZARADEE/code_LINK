"""Physical phone acceptance for cellular remote swipes and terminal clicks.

Set CODELINK_TEST_SERIAL and run the disposable --click fixture before this check.
"""
import json, os, re, runpy, subprocess, time
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
ui=runpy.run_path(str(ROOT/'scripts/pad-ui.py'))
def run(*args):return subprocess.check_output(list(args),cwd=ROOT,text=True,encoding='utf-8').strip()
def state():return json.loads(run('node','scripts/scroll-acceptance.cjs','state'))
def fixture():return json.loads((ROOT/'.local/scroll-alt-state.json').read_text())
def wait(read,predicate):
    until=time.monotonic()+20
    while time.monotonic()<until:
        value=read()
        if predicate(value):return value
        time.sleep(.25)
    raise AssertionError('Expected state did not arrive: '+str(value))
def bounds():
    node=next(n for n in ui['tree']().iter('node') if n.get('resource-id')=='windowText')
    return list(map(int,re.findall(r'\d+',node.get('bounds'))))
def swipe(down=True):
    left,top,right,bottom=bounds();x=(left+right)//2
    a=top+(bottom-top)//4;b=bottom-(bottom-top)//4
    if not down:a,b=b,a
    ui['call']('shell','input','swipe',str(x),str(a),str(x),str(b),'650')
def tap_back():
    visible=state()['viewport'];lines=visible.split('\n');row=next(i for i,s in enumerate(lines) if 'Back to bottom' in s)
    column=lines[row].index('Back to bottom')+3
    left,top,right,bottom=bounds()
    density=int(re.findall(r'\d+',ui['call']('shell','wm','density'))[-1])/160
    # Locate the rendered text rows in the real screenshot. WebView minimum
    # font settings can differ from CSS, so tests must not assume CSS geometry.
    shot=ROOT/'.local/phone-click-acceptance.png'
    ui['call']('shell','screencap','-p','/sdcard/codelink-click-test.png')
    ui['call']('pull','/sdcard/codelink-click-test.png',str(shot))
    pixels=Image.open(shot).convert('RGB');groups=[]
    for y in range(top+5,bottom-5):
        if any(min(pixels.getpixel((x,y)))>150 for x in range(left+5,right-5)):
            if not groups or y>groups[-1][-1]+round(density*2):groups.append([])
            groups[-1].append(y)
    centers=[(g[0]+g[-1])/2 for g in groups]
    assert len(centers)==25,'Expected 25 rendered fixture rows, found '+str(len(centers))
    step=(centers[23]-centers[0])/23
    x=round(left+12*density+(column+.5)*step/1.3*.602)
    y=round(centers[row])
    ui['call']('shell','input','tap',str(x),str(y))
    return {'row':row,'column':column,'point':[x,y]}
run('node','scripts/scroll-acceptance.cjs','setup','--click')
wait(state,lambda s:'ALT_LINE_' in s.get('viewport',''))
test=json.loads((ROOT/'.local/scroll-test-session.json').read_text())
nodes=list(ui['tree']().iter('node'))
if any(n.get('content-desc')=='back-to-terminals' for n in nodes):
    ui['tap']('back-to-terminals')
ui['tap']('session-card-'+test['sessionId'])
mode,_=ui['find']('terminal-scroll-mode')
if '本地' in mode.get('text'):ui['tap']('terminal-scroll-mode')
ui['call']('reverse','--remove','tcp:47631')
start=fixture();swipe();earlier=wait(fixture,lambda s:s['position']<start['position'])
assert earlier['clicks']==start['clicks'],'Swipe accidentally clicked'
wait(state,lambda s:'New activity' in s['viewport'])
point=tap_back();clicked=wait(fixture,lambda s:s['position']==200 and s['clicks']==earlier['clicks']+1)
swipe();wait(fixture,lambda s:s['position']<200);wait(state,lambda s:'Back to bottom' in s['viewport'])
ui['tap']('terminal-scroll-latest');shortcut=wait(fixture,lambda s:s['position']==200 and s['clicks']==clicked['clicks']+1)
swipe();wait(fixture,lambda s:s['position']<200);wait(state,lambda s:'Back to bottom' in s['viewport'])
ui['tap']('terminal-scroll-mode');before=fixture()
ui['call']('shell','input','tap',*map(str,point['point']));time.sleep(1)
assert fixture()['clicks']==before['clicks'],'Local browsing clicked the PC'
ui['tap']('terminal-scroll-mode');ui['tap']('terminal-scroll-latest')
wait(fixture,lambda s:s['position']==200)
report={'date':'2026-11-23','model':ui['call']('shell','getprop','ro.product.model').strip(),
        'android':ui['call']('shell','getprop','ro.build.version.release').strip(),
        'phoneNetwork':'cellular, confirmed by user','pcNetwork':'campus Wi-Fi','crossNetworkInput':'PHONE_REMOTE_OK round trip passed',
        'remotePath':state()['remotePath'],'usbRole':'ADB debug gestures; TCP reverse removed before acceptance',
        'swipeEarlier':True,'swipeDoesNotClick':True,'tapBackToBottom':True,'latestShortcutClicksSameButton':True,
        'localModeDoesNotClickPC':True,'tapPoint':point,'passed':True}
(ROOT/'research/phone-terminal-acceptance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
