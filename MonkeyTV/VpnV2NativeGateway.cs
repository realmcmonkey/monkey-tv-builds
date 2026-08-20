using System.Diagnostics;

namespace MonkeyTV;

internal sealed class VpnV2NativeGateway : IAsyncDisposable
{
    private Process? _process;
    private Task? _stdoutPump;
    private Task? _stderrPump;
    public int Port { get; private set; }
    public VpnV2CandidateFailure Failure { get; private set; }
    public event EventHandler? Exited;

    public async Task StartAsync(VpnV2ImportedConfig config, NordCredentials credentials, CancellationToken token)
    {
        if (_process is not null) throw new InvalidOperationException("The experimental gateway is already running.");
        var helper = Path.Combine(PortablePaths.VpnV2RequirementsDirectory, "MonkeyTV.UserSpaceOpenVpnProof.exe");
        VpnV2Diagnostics.Write("native-helper", $"selected={config.Country}/{config.ServerName} managed-config={VpnV2Diagnostics.ManagedConfigPath(config)} exists={File.Exists(config.FullPath)}");
        VpnV2Diagnostics.Dependency("helper", helper);
        VpnV2Diagnostics.Dependency("OpenSSL crypto", Path.Combine(Path.GetDirectoryName(helper)!, "libcrypto-3-x64.dll"));
        VpnV2Diagnostics.Dependency("OpenSSL ssl", Path.Combine(Path.GetDirectoryName(helper)!, "libssl-3-x64.dll"));
        VpnV2Diagnostics.Dependency("lz4", Path.Combine(Path.GetDirectoryName(helper)!, "lz4.dll"));
        if (!File.Exists(helper)) throw new FileNotFoundException("The experimental VPN Location V2 native helper is not installed.", helper);
        if (!File.Exists(config.FullPath)) throw new FileNotFoundException("The selected managed Nord configuration is missing.", config.FullPath);
        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // This mode emits only sanitized lifecycle stages on stderr. Credentials still
        // enter via standard input and never appear in a command line or a log.
        start.ArgumentList.Add("--proxy-diagnostics");
        start.ArgumentList.Add(config.FullPath);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) =>
        {
            VpnV2Diagnostics.Write("native-helper-exit", $"exit-code={SafeExitCode(process)} ready={ready.Task.IsCompletedSuccessfully}");
            ready.TrySetException(new InvalidOperationException("The experimental native gateway exited before becoming ready (" + Failure + ")."));
            Exited?.Invoke(this, EventArgs.Empty);
        };
        if (!process.Start()) throw new InvalidOperationException("The experimental native gateway could not start.");
        _process = process;
        Failure = VpnV2CandidateFailure.None;
        VpnV2Diagnostics.Write("native-helper-launch", "process started successfully");
        _stdoutPump = PumpAsync(process.StandardOutput, "helper-stdout", ready);
        _stderrPump = PumpAsync(process.StandardError, "helper-stderr", ready);
        await process.StandardInput.WriteLineAsync(credentials.Username.AsMemory(), token);
        await process.StandardInput.WriteLineAsync(credentials.Password.AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
        VpnV2Diagnostics.Write("credential-retrieval", "credentials supplied to native helper through standard input");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        Port = await ready.Task.WaitAsync(timeout.Token);
        VpnV2Diagnostics.Write("route-gateway", $"native loopback proxy ready port={Port}");
    }

    public async ValueTask DisposeAsync()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("STOP");
                await process.StandardInput.FlushAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        }
        finally { process.Dispose(); Port = 0; }
    }

    private async Task PumpAsync(StreamReader reader, string source, TaskCompletionSource<int> ready)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                ClassifyFailure(line);
                if (line.StartsWith("PROXY_READY ", StringComparison.Ordinal) && int.TryParse(line[12..], out var port) && port is > 0 and <= 65535)
                {
                    VpnV2Diagnostics.Write("native-helper-ready", $"loopback-port={port}");
                    ready.TrySetResult(port);
                }
                else
                    VpnV2Diagnostics.Write(source, line);
            }
        }
        catch (Exception ex)
        {
            VpnV2Diagnostics.Write(source, $"pump-failed={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }

    private void ClassifyFailure(string line)
    {
        if (line.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase)) Failure = VpnV2CandidateFailure.Authentication;
        else if (line.Contains("COMPRESS_ERROR", StringComparison.OrdinalIgnoreCase)) Failure = VpnV2CandidateFailure.Compression;
        else if (line.Contains("COUNTRY_CONSENSUS_DISAGREEMENT", StringComparison.OrdinalIgnoreCase)) Failure = VpnV2CandidateFailure.CountryMismatch;
        else if (line.Contains("TRANSPORT_ERROR", StringComparison.OrdinalIgnoreCase) || line.Contains("Timeout before transport", StringComparison.OrdinalIgnoreCase)) Failure = VpnV2CandidateFailure.Transport;
        else if (line.Contains("READINESS_", StringComparison.OrdinalIgnoreCase) && line.Contains("error=1", StringComparison.OrdinalIgnoreCase)) Failure = VpnV2CandidateFailure.TunnelEstablishedButUnusable;
    }
}

public enum VpnV2CandidateFailure { None, Authentication, Compression, CountryMismatch, Transport, TunnelEstablishedButUnusable, Unknown }
