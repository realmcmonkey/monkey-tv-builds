namespace MonkeyTV;

public enum LocationRouteProgressStage { Switching, Connecting, TryingAnotherConnection, Verifying, Connected, Failed, Direct }

public sealed record LocationRouteProgress(LocationRouteProgressStage Stage, string? LocationName = null);

public sealed class CurrentLocationRouter : IAsyncDisposable
{
    private LoopbackLocationGateway? _gateway;
    private VpnV2NativeGateway? _vpnV2;
    private readonly VpnV2WfpBroker _vpnV2Containment = new();
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    public CurrentLocationState State { get; private set; } = CurrentLocationState.Direct;
    public NordLocation? ActiveLocation { get; private set; }
    public VpnV2ImportedConfig? ActiveVpnV2Location { get; private set; }
    public NordLocation? RequestedLocation { get; private set; }
    public bool VpnV2ContainmentFailed { get; private set; }
    public bool HasAuthenticatedVpnV2ThisSession { get; private set; }
    public VpnV2CandidateFailure LastVpnV2Failure { get; private set; }
    public string DisplayName => State switch { CurrentLocationState.Direct => "Direct", CurrentLocationState.Connecting => "Connecting…", CurrentLocationState.Failed => "Connection failed", _ => ActiveVpnV2Location?.Country ?? ActiveLocation?.Country ?? "Direct" };
    public event EventHandler? Changed;
    public event EventHandler<LocationRouteProgress>? ProgressChanged;

