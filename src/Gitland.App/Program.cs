using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Gitland.Core;

namespace Gitland.App;

internal static class Program {
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<GitlandApplication>().UsePlatformDetect().LogToTrace();
}

public sealed class GitlandApplication : Application {
    public static readonly SettingsStore SettingsStore = new(Environment.GetEnvironmentVariable("GITLAND_SETTINGS_PATH") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gitland", "settings.json"));
    public static UserSettings Preferences { get; set; } = new();
    public override void Initialize() {
        Preferences = SettingsStore.Load(); Palette.Apply(Preferences.Theme);
        Palette.ApplyFonts(Preferences.InterfaceFont, Preferences.CodeFont);
        RequestedThemeVariant = Palette.Current.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(Palette.Styles());
    }
    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(desktop.Args?.FirstOrDefault());
        base.OnFrameworkInitializationCompleted();
    }
}
