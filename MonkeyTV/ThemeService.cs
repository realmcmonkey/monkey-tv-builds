using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace MonkeyTV;

internal static class ThemeService
{
    private static MonkeySettings? _settings;

    internal static void Initialize(MonkeySettings settings)
    {
        _settings = settings;
        Apply(settings);
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    internal static void Shutdown() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    internal static void Apply(MonkeySettings settings)
    {
        _settings = settings;
        var light = settings.Theme == AppTheme.Light ||
                    settings.Theme == AppTheme.System && IsWindowsLightTheme();
        var accent = AccentColor(settings.Accent);
        var resources = Application.Current.Resources;

        Set(resources, "BackgroundBrush", light ? "#FFF3F3F5" : "#FF101116");
        Set(resources, "SurfaceBrush", light ? "#FFFDFDFE" : "#FF1B1C22");
        Set(resources, "SurfaceHoverBrush", light ? "#FFE8E8ED" : "#FF292B33");
        Set(resources, "ControlBrush", light ? "#FFF7F7F9" : "#FF25262D");
        Set(resources, "ControlHoverBrush", light ? "#FFE5E5EA" : "#FF393B45");
        Set(resources, "SubtleSurfaceBrush", light ? "#BFFFFFFF" : "#991D1F27");
        Set(resources, "TextBrush", light ? "#FF17171A" : "#FFF5F5F7");
        Set(resources, "MutedBrush", light ? "#FF5D606A" : "#FFA8ABB4");
        Set(resources, "BorderBrush", light ? "#2D27272D" : "#3FFFFFFF");
        Set(resources, "AccentBrush", accent);
        Set(resources, "AccentSubtleBrush", Color.FromArgb(light ? (byte)35 : (byte)48, accent.R, accent.G, accent.B));
        Set(resources, "AccentTextBrush", Colors.White);
        Set(resources, "OverlayBrush", light ? "#F5FAFAFC" : "#F5191A20");
        Set(resources, "HomeGlowBrush", Color.FromArgb(light ? (byte)22 : (byte)36, accent.R, accent.G, accent.B));
    }

    internal static bool ReduceMotion => _settings?.Motion == MotionPreference.Reduced ||
        (_settings?.Motion == MotionPreference.System && !SystemParameters.ClientAreaAnimation);

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings?.Theme != AppTheme.System) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(_settings));
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }

    private static Color AccentColor(AppAccent accent)
    {
        if (accent == AppAccent.System)
        {
            try
            {
                var color = SystemParameters.WindowGlassColor;
                if (color.A > 0) return Color.FromRgb(color.R, color.G, color.B);
            }
            catch { }
            return (Color)ColorConverter.ConvertFromString("#7C5CFC");
        }
        return (Color)ColorConverter.ConvertFromString(accent switch
        {
            AppAccent.Blue => "#2563EB",
            AppAccent.Green => "#168A55",
            AppAccent.Orange => "#C86516",
            AppAccent.Red => "#C33B43",
            _ => "#7C5CFC"
        });
    }

    private static void Set(ResourceDictionary resources, string key, string color) =>
        Set(resources, key, (Color)ColorConverter.ConvertFromString(color));

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush existing && !existing.IsFrozen)
            existing.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }
}
