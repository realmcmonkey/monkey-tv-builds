using System.Reflection;
using System.Windows;

namespace MonkeyTV;

public partial class App : Application
{
    private const string FallbackVersion = "0.0.2-alpha";

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            PortablePaths.EnsureCreated();
            StorageService.CleanupQueuedSharedProfileDeletes();
            var recoveredFromUncleanExit = SessionMarker.Begin();
            StorageService.ClearAllTemporaryData();
            var store = new SettingsStore();
            var settings = store.Load();

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
            main.Show();
        }
        catch (Exception ex)
        {
            AppLog.Write("startup", ex.ToString());
            System.Windows.MessageBox.Show("Monkey TV could not start. A small diagnostic entry was saved in Data\\Logs.", "Monkey TV");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SessionMarker.MarkClean();
        base.OnExit(e);
    }
}
