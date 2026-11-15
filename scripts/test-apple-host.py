"""Exercise the shipped macOS host over local admin and pinned TLS connections."""
from pathlib import Path
import base64
import hashlib
import json
import os
import platform
import select
import shutil
import socket
import ssl
import struct
import subprocess
import sys
import tempfile
import time
import uuid

APP = Path(sys.argv[1]).resolve()
ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'dist/apple'
OUT.mkdir(parents=True, exist_ok=True)
checks = []
active_simulator = None
created_simulators = []


def check(condition, message):
    if not condition:
        raise AssertionError(message)
    checks.append(message)
    print('PASS:', message, flush=True)


class Peer:
    def __init__(self, stream):
        self.stream = stream
        self.stream.settimeout(15)
        self.states = []

    def send(self, message):
        payload = json.dumps(message).encode()
        self.stream.sendall(struct.pack('!I', len(payload)) + payload)

    def exact(self, count):
        result = b''
        while len(result) < count:
            chunk = self.stream.recv(count - len(result))
            if not chunk:
                raise EOFError('Host disconnected')
            result += chunk
        return result

    def read(self):
        size = struct.unpack('!I', self.exact(4))[0]
        check(2 <= size <= 1048576, 'Host frame within protocol limit')
        value = json.loads(self.exact(size))
        if value.get('type') == 'state':
            self.states.append(value)
        return value

    def until(self, predicate, timeout=20):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            value = self.read()
            if predicate(value):
                return value
        raise TimeoutError('Expected host message did not arrive')

    def action(self, action, **values):
        key = uuid.uuid4().hex
        self.send(dict(type='action', id=key, action=action, **values))
        return self.until(lambda m: m.get('type') == 'result' and m.get('id') == key)['result']


