using System.Text.Json.Nodes;

namespace M3UProxyApp;

internal static class XtreamResponseRewriter
{
    public static string RewriteLogin(string content, Uri publicBaseUri)
    {
        if (JsonNode.Parse(content) is not { } root || root["server_info"] is not { } serverInfo)
        {
            return content;
        }

        serverInfo["url"] = publicBaseUri.Host;
        serverInfo["port"] = publicBaseUri.Port.ToString();
        serverInfo["https_port"] = publicBaseUri.Port.ToString();
        serverInfo["server_protocol"] = publicBaseUri.Scheme;

        return root.ToJsonString();
    }
}
