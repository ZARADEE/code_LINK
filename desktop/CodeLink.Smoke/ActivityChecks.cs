using CodeLink;
namespace CodeLink.Smoke;
internal static class ActivityChecks
{
 public static void Run()
 {
  static void Require(bool value,string message){if(!value)throw new Exception(message);}
  var busy="• Working (3m 12s • esc to interrupt)\n› Ask Codex to do anything\nGPT-6 · ? for shortcuts";
  var done="─ Worked for 3m 20s ─\n• Finished the requested work.\n› Ask Codex to do anything\nGPT-6 · ? for shortcuts";
  var request="Would you like to run the following command?\n$ echo hello\n› 1. Yes, proceed (y)\n2. No, and tell Codex what to do differently (esc)\nPress enter to confirm or esc to cancel";
  var s=new WindowActivity();s.Observe(busy);Require(s.Status=="running"&&s.Cycle==1,"Busy detection");
  s.Observe(busy.Replace('•','◦'));Require(s.Status=="running"&&s.Cycle==1,"Animated hollow-dot busy marker retains run");
  s.Observe(request);Require(s.Status=="approval","Approval needs real dialog signature");
  s.Observe(busy);Require(s.Cycle==1,"Approval continuation retains run");
  s.Observe(done);Require(s.Status=="running","End marker must survive two observations");s.Observe(done);Require(s.Status=="completed","Completion transition");
  s.Observe(done+"\n"+busy);Require(s.Status=="running"&&s.Cycle==2,"Old completion does not end a new run");
  s.Observe("This log discusses approvals and completion.\nD:\\project>");Require(s.Status=="unknown","Silence and prose cannot mean completion");
  Require(s.Expire(DateTime.UtcNow.AddSeconds(20))&&s.Status=="disconnected","Stale observer cannot keep reporting an active task");
  Require(WindowActivity.Classify("CONFIRM [Y,N]?")=="input","Native prompt recognition");
  Require(WindowActivity.Classify("Conversation interrupted - tell the model what to do differently\n› Retry\n? for shortcuts")=="interrupted","Interrupted is not successful");
  File.WriteAllText("research/activity-acceptance.json",J.Stringify(new{passed=true,checks=11,source="Codex screen fixtures; not app-server protocol events"}));
  Console.WriteLine("WindowActivity: 11 behavioral checks passed");
 }
}
