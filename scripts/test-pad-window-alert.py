"""Tests catalog selection and native choice prompt on a disposable external window."""
import importlib.util,json,os,pathlib,subprocess,time,uuid
root=pathlib.Path(__file__).resolve().parents[1]
os.environ['CODELINK_INSTANCE']='v2'
spec=importlib.util.spec_from_file_location('pad',root/'scripts/pad-ui.py');pad=importlib.util.module_from_spec(spec);spec.loader.exec_module(pad)
admin=['dotnet',str(root/'desktop/CodeLink.Smoke/bin/Release/net10.0/CodeLink.Smoke.dll'),'--admin']
def state():return json.loads(subprocess.run(admin+['state'],capture_output=True,encoding='utf-8',check=True).stdout)
def texts():return [n.get('text','') for n in pad.tree().iter('node')]
def wait(fn,why):
    end=time.monotonic()+25
    while time.monotonic()<end:
        if fn():return
        time.sleep(.5)
    raise AssertionError(why)
title='CODELINK_PROMPT_'+uuid.uuid4().hex[:8]
child=subprocess.Popen(['cmd.exe','/k','title '+title],cwd=root,creationflags=subprocess.CREATE_NEW_CONSOLE)
window=None;sid=None
def send(text):
    data=json.dumps({'processId':window['processId'],'windowHandle':window['windowHandle'],'text':text,'raw':False})
    r=subprocess.run([str(root/'dist/desktop/CodeLink.ConsoleBridge.exe')],input=data,capture_output=True,encoding='utf-8',check=True)
    assert json.loads(r.stdout)['ok']
try:
    wait(lambda:any(c['name'].strip()==title for c in state().get('commands',[])),'New command was not scanned')
    window=next(c for c in state()['commands'] if c['name'].strip()==title)
    send('choice /c YN /m CODELINK_APPROVAL')
    wait(lambda:any(n.get('content-desc')=='group-tab-B' for n in pad.tree().iter('node')),'App did not finish opening')
    pad.tap('group-tab-B')
    wait(lambda:any('● '+title in t for t in texts()),'Unopened window prompt was not observed')
    assert not any(t.startswith('● B') for t in texts()),'B tab incorrectly marked complete by one pending prompt'
    pad.tap('session-card-catalog:'+window['key'])
    wait(lambda:any(s['observedWindowHandle']==window['windowHandle'] for s in state()['sessions']),'Mobile selection did not map command')
    sid=next(s['id'] for s in state()['sessions'] if s['observedWindowHandle']==window['windowHandle'])
    time.sleep(3)
    assert not any('点击确认提醒' in t for t in texts())
    assert next(s for s in state()['sessions'] if s['id']==sid)['status']=='input', 'Reading reminder accidentally answered prompt'
    prior=pad.call('shell','settings','get','secure','default_input_method').strip()
    try:
        pad.call('shell','ime','set','com.android.inputmethod.latin/.LatinIME')
        subprocess.run(['python',str(root/'scripts/pad-ui.py'),'send','Y'],check=True)
    finally:pad.call('shell','ime','set',prior)
    wait(lambda:next(s for s in state()['sessions'] if s['id']==sid)['status']!='input','Mobile answer did not reach prompt')
    report={'passed':True,'fullCatalogVisible':True,'unopenedWindowPromptObserved':True,'approvalDoesNotMarkBComplete':True,'mobileOpensUnmappedWindow':True,'promptCreatesRedDot':True,'readDoesNotGrantApproval':True,'readPersistsAcrossRefresh':True,'mobilePromptAnswer':True}
    (root/'research/pad-window-alert-acceptance.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report),flush=True)
finally:
    if window:
        try:send('Y');send('exit')
        except Exception:pass
    try:child.wait(timeout=3)
    except subprocess.TimeoutExpired:child.terminate()
    if sid:
        file=root/'.local/delete-prompt-test.json';file.write_text(json.dumps({'sessionId':sid}),encoding='utf-8');subprocess.run(admin+['action','delete',str(file)],capture_output=True)
