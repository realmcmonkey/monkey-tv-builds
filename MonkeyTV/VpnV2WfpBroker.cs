using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace MonkeyTV;

internal sealed class VpnV2WfpBroker : IAsyncDisposable
{
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _enabled;

    public async Task EnableAsync(string fixedRuntimeExecutable, CancellationToken token)
    {
        if (_enabled) return;
        await EnsureStartedAsync(fixedRuntimeExecutable, token);
        await SendAsync("ENABLE", token);
        _enabled = true;
    }

    public async Task DisableAsync(CancellationToken token = default)
    {
        if (!_enabled) return;
        await SendAsync("DISABLE", token);
        _enabled = false;
    }

    private async Task EnsureStartedAsync(string fixedRuntimeExecutable, CancellationToken token)
    {
        if (_process is { HasExited: false } && _pipe?.IsConnected == true) return;
        await DisposeConnectionAsync();
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("Monkey TV could not locate its executable for the V2 containment broker.");
        var pipeName = "MonkeyTV-VpnV2-Wfp-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--vpn-v2-wfp-broker=" + pipeName);
        start.ArgumentList.Add("--fixed-runtime-exe=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(fixedRuntimeExecutable))));
        try { _process = Process.Start(start) ?? throw new InvalidOperationException("The V2 containment broker did not start."); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new InvalidOperationException("V2 containment was not enabled because the Windows administrator prompt was cancelled."); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await _pipe.WaitForConnectionAsync(timeout.Token);
        _reader = new StreamReader(_pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        var ready = await _reader.ReadLineAsync(timeout.Token);
        if (!string.Equals(ready, "READY", StringComparison.Ordinal))
            throw new InvalidOperationException("The elevated V2 containment broker did not become ready.");
    }

    private async Task SendAsync(string command, CancellationToken token)
    {
        if (_writer is null || _reader is null || _process is null || _process.HasExited)
            throw new InvalidOperationException("The V2 containment broker is unavailable.");
        await _writer.WriteLineAsync(command.AsMemory(), token);
        var response = await _reader.ReadLineAsync(token);
        if (!string.Equals(response, "OK", StringComparison.Ordinal))
            throw new InvalidOperationException(response?.StartsWith("ERROR ", StringComparison.Ordinal) == true
                ? response[6..] : "The V2 containment broker rejected the operation.");
    }

    public static async Task<bool> TryRunBrokerAsync(string[] args)
    {
        var pipeArg = args.FirstOrDefault(value => value.StartsWith("--vpn-v2-wfp-broker=", StringComparison.Ordinal));
        var pathArg = args.FirstOrDefault(value => value.StartsWith("--fixed-runtime-exe=", StringComparison.Ordinal));
        if (pipeArg is null) return false;
        if (pathArg is null) return true;
        var pipeName = pipeArg[(pipeArg.IndexOf('=') + 1)..];
        string executable;
        try { executable = Encoding.UTF8.GetString(Convert.FromBase64String(pathArg[(pathArg.IndexOf('=') + 1)..])); }
        catch { return true; }
        MonkeyTV.VpnProxyProof.WfpContainmentSpike? containment = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await pipe.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("READY");
            while (await reader.ReadLineAsync() is { } command)
            {
                try
                {
                    if (command == "ENABLE")
                        containment ??= MonkeyTV.VpnProxyProof.WfpContainmentSpike.InstallMinimalIPv4Block(executable, _ => { }, completePolicy: true);
                    else if (command == "DISABLE") { containment?.Dispose(); containment = null; }
                    else if (command == "STOP") { containment?.Dispose(); containment = null; await writer.WriteLineAsync("OK"); break; }
                    else throw new InvalidOperationException("Unknown containment command.");
                    await writer.WriteLineAsync("OK");
                }
                catch (Exception ex) { await writer.WriteLineAsync("ERROR " + Sanitize(ex.Message)); }
            }
        }
        catch { }
        finally { containment?.Dispose(); }
        return true;
    }

    private static string Sanitize(string message) => message.Replace('\r', ' ').Replace('\n', ' ').Replace("\\", "/");

    private async Task DisposeConnectionAsync()
    {
        _reader?.Dispose(); _writer?.Dispose(); _pipe?.Dispose();
        _reader = null; _writer = null; _pipe = null;
        var process = Interlocked.Exchange(ref _process, null);
        if (process is not null)
        {
            try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } } catch { }
            process.Dispose();
        }
        _enabled = false;
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_writer is not null && _reader is not null && _process is { HasExited: false }) await SendAsync("STOP", CancellationToken.None); }
        catch { }
        await DisposeConnectionAsync();
    }
}
