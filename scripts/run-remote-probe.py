"""Start the isolated native probe on the USB-attached PAD; payload uses Wi-Fi only."""
import json
from pathlib import Path
import shlex
import subprocess
import time

ROOT=Path(__file__).resolve().parents[1]
ADB=str(ROOT/".tools/platform-tools/adb.exe")
def adb(*args):
    return subprocess.check_output([ADB,"-d",*args],text=True,encoding="utf-8",errors="replace").strip()

info=json.loads((ROOT/".local/remote-probe/server.json").read_text(encoding="utf-8"))
print("ADB reverse mappings:",adb("reverse","--list"),flush=True)
adb("shell","am","force-stop","com.codelink.remoteprobe")
adb("shell","run-as","com.codelink.remoteprobe","rm","-f","files/result.json")
command="am start -n com.codelink.remoteprobe/.ProbeActivity --es peer "+shlex.quote(info["peer"])+" --es relays "+shlex.quote(json.dumps(info["relays"]))
print(adb("shell",command),flush=True)
deadline=time.monotonic()+115
while time.monotonic()<deadline:
    proc=subprocess.run([ADB,"-d","shell","run-as","com.codelink.remoteprobe","cat","files/result.json"],capture_output=True,text=True,encoding="utf-8",errors="replace")
    if proc.returncode==0 and proc.stdout.startswith("{"):
        result=json.loads(proc.stdout)
        (ROOT/".local/remote-probe/android-result.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
        print(json.dumps(result,ensure_ascii=False),flush=True)
        raise SystemExit(0 if result.get("passed") else 1)
    time.sleep(2)
raise TimeoutError("Android probe did not report; inspect logcat for crash")