with tempfile.TemporaryDirectory(prefix='cl-') as temporary:
    data = Path(temporary)
    probe = socket.socket(); probe.bind(('127.0.0.1', 0)); port = probe.getsockname()[1]; probe.close()
    (data / 'host.json').write_text(json.dumps({'Port':port, 'LanEnabled':False, 'BluetoothEnabled':False}))
    env = dict(os.environ, CODELINK_DATA_DIR=str(data), CODELINK_INSTANCE='apple-ci-' + uuid.uuid4().hex)
    host = subprocess.Popen([str(APP / 'Contents/Resources/backend/CodeLink.Host')], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    try:
        for _ in range(200):
            if (data / 'control.sock').exists():
                break
            if host.poll() is not None:
                raise RuntimeError(host.stdout.read().decode())
            time.sleep(0.1)
        check((data / 'control.sock').exists(), 'Local admin socket starts')
        check((data.stat().st_mode & 0o777) == 0o700, 'Host directory restricted to its owner')
        check(((data / 'control.sock').stat().st_mode & 0o777) == 0o600, 'Admin socket restricted to its owner')
        local = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM); local.connect(str(data / 'control.sock'))
        admin = Peer(local); admin.send({'type':'hello'})
        check(admin.read()['type'] == 'welcome', 'Local admin handshake')
        state = admin.until(lambda m: m.get('type') == 'state')
        pairing = state['pairing']
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT); context.check_hostname = False; context.verify_mode = ssl.CERT_NONE

        def remote(identity, key='', code=None):
            stream = context.wrap_socket(socket.create_connection(('127.0.0.1', port)), server_hostname='localhost')
            check(hashlib.sha256(stream.getpeercert(binary_form=True)).hexdigest() == pairing['fingerprint'], 'TLS leaf matches invitation fingerprint')
            peer = Peer(stream); peer.send(dict(type='hello', deviceId=identity, name='iPhone CI', key=key, code=code or pairing['code']))
            return peer

        phone = remote('apple-smoke-phone')
        welcome = phone.read(); key = welcome['key']
        check(welcome['type'] == 'welcome' and len(key) == 64, 'TLS pairing issues a device credential')
        denied = phone.action('create', kind='terminal', command='id', cwd=str(data))
        check(not denied['ok'], 'Remote device cannot create privileged host sessions')
        created = admin.action('create', kind='terminal', name='Release check', cwd=str(data), command="printf 'READY\\n'; read answer; printf 'RECEIVED:%s\\n' \"$answer\"; exit 0")
        check(created['ok'], 'Real macOS PTY starts')
        session = created['sessionId']
        check(not phone.action('input', sessionId=session, text='denied')['ok'], 'Unmapped session rejects remote input')
        check(phone.action('openCommand', key=session)['ok'], 'Device can open and control a managed session')
        phone.until(lambda m: m.get('type') == 'state' and 'READY' in m.get('output', ''))
        check(phone.action('input', sessionId=session, text='hello-中文')['ok'], 'Remote input acknowledged')
        phone.until(lambda m: m.get('type') == 'state' and any(s['id'] == session and s['status'] == 'completed' for s in m['sessions']))
        check(any('RECEIVED:hello-中文' in s.get('output', '') for s in phone.states), 'UTF-8 PTY output reaches the mobile connection')
        stored = json.loads((data / 'host.json').read_text())
        check(stored['CertificatePassword'].startswith('keychain:'), 'Certificate password encrypted with Keychain-backed key')
        check(key not in (data / 'host.json').read_text(), 'Device credential is not stored in plaintext')
        phone.stream.close()
        phone = remote('apple-smoke-phone', key=key, code='000000')
        check(phone.read()['type'] == 'welcome', 'Saved credential reconnects without fresh pairing code')
        check(admin.action('revoke', deviceId='apple-smoke-phone')['ok'], 'Host revokes a paired device')
        revoked = remote('apple-smoke-phone', key=key)
        rejection = revoked.read()
        check(rejection['type'] == 'error' and rejection.get('code') == 'auth_expired', 'Revoked device is rejected with a language-independent error code')
        revoked.stream.close(); phone.stream.close()
        check(admin.action('delete', sessionId=session)['ok'], 'Ended session can be removed')
        failed = admin.action('create', kind='terminal', name='Exit status', cwd=str(data), command='exit 7')
        check(failed['ok'], 'Short-lived PTY starts')
        failure = admin.until(lambda m: m.get('type') == 'state' and any(s['id'] == failed['sessionId'] and s['status'] == 'failed' for s in m['sessions']))
        check('7' in failure['output'], 'Nonzero exit is failure, not successful completion')
        admin.action('delete', sessionId=failed['sessionId'])

        # Screenshot a real host session and a real iOS simulator connection.
        demo = admin.action('create', kind='terminal', name='Release workspace', cwd=str(data), command="export PS1='codelink % '; exec /bin/zsh -f")
        check(demo['ok'], 'Screenshot terminal is a real managed PTY')
        admin.action('settings', lan=True)
        invite = dict(host='127.0.0.1', port=port, pin=pairing['fingerprint'], code=pairing['code'])
        def invitation_text(value):
            return 'codelink:' + base64.urlsafe_b64encode(json.dumps(value).encode()).decode().rstrip('=')
        probe = str(ROOT / '.local/apple/network-probe')
        subprocess.run([probe, invitation_text(invite), 'swift-probe', demo['sessionId']], check=True, timeout=30)
        check(True, 'Production Swift networking code exchanges real terminal input/output')
        wrong_pin = dict(invite, pin='0'*64)
        subprocess.run([probe, invitation_text(wrong_pin), 'swift-bad-pin', demo['sessionId'], '--reject'], check=True, timeout=30)
        check(True, 'Production Swift networking rejects a different certificate')
        saved_invite = dict(invite, code='000000')
        subprocess.run([probe, invitation_text(saved_invite), 'swift-probe', demo['sessionId']], check=True, timeout=30)
        check(True, 'Swift Keychain credential reconnects with an expired pairing code')
        admin.action('input', sessionId=demo['sessionId'], text="clear; printf '\\033[32mCode Link · macOS + iOS\\033[0m\\n\\nLocal connection is ready.\\n\\n  [passed] TLS certificate verification\\n  [passed] Real terminal input and output\\n  [passed] Saved device credential reconnect\\n\\nType a command below to continue.\\n\\n'")
        native = subprocess.Popen([str(APP / 'Contents/MacOS/CodeLink')], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            time.sleep(6)
            subprocess.run(['screencapture', '-x', str(OUT / 'macos-live.png')], check=True)
            simulators = json.loads(subprocess.check_output(['xcrun','simctl','list','devices','available','--json']))
            sdk = subprocess.check_output(['xcrun','--sdk','iphonesimulator','--show-sdk-version'], text=True).strip()
            runtime_suffix = 'iOS-' + sdk.replace('.', '-')
            runtimes = [r for r in simulators['devices'] if r.endswith(runtime_suffix)]
            check(bool(runtimes), 'Simulator runtime matches the selected Xcode SDK ' + sdk)
            runtime_devices = simulators['devices'][runtimes[0]]
            (OUT / 'simulator-environment.json').write_text(json.dumps({'sdk':sdk,'runtime':runtimes[0],'devices':runtime_devices},indent=2))
            candidates = [d for d in runtime_devices if 'iPhone' in d['name']]
            check(bool(candidates), 'iOS simulator runtime is available')
            def boot_simulator(template):
                global active_simulator
                # CI images include restored device data. Use an isolated device and
                # simctl directly: a Simulator GUI / Settings launch is not an app test.
                for attempt in range(2):
                    identity = subprocess.check_output([
                        'xcrun','simctl','create','CodeLink-' + uuid.uuid4().hex[:8],
                        template['deviceTypeIdentifier'],runtimes[0]], text=True, timeout=60).strip()
                    created_simulators.append(identity)
                    active_simulator = identity
                    print('Booting isolated simulator', identity, 'attempt', attempt + 1, flush=True)
                    try:
                        subprocess.run(['xcrun','simctl','boot',identity], check=True, timeout=120)
                        subprocess.run(['xcrun','simctl','bootstatus',identity,'-b'], check=True, timeout=240)
                        return identity
                    except (subprocess.TimeoutExpired, subprocess.CalledProcessError):
                        if attempt == 1:
                            raise
                        # Only cold-boot infrastructure is retried. App installation,
                        # launch, pairing and mapping below must succeed on their own.
                        subprocess.run(['xcrun','simctl','shutdown',identity], check=False, timeout=60)
                raise RuntimeError('Simulator did not boot')

            def verify_terminal_screen(identity, label):
                container = Path(subprocess.check_output([
                    'xcrun','simctl','get_app_container',identity,'com.codelink.ios','data'],
                    text=True, timeout=30).strip())
                rendered = container / 'Documents/codelink-render.json'
                marker = 'CL-' + uuid.uuid4().hex[:10]
                # Encode the marker in the command so the echoed input cannot
                # satisfy the screen assertion. Only real PTY output contains it.
                escaped = ''.join('\\x' + format(ord(char), '02x') for char in marker)
                check(admin.action('input', sessionId=demo['sessionId'], text="printf '\\n" + escaped + "\\n'")['ok'], label + ' terminal probe command accepted')
                deadline = time.monotonic() + 120
                while time.monotonic() < deadline:
                    try:
                        value = json.loads(rendered.read_text())
                        if value.get('sessionId') == demo['sessionId'] and marker in value.get('screenText',''):
                            (OUT / (label.lower() + '-render.json')).write_text(json.dumps(value, indent=2))
                            check(True, label + ' WebView visibly renders fresh PTY output')
                            return
                    except (FileNotFoundError, json.JSONDecodeError):
                        pass
                    time.sleep(0.5)
                raise AssertionError(label + ' paired but did not render fresh terminal output')

            device = boot_simulator(candidates[0])
            simulator_app = ROOT / '.local/apple/ios/Build/Products/Release-iphonesimulator/CodeLink.app'
            subprocess.run(['xcrun','simctl','install',device,str(simulator_app)], check=True, timeout=180)
            invitation = invitation_text(invite)
            print('Launching signed iPhone application', flush=True)
            subprocess.run(['xcrun','simctl','launch','--terminate-running-process',device,'com.codelink.ios','--invite',invitation,'--validate-terminal'], check=True, timeout=120)
            test_ids = {'apple-smoke-phone', 'swift-probe'}
            connected = admin.until(lambda m: m.get('type') == 'state' and any(d['online'] for d in m.get('devices',[]) if d['id'] not in test_ids))
            mobile = next(d for d in connected['devices'] if d['online'] and d['id'] not in test_ids)
            check(admin.action('map', sessionId=demo['sessionId'], deviceId=mobile['id'], enabled=True, control=True)['ok'], 'Real iOS app pairs and receives session mapping')
            verify_terminal_screen(device, 'iPhone')
            subprocess.run(['xcrun','simctl','io',device,'screenshot',str(OUT / 'ios-live.png')], check=True)
            subprocess.run(['xcrun','simctl','terminate',device,'com.codelink.ios'], check=True)
            subprocess.run(['xcrun','simctl','shutdown',device], check=True, timeout=60)
            tablets = [d for d in runtime_devices if 'iPad' in d['name']]
            check(bool(tablets), 'iPad simulator is available')
            tablet = boot_simulator(tablets[0])
            subprocess.run(['xcrun','simctl','install',tablet,str(simulator_app)], check=True, timeout=180)
            print('Launching signed iPad application', flush=True)
            subprocess.run(['xcrun','simctl','launch','--terminate-running-process',tablet,'com.codelink.ios','--invite',invitation,'--validate-terminal'], check=True, timeout=120)
            test_ids.add(mobile['id'])
            connected = admin.until(lambda m: m.get('type') == 'state' and any(d['online'] for d in m.get('devices',[]) if d['id'] not in test_ids))
            mobile = next(d for d in connected['devices'] if d['online'] and d['id'] not in test_ids)
            check(admin.action('map', sessionId=demo['sessionId'], deviceId=mobile['id'], enabled=True, control=True)['ok'], 'Real iPad app pairs and receives session mapping')
            subprocess.run(['xcrun','simctl','io',tablet,'orientation','landscapeLeft'], check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            verify_terminal_screen(tablet, 'iPad')
            subprocess.run(['xcrun','simctl','io',tablet,'screenshot',str(OUT / 'ipados-live.png')], check=True)
            subprocess.run(['xcrun','simctl','terminate',tablet,'com.codelink.ios'], check=True)
            active_simulator = None
        finally:
            if active_simulator:
                try:
                    subprocess.run(['xcrun','simctl','io',active_simulator,'screenshot',str(OUT / 'simulator-failure.png')],timeout=30)
                    with (OUT / 'simulator-failure.log').open('w') as log:
                        subprocess.run(['xcrun','simctl','spawn',active_simulator,'log','show','--last','10m','--style','compact','--predicate','eventMessage CONTAINS[c] "codelink" OR process == "CodeLink" OR process == "runningboardd" OR process == "amfid"'],stdout=log,stderr=log,timeout=30)
                except subprocess.TimeoutExpired: pass
            native.terminate()
            try: native.wait(timeout=8)
            except subprocess.TimeoutExpired: native.kill()
            for identity in created_simulators:
                try:
                    subprocess.run(['xcrun','simctl','shutdown',identity],check=False,timeout=30,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                    subprocess.run(['xcrun','simctl','delete',identity],check=False,timeout=30)
                except subprocess.TimeoutExpired:
                    pass
        local.close()
    finally:
        host.terminate()
        try: host.wait(timeout=8)
        except subprocess.TimeoutExpired: host.kill(); host.wait()
        log = host.stdout.read().decode(errors='replace')
        (OUT / 'host-validation.log').write_text(log)
(OUT / 'validation.json').write_text(json.dumps({'passed':len(checks), 'architecture':platform.machine(), 'checks':checks}, indent=2))
print(f'PASS: {len(checks)} macOS host and iOS simulator checks')
