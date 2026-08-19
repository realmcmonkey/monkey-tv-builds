namespace MonkeyTV;

public sealed class CurrentLocationRouter : IAsyncDisposable
{
    private LoopbackLocationGateway? _gateway;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    public CurrentLocationState State { get; private set; } = CurrentLocationState.Direct;
    public NordLocation? ActiveLocation { get; private set; }
    public NordLocation? RequestedLocation { get; private set; }
    public string DisplayName => State switch { CurrentLocationState.Direct => "Direct", CurrentLocationState.Connecting => "Connecting…", CurrentLocationState.Failed => "Connection failed", _ => ActiveLocation?.Name ?? "Direct" };
    public event EventHandler? Changed;
    public int EnsureGateway()
    {
        if (_gateway is null) { _gateway = new LoopbackLocationGateway(); _gateway.Start(); }
        return _gateway.Port;
    }
    public async Task SelectDirectAsync()
    {
        await _switchLock.WaitAsync(); try { if (_gateway is not null) _gateway.SetDirect(); ActiveLocation = null; RequestedLocation = null; State = CurrentLocationState.Direct; Changed?.Invoke(this, EventArgs.Empty); }
        finally { _switchLock.Release(); }
    }
    public async Task<bool> SelectNordAsync(NordLocation location, NordCredentials credentials, CancellationToken token = default)
    {
        await _switchLock.WaitAsync(token);
        try
        {
            var gateway = GetGateway(); gateway.SetDeny(); ActiveLocation = null; RequestedLocation = location; State = CurrentLocationState.Connecting; Changed?.Invoke(this, EventArgs.Empty);
            gateway.SetNord(location, credentials);
            try { await gateway.VerifyAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token); ActiveLocation = location; State = CurrentLocationState.Connected; Changed?.Invoke(this, EventArgs.Empty); return true; }
            catch { gateway.SetDeny(); State = CurrentLocationState.Failed; Changed?.Invoke(this, EventArgs.Empty); return false; }
        }
        finally { _switchLock.Release(); }
    }
    public void Block() { _gateway?.SetDeny(); }
    private LoopbackLocationGateway GetGateway() { EnsureGateway(); return _gateway!; }
    public async ValueTask DisposeAsync() { if (_gateway is not null) await _gateway.DisposeAsync(); _switchLock.Dispose(); }
}
