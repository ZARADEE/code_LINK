const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const elements=new Map(['terminal','windowText'].map(id=>[id,{style:{},textContent:'',scrollTop:0,scrollHeight:1800,clientHeight:400,events:{},addEventListener(name,fn){this.events[name]=fn;}}]));
let terminal,write;
class Terminal {
 constructor(){terminal=this;this.buffer={active:{baseY:100,viewportY:100}};}
 open(){} onData(fn){this.data=fn;}
 reset(){this.buffer.active.baseY=0;this.buffer.active.viewportY=0;}
 write(text,done){write=()=>{this.buffer.active.baseY+=text.length;if(this.buffer.active.viewportY===0)this.buffer.active.viewportY=this.buffer.active.baseY;done();};}
 scrollLines(lines){this.scrollToLine(this.buffer.active.viewportY+lines);}
 scrollToLine(line){this.buffer.active.viewportY=Math.max(0,Math.min(line,this.buffer.active.baseY));}
 scrollToBottom(){this.scrollToLine(this.buffer.active.baseY);}
}
const sent=[],keys=[],window={addEventListener(){}};
const context=vm.createContext({Terminal,window,document:{getElementById:id=>elements.get(id)},innerWidth:600,innerHeight:400,CodeLink:{keys:s=>keys.push(s),scroll:(...args)=>sent.push(args)}});
vm.runInContext(fs.readFileSync('android/app/src/main/assets/terminal-scroll.js','utf8'),context);
function swipe(element,from,to,x=100){const events=elements.get(element).events;events.touchstart({touches:[{clientX:x,clientY:from}]});events.touchmove({touches:[{clientX:x,clientY:to}],preventDefault(){},stopImmediatePropagation(){}});events.touchend();}
window.configureTerminal('pc-window',true,true);window.showWindowText('full history','visible bottom');
assert.equal(elements.get('windowText').textContent,'visible bottom');
swipe('windowText',100,320);assert.deepEqual(sent.pop(),['pc-window',-10,false]);
swipe('windowText',320,100);assert.deepEqual(sent.pop(),['pc-window',10,false]);
assert.deepEqual(keys,[],'swipes must never inject terminal keys');
window.configureTerminal('pc-window',true,false);assert.equal(elements.get('windowText').textContent,'full history');
elements.get('windowText').scrollTop=500;swipe('windowText',100,220);assert.equal(elements.get('windowText').scrollTop,380);
window.showWindowText('full history plus fresh output','new viewport');assert.equal(elements.get('windowText').scrollTop,380,'new output must not snap local reading to bottom');
assert.equal(sent.length,0,'local browsing must not control the desktop');
window.configureTerminal('managed',false,false);window.appendTerminal('a'.repeat(100),true);write();
assert.equal(terminal.buffer.active.viewportY,100);
swipe('terminal',100,260);assert.equal(terminal.buffer.active.viewportY,90);
window.appendTerminal('next',false);write();assert.equal(terminal.buffer.active.viewportY,90);
window.scrollLatest();assert.equal(terminal.buffer.active.viewportY,104);
window.appendTerminal('new',false);write();assert.equal(terminal.buffer.active.viewportY,107);
window.configureTerminal('another',false,false);window.appendTerminal('reset',true);write();assert.equal(terminal.buffer.active.viewportY,5);
console.log('PASS: swipe directions, viewport/local modes, no key injection, history position, follow latest and session switch');
