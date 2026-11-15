"""Exercise the real Android UI with an authorized device and disposable terminal fixtures.

Window-size overrides simulate phone widths on a tablet; they do not establish
physical phone compatibility. Original display, rotation and IME settings are restored.
Run against a running test host: --serial SERIAL --instance INSTANCE.
"""
from pathlib import Path
import argparse
import datetime
import json
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ET
from PIL import Image, ImageChops

parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--adb-port', default='5038')
parser.add_argument('--instance', required=True)
parser.add_argument('--language', choices=('zh-CN', 'en'), default='zh-CN')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
out = root / '.local/adaptive-acceptance' / args.language
out.mkdir(parents=True, exist_ok=True)
adb = [str(root / '.tools/platform-tools/adb.exe'), '-P', args.adb_port, '-s', args.serial]
env = dict(os.environ, CODELINK_INSTANCE=args.instance)
admin = ['dotnet', str(root / 'desktop/CodeLink.Smoke/bin/Release/net10.0/CodeLink.Smoke.dll'), '--admin']

def call(*parts):
    return subprocess.run(adb + list(parts), capture_output=True, check=True, timeout=25).stdout.decode('utf-8', errors='replace').strip()

def state():
    return json.loads(subprocess.run(admin + ['state'], env=env, capture_output=True, check=True, timeout=15).stdout)

def action(operation, **fields):
    request = out / 'request.json'
    request.write_text(json.dumps(fields), encoding='utf-8')
    return json.loads(subprocess.run(admin + ['action', operation, str(request)], env=env, capture_output=True, check=True, timeout=20).stdout)

def tree():
    for _ in range(6):
        process = subprocess.run(adb + ['shell','uiautomator','dump','--compressed','/sdcard/codelink-layout-test.xml'], capture_output=True, timeout=25)
        result = (process.stdout + process.stderr).decode('utf-8',errors='replace')
        if 'dumped to' in result:
            return ET.fromstring(call('shell', 'cat', '/sdcard/codelink-layout-test.xml'))
        time.sleep(.7)
    raise AssertionError('No fresh UI hierarchy: ' + result)

def node(description, view=None):
    view = tree() if view is None else view
    return next((n for n in view.iter('node') if n.get('content-desc') == description or n.get('text') == description), None)

def bounds(item):
    return tuple(map(int, re.findall(r'\d+', item.get('bounds'))))

