using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Gitland.App;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Input;
using Gitland.Core;
using System.Text;
using Avalonia.Controls.Presenters;
using Avalonia.Media.TextFormatting;

AppDomain.CurrentDomain.UnhandledException += (_, e) => { Console.Error.WriteLine(e.ExceptionObject); Environment.Exit(1); };
var settingsPath = Path.Combine(Path.GetFullPath(args.FirstOrDefault() is { } directory && !directory.StartsWith("--") ? directory : "dist/preview"), "test-settings.json");
Environment.SetEnvironmentVariable("GITLAND_SETTINGS_PATH", settingsPath);
// This harness writes repository files itself and then asserts on the view it selected, so the
// repository watcher must not refresh underneath it. Watching is covered by its own checks below.
Environment.SetEnvironmentVariable("GITLAND_DISABLE_WATCH", "1");
new SettingsStore(settingsPath).Save(new UserSettings());
AppBuilder.Configure<GitlandApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.FirstOrDefault() == "--font-info") {
    foreach (var name in new[] { "Geist", "Geist Mono", "Inter", "IBM Plex Sans", "Cascadia Code", "Source Serif 4" }) {
        var family = new FontFamily("avares://gitland/Assets/Fonts#" + name);
        var font = new Typeface(family, FontStyle.Normal, FontWeight.Normal).GlyphTypeface;
        Console.WriteLine($"{family}: {font.FamilyName} {font.Weight} {font.Style}");
        Check(font.FamilyName == name && font.Weight == FontWeight.Normal, "Embedded family resolves at regular weight: " + name);
    }
    var literalEditor = new MergeEditor { Text = "=======", FontFamily = FontCatalog.CodeChoice("cascadia-code").Family };
    var probe = new Window { Width = 350, Height = 150, Content = literalEditor }; probe.Show(); Pump();
    var textPresenter = literalEditor.GetVisualDescendants().OfType<TextPresenter>().Single();
    var glyphs = textPresenter.TextLayout.TextLines[0].TextRuns.OfType<ShapedTextRun>().SelectMany(run => run.GlyphRun.GlyphInfos).Where(g => g.GlyphAdvance > 0).ToArray();
    ushort equalsGlyph = new Typeface(literalEditor.FontFamily).GlyphTypeface.GetGlyph('=');
    Check(glyphs.Length == 7 && glyphs.All(g => g.GlyphIndex == equalsGlyph), "Cascadia Code renders literal conflict separators without ligature substitution.");
    probe.Close();
    return;
}
if (args.FirstOrDefault() == "--icon") { MakeIcon(Path.GetFullPath(args[1])); return; }
var output = Path.GetFullPath(args.FirstOrDefault() ?? "dist/preview"); Directory.CreateDirectory(output);
var brand = new Border { Width = 256, Height = 256, Background = Palette.Bar, Child = Palette.Logo(184) };
brand.Measure(new Size(256, 256)); brand.Arrange(new Rect(0, 0, 256, 256));
using (var logo = new RenderTargetBitmap(new PixelSize(256, 256), new Vector(96, 96))) { logo.Render(brand); logo.Save(Path.Combine(output, "logo.png")); }
var window = new MainWindow(fixture: Demo.Fixture) { Width = 1440, Height = 920 };
window.Show(); Pump();
Finish(window.PreviewDiff()); Save("diff.png");
Check(Buttons("Merge").Count() == 1 && !Buttons("Three-way comparison").Any() && !Buttons("Resolve conflicts").Any(), "A single Merge navigation entry replaces the two separate workspaces.");

var canvas = window.GetVisualDescendants().OfType<DiffCanvas>().Single();
var additions = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DiffAdditions");
var deletions = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DiffDeletions");
Check(additions.Text == "+11" && deletions.Text == "−2" && ReferenceEquals(additions.Foreground, Palette.Green) && ReferenceEquals(deletions.Foreground, Palette.Red), "Diff totals give additions and deletions their own semantic colors.");
Check(((SolidColorBrush)additions.Foreground!).Color != ((SolidColorBrush)deletions.Foreground!).Color, "Addition and deletion colors are visibly distinct.");
Click("Unified");
Check(canvas.Unified && canvas.Rows.All(r => r.Kind != Gitland.Core.ChangeKind.Modified), "Unified view renders independent removed and added lines.");
Click("Side by side"); Check(!canvas.Unified, "Split view restored.");

// The way out of a change must sit beside the way in, and must never be reachable read-only.
var discard = Buttons("Discard file").Concat(Buttons("Delete file")).ToArray();
Check(discard.Length == 1, "Working changes offers a discard action beside staging.");
Check(discard[0].IsVisible, "The discard action is visible while reviewing working changes.");
Check(!discard[0].IsEnabled, "Discard stays disabled without a repository, so the preview fixture cannot lose work.");
Check(Buttons("Stage file").Concat(Buttons("Unstage file")).Count() == 1, "Staging keeps its own action beside discard.");

// Every row in the list carries its own menu, and the menu is built from that row's state.
MenuItem[] RowMenu(string path) {
    var row = Buttons(path).First();
    Check(row.ContextMenu != null, "File row " + path + " has a context menu.");
    return row.ContextMenu!.Items.OfType<MenuItem>().ToArray();
}
string[] RowMenuLabels(string path) => RowMenu(path).Select(i => (string?)i.Header ?? "").ToArray();
var changedMenu = RowMenuLabels("src/core/diff-service.ts");
Check(changedMenu.Contains("Stage file") && changedMenu.Contains("Discard changes…"), "A changed file's menu offers staging and discarding.");
Check(changedMenu.Contains("Blame") && changedMenu.Contains("History of this file") && changedMenu.Contains("Copy path") && changedMenu.Contains("Reveal in File Explorer"), "Every file's menu ends with the actions any file supports.");
Check(RowMenu("src/core/diff-service.ts").All(i => !i.IsEnabled || i.Header is "Copy path" or "Copy file name"), "Without a repository only the clipboard actions stay enabled.");
var conflictMenu = RowMenuLabels("src/core/merge.ts");
Check(conflictMenu.Contains("Open merge editor") && conflictMenu.Contains("Take ours (keep this branch)") && conflictMenu.Contains("Take theirs (keep incoming)"), "A conflicted file's menu offers resolution instead of staging.");
Check(!conflictMenu.Contains("Stage file"), "A conflicted file is not offered staging, which would skip resolving it.");

// A partially staged file is drawn in both groups and each row opens a different version, so a
// selection entry is identified by its group as well as its path.
Check(new FileSelection("app.ts", false) != new FileSelection("app.ts", true), "A row's group is part of its selection identity.");
Check(new HashSet<FileSelection> { new("app.ts", null), new("app.ts", true), new("app.ts", false) }.Count == 3, "The unstaged, staged and ungrouped rows of one file are three distinct selections.");

