using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MonkeyTV.VpnProxyProof;

/// <summary>
/// Deliberately minimal HTTP CONNECT proxy used only by the Feature #0.01 proof harness.
/// It records every attempted endpoint and can fail closed without any direct-browser fallback.
/// It is not production VPN code and does not create a tunnel.
/// </summary>
internal sealed class LoopbackProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<ProxyAttempt> _attempts = new();
    private Task? _acceptLoop;

    internal bool AllowConnections { get; set; } = true;
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    internal IReadOnlyCollection<ProxyAttempt> Attempts => _attempts.ToArray();

    internal void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_stop.Token);
    }

    internal bool SawHost(string host) => Attempts.Any(attempt =>
        attempt.Target.StartsWith(host + ":", StringComparison.OrdinalIgnoreCase));

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
                _ = HandleClientAsync(await _listener.AcceptTcpClientAsync(cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        try
        {
            using var stream = client.GetStream();
            var header = await ReadHeaderAsync(stream, cancellationToken);
            if (string.IsNullOrWhiteSpace(header)) return;
            var first = header.Split("\r\n", 2)[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (first.Length < 2) return;

            if (!first[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                _attempts.Enqueue(new ProxyAttempt(first[1], false, AllowConnections, Array.Empty<IPAddress>(), "Only CONNECT is supported by this proof proxy."));
                await WriteResponseAsync(stream, 501, "CONNECT required", cancellationToken);
                return;
            }

            var target = first[1];
            if (!TryParseTarget(target, out var host, out var port))
            {
                _attempts.Enqueue(new ProxyAttempt(target, true, false, Array.Empty<IPAddress>(), "Invalid CONNECT target."));
                await WriteResponseAsync(stream, 400, "Invalid target", cancellationToken);
                return;
            }

            if (!AllowConnections)
            {
                _attempts.Enqueue(new ProxyAttempt(target, true, false, Array.Empty<IPAddress>(), "Gateway deny mode."));
                await WriteResponseAsync(stream, 502, "Gateway deny mode", cancellationToken);
                return;
            }

            IPAddress[] addresses;
            try { addresses = await Dns.GetHostAddressesAsync(host, cancellationToken); }
            catch (Exception ex)
            {
                _attempts.Enqueue(new ProxyAttempt(target, true, false, Array.Empty<IPAddress>(), "DNS: " + ex.GetType().Name));
                await WriteResponseAsync(stream, 502, "DNS failed", cancellationToken);
                return;
            }

            TcpClient? upstream = null;
            Exception? lastError = null;
            foreach (var address in addresses)
            {
                try
                {
                    var candidate = new TcpClient(address.AddressFamily);
                    await candidate.ConnectAsync(address, port, cancellationToken);
                    upstream = candidate;
                    break;
                }
                catch (Exception ex) { lastError = ex; }
            }

            if (upstream is null)
            {
                _attempts.Enqueue(new ProxyAttempt(target, true, false, addresses, "Connect: " + lastError?.GetType().Name));
                await WriteResponseAsync(stream, 502, "Upstream unavailable", cancellationToken);
                return;
            }

            using (upstream)
            using (var upstreamStream = upstream.GetStream())
            {
                _attempts.Enqueue(new ProxyAttempt(target, true, true, addresses, null));
                await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), cancellationToken);
                await Task.WhenAny(stream.CopyToAsync(upstreamStream, cancellationToken), upstreamStream.CopyToAsync(stream, cancellationToken));
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private static async Task<string> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(1024);
        var buffer = new byte[1];
        while (bytes.Count < 16384 && await stream.ReadAsync(buffer, cancellationToken) == 1)
        {
            bytes.Add(buffer[0]);
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
                return Encoding.ASCII.GetString(bytes.ToArray());
        }
        return string.Empty;
    }

    private static bool TryParseTarget(string target, out string host, out int port)
    {
        host = string.Empty; port = 443;
        var separator = target.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(target[(separator + 1)..], out port) || port is < 1 or > 65535) return false;
        host = target[..separator].Trim('[', ']');
        return host.Length > 0;
    }

    private static Task WriteResponseAsync(NetworkStream stream, int status, string message, CancellationToken cancellationToken) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {message}\r\nConnection: close\r\n\r\n"), cancellationToken).AsTask();

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        if (_acceptLoop is not null) try { await _acceptLoop; } catch { }
        _stop.Dispose();
    }
}

internal sealed record ProxyAttempt(string Target, bool IsConnect, bool Allowed, IReadOnlyList<IPAddress> ResolvedAddresses, string? Detail);
