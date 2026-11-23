package com.codelink.app;
import java.util.*;
public final class ReminderStateTest {
 static ReminderState.Item i(String id,String group,String status,String turn){return new ReminderState.Item(id,group,status,turn,"");}
 static List<ReminderState.Notice> tick(ReminderState r,ReminderState.Item... items){return r.update(Arrays.asList(items),true);}
 static void check(boolean ok,String why){if(!ok)throw new AssertionError(why);}
 public static void main(String[] args){
  ReminderState r=new ReminderState();
  tick(r,i("a","A","running","1"),i("b1","B","running","1"),i("b2","B","running","1"),i("idle","B","idle",""));
  List<ReminderState.Notice> n=tick(r,i("a","A","completed","1"),i("b1","B","completed","1"),i("b2","B","running","1"),i("idle","B","idle",""));
  check(n.size()==1&&n.get(0).id.equals("a"),"A individually completes; partial B is silent");
  check(r.unread("b1")==null&&r.unread("b2")==null,"Partial B completion does not create member dots");
  check(r.unread("a")!=null&&!r.batchUnread(),"A red dot persists");r.acknowledge("a");
  n=tick(r,i("a","A","completed","1"),i("b1","B","completed","1"),i("b2","B","approval","1"),i("idle","B","idle",""));
  check(n.size()==1&&n.get(0).kind.equals("approval"),"B approval immediately alerts");r.acknowledge("b2");
  n=tick(r,i("a","A","completed","1"),i("b1","B","completed","1"),i("b2","B","approval","1"),i("idle","B","idle",""));
  check(n.isEmpty()&&r.unread("b2")==null,"Acknowledged approval stays read while pending");
  ReminderState restored=new ReminderState();restored.restore(r.save());r=restored;
  n=tick(r,i("a","A","completed","1"),i("b1","B","completed","1"),i("b2","B","completed","1"),i("idle","B","idle",""));
  check(n.size()==1&&n.get(0).kind.equals("batch")&&r.batchUnread(),"B final task creates one aggregate event, idle excluded");
  ReminderState savedBatch=new ReminderState();savedBatch.restore(r.save());r=savedBatch;
  check(r.batchUnread(),"Unread aggregate survives persistence");
  check(r.unread("b1").kind.equals("completed")&&r.unread("b2").kind.equals("completed"),"All-complete B exposes unread completion dots for its members");
  String batchKey=r.batchNoticeKey();r.acknowledge("idle");check(r.batchUnread(),"Reading a nonparticipant cannot clear B completion");
  r.acknowledge("b1");check(r.unread("b1")==null&&r.unread("b2")!=null&&r.batchUnread(),"Reading one B member clears only its dot and keeps B red");
  ReminderState partlyRead=new ReminderState();partlyRead.restore(r.save());r=partlyRead;
  check(r.unread("b1")==null&&r.unread("b2")!=null&&r.batchUnread()&&r.batchNoticeKey().equals(batchKey),"Partial read state and aggregate survive restart");
  n=tick(r,i("b1","B","completed","1"),i("b2","B","completed","1"));
  check(n.isEmpty()&&r.unread("b1")==null&&r.unread("b2")!=null,"Repeated snapshot does not restore acknowledged dots");
  r.acknowledge("b2");check(r.unread("b2")==null&&!r.batchUnread()&&r.batchNoticeKey().isEmpty(),"Reading the last B member automatically clears the tab dot");
  r.acknowledge("b2");check(!r.batchUnread(),"Acknowledgement is idempotent");
  ReminderState fullyRead=new ReminderState();fullyRead.restore(r.save());r=fullyRead;
  check(!r.batchUnread()&&r.unread("b1")==null&&r.unread("b2")==null,"Fully read B stays clear after restart");
  n=tick(r,i("b1","B","completed","1"),i("b2","B","completed","1"));check(n.isEmpty(),"Unchanged snapshot never re-alerts");
  tick(r,i("b1","B","running","2"),i("b2","B","running","2"));
  n=tick(r,i("b1","B","completed","2"),i("b2","B","failed","2"));check(!r.batchUnread()&&n.get(0).kind.equals("failed"),"Failure is not aggregate success");
  n=tick(r,i("b1","B","completed","2"),i("b2","B","disconnected","2"));check(n.isEmpty()&&!r.batchUnread(),"Disconnection cannot complete batch");
  tick(r,i("b1","B","completed","2"),i("b2","B","running","3"));n=tick(r,i("b1","B","completed","2"),i("b2","B","completed","3"));check(n.size()==1&&n.get(0).kind.equals("batch"),"Retry success completes remaining batch");
  ReminderState fresh=new ReminderState();check(tick(fresh,i("old","B","completed","old")).isEmpty(),"First connection does not announce old history");
  tick(fresh,i("old","B","unknown","old"));check(tick(fresh,i("old","B","completed","old")).isEmpty(),"Unknown screen recovery cannot announce the same completion twice");
  tick(fresh,i("x","B","running","1"),i("y","B","running","1"));tick(fresh,i("x","B","completed","1"),i("y","B","running","1"));
  check(tick(fresh,i("x","B","completed","1")).isEmpty(),"Unmapping unfinished task cannot fake all-complete");
  check(fresh.update(Arrays.asList(i("x","B","completed","2")),false).isEmpty(),"Offline state cannot trigger notifications");
  ReminderState next=new ReminderState();tick(next,i("x","B","running","1"));tick(next,i("x","B","completed","1"));check(next.batchUnread(),"Finished group is red");tick(next,i("x","B","running","2"));check(!next.batchUnread(),"Starting the next batch clears the previous all-finished mark");
  check(next.unread("x")==null,"New batch also clears old member completion dots");
  ReminderState shortcut=new ReminderState();tick(shortcut,i("x","B","running","1"),i("y","B","running","1"));tick(shortcut,i("x","B","completed","1"),i("y","B","completed","1"));
  shortcut.acknowledgeBatch();check(!shortcut.batchUnread()&&shortcut.unread("x")==null&&shortcut.unread("y")==null,"Summary shortcut clears all completion dots together");
  ReminderState pending=new ReminderState();tick(pending,i("x","B","approval","1"));pending.acknowledgeBatch();check(pending.unread("x").kind.equals("approval")&&!pending.batchUnread(),"Batch acknowledgement never acknowledges pending authorization");
  tick(pending,i("x","B","completed","1"));pending.acknowledgeBatch();check(pending.unread("x")==null&&!pending.batchUnread(),"Completed work cannot retain an obsolete approval notice");
  ReminderState resolved=new ReminderState();tick(resolved,new ReminderState.Item("pc","A","approval","turn","request-1"));
  check(resolved.unread("pc")!=null,"Pending PC authorization is unread");
  tick(resolved,i("pc","A","unknown","turn"));check(resolved.unread("pc")!=null,"An unreadable screen does not prove authorization was handled");
  check(tick(resolved,new ReminderState.Item("pc","A","approval","turn","request-1")).isEmpty(),"Screen recovery does not repeat the same request");
  ReminderState reopened=new ReminderState();reopened.restore(resolved.save());resolved=reopened;
  tick(resolved,i("pc","A","running","turn"));check(resolved.unread("pc")==null,"Desktop approval clears the persisted reminder on reconnect");
  check(tick(resolved,new ReminderState.Item("pc","A","approval","turn","request-2")).size()==1,"A later authorization still alerts");
  tick(resolved,i("pc","A","input","turn"));check(resolved.unread("pc").kind.equals("input"),"A follow-up question replaces authorization");
  tick(resolved,i("pc","A","running","turn"));check(resolved.unread("pc")==null,"Answering on the PC clears the question reminder");
  ReminderState moved=new ReminderState();tick(moved,i("x","B","running","1"),i("y","B","running","1"));tick(moved,i("x","B","completed","1"),i("y","B","completed","1"));moved.acknowledge("x");
  tick(moved,i("x","B","completed","1"),i("y","A","completed","1"));check(!moved.batchUnread()&&moved.unread("y")==null,"Moving the last unread member out of B clears the B dot");
  System.out.println("ReminderState behavioral checks passed");
 }
}
