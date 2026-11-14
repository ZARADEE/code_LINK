from pathlib import Path
p=Path('android/app/src/main/java/com/codelink/app/LinkConnection.java')
s=p.read_text('utf8')
s=s.replace('private DataOutputStream output;', 'private volatile DataOutputStream output;')
s=s.replace('public void start(){stop();enabled=true;final int g=generation;', 'public synchronized void start(){stop();enabled=true;final int g=generation;')
s=s.replace('public void stop(){enabled=false;', 'public synchronized void stop(){enabled=false;')
s=s.replace('private void loop(int g){int retry=0;while(enabled&&g==generation){try{', 'private synchronized void own(int g,Closeable socket)throws IOException{if(g!=generation||!enabled){socket.close();throw new IOException("连接已切换");}connection=socket;}\n private synchronized void ready(int g,OutputStream out)throws IOException{if(g!=generation||!enabled)throw new IOException("连接已切换");output=new DataOutputStream(out);}\n private void loop(int g){int retry=0;while(enabled&&g==generation){Closeable owned=null;try{')
s=s.replace('connection=socket;adapter.cancelDiscovery();', 'owned=socket;own(g,socket);adapter.cancelDiscovery();')
s=s.replace('connection=raw;raw.connect', 'owned=raw;own(g,raw);raw.connect')
s=s.replace('connection=socket;socket.setSoTimeout', 'owned=socket;own(g,socket);socket.setSoTimeout')
s=s.replace('output=new DataOutputStream(socket.getOutputStream());', 'ready(g,socket.getOutputStream());')
s=s.replace('JSONObject msg=new JSONObject(new String(bytes,"UTF-8"));', 'if(g!=generation||!enabled)break;JSONObject msg=new JSONObject(new String(bytes,"UTF-8"));')
s=s.replace('finally{Closeable old=connection;connection=null;output=null;if(old!=null)try{old.close();}catch(Exception ignored){}}', 'finally{synchronized(this){if(g==generation){connection=null;output=null;}}if(owned!=null)try{owned.close();}catch(Exception ignored){}}')
p.write_text(s,'utf8')
p=Path('android/app/src/main/java/com/codelink/app/MainActivity.java')
s=p.read_text('utf8')
s=s.replace('private TextView connectionStatus,', 'private LinearLayout terminalKeys;private TextView connectionStatus,')
s=s.replace('if(!link.pin.isEmpty())link.start();', 'if(!link.pin.isEmpty()&&getIntent().getStringExtra("invite")==null&&getIntent().getStringExtra("btAddress")==null)link.start();')
s=s.replace('connectionStatus.setTextColor(online?GREEN:0xffa35a33);if(!online)', 'connectionStatus.setTextColor(online?GREEN:0xffa35a33);input.setEnabled(online&&controller);if(!online)')
s=s.replace('controller?"输入下一项任务或终端命令":"请在电脑为此会话分配控制权"', 'controller?"输入下一项任务或终端命令":"请在电脑映射此命令行"')
s=s.replace('input.setHint("输入下一项任务或终端命令");', 'input.setHint("输入下一项任务或终端命令");')
s=s.replace('android.text.InputType.TYPE_TEXT_FLAG_CAP_SENTENCES', 'android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS')
s=s.replace('setContentView(root);building=false;', '''terminalKeys=new LinearLayout(this);terminalKeys.setOrientation(0);
  String[] labels={"Esc","Tab","Ctrl+C","↑","↓","←","→"};
  String[] codes={"\\u001b","\\t","\\u0003","\\u001b[A","\\u001b[B","\\u001b[D","\\u001b[C"};
  for(int i=0;i<labels.length;i++){final String value=codes[i];terminalKeys.addView(button(labels[i],()->{if(controller)try{action("input",fields().put("text",value).put("raw",true));}catch(Exception ignored){}}));}
  content.addView(terminalKeys,new LinearLayout.LayoutParams(-1,dp(48)));terminalKeys.setVisibility(View.GONE);
  setContentView(root);building=false;''')
s=s.replace('boolean isTerminal=currentKind.equals("terminal");', 'boolean isTerminal=currentKind.equals("terminal");terminalKeys.setVisibility(isTerminal?View.VISIBLE:View.GONE);')
s=s.replace('Arrays.asList("completed","failed","interrupted")', 'Arrays.asList("completed","failed","interrupted","exited")')
p.write_text(s,'utf8')
