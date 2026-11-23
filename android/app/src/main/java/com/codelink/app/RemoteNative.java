package com.codelink.app;

/** Native byte-stream transport. TLS identity and device authorization stay in LinkConnection. */
public final class RemoteNative {
    static { System.loadLibrary("codelink_remote"); }
    private RemoteNative() {}
    public static native long start(String config);
    public static native String status(long handle);
    public static native void stop(long handle);
    public static native void networkChanged(long handle);
}
