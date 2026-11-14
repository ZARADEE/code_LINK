package com.codelink.app;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.os.Build;
import android.provider.Settings;
import android.service.notification.StatusBarNotification;
import org.json.*;
import java.util.*;

/** System notifications are independent of the terminal Activity. */
public final class LinkNotifications {
 public static final String CONNECTION="connection",COMPLETED="completed_banner_v2",REQUESTS="requests",TAG="task";
 public static final int SERVICE_ID=47632;
 private final Context context;private final NotificationManager manager;
 public LinkNotifications(Context context){this.context=context;manager=context.getSystemService(NotificationManager.class);createChannels();}
 private void createChannels(){
  NotificationChannel connection=new NotificationChannel(CONNECTION,"后台连接",NotificationManager.IMPORTANCE_LOW);
  connection.setDescription("保持与电脑连接；任务提醒使用独立横幅");connection.setShowBadge(false);manager.createNotificationChannel(connection);
  NotificationChannel completed=new NotificationChannel(COMPLETED,"任务完成横幅",NotificationManager.IMPORTANCE_HIGH);
  completed.setDescription("A 组逐个完成、B 组全部完成时提醒");completed.enableVibration(true);manager.createNotificationChannel(completed);
  NotificationChannel requests=new NotificationChannel(REQUESTS,"需要处理",NotificationManager.IMPORTANCE_HIGH);
  requests.enableVibration(true);manager.createNotificationChannel(requests);
 }
 private PendingIntent open(String target,String key){
  Intent intent=new Intent(context,MainActivity.class).setAction("com.codelink.OPEN."+key).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP|Intent.FLAG_ACTIVITY_CLEAR_TOP);
  if(!target.isEmpty())intent.putExtra("notice-session",target);
  return PendingIntent.getActivity(context,key.hashCode(),intent,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
 }
 public Notification connection(String text){
  PendingIntent stop=PendingIntent.getService(context,0,new Intent(context,LinkService.class).setAction(LinkService.STOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  Notification.Builder b=new Notification.Builder(context,CONNECTION).setSmallIcon(R.drawable.notification_link).setContentTitle("Code Link · 后台连接")
   .setContentText(text).setContentIntent(open("","connection")).setOngoing(true).setOnlyAlertOnce(true).setShowWhen(false).setCategory(Notification.CATEGORY_SERVICE)
   .addAction(new Notification.Action.Builder(null,"断开连接",stop).build());
  if(Build.VERSION.SDK_INT>=31)b.setForegroundServiceBehavior(Notification.FOREGROUND_SERVICE_IMMEDIATE);
  return b.build();
 }
 public void updateConnection(String text){manager.notify(SERVICE_ID,connection(text));}
 public void show(ReminderState.Notice note,String name){
  if(Build.VERSION.SDK_INT>=33&&context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=0)return;
  boolean complete=note.kind.equals("completed")||note.kind.equals("batch");
  android.os.Bundle data=new android.os.Bundle();data.putString("codelink.event",note.key);data.putString("codelink.target",note.id);data.putString("codelink.kind",note.kind);
  Notification n=new Notification.Builder(context,complete?COMPLETED:REQUESTS).setSmallIcon(R.drawable.notification_link)
   .setContentTitle(name).setContentText(label(note.kind)).setContentIntent(open(note.id,note.key)).setAutoCancel(true)
   .setVisibility(Notification.VISIBILITY_PRIVATE).setCategory(complete?Notification.CATEGORY_STATUS:Notification.CATEGORY_REMINDER).addExtras(data).build();
  manager.notify(TAG,note.key.hashCode(),n);
  android.util.Log.i("CodeLinkNotice","posted "+note.kind+" target="+note.id);
 }
 public void cancel(String key){if(!key.isEmpty()){manager.cancel(TAG,key.hashCode());manager.cancel(key.hashCode());}}
 public void prune(ReminderState reminders){
  for(StatusBarNotification n:manager.getActiveNotifications()){
   String target=n.getNotification().extras.getString("codelink.target","");if(target.isEmpty())continue;
   String key=n.getNotification().extras.getString("codelink.event","");
   ReminderState.Notice unread=reminders.unread(target);
   if(target.equals("@B")?!key.equals(reminders.batchNoticeKey()):unread==null||!unread.key.equals(key))manager.cancel(n.getTag(),n.getId());
  }
 }
 public static String label(String kind){switch(kind){case"batch":return"本轮任务全部完成";case"completed":return"本轮完成";case"approval":return"等待你的授权";case"input":return"等待你的回答";case"failed":return"执行失败";case"interrupted":return"任务已中断";default:return kind;}}
}
