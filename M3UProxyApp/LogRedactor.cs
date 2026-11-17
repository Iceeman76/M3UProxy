using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace DotNetLab.M3UProxyApp;

internal sealed class LogRedactor(IConfiguration configuration)
{
    private readonly string[] secrets = GetSecrets(configuration);

    public string Redact(string message)
    {
        var sanitized = Regex.Replace(message, @"https?://[^\s<>""']+", "[redacted URL]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"(?i)\b(authorization|cookie|set-cookie)\s*:\s*[^\r\n]+", "$1: [redacted]");
        sanitized = Regex.Replace(sanitized, @"(?i)(/(?:live|movie|series|timeshift)/)[^/\s]+/[^/\s]+", "$1[redacted]/[redacted]");
        sanitized = Regex.Replace(sanitized, @"(?i)([?&][^=\s&]+)=([^&\s""'<>]+)", "$1=[redacted]");
        sanitized = Regex.Replace(sanitized, @"(?i)\b(Bearer|Basic)\s+[^\s,;""']+", "$1 [redacted]");
        sanitized = Regex.Replace(sanitized, "(?i)([\"'](?:username|password|passwd|token|access_token|refresh_token|api[_-]?key|secret|authorization|cookie)[\"']\\s*:\\s*)[\"'][^\"']*[\"']", "$1\"[redacted]\"");
        sanitized = Regex.Replace(sanitized, @"(?i)\b(username|password|passwd|token|access_token|refresh_token|client_secret|api[_-]?key|secret|authorization|cookie)\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s,;]+)", "$1=[redacted]");

        foreach (var secret in secrets)
        {
            sanitized = sanitized.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        return sanitized.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static string[] GetSecrets(IConfiguration configuration)
    {
        var values = new List<string>();

        foreach (var setting in configuration.AsEnumerable())
        {
            if (string.IsNullOrEmpty(setting.Value))
            {
                continue;
            }

            if (Regex.IsMatch(setting.Key, @"(?i)(password|passwd|username|token|secret|apikey|api_key|authorization|cookie)"))
            {
                values.Add(setting.Value);
            }

            if (Uri.TryCreate(setting.Value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                values.AddRange(QueryHelpers.ParseQuery(uri.Query).Where(parameter => parameter.Key.ToLowerInvariant() is not ("type" or "output" or "action")).SelectMany(parameter => parameter.Value).OfType<string>());
                values.AddRange(uri.UserInfo.Split(':'));
            }
        }

        return values.Where(value => string.IsNullOrEmpty(value) is false).SelectMany(value => new[] { value, Uri.EscapeDataString(value), System.Net.WebUtility.UrlEncode(value) }).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length).ToArray();
    }
}
