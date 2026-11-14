"""Start an unmanaged, disposable console for real Pad input tests."""
import pathlib,subprocess,json
root=pathlib.Path(__file__).resolve().parents[1]
child=subprocess.Popen(['cmd.exe','/k','title CODELINK_EXTERNAL_TEST & echo EXTERNAL_TERMINAL_READY'],cwd=root,creationflags=subprocess.CREATE_NEW_CONSOLE)
(root/'.local/external-fixture.json').write_text(json.dumps({'pid':child.pid}),encoding='utf-8')
print(child.pid)
