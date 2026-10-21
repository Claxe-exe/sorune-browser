using System.Globalization;
using System.Net;

namespace Claxe.Services;

/// <summary>Decides whether address bar input is a URL or a search query.</summary>
public static class AddressResolver
{
    private static readonly HashSet<string> NavigableSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "file", "about" };

    public static bool IsNavigable(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && NavigableSchemes.Contains(uri.Scheme);

    public static string Resolve(string input, SearchEngine engine)
    {
        var text = input.Trim();

        // "example.com:8080" parses as an absolute URI with scheme "example.com",
        // so only accept schemes we actually know.
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) && NavigableSchemes.Contains(absolute.Scheme))
            return absolute.AbsoluteUri;

        if (!text.Contains(' ') && TryGetHost(text, out var host))
        {
            var scheme = IsLocalHost(host) ? "http://" : "https://";
            if (Uri.TryCreate(scheme + text, UriKind.Absolute, out var guessed))
                return guessed.AbsoluteUri;
        }

        return engine.BuildUrl(text);
    }

    private static bool TryGetHost(string text, out string host)
    {
        host = text;
        var end = text.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
            host = text[..end];

        // Strip the port, but keep IPv6 literals like [::1] intact.
        var colon = host.LastIndexOf(':');
        if (colon > 0 && !host.EndsWith(']'))
        {
            if (!int.TryParse(host[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return false;
            host = host[..colon];
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // IPAddress.TryParse also accepts shorthand like "1" or "1.2"; only dotted quads count.
        if (IPAddress.TryParse(host.Trim('[', ']'), out _))
            return host.StartsWith('[') || host.Count(c => c == '.') == 3;

        var labels = host.Split('.');
        if (labels.Length < 2 || labels.Any(l => l.Length == 0 || l.StartsWith('-') || l.EndsWith('-')))
            return false;

        if (!labels.All(l => l.All(c => char.IsLetterOrDigit(c) || c == '-')))
            return false;

        // The TLD must be alphabetic ("3.14" is a search, "site.dev" is a host).
        var tld = labels[^1];
        return tld.Length >= 2 && tld.All(char.IsLetter);
    }

    private static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168));
    }
}
