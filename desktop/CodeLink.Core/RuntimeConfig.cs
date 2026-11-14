using System.Text.Json.Nodes;
namespace CodeLink;

public static class RuntimeConfig
{
    private static bool loaded;
    public static void Load()
    {
        if(loaded)return;loaded=true;
        var path=Path.Combine(AppContext.BaseDirectory,"runtime.json");
        if(!File.Exists(path))return;
        var config=JsonNode.Parse(File.ReadAllText(path));
        if(Environment.GetEnvironmentVariable("CODELINK_INSTANCE") is null && config?.S("instance") is {Length:>0} instance)
            Environment.SetEnvironmentVariable("CODELINK_INSTANCE",instance);
        if(Environment.GetEnvironmentVariable("CODELINK_DATA_DIR") is null && config?.S("dataDirectory") is {Length:>0} data)
            Environment.SetEnvironmentVariable("CODELINK_DATA_DIR",Path.GetFullPath(data,AppContext.BaseDirectory));
    }
}
