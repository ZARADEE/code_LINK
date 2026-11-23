"""Integration checks against the isolated broker through the actual native relay path."""
import ctypes
import hashlib
import importlib.util
import json
from pathlib import Path
import secrets
import socket
import ssl
import struct
import subprocess
import time

ROOT=Path(__file__).resolve().parents[1]
WORK=ROOT/'.local/remote-acceptance'
spec=importlib.util.spec_from_file_location('native_probe',ROOT/'scripts/remote-probe.py')
probe=importlib.util.module_from_spec(spec);spec.loader.exec_module(probe)

def admin(action,fields):
    output=subprocess.check_output(['node',str(ROOT/'scripts/remote-acceptance.cjs'),'action',action,json.dumps(fields)],text=True,encoding='utf-8')
    return json.loads(output)

def send(sock,value):
    data=json.dumps(value).encode();sock.sendall(struct.pack('>I',len(data))+data)

def read(sock):
    def exact(size):
        result=bytearray()
        while len(result)<size:
            part=sock.recv(size-len(result))
            if not part:raise EOFError('stream closed')
            result.extend(part)
        return bytes(result)
    size=struct.unpack('>I',exact(4))[0]
    assert 0<size<=1048576
    return json.loads(exact(size))

subprocess.run(['node',str(ROOT/'scripts/remote-acceptance.cjs'),'invitation'],check=True)
state=json.loads((WORK/'admin-state.json').read_text(encoding='utf-8'))
native=probe.Native(ROOT/'.local/remote-native/windows/codelink_remote.dll')
client=native.start(role='client',peer=state['remote']['endpoint'],relays=state['remote']['relays'],relayOnly=True)
port=native.ready(client)['port']

def connect(pin=state['pairing']['fingerprint']):
    context=ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.check_hostname=False;context.verify_mode=ssl.CERT_NONE
    sock=context.wrap_socket(socket.create_connection(('127.0.0.1',port),timeout=40),server_hostname='Code Link')
    if hashlib.sha256(sock.getpeercert(binary_form=True)).hexdigest()!=pin:
        sock.close();raise ssl.SSLCertVerificationError('pin mismatch')
    return sock

result={}
device='remote-security-'+secrets.token_hex(8)
try:
    try:connect('0'*64)
    except ssl.SSLCertVerificationError:result['tlsPinMismatchRejected']=True
    else:raise AssertionError('mismatched pin accepted')
    with connect() as sock:
        send(sock,dict(type='hello',deviceId=device+'-short',name='Remote test',code=state['pairing']['code']))
        rejected=read(sock);assert rejected['type']=='error',rejected
        result['localSixDigitCodeRejectedRemotely']=True
    with connect() as sock:
        send(sock,dict(type='hello',deviceId=device,name='Remote security test',code=state['pairing']['remoteCode']))
        welcome=read(sock);assert welcome['type']=='welcome',welcome
        key=welcome['key'];assert len(key)==64
        result['randomRemoteInvitationAccepted']=True
        operation=secrets.token_hex(16)
        send(sock,dict(type='action',id=operation,action='remoteAccess',enabled=False))
        while True:
            message=read(sock)
            if message.get('id')==operation:
                assert message['result']['ok'] is False,message
                result['remoteCannotChangeHostAccessSettings']=True
                break
        admin('revoke',dict(deviceId=device))
        sock.settimeout(8)
        disconnected=False
        try:
            while sock.recv(8192):pass
            disconnected=True
        except (EOFError,ConnectionError,ssl.SSLError):disconnected=True
        assert disconnected,'revocation did not close the stream'
        result['revocationDisconnectsActivePeer']=True
    with connect() as sock:
        send(sock,dict(type='hello',deviceId=device,name='Revoked test',key=key))
        rejected=read(sock);assert rejected['type']=='error' and rejected['code']=='auth_expired',rejected
        result['revokedCredentialCannotReconnect']=True
    config=json.loads((WORK/'host.json').read_text(encoding='utf-8-sig'))
    assert config['RemoteIdentity'].startswith('dpapi:'),'host identity is not protected by Windows DPAPI'
    result['hostIdentityProtectedAtRest']=True
    result['actualPath']=native.status(client)['path']
    assert result['actualPath']=='relay'
    result['passed']=True
    (ROOT/'research/remote-security-acceptance.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result),flush=True)
finally:
    native.close()
