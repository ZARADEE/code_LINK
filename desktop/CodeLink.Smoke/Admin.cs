using CodeLink;
using System.Text.Json.Nodes;
namespace CodeLink.Smoke;
public static class Admin
{
    public static async Task Run(string[] args)
    {
        using var client=new LocalClient();await client.Connect(4000);
        for(var i=0;client.CurrentState is null&&i<100;i++)await Task.Delay(30);
        if(client.CurrentState is null)throw new IOException("No state");
        if(args[0]=="state")
        {
            var state=client.CurrentState.DeepClone();state.AsObject().Remove("pairing");Console.WriteLine(state.ToJsonString());return;
        }
        if(args[0]=="watch")
        {
            await client.Watch(args[1]);await Task.Delay(600);Console.WriteLine(client.CurrentState.S("output"));return;
        }
        if(args[0]=="invite")
        {
            await client.Action("rotatePairing");await Task.Delay(300);
            var p=client.CurrentState["pairing"]!;
            var value=J.Stringify(new{host=args.ElementAtOrDefault(1)??"127.0.0.1",port=int.Parse(p.S("port")),pin=p.S("fingerprint"),code=p.S("code")});
            Console.WriteLine("codelink:"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+','-').Replace('/','_'));return;
        }
        var parameters=args.Length>2?JsonNode.Parse(File.ReadAllText(args[2])):new JsonObject();
        Console.WriteLine((await client.Action(args[1],parameters)).ToJsonString());
    }
}
