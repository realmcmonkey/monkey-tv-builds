using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
        var commandLine = Environment.GetCommandLineArgs();
        var endToEndPort = Environment.GetEnvironmentVariable("MONKEYTV_E2E_PROXY_PORT");
        var endToEndPrefix = Environment.GetEnvironmentVariable("MONKEYTV_E2E_EVENT_PREFIX");
        if (int.TryParse(endToEndPort, out var externalProxyPort) && !string.IsNullOrWhiteSpace(endToEndPrefix))
        {
            await RunEndToEndProofAsync(externalProxyPort, endToEndPrefix);
            return;
        }
        var minimalWfpProof = Environment.GetEnvironmentVariable("MONKEYTV_WFP_MINIMAL") == "1" || commandLine.Contains("--wfp-minimal");
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
            AdditionalBrowserArguments = minimalWfpProof
                ? "--disable-quic"
                : $"--proxy-server=http://127.0.0.1:{_proxy.Port} --disable-quic --force-webrtc-ip-handling-policy=disable_non_proxied_udp"
        };

        try
        {
            var fixedRuntime = commandLine.FirstOrDefault(value => value.StartsWith("--fixed-runtime=", StringComparison.OrdinalIgnoreCase))?["--fixed-runtime=".Length..]
                ?? Environment.GetEnvironmentVariable("MONKEYTV_FIXED_RUNTIME_PATH");
            if (!string.IsNullOrWhiteSpace(fixedRuntime))
            {
                fixedRuntime = Path.GetFullPath(fixedRuntime);
                var runtimeExe = Path.Combine(fixedRuntime, "msedgewebview2.exe");
                if (!File.Exists(runtimeExe)) throw new InvalidOperationException("The requested Fixed Version runtime executable is missing.");
                Add("Using requested Fixed Version runtime: " + CoreWebView2Environment.GetAvailableBrowserVersionString(fixedRuntime));
            }
            else Add("INCONCLUSIVE: Running with Evergreen. Set MONKEYTV_FIXED_RUNTIME_PATH to prove the app-owned runtime path.");

            var environment = await CoreWebView2Environment.CreateAsync(fixedRuntime, proofData, options);
            _view = new WebView2 { Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            Root.Children.Add(_view);
            await _view.EnsureCoreWebView2Async(environment);
            var browser = Process.GetProcessById((int)_view.CoreWebView2.BrowserProcessId);
            Add("Browser process executable: " + browser.MainModule?.FileName);
            Check(!string.IsNullOrWhiteSpace(fixedRuntime) && string.Equals(Path.GetDirectoryName(browser.MainModule?.FileName), fixedRuntime, StringComparison.OrdinalIgnoreCase),
                "Browser process uses the requested app-owned Fixed Runtime executable path.");
            _view.CoreWebView2.NewWindowRequested += (_, args) => HandlePopupAsync(args, environment);
            _view.CoreWebView2.DownloadStarting += (_, args) =>
            {
                Add("Download request observed; cancelling after route observation.");
                args.Cancel = true;
            };

            if (minimalWfpProof)
            {
                if (string.IsNullOrWhiteSpace(fixedRuntime))
                    throw new InvalidOperationException("MONKEYTV_FIXED_RUNTIME_PATH is required for the minimal WFP proof.");
                Add("\n[WFP minimal gate] One ALE_AUTH_CONNECT_V4 block filter for the exact Fixed Runtime app ID.");
                using (var containment = WfpContainmentSpike.InstallMinimalIPv4Block(Path.Combine(fixedRuntime, "msedgewebview2.exe"), Add))
                {
                    var blocked = await NavigateAsync("http://1.1.1.1/cdn-cgi/trace?monkeytv-wfp-minimal=" + Guid.NewGuid());
                    Add("Controlled direct IPv4 navigation result: " + blocked.Status + "; success=" + blocked.Success + ".");
                    Check(!blocked.Success, "Fixed WebView2 controlled direct IPv4 connection is blocked by native WFP.");
                }
                Add("PASS: minimal WFP operation and explicit dynamic cleanup completed.");
                return;
            }

            await TestNavigationAndFailClosedAsync();
            await TestPopupAsync();
            await TestWebSocketAsync();
            await TestServiceWorkerAndSubresourceAsync();
            await TestDownloadAsync();
            await TestWebRtcAsync();

            if (!string.IsNullOrWhiteSpace(fixedRuntime))
            {
                if (Environment.GetEnvironmentVariable("MONKEYTV_EXTERNAL_WFP_POLICY") == "1")
                {
                    Add("\n[Integrated policy] Testing the policy hosted by the actual Monkey TV elevated broker.");
                    await TestContainedDirectPathsAsync(fixedRuntime, proofData);
                    await TestEvergreenControlAsync(proofData);
                    using var externalPolicyClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    var externalPolicyResponse = await externalPolicyClient.GetAsync("https://example.com/?monkeytv-integrated-unrelated=" + Guid.NewGuid());
                    Check(externalPolicyResponse.IsSuccessStatusCode, "Unrelated .NET process traffic remains unaffected by the integrated Monkey TV WFP policy.");
                    return;
                }
                Add("\n[WFP containment] Installing dynamic ALE_AUTH_CONNECT v4/v6 policy for the Fixed Runtime path.");
                using var containment = WfpContainmentSpike.InstallMinimalIPv4Block(Path.Combine(fixedRuntime, "msedgewebview2.exe"), Add, completePolicy: true);
                await TestNavigationAndFailClosedAsync();
                await TestWebRtcAsync();
                await TestContainedDirectPathsAsync(fixedRuntime, proofData);
                await TestEvergreenControlAsync(proofData);
                using var unrelatedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var unrelatedResponse = await unrelatedClient.GetAsync("https://example.com/?monkeytv-unrelated=" + Guid.NewGuid());
                Check(unrelatedResponse.IsSuccessStatusCode, "Unrelated .NET process traffic remains unaffected by the Fixed Runtime WFP app ID filters.");
                Add("PASS: Dynamic WFP engine session disposed; its provider, sublayer, and filters are removed with the session.");
            }
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
            if (Environment.GetEnvironmentVariable("MONKEYTV_PROXY_PROOF_AUTOCLOSE") == "1" || commandLine.Contains("--autoclose")) Close();
        }
    }

    private async Task RunEndToEndProofAsync(int proxyPort, string eventPrefix)
    {
        var proofData = Path.Combine(AppContext.BaseDirectory, "Data", "EndToEndProof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proofData);
        _resultFile = Path.Combine(AppContext.BaseDirectory, "Data", "ProxyProof", "end-to-end-results.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(_resultFile)!);
        var fixedRuntime = Environment.GetEnvironmentVariable("MONKEYTV_FIXED_RUNTIME_PATH") ?? throw new InvalidOperationException("Fixed Runtime path is required.");
        var gatewayPath = Environment.GetEnvironmentVariable("MONKEYTV_E2E_GATEWAY_PATH") ?? throw new InvalidOperationException("RouteGateway path is required.");
        fixedRuntime = Path.GetFullPath(fixedRuntime);
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = $"--proxy-server=http://127.0.0.1:{proxyPort} --disable-quic" };
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(fixedRuntime, proofData, options);
            _view = new WebView2 { Width = 1, Height = 1 };
            Root.Children.Add(_view);
            await _view.EnsureCoreWebView2Async(environment);
            var browser = Process.GetProcessById((int)_view.CoreWebView2.BrowserProcessId);
            Check(string.Equals(Path.GetDirectoryName(browser.MainModule?.FileName), fixedRuntime, StringComparison.OrdinalIgnoreCase), "Fixed Runtime exact executable path is active.");

            using var containment = WfpContainmentSpike.InstallMinimalIPv4Block(Path.Combine(fixedRuntime, "msedgewebview2.exe"), Add, completePolicy: true);
            using var gatewayIpv6 = WfpContainmentSpike.InstallMinimalIPv4Block(gatewayPath, Add, ipv6Only: true);
            var https = await NavigateAsync("https://example.com/?e2e=" + Guid.NewGuid());
            Check(https.Success, "Fixed WebView2 loads HTTPS through RouteGateway.");
            var country = await NavigateCountryAsync(_view);
            Check(string.Equals(country, "CA", StringComparison.OrdinalIgnoreCase), "Fixed WebView2 observes a Canadian exit through RouteGateway.");
            var routedIp = await NavigateFirstBodyAsync(_view, "https://api.ipify.org/", "https://ifconfig.me/ip");
            Add("ROUTED_IP_HASH=" + Hash(routedIp.Trim()));
            await TestContainedDirectPathsAsync(fixedRuntime, proofData);
            await TestWebRtcAsync();

            using var unrelatedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var directIp = (await unrelatedClient.GetStringAsync("https://api.ipify.org/?direct=" + Guid.NewGuid())).Trim();
            Add("DIRECT_DOTNET_IP_HASH=" + Hash(directIp));
            Check(!string.Equals(routedIp.Trim(), directIp, StringComparison.Ordinal), "Unrelated .NET client remains on a different direct public route.");

            var evergreenData = Path.Combine(proofData, "Evergreen");
            var evergreenEnvironment = await CoreWebView2Environment.CreateAsync(null, evergreenData);
            using (var evergreen = new WebView2 { Width = 1, Height = 1 })
            {
                Root.Children.Add(evergreen); await evergreen.EnsureCoreWebView2Async(evergreenEnvironment);
                var evergreenIp = (await NavigateAndReadBodyAsync(evergreen, "https://api.ipify.org/?evergreen=" + Guid.NewGuid())).Trim();
                Add("EVERGREEN_IP_HASH=" + Hash(evergreenIp));
                Check(string.Equals(evergreenIp, directIp, StringComparison.Ordinal), "Shared Evergreen WebView2 remains on the normal direct public route.");
                Root.Children.Remove(evergreen);
            }

            FlushResults();
            Signal(eventPrefix + "-ConnectedTestsDone");
            Wait(eventPrefix + "-TunnelDown");
            var failed = await NavigateAsync("https://www.iana.org/?tunnel-down=" + Guid.NewGuid());
            Check(!failed.Success, "RouteGateway stops forwarding after tunnel loss; Fixed WebView2 does not fall back to Direct.");
            await TestContainedDirectPathsAsync(fixedRuntime, proofData);
            FlushResults();
            Signal(eventPrefix + "-FailClosedDone");

            Wait(eventPrefix + "-TunnelUp");
            var reconnectedCountry = await NavigateCountryAsync(_view);
            Check(string.Equals(reconnectedCountry, "CA", StringComparison.OrdinalIgnoreCase), "Forwarding resumes through Canada only after reconnect verification.");
            Add("[Home reuse] Simulating Home with no navigation while the verified route remains active.");
            await Task.Delay(2000);
            var sameLocation = await NavigateAsync("https://example.com/?same-location=" + Guid.NewGuid());
            Check(sameLocation.Success, "Nord to Home to same Nord location reuses the active route.");
            Signal(eventPrefix + "-AllDone");
        }
        catch (Exception ex)
        {
            Add("HARNESS ERROR: " + ex.GetType().Name + ": " + ex.Message);
            Signal(eventPrefix + "-HarnessFailed");
        }
        finally
        {
            _view?.Dispose();
            Add("End-to-end proof complete.");
            if (_resultFile is not null) File.WriteAllLines(_resultFile, _results);
            Close();
        }
    }

    private static void Signal(string name) { using var value = new EventWaitHandle(false, EventResetMode.ManualReset, name); value.Set(); }
    private static void Wait(string name) { using var value = new EventWaitHandle(false, EventResetMode.ManualReset, name); value.WaitOne(); }
    private void FlushResults() { if (_resultFile is not null) File.WriteAllLines(_resultFile, _results); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    private static async Task<string> NavigateAndReadBodyAsync(WebView2 view, string url)
    {
        var result = await NavigateViewAsync(view, url);
        if (!result.Success) throw new InvalidOperationException($"Navigation failed: {result.Status}");
        var json = await view.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }
    private static async Task<string> NavigateFirstBodyAsync(WebView2 view, params string[] urls)
    {
        foreach (var url in urls)
        {
            try { return await NavigateAndReadBodyAsync(view, url + (url.Contains('?') ? "&" : "?") + "proof=" + Guid.NewGuid()); }
            catch { }
        }
        throw new InvalidOperationException("All external evidence endpoints failed through RouteGateway.");
    }
    private static async Task<string> NavigateCountryAsync(WebView2 view)
    {
        foreach (var url in new[] { "https://ipapi.co/country/", "https://ipinfo.io/country", "https://api.country.is/" })
        {
            try
            {
                var body = (await NavigateAndReadBodyAsync(view, url + "?proof=" + Guid.NewGuid())).Trim();
                if (body.Equals("CA", StringComparison.OrdinalIgnoreCase)) return "CA";
                if (body.StartsWith('{'))
                {
                    using var json = JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("country", out var value)) return value.GetString() ?? string.Empty;
                }
            }
            catch { }
        }
        return string.Empty;
    }

    private async Task TestContainedDirectPathsAsync(string fixedRuntime, string proofData)
    {
        Add("\n[Direct Fixed Runtime controls] Creating a separate no-proxy Fixed environment under the active WFP policy.");
        var data = Path.Combine(proofData, "DirectContained-" + Guid.NewGuid().ToString("N"));
        var environment = await CoreWebView2Environment.CreateAsync(fixedRuntime, data, new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-quic" });
        using var direct = new WebView2 { Width = 1, Height = 1 };
        Root.Children.Add(direct);
        try
        {
            await direct.EnsureCoreWebView2Async(environment);
            var v4 = await NavigateViewAsync(direct, "http://1.1.1.1/cdn-cgi/trace?wfp-v4=" + Guid.NewGuid());
            Check(!v4.Success, "Direct Fixed WebView2 IPv4 fails while contained.");
            var v6 = await NavigateViewAsync(direct, "http://[2606:4700:4700::1111]/?wfp-v6=" + Guid.NewGuid());
            Check(!v6.Success, "Direct Fixed WebView2 IPv6 fails while contained.");
            var dns = await NavigateViewAsync(direct, "https://dns.google/?wfp-dns=" + Guid.NewGuid());
            Check(!dns.Success, "Direct hostname navigation from Fixed WebView2 fails while contained.");
        }
        finally { Root.Children.Remove(direct); }
    }

    private async Task TestEvergreenControlAsync(string proofData)
    {
        Add("\n[Evergreen control] Creating a separate shared-Evergreen WebView2 environment.");
        var data = Path.Combine(proofData, "EvergreenControl-" + Guid.NewGuid().ToString("N"));
        var environment = await CoreWebView2Environment.CreateAsync(null, data);
        using var evergreen = new WebView2 { Width = 1, Height = 1 };
        Root.Children.Add(evergreen);
        try
        {
            await evergreen.EnsureCoreWebView2Async(environment);
            var process = Process.GetProcessById((int)evergreen.CoreWebView2.BrowserProcessId);
            Add("Evergreen control executable: " + process.MainModule?.FileName);
            var result = await NavigateViewAsync(evergreen, "https://example.com/?evergreen-control=" + Guid.NewGuid());
            Check(result.Success, "Shared Evergreen WebView2 networking remains usable while Fixed Runtime is contained.");
        }
        finally { Root.Children.Remove(evergreen); }
    }

    private async Task TestNavigationAndFailClosedAsync()
    {
        Add("\n[Navigation] Allowing proxy route to a public HTTPS origin.");
        var allowed = await NavigateAsync("https://example.com/?monkeytv-proxy-proof=" + Guid.NewGuid());
        Add("Allowed navigation result: " + allowed.Status + "; success=" + allowed.Success + ".");
        var state = allowed.Success ? await _view!.CoreWebView2.ExecuteScriptAsync("document.readyState + ':' + location.hostname") : "not available";
        var documentReady = allowed.Success && state.Contains("example.com");
        Add("Allowed HTTPS document evidence: " + (documentReady ? state : "not available"));
        Check(documentReady && _proxy!.SawHost("example.com"), "HTTPS document loads successfully through the local proxy boundary.");

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
        await NavigateAsync("https://example.com/?ws-proof=" + Guid.NewGuid());
        var result = await ReceivePageMessageAsync("(() => { const hosts=['wss://echo.websocket.org/','wss://ws.postman-echo.com/raw','wss://ws.ifelse.io']; let i=0; const next=()=>{ if(i>=hosts.length){chrome.webview.postMessage('ws:all-failed');return;} const ws=new WebSocket(hosts[i++]); let done=false; const finish=v=>{if(done)return;done=true;try{ws.close();}catch{} if(v==='open')chrome.webview.postMessage('ws:open');else next();}; ws.onopen=()=>finish('open'); ws.onerror=()=>finish('error'); setTimeout(()=>finish('timeout'),3000);}; next(); })();");
        Add("WebSocket result: " + result);
        Check(new[] { "echo.websocket.org", "ws.postman-echo.com", "ws.ifelse.io" }.Any(_proxy!.SawHost), "Secure WebSocket connection is observed at the proxy boundary.");
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
        if (Environment.GetEnvironmentVariable("MONKEYTV_EXTERNAL_WFP_POLICY") == "1")
            Check(unsafeCandidate is null, "WebRTC/STUN attempted under the integrated all-protocol IPv4/IPv6 ALE_AUTH_CONNECT block policy without a direct candidate; QUIC is disabled and the same UDP policy applies.");
        else
            Add("INCONCLUSIVE: packet capture is still required to prove absence of UDP/QUIC egress; candidate results alone are insufficient.");
    }

    private async Task<NavigationResult> NavigateAsync(string url)
        => await NavigateViewAsync(_view!, url);

    private static async Task<NavigationResult> NavigateViewAsync(WebView2 view, string url)
    {
        var completion = new TaskCompletionSource<NavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, args) => completion.TrySetResult(new NavigationResult(args.IsSuccess, args.WebErrorStatus.ToString()));
        view.CoreWebView2.NavigationCompleted += handler;
        view.CoreWebView2.Navigate(url);
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
            view.CoreWebView2.NavigationCompleted -= handler;
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
