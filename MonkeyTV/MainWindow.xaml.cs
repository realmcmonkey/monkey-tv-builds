using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private WebViewRuntimeSelection? _browserRuntime;
    private PopupWebViewWindow? _popupWindow;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _serviceUiTimer;
    private readonly DispatcherTimer _cacheBudgetTimer;
    private readonly DispatcherTimer _launchTimeoutTimer;
    private readonly ServiceControlsWindow _serviceControls;
    private CurrentLocationRouter _locationRouter = new();
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
    private bool _serviceNavigationLoading;
    private string? _lastNavigationUrl;
    private string? _lastErrorDiagnostic;
    private int _launchStatusVersion;
    private readonly HashSet<ulong> _ambiguousAuthenticationNavigationIds = new();
    private Point _dragStart;
    private Button? _dragSource;
    private Button? _dragOverCard;
    private double _lastHomeLayoutWidth;
    private double _lastServicesLayoutWidth;
    private bool _updatingPrototypeAppearance;
    private bool _railScrollAnimating;
    private readonly Dictionary<Guid, Border> _favoriteBadges = [];
    private readonly Dictionary<Guid, ServiceDefinition> _unsavedEditorDrafts = [];
    private readonly HashSet<Guid> _servicesRequiringFreshReload = [];
    private readonly HashSet<Guid> _servicesWithPendingDataReset = [];
    private readonly SessionPermissionStore _sessionPermissions = new();
    private ServiceDefinition? _unsavedTestService;
    private ServiceEditorWindow? _activeServiceEditor;
    private Action? _pendingEditorDismissal;
    private bool _serviceEditorDrawerClosing;
    private bool _serviceEditorDismissalInProgress;
    private bool _vpnLocationDrawerClosing;
    private bool _showingBuiltInServiceLibrary;
    // Set only while a Switch & Open request is being redirected to credential setup. This
    // prevents the launch flow from replacing Settings with a misleading route-failure page.
    private bool _preferredRouteRedirectedToCredentialSetup;
    private string _integratedPage = "Home";
    private static readonly TimeSpan ServiceLaunchTimeout = TimeSpan.FromSeconds(45);

    public MainWindow(MonkeySettings settings, SettingsStore store)
    {
        InitializeComponent(); _settings = settings; _store = store;
        ConfigureButtonHover(HomeNavButton);
        ConfigureButtonHover(ServicesNavButton);
        ConfigureButtonHover(SettingsNavButton);
        ConfigureLargeShellChrome();
        ConfigureHomeScale();
        ConfigureButtonHover(CurrentLocationButton);
        SettingsPrototypeView.SizeChanged += (_, _) => SizeIntegratedSettingsContent();
        ServicesPrototypeView.SizeChanged += (_, e) =>
        {
            if (ServicesPrototypeView.Visibility != Visibility.Visible || Math.Abs(e.NewSize.Width - _lastServicesLayoutWidth) < 8) return;
            _lastServicesLayoutWidth = e.NewSize.Width;
            RenderServicesPrototype();
        };
        ExternalNavigationPolicy.AskBeforeOpening = _settings.ExternalLinkBehavior == ExternalLinkBehavior.AskFirst;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt");
        _clockTimer.Start();
        _serviceControls = new ServiceControlsWindow();
        _serviceControls.BackRequested += (_, _) => BrowserBack_Click(this, new RoutedEventArgs());
        _serviceControls.ForwardRequested += (_, _) => BrowserForward_Click(this, new RoutedEventArgs());
        _serviceControls.ReloadRequested += (_, _) => BrowserReload_Click(this, new RoutedEventArgs());
        _serviceControls.RestartRequested += (_, _) => RestartActiveService();
        _serviceControls.ResetSiteDataRequested += (_, _) => _ = ResetActiveServiceSiteDataAsync();
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
        Loaded += (_, _) =>
        {
            _serviceControls.Owner = this;
            foreach (var button in FindVisualChildren<Button>(SettingsPrototypeView))
                if (button.Tag is string section && section is "General" or "Appearance" or "Streaming" or "VPN" or "Storage" or "About")
                    ConfigureButtonHover(button);
            // Constructor-time layout has no measured ScrollViewer viewport yet. Render again
            // after the first layout pass so Home uses the real width on first launch instead
            // of the narrow fallback until a later settings/favorite change happens.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RenderHome));
        };
        LocationChanged += (_, _) => PositionServiceControls();
        SizeChanged += (_, _) => PositionServiceControls();
        StateChanged += (_, _) => Dispatcher.BeginInvoke(PositionServiceControls);
        Closing += MainWindow_Closing;
        Closed += (_, _) => Application.Current.Shutdown();
        AttachLocationRouterEvents();
        UpdateCurrentLocationControl();
        RenderHome();
    }

    private void AttachLocationRouterEvents()
    {
        _locationRouter.Changed += (_, _) => Dispatcher.BeginInvoke(UpdateCurrentLocationControl);
        _locationRouter.ProgressChanged += (_, progress) => Dispatcher.BeginInvoke(() => UpdateLaunchStatusFromRoute(progress));
    }

    private void RenderHome()
    {
        ClockText.Visibility = _settings.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        // The quick selector belongs to the persistent shell. It always shows the active
        // state (Direct on first launch) and can guide setup without becoming a Settings page.
        CurrentLocationButton.Visibility = Visibility.Visible;
        var visibleServices = GetHomeServices().Where(service => !service.IsHiddenFromHome).ToList();
        var favorites = visibleServices.Where(service => service.IsFavorite).ToList();
        var services = visibleServices.Where(service => !service.IsFavorite).ToList();
        FavoritesPanel.Children.Clear();
        AllServicesPanel.Children.Clear();
        _favoriteBadges.Clear();
        AllServicesPanel.AllowDrop = _settings.ServiceSorting == ServiceSorting.Custom;
        RenderFavoriteRows(favorites);
        var railCardWidth = GetHomeRailCardWidth();
        // The viewport itself is exactly four cards plus three gaps. This is intentionally
        // constrained so the next card cannot appear as a distracting partial fifth card.
        // A small internal gutter gives the first/last card room for hover elevation without
        // clipping against the scroll viewport or either navigation arrow.
        const double railHoverGutter = 14;
        AllServicesPanel.Margin = new Thickness(railHoverGutter, 20, railHoverGutter, 20);
        OtherServicesScroll.Width = railCardWidth * 4 + 48 + railHoverGutter * 2;
        for (var index = 0; index < services.Count; index++)
        {
            var card = CreateTile(services[index], compact: true, preferredWidth: railCardWidth, homeLauncher: true);
            card.Margin = new Thickness(0, 0, index == services.Count - 1 ? 0 : 16, 8);
            AllServicesPanel.Children.Add(card);
        }

        FavoritesSection.Visibility = favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AllServicesSection.Visibility = services.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = visibleServices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateOtherServicesNavigationState));
        if (ServicesPrototypePanel is not null) RenderServicesPrototype();
    }

    private void RenderFavoriteRows(IReadOnlyList<ServiceDefinition> favorites)
    {
        if (favorites.Count == 0) return;
        var available = HomeScroll.ViewportWidth > 0 ? HomeScroll.ViewportWidth - 8 : Math.Max(640, ActualWidth - 104);
        const double gap = 16;
        // Favorites are intentionally a four-across launcher layout. Each final row is its
        // own centered StackPanel, so 1/2 cards never look stranded against the left edge.
        var cardWidth = Math.Clamp(Math.Floor((available - gap * 3) / 4), 240, 410);
        var fit = Math.Max(1, (int)Math.Floor((available + gap) / (cardWidth + gap)));
        var columns = Math.Min(4, fit);
        for (var index = 0; index < favorites.Count; index += columns)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
            var rowServices = favorites.Skip(index).Take(columns).ToList();
            for (var cardIndex = 0; cardIndex < rowServices.Count; cardIndex++)
            {
                var card = CreateTile(rowServices[cardIndex], compact: true, preferredWidth: cardWidth, homeLauncher: true);
                card.Margin = new Thickness(0, 0, cardIndex == rowServices.Count - 1 ? 0 : gap, 0);
                row.Children.Add(card);
            }
            FavoritesPanel.Children.Add(row);
        }
    }

    private double GetHomeRailCardWidth()
    {
        // Reserve the two rail arrows and their margins, then size the viewport for exactly
        // four complete cards in the actual visible rail viewport.
        var viewport = HomeScroll.ViewportWidth > 0 ? HomeScroll.ViewportWidth : Math.Max(760, ActualWidth);
        var available = Math.Max(620, viewport - HomeScroll.Padding.Left - HomeScroll.Padding.Right - 132);
        return Math.Clamp(Math.Floor((available - 48) / 4), 230, 410);
    }

    private void RenderServicesPrototype()
    {
        var query = ServicesPrototypeSearch?.Text?.Trim() ?? string.Empty;
        ServicesPrototypePanel.HorizontalAlignment = HorizontalAlignment.Center;
        ServicesPrototypePanel.Children.Clear();
        var cardWidth = GetServicesCardWidth();
        if (_showingBuiltInServiceLibrary)
        {
            ServicesHeadingText.Text = "Browse services";
            ServicesDescriptionText.Text = "Add, remove, or restore Monkey TV's built-in services.";
            ServicesLibraryButton.Content = "My services";
            ServicesSearchLabel.Text = "Search built-in services";
            ServicesCatalogControls.Visibility = Visibility.Visible;
            var region = (ServicesCatalogRegionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var templates = ServiceLibrarySelection.Filter(query, region);
            foreach (var template in templates)
                ServicesPrototypePanel.Children.Add(CreateBuiltInServiceTile(template, cardWidth));
            return;
        }

        ServicesHeadingText.Text = "Services";
        ServicesDescriptionText.Text = "Your personal streaming library.";
        ServicesLibraryButton.Content = "Browse services";
        ServicesSearchLabel.Text = "Search services";
        ServicesCatalogControls.Visibility = Visibility.Collapsed;
        var services = GetHomeServices()
            .Where(service => string.IsNullOrEmpty(query) || service.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        foreach (var service in services)
            ServicesPrototypePanel.Children.Add(CreateTile(service, compact: true, preferredWidth: cardWidth));
    }

    private Border CreateBuiltInServiceTile(BuiltInService template, double width)
    {
        var existing = ServiceLibrarySelection.FindConfigured(_settings, template);
        var banner = _settings.CardStyle == ServiceCardStyle.Banner;
        var height = Math.Floor(banner ? width * 9d / 16d : width);
        var card = new Border
        {
            Width = width,
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 20, 24)
        };
        var stack = new StackPanel();
        var artwork = new Border
        {
            Height = height,
            Background = (Brush)FindResource("ControlBrush"),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true
        };
        artwork.Child = CreateCardArtwork(existing ?? template.Create(), width, height, banner, showServiceName: true);
        stack.Children.Add(artwork);
        stack.Children.Add(new TextBlock
        {
            Text = template.Name,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 13, 2, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var customized = existing is not null && BuiltInServiceCatalog.IsCustomized(existing, template);
        stack.Children.Add(new TextBlock
        {
            Text = existing is null ? "Not added" : customized ? "Added · customized" : "Added",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 13,
            Margin = new Thickness(2, 4, 2, 14)
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (existing is null)
        {
            var add = new Button { Content = "Add", Style = (Style)FindResource("AccentButton"), MinWidth = 88 };
            add.Click += (_, _) => AddBuiltInService(template);
            actions.Children.Add(add);
        }
        else
        {
            var remove = new Button { Content = "Remove", MinWidth = 88 };
            remove.Click += (_, _) => RemoveBuiltInService(existing);
            actions.Children.Add(remove);
            if (customized)
            {
                var restore = new Button { Content = "Restore defaults", Margin = new Thickness(9, 0, 0, 0) };
                restore.Click += (_, _) => RestoreBuiltInService(existing, template);
                actions.Children.Add(restore);
            }
        }
        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private void AddBuiltInService(BuiltInService template)
    {
        if (ServiceLibrarySelection.FindConfigured(_settings, template) is not null) return;
        var match = BuiltInServiceCatalog.FindProbableCustomMatch(_settings.Services, template);
        if (match is not null)
        {
            var choice = new ConfirmWindow("Possible existing service",
                $"{match.Name} may already be your {template.Name} service. Add the built-in service anyway?", "Add built-in service") { Owner = this };
            if (choice.ShowDialog() != true) return;
        }
        _settings.Services.Add(template.Create());
        _store.Save(_settings);
        RenderHome();
    }

    private void RemoveBuiltInService(ServiceDefinition service)
    {
        var confirmation = new ConfirmWindow("Remove service?",
            $"Remove {service.Name}? Provider cookies, logins, and shared website data will be kept.", "Remove") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        StorageService.RemoveRetainedServiceProfileData(service);
        if (_webView is null) StorageService.CleanupQueuedSharedProfileDeletes();
        _settings.Services.Remove(service);
        _store.Save(_settings);
        StorageService.CleanupOrphanAssets(_settings);
        RenderHome();
    }

    private void RestoreBuiltInService(ServiceDefinition service, BuiltInService template)
    {
        var confirmation = new ConfirmWindow("Restore service defaults?",
            $"Restore Monkey TV's default name, URL, artwork, and Experience for {template.Name}?", "Restore") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        BuiltInServiceCatalog.RestoreDefaults(service, template);
        _store.Save(_settings);
        RenderHome();
    }

    private double GetServicesCardWidth()
    {
        var viewport = ServicesPrototypeView.ViewportWidth > 0 ? ServicesPrototypeView.ViewportWidth : Math.Max(760, ActualWidth);
        var available = Math.Max(620, viewport - ServicesPrototypeView.Padding.Left - ServicesPrototypeView.Padding.Right);
        var columns = available >= 2100 ? 5 : available >= 1640 ? 4 : available >= 1120 ? 3 : available >= 700 ? 2 : 1;
        return Math.Clamp(Math.Floor((available - 20 * (columns - 1)) / columns), 280, 410);
    }

    private IReadOnlyList<ServiceDefinition> GetHomeServices() => _settings.ServiceSorting switch
    {
        ServiceSorting.AlphabeticalAscending => _settings.Services.OrderBy(service => service.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        ServiceSorting.AlphabeticalDescending => _settings.Services.OrderByDescending(service => service.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        ServiceSorting.RecentlyAddedNewest => _settings.Services.OrderByDescending(service => service.AddedUtc).ToList(),
        ServiceSorting.RecentlyAddedOldest => _settings.Services.OrderBy(service => service.AddedUtc).ToList(),
        _ => _settings.Services
    };

    private Button CreateTile(ServiceDefinition service, bool compact = false, double? preferredWidth = null, bool homeLauncher = false)
    {
        var (width, height) = GetHomeCardSize();
        if (compact)
        {
            // Favorites are a real media rail, not tiny shortcut chips. Keep their artwork
            // close to the primary cards while still allowing several to sit in one row.
            width = Math.Min(width, 310);
            height = Math.Floor(_settings.CardStyle == ServiceCardStyle.Banner ? width * 9d / 16d : width);
        }
        if (preferredWidth is { } fixedWidth)
        {
            width = fixedWidth;
            height = Math.Floor(_settings.CardStyle == ServiceCardStyle.Banner ? width * 9d / 16d : width);
        }
        var banner = _settings.CardStyle == ServiceCardStyle.Banner;

        var button = new Button
        {
            Width = width,
            Margin = compact ? new Thickness(0, 0, 16, 4) : new Thickness(0, 0, 20, 28),
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
        var shadow = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 8, Opacity = .34, Direction = 270 };
        button.Effect = shadow;

        var content = new StackPanel();
        var cardBorder = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
        var card = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(banner ? 16 : 18),
            Background = CreateCardBackground(service),
            BorderBrush = cardBorder,
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        var cardContent = new Grid();
        cardContent.Children.Add(CreateCardArtwork(service, width, height, banner, _settings.ShowServiceNames));
        // The artwork retains priority, while this restrained scrim makes the affordance and
        // service identity legible on every artwork style.
        var scrim = new System.Windows.Shapes.Rectangle
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1),
                GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(168, 5, 6, 10), 1) }
            },
            Opacity = .34,
            IsHitTestVisible = false
        };
        cardContent.Children.Add(scrim);

        var launchPill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(224, 17, 18, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform(0, 7)
        };
        launchPill.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = "▶", FontSize = 10, Foreground = (Brush)FindResource("AccentBrush"), Margin = new Thickness(0, 1, 6, 0) },
                new TextBlock { Text = "Open service", FontSize = 12, FontWeight = FontWeights.SemiBold }
            }
        };
        cardContent.Children.Add(launchPill);

        if (service.IsFavorite)
        {
            var favorite = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(222, 19, 20, 27)),
                BorderBrush = (Brush)FindResource("AccentBrush"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 4, 7, 5),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10), IsHitTestVisible = false,
                RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            favorite.Child = new TextBlock { Text = "★", FontSize = 12, Foreground = (Brush)FindResource("AccentBrush") };
            cardContent.Children.Add(favorite);
            _favoriteBadges[service.Id] = favorite;
        }
        card.Child = cardContent;
        content.Children.Add(card);
        if (_settings.ShowServiceNames)
        {
            content.Children.Add(new TextBlock
            {
                Text = service.Name,
                FontSize = banner ? 17 : 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 11, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = width - 8
            });
        }

        button.Content = content;
        var hovered = false;
        var focused = false;
        var pressed = false;
        void UpdateCardHighlight()
        {
            var highlighted = hovered || focused;
            var targetScale = pressed ? (highlighted ? 1.018 : .988) : highlighted ? 1.028 : 1;
            AnimateCard(scale, lift, shadow, targetScale, pressed ? -3 : highlighted ? -6 : 0, pressed ? 29 : highlighted ? 33 : 20, pressed ? .56 : highlighted ? .64 : .34);
            AnimateCardBorder(cardBorder, highlighted);
            AnimateCardOverlay(scrim, launchPill, highlighted);
        }
        button.MouseEnter += (_, _) => { hovered = true; UpdateCardHighlight(); };
        button.MouseLeave += (_, _) => { hovered = false; UpdateCardHighlight(); };
        button.GotKeyboardFocus += (_, _) => { focused = true; UpdateCardHighlight(); };
        button.LostKeyboardFocus += (_, _) => { focused = false; UpdateCardHighlight(); };
        button.PreviewMouseLeftButtonDown += (_, _) => { pressed = true; UpdateCardHighlight(); };
        button.PreviewMouseLeftButtonUp += (_, _) => { pressed = false; UpdateCardHighlight(); };
        button.LostMouseCapture += (_, _) => { pressed = false; UpdateCardHighlight(); };
        if (_settings.ServiceSorting == ServiceSorting.Custom)
        {
            button.PreviewMouseLeftButtonDown += ServiceCard_MouseLeftButtonDown;
            button.PreviewMouseMove += ServiceCard_MouseMove;
        }
        button.Click += async (_, _) => await OpenServiceAsync(service);
        button.ContextMenu = CreateServiceContextMenu(service);
        return button;
    }

    private (double Width, double Height) GetHomeCardSize()
    {
        var available = HomeScroll.ViewportWidth > 0 ? HomeScroll.ViewportWidth - 80 : ActualWidth - 80;
        available = Math.Max(520, available);
        var columns = available >= 1040 ? 4 : available >= 760 ? 3 : 2;
        var width = (available - 20 * (columns - 1)) / columns;
        var (minimum, maximum) = _settings.TileSize switch { TileSize.Small => (180d, 240d), TileSize.Large => (250d, 350d), _ => (220d, 310d) };
        width = Math.Clamp(width, minimum, maximum);
        return (Math.Floor(width), Math.Floor(_settings.CardStyle == ServiceCardStyle.Banner ? width * 9 / 16 : width));
    }

    internal static Grid CreateCardArtwork(ServiceDefinition service, double width, double height, bool banner, bool showServiceName)
    {
        var grid = new Grid();
        // Banner cards always treat the best available image as artwork. Previously an icon
        // fallback was forced into a small centered logo surface, so BannerFit was silently
        // ignored. Both banner and icon sources now obey one explicit Fit/Fill contract.
        BitmapSource? artwork = banner
            ? TryLoadAsset(service.BannerPath) ?? TryLoadAsset(service.IconPath)
            : TryLoadAsset(service.IconPath) ?? TryLoadAsset(service.BannerPath);
        if (artwork is not null && banner)
        {
            artwork = TrimEmbeddedSideBars(artwork);
            grid.Children.Add(new Image
            {
                Source = artwork,
                Stretch = service.BannerFit == BannerFit.Fit ? Stretch.Uniform : Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            });
            return grid;
        }

        var icon = artwork;
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

        if (icon is null && !showServiceName)
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

    private static BitmapSource TrimEmbeddedSideBars(BitmapSource source)
    {
        // Some otherwise-good supplied banners are 16:9 files containing a narrower image
        // padded with solid black columns. WPF correctly sees the file as 16:9, which means
        // UniformToFill cannot remove those baked-in bars. Detect only sizeable, consistently
        // near-black edge runs; natural dark artwork with ordinary edge detail is untouched.
        if (source.PixelWidth < 160 || source.PixelHeight < 90) return source;
        try
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            bool IsSolidBlackColumn(int x)
            {
                for (var y = 0; y < converted.PixelHeight; y += 6)
                {
                    var offset = y * stride + x * 4;
                    if (pixels[offset] > 10 || pixels[offset + 1] > 10 || pixels[offset + 2] > 10)
                        return false;
                }
                return true;
            }

            var left = 0;
            while (left < converted.PixelWidth / 3 && IsSolidBlackColumn(left)) left++;
            var right = 0;
            while (right < converted.PixelWidth / 3 && IsSolidBlackColumn(converted.PixelWidth - 1 - right)) right++;

            // Ignore tiny compression-colored edges. Only deliberate bars are removed.
            var minimumBar = Math.Max(8, converted.PixelWidth / 100);
            if (left < minimumBar) left = 0;
            if (right < minimumBar) right = 0;
            var croppedWidth = converted.PixelWidth - left - right;
            if ((left == 0 && right == 0) || croppedWidth < converted.PixelWidth * .55) return source;

            var cropped = new CroppedBitmap(source, new Int32Rect(left, 0, croppedWidth, source.PixelHeight));
            if (cropped.CanFreeze) cropped.Freeze();
            return cropped;
        }
        catch
        {
            // Artwork must remain usable even if a decoder exposes an unexpected format.
            return source;
        }
    }

    private ContextMenu CreateServiceContextMenu(ServiceDefinition service)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => AnimateMenuOpen(menu);
        void Add(string header, Action action, bool enabled = true, bool destructive = false)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            if (destructive) item.Style = (Style)FindResource("DestructiveMenuItem");
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("Open", () => _ = OpenServiceAsync(service));
        Add("Edit", () => EditService(service));
        Add(service.IsFavorite ? "Remove from Favorites" : "Add to Favorites", () => ToggleFavorite(service));
        Add(service.IsHiddenFromHome ? "Show on Home" : "Hide from Home", () => ToggleHomeVisibility(service));
        menu.Items.Add(new Separator());
        var index = _settings.Services.IndexOf(service);
        Add("Move left", () => MoveService(service, -1), index > 0);
        Add("Move right", () => MoveService(service, 1), index >= 0 && index < _settings.Services.Count - 1);
        Add("Change banner…", () => ChangeBanner(service));
        Add("Clear shared temporary data…", () => ClearTemporaryDataWithConfirmation(service));
        menu.Items.Add(new Separator());
        Add("Remove…", () => RemoveService(service), destructive: true);
        return menu;
    }

    private async void ToggleFavorite(ServiceDefinition service)
    {
        if (service.IsFavorite && _favoriteBadges.TryGetValue(service.Id, out var existingBadge))
        {
            AnimateFavoriteBadge(existingBadge, appearing: false);
            if (!ThemeService.ReduceMotion) await Task.Delay(105);
        }
        service.IsFavorite = !service.IsFavorite;
        _store.Save(_settings);
        RenderHome();
        if (service.IsFavorite)
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (_favoriteBadges.TryGetValue(service.Id, out var badge)) AnimateFavoriteBadge(badge, appearing: true);
            }));
    }

    private void ToggleHomeVisibility(ServiceDefinition service)
    {
        service.IsHiddenFromHome = !service.IsHiddenFromHome;
        _store.Save(_settings);
        RenderHome();
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
        var editorSource = _unsavedEditorDrafts.TryGetValue(service.Id, out var draft) ? draft : service;
        var editor = new ServiceEditorWindow(editorSource);
        _activeServiceEditor = editor;
        editor.SetImmediateResetHandler(_ => ConfirmAndResetServiceDataAsync(service, reloadImmediately: false));
        editor.SetImplicitDismissalHandler(() => RequestImplicitEditorDismissal(editor));
        ServiceEditorHost.Content = editor.DetachForEmbeddedHost(completion =>
        {
            if (completion == ServiceEditorWindow.EmbeddedCompletion.ImplicitDismissalRequested)
            {
                RequestImplicitEditorDismissal(editor);
                return;
            }
            CloseServiceEditorDrawer(() =>
            {
                _activeServiceEditor = null;
                _serviceEditorDismissalInProgress = false;
                if (completion == ServiceEditorWindow.EmbeddedCompletion.TestRequested)
                {
                    // A test is deliberately in-memory only. Reopening Edit Service restores this
                    // draft, while Cancel discards it and Save remains the only persistence point.
                    _unsavedEditorDrafts[service.Id] = editor.Result;
                    _unsavedTestService = editor.Result;
                    _ = OpenServiceAsync(editor.Result);
                    return;
                }
                if (completion == ServiceEditorWindow.EmbeddedCompletion.Cancelled)
                {
                    _unsavedEditorDrafts.Remove(service.Id);
                    var afterCancel = _pendingEditorDismissal;
                    _pendingEditorDismissal = null;
                    afterCancel?.Invoke();
                    return;
                }
                _settings.Services[index] = editor.Result;
                _unsavedEditorDrafts.Remove(service.Id);
                if (_unsavedTestService?.Id == service.Id) _unsavedTestService = null;
                _store.Save(_settings); StorageService.CleanupOrphanAssets(_settings); RenderHome();
                var continuation = _pendingEditorDismissal;
                _pendingEditorDismissal = null;
                continuation?.Invoke();
            });
        });
        OpenServiceEditorDrawer();
    }

    private void AddServiceInDrawer()
    {
        var editor = new ServiceEditorWindow(defaultExperience: _settings.DefaultCustomServiceExperience);
        _activeServiceEditor = editor;
        editor.SetImplicitDismissalHandler(() => RequestImplicitEditorDismissal(editor));
        ServiceEditorHost.Content = editor.DetachForEmbeddedHost(completion =>
        {
            if (completion == ServiceEditorWindow.EmbeddedCompletion.ImplicitDismissalRequested)
            {
                RequestImplicitEditorDismissal(editor);
                return;
            }

            CloseServiceEditorDrawer(() =>
            {
                _activeServiceEditor = null;
                _serviceEditorDismissalInProgress = false;
                if (completion == ServiceEditorWindow.EmbeddedCompletion.TestRequested)
                {
                    // Testing a new service remains an in-memory preview. It does not add the
                    // draft to the library or save any configuration until Save is chosen.
                    _unsavedTestService = editor.Result;
                    _ = OpenServiceAsync(editor.Result);
                    return;
                }

                if (completion == ServiceEditorWindow.EmbeddedCompletion.Cancelled)
                {
                    var afterCancel = _pendingEditorDismissal;
                    _pendingEditorDismissal = null;
                    afterCancel?.Invoke();
                    return;
                }

                _settings.Services.Add(editor.Result);
                _store.Save(_settings);
                StorageService.CleanupOrphanAssets(_settings);
                RenderHome();
                var continuation = _pendingEditorDismissal;
                _pendingEditorDismissal = null;
                continuation?.Invoke();
            });
        });
        OpenServiceEditorDrawer();
    }

    private async void RequestImplicitEditorDismissal(ServiceEditorWindow editor, Action? continuation = null)
    {
        if (!ReferenceEquals(editor, _activeServiceEditor) || _serviceEditorDismissalInProgress) return;
        if (!editor.HasMeaningfulUnsavedChanges)
        {
            _serviceEditorDismissalInProgress = true;
            _pendingEditorDismissal = continuation;
            editor.DiscardFromUnsavedChangesPrompt();
            return;
        }

        _serviceEditorDismissalInProgress = true;
        try
        {
            var choice = editor.ShowUnsavedChangesPrompt();
            if (choice == UnsavedChangesChoice.KeepEditing) return;
            _pendingEditorDismissal = continuation;
            if (choice == UnsavedChangesChoice.DiscardChanges)
            {
                editor.DiscardFromUnsavedChangesPrompt();
                return;
            }

            if (!await editor.SaveFromUnsavedChangesPromptAsync())
                _pendingEditorDismissal = null;
        }
        catch (Exception ex)
        {
            _pendingEditorDismissal = null;
            AppLog.Write("service-editor", "Could not complete drawer dismissal: " + ex.Message);
        }
        finally
        {
            _serviceEditorDismissalInProgress = false;
        }
    }

    private void OpenServiceEditorDrawer()
    {
        _serviceEditorDrawerClosing = false;
        _serviceEditorDismissalInProgress = false;
        ServiceEditorOverlay.Visibility = Visibility.Visible;
        ServiceEditorScrim.BeginAnimation(OpacityProperty, null);
        ServiceEditorDrawerTransform.BeginAnimation(TranslateTransform.XProperty, null);
        if (ThemeService.ReduceMotion)
        {
            ServiceEditorScrim.Opacity = 1;
            ServiceEditorDrawerTransform.X = 0;
            return;
        }
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        ServiceEditorScrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        ServiceEditorDrawerTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(760, 0, duration) { EasingFunction = easing });
    }

    private void CloseServiceEditorDrawer(Action completed)
    {
        if (_serviceEditorDrawerClosing) return;
        _serviceEditorDrawerClosing = true;
        void Finish()
        {
            ServiceEditorScrim.BeginAnimation(OpacityProperty, null);
            ServiceEditorDrawerTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ServiceEditorScrim.Opacity = 0;
            ServiceEditorDrawerTransform.X = 760;
            ServiceEditorHost.Content = null;
            ServiceEditorOverlay.Visibility = Visibility.Collapsed;
            _serviceEditorDrawerClosing = false;
            UpdateCurrentLocationControl();
            completed();
        }
        if (ThemeService.ReduceMotion) { Finish(); return; }
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        ServiceEditorScrim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
        var slide = new DoubleAnimation(0, 760, duration) { EasingFunction = easing };
        slide.Completed += (_, _) => Finish();
        ServiceEditorDrawerTransform.BeginAnimation(TranslateTransform.XProperty, slide);
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

    internal static Brush CreateCardBackground(ServiceDefinition service)
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
        if (ThemeService.ReduceMotion)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            lift.BeginAnimation(TranslateTransform.YProperty, null); shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null); shadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            scale.ScaleX = scale.ScaleY = targetScale; lift.Y = targetY; shadow.BlurRadius = blur; shadow.Opacity = opacity;
            return;
        }
        var duration = new Duration(TimeSpan.FromMilliseconds(140));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
        lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(targetY, duration) { EasingFunction = easing });
        shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(blur, duration) { EasingFunction = easing });
        shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = easing });
    }

    private static void AnimateCardOverlay(UIElement scrim, FrameworkElement action, bool highlighted)
    {
        var scrimTarget = highlighted ? .72 : .34;
        var actionTarget = highlighted ? 1d : 0d;
        var actionY = highlighted ? 0d : 7d;
        if (ThemeService.ReduceMotion)
        {
            scrim.BeginAnimation(UIElement.OpacityProperty, null);
            action.BeginAnimation(UIElement.OpacityProperty, null);
            scrim.Opacity = scrimTarget; action.Opacity = actionTarget;
            if (action.RenderTransform is TranslateTransform transform) transform.Y = actionY;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        scrim.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(scrimTarget, TimeSpan.FromMilliseconds(170)) { EasingFunction = easing });
        action.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(actionTarget, TimeSpan.FromMilliseconds(150)) { EasingFunction = easing });
        if (action.RenderTransform is TranslateTransform lift)
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(actionY, TimeSpan.FromMilliseconds(170)) { EasingFunction = easing });
    }

    private static void AnimateCardBorder(SolidColorBrush border, bool highlighted)
    {
        var accent = Application.Current.Resources["AccentBrush"] as SolidColorBrush;
        var target = highlighted && accent is not null
            ? Color.FromArgb(245, accent.Color.R, accent.Color.G, accent.Color.B)
            : Color.FromArgb(42, 255, 255, 255);
        if (ThemeService.ReduceMotion)
        {
            border.BeginAnimation(SolidColorBrush.ColorProperty, null);
            border.Color = target;
            return;
        }
        border.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(target, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private static void AnimateFavoriteBadge(Border badge, bool appearing)
    {
        if (badge.RenderTransform is not ScaleTransform scale) return;
        var targetOpacity = appearing ? 1d : 0d;
        var fromScale = appearing ? .72 : 1d;
        var targetScale = appearing ? 1d : .72;
        if (ThemeService.ReduceMotion)
        {
            badge.BeginAnimation(UIElement.OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            badge.Opacity = targetOpacity; scale.ScaleX = scale.ScaleY = targetScale;
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(115));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        badge.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(targetOpacity, duration) { From = appearing ? 0 : null, EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, duration) { From = fromScale, EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, duration) { From = fromScale, EasingFunction = easing });
    }

    private static void AnimateMenuOpen(ContextMenu menu)
    {
        menu.BeginAnimation(UIElement.OpacityProperty, null);
        if (ThemeService.ReduceMotion) { menu.Opacity = 1; return; }
        menu.Opacity = 0;
        menu.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(90)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private static void AnimateElementIn(FrameworkElement element, double offset = 7, int milliseconds = 135)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (element.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        if (ThemeService.ReduceMotion)
        {
            element.Opacity = 1; translate.Y = 0;
            return;
        }

        element.Opacity = 0;
        translate.Y = offset;
        var duration = new Duration(TimeSpan.FromMilliseconds(milliseconds));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = easing });
    }

    private static void AnimateSelectionPulse(FrameworkElement element)
    {
        if (ThemeService.ReduceMotion) return;
        if (element.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransformOrigin = new Point(.5, .5);
            element.RenderTransform = scale;
        }
        var duration = new Duration(TimeSpan.FromMilliseconds(100));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { From = 1.018, EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { From = 1.018, EasingFunction = easing });
    }

    private void ConfigureLargeShellChrome()
    {
        // The navigation is part of the same high-readability desktop surface as Settings.
        // It remains compact enough for mouse/keyboard use, but no longer reads like links
        // from a small web page.
        foreach (var nav in new[] { HomeNavButton, ServicesNavButton, SettingsNavButton })
        {
            nav.FontSize = 26;
            nav.Padding = new Thickness(30, 18, 30, 18);
            nav.Margin = new Thickness(8, 0, 8, 0);
        }
        CurrentLocationButton.FontSize = 22;
        CurrentLocationButton.Padding = new Thickness(22, 15, 22, 15);
        CurrentLocationButton.Margin = new Thickness(0, 0, 24, 0);
        CurrentLocationText.FontSize = 22;
        foreach (var icon in FindLogicalChildren<TextBlock>(CurrentLocationButton))
            if (!ReferenceEquals(icon, CurrentLocationText)) icon.FontSize = 21;
        ClockText.FontSize = 27;
        ClockText.Margin = new Thickness(0, 0, 26, 0);
        FullscreenButton.Width = 66;
        FullscreenButton.Height = 66;
        FullscreenButton.Margin = new Thickness(4, 0, 0, 0);
        FullscreenGlyph.FontSize = 25;
    }

    private void ConfigureHomeScale()
    {
        // Home uses larger launcher-scale artwork and section controls, but remains denser
        // than Settings so additional favorites can wrap without overwhelming the page.
        foreach (var heading in FindLogicalChildren<TextBlock>(HomeScroll))
        {
            if (heading.Text is "Favorites" or "Other Services")
            {
                heading.FontSize = 30;
                heading.FontWeight = FontWeights.SemiBold;
            }
        }
        foreach (var button in FindLogicalChildren<Button>(HomeScroll))
        {
            if (button.Content is "View all services")
            {
                button.FontSize = 18;
                button.MinHeight = 58;
                button.Padding = new Thickness(28, 15, 28, 15);
            }
            else if (button.Content is "‹" or "›")
            {
                button.Width = 64;
                button.Height = 82;
                button.FontSize = 38;
            }
        }
    }

    private static void ConfigureButtonHover(Button button)
    {
        button.RenderTransformOrigin = new Point(.5, .5);
        button.RenderTransform = new ScaleTransform(1, 1);
        button.MouseEnter += (_, _) => AnimateButtonScale(button, 1.018);
        button.MouseLeave += (_, _) => AnimateButtonScale(button, 1);
        button.PreviewMouseLeftButtonDown += (_, _) => AnimateButtonScale(button, .985);
        button.PreviewMouseLeftButtonUp += (_, _) => AnimateButtonScale(button, button.IsMouseOver ? 1.018 : 1);
        button.LostMouseCapture += (_, _) => AnimateButtonScale(button, button.IsMouseOver ? 1.018 : 1);
    }

    private static void AnimateButtonScale(Button button, double target)
    {
        if (button.RenderTransform is not ScaleTransform scale) return;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (ThemeService.ReduceMotion)
        {
            scale.ScaleX = scale.ScaleY = target;
            return;
        }
        var duration = new Duration(TimeSpan.FromMilliseconds(95));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration) { EasingFunction = easing });
    }

    private void HomeScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - _lastHomeLayoutWidth) < 8) return;
        _lastHomeLayoutWidth = e.NewSize.Width;
        if (IsLoaded) RenderHome();
    }

    private void OtherServicesPrevious_Click(object sender, RoutedEventArgs e) =>
        _ = ScrollOtherServicesByAsync(-GetHomeRailCardWidth() - 16);

    private void OtherServicesNext_Click(object sender, RoutedEventArgs e) =>
        _ = ScrollOtherServicesByAsync(GetHomeRailCardWidth() + 16);

    private void OtherServicesScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        UpdateOtherServicesNavigationState();

    private void UpdateOtherServicesNavigationState()
    {
        const double edgeTolerance = 0.5;
        var canScrollLeft = OtherServicesScroll.ScrollableWidth > edgeTolerance &&
                            OtherServicesScroll.HorizontalOffset > edgeTolerance;
        var canScrollRight = OtherServicesScroll.ScrollableWidth > edgeTolerance &&
                             OtherServicesScroll.HorizontalOffset < OtherServicesScroll.ScrollableWidth - edgeTolerance;

        OtherServicesPreviousButton.IsEnabled = canScrollLeft;
        OtherServicesPreviousButton.Opacity = canScrollLeft ? 1 : 0.35;
        OtherServicesNextButton.IsEnabled = canScrollRight;
        OtherServicesNextButton.Opacity = canScrollRight ? 1 : 0.35;
    }

    private void OtherServicesScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Math.Abs(e.Delta) < 1) return;
        _ = ScrollOtherServicesByAsync(e.Delta < 0 ? 150 : -150);
        e.Handled = true;
    }

    private void OtherServicesScroll_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Right or Key.Down) { _ = ScrollOtherServicesByAsync(GetHomeRailCardWidth() + 16); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Up) { _ = ScrollOtherServicesByAsync(-GetHomeRailCardWidth() - 16); e.Handled = true; }
        else if (e.Key == Key.Home) { OtherServicesScroll.ScrollToHorizontalOffset(0); e.Handled = true; }
        else if (e.Key == Key.End) { OtherServicesScroll.ScrollToHorizontalOffset(OtherServicesScroll.ScrollableWidth); e.Handled = true; }
    }

    private async Task ScrollOtherServicesByAsync(double distance)
    {
        if (_railScrollAnimating || OtherServicesScroll.ScrollableWidth <= 0) return;
        var start = OtherServicesScroll.HorizontalOffset;
        var target = Math.Clamp(start + distance, 0, OtherServicesScroll.ScrollableWidth);
        if (ThemeService.ReduceMotion) { OtherServicesScroll.ScrollToHorizontalOffset(target); return; }
        _railScrollAnimating = true;
        try
        {
            const int steps = 8;
            for (var step = 1; step <= steps; step++)
            {
                var progress = step / (double)steps;
                var eased = 1 - Math.Pow(1 - progress, 3);
                OtherServicesScroll.ScrollToHorizontalOffset(start + (target - start) * eased);
                await Task.Delay(14);
            }
        }
        finally { _railScrollAnimating = false; }
    }

    private void ServiceCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button) { _dragSource = button; _dragStart = e.GetPosition(this); }
    }

    private void ServiceCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource is not { Tag: ServiceDefinition } source || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragStart.X) < 8 && Math.Abs(point.Y - _dragStart.Y) < 8) return;
        _dragSource.Opacity = .55;
        try { DragDrop.DoDragDrop(_dragSource, new DataObject(typeof(ServiceDefinition), source), DragDropEffects.Move); }
        finally { if (_dragSource is not null) _dragSource.Opacity = 1; _dragSource = null; }
    }

    private void ServicesPanel_DragOver(object sender, DragEventArgs e)
    {
        if (_settings.ServiceSorting != ServiceSorting.Custom) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = e.Data.GetDataPresent(typeof(ServiceDefinition)) ? DragDropEffects.Move : DragDropEffects.None;
        SetDropTarget(FindParent<Button>(e.OriginalSource as DependencyObject));
        e.Handled = true;
    }

    private void ServicesPanel_DragLeave(object sender, DragEventArgs e) { SetDropTarget(null); e.Handled = true; }

    private void ServicesPanel_Drop(object sender, DragEventArgs e)
    {
        if (_settings.ServiceSorting != ServiceSorting.Custom) return;
        SetDropTarget(null);
        if (!e.Data.GetDataPresent(typeof(ServiceDefinition)) || e.Data.GetData(typeof(ServiceDefinition)) is not ServiceDefinition source) return;
        var target = FindParent<Button>(e.OriginalSource as DependencyObject);
        if (target?.Tag is ServiceDefinition destination && ReferenceEquals(source, destination)) return;
        var from = _settings.Services.IndexOf(source); var to = target?.Tag is ServiceDefinition targetService ? _settings.Services.IndexOf(targetService) : _settings.Services.Count;
        if (from < 0 || to < 0) return;
        if (target is not null && e.GetPosition(target).X > target.ActualWidth / 2) to++;
        _settings.Services.RemoveAt(from); if (from < to) to--; _settings.Services.Insert(Math.Clamp(to, 0, _settings.Services.Count), source);
        _store.Save(_settings); RenderHome(); e.Handled = true;
    }

    private void SetDropTarget(Button? button)
    {
        if (ReferenceEquals(_dragOverCard, button)) return;
        SetCardBorder(_dragOverCard, new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), 1);
        _dragOverCard = button;
        SetCardBorder(_dragOverCard, (Brush)FindResource("AccentBrush"), 2);
    }

    private static void SetCardBorder(Button? button, Brush brush, double thickness)
    {
        if (button?.Content is StackPanel { Children: { Count: > 0 } } stack && stack.Children[0] is Border card)
        { card.BorderBrush = brush; card.BorderThickness = new Thickness(thickness); }
    }

    private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null) { if (element is T match) return match; element = VisualTreeHelper.GetParent(element); }
        return null;
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
        if (!await ConfirmPreferredVpnLocationForLaunchAsync(service)) return;
        var launchAlreadyPrepared = _launchTransitionActive && _launchServiceId == service.Id;

        var forceFreshReload = _servicesRequiringFreshReload.Remove(service.Id);
        if (_webView?.CoreWebView2 is { } existingCore && _servicesWithPendingDataReset.Remove(service.Id))
            await ResetServiceDataCoreAsync(service, existingCore);

        // A normal card click after testing a draft must return to the saved configuration,
        // rather than resuming the temporary draft that happened to share the same service ID.
        if (_unsavedTestService is { } draft && draft.Id == service.Id && !ReferenceEquals(draft, service))
        {
            _unsavedTestService = null;
            _activeServiceId = null;
        }
        var isUnsavedTest = ReferenceEquals(_unsavedTestService, service);
        // Any service selection supersedes a still-pending Home suspension request.
        ++_homeSuspensionGeneration;
        if (_settings.LastServiceId != service.Id)
        {
            _settings.LastServiceId = service.Id;
            _store.Save(_settings);
        }
        if (_activeServiceId == service.Id && (_webView is not null || _isBrowserLaunching) && !isUnsavedTest && !forceFreshReload)
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
            ShowServiceError(service, "Choose or retry a VPN Location before opening this service.", "The selected location route is not ready.");
            return;
        }

        HomeView.Visibility = Visibility.Collapsed;
        BrowserView.Visibility = Visibility.Visible;
        if (!launchAlreadyPrepared) BeginServiceLaunch(service);

        // Build #2 is a single-tab browser. A different service changes the destination of the
        // same hidden WebView instead of creating another WebView or profile.
        if (_webView?.CoreWebView2 is not null)
        {
            var isDifferentService = _activeServiceId is { } activeServiceId && activeServiceId != service.Id;
            _browserProcessFailed = false;
            _activeServiceId = service.Id;
            try { _webView.CoreWebView2.Resume(); } catch { }
            // A destination change is the right time for the same bounded, safe temporary-data
            // cleanup available from Settings. A same-service fresh reload keeps its narrower
            // disk-cache refresh behavior, and Home resume never reaches this branch.
            UpdateLaunchStatus("Preparing browser…");
            if (isDifferentService)
                await ClearTemporaryDataAsync();
            else if (forceFreshReload)
                await StorageService.ClearActiveDiskCacheAsync(_webView.CoreWebView2.Profile);
            UpdateLaunchStatusForExperience(service);
            ApplyServiceSettings(_webView, service);
            UpdateLaunchStatus($"Loading {service.Name}…");
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
            UpdateLaunchStatus("Preparing browser…");
            var proxyPort = _locationRouter.EnsureGateway();
            var options = new CoreWebView2EnvironmentOptions
            {
                // Narrow Feature #0.01 exception: one fixed loopback routing boundary.
                AdditionalBrowserArguments = $"--proxy-server=http://127.0.0.1:{proxyPort} --disable-quic --force-webrtc-ip-handling-policy=disable_non_proxied_udp"
            };
            // Fixed Version is preferred when the separately installed runtime pack
            // is present. Evergreen remains the v1/SOCKS fallback only; v2 must
            // require a Fixed runtime before it installs any WFP/VPN state.
            _browserRuntime = WebViewRuntimeResolver.Select();
            if (_browserRuntime.IsFixed)
                AppLog.Write("browser", $"Using Fixed Version WebView2 {_browserRuntime.Version}.");
            else
                AppLog.Write("browser", "Using Evergreen WebView2. Current Location v2 is unavailable: " + _browserRuntime.Detail);
            var environment = await CoreWebView2Environment.CreateAsync(_browserRuntime.IsFixed ? _browserRuntime.BrowserExecutableFolder : null, PortablePaths.SharedBrowserUserDataDirectory, options);
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
                // During Switch & Open the existing page belongs to the previous service.
                // It stays hidden while the route changes and must not navigate or escape to
                // the default browser before the requested service becomes active.
                if (IsSwitchingAwayFromActiveService())
                {
                    e.Cancel = true;
                    return;
                }
                if (e.IsUserInitiated && ActiveService is { } originService &&
                    ExternalNavigationPolicy.TryHttpUri(originService.Url, out var origin) &&
                    ExternalNavigationPolicy.TryHttpUri(e.Uri, out var target) &&
                    !ExternalNavigationPolicy.IsRelatedOrAuthentication(origin, target))
                {
                    e.Cancel = true;
                    ExternalNavigationPolicy.OpenInDefaultBrowser(target.AbsoluteUri);
                    return;
                }
                _lastNavigationUrl = SafeDiagnosticUrl(e.Uri);
                // Some providers complete an intermediate account/login redirect with Unknown
                // before the next redirect starts. Keep that narrow case alive for the next
                // navigation instead of showing a false browser error over the login flow.
                var current = ExternalNavigationPolicy.TryHttpUri(view.Source?.AbsoluteUri, out var currentUri) ? currentUri : null;
                if (ExternalNavigationPolicy.TryHttpUri(e.Uri, out var navigationUri) &&
                    ExternalNavigationPolicy.IsAmbiguousAuthenticationRedirect(
                        ActiveService is { } active && ExternalNavigationPolicy.TryHttpUri(active.Url, out var serviceUri) ? serviceUri : null,
                        current, navigationUri))
                    _ambiguousAuthenticationNavigationIds.Add(e.NavigationId);
                _serviceNavigationLoading = true;
                UpdateNavigationControls(view);
                if (_launchTransitionActive && _launchServiceId == _activeServiceId)
                {
                    _launchNavigationId = e.NavigationId;
                    UpdateLaunchStatus($"Waiting for {ActiveService?.Name ?? "service"}…");
                }
            });
            view.CoreWebView2.HistoryChanged += (_, _) => Dispatcher.Invoke(() => UpdateNavigationControls(view));
            view.CoreWebView2.DOMContentLoaded += (_, e) => Dispatcher.Invoke(() =>
            {
                if (generation != _browserGeneration || !ReferenceEquals(_webView, view)) return;
                // Routed provider pages can keep WebView2's final NavigationCompleted event
                // pending long after their document is usable. DOMContentLoaded is a real
                // browser lifecycle milestone, so reveal the service as soon as its current
                // top-level document is ready; route verification still occurred before the
                // navigation and remains the fail-closed gate.
                if (_launchTransitionActive && _launchServiceId == _activeServiceId &&
                    _launchNavigationId == e.NavigationId)
                {
                    UpdateLaunchStatus("Finishing up…");
                    CompleteServiceLaunch(view);
                }
            });
            view.CoreWebView2.ContainsFullScreenElementChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                if (generation == _browserGeneration && ReferenceEquals(_webView, view))
                    SetWebsiteFullscreen(view.CoreWebView2.ContainsFullScreenElement);
            });
            view.NavigationCompleted += (_, e) =>
            {
                if (generation != _browserGeneration || !ReferenceEquals(_webView, view)) return;
                var ambiguousAuthenticationRedirect = _ambiguousAuthenticationNavigationIds.Remove(e.NavigationId);
                _serviceNavigationLoading = false;
                Dispatcher.Invoke(() => UpdateNavigationControls(view));
                if (_launchTransitionActive)
                {
                    // A new service navigation cancels the previous top-level navigation. Ignore
                    // that stale completion instead of showing its failure against the new service.
                    if (_launchServiceId != _activeServiceId || _launchNavigationId != e.NavigationId) return;
                    if (e.IsSuccess)
                    {
                        UpdateLaunchStatus("Finishing up…");
                        CompleteServiceLaunch(view);
                    }
                    else if (e.WebErrorStatus == CoreWebView2WebErrorStatus.Unknown && ambiguousAuthenticationRedirect)
                    {
                        // Wait for the provider's follow-up redirect or the existing launch timeout.
                    }
                    else if (ActiveService is { } launchingService)
                        ShowNavigationError(launchingService, e.WebErrorStatus, "WebView2 NavigationCompleted");
                    return;
                }

                if (!e.IsSuccess && e.WebErrorStatus.ToString() != "OperationCanceled" &&
                    !(e.WebErrorStatus == CoreWebView2WebErrorStatus.Unknown && ambiguousAuthenticationRedirect) &&
                    BrowserView.Visibility == Visibility.Visible && ActiveService is { } active)
                    ShowNavigationError(active, e.WebErrorStatus, "WebView2 NavigationCompleted");
                else if (e.IsSuccess)
                    _ = EnforceActiveCacheBudgetAsync(view, generation);
            };
            view.CoreWebView2.ProcessFailed += (_, e) => Dispatcher.Invoke(() =>
            {
                if (generation != _browserGeneration || !ReferenceEquals(_webView, view)) return;
                _browserProcessFailed = true;
                if (ActiveService is { } active)
                    ShowServiceError(active, "The browser component stopped responding or closed unexpectedly.", "WebView2 process failure: " + e.ProcessFailedKind, "WebView2 ProcessFailed");
            });
            if (_servicesWithPendingDataReset.Remove(service.Id))
                await ResetServiceDataCoreAsync(service, view.CoreWebView2);
            UpdateLaunchStatusForExperience(service);
            ApplyServiceSettings(view, service);
            UpdateLaunchStatus($"Loading {service.Name}…");
            view.CoreWebView2.Navigate(service.Url);
            _isBrowserLaunching = false;
        }
        catch (Exception ex)
        {
            if (generation == _browserGeneration)
            {
                _isBrowserLaunching = false;
                ShowServiceError(service, "Monkey TV couldn't start the browser for this service.", "Browser startup exception: " + ex.GetType().Name, "Monkey TV browser startup");
            }
        }
    }

    private async Task<bool> ConfirmPreferredVpnLocationForLaunchAsync(ServiceDefinition service)
    {
        var preference = service.PreferredVpnLocation;
        if (preference == PreferredVpnLocation.None || PreferredVpnLocationPolicy.Matches(
                preference, _locationRouter.State, _locationRouter.ActiveLocation, _locationRouter.ActiveVpnV2Location))
            return true;

        var preferredName = PreferredVpnLocationPolicy.DisplayName(preference);
        var currentName = _locationRouter.State switch
        {
            CurrentLocationState.Direct => "Direct",
            CurrentLocationState.Connecting => "a location that is still connecting",
            CurrentLocationState.Failed => "a failed location connection",
            _ => _locationRouter.DisplayName == "United States" ? "USA" : _locationRouter.DisplayName
        };
        var prompt = new PreferredVpnLaunchWindow(service.Name, preferredName, currentName) { Owner = this };
        prompt.ShowDialog();
        if (prompt.Choice == PreferredVpnLaunchChoice.Cancel) return false;
        if (prompt.Choice == PreferredVpnLaunchChoice.OpenAnyway) return true;

        // The user explicitly chose Switch & Open. Show the existing launch surface before
        // invoking the existing verified route switch, so it can report real route stages.
        HomeView.Visibility = Visibility.Collapsed;
        BrowserView.Visibility = Visibility.Visible;
        BeginServiceLaunch(service);
        UpdateLaunchStatus("Checking preferred VPN Location…");
        _preferredRouteRedirectedToCredentialSetup = false;
        await SwitchToPreferredVpnLocationAsync(preference);
        if (_preferredRouteRedirectedToCredentialSetup)
        {
            _preferredRouteRedirectedToCredentialSetup = false;
            return false;
        }
        var ready = PreferredVpnLocationPolicy.Matches(
            preference, _locationRouter.State, _locationRouter.ActiveLocation, _locationRouter.ActiveVpnV2Location);
        if (!ready)
        {
            ShowServiceError(service, $"Monkey TV couldn't safely connect to {preferredName}.",
                $"The requested preferred VPN Location was not verified. {service.Name} was not opened.", "VPN Location");
        }
        return ready;
    }

    private async Task SwitchToPreferredVpnLocationAsync(PreferredVpnLocation preference)
    {
        if (preference == PreferredVpnLocation.Direct)
        {
            await ChangeLocationAsync(null, reloadActiveService: false);
            return;
        }

        if (preference == PreferredVpnLocation.USA)
        {
            await ChangeLocationAsync(NordLocationCatalog.Get(NordLocationId.UnitedStates), reloadActiveService: false);
            return;
        }

        var country = preference == PreferredVpnLocation.Canada ? "Canada" : "United Kingdom";
        var config = VpnV2ConfigStore.Load().FirstOrDefault(item =>
            item.Country.Equals(country, StringComparison.OrdinalIgnoreCase));
        if (config is null)
        {
            new ConfirmWindow("VPN Location isn't ready",
                $"Prepare {country} in Settings → VPN Location before opening this service with its preferred location.", "OK")
                { Owner = this }.ShowDialog();
            return;
        }

        await ChangeVpnV2LocationAsync(config, reloadActiveService: false);
    }

    private ServiceDefinition? ActiveService => _activeServiceId is not { } id ? null :
        _unsavedTestService is { } draft && draft.Id == id ? draft :
        _settings.Services.FirstOrDefault(service => service.Id == id);

    private bool IsSwitchingAwayFromActiveService() =>
        _launchTransitionActive && _launchServiceId is { } launchId &&
        _activeServiceId is { } activeId && launchId != activeId;

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
            if (IsSwitchingAwayFromActiveService()) return;
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
            await popup.InitializeAsync(_browserEnvironment, _webView?.CoreWebView2?.Settings.UserAgent, WebView_PermissionRequested);
            e.NewWindow = popup.Core;
        }
        catch (Exception ex) { AppLog.Write("popup", ex.Message); }
        finally { deferral.Complete(); }
    }

    private async void WebView_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
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

            if (!SessionPermissionStore.TryCreateKey(e.Uri, e.PermissionKind, out var permissionKey))
            {
                e.State = CoreWebView2PermissionState.Deny;
                return;
            }

            var label = e.PermissionKind switch
            {
                CoreWebView2PermissionKind.Camera => "camera",
                CoreWebView2PermissionKind.Microphone => "microphone",
                CoreWebView2PermissionKind.Geolocation => "location",
                _ => "clipboard"
            };
            e.State = await _sessionPermissions.GetOrPromptAsync(permissionKey, () => Dispatcher.InvokeAsync(() =>
            {
                var displayOrigin = new Uri(permissionKey.Origin).Host;
                var dialog = new ConfirmWindow($"Allow {label}?",
                    $"{displayOrigin} is requesting access to your {label}. Your choice will be remembered for this site until its website data is reset.",
                    "Allow", cancelLabel: "Deny", requestBadge: "WEBSITE PERMISSION", requestOrigin: displayOrigin, requestedCapability: label) { Owner = this };
                return dialog.ShowDialog() == true ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            }).Task);
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
        ++_launchStatusVersion;
        LoadingText.Text = $"Opening {service.Name}";
        LoadingStatusText.Text = "Preparing service…";
        LoadingStatusText.Opacity = 1;
        SetLaunchArtwork(service);
        AnimateElementIn(LoadingOverlay, 5, 115);
    }

    private void SetLaunchArtwork(ServiceDefinition service)
    {
        LoadingArtwork.Source = null;
        LoadingArtwork.Visibility = Visibility.Collapsed;
        LoadingInitial.Text = string.IsNullOrWhiteSpace(service.Name) ? "?" : service.Name[..1].ToUpperInvariant();
        LoadingInitial.Visibility = Visibility.Visible;

        var artwork = TryLoadAsset(service.BannerPath) ?? TryLoadAsset(service.IconPath);
        if (artwork is null) return;
        LoadingArtwork.Stretch = service.BannerFit == BannerFit.Fit ? Stretch.Uniform : Stretch.UniformToFill;
        LoadingArtwork.Source = artwork;
        LoadingArtwork.Visibility = Visibility.Visible;
        LoadingInitial.Visibility = Visibility.Collapsed;
    }

    private void CompleteServiceLaunch(WebView2 view)
    {
        UpdateLaunchStatus("Ready");
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
        ++_launchStatusVersion;
        _launchTimeoutTimer.Stop();
        _launchTransitionActive = false;
        _launchServiceId = null;
        _launchNavigationId = null;
    }

    private void UpdateLaunchStatusForExperience(ServiceDefinition service)
    {
        UpdateLaunchStatus(ServiceLaunchProgress.ForExperience(service));
    }

    private void UpdateLaunchStatusFromRoute(LocationRouteProgress progress)
    {
        if (!_launchTransitionActive || LoadingOverlay.Visibility != Visibility.Visible) return;
        UpdateLaunchStatus(ServiceLaunchProgress.ForRoute(progress));
    }

    private void UpdateLaunchStatus(string status)
    {
        if (!_launchTransitionActive || string.Equals(LoadingStatusText.Text, status, StringComparison.Ordinal)) return;
        var version = ++_launchStatusVersion;
        if (ThemeService.ReduceMotion)
        {
            LoadingStatusText.BeginAnimation(OpacityProperty, null);
            LoadingStatusText.Text = status;
            LoadingStatusText.Opacity = 1;
            return;
        }

        LoadingStatusText.BeginAnimation(OpacityProperty, null);
        var fadeOut = new DoubleAnimation(0.2, TimeSpan.FromMilliseconds(75));
        fadeOut.Completed += (_, _) =>
        {
            if (version != _launchStatusVersion || !_launchTransitionActive) return;
            LoadingStatusText.Text = status;
            LoadingStatusText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(105)));
        };
        LoadingStatusText.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void LaunchTimeoutTimer_Tick(object? sender, EventArgs e)
    {
        _launchTimeoutTimer.Stop();
        if (!_launchTransitionActive || ActiveService is not { } service) return;
        try { _webView?.CoreWebView2?.Stop(); } catch { }
        ShowServiceError(service, "The website did not finish loading within Monkey TV's navigation time limit.", "The initial navigation did not complete within 45 seconds.", "Monkey TV navigation timeout", timedOut: true);
    }

    private void ShowNavigationError(ServiceDefinition service, CoreWebView2WebErrorStatus status, string source)
    {
        var message = status.ToString() switch
        {
            "HostNameNotResolved" => "The website address couldn't be found.",
            "CannotConnect" or "ServerUnreachable" => "Monkey TV couldn't connect to the website.",
            "ConnectionRefused" => "The website refused the connection.",
            "ConnectionAborted" or "ConnectionReset" or "Disconnected" => "The connection was interrupted.",
            "Timeout" => "The website took too long to respond.",
            "CertificateCommonNameIsIncorrect" or "CertificateExpired" or "CertificateIsInvalid" or "CertificateRevoked" => "Monkey TV couldn't establish a secure connection.",
            "ErrorHttpInvalidServerResponse" => "The website returned an invalid server response.",
            _ => "Monkey TV couldn't connect to the website."
        };
        ShowServiceError(service, message, "WebView2 navigation status: " + status, source, status.ToString());
    }

    private void ShowServiceError(ServiceDefinition service, string message, string details, string source = "Monkey TV", string? webErrorStatus = null, bool timedOut = false)
    {
        EndServiceLaunchTransition();
        _activeServiceId = service.Id;
        LoadingOverlay.Visibility = Visibility.Collapsed; ErrorOverlay.Visibility = Visibility.Visible;
        AnimateElementIn(ErrorOverlay, 5, 120);
        SetBrowserMuted(_webView, true);
        if (_webView is not null) _webView.Visibility = Visibility.Collapsed;
        SetErrorArtwork(service);
        ErrorTitle.Text = $"Couldn't load {service.Name}"; ErrorMessage.Text = message;
        _lastErrorDiagnostic = BuildErrorDiagnostic(service, details, source, webErrorStatus, timedOut);
        ErrorDetails.Text = _lastErrorDiagnostic;
        AppLog.Write("browser", $"{service.Name}: {details}");
        StopServiceUi();
    }

    private void SetErrorArtwork(ServiceDefinition service)
    {
        ErrorArtwork.Source = null;
        ErrorArtwork.Visibility = Visibility.Collapsed;
        ErrorInitial.Text = string.IsNullOrWhiteSpace(service.Name) ? "?" : service.Name[..1].ToUpperInvariant();
        ErrorInitial.Visibility = Visibility.Visible;
        var artwork = TryLoadAsset(service.BannerPath) ?? TryLoadAsset(service.IconPath);
        if (artwork is null) return;
        ErrorArtwork.Stretch = service.BannerFit == BannerFit.Fit ? Stretch.Uniform : Stretch.UniformToFill;
        ErrorArtwork.Source = artwork;
        ErrorArtwork.Visibility = Visibility.Visible;
        ErrorInitial.Visibility = Visibility.Collapsed;
    }

    private string BuildErrorDiagnostic(ServiceDefinition service, string details, string source, string? webErrorStatus, bool timedOut)
    {
        var configured = SafeDiagnosticUrl(service.Url) ?? "Not available";
        return $"Monkey TV version: v{App.DisplayVersion} — Build #{App.BuildNumber}\nService: {service.Name}\nConfigured URL: {configured}\nFailing URL: {_lastNavigationUrl ?? "Not available"}\nError source: {source}\nWebErrorStatus: {webErrorStatus ?? "Not available"}\nHTTP status: Not available\nMonkey TV navigation timeout: {timedOut.ToString().ToLowerInvariant()}\nTimestamp: {DateTimeOffset.Now:O}\nDetails: {details}";
    }

    private static string? SafeDiagnosticUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        return uri.GetLeftPart(UriPartial.Path);
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
        UpdateLaunchStatusForExperience(service);
        ApplyServiceSettings(_webView, service);
        UpdateLaunchStatus($"Loading {service.Name}…");
        _webView.CoreWebView2.Navigate(service.Url);
    }

    private void RestartActiveService()
    {
        if (ActiveService is not { } service || _webView?.CoreWebView2 is null) return;
        BeginServiceLaunch(service);
        UpdateLaunchStatusForExperience(service);
        ApplyServiceSettings(_webView, service);
        UpdateLaunchStatus($"Loading {service.Name}…");
        _webView.CoreWebView2.Navigate(service.Url);
    }

    private async Task ResetActiveServiceSiteDataAsync()
    {
        if (ActiveService is not { } service || _webView?.CoreWebView2 is null) return;
        await ConfirmAndResetServiceDataAsync(service, reloadImmediately: true);
    }

    private async Task<bool> ConfirmAndResetServiceDataAsync(ServiceDefinition service, bool reloadImmediately)
    {
        var confirm = new ConfirmWindow($"Reset {service.Name} site data?",
            $"This will sign you out of {service.Name} and remove its saved website cookies. {service.Name} will behave like it is being visited for the first time.\n\nThis cannot be undone.",
            $"Reset {service.Name}", destructive: true) { Owner = this };
        if (confirm.ShowDialog() != true) return false;

        _sessionPermissions.ClearForHosts(GetServiceResetHosts(service));

        if (_webView?.CoreWebView2 is { } core)
            await ResetServiceDataCoreAsync(service, core);
        else
        {
            // Do not initialize WebView2 from Home just to clear data. Queue the shared-profile
            // operation for immediately before this exact service's next navigation.
            _servicesWithPendingDataReset.Add(service.Id);
            try { StorageService.ClearServiceData(service.Id); }
            catch (Exception ex) { AppLog.Write("storage", ex.Message); }
        }

        if (reloadImmediately) RestartActiveService();
        else _servicesRequiringFreshReload.Add(service.Id);
        return true;
    }

    private static async Task ResetServiceDataCoreAsync(ServiceDefinition service, CoreWebView2 core)
    {
        foreach (var host in GetServiceResetHosts(service))
        {
            var cookies = await core.CookieManager.GetCookiesAsync("https://" + host + "/");
            foreach (var cookie in cookies)
                if (cookie.Domain.TrimStart('.').Equals(host, StringComparison.OrdinalIgnoreCase) || cookie.Domain.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))
                    core.CookieManager.DeleteCookie(cookie);
        }
        // This WebView2 version has no origin-scoped DOM-storage/IndexedDB clearing API.
        // Disk cache is safe to clear globally; other provider cookies and site storage remain untouched.
        await StorageService.ClearActiveDiskCacheAsync(core.Profile);
    }

    private static IEnumerable<string> GetServiceResetHosts(ServiceDefinition service)
    {
        if (Uri.TryCreate(service.Url, UriKind.Absolute, out var uri)) yield return uri.Host;
        if (service.FutureSettings.TryGetValue("KnownDomains", out var domains))
            foreach (var domain in domains.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                if (Uri.TryCreate("https://" + domain, UriKind.Absolute, out var known)) yield return known.Host;
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
        ShowIntegratedPage("Home");
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

    private void AddService_Click(object sender, RoutedEventArgs e)
    {
        AddServiceInDrawer();
    }
    private void ServicesLibrary_Click(object sender, RoutedEventArgs e)
    {
        _showingBuiltInServiceLibrary = !_showingBuiltInServiceLibrary;
        ServicesPrototypeSearch.Clear();
        if (ServicesCatalogRegionBox.SelectedIndex < 0) ServicesCatalogRegionBox.SelectedIndex = 0;
        RenderServicesPrototype();
    }

    private void ServicesCatalogRegion_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_showingBuiltInServiceLibrary) RenderServicesPrototype();
    }

    private void AddAllBuiltInServices_Click(object sender, RoutedEventArgs e)
    {
        var candidates = BuiltInServiceCatalog.All
            .Where(template => !_settings.Services.Any(service => string.Equals(service.BuiltInServiceId, template.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (candidates.Count == 0) return;
        var confirmation = new ConfirmWindow("Add built-in services?",
            $"Add {candidates.Count} built-in service(s) to Monkey TV? Services that look like existing custom services will be skipped so nothing is duplicated.", "Add all") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        foreach (var template in candidates)
        {
            if (BuiltInServiceCatalog.FindProbableCustomMatch(_settings.Services, template) is null)
                _settings.Services.Add(template.Create());
        }
        _store.Save(_settings);
        RenderHome();
    }

    private void RemoveAllBuiltInServices_Click(object sender, RoutedEventArgs e)
    {
        var removals = _settings.Services.Where(service => !string.IsNullOrWhiteSpace(service.BuiltInServiceId)).ToList();
        if (removals.Count == 0) return;
        var confirmation = new ConfirmWindow("Remove built-in services?",
            $"Remove {removals.Count} built-in service(s) from Monkey TV? Custom services, provider cookies, logins, and shared website data will be kept.", "Remove all", destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        foreach (var service in removals)
        {
            StorageService.RemoveRetainedServiceProfileData(service);
            _settings.Services.Remove(service);
        }
        if (_webView is null) StorageService.CleanupQueuedSharedProfileDeletes();
        _store.Save(_settings);
        StorageService.CleanupOrphanAssets(_settings);
        RenderHome();
    }

    private async void HomeNav_Click(object sender, RoutedEventArgs e)
    {
        if (_activeServiceEditor is { } editor)
        {
            RequestImplicitEditorDismissal(editor, () => ShowIntegratedPage("Home"));
            return;
        }
        if (BrowserView.Visibility == Visibility.Visible)
            await ReturnHomeAsync();
        ShowIntegratedPage("Home");
    }

    private async void ServicesNav_Click(object sender, RoutedEventArgs e)
    {
        if (_activeServiceEditor is { } editor)
        {
            RequestImplicitEditorDismissal(editor, () => ShowIntegratedPage("Services"));
            return;
        }
        if (BrowserView.Visibility == Visibility.Visible)
            await ReturnHomeAsync();
        _showingBuiltInServiceLibrary = false;
        ShowIntegratedPage("Services");
    }

    private async void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        if (_activeServiceEditor is { } editor)
        {
            RequestImplicitEditorDismissal(editor, () => ShowIntegratedPage("Settings"));
            return;
        }
        if (BrowserView.Visibility == Visibility.Visible)
            await ReturnHomeAsync();
        ShowIntegratedPage("Settings");
    }

    private void ShowIntegratedPage(string page)
    {
        var pages = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            ["Home"] = HomeScroll,
            ["Services"] = ServicesPrototypeView,
            ["Settings"] = SettingsPrototypeView
        };
        var target = pages[page];
        var isNewPage = !string.Equals(_integratedPage, page, StringComparison.Ordinal);
        foreach (var pair in pages) pair.Value.Visibility = pair.Key == page ? Visibility.Visible : Visibility.Collapsed;
        HomeNavButton.Tag = page == "Home" ? "Active" : null;
        ServicesNavButton.Tag = page == "Services" ? "Active" : null;
        SettingsNavButton.Tag = page == "Settings" ? "Active" : null;
        AnimateSelectionPulse(page == "Home" ? HomeNavButton : page == "Services" ? ServicesNavButton : SettingsNavButton);
        if (page == "Settings") LoadIntegratedSettings();
        if (page == "Services") RenderServicesPrototype();
        if (page == "Home") RenderHome();
        if (isNewPage) AnimateElementIn(target, 6, 125);
        _integratedPage = page;
    }

    private void ServicesPrototypeSearch_TextChanged(object sender, TextChangedEventArgs e) => RenderServicesPrototype();

    private void LoadPrototypeAppearance()
    {
        _updatingPrototypeAppearance = true;
        try
        {
            PrototypeThemeBox.SelectedIndex = (int)_settings.Theme;
            PrototypeAccentBox.SelectedIndex = (int)_settings.Accent;
            PrototypeMotionBox.SelectedIndex = (int)_settings.Motion;
            PrototypeCardStyleBox.SelectedIndex = (int)_settings.CardStyle;
            PrototypeShowNamesCheck.IsChecked = _settings.ShowServiceNames;
            PrototypeShowClockCheck.IsChecked = _settings.ShowClock;
        }
        finally { _updatingPrototypeAppearance = false; }
        // Home remains permanently available in the app shell, so an in-page duplicate is
        // unnecessary. This also keeps the Appearance view focused on its controls.
        foreach (var button in FindVisualChildren<Button>(SettingsPrototypeView))
            if (string.Equals(button.Content as string, "Back to Home", StringComparison.Ordinal)) button.Visibility = Visibility.Collapsed;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is not DependencyObject dependencyObject) continue;
            if (dependencyObject is T match) yield return match;
            foreach (var nested in FindLogicalChildren<T>(dependencyObject)) yield return nested;
        }
    }

    private void LoadIntegratedSettings()
    {
        // The Settings view is part of the main desktop shell, not a narrow utility dialog.
        // Use the available viewport while retaining a comfortable reading measure on ultrawide
        // windows.
        // Match the typography and control density used by the Services workspace while
        // retaining Settings' full-width two-column layout.
        SettingsPrototypeView.Padding = new Thickness(48, 38, 48, 56);
        var settingsCanvas = SettingsPrototypeView.Content as Grid;
        if (settingsCanvas is not null)
        {
            settingsCanvas.MaxWidth = 2800;
            settingsCanvas.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (settingsCanvas.ColumnDefinitions.Count >= 2)
            {
                settingsCanvas.ColumnDefinitions[0].Width = new GridLength(240);
                settingsCanvas.ColumnDefinitions[1].Width = new GridLength(48);
            }
        }
        EnsureRerunSetupAction();
        SizeIntegratedSettingsContent();
        ScaleIntegratedSettingsControls();
        LoadPrototypeAppearance();
        SettingsStartupModeBox.SelectedIndex = (int)_settings.StartupMode;
        SettingsCloseBehaviorBox.SelectedIndex = (int)_settings.CloseBehavior;
        SettingsFullscreenCheck.IsChecked = _settings.StartupFullscreen;
        SettingsExperienceBox.SelectedIndex = (int)_settings.DefaultCustomServiceExperience;
        SettingsExternalLinksBox.SelectedIndex = (int)_settings.ExternalLinkBehavior;
        SettingsNordUsernameBox.Clear(); SettingsNordPasswordBox.Clear();
        SettingsNordStatusText.Text = NordCredentialStore.HasCredentials
            ? "Service credentials are saved securely."
            : "No service credentials are saved yet.";
        SettingsVpnCountryBox.SelectedIndex = 0;
        RefreshIntegratedStorage();
        SettingsAboutText.Text = $"Version v{App.DisplayVersion} — Build #{App.BuildNumber}\nWindows desktop streaming hub using Microsoft Edge WebView2.";
        ShowSettingsSection("General");
    }

    private void EnsureRerunSetupAction()
    {
        if (SettingsStoragePanel.Tag is true) return;
        if (SettingsStoragePanel.Children.OfType<Border>().FirstOrDefault()?.Child is not StackPanel content) return;

        var separator = new Border { Height = 1, Margin = new Thickness(0, 26, 0, 22) };
        separator.SetResourceReference(BackgroundProperty, "BorderBrush");
        var description = new TextBlock
        {
            Text = "Run first-time setup again. Nothing is reset until you finish the setup wizard.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 16)
        };
        description.SetResourceReference(ForegroundProperty, "MutedBrush");
        var restartButton = new Button { Content = "Run first-time setup again…", HorizontalAlignment = HorizontalAlignment.Left };
        restartButton.Click += RerunSetup_Click;

        content.Children.Add(separator);
        content.Children.Add(new TextBlock { Text = "Start over", FontSize = 21, FontWeight = FontWeights.SemiBold });
        content.Children.Add(description);
        content.Children.Add(restartButton);
        SettingsStoragePanel.Tag = true;
    }

    private async void RerunSetup_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmWindow("Run first-time setup again?",
            "Finishing setup will reset Monkey TV’s services, settings, browser data, saved permissions, downloaded locations, and saved credentials. Required runtime files will be kept.\n\nNothing is erased unless you finish the setup wizard.",
            "Start over", destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;

        var setup = new SetupWindow(new MonkeySettings(), _store, deferCommit: true) { Owner = this };
        if (setup.ShowDialog() != true) return;
        await CompleteRerunSetupAsync(setup);
    }

    private async Task CompleteRerunSetupAsync(SetupWindow setup)
    {
        var stagedAssets = StageSetupAssets(setup.ResultSettings);
        DestroyBrowser();
        _sessionPermissions.ClearAll();
        try { await _locationRouter.DisposeAsync(); } catch { }
        _locationRouter = new CurrentLocationRouter();
        AttachLocationRouterEvents();

        try
        {
            StorageService.ResetMonkeyTvData();
            PortablePaths.ResetDataRootToDefault();
            RestoreStagedSetupAssets(stagedAssets);

            _settings = setup.ResultSettings;
            _settings.SetupComplete = true;
            _store.Save(_settings);
            if (setup.PendingNordCredentials is { } credentials)
                NordCredentialStore.Save(credentials.Username, credentials.Password);

            _unsavedEditorDrafts.Clear();
            _servicesRequiringFreshReload.Clear();
            _servicesWithPendingDataReset.Clear();
            _unsavedTestService = null;
            ExternalNavigationPolicy.AskBeforeOpening = _settings.ExternalLinkBehavior == ExternalLinkBehavior.AskFirst;
            ThemeService.Apply(_settings);
            UpdateCurrentLocationControl();
            ShowIntegratedPage("Home");
        }
        catch (Exception ex)
        {
            AppLog.Write("setup", "Could not finish setup reset: " + ex.GetType().Name);
            new ConfirmWindow("Monkey TV could not finish setup", "Your previous data has not been restored. Close Monkey TV, then try first-time setup again.", "OK") { Owner = this }.ShowDialog();
        }
    }

    private static List<SetupAsset> StageSetupAssets(MonkeySettings settings)
    {
        var staged = new List<SetupAsset>();
        var assetsRoot = Path.GetFullPath(PortablePaths.AssetsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var service in settings.Services)
        {
            Stage(service, isBanner: false, service.IconPath);
            Stage(service, isBanner: true, service.BannerPath);
        }
        return staged;

        void Stage(ServiceDefinition service, bool isBanner, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || BuiltInArtworkCatalog.IsBuiltInReference(relativePath)) return;
            try
            {
                var path = Path.GetFullPath(Path.Combine(PortablePaths.AppRoot, relativePath));
                if (!path.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
                staged.Add(new SetupAsset(service, isBanner, Path.GetFileName(path), File.ReadAllBytes(path)));
            }
            catch { }
        }
    }

    private static void RestoreStagedSetupAssets(IEnumerable<SetupAsset> stagedAssets)
    {
        foreach (var asset in stagedAssets)
        {
            var directory = asset.IsBanner ? PortablePaths.BannersDirectory : PortablePaths.AssetsDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, asset.FileName);
            File.WriteAllBytes(path, asset.Bytes);
            var relative = Path.GetRelativePath(PortablePaths.AppRoot, path);
            if (asset.IsBanner) asset.Service.BannerPath = relative;
            else asset.Service.IconPath = relative;
        }
    }

    private sealed record SetupAsset(ServiceDefinition Service, bool IsBanner, string FileName, byte[] Bytes);

    private void SettingsSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section }) ShowSettingsSection(section);
    }

    private void ShowSettingsSection(string section)
    {
        var panels = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            ["General"] = SettingsGeneralPanel,
            ["Appearance"] = SettingsAppearancePanel,
            ["Streaming"] = SettingsStreamingPanel,
            ["VPN"] = SettingsVpnPanel,
            ["Storage"] = SettingsStoragePanel,
            ["About"] = SettingsAboutPanel
        };
        foreach (var pair in panels) pair.Value.Visibility = pair.Key == section ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in FindVisualChildren<Button>(SettingsPrototypeView))
        {
            if (button.Tag is not string tag || !panels.ContainsKey(tag)) continue;
            var selected = tag == section;
            button.Background = selected ? (Brush)FindResource("AccentSubtleBrush") : Brushes.Transparent;
            button.Foreground = selected ? (Brush)FindResource("TextBrush") : (Brush)FindResource("MutedBrush");
            if (selected) AnimateSelectionPulse(button);
        }
        SizeIntegratedSettingsContent();
    }

    private void SizeIntegratedSettingsContent()
    {
        if (SettingsPrototypeView.ViewportHeight <= 0) return;
        // A desktop Settings view should feel like a real workspace, not a compact dialog
        // floating at the top of an otherwise empty window. Keep one focused surface tall
        // enough to balance the persistent sidebar, without forcing the multi-card VPN page.
        // Keep the main section substantial, but let it end with its content rather than
        // becoming a giant empty framed panel. This matches the desktop workspace layout.
        foreach (var panel in new[] { SettingsGeneralPanel, SettingsAppearancePanel, SettingsStreamingPanel, SettingsStoragePanel, SettingsAboutPanel })
        {
            if (panel.Children.OfType<Border>().FirstOrDefault() is Border primaryCard)
            {
                primaryCard.MinHeight = 0;
                primaryCard.Padding = new Thickness(30, 28, 30, 32);
            }
        }
    }

    private void ScaleIntegratedSettingsControls()
    {
        // Keep controls consistent with Services instead of applying a separate oversized
        // Settings scale. Explicit values also normalize a view if it is reloaded in-process.
        foreach (var text in FindLogicalChildren<TextBlock>(SettingsPrototypeView))
        {
            if (text.Text.StartsWith("Monkey TV is a personal streaming-site launcher.", StringComparison.Ordinal))
            {
                text.FontSize = 20;
                text.LineHeight = 28;
            }
            else if (text.FontSize <= 12) text.FontSize = 15;
            else if (text.FontSize <= 15) text.FontSize = 18;
            else if (text.FontSize <= 22) text.FontSize = 24;
            else if (text.FontSize < 36) text.FontSize = 30;
        }
        foreach (var grid in FindLogicalChildren<UniformGrid>(SettingsPrototypeView))
        {
            grid.Columns = 2;
            grid.Margin = new Thickness(0, 20, 0, 18);
        }
        foreach (var combo in FindLogicalChildren<ComboBox>(SettingsPrototypeView))
        {
            combo.FontSize = 17;
            combo.MinHeight = 50;
            combo.Padding = new Thickness(14, 10, 14, 10);
        }
        foreach (var textBox in FindLogicalChildren<TextBox>(SettingsPrototypeView))
        {
            textBox.FontSize = 17;
            textBox.MinHeight = 50;
            textBox.Padding = new Thickness(14, 10, 14, 10);
        }
        foreach (var password in FindLogicalChildren<PasswordBox>(SettingsPrototypeView))
        {
            password.FontSize = 17;
            password.MinHeight = 50;
            password.Padding = new Thickness(14, 10, 14, 10);
        }
        foreach (var check in FindLogicalChildren<CheckBox>(SettingsPrototypeView))
            check.FontSize = 17;
        foreach (var button in FindLogicalChildren<Button>(SettingsPrototypeView))
        {
            var isCategory = button.Tag is string tag && tag is "General" or "Appearance" or "Streaming" or "VPN" or "Storage" or "About";
            button.FontSize = isCategory ? 18 : 17;
            button.MinHeight = isCategory ? 56 : 50;
            button.Padding = isCategory ? new Thickness(20, 14, 20, 14) : new Thickness(18, 12, 18, 12);
        }
    }

    private void SaveGeneralSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartupMode = (StartupMode)Math.Max(0, SettingsStartupModeBox.SelectedIndex);
        _settings.CloseBehavior = (CloseBehavior)Math.Max(0, SettingsCloseBehaviorBox.SelectedIndex);
        _settings.StartupFullscreen = SettingsFullscreenCheck.IsChecked == true;
        _store.Save(_settings);
    }

    private void SaveStreamingSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.DefaultCustomServiceExperience = (ServiceExperience)Math.Max(0, SettingsExperienceBox.SelectedIndex);
        _settings.ExternalLinkBehavior = (ExternalLinkBehavior)Math.Max(0, SettingsExternalLinksBox.SelectedIndex);
        ExternalNavigationPolicy.AskBeforeOpening = _settings.ExternalLinkBehavior == ExternalLinkBehavior.AskFirst;
        _store.Save(_settings);
    }

    private void SaveSettingsNordCredentials_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NordCredentialStore.Save(SettingsNordUsernameBox.Text, SettingsNordPasswordBox.Password);
            SettingsNordUsernameBox.Clear(); SettingsNordPasswordBox.Clear();
            SettingsNordStatusText.Text = "Service credentials saved securely. VPN Location is ready to use.";
            RefreshVpnLocationControl();
        }
        catch { SettingsNordStatusText.Text = "Could not save the service credentials."; }
    }

    private void ClearSettingsNordCredentials_Click(object sender, RoutedEventArgs e)
    {
        NordCredentialStore.Clear(); SettingsNordUsernameBox.Clear(); SettingsNordPasswordBox.Clear();
        SettingsNordStatusText.Text = "Service credentials cleared.";
        RefreshVpnLocationControl();
    }

    private async void PrepareSettingsVpnCountry_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsVpnCountryBox.SelectedItem is not ComboBoxItem { Content: string name } ||
            VpnV2LocationCatalog.All.FirstOrDefault(item => item.Country == name) is not { } country) return;
        SettingsVpnPrepareStatusText.Text = "Preparing " + country.Country + "…";
        try
        {
            var progress = new Progress<string>(message => SettingsVpnPrepareStatusText.Text = message);
            var result = await VpnV2LocationCatalog.DownloadCountryAsync(country, progress);
            SettingsVpnPrepareStatusText.Text = $"{result.Country} is ready ({result.InstalledCount} locations).";
        }
        catch { SettingsVpnPrepareStatusText.Text = "Could not prepare this country. Check your connection and try again."; }
    }

    private void RefreshIntegratedStorage()
    {
        var snapshot = StorageService.GetSnapshot();
        SettingsStorageSummaryText.Text = $"Portable Data: {StorageService.FormatBytes(snapshot.TotalBytes)}\n" +
            $"Browser data: {StorageService.FormatBytes(snapshot.BrowserTotalBytes)} · Artwork: {StorageService.FormatBytes(snapshot.ArtworkBytes)} · Logs: {StorageService.FormatBytes(snapshot.LogBytes)}";
    }

    private void RefreshIntegratedStorage_Click(object sender, RoutedEventArgs e) => RefreshIntegratedStorage();

    private void PrototypeAppearance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPrototypeAppearance) return;
        _settings.Theme = (AppTheme)Math.Max(0, PrototypeThemeBox.SelectedIndex);
        _settings.Accent = (AppAccent)Math.Max(0, PrototypeAccentBox.SelectedIndex);
        ThemeService.Apply(_settings);
    }

    private void PrototypeSaveAppearance_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = (AppTheme)Math.Max(0, PrototypeThemeBox.SelectedIndex);
        _settings.Accent = (AppAccent)Math.Max(0, PrototypeAccentBox.SelectedIndex);
        _settings.Motion = (MotionPreference)Math.Max(0, PrototypeMotionBox.SelectedIndex);
        _settings.CardStyle = (ServiceCardStyle)Math.Max(0, PrototypeCardStyleBox.SelectedIndex);
        _settings.ShowServiceNames = PrototypeShowNamesCheck.IsChecked == true;
        _settings.ShowClock = PrototypeShowClockCheck.IsChecked == true;
        _store.Save(_settings);
        ThemeService.Apply(_settings);
        RenderHome();
    }

    private void UpdateCurrentLocationControl()
    {
        CurrentLocationText.Text = "VPN Location: " + _locationRouter.DisplayName;
        CurrentLocationButton.Visibility = Visibility.Visible;
        CurrentLocationButton.IsEnabled = _locationRouter.State != CurrentLocationState.Connecting &&
            VpnLocationDrawerOverlay.Visibility != Visibility.Visible;
        CurrentLocationButton.ToolTip = _locationRouter.State == CurrentLocationState.Failed
            ? "VPN Location failed — choose Retry or Direct"
            : "Choose the route used by Monkey TV websites";
        AnimateSelectionPulse(CurrentLocationButton);
        if (VpnLocationDrawerOverlay.Visibility == Visibility.Visible) UpdateVpnLocationDrawerStatus();
    }
    internal void RefreshVpnLocationControl() { RenderHome(); UpdateCurrentLocationControl(); }

    private void CurrentLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_activeServiceEditor is { } editor)
        {
            RequestImplicitEditorDismissal(editor, () =>
            {
                PopulateVpnLocationDrawer();
                OpenVpnLocationDrawer();
            });
            return;
        }
        PopulateVpnLocationDrawer();
        OpenVpnLocationDrawer();
    }

    private void PopulateVpnLocationDrawer()
    {
        VpnUsaLocationsPanel.Children.Clear();
        foreach (var location in NordLocationCatalog.ForPickerCountry("United States"))
        {
            var label = location.City is null ? "Automatic" : location.City;
            var button = new Button
            {
                Content = label,
                Tag = location,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(16, 11, 16, 11),
                Margin = new Thickness(0, 0, 0, 6)
            };
            button.Click += async (_, _) => await SelectVpnRouteFromDrawerAsync(VpnLocationSelection.ForSocks5(location));
            VpnUsaLocationsPanel.Children.Add(button);
        }
        var usaActive = _locationRouter.State == CurrentLocationState.Connected && _locationRouter.ActiveLocation is not null;
        VpnUsaLocationsPanel.Visibility = usaActive ? Visibility.Visible : Visibility.Collapsed;
        VpnUsaChevron.Text = usaActive ? "⌃" : "⌄";

        var configs = VpnV2ConfigStore.Load();
        var hasCredentials = NordCredentialStore.HasCredentials;
        var hasCanada = configs.Any(item => item.Country.Equals("Canada", StringComparison.OrdinalIgnoreCase));
        var hasUnitedKingdom = configs.Any(item => item.Country.Equals("United Kingdom", StringComparison.OrdinalIgnoreCase));
        VpnUsaButton.IsEnabled = hasCredentials;
        VpnCanadaButton.IsEnabled = hasCredentials && hasCanada;
        VpnUnitedKingdomButton.IsEnabled = hasCredentials && hasUnitedKingdom;
        VpnCanadaSetupText.Visibility = hasCanada ? Visibility.Collapsed : Visibility.Visible;
        VpnUnitedKingdomSetupText.Visibility = hasUnitedKingdom ? Visibility.Collapsed : Visibility.Visible;
        VpnDrawerSetupPanel.Visibility = !hasCredentials || !hasCanada || !hasUnitedKingdom ? Visibility.Visible : Visibility.Collapsed;
        VpnDrawerSetupText.Text = !hasCredentials
            ? "Save your Nord service credentials in Settings before connecting to a country."
            : "Prepare any unavailable country in Settings → VPN Location.";
        UpdateVpnLocationDrawerStatus();
    }

    private void OpenVpnLocationDrawer()
    {
        _vpnLocationDrawerClosing = false;
        HomeNavButton.IsEnabled = ServicesNavButton.IsEnabled = SettingsNavButton.IsEnabled = false;
        CurrentLocationButton.IsEnabled = false;
        VpnLocationDrawerOverlay.Visibility = Visibility.Visible;
        VpnLocationDrawerScrim.BeginAnimation(OpacityProperty, null);
        VpnLocationDrawerTransform.BeginAnimation(TranslateTransform.XProperty, null);
        if (ThemeService.ReduceMotion)
        {
            VpnLocationDrawerScrim.Opacity = 1;
            VpnLocationDrawerTransform.X = 0;
            return;
        }
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        VpnLocationDrawerScrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        VpnLocationDrawerTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(520, 0, duration) { EasingFunction = easing });
    }

    private void CloseVpnLocationDrawer(Action? completed = null)
    {
        if (_vpnLocationDrawerClosing) return;
        _vpnLocationDrawerClosing = true;
        void Finish()
        {
            VpnLocationDrawerScrim.BeginAnimation(OpacityProperty, null);
            VpnLocationDrawerTransform.BeginAnimation(TranslateTransform.XProperty, null);
            VpnLocationDrawerScrim.Opacity = 0;
            VpnLocationDrawerTransform.X = 520;
            VpnLocationDrawerOverlay.Visibility = Visibility.Collapsed;
            _vpnLocationDrawerClosing = false;
            HomeNavButton.IsEnabled = ServicesNavButton.IsEnabled = SettingsNavButton.IsEnabled = true;
            UpdateCurrentLocationControl();
            completed?.Invoke();
        }
        if (ThemeService.ReduceMotion) { Finish(); return; }
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        VpnLocationDrawerScrim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
        var slide = new DoubleAnimation(0, 520, duration) { EasingFunction = easing };
        slide.Completed += (_, _) => Finish();
        VpnLocationDrawerTransform.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void UpdateVpnLocationDrawerStatus()
    {
        (VpnDrawerStatusText.Text, VpnDrawerStatusDetail.Text) = _locationRouter.State switch
        {
            CurrentLocationState.Direct => ("Direct", "Monkey TV websites use your normal connection."),
            CurrentLocationState.Connecting => ("Connecting…", "Monkey TV is preparing and verifying the selected location before it can be used."),
            CurrentLocationState.Connected => ($"Connected: {(_locationRouter.DisplayName == "United States" ? "USA" : _locationRouter.DisplayName)}", "The selected location is ready and verified."),
            _ => ("Connection failed", "Monkey TV stayed safely blocked. Choose another location, retry, or return to Direct.")
        };
        VpnDrawerChoices.IsEnabled = _locationRouter.State != CurrentLocationState.Connecting;
        foreach (var button in new[] { VpnDirectButton, VpnUsaButton, VpnCanadaButton, VpnUnitedKingdomButton })
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
        }
        foreach (var button in VpnUsaLocationsPanel.Children.OfType<Button>())
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
        }
        Button? activeButton = _locationRouter.State switch
        {
            CurrentLocationState.Direct => VpnDirectButton,
            CurrentLocationState.Connected when _locationRouter.ActiveLocation is not null =>
                VpnUsaLocationsPanel.Children.OfType<Button>().FirstOrDefault(button =>
                    button.Tag is NordLocation location && location.Id == _locationRouter.ActiveLocation.Id) ?? VpnUsaButton,
            CurrentLocationState.Connected when _locationRouter.ActiveVpnV2Location?.Country == "Canada" => VpnCanadaButton,
            CurrentLocationState.Connected when _locationRouter.ActiveVpnV2Location?.Country == "United Kingdom" => VpnUnitedKingdomButton,
            _ => null
        };
        if (activeButton is not null)
        {
            activeButton.Background = (Brush)FindResource("AccentSubtleBrush");
            activeButton.BorderBrush = (Brush)FindResource("AccentBrush");
        }
    }

    private async Task SelectVpnRouteFromDrawerAsync(VpnLocationSelection route)
    {
        if (route.Kind != VpnLocationKind.Direct && !NordCredentialStore.HasCredentials)
        {
            PopulateVpnLocationDrawer();
            return;
        }
        try { await ChangeVpnLocationAsync(route); }
        catch (InvalidOperationException ex)
        {
            AppLog.Write("vpn-location", ex.Message);
            PopulateVpnLocationDrawer();
        }
        UpdateVpnLocationDrawerStatus();
    }

    private async void VpnDrawerDirect_Click(object sender, RoutedEventArgs e) => await SelectVpnRouteFromDrawerAsync(VpnLocationSelection.Direct);
    private void VpnDrawerUsa_Click(object sender, RoutedEventArgs e)
    {
        var expanded = VpnUsaLocationsPanel.Visibility != Visibility.Visible;
        VpnUsaLocationsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        VpnUsaChevron.Text = expanded ? "⌃" : "⌄";
    }
    private async void VpnDrawerCanada_Click(object sender, RoutedEventArgs e) => await SelectVpnRouteFromDrawerAsync(VpnLocationSelection.ForOpenVpn("Canada"));
    private async void VpnDrawerUnitedKingdom_Click(object sender, RoutedEventArgs e) => await SelectVpnRouteFromDrawerAsync(VpnLocationSelection.ForOpenVpn("United Kingdom"));
    private void VpnLocationDrawerClose_Click(object sender, RoutedEventArgs e) => CloseVpnLocationDrawer();
    private void VpnDrawerOpenSettings_Click(object sender, RoutedEventArgs e) => CloseVpnLocationDrawer(() =>
    {
        ShowIntegratedPage("Settings");
        ShowSettingsSection("VPN");
    });

    private void ServiceEditorScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_serviceEditorDrawerClosing || _activeServiceEditor is not { } editor) return;
        e.Handled = true;
        RequestImplicitEditorDismissal(editor);
    }

    private void VpnLocationDrawerScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vpnLocationDrawerClosing) return;
        e.Handled = true;
        CloseVpnLocationDrawer();
    }
    private Task ChangeVpnLocationAsync(VpnLocationSelection selection) => selection.Kind switch
    {
        VpnLocationKind.Direct => ChangeLocationAsync(null),
        VpnLocationKind.Socks5 when selection.Socks5Location is not null => ChangeLocationAsync(selection.Socks5Location),
        VpnLocationKind.OpenVpn => ChangeVpnV2LocationAsync(selection.OpenVpnConfig ?? VpnV2ConfigStore.Load().FirstOrDefault(item => item.Country.Equals(selection.Country, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No downloaded configuration is available for " + selection.Country + ".")),
        _ => Task.CompletedTask
    };
    private async Task ChangeVpnV2LocationAsync(VpnV2ImportedConfig config, bool reloadActiveService = true)
    {
        VpnV2Diagnostics.Write("vpn-v2", $"location selected={config.Country}/{config.ServerName} managed-config={VpnV2Diagnostics.ManagedConfigPath(config)} exists={File.Exists(config.FullPath)}");
        if (!WebViewRuntimeResolver.TryRequireFixedForCurrentLocationV2(out var fixedRuntime, out var runtimeMessage) ||
            string.IsNullOrWhiteSpace(fixedRuntime.BrowserExecutableFolder))
        {
            VpnV2Diagnostics.Write("fixed-runtime", "unavailable: " + runtimeMessage);
            new ConfirmWindow("VPN Location is unavailable", "Monkey TV needs its required VPN Location components before it can connect. Reinstall Monkey TV, then try again.", "OK") { Owner = this }.ShowDialog();
            return;
        }
        VpnV2Diagnostics.Dependency("Fixed WebView2 runtime", Path.Combine(fixedRuntime.BrowserExecutableFolder, WebViewRuntimeResolver.FixedRuntimeExecutableName));
        VpnV2Diagnostics.Write("fixed-runtime", $"validated version={fixedRuntime.Version}");
        if (_webView?.CoreWebView2 is not null && _browserRuntime?.IsFixed != true)
        {
            new ConfirmWindow("Restart required for VPN Location", "The current browser was created with shared WebView2. Restart Monkey TV so its required browser runtime can be used before connecting.", "OK") { Owner = this }.ShowDialog();
            return;
        }
        var credentials = NordCredentialStore.Load();
        VpnV2Diagnostics.Write("credential-retrieval", $"DPAPI credentials available={credentials is not null}");
        if (credentials is null)
        {
            // Credentials are configured in the integrated Settings view. Do not create an
            // extra settings taskbar window merely because a location was selected.
            await ShowVpnCredentialSetupAsync();
            return;
        }
        var service = reloadActiveService ? ActiveService : null;
        var wasBrowsing = service is not null && BrowserView.Visibility == Visibility.Visible;
        if (wasBrowsing) BeginServiceLaunch(service!);
        _locationRouter.Block();
        var countryConfigs = VpnV2ConfigStore.Load().Where(item => item.Country.Equals(config.Country, StringComparison.OrdinalIgnoreCase)).ToList();
        var candidates = new[] { config }
            .Concat(countryConfigs.Where(item => item.Id != config.Id).OrderBy(_ => Random.Shared.Next()))
            .Take(5).ToList();
        VpnV2Diagnostics.Write("vpn-v2", $"candidate-count={candidates.Count}");
        var connected = false;
        var authenticatedCandidateRejectedForCountry = false;
        var freshSessionAuthenticationRetries = 0;
        VpnV2ImportedConfig? connectedConfig = null;
        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            var candidate = candidates[candidateIndex];
            var hasFallbackCandidate = candidateIndex < candidates.Count - 1;
            connected = await _locationRouter.SelectVpnV2Async(candidate, credentials,
                Path.Combine(fixedRuntime.BrowserExecutableFolder, WebViewRuntimeResolver.FixedRuntimeExecutableName));
            if (connected) { connectedConfig = candidate; break; }
            if (_locationRouter.VpnV2ContainmentFailed) break;
            if (_locationRouter.LastVpnV2Failure == VpnV2CandidateFailure.CountryMismatch)
            {
                if (!hasFallbackCandidate) break;
                authenticatedCandidateRejectedForCountry = true;
                VpnV2Diagnostics.Write("vpn-v2", "candidate rejected: independent country checks disagreed; waiting for Nord session teardown before fallback.");
                _locationRouter.ContinueVpnV2Fallback(config.Country);
                await Task.Delay(TimeSpan.FromSeconds(15));
                VpnV2Diagnostics.Write("vpn-v2", "candidate fallback cooldown complete.");
                continue;
            }
            if (_locationRouter.LastVpnV2Failure == VpnV2CandidateFailure.Authentication &&
                (authenticatedCandidateRejectedForCountry || _locationRouter.HasAuthenticatedVpnV2ThisSession))
            {
                if (!hasFallbackCandidate) break;
                // Credentials were proven by a fully established tunnel in this app
                // session. Nord can retain the prior UDP login briefly across Direct
                // or country switching, so this rejection is not evidence that the
                // stored credentials are invalid.
                // Nord can briefly reject a new session while the old UDP session is
                // being released, so keep fail-closed and continue after a cooldown.
                VpnV2Diagnostics.Write("vpn-v2", "authentication rejected after a proven authenticated candidate; treating as transient session teardown and continuing fallback.");
                _locationRouter.ContinueVpnV2Fallback(config.Country);
                await Task.Delay(TimeSpan.FromSeconds(15));
                VpnV2Diagnostics.Write("vpn-v2", "transient authentication cooldown complete.");
                continue;
            }
            if (_locationRouter.LastVpnV2Failure == VpnV2CandidateFailure.Authentication &&
                freshSessionAuthenticationRetries++ == 0)
            {
                if (!hasFallbackCandidate) break;
                VpnV2Diagnostics.Write("vpn-v2", "first authentication rejection in a fresh app session; allowing one cooled-down candidate retry.");
                _locationRouter.ContinueVpnV2Fallback(config.Country);
                await Task.Delay(TimeSpan.FromSeconds(15));
                VpnV2Diagnostics.Write("vpn-v2", "fresh-session authentication retry cooldown complete.");
                continue;
            }
            if (_locationRouter.LastVpnV2Failure is VpnV2CandidateFailure.Authentication or VpnV2CandidateFailure.Compression)
            {
                VpnV2Diagnostics.Write("vpn-v2", $"candidate fallback stopped: {_locationRouter.LastVpnV2Failure} applies to the current credentials or profile family.");
                break;
            }
        }
        if (!connected)
        {
            var detail = _locationRouter.VpnV2ContainmentFailed
                ? "Monkey TV could not safely prepare the selected location. The connection remained blocked. Approve the requested Windows prompt and retry."
                : "Monkey TV tried other available connections automatically but could not connect safely. Choose Direct or try again later.";
            if (wasBrowsing) ShowServiceError(service!, "Monkey TV couldn't connect to " + config.Country + ".", detail);
            return;
        }
        if (connectedConfig is not null && connectedConfig.Id != config.Id)
            AppLog.Write("vpn-v2", $"Selected server was unavailable; connected using another downloaded {config.Country} configuration.");
        if (connectedConfig is not null)
        {
            _settings.LastUsedVpnLocation = new VpnLocationLastUsed { Kind = VpnLocationKind.OpenVpn, Country = connectedConfig.Country, ServerName = connectedConfig.ServerName };
            _store.Save(_settings);
        }
        if (wasBrowsing && _webView?.CoreWebView2 is not null)
        {
            BeginServiceLaunch(service!); ApplyServiceSettings(_webView, service!); _webView.CoreWebView2.Navigate(service!.Url);
        }
    }

    private async Task ChangeLocationAsync(NordLocation? location, bool reloadActiveService = true)
    {
        var service = reloadActiveService ? ActiveService : null;
        var wasBrowsing = service is not null && BrowserView.Visibility == Visibility.Visible;
        if (location is null)
        {
            if (wasBrowsing) BeginServiceLaunch(service!);
            _locationRouter.Block();
            var directReady = await _locationRouter.SelectDirectAsync();
            if (!directReady)
            {
            if (wasBrowsing) ShowServiceError(service!, "Monkey TV couldn't verify Direct.", "The previous route was removed, but the intended Direct route could not be verified. Monkey TV remains blocked.");
                return;
            }
            if (wasBrowsing && _webView?.CoreWebView2 is not null)
            { BeginServiceLaunch(service!); ApplyServiceSettings(_webView, service!); _webView.CoreWebView2.Navigate(service!.Url); }
            return;
        }
        var credentials = NordCredentialStore.Load();
        if (credentials is null)
        {
            // USA uses the same Nord service credentials as the other VPN Location choices.
            // Keep setup inside the integrated Settings workspace instead of reviving the
            // retired standalone Settings window.
            if (!reloadActiveService) _preferredRouteRedirectedToCredentialSetup = true;
            await ShowVpnCredentialSetupAsync();
            return;
        }
        if (wasBrowsing) BeginServiceLaunch(service!);
        _locationRouter.Block();
        var candidates = new[] { location }
            .Concat(NordLocationCatalog.ForPickerCountry(location.Country).Where(candidate => candidate.Id != location.Id).OrderBy(_ => Random.Shared.Next()))
            .Take(5).ToList();
        var connected = false;
        NordLocation? connectedLocation = null;
        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            var candidate = candidates[candidateIndex];
            connected = await _locationRouter.SelectNordAsync(candidate, credentials);
            if (connected) { connectedLocation = candidate; break; }
            if (candidateIndex < candidates.Count - 1) _locationRouter.ContinueVpnV2Fallback(location.Country);
        }
        if (!connected)
        {
            if (wasBrowsing) ShowServiceError(service!, "Monkey TV couldn't connect to " + location.Country + ".", "Monkey TV tried other available connections automatically but could not connect safely. Retry from VPN Location or choose Direct.");
            return;
        }
        var successfulLocation = connectedLocation ?? location;
        _settings.LastUsedNordLocation = successfulLocation.Id;
        _settings.LastUsedVpnLocation = new VpnLocationLastUsed { Kind = VpnLocationKind.Socks5, Socks5Location = successfulLocation.Id, Country = successfulLocation.Country };
        _store.Save(_settings);
        if (wasBrowsing && _webView?.CoreWebView2 is not null)
        {
            BeginServiceLaunch(service!); ApplyServiceSettings(_webView, service!); _webView.CoreWebView2.Navigate(service!.Url);
        }
    }

    private async Task ShowVpnCredentialSetupAsync()
    {
        // A route setup request can originate from an active service, including its launch
        // progress surface. Return the shared browser to its existing safe Home lifecycle
        // before showing Settings, and cancel that incomplete launch rather than timing out
        // behind the credential page.
        EndServiceLaunchTransition();
        if (BrowserView.Visibility == Visibility.Visible)
            await ReturnHomeAsync();

        ShowIntegratedPage("Settings");
        ShowSettingsSection("VPN");
    }

    internal async Task SetCurrentLocationFeatureEnabledAsync(bool enabled)
    {
        if (!enabled)
        {
            // Do not leave a hidden Nord route active. This waits for Direct mode before the
            // Settings page hides the Home control, and reloads only if a service is visible.
            if (_locationRouter.State != CurrentLocationState.Direct) await ChangeVpnLocationAsync(VpnLocationSelection.Direct);
            _settings.CurrentLocationEnabled = false; _store.Save(_settings); RenderHome();
            return;
        }
        _settings.CurrentLocationEnabled = true; _store.Save(_settings); RenderHome();
    }
    private void BrowserBack_Click(object sender, RoutedEventArgs e) { if (_webView?.CanGoBack == true) _webView.GoBack(); }
    private void BrowserForward_Click(object sender, RoutedEventArgs e) { if (_webView?.CanGoForward == true) _webView.GoForward(); }
    private void BrowserReload_Click(object sender, RoutedEventArgs e) { _webView?.Reload(); }
    private async void BrowserHome_Click(object sender, RoutedEventArgs e) => await ReturnHomeAsync();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleApplicationFullscreen();

    private void UpdateNavigationControls(WebView2? view = null)
    {
        view ??= _webView;
        var canGoBack = view?.CanGoBack == true;
        var canGoForward = view?.CanGoForward == true;
        InlineBackButton.IsEnabled = canGoBack;
        InlineForwardButton.IsEnabled = canGoForward;
        _serviceControls.SetNavigationState(canGoBack, canGoForward, _serviceNavigationLoading);
    }

    private void CopyErrorDetails_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastErrorDiagnostic)) return;
        try { Clipboard.SetText(_lastErrorDiagnostic); } catch { }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsNav_Click(sender, e);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && VpnLocationDrawerOverlay.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            CloseVpnLocationDrawer();
            return;
        }
        if (e.Key == Key.Escape && ServiceEditorOverlay.Visibility == Visibility.Visible && _activeServiceEditor is { } editor)
        {
            e.Handled = true;
            RequestImplicitEditorDismissal(editor);
            return;
        }
        if (HandleApplicationShortcut(e)) return;
        if (e.Key == Key.F11) { ToggleApplicationFullscreen(); e.Handled = true; }
        // Home is a temporary suspension state when a service exists. Escape resumes that exact
        // service rather than treating Home as an application-fullscreen exit.
        else if (e.Key == Key.Escape && HomeScroll.Visibility == Visibility.Visible &&
                 _activeServiceId is { } id && _webView is not null &&
                 _settings.Services.FirstOrDefault(service => service.Id == id) is { } suspendedService)
        { e.Handled = true; _ = OpenServiceAsync(suspendedService); }
        // A visible service owns Escape. Do not use application fullscreen as a reason to
        // consume it: TV/web players need the same Escape behavior as in a normal window.
        else if (e.Key == Key.Escape && _webView is not null && BrowserView.Visibility == Visibility.Visible) { return; }
        else if (e.Key == Key.Escape && _websiteFullscreen) { return; }
        else if (e.Key == Key.Escape && _appFullscreen) { SetApplicationFullscreen(false); e.Handled = true; }
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
        if (e.Key == Key.Right && modifiers == ModifierKeys.Alt && serviceActive)
        { BrowserForward_Click(this, e); e.Handled = true; return true; }
        if (((e.Key == Key.R && modifiers == ModifierKeys.Control) || (e.Key == Key.F5 && modifiers == ModifierKeys.None)) && serviceActive)
        { BrowserReload_Click(this, e); e.Handled = true; return true; }
        if (e.Key == Key.H && modifiers == ModifierKeys.Control && serviceActive)
        { e.Handled = true; _ = ReturnHomeAsync(); return true; }
        if (e.Key == Key.OemComma && modifiers == ModifierKeys.Control)
        { e.Handled = true; Settings_Click(this, e); return true; }
        return false;
    }

    internal async Task ClearTemporaryDataAsync()
    {
        // This is the one safe temporary-data definition used by Settings and automatic callers.
        // It deliberately excludes cookies, DOM storage, service workers, DRM/provider state,
        // saved permissions, VPN data, settings, artwork, and Data\Requirements.
        try
        {
            await StorageService.ClearActiveDiskCacheAsync(_webView?.CoreWebView2?.Profile)
                .WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException) { AppLog.Write("storage-cleanup", "WebView2 disk-cache cleanup timed out."); }
        catch (Exception ex) { AppLog.Write("storage-cleanup", ex.Message); }

        try
        {
            await Task.Run(() => StorageService.ClearAllTemporaryData(includeReplaceableComponents: true))
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException) { AppLog.Write("storage-cleanup", "Temporary-data cleanup exceeded its time limit."); }
        catch (Exception ex) { AppLog.Write("storage-cleanup", ex.Message); }
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

    internal async Task ResetAllWebsiteDataAsync()
    {
        try { if (_webView?.CoreWebView2?.Profile is { } profile) await profile.ClearBrowsingDataAsync(); }
        catch (Exception ex) { AppLog.Write("storage", "WebView2 browsing-data reset: " + ex.Message); }
        _sessionPermissions.ClearAll();
        ClearAllBrowserData();
    }

    internal void ResetMonkeyTv()
    {
        DestroyBrowser();
        try
        {
            _sessionPermissions.ClearAll();
            StorageService.ResetMonkeyTvData();
            _settings = new MonkeySettings();
            _store.Save(_settings);
            RenderHome();
        }
        catch (Exception ex) { AppLog.Write("storage", ex.Message); }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (ServiceEditorOverlay.Visibility == Visibility.Visible && _activeServiceEditor is { } editor)
        {
            e.Cancel = true;
            RequestImplicitEditorDismissal(editor, () => Close());
            return;
        }
        if (_allowClose) return;
        if (!_closing && _settings.CloseBehavior == CloseBehavior.AskBeforeExiting)
        {
            var confirmation = new ConfirmWindow("Exit Monkey TV?", "Your current service will close. Monkey TV will safely save its settings before exiting.", "Exit") { Owner = this };
            if (confirmation.ShowDialog() != true) { e.Cancel = true; return; }
        }
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
                StorageService.CleanupQueuedSharedProfileDeletes();
                StorageService.CleanupOrphanAssets(_settings);
            }).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException) { AppLog.Write("shutdown", "Post-disposal housekeeping exceeded the shutdown time limit."); }
        catch (Exception ex) { AppLog.Write("shutdown", ex.Message); }
        // The WebView has been disposed, so this uses the same temporary-data cleanup path as
        // Settings without touching an active profile.
        try { await ClearTemporaryDataAsync(); }
        catch (Exception ex) { AppLog.Write("shutdown", ex.Message); }
        SessionMarker.MarkClean();
        _allowClose = true;
        Close();
    }

    internal void ToggleApplicationFullscreen() => SetApplicationFullscreen(!_appFullscreen);

    internal void ApplyStartupPreferences()
    {
        if (_settings.StartupFullscreen) SetApplicationFullscreen(true);
        if (_settings.StartupMode == StartupMode.LastService && _settings.LastServiceId is { } id &&
            _settings.Services.FirstOrDefault(service => service.Id == id) is { } service)
            Dispatcher.BeginInvoke(new Action(() => _ = OpenServiceAsync(service)), DispatcherPriority.ApplicationIdle);
    }

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
        // Shell fullscreen deliberately retains Home/Services/Settings and a visible exit
        // control. This is application fullscreen, not provider-page fullscreen.
        HomeHeader.Visibility = Visibility.Visible;
        FullscreenGlyph.Text = _shellFullscreen ? "\uE73F" : "\uE740";
        FullscreenButton.ToolTip = _shellFullscreen ? "Exit fullscreen (F11)" : "Enter fullscreen (F11)";
        HomeScroll.Padding = _shellFullscreen ? new Thickness(28, 26, 28, 36) : new Thickness(24, 22, 24, 34);
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