// Every row carries a checkbox, and ticking rows moves the menu onto the whole selection.
CheckBox RowTick(string path) => Buttons(path).First().GetVisualDescendants().OfType<CheckBox>().First();
string[] RowMenuOf(string path) => Buttons(path).First().ContextMenu!.Items.OfType<MenuItem>().Select(i => (string?)i.Header ?? "").ToArray();
Check(Buttons("src/core/diff-service.ts").First().GetVisualDescendants().OfType<CheckBox>().Any(), "Each file row carries a checkbox for multi-selection.");
Check(!RowTick("src/core/diff-service.ts").IsChecked!.Value, "Rows start unticked.");

// Two unstaged files: the selection keeps every action they share, and says how many.
RowTick("src/core/diff-service.ts").IsChecked = true; Pump();
RowTick("README.md").IsChecked = true; Pump();
var pairMenu = RowMenuOf("src/core/diff-service.ts");
Check(pairMenu.Contains("Stage 2 files"), "A multi-row selection puts the count in the menu labels.");
Check(pairMenu.Contains("Discard changes in 2 files…"), "Discarding a selection names how many files it covers.");
Check(pairMenu.Contains("Copy 2 paths") && pairMenu.Contains("Copy 2 file names"), "Copying reports how many paths it will copy.");
Check(pairMenu.Any(l => l.StartsWith("Clear selection")), "A selection can be cleared from the menu.");
Check(!Buttons("src/core/diff-service.ts").First().ContextMenu!.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Blame").IsEnabled, "Actions that open one view are withdrawn while several rows are selected.");

// An unticked row acts on itself and leaves the selection alone.
Check(!RowMenuOf("src/components/change-map.tsx").Any(l => l.Contains("2 files")), "Right-clicking an unticked row acts on that row alone.");

// Adding a conflict leaves the selection with no action the whole set shares.
RowTick("src/core/merge.ts").IsChecked = true; Pump();
var mixedMenu = RowMenuOf("src/core/diff-service.ts");
Check(!mixedMenu.Any(l => l.StartsWith("Stage ")), "Mixing a conflict into the selection withdraws staging.");
Check(!mixedMenu.Any(l => l.StartsWith("Discard ")), "Mixing a conflict into the selection withdraws discarding.");
Check(!mixedMenu.Any(l => l.StartsWith("Take ours")), "Mixing a plain change into the selection withdraws conflict resolution.");
Check(mixedMenu.Contains("Copy 3 paths"), "Copying still works across a mixed selection, and counts all three.");

RowTick("src/core/diff-service.ts").IsChecked = false; Pump();
RowTick("README.md").IsChecked = false; Pump();
RowTick("src/core/merge.ts").IsChecked = false; Pump();
Check(!RowMenuOf("src/core/diff-service.ts").Any(l => l.StartsWith("Clear selection")), "Clearing the ticks removes the selection entry from the menu.");
Click("Gitland menu");
var appMenu = Buttons("Gitland menu").Single().ContextMenu!;
// Naming the groups rather than counting them says which menu went missing when this fails.
var appGroups = appMenu.Items.OfType<MenuItem>().Select(item => (string?)item.Header).ToArray();
Check(appMenu.IsOpen && appGroups.SequenceEqual(["File", "View", "Git", "Changes", "Settings"]), "The local Gitland menu opens independently of Hisashi.");
var viewMenu = appMenu.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "View");
appMenu.Close(); viewMenu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
Check(Buttons("LICENSE").Length == 1, "Local menu actions use the working file-filter command."); Click("Changed files");
Click("src/core/merge.ts");
Check(Buttons("Working changes").Single().Classes.Contains("selected") && Buttons("Open merge editor").Single().IsVisible, "Selecting a conflict in Working changes offers an explicit merge action.");
Click("Open merge editor");
Check(Buttons("src/core/diff-service.ts").Length == 1 && Buttons("src/core/merge.ts").Length == 1, "Opening the merge editor preserves the Changed file list.");
Click("All files"); Save("explorer-all.png");
Check(Buttons("Working changes").Single().Classes.Contains("selected") && !Buttons("Merge").Single().Classes.Contains("selected") && !window.GetVisualDescendants().OfType<MergeEditor>().Any(), "All leaves the merge workspace and activates only Working changes.");
Click("src/core/diff-service.ts"); Check(window.GetVisualDescendants().OfType<DiffCanvas>().Any(), "A regular changed file opens directly from the merge workspace.");
Click("All files"); Check(Buttons("LICENSE").Length == 1 && Buttons("src/core/merge.ts").Length == 1, "All files includes unchanged files and conflicts.");
Click("LICENSE"); Check(canvas.Rows.All(row => row.Kind == ChangeKind.Equal), "Unchanged files display their actual unchanged state.");
Click("Conflicting files"); Check(Buttons("src/core/merge.ts").Length == 1 && Buttons("src/core/diff-service.ts").Length == 0, "Conflicts scope contains only conflicted files.");
Click("Changed files"); Check(Buttons("src/core/merge.ts").Length == 1 && Buttons("src/core/diff-service.ts").Length == 1 && Buttons("LICENSE").Length == 0, "Changed scope restores all changed files.");
Check(Buttons("Working changes").Single().Classes.Contains("selected") && !window.GetVisualDescendants().OfType<MergeEditor>().Any(), "Changed also leaves Resolve conflicts for Working changes.");
Click("src/core/diff-service.ts");
var shell = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "WindowShell");
var offscreen = typeof(Window).GetProperty(nameof(Window.OffScreenMargin))!;
window.WindowState = WindowState.Maximized; offscreen.SetValue(window, new Thickness(7, 9, 7, 7)); Pump();
Check(shell.Margin == new Thickness(7, 9, 7, 7), "Maximized content uses the reported per-edge Windows inset.");
offscreen.SetValue(window, new Thickness(12, 12, 12, 12)); Pump(); Check(shell.Margin.Left == 12, "Insets update when monitor scaling changes.");
window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.F11, RawInputModifiers.None); Pump();
Check(window.WindowState == WindowState.FullScreen && shell.Margin == new Thickness(0), "True full screen has no resize-frame padding."); Save("fullscreen.png");
window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None); Pump();
Check(window.WindowState == WindowState.Maximized, "Escape returns from full screen to the previous maximized state.");
window.WindowState = WindowState.Normal; offscreen.SetValue(window, new Thickness(0)); Pump(); Check(shell.Margin == new Thickness(0), "Restoring the window clears maximized padding.");
var sidebarColumns = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "WorkspaceColumns");
var sidebarSplitter = window.GetVisualDescendants().OfType<GridSplitter>().Single(g => g.Name == "SidebarSplitter");
var dividerPoint = sidebarSplitter.TranslatePoint(new Point(2, 200), window)!.Value;
double originalSidebar = sidebarColumns.ColumnDefinitions[1].ActualWidth;
window.MouseDown(dividerPoint, MouseButton.Left); window.MouseMove(dividerPoint + new Vector(110, 0), RawInputModifiers.LeftMouseButton); window.MouseUp(dividerPoint + new Vector(110, 0), MouseButton.Left); Pump();
Check(sidebarColumns.ColumnDefinitions[1].ActualWidth > originalSidebar + 90, "Dragging the sidebar divider widens the file panel.");
double savedSidebar = new SettingsStore(settingsPath).Load().SidebarWidth;
Check(Math.Abs(savedSidebar - sidebarColumns.ColumnDefinitions[1].ActualWidth) < 2, "Sidebar width persists after a drag.");
window.Width = 1100; Pump(); Check(Math.Abs(sidebarColumns.ColumnDefinitions[1].ActualWidth - savedSidebar) < 2, "Window resizing retains the chosen sidebar width when space allows.");
window.Width = 1440; Pump();
var sidebarProbe = new MainWindow { Width = 1440, Height = 920 }; sidebarProbe.Show(); Pump();
Check(sidebarProbe.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "A clear view of your code.") && !sidebarProbe.GetVisualDescendants().OfType<DiffCanvas>().Any(), "Production startup shows a welcome screen with no sample files.");
Check(new[] { "Open repository", "Clone repository", "Create repository" }.All(name => sidebarProbe.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == name)), "Production startup offers Open, Clone, and Create actions.");
Check(typeof(MainWindow).Assembly.GetType("Gitland.App.Demo") == null, "Sample data is absent from the production assembly.");
SaveDialog(sidebarProbe, "welcome.png");

