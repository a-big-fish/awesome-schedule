using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    event Action? portableAppearanceChanged;

    string EnsurePortableStyleFolder() => PortableAppearancePreset.EnsureFolder(
        Path.Combine(Path.GetDirectoryName(PreferencesPath)!, "PortableStyles"));

    void AddPortableStyleControls(Panel body)
    {
        var styles = new ComboBox { MinWidth = 124, DisplayMemberPath = nameof(PortableAppearancePreset.Name) };
        AutomationProperties.SetName(styles, "随身日程样式");
        var status = Text("", 11, "#52647D");
        status.TextWrapping = TextWrapping.Wrap;
        status.Visibility = Visibility.Collapsed;
        void Report(string message)
        {
            status.Text = message;
            status.Visibility = Visibility.Visible;
        }
        void Apply(PortableAppearancePreset preset)
        {
            var previous = preferences;
            preferences = preset.Apply(preferences);
            if (!SavePreferences())
            {
                preferences = previous;
                Report("样式未保存，请检查目录权限或磁盘空间。");
            }
            else Report($"已应用「{preset.Name}」");
            ApplyPortableAppearance();
        }

        void Reload(PortableAppearancePreset? selected = null)
        {
            var previous = styles.SelectedItem as PortableAppearancePreset;
            try { styles.ItemsSource = PortableAppearancePreset.ReadFolder(EnsurePortableStyleFolder()); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                styles.ItemsSource = PortableAppearancePreset.BuiltIn;
                Report("无法读取样式目录，已显示内置样式。");
            }
            var available = styles.Items.Cast<PortableAppearancePreset>().ToArray();
            var current = preferences with { PortableStyle = ActivePortableStyle };
            string? requested = selected?.FileName ?? previous?.FileName ?? preferences.PortableStyleFile;
            styles.SelectedItem = requested == null
                ? available.FirstOrDefault(item => item.Apply(current) with { PortableStyleFile = null } == current)
                    ?? available.FirstOrDefault(item => item.FileName == PortableAppearancePreset.DefaultFile)
                    ?? available.FirstOrDefault()
                : available.FirstOrDefault(item => item.FileName == requested);
            if (styles.SelectedItem == null) Report("选中的样式无法读取，请检查 JSON 或重新选择。");
        }
        Reload();

        var row = new WrapPanel();
        styles.Margin = new(0, 0, 5, 4);
        row.Children.Add(styles);
        void AddButton(string label, Action action)
        {
            var button = CompactPortableButton(label, action);
            button.Margin = new(0, 0, 5, 4);
            row.Children.Add(button);
        }
        void ApplySelection()
        {
            if (styles.SelectedItem is PortableAppearancePreset selected) Apply(selected);
        }
        AddButton("应用", ApplySelection);
        AddButton("导入 JSON", () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "随身日程样式 (*.json)|*.json" };
            bool opened = portableDetailOpen;
            portableDetailOpen = true;
            portableFocusReset?.Stop();
            try
            {
                var owner = Window.GetWindow(body);
                if ((owner == null ? picker.ShowDialog() : picker.ShowDialog(owner)) != true) return;
            }
            finally
            {
                portableDetailOpen = opened;
                RestartPortableIdleTimer();
            }
            try
            {
                var imported = PortableAppearancePreset.Import(picker.FileName, EnsurePortableStyleFolder());
                Reload(imported);
                Apply(imported);
            }
            catch (JsonException error) { Report($"无法导入样式。{error.Message}"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Report("无法读取或保存样式文件。"); }
        });
        AddButton("打开目录", () =>
        {
            try { Process.Start(new ProcessStartInfo(EnsurePortableStyleFolder()) { UseShellExecute = true }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { Report("无法打开样式目录。"); }
        });
        AddButton("重新读取", () => { Reload(); ApplySelection(); });
        body.Children.Add(row);
        body.Children.Add(status);
    }

    void AddPortableTransparencyControls(Panel body)
    {
        bool updating = false;
        void Change(Func<double, DesktopPreferences> update, double opacity)
        {
            if (updating) return;
            preferences = update(opacity);
            ApplyPortableAppearance();
        }

        AddPortableStyleControls(body);

        var overall = AddTransparencySlider(body, "整体透明度", PortableOverallOpacity, .9,
            value => Change(opacity => preferences with { PortableOverallOpacity = opacity }, value));
        var background = AddTransparencySlider(body, "底色透明度", PortableBackgroundOpacity, 1,
            value => Change(opacity => preferences with { PortableOpacity = opacity }, value));
        var border = AddTransparencySlider(body, "边框透明度", PortableBorderOpacity, 1,
            value => Change(opacity => preferences with { PortableBorderOpacity = opacity }, value));
        var text = AddTransparencySlider(body, "字体透明度", PortableTextOpacity, .9,
            value => Change(opacity => preferences with { PortableTextOpacity = opacity }, value));

        void Refresh()
        {
            updating = true;
            overall.Value = 1 - PortableOverallOpacity;
            background.Value = 1 - PortableBackgroundOpacity;
            border.Value = 1 - PortableBorderOpacity;
            text.Value = 1 - PortableTextOpacity;
            updating = false;
        }

        body.Loaded += (_, _) => { portableAppearanceChanged += Refresh; Refresh(); };
        body.Unloaded += (_, _) => portableAppearanceChanged -= Refresh;
        Refresh();
    }

    static Slider AddTransparencySlider(Panel body, string name, double opacity, double maxTransparency, Action<double> change)
    {
        var row = new Grid { Margin = new(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new() { Width = new(74) });
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = new(40) });
        var title = Text(name, 11);
        title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(title);
        var slider = new Slider
        {
            Minimum = 0, Maximum = maxTransparency, Value = Math.Clamp(1 - opacity, 0, maxTransparency),
            SmallChange = .01, LargeChange = .1, VerticalAlignment = VerticalAlignment.Center,
            Margin = new(2, 0, 3, 0)
        };
        AutomationProperties.SetName(slider, name);
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        var value = Text(slider.Value.ToString("P0"), 11, "#52647D");
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        slider.ValueChanged += (_, args) =>
        {
            value.Text = args.NewValue.ToString("P0");
            change(1 - args.NewValue);
        };
        body.Children.Add(row);
        return slider;
    }

    void AddPortableIdleSetting(Panel body)
    {
        const string title = "空闲后自动鼠标穿透";
        body.Children.Add(Text(title, 13, margin: new(0, 12, 0, 5)));
        var choice = new ComboBox
        {
            ItemsSource = new[] { 0, 5, 10, 15, 30, 60 }.Select(seconds =>
                new { Seconds = seconds, Label = seconds == 0 ? "关闭" : $"{seconds} 秒" }),
            DisplayMemberPath = "Label",
            SelectedValuePath = "Seconds",
            SelectedValue = PortableAutoLockSeconds,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 132
        };
        AutomationProperties.SetName(choice, title);
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedValue is not int seconds || seconds == PortableAutoLockSeconds) return;
            var previous = preferences;
            preferences = preferences with { PortableAutoLockSeconds = seconds };
            if (!SavePreferences())
            {
                preferences = previous;
                choice.SelectedValue = PortableAutoLockSeconds;
            }
            RestartPortableIdleTimer();
        };
        body.Children.Add(choice);
    }
}
