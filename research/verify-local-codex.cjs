// Read-only protocol probe. It sends initialize/initialized only: no model turn,
// thread creation, tool execution, login, or configuration change.
const { spawn, execFileSync } = require('node:child_process');
const { createRequire } = require('node:module');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');

async function main() {
  const cliEntry = process.argv[2];
  const schemaDir = process.argv[3];
  const outputPath = process.argv[4];
  if (!cliEntry || !schemaDir || !outputPath) {
    throw new Error('Usage: node verify-local-codex.cjs CLI_ENTRY SCHEMA_DIR OUTPUT_JSON');
  }

  const localRequire = createRequire(path.resolve(cliEntry));
  const platformPackage = localRequire.resolve('@openai/codex-win32-x64/package.json');
  const executable = path.join(path.dirname(platformPackage), 'vendor',
    'x86_64-pc-windows-msvc', 'bin', 'codex.exe');
  const schema = (name) => JSON.parse(fs.readFileSync(path.join(schemaDir, name), 'utf8'));
  const methods = (name) => (schema(name).oneOf || [])
    .flatMap((entry) => entry.properties?.method?.enum || []);

  const report = {
    checkedAt: new Date().toISOString(),
    platform: process.platform,
    osRelease: os.release(),
    codexVersion: execFileSync(executable, ['--version'], { encoding: 'utf8' }).trim(),
    schemaBundleSha256: crypto.createHash('sha256')
      .update(fs.readFileSync(path.join(schemaDir, 'codex_app_server_protocol.schemas.json')))
      .digest('hex'),
    clientMethods: methods('ClientRequest.json').filter((method) =>
      /^(thread\/(start|resume|read|list)|turn\/(start|steer|interrupt))$/.test(method)),
    serverRequests: methods('ServerRequest.json').filter((method) =>
      /requestApproval|requestUserInput|elicitation/.test(method)),
    serverNotifications: methods('ServerNotification.json').filter((method) =>
      /^(turn\/(started|completed)|thread\/status\/changed|serverRequest\/resolved|item\/agentMessage\/delta|item\/commandExecution\/outputDelta)$/.test(method)),
    questionParams: {
      description: schema('ToolRequestUserInputParams.json').description,
      required: schema('ToolRequestUserInputParams.json').required,
      fields: Object.keys(schema('ToolRequestUserInputParams.json').properties),
    },
    probe: { sentMethods: [], initializeSucceeded: false, shutdown: null },
    coverage: {
      modelInference: false,
      realApprovalRoundTrip: false,
      androidAppConnection: false,
      usbConnection: false,
      rfcommConnection: false,
      wifiConnection: false,
      originalCodexTuiProxy: false,
    },
  };

  const child = spawn(executable, ['app-server', '--listen', 'stdio://'], {
    stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true,
  });
  let stdoutBuffer = '';
  let stderrBytes = 0;
  child.stderr.on('data', (chunk) => { stderrBytes += chunk.length; });
  child.stdin.on('error', () => {});

  await new Promise((resolve) => {
    let done = false;
    const finish = (reason) => {
      if (done) return;
      done = true;
      clearTimeout(deadline);
      report.probe.shutdown = reason;
      child.stdin.end();
      resolve();
    };
    const deadline = setTimeout(() => finish('initialize-timeout'), 15000);
    child.on('error', (error) => {
      report.probe.errorCode = error.code || error.name;
      finish('spawn-error');
    });
    child.on('exit', (code, signal) => {
      report.probe.exitCode = code;
      report.probe.signal = signal;
      finish('exited');
    });
    child.stdout.on('data', (chunk) => {
      stdoutBuffer += chunk.toString('utf8');
      let newline;
      while ((newline = stdoutBuffer.indexOf('\n')) >= 0) {
        const line = stdoutBuffer.slice(0, newline);
        stdoutBuffer = stdoutBuffer.slice(newline + 1);
        let message;
        try { message = JSON.parse(line); } catch { continue; }
        if (message.id !== 'code-link-research-init') continue;
        if (message.error) {
          report.probe.rpcErrorCode = message.error.code;
          finish('initialize-rpc-error');
          return;
        }
        report.probe.initializeSucceeded = Boolean(message.result);
        report.probe.resultFields = Object.keys(message.result || {});
        child.stdin.write(JSON.stringify({ method: 'initialized', params: {} }) + '\n');
        report.probe.sentMethods.push('initialized');
        finish('stdin-closed-after-initialize');
      }
    });
    child.stdin.write(JSON.stringify({
      method: 'initialize',
      id: 'code-link-research-init',
      params: { clientInfo: { name: 'code_link_research', title: 'Code Link Research', version: '0.0.1' } },
    }) + '\n');
    report.probe.sentMethods.push('initialize');
  });

  if (child.exitCode === null && child.signalCode === null) {
    await new Promise((resolve) => {
      const timer = setTimeout(() => { child.kill(); resolve(); }, 1500);
      child.once('exit', () => { clearTimeout(timer); resolve(); });
    });
  }
  report.probe.stderrBytes = stderrBytes;
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + '\n');
  process.stdout.write(JSON.stringify(report, null, 2) + '\n');
  if (!report.probe.initializeSucceeded) process.exitCode = 1;
}

main().catch((error) => {
  process.stderr.write(`${error.name}: ${error.message}\n`);
  process.exitCode = 1;
});