def tap(description):
    item = node(description)
    assert item is not None and item.get('enabled') == 'true', description
    x1, y1, x2, y2 = bounds(item)
    assert x2 > x1 and y2 > y1, description
    call('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
    time.sleep(.35)

def screenshot(name, hierarchy=True):
    with (out / (name + '.png')).open('wb') as stream:
        subprocess.run(adb + ['exec-out', 'screencap', '-p'], stdout=stream, check=True, timeout=25)
    if not hierarchy:
        return None
    view = tree()
    (out / (name + '.xml')).write_bytes(ET.tostring(view, encoding='utf-8'))
    return view

checks = []
def check(ok, label):
    assert ok, label
    checks.append(label)
    print('PASS:', label, flush=True)

def wait(predicate, label, seconds=15):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        if predicate():
            return
        time.sleep(.3)
    raise AssertionError(label)

def list_page():
    if node('back-to-terminals') is not None:
        tap('back-to-terminals')

def viewport(width, height, density=480, rotation=0):
    call('shell', 'settings', 'put', 'system', 'accelerometer_rotation', '0')
    call('shell', 'settings', 'put', 'system', 'user_rotation', str(rotation))
    call('shell', 'wm', 'size', f'{width}x{height}')
    call('shell', 'wm', 'density', str(density))
    time.sleep(1.2)

def type_text(value):
    tap('command-input')
    call('shell', 'input', 'keycombination', '113', '29')
    call('shell', 'input', 'keyevent', '67')
    call('shell', 'input', 'text', value)
    time.sleep(.5)

def override(name):
    current = call('shell', 'wm', name)
    found = re.search(r'Override (?:size|density): (\S+)', current)
    return found.group(1) if found else 'reset'

original = {key: call('shell', 'settings', 'get', 'system', key) for key in ('accelerometer_rotation','user_rotation','font_scale')}
original_size, original_density = override('size'), override('density')
original_ime = call('shell', 'settings', 'get', 'secure', 'default_input_method')
call('shell','am','start','-n','com.codelink.app/.MainActivity')
wait(lambda:len([d for d in state()['devices'] if d['online']])==1,'Use one connected test device')
before = state()
devices = [d for d in before['devices'] if d['online']]
assert len(devices) == 1, 'Use one connected test device'
device_id = devices[0]['id']
sessions = []
record = out / 'received.txt'
record.write_text('', encoding='utf-8')
fixture = out / 'input-fixture.ps1'
fixture.write_text("Write-Output 'CODE LINK LAYOUT CHECK'; while ($true) { $value=Read-Host 'INPUT_READY'; Add-Content -LiteralPath '" + str(record).replace("'", "''") + "' -Value $value; Write-Output ('RECEIVED:' + $value) }", encoding='utf-8-sig')
try:
    for name in ('Layout input test', 'Second terminal'):
        sid = action('create', kind='terminal', name=name, cwd=str(out), command='powershell.exe -NoLogo -NoProfile -File "' + str(fixture) + '"')['sessionId']
        sessions.append(sid)
        action('map', sessionId=sid, deviceId=device_id, enabled=True, control=True)
    if 'com.android.inputmethod.latin/.LatinIME' in call('shell','ime','list','-s'):
        call('shell','ime','set','com.android.inputmethod.latin/.LatinIME')
    call('shell','am','start','-n','com.codelink.app/.MainActivity')
    time.sleep(2)
    viewport(1080,2340)
    list_page()
    view=screenshot('phone-360-list')
    check(node('terminal-list-pane',view) is not None and node('terminal-detail-pane',view) is None,'360dp phone shows a full-height terminal list')
    tap('session-card-'+sessions[0])
    view=screenshot('phone-360-terminal')
    check(node('terminal-detail-pane',view) is not None and node('terminal-list-pane',view) is None,'Opening a phone terminal replaces the list')
    check(node('back-to-terminals',view) is not None and node('send-command',view) is not None,'Phone navigation and send action remain visible')
    type_text('CODELINK_KEYBOARD')
    screenshot('phone-360-keyboard', hierarchy=False)
    ime=call('shell','dumpsys','input_method')
    check('mInputShown=true' in ime or 'isInputViewShown=true' in ime,'Soft keyboard was actually shown')
    # Check the rendered Send button and prove it works while the IME is open;
    # the accessibility hierarchy alone does not establish that it is unobscured.
    with Image.open(out/'phone-360-keyboard.png') as capture:
        width,height=capture.size
        region=capture.convert('RGB').crop((width//2,height//5,width,height))
        masks=[channel.point(lambda v, expected=target:255 if abs(v-expected)<=3 else 0) for channel,target in zip(region.split(),(233,187,69))]
        rect=ImageChops.multiply(ImageChops.multiply(masks[0],masks[1]),masks[2]).getbbox()
        check(rect is not None and rect[2]-rect[0]>width//12 and rect[3]+height//5<height*.75,'Send button is visibly above the open keyboard')
        call('shell','input','tap',str(width//2+(rect[0]+rect[2])//2),str(height//5+(rect[1]+rect[3])//2))
    wait(lambda:'CODELINK_KEYBOARD' in record.read_text(encoding='utf-8-sig'),'Keyboard-visible send did not reach Windows')
    check(True,'Sending with the soft keyboard open reaches the real Windows terminal')
    call('shell','input','keyevent','4')
    type_text('CODELINK_LAYOUT_360')
    call('shell','input','keyevent','4')
    viewport(1236,2676)
    check(node('command-input').get('text')=='CODELINK_LAYOUT_360','Draft survives resizing from 360dp to 412dp')
    screenshot('phone-412-terminal')
    call('shell','settings','put','system','user_rotation','1')
    time.sleep(1)
    view=screenshot('phone-landscape')
    check(node('command-input',view).get('text')=='CODELINK_LAYOUT_360','Draft survives rotation to phone landscape')
    call('shell','settings','put','system','user_rotation','0')
    time.sleep(1)
    check(node('command-input').get('text')=='CODELINK_LAYOUT_360','Draft survives phone landscape and return to portrait')
    tap('send-command')
    wait(lambda:'CODELINK_LAYOUT_360' in record.read_text(encoding='utf-8-sig'), 'Phone input did not reach the fixture')
    check(True,'Phone send reaches the real Windows terminal')
    type_text('DRAFT_FIRST_SESSION')
    call('shell','input','keyevent','4')
    tap('back-to-terminals')
    tap('session-card-'+sessions[1])
    check(node('command-input').get('text')!='DRAFT_FIRST_SESSION','Unsent text is not carried into another terminal')
    list_page();tap('session-card-'+sessions[0])
    check(node('command-input').get('text')=='DRAFT_FIRST_SESSION','Returning to a terminal restores its own draft')
    call('shell','input','keyevent','4')
    list_page()
    call('shell','settings','put','system','font_scale','1.3')
    time.sleep(1)
    screenshot('phone-large-text-list')
    tap('session-card-'+sessions[0])
    check(node('send-command') is not None,'Phone actions remain available at 130 percent font size')
    screenshot('phone-large-text-terminal')
    call('shell','settings','put','system','font_scale',original['font_scale'])
    viewport(1840,2944,340,1)
    view=screenshot('tablet-landscape')
    check(node('terminal-list-pane',view) is not None and node('terminal-detail-pane',view) is not None,'Tablet landscape shows list and detail together')
    check(node('command-input',view).get('text')=='DRAFT_FIRST_SESSION','Draft survives switching to the tablet layout')
    call('shell','settings','put','system','user_rotation','0')
    time.sleep(1)
    view=screenshot('tablet-portrait')
    check(node('terminal-list-pane',view) is not None and node('terminal-detail-pane',view) is not None,'Tablet portrait keeps two panes when enough width is available')
    tap('connection-options')
    tap('后台与通知' if args.language=='zh-CN' else 'Background & alerts')
    screenshot('tablet-settings')
    call('shell','input','keyevent','4')
    report={'date':datetime.datetime.now(datetime.timezone.utc).isoformat(),'language':args.language,'device':call('shell','getprop','ro.product.model'),'phoneCoverage':'360dp and 412dp window overrides on a physical tablet, not physical phone testing','checks':checks,'passed':True}
    (out/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
finally:
    for key,value in original.items():
        call('shell','settings','delete' if value=='null' else 'put','system',key,*(() if value=='null' else (value,)))
    call('shell','wm','size',original_size)
    call('shell','wm','density',original_density)
    if original_ime and original_ime!='null':call('shell','ime','set',original_ime)
    for sid in sessions:
        action('delete',sessionId=sid)
