using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace MonkeyTV.VpnProxyProof;

/// <summary>
/// Non-production proof runner for the narrowly approved Feature #0.01 exception.
/// A failed check is deliberately reported as a no-go; this harness must never be
/// interpreted as a VPN implementation or used by Monkey TV itself.
/// </summary>
public partial class MainWindow : Window
{
    private readonly List<string> _results = new();
    private readonly List<WebView2> _popupViews = new();
    private LoopbackProxy? _proxy;
    private WebView2? _view;
    private string? _resultFile;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunProofAsync();
    }

    private async Task RunProofAsync()
    {
        _proxy = new LoopbackProxy();
        _proxy.Start();
        Add($"Proof proxy listening on 127.0.0.1:{_proxy.Port}.");

        var proofData = Path.Combine(AppContext.BaseDirectory, "Data", "ProxyProof");
        Directory.CreateDirectory(proofData);
        _resultFile = Path.Combine(proofData, "results.txt");
        var options = new CoreWebView2EnvironmentOptions
        {
            // Approved only for Feature #0.01 feasibility testing. This is intentionally
            // a fixed routing boundary: no destination can change it after WebView creation.
            AdditionalBrowserArguments = $"--proxy-server=http://127.0.0.1:{_proxy.Port} --disable-quic --force-webrtc-ip-handling-policy=disable_non_proxied_udp"
        };

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, proofData, options);
            _view = new WebView2 { Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            Root.Children.Add(_view);
            await _view.EnsureCoreWebView2Async(environment);
            _view.CoreWebView2.NewWindowRequested += (_, args) => HandlePopupAsync(args, environment);
            _view.CoreWebView2.DownloadStarting += (_, args) =>
            {
                Add("Download request observed; cancelling after route observation.");
                args.Cancel = true;
            };

            await TestNavigationAndFailClosedAsync();
            await TestPopupAsync();
            await TestWebSocketAsync();
            await TestServiceWorkerAndSubresourceAsync();
            await TestDownloadAsync();
            await TestWebRtcAsync();
        }
        catch (Exception ex)
        {
            Add("HARNESS ERROR: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            foreach (var popup in _popupViews) popup.Dispose();
            _view?.Dispose();
            if (_proxy is not null) await _proxy.DisposeAsync();
            if (_proxy is not null) ReportProxyAttempts();
            if (_proxy is not null) ReportAddressFamilies();
            Add("Proof complete. Review FAILED/INCONCLUSIVE lines before any production work.");
            if (_resultFile is not null) File.WriteAllLines(_resultFile, _results);
            if (Environment.GetEnvironmentVariable("MONKEYTV_PROXY_PROOF_AUTOCLOSE") == "1") Close();
        }
    }

    private async Task TestNavigationAndFailClosedAsync()
    {
        Add("\n[Navigation] Allowing proxy route to a public HTTPS origin.");
        var allowed = await NavigateAsync("https://mdn.github.io/dom-examples/service-workers/simple-service-worker/?monkeytv-proxy-proof=" + Guid.NewGuid());
        Add("Allowed navigation result: " + allowed.Status + "; success=" + allowed.Success + ".");
        Check(allowed.Success && _proxy!.SawHost("mdn.github.io"), "HTTPS navigation uses the local proxy boundary.");

        _proxy!.AllowConnections = false;
        Add("[Fail closed] Gateway deny mode; navigating to unique remote origin.");
        var denied = await NavigateAsync("https://example.org/?monkeytv-proxy-proof=" + Guid.NewGuid());
        Add("Denied navigation result: " + denied.Status + "; success=" + denied.Success + ".");
        Check(_proxy.SawHost("example.org") && !denied.Success, "Denied gateway prevents remote navigation without an observed direct fallback.");
        _proxy.AllowConnections = true;
    }

    private async Task TestPopupAsync()
    {
        Add("\n[Popup] Opening a same-environment popup to example.net.");
        await NavigateAsync("https://mdn.github.io/dom-examples/service-workers/simple-service-worker/?popup-proof=" + Guid.NewGuid());
        await _view!.CoreWebView2.ExecuteScriptAsync("window.open('https://www.iana.org/domains/reserved?popup-proof=' + Date.now(), '_blank');");
        await Task.Delay(3000);
        Check(_proxy!.SawHost("www.iana.org"), "Popup request reaches the same fixed proxy boundary.");
    }

    private async Task TestWebSocketAsync()
    {
        Add("\n[WebSocket] Opening a secure WebSocket through the page.");
        await NavigateAsync("https://mdn.github.io/dom-examples/service-workers/simple-service-worker/?ws-proof=" + Guid.NewGuid());
        var result = await ReceivePageMessageAsync("(() => { const ws = new WebSocket('wss://ws.postman-echo.com/raw'); const done = v => { try { ws.close(); } catch {} chrome.webview.postMessage('ws:' + v); }; ws.onopen = () => done('open'); ws.onerror = () => done('error'); setTimeout(() => done('timeout'), 8000); })();");
        Add("WebSocket result: " + result);
        Check(_proxy!.SawHost("ws.postman-echo.com"), "Secure WebSocket connection is observed at the proxy boundary.");
    }

    private async Task TestServiceWorkerAndSubresourceAsync()
    {
        Add("\n[Service worker/subresource] Navigating to a public service-worker example.");
        await NavigateAsync("https://mdn.github.io/dom-examples/service-workers/simple-service-worker/?proof=" + Guid.NewGuid());
        await Task.Delay(2500);
        Check(_proxy!.SawHost("mdn.github.io"), "Service-worker test origin is observed at the proxy boundary.");
    }

    private async Task TestDownloadAsync()
    {
        Add("\n[Download] Requesting a disposable attachment and cancelling it in WebView2.");
        await NavigateAsync("https://httpbin.org/response-headers?Content-Disposition=attachment%3B%20filename%3Dmonkeytv-proof.txt&proof=" + Guid.NewGuid());
        await Task.Delay(1000);
        Check(_proxy!.SawHost("httpbin.org"), "Download-origin request is observed at the proxy boundary.");
    }

    private async Task TestWebRtcAsync()
    {
        Add("\n[WebRTC] Requesting ICE candidates with a public STUN server.");
        await NavigateAsync("https://mdn.github.io/dom-examples/service-workers/simple-service-worker/?webrtc-proof=" + Guid.NewGuid());
        var candidateJson = await ReceivePageMessageAsync("(() => { const pc = new RTCPeerConnection({iceServers:[{urls:'stun:stun.l.google.com:19302'}]}); const candidates=[]; let done=false; const finish=()=>{ if(done)return; done=true; pc.close(); chrome.webview.postMessage(JSON.stringify(candidates)); }; pc.onicecandidate=e=>{ if(e.candidate) candidates.push(e.candidate.candidate); else finish(); }; pc.createDataChannel('p'); pc.createOffer().then(o=>pc.setLocalDescription(o)).catch(finish); setTimeout(finish, 7000); })();");
        var candidates = JsonSerializer.Deserialize<string[]>(candidateJson) ?? Array.Empty<string>();
        var unsafeCandidate = candidates.FirstOrDefault(candidate =>
            candidate.Contains(" typ srflx", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains(" typ relay", StringComparison.OrdinalIgnoreCase) ||
            (candidate.Contains(" typ host", StringComparison.OrdinalIgnoreCase) && !candidate.Contains(".local", StringComparison.OrdinalIgnoreCase)));

        if (unsafeCandidate is null)
            Check(true, "WebRTC emitted no public/server-reflexive or unmasked host candidate in this run.");
        else
            Add("FAILED: WebRTC emitted a potentially bypassing candidate: " + unsafeCandidate);

        Add("WebRTC candidates observed: " + candidates.Length + ".");
        Add("INCONCLUSIVE: packet capture is still required to prove absence of UDP/QUIC egress; candidate results alone are insufficient.");
    }

    private async Task<NavigationResult> NavigateAsync(string url)
    {
        var completion = new TaskCompletionSource<NavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, args) => completion.TrySetResult(new NavigationResult(args.IsSuccess, args.WebErrorStatus.ToString()));
        _view!.CoreWebView2.NavigationCompleted += handler;
        _view.CoreWebView2.Navigate(url);
        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            return new NavigationResult(false, "Timeout");
        }
        finally
        {
            _view.CoreWebView2.NavigationCompleted -= handler;
        }
    }

    private async Task<string> ReceivePageMessageAsync(string script)
    {
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2WebMessageReceivedEventArgs>? handler = null;
        handler = (_, args) => result.TrySetResult(args.TryGetWebMessageAsString());
        _view!.CoreWebView2.WebMessageReceived += handler;
        try
        {
            await _view.CoreWebView2.ExecuteScriptAsync(script);
            return await result.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException) { return "timeout"; }
        finally { _view.CoreWebView2.WebMessageReceived -= handler; }
    }

    private async void HandlePopupAsync(CoreWebView2NewWindowRequestedEventArgs args, CoreWebView2Environment environment)
    {
        var deferral = args.GetDeferral();
        try
        {
            var popup = new WebView2 { Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            _popupViews.Add(popup);
            Root.Children.Add(popup);
            await popup.EnsureCoreWebView2Async(environment);
            args.NewWindow = popup.CoreWebView2;
        }
        catch (Exception ex)
        {
            Add("Popup setup failed: " + ex.GetType().Name);
        }
        finally { deferral.Complete(); }
    }

    private void ReportAddressFamilies()
    {
        var allAddresses = _proxy!.Attempts.SelectMany(attempt => attempt.ResolvedAddresses).Distinct().ToArray();
        Add("\nResolved upstream addresses: " + string.Join(", ", allAddresses.Select(address => address.ToString())));
        Add(allAddresses.Any(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            ? "IPv6 DNS results were observed by the gateway."
            : "INCONCLUSIVE: no IPv6 upstream address was used in this network run.");
    }

    private void ReportProxyAttempts()
    {
        Add("\nProxy attempts:");
        foreach (var attempt in _proxy!.Attempts)
            Add($"- {attempt.Target}; connect={attempt.IsConnect}; allowed={attempt.Allowed}; {attempt.Detail ?? "ok"}");
    }

    private void Check(bool passed, string detail) => Add((passed ? "PASS: " : "FAILED: ") + detail);
    private void Add(string line)
    {
        _results.Add(line);
        Output.Text = string.Join(Environment.NewLine, _results);
        Output.ScrollToEnd();
        Debug.WriteLine(line);
    }

    private sealed record NavigationResult(bool Success, string Status);
}
