package com.codelink.app;

import android.bluetooth.*;
import android.content.*;
import android.os.SystemClock;
import android.util.Base64;
import android.security.keystore.*;
import java.io.*;
import java.net.*;
import java.security.*;
import java.security.cert.X509Certificate;
import java.util.*;
import java.util.concurrent.*;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;
import javax.net.ssl.*;
import org.json.*;

public final class LinkConnection {
 public interface Listener {void message(JSONObject msg);void status(String text,boolean connected);}
 private final Context context;private final SharedPreferences prefs;private final Listener listener;
 private final ScheduledExecutorService watchdog=Executors.newSingleThreadScheduledExecutor(r->{Thread t=new Thread(r,"CodeLink-watchdog");t.setDaemon(true);return t;});
 private volatile long lastMessageAt;
 private volatile boolean enabled=false;private volatile int generation=0;private volatile Closeable connection;private volatile DataOutputStream output;
 private final Object lock=new Object();
 public String mode="USB",host="127.0.0.1",pin="",code="",btAddress="";public int port=47631;
 public String deviceId;
 public LinkConnection(Context c,Listener l){context=c;prefs=c.getSharedPreferences("connection",0);listener=l;deviceId=prefs.getString("deviceId",UUID.randomUUID().toString());prefs.edit().putString("deviceId",deviceId).apply();load();}
 private void load(){host=prefs.getString("host","127.0.0.1");port=prefs.getInt("port",47631);pin=prefs.getString("pin","");code=prefs.getString("code","");mode=prefs.getString("mode","USB");if(mode.equals("\u84dd\u7259"))mode="Bluetooth";btAddress=prefs.getString("bt","");}
 public String modeLabel(){return mode.equals("Bluetooth")?"蓝牙":mode;}
 public void save(){if(mode.equals("\u84dd\u7259"))mode="Bluetooth";prefs.edit().putString("host",host).putInt("port",port).putString("pin",pin).putString("code",code).putString("mode",mode).putString("bt",btAddress).apply();}
 public void invite(String value)throws Exception{if(!value.startsWith("codelink:"))throw new IOException("请使用电脑生成的 Code Link 连接信息");JSONObject n=new JSONObject(new String(Base64.decode(value.substring(9),Base64.URL_SAFE|Base64.NO_WRAP),"UTF-8"));String newPin=n.getString("pin");if(!newPin.equals(pin))prefs.edit().remove("key").apply();host=n.getString("host");port=n.getInt("port");pin=newPin;code=n.getString("code");mode=host.equals("127.0.0.1")?"USB":"Wi-Fi";save();}
 public synchronized void start(){stop();enabled=true;final int g=generation;new Thread(()->loop(g),"CodeLink-connection").start();}
 public synchronized void stop(){enabled=false;generation++;Closeable old=connection;connection=null;output=null;if(old!=null)try{old.close();}catch(Exception ignored){}}
 private synchronized void own(int g,Closeable socket)throws IOException{if(g!=generation||!enabled){socket.close();throw new IOException("连接已切换");}connection=socket;}
 private synchronized void ready(int g,OutputStream out)throws IOException{if(g!=generation||!enabled)throw new IOException("连接已切换");output=new DataOutputStream(out);}
 private void loop(int g){int retry=0;while(enabled&&g==generation){Closeable owned=null;try{listener.status(retry==0?"正在连接 "+modeLabel()+"…":"正在重连 "+modeLabel()+"…",false);InputStream input;
   if(mode.equals("Bluetooth")){BluetoothManager manager=(BluetoothManager)context.getSystemService(Context.BLUETOOTH_SERVICE);BluetoothAdapter adapter=manager.getAdapter();if(adapter==null||!adapter.isEnabled())throw new IOException("请开启蓝牙");if(btAddress.isEmpty())throw new IOException("请选择已配对的电脑");BluetoothSocket socket=adapter.getRemoteDevice(btAddress).createRfcommSocketToServiceRecord(UUID.fromString("944d82d8-3d07-4c96-9d86-04a95bf20579"));owned=socket;own(g,socket);adapter.cancelDiscovery();socket.connect();input=socket.getInputStream();ready(g,socket.getOutputStream());}
   else{if(pin.length()!=64)throw new IOException("请先扫描电脑的配对二维码");final String expected=pin.toLowerCase(Locale.ROOT);TrustManager[] trust={new X509TrustManager(){public X509Certificate[] getAcceptedIssuers(){return new X509Certificate[0];}public void checkClientTrusted(X509Certificate[] chain,String auth)throws java.security.cert.CertificateException{throw new java.security.cert.CertificateException("client");}public void checkServerTrusted(X509Certificate[] chain,String auth)throws java.security.cert.CertificateException{try{if(chain.length==0||!hex(MessageDigest.getInstance("SHA-256").digest(chain[0].getEncoded())).equals(expected))throw new java.security.cert.CertificateException("电脑证书与配对信息不符，请重新扫码");}catch(java.security.cert.CertificateException e){throw e;}catch(Exception e){throw new java.security.cert.CertificateException(e);}}}};
    SSLContext ssl=SSLContext.getInstance("TLS");ssl.init(null,trust,new SecureRandom());Socket raw=new Socket();owned=raw;own(g,raw);raw.connect(new InetSocketAddress(host,port),6000);raw.setTcpNoDelay(true);SSLSocket socket=(SSLSocket)ssl.getSocketFactory().createSocket(raw,host,port,true);owned=socket;own(g,socket);socket.setSoTimeout(25000);socket.startHandshake();input=socket.getInputStream();ready(g,socket.getOutputStream());}
   Closeable active=owned;lastMessageAt=SystemClock.elapsedRealtime();
   ScheduledFuture<?> monitor=watchdog.scheduleWithFixedDelay(()->{
    if(mode.equals("Bluetooth")&&enabled&&g==generation&&SystemClock.elapsedRealtime()-lastMessageAt>25000){android.util.Log.w("CodeLinkLink","Bluetooth heartbeat timed out; closing stale socket");try{active.close();}catch(Exception ignored){}}
   },5,5,TimeUnit.SECONDS);
   try{
    send(new JSONObject().put("type","hello").put("deviceId",deviceId).put("name",android.os.Build.MODEL).put("key",readKey()).put("code",code));
    DataInputStream in=new DataInputStream(input);while(enabled&&g==generation){int length=in.readInt();if(length<2||length>1048576)throw new IOException("消息长度无效");byte[] bytes=new byte[length];in.readFully(bytes);lastMessageAt=SystemClock.elapsedRealtime();if(g!=generation||!enabled)break;JSONObject msg=new JSONObject(new String(bytes,"UTF-8"));String type=msg.optString("type");if(type.equals("welcome")){String key=msg.optString("key");if(!key.isEmpty())storeKey(key);retry=0;listener.status(modeLabel()+" 已连接 · "+msg.optString("host"),true);}else if(type.equals("error")){String error=msg.optString("error");if(msg.optString("code").equals("auth_expired")||error.contains("\u6388\u6743\u5df2\u5931\u6548")||error.contains("Device authorization expired"))prefs.edit().remove("key").apply();throw new IOException(UiMessages.server(error));}listener.message(msg);}
   }finally{monitor.cancel(true);}
  }catch(Exception e){if(enabled&&g==generation){android.util.Log.w("CodeLinkLink",mode+" disconnected: "+e.getClass().getSimpleName()+": "+friendly(e));listener.status("连接中断 · "+friendly(e),false);}}finally{synchronized(this){if(g==generation){connection=null;output=null;}}if(owned!=null)try{owned.close();}catch(Exception ignored){}}if(!enabled||g!=generation)break;retry++;try{long delay=Math.min(30000,1000L<<(Math.min(retry-1,5)));Thread.sleep(delay+ThreadLocalRandom.current().nextLong(Math.max(1,delay/5)));}catch(InterruptedException ignored){}}
 }
 public void send(JSONObject msg)throws Exception{synchronized(lock){DataOutputStream out=output;if(out==null)throw new IOException("尚未连接到电脑");byte[] bytes=msg.toString().getBytes("UTF-8");if(bytes.length>1048576)throw new IOException("输入过长");out.writeInt(bytes.length);out.write(bytes);out.flush();}}
 public String action(String action,JSONObject fields)throws Exception{JSONObject msg=fields==null?new JSONObject():fields;String id=UUID.randomUUID().toString();msg.put("type","action").put("action",action).put("id",id);send(msg);return id;}
 public void shutdown(){stop();watchdog.shutdownNow();}
 private SecretKey secretKey()throws Exception{KeyStore ks=KeyStore.getInstance("AndroidKeyStore");ks.load(null);if(!ks.containsAlias("CodeLinkDevice")){KeyGenerator generator=KeyGenerator.getInstance("AES","AndroidKeyStore");generator.init(new KeyGenParameterSpec.Builder("CodeLinkDevice",KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());return generator.generateKey();}return((KeyStore.SecretKeyEntry)ks.getEntry("CodeLinkDevice",null)).getSecretKey();}
 private void storeKey(String key)throws Exception{Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,secretKey());String value=Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)+":"+Base64.encodeToString(cipher.doFinal(key.getBytes("UTF-8")),Base64.NO_WRAP);prefs.edit().putString("key",value).apply();}
 private String readKey(){try{String value=prefs.getString("key","");if(value.isEmpty())return "";String[] parts=value.split(":");Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,secretKey(),new GCMParameterSpec(128,Base64.decode(parts[0],Base64.NO_WRAP)));return new String(cipher.doFinal(Base64.decode(parts[1],Base64.NO_WRAP)),"UTF-8");}catch(Exception e){return "";}}
 public static String hex(byte[] b){StringBuilder s=new StringBuilder();for(byte n:b)s.append(String.format(Locale.ROOT,"%02x",n&255));return s.toString();}
 private String friendly(Exception e){String msg=e.getMessage();if(msg==null)msg=e.getClass().getSimpleName();if(msg.contains("Software caused connection abort")||msg.contains("Broken pipe"))return"蓝牙链路已中断，正在重连";return msg;}
}
