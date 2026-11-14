package com.codelink.app;

import java.util.*;
import java.io.*;

/** Deterministic notification state; independent of Activity, sockets and Android. */
public final class ReminderState {
 public static final class Item {
  public final String id,group,status,turn,requests;
  public Item(String id,String group,String status,String turn,String requests){this.id=id;this.group=group;this.status=status;this.turn=turn;this.requests=requests;}
 }
 public static final class Notice {
  public final String id,kind,key;
  public Notice(String id,String kind,String key){this.id=id;this.kind=kind;this.key=key;}
 }
 private static final class Seen {String status="",turn="",request="",completedToken="";long sequence;}
 private final Map<String,Seen> seen=new LinkedHashMap<>();
 private final Map<String,Notice> unread=new LinkedHashMap<>();
 private final Set<String> batch=new LinkedHashSet<>(),batchUnread=new LinkedHashSet<>();
 private long batchNumber;
 private int batchDone,batchTotal;
 public List<Notice> update(List<Item> items,boolean online){
  List<Notice> events=new ArrayList<>();if(!online)return events;
  Map<String,Item> current=new LinkedHashMap<>();boolean finishedNow=false;
  for(Item item:items){
   current.put(item.id,item);Seen prior=seen.get(item.id);boolean first=prior==null;
   if(first){prior=new Seen();seen.put(item.id,prior);}
   boolean active=isActive(item.status);
   boolean started=active&&(!isActive(prior.status)&&!prior.status.equals("unknown")&&!prior.status.equals("disconnected")||!item.turn.isEmpty()&&!item.turn.equals(prior.turn));
   if(started||first&&active)prior.sequence++;
   if(item.group.equals("B")&&active){batch.add(item.id);batchUnread.clear();}
   if(item.group.equals("B")){Notice old=unread.get(item.id);if(old!=null&&old.kind.equals("completed"))unread.remove(item.id);}
   String request=item.requests.isEmpty()?(item.status.equals("approval")||item.status.equals("input")?item.status+":"+item.turn:""):item.requests;
   if(!request.isEmpty()&&!request.equals(prior.request)){
    prior.sequence++;
    String kind=item.status.equals("input")?"input":"approval";
    Notice note=new Notice(item.id,kind,item.id+":"+request+":"+prior.sequence);unread.put(item.id,note);events.add(note);
   }
   boolean complete=item.status.equals("completed");
   String completionToken=item.turn.isEmpty()?"run:"+prior.sequence:item.turn;
   boolean completion=complete&&!first&&!completionToken.equals(prior.completedToken);
   if(complete)prior.completedToken=completionToken;
   if(completion){
    finishedNow=true;
    if(item.group.equals("A")){Notice note=new Notice(item.id,"completed",item.id+":done:"+item.turn+":"+prior.sequence);unread.put(item.id,note);events.add(note);}
    // A disconnected device can return with a new completed turn. Include it
    // even if its short running state wasn't delivered over this connection.
    else batch.add(item.id);
   }
   if((item.status.equals("failed")||item.status.equals("interrupted"))&&!item.status.equals(prior.status)){
    Notice note=new Notice(item.id,item.status,item.id+":"+item.status+":"+item.turn+":"+prior.sequence);unread.put(item.id,note);events.add(note);
   }
   prior.status=item.status;prior.turn=item.turn;prior.request=request;
  }
  // Explicitly removed/moved windows leave the batch. Removal itself never
  // generates a false all-complete notification.
  batch.removeIf(id->!current.containsKey(id)||!current.get(id).group.equals("B"));
  batchUnread.removeIf(id->!current.containsKey(id)||!current.get(id).group.equals("B"));
  unread.keySet().removeIf(id->!current.containsKey(id));
  seen.keySet().removeIf(id->!current.containsKey(id));
  batchTotal=batch.size();batchDone=0;
  for(String id:batch)if(current.get(id).status.equals("completed"))batchDone++;
  if(finishedNow&&batchTotal>0&&batchDone==batchTotal){
   batchNumber++;batchUnread.addAll(batch);
   events.add(new Notice("@B","batch","batch:"+batchNumber));batch.clear();
  }
  return events;
 }
 private static boolean isActive(String s){return s.equals("running")||s.equals("approval")||s.equals("input");}
 // B completion dots are views of the aggregate's unread members. They only
 // exist after the whole batch finishes, and cannot drift from the tab dot.
 public Notice unread(String id){Notice note=unread.get(id);return note!=null?note:batchUnread.contains(id)?new Notice(id,"completed","batch:"+batchNumber+":"+id):null;}
 public boolean batchUnread(){return !batchUnread.isEmpty();}
 public String batchNoticeKey(){return batchUnread()?"batch:"+batchNumber:"";}
 public int batchDone(){return batchDone;}
 public int batchTotal(){return batchTotal;}
 public void acknowledge(String id){unread.remove(id);batchUnread.remove(id);}
 public void acknowledgeBatch(){for(String id:batchUnread){Notice n=unread.get(id);if(n!=null&&n.kind.equals("completed"))unread.remove(id);}batchUnread.clear();}
 public String save(){
  Properties p=new Properties();
  for(Map.Entry<String,Seen> pair:seen.entrySet()){String id=pair.getKey();Seen s=pair.getValue();p.setProperty("s."+id,s.status);p.setProperty("t."+id,s.turn);p.setProperty("r."+id,s.request);p.setProperty("c."+id,s.completedToken);p.setProperty("q."+id,""+s.sequence);}
  for(Notice n:unread.values()){p.setProperty("u."+n.id,n.kind);p.setProperty("k."+n.id,n.key);}
  p.setProperty("batch",String.join(",",batch));p.setProperty("batchUnread",String.join(",",batchUnread));p.setProperty("batchNumber",""+batchNumber);
  try{StringWriter out=new StringWriter();p.store(out,null);return out.toString();}catch(IOException e){return "";}
 }
 public void restore(String value){
  if(value==null||value.isEmpty())return;
  try{Properties p=new Properties();p.load(new StringReader(value));
   for(String key:p.stringPropertyNames())if(key.startsWith("s.")){String id=key.substring(2);Seen s=new Seen();s.status=p.getProperty(key,"");s.turn=p.getProperty("t."+id,"");s.request=p.getProperty("r."+id,"");s.completedToken=p.getProperty("c."+id,"");s.sequence=Long.parseLong(p.getProperty("q."+id,"0"));seen.put(id,s);}
   for(String key:p.stringPropertyNames())if(key.startsWith("u.")){String id=key.substring(2);unread.put(id,new Notice(id,p.getProperty(key),p.getProperty("k."+id,"")));}
   for(String id:p.getProperty("batch","").split(","))if(!id.isEmpty())batch.add(id);
   for(String id:p.getProperty("batchUnread","").split(","))if(!id.isEmpty())batchUnread.add(id);
   batchNumber=Long.parseLong(p.getProperty("batchNumber","0"));
  }catch(IOException|IllegalArgumentException ignored){}
 }
}
