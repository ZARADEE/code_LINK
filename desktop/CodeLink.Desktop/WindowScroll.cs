using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CodeLink.Desktop;

internal static class WindowScroll
{
    // Prefer the terminal's UIA history scrollbar. Full-screen programs own their
    // scrolling, so forward a wheel message to that Windows Terminal only.
    // Never move the global mouse, activate a window or synthesize navigation keys.
    public static void Apply(int processId,long windowHandle,int lines,bool latest)
    {
        var handle=new IntPtr(windowHandle);
        GetWindowThreadProcessId(handle,out var owner);
        if(owner!=processId)throw new IOException("目标终端窗口已关闭或已改变");
        var window=AutomationElement.FromHandle(handle);
        var nodes=window.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().ToArray();
        var terminals=nodes.Where(e=>e.Current.ClassName=="TermControl").ToArray();
        if(terminals.Length>1)throw new IOException("该窗口包含多个终端分栏，请先使用单个终端窗口滚动。");
        var scrollbars=nodes.Where(e=>e.Current.ControlType==ControlType.ScrollBar &&
            (OrientationType)e.GetCurrentPropertyValue(AutomationElement.OrientationProperty)==OrientationType.Vertical &&
            e.TryGetCurrentPattern(RangeValuePattern.Pattern,out _)).ToArray();
        if(scrollbars.Length==1)
        {
            var range=(RangeValuePattern)scrollbars[0].GetCurrentPattern(RangeValuePattern.Pattern);
            var current=range.Current;
            if(current.IsReadOnly)throw new IOException("该终端的滚动条不允许远程操作。");
            if(current.Maximum<=current.Minimum && terminals.Length==1 && window.Current.ClassName=="CASCADIA_HOSTING_WINDOW_CLASS")
            {
                if(latest)throw new IOException("该窗口没有可定位的历史滚动条。全屏程序请继续上滑查看后文。");
                var bounds=terminals[0].Current.BoundingRectangle;
                if(bounds.IsEmpty||bounds.Width<=0||bounds.Height<=0||IsIconic(handle))throw new IOException("请先在电脑上还原该终端窗口，再同步滚动。");
                var x=(int)(bounds.Left+bounds.Width/2);var y=(int)(bounds.Top+bounds.Height/2);
                var position=new IntPtr(unchecked((y&0xffff)<<16|(x&0xffff)));
                var wheel=new IntPtr(unchecked((-Math.Sign(lines)*120)<<16));
                for(var i=0;i<(Math.Abs(lines)+2)/3;i++)
                    if(!PostMessage(handle,0x020A,wheel,position))throw new IOException("无法向该终端窗口发送滚轮操作。");
                return;
            }
            var step=current.SmallChange>0?current.SmallChange:1;
            var target=latest?current.Maximum:Math.Clamp(current.Value+lines*step,current.Minimum,current.Maximum);
            if(Math.Abs(target-current.Value)>.001)range.SetValue(target);
            return;
        }
        // Classic consoles may expose scrolling directly on their text area.
        var candidates=nodes.Prepend(window).Where(e=>e.TryGetCurrentPattern(TextPattern.Pattern,out _) &&
            e.TryGetCurrentPattern(ScrollPattern.Pattern,out _)).ToArray();
        if(candidates.Length==1)
        {
            var scroll=(ScrollPattern)candidates[0].GetCurrentPattern(ScrollPattern.Pattern);
            if(!scroll.Current.VerticallyScrollable)return;
            if(latest)scroll.SetScrollPercent(ScrollPattern.NoScroll,100);
            else for(var i=0;i<Math.Abs(lines);i++)scroll.Scroll(ScrollAmount.NoAmount,lines<0?ScrollAmount.SmallDecrement:ScrollAmount.SmallIncrement);
            return;
        }
        throw new IOException("该终端未提供可操作的滚动条。请切换为本地查看，或使用 Windows Terminal 单窗口。");
    }
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out int processId);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
}
