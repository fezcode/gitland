using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    Window? _settingsWindow;
    TextBlock? _integrationStatus, _settingsStatus;
    void ApplyPreferences(bool resetDiffLayout = false) {
        var preferences = GitlandApplication.Preferences;
        Palette.Apply(preferences.Theme);
        Palette.ApplyFonts(preferences.InterfaceFont, preferences.CodeFont);
        FontFamily = Sans;
        foreach (var window in OwnedWindows) window.FontFamily = Sans;
        if (Application.Current != null) Application.Current.RequestedThemeVariant = Current.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        _canvas.CodeSize = preferences.CodeSize; _canvas.RefreshAppearance();
        foreach (var canvas in _threeCanvases) { canvas.CodeSize = preferences.CodeSize; canvas.RefreshAppearance(); }
        foreach (var editor in this.GetVisualDescendants().Concat(OwnedWindows.SelectMany(w => w.GetVisualDescendants())).OfType<MergeEditor>()) { editor.FontFamily = Mono; editor.FontSize = preferences.CodeSize; editor.RefreshAppearance(); }
        if (resetDiffLayout) {
            _unified = preferences.DefaultUnified; _splitButton.Classes.Set("primary", !_unified); _unifiedButton.Classes.Set("primary", _unified);
            if (_comparison != null) SetUnified(preferences.DefaultUnified);
        }
        _map.InvalidateVisual(); UpdateMenus();
    }
    bool SavePreferences(UserSettings settings) {
        try {
            bool resetLayout = settings.DefaultUnified != GitlandApplication.Preferences.DefaultUnified;
            GitlandApplication.SettingsStore.Save(settings); GitlandApplication.Preferences = settings.Normalize(); ApplyPreferences(resetLayout);
            if (_settingsStatus != null) { _settingsStatus.Text = "Saved automatically"; _settingsStatus.Foreground = Faint; }
            return true;
        } catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) {
            if (_settingsStatus != null) { _settingsStatus.Text = "Could not save settings: " + e.Message; _settingsStatus.Foreground = Red; }
            return false;
        }
    }
    async Task ShowSettings() {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        if (_busy || OwnedWindows.Count > 0) return;
        var dialog = _settingsWindow = new Window { Title = "Gitland Settings", Width = 840, Height = 640, MinWidth = 700, MinHeight = 560, Background = Ground, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var heading = Col(Text("Settings", 23, strong: true), Text("Make Gitland feel like your workspace.", 12, Muted)); heading.Spacing = 7;
        Add(shell, new Border { Child = heading, Padding = new Thickness(26, 22), BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1) }, 0);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("178,*") };
        var tabs = new StackPanel { Spacing = 4, Margin = new Thickness(12, 18) };
        var content = new ContentControl();
        Add(body, new Border { Background = Bar, Child = tabs, BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 1, 0) }, 0);
        Add(body, new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, 0, 1); Add(shell, body, 1);
        string selected = "appearance";
        void Render() {
            tabs.Children.Clear();
            foreach (var (id, label, icon) in new[] { ("appearance", "Appearance", "layers"), ("fonts", "Fonts", "file"), ("editor", "Editor", "diff"), ("git", "Git", "branch"), ("integrations", "Integrations", "cloud"), ("about", "About Gitland", "branch") }) {
                var tab = Button(label, () => { selected = id; Render(); }, icon); tab.HorizontalAlignment = HorizontalAlignment.Stretch; tab.HorizontalContentAlignment = HorizontalAlignment.Left; tab.Classes.Add("selection-item"); tab.Classes.Set("selected", selected == id); tab.Background = selected == id ? SelectedSurface : Brushes.Transparent; tab.BorderThickness = new Thickness(0); tab.Padding = new Thickness(12, 9); tabs.Children.Add(tab);
            }
            var page = new StackPanel { Spacing = 22, Margin = new Thickness(26, 24) };
            if (selected == "appearance") {
                page.Children.Add(Col(Text("Color theme", 17, strong: true), Paragraph("Colors for the interface, diffs, and merge editor.")));
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 12, RowSpacing = 12 };
                for (int i = 0; i < Themes.Length; i++) {
                    var theme = Themes[i]; bool active = theme.Id == GitlandApplication.Preferences.Theme;
                    var tile = Button("Use " + theme.Name + " theme", () => { if (SavePreferences(GitlandApplication.Preferences with { Theme = theme.Id })) Render(); });
                    tile.HorizontalAlignment = HorizontalAlignment.Stretch; tile.HorizontalContentAlignment = HorizontalAlignment.Stretch; tile.Padding = new Thickness(0);
                    tile.BorderBrush = Hairline; tile.BorderThickness = new Thickness(1); tile.Background = active ? SelectedSurface : Raised;
                    var preview = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*"), Height = 62, Background = Brush(theme.Ground) };
                    preview.Children.Add(new Border { Background = Brush(theme.Bar) });
                    var code = new StackPanel { Spacing = 6, Margin = new Thickness(12, 12) };
                    foreach (double width in new[] { 85d, 120d, 64d }) code.Children.Add(new Border { Width = width, Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = Brush(width == 120 ? theme.Accent : theme.Muted), Opacity = width == 85 ? .5 : .8 });
                    Add(preview, code, 0, 1);
                    var label = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 10) }; label.Children.Add(Text(theme.Name, 12, strong: true)); if (active) Add(label, Icon("check", Ink, 14), 0, 1);
                    tile.Content = Col(preview, label); ToolTip.SetTip(tile, theme.Description); Add(grid, tile, i / 2, i % 2);
                }
                page.Children.Add(grid); page.Children.Add(Paragraph("Themes apply immediately. Your open files and merge edits stay in place.", Faint));
            } else if (selected == "fonts") {
                page.Children.Add(Col(Text("Fonts", 17, strong: true), Paragraph("Choose interface and source fonts separately.")));
                ComboBox Picker(string name, IReadOnlyList<FontChoice> choices, string current, Action<FontChoice> change) {
                    var picker = new ComboBox { Name = name, ItemsSource = choices, SelectedItem = choices.First(f => f.Id == current), HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 370 };
                    Avalonia.Automation.AutomationProperties.SetName(picker, name == "InterfaceFont" ? "Interface font" : "Code font");
                    picker.ItemTemplate = new FuncDataTemplate<FontChoice>((choice, _) => {
                        if (choice == null) return new TextBlock();
                        var sample = Text("The quick brown fox jumps over the lazy dog 0123456789", 11, Muted); sample.FontFamily = choice.Family;
                        var entry = Col(Text(choice.Name, 12), sample); entry.Spacing = 5; entry.Margin = new Thickness(0, 4); return entry;
                    });
                    picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is FontChoice choice) change(choice); };
                    return picker;
                }
                var uiSource = Paragraph(FontCatalog.InterfaceChoice(GitlandApplication.Preferences.InterfaceFont).Source, Faint);
                var codeSource = Paragraph(FontCatalog.CodeChoice(GitlandApplication.Preferences.CodeFont).Source, Faint);
                var ui = Picker("InterfaceFont", FontCatalog.Interface, GitlandApplication.Preferences.InterfaceFont, choice => {
                    if (SavePreferences(GitlandApplication.Preferences with { InterfaceFont = choice.Id })) uiSource.Text = choice.Source;
                });
                var code = Picker("CodeFont", FontCatalog.Code, GitlandApplication.Preferences.CodeFont, choice => {
                    if (SavePreferences(GitlandApplication.Preferences with { CodeFont = choice.Id })) codeSource.Text = choice.Source;
                });
                page.Children.Add(Col(Field("Interface font", ui), uiSource));
                page.Children.Add(Col(Field("Code font", code), codeSource));
                page.Children.Add(Paragraph("Bundled fonts work without installation. System presets use the first available family and fall back to a bundled font. The resolved family is shown above.", Faint));
                page.Children.Add(Paragraph("Changes apply immediately and are saved. Source fonts stay monospaced; operators and conflict markers remain literal.", Faint));
                page.Children.Add(Button("Restore default fonts", () => { if (SavePreferences(GitlandApplication.Preferences with { InterfaceFont = "geist", CodeFont = "geist-mono" })) Render(); }));
            } else if (selected == "editor") {
                page.Children.Add(Col(Text("Source & comparison", 17, strong: true), Paragraph("Comfortable text and predictable editing.")));
                var size = new ComboBox { ItemsSource = new[] { 11d, 12d, 13d, 14d, 15d, 16d, 17d, 18d }, SelectedItem = GitlandApplication.Preferences.CodeSize, Width = 130 };
                size.SelectionChanged += (_, _) => { if (size.SelectedItem is double value) SavePreferences(GitlandApplication.Preferences with { CodeSize = value }); };
                page.Children.Add(Field("Code font size", size, "Choose the typeface in Fonts. Operators and conflict markers stay literal."));
                var unified = new CheckBox { Content = "Use unified diffs by default", IsChecked = GitlandApplication.Preferences.DefaultUnified };
                unified.IsCheckedChanged += (_, _) => SavePreferences(GitlandApplication.Preferences with { DefaultUnified = unified.IsChecked == true }); page.Children.Add(unified);
                var sync = new CheckBox { Content = "Synchronize scrolling across merge panes", IsChecked = GitlandApplication.Preferences.SyncMergeScroll };
                sync.IsCheckedChanged += (_, _) => SavePreferences(GitlandApplication.Preferences with { SyncMergeScroll = sync.IsChecked == true }); page.Children.Add(sync);
                page.Children.Add(Paragraph("Ctrl+Z undoes text edits. Conflict choices have their own Undo resolution action.", Faint));
            } else if (selected == "git") {
                page.Children.Add(Col(Text("Git", 17, strong: true), Paragraph("Gitland runs every repository operation through Git, so it needs Git for Windows on your PATH.")));
                _gitStatus = Text("Checking…", 13);
                _gitDetail = Paragraph("", Faint);
                _gitInstall = Button("Install the latest Git", () => Run(InstallGit), "arrow-down", primary: true);
                _gitInstall.IsEnabled = false;
                var recheck = Button("Check again", () => Run(RefreshGitStatus), "refresh");
                page.Children.Add(Row(Icon("branch", Faint, 17), _gitStatus));
                page.Children.Add(_gitDetail);
                page.Children.Add(WrapActions(_gitInstall, recheck));
                page.Children.Add(Paragraph("Gitland installs Git with winget when it is available, and otherwise downloads the official 64-bit installer from the Git for Windows project on GitHub. Windows asks for administrator permission; Gitland never bypasses that prompt."));
                page.Children.Add(Paragraph("After a first install, restart Gitland so it picks up the updated PATH.", Faint));
                _ = RefreshGitStatus();
            } else if (selected == "integrations") {
                page.Children.Add(Col(Text("Hisashi menubar", 17, strong: true), Paragraph("Put Gitland’s File, View, Git and Settings menus in Hisashi’s OS Window Layer.")));
                var enabled = new CheckBox { Content = "Enable hoswl integration", IsChecked = GitlandApplication.Preferences.HoswlEnabled };
                enabled.IsCheckedChanged += (_, _) => { SavePreferences(GitlandApplication.Preferences with { HoswlEnabled = enabled.IsChecked == true }); UpdateIntegrationStatus(); }; page.Children.Add(enabled);
                _integrationStatus = Text("", 12, Accent); UpdateIntegrationStatus(); page.Children.Add(Row(Icon("cloud", Faint, 17), _integrationStatus));
                page.Children.Add(Paragraph("In Hisashi, enable Settings → OS Window Layer. Gitland reconnects automatically when Hisashi starts. Menus follow the active Gitland window and reflect which actions are available."));
                page.Children.Add(Paragraph("The Gitland menu remains available in the title bar.", Faint));
            } else {
                page.Children.Add(Col(Logo(56), Text("Gitland " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3), 23, strong: true), Paragraph("A workspace for changes, comparisons and confident merges.")));
                page.Children.Add(Paragraph("Built with .NET and Avalonia. Xcode Dark follows Clockt’s palette. Geist, Inter, Cascadia Code, IBM Plex Sans, and Source Serif 4 are bundled with their licenses."));
                page.Children.Add(Paragraph("Ctrl+O · Open repository\nCtrl+Enter · Commit staged changes\nCtrl+, · Settings\nF11 · Enter or leave full screen\nEscape · Leave full screen", Faint));
            }
            content.Content = page;
        }
        _settingsStatus = Text("Saved automatically", 11, Faint);
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(26, 12) }; foot.Children.Add(_settingsStatus); Add(foot, Button("Done", () => dialog.Close(), primary: true), 0, 1); Add(shell, foot, 2);
        Render(); dialog.Content = shell; dialog.Opened += (_, _) => UpdateMenus();
        try { await dialog.ShowDialog(this); } finally { _settingsWindow = null; _integrationStatus = null; _settingsStatus = null; UpdateMenus(); }
    }
    void UpdateIntegrationStatus() {
        if (_integrationStatus != null) _integrationStatus.Text = !GitlandApplication.Preferences.HoswlEnabled ? "Off" : _hoswl?.Connected == true ? "Connected to Hisashi" : "Waiting for Hisashi · reconnects automatically";
    }
}
