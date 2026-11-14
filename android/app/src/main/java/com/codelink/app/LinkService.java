package com.codelink.app;

import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.os.*;

/** User-visible local device connection, independent of Activity lifecycle. */
public final class LinkService extends Service {
 public static final String CONNECT="com.codelink.CONNECT",STOP="com.codelink.STOP";
 private LinkRuntime runtime;private PowerManager.WakeLock wake;
 public void onCreate(){super.onCreate();runtime=LinkRuntime.get(this);LinkNotifications notices=new LinkNotifications(this);
  if(Build.VERSION.SDK_INT>=29)startForeground(LinkNotifications.SERVICE_ID,notices.connection("正在连接电脑"),ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);
  else startForeground(LinkNotifications.SERVICE_ID,notices.connection("正在连接电脑"));
  // Held only for the explicitly enabled continuous local connection. Released
  // on disconnect/destruction; the visible notification provides a stop action.
  wake=getSystemService(PowerManager.class).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"CodeLink:LocalConnection");wake.setReferenceCounted(false);wake.acquire();
 }
 public int onStartCommand(Intent intent,int flags,int startId){
  if(intent!=null&&STOP.equals(intent.getAction())){runtime.disconnect();stopSelf();return START_NOT_STICKY;}
  if(!runtime.requested()){stopSelf();return START_NOT_STICKY;}
  runtime.activate(this,intent!=null&&CONNECT.equals(intent.getAction()));return START_STICKY;
 }
 public IBinder onBind(Intent intent){return null;}
 protected void dump(java.io.FileDescriptor fd,java.io.PrintWriter writer,String[] args){runtime.dump(writer);}
 public void onDestroy(){runtime.detached(this);if(wake!=null&&wake.isHeld())wake.release();stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();}
}
