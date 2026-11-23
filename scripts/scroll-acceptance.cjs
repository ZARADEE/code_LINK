const fs=require('node:fs'),path=require('node:path');
const {BrokerClient}=require('./broker-client.cjs');
const root=path.resolve(__dirname,'..'),file=path.join(root,'.local/scroll-test-session.json');
(async()=>{const client=await new BrokerClient('main').connect();try{
 function testDevice(){const devices=client.state.devices.filter(d=>d.online&&d.transport==='Remote'&&(!process.env.CODELINK_TEST_DEVICE_ID||d.id===process.env.CODELINK_TEST_DEVICE_ID));if(devices.length!==1)throw Error('Select one remote device with CODELINK_TEST_DEVICE_ID');return devices[0];}
 const op=process.argv[2]||'state';
 if(op==='managed'){
  const device=testDevice();
  const created=await client.action('create',{kind:'terminal',name:'CODELINK_SCROLL_MANAGED',cwd:root,command:'python -u "'+path.join(root,'scripts/scroll-terminal-fixture.py')+'"'});
  await client.action('map',{sessionId:created.sessionId,deviceId:device.id,enabled:true,control:true});
  fs.writeFileSync(path.join(root,'.local/scroll-managed-session.json'),JSON.stringify({sessionId:created.sessionId}));
  console.log('Disposable managed terminal mapped.');
 }else if(op==='cleanup-all'){
  for(const s of client.state.sessions.filter(s=>['CODELINK_SCROLL_TEST','CODELINK_SCROLL_ALT','CODELINK_SCROLL_MANAGED','CODELINK_CLICK_TEST','CODELINK_PHONE_UI_TEST'].includes(s.name)))await client.action('delete',{sessionId:s.id});
  console.log('Disposable test mappings removed.');
 }else if(op==='setup'){
  const name=process.argv.includes('--reminder')?'CODELINK_PHONE_UI_TEST':process.argv.includes('--click')?'CODELINK_CLICK_TEST':process.argv.includes('--alt')?'CODELINK_SCROLL_ALT':'CODELINK_SCROLL_TEST';
  const command=client.state.commands.find(c=>c.name===name);if(!command)throw Error('Disposable terminal not discovered');
  const device=testDevice();
  const created=await client.action('observe',{processId:command.processId,windowHandle:command.windowHandle,name});
  await client.action('map',{sessionId:created.sessionId,deviceId:device.id,enabled:true,control:true});
  fs.writeFileSync(file,JSON.stringify({sessionId:created.sessionId,deviceId:device.id,handle:command.windowHandle,pid:command.processId}));console.log('Disposable terminal mapped.');
 }else{
  const test=JSON.parse(fs.readFileSync(file));client.watch(test.sessionId);await client.until(()=>client.state.selected===test.sessionId);
  if(op==='scroll'){console.log(JSON.stringify(await client.action('scroll',{sessionId:test.sessionId,lines:Number(process.argv[3]||-10),latest:process.argv.includes('--latest')})));}
  else if(op==='cleanup'){await client.action('delete',{sessionId:test.sessionId});console.log('Test mapping removed.');}
  else if(op==='pointer'){
    const lines=client.state.viewport.split('\n'),row=lines.findIndex(line=>line.includes('Back to bottom'));if(row<0)throw Error('Test button not visible');
    console.log(JSON.stringify(await client.action('pointer',{sessionId:test.sessionId,row,column:lines[row].indexOf('Back')+2,expectedLine:lines[row].replace(/\r$/,'')})));
  }else{console.log(JSON.stringify({viewport:client.state.viewport,outputLength:client.outputs.get(test.sessionId)?.length,remotePath:client.state.remote.path}));}
 }
}finally{client.close();}})().catch(error=>{console.error(error.message);process.exitCode=1;});
