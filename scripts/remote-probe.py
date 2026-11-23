"""Exercise the native transport through real public relays, never ADB forwarding."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import socketserver
import threading
import time

ROOT = Path(__file__).resolve().parents[1]


class Native:
    def __init__(self, path):
        self.dll = ctypes.CDLL(str(Path(path).resolve()))
        self.dll.cl_remote_start.argtypes = [ctypes.c_char_p]
        self.dll.cl_remote_start.restype = ctypes.c_uint64
        self.dll.cl_remote_status.argtypes = [ctypes.c_uint64, ctypes.c_void_p, ctypes.c_size_t]
        self.dll.cl_remote_status.restype = ctypes.c_size_t
        self.dll.cl_remote_stop.argtypes = [ctypes.c_uint64]
        self.handles = []

    def start(self, **config):
        handle = self.dll.cl_remote_start(json.dumps(dict(secret=secrets.token_hex(32), **config)).encode())
        if not handle:
            raise RuntimeError("native rejected configuration")
        self.handles.append(handle)
        return handle

    def status(self, handle):
        buf = ctypes.create_string_buffer(65536)
        size = self.dll.cl_remote_status(handle, buf, len(buf))
        if size > len(buf):
            raise RuntimeError("unexpected status length")
        return json.loads(buf.value)

    def ready(self, handle, relay=True, timeout=55):
        end = time.monotonic() + timeout
        last = None
        while time.monotonic() < end:
            last = self.status(handle)
            if last.get("state") == "error":
                raise RuntimeError(last)
            if last.get("state") == "ready" and (not relay or last.get("relayOnline")):
                return last
            time.sleep(.25)
        raise TimeoutError(last)

    def close(self):
        for handle in self.handles:
            self.dll.cl_remote_stop(handle)


class Echo(socketserver.BaseRequestHandler):
    def handle(self):
        self.request.settimeout(120)
        while chunk := self.request.recv(16384):
            self.request.sendall(chunk)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dll", default=ROOT / ".local/remote-target/x86_64-pc-windows-msvc/debug/codelink_remote.dll")
    parser.add_argument("--serve", action="store_true", help="Keep a relay-only echo endpoint for the Android probe")
    parser.add_argument("--seconds", type=int, default=900)
    args = parser.parse_args()
    native = Native(args.dll)
    echo = socketserver.ThreadingTCPServer(("127.0.0.1", 0), Echo)
    echo.daemon_threads = True
    threading.Thread(target=echo.serve_forever, daemon=True).start()
    out = ROOT / ".local/remote-probe"
    out.mkdir(parents=True, exist_ok=True)
    started = time.monotonic()
    try:
        server = native.start(role="server", targetPort=echo.server_address[1], relayOnly=True)
        info = native.ready(server)
        print(json.dumps(dict(event="server-online", **info)), flush=True)
        (out / "server.json").write_text(json.dumps(dict(peer=info["endpoint"], relays=info["relays"], pid=os.getpid())), encoding="utf-8")
        if args.serve:
            while time.monotonic() - started < args.seconds:
                (out / "server-status.json").write_text(json.dumps(native.status(server)), encoding="utf-8")
                time.sleep(1)
            return
        client = native.start(role="client", peer=info["endpoint"], relays=info["relays"], relayOnly=True)
        ready = native.ready(client)
        checks = []
        for attempt in range(2):
            payload = secrets.token_bytes(32768)
            with socket.create_connection(("127.0.0.1", ready["port"]), timeout=40) as sock:
                sock.sendall(payload)
                received = bytearray()
                while len(received) < len(payload):
                    chunk = sock.recv(len(payload) - len(received))
                    if not chunk:
                        raise RuntimeError("unexpected EOF")
                    received.extend(chunk)
                assert received == payload, "payload mismatch"
                time.sleep(.5)
                observed = native.status(client)
                assert observed["path"] == "relay", observed
                checks.append(dict(attempt=attempt+1, bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest(), path=observed["path"]))
        native.dll.cl_remote_stop(client)
        assert native.status(client)["state"] == "stopped"
        result = dict(passed=True, seconds=round(time.monotonic()-started, 2), transport="public-relay-only", checks=checks, server=native.status(server))
        (out / "windows-result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(json.dumps(result), flush=True)
    finally:
        native.close()
        echo.shutdown()


if __name__ == "__main__":
    main()