    private void ReportProgress(LocationRouteProgressStage stage, string? locationName = null) =>
        ProgressChanged?.Invoke(this, new LocationRouteProgress(stage, locationName));
    public int EnsureGateway()
    {
        if (_gateway is null) { _gateway = new LoopbackLocationGateway(); _gateway.Start(); }
        return _gateway.Port;
    }
    public async Task<bool> SelectDirectAsync()
    {
        await _switchLock.WaitAsync();
        try
        {
            var gateway = GetGateway();
            ReportProgress(LocationRouteProgressStage.Switching, "Direct");
            VpnV2Diagnostics.Write("direct", "begin: gateway blocked before V2 teardown");
            gateway.SetDeny();
            try
            {
                await StopVpnV2Async();
                await _vpnV2Containment.DisableAsync();
                gateway.SetDirect();
                ReportProgress(LocationRouteProgressStage.Verifying, "Direct");
                VpnV2Diagnostics.Write("direct", "gateway set to Direct; verification starting");
                await gateway.VerifyAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
                VpnV2Diagnostics.Write("direct", "verification passed");
                ActiveLocation = null; ActiveVpnV2Location = null; RequestedLocation = null;
                State = CurrentLocationState.Direct; Changed?.Invoke(this, EventArgs.Empty); ReportProgress(LocationRouteProgressStage.Direct); return true;
            }
            catch (Exception ex)
            {
                VpnV2Diagnostics.Write("direct", $"verification failed={ex.GetType().Name}: {ex.Message}");
                gateway.SetDeny(); ActiveLocation = null; ActiveVpnV2Location = null; RequestedLocation = null;
                State = CurrentLocationState.Failed; Changed?.Invoke(this, EventArgs.Empty); ReportProgress(LocationRouteProgressStage.Failed, "Direct"); return false;
            }
        }
        finally { _switchLock.Release(); }
    }
    public async Task<bool> SelectNordAsync(NordLocation location, NordCredentials credentials, CancellationToken token = default)
    {
        await _switchLock.WaitAsync(token);
        try
        {
            var gateway = GetGateway(); gateway.SetDeny(); ActiveLocation = null; ActiveVpnV2Location = null; RequestedLocation = location; State = CurrentLocationState.Connecting; Changed?.Invoke(this, EventArgs.Empty);
            ReportProgress(LocationRouteProgressStage.Switching, location.Country);
            try
            {
                await StopVpnV2Async(); await _vpnV2Containment.DisableAsync(token);
                gateway.SetNord(location, credentials);
                ReportProgress(LocationRouteProgressStage.Connecting, location.Country);
                ReportProgress(LocationRouteProgressStage.Verifying, location.Country);
                await gateway.VerifyAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token);
                ActiveLocation = location; State = CurrentLocationState.Connected; Changed?.Invoke(this, EventArgs.Empty);
                ReportProgress(LocationRouteProgressStage.Connected, location.Country); return true;
            }
            catch
            {
                gateway.SetDeny(); State = CurrentLocationState.Failed; Changed?.Invoke(this, EventArgs.Empty);
                ReportProgress(LocationRouteProgressStage.Failed, location.Country); return false;
            }
        }
        finally { _switchLock.Release(); }
    }
    public async Task<bool> SelectVpnV2Async(VpnV2ImportedConfig config, NordCredentials credentials, string fixedRuntimeExecutable, CancellationToken token = default)
    {
        await _switchLock.WaitAsync(token);
        try
        {
            var gateway = GetGateway(); gateway.SetDeny(); await StopVpnV2Async();
            VpnV2Diagnostics.Write("vpn-v2", $"begin selected={config.Country}/{config.ServerName} fixed-runtime={Path.GetFileName(fixedRuntimeExecutable)} exists={File.Exists(fixedRuntimeExecutable)}");
            ActiveLocation = null; ActiveVpnV2Location = null; RequestedLocation = null; VpnV2ContainmentFailed = false; State = CurrentLocationState.Connecting; Changed?.Invoke(this, EventArgs.Empty);
            ReportProgress(LocationRouteProgressStage.Switching, config.Country);
            LastVpnV2Failure = VpnV2CandidateFailure.None;
            VpnV2NativeGateway? native = null;
            try
            {
                try { await _vpnV2Containment.EnableAsync(fixedRuntimeExecutable, token); VpnV2Diagnostics.Write("containment", "WFP policy enabled"); }
                catch (Exception ex)
                {
                    VpnV2ContainmentFailed = true;
                    AppLog.Write("vpn-v2-wfp", ex.Message);
                    VpnV2Diagnostics.Write("containment", $"WFP enable failed={ex.GetType().Name}: {ex.Message}");
                    throw;
                }
                native = new VpnV2NativeGateway();
                native.Exited += VpnV2_Exited;
                _vpnV2 = native;
                ReportProgress(LocationRouteProgressStage.Connecting, config.Country);
                await native.StartAsync(config, credentials, token);
                HasAuthenticatedVpnV2ThisSession = true;
                gateway.SetVpnV2(native.Port);
                ReportProgress(LocationRouteProgressStage.Verifying, config.Country);
                VpnV2Diagnostics.Write("route-gateway", "V2 proxy route selected; verification starting");
                await gateway.VerifyAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token);
                VpnV2Diagnostics.Write("route-gateway", "verification passed");
                ActiveVpnV2Location = config; State = CurrentLocationState.Connected; Changed?.Invoke(this, EventArgs.Empty);
                ReportProgress(LocationRouteProgressStage.Connected, config.Country); return true;
            }
            catch (Exception ex)
            {
                LastVpnV2Failure = native?.Failure ?? VpnV2CandidateFailure.Unknown;
                VpnV2Diagnostics.Write("vpn-v2", $"failed stage/error={ex.GetType().Name}: {ex.Message}; candidate-result={LastVpnV2Failure}");
                gateway.SetDeny(); State = CurrentLocationState.Failed; Changed?.Invoke(this, EventArgs.Empty); ReportProgress(LocationRouteProgressStage.Failed, config.Country);
                await StopVpnV2Async(); return false;
            }
        }
        finally { _switchLock.Release(); }
    }
    private void VpnV2_Exited(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _vpnV2)) return;
        VpnV2Diagnostics.Write("native-helper-exit", "gateway helper exited while V2 was active; gateway denied");
        _gateway?.SetDeny(); State = CurrentLocationState.Failed; Changed?.Invoke(this, EventArgs.Empty);
        ReportProgress(LocationRouteProgressStage.Failed, ActiveVpnV2Location?.Country);
    }
    private async Task StopVpnV2Async()
    {
        var native = _vpnV2; _vpnV2 = null;
        if (native is null) return;
        VpnV2Diagnostics.Write("vpn-v2", "stopping previous native helper");
        native.Exited -= VpnV2_Exited;
        await native.DisposeAsync();
    }
    public void Block() { _gateway?.SetDeny(); }
    public void ContinueVpnV2Fallback(string? country = null)
    {
        // A rejected candidate is not the final route result. Keep the browser
        // blocked and the UI honest while the caller tries another server.
        State = CurrentLocationState.Connecting;
        Changed?.Invoke(this, EventArgs.Empty);
        ReportProgress(LocationRouteProgressStage.TryingAnotherConnection, country);
    }
    private LoopbackLocationGateway GetGateway() { EnsureGateway(); return _gateway!; }
    public async ValueTask DisposeAsync() { await StopVpnV2Async(); await _vpnV2Containment.DisposeAsync(); if (_gateway is not null) await _gateway.DisposeAsync(); _switchLock.Dispose(); }
}