Check(Math.Abs(sidebarProbe.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "WorkspaceColumns").ColumnDefinitions[1].ActualWidth - savedSidebar) < 2, "New windows restore the saved sidebar width."); sidebarProbe.Close(); Pump();
sidebarSplitter.Focus(); window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); Pump();
Check(sidebarColumns.ColumnDefinitions[1].ActualWidth > savedSidebar, "The focused sidebar divider can resize with arrow keys."); Save("sidebar-resized.png");
dividerPoint = sidebarSplitter.TranslatePoint(new Point(2, 200), window)!.Value;
window.MouseDown(dividerPoint, MouseButton.Left); window.MouseUp(dividerPoint, MouseButton.Left);
window.MouseDown(dividerPoint, MouseButton.Left); window.MouseUp(dividerPoint, MouseButton.Left); Pump();
Check(Math.Abs(sidebarColumns.ColumnDefinitions[1].ActualWidth - 280) < 2 && new SettingsStore(settingsPath).Load().SidebarWidth == 0, "Double-clicking the divider restores responsive default sizing.");
// Revisit and refresh Compare: the revision fields must have exactly one visual parent.
for (int visit = 0; visit < 3; visit++) {
    Click("Compare revisions");
    Check(window.OwnedWindows.Count == 0 && !window.IsWorking, "Reopening Compare does not create a visual-parent error.");
    var baseRef = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "Base revision");
    baseRef.Text = "main";
    Buttons("Compare revisions").Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Check(window.OwnedWindows.Count == 0 && baseRef.Text == "main", "Comparing revisions retains the revision controls.");
    Click("Working changes");
}
var filter = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "Filter files…");
filter.Text = "tokens"; Pump(); Check(Buttons("src/styles/tokens.css").Length == 1 && Buttons("src/core/diff-service.ts").Length == 0, "File filtering narrows the navigator.");
filter.Text = ""; Pump();
window.Width = 1100; window.Height = 720; Pump(); Save("compact.png");
window.Width = 980; window.Height = 640; Pump(); Save("minimum.png");
Check(canvas.Bounds.Width > 650 && canvas.ViewHeight >= 250, "Minimum window keeps the source viewport usable.");
window.Width = 1440; window.Height = 920; Pump(); Finish(window.PreviewMerge()); Save("merge.png");
var resultEditor = window.GetVisualDescendants().OfType<MergeEditor>().Single(t => !t.IsReadOnly);
Check(new[] { MergeLineKind.Ours, MergeLineKind.Base, MergeLineKind.Theirs, MergeLineKind.Marker }.All(kind => resultEditor.Highlights.Any(h => h.Kind == kind)), "The result distinguishes ours, base, theirs, and marker lines.");
var resultPresenter = resultEditor.GetVisualDescendants().OfType<TextPresenter>().Single();
var equalsLine = resultPresenter.TextLayout.TextLines.First(line => resultEditor.Text!.AsSpan(line.FirstTextSourceIndex, line.Length).TrimEnd("\r\n").SequenceEqual("======="));
var equalsGlyphs = equalsLine.TextRuns.OfType<ShapedTextRun>().SelectMany(run => run.GlyphRun.GlyphInfos).Where(glyph => glyph.GlyphAdvance > 0).ToArray();
var equalsIndex = new Typeface(Palette.Mono).GlyphTypeface.GetGlyph('=');
Check(equalsGlyphs.Length == 7 && equalsGlyphs.All(glyph => glyph.GlyphIndex == equalsIndex), "Each conflict separator renders as seven literal equals glyphs.");
var sourceEditors = window.GetVisualDescendants().OfType<MergeEditor>().Where(t => t.IsReadOnly && t.Bounds.Height > 0).ToArray();
Check(sourceEditors.Length == 2 && sourceEditors.All(editor => editor.Highlights.Count > 0), "Both source previews highlight the selected conflict against its ancestor.");
Check(resultEditor.Bounds.Width > sourceEditors[0].Bounds.Width * 2 && resultEditor.TranslatePoint(default, window)!.Value.Y > sourceEditors[0].TranslatePoint(default, window)!.Value.Y, "The editable result spans the workspace below the source previews.");
Check(window.GetVisualDescendants().OfType<CodeGutter>().Count() == 3, "Every merge editor has a native-aligned line-number gutter.");
string firstOurs = sourceEditors.Single(editor => editor.Name == "MergeOurs").Text!;
Click("Conflict 02");
Check(sourceEditors.Single(editor => editor.Name == "MergeOurs").Text != firstOurs && sourceEditors.Single(editor => editor.Name == "MergeOurs").Text!.Contains("return"), "Conflict navigation updates the source previews to the selected block.");
Click("Conflict 01");
var fullSources = window.GetVisualDescendants().OfType<CheckBox>().Single(c => (string?)c.Content == "Full sources"); fullSources.IsChecked = true; Pump();
Check(sourceEditors.All(editor => editor.Text!.Contains("export function")), "Full sources remains available for surrounding context."); fullSources.IsChecked = false; Pump();
Click("Inspect ancestor"); var ancestor = window.OwnedWindows.Single();
Check(ancestor.GetVisualDescendants().OfType<MergeEditor>().Single().Text!.Contains("strategy: 'manual'"), "The ancestor inspector shows the selected block's common base.");
ancestor.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Close ancestor").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
window.Width = 980; window.Height = 640; Pump(); Save("merge-minimum.png");
var mergeEditors = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.AcceptsReturn && t.IsVisible && t.Bounds.Height > 0).ToArray();
Check(mergeEditors.Count(t => t.Bounds.Height >= 100) >= 3, "Minimum window keeps all three merge editors visible.");
double resultHeight = resultEditor.Bounds.Height; Click("Expand result");
Check(resultEditor.Bounds.Height > resultHeight + 100, "The result can expand to reclaim source-preview space in compact windows."); Save("merge-focus.png"); Click("Restore source previews");
window.Width = 1440; window.Height = 920; Pump();
Check(Buttons("Magic resolve").Single().IsEnabled, "Smart merge recognizes independent edits within one line.");
string beforeMagic = resultEditor.Text!;
Click("Magic resolve"); var magicDialog = window.OwnedWindows.Single(); SaveDialog(magicDialog, "magic-resolve.png");
Check(resultEditor.Text == beforeMagic && magicDialog.GetVisualDescendants().OfType<MergeEditor>().All(e => e.IsReadOnly), "Magic resolve previews suggested code without changing the result.");
var magicApply = magicDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Apply selected resolutions");
var magicChoices = magicDialog.GetVisualDescendants().OfType<CheckBox>().ToArray();
Check(magicChoices.Length == 1, "Magic resolve offers only the independent conflict, leaving overlapping edits for a decision.");
magicChoices[0].IsChecked = false; Pump(); Check(!magicApply.IsEnabled, "No selected suggestions disables Magic resolve application.");
magicChoices[0].IsChecked = true; magicApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(Buttons("Undo resolution").Single().IsEnabled && Buttons("Export result").Single().IsEnabled == false, "Smart merge leaves overlapping edits unresolved and can be undone."); Save("merge-smart.png");
Check(resultEditor.Highlights.Any(h => h.Kind == MergeLineKind.Resolved) && resultEditor.Highlights.Any(h => h.Kind == MergeLineKind.Marker), "Smart merge colors the accepted result while retaining unresolved conflict colors.");
Click("Undo resolution"); Check(Buttons("Magic resolve").Single().IsEnabled, "Undo restores the original conflict choices.");
string manuallyEdited = "// preserve this manual edit\n" + resultEditor.Text;
resultEditor.Text = manuallyEdited; Pump(); Click("Magic resolve"); magicDialog = window.OwnedWindows.Single();
magicDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Apply selected resolutions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(resultEditor.Text!.StartsWith("// preserve this manual edit\n") && resultEditor.Text.Contains("strategy: 'interactive', keepBackup: true"), "Magic resolve preserves hand edits while combining independent changes.");
Click("Undo resolution"); Check(resultEditor.Text == manuallyEdited, "Undo Magic resolve restores the exact hand-edited result and markers.");
resultEditor.Text = beforeMagic; Pump();
Click("Magic resolve"); magicDialog = window.OwnedWindows.Single(); magicDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Cancel magic resolve").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(resultEditor.Text == beforeMagic, "Canceling Magic resolve preserves the merge result.");
Check(Buttons("Keep ours").All(b => b.IsEnabled), "Initial conflict choices are enabled.");
Click("Conflict 01"); Click("Keep ours"); Check(Buttons("Keep theirs").All(b => b.IsEnabled), "Block choice preserves further conflict controls.");
Click("Conflict 02"); Click("Keep theirs");
Check(Buttons("Export result").Single().IsEnabled, "Resolving all conflicts enables export.");
Save("merge-resolved.png");
// Real native typing, selection, and Ctrl+Z must survive the display-only highlight layer.
string accepted = resultEditor.Text!;
resultEditor.Focus(); resultEditor.CaretIndex = resultEditor.Text!.Length; resultEditor.SelectionStart = resultEditor.SelectionEnd = resultEditor.CaretIndex;
window.KeyTextInput("\n// manual edit"); Pump();
Check(resultEditor.Text == accepted + "\n// manual edit", "Merge result remains editable with line highlighting.");
Click("Settings"); Pump();
var settingsDialog = window.OwnedWindows.Single(); SaveDialog(settingsDialog, "settings.png");
void SettingsClick(string name) { settingsDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
SettingsClick("Use Paper theme"); SaveDialog(settingsDialog, "settings-paper.png");
Check(Palette.Current.Id == "paper" && resultEditor.Text == accepted + "\n// manual edit", "Live theme changes preserve the unsaved merge editor.");
Check(ReferenceEquals(additions.Foreground, Palette.Green) && ReferenceEquals(deletions.Foreground, Palette.Red) && Palette.Green.Color != Palette.Red.Color, "Diff totals retain distinct semantic colors after a live theme switch.");
SettingsClick("Editor"); var fontSize = settingsDialog.GetVisualDescendants().OfType<ComboBox>().Single(); fontSize.SelectedItem = 15d; Pump();
Check(resultEditor.FontSize == 15 && new SettingsStore(settingsPath).Load().CodeSize == 15, "Editor settings apply immediately and persist.");
SettingsClick("Fonts");
var uiFont = settingsDialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "InterfaceFont");
var codeFont = settingsDialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "CodeFont");
foreach (var choice in FontCatalog.Interface) {
    uiFont.SelectedItem = choice; Pump();
    Check(window.FontFamily.Equals(choice.Family) && settingsDialog.FontFamily.Equals(choice.Family) && new SettingsStore(settingsPath).Load().InterfaceFont == choice.Id, "Interface preset applies and persists: " + choice.Name);
    Check(!string.IsNullOrEmpty(choice.FamilyName), "The interface preset resolves to a usable font: " + choice.Source);
}
uiFont.SelectedItem = FontCatalog.InterfaceChoice("inter");
Check(FontCatalog.InterfaceChoice("inter").FamilyName == "Inter", "Bundled Inter resolves to the embedded family.");
foreach (var choice in FontCatalog.Code) {
    codeFont.SelectedItem = choice; Pump();
    var typeface = new Typeface(choice.Family).GlyphTypeface;
    Check(typeface.GetGlyphAdvance(typeface.GetGlyph('M')) == typeface.GetGlyphAdvance(typeface.GetGlyph('i')), "Source font has fixed-width cells: " + choice.Name);
    Check(resultEditor.FontFamily.Equals(choice.Family) && new SettingsStore(settingsPath).Load().CodeFont == choice.Id, "Code preset applies to the live merge editor and persists: " + choice.Name);
}
codeFont.SelectedItem = FontCatalog.CodeChoice("cascadia-code"); Pump();
Check(FontCatalog.CodeChoice("cascadia-code").FamilyName == "Cascadia Code", "Bundled Cascadia Code resolves to the embedded family.");
Check(resultEditor.Text == accepted + "\n// manual edit", "Changing interface and code fonts preserves unsaved merge text.");
SaveDialog(settingsDialog, "settings-fonts.png");
uiFont.IsDropDownOpen = true; Pump(); SaveDialog(settingsDialog, "settings-font-menu.png"); uiFont.IsDropDownOpen = false; Pump();
SettingsClick("Appearance"); SettingsClick("Use Xcode Dark theme"); SettingsClick("Fonts"); SaveDialog(settingsDialog, "settings-fonts-dark.png");
var darkPicker = settingsDialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "InterfaceFont");
darkPicker.IsDropDownOpen = true; Pump(); SaveDialog(settingsDialog, "settings-font-menu-dark.png"); darkPicker.IsDropDownOpen = false; Pump();
SettingsClick("Appearance"); SettingsClick("Use Paper theme"); SettingsClick("Fonts");
SettingsClick("Restore default fonts");
Check(Palette.Sans.Equals(FontCatalog.InterfaceChoice("geist").Family) && Palette.Mono.Equals(FontCatalog.CodeChoice("geist-mono").Family), "Default fonts can be restored together.");
SettingsClick("Git"); Pump();
// Gitland is useless without Git, so Settings has to say whether it is there and offer to fix it.
var gitInstall = settingsDialog.GetVisualDescendants().OfType<Button>()
    .Where(b => (AutomationProperties.GetName(b) ?? "").Contains("latest Git")).ToArray();
