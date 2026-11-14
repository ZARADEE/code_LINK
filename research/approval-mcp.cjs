// Disposable MCP server: exercises real Codex elicitation without file or command side effects.
const readline=require('node:readline');
const fs=require('node:fs');const path=require('node:path');
function trace(value){fs.appendFileSync(path.join(__dirname,'../.local/mcp-validation.jsonl'),JSON.stringify({at:new Date().toISOString(),...value})+'\n');}
let waiting;
function send(msg){process.stdout.write(JSON.stringify({jsonrpc:'2.0',...msg})+'\n');}
readline.createInterface({input:process.stdin}).on('line',line=>{
 const m=JSON.parse(line);
 trace({method:m.method,id:m.id,...(m.id==='pad-confirm'?{result:m.result,error:m.error}:{})});
 if(m.method==='initialize')send({id:m.id,result:{protocolVersion:m.params.protocolVersion,capabilities:{tools:{}},serverInfo:{name:'codelink-validation',version:'1.0'}}});
 else if(m.method==='tools/list')send({id:m.id,result:{tools:[{name:'confirm_link',description:'Show a confirmation form to validate the Code Link tablet interaction. No external side effects.',inputSchema:{type:'object',properties:{}}}]}});
 else if(m.method==='tools/call'){
  waiting=m.id;
  send({id:'pad-confirm',method:'elicitation/create',params:{mode:'form',message:'Code Link 真机交互验证：此操作只返回确认结果，不修改文件。',requestedSchema:{type:'object',properties:{confirmed:{type:'boolean',title:'确认平板操作已送达',default:true}},required:['confirmed']}}});
 }else if(m.id==='pad-confirm'){
  send({id:waiting,result:{content:[{type:'text',text:'PAD_APPROVAL_RESULT='+JSON.stringify(m.result)}]}});
 }else if(m.method==='ping')send({id:m.id,result:{}});
 else if(m.id!==undefined)send({id:m.id,error:{code:-32601,message:'Method not found'}});
});
