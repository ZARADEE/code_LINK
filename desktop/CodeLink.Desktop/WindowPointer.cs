using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace CodeLink.Desktop;

internal static class WindowPointer
{
    private static string[] Read(int processId,long windowHandle)
    {
        var handle=new IntPtr(windowHandle);GetWindowThreadProcessId(handle,out var owner);
        if(owner!=processId)throw new IOException("目标终端窗口已关闭或已改变");
        if(IsIconic(handle))throw new IOException("请先在电脑上还原该终端窗口，再同步滚动。");
        var window=AutomationElement.FromHandle(handle);
        var terms=window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ClassNameProperty,"TermControl")).Cast<AutomationElement>().ToArray();
        if(terms.Length!=1)throw new IOException("点击目前支持 Windows Terminal 单窗格。");
        var pattern=(TextPattern)terms[0].GetCurrentPattern(TextPattern.Pattern);
        return string.Join("",pattern.GetVisibleRanges().Select(r=>r.GetText(-1))).TrimEnd('\0').Split('\n').Select(s=>s.TrimEnd('\r')).ToArray();
    }
    public static void Validate(int processId,long handle,int row,int column,string expected)
    {
        var lines=Read(processId,handle);
        if(row<0||row>=lines.Length||column<0||column>=1000||lines[row]!=expected)throw new IOException("终端画面已变化，请等待更新后重新点击。");
    }
    public static JsonObject? BackToBottom(int processId,long handle)
    {
        string[] lines;
        try{lines=Read(processId,handle);}catch(IOException){return null;}
        var matches=lines.Select((line,row)=>(line,row)).Where(x=>Regex.IsMatch(x.line,@"^\s*(?:New activity\s*[·•]\s*)?[↓▼↧\[\(]*\s*back to (bottom|latest)\s*[\]\)]*(?:\s*[·•]\s*esc)?\s*$",RegexOptions.IgnoreCase)).ToArray();
        if(matches.Length!=1)return null;
        var found=matches[0];var column=found.line.IndexOf("back to",StringComparison.OrdinalIgnoreCase)+2;
        return new JsonObject{["operation"]="pointer",["row"]=found.row,["column"]=column,["expectedLine"]=found.line};
    }
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out int processId);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
}
