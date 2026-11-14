"""Real finite tasks, foreground-service lifecycle, system banners and screen-off.
Only test fixtures receive input. Original groups and DND are restored in finally.
"""
import importlib.util,json,os,pathlib,re,subprocess,time,uuid
root=pathlib.Path(__file__).resolve().parents[1];os.environ['CODELINK_INSTANCE']='v2'
spec=importlib.util.spec_from_file_location('pad',root/'scripts/pad-ui.py');pad=importlib.util.module_from_spec(spec);spec.loader.exec_module(pad)
admin=['dotnet',str(root/'desktop/CodeLink.Smoke/bin/Release/net10.0/CodeLink.Smoke.dll'),'--admin']
work=root/'.local'/('background-'+uuid.uuid4().hex);work.mkdir();ids=[];moved=[];report={};group_position=None
device='777abadb-fb91-4413-9a97-ffd6aefcee6b'
def state():return json.loads(subprocess.run(admin+['state'],capture_output=True,encoding='utf-8',check=True).stdout)
def action(op,**fields):
 f=work/'action.json';f.write_text(json.dumps(fields),encoding='utf-8')
 process=subprocess.run(admin+['action',op,str(f)],capture_output=True,encoding='utf-8')
 if process.returncode:raise RuntimeError(op+': '+process.stderr+' '+process.stdout)
 result=json.loads(process.stdout)
 assert result.get('ok',True),result
 return result
def runtime():
 text=pad.call('shell','dumpsys','activity','service','com.codelink.app/.LinkService')
 m=re.search(r'CodeLinkRuntime=(.+)',text);assert m,'Foreground service has no state: '+text
 return json.loads(m[1])
def wait(fn,why,seconds=20):
 end=time.monotonic()+seconds
 while time.monotonic()<end:
  value=fn()
  if value:return value
  time.sleep(.3)
 raise AssertionError(why)
def terminal(sid):return next((s for s in runtime()['terminals'] if s['id']==sid),{})
def map_to(sid):
 action('map',sessionId=sid,deviceId=device,enabled=True,control=True)
 wait(lambda:runtime()['selected']==sid,'Mapping did not select terminal')
def group(sid,value):
 map_to(sid)
 if terminal(sid)['group']!=value:
  # Same native caption button throughout this fixed-orientation test. Read
  # its bounds once; a live WebView may prevent uiautomator from becoming idle.
  pad.call('shell','input','tap',*map(str,group_position));wait(lambda:terminal(sid)['group']==value,'Group change did not apply')
def create(name,kind='B'):
 gate=work/name
 cmd='powershell.exe -NoLogo -NoProfile -File "'+str(root/'scripts/reminder-fixture.ps1')+'" -GatePath "'+str(gate)+'"'
 sid=action('create',kind='terminal',name=name,cwd=str(work),command=cmd)['sessionId'];ids.append(sid)
 group(sid,kind);wait(lambda:terminal(sid).get('status')=='running','Task was not observed running')
 return sid,gate
def finish(sid,gate):
 gate.touch();wait(lambda:terminal(sid).get('status')=='completed','Background completion not received')
def notices():
 keys=[k.strip() for k in pad.call('shell','cmd','notification','list').splitlines() if '|com.codelink.app|' in k and '|task|' in k]
 return {k:pad.call('shell','cmd','notification','get',"'"+k+"'") for k in keys}
def event(target,kind):return next(((k,v) for k,v in notices().items() if target in v and kind in v),None)
def headsup():
 dump=pad.call('shell','dumpsys','activity','service','com.android.systemui/.SystemUIService')
 start=dump.find('HeadsUpManagerPhone state:');section=dump[start:start+5000] if start>=0 else ''
 return 'HeadsUpEntry=' in section,section
def screenshot(name):
 image=subprocess.run(pad.adb+['exec-out','screencap','-p'],capture_output=True,check=True,timeout=20).stdout
 (work/(name+'.png')).write_bytes(image)
def background():
 pad.call('shell','input','keyevent','3');wait(lambda:runtime()['visibleActivities']==0,'Activity still subscribed in background')
 assert runtime()['online']
def foreground():
 pad.call('shell','cmd','statusbar','collapse')
 pad.call('shell','am','start','-n','com.codelink.app/.MainActivity');time.sleep(3)
 wait(lambda:runtime()['visibleActivities']==1,'Activity did not attach')
