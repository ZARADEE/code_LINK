"""Finish the background acceptance after the owner unlocks the test Pad."""
import importlib.util,json,os,pathlib,re,subprocess,sys,time
root=pathlib.Path(__file__).resolve().parents[1];os.environ['CODELINK_INSTANCE']='v2'
work=pathlib.Path(sys.argv[1]).resolve();assert work.parent==root/'.local'
journal=json.loads((work/'resume.json').read_text(encoding='utf-8'))
spec=importlib.util.spec_from_file_location('pad',root/'scripts/pad-ui.py');pad=importlib.util.module_from_spec(spec);spec.loader.exec_module(pad)
admin=['dotnet',str(root/'desktop/CodeLink.Smoke/bin/Release/net10.0/CodeLink.Smoke.dll'),'--admin']
def runtime():return json.loads(re.search(r'CodeLinkRuntime=(.+)',pad.call('shell','dumpsys','activity','service','com.codelink.app/.LinkService'))[1])
def action(op,**fields):
 f=work/'resume-action.json';f.write_text(json.dumps(fields),encoding='utf-8')
 return json.loads(subprocess.run(admin+['action',op,str(f)],capture_output=True,encoding='utf-8',check=True).stdout)
def wait(fn,why):
 for _ in range(60):
  if fn():return
  time.sleep(.3)
 raise AssertionError(why)
pad.call('shell','cmd','statusbar','collapse');pad.call('shell','am','start','-n','com.codelink.app/.MainActivity');time.sleep(3)
wait(lambda:runtime()['visibleActivities']==1,'Pad must be unlocked before resuming')
assert runtime()['online'] and runtime()['selected']==journal['selected']
assert runtime()['cachedOutputSha256']==journal['outputHash'],'Latest background output changed before restoration'
pad.find('BACKGROUND_SCREEN_OFF')
report=json.loads((root/'research/pad-background-acceptance.json').read_text(encoding='utf-8'))
report['returnRestoresLatestContent']=True
for sid in journal['ids']:action('delete',sessionId=sid)
for sid in journal['moved']:
 action('map',sessionId=sid,deviceId='777abadb-fb91-4413-9a97-ffd6aefcee6b',enabled=True,control=True)
 wait(lambda:runtime()['selected']==sid,'Original terminal was not selected')
 if next(t for t in runtime()['terminals'] if t['id']==sid)['group']!='B':pad.tap('toggle-session-group')
 wait(lambda:next(t for t in runtime()['terminals'] if t['id']==sid)['group']=='B','Original group was not restored')
report['originalGroupsRestored']=True;report['passed']=True
(root/'research/pad-background-acceptance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report),flush=True)
