using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DafeiyuConsole
{
    /// <summary>
    /// 一个窗口管所有设置：环境（路径/端口）、行为（模式/开关）、外观（头像/背景）
    /// </summary>
    public class ConsoleSettingsWindow : Window
    {
        readonly string api;
        readonly string cfgPath;
        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        readonly TextBox rootBox = Box();
        readonly TextBox pythonBox = Box();
        readonly TextBox portBox = Box();
        readonly TextBox petBox = Box();
        readonly TextBox napcatBox = Box();
        readonly TextBox ambBox = Box();
        readonly TextBox budgetBox = Box();
        readonly TextBox baseUrlBox = Box();
        readonly PasswordBox apiKeyBox = new PasswordBox
        {
            FontSize = 12, Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(228, 222, 245)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(203, 166, 247))
        };
        readonly TextBox modelBox = Box();
        readonly PasswordBox visionKeyBox = new PasswordBox
        {
            FontSize = 12, Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(228, 222, 245)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };

        readonly CheckBox privChk = Chk("私聊回复");
        readonly CheckBox groupChk = Chk("群聊回复");
        readonly CheckBox bootChk = Chk("开机自启");
        readonly CheckBox quietChk = Chk("静音提醒");
        bool oldPortChanged;

        string AvatarDir => Path.Combine(MainWindow.RootDir, "头像");
        string AssetDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");

        public ConsoleSettingsWindow(string apiBase, string cfgPath)
        {
            api = apiBase;
            this.cfgPath = cfgPath;

            Title = "设置";
            Width = 620; Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var root = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromRgb(22, 19, 36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24)
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock
            {
                Text = "⚙", FontSize = 20, Margin = new Thickness(0, 0, 10, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 166, 247)),
                VerticalAlignment = VerticalAlignment.Center
            });
            head.Children.Add(new TextBlock
            {
                Text = "设置", FontSize = 17, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(236, 232, 250)),
                VerticalAlignment = VerticalAlignment.Center
            });
            head.Children.Add(new TextBlock
            {
                Text = "留空就自动找", FontSize = 11, Margin = new Thickness(12, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(130, 122, 155)),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetRow(head, 0);
            grid.Children.Add(head);

            // 内容
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 16, 0, 12),
                Padding = new Thickness(0, 0, 8, 0)
            };
            var body = new StackPanel();
            body.Children.Add(Section("环境", new FrameworkElement[]
            {
                Row("机器人目录", rootBox, "选择", PickRoot),
                Row("Python", pythonBox, "选择", PickPython),
                Row("控制台端口", portBox, null, null),
                Row("桌宠 exe", petBox, null, null),
                Row("NapCat 脚本", napcatBox, "选择", PickNapcat)
            }));
            body.Children.Add(Section("接口", new FrameworkElement[]
            {
                Row("接口地址", baseUrlBox, null, null),
                RowPwd("API Key", apiKeyBox),
                Row("模型", modelBox, null, null),
                RowPwd("视觉 Key（可选）", visionKeyBox)
            }));
            body.Children.Add(Section("行为", new FrameworkElement[]
            {
                Wrap(privChk, groupChk, bootChk, quietChk),
                Row("主动发言概率 %", ambBox, null, null),
                Row("每日预算 ¥", budgetBox, null, null)
            }));
            body.Children.Add(Section("外观", new FrameworkElement[]
            {
                Wrap(Btn("🖼 换头像", false, PickAvatar), Btn("🌄 换背景", false, PickBackground))
            }));
            scroll.Content = body;
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            // 底部
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var openCfg = Btn("打开配置文件夹", false, () =>
            {
                var dir = Path.GetDirectoryName(string.IsNullOrEmpty(cfgPath)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console.json")
                    : cfgPath);
                if (!Directory.Exists(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            });
            openCfg.Margin = new Thickness(0, 0, 8, 0);
            bar.Children.Add(openCfg);
            bar.Children.Add(Btn("取消", false, Close));
            var save = Btn("保存", true, Save);
            save.Margin = new Thickness(8, 0, 0, 0);
            bar.Children.Add(save);
            Grid.SetRow(bar, 2);
            grid.Children.Add(bar);

            root.Child = grid;
            Content = root;

            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is FrameworkElement fe
                    && !(fe is TextBox)) DragMove();
            };

            Loaded += (_, __) => LoadCurrent();
        }

        // ── 读取当前值 ──
        async void LoadCurrent()
        {
            rootBox.Text = MainWindow.RootDir;
            pythonBox.Text = MainWindow.PythonPath;
            portBox.Text = MainWindow.ApiPort.ToString();
            petBox.Text = MainWindow.PetExe;
            napcatBox.Text = MainWindow.NapcatBat;

            try
            {
                var json = await http.GetStringAsync(api + "/api/settings").ConfigureAwait(true);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("settings", out var s))
                {
                    var mode = s.TryGetProperty("mode", out var m) ? m.GetString() : "both";
                    privChk.IsChecked = mode == "both" || mode == "priv";
                    groupChk.IsChecked = mode == "both" || mode == "group";
                    if (s.TryGetProperty("amb_pct", out var ap) && ap.ValueKind == JsonValueKind.Number)
                        ambBox.Text = ap.GetDouble().ToString("0.##");
                }
            }
            catch { privChk.IsChecked = true; groupChk.IsChecked = true; }

            LoadApiConfig();

            bootChk.IsChecked = AppState.AutostartEnabled();
            quietChk.IsChecked = MainWindow.Quiet;
            ambBox.Text = "6";
            budgetBox.Text = "1.0";
        }

        // ── 保存 ──
        async void Save()
        {
            try
            {
                // 1) 环境 → console.json
                var cfg = new Dictionary<string, object>
                {
                    ["bot_root"] = rootBox.Text.Trim().Replace('\\', '/'),
                    ["python"] = pythonBox.Text.Trim().Replace('\\', '/'),
                    ["pet_exe"] = petBox.Text.Trim(),
                    ["napcat_bat"] = napcatBox.Text.Trim().Replace('\\', '/'),
                    ["ports"] = new Dictionary<string, int>
                    {
                        ["console"] = ParsePort(portBox.Text, 8771),
                        ["pet_api"] = 8765,
                        ["onebot"] = 3001
                    }
                };
                var target = string.IsNullOrEmpty(cfgPath)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console.json")
                    : cfgPath;
                File.WriteAllText(target,
                    JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));

                SaveApiConfig();

                // 2) 行为 → 后端
                var mode = privChk.IsChecked == true
                    ? (groupChk.IsChecked == true ? "both" : "priv")
                    : (groupChk.IsChecked == true ? "group" : "none");
                var body = new Dictionary<string, object>
                {
                    ["mode"] = mode,
                    ["amb"] = ParseDouble(ambBox.Text, 6),
                    ["budget"] = ParseDouble(budgetBox.Text, 1.0)
                };
                var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                await http.PostAsync(api + "/api/settings", content).ConfigureAwait(true);

                AppState.SetAutostart(bootChk.IsChecked == true);
                MainWindow.Quiet = quietChk.IsChecked == true;
                

                oldPortChanged = ParsePort(portBox.Text, 8771) != MainWindow.ApiPort;

                if (oldPortChanged)
                    MessageBox.Show("端口改了，要重启控制台才能生效。", "设置已保存",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception e)
            {
                MessageBox.Show("保存失败：" + e.Message, "出错了",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        string BotConfigPath => Path.Combine(MainWindow.RootDir, "config.json");

        void LoadApiConfig()
        {
            try
            {
                if (!File.Exists(BotConfigPath)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(BotConfigPath));
                var r = doc.RootElement;
                if (r.TryGetProperty("api", out var api))
                {
                    if (api.TryGetProperty("base_url", out var b)) baseUrlBox.Text = b.GetString();
                    if (api.TryGetProperty("api_key", out var k)) apiKeyBox.Password = k.GetString();
                    if (api.TryGetProperty("model", out var m)) modelBox.Text = m.GetString();
                }
                if (r.TryGetProperty("bot", out var bot)
                    && bot.TryGetProperty("vision", out var vis)
                    && vis.TryGetProperty("api_key", out var vk))
                    visionKeyBox.Password = vk.GetString();
            }
            catch { }
        }

        void SaveApiConfig()
        {
            try
            {
                System.Text.Json.Nodes.JsonNode node;
                if (File.Exists(BotConfigPath))
                    node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(BotConfigPath));
                else
                    node = new System.Text.Json.Nodes.JsonObject();

                if (node is not System.Text.Json.Nodes.JsonObject root)
                    root = new System.Text.Json.Nodes.JsonObject();

                var api = root["api"] as System.Text.Json.Nodes.JsonObject
                          ?? new System.Text.Json.Nodes.JsonObject();
                api["base_url"] = baseUrlBox.Text.Trim();
                api["api_key"] = apiKeyBox.Password.Trim();
                api["model"] = modelBox.Text.Trim();
                root["api"] = api;

                if (!string.IsNullOrWhiteSpace(visionKeyBox.Password))
                {
                    var bot = root["bot"] as System.Text.Json.Nodes.JsonObject
                              ?? new System.Text.Json.Nodes.JsonObject();
                    var vis = bot["vision"] as System.Text.Json.Nodes.JsonObject
                              ?? new System.Text.Json.Nodes.JsonObject();
                    vis["api_key"] = visionKeyBox.Password.Trim();
                    bot["vision"] = vis;
                    root["bot"] = bot;
                }

                File.WriteAllText(BotConfigPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                MessageBox.Show("写 config.json 失败：" + e.Message);
            }
        }

        static int ParsePort(string s, int def) =>
            int.TryParse((s ?? "").Trim(), out var v) && v > 1024 && v < 65535 ? v : def;

        static double ParseDouble(string s, double def) =>
            double.TryParse((s ?? "").Trim(), out var v) && v >= 0 ? v : def;

        // ── 选文件 ──
        void PickRoot()
        {
            // 不弹文件夹框（那样要引入 WinForms），改成选机器人里的 bot.py
            var f = File2("选机器人目录里的 bot.py（或 config.json）",
                          "机器人入口|bot.py|配置文件|config.json|所有文件|*.*");
            if (f == null) return;
            rootBox.Text = Path.GetDirectoryName(f) ?? f;
        }

        void PickPython()
        {
            var f = File2("选 pythonw.exe 或 python.exe", "可执行|*.exe|所有文件|*.*");
            if (f != null) pythonBox.Text = f;
        }

        void PickNapcat()
        {
            var f = File2("选 NapCat 的启动脚本", "脚本|*.bat;*.exe|所有文件|*.*");
            if (f != null) napcatBox.Text = f;
        }

        void PickAvatar()
        {
            var f = File2("选一张头像", "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*");
            if (f == null) return;
            try
            {
                Program.Root = MainWindow.RootDir;
                var crop = new CropWindow(f) { Owner = this };
                if (crop.ShowDialog() == true && !string.IsNullOrEmpty(crop.ResultPath))
                {
                    MainWindow.RequestAvatarRefresh();
                    MessageBox.Show("头像换好了，主界面已经更新。", "完成",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception e) { MessageBox.Show("换头像失败：" + e.Message); }
        }

        void PickBackground()
        {
            var f = File2("选一张背景图", "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*");
            if (f == null) return;
            try
            {
                Directory.CreateDirectory(AssetDir);
                File.Copy(f, Path.Combine(AssetDir, "bg.png"), true);
                MainWindow.RequestBackgroundRefresh();
                MessageBox.Show("背景换好了。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception e) { MessageBox.Show("换背景失败：" + e.Message); }
        }

        static string File2(string title, string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ── 小工具 ──
        static TextBox Box() => new TextBox
        {
            FontSize = 12, Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(228, 222, 245)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(203, 166, 247))
        };

        static CheckBox Chk(string text) => new CheckBox
        {
            Content = text, FontSize = 12.5, Margin = new Thickness(0, 0, 18, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 198, 228))
        };

        static StackPanel Wrap(params FrameworkElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
            foreach (var i in items) sp.Children.Add(i);
            return sp;
        }

        static FrameworkElement Section(string title, FrameworkElement[] rows)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            sp.Children.Add(new TextBlock
            {
                Text = title, FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 140, 220)),
                Margin = new Thickness(0, 0, 0, 8)
            });
            foreach (var r in rows) sp.Children.Add(r);
            return sp;
        }

        static FrameworkElement Row(string label, TextBox box, string btn, Action onClick)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var t = new TextBlock
            {
                Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(176, 168, 200))
            };
            Grid.SetColumn(t, 0);
            g.Children.Add(t);

            Grid.SetColumn(box, 1);
            g.Children.Add(box);

            if (btn != null && onClick != null)
            {
                var b = Btn(btn, false, onClick);
                b.Margin = new Thickness(8, 0, 0, 0);
                b.Padding = new Thickness(12, 6, 12, 6);
                Grid.SetColumn(b, 2);
                g.Children.Add(b);
            }
            return g;
        }

        static FrameworkElement RowPwd(string label, PasswordBox box)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var t = new TextBlock
            {
                Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(176, 168, 200))
            };
            Grid.SetColumn(t, 0);
            g.Children.Add(t);
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            return g;
        }

        public static Button Btn(string text, bool primary, Action onClick)
        {
            var b = new Button
            {
                Content = text, FontSize = 12.5,
                Padding = new Thickness(16, 7, 16, 7),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = primary
                    ? new SolidColorBrush(Color.FromRgb(203, 166, 247))
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Foreground = primary
                    ? new SolidColorBrush(Color.FromRgb(28, 20, 46))
                    : new SolidColorBrush(Color.FromRgb(196, 188, 214))
            };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            b.Template = tpl;
            if (onClick != null) b.Click += (_, __) => onClick();
            return b;
        }
    }
}
