const net = require('node:net');
const crypto = require('node:crypto');
class BrokerClient {
  constructor(instance = process.env.CODELINK_INSTANCE || '') { this.instance = instance; this.state = null; this.outputs = new Map(); this.pending = new Map(); this.buffer = Buffer.alloc(0); }
  async connect() {
    this.socket = net.createConnection('\\\\.\\pipe\\CodeLink.Local.v1' + (this.instance ? '.' + this.instance : ''));
    this.socket.on('data', data => {
      this.buffer = Buffer.concat([this.buffer, data]);
      while (this.buffer.length >= 4 && this.buffer.length >= this.buffer.readUInt32BE(0) + 4) {
        const length = this.buffer.readUInt32BE(0);
        const msg = JSON.parse(this.buffer.subarray(4, 4 + length).toString('utf8'));
        this.buffer = this.buffer.subarray(4 + length);
        if (msg.type === 'state') {
          this.state = msg;
          this.outputs.set(msg.selected, (msg.reset ? '' : this.outputs.get(msg.selected) || '') + msg.output);
          this.onState?.(msg);
        }
        if (msg.type === 'result') {
          const pending = this.pending.get(msg.id);this.pending.delete(msg.id);
          if (msg.result.ok) pending?.resolve(msg.result);
          else pending?.reject(new Error(msg.result.error));
        }
      }
    });
    await new Promise((resolve, reject) => {this.socket.once('connect', resolve);this.socket.once('error', reject);});
    this.send({type:'hello'});
    await this.until(() => this.state, 7000);
    return this;
  }
  send(msg) { const bytes = Buffer.from(JSON.stringify(msg));const frame = Buffer.alloc(bytes.length + 4);frame.writeUInt32BE(bytes.length);bytes.copy(frame, 4);this.socket.write(frame); }
  action(action, fields={}) {
    const id = crypto.randomUUID();
    return new Promise((resolve,reject) => {
      const timeout = setTimeout(() => {this.pending.delete(id);reject(new Error('Action timed out: '+action));},100000);
      this.pending.set(id,{resolve:v=>{clearTimeout(timeout);resolve(v);},reject:e=>{clearTimeout(timeout);reject(e);}});
      this.send({type:'action',id,action,...fields});
    });
  }
  watch(sessionId) {this.send({type:'watch',sessionId});}
  async until(check,timeout=10000) { const end=Date.now()+timeout;while(!check()){if(Date.now()>end)throw new Error('Condition timed out');await new Promise(r=>setTimeout(r,60));}return check();}
  close(){this.socket?.destroy();}
}
module.exports={BrokerClient};
if(require.main===module)(async()=>{
 const client=await new BrokerClient().connect();
 try{
  if(process.argv[2]==='action')console.log(JSON.stringify(await client.action(process.argv[3],JSON.parse(process.argv[4]||'{}'))));
  else if(process.argv[2]==='invite'){
   await client.action('rotatePairing');await new Promise(r=>setTimeout(r,300));
   const p=client.state.pairing;console.log('codelink:'+Buffer.from(JSON.stringify({host:process.argv[3]||'127.0.0.1',port:p.port,pin:p.fingerprint,code:p.code})).toString('base64url'));
  }else {const {pairing,...publicState}=client.state;console.log(JSON.stringify(publicState,null,2));}
 }finally{client.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
