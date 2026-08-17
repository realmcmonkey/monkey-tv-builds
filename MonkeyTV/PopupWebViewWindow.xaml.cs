using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace MonkeyTV;

public partial class PopupWebViewWindow : Window
{
    public PopupWebViewWindow() => InitializeComponent();

    internal CoreWebView2 Core => PopupView.CoreWebView2;

    internal async Task InitializeAsync(CoreWebView2Environment environment, string? userAgent)
    {
        await PopupView.EnsureCoreWebView2Async(environment);
        PopupView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        PopupView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        PopupView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        PopupView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        PopupView.ZoomFactor = 1.0;
        if (!string.IsNullOrWhiteSpace(userAgent)) PopupView.CoreWebView2.Settings.UserAgent = userAgent;
        PopupView.CoreWebView2.WindowCloseRequested += (_, _) => Dispatcher.BeginInvoke(Close);
        PopupView.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.IsUserInitiated) ExternalNavigationPolicy.OpenInDefaultBrowser(e.Uri);
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        try { PopupView.Dispose(); } catch { }
        base.OnClosed(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
