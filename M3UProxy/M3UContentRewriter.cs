namespace M3UProxy;

public static class M3UContentRewriter
{
    public static string Rewrite(string content, Uri upstreamBaseUri, Uri proxyBaseUri)
    {
        var upstreamOrigin = upstreamBaseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        var proxyOrigin = proxyBaseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

        return content.Replace(upstreamOrigin, proxyOrigin, StringComparison.OrdinalIgnoreCase);
    }
}
