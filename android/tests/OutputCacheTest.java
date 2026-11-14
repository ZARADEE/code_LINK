package com.codelink.app;
public final class OutputCacheTest {
 static void check(boolean value,String why){if(!value)throw new AssertionError(why);}
 public static void main(String[] args){
  OutputCache cache=new OutputCache();cache.accept("a","first\n",true);cache.accept("a","background output\n",false);
  check(cache.text().equals("first\nbackground output\n"),"Output arriving without an Activity must survive resubscription");
  cache.accept("b","other terminal",false);check(cache.text().equals("other terminal"),"Changing selected terminal must not mix output");
  cache.accept("b","reconnected snapshot",true);check(cache.text().equals("reconnected snapshot"),"Reconnection replaces, rather than duplicates, the snapshot");
  cache.accept("b",new String(new char[120001]).replace('\0','x'),false);check(cache.text().length()==100000,"Background output must stay bounded");
  cache.clear();check(cache.text().isEmpty(),"Clearing connection must discard stale output");
  System.out.println("OutputCache behavioral checks passed");
 }
}