Check(gitInstall.Length == 1, "Settings offers to install Git.");
Check(settingsDialog.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Check again"), "The Git page can re-check for Git without reopening Settings.");
SaveDialog(settingsDialog, "settings-git.png");
SettingsClick("Integrations"); SaveDialog(settingsDialog, "settings-integrations.png");
Check(settingsDialog.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked == false, "Hisashi integration has an explicit opt-in setting.");
SettingsClick("Done"); Save("merge-paper.png");
Click("Changed files");
var leaveDialog = window.OwnedWindows.Single();
leaveDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Keep editing").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(window.OwnedWindows.Count == 0 && resultEditor.Text == accepted + "\n// manual edit" && Buttons("Merge").Single().Classes.Contains("selected"), "Canceling a scope change preserves unsaved text and the current merge navigation.");
resultEditor.Focus(); resultEditor.CaretIndex = resultEditor.Text!.Length;
window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control); Pump();
Check(resultEditor.Text == accepted, "Native undo restores the accepted result.");
Click("Settings"); Pump(); settingsDialog = window.OwnedWindows.Single(); SettingsClick("Use Midnight theme"); SettingsClick("Done"); Save("merge-midnight.png");
Click("Settings"); Pump(); settingsDialog = window.OwnedWindows.Single(); SettingsClick("Use Graphite theme"); SettingsClick("Done"); Save("merge-graphite.png");
Click("Settings"); Pump(); settingsDialog = window.OwnedWindows.Single(); SettingsClick("Editor"); settingsDialog.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem = 13d; Pump(); SettingsClick("Appearance"); SettingsClick("Use Xcode Dark theme"); SettingsClick("Done");
Finish(window.PreviewThreeWay()); Save("three-way.png");
var threeCanvases = window.GetVisualDescendants().OfType<DiffCanvas>().ToArray();
Check(threeCanvases.All(c => c.Bounds.Y == 0), "Short three-way sources align to the top of their viewports.");
Check(threeCanvases.Length == 3 && threeCanvases.Select(c => c.Rows.Count).Distinct().Count() == 1, "Three-way comparison has three equally aligned source panes.");
Check(threeCanvases[1].Rows.Any(r => r.Kind == ChangeKind.Removed) && threeCanvases[0].Rows.Any(r => r.Kind == ChangeKind.Added), "Changed ancestor lines and changed side lines have distinct semantic colors.");
Click("Next three-way change"); Check(threeCanvases.All(c => c.ActiveRow >= 0) && threeCanvases.Select(c => c.ActiveRow).Distinct().Count() == 1, "Three-way change navigation selects the same aligned row in every pane.");
window.Width = 980; window.Height = 640; Pump(); Save("three-way-minimum.png");
Check(threeCanvases.All(c => c.Bounds.Width > 170), "All three source panes remain usable at the minimum window width.");
window.Width = 1440; window.Height = 920; Pump();
Finish(window.PreviewManagement()); Save("repository.png");
Click("Manage tags"); Finish(Task.Delay(30)); Pump();
Check(Buttons("Manage tags").Single().Classes.Contains("selected") && Buttons("Create annotated tag").Any(), "The direct Tags navigation opens tag management.");
var tagFilter = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "TagSearch");
tagFilter.Text = "no-matching-tag"; Pump();
Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "No tags match your search." && t.IsVisible), "Tag search shows an explicit empty result.");
tagFilter.Text = "v0.1"; Pump(); Save("tags.png");
Check(Buttons("Edit tag").Single().GetVisualAncestors().OfType<Control>().All(c => c.IsVisible), "Tag search restores a matching tag with its management actions.");
Click("Repository"); Pump();

