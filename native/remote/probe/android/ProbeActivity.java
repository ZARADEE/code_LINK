package com.codelink.remoteprobe;

import android.app.Activity;
import android.os.Bundle;
import android.net.ConnectivityManager;
import android.net.LinkProperties;
import android.widget.TextView;
import com.codelink.app.RemoteNative;
import java.net.*;
import java.io.*;
import java.security.*;
import java.util.*;
import org.json.*;

public class ProbeActivity extends Activity {
    private TextView text;
    private volatile long handle;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        text=new TextView(this); text.setText("Checking public relay transport…"); text.setPadding(32,48,32,24); setContentView(text);
        new Thread(this::probe,"native-probe").start();
    }
    private void probe() {
        JSONObject result=new JSONObject();
        try {
            ConnectivityManager cm=(ConnectivityManager)getSystemService(CONNECTIVITY_SERVICE);
            LinkProperties properties=cm.getLinkProperties(cm.getActiveNetwork());
            JSONArray dns=new JSONArray(); for(InetAddress address:properties.getDnsServers())dns.put(address.getHostAddress());
            byte[] key=new byte[32];new SecureRandom().nextBytes(key);
            JSONObject config=new JSONObject().put("role","client").put("secret",hex(key)).put("peer",getIntent().getStringExtra("peer"))
                .put("relays",new JSONArray(getIntent().getStringExtra("relays"))).put("dnsServers",dns).put("relayOnly",true);
            handle=RemoteNative.start(config.toString()); if(handle==0)throw new IOException("native start rejected");
            long deadline=android.os.SystemClock.elapsedRealtime()+60000;
            JSONObject state=null;
            while(android.os.SystemClock.elapsedRealtime()<deadline){state=new JSONObject(RemoteNative.status(handle));if(state.optInt("port")>0&&state.optBoolean("relayOnline"))break;if(state.optString("state").equals("error"))throw new IOException(state.toString());Thread.sleep(250);}
            if(state==null||!state.optBoolean("relayOnline"))throw new IOException("Relay offline: "+state);
            JSONArray checks=new JSONArray();
            for(int attempt=0;attempt<2;attempt++) {
                byte[] data=new byte[32768];new SecureRandom().nextBytes(data);
                try(Socket socket=new Socket()) {
                    socket.connect(new InetSocketAddress("127.0.0.1",state.getInt("port")),40000);socket.setSoTimeout(40000);
                    socket.getOutputStream().write(data);socket.getOutputStream().flush();
                    byte[] reply=new byte[data.length];new DataInputStream(socket.getInputStream()).readFully(reply);
                    if(!Arrays.equals(data,reply))throw new IOException("Echo bytes differ");
                    Thread.sleep(500);state=new JSONObject(RemoteNative.status(handle));
                    if(!state.optString("path").equals("relay"))throw new IOException("Unexpected route: "+state);
                    checks.put(new JSONObject().put("bytes",data.length).put("sha256",hex(MessageDigest.getInstance("SHA-256").digest(data))).put("path",state.getString("path")));
                }
                RemoteNative.networkChanged(handle);
            }
            result.put("passed",true).put("checks",checks).put("state",state).put("dnsServers",dns).put("interface",properties.getInterfaceName());
        } catch(Throwable error) {try{result.put("passed",false).put("error",error.toString()).put("state",handle==0?"unavailable":RemoteNative.status(handle));}catch(Exception ignored){}}
        finally { if(handle!=0)RemoteNative.stop(handle); }
        final String json=result.toString();android.util.Log.i("CodeLinkRemoteProbe",json);
        try(FileOutputStream file=openFileOutput("result.json",MODE_PRIVATE)){file.write(json.getBytes("UTF-8"));}catch(Exception ignored){}
        runOnUiThread(()->text.setText(json));
    }
    private static String hex(byte[] bytes){StringBuilder b=new StringBuilder();for(byte value:bytes)b.append(String.format(Locale.ROOT,"%02x",value&255));return b.toString();}
}
