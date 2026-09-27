using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Gitland.App;

/// <summary>Short, interruptible feedback. Reduced motion applies to every live control.</summary>
public static class Motion {
    public static bool Reduced => GitlandApplication.Preferences.ReduceMotion;
    static event Action? Changed;
    public static void Refresh() => Changed?.Invoke();
    public static void Bind(Control control, Action<bool> apply) {
        bool listening = false;
        void Update() => apply(Reduced);
        void Attach() { if (!listening) { Changed += Update; listening = true; } Update(); }
        Update();
        control.AttachedToVisualTree += (_, _) => Attach();
        control.DetachedFromVisualTree += (_, _) => { Changed -= Update; listening = false; };
        if (control.IsAttachedToVisualTree()) Attach();
    }
    public static void ButtonFeedback(Button button) {
        button.TemplateApplied += (_, e) => {
            if (e.NameScope.Find("PART_ContentPresenter") is ContentPresenter presenter)
                // Navigation must clear hover in the same frame the pointer leaves.
                // Fading independent tab surfaces leaves two apparent hover targets.
                Bind(presenter, reduced => presenter.Transitions = reduced || button.Classes.Contains("workspace-tab") ? null : new Transitions {
                    new BrushTransition { Property = ContentPresenter.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(120), Easing = new CubicEaseOut() }
                });
        };
    }
    public static void PrepareReveal(Control control) => Bind(control, reduced => {
        control.Transitions = reduced ? null : new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseOut() } };
        if (reduced) control.Opacity = 1;
    });
    public static void Reveal(Control control) {
        if (Reduced) { control.Opacity = 1; return; }
        // Keep source text readable during the transition; never slide code under the pointer.
        var transitions = control.Transitions;
        control.Transitions = null; control.Opacity = .78; control.Transitions = transitions;
        Dispatcher.UIThread.Post(() => control.Opacity = 1, DispatcherPriority.Render);
    }
}
