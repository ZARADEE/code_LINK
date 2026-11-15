const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const html = fs.readFileSync('apple/Resources/Terminal/terminal.html', 'utf8');
const inline = html.match(/<script>\s*([\s\S]*?)<\/script>/)[1];
const elements = new Map(['terminal', 'snapshot'].map(id => [id, {
  style: {}, textContent: '', scrollHeight: 0, scrollTop: 0, clientHeight: 0,
}]));
let terminal, writeComplete;
const messages = [];
class Terminal {
  constructor(options) {
    terminal = this;
    this.options = options;
    this.rows = options.rows;
    this.text = '';
    this.buffer = {active: {viewportY: 0, getLine: row => ({translateToString: () => row === 0 ? this.text : ''})}};
  }
  open() {}
  onData() {}
  resize(cols, rows) { this.rows = rows; }
  reset() { this.text = ''; }
  write(text, callback) { writeComplete = () => { this.text += text; callback(); }; }
}
const window = {addEventListener() {}, webkit: {messageHandlers: {terminal: {postMessage: value => messages.push(value)}}}};
const context = vm.createContext({Terminal, window, document: {getElementById: id => elements.get(id)},
  innerWidth: 390, innerHeight: 640, requestAnimationFrame: callback => queueMicrotask(callback)});
vm.runInContext(inline, context);
assert.equal(messages[0].event, 'ready');
(async () => {
  let completed = false;
  const rendering = window.renderTerminal({text: 'CL-fresh-output', reset: true, observer: false, enabled: true})
    .then(screen => { completed = true; return screen; });
  await Promise.resolve();
  assert.equal(completed, false, 'render must wait for xterm to consume the output');
  writeComplete();
  assert.match(await rendering, /CL-fresh-output/);
  assert.equal(terminal.options.disableStdin, false);
  const observed = await window.renderTerminal({text: 'observer snapshot', reset: true, observer: true, enabled: false});
  assert.equal(observed, 'observer snapshot');
  assert.equal(elements.get('terminal').style.display, 'none');
  assert.equal(terminal.options.disableStdin, true);
  console.log('PASS: terminal readiness, asynchronous render acknowledgement, screen text and observer output');
})().catch(error => { console.error(error); process.exitCode = 1; });
