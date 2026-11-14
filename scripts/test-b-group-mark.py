"""Real Pad regression: all B tasks finish, then each read clears its dot."""
import importlib.util,json,os,pathlib,re,subprocess,time,uuid
root=pathlib.Path(__file__).resolve().parents[1];os.environ['CODELINK_INSTANCE']='v2'
spec=importlib.util.spec_from_file_location('pad',root/'scripts/pad-ui.py');pad=importlib.util.module_from_spec(spec);spec.loader.exec_module(pad)
admin=['dotnet',str(root/'desktop/CodeLink.Smoke/bin/Release/net10.0/CodeLink.Smoke.dll'),'--admin']
work=root/'.local'/('b-mark-'+uuid.uuid4().hex);work.mkdir();ids=[];moved=[]
def state():return json.loads(subprocess.run(admin+['state'],capture_output=True,encoding='utf-8',check=True).stdout)
def action(op,**fields):
 f=work/'action.json';f.write_text(json.dumps(fields),encoding='utf-8');return json.loads(subprocess.run(admin+['action',op,str(f)],capture_output=True,encoding='utf-8',check=True).stdout)
def wait(sid):
 for _ in range(60):
  if next(s for s in state()['sessions'] if s['id']==sid)['status']=='completed':return
  time.sleep(.2)
 raise AssertionError('Fixture process did not finish')
def nodes():return {n.get('content-desc'):n for n in pad.tree().iter('node') if n.get('content-desc')}
def marked(ui,desc):return ui[desc].get('text','').startswith('\u25cf')
def check_dots(first,second,group):
 deadline=time.monotonic()+45
 while True:
  ui=nodes()
  expected={'group-tab-B':group,**{'session-card-'+sid:dot for sid,dot in zip(ids,(first,second))}}
  if all(k in ui and marked(ui,k)==v for k,v in expected.items()):return
  if time.monotonic()>=deadline:
   observed={k:v.get('text','') for k,v in ui.items() if k.startswith(('group-','session-card-','toggle-session-'))}
   (work/'failure-ui.json').write_text(json.dumps(observed,ensure_ascii=False,indent=2),encoding='utf-8')
   raise AssertionError('Completion dots did not settle: '+json.dumps(observed,ensure_ascii=True))
  # Reconnection or a changed selected session can switch the visible tab.
  # Inspect B explicitly before asserting the member cards' read state.
  if any('session-card-'+sid not in ui for sid in ids):pad.tap('group-tab-B')
  time.sleep(.5)
def select(sid):
 desc='session-card-'+sid
 ui=nodes()
 for group in ('A','B','A','B'):
  if desc in ui:
   p=list(map(int,re.findall(r'\d+',ui[desc].get('bounds'))))
   pad.call('shell','input','tap',str((p[0]+p[2])//2),str((p[1]+p[3])//2));time.sleep(.5);return
  pad.tap('group-tab-'+group);time.sleep(.5);ui=nodes()
 raise AssertionError('Existing terminal card not found: '+sid)
try:
 # Temporarily isolate the real running Codex session from the finite test
 # batch, using the normal Pad group control. Restore it in finally.
 for s in state()['sessions']:
  if s['status'] not in ('running','approval','input'):continue
  select(s['id'])
  if nodes()['toggle-session-group'].get('text','').startswith('B '):
   pad.tap('toggle-session-group');moved.append(s['id']);time.sleep(.5)
   assert nodes()['toggle-session-group'].get('text','').startswith('A '),'Existing task did not leave B'
 print('Existing running terminals isolated; starting two finite B tasks',flush=True)
 for i in (1,2):
  command='powershell.exe -NoLogo -NoProfile -File "'+str(root/'scripts/reminder-fixture.ps1')+'" -GatePath "'+str(work/str(i))+'"'
  sid=action('create',kind='terminal',name='B_MARK_'+str(i),cwd=str(work),command=command)['sessionId'];ids.append(sid)
  action('map',sessionId=sid,deviceId='777abadb-fb91-4413-9a97-ffd6aefcee6b',enabled=True,control=True)
 time.sleep(2);(work/'1').touch();wait(ids[0]);time.sleep(1)
 check_dots(False,False,False)
 (work/'2').touch();wait(ids[1]);time.sleep(1)
 check_dots(True,True,True)
 print('Partial completion stays quiet; all complete shows B and member dots',flush=True)
 pad.tap('session-card-'+ids[0]);time.sleep(.5);check_dots(False,True,True)
 pad.call('shell','am','force-stop','com.codelink.app')
 pad.call('shell','am','start','-n','com.codelink.app/.MainActivity');time.sleep(3)
 pad.tap('group-tab-B');check_dots(False,True,True)
 print('First read clears only its own dot; partial read survives app restart',flush=True)
 pad.tap('session-card-'+ids[1]);time.sleep(.5);check_dots(False,False,False)
 assert not nodes()['group-summary'].get('text','').startswith('\u25cf'),'Summary dot remains after final read'
 pad.call('shell','am','force-stop','com.codelink.app')
 pad.call('shell','am','start','-n','com.codelink.app/.MainActivity');time.sleep(3)
 pad.tap('group-tab-B');check_dots(False,False,False)
 result={'passed':True,'oneFinishedBStaysPlain':True,'allFinishedBMarked':True,'memberDotsAppearOnlyAfterAllComplete':True,'firstReadClearsOnlyItsDot':True,'partialReadPersistsAfterRestart':True,'lastReadClearsBTabAndSummary':True,'fullyReadStaysClearAfterRestart':True,'appVersion':'1.1.1'}
 print('All completion/read/restart assertions passed; restoring original groups',flush=True)
finally:
 for sid in ids:
  try:action('delete',sessionId=sid)
  except Exception as e:print('Fixture cleanup failed:',e,flush=True)
 for sid in moved:
  select(sid)
  if nodes()['toggle-session-group'].get('text','').startswith('A '):pad.tap('toggle-session-group')
result['originalGroupsRestored']=True
(root/'research/b-group-mark-acceptance.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result),flush=True)
