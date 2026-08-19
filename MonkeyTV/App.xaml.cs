using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace MonkeyTV;

public partial class App : Application
{
    private const string FallbackVersion = "0.04-alpha";
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromSeconds(3);
    internal static long LastStartupMilliseconds { get; private set; }

    private static string InformationalVersion => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? FallbackVersion;

    public static string DisplayVersion
    {
        get => InformationalVersion.Split('+')[0];
    }

    public static string BuildNumber
    {
        get
        {
            const string marker = "+build.";
            var index = InformationalVersion.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index >= 0
                ? InformationalVersion[(index + marker.Length)..].Split('.')[0]
                : "0";
        }
    }

    // A build is a distinct alpha checkpoint even when its semantic prerelease version is unchanged.
    public static string AlphaWarningKey => $"{DisplayVersion}-build-{BuildNumber}";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startup = Stopwatch.StartNew();
        SplashWindow? splash = null;
        try
        {
            splash = new SplashWindow();
            splash.Show();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            PortablePaths.EnsureCreated();
            StorageService.CleanupQueuedSharedProfileDeletes();
            var recoveredFromUncleanExit = SessionMarker.Begin();
            StorageService.ClearAllTemporaryData();
            var store = new SettingsStore();
            var settings = store.Load();
            ThemeService.Initialize(settings);

            // Keep the branded splash unobscured for the requested minimum. Any genuinely
            // slower startup naturally keeps it visible longer without loading WebView2.
            var splashRemaining = MinimumSplashDuration - startup.Elapsed;
            if (splashRemaining > TimeSpan.Zero) await Task.Delay(splashRemaining);

            if (!string.Equals(settings.DismissedAlphaVersion, AlphaWarningKey, StringComparison.OrdinalIgnoreCase))
            {
                var warning = new AlphaWarningWindow(DisplayVersion) { Topmost = true };
                if (warning.ShowDialog() != true) { Shutdown(); return; }
                if (warning.DontShowAgain)
                {
                    settings.DismissedAlphaVersion = AlphaWarningKey;
                    store.Save(settings);
                }
            }

            if (!settings.SetupComplete)
            {
                var setup = new SetupWindow(settings, store);
                if (setup.ShowDialog() != true) { Shutdown(); return; }
                settings = store.Load();
            }

            if (recoveredFromUncleanExit && settings.SetupComplete)
                new RecoveryWindow().ShowDialog();

            var main = new MainWindow(settings, store);
            MainWindow = main;
            WindowPlacementService.Restore(main, settings);
            main.ContentRendered += (_, _) =>
            {
                if (splash is not null)
                {
                    splash.Close();
                    splash = null;
                }
                startup.Stop();
                LastStartupMilliseconds = startup.ElapsedMilliseconds;
            };
            main.Show();
        }
        catch (Exception ex)
        {
            AppLog.Write("startup", ex.ToString());
            splash?.Close();
            System.Windows.MessageBox.Show("Monkey TV could not start. A small diagnostic entry was saved in Data\\Logs.", "Monkey TV");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.Shutdown();
        SessionMarker.MarkClean();
        base.OnExit(e);
    }
}