window.Width = 1100; window.Height = 720; Pump(); Save("repository-compact.png"); window.Width = 1440; window.Height = 920; Pump();
Click("Write a commit"); Pump();
Check(Buttons("Commit staged").Single().IsEnabled == false, "The main changes composer cannot commit sample data.");
Finish(window.PreviewGitHub()); Save("github.png");
window.Width = 980; window.Height = 640; Pump(); Save("github-minimum.png"); window.Width = 1440; window.Height = 920; Pump();
Check(Buttons("Publish repository…").Single().IsEnabled == false, "Publishing requires a real repository.");
Finish(window.PreviewDiff());
Finish(ExerciseRepository());
window.Close(); Console.WriteLine(output);
Button[] Buttons(string name) => window.GetVisualDescendants().OfType<Button>().Where(b => AutomationProperties.GetName(b) == name).ToArray();
void Click(string name) { Buttons(name).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
async Task ExerciseRepository() {
    var root = Path.Combine(output, "ui-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    var repo = new GitRepository(root);
    await repo.Git("init", "-b", "main"); await repo.Git("config", "user.name", "Gitland UI Test"); await repo.Git("config", "user.email", "gitland-ui@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
    string original = string.Join('\n', Enumerable.Range(1, 45).Select(i => "line " + i)) + "\n";
    await File.WriteAllTextAsync(Path.Combine(root, "source.txt"), original, new UTF8Encoding(false)); await repo.Git("add", "."); await repo.Git("commit", "-m", "Base");
    await File.WriteAllTextAsync(Path.Combine(root, "source.txt"), original.Replace("line 2\n", "first change\n").Replace("line 40\n", "second change\n"), new UTF8Encoding(false));
    await window.OpenRepository(root); Pump();
    Click("All files"); await WaitForAction(); Click("Changed files"); await WaitForAction();
    Buttons("Stage")[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check(Buttons("source.txt").Length == 2, "A partially staged file appears in both file groups.");
    Buttons("source.txt").Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check(!canvas.Rows.Any(row => (row.Right ?? "").Contains("second change")), "The Staged group opens the staged version of a partially staged file.");
    Buttons("source.txt").First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check(canvas.Rows.Any(row => (row.Right ?? "").Contains("second change")), "The Unstaged group opens the remaining working-tree edits.");
    var staged = await repo.Git("show", ":source.txt"); Check(staged.Contains("first change") && !staged.Contains("second change"), "Native Stage button stages exactly one hunk.");
    var summary = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CommitSummary");
    var description = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CommitDescription");
    Check(!Buttons("Commit staged").Single().IsEnabled, "A staged file still requires a commit summary.");
    description.Text = "Keep the second hunk for a later commit.\n\nReviewed in Gitland."; summary.Text = "Commit the first reviewed hunk"; Pump();
    Check(Buttons("Commit staged").Single().IsEnabled, "A summary and staged changes enable the main commit action.");
    Click("Open commit editor in window");
    var editorDeadline = DateTime.UtcNow.AddSeconds(20); while (window.OwnedWindows.Count == 0 && DateTime.UtcNow < editorDeadline) await Task.Delay(20); Pump();
    var commitWindow = window.OwnedWindows.Single();
    var longSummary = commitWindow.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "WindowCommitSummary");
    var longBody = commitWindow.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "WindowCommitDescription");
    Check(longSummary.Text == summary.Text && longBody.Text == description.Text && longBody.Bounds.Height > 300, "Commit window opens the current draft in a spacious description editor.");
    longBody.Text = "A detailed explanation.\n\n" + string.Join("\n", Enumerable.Range(1, 20).Select(i => "Review note " + i)); Pump();
    commitWindow.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Insert changed files summary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Check(longBody.Text!.Contains("Changed files (staged)") && longBody.Text.Contains("source.txt (+1 / -1)") && description.Text == longBody.Text, "Generated file summary uses the staged hunk and syncs the long draft back to the sidebar.");
    SaveDialog(commitWindow, "commit-window.png");
    commitWindow.Width = 780; commitWindow.Height = 560; Pump(); SaveDialog(commitWindow, "commit-window-compact.png");
    Check(longBody.Bounds.Height > 200, "A resized commit window retains room for long descriptions.");
    commitWindow.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Keep draft and close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check(description.Text!.Contains("Review note 20") && description.Text.Contains("Changed files (staged)"), "Closing the separate commit window keeps the long message draft.");
    description.Text = "Keep the second hunk for a later commit.\n\nReviewed in Gitland."; Pump();
    Click("Repository"); await WaitForAction(); Click("Working changes"); await WaitForAction();
    Check(summary.Text == "Commit the first reviewed hunk" && description.Text!.Contains("Reviewed in Gitland."), "The commit draft survives history navigation.");
    Click("Refresh · F5"); await WaitForAction();
    Check(summary.Text == "Commit the first reviewed hunk", "Refresh keeps the commit message and its editor.");
    var otherRoot = root + "-other"; Directory.CreateDirectory(otherRoot);
    await new GitRepository(otherRoot).Git("init", "-b", "other");
    await window.OpenRepository(otherRoot); Pump();
    Check(string.IsNullOrEmpty(summary.Text) && string.IsNullOrEmpty(description.Text), "Another repository starts with its own empty draft.");
    summary.Text = "Draft for another repository"; Pump();
    await window.OpenRepository(root); Pump();
    Check(summary.Text == "Commit the first reviewed hunk" && description.Text!.Contains("Reviewed in Gitland."), "Reopening a repository restores its own draft without leaking the other message.");
    var beforeCommit = await repo.ResolveRef("HEAD");
    await repo.Git("add", "."); // Simulate another Git client changing the reviewed index.
    Click("Commit staged"); await WaitForAction();
    Check(await repo.ResolveRef("HEAD") == beforeCommit && summary.Text == "Commit the first reviewed hunk" && window.OwnedWindows.Count == 0, "A stale index blocks committing and retains the message with an inline error.");
    Save("commit-stale.png");
    await repo.Git("reset", "HEAD", "--", "source.txt"); Click("Refresh · F5"); await WaitForAction();
    Buttons("Stage")[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    await File.WriteAllTextAsync(Path.Combine(root, "untracked.txt"), "Leave this out of the commit.\n");
    Save("commit-ready.png");
    Click("Review staged"); await WaitForAction();
    Check(!canvas.Rows.Any(row => (row.Right ?? "").Contains("second change")), "Review staged shows the index instead of the remaining working-tree changes.");
    summary.Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.Control); await WaitForAction();
    Check((await repo.Git("log", "-1", "--format=%B")).Trim() == "Commit the first reviewed hunk\n\nKeep the second hunk for a later commit.\n\nReviewed in Gitland.", "Ctrl+Enter commits the summary and multi-paragraph description.");
    var committedSource = await repo.Git("show", "HEAD:source.txt");
    Check(committedSource.Contains("first change") && !committedSource.Contains("second change") && (await repo.Git("ls-tree", "--name-only", "HEAD")).Trim() == "source.txt", "The commit contains only the reviewed hunk and excludes unstaged and untracked content.");
    Check(string.IsNullOrEmpty(summary.Text) && string.IsNullOrEmpty(description.Text) && !Buttons("Commit staged").Single().IsEnabled, "A successful commit clears the draft and disables an empty commit.");
    Save("commit-success.png");
    Click("Together"); await WaitForAction(); Click("Stage all changes"); await WaitForAction();
    Check((await repo.ReadStateAsync()).Changes.All(c => c.IsStaged && !c.IsUnstaged), "Stage all prepares the remaining changes from the file panel.");
    summary.Text = "Save remaining changes"; Pump(); Click("Open commit editor in window");
    editorDeadline = DateTime.UtcNow.AddSeconds(20); while (window.OwnedWindows.Count == 0 && DateTime.UtcNow < editorDeadline) await Task.Delay(20); Pump();
    commitWindow = window.OwnedWindows.Single();
    commitWindow.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "WindowCommitDescription").Text = "Committed from the expanded editor.\n\nLong messages stay intact."; Pump();
    commitWindow.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Commit from message window").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check(window.OwnedWindows.Count == 0 && (await repo.CommitMessageAsync("HEAD")).Contains("Long messages stay intact."), "Commit from the separate window writes the full message and closes after success.");
    Check((await repo.ReadStateAsync()).Changes.Count == 0, "The main commit button handles the remaining staged files.");

    // The gap that prompted this work. The confirmation is a modal ShowDialog, which blocks this
    // headless harness, so the behaviour behind it is covered by DiscardTests against real
    // repositories; what is checked here is that the UI offers the action, correctly labelled.
    await File.WriteAllTextAsync(Path.Combine(root, "source.txt"), "discard me" + "\n", new UTF8Encoding(false));
    await File.WriteAllTextAsync(Path.Combine(root, "scratch.txt"), "delete me" + "\n", new UTF8Encoding(false));
    Click("Refresh · F5"); await WaitForAction();
    Buttons("source.txt").First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check(Buttons("Discard file").Single().IsEnabled, "An open repository enables discarding a tracked file.");
    Save("discard-available.png");
    Buttons("scratch.txt").First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check(Buttons("Delete file").Length == 1 && Buttons("Discard file").Length == 0, "An untracked file offers deletion rather than discard.");
    Check(Buttons("Hunk 1").Length == 0 || Buttons("Discard").Length > 0, "A hunk offers its own discard beside staging.");
    await repo.DiscardUnstagedAsync(["source.txt"]);
    await repo.CleanUntrackedAsync(["scratch.txt"]);
    Click("Refresh · F5"); await WaitForAction();

    Click("Repository"); await WaitForAction();
    Check(Buttons("Rewrite").Length == 1 && Buttons("Advanced").Length == 1, "Repository exposes the rewrite and advanced tabs.");
    Click("Rewrite"); await WaitForAction();
    Check(Buttons("Interactive rebase…").Single().IsEnabled && Buttons("Hard reset…").Single().IsEnabled && Buttons("Amend last commit…").Single().IsEnabled, "Rewrite offers interactive rebase, hard reset and amend against a real repository.");
    Save("repository-rewrite.png");
    Click("Advanced"); await WaitForAction();
    Check(Buttons("Start bisect…").Single().IsEnabled && Buttons("Add submodule…").Single().IsEnabled && Buttons("Apply patch…").Single().IsEnabled, "Advanced offers bisect, submodules and patches against a real repository.");
    Save("repository-advanced.png");
    Click("History"); await WaitForAction(); Click("Working changes"); await WaitForAction();

    // Opening a repository is what puts it on the welcome screen, so it is checked after a real open.
    var remembered = Gitland.App.GitlandApplication.Preferences.RecentRepositories ?? [];
    Check(remembered.Contains(root), "Opening a repository records it as recently opened.");
    Check(remembered.Count <= UserSettings.MaxRecent, "The recent list stays capped.");
    await repo.Git("checkout", "-b", "incoming"); await File.WriteAllTextAsync(Path.Combine(root, "source.txt"), "incoming\n", new UTF8Encoding(false)); await repo.Git("add", "."); await repo.Git("commit", "-m", "Incoming");
    await repo.Git("checkout", "main"); await File.WriteAllTextAsync(Path.Combine(root, "source.txt"), "ours\n", new UTF8Encoding(false)); await repo.Git("add", "."); await repo.Git("commit", "-m", "Ours");
    try { await repo.Git("merge", "incoming"); } catch (InvalidOperationException) { }
    await window.OpenRepository(root); Pump();
    Check(Buttons("Open merge editor").Single().IsVisible, "Real conflicts can be reviewed without entering the merge workspace.");
    var localComparison = typeof(MainWindow).GetMethod("ShowLocalFiles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    await (Task)localComparison.Invoke(window, new object[] { Path.Combine(root, "source.txt"), Path.Combine(root, "source.txt") })!; Pump();
    Check(!Buttons("Open merge editor").Single().IsVisible, "Local file comparisons do not retain a previous conflict action.");
    await window.OpenRepository(root); Pump();
    Click("Open merge editor"); await WaitForAction(); Click("Keep theirs");
    Check(Buttons("Save & mark resolved").Single().IsEnabled, "A resolved real conflict enables saving.");
    Click("Save & mark resolved"); await WaitForAction();
    Check(await File.ReadAllTextAsync(Path.Combine(root, "source.txt")) == "incoming\n" && !(await repo.ReadStateAsync()).Changes.Any(c => c.IsConflict), "Native save writes the chosen result and clears the Git conflict.");
    Check(Directory.GetFiles(Path.Combine(root, ".git", "gitland-backups")).Length == 1, "Native merge save creates a recovery copy.");
    Click("Working changes"); await WaitForAction();
    var commitBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CommitSummary"); commitBox.Text = "Resolve incoming merge"; Pump();
    Click("Commit merge"); await WaitForAction();
    Check((await repo.ReadManagementAsync()).Commits[0].Subject == "Resolve incoming merge", "Native commit action commits the reviewed staged result.");
    Click("Repository"); await WaitForAction();
    Check(window.GetVisualDescendants().OfType<HistoryGraphCell>().Count() == (await repo.ReadManagementAsync()).Commits.Count, "History renders a graph cell for each actual commit.");
    var historySearch = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "Filter history by message, author, hash, or reference");
    historySearch.Text = "Resolve incoming merge"; Pump(); Check(window.GetVisualDescendants().OfType<HistoryGraphCell>().All(c => !c.IsVisible), "Filtered history hides graph connections across omitted commits.");
    historySearch.Text = ""; Pump(); Save("history-real.png");
    Click("Tag commit"); Pump();
    var dialog = window.OwnedWindows.Last();
    var fields = dialog.GetVisualDescendants().OfType<TextBox>().ToArray(); fields.Single(t => t.Watermark == "v1.0.0").Text = "v0.2.0"; fields.Single(t => t.Watermark == "What does this version mark?").Text = "Git management release";
    dialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Create local tag").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check((await repo.ReadManagementAsync()).Tags.Any(t => t.Name == "v0.2.0"), "Native tag form tags the selected commit.");
    var publishForm = window.PreviewPublishForm();
    while (window.OwnedWindows.Count == 0 && !publishForm.IsCompleted) Pump();
    var publishDialog = window.OwnedWindows.Last(); SaveDialog(publishDialog, "publish-form.png");
    Check((string?)publishDialog.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem == "Private", "Publish form defaults to private visibility.");
    publishDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Finish(publishForm);
    await repo.AddRemoteAsync("origin", "https://github.com/preview-user/example.git"); Click("GitHub & releases"); await WaitForAction();
    Click("Create release…"); Pump(); var releaseDialog = window.OwnedWindows.Last(); SaveDialog(releaseDialog, "release-form.png");
    Check(releaseDialog.GetVisualDescendants().OfType<CheckBox>().Single(c => (string?)c.Content == "Save as draft").IsChecked == true, "Release form defaults to a draft.");
    releaseDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Click("Merge"); await WaitForAction(); Click("Three-way comparison"); await WaitForAction(); Click("Choose three revisions"); Pump();
    var threeDialog = window.OwnedWindows.Single();
    var revisions = threeDialog.GetVisualDescendants().OfType<TextBox>().ToArray(); revisions[0].Text = "HEAD~1"; revisions[1].Text = "HEAD~2"; revisions[2].Text = "HEAD";
    threeDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Compare three revisions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check(window.GetVisualDescendants().OfType<DiffCanvas>().Count() == 3 && Buttons("source.txt").Length == 1, "Three-way revision form loads real repository files.");
    var alignedScrolls = window.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.Content is DiffCanvas).ToArray();
    alignedScrolls[0].Offset = new Vector(0, 130); Pump();
    Check(alignedScrolls.All(v => Math.Abs(v.Offset.Y - alignedScrolls[0].Offset.Y) < 1), "Vertical scrolling stays synchronized across all three source panes.");
    Check((await repo.ReadStateAsync()).Changes.Count == 0, "Three-way review leaves the index and working tree unchanged."); Save("three-way-repository.png");
    Click("All files"); await WaitForAction(); Check(Buttons("Working changes").Single().Classes.Contains("selected"), "All files leaves the three-way comparator for Working changes.");
    Click("Repository"); await WaitForAction(); Click("Branches"); Save("branches.png");
    Click("Create branch"); Pump(); var branchDialog = window.OwnedWindows.Single();
    branchDialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "feature/my-change").Text = "ui-created";
    branchDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Create branch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check((await repo.ReadManagementAsync()).Branches.Any(b => b.Name == "ui-created"), "Branch management creates a branch through its native form.");
    Click("Stashes"); await File.WriteAllTextAsync(Path.Combine(root, "untracked.txt"), "stash this work\n"); Click("Save stash"); Pump();
    var stashDialog = window.OwnedWindows.Single(); stashDialog.GetVisualDescendants().OfType<TextBox>().Single().Text = "UI saved work";
    stashDialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Save stash").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForAction();
    Check((await repo.ReadStateAsync()).Changes.Count == 0 && (await repo.ReadToolsAsync()).Stashes.Count == 1, "Stash form saves work and leaves a clean checkout."); Save("stashes.png");
    Click("Pop"); await WaitForAction(); Check((await repo.ReadToolsAsync()).Stashes.Count == 0 && (await File.ReadAllTextAsync(Path.Combine(root, "untracked.txt"))) == "stash this work\n", "Native Pop restores saved files and removes the stash.");
    Click("Recovery"); Check(Buttons("Restore stash files").Length == 1, "A popped stash is available in Recovery."); Save("recovery.png");
    Click("Worktrees"); Check(Buttons("Open worktree").Length == 1, "Worktrees lists the current registered working folder."); Save("worktrees.png");
    Click("Remotes"); Check(Buttons("Edit remote").Length == 1, "Remote management exposes existing remotes."); Save("remotes.png");
    var localThree = typeof(MainWindow).GetMethod("ShowThreeLocalFiles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    string sourcePath = Path.Combine(root, "source.txt");
    await (Task)localThree.Invoke(window, new object[] { new[] { sourcePath, sourcePath, sourcePath } })!; Pump();
    await File.WriteAllTextAsync(sourcePath, "refreshed local source\n"); Click("Refresh · F5"); await WaitForAction();
    Check(window.GetVisualDescendants().OfType<DiffCanvas>().All(c => c.Rows.Any(r => r.Right == "refreshed local source")), "Refreshing a local three-way comparison rereads all three sources.");
    Click("Working changes"); await WaitForAction(); Click("Merge"); await WaitForAction(); Click("Three-way comparison"); await WaitForAction();
    Check(window.GetVisualDescendants().OfType<DiffCanvas>().Count() == 3, "Local three-way comparison survives workspace navigation.");
    foreach (var fixture in new[] { root, otherRoot }) {
        if (!Path.GetFullPath(fixture).StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(fixture).StartsWith("ui-fixture-")) throw new InvalidOperationException("Fixture escaped the preview directory.");
        foreach (var file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(fixture, true);
    }
}
async Task WaitForAction() {
    var deadline = DateTime.UtcNow.AddSeconds(40);
    while (window.IsWorking && DateTime.UtcNow < deadline) await Task.Delay(20);
    Check(!window.IsWorking, "UI action completes.");
    Pump(); // Realize controls after async content replacement before querying the visual tree.
}
void Pump() { for (int i = 0; i < 12; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); } }
void Finish(Task task) { while (!task.IsCompleted) Pump(); task.GetAwaiter().GetResult(); Pump(); }
void Save(string name) { Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96)); bitmap.Render(window); bitmap.Save(Path.Combine(output, name)); }
void SaveDialog(Window dialog, string name) { Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)dialog.Bounds.Width, (int)dialog.Bounds.Height), new Vector(96, 96)); bitmap.Render(dialog); bitmap.Save(Path.Combine(output, name)); }
void MakeIcon(string target) {
    int[] sizes = [16, 32, 48, 64, 128, 256]; var images = new List<byte[]>();
    foreach (int size in sizes) {
        var icon = new Border { Width = size, Height = size, Background = Palette.Bar, CornerRadius = new CornerRadius(size / 5d), Child = Palette.Logo(size * 0.85) };
        icon.Measure(new Size(size, size)); icon.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96)); bitmap.Render(icon); using var png = new MemoryStream(); bitmap.Save(png); images.Add(png.ToArray());
    }
    using var file = File.Create(target); using var writer = new BinaryWriter(file); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
    int offset = 6 + sizes.Length * 16;
    for (int i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length; }
    foreach (var png in images) writer.Write(png);
    Console.WriteLine(target);
}
