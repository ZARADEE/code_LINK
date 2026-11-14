// Uses a selected USB ADB device for a temporary loopback echo probe.
// Reads device properties, creates one reverse mapping, then removes that mapping.
// Does not install an APK or change device settings.
const { execFile, spawn } = require('node:child_process');
const { promisify } = require('node:util');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const crypto = require('node:crypto');
const run = promisify(execFile);

async function main() {
  const [adbExecutable, serial, outputPath] = process.argv.slice(2);
  if (!adbExecutable || !serial || !outputPath) {
    throw new Error('Usage: node verify-usb-link.cjs ADB_EXECUTABLE USB_SERIAL OUTPUT_JSON');
  }
  const adb = async (...args) => (await run(adbExecutable,
    ['-P', '5038', '-s', serial, ...args],
    { encoding: 'utf8', windowsHide: true, timeout: 10000, maxBuffer: 64 * 1024 })).stdout.trim();

  const usbSelection = (await run(adbExecutable,
    ['-P', '5038', '-d', 'get-serialno'],
    { encoding: 'utf8', windowsHide: true, timeout: 10000 })).stdout.trim();
  const report = {
    checkedAt: new Date().toISOString(),
    deviceAlias: 'sha256:' + crypto.createHash('sha256').update(serial).digest('hex').slice(0, 12),
    state: await adb('get-state'),
    model: await adb('shell', 'getprop', 'ro.product.model'),
    androidVersion: await adb('shell', 'getprop', 'ro.build.version.release'),
    buildDisplayId: await adb('shell', 'getprop', 'ro.build.display.id'),
    transport: usbSelection === serial ? 'usb' : 'unconfirmed',
    transportVerification: 'adb -d selected the same device; -d selects a USB device',
    reverseCreated: false,
    androidToPcBytesVerified: 0,
    pcToAndroidReplyVerified: false,
    reverseRemoved: false,
    apkInstalled: false,
    deviceSettingsChanged: false,
  };
  if (report.state !== 'device' || report.transport !== 'usb') {
    throw new Error('Selected device is not an authorized USB transport');
  }

  const payload = crypto.randomBytes(512).toString('hex');
  const expectedInput = payload;
  const expectedReply = 'CODE_LINK_ACK_' + crypto.createHash('sha256').update(payload).digest('hex');
  const server = net.createServer((socket) => {
    report.pcAcceptedConnection = true;
    socket.setTimeout(5000, () => socket.destroy());
    let input = '';
    socket.on('error', () => {});
    socket.on('data', (chunk) => {
      input += chunk.toString('utf8');
      report.androidToPcBytesObserved = Buffer.byteLength(input);
      report.inputEqualsExpected = input === expectedInput;
      if (input.length >= expectedInput.length) {
        if (input === expectedInput) {
          report.androidToPcBytesVerified = Buffer.byteLength(input);
          socket.end(expectedReply + '\n');
        } else {
          socket.destroy();
        }
      }
    });
  });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, 'localhost', resolve);
  });

  let androidPort;
  try {
    const pcPort = server.address().port;
    androidPort = Number(await adb('reverse', 'tcp:0', `tcp:${pcPort}`));
    if (!Number.isInteger(androidPort) || androidPort < 1 || androidPort > 65535) {
      throw new Error('ADB did not return a valid allocated reverse port');
    }
    report.reverseCreated = true;
    const reply = await new Promise((resolve, reject) => {
      const child = spawn(adbExecutable, ['-P', '5038', '-s', serial,
        'shell', '-T', 'toybox', 'nc', '-w', '3', '-W', '3', '127.0.0.1', String(androidPort)],
      { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
      let received = '';
      child.stdin.on('error', () => {});
      child.stderr.resume();
      const timer = setTimeout(() => { child.kill(); reject(new Error('USB echo timeout')); }, 6000);
      child.on('error', (error) => { clearTimeout(timer); reject(error); });
      child.stdout.on('data', (chunk) => {
        received += chunk.toString('utf8');
        if (received.trim() === expectedReply) child.stdin.end();
      });
      child.on('exit', () => { clearTimeout(timer); resolve(received.trim()); });
      child.stdin.write(payload);
    });
    report.replyBytesObserved = Buffer.byteLength(reply);
    report.pcToAndroidReplyVerified = reply === expectedReply;
  } finally {
    if (androidPort) {
      await adb('reverse', '--remove', `tcp:${androidPort}`);
      report.reverseRemoved = true;
    }
    await new Promise((resolve) => server.close(resolve));
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + '\n');
    process.stdout.write(JSON.stringify(report, null, 2) + '\n');
  }
  if (!report.pcToAndroidReplyVerified || !report.androidToPcBytesVerified) {
    process.exitCode = 1;
  }
}

main().catch((error) => {
  process.stderr.write(`${error.name}: ${error.message}\n`);
  process.exitCode = 1;
});
