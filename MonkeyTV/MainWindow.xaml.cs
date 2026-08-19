using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using System.Text.Json;

namespace MonkeyTV;

public partial class MainWindow : Window
{
    private MonkeySettings _settings;
    private readonly SettingsStore _store;
    private WebView2? _webView;
    private CoreWebView2Environment? _browserEnvironment;
    private PopupWebViewWindow? _popupWindow;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _serviceUiTimer;
    private readonly DispatcherTimer _cacheBudgetTimer;
    private readonly DispatcherTimer _launchTimeoutTimer;
    private readonly ServiceControlsWindow _serviceControls;
    private readonly CurrentLocationRouter _locationRouter = new();
    private long _browserGeneration;
    private Guid? _activeServiceId;
    private bool _isBrowserLaunching;
    private bool _appFullscreen;
    private bool _websiteFullscreen;
    private bool _shellFullscreen;
    private bool _cursorHidden;
    private DateTime _lastPointerActivity = DateTime.UtcNow;
    private NativePoint _lastPointerPosition;
    private WindowStyle _previousStyle;
    private WindowState _previousState;
    private ResizeMode _previousResizeMode;
    private bool _previousTopmost;
    private Rect _previousBounds;
    private bool _browserProcessFailed;
    private bool _closing;
    private bool _allowClose;
    private string? _defaultUserAgent;
    private bool _launchTransitionActive;
    private Guid? _launchServiceId;
    private ulong? _launchNavigationId;
    private long _homeSuspensionGeneration;
    private bool _cacheBudgetCheckRunning;
    private static readonly TimeSpan ServiceLaunchTimeout = TimeSpan.FromSeconds(45);

