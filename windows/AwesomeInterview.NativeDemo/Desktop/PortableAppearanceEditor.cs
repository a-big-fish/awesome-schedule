using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    event Action? portableAppearanceChanged;

    void AddPortableTransparencyControls(Panel body)
    {
        bool updating = false;
        void Change(Func<double, DesktopPreferences> update, double opacity)
        {
            if (updating) return;
            preferences = update(opacity);
            ApplyPortableAppearance();
        }

        var choices = PortableAppearancePreset.BuiltIn.Select(preset =>
        {
            var button = CompactPortableButton(preset.Name, () =>
            {
                var previous = preferences;
                preferences = preset.Apply(preferences);
                if (!SavePreferences()) preferences = previous;
                ApplyPortableAppearance();
            });
            button.Margin = new(0, 0, 5, 4);
            button.ToolTip = $"整体 {preset.OverallTransparency}% · 底色 {preset.BackgroundTransparency}% · " +
                $"边框 {preset.BorderTransparency}% · 字体 {preset.TextTransparency}%";
            return (Preset: preset, Button: button);
        }).ToArray();
        if (choices.Length > 0)
        {
            var row = new WrapPanel();
            foreach (var choice in choices) row.Children.Add(choice.Button);
            body.Children.Add(row);
        }

        var overall = AddTransparencySlider(body, "整体透明度", PortableOverallOpacity, .9,
            value => Change(opacity => preferences with { PortableOverallOpacity = opacity }, value));
        var background = AddTransparencySlider(body, "底色透明度", PortableBackgroundOpacity, 1,
            value => Change(opacity => preferences with { PortableOpacity = opacity }, value));
        var border = AddTransparencySlider(body, "边框透明度", PortableBorderOpacity, 1,
            value => Change(opacity => preferences with { PortableBorderOpacity = opacity }, value));
        var text = AddTransparencySlider(body, "字体透明度", PortableTextOpacity, .9,
            value => Change(opacity => preferences with { PortableTextOpacity = opacity }, value));

        var activeFill = Brush("#EBF2FF");
        var activeInk = Brush("#2357CE");
        void Refresh()
        {
            updating = true;
            overall.Value = 1 - PortableOverallOpacity;
            background.Value = 1 - PortableBackgroundOpacity;
            border.Value = 1 - PortableBorderOpacity;
            text.Value = 1 - PortableTextOpacity;
            updating = false;
            foreach (var (preset, button) in choices)
            {
                bool selected = preset.Apply(preferences) == preferences;
                if (selected)
                {
                    button.Background = activeFill;
                    button.Foreground = activeInk;
                }
                else
                {
                    button.ClearValue(Control.BackgroundProperty);
                    button.ClearValue(Control.ForegroundProperty);
                }
                AutomationProperties.SetItemStatus(button, selected ? "当前预设" : "");
            }
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