original_zen=pad.call('shell','settings','get','global','zen_mode').strip()
original_finish=pad.call('shell','settings','get','global','always_finish_activities').strip()
try:
 foreground()
 group_position=pad.find('toggle-session-group')[1]
 for s in state()['sessions']:
  if s['status'] in ('running','approval','input') and terminal(s['id']).get('group')=='B':
   moved.append(s['id']);group(s['id'],'A')
 a,ga=create('BACKGROUND_A','A');b1,g1=create('BACKGROUND_B1');b2,g2=create('BACKGROUND_B2')
 pad.call('shell','cmd','notification','set_dnd','off')
 assert pad.call('shell','settings','get','global','zen_mode').strip()=='0'
 background();before=runtime()['cachedOutputSha256'];pid=pad.call('shell','pidof','com.codelink.app').strip()
 finish(a,ga);notice=wait(lambda:event(a,'completed'),'No system A completion notification')
 screenshot('a-banner')
 visible,section=headsup();(work/'a-headsup.txt').write_text(section,encoding='utf-8')
 report['aCompletionInBackground']=True;report['aHeadsUpObserved']=visible
 assert 'importance=4' in notice[1] or 'mImportance=4' in notice[1],notice[1]
 print('A completed in background; system high-priority notification received',flush=True)
 finish(b1,g1);assert not runtime()['batchUnread'];assert not event('@B','batch');report['bPartialCompletionSilent']=True
 time.sleep(6) # Separate heads-up alerts so platform notification cooldown cannot merge them.
 finish(b2,g2);wait(lambda:event('@B','batch'),'No system B aggregate notification')
 screenshot('b-banner')
 visible,section=headsup();(work/'b-headsup.txt').write_text(section,encoding='utf-8');report['bHeadsUpObserved']=visible
 assert runtime()['cachedOutputSha256']!=before;assert runtime()['online'] and runtime()['visibleActivities']==0
 report['bAggregateInBackground']=True;report['outputUpdatedInBackground']=True
 # Use the real notification PendingIntent from the shade.
 pad.call('shell','cmd','statusbar','expand-notifications');time.sleep(.5);pad.tap('本轮任务全部完成');time.sleep(1)
 if runtime()['visibleActivities']==0:
  screenshot('notification-tap');pad.tap('本轮任务全部完成')
 wait(lambda:runtime()['visibleActivities']==1,'Notification did not open app');pad.call('shell','cmd','statusbar','collapse');time.sleep(1)
 ui={n.get('content-desc'):n.get('text','') for n in pad.tree().iter('node')}
 assert ui['group-tab-B'].startswith('●') and '本轮全部完成' in ui['group-summary']
 report['bNotificationOpensUnreadBatch']=True
 for sid in (b1,b2):
  n=next(n for n in pad.tree().iter('node') if n.get('content-desc')=='session-card-'+sid)
  bounds=list(map(int,re.findall(r'\d+',n.get('bounds'))));pad.call('shell','input','tap',str((bounds[0]+bounds[2])//2),str((bounds[1]+bounds[3])//2))
  wait(lambda:not terminal(sid)['unread'],'Completion dot did not clear')
 assert not runtime()['batchUnread'];assert not event('@B','batch');report['lastReadCancelsBatchNotification']=True
 print('B all-complete banner and notification tap passed; testing Activity destruction',flush=True)
 for sid in list(ids):action('delete',sessionId=sid);ids.remove(sid)
 c,gc=create('BACKGROUND_SCREEN_OFF','A')
 pad.call('shell','settings','put','global','always_finish_activities','1')
 background();time.sleep(2)
 activities=pad.call('shell','dumpsys','activity','activities')
 assert not re.search(r'Hist #\d+: ActivityRecord[^\n]*com.codelink.app/\.MainActivity',activities),'Main Activity was not destroyed'
 report['activityDestroyedConnectionSurvives']=runtime()['online']
 pad.call('shell','settings','put','global','always_finish_activities',original_finish if original_finish!='null' else '0')
 pad.call('shell','input','keyevent','223');time.sleep(3)
 finish(c,gc);wait(lambda:event(c,'completed'),'Screen-off task did not notify')
 assert runtime()['online'] and runtime()['visibleActivities']==0
 assert pad.call('shell','pidof','com.codelink.app').strip()==pid
 report['screenOffCompletionReceived']=True;report['sameProcessConnectionSurvives']=True
 report['batteryOptimizationExempt']='com.codelink.app' in pad.call('shell','dumpsys','deviceidle','whitelist')
 report['version']='1.2.0'
 (root/'research/pad-background-acceptance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 (work/'resume.json').write_text(json.dumps({'ids':ids,'moved':moved,'selected':c,'groupPosition':group_position,'outputHash':runtime()['cachedOutputSha256']},indent=2),encoding='utf-8')
 pad.call('shell','input','keyevent','224');pad.call('shell','wm','dismiss-keyguard');foreground()
 assert runtime()['selected']==c and runtime()['cachedOutputLength']>0
 report['returnRestoresLatestContent']=True
 (work/'screenoff-notification.txt').write_text(event(c,'completed')[1],encoding='utf-8')
 report['version']='1.2.0';report['passed']=True
 print(json.dumps(report),flush=True)
except Exception:
 (work/'failure-runtime.json').write_text(json.dumps(runtime(),indent=2),encoding='utf-8')
 (work/'failure-notices.json').write_text(json.dumps(notices(),indent=2),encoding='utf-8')
 raise
finally:
 pad.call('shell','input','keyevent','224');pad.call('shell','wm','dismiss-keyguard')
 if original_finish=='null':pad.call('shell','settings','delete','global','always_finish_activities')
 else:pad.call('shell','settings','put','global','always_finish_activities',original_finish)
 pad.call('shell','cmd','notification','set_dnd',{'0':'off','1':'priority','2':'none','3':'alarms'}[original_zen])
 report['dndRestored']=pad.call('shell','settings','get','global','zen_mode').strip()==original_zen
 (root/'research/pad-background-acceptance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 try:
  foreground()
  for sid in ids:
   try:action('delete',sessionId=sid)
   except Exception as e:print('Fixture cleanup failed:',e,flush=True)
  for sid in moved:group(sid,'B')
 except Exception:
  (work/'resume.json').write_text(json.dumps({'ids':ids,'moved':moved,'selected':runtime()['selected'],'groupPosition':group_position,'outputHash':runtime()['cachedOutputSha256']},indent=2),encoding='utf-8')
  raise
