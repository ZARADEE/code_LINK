const fs=require('node:fs');
const path=require('node:path');
const cp=require('node:child_process');
const {BrokerClient}=require('./broker-client.cjs');
const root=path.resolve(__dirname,'..');
const work=path.join(root,'.local','remote-acceptance');
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function adb(...args){return cp.execFileSync(path.join(root,'.tools/platform-tools/adb.exe'),['-P',process.env.CODELINK_ADB_PORT||'5037','-d',...args],{encoding:'utf8',timeout:30000,windowsHide:true});}
(async()=>{
 const client=await new BrokerClient(process.argv.includes('--main')?'main':process.env.CODELINK_ACCEPTANCE_INSTANCE||'remote-acceptance').connect();
 try{
  const command=process.argv[2]||'status';
  if(command==='setup'){
   await client.action('remoteAccess',{enabled:true});
   await client.until(()=>client.state.remote?.relayOnline,60000);
   await client.action('rotatePairing');await sleep(500);
   const p=client.state.pairing,r=client.state.remote;
   const attempt=require('node:crypto').randomUUID().replaceAll('-','');
   const invite='codelink:'+Buffer.from(JSON.stringify({host:'127.0.0.1',port:p.port,pin:p.fingerprint,code:p.remoteCode,transport:'Remote',remoteProtocol:1,endpoint:r.endpoint,relays:r.relays,attempt})).toString('base64url');
   fs.writeFileSync(path.join(work,'invite.txt'),invite);
   adb('shell','am','start','-n','com.codelink.app/.MainActivity','--es','invite',invite);
   await client.until(()=>client.state.devices.find(d=>d.online&&d.transport==='Remote'&&d.pairingAttempt===attempt),70000);
   const device=client.state.devices.find(d=>d.online&&d.pairingAttempt===attempt);
   const created=await client.action('create',{kind:'terminal',name:'REMOTE ACCEPTANCE',cwd:work,command:'python -u "'+path.join(root,'scripts/remote-terminal-fixture.py')+'"'});
   await client.action('map',{sessionId:created.sessionId,deviceId:device.id,enabled:true,control:true});
   fs.writeFileSync(path.join(work,'test-session.json'),JSON.stringify({sessionId:created.sessionId,deviceId:device.id}));
   await sleep(1200);
   console.log(JSON.stringify({connected:true,transport:device.transport,path:client.state.remote.path,sessionId:created.sessionId}));
  } else if(command==='background-setup'){
   const {deviceId}=JSON.parse(fs.readFileSync(path.join(work,'test-session.json')));
   const gate=path.join(work,'background-'+Date.now()+'.gate');
   const created=await client.action('create',{kind:'terminal',name:'REMOTE BACKGROUND',cwd:work,command:'python -u "'+path.join(root,'scripts/remote-terminal-fixture.py')+'" --gate "'+gate+'"'});
   await client.action('map',{sessionId:created.sessionId,deviceId,enabled:true,control:true});
   fs.writeFileSync(path.join(work,'background-session.json'),JSON.stringify({sessionId:created.sessionId,deviceId,gate}));
   console.log('Background completion fixture is waiting.');
  } else if(command==='background-complete'){
   const test=JSON.parse(fs.readFileSync(path.join(work,'background-session.json')));
   fs.writeFileSync(test.gate,'complete');
   await client.until(()=>client.state.sessions.find(s=>s.id===test.sessionId)?.status==='completed',10000);
   console.log(JSON.stringify({completed:true,deviceOnline:client.state.devices.find(d=>d.id===test.deviceId)?.online,remotePath:client.state.remote.path}));
  } else if(command==='reset-terminal'){
   const old=JSON.parse(fs.readFileSync(path.join(work,'test-session.json')));
   if(client.state.sessions.some(s=>s.id===old.sessionId))await client.action('delete',{sessionId:old.sessionId});
   const created=await client.action('create',{kind:'terminal',name:'REMOTE ACCEPTANCE',cwd:work,command:'python -u "'+path.join(root,'scripts/remote-terminal-fixture.py')+'"'});
   await client.action('map',{sessionId:created.sessionId,deviceId:old.deviceId,enabled:true,control:true});
   fs.writeFileSync(path.join(work,'test-session.json'),JSON.stringify({sessionId:created.sessionId,deviceId:old.deviceId}));console.log('Acceptance terminal ready.');
  } else if(command==='test-output'){
   const {sessionId}=JSON.parse(fs.readFileSync(path.join(work,'test-session.json')));client.watch(sessionId);
   await client.until(()=>client.outputs.has(sessionId));await sleep(500);
   const output=client.outputs.get(sessionId);console.log(JSON.stringify({length:output.length,tail:output.slice(-2500)}));
  } else if(command==='input-check'){
   const {sessionId}=JSON.parse(fs.readFileSync(path.join(work,'test-session.json')));
   client.watch(sessionId);
   const value=process.argv[3]||'WAN_PAD_OK';
   await client.until(()=>(client.outputs.get(sessionId)||'').includes('RECEIVED:'+value),30000);
   console.log(JSON.stringify({inputReceived:true,value,path:client.state.remote.path}));
  } else if(command==='cleanup-terminal'){
   const test=JSON.parse(fs.readFileSync(path.join(work,'test-session.json')));
   if(client.state.sessions.some(s=>s.id===test.sessionId))await client.action('delete',{sessionId:test.sessionId});
   console.log('Acceptance-only terminal removed; device pairing preserved.');
  } else if(command==='connection-status'){
   console.log(JSON.stringify({remote:client.state.remote,devices:client.state.devices.map(d=>({online:d.online,transport:d.transport})),sessionCount:client.state.sessions.length}));
  } else if(command==='action'){
   const result=await client.action(process.argv[3],JSON.parse(process.argv[4]||'{}'));
   console.log(JSON.stringify(result));
  } else if(command==='invitation'){
   // File only: this contains temporary credentials and must not enter logs.
   fs.writeFileSync(path.join(work,'admin-state.json'),JSON.stringify(client.state));
   console.log('Local test state saved.');
  } else {
   const {pairing,...state}=client.state;console.log(JSON.stringify(state));
  }
 }finally{client.close();}
})().catch(error=>{console.error(error.message);process.exitCode=1;});
