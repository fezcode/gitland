using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    void ShowWelcome() {
        _contextBar.IsVisible = false;
        _repoName.Text = "Your repositories"; _branch.Text = "";
        var content = new StackPanel { Spacing = 22, MaxWidth = 520, Margin = new Thickness(36), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(Logo(64));
        content.Children.Add(Text("A clear view of your code.", 30, strong: true));
        content.Children.Add(Paragraph("Open a repository to review changes, write commits, and bring branches together. Everything starts with your files.", Muted));
        var actions = Col(
            WelcomeAction("Open repository", "Continue with a folder on this computer", "folder", PickRepository),
            WelcomeAction("Clone repository", "Bring a remote repository to this computer", "cloud", CloneDialog),
            WelcomeAction("Create repository", "Start a new Git history in a local folder", "plus", CreateRepositoryDialog));
        actions.Spacing = 8; content.Children.Add(actions);
        content.Children.Add(Button("Compare local files", () => Run(CompareLocalFiles), "compare"));
        if (RecentRepositories() is { Count: > 0 } recent) content.Children.Add(RecentSection(recent));
        _workspace.Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _status.Text = "Ready · Open, clone, or create a repository";
    }

    static IReadOnlyList<string> RecentRepositories() => GitlandApplication.Preferences.RecentRepositories ?? [];

    Control RecentSection(IReadOnlyList<string> recent) {
        var section = new StackPanel { Spacing = 4 };
        var heading = Text("Recent", 11, Faint, true); heading.Margin = new Thickness(2, 10, 0, 4);
        section.Children.Add(heading);
        foreach (string path in recent) section.Children.Add(RecentRow(path));
        return section;
    }

    Button RecentRow(string path) {
        // A folder that has been moved or deleted stays listed and says so, rather than silently
        // disappearing or failing only once it is clicked.
        bool present = Directory.Exists(path);
        string name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        if (name.Length == 0) name = path;

        var button = Button(name + " · " + path, () => Run(() => OpenRecent(path)));
        var labels = Col(Text(name, 13, present ? Ink : Muted, true), Text(present ? path : path + " · missing", 10, present ? Faint : Amber));
        labels.Spacing = 3;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        row.Children.Add(Icon(present ? "folder" : "close", present ? Muted : Amber, 16));
        Add(row, labels, 0, 1);
        Add(row, Icon("arrow-out", Faint, 13), 0, 2);
        button.Content = row;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(14, 10);
        button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.BorderThickness = new Thickness(0);
        button.Classes.Add("selection-item");
        ToolTip.SetTip(button, present ? path : path + Environment.NewLine + "This folder no longer exists.");

        var menu = new ContextMenu();
        menu.Items.Add(MenuAction("Open", () => OpenRecent(path), present));
        menu.Items.Add(MenuAction("Copy path", () => CopyText(path, "Path copied."), true));
        menu.Items.Add(MenuAction("Reveal in File Explorer", () => Reveal(path), present));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Remove from this list", () => Do(() => {
            SavePreferences(GitlandApplication.Preferences.WithoutRecent(path));
            ShowWelcome();
        }), true));
        button.ContextMenu = menu;
        return button;
    }

    async Task OpenRecent(string path) {
        if (!Directory.Exists(path)) {
            if (!await ReviewAction("This folder is gone", $"{path} no longer exists. Remove it from the recent list?", "Remove from list")) return;
            SavePreferences(GitlandApplication.Preferences.WithoutRecent(path));
            ShowWelcome();
            return;
        }
        await OpenRepository(path);
    }

    /// <summary>Records a repository as recently opened. Called for every successful open, so
    /// cloning and creating land here too, not only the folder picker.</summary>
    void RememberRepository(string root) => SavePreferences(GitlandApplication.Preferences.WithRecent(root));

    Button WelcomeAction(string title, string detail, string icon, Func<Task> action) {
        var button = Button(title, () => Run(action));
        var labels = Col(Text(title, 14, strong: true), Text(detail, 11, Faint)); labels.Spacing = 5;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 16 };
        row.Children.Add(Icon(icon, Muted, 20)); Add(row, labels, 0, 1); Add(row, Icon("arrow-out", Faint, 16), 0, 2);
        button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(18, 16);
        return button;
    }
}
