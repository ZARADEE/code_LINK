/* Touch browsing never translates gestures into terminal input keys. */
const terminal=new Terminal({cols:120,rows:32,fontSize:12,fontFamily:'monospace',theme:{background:'#192d27',foreground:'#e4ebdd'},scrollback:5000,convertEol:false,scrollOnUserInput:false});
const screen=document.getElementById('windowText'),grid=document.getElementById('terminal');
terminal.open(grid);
terminal.onData(s=>CodeLink.keys(s));
let screenColumns=80,screenRows=1,session='',observer=false,syncDesktop=false,following=true;
let gesture=null,fraction=0,renderVersion=0,windowFull='',windowVisible='';
function bottom(){return observer?screen.scrollHeight-screen.clientHeight-screen.scrollTop<3:terminal.buffer.active.viewportY>=terminal.buffer.active.baseY}
function fitScreen(){
  const width=(innerWidth-24)/(screenColumns*.62),height=syncDesktop?(screen.clientHeight-24)/(screenRows*1.3):14;
  screen.style.fontSize=Math.max(syncDesktop?6:9,Math.min(14,width,height))+'px';
}
window.configureTerminal=(id,isObserver,desktop)=>{
  if(session!==id||observer!==isObserver){following=true;gesture=null;fraction=0;renderVersion++;windowFull='';windowVisible='';screen.textContent='';screen.scrollTop=0;terminal.reset();}
  const modeChanged=syncDesktop!==(isObserver&&desktop);
  session=id;observer=isObserver;syncDesktop=isObserver&&desktop;
  if(modeChanged&&observer)window.showWindowText(windowFull,windowVisible);
};
window.appendTerminal=(text,reset)=>{
  observer=false;screen.style.display='none';grid.style.display='block';
  const version=renderVersion,follow=following,position=terminal.buffer.active.viewportY;
  if(reset)terminal.reset();
  terminal.write(text,()=>{
    if(version!==renderVersion)return;
    if(follow&&following)terminal.scrollToBottom();
    else if(!follow)terminal.scrollToLine(Math.min(position,terminal.buffer.active.baseY));
  });
};
window.showWindowText=(full,visible)=>{
  windowFull=full;windowVisible=visible==null?full:visible;
  const text=syncDesktop?windowVisible:windowFull;
  const opening=screen.style.display!=='block';
  grid.style.display='none';screen.style.display='block';
  if(screen.textContent===text&&!opening)return;
  const top=screen.scrollTop;
  screenRows=Math.max(1,text.replace(/\n$/,'').split('\n').length);
  screenColumns=Math.max(40,...text.split('\n').map(line=>Array.from(line.replace(/\s+$/,'')).reduce((n,c)=>n+(c.codePointAt(0)>0x2e7f?2:1),0)));
  fitScreen();screen.textContent=text;
  if(syncDesktop)screen.scrollTop=0;
  else screen.scrollTop=following?screen.scrollHeight:top;
};
window.scrollLatest=()=>{
  following=true;fraction=0;
  if(observer){screen.scrollTop=screen.scrollHeight;if(syncDesktop)CodeLink.scroll(session,0,true);}
  else terminal.scrollToBottom();
};
function scrollPixels(delta){
  if(observer&&!syncDesktop){screen.scrollTop+=delta;following=bottom();return;}
  const lineHeight=observer?22:16;
  fraction+=delta/lineHeight;
  const lines=Math.trunc(fraction);if(!lines)return;fraction-=lines;
  if(observer){following=false;CodeLink.scroll(session,Math.max(-36,Math.min(36,lines)),false);}
  else{terminal.scrollLines(lines);following=bottom();}
}
for(const element of [grid,screen]){
  element.addEventListener('touchstart',event=>{
    if(event.touches.length!==1){gesture=null;return;}
    const touch=event.touches[0];gesture={x:touch.clientX,y:touch.clientY,last:touch.clientY,vertical:false,horizontal:false};fraction=0;
  },{capture:true,passive:true});
  element.addEventListener('touchmove',event=>{
    if(!gesture||event.touches.length!==1){gesture=null;return;}
    const touch=event.touches[0],dx=touch.clientX-gesture.x,dy=touch.clientY-gesture.y;
    if(!gesture.vertical&&!gesture.horizontal){if(Math.max(Math.abs(dx),Math.abs(dy))<8)return;gesture.vertical=Math.abs(dy)>Math.abs(dx);gesture.horizontal=!gesture.vertical;}
    if(!gesture.vertical)return;
    event.preventDefault();event.stopImmediatePropagation();
    scrollPixels(gesture.last-touch.clientY);gesture.last=touch.clientY;
  },{capture:true,passive:false});
  element.addEventListener('touchend',()=>{gesture=null;},{capture:true,passive:true});
  element.addEventListener('touchcancel',()=>{gesture=null;fraction=0;},{capture:true,passive:true});
  element.addEventListener('wheel',event=>{
    if(event.ctrlKey||Math.abs(event.deltaX)>Math.abs(event.deltaY))return;
    event.preventDefault();event.stopImmediatePropagation();scrollPixels(event.deltaY*(event.deltaMode===1?16:event.deltaMode===2?innerHeight:1));
  },{capture:true,passive:false});
}
screen.addEventListener('scroll',()=>{if(!syncDesktop)following=bottom();},{passive:true});
window.addEventListener('resize',fitScreen);
