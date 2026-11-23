package com.codelink.app;

import android.content.Context;
import android.net.*;
import android.os.SystemClock;
import java.io.*;
import java.net.InetAddress;
import org.json.*;

/** Owns one connection generation, including the native endpoint and its network callback. */
final class RemoteConnection implements Closeable {
    private final ConnectivityManager manager;
    private final Network network;
    private final String dns;
    private final ConnectivityManager.NetworkCallback callback;
    private long handle;
    private boolean closed, registered;
    private Closeable socket;

    RemoteConnection(Context context,String identity,String peer,String relays)throws IOException {
        manager=(ConnectivityManager)context.getSystemService(Context.CONNECTIVITY_SERVICE);
        network=manager.getActiveNetwork();
        dns=dns(manager.getLinkProperties(network)).toString();
        callback=new ConnectivityManager.NetworkCallback(){
            @Override public void onAvailable(Network next){if(!next.equals(network))close();else changed();}
            @Override public void onLost(Network lost){if(lost.equals(network))close();}
            @Override public void onLinkPropertiesChanged(Network next,LinkProperties properties){
                if(next.equals(network)&&!dns(properties).toString().equals(dns))close();else changed();
            }
        };
        try {
            JSONObject config=new JSONObject().put("role","client").put("secret",identity).put("peer",peer)
                .put("relays",new JSONArray(relays)).put("dnsServers",new JSONArray(dns));
            handle=RemoteNative.start(config.toString());
            if(handle==0)throw new IOException("Remote transport configuration failed");
            synchronized(this){manager.registerDefaultNetworkCallback(callback);registered=true;}
        } catch(LinkageError error){close();throw new IOException("This APK does not include a remote library for this device",error);}
        catch(Exception error){close();throw new IOException(error.getMessage(),error);}
    }
    private static JSONArray dns(LinkProperties properties){JSONArray servers=new JSONArray();if(properties!=null)for(InetAddress address:properties.getDnsServers())servers.put(address.getHostAddress());return servers;}
    private synchronized void changed(){if(!closed&&handle!=0)RemoteNative.networkChanged(handle);}
    synchronized Closeable attach(Closeable value)throws IOException{if(closed){value.close();throw new IOException("Remote connection closed");}socket=value;return this;}
    int port()throws Exception {
        long until=SystemClock.elapsedRealtime()+35000;
        while(SystemClock.elapsedRealtime()<until){
            JSONObject state;
            synchronized(this){if(closed)throw new IOException("Network changed; reconnecting");state=new JSONObject(RemoteNative.status(handle));}
            if(state.optString("state").equals("error"))throw new IOException(state.optString("error"));
            if(state.optInt("port")>0)return state.getInt("port");
            Thread.sleep(100);
        }
        throw new IOException("Remote transport startup timed out");
    }
    synchronized JSONObject snapshot(){try{return new JSONObject(closed?"{\"state\":\"stopped\"}":RemoteNative.status(handle));}catch(Exception ignored){return new JSONObject();}}
    @Override public synchronized void close(){
        if(closed)return;closed=true;
        if(socket!=null)try{socket.close();}catch(IOException ignored){}
        if(handle!=0){RemoteNative.stop(handle);handle=0;}
        if(registered){manager.unregisterNetworkCallback(callback);registered=false;}
    }
}
