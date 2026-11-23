"""Real Android full-screen and PC-resolved reminder checks on a disposable terminal."""
import json,os,re,runpy,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];u=runpy.run_path(str(ROOT/'scripts/pad-ui.py'))
mode=ROOT/'.local/phone-reminder-mode.txt';mode.write_text('running')
def run(*args):return subprocess.check_output(list(args),cwd=ROOT,text=True,encoding='utf-8')
def nodes():return list(u['tree']().iter('node'))
def has(desc):return any(n.get('content-desc')==desc for n in nodes())
def wait(check):
    deadline=time.monotonic()+30
    while time.monotonic()<deadline:
        if check():return
        time.sleep(.5)
    raise AssertionError('Phone UI did not reach the expected state')
def rect(desc):
    n=next(n for n in nodes() if n.get('content-desc')==desc or n.get('resource-id')==desc)
    return list(map(int,re.findall(r'\d+',n.get('bounds'))))
def runtime():
    dump=u['call']('shell','dumpsys','activity','service','com.codelink.app/.LinkService')
    return json.loads(re.search(r'CodeLinkRuntime=(.*)',dump)[1])
child=subprocess.Popen(['cmd.exe','/c',sys.executable,'-u',str(ROOT/'scripts/phone-reminder-fixture.py')],cwd=ROOT,creationflags=subprocess.CREATE_NEW_CONSOLE)
mapped=False
try:
    wait(lambda:runtime()['online'])
    if has('exit-terminal-fullscreen'):u['tap']('exit-terminal-fullscreen')
    time.sleep(3)
    run('node','scripts/scroll-acceptance.cjs','setup','--reminder');mapped=True
    test=json.loads((ROOT/'.local/scroll-test-session.json').read_text())
    time.sleep(2)
    for _ in range(3):
        if runtime()['selected']==test['sessionId'] and has('terminal-fullscreen'):break
        if has('back-to-terminals'):u['tap']('back-to-terminals')
        if has('session-card-'+test['sessionId']):u['tap']('session-card-'+test['sessionId']);break
        if has('group-tab-B'):u['tap']('group-tab-B')
    u['call']('reverse','--remove','tcp:47631')
    wait(lambda:runtime()['selected']==test['sessionId'])
    portrait=rect('windowText');u['tap']('terminal-fullscreen');wait(lambda:has('exit-terminal-fullscreen'));time.sleep(2)
    landscape=rect('windowText');assert landscape[2]-landscape[0]>landscape[3]-landscape[1]
    assert landscape[2]-landscape[0]>1.5*(portrait[2]-portrait[0]),'Landscape must enlarge the terminal'
    u['tap']('fullscreen-input');wait(lambda:has('command-input'));u['tap']('fullscreen-input')
    u['tap']('exit-terminal-fullscreen');wait(lambda:has('terminal-fullscreen'));time.sleep(2)
    restored=rect('windowText');assert abs(restored[2]-restored[0]-(portrait[2]-portrait[0]))<10
    assert runtime()['selected']==test['sessionId'],'Full screen changed the selected session'
    u['tap']('terminal-fullscreen');wait(lambda:has('exit-terminal-fullscreen'));u['call']('shell','input','keyevent','4');wait(lambda:has('terminal-fullscreen'))
    mode.write_text('approval');wait(lambda:has('confirm-reminder'))
    def request_unread():return any(t['id']==test['sessionId'] and t['unread'] for t in runtime()['terminals'])
    assert request_unread(),'Authorization did not create a reminder'
    notifications=u['call']('shell','dumpsys','notification','--noredact')
    marker='codelink.kind=String (approval)'
    assert marker in notifications,'Authorization system notification missing'
    mode.write_text('running');wait(lambda:not has('confirm-reminder'))
    assert not request_unread(),'PC resolution left a red dot'
    assert not any('等待你的授权' in n.get('text','') for n in nodes()),'Obsolete authorization banner remains'
    assert marker not in u['call']('shell','dumpsys','notification','--noredact'),'Resolved system notification remains'
    report={'date':'2026-11-23','model':u['call']('shell','getprop','ro.product.model').strip(),'remotePath':runtime()['remote'].get('path'),
            'landscapeFullscreen':True,'largerTerminalViewport':True,'fullscreenInputToggle':True,'topLeftBackRestoresLayout':True,
            'systemBackExitsFullscreen':True,'selectedSessionPreserved':True,'pcApprovalClearsReminder':True,
            'pcApprovalClearsBanner':True,'pcApprovalCancelsSystemNotification':True,'passed':True}
    (ROOT/'research/phone-ui-acceptance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(json.dumps(report,ensure_ascii=False))
finally:
    if has('exit-terminal-fullscreen'):u['tap']('exit-terminal-fullscreen')
    mode.write_text('stop')
    try:child.wait(timeout=5)
    except subprocess.TimeoutExpired:child.terminate()
    if mapped:run('node','scripts/scroll-acceptance.cjs','cleanup')
