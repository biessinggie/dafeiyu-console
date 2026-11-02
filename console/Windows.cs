using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.IO;

namespace DafeiyuConsole
{
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


    /// <summary>换头像的裁剪窗口：拖动 + 滚轮缩放，圆形预览</summary>
    public class CropWindow : Window
    {
        const int VIEW = 480;
        readonly Image img = new Image { Stretch = Stretch.UniformToFill };
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly TranslateTransform shift = new TranslateTransform(0, 0);
        readonly Border frame;
        Point dragStart;
        bool dragging;

        public string ResultPath { get; private set; }

        public CropWindow(string srcPath)
        {
            Title = "裁剪头像";
            Width = 560; Height = 660;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var root = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromRgb(24, 20, 40)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20)
            };
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "调整头像", FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 230, 255))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "拖动移动 · 滚轮缩放 · 圆形范围就是最终头像",
                FontSize = 11.5, Margin = new Thickness(0, 6, 0, 14),
                Foreground = new SolidColorBrush(Color.FromRgb(150, 142, 175))
            });

            // 圆形预览框
            frame = new Border
            {
                Width = VIEW, Height = VIEW,
                CornerRadius = new CornerRadius(VIEW / 2),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(16, 12, 28)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var tg = new TransformGroup();
            tg.Children.Add(scale);
            tg.Children.Add(shift);
            img.RenderTransform = tg;
            img.RenderTransformOrigin = new Point(0.5, 0.5);
            img.Source = Load(srcPath);
            frame.Child = img;

            // 圆形描边
            var ring = new Border
            {
                Width = VIEW, Height = VIEW,
                CornerRadius = new CornerRadius(VIEW / 2),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 166, 247)),
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false
            };
            var stack = new Grid();
            stack.Children.Add(frame);
            stack.Children.Add(ring);
            panel.Children.Add(stack);

            // 缩放滑条
            var slider = new Slider
            {
                Minimum = 0.6, Maximum = 3, Value = 1,
                Margin = new Thickness(24, 16, 24, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 166, 247))
            };
            slider.ValueChanged += (_, e) => scale.ScaleX = scale.ScaleY = e.NewValue;
            panel.Children.Add(slider);

            // 按钮
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cancel = Btn("取消", false);
            var ok = Btn("用这张", true);
            cancel.Click += (_, __) => { DialogResult = false; Close(); };
            ok.Click += (_, __) => Save();
            bar.Children.Add(cancel);
            bar.Children.Add(ok);
            panel.Children.Add(bar);

            root.Child = panel;
            Content = root;

            // 拖动
            frame.MouseLeftButtonDown += (s, e) =>
            {
                dragging = true;
                dragStart = e.GetPosition(frame);
                frame.CaptureMouse();
            };
            frame.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var p = e.GetPosition(frame);
                shift.X += p.X - dragStart.X;
                shift.Y += p.Y - dragStart.Y;
                dragStart = p;
            };
            frame.MouseLeftButtonUp += (s, e) => { dragging = false; frame.ReleaseMouseCapture(); };
            frame.MouseWheel += (s, e) =>
            {
                double v = scale.ScaleX + (e.Delta > 0 ? 0.08 : -0.08);
                v = Math.Max(0.6, Math.Min(3, v));
                scale.ScaleX = scale.ScaleY = v;
                slider.Value = v;
            };
        }

        static BitmapImage Load(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        void Save()
        {
            try
            {
                // 把圆形框里的内容渲染成 512×512
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    512, 512, 96, 96, PixelFormats.Pbgra32);
                frame.UpdateLayout();
                rtb.Render(frame);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                var dir = Path.Combine(Program.Root, "头像");
                Directory.CreateDirectory(dir);
                ResultPath = Path.Combine(dir, "avatar.png");
                using var fs = File.Create(ResultPath);
                enc.Save(fs);
                DialogResult = true;
                Close();
            }
            catch (Exception e)
            {
                MessageBox.Show("保存失败：" + e.Message);
            }
        }

        Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Content = text, FontSize = 13,
                Padding = new Thickness(18, 8, 18, 8),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = primary
                    ? new SolidColorBrush(Color.FromRgb(203, 166, 247))
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Foreground = primary
                    ? new SolidColorBrush(Color.FromRgb(30, 20, 50))
                    : new SolidColorBrush(Color.FromRgb(190, 180, 205))
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

    /// <summary>让弹窗能拿到机器人根目录</summary>
    public static class Program
    {
        public static string Root = AppDomain.CurrentDomain.BaseDirectory;
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
