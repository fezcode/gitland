using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
        _workspace.Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _status.Text = "Ready · Open, clone, or create a repository";
    }
    Button WelcomeAction(string title, string detail, string icon, Func<Task> action) {
        var button = Button(title, () => Run(action));
        var labels = Col(Text(title, 14, strong: true), Text(detail, 11, Faint)); labels.Spacing = 5;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 16 };
        row.Children.Add(Icon(icon, Muted, 20)); Add(row, labels, 0, 1); Add(row, Icon("arrow-out", Faint, 16), 0, 2);
        button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(18, 16);
        return button;
    }
}
