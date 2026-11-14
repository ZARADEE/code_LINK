package com.codelink.app;

import android.content.*;
import android.os.*;
import org.json.*;
import java.util.*;

/** One connection and reminder state per process. The service owns its lifetime;
 * Activities only subscribe while visible. State changes run on the main thread. */
public final class LinkRuntime implements LinkConnection.Listener {
 private static LinkRuntime instance;
 public static synchronized LinkRuntime get(Context context){if(instance==null)instance=new LinkRuntime(context.getApplicationContext());return instance;}
 public final LinkConnection link;public final ReminderState reminders=new ReminderState();public final SharedPreferences presentation;
 private final Context context;private final SharedPreferences settings;private final Handler main=new Handler(Looper.getMainLooper());
 private final LinkNotifications notices;private final Set<LinkConnection.Listener> listeners=new LinkedHashSet<>();private final OutputCache output=new OutputCache();
 private JSONObject state;private String signature="",statusText="尚未连接",connectionSummary="";private boolean online=false,started=false;private LinkService service;
 private LinkRuntime(Context context){this.context=context;settings=context.getSharedPreferences("connection",0);presentation=context.getSharedPreferences("terminal-presentation",0);reminders.restore(presentation.getString("reminders",""));link=new LinkConnection(context,this);notices=new LinkNotifications(context);}
 public boolean requested(){return settings.getBoolean("backgroundEnabled",true)&&!link.pin.isEmpty();}
 public void start(){settings.edit().putBoolean("backgroundEnabled",true).apply();context.startForegroundService(new Intent(context,LinkService.class).setAction(LinkService.CONNECT));}
 public void ensureStarted(){if(requested())context.startForegroundService(new Intent(context,LinkService.class));}
 public void disconnect(){settings.edit().putBoolean("backgroundEnabled",false).apply();suspend();context.stopService(new Intent(context,LinkService.class));}
 void activate(LinkService owner,boolean reconnect){service=owner;connectionSummary="";if(!started||reconnect){started=true;link.start();}updateConnection();}
 void detached(LinkService owner){if(service==owner){service=null;suspend();}}
 private void suspend(){started=false;link.stop();online=false;statusText="未连接";signature="";save();for(LinkConnection.Listener l:new ArrayList<>(listeners))l.status(statusText,false);}
 public void attach(LinkConnection.Listener listener){listeners.add(listener);listener.status(statusText,online);JSONObject cached=snapshot();if(cached!=null)listener.message(cached);}
 public void detach(LinkConnection.Listener listener){listeners.remove(listener);}
 public void dump(java.io.PrintWriter writer){
  java.util.concurrent.CountDownLatch ready=new java.util.concurrent.CountDownLatch(1);String[] value={"{}"};Runnable read=()->{try{
   JSONArray items=new JSONArray();JSONArray all=visible();for(int i=0;i<all.length();i++){JSONObject s=all.optJSONObject(i);if(s!=null)items.put(new JSONObject().put("id",s.optString("id")).put("group",groupOf(s)).put("status",s.optString("status")).put("unread",reminders.unread(SessionCatalog.reminderId(s))!=null));}
   value[0]=new JSONObject().put("online",online).put("started",started).put("visibleActivities",listeners.size()).put("selected",state==null?"":state.optString("selected"))
    .put("cachedOutputLength",output.text().length()).put("cachedOutputSha256",LinkConnection.hex(java.security.MessageDigest.getInstance("SHA-256").digest(output.text().getBytes("UTF-8"))))
    .put("batchUnread",reminders.batchUnread()).put("batchNoticeKey",reminders.batchNoticeKey()).put("terminals",items).toString();
  }catch(Exception ignored){}finally{ready.countDown();}};if(Looper.myLooper()==Looper.getMainLooper())read.run();else main.post(read);
  try{ready.await(3,java.util.concurrent.TimeUnit.SECONDS);}catch(InterruptedException e){Thread.currentThread().interrupt();}writer.println("CodeLinkRuntime="+value[0]);
 }
 public JSONObject snapshot(){if(state==null)return null;try{return new JSONObject(state.toString()).put("reset",true).put("output",output.text());}catch(JSONException e){return null;}}
 public void status(String text,boolean connected){main.post(()->{if(!started)return;statusText=text;online=connected;if(!connected)signature="";updateConnection();for(LinkConnection.Listener l:new ArrayList<>(listeners))l.status(text,connected);});}
 public void message(JSONObject message){main.post(()->{if(!started)return;
  if(message.optString("type").equals("state")){state=message;output.accept(message.optString("selected"),message.optString("output"),message.optBoolean("reset"));refreshReminders();updateConnection();}
  for(LinkConnection.Listener l:new ArrayList<>(listeners))l.message(message);
 });}
 public JSONArray visible(){return SessionCatalog.visible(state);}
 public String prefKey(JSONObject s){return link.pin+":"+(s.optString("kind").equals("observer")?"window:"+s.optLong("observedWindowHandle")+":"+s.optInt("observedProcessId"):s.optString("id"));}
 public String groupOf(JSONObject s){return presentation.getString("group:"+prefKey(s),"B");}
 public String displayName(JSONObject s){String name=presentation.getString("name:"+prefKey(s),"");return name.isEmpty()?s.optString("name"):name;}
 public void presentationChanged(){signature="";refreshReminders();updateConnection();}
 private void refreshReminders(){
  if(!online||state==null)return;
  JSONArray sessions=visible();List<ReminderState.Item> items=new ArrayList<>();JSONArray sig=new JSONArray();Map<String,JSONObject> targets=new HashMap<>();
  for(int i=0;i<sessions.length();i++){JSONObject s=sessions.optJSONObject(i);if(s==null)continue;String id=SessionCatalog.reminderId(s);targets.put(id,s);
   JSONArray requests=s.optJSONArray("approvals");List<String> ids=new ArrayList<>();if(requests!=null)for(int j=0;j<requests.length();j++){JSONObject r=requests.optJSONObject(j);if(r!=null)ids.add(r.optString("id"));}Collections.sort(ids);
   String token=s.optString("kind").equals("observer")?""+s.optLong("activityCycle"):s.optString("turnId"),group=groupOf(s),status=s.optString("status"),request=String.join(",",ids);
   items.add(new ReminderState.Item(id,group,status,token,request));sig.put(new JSONArray(Arrays.asList(id,group,status,token,request)));
  }
  if(sig.toString().equals(signature))return;signature=sig.toString();String oldBatch=reminders.batchNoticeKey();
  List<ReminderState.Notice> events=reminders.update(items,true);save();
  if(!oldBatch.equals(reminders.batchNoticeKey()))notices.cancel(oldBatch);notices.prune(reminders);
  for(ReminderState.Notice n:events){JSONObject target=targets.get(n.id);String name=n.id.equals("@B")?"B 组":target==null?"命令行":displayName(target);notices.show(n,name);
   try{JSONObject event=new JSONObject().put("type","notice").put("text",name+" · "+LinkNotifications.label(n.kind));for(LinkConnection.Listener l:new ArrayList<>(listeners))l.message(event);}catch(JSONException ignored){}
  }
 }
 public void acknowledge(String id){JSONObject s=SessionCatalog.session(state,id);if(s!=null)id=SessionCatalog.reminderId(s);String oldBatch=reminders.batchNoticeKey();ReminderState.Notice n=reminders.unread(id);if(n!=null)notices.cancel(n.key);reminders.acknowledge(id);afterAcknowledgement(oldBatch);}
 public void acknowledgeBatch(){String oldBatch=reminders.batchNoticeKey();reminders.acknowledgeBatch();afterAcknowledgement(oldBatch);}
 private void afterAcknowledgement(String oldBatch){if(!oldBatch.equals(reminders.batchNoticeKey()))notices.cancel(oldBatch);notices.prune(reminders);save();}
 private void save(){presentation.edit().putString("reminders",reminders.save()).apply();}
 private void updateConnection(){if(service==null)return;int running=0,waiting=0;JSONArray all=visible();for(int i=0;i<all.length();i++){JSONObject s=all.optJSONObject(i);if(s==null)continue;String status=s.optString("status");if(status.equals("running"))running++;if(status.equals("approval")||status.equals("input"))waiting++;}
  String text=online?link.mode+" 已连接 · "+running+" 个运行中 · "+waiting+" 个待处理":statusText;
  if(!text.equals(connectionSummary)){connectionSummary=text;notices.updateConnection(text);}
 }
}
