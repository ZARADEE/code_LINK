package com.codelink.app;

import android.app.*;
import android.content.*;
import android.net.Uri;
import android.text.InputType;
import android.view.View;
import android.widget.*;
import org.json.*;
import java.util.*;

/** Renders MCP primitive schemas without requiring users to write JSON. */
final class FormDialog {
 interface Submit { void send(JSONObject response); }
 interface Value { Object read() throws Exception; }
 static void show(Activity activity, JSONObject params, Submit submit) {
  LinearLayout panel=new LinearLayout(activity);panel.setOrientation(1);panel.setPadding(28,16,28,16);
  TextView message=new TextView(activity);message.setText(params.optString("message"));message.setTextSize(16);panel.addView(message);
  Map<String,Value> values=new LinkedHashMap<>();
  JSONObject schema=params.optJSONObject("requestedSchema");
  boolean url=params.optString("mode").equals("url");
  if(url){
   String address=params.optString("url");TextView target=new TextView(activity);target.setText(address);target.setTextIsSelectable(true);panel.addView(target);
   Button open=new Button(activity);open.setText("在浏览器中打开");panel.addView(open);
   open.setOnClickListener(v->{try{Uri uri=Uri.parse(address);if(!"https".equals(uri.getScheme()))throw new Exception("仅支持 HTTPS 地址，请在电脑处理此请求");activity.startActivity(new Intent(Intent.ACTION_VIEW,uri));}catch(Exception e){Toast.makeText(activity,e.getMessage(),Toast.LENGTH_LONG).show();}});
  } else {
   JSONObject properties=schema==null?null:schema.optJSONObject("properties");
   if(properties==null){new AlertDialog.Builder(activity).setTitle("工具交互").setMessage("此表单格式暂未支持，请在电脑命令行处理。").setNegativeButton("关闭",null).show();return;}
   Set<String> required=new HashSet<>();JSONArray keys=schema.optJSONArray("required");if(keys!=null)for(int i=0;i<keys.length();i++)required.add(keys.optString(i));
   Iterator<String> iterator=properties.keys();
   while(iterator.hasNext()){
    String key=iterator.next();JSONObject field=properties.optJSONObject(key);if(field==null)continue;
    String title=field.optString("title",key),type=field.optString("type","string");
    TextView caption=new TextView(activity);caption.setText(title+(required.contains(key)?" *":""));caption.setTextSize(15);caption.setPadding(0,18,0,4);panel.addView(caption);
    if(!field.optString("description").isEmpty()){TextView hint=new TextView(activity);hint.setText(field.optString("description"));panel.addView(hint);}
    if(type.equals("boolean")){
     CheckBox box=new CheckBox(activity);box.setText(title);box.setChecked(field.optBoolean("default",false));box.setContentDescription("form-"+key);panel.addView(box);values.put(key,()->box.isChecked());continue;
    }
    JSONArray options=field.optJSONArray("enum"),oneOf=field.optJSONArray("oneOf"),names=field.optJSONArray("enumNames");
    if(options!=null||oneOf!=null){
     final ArrayList<Object> choices=new ArrayList<>();ArrayList<String> labels=new ArrayList<>();labels.add("请选择");choices.add(null);
     int count=options!=null?options.length():oneOf.length();for(int i=0;i<count;i++){Object value=options!=null?options.opt(i):oneOf.optJSONObject(i).opt("const");choices.add(value);labels.add(options!=null?(names!=null?names.optString(i,String.valueOf(value)):String.valueOf(value)):oneOf.optJSONObject(i).optString("title",String.valueOf(value)));}
     Spinner spinner=new Spinner(activity);spinner.setAdapter(new ArrayAdapter<String>(activity,android.R.layout.simple_spinner_dropdown_item,labels));spinner.setContentDescription("form-"+key);panel.addView(spinner);Object def=field.opt("default");if(def!=null)spinner.setSelection(Math.max(0,choices.indexOf(def)));
     values.put(key,()->{Object value=choices.get(spinner.getSelectedItemPosition());if(value==null&&required.contains(key))throw new Exception("请选择 "+title);return value;});continue;
    }
    if(type.equals("array")){
     JSONObject items=field.optJSONObject("items");JSONArray entries=items==null?null:items.optJSONArray("enum");JSONArray titled=items==null?null:items.optJSONArray("anyOf");
     if(entries!=null||titled!=null){
      ArrayList<CheckBox> boxes=new ArrayList<>();ArrayList<Object> choices=new ArrayList<>();int count=entries!=null?entries.length():titled.length();
      for(int i=0;i<count;i++){Object value=entries!=null?entries.opt(i):titled.optJSONObject(i).opt("const");CheckBox box=new CheckBox(activity);box.setText(entries!=null?String.valueOf(value):titled.optJSONObject(i).optString("title",String.valueOf(value)));panel.addView(box);boxes.add(box);choices.add(value);}
      values.put(key,()->{JSONArray result=new JSONArray();for(int i=0;i<boxes.size();i++)if(boxes.get(i).isChecked())result.put(choices.get(i));if(result.length()<field.optInt("minItems",required.contains(key)?1:0)||result.length()>field.optInt("maxItems",Integer.MAX_VALUE))throw new Exception("请检查 "+title+" 的选择数量");return result;});continue;
     }
    }
    if(!Arrays.asList("string","integer","number").contains(type)){new AlertDialog.Builder(activity).setTitle("工具交互").setMessage("该工具包含暂未支持的表单字段，请在电脑命令行处理。").setNegativeButton("关闭",null).show();return;}
    EditText edit=new EditText(activity);edit.setSingleLine(false);edit.setContentDescription("form-"+key);
    edit.setInputType(type.equals("string")?InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS:InputType.TYPE_CLASS_NUMBER|InputType.TYPE_NUMBER_FLAG_SIGNED|InputType.TYPE_NUMBER_FLAG_DECIMAL);
    if(field.has("default")&&!field.isNull("default"))edit.setText(field.optString("default"));panel.addView(edit);
    values.put(key,()->{
     String text=edit.getText().toString();if(text.isEmpty()){if(required.contains(key))throw new Exception("请填写 "+title);return null;}
     if(!type.equals("string")){double number;try{number=Double.parseDouble(text);}catch(Exception e){throw new Exception(title+" 需要填写数字");}if(!Double.isFinite(number)||number<field.optDouble("minimum",-Double.MAX_VALUE)||number>field.optDouble("maximum",Double.MAX_VALUE))throw new Exception(title+" 超出允许范围");if(type.equals("integer")){if(number!=Math.rint(number))throw new Exception(title+" 需要填写整数");return Long.parseLong(text);}return number;}
     if(text.length()<field.optInt("minLength",0)||text.length()>field.optInt("maxLength",Integer.MAX_VALUE))throw new Exception("请检查 "+title+" 的长度");return text;
    });
   }
  }
  ScrollView scroll=new ScrollView(activity);scroll.addView(panel);
  AlertDialog dialog=new AlertDialog.Builder(activity).setTitle("工具需要你的确认").setView(scroll).setPositiveButton(url?"我已完成":"提交",null).setNegativeButton("拒绝",(d,w)->respond(submit,"decline",null)).setNeutralButton("稍后",null).create();
  dialog.setOnShowListener(d->dialog.getButton(-1).setOnClickListener(v->{try{JSONObject content=new JSONObject();for(Map.Entry<String,Value> entry:values.entrySet()){Object value=entry.getValue().read();if(value!=null)content.put(entry.getKey(),value);}respond(submit,"accept",url?null:content);dialog.dismiss();}catch(Exception e){Toast.makeText(activity,e.getMessage(),Toast.LENGTH_LONG).show();}}));dialog.show();
 }
 private static void respond(Submit submit,String action,JSONObject content){try{submit.send(new JSONObject().put("action",action).put("content",content==null?JSONObject.NULL:content));}catch(JSONException e){throw new IllegalStateException(e);}}
}
