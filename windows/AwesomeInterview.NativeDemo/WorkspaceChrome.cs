using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

public sealed partial class MainWindow
{
    internal bool Confirm(string title, string message, string accept = "确认", string cancel = "取消",
        Window? owner = null)
    {
        var dialog = new Window
        {
            Title = title, Owner = owner ?? this, Width = 480,
            SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 60,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#F6F8FC"), FontFamily = FontFamily, ShowInTaskbar = false
        };
        var body = new StackPanel { Margin = new(28) };
        body.Children.Add(Text(title, 21));
        body.Children.Add(Text(message, 14, "#64748B", new(0, 18, 0, 24)));
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right
        };
        var back = Button(cancel, () => dialog.DialogResult = false);
        back.IsCancel = true;
        actions.Children.Add(back);
        actions.Children.Add(Button(accept, () => dialog.DialogResult = true));
        body.Children.Add(actions);
        dialog.Content = body;
        dialog.Loaded += (_, _) => back.Focus();
        return dialog.ShowDialog() == true;
    }

    internal bool CanLeaveForm()
    {
        if (!FormDirty) return true;
        bool editingNote = Route == PageRoute.NoteEditor;
        string title = editingNote ? "尚未提交的面经" : "未保存的日程";
        string message = editingNote
            ? "离开前将保留已自动保存的本机草稿。尚未同步的输入请先点击保存。是否离开？"
            : "当前表单尚未保存，是否放弃输入？";
        if (!Confirm(title, message, "离开页面", "继续编辑")) return false;
        FormDirty = false;
        return true;
    }

    internal void OpenLink(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return;

        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Status.Text = "无法打开系统浏览器。"; }
    }

    internal void RestoreMain()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
