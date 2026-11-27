using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal enum PageRoute
{
    Today, Account, Admin, Calendar, CalendarDay, ScheduleEditor, Ideas,
    Plaza, Experiences, ExperienceDetail, NoteEditor, NoteExport, Interviews, Resume, Settings
}

internal abstract class WorkspacePage(MainWindow shell)
{
    protected Window Owner => shell;
    protected Dispatcher Dispatcher => shell.Dispatcher;
    protected FontFamily FontFamily => shell.FontFamily;
    protected WorkspaceRepository Repository => shell.Repository;
    protected StackPanel Content { get; private set; } = null!;
    protected ScrollViewer Viewport => shell.Viewport;
    protected TextBlock Status => shell.Status;
    protected PageRoute Route => shell.Route;
    protected bool FormDirty { get => shell.FormDirty; set => shell.FormDirty = value; }

    protected void Begin(PageRoute route, string? navigation, string title, string description = "")
    {
        Content = new StackPanel();
        shell.BeginPage(route, navigation, title, description, Content);
    }

    protected bool NeedAccount()
    {
        if (Repository.Current != null) return false;
        var panel = new StackPanel { Margin = new(12, 20, 12, 20) };
        panel.Children.Add(Text("从你的账号开始", 22));
        panel.Children.Add(Text("登录后，日程与面经会出现在这里。之后断网也能查看已缓存内容。",
            14, "#64748B", new(0, 12, 0, 20)));
        panel.Children.Add(Button("登录账号", shell.ShowAccount));
        Content.Children.Add(Surface(panel));
        return true;
    }

    protected void Navigate(Action destination) => shell.Navigate(destination);
    protected void OpenAccount() => shell.ShowAccount();
    protected void RestoreMain() => shell.RestoreMain();
    protected void Run(Func<Task> work) => shell.Run(work);
    protected void OpenLink(string value) => shell.OpenLink(value);
    protected bool Confirm(string title, string message, string accept = "确认", string cancel = "取消",
        Window? owner = null) => shell.Confirm(title, message, accept, cancel, owner);

    protected void Paragraph(string text) =>
        Content.Children.Add(Text(text, 14, "#64748B", new(0, 0, 0, 16)));

    protected void TrackViewportSize(FrameworkElement view, Action resize)
    {
        SizeChangedEventHandler changed = (_, _) =>
        {
            if (view.IsDescendantOf(Viewport)) resize();
        };
        resize();
        view.Loaded += (_, _) =>
        {
            Viewport.SizeChanged += changed;
            resize();
        };
        view.Unloaded += (_, _) => Viewport.SizeChanged -= changed;
    }
}
