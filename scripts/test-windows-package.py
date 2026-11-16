"""Launch bundled runtimes and an isolated host without touching user sessions."""
from pathlib import Path
import ctypes
import json
import os
import socket
import subprocess
import sys
import tempfile
import time
import uuid
import zipfile

folder = Path(sys.argv[1]).resolve()
if sys.platform != 'win32':
    raise SystemExit('Run this package check on Windows')
if folder.is_file():
    with tempfile.TemporaryDirectory(prefix='codelink-unzip-') as temporary:
        destination = Path(temporary)
        with zipfile.ZipFile(folder) as package:
            for entry in package.infolist():
                path = (destination / entry.filename).resolve()
                assert path.is_relative_to(destination.resolve()), 'Archive path escapes the package'
                assert not {'state', '.local', '.git', 'bin', 'obj'}.intersection(Path(entry.filename).parts), entry.filename
                assert path.suffix.lower() not in {'.pfx', '.key', '.keystore', '.pdb'}, entry.filename
            package.extractall(destination)
        assert json.loads((destination / 'runtime.json').read_text()) == {'instance':'main','dataDirectory':'state'}
        result = subprocess.run([sys.executable, str(Path(__file__).resolve()), str(destination)], timeout=120)
        raise SystemExit(result.returncode)

for app in ('CodeLink', 'CodeLink.Host', 'clink', 'CodeLink.ConsoleBridge'):
    runtime = json.loads((folder / f'{app}.runtimeconfig.json').read_text())['runtimeOptions']
    assert runtime.get('includedFrameworks') and not runtime.get('frameworks') and not runtime.get('framework'), app
for name in ('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll'):
    assert (folder / name).is_file(), name

with tempfile.TemporaryDirectory(prefix='codelink-package-') as temporary:
    data = Path(temporary)
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        port = probe.getsockname()[1]
    (data / 'host.json').write_text(json.dumps({'Port': port, 'LanEnabled': False, 'BluetoothEnabled': False}))
    env = dict(os.environ, CODELINK_DATA_DIR=str(data), CODELINK_INSTANCE='package-' + uuid.uuid4().hex,
               DOTNET_ROOT=str(data / 'no-global-runtime'), DOTNET_ROOT_X64=str(data / 'no-global-runtime'),
               DOTNET_MULTILEVEL_LOOKUP='0')
    flags = subprocess.CREATE_NO_WINDOW
    host = subprocess.Popen([str(folder / 'CodeLink.Host.exe')], cwd=folder, env=env,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=flags)
    desktop = None
    try:
        startup = subprocess.STARTUPINFO()
        startup.dwFlags = subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
        desktop = subprocess.Popen([str(folder / 'CodeLink.exe')], cwd=folder, env=env,
                                   startupinfo=startup, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        user32 = ctypes.windll.user32
        user32.GetWindowThreadProcessId.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
        user32.GetWindowTextW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_int]
        callback_type = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
        windows = []
        @callback_type
        def find_window(handle, _):
            pid = ctypes.c_ulong()
            user32.GetWindowThreadProcessId(handle, ctypes.byref(pid))
            if pid.value == desktop.pid:
                title = ctypes.create_unicode_buffer(256)
                user32.GetWindowTextW(handle, title, 256)
                if title.value.startswith('Code Link'):
                    windows.append(handle)
            return True
        for _ in range(100):
            assert desktop.poll() is None, 'Bundled desktop failed to start; check merged WPF runtime files'
            user32.EnumWindows(find_window, 0)
            if windows:
                break
            time.sleep(.2)
        assert windows, 'Bundled desktop did not create its main window'
        help_result = subprocess.run([str(folder / 'clink.exe'), '--help'], cwd=folder, env=env,
                                     capture_output=True, timeout=20, creationflags=flags)
        assert help_result.returncode == 0 and b'clink terminal' in help_result.stdout, 'Bundled CLI does not start'
        for _ in range(60):
            assert host.poll() is None, 'Bundled host exited during startup'
            if (data / 'host.pfx').is_file():
                break
            time.sleep(.2)
        result = subprocess.run([str(folder / 'clink.exe'), 'list'], cwd=folder, env=env,
                                capture_output=True, timeout=20, creationflags=flags)
        assert result.returncode == 0 and json.loads(result.stdout) == [], ('Isolated host/CLI round trip failed', result.returncode, result.stdout, result.stderr)
        bridge = subprocess.run([str(folder / 'CodeLink.ConsoleBridge.exe')], input=b'{}', cwd=folder,
                                env=env, capture_output=True, timeout=10, creationflags=flags)
        response = json.loads(bridge.stdout)
        assert response.get('ok') is False and response.get('error'), 'Console bridge did not reject an invalid target'
        language_file = folder / 'build-language.txt'
        if not language_file.is_file():
            language_file = folder / 'LANGUAGE.txt'
        language = language_file.read_text(encoding='utf-8-sig').strip()
        assert language in ('zh-CN', 'en'), 'Package language is missing or invalid'
        wizard = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                                 str(Path(__file__).with_name('test-bluetooth-wizard.ps1')),
                                 '-DesktopPid', str(desktop.pid), '-Language', language],
                                capture_output=True, encoding='utf-8', errors='replace',
                                timeout=45, creationflags=flags)
        print(wizard.stdout, end='')
        assert wizard.returncode == 0, wizard.stderr
    finally:
        if desktop and desktop.poll() is None:
            desktop.terminate()
            desktop.wait(timeout=10)
        host.terminate()
        try:
            host.wait(timeout=10)
        except subprocess.TimeoutExpired:
            host.kill()
            host.wait()
print('PASS: included desktop runtime, real WPF window, standalone CLI/host handshake, safe console-bridge rejection')
