package com.codelink.app;

import android.Manifest;import android.app.*;import android.content.*;import android.content.pm.PackageManager;import android.content.res.Configuration;import android.graphics.*;import android.graphics.drawable.*;import android.media.*;import android.os.*;import android.view.*;import android.view.inputmethod.*;import android.webkit.*;import android.widget.*;import android.bluetooth.*;
import java.util.*;import java.util.concurrent.*;import org.json.*;

public final class MainActivity extends Activity implements LinkConnection.Listener {
 private static final int CREAM=0xfff5f2e9,INK=0xff20352e,GREEN=0xff52745b,YELLOW=0xffe9bb45,DARK=0xff192d27;
 private SharedPreferences presentation;private ReminderState reminders;private String groupTab="B",notificationTarget="";private Button tabA,tabB,groupButton,renameButton;private TextView groupSummary,attention;
 private LinkRuntime runtime;private boolean observing=false;private LinkConnection link;private final ExecutorService io=Executors.newSingleThreadExecutor();private final Handler handler=new Handler(Looper.getMainLooper());
 private LinearLayout terminalKeys;private TextView connectionStatus,title,subtitle,emptyText,banner,output;private EditText input;private LinearLayout sessionList,approvals,body;private ScrollView outputScroll;private WebView terminal;private FrameLayout outputFrame;
 private LinearLayout sessionPane,detailPane,appHeader;private View keyStrip,detailActions;private ScrollView requestScroll;
 private boolean dualPane=false,detailOpen=false,keepAwake=true;private int requestCount=0;private final Bundle drafts=new Bundle();
 private View layoutRoot;private android.view.ViewTreeObserver.OnGlobalLayoutListener layoutListener;
 private AlertDialog connectionSetup;private boolean bluetoothScan=false;
 private Button scrollMode,scrollLatest;private LinearLayout scrollControls;private boolean desktopScroll=true;
 private Dialog terminalFullscreen;private View composerView;private boolean fullscreenInput=false,restoringOrientation=false;private int priorOrientation,priorDisplayOrientation;private String noticeBannerId="",noticeBannerKey="",noticeBannerText="";
 private int pendingScrollLines;private boolean pendingScrollLatest;private String scrollSession="",scrollRequest="";private volatile long scrollEpoch;
 private void detachLayoutListener(){if(layoutRoot!=null&&layoutListener!=null&&layoutRoot.getViewTreeObserver().isAlive())layoutRoot.getViewTreeObserver().removeOnGlobalLayoutListener(layoutListener);layoutRoot=null;layoutListener=null;}
 private String selected="",approvalsSignature="",sessionSignature="",currentKind="",fullText="";private JSONObject lastState;private boolean connected=false,controller=false,terminalReady=false,building=false;
 public void onCreate(Bundle saved){super.onCreate(saved);runtime=LinkRuntime.get(this);link=runtime.link;presentation=runtime.presentation;reminders=runtime.reminders;groupTab=presentation.getString("tab","B");keepAwake=presentation.getBoolean("keepAwake",true);if(keepAwake)getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);if(saved!=null){bluetoothScan=saved.getBoolean("bluetoothScan");selected=saved.getString("selected","");detailOpen=saved.getBoolean("detailOpen");Bundle prior=saved.getBundle("drafts");if(prior!=null)drafts.putAll(prior);}buildUi();handleIntent(getIntent());if(saved==null)showSplash();if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED)requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},100);if(getIntent().getStringExtra("invite")==null&&getIntent().getStringExtra("btAddress")==null)runtime.ensureStarted();}
 protected void onNewIntent(Intent intent){super.onNewIntent(intent);setIntent(intent);handleIntent(intent);if(observing&&lastState!=null)consumeNotificationTarget();}
 private void handleIntent(Intent intent){if(intent==null)return;String notice=intent.getStringExtra("notice-session");if(notice!=null){notificationTarget=notice;intent.removeExtra("notice-session");}String invite=intent.getStringExtra("invite"),bt=intent.getStringExtra("btAddress");try{if(invite!=null&&!invite.isEmpty()){link.invite(invite);String mode=intent.getStringExtra("mode");if(mode!=null)link.mode=mode;link.save();}if(bt!=null){link.btAddress=bt;link.mode="Bluetooth";link.save();}if(invite!=null||bt!=null){if(link.mode.equals("Bluetooth")&&bt==null)bluetoothDialog();else runtime.start();}}catch(Exception e){toast(e.getMessage());}}
 public void onConfigurationChanged(Configuration c){super.onConfigurationChanged(c);if(terminalFullscreen!=null||restoringOrientation)return;buildUi();JSONObject snapshot=runtime.snapshot();if(snapshot!=null)applyState(snapshot);}
 protected void onSaveInstanceState(Bundle saved){saveDraft();saved.putBoolean("bluetoothScan",bluetoothScan);saved.putBundle("drafts",drafts);saved.putString("selected",selected);saved.putBoolean("detailOpen",detailOpen);super.onSaveInstanceState(saved);}
 private void saveDraft(){if(input!=null&&!selected.isEmpty())drafts.putString(selected,input.getText().toString());}
 private void showSessionList(){detailOpen=false;((InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(input.getWindowToken(),0);input.clearFocus();updatePanes();}
 private void updatePanes(){sessionPane.setVisibility(dualPane||!detailOpen?View.VISIBLE:View.GONE);detailPane.setVisibility(dualPane||detailOpen?View.VISIBLE:View.GONE);}
 @Override public void onBackPressed(){if(terminalFullscreen!=null){terminalFullscreen.dismiss();return;}if(!dualPane&&detailOpen){showSessionList();return;}super.onBackPressed();}
 private void fullscreenTerminal(){
  if(terminalFullscreen!=null||selected.isEmpty())return;
  ((InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(input.getWindowToken(),0);input.clearFocus();
  priorOrientation=getRequestedOrientation();priorDisplayOrientation=getResources().getConfiguration().orientation==Configuration.ORIENTATION_LANDSCAPE?android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE:android.content.pm.ActivityInfo.SCREEN_ORIENTATION_PORTRAIT;fullscreenInput=false;
  final View[] moved={outputFrame,composerView,keyStrip};final int[] positions=new int[moved.length];
  for(int i=0;i<moved.length;i++)positions[i]=detailPane.indexOfChild(moved[i]);
  for(View view:moved)detailPane.removeView(view);
  Dialog dialog=new Dialog(this,android.R.style.Theme_Material_Light_NoActionBar_Fullscreen);terminalFullscreen=dialog;
  LinearLayout full=new LinearLayout(this);full.setOrientation(1);full.setBackgroundColor(DARK);full.setPadding(dp(8),0,dp(8),0);full.setContentDescription("terminal-fullscreen-pane");
  LinearLayout bar=new LinearLayout(this);bar.setGravity(Gravity.CENTER_VERTICAL);bar.setPadding(0,dp(2),0,dp(2));
  Button back=button("返回",()->dialog.dismiss());back.setContentDescription("exit-terminal-fullscreen");bar.addView(back);
  TextView name=text(title.getText().toString(),13,0xffe4ebdd);name.setSingleLine(true);name.setEllipsize(android.text.TextUtils.TruncateAt.END);bar.addView(name,new LinearLayout.LayoutParams(0,-2,1));
  Button typing=button("输入",()->{fullscreenInput=!fullscreenInput;composerView.setVisibility(fullscreenInput?View.VISIBLE:View.GONE);keyStrip.setVisibility(fullscreenInput?View.VISIBLE:View.GONE);if(fullscreenInput){input.requestFocus();((InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).showSoftInput(input,InputMethodManager.SHOW_IMPLICIT);}else{((InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(input.getWindowToken(),0);input.clearFocus();}});typing.setContentDescription("fullscreen-input");bar.addView(typing);
  Button latest=button("回到最新",()->{if(terminalReady)terminal.evaluateJavascript("scrollLatest()",null);});latest.setContentDescription("fullscreen-latest");bar.addView(latest);
  full.addView(bar,new LinearLayout.LayoutParams(-1,dp(48)));full.addView(outputFrame,new LinearLayout.LayoutParams(-1,0,1));
  full.addView(composerView,new LinearLayout.LayoutParams(-1,-2));full.addView(keyStrip,new LinearLayout.LayoutParams(-1,dp(48)));composerView.setVisibility(View.GONE);keyStrip.setVisibility(View.GONE);
  dialog.setContentView(full);dialog.setOnDismissListener(d->{
   ((InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(input.getWindowToken(),0);input.clearFocus();
   for(int i=0;i<moved.length;i++){full.removeView(moved[i]);detailPane.addView(moved[i],positions[i],new LinearLayout.LayoutParams(-1,i==0?0:i==2?dp(48):-2,i==0?1:0));}
   composerView.setVisibility(View.VISIBLE);keyStrip.setVisibility(currentKind.equals("terminal")||currentKind.equals("observer")?View.VISIBLE:View.GONE);
   terminalFullscreen=null;restoringOrientation=true;setRequestedOrientation(priorDisplayOrientation);handler.postDelayed(()->{if(terminalFullscreen==null){boolean rotationLocked=android.provider.Settings.System.getInt(getContentResolver(),android.provider.Settings.System.ACCELEROMETER_ROTATION,0)==0;setRequestedOrientation(priorOrientation==android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED&&rotationLocked?priorDisplayOrientation:priorOrientation);restoringOrientation=false;}},1500);
  });
  dialog.show();Window window=dialog.getWindow();window.setLayout(-1,-1);window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);
  window.getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN|View.SYSTEM_UI_FLAG_HIDE_NAVIGATION|View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
  setRequestedOrientation(android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);
 }
 private int dp(float n){return(int)(n*getResources().getDisplayMetrics().density+0.5f);}
 private TextView text(String value,int size,int color){TextView v=new TextView(this);v.setText(value);v.setTextSize(size);v.setTextColor(color);return v;}
 private GradientDrawable bg(int color,int radius){GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(radius));return d;}
 private Button button(String label,Runnable action){Button b=new Button(this);b.setText(label);b.setTextColor(INK);b.setTextSize(13);b.setAllCaps(false);b.setMinWidth(dp(48));b.setMinimumWidth(dp(48));b.setMinHeight(dp(48));b.setMinimumHeight(dp(48));b.setBackground(bg(0xffe8e5d9,8));b.setPadding(dp(12),0,dp(12),0);LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-2,-2);p.setMargins(0,0,dp(8),0);b.setLayoutParams(p);b.setOnClickListener(v->action.run());return b;}
 private void buildUi(){
  detachLayoutListener();
  saveDraft();
  String draft=drafts.getString(selected,"");
  String connectionText=connectionStatus==null?"尚未连接":connectionStatus.getText().toString();
  if(terminal!=null){terminal.removeJavascriptInterface("CodeLink");terminal.destroy();}
  building=true;sessionSignature="";approvalsSignature="";terminalReady=false;
  Configuration config=getResources().getConfiguration();
  // Use the available window, so a tablet in split-screen gets the compact layout too.
  dualPane=config.screenWidthDp>=600&&config.screenHeightDp>=480;
  LinearLayout root=new LinearLayout(this);root.setOrientation(1);root.setBackgroundColor(CREAM);
  root.setPadding(dp(dualPane?20:12),dp(dualPane?12:6),dp(dualPane?20:12),dp(8));
  root.setFocusableInTouchMode(true);
  appHeader=new LinearLayout(this);appHeader.setOrientation(1);root.addView(appHeader,new LinearLayout.LayoutParams(-1,-2));
  LinearLayout header=new LinearLayout(this);header.setGravity(Gravity.CENTER_VERTICAL);appHeader.addView(header,new LinearLayout.LayoutParams(-1,-2));
  ImageView icon=new ImageView(this);icon.setImageResource(com.codelink.app.R.drawable.app_icon);icon.setScaleType(ImageView.ScaleType.CENTER_CROP);
  icon.setBackground(bg(YELLOW,10));icon.setClipToOutline(true);LinearLayout.LayoutParams ip=new LinearLayout.LayoutParams(dp(dualPane?44:36),dp(dualPane?44:36));ip.setMargins(0,0,dp(10),0);header.addView(icon,ip);
  LinearLayout brand=new LinearLayout(this);brand.setOrientation(1);brand.addView(text("Code Link",dualPane?25:21,INK));
  if(dualPane)brand.addView(text("电脑专注运行，进度随你掌握",12,0xff6c7b70));header.addView(brand,new LinearLayout.LayoutParams(0,-2,1));
  Button connect=button("连接电脑",()->connectionDialog());connect.setContentDescription("connect-device");header.addView(connect);
  Button more=button("更多",()->{});more.setContentDescription("connection-options");more.setOnClickListener(v->connectionMenu(v));header.addView(more);
  connectionStatus=text(connectionText,12,connected?GREEN:0xffa35a33);connectionStatus.setContentDescription("connection-status");connectionStatus.setMaxLines(2);
  connectionStatus.setEllipsize(android.text.TextUtils.TruncateAt.END);connectionStatus.setPadding(0,dp(6),0,dp(8));appHeader.addView(connectionStatus,new LinearLayout.LayoutParams(-1,-2));

  body=new LinearLayout(this);body.setOrientation(LinearLayout.HORIZONTAL);root.addView(body,new LinearLayout.LayoutParams(-1,0,1));
  sessionPane=new LinearLayout(this);sessionPane.setOrientation(1);sessionPane.setContentDescription("terminal-list-pane");sessionPane.setPadding(dp(12),dp(12),dp(12),dp(10));sessionPane.setBackground(bg(0xfffcfbf6,12));
  sessionPane.addView(text("命令行",18,INK));
  LinearLayout tabs=new LinearLayout(this);tabA=button("A · 逐个提醒",()->chooseGroup("A"));tabB=button("B · 全部完成",()->chooseGroup("B"));
  tabA.setContentDescription("group-tab-A");tabB.setContentDescription("group-tab-B");tabA.setPadding(dp(4),0,dp(4),0);tabB.setPadding(dp(4),0,dp(4),0);
  tabs.addView(tabA,new LinearLayout.LayoutParams(0,-2,1));tabs.addView(tabB,new LinearLayout.LayoutParams(0,-2,1));sessionPane.addView(tabs);
  groupSummary=text("本轮尚无运行任务",12,0xff6c7b70);groupSummary.setPadding(0,dp(8),0,dp(8));groupSummary.setContentDescription("group-summary");
  groupSummary.setOnClickListener(v->{if(groupTab.equals("B"))acknowledgeBatch();});sessionPane.addView(groupSummary);
  sessionList=new LinearLayout(this);sessionList.setOrientation(1);ScrollView listScroll=new ScrollView(this);listScroll.addView(sessionList);sessionPane.addView(listScroll,new LinearLayout.LayoutParams(-1,0,1));
  emptyText=text("连接后，这里会出现\n电脑映射给你的命令行。",14,0xff718071);emptyText.setPadding(0,dp(22),0,0);sessionList.addView(emptyText);
  LinearLayout.LayoutParams sp=new LinearLayout.LayoutParams(dualPane?dp(config.screenWidthDp>=840?280:220):-1,-1);sp.setMargins(0,0,dualPane?dp(12):0,0);body.addView(sessionPane,sp);
  detailPane=new LinearLayout(this);detailPane.setOrientation(1);detailPane.setContentDescription("terminal-detail-pane");body.addView(detailPane,new LinearLayout.LayoutParams(0,-1,1));
  {
   LinearLayout nav=new LinearLayout(this);nav.setGravity(Gravity.CENTER_VERTICAL);
   if(!dualPane){Button back=button("返回列表",()->showSessionList());back.setContentDescription("back-to-terminals");nav.addView(back);}
   View space=new View(this);nav.addView(space,new LinearLayout.LayoutParams(0,1,1));
   Button options=button("终端操作",()->{});options.setContentDescription("terminal-options");options.setOnClickListener(v->terminalMenu(v));nav.addView(options);
   Button fullscreen=button("全屏",()->fullscreenTerminal());fullscreen.setContentDescription("terminal-fullscreen");nav.addView(fullscreen);
   detailPane.addView(nav,new LinearLayout.LayoutParams(-1,-2));
  }
  title=text("选择电脑映射的命令行",dualPane?21:18,INK);title.setMaxLines(2);title.setEllipsize(android.text.TextUtils.TruncateAt.END);title.setContentDescription("session-title");detailPane.addView(title);
  subtitle=text("输出、授权与任务状态将在这里同步",12,0xff6c7b70);subtitle.setMaxLines(2);subtitle.setEllipsize(android.text.TextUtils.TruncateAt.END);subtitle.setPadding(0,dp(3),0,dp(6));detailPane.addView(subtitle);
  LinearLayout controls=new LinearLayout(this);controls.setGravity(Gravity.CENTER_VERTICAL);
  groupButton=button("B 模式",()->toggleGroup());groupButton.setContentDescription("toggle-session-group");controls.addView(groupButton);
  renameButton=button("重命名",()->renameSession());renameButton.setContentDescription("rename-session");controls.addView(renameButton);
  controls.addView(button("中断",()->action("interrupt",fields())));detailActions=horizontalStrip(controls);detailPane.addView(detailActions,new LinearLayout.LayoutParams(-1,-2));
  attention=text("",13,0xffa52c35);attention.setContentDescription("confirm-reminder");attention.setPadding(dp(10),dp(8),dp(10),dp(8));attention.setBackground(bg(0xffffe4e3,8));attention.setOnClickListener(v->acknowledge(selected));attention.setVisibility(View.GONE);detailPane.addView(attention,new LinearLayout.LayoutParams(-1,-2));
  outputFrame=new FrameLayout(this);outputFrame.setBackground(bg(DARK,10));detailPane.addView(outputFrame,new LinearLayout.LayoutParams(-1,0,1));
  outputScroll=new ScrollView(this);outputScroll.setFillViewport(true);output=new TextView(this);output.setTextColor(0xffe4ebdd);output.setTextSize(14);output.setTypeface(Typeface.MONOSPACE);output.setTextIsSelectable(true);output.setPadding(dp(12),dp(12),dp(12),dp(12));output.setContentDescription("session-output");outputScroll.addView(output,new ScrollView.LayoutParams(-1,-2));outputFrame.addView(outputScroll,new FrameLayout.LayoutParams(-1,-1));
  terminal=new WebView(this);terminal.setBackgroundColor(DARK);terminal.getSettings().setJavaScriptEnabled(true);terminal.getSettings().setMinimumFontSize(1);terminal.getSettings().setMinimumLogicalFontSize(1);terminal.getSettings().setAllowFileAccess(false);terminal.getSettings().setAllowContentAccess(false);terminal.addJavascriptInterface(new TerminalBridge(),"CodeLink");
  terminal.setWebViewClient(new WebViewClient(){public boolean shouldOverrideUrlLoading(WebView view,WebResourceRequest request){return true;}public void onPageFinished(WebView view,String url){terminalReady=true;configureTerminalScroll();if(currentKind.equals("terminal"))terminal.evaluateJavascript("appendTerminal("+JSONObject.quote(fullText)+",true)",null);else if(currentKind.equals("observer"))renderWindowText();}});
  terminal.loadUrl("file:///android_asset/terminal.html");terminal.setVisibility(View.GONE);outputFrame.addView(terminal,new FrameLayout.LayoutParams(-1,-1));
  terminal.setContentDescription("terminal-output-view");
  scrollControls=new LinearLayout(this);scrollControls.setGravity(Gravity.CENTER_VERTICAL);
  scrollMode=button("滑动：同步电脑",()->{desktopScroll=!desktopScroll;clearScroll();configureTerminalScroll();});scrollMode.setContentDescription("terminal-scroll-mode");scrollControls.addView(scrollMode,new LinearLayout.LayoutParams(0,dp(40),1));
  scrollLatest=button("回到最新",()->{if(terminalReady)terminal.evaluateJavascript("scrollLatest()",null);});scrollLatest.setContentDescription("terminal-scroll-latest");scrollControls.addView(scrollLatest,new LinearLayout.LayoutParams(-2,dp(40)));
  detailPane.addView(scrollControls,new LinearLayout.LayoutParams(-1,-2));scrollControls.setVisibility(View.GONE);
  approvals=new LinearLayout(this);approvals.setOrientation(1);requestScroll=new ScrollView(this);requestScroll.setFillViewport(false);requestScroll.addView(approvals);detailPane.addView(requestScroll,new LinearLayout.LayoutParams(-1,0));requestCount=0;
  banner=text("连接电脑后同步终端内容",12,GREEN);banner.setMaxLines(2);banner.setEllipsize(android.text.TextUtils.TruncateAt.END);banner.setPadding(0,dp(6),0,dp(6));banner.setContentDescription("activity-banner");detailPane.addView(banner);
  LinearLayout composer=new LinearLayout(this);composer.setGravity(Gravity.CENTER_VERTICAL);input=new EditText(this);input.setHint("输入下一项任务或终端命令");input.setTextSize(14);input.setTextColor(INK);input.setBackground(bg(-1,8));input.setPadding(dp(12),dp(8),dp(12),dp(8));input.setMinHeight(dp(48));input.setMinLines(dualPane?2:1);input.setMaxLines(dualPane?4:3);input.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE|android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);input.setImeOptions(EditorInfo.IME_FLAG_NO_EXTRACT_UI);input.setContentDescription("command-input");input.setText(draft);input.setSelection(input.length());input.setEnabled(connected&&controller);composer.addView(input,new LinearLayout.LayoutParams(0,-2,1));
  Button send=button("发送",()->sendInput());send.setContentDescription("send-command");send.setBackground(bg(YELLOW,8));LinearLayout.LayoutParams bp=(LinearLayout.LayoutParams)send.getLayoutParams();bp.setMargins(dp(8),0,0,0);composer.addView(send,bp);detailPane.addView(composer,new LinearLayout.LayoutParams(-1,-2));
  terminalKeys=new LinearLayout(this);String[] labels={"Enter","Esc","Tab","Ctrl+C","↑","↓","←","→"};String[] codes={"\r","\u001b","\t","\u0003","\u001b[A","\u001b[B","\u001b[D","\u001b[C"};
  for(int i=0;i<labels.length;i++){final String value=codes[i];terminalKeys.addView(button(labels[i],()->{if(controller)try{action("input",fields().put("text",value).put("raw",true));}catch(Exception ignored){}}));}
  keyStrip=horizontalStrip(terminalKeys);keyStrip.setContentDescription("terminal-shortcuts");detailPane.addView(keyStrip,new LinearLayout.LayoutParams(-1,dp(48)));keyStrip.setVisibility(View.GONE);
  composerView=composer;layoutRoot=root;layoutListener=()->{
   if(isFinishing())return;
   boolean tight=!dualPane&&detailOpen&&root.getHeight()<dp(500);
   appHeader.setVisibility(tight?View.GONE:View.VISIBLE);detailActions.setVisibility(tight?View.GONE:View.VISIBLE);banner.setVisibility(tight?View.GONE:View.VISIBLE);
   int height=requestCount==0?0:Math.min(dp(requestCount*110),Math.min(dp(dualPane?220:140),Math.max(dp(64),detailPane.getHeight()/3)));
   if(requestScroll.getLayoutParams().height!=height){ViewGroup.LayoutParams p=requestScroll.getLayoutParams();p.height=height;requestScroll.setLayoutParams(p);}
  };root.getViewTreeObserver().addOnGlobalLayoutListener(layoutListener);
  updatePanes();setContentView(root);root.requestFocus();building=false;
 }
 private HorizontalScrollView horizontalStrip(View child){HorizontalScrollView scroll=new HorizontalScrollView(this);scroll.setHorizontalScrollBarEnabled(false);scroll.addView(child);return scroll;}
 private void connectionMenu(View anchor){
  PopupMenu menu=new PopupMenu(this,anchor);menu.getMenu().add("蓝牙连接").setOnMenuItemClickListener(i->{bluetoothDialog();return true;});
  menu.getMenu().add("重新连接").setOnMenuItemClickListener(i->{runtime.start();return true;});
  menu.getMenu().add("后台与通知").setOnMenuItemClickListener(i->{backgroundSettings();return true;});
  menu.getMenu().add("保持常亮").setCheckable(true).setChecked(keepAwake).setOnMenuItemClickListener(i->{keepAwake=!keepAwake;presentation.edit().putBoolean("keepAwake",keepAwake).apply();if(keepAwake)getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);else getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);return true;});menu.show();
 }
 private void terminalMenu(View anchor){
  PopupMenu menu=new PopupMenu(this,anchor);boolean active=session(selected)!=null;
  menu.getMenu().add("切换提醒分组").setEnabled(active).setOnMenuItemClickListener(i->{toggleGroup();return true;});
  menu.getMenu().add("重命名").setEnabled(active).setOnMenuItemClickListener(i->{renameSession();return true;});
  menu.getMenu().add("中断").setEnabled(active&&controller&&connected).setOnMenuItemClickListener(i->{action("interrupt",fields());return true;});menu.show();
 }
 private void showSplash(){FrameLayout overlay=new FrameLayout(this);overlay.setBackgroundColor(CREAM);LinearLayout panel=new LinearLayout(this);panel.setOrientation(1);panel.setGravity(Gravity.CENTER);ImageView animation=new ImageView(this);LinearLayout.LayoutParams imageSize=new LinearLayout.LayoutParams(dp(Math.min(360,getResources().getConfiguration().screenWidthDp-32)),dp(220));panel.addView(animation,imageSize);TextView label=text("Code Link",30,INK);label.setGravity(Gravity.CENTER);panel.addView(label);TextView note=text("连接你的另一块屏幕",14,GREEN);note.setGravity(Gravity.CENTER);note.setPadding(0,dp(14),0,0);panel.addView(note);overlay.addView(panel,new FrameLayout.LayoutParams(-1,-1));addContentView(overlay,new ViewGroup.LayoutParams(-1,-1));try{byte[] bytes;try(java.io.InputStream in=getAssets().open("startup.gif");java.io.ByteArrayOutputStream out=new java.io.ByteArrayOutputStream()){byte[] b=new byte[8192];int n;while((n=in.read(b))>0)out.write(b,0,n);bytes=out.toByteArray();}Drawable d=ImageDecoder.decodeDrawable(ImageDecoder.createSource(java.nio.ByteBuffer.wrap(bytes)));animation.setImageDrawable(d);if(d instanceof AnimatedImageDrawable)((AnimatedImageDrawable)d).start();}catch(Exception ignored){}handler.postDelayed(()->{ViewGroup p=(ViewGroup)overlay.getParent();if(p!=null)p.removeView(overlay);},2400);}
 public void status(String text,boolean online){runOnUiThread(()->{if(!observing)return;connected=online;connectionStatus.setText(text);connectionStatus.setTextColor(online?GREEN:0xffa35a33);input.setEnabled(online&&controller);if(online)banner.setText("已连接电脑，状态已同步");if(!online){clearScroll();input.setEnabled(false);banner.setText("连接恢复后同步状态，电脑上的任务继续运行");}configureTerminalScroll();});}
 public void message(JSONObject message){runOnUiThread(()->{if(!observing)return;String type=message.optString("type");if(type.equals("state"))applyState(message);else if(type.equals("notice")){noticeBannerId=message.optString("sessionId");noticeBannerKey=message.optString("noticeKey");noticeBannerText=UiMessages.server(message.optString("text"));banner.setText(noticeBannerText);}else if(type.equals("result")){if(scrollResult(message))return;JSONObject result=message.optJSONObject("result");if(result!=null&&!result.optBoolean("ok")){banner.setText(UiMessages.server(result.optString("error")));toast(result.optString("error"));}else if(result!=null&&result.optBoolean("queued"))banner.setText("任务已排队，当前回合结束后开始");else banner.setText("操作已确认");}});}
 private void applyState(JSONObject state){lastState=state;JSONArray sessions=state.optJSONArray("sessions");if(sessions==null)return;String previous=selected;String next=state.optString("selected");if(!previous.equals(next))saveDraft();selected=next;JSONObject active=null;for(int i=0;i<sessions.length();i++){JSONObject item=sessions.optJSONObject(i);if(item!=null&&item.optString("id").equals(selected))active=item;}
  if(!selected.equals(previous)){input.setText(drafts.getString(selected,""));input.setSelection(input.length());if(active!=null){detailOpen=true;groupTab=groupOf(active);presentation.edit().putString("tab",groupTab).apply();}}
  if(active==null){detailOpen=false;if(terminalFullscreen!=null)terminalFullscreen.dismiss();}updatePanes();
  consumeNotificationTarget();renderSessions();
  boolean reset=state.optBoolean("reset")||!previous.equals(selected);String chunk=state.optString("output");if(reset)fullText=chunk;else fullText+=chunk;if(fullText.length()>120000)fullText=fullText.substring(fullText.length()-100000);
  currentKind=active==null?"":active.optString("kind");boolean isTerminal=currentKind.equals("terminal");keyStrip.setVisibility((terminalFullscreen==null||fullscreenInput)&&(isTerminal||currentKind.equals("observer"))?View.VISIBLE:View.GONE);terminal.setVisibility(isTerminal||currentKind.equals("observer")?View.VISIBLE:View.GONE);outputScroll.setVisibility(isTerminal||currentKind.equals("observer")?View.GONE:View.VISIBLE);
  if(!selected.equals(previous))clearScroll();
  controller=active!=null&&link.deviceId.equals(active.optString("controller"));configureTerminalScroll();
  if(currentKind.equals("observer")){if(terminalReady)renderWindowText();}else if(isTerminal){if(terminalReady&&(!chunk.isEmpty()||reset))terminal.evaluateJavascript("appendTerminal("+JSONObject.quote(chunk)+","+reset+")",null);}else{boolean follow=reset||outputScroll.getChildAt(0).getHeight()-outputScroll.getHeight()-outputScroll.getScrollY()<dp(80);if(reset)output.setText(strip(chunk));else if(!chunk.isEmpty())output.append(strip(chunk));if(output.length()>120000)output.setText(output.getText().subSequence(output.length()-100000,output.length()));if(follow)outputScroll.post(()->outputScroll.fullScroll(View.FOCUS_DOWN));}
  if(active==null){title.setText("选择电脑映射的命令行");subtitle.setText("在电脑上选择窗口，点击映射到移动端");controller=false;}else{title.setText(displayName(active));controller=link.deviceId.equals(active.optString("controller"));subtitle.setText(label(active.optString("status"))+(currentKind.equals("observer")?" · 窗口识别":"")+"  ·  "+(controller?"可操作":"未分配输入")+(currentKind.equals("observer")?"":"  ·  "+active.optString("cwd")));}
  input.setEnabled(connected&&controller);input.setHint(controller?"输入下一项任务或终端命令":"请在电脑映射此命令行");
  JSONArray requests=active==null?new JSONArray():active.optJSONArray("approvals");if(requests==null)requests=new JSONArray();String reqSignature=requests.toString()+selected;
  if(!reqSignature.equals(approvalsSignature)){approvalsSignature=reqSignature;approvals.removeAllViews();requestCount=requests.length();requestScroll.requestLayout();for(int i=0;i<requests.length();i++){JSONObject request=requests.optJSONObject(i);if(request!=null){addRequest(request);}}}
 }
 private JSONObject session(String id){return SessionCatalog.session(lastState,id);}
 private String reminderId(JSONObject s){return SessionCatalog.reminderId(s);}
 private JSONObject reminderSession(String key){JSONArray all=visibleSessions();for(int i=0;i<all.length();i++){JSONObject s=all.optJSONObject(i);if(s!=null&&reminderId(s).equals(key))return s;}return null;}
 private String prefKey(JSONObject s){return runtime.prefKey(s);}
 private String groupOf(JSONObject s){return runtime.groupOf(s);}
 private String displayName(JSONObject s){return runtime.displayName(s);}
 private void chooseGroup(String group){groupTab=group;presentation.edit().putString("tab",group).apply();renderSessions();}
 private void toggleGroup(){JSONObject s=session(selected);if(s==null)return;String group=groupOf(s).equals("B")?"A":"B";presentation.edit().putString("group:"+prefKey(s),group).putString("tab",group).apply();groupTab=group;runtime.presentationChanged();renderSessions();}
 private void renameSession(){JSONObject s=session(selected);if(s==null)return;final String key="name:"+prefKey(s);EditText name=new EditText(this);name.setSingleLine(true);name.setText(displayName(s));name.setSelectAllOnFocus(true);name.setContentDescription("session-name-input");name.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(40)});AlertDialog dialog=new AlertDialog.Builder(this).setTitle("重命名命令行").setView(name).setPositiveButton("保存",null).setNegativeButton("取消",null).setNeutralButton("恢复窗口名称",(d,w)->{presentation.edit().remove(key).apply();renderSessions();}).create();dialog.setOnShowListener(d->dialog.getButton(-1).setOnClickListener(v->{String value=name.getText().toString().trim();if(value.isEmpty()){name.setError("请输入名称");return;}presentation.edit().putString(key,value).apply();renderSessions();dialog.dismiss();}));dialog.show();}
 private String reminderLabel(String kind){return LinkNotifications.label(kind);}
 private void acknowledge(String id){runtime.acknowledge(id);renderSessions();}
 private void acknowledgeBatch(){runtime.acknowledgeBatch();renderSessions();}
 private JSONArray visibleSessions(){return SessionCatalog.visible(lastState);}
 private void consumeNotificationTarget(){if(notificationTarget.isEmpty())return;String target=notificationTarget;
  if(target.equals("@B")){notificationTarget="";chooseGroup("B");showSessionList();}
  else{JSONObject item=reminderSession(target);if(item!=null){notificationTarget="";openSession(item);}}
 }
 private void openSession(JSONObject s){if(s.has("catalogKey")){acknowledge(reminderId(s));try{action("openCommand",new JSONObject().put("key",s.optString("catalogKey")));}catch(Exception e){toast(e.getMessage());}}else watch(s.optString("id"));}
 private void renderSessions(){if(sessionList==null)return;JSONArray all=visibleSessions();List<JSONObject> shown=new ArrayList<>();int a=0,b=0;boolean redA=false,redB=reminders.batchUnread();if(all!=null)for(int i=0;i<all.length();i++){JSONObject s=all.optJSONObject(i);if(s==null)continue;boolean isA=groupOf(s).equals("A");if(isA){a++;redA|=reminders.unread(reminderId(s))!=null;}else{b++;}if(groupOf(s).equals(groupTab))shown.add(s);}
  tabA.setText((redA?"● ":"")+"A · 逐个 ("+a+")");tabB.setText((redB?"● ":"")+"B · 汇总 ("+b+")");tabA.setTextColor(redA?0xffb02e38:INK);tabB.setTextColor(redB?0xffb02e38:INK);tabA.setBackground(bg(groupTab.equals("A")?0xffdce7d4:0xffece9df,4));tabB.setBackground(bg(groupTab.equals("B")?0xffdce7d4:0xffece9df,4));
  groupSummary.setText(groupTab.equals("A")?"每个任务完成后单独提醒":reminders.batchUnread()?"● 本轮全部完成 · 点击确认":reminders.batchTotal()>0?"本轮完成 "+reminders.batchDone()+" / "+reminders.batchTotal():"本轮尚无运行任务");groupSummary.setTextColor(reminders.batchUnread()&&groupTab.equals("B")?0xffb02e38:0xff6c7b70);
  StringBuilder signature=new StringBuilder(groupTab).append(selected);for(JSONObject s:shown){ReminderState.Notice n=reminders.unread(reminderId(s));signature.append(s.optString("id")).append(displayName(s)).append(s.optString("status")).append(n==null?"":n.key);}
  if(!signature.toString().equals(sessionSignature)){sessionSignature=signature.toString();sessionList.removeAllViews();if(shown.isEmpty()){emptyText.setText(all==null||all.length()==0?"连接后，这里会出现\n电脑映射给你的命令行。":"本组暂无命令行\n选中命令行后可切换 A / B 模式");sessionList.addView(emptyText);}for(JSONObject s:shown){String id=s.optString("id");ReminderState.Notice n=reminders.unread(reminderId(s));Button item=button((n==null?"":"● ")+displayName(s)+"\n"+label(s.optString("status"))+(s.has("catalogKey")?" · 点击打开":""),()->openSession(s));if(n!=null){android.text.SpannableString marked=new android.text.SpannableString(item.getText());marked.setSpan(new android.text.style.ForegroundColorSpan(0xffcc303d),0,1,0);item.setText(marked);}item.setContentDescription("session-card-"+id);item.setGravity(Gravity.LEFT|Gravity.CENTER_VERTICAL);item.setTextSize(14);item.setMaxLines(3);item.setEllipsize(android.text.TextUtils.TruncateAt.END);item.setMinHeight(dp(88));GradientDrawable background=bg(id.equals(selected)?0xffe5eadb:0xfff2f1e8,8);background.setStroke(dp(n==null?1:2),n==null?0xffdadcd0:0xffcc4851);item.setBackground(background);LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.setMargins(0,dp(10),0,0);sessionList.addView(item,lp);}}
  JSONObject active=session(selected);groupButton.setEnabled(active!=null);renameButton.setEnabled(active!=null);if(active!=null){title.setText(displayName(active));groupButton.setText(groupOf(active)+" 模式 ⇄");}
  ReminderState.Notice note=active==null?null:reminders.unread(reminderId(active));attention.setVisibility(note==null?View.GONE:View.VISIBLE);if(note!=null)attention.setText("● "+reminderLabel(note.kind)+"  ·  点击确认提醒");
  if(!noticeBannerKey.isEmpty()){ReminderState.Notice latest=reminders.unread(noticeBannerId);String key=noticeBannerId.equals("@B")?reminders.batchNoticeKey():latest==null?"":latest.key;if(!noticeBannerKey.equals(key)){if(banner.getText().toString().equals(noticeBannerText))banner.setText(connected?"已连接电脑，状态已同步":"连接恢复后同步状态，电脑上的任务继续运行");noticeBannerId="";noticeBannerKey="";noticeBannerText="";}}
 }
 private String strip(String s){return s.replaceAll("\\x1B(?:\\[[0-?]*[ -/]*[@-~]|\\][^\\x07]*(?:\\x07|\\x1B\\\\))","").replace("\r","");}
 private String label(String s){switch(s){case"running":return"运行中";case"approval":return"等待授权";case"input":return"等待输入";case"completed":return"本轮完成";case"failed":return"运行失败";case"interrupted":return"已中断";case"exited":return"进程已退出";case"unmapped":return"点击查看并连接";case"unknown":return"任务状态待识别";case"disconnected":return"状态待同步";default:return"就绪";}}
 private JSONObject fields(){try{return new JSONObject().put("sessionId",selected);}catch(Exception e){return new JSONObject();}}
 private void action(String action,JSONObject fields){if(!connected){toast("当前未连接电脑");return;}io.execute(()->{try{link.action(action,fields);}catch(Exception e){runOnUiThread(()->toast(e.getMessage()));}});}
 private void watch(String id){if(id.equals(selected)){detailOpen=true;updatePanes();}acknowledge(id);io.execute(()->{try{link.send(new JSONObject().put("type","watch").put("sessionId",id));}catch(Exception e){runOnUiThread(()->toast(e.getMessage()));}});}
 private void sendInput(){String value=input.getText().toString();if(value.isEmpty()||!controller||!connected)return;try{action("input",fields().put("text",value));input.setText("");drafts.remove(selected);}catch(Exception e){toast(e.getMessage());}}
 private void renderWindowText(){terminal.evaluateJavascript("showWindowText("+JSONObject.quote(fullText)+","+JSONObject.quote(lastState==null?fullText:lastState.optString("viewport",fullText))+")",null);}
 private void configureTerminalScroll(){
  if(scrollControls==null)return;boolean observer=currentKind.equals("observer");
  scrollControls.setVisibility(observer||currentKind.equals("terminal")?View.VISIBLE:View.GONE);
  scrollMode.setVisibility(observer?View.VISIBLE:View.GONE);scrollMode.setEnabled(controller&&connected);
  boolean sync=observer&&desktopScroll&&controller&&connected;
  scrollMode.setText(sync?"滑动：同步电脑":"滑动：本地查看");
  if(terminalReady)terminal.evaluateJavascript("configureTerminal("+JSONObject.quote(selected)+","+observer+","+sync+")",null);
 }
 private void clearScroll(){scrollEpoch++;scrollSession="";scrollRequest="";pendingScrollLines=0;pendingScrollLatest=false;handler.removeCallbacks(flushScroll);}
 private final Runnable flushScroll=()->flushDesktopScroll();
 private void requestScroll(String sessionId,int lines,boolean latest){
  if(!observing||!connected||!controller||!desktopScroll||!currentKind.equals("observer")||!selected.equals(sessionId))return;
  scrollSession=sessionId;pendingScrollLines=Math.max(-72,Math.min(72,pendingScrollLines+Math.max(-36,Math.min(36,lines))));
  if(latest){pendingScrollLatest=true;pendingScrollLines=0;}
  handler.removeCallbacks(flushScroll);handler.postDelayed(flushScroll,100);
 }
 private void flushDesktopScroll(){
  if(!scrollRequest.isEmpty()||scrollSession.isEmpty()||(!pendingScrollLatest&&pendingScrollLines==0))return;
  if(!observing||!connected||!controller||!desktopScroll||!selected.equals(scrollSession)){clearScroll();return;}
  final String id=UUID.randomUUID().toString(),sessionId=scrollSession;final long epoch=scrollEpoch;
  final int lines=Math.max(-36,Math.min(36,pendingScrollLines));final boolean latest=pendingScrollLatest;
  pendingScrollLines-=lines;pendingScrollLatest=false;scrollRequest=id;
  io.execute(()->{try{if(epoch==scrollEpoch)link.send(new JSONObject().put("type","action").put("id",id).put("action","scroll").put("sessionId",sessionId).put("lines",lines).put("latest",latest));}catch(Exception error){handler.post(()->{if(scrollRequest.equals(id)){clearScroll();banner.setText(error.getMessage());}});}});
  handler.postDelayed(()->{if(scrollRequest.equals(id)){clearScroll();banner.setText("电脑滚动未及时响应，请重试或切换本地查看。");}},16000);
 }
 private boolean scrollResult(JSONObject message){
  if(!message.optString("id").equals(scrollRequest)||scrollRequest.isEmpty())return false;
  scrollRequest="";JSONObject result=message.optJSONObject("result");
  if(result==null||!result.optBoolean("ok")){clearScroll();banner.setText(result==null?"电脑滚动未及时响应，请重试或切换本地查看。":UiMessages.server(result.optString("error")));}
  else{banner.setText("已滚动电脑窗口 · 下滑查看前文，上滑查看后文");handler.post(flushScroll);}return true;
 }
 private void requestPointer(String sessionId,int row,int column,String expectedLine){
  if(!observing||!connected||!controller||!desktopScroll||!currentKind.equals("observer")||!selected.equals(sessionId))return;
  if(!scrollRequest.isEmpty()||pendingScrollLines!=0||pendingScrollLatest){banner.setText("请等滚动停止后再点击终端内容。");return;}
  final long epoch=scrollEpoch;
  io.execute(()->{try{if(epoch==scrollEpoch)link.action("pointer",new JSONObject().put("sessionId",sessionId).put("row",row).put("column",column).put("expectedLine",expectedLine));}catch(Exception error){handler.post(()->banner.setText(error.getMessage()));}});
 }
 public final class TerminalBridge{
  @JavascriptInterface public void keys(String value){if(controller&&connected)try{action("input",fields().put("text",value).put("raw",true));}catch(Exception ignored){}}
  @JavascriptInterface public void scroll(String sessionId,int lines,boolean latest){handler.post(()->requestScroll(sessionId,lines,latest));}
  @JavascriptInterface public void pointer(String sessionId,int row,int column,String expectedLine){handler.post(()->requestPointer(sessionId,row,column,expectedLine));}
 }
 private void addRequest(JSONObject request){try{request.put("_sessionId",selected);}catch(Exception ignored){}LinearLayout card=new LinearLayout(this);card.setOrientation(1);card.setPadding(dp(12),dp(10),dp(12),dp(10));card.setBackground(bg(0xfffff2ca,8));TextView heading=text(UiMessages.server(request.optString("summary")),16,INK);card.addView(heading);JSONObject params=request.optJSONObject("parameters");String details=params==null?"":params.optString("command",params.optString("reason",params.toString()));TextView detail=text(details,12,INK);detail.setMaxLines(2);card.addView(detail);LinearLayout row=new LinearLayout(this);card.addView(row);row.addView(button("查看并处理",()->showRequest(request)));approvals.addView(card);}
 private void showRequest(JSONObject request){if(!controller){toast("请先在电脑映射此命令行");return;}String method=request.optString("method");JSONObject params=request.optJSONObject("parameters");if(method.contains("requestUserInput")){answerQuestions(request);return;}if(method.contains("elicitation")){answerForm(request);return;}
  LinearLayout content=new LinearLayout(this);content.setOrientation(1);content.setPadding(dp(18),dp(5),dp(18),dp(8));TextView detail=text(params==null?"":pretty(params),14,INK);detail.setTextIsSelectable(true);content.addView(detail);ScrollView scroll=new ScrollView(this);scroll.addView(content);
  AlertDialog.Builder builder=new AlertDialog.Builder(this).setTitle(UiMessages.server(request.optString("summary"))).setView(scroll);
  JSONArray decisions=params==null?null:params.optJSONArray("availableDecisions");
  if(offers(decisions,"accept"))builder.setPositiveButton("允许一次",(d,w)->approve(request,"accept",null));
  if(offers(decisions,"decline"))builder.setNegativeButton("拒绝",(d,w)->approve(request,"decline",null));
  if(!method.contains("permissions")&&offers(decisions,"cancel"))builder.setNeutralButton("取消任务",(d,w)->approve(request,"cancel",null));
  builder.show();}
 private boolean offers(JSONArray choices,String decision){if(choices==null)return true;for(int i=0;i<choices.length();i++)if(decision.equals(choices.optString(i)))return true;return false;}
 private String pretty(JSONObject o){try{return o.toString(2);}catch(Exception e){return o.toString();}}
 private void approve(JSONObject r,String decision,JSONObject response){if(!connected||!controller){toast("连接尚未就绪");return;}try{JSONObject msg=new JSONObject().put("sessionId",r.optString("_sessionId",selected)).put("requestId",r.optString("id")).put("decision",decision);if(response!=null)msg.put("response",response);action("approve",msg);}catch(Exception e){toast(e.getMessage());}}
 private void answerQuestions(JSONObject request){JSONArray questions=request.optJSONObject("parameters").optJSONArray("questions");if(questions==null)return;LinearLayout panel=new LinearLayout(this);panel.setOrientation(1);panel.setPadding(dp(20),0,dp(20),0);Map<String,EditText> answers=new LinkedHashMap<>();for(int i=0;i<questions.length();i++){JSONObject q=questions.optJSONObject(i);if(q==null)continue;panel.addView(text(q.optString("question"),16,INK));EditText field=new EditText(this);field.setTextSize(15);if(q.optBoolean("isSecret"))field.setInputType(129);panel.addView(field);answers.put(q.optString("id"),field);JSONArray options=q.optJSONArray("options");if(options!=null)for(int j=0;j<options.length();j++){JSONObject option=options.optJSONObject(j);if(option!=null)panel.addView(button(option.optString("label"),()->field.setText(option.optString("label"))));}}
  ScrollView scroll=new ScrollView(this);scroll.addView(panel);AlertDialog dialog=new AlertDialog.Builder(this).setTitle("需要你的回答").setView(scroll).setPositiveButton("提交",null).setNegativeButton("稍后",null).create();dialog.setOnShowListener(d->dialog.getButton(-1).setOnClickListener(v->{try{JSONObject values=new JSONObject();for(Map.Entry<String,EditText> a:answers.entrySet())values.put(a.getKey(),new JSONObject().put("answers",new JSONArray().put(a.getValue().getText().toString())));approve(request,"",new JSONObject().put("answers",values));dialog.dismiss();}catch(Exception e){toast(e.getMessage());}}));dialog.show();}
 private void answerForm(JSONObject request){JSONObject params=request.optJSONObject("parameters");if(params!=null)FormDialog.show(this,params,response->approve(request,"",response));}
 private void connectionDialog(){connectionDialog(link.mode);}
 private void connectionDialog(String initialMode){
  if(connectionSetup!=null)connectionSetup.dismiss();
  LinearLayout panel=new LinearLayout(this);panel.setOrientation(1);panel.setPadding(dp(20),dp(6),dp(20),dp(8));
  panel.addView(text("电脑 → 连接设备，扫描二维码或粘贴连接信息。",14,INK));
  Spinner modes=new Spinner(this);modes.setContentDescription("connection-transport");modes.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"USB","蓝牙","Wi-Fi","远程"}));modes.setSelection(initialMode.equals("Bluetooth")?1:initialMode.equals("Wi-Fi")?2:initialMode.equals("Remote")?3:0);panel.addView(modes);
  TextView hint=text("",12,GREEN);panel.addView(hint);
  modes.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int position,long id){hint.setText(position==3?"在电脑开启远程连接，扫描远程二维码。配对后可通过不同网络连接；电脑需保持开机、不睡眠。":position==1?"先在系统蓝牙设置中配对电脑。首次应用配对请扫码或粘贴信息；已配对过可直接点连接。无需 USB 或 Wi-Fi。":position==0?"USB 连接后电脑会自动填入地址，无需手动输入。":"扫描电脑局域网地址对应的二维码，两端需能通过网络互访。");}});
  EditText invite=new EditText(this);invite.setHint("codelink:...");invite.setContentDescription("connection-invitation");invite.setTextSize(12);invite.setMinLines(3);panel.addView(invite);
  panel.addView(button("扫描二维码",()->{bluetoothScan=modes.getSelectedItemPosition()==1;scanConnection();}));
  AlertDialog dialog=new AlertDialog.Builder(this).setTitle("连接电脑").setView(scrollable(panel)).setPositiveButton("连接",null).setNegativeButton("取消",null).setNeutralButton("断开连接",(d,w)->{runtime.disconnect();status("未连接",false);}).create();
  connectionSetup=dialog;dialog.setOnDismissListener(d->{if(connectionSetup==dialog)connectionSetup=null;});
  dialog.setOnShowListener(d->dialog.getButton(-1).setOnClickListener(v->{try{
   int choice=modes.getSelectedItemPosition();boolean imported=invite.length()>0;
   if(imported)link.invite(invite.getText().toString().trim());
   if(!imported||choice==1)link.mode=choice==1?"Bluetooth":choice==2?"Wi-Fi":choice==3?"Remote":"USB";
   if(link.pin.isEmpty()){toast("请先扫描或粘贴电脑的配对信息。");return;}
   link.save();dialog.dismiss();if(link.mode.equals("Bluetooth"))bluetoothDialog();else runtime.start();
  }catch(Exception e){toast(e.getMessage());}}));dialog.show();
 }
 private void scanConnection(){
  if(checkSelfPermission(Manifest.permission.CAMERA)!=PackageManager.PERMISSION_GRANTED){requestPermissions(new String[]{Manifest.permission.CAMERA},102);return;}
  if(connectionSetup!=null)connectionSetup.dismiss();startActivityForResult(new Intent(this,ScanActivity.class),200);
 }
 private void bluetoothSettings(){try{startActivityForResult(new Intent(android.provider.Settings.ACTION_BLUETOOTH_SETTINGS),201);}catch(Exception e){toast(e.getMessage());}}
 private void bluetoothDialog(){
  if(link.pin.isEmpty()){connectionDialog("Bluetooth");return;}
  if(Build.VERSION.SDK_INT>=31&&(checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)!=0||checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN)!=0)){requestPermissions(new String[]{Manifest.permission.BLUETOOTH_CONNECT,Manifest.permission.BLUETOOTH_SCAN},101);return;}
  try{
   BluetoothManager manager=(BluetoothManager)getSystemService(BLUETOOTH_SERVICE);BluetoothAdapter adapter=manager==null?null:manager.getAdapter();
   if(adapter==null){toast("此设备不支持蓝牙，请使用 USB 或 Wi-Fi。");return;}
   if(!adapter.isEnabled()){new AlertDialog.Builder(this).setTitle("蓝牙连接").setMessage("蓝牙尚未开启。请在系统设置中开启并配对电脑，再返回继续。").setPositiveButton("打开蓝牙设置",(d,w)->bluetoothSettings()).setNegativeButton("取消",null).show();return;}
   ArrayList<BluetoothDevice> devices=new ArrayList<>(adapter.getBondedDevices());Collections.sort(devices,(a,b)->String.valueOf(a.getName()).compareToIgnoreCase(String.valueOf(b.getName())));
   String[] names=new String[devices.size()];for(int i=0;i<names.length;i++)names[i]=(devices.get(i).getName()==null?"蓝牙设备":devices.get(i).getName())+"\n"+devices.get(i).getAddress();
   AlertDialog.Builder picker=new AlertDialog.Builder(this).setTitle("选择配对的电脑").setNegativeButton("取消",null).setNeutralButton("打开蓝牙设置",(d,w)->bluetoothSettings());
   if(names.length==0)picker.setMessage("尚无已配对的设备。请先在系统蓝牙设置中配对电脑，再返回刷新。").setPositiveButton("刷新设备",(d,w)->bluetoothDialog());
   else picker.setItems(names,(d,w)->{link.btAddress=devices.get(w).getAddress();link.mode="Bluetooth";link.save();runtime.start();});
   picker.show();
  }catch(Exception e){toast(e.getMessage());}
 }
 @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] results){
  super.onRequestPermissionsResult(request,permissions,results);
  boolean granted=results.length>0;for(int result:results)granted&=result==PackageManager.PERMISSION_GRANTED;
  if(request==101){if(granted)bluetoothDialog();else new AlertDialog.Builder(this).setTitle("需要附近的设备权限").setMessage("允许附近的设备权限后才能连接已配对的电脑。可在应用设置的权限中开启。").setPositiveButton("打开应用设置",(d,w)->{try{startActivityForResult(new Intent(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS,android.net.Uri.parse("package:"+getPackageName())),201);}catch(Exception e){toast(e.getMessage());}}).setNegativeButton("取消",null).show();}
  if(request==102){if(granted)scanConnection();else toast("相机权限未开启，也可以粘贴电脑的配对信息。");}
 }
 protected void onActivityResult(int request,int result,Intent data){
  super.onActivityResult(request,result,data);
  if(request==201){bluetoothDialog();return;}
  if(request==200&&result==RESULT_OK&&data!=null)try{link.invite(data.getStringExtra("invite"));if(bluetoothScan)link.mode="Bluetooth";link.save();if(link.mode.equals("Bluetooth"))bluetoothDialog();else runtime.start();}catch(Exception e){toast(e.getMessage());}
  if(request==200)bluetoothScan=false;
 }
 private void backgroundSettings(){
  NotificationManager nm=getSystemService(NotificationManager.class);PowerManager pm=getSystemService(PowerManager.class);
  NotificationChannel channel=nm.getNotificationChannel(LinkNotifications.COMPLETED);boolean high=channel!=null&&channel.getImportance()>=NotificationManager.IMPORTANCE_HIGH;
  LinearLayout panel=new LinearLayout(this);panel.setOrientation(1);panel.setPadding(dp(20),dp(8),dp(20),dp(12));
  panel.addView(text("切换到其他应用后继续接收终端内容。A 组单个完成、B 组全部完成和等待授权时发送通知。\n\n通知权限："+(nm.areNotificationsEnabled()?"已开启":"未开启")+"\n完成横幅："+(high?"已开启":"需要开启")+"\n勿扰模式："+(nm.getCurrentInterruptionFilter()==NotificationManager.INTERRUPTION_FILTER_ALL?"已关闭":"已开启，可能拦截横幅")+"\n电池优化："+(pm.isIgnoringBatteryOptimizations(getPackageName())?"已允许后台运行":"可能限制锁屏后的连接")+"\n\nZUI 中请允许 Code Link 后台运行，并开启悬浮通知。通知栏的“断开连接”可停止后台连接。",14,INK));
  panel.addView(button("设置完成横幅",()->{if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=0)requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},100);else startActivity(new Intent(android.provider.Settings.ACTION_CHANNEL_NOTIFICATION_SETTINGS).putExtra(android.provider.Settings.EXTRA_APP_PACKAGE,getPackageName()).putExtra(android.provider.Settings.EXTRA_CHANNEL_ID,LinkNotifications.COMPLETED));}));
  panel.addView(button("允许后台运行",()->{try{if(!pm.isIgnoringBatteryOptimizations(getPackageName()))startActivity(new Intent(android.provider.Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,android.net.Uri.parse("package:"+getPackageName())));else toast("已允许后台运行");}catch(ActivityNotFoundException e){startActivity(new Intent(android.provider.Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS));}}));
  panel.addView(button("应用系统设置",()->startActivity(new Intent(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS,android.net.Uri.parse("package:"+getPackageName())))));
  panel.addView(button("勿扰模式设置",()->{try{startActivity(new Intent("android.settings.ZEN_MODE_SETTINGS"));}catch(ActivityNotFoundException e){startActivity(new Intent(android.provider.Settings.ACTION_SOUND_SETTINGS));}}));
  new AlertDialog.Builder(this).setTitle("后台与通知").setView(scrollable(panel)).setPositiveButton("完成",null).show();
 }
 private ScrollView scrollable(View child){ScrollView scroll=new ScrollView(this);scroll.addView(child);return scroll;}
 private void toast(String message){Toast.makeText(this,message==null?"操作失败":UiMessages.server(message),Toast.LENGTH_LONG).show();}
 protected void onStart(){super.onStart();observing=true;runtime.attach(this);}
 protected void onStop(){observing=false;clearScroll();runtime.detach(this);super.onStop();}
 protected void onDestroy(){if(terminalFullscreen!=null)terminalFullscreen.dismiss();if(connectionSetup!=null)connectionSetup.dismiss();observing=false;runtime.detach(this);detachLayoutListener();handler.removeCallbacksAndMessages(null);io.shutdownNow();if(terminal!=null)terminal.destroy();super.onDestroy();}
}

