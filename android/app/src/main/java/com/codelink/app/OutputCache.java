package com.codelink.app;

/** Keeps the selected terminal's deltas while no Activity is displaying them. */
public final class OutputCache {
 private String selected="",text="";
 public void accept(String id,String chunk,boolean reset){
  text=reset||!selected.equals(id)?chunk:text+chunk;selected=id;
  if(text.length()>120000)text=text.substring(text.length()-100000);
 }
 public String text(){return text;}
 public void clear(){selected="";text="";}
}
