"""Real Pad UI plus real finite PowerShell processes; no model calls or user's inputs."""
import importlib.util,json,os,pathlib,subprocess,time,uuid
root=pathlib.Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('pad',root/'scripts/pad-ui.py');pad=importlib.util.module_from_spec(spec);spec.loader.exec_module(pad)
os.environ['CODELINK_INSTANCE']='v2'
work=root/'.local'/('reminder-test-'+uuid.uuid4().hex);work.mkdir()
admin=['dotnet',str(root/'desktop/CodeLink.Smoke/bin/Release/net10.0/CodeLink.Smoke.dll'),'--admin']
def action(operation,**fields):
    request=work/'request.json';request.write_text(json.dumps(fields),encoding='utf-8')
    result=subprocess.run(admin+['action',operation,str(request)],capture_output=True,encoding='utf-8',check=True)
    return json.loads(result.stdout)
def state():return json.loads(subprocess.run(admin+['state'],capture_output=True,encoding='utf-8',check=True).stdout)
def texts():return [n.get('text','') for n in pad.tree().iter('node')]
def has(value):return any(value in t for t in texts())
def wait(predicate,why):
    end=time.monotonic()+18
    while time.monotonic()<end:
        if predicate():return
        time.sleep(.3)
    raise AssertionError(why)
def tap(desc):pad.tap(desc);time.sleep(.5)
def create(name):
    gate=work/(name+'.done')
    cmd='powershell.exe -NoLogo -NoProfile -File "'+str(root/'scripts/reminder-fixture.ps1')+'" -GatePath "'+str(gate)+'"'
    sid=action('create',kind='terminal',name=name,cwd=str(work),command=cmd)['sessionId']
    action('map',sessionId=sid,deviceId='777abadb-fb91-4413-9a97-ffd6aefcee6b',enabled=True,control=True)
    wait(lambda:has(name),'Mapped task did not appear')
    return sid,gate
report={}
sessions=[]
try:
    a,gate_a=create('REMINDER_A');sessions.append(a)
    tap('toggle-session-group');wait(lambda:has('A 模式'),'Mode switch failed');report['modeToggle']=True
    tap('rename-session');tap('session-name-input')
    prior=pad.call('shell','settings','get','secure','default_input_method').strip()
    try:
        pad.call('shell','ime','set','com.android.inputmethod.latin/.LatinIME')
        pad.call('shell','input','keycombination','113','29')
        pad.call('shell','input','text','Renamed-A')
        pad.call('shell','input','keyevent','4')
        tap('保存')
    finally:pad.call('shell','ime','set',prior)
    wait(lambda:has('Renamed-A'),'Rename failed');report['rename']=True
    b1,gate_b1=create('REMINDER_B1');sessions.append(b1)
    b2,gate_b2=create('REMINDER_B2');sessions.append(b2)
    assert has('B 模式');report['defaultB']=True
    gate_a.touch();wait(lambda:has('● A'),'A completion red dot missing')
    tap('group-tab-A');tap('session-card-'+a)
    wait(lambda:has('Renamed-A') and has('本轮完成'),'A individual completion missing')
    assert not has('点击确认提醒');report['individualCompletionAndRead']=True
    gate_b1.touch();tap('group-tab-B')
    wait(lambda:has('本轮完成 1 / 2'),'Partial B progress incorrect')
    assert not has('本轮全部完成');report['bWaitsForAll']=True
    gate_b2.touch();wait(lambda:has('本轮全部完成'),'B final task did not aggregate')
    assert has('● B');report['bAggregateCompletion']=True
    print('A individual and B aggregate passed; restarting app',flush=True)
    pad.call('shell','am','force-stop','com.codelink.app')
    pad.call('shell','am','start','-n','com.codelink.app/.MainActivity')
    time.sleep(3)
    wait(lambda:has('已连接'),'App did not reconnect')
    tap('group-tab-B');wait(lambda:has('本轮全部完成'),'Unread batch lost after restart: '+str(texts()))
    report['unreadPersistsAfterRestart']=True
    tap('group-summary');assert not has('本轮全部完成');report['batchReadAcknowledgement']=True
    tap('group-tab-A');assert has('Renamed-A');report['renameAndGroupPersist']=True
    # Rename back so the fixture leaves no confusing alias in acceptance captures.
    tap('session-card-'+a);tap('rename-session');tap('恢复窗口名称')
    report['passed']=True
    (root/'research/pad-reminder-acceptance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report),flush=True)
except Exception:
    (work/'failure-ui.json').write_text(json.dumps(texts(),ensure_ascii=False,indent=2),encoding='utf-8')
    raise
finally:
    for sid in sessions:
        try:action('delete',sessionId=sid)
        except Exception as e:print('Fixture cleanup failed:',e,flush=True)
