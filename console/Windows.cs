using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DafeiyuConsole
{
    public class SettingsWindow : Window
    {
        const string Api = "http://127.0.0.1:8771";
        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        readonly string mode;
        readonly Dictionary<string, bool> flags = new Dictionary<string, bool>();
        readonly TextBox ambBox = new TextBox();
        readonly TextBox budgetBox = new TextBox();
        readonly StackPanel modeRow = new StackPanel { Orientation = Orientation.Horizontal };

        public SettingsWindow(string root)
        {
            Title = "设置";
            Width = 560; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var json = SafeGetSync();
            var s = json.HasValue ? json.Value.GetProperty("settings") : default;
            mode = s.ValueKind == JsonValueKind.Object ? s.GetProperty("mode").GetString() : "group";

            var root_ = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(26)
            };
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "设置", FontSize = 19, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            panel.Children.Add(Section("回复模式"));
            modeRow.Margin = new Thickness(0, 4, 0, 6);
            panel.Children.Add(modeRow);
            foreach (var (v, t) in new[] { ("group", "只群聊"), ("priv", "只私聊"), ("both", "都回") })
                modeRow.Children.Add(ModeButton(v, t));

            panel.Children.Add(Section("程序"));
            panel.Children.Add(ToggleRowLocal("开机自启", AppState.AutostartEnabled(),
                v => AppState.SetAutostart(v)));

            panel.Children.Add(Section("功能"));
            foreach (var (key, label) in new[]
            {
                ("moments", "自己发说说"), ("tieba", "逛贴吧"), ("miss", "会想他"),
                ("proactive", "主动搭话"), ("quiet", "夜里睡觉")
            })
            {
                bool val = s.ValueKind == JsonValueKind.Object &&
                           s.TryGetProperty(key, out var pv) && pv.GetBoolean();
                flags[key] = val;
                panel.Children.Add(ToggleRow(key, label, val));
            }

            panel.Children.Add(Section("数值"));
            panel.Children.Add(NumberRow("插话概率 %", ambBox,
                s.ValueKind == JsonValueKind.Object ? s.GetProperty("amb").GetInt32().ToString() : "6"));
            panel.Children.Add(NumberRow("每日预算 ¥", budgetBox,
                s.ValueKind == JsonValueKind.Object ? s.GetProperty("budget").GetDouble().ToString("0.0") : "1.0"));

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 22, 0, 0)
            };
            btns.Children.Add(Btn("取消", false, () => { DialogResult = false; Close(); }));
            btns.Children.Add(Btn("保存并重启", true, async () => await SaveAsync()));
            panel.Children.Add(btns);

            root_.Child = panel;
            Content = root_;
            MouseLeftButtonDown += (s2, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        }

        TextBlock Section(string t) => new TextBlock
        {
            Text = t, FontSize = 12, Margin = new Thickness(0, 16, 0, 6),
            Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134))
        };

        Button ModeButton(string value, string text)
        {
            var b = Btn(text, value == mode, () =>
            {
                foreach (Button c in modeRow.Children)
                {
                    bool on = (string)c.Tag == value;
                    c.Background = new SolidColorBrush(on
                        ? Color.FromArgb(46, 245, 194, 231)
                        : Color.FromArgb(12, 255, 255, 255));
                    c.Foreground = new SolidColorBrush(on
                        ? Color.FromRgb(245, 194, 231)
                        : Color.FromRgb(166, 173, 200));
                }
            });
            b.Tag = value;
            b.Margin = new Thickness(0, 0, 8, 0);
            b.Width = 100;
            if (value != mode) b.Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
            return b;
        }

        FrameworkElement ToggleRow(string key, string label, bool value)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new TextBlock
            {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(t, 0);
            g.Children.Add(t);

            var sw = new Border
            {
                Width = 42, Height = 24, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(value
                    ? Color.FromRgb(203, 166, 247) : Color.FromArgb(30, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var knob = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Background = Brushes.White, HorizontalAlignment = value
                    ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(3)
            };
            sw.Child = knob;
            sw.MouseLeftButtonUp += (_, __) =>
            {
                flags[key] = !flags[key];
                sw.Background = new SolidColorBrush(flags[key]
                    ? Color.FromRgb(203, 166, 247) : Color.FromArgb(30, 255, 255, 255));
                knob.HorizontalAlignment = flags[key] ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            };
            Grid.SetColumn(sw, 1);
            g.Children.Add(sw);
            return g;
        }

        FrameworkElement ToggleRowLocal(string label, bool value, Action<bool> onChange)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new TextBlock
            {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(t, 0);
            g.Children.Add(t);
            bool state = value;
            var sw = new Border
            {
                Width = 42, Height = 24, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(state
                    ? Color.FromRgb(203, 166, 247) : Color.FromArgb(30, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var knob = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Background = Brushes.White,
                HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(3)
            };
            sw.Child = knob;
            sw.MouseLeftButtonUp += (_, __) =>
            {
                state = !state;
                sw.Background = new SolidColorBrush(state
                    ? Color.FromRgb(203, 166, 247) : Color.FromArgb(30, 255, 255, 255));
                knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                onChange(state);
            };
            Grid.SetColumn(sw, 1);
            g.Children.Add(sw);
            return g;
        }

        FrameworkElement NumberRow(string label, TextBox box, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new TextBlock
            {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(t, 0);
            g.Children.Add(t);
            box.Text = value; box.Width = 90; box.FontSize = 13;
            box.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            box.Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
            box.BorderThickness = new Thickness(0);
            box.Padding = new Thickness(8, 5, 8, 5);
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            return g;
        }

        Button Btn(string text, bool primary, Action click)
        {
            var b = new Button
            {
                Content = text, Padding = new Thickness(16, 8, 16, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 13, BorderThickness = new Thickness(0),
                Background = primary
                    ? new LinearGradientBrush(Color.FromRgb(245, 194, 231), Color.FromRgb(203, 166, 247), 45)
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Foreground = primary
                    ? new SolidColorBrush(Color.FromRgb(42, 16, 48))
                    : new SolidColorBrush(Color.FromRgb(166, 173, 200))
            };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            b.Template = tpl;
            b.Click += (_, __) => click();
            return b;
        }

        JsonElement? SafeGetSync()
        {
            try
            {
                // 关键：ConfigureAwait(false) —— 否则 UI 线程同步等待会和自己死锁
                var s = http.GetStringAsync(Api + "/api/state")
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                using var d = JsonDocument.Parse(s);
                return d.RootElement.Clone();
            }
            catch { return null; }
        }

        async Task SaveAsync()
        {
            string curMode = mode;
            foreach (Button c in modeRow.Children)
                if (c.Background is SolidColorBrush sb && sb.Color.A > 30) curMode = (string)c.Tag;

            var payload = new Dictionary<string, object>
            {
                ["mode"] = curMode,
                ["amb"] = int.TryParse(ambBox.Text, out var a) ? a : 6,
                ["budget"] = double.TryParse(budgetBox.Text, out var b) ? b : 1.0
            };
            foreach (var kv in flags) payload[kv.Key] = kv.Value;
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));
                await http.PostAsync(Api + "/api/settings", content, cts.Token);
            }
            catch { }
            DialogResult = true;
            Close();
        }
    }


    /// <summary>需要输入文字才能确认的危险操作</summary>
    public class DangerWindow : Window
    {
        const string KEYWORD = "全部关闭";

        public DangerWindow()
        {
            Title = "彻底关闭";
            Width = 460; Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var root = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromRgb(32, 18, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 243, 139, 168)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24)
            };
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "彻底关闭", FontSize = 17, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "机器人、看门狗、桌宠、还有她的 QQ 都会被关掉。\n"
                     + "之后要手动按 F1 才能重新启动。",
                FontSize = 12.5, Margin = new Thickness(0, 10, 0, 16),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 190, 210))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "请输入「" + KEYWORD + "」确认：",
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(150, 140, 160))
            });

            var input = new TextBox
            {
                FontSize = 14,
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(240, 235, 245)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                CaretBrush = new SolidColorBrush(Color.FromRgb(243, 139, 168))
            };
            panel.Children.Add(input);

            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var ok = MakeBtn("确认关闭", true);
            ok.IsEnabled = false;
            ok.Opacity = 0.45;
            var cancel = MakeBtn("取消", false);
            bar.Children.Add(cancel);
            bar.Children.Add(ok);
            panel.Children.Add(bar);

            input.TextChanged += (_, __) =>
            {
                bool match = input.Text.Trim() == KEYWORD;
                ok.IsEnabled = match;
                ok.Opacity = match ? 1.0 : 0.45;
            };
            input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && ok.IsEnabled) ok.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };

            cancel.Click += (_, __) => { DialogResult = false; Close(); };
            ok.Click += (_, __) => { DialogResult = true; Close(); };

            root.Child = panel;
            Content = root;
            Loaded += (_, __) => input.Focus();
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
            };
        }

        Button MakeBtn(string text, bool primary)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 13,
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = primary
                    ? new SolidColorBrush(Color.FromRgb(243, 139, 168))
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Foreground = primary
                    ? new SolidColorBrush(Color.FromRgb(40, 12, 20))
                    : new SolidColorBrush(Color.FromRgb(190, 180, 200))
            };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            b.Template = tpl;
            return b;
        }
    }

    public class MemoriesWindow : Window
    {
        const string Api = "http://127.0.0.1:8771";
        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        readonly StackPanel list = new StackPanel();
        string which = "group";

        public MemoriesWindow(string root)
        {
            Title = "记忆";
            Width = 640; Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var root_ = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24)
            };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "长期记忆", FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "双击不太方便，直接点删除", FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                Margin = new Thickness(0, 4, 0, 12)
            });

            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            tabs.Children.Add(TabButton("群聊记忆", "group"));
            tabs.Children.Add(TabButton("私聊记忆", "priv"));
            panel.Children.Add(tabs);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 430 };
            scroll.Content = list;
            panel.Children.Add(scroll);

            var close = new Button
            {
                Content = "关闭", Padding = new Thickness(16, 8, 16, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                BorderThickness = new Thickness(0)
            };
            close.Click += (_, __) => Close();
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            close.Template = tpl;
            panel.Children.Add(close);

            root_.Child = panel;
            Content = root_;
            MouseLeftButtonDown += (s2, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
            };
            Loaded += async (_, __) => await ReloadAsync();
        }

        Button TabButton(string text, string key)
        {
            var b = new Button
            {
                Content = text, Tag = key, Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(14, 6, 14, 6), BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                FontSize = 12
            };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            b.Template = tpl;
            b.Click += async (_, __) => { which = key; await ReloadAsync(); };
            return b;
        }

        async Task ReloadAsync()
        {
            list.Children.Clear();
            try
            {
                var s = await http.GetStringAsync(Api + "/api/memories?which=" + which);
                using var doc = JsonDocument.Parse(s);
                var items = doc.RootElement.GetProperty("items");
                if (items.GetArrayLength() == 0)
                {
                    list.Children.Add(new TextBlock
                    {
                        Text = "没有记忆", Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                        Margin = new Thickness(4, 8, 0, 0)
                    });
                }
                foreach (var it in items.EnumerateArray())
                {
                    int id = it.GetProperty("id").GetInt32();
                    string kind = it.GetProperty("kind").GetString();
                    string text = it.GetProperty("text").GetString();
                    var row = new Border
                    {
                        CornerRadius = new CornerRadius(10),
                        Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
                        Padding = new Thickness(12, 8, 12, 8),
                        Margin = new Thickness(0, 3, 0, 3)
                    };
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var tag = new TextBlock
                    {
                        Text = kind, FontSize = 10, Margin = new Thickness(0, 0, 10, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(203, 166, 247)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(tag, 0);
                    g.Children.Add(tag);

                    var body = new TextBlock
                    {
                        Text = text ?? "", FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.FromRgb(228, 219, 255)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(body, 1);
                    g.Children.Add(body);

                    var del = new Button
                    {
                        Content = "删除", FontSize = 11, Padding = new Thickness(9, 3, 9, 3),
                        BorderThickness = new Thickness(0),
                        Background = new SolidColorBrush(Color.FromArgb(30, 243, 139, 168)),
                        Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168)),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    del.Click += async (_, __) =>
                    {
                        var payload = new StringContent(
                            JsonSerializer.Serialize(new { which, id }),
                            Encoding.UTF8, "application/json");
                        await http.PostAsync(Api + "/api/mem/delete", payload);
                        await ReloadAsync();
                    };
                    Grid.SetColumn(del, 2);
                    g.Children.Add(del);

                    row.Child = g;
                    list.Children.Add(row);
                }
            }
            catch { }
        }
    }
}
