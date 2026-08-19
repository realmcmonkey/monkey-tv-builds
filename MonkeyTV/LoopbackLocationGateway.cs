using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MonkeyTV;

/// <summary>Private loopback-only HTTP proxy with either a direct or Nord SOCKS5 upstream.</summary>
internal sealed class LoopbackLocationGateway : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _acceptLoop;
    private GatewayRoute _route = GatewayRoute.Direct;
    // A route change must not leave an old upstream SOCKS stream alive. New browser requests
    // receive a fresh route token; in-flight requests are cancelled and reconnect normally.
    private CancellationTokenSource _routeConnections = new();

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public void Start() { _listener.Start(); _acceptLoop = AcceptLoopAsync(_stop.Token); }
    public void SetDirect() => ChangeRoute(GatewayRoute.Direct);
    public void SetDeny() => ChangeRoute(GatewayRoute.Deny);
    public void SetNord(NordLocation location, NordCredentials credentials) => ChangeRoute(new GatewayRoute(location, credentials));

    private void ChangeRoute(GatewayRoute route)
    {
        Volatile.Write(ref _route, route);
        var previous = Interlocked.Exchange(ref _routeConnections, new CancellationTokenSource());
        previous.Cancel();
        // Do not dispose immediately: an in-flight handler may still be observing its token.
    }

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var route = Volatile.Read(ref _route);
        if (route.IsDeny) throw new InvalidOperationException("The location gateway is blocked.");
        using var upstream = await ConnectAsync(route, "example.com", 443, cancellationToken);
        // The successful CONNECT handshake establishes that the selected route authenticated and
        // accepted a remote hostname; no service/provider endpoint is contacted here.
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try { while (!token.IsCancellationRequested) _ = HandleAsync(await _listener.AcceptTcpClientAsync(token), token); }
        catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        try
        {
            using var routeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, Volatile.Read(ref _routeConnections).Token);
            var routeToken = routeCancellation.Token;
            using var browser = client.GetStream();
            var header = await ReadHeaderAsync(browser, routeToken);
            if (header is null) return;
            var first = header.Split("\r\n", 2)[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (first.Length < 2) return;
            if (!TryParseRequest(first, out var host, out var port, out var rewrittenStart)) { await RespondAsync(browser, 400, "Bad proxy request", routeToken); return; }
            var route = Volatile.Read(ref _route);
            if (route.IsDeny) { await RespondAsync(browser, 502, "Location is changing", routeToken); return; }
            try
            {
                using var upstream = await ConnectAsync(route, host, port, routeToken);
                using var target = upstream.GetStream();
                if (first[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                    await browser.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), routeToken);
                else
                    await target.WriteAsync(Encoding.ASCII.GetBytes(rewrittenStart + "\r\n" + header[(header.IndexOf("\r\n", StringComparison.Ordinal) + 2)..]), routeToken);
                await Task.WhenAny(browser.CopyToAsync(target, routeToken), target.CopyToAsync(browser, routeToken));
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
            { await RespondAsync(browser, 502, "Location route unavailable", routeToken); }
        }
        catch (OperationCanceledException) { } catch (IOException) { } catch (ObjectDisposedException) { }
    }

    private static async Task<TcpClient> ConnectAsync(GatewayRoute route, string host, int port, CancellationToken token)
    {
        if (route.Location is null)
        {
            var direct = new TcpClient(); await direct.ConnectAsync(host, port, token); return direct;
        }
        var socks = new TcpClient();
        try
        {
            await socks.ConnectAsync(route.Location.Host, 1080, token);
            var stream = socks.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 2 }, token); // username/password only
            var hello = await ReadExactAsync(stream, 2, token);
            if (hello[0] != 5 || hello[1] != 2) throw new InvalidOperationException("Nord SOCKS5 authentication was rejected.");
            var user = Encoding.UTF8.GetBytes(route.Credentials!.Username); var password = Encoding.UTF8.GetBytes(route.Credentials.Password);
            if (user.Length > 255 || password.Length > 255) throw new InvalidOperationException("NordVPN service credentials are too long.");
            await stream.WriteAsync(new[] { (byte)1, (byte)user.Length }.Concat(user).Append((byte)password.Length).Concat(password).ToArray(), token);
            var auth = await ReadExactAsync(stream, 2, token);
            if (auth[1] != 0) throw new InvalidOperationException("NordVPN service credentials were not accepted.");
            var domain = Encoding.ASCII.GetBytes(host); if (domain.Length > 255) throw new InvalidOperationException("Destination hostname is invalid.");
            await stream.WriteAsync(new byte[] { 5, 1, 0, 3, (byte)domain.Length }.Concat(domain).Concat(new[] { (byte)(port >> 8), (byte)port }).ToArray(), token);
            var response = await ReadExactAsync(stream, 4, token);
            if (response[0] != 5 || response[1] != 0) throw new InvalidOperationException("Nord SOCKS5 could not connect to the destination.");
            var remaining = response[3] switch { 1 => 6, 3 => (await ReadExactAsync(stream, 1, token))[0] + 2, 4 => 18, _ => throw new InvalidOperationException("Invalid SOCKS5 response.") };
            await ReadExactAsync(stream, remaining, token);
            return socks;
        }
        catch { socks.Dispose(); throw; }
    }

    private static bool TryParseRequest(string[] first, out string host, out int port, out string rewrittenStart)
    {
        host = ""; port = 443; rewrittenStart = "";
        if (first[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            var value = first[1].Trim('[', ']'); var split = value.LastIndexOf(':');
            if (split <= 0 || !int.TryParse(value[(split + 1)..], out port)) return false;
            host = value[..split]; rewrittenStart = first[0] + " " + first[1] + " HTTP/1.1"; return true;
        }
        if (!Uri.TryCreate(first[1], UriKind.Absolute, out var uri)) return false;
        host = uri.Host; port = uri.Port; rewrittenStart = first[0] + " " + uri.PathAndQuery + " " + (first.Length > 2 ? first[2] : "HTTP/1.1"); return true;
    }
    private static async Task<string?> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new List<byte>(); var one = new byte[1];
        while (buffer.Count < 32768 && await stream.ReadAsync(one, token) == 1) { buffer.Add(one[0]); if (buffer.Count >= 4 && buffer[^4] == 13 && buffer[^3] == 10 && buffer[^2] == 13 && buffer[^1] == 10) return Encoding.ASCII.GetString(buffer.ToArray()); }
        return null;
    }
    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken token)
    {
        var data = new byte[count]; var offset = 0; while (offset < count) { var read = await stream.ReadAsync(data.AsMemory(offset), token); if (read == 0) throw new IOException("Unexpected end of SOCKS5 response."); offset += read; } return data;
    }
    private static Task RespondAsync(NetworkStream stream, int code, string text, CancellationToken token) => stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {text}\r\nConnection: close\r\n\r\n"), token).AsTask();
    public async ValueTask DisposeAsync() { _routeConnections.Cancel(); _stop.Cancel(); _listener.Stop(); if (_acceptLoop is not null) try { await _acceptLoop; } catch { } _routeConnections.Dispose(); _stop.Dispose(); }
    private sealed record GatewayRoute(NordLocation? Location, NordCredentials? Credentials, bool IsDeny = false)
    { public static GatewayRoute Direct { get; } = new(null, null); public static GatewayRoute Deny { get; } = new(null, null, true); }
}
