using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace Claxe.Services;

/// <summary>
/// Secure DNS for Sorune only: a tiny proxy on this computer. The browser hands it its HTTPS
/// connections; it looks the site's name up over DNS-over-HTTPS (the chosen service, reached by
/// address, so the operator's DNS is never asked) and joins the two ends. The encrypted traffic
/// passes untouched, and the rest of Windows keeps its own DNS.
/// </summary>
public static class SecureDns
{
    public sealed record Provider(string Id, string Name, string Note, string Url);

    /// <summary>Services to choose from; each answers standard DoH over HTTP/1.1 at its address.</summary>
    public static readonly Provider[] Providers =
    [
        new("cloudflare", "Cloudflare", "Hızlı, Türkiye'de sunucusu var (önerilen)", "https://1.1.1.1/dns-query"),
        new("google", "Google", "Hızlı ve kararlı", "https://8.8.8.8/dns-query"),
        new("adguard", "AdGuard", "Reklam ve izleyicileri de engeller", "https://94.140.14.14/dns-query"),
        new("opendns", "OpenDNS", "Cisco'nun, kararlı", "https://146.112.41.2/dns-query"),
        new("dnssb", "DNS.SB", "Kayıt tutmaz, gizliliğe odaklı", "https://185.222.222.222/dns-query"),
    ];

    public static Provider Find(string? id) => Providers.FirstOrDefault(p => p.Id == id) ?? Providers[0];

    private static Provider _current = Providers[0];
    private static readonly ConcurrentDictionary<string, (IPAddress Address, DateTime Until)> Cache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static TcpListener? _listener;

    public static Provider Current
    {
        get => _current;
        set
        {
            _current = value;
            Cache.Clear();
        }
    }

    /// <summary>Starts the proxy (only reachable from this computer) and returns its port.</summary>
    public static int Start()
    {
        if (_listener is { } running) return ((IPEndPoint)running.LocalEndpoint).Port;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listener = listener;
        _ = AcceptLoop(listener);
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch { return; }
            _ = Handle(client);
        }
    }

    private static async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                var page = client.GetStream();
                var head = await ReadHead(page);
                var parts = head?.Split('\r')[0].Split(' ');
                var target = parts is { Length: >= 2 } && parts[0] == "CONNECT" ? SplitHostPort(parts[1]) : null;
                if (target is not { } t)
                {
                    await page.WriteAsync("HTTP/1.1 405 Method Not Allowed\r\n\r\n"u8.ToArray());
                    return;
                }
                using var remote = new TcpClient();
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await remote.ConnectAsync(await Resolve(t.Host), t.Port, timeout.Token);
                }
                catch
                {
                    await page.WriteAsync("HTTP/1.1 502 Bad Gateway\r\n\r\n"u8.ToArray());
                    return;
                }
                await page.WriteAsync("HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray());
                var site = remote.GetStream();
                // Whichever side ends first closes both.
                await Task.WhenAny(page.CopyToAsync(site), site.CopyToAsync(page));
            }
            catch
            {
                // A dropped connection: the browser retries by itself.
            }
        }
    }

    /// <summary>The request head up to the empty line, read byte by byte so nothing of the tunnel is taken.</summary>
    private static async Task<string?> ReadHead(Stream stream)
    {
        var head = new StringBuilder();
        var one = new byte[1];
        while (head.Length < 8192)
        {
            if (await stream.ReadAsync(one) == 0) return null;
            head.Append((char)one[0]);
            if (head.Length >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n') return head.ToString();
        }
        return null;
    }

    private static (string Host, int Port)? SplitHostPort(string value)
    {
        var colon = value.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(value[(colon + 1)..], out var port)) return null;
        return (value[..colon].Trim('[', ']'), port);
    }

    /// <summary>
    /// DNS-over-HTTPS: the chosen service, then the others if it cannot be reached. If none answers,
    /// an older answer is still better than the operator's, which for a blocked site is the block
    /// page. Windows' own DNS is the very last resort and is never remembered.
    /// </summary>
    private static async Task<IPAddress> Resolve(string host)
    {
        if (IPAddress.TryParse(host, out var literal)) return literal;
        var hasCached = Cache.TryGetValue(host, out var cached);
        if (hasCached && cached.Until > DateTime.UtcNow) return cached.Address;
        foreach (var service in Providers.OrderBy(p => p == Current ? 0 : 1))
        {
            (IPAddress Address, int Ttl)? found;
            try { found = await Lookup(host, service.Url); }
            catch (Exception ex)
            {
                Log.Info($"Güvenli DNS: {service.Name} cevap vermedi ({ex.GetType().Name}).");
                continue;
            }
            // A working service with no address for the name: asking the others would not help.
            if (found is not { } answer) break;
            Cache[host] = (answer.Address, DateTime.UtcNow.AddSeconds(Math.Max(answer.Ttl, 300)));
            return answer.Address;
        }
        if (hasCached) return cached.Address;
        return (await Dns.GetHostAddressesAsync(host)).OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).First();
    }

    /// <summary>Asks a service (RFC 8484, the standard every one of them speaks): A first, then AAAA.</summary>
    public static async Task<(IPAddress Address, int Ttl)?> Lookup(string host, string resolver)
    {
        foreach (var type in new[] { 1, 28 })
        {
            var query = Convert.ToBase64String(Query(host, type)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{resolver}?dns={query}");
            request.Headers.Accept.ParseAdd("application/dns-message");
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            if (Answer(await response.Content.ReadAsByteArrayAsync(), type) is { } answer) return answer;
        }
        return null;
    }

    private static byte[] Query(string host, int type)
    {
        var bytes = new List<byte> { 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 }; // id 0, recursion wanted, one question
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var text = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)text.Length);
            bytes.AddRange(text);
        }
        bytes.AddRange([0, 0, (byte)type, 0, 1]);
        return [.. bytes];
    }

    /// <summary>The first address of <paramref name="type"/> in a DNS answer, with its lifetime in seconds.</summary>
    private static (IPAddress Address, int Ttl)? Answer(byte[] message, int type)
    {
        int U16(int at) => (message[at] << 8) | message[at + 1];
        int SkipName(int at)
        {
            while (true)
            {
                var length = message[at];
                if (length == 0) return at + 1;
                if ((length & 0xc0) == 0xc0) return at + 2;
                at += length + 1;
            }
        }
        if (message.Length < 12) return null;
        var at = 12;
        for (var i = U16(4); i > 0; i--) at = SkipName(at) + 4;
        for (var i = U16(6); i > 0; i--)
        {
            at = SkipName(at);
            var recordType = U16(at);
            var ttl = (U16(at + 4) << 16) | U16(at + 6);
            var length = U16(at + 8);
            at += 10;
            if (recordType == type && length is 4 or 16)
                return (new IPAddress(message.AsSpan(at, length)), Math.Clamp(ttl, 30, 3600));
            at += length;
        }
        return null;
    }
}
