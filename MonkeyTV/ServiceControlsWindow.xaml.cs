using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonkeyTV;

public partial class ServiceControlsWindow : Window
{
    private long _animationGeneration;
    private bool _isHiding;
    private bool _isShown;
    private bool _homeRequestPending;

    public event EventHandler? BackRequested;
    public event EventHandler? ForwardRequested;
    public event EventHandler? ReloadRequested;
    public event EventHandler? HomeRequested;
    public event EventHandler? FullscreenRequested;
    public event EventHandler? RestartRequested;
    public event EventHandler? ResetSiteDataRequested;
    public event EventHandler? UserActivity;

    public ServiceControlsWindow()
    {
        InitializeComponent();
    }

    public void ShowOverlay()
    {
        if (_isShown && !_isHiding) return;
        var generation = ++_animationGeneration;
        _isHiding = false;
        _isShown = true;
        if (!IsVisible) Show();
        var opacity = new DoubleAnimation(1, TimeSpan.FromMilliseconds(145));
        opacity.Completed += (_, _) =>
        {
            if (generation == _animationGeneration) Opacity = 1;
        };
        BeginAnimation(OpacityProperty, opacity);
        ((TranslateTransform)Surface.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(145)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    public void HideOverlay(bool immediate = false)
    {
        if (!IsVisible) return;
        if (immediate)
        {
            ++_animationGeneration;
            _isHiding = false;
            _isShown = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Hide();
            return;
        }
        if (_isHiding) return;
        _isHiding = true;
        var generation = ++_animationGeneration;
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        animation.Completed += (_, _) =>
        {
            if (generation != _animationGeneration) return;
            _isHiding = false;
            _isShown = false;
            Opacity = 0;
            Hide();
        };
        BeginAnimation(OpacityProperty, animation);
        ((TranslateTransform)Surface.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-8, TimeSpan.FromMilliseconds(130)));
    }

    public void SetFullscreenState(bool fullscreen)
    {
        FullscreenButton.Content = fullscreen ? "\uE73F" : "\uE740";
        FullscreenButton.ToolTip = fullscreen ? "Exit application fullscreen (F11)" : "Enter application fullscreen (F11)";
    }

    public void SetNavigationState(bool canGoBack, bool canGoForward, bool isLoading)
    {
        BackButton.IsEnabled = canGoBack;
        ForwardButton.IsEnabled = canGoForward;
        NavigationProgress.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Surface_MouseEnter(object sender, MouseEventArgs e) => UserActivity?.Invoke(this, EventArgs.Empty);
    private void Surface_MouseMove(object sender, MouseEventArgs e) => UserActivity?.Invoke(this, EventArgs.Empty);
    private void Back_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); BackRequested?.Invoke(this, EventArgs.Empty); }
    private void Forward_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); ForwardRequested?.Invoke(this, EventArgs.Empty); }
    private void Reload_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); ReloadRequested?.Invoke(this, EventArgs.Empty); }
    private void Home_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => QueueHomeRequest();
    private void Home_Click(object sender, RoutedEventArgs e) => QueueHomeRequest();

    private void QueueHomeRequest()
    {
        // Pointer activation raises PreviewMouseLeftButtonUp and normally Click; UI Automation
        // may raise only Click. Coalesce both paths and dispatch after the current input route.
        if (_homeRequestPending) return;
        _homeRequestPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            _homeRequestPending = false;
            HomeRequested?.Invoke(this, EventArgs.Empty);
        }));
    }
    private void Fullscreen_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); FullscreenRequested?.Invoke(this, EventArgs.Empty); }
    private void Restart_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); RestartRequested?.Invoke(this, EventArgs.Empty); }
    private void ResetSiteData_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); ResetSiteDataRequested?.Invoke(this, EventArgs.Empty); }
}
