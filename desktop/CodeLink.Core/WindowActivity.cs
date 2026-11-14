using System.Text.RegularExpressions;

namespace CodeLink;

// Conservative screen adapter for the tested Codex CLI. Silence is never a
// completion signal. Unknown screens remain unknown and cannot finish a batch.
public sealed class WindowActivity
{
    private string candidate="";
    private int observations;
    public string Status {get;private set;}="unknown";
    public long Cycle {get;private set;}
    public long Revision {get;private set;}
    public DateTime SeenAt {get;private set;}=DateTime.UtcNow;
    public string Source {get;private set;}="window";
    public void Observe(string text,DateTime? now=null)
    {
        SeenAt=now??DateTime.UtcNow;
        var next=Classify(text);
        if(next==candidate)observations++;else{candidate=next;observations=1;}
        // Wait for a second sampled screen before accepting an end marker.
        if(next=="completed"&&observations<2)return;
        if(next==Status)return;
        if(next=="running"&&Status is not ("running" or "approval" or "input"))Cycle++;
        if(next is "approval" or "input"&&Cycle==0)Cycle++;
        Status=next;Revision++;
        if(next is "running" or "completed" or "approval" or "interrupted")Source="codex-screen";
    }
    public bool Expire(DateTime now)
    {
        if(now-SeenAt<=TimeSpan.FromSeconds(12)||Status=="disconnected")return false;
        Status="disconnected";Revision++;return true;
    }
    public static string Classify(string text)
    {
        if(text.StartsWith("[Terminal window closed]")||text.StartsWith("[Terminal text is temporarily unavailable]")||text.StartsWith("[This terminal does not expose")||text.StartsWith("[终端窗口已关闭]")||text.StartsWith("[终端文本暂不可读取]")||text.StartsWith("[此终端未公开"))return "disconnected";
        var lines=text.Replace("\r","").Split('\n').Select(l=>l.Trim()).Where(l=>l.Length>0).ToArray();
        var tail=string.Join("\n",lines.TakeLast(32));
        bool Match(string pattern)=>Regex.IsMatch(tail,pattern,RegexOptions.IgnoreCase|RegexOptions.Multiline);
        if(Match(@"^(?:Would you like to (?:run the following command|make the following edits)|Do you want to (?:run this command|allow))\??$")
           &&Match(@"^(?:[›❯>]\s*)?1\.\s*Yes")&&Match(@"(?:esc to cancel|escape to cancel|No,? and tell Codex)"))return "approval";
        if(Match(@"^.*\[Y,N\]\?$")||Match(@"^.*\[y/N\]\s*$"))return "input";
        var busy=-1;var done=-1;var interrupted=-1;
        for(var i=0;i<lines.Length;i++)
        {
            if(Regex.IsMatch(lines[i],@"^[•◦∙●·⠋-⣿]\s*.+\(.*esc to interrupt",RegexOptions.IgnoreCase))busy=i;
            if(Regex.IsMatch(lines[i],@"^[─━\s]*[•●]?\s*Worked for \d",RegexOptions.IgnoreCase))done=i;
            if(Regex.IsMatch(lines[i],@"^[•●]?\s*Conversation interrupted",RegexOptions.IgnoreCase))interrupted=i;
        }
        if(busy>Math.Max(done,interrupted))return "running";
        if(interrupted>=0&&interrupted>done)return "interrupted";
        // A prompt and footer distinguish the idle TUI from quoted log text.
        if(done>=0&&Match(@"^[›❯]\s")&&Match(@"(?:for shortcuts|context left|GPT-\d|gpt-\d)"))return "completed";
        return "unknown";
    }
}
