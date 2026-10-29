using System.Text.Json.Nodes;
using DotNetLab.M3UProxyApp;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class XtreamContentRewriterTests
{
    [Test]
    public void RewriteLoginResponse_ReplacesServerConnectionDetails()
    {
        var content = """{"user_info":{"auth":1},"server_info":{"url":"provider.example","port":"8080","https_port":"8443","server_protocol":"http"}}""";

        var result = XtreamResponseRewriter.RewriteLogin(content, new Uri("http://127.0.0.1:5050"));
        var serverInfo = GetRequiredNode(result, "server_info");

        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredValue(serverInfo, "url"), Is.EqualTo("127.0.0.1"));
            Assert.That(GetRequiredValue(serverInfo, "port"), Is.EqualTo("5050"));
            Assert.That(GetRequiredValue(serverInfo, "https_port"), Is.EqualTo("5050"));
            Assert.That(GetRequiredValue(serverInfo, "server_protocol"), Is.EqualTo("http"));
        });
    }

    [Test]
    public void RewriteLoginResponse_PreservesUserInformation()
    {
        var content = """{"user_info":{"auth":1,"status":"Active"},"server_info":{"url":"provider.example"}}""";

        var result = XtreamResponseRewriter.RewriteLogin(content, new Uri("https://proxy.example:5443"));
        var userInfo = GetRequiredNode(result, "user_info");
        var serverInfo = GetRequiredNode(result, "server_info");

        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredValue<int>(userInfo, "auth"), Is.EqualTo(1));
            Assert.That(GetRequiredValue(userInfo, "status"), Is.EqualTo("Active"));
            Assert.That(GetRequiredValue(serverInfo, "server_protocol"), Is.EqualTo("https"));
        });
    }

    [Test]
    public void RewriteLoginResponse_ReturnsOriginalContentWithoutServerInformation()
    {
        var content = """{"user_info":{"auth":1}}""";

        var result = XtreamResponseRewriter.RewriteLogin(content, new Uri("http://127.0.0.1:5050"));

        Assert.That(result, Is.EqualTo(content));
    }

    private static JsonNode GetRequiredNode(string content, string propertyName)
    {
        var root = JsonNode.Parse(content) ?? throw new InvalidOperationException("Expected valid JSON content.");

        return root[propertyName] ?? throw new InvalidOperationException($"Expected property '{propertyName}'.");
    }

    private static string GetRequiredValue(JsonNode node, string propertyName) => GetRequiredValue<string>(node, propertyName);

    private static T GetRequiredValue<T>(JsonNode node, string propertyName)
    {
        var property = node[propertyName] ?? throw new InvalidOperationException($"Expected property '{propertyName}'.");

        return property.GetValue<T>();
    }
}
