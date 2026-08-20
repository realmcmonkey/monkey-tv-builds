using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class GatewayProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _acceptLoop;
    private int _attemptCount;
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    internal int AttemptCount => Volatile.Read(ref _attemptCount);

    internal void Start() { _listener.Start(); _acceptLoop = AcceptLoopAsync(_stop.Token); }
    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try { while (!token.IsCancellationRequested) _ = HandleAsync(await _listener.AcceptTcpClientAsync(token), token); }
        catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
    }
    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using var local = client;
        try
        {
            using var stream = local.GetStream();
            var header = await ReadHeaderAsync(stream, token);
            var first = header.Split("\r\n", 2)[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (first.Length < 2 || !first[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase) || !TryParseTarget(first[1], out var host, out var port))
            { await stream.WriteAsync("HTTP/1.1 400 Invalid CONNECT\r\nConnection: close\r\n\r\n"u8.ToArray(), token); return; }
            Interlocked.Increment(ref _attemptCount);
            var addresses = await Dns.GetHostAddressesAsync(host, token);
            TcpClient? upstream = null;
            foreach (var address in addresses)
            {
                try { var candidate = new TcpClient(address.AddressFamily); await candidate.ConnectAsync(address, port, token); upstream = candidate; break; }
                catch { }
            }
            if (upstream is null)
            {
                Console.WriteLine($"GATEWAY_BLOCKED target={host}:{port}"); Console.Out.Flush();
                await stream.WriteAsync("HTTP/1.1 502 Route unavailable\r\nConnection: close\r\n\r\n"u8.ToArray(), token); return;
            }
            using (upstream)
            using (var remote = upstream.GetStream())
            {
                var localEndpoint = (IPEndPoint?)upstream.Client.LocalEndPoint;
                Console.WriteLine($"GATEWAY_UPSTREAM local={localEndpoint?.Address} family={localEndpoint?.AddressFamily} target={host}:{port}"); Console.Out.Flush();
                await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), token);
                await Task.WhenAll(stream.CopyToAsync(remote, token), remote.CopyToAsync(stream, token));
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        { Console.WriteLine($"GATEWAY_BLOCKED reason={ex.GetType().Name}"); Console.Out.Flush(); }
    }
    private static async Task<string> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        var bytes = new List<byte>(1024); var buffer = new byte[1];
        while (bytes.Count < 16384 && await stream.ReadAsync(buffer, token) == 1)
        { bytes.Add(buffer[0]); if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n') break; }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }
    private static bool TryParseTarget(string target, out string host, out int port)
    {
        host = string.Empty; port = 0; var separator = target.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(target[(separator + 1)..], out port) || port is < 1 or > 65535) return false;
        host = target[..separator].Trim('[', ']'); return host.Length > 0;
    }
    public async ValueTask DisposeAsync()
    { _stop.Cancel(); _listener.Stop(); if (_acceptLoop is not null) try { await _acceptLoop; } catch { } _stop.Dispose(); }
}