    public MainWindow(MonkeySettings settings, SettingsStore store)
    {
        InitializeComponent(); _settings = settings; _store = store;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt");
        _clockTimer.Start();
        _serviceControls = new ServiceControlsWindow();
        _serviceControls.BackRequested += (_, _) => BrowserBack_Click(this, new RoutedEventArgs());
        _serviceControls.ReloadRequested += (_, _) => BrowserReload_Click(this, new RoutedEventArgs());
        // Defer Home until the owned overlay's click route has completed. Hiding an owned
        // no-activate window synchronously inside its own Button.Click can cancel the pointer
        // route before WPF reliably transfers control back to the owner window.
        _serviceControls.HomeRequested += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _ = ReturnHomeAsync()));
        _serviceControls.FullscreenRequested += (_, _) => ToggleApplicationFullscreen();
        _serviceControls.UserActivity += (_, _) => NoteServiceActivity(showControls: true);
        _serviceUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        _serviceUiTimer.Tick += ServiceUiTimer_Tick;
        // A streaming page can add hundreds of megabytes without completing another top-level
        // navigation. Check only every five minutes to keep disk-metadata activity negligible
        // while enforcing the low-storage-oriented 100 MiB soft target during long sessions.
        _cacheBudgetTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _cacheBudgetTimer.Tick += CacheBudgetTimer_Tick;
        _launchTimeoutTimer = new DispatcherTimer { Interval = ServiceLaunchTimeout };
        _launchTimeoutTimer.Tick += LaunchTimeoutTimer_Tick;
        Loaded += (_, _) => _serviceControls.Owner = this;
        LocationChanged += (_, _) => PositionServiceControls();
        SizeChanged += (_, _) => PositionServiceControls();
        StateChanged += (_, _) => Dispatcher.BeginInvoke(PositionServiceControls);
        Closing += MainWindow_Closing;
        Closed += (_, _) => Application.Current.Shutdown();
        _locationRouter.Changed += (_, _) => Dispatcher.BeginInvoke(UpdateCurrentLocationControl);
        UpdateCurrentLocationControl();
        RenderHome();
    }

    private void RenderHome()
    {
        ClockText.Visibility = _settings.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        CurrentLocationButton.Visibility = _settings.CurrentLocationEnabled ? Visibility.Visible : Visibility.Collapsed;
        var filter = SearchBox.Text?.Trim() ?? "";
        var services = _settings.Services.Where(s => s.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        ServicesPanel.Children.Clear();
        foreach (var service in services)
            ServicesPanel.Children.Add(CreateTile(service));

        ServicesSummaryText.Text = string.IsNullOrWhiteSpace(filter)
            ? _settings.Services.Count switch { 0 => "Ready when you are", 1 => "1 configured service", _ => $"{_settings.Services.Count} configured services" }
            : services.Count switch { 0 => $"No services match “{filter}”", 1 => "1 matching service", _ => $"{services.Count} matching services" };

        EmptyState.Visibility = services.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.Services.Count == 0)
        {
            EmptyTitle.Text = "Your services will appear here";
            EmptyDescription.Text = "Add a streaming website to create your first launcher card. Nothing is loaded until you click it.";
            EmptyAddButton.Visibility = Visibility.Visible;
        }
        else if (services.Count == 0)
        {
            EmptyTitle.Text = "No matching services";
            EmptyDescription.Text = "Search only checks the names of services you have configured.";
            EmptyAddButton.Visibility = Visibility.Collapsed;
        }
    }

    private Button CreateTile(ServiceDefinition service)
    {
        var banner = _settings.CardStyle == ServiceCardStyle.Banner;
        var (width, height) = banner
            ? _settings.TileSize switch { TileSize.Small => (210d, 118d), TileSize.Large => (336d, 189d), _ => (270d, 152d) }
            : _settings.TileSize switch { TileSize.Small => (128d, 128d), TileSize.Large => (220d, 220d), _ => (170d, 170d) };

        var button = new Button
        {
            Width = width,
            Margin = new Thickness(0, 0, banner ? 20 : 18, 24),
            Tag = service,
            Style = (Style)FindResource("ServiceCardButtonStyle"),
            RenderTransformOrigin = new Point(.5, .5),
            ToolTip = $"Open {service.Name}"
        };
        System.Windows.Automation.AutomationProperties.SetName(button, service.Name);

        var transform = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        var lift = new TranslateTransform(0, 0);
        transform.Children.Add(scale); transform.Children.Add(lift); button.RenderTransform = transform;
        var shadow = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 7, Opacity = .32, Direction = 270 };
        button.Effect = shadow;

        var content = new StackPanel();
        var card = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(banner ? 16 : 18),
            Background = CreateCardBackground(service),
            BorderBrush = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        card.Child = CreateCardArtwork(service, width, height, banner);
        content.Children.Add(card);
        if (_settings.ShowServiceNames)
        {
            content.Children.Add(new TextBlock
            {
                Text = service.Name,
                FontSize = banner ? 14 : 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 11, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = width - 8
            });
        }

        button.Content = content;
        button.MouseEnter += (_, _) => AnimateCard(scale, lift, shadow, 1.025, -3, 25, .48);
        button.MouseLeave += (_, _) => AnimateCard(scale, lift, shadow, 1, 0, 18, .32);
        button.Click += async (_, _) => await OpenServiceAsync(service);
        button.ContextMenu = CreateServiceContextMenu(service);
        return button;
    }

    private Grid CreateCardArtwork(ServiceDefinition service, double width, double height, bool banner)
    {
        var grid = new Grid();
        var artwork = banner ? TryLoadAsset(service.BannerPath) : null;
        if (artwork is not null)
        {
            grid.Children.Add(new System.Windows.Shapes.Rectangle
            {
                RadiusX = 16,
                RadiusY = 16,
                Fill = new ImageBrush(artwork) { Stretch = service.BannerFit == BannerFit.Fit ? Stretch.Uniform : Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center }
            });
            grid.Children.Add(new System.Windows.Shapes.Rectangle
            {
                RadiusX = 16,
                RadiusY = 16,
                Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(68, 0, 0, 0), new Point(.5, 0), new Point(.5, 1))
            });
            return grid;
        }

        var icon = TryLoadAsset(service.IconPath);
        var logoSize = Math.Min(height * (banner ? .46 : .48), width * .42);
        var logoSurface = new Border
        {
            Width = logoSize,
            Height = logoSize,
            CornerRadius = new CornerRadius(banner ? 14 : 18),
            Background = new SolidColorBrush(Color.FromArgb(88, 255, 255, 255)),
            Padding = new Thickness(logoSize * .15),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (icon is not null)
            logoSurface.Child = new Image { Source = icon, Stretch = Stretch.Uniform };
        else
            logoSurface.Child = Initial(service.Name, banner ? 28 : 32);
        grid.Children.Add(logoSurface);

        if (icon is null && !_settings.ShowServiceNames)
        {
            grid.Children.Add(new TextBlock
            {
                Text = service.Name,
                FontSize = banner ? 15 : 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(14, 0, 14, height * .1),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = width - 28
            });
            logoSurface.Margin = new Thickness(0, 0, 0, height * .16);
        }
        return grid;
    }

    private ContextMenu CreateServiceContextMenu(ServiceDefinition service)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("Open", () => _ = OpenServiceAsync(service));
        Add("Edit", () => EditService(service));
        menu.Items.Add(new Separator());
        var index = _settings.Services.IndexOf(service);
        Add("Move left", () => MoveService(service, -1), index > 0);
        Add("Move right", () => MoveService(service, 1), index >= 0 && index < _settings.Services.Count - 1);
        Add("Change banner…", () => ChangeBanner(service));
        Add("Clear shared temporary data…", () => ClearTemporaryDataWithConfirmation(service));
        menu.Items.Add(new Separator());
        Add("Remove…", () => RemoveService(service));
        return menu;
    }

    private void MoveService(ServiceDefinition service, int delta)
    {
        var from = _settings.Services.IndexOf(service); var to = from + delta;
        if (from < 0 || to < 0 || to >= _settings.Services.Count) return;
        _settings.Services.RemoveAt(from); _settings.Services.Insert(to, service); _store.Save(_settings); RenderHome();
    }

    private void EditService(ServiceDefinition service)
    {
        var index = _settings.Services.IndexOf(service); if (index < 0) return;
        var editor = new ServiceEditorWindow(service) { Owner = this };
        if (editor.ShowDialog() != true) return;
        if (editor.ClearDataRequested) ClearServiceData(service.Id);
        _settings.Services[index] = editor.Result; _store.Save(_settings); StorageService.CleanupOrphanAssets(_settings); RenderHome();
    }

    private void ChangeBanner(ServiceDefinition service)
    {
        var picker = new ArtworkPickerWindow { Owner = this };
        if (picker.ShowDialog() != true) return;
        try
        {
            service.BannerPath = picker.SelectedReference ??
                (picker.SelectedFilePath is not null ? PortableAssetImporter.ImportBanner(picker.SelectedFilePath, service.Id) : service.BannerPath);
            _store.Save(_settings); StorageService.CleanupOrphanAssets(_settings); RenderHome();
        }
        catch (Exception ex) { AppLog.Write("artwork", ex.Message); }
    }

    private void ClearTemporaryDataWithConfirmation(ServiceDefinition service)
    {
        var confirmation = new ConfirmWindow("Clear shared temporary data?", $"Remove safe temporary browser cache shared by Monkey TV? Login and persistent site storage for {service.Name} and other services will be kept.", "Clear") { Owner = this };
        if (confirmation.ShowDialog() == true) _ = ClearTemporaryDataAsync();
    }

    private void RemoveService(ServiceDefinition service)
    {
        var confirmation = new ConfirmWindow("Remove service?", $"Remove {service.Name}? Any retained Build #1 profile data belonging only to this service will also be removed. The current shared browser profile is kept.", "Remove") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        StorageService.RemoveRetainedServiceProfileData(service);
        if (_webView is null) StorageService.CleanupQueuedSharedProfileDeletes();
        _settings.Services.Remove(service); _store.Save(_settings); StorageService.CleanupOrphanAssets(_settings); RenderHome();
    }

    private static Brush CreateCardBackground(ServiceDefinition service)
    {
        var palettes = new[]
        {
            (Color.FromRgb(48, 44, 70), Color.FromRgb(27, 29, 43)),
            (Color.FromRgb(31, 58, 72), Color.FromRgb(22, 31, 42)),
            (Color.FromRgb(67, 41, 55), Color.FromRgb(34, 27, 38)),
            (Color.FromRgb(43, 57, 48), Color.FromRgb(25, 32, 31))
        };
        var pair = palettes[(service.Id.GetHashCode() & int.MaxValue) % palettes.Length];
        return new LinearGradientBrush(pair.Item1, pair.Item2, new Point(0, 0), new Point(1, 1));
    }

    private static void AnimateCard(ScaleTransform scale, TranslateTransform lift, DropShadowEffect shadow, double targetScale, double targetY, double blur, double opacity)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(140));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
        lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(targetY, duration) { EasingFunction = easing });
        shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(blur, duration) { EasingFunction = easing });
        shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = easing });
    }

    private static TextBlock Initial(string name, double fontSize = 26) => new() { Text = string.IsNullOrWhiteSpace(name) ? "?" : name[..1].ToUpperInvariant(), FontSize = fontSize, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private static string? ResolveAsset(string? path) { if (string.IsNullOrWhiteSpace(path)) return null; var file = Path.IsPathRooted(path) ? path : Path.Combine(PortablePaths.AppRoot, path); return File.Exists(file) ? file : null; }
    private static BitmapImage? TryLoadAsset(string? path)
    {
        if (BuiltInArtworkCatalog.IsBuiltInReference(path)) return BuiltInArtworkCatalog.TryLoad(path);
        var file = ResolveAsset(path);
        return file is null ? null : TryLoadImage(file);
    }

    private static BitmapImage? TryLoadImage(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file); image.EndInit(); image.Freeze();
            return image;
        }
        catch { return null; }
    }
    private async Task OpenServiceAsync(ServiceDefinition service)
    {
        // Any service selection supersedes a still-pending Home suspension request.
        ++_homeSuspensionGeneration;
        if (_activeServiceId == service.Id && (_webView is not null || _isBrowserLaunching))
        {
            HomeView.Visibility = Visibility.Collapsed;
            BrowserView.Visibility = Visibility.Visible;
            if (_launchTransitionActive || _isBrowserLaunching)
            {
                try { _webView?.CoreWebView2?.Resume(); } catch { }
                if (_webView is not null) _webView.Visibility = Visibility.Collapsed;
                LoadingOverlay.Visibility = Visibility.Visible;
                ErrorOverlay.Visibility = Visibility.Collapsed;
                StopServiceUi();
            }
            else if (ErrorOverlay.Visibility == Visibility.Visible)
            {
                if (_webView is not null) _webView.Visibility = Visibility.Collapsed;
                LoadingOverlay.Visibility = Visibility.Collapsed;
                StopServiceUi();
            }
            else if (_webView?.CoreWebView2 is not null)
            {
                try { _webView.CoreWebView2.Resume(); } catch { }
                SetBrowserMuted(_webView, false);
                _webView.Visibility = Visibility.Visible;
                _ = ResumeMediaPausedByHomeAsync(_webView);
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ErrorOverlay.Visibility = Visibility.Collapsed;
                StartServiceUi();
            }
            return;
        }

        if (_locationRouter.State is CurrentLocationState.Connecting or CurrentLocationState.Failed)
        {
            HomeView.Visibility = Visibility.Collapsed; BrowserView.Visibility = Visibility.Visible;
            BeginServiceLaunch(service);
            ShowServiceError(service, "Choose or retry a Current Location before opening this service.", "The selected location route is not ready.");
            return;
        }

        HomeView.Visibility = Visibility.Collapsed;
        BrowserView.Visibility = Visibility.Visible;
        BeginServiceLaunch(service);

        // Build #2 is a single-tab browser. A different service changes the destination of the
        // same hidden WebView instead of creating another WebView or profile.
        if (_webView?.CoreWebView2 is not null)
        {
            _browserProcessFailed = false;
            _activeServiceId = service.Id;
            try { _webView.CoreWebView2.Resume(); } catch { }
            // This is a genuine destination switch. Clear only WebView2's supported disposable
            // disk cache before navigating the shared tab; same-service resume never comes here.
            await StorageService.ClearActiveDiskCacheAsync(_webView.CoreWebView2.Profile);
            ApplyServiceSettings(_webView, service);
            _webView.CoreWebView2.Navigate(service.Url);
            return;
        }

        if (_isBrowserLaunching) return;

        var generation = ++_browserGeneration;
        _browserProcessFailed = false;
        _activeServiceId = service.Id;
        _isBrowserLaunching = true;
        RemoveStaleBrowserChildren();
        try
        {
            var proxyPort = _locationRouter.EnsureGateway();
            var options = new CoreWebView2EnvironmentOptions
            {
                // Narrow Feature #0.01 exception: one fixed loopback routing boundary.
                AdditionalBrowserArguments = $"--proxy-server=http://127.0.0.1:{proxyPort} --disable-quic --force-webrtc-ip-handling-policy=disable_non_proxied_udp"
            };
            var environment = await CoreWebView2Environment.CreateAsync(null, PortablePaths.SharedBrowserUserDataDirectory, options);
            _browserEnvironment = environment;
            if (generation != _browserGeneration) return;

            var view = new WebView2
            {
                Visibility = Visibility.Collapsed
            };
            _webView = view; BrowserHost.Children.Add(view);
            await view.EnsureCoreWebView2Async(environment);
            if (generation != _browserGeneration || !ReferenceEquals(_webView, view))
            {
                DisposeView(view);
                return;
            }

            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.CoreWebView2.Settings.IsZoomControlEnabled = false;
            view.ZoomFactor = 1.0;
            view.ZoomFactorChanged += (_, _) =>
            {
                if (Math.Abs(view.ZoomFactor - 1.0) > 0.001) view.ZoomFactor = 1.0;
            };
            _defaultUserAgent = view.CoreWebView2.Settings.UserAgent;
            SetBrowserMuted(view, true);
            // WPF WebView2 forwards accelerator keys into the WPF input route.
            view.PreviewKeyDown += (_, e) =>
            {
                if (HandleApplicationShortcut(e)) return;
                if (e.Key == Key.F11)
                {
                    e.Handled = true;
                    Dispatcher.BeginInvoke(ToggleApplicationFullscreen);
                }
                // Escape is deliberately not handled here. While a service is visible the
                // website owns Escape, including in Monkey TV application fullscreen.
            };
            view.CoreWebView2.NewWindowRequested += WebView_NewWindowRequested;
            view.CoreWebView2.PermissionRequested += WebView_PermissionRequested;
            view.CoreWebView2.DownloadStarting += WebView_DownloadStarting;
            view.CoreWebView2.NavigationStarting += (_, e) => Dispatcher.Invoke(() =>
            {
                if (generation != _browserGeneration || !ReferenceEquals(_webView, view)) return;
                if (e.IsUserInitiated && ActiveService is { } originService &&
                    ExternalNavigationPolicy.TryHttpUri(originService.Url, out var origin) &&
                    ExternalNavigationPolicy.TryHttpUri(e.Uri, out var target) &&
                    !ExternalNavigationPolicy.IsRelatedOrAuthentication(origin, target))
                {
                    e.Cancel = true;
                    ExternalNavigationPolicy.OpenInDefaultBrowser(target.AbsoluteUri);
                    return;
                }
                if (_launchTransitionActive && _launchServiceId == _activeServiceId)
                    _launchNavigationId = e.NavigationId;
            });
            view.CoreWebView2.ContainsFullScreenElementChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                if (generation == _browserGeneration && ReferenceEquals(_webView, view))
                    SetWebsiteFullscreen(view.CoreWebView2.ContainsFullScreenElement);
            });
            view.NavigationCompleted += (_, e) =>
            {
                if (generation != _browserGeneration || !ReferenceEquals(_webView, view)) return;
                if (_launchTransitionActive)
                {
                    // A new service navigation cancels the previous top-level navigation. Ignore
                    // that stale completion instead of showing its failure against the new service.
                    if (_launchServiceId != _activeServiceId || _launchNavigationId != e.NavigationId) return;
                    if (e.IsSuccess) CompleteServiceLaunch(view);
                    else if (ActiveService is { } launchingService)
                        ShowServiceError(launchingService, "Something went wrong while connecting to this service.", "WebView2 navigation status: " + e.WebErrorStatus);
                    return;
                }

                if (!e.IsSuccess && BrowserView.Visibility == Visibility.Visible && ActiveService is { } active)
                    ShowServiceError(active, "Something went wrong while connecting to this service.", "WebView2 navigation status: " + e.WebErrorStatus);
                else if (e.IsSuccess)
                    _ = EnforceActiveCacheBudgetAsync(view, generation);
            };
            view.CoreWebView2.ProcessFailed += (_, e) => Dispatcher.Invoke(() =>
            {
                if (generation != _browserGeneration || !ReferenceEquals(_webView, view)) return;
                _browserProcessFailed = true;
                if (ActiveService is { } active)
                    ShowServiceError(active, "The browser process stopped unexpectedly.", "WebView2 process failure: " + e.ProcessFailedKind);
            });
            ApplyServiceSettings(view, service);
            view.CoreWebView2.Navigate(service.Url);
            _isBrowserLaunching = false;
        }
        catch (Exception ex)
        {
            if (generation == _browserGeneration)
            {
                _isBrowserLaunching = false;
                ShowServiceError(service, "Monkey TV couldn't start the browser for this service.", ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    private ServiceDefinition? ActiveService => _activeServiceId is { } id
        ? _settings.Services.FirstOrDefault(service => service.Id == id)
        : null;

    private void ApplyServiceSettings(WebView2 view, ServiceDefinition service)
    {
        // CoreWebView2 cannot reset UserAgent by assigning an empty value. Save the runtime's
        // original value once and explicitly restore it before a Default Experience navigation.
        var userAgent = BrowserIdentity.ResolveUserAgent(service) ?? _defaultUserAgent;
        if (!string.IsNullOrWhiteSpace(userAgent)) view.CoreWebView2.Settings.UserAgent = userAgent;
    }

    private async void WebView_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            e.Handled = true;
            if (!e.IsUserInitiated) return;
            var hasTarget = ExternalNavigationPolicy.TryHttpUri(e.Uri, out var target);
            var blankTarget = string.IsNullOrWhiteSpace(e.Uri) || string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase);
            if (!hasTarget && !blankTarget) return;
            var current = ExternalNavigationPolicy.TryHttpUri(_webView?.Source?.AbsoluteUri, out var source) ? source : null;
            if (hasTarget && !ExternalNavigationPolicy.IsRelatedOrAuthentication(current, target))
            {
                ExternalNavigationPolicy.OpenInDefaultBrowser(target.AbsoluteUri);
                return;
            }

            _popupWindow?.Close();
            if (_browserEnvironment is null) return;
            var popup = new PopupWebViewWindow { Owner = this, Title = $"{ActiveService?.Name ?? "Monkey TV"} sign-in" };
            _popupWindow = popup;
            popup.Closed += (_, _) => { if (ReferenceEquals(_popupWindow, popup)) _popupWindow = null; };
            popup.Show();
            await popup.InitializeAsync(_browserEnvironment, _webView?.CoreWebView2?.Settings.UserAgent);
            e.NewWindow = popup.Core;
        }
        catch (Exception ex) { AppLog.Write("popup", ex.Message); }
        finally { deferral.Complete(); }
    }

    private void WebView_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            e.Handled = true;
            e.SavesInProfile = false;
            if (e.PermissionKind == CoreWebView2PermissionKind.Autoplay)
            {
                e.State = CoreWebView2PermissionState.Allow;
                return;
            }
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
            {
                e.State = CoreWebView2PermissionState.Deny;
                return;
            }

            var promptable = e.PermissionKind is CoreWebView2PermissionKind.Camera or
                CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Geolocation or
                CoreWebView2PermissionKind.ClipboardRead;
            if (!promptable)
            {
                e.State = CoreWebView2PermissionState.Deny;
                return;
            }

            var origin = ExternalNavigationPolicy.TryHttpUri(e.Uri, out var uri) ? uri.Host : "this website";
            var label = e.PermissionKind switch
            {
                CoreWebView2PermissionKind.Camera => "camera",
                CoreWebView2PermissionKind.Microphone => "microphone",
                CoreWebView2PermissionKind.Geolocation => "location",
                _ => "clipboard"
            };
            var dialog = new ConfirmWindow($"Allow {label}?", $"{origin} is requesting temporary access to your {label}. Monkey TV will ask again next time.", "Allow once") { Owner = this };
            e.State = dialog.ShowDialog() == true ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        }
        catch (Exception ex)
        {
            e.State = CoreWebView2PermissionState.Deny;
            AppLog.Write("permission", ex.Message);
        }
        finally { deferral.Complete(); }
    }

    private void WebView_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var deferral = e.GetDeferral();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                e.Handled = true;
                var suggested = Path.GetFileName(e.ResultFilePath);
                var dialog = new SaveFileDialog { Title = "Save website download", FileName = suggested, AddExtension = true, OverwritePrompt = true };
                if (dialog.ShowDialog(this) == true) e.ResultFilePath = dialog.FileName;
                else e.Cancel = true;
            }
            catch (Exception ex) { e.Cancel = true; AppLog.Write("download", ex.Message); }
            finally { deferral.Complete(); }
        }));
    }

    private void BeginServiceLaunch(ServiceDefinition service)
    {
        _launchTransitionActive = true;
        _launchServiceId = service.Id;
        _launchNavigationId = null;
        _launchTimeoutTimer.Stop();
        _launchTimeoutTimer.Start();
        StopServiceUi();
        SetBrowserMuted(_webView, true);
        if (_webView is not null) _webView.Visibility = Visibility.Collapsed;
        ErrorOverlay.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingText.Text = $"Launching {service.Name}…";
        SetLaunchArtwork(service);
    }

    private void SetLaunchArtwork(ServiceDefinition service)
    {
        LoadingArtwork.Source = null;
        LoadingArtwork.Visibility = Visibility.Collapsed;
        LoadingInitial.Text = string.IsNullOrWhiteSpace(service.Name) ? "?" : service.Name[..1].ToUpperInvariant();
        LoadingInitial.Visibility = Visibility.Visible;

        var artwork = TryLoadAsset(service.BannerPath) ?? TryLoadAsset(service.IconPath);
        if (artwork is null) return;
        LoadingArtwork.Source = artwork;
        LoadingArtwork.Visibility = Visibility.Visible;
        LoadingInitial.Visibility = Visibility.Collapsed;
    }

    private void CompleteServiceLaunch(WebView2 view)
    {
        EndServiceLaunchTransition();
        LoadingOverlay.Visibility = Visibility.Collapsed;
        ErrorOverlay.Visibility = Visibility.Collapsed;
        _ = EnforceActiveCacheBudgetAsync(view, _browserGeneration);
        if (BrowserView.Visibility == Visibility.Visible)
        {
            SetBrowserMuted(view, false);
            view.Visibility = Visibility.Visible;
            StartServiceUi();
        }
        else
        {
            SetBrowserMuted(view, true);
            view.Visibility = Visibility.Collapsed;
        }
    }

    private void EndServiceLaunchTransition()
    {
        _launchTimeoutTimer.Stop();
        _launchTransitionActive = false;
        _launchServiceId = null;
        _launchNavigationId = null;
    }

    private void LaunchTimeoutTimer_Tick(object? sender, EventArgs e)
    {
        _launchTimeoutTimer.Stop();
        if (!_launchTransitionActive || ActiveService is not { } service) return;
        try { _webView?.CoreWebView2?.Stop(); } catch { }
        ShowServiceError(service, "This service took too long to respond.", "The initial navigation did not complete within 45 seconds.");
    }

    private void ShowServiceError(ServiceDefinition service, string message, string details)
    {
        EndServiceLaunchTransition();
        LoadingOverlay.Visibility = Visibility.Collapsed; ErrorOverlay.Visibility = Visibility.Visible;
        SetBrowserMuted(_webView, true);
        if (_webView is not null) _webView.Visibility = Visibility.Collapsed;
        ErrorTitle.Text = $"Couldn't load {service.Name}"; ErrorMessage.Text = message; ErrorDetails.Text = details;
        AppLog.Write("browser", $"{service.Name}: {details}");
        StopServiceUi();
    }

    private async Task EnforceActiveCacheBudgetAsync(WebView2 view, long generation)
    {
        // The supported API clears only HTTP/disk cache. DOM storage, cookies, service workers,
        // IndexedDB and provider state are deliberately excluded.
        if (generation != _browserGeneration || !ReferenceEquals(_webView, view) || !StorageService.ShouldClearActiveDiskCache()) return;
        await StorageService.ClearActiveDiskCacheAsync(view.CoreWebView2?.Profile);
    }

    private async void CacheBudgetTimer_Tick(object? sender, EventArgs e)
    {
        if (_cacheBudgetCheckRunning || BrowserView.Visibility != Visibility.Visible ||
            _webView?.CoreWebView2 is null) return;

        _cacheBudgetCheckRunning = true;
        try { await EnforceActiveCacheBudgetAsync(_webView, _browserGeneration); }
        finally { _cacheBudgetCheckRunning = false; }
    }

    private async void RetryService_Click(object sender, RoutedEventArgs e)
    {
        if (_activeServiceId is not { } id || _settings.Services.FirstOrDefault(s => s.Id == id) is not { } service) { await ReturnHomeAsync(); return; }
        if (_browserProcessFailed || _webView?.CoreWebView2 is null)
        {
            DestroyBrowser(); await OpenServiceAsync(service); return;
        }
        BeginServiceLaunch(service);
        ApplyServiceSettings(_webView, service);
        _webView.CoreWebView2.Navigate(service.Url);
    }

    private void DestroyBrowser()
    {
        ++_browserGeneration;
        _isBrowserLaunching = false;
        _activeServiceId = null;
        _browserProcessFailed = false;
        EndServiceLaunchTransition();
        StopServiceUi();
        SetWebsiteFullscreen(false);
        var view = _webView; _webView = null;
        if (view is not null) DisposeView(view);
        RemoveStaleBrowserChildren();
    }

    private void DisposeView(WebView2 view)
    {
        // Never allow one WebView2 cleanup failure to skip visual removal or disposal.
        try { BrowserHost.Children.Remove(view); } catch { }
        try { view.CoreWebView2?.Resume(); } catch { }
        try { view.CoreWebView2?.Stop(); } catch { }
        try { view.Dispose(); } catch { }
    }

    private void RemoveStaleBrowserChildren()
    {
        foreach (var stale in BrowserHost.Children.OfType<WebView2>().ToArray())
        {
            try { BrowserHost.Children.Remove(stale); } catch { }
            if (!ReferenceEquals(stale, _webView))
            {
                try { stale.CoreWebView2?.Stop(); } catch { }
                try { stale.Dispose(); } catch { }
            }
        }
    }

    private async Task ReturnHomeAsync()
    {
        var suspensionGeneration = ++_homeSuspensionGeneration;
        var view = _webView;

        // Audio must stop and Home must render synchronously with the click/shortcut. WebView2
        // requires its controller to be invisible before TrySuspendAsync, so suspension follows
        // the visual state change rather than blocking it.
        StopServiceUi();
        SetBrowserMuted(view, true);
        if (view is not null) view.Visibility = Visibility.Collapsed;
        BrowserView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
        RenderHome();
        UpdateHomeChrome();

        if (view?.CoreWebView2 is null) return;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (suspensionGeneration != _homeSuspensionGeneration || !ReferenceEquals(view, _webView)) return;

        try
        {
            // Standard finite-duration HTML media is paused so reopening this service can resume
            // from the same position. A clearly live stream (duration === Infinity) stays alive
            // and muted instead of being suspended, allowing the provider to maintain live state.
            var liveMediaDetected = await PauseOnDemandMediaAsync(view);
            if (suspensionGeneration != _homeSuspensionGeneration ||
                !ReferenceEquals(view, _webView) || BrowserView.Visibility == Visibility.Visible) return;
            if (liveMediaDetected) return;

            var suspended = false;
            for (var attempt = 0; attempt < 3 && !suspended; attempt++)
            {
                suspended = await view.CoreWebView2.TrySuspendAsync();
                if (suspended || suspensionGeneration != _homeSuspensionGeneration ||
                    !ReferenceEquals(view, _webView) || BrowserView.Visibility == Visibility.Visible) break;
                await Task.Delay(250);
            }
            if (!suspended && suspensionGeneration == _homeSuspensionGeneration)
                AppLog.Write("browser", "The hidden WebView declined its best-effort suspension; audio remains muted.");
        }
        catch (Exception ex)
        {
            if (suspensionGeneration == _homeSuspensionGeneration)
                AppLog.Write("browser", "Could not suspend the hidden WebView: " + ex.Message);
        }
        finally
        {
            // If the user reopened a service while suspension was in flight, ensure the late
            // completion cannot leave the now-visible page asleep or muted.
            if (suspensionGeneration != _homeSuspensionGeneration &&
                ReferenceEquals(view, _webView) && BrowserView.Visibility == Visibility.Visible)
            {
                try { view.CoreWebView2.Resume(); } catch { }
                SetBrowserMuted(view, false);
                _ = ResumeMediaPausedByHomeAsync(view);
            }
        }
    }

    private static async Task<bool> PauseOnDemandMediaAsync(WebView2 view)
    {
        if (view.CoreWebView2 is null) return false;
        const string script = """
            (() => {
              try {
                const media = [];
                const collect = doc => {
                  if (!doc) return;
                  media.push(...doc.querySelectorAll('video,audio'));
                  for (const frame of doc.querySelectorAll('iframe')) {
                    try { collect(frame.contentDocument); } catch { }
                  }
                };
                collect(document);
                const live = media.some(item => item.readyState > 0 && item.duration === Infinity);
                if (live) return 'live';
                for (const item of media) {
                  if (!item.paused && !item.ended) {
                    item.__monkeyTvPausedByHome = true;
                    item.pause();
                  }
                }
                return 'on-demand';
              } catch { return 'unknown'; }
            })()
            """;
        try
        {
            var result = await view.CoreWebView2.ExecuteScriptAsync(script);
            return string.Equals(JsonSerializer.Deserialize<string>(result), "live", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static async Task ResumeMediaPausedByHomeAsync(WebView2 view)
    {
        if (view.CoreWebView2 is null) return;
        const string script = """
            (() => {
              try {
                const media = [];
                const collect = doc => {
                  if (!doc) return;
                  media.push(...doc.querySelectorAll('video,audio'));
                  for (const frame of doc.querySelectorAll('iframe')) {
                    try { collect(frame.contentDocument); } catch { }
                  }
                };
                collect(document);
                for (const item of media) {
                  if (item.__monkeyTvPausedByHome) {
                    item.__monkeyTvPausedByHome = false;
                    item.play().catch(() => {});
                  }
                }
              } catch { }
            })()
            """;
        try { await view.CoreWebView2.ExecuteScriptAsync(script); } catch { }
    }

    private static void SetBrowserMuted(WebView2? view, bool muted)
    {
        try
        {
            if (view?.CoreWebView2 is not null) view.CoreWebView2.IsMuted = muted;
        }
        catch { }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) RenderHome(); }
    private void SearchToggle_Click(object sender, RoutedEventArgs e)
    {
        SearchContainer.Visibility = Visibility.Visible;
        HeaderAddServiceButton.Visibility = Visibility.Collapsed;
        SearchBox.Focus(); SearchBox.SelectAll();
    }
    private void CloseSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear(); SearchContainer.Visibility = Visibility.Collapsed; HeaderAddServiceButton.Visibility = Visibility.Visible;
    }
    private void AddService_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ServiceEditorWindow { Owner = this };
        if (editor.ShowDialog() != true) return;
        _settings.Services.Add(editor.Result); _store.Save(_settings); RenderHome();
    }

    private void UpdateCurrentLocationControl()
    {
        CurrentLocationText.Text = _locationRouter.DisplayName;
        CurrentLocationButton.Visibility = _settings.CurrentLocationEnabled ? Visibility.Visible : Visibility.Collapsed;
        CurrentLocationButton.IsEnabled = _locationRouter.State != CurrentLocationState.Connecting;
        CurrentLocationButton.ToolTip = _locationRouter.State == CurrentLocationState.Failed
            ? "Current Location failed — choose Retry or Direct"
            : "Choose the route used by Monkey TV websites";
    }

    private void CurrentLocation_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var direct = new MenuItem { Header = "🌐 Direct", IsCheckable = true, IsChecked = _locationRouter.State == CurrentLocationState.Direct };
        direct.Click += async (_, _) => await ChangeLocationAsync(null);
        menu.Items.Add(direct); menu.Items.Add(new Separator());
        if (_locationRouter.State == CurrentLocationState.Failed && _locationRouter.RequestedLocation is { } failed)
        {
            var retry = new MenuItem { Header = "Retry " + failed.Name };
            retry.Click += async (_, _) => await ChangeLocationAsync(failed);
            menu.Items.Add(retry); menu.Items.Add(new Separator());
        }
        if (_settings.LastUsedNordLocation is { } last)
        {
            var previous = NordLocationCatalog.Get(last);
            var item = new MenuItem { Header = "🌐 " + previous.Name + " — Last used" };
            item.Click += async (_, _) => await ChangeLocationAsync(previous);
            menu.Items.Add(item); menu.Items.Add(new Separator());
        }
        foreach (var location in NordLocationCatalog.All)
        {
            var item = new MenuItem { Header = "🌐 " + location.Name, IsCheckable = true, IsChecked = _locationRouter.ActiveLocation?.Id == location.Id };
            item.Click += async (_, _) => await ChangeLocationAsync(location);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var setup = new MenuItem { Header = "Set up NordVPN…" };
        setup.Click += (_, _) => Settings_Click(this, new RoutedEventArgs());
        menu.Items.Add(setup); menu.PlacementTarget = CurrentLocationButton; menu.IsOpen = true;
    }

    private async Task ChangeLocationAsync(NordLocation? location)
    {
        var service = ActiveService;
        var wasBrowsing = service is not null && BrowserView.Visibility == Visibility.Visible;
        if (location is null)
        {
            if (wasBrowsing) BeginServiceLaunch(service!);
            _locationRouter.Block(); await _locationRouter.SelectDirectAsync();
            if (wasBrowsing && _webView?.CoreWebView2 is not null)
            { BeginServiceLaunch(service!); ApplyServiceSettings(_webView, service!); _webView.CoreWebView2.Navigate(service!.Url); }
            return;
        }
        var credentials = NordCredentialStore.Load();
        if (credentials is null)
        {
            var setup = new SettingsWindow(_settings, _store, "VPN") { Owner = this };
            setup.ShowDialog(); credentials = NordCredentialStore.Load(); if (credentials is null) return;
        }
        if (wasBrowsing) BeginServiceLaunch(service!);
        _locationRouter.Block();
        var connected = await _locationRouter.SelectNordAsync(location, credentials);
        if (!connected)
        {
            if (wasBrowsing) ShowServiceError(service!, "Monkey TV couldn't connect to " + location.Name + ".", "Nord SOCKS5 authentication or connection verification failed. Retry from Current Location or choose Direct.");
            return;
        }
        _settings.LastUsedNordLocation = location.Id; _store.Save(_settings);
        if (wasBrowsing && _webView?.CoreWebView2 is not null)
        {
            BeginServiceLaunch(service!); ApplyServiceSettings(_webView, service!); _webView.CoreWebView2.Navigate(service!.Url);
        }
    }

    internal async Task SetCurrentLocationFeatureEnabledAsync(bool enabled)
    {
        if (!enabled)
        {
            // Do not leave a hidden Nord route active. This waits for Direct mode before the
            // Settings page hides the Home control, and reloads only if a service is visible.
            if (_locationRouter.State != CurrentLocationState.Direct) await ChangeLocationAsync(null);
            _settings.CurrentLocationEnabled = false; _store.Save(_settings); RenderHome();
            return;
        }
        _settings.CurrentLocationEnabled = true; _store.Save(_settings); RenderHome();
    }
    private void BrowserBack_Click(object sender, RoutedEventArgs e) { if (_webView?.CanGoBack == true) _webView.GoBack(); }
    private void BrowserReload_Click(object sender, RoutedEventArgs e) { _webView?.Reload(); }
    private async void BrowserHome_Click(object sender, RoutedEventArgs e) => await ReturnHomeAsync();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleApplicationFullscreen();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var serviceWasVisible = BrowserView.Visibility == Visibility.Visible;
        if (serviceWasVisible) StopServiceUi();
        var settings = new SettingsWindow(_settings, _store) { Owner = this };
        settings.ShowDialog(); _settings = _store.Load(); ThemeService.Apply(_settings);
        if (_activeServiceId is { } activeId && _settings.Services.All(s => s.Id != activeId)) DestroyBrowser();
        RenderHome();
        if (serviceWasVisible && _webView is not null) StartServiceUi();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (HandleApplicationShortcut(e)) return;
        if (e.Key == Key.F11) { ToggleApplicationFullscreen(); e.Handled = true; }
        // A visible service owns Escape. Do not use application fullscreen as a reason to
        // consume it: TV/web players need the same Escape behavior as in a normal window.
        else if (e.Key == Key.Escape && _webView is not null && BrowserView.Visibility == Visibility.Visible) { return; }
        else if (e.Key == Key.Escape && _websiteFullscreen) { return; }
        else if (e.Key == Key.Escape && _appFullscreen) { SetApplicationFullscreen(false); e.Handled = true; }
        else if (e.Key == Key.Escape && SearchContainer.Visibility == Visibility.Visible) { CloseSearch_Click(this, e); e.Handled = true; }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (BrowserView.Visibility == Visibility.Visible && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            e.Handled = true;
    }

    private bool HandleApplicationShortcut(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var serviceActive = _webView is not null && BrowserView.Visibility == Visibility.Visible;
        if (serviceActive && modifiers == ModifierKeys.Control && e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract or Key.D0 or Key.NumPad0)
        { if (_webView is not null) _webView.ZoomFactor = 1.0; e.Handled = true; return true; }
        if (e.Key == Key.Left && modifiers == ModifierKeys.Alt && serviceActive)
        { BrowserBack_Click(this, e); e.Handled = true; return true; }
        if (e.Key == Key.R && modifiers == ModifierKeys.Control && serviceActive)
        { BrowserReload_Click(this, e); e.Handled = true; return true; }
        if (e.Key == Key.H && modifiers == ModifierKeys.Control && serviceActive)
        { e.Handled = true; _ = ReturnHomeAsync(); return true; }
        if (e.Key == Key.OemComma && modifiers == ModifierKeys.Control)
        { e.Handled = true; Settings_Click(this, e); return true; }
        return false;
    }

    internal async Task ClearTemporaryDataAsync()
    {
        await StorageService.ClearActiveDiskCacheAsync(_webView?.CoreWebView2?.Profile);
        StorageService.ClearAllTemporaryData(includeReplaceableComponents: true);
    }

    internal void ClearServiceData(Guid serviceId)
    {
        if (_activeServiceId == serviceId) DestroyBrowser();
        try { StorageService.ClearServiceData(serviceId); }
        catch (Exception ex) { AppLog.Write("storage", ex.Message); }
    }

    internal void PrepareForConfigurationImport() => DestroyBrowser();

    internal void ClearAllBrowserData()
    {
        DestroyBrowser();
        try { StorageService.ResetBrowserData(); }
        catch (Exception ex) { AppLog.Write("storage", ex.Message); }
    }

    internal void ResetMonkeyTv()
    {
        DestroyBrowser();
        try
        {
            StorageService.ResetMonkeyTvData();
            _settings = new MonkeySettings();
            _store.Save(_settings);
            RenderHome();
        }
        catch (Exception ex) { AppLog.Write("storage", ex.Message); }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        ShutdownOverlay.Visibility = Visibility.Visible;
        StopServiceUi();
        await Dispatcher.Yield(DispatcherPriority.Render);

        WindowPlacementService.Capture(this, _settings, _appFullscreen);
        try { _store.Save(_settings); } catch (Exception ex) { AppLog.Write("shutdown", ex.Message); }
        _serviceUiTimer.Stop();
        try { _serviceControls.Close(); } catch { }
        try { _popupWindow?.Close(); } catch { }
        DestroyBrowser();
        try { await _locationRouter.DisposeAsync(); } catch { }
        try
        {
            await Task.Run(() =>
            {
                StorageService.ClearAllTemporaryData();
                StorageService.CleanupQueuedSharedProfileDeletes();
                StorageService.CleanupOrphanAssets(_settings);
            });
            SessionMarker.MarkClean();
        }
        catch (Exception ex) { AppLog.Write("shutdown", ex.Message); }
        finally
        {
            _allowClose = true;
            Close();
        }
    }

    internal void ToggleApplicationFullscreen() => SetApplicationFullscreen(!_appFullscreen);

    private void SetApplicationFullscreen(bool value)
    {
        if (value == _appFullscreen) return;
        if (value)
        {
            WindowPlacementService.Capture(this, _settings, appFullscreen: false);
            try { _store.Save(_settings); } catch { }
        }
        _appFullscreen = value;
        UpdateFullscreenShell();
        _serviceControls.SetFullscreenState(_appFullscreen);
        if (_webView is not null && !_websiteFullscreen) NoteServiceActivity(showControls: true);
    }

    private void SetWebsiteFullscreen(bool value)
    {
        if (value == _websiteFullscreen) return;
        _websiteFullscreen = value;
        if (value)
        {
            _serviceControls.HideOverlay(true);
            RestoreCursor();
        }
        UpdateFullscreenShell();
        if (!value && _webView is not null && BrowserView.Visibility == Visibility.Visible)
            NoteServiceActivity(showControls: false);
    }

    private void UpdateFullscreenShell()
    {
        var shouldBeFullscreen = _appFullscreen || _websiteFullscreen;
        if (shouldBeFullscreen && !_shellFullscreen)
        {
            _previousStyle = WindowStyle;
            _previousState = WindowState;
            _previousResizeMode = ResizeMode;
            _previousTopmost = Topmost;
            _previousBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            var handle = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                monitorInfo.Monitor = new NativeRect { Left = 0, Top = 0, Right = (int)SystemParameters.PrimaryScreenWidth, Bottom = (int)SystemParameters.PrimaryScreenHeight };
            var topLeft = ScreenPixelsToDip(new Point(monitorInfo.Monitor.Left, monitorInfo.Monitor.Top));
            var bottomRight = ScreenPixelsToDip(new Point(monitorInfo.Monitor.Right, monitorInfo.Monitor.Bottom));

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Left = topLeft.X; Top = topLeft.Y;
            Width = bottomRight.X - topLeft.X; Height = bottomRight.Y - topLeft.Y;
            _shellFullscreen = true;
        }
        else if (!shouldBeFullscreen && _shellFullscreen)
        {
            Topmost = _previousTopmost;
            WindowStyle = _previousStyle;
            ResizeMode = _previousResizeMode;
            WindowState = WindowState.Normal;
            Left = _previousBounds.Left; Top = _previousBounds.Top;
            Width = _previousBounds.Width; Height = _previousBounds.Height;
            if (_previousState == WindowState.Maximized) WindowState = WindowState.Maximized;
            _shellFullscreen = false;
        }
        UpdateHomeChrome();
        Dispatcher.BeginInvoke(PositionServiceControls);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_webView is not null) NoteServiceActivity(showControls: false);
    }

    private void UpdateHomeChrome()
    {
        HomeHeader.Visibility = _shellFullscreen ? Visibility.Collapsed : Visibility.Visible;
        HomeScroll.Padding = _shellFullscreen ? new Thickness(46, 38, 34, 42) : new Thickness(46, 8, 34, 42);
    }

    private void StartServiceUi()
    {
        GetCursorPos(out _lastPointerPosition);
        _lastPointerActivity = DateTime.UtcNow;
        RestoreCursor();
        _serviceUiTimer.Start();
        _cacheBudgetTimer.Start();
        if (!_websiteFullscreen)
        {
            _serviceControls.ShowOverlay();
            PositionServiceControls();
        }
    }

    private void StopServiceUi()
    {
        _serviceUiTimer.Stop();
        _cacheBudgetTimer.Stop();
        _serviceControls.HideOverlay(true);
        RestoreCursor();
    }

    private void NoteServiceActivity(bool showControls)
    {
        _lastPointerActivity = DateTime.UtcNow;
        RestoreCursor();
        if (showControls && !_websiteFullscreen && BrowserView.Visibility == Visibility.Visible)
        {
            _serviceControls.ShowOverlay();
            PositionServiceControls();
        }
    }

    private void ServiceUiTimer_Tick(object? sender, EventArgs e)
    {
        if (_webView is null || BrowserView.Visibility != Visibility.Visible)
        {
            StopServiceUi();
            return;
        }

        if (GetCursorPos(out var pointer) && (pointer.X != _lastPointerPosition.X || pointer.Y != _lastPointerPosition.Y))
        {
            _lastPointerPosition = pointer;
            NoteServiceActivity(showControls: false);
            if (!_websiteFullscreen && IsPointerInTopRevealZone(pointer))
            {
                _serviceControls.ShowOverlay();
                PositionServiceControls();
            }
        }

        var inactive = DateTime.UtcNow - _lastPointerActivity;
        if (_websiteFullscreen)
        {
            if (inactive >= TimeSpan.FromSeconds(3)) HideCursor();
            return;
        }
        if (inactive >= TimeSpan.FromSeconds(3))
        {
            _serviceControls.HideOverlay();
            HideCursor();
        }
    }

    private bool IsPointerInTopRevealZone(NativePoint pointer)
    {
        var topLeft = RootGrid.PointToScreen(new Point(0, 0));
        var bottomRight = RootGrid.PointToScreen(new Point(RootGrid.ActualWidth, RootGrid.ActualHeight));
        return pointer.X >= topLeft.X && pointer.X <= bottomRight.X &&
               pointer.Y >= topLeft.Y && pointer.Y <= topLeft.Y + 24;
    }

    private void PositionServiceControls()
    {
        if (!_serviceControls.IsLoaded || BrowserView.Visibility != Visibility.Visible) return;
        var screenPoint = RootGrid.PointToScreen(new Point(RootGrid.ActualWidth / 2, 0));
        var dipPoint = ScreenPixelsToDip(screenPoint);
        _serviceControls.Left = dipPoint.X - _serviceControls.Width / 2;
        _serviceControls.Top = dipPoint.Y + 10;
    }

    private Point ScreenPixelsToDip(Point point)
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(point) ?? point;
    }

    private void HideCursor()
    {
        if (_cursorHidden) return;
        _cursorHidden = true;
        Mouse.OverrideCursor = Cursors.None;
        SetCursor(IntPtr.Zero);
    }

    private void RestoreCursor()
    {
        if (!_cursorHidden && Mouse.OverrideCursor is null) return;
        _cursorHidden = false;
        Mouse.OverrideCursor = null;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
}
