package com.codelink.app;

/** Only application-owned server messages pass through this boundary. */
public final class UiMessages {
 private UiMessages() {}
 public static String server(String message) { return message; }
}
