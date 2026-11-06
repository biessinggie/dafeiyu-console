using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;

namespace DafeiyuConsole
{
    public partial class MainWindow : Window
    {
        static readonly string Root = FindRoot();
        // ── 给设置窗口用的公开入口 ──
        public static string RootDir => Root;
        public static string PythonPath => Python;
        public static int ApiPort => _ApiPort;
        public static string PetExe => CfgString("pet_exe", "BigBlueFish.exe");
        public static string NapcatBat
        {
            get
            {
                var cfg = CfgString("napcat_bat");
                if (!string.IsNullOrEmpty(cfg)) return cfg;
                var p = Path.Combine(Root, "..", "NapCat", "1-启动NapCat.bat");
                return Path.GetFullPath(p);
            }
        }
        static bool quietMode;
        public static bool Quiet
        {
            get => quietMode;
            set { quietMode = value; }
        }
        public static Action AvatarRefresh;
        public static Action BackgroundRefresh;
        public static void RequestAvatarRefresh() => AvatarRefresh?.Invoke();
        public static void RequestBackgroundRefresh() => BackgroundRefresh?.Invoke();

        // ── 用户环境配置（console.json，全都可以不填） ──
        public static readonly string CfgPath = FindCfgPath();
        static JsonElement? cfgEl;

        static string FindCfgPath()
        {
            var env = Environment.GetEnvironmentVariable("DAFEIYU_CONSOLE_CFG");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
            var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console.json");
            return File.Exists(p) ? p : "";
        }

        public static string CfgString(string key, string def = "")
        {
            try
            {
                if (cfgEl == null)
                {
                    if (string.IsNullOrEmpty(CfgPath) || !File.Exists(CfgPath)) { cfgEl = default; }
                    else cfgEl = JsonDocument.Parse(File.ReadAllText(CfgPath)).RootElement.Clone();
                }
                if (cfgEl is JsonElement el && el.ValueKind == JsonValueKind.Object
                    && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch { }
            return def;
        }

        public static int CfgPort(string key, int def)
        {
            try
            {
                if (cfgEl == null) CfgString("_", "");   // 触发加载
                if (cfgEl is JsonElement el && el.ValueKind == JsonValueKind.Object
                    && el.TryGetProperty("ports", out var ports)
                    && ports.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                    return v.GetInt32();
            }
            catch { }
            return def;
        }

        static readonly string ServerScript = FindServerScript();
        static readonly string Python = FindPython();
        static readonly int _ApiPort = CfgPort("console", 8771);
        static readonly string Api = "http://127.0.0.1:" + _ApiPort;

        static bool ServerIsExe;
        public static bool PythonFound;

        static string FindServerScript()
        {
            // 打包版：console_server.exe，不需要装 Python
            var exeCands = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console_server.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend", "console_server.exe"),
                Path.Combine(Root, "console_server.exe"),
                Path.Combine(Root, "backend", "console_server.exe")
            };
            foreach (var e in exeCands)
                if (File.Exists(e)) { ServerIsExe = true; return e; }

            var cands = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console_server.py"),
                Path.Combine(Root, "console_server.py"),
                Path.Combine(Root, "backend", "console_server.py"),
                Path.Combine(Root, "console", "console_server.py")
            };
            foreach (var c in cands) if (File.Exists(c)) return c;
            return cands[0];
        }

        static string FindPython()
        {
            // 1. 配置里写的
            var cfg = CfgString("python");
            if (!string.IsNullOrEmpty(cfg) && File.Exists(cfg)) { PythonFound = true; return cfg; }

            // 2. 注册表（官方安装包都会写这里）
            foreach (var root in new[]
            {
                @"SOFTWARE\\Python\\PythonCore",
                @"SOFTWARE\\WOW6432Node\\Python\\PythonCore"
            })
            {
                foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                {
                    try
                    {
                        using var k = hive.OpenSubKey(root);
                        if (k == null) continue;
                        foreach (var ver in k.GetSubKeyNames().OrderByDescending(x => x))
                        {
                            using var ip = k.OpenSubKey(ver + @"\\InstallPath");
                            var dir = ip?.GetValue("") as string ?? ip?.GetValue("ExecutablePath") as string;
                            if (!string.IsNullOrEmpty(dir))
                            {
                                var d = dir.EndsWith(".exe") ? Path.GetDirectoryName(dir) : dir;
                                foreach (var n in new[] { "pythonw.exe", "python.exe" })
                                {
                                    var exe = Path.Combine(d, n);
                                    if (File.Exists(exe)) { PythonFound = true; return exe; }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // 3. py 启动器（微软商店版也有）
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("py", "-3 -c \"import sys;print(sys.executable)\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var outp = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(3000);
                    if (!string.IsNullOrEmpty(outp) && File.Exists(outp))
                    {
                        var w = Path.Combine(Path.GetDirectoryName(outp), "pythonw.exe");
                        PythonFound = true;
                        return File.Exists(w) ? w : outp;
                    }
                }
            }
            catch { }

            // 4. 常见安装位置
            var names = new[] { "pythonw.exe", "python.exe" };
            var bases = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Programs\\Python",
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "C:/"
            };
            foreach (var b in bases)
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(b, "Python3*").OrderByDescending(x => x))
                        foreach (var n in names)
                        {
                            var exe = Path.Combine(d, n);
                            if (File.Exists(exe)) { PythonFound = true; return exe; }
                        }
                }
                catch { }
            }

            // 5. PATH
            foreach (var n in names)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("where", n)
                    {
                        RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc == null) continue;
                    var line = proc.StandardOutput.ReadToEnd().Split('\n').FirstOrDefault();
                    proc.WaitForExit(2000);
                    if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                    { PythonFound = true; return line.Trim(); }
                }
                catch { }
            }

            // 6. 实在找不到
            return "pythonw.exe";
        }


        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        readonly DispatcherTimer poll = new DispatcherTimer();
        readonly DispatcherTimer anim = new DispatcherTimer();
        readonly Random rnd = new Random();
        readonly Dictionary<string, string> modeName = new Dictionary<string, string>
        {
            ["都回"] = "都回", ["只私聊"] = "只私聊", ["只群聊"] = "只群聊"
        };

        string tab = "log";
        string filter = "all";
        bool? wasOnline = null;
        string lastLogText = null;
        string lastAvatar = "";
        string manualAvatar = null;
        double mood = 0;
        readonly List<(string key, string text, string icon, string hint, Action act)> keys;

        public MainWindow()
        {
            InitializeComponent();
            keys = new List<(string, string, string, string, Action)>
            {
                ("§运行", "", "", "", null),
                ("start", "启动全部", "🚀", "F1", () => { if (NeedPython()) _ = StartFlowAsync(); }),
                ("qr", "刷新二维码", "📷", "F2", () => Action("qr")),
                ("restart", "重启机器人", "🔄", "F3", () => { if (NeedPython()) Action("restart"); }),
                ("diag", "检测连接", "🩺", "F4", () => Action("diag")),
                ("mode", "切换模式", "🔀", "F5", () => Action("mode")),

                ("§数据", "", "", "", null),
                ("backup", "备份记忆", "💾", "F6", () => Action("backup")),
                ("restore", "还原最近", "⏪", "F7", () => Action("restore_latest")),
                ("export", "导出资料", "📦", "F8", () => Action("export")),
                ("open_export", "导出目录", "📂", "", () => Action("open_export")),
                ("open_backup", "备份目录", "🗂", "", () => Action("open_backup")),

                ("§程序", "", "", "", null),
                ("memory", "记忆", "🧠", "F12", OpenMemories),
                ("stickers", "表情包", "😊", "F9", () => Action("stickers")),
                ("folder", "程序目录", "📁", "F10", () => Action("folder")),
                ("settings", "设置", "⚙️", "F11", OpenSettings),

                ("§暂停", "", "", "", null),
                ("pause30", "休息 30 分钟", "🌙", "", () => Action("pause30")),
                ("pause120", "休息 2 小时", "🌙", "", () => Action("pause120")),
                ("pause_morning", "到明早 8 点", "🌅", "", () => Action("pause_morning")),
                ("resume", "叫她回来", "☀️", "", () => Action("resume")),

                ("§危险", "", "", "", null),
                ("hardstop", "全部关闭", "⏹", "", HardStop),
            };
            LoadAssets();
            BuildKeys();
            BuildStatusRows();
            Task.Run(() => StartServerIfNeeded());

            poll.Interval = TimeSpan.FromSeconds(5);
            poll.Tick += async (_, __) => await RefreshAsync();
            poll.Start();
            _ = RefreshAsync();




            SayBox.TextChanged += (_, __) =>
                SayHint.Visibility = string.IsNullOrEmpty(SayBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            StateChanged += (_, __) =>
            {
                RootBorder.CornerRadius = WindowState == WindowState.Maximized
                    ? new CornerRadius(0) : new CornerRadius(14);
            };

            PreviewKeyDown += (_, e) => Hotkey(e);

            Loaded += (_, __) =>
            {
                FadeIn(); Adapt();

                var b = AppState.Load();
                if (b != null && b.Width > 400)
                {
                    Left = b.Left; Top = b.Top;
                    Width = b.Width; Height = b.Height;
                }

                AvatarRefresh = () =>
                {
                    avatarCache.Clear();
                    lastAvatar = "";
                    manualAvatar = "avatar.png";
                    UpdateAvatar(mood, wasOnline ?? false);
                };
                BackgroundRefresh = ReloadBackground;

                FirstRunHint();
                SetupTray();
            };
        }

        void LoadAssets()
        {
            foreach (var dir in AssetDirs())
            {
                var bg = Path.Combine(dir, "bg.png");
                if (File.Exists(bg))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(bg);
                        bmp.EndInit();
                        bmp.Freeze();
                        BgImage.Source = bmp;
                    }
                    catch { }
                }

                var ico = Path.Combine(dir, "app.ico");
                if (File.Exists(ico))
                {
                    try { Icon = BitmapFrame.Create(new Uri(ico)); } catch { }
                }
            }
        }

        // ── 无边框窗口最大化时别盖住任务栏 ──
        const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO
        {
            public POINT ptReserved, ptMaxSize, ptMaxPosition;
            public POINT ptMinTrackSize, ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO mi);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook(MaxFixHook);
        }

        IntPtr MaxFixHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
            try
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                var mon = MonitorFromWindow(hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/);
                if (GetMonitorInfo(mon, ref mi))
                {
                    var mm = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    mm.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
                    mm.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
                    mm.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                    mm.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                    mm.ptMaxTrackSize.X = mm.ptMaxSize.X;
                    mm.ptMaxTrackSize.Y = mm.ptMaxSize.Y;
                    Marshal.StructureToPtr(mm, lParam, true);
                    handled = true;
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        static string FindRoot()
        {
            // 0. console.json 里写的机器人目录
            var cfgRoot = CfgString("bot_root");
            if (!string.IsNullOrWhiteSpace(cfgRoot) && Directory.Exists(cfgRoot)) return cfgRoot;

            // 1. 环境变量指定
            var env = Environment.GetEnvironmentVariable("DAFEIYU_ROOT");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

            // 2. 从 exe 目录往上找 config.json（机器人目录）
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8 && d != null; i++)
            {
                if (File.Exists(Path.Combine(d.FullName, "config.json"))) return d.FullName;
                d = d.Parent;
            }

            // 3. 找不到就用 exe 自己所在目录（不再用"当前工作目录"，那个不可控）
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>按顺序找资源目录：exe 目录 → 机器人根 → 根/console</summary>
        static IEnumerable<string> AssetDirs()
        {
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            yield return Path.Combine(Root, "Assets");
            yield return Path.Combine(Root, "console", "Assets");
        }

        /// <summary>按顺序找头像目录</summary>
        static IEnumerable<string> AvatarDirs()
        {
            yield return Path.Combine(Root, "头像");
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "头像");
            yield return Path.Combine(Root, "console", "头像");
        }

        static readonly string[] avatarViews = { "圆-正面.png", "圆-左45.png", "圆-右45.png", "圆-左侧.png", "圆-右前45.png" };
        int avatarIdx = -1;

        void Avatar_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        {
            avatarIdx = (avatarIdx + 1) % avatarViews.Length;
            manualAvatar = avatarViews[avatarIdx];
            lastAvatar = "";
            UpdateAvatar(mood, wasOnline ?? false);
        }

        TrayIcon tray;

        void SetupTray()
        {
            try
            {
                var ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                if (!File.Exists(ico)) ico = Path.Combine(Root, "console", "Assets", "app.ico");
                tray = new TrayIcon(this, ico, "大肥鱼 · 控制台",
                    () => { Show(); WindowState = WindowState.Normal; Activate(); },
                    () => Action("qr"),
                    OpenSettings,
                    () => { tray?.Dispose(); tray = null; Close(); },
                    HotkeyToggle);
            }
            catch { }
        }

        async Task StartFlowAsync()
        {
            var r = await Post("/api/action", new { name = "start" });
            if (r != null && r.Value.TryGetProperty("msg", out var m)) Toast(m.GetString());

            string last = "";
            for (int i = 0; i < 140; i++)
            {
                await Task.Delay(1200);
                var st = await Get("/api/startup");
                if (st == null) continue;
                string state = st.Value.GetProperty("state").GetString();
                string msg = st.Value.GetProperty("msg").GetString();
                if (!string.IsNullOrEmpty(msg) && msg != last)
                {
                    last = msg;
                    Toast(msg);
                }
                if (state == "done" || state == "failed") break;
            }
            await RefreshAsync();
        }

        void HotkeyToggle()
        {
            if (IsVisible && IsActive) Hide();
            else
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
            }
        }

        // ── 主题 ──
        void SnapToEdges()
        {
            var wa = SystemParameters.WorkArea;
            const double gap = 22;
            if (Math.Abs(Left - wa.Left) < gap) Left = wa.Left;
            else if (Math.Abs(Left + Width - wa.Right) < gap) Left = wa.Right - Width;
            if (Math.Abs(Top - wa.Top) < gap) Top = wa.Top;
            else if (Math.Abs(Top + Height - wa.Bottom) < gap) Top = wa.Bottom - Height;
        }

        void Hotkey(KeyEventArgs e)
        {
            var map = new Dictionary<Key, Action>
            {
                [Key.F1] = () => _ = StartFlowAsync(),
                [Key.F2] = () => Action("qr"),
                [Key.F3] = () => Action("restart"),
                [Key.F4] = () => Action("diag"),
                [Key.F5] = () => Action("mode"),
                [Key.F6] = () => Action("backup"),
                [Key.F7] = () => Action("restore_latest"),
                [Key.F8] = () => Action("export"),
                [Key.F9] = () => Action("stickers"),
                [Key.F10] = () => Action("folder"),
                [Key.F11] = OpenSettings,
                [Key.F12] = OpenMemories,
            };
            if (map.TryGetValue(e.Key, out var fn)) { e.Handled = true; fn(); }
        }

        void Adapt()
        {
            double w = ActualWidth, h = ActualHeight;
            bool narrow = w < 1180;
            bool shortWin = h < 780;
            LeftCol.Width = new GridLength(narrow ? 258 : 296);
            RightCol.Width = new GridLength(narrow ? 214 : 248);
            double av = narrow ? 168 : 196;
            AvatarBox.Width = av;
            AvatarBox.Height = av;
            HeroCard.Padding = narrow ? new Thickness(14, 16, 14, 14) : new Thickness(20, 22, 20, 18);
            Credit.Visibility = shortWin ? Visibility.Collapsed : Visibility.Visible;
            MiniCard.Visibility = shortWin ? Visibility.Collapsed : Visibility.Visible;
            LogText.FontSize = w < 1100 ? 11 : 12;
        }

        void FadeIn()
        {
            Opacity = 0;
            var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            BeginAnimation(OpacityProperty, a);
        }

        static bool PortAlive(int port)
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                var task = s.ConnectAsync("127.0.0.1", port);
                if (!task.Wait(220))
                {
                    // 超时后必须把这个任务的异常“消费”掉，
                    // 否则会在后台抛 SocketException 995 并写进崩溃日志
                    _ = task.ContinueWith(t => { _ = t.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted);
                    return false;
                }
                return s.Connected;
            }
            catch { return false; }
            finally { try { s.Dispose(); } catch { } }
        }

        void StartServerIfNeeded()
        {
            if (PortAlive(_ApiPort)) return;
            try
            {
                if (!string.IsNullOrEmpty(CfgPath))
                Environment.SetEnvironmentVariable("DAFEIYU_CONSOLE_CFG", CfgPath);
            var psi = new ProcessStartInfo(
                ServerIsExe ? ServerScript : Python,
                ServerIsExe ? "" : "\"" + ServerScript + "\"")
                {
                    WorkingDirectory = Root,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                for (int i = 0; i < 40 && !PortAlive(_ApiPort); i++) System.Threading.Thread.Sleep(150);
            }
            catch (Exception ex)
            {
                try
                {
                    var log = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "DafeiyuConsole", "crash.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(log));
                    File.AppendAllText(log,
                        "[" + DateTime.Now.ToString("s") + "] 启动后端失败: " + ex.Message + "\r\n");
                }
                catch { }
            }
        }

        async Task<JsonElement?> Get(string path)
        {
            try
            {
                var s = await http.GetStringAsync(Api + path);
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.Clone();
            }
            catch { return null; }
        }

        async Task<JsonElement?> Post(string path, object body)
        {
            try
            {
                var json = JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var s = await (await http.PostAsync(Api + path, content)).Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.Clone();
            }
            catch { return null; }
        }

        async void Action(string name)
        {
            var r = await Post("/api/action", new { name });
            if (r != null && r.Value.TryGetProperty("msg", out var m)) Toast(m.GetString());
            await RefreshAsync();
        }

        // ── 花瓣 ──
        readonly Dictionary<string, (Button head, StackPanel body, TextBlock chevron)> groups
            = new Dictionary<string, (Button, StackPanel, TextBlock)>();

        void BuildKeys()
        {
            StackPanel current = null;
            string currentName = null;

            foreach (var k in keys)
            {
                if (k.key.StartsWith("§"))
                {
                    currentName = k.key.Substring(1);
                    current = new StackPanel { Visibility = Visibility.Collapsed };

                    var head = new Button { Style = (Style)FindResource("KeyButton") };
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var chev = new TextBlock
                    {
                        Text = "▸", FontSize = 10,
                        Foreground = (SolidColorBrush)FindResource("OverlayBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    Grid.SetColumn(chev, 0);
                    g.Children.Add(chev);

                    var title = new TextBlock
                    {
                        Text = currentName, FontSize = 11.5,
                        Foreground = new SolidColorBrush(Color.FromArgb(210, 214, 205, 240)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(title, 1);
                    g.Children.Add(title);

                    var count = new TextBlock
                    {
                        Text = "", FontSize = 10,
                        Foreground = (SolidColorBrush)FindResource("OverlayBrush"),
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
                    };
                    Grid.SetColumn(count, 2);
                    g.Children.Add(count);

                    head.Content = g;
                    var body = current;
                    var groupName = currentName;   // 关键：拷一份，否则闭包全指向最后一组
                    head.Click += (_, __) => ToggleGroup(groupName);
                    KeyList.Children.Add(head);
                    KeyList.Children.Add(body);
                    groups[currentName] = (head, body, chev);
                    continue;
                }

                current?.Children.Add(MakeKeyButton(k));
            }

            // 计数字；只有一项的组不做父目录，直接平铺
            var singles = new List<string>();
            foreach (var kv in groups)
            {
                var g = (Grid)kv.Value.head.Content;
                var count = (TextBlock)g.Children[2];
                count.Text = kv.Value.body.Children.Count.ToString();
                if (kv.Value.body.Children.Count <= 1) singles.Add(kv.Key);
            }
            foreach (var name in singles)
            {
                if (!groups.TryGetValue(name, out var g)) continue;
                KeyList.Children.Remove(g.head);      // 去掉父级标题
                g.body.Visibility = Visibility.Visible; // 子项直接露出
                groups.Remove(name);
            }

            // 默认展开第一个，其余收起
            foreach (var name in groups.Keys)
                SetGroup(name, name == "运行");
        }

        Button MakeKeyButton((string key, string text, string icon, string hint, Action act) k)
        {
            var btn = new Button { Style = (Style)FindResource("KeyButton") };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = k.icon, FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var text = new TextBlock
            {
                Text = k.text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(4, 0, 0, 0)
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (!string.IsNullOrEmpty(k.hint))
            {
                var hint = new Border
                {
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 6, 2),
                    Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = k.hint, FontSize = 10,
                        Foreground = (Brush)FindResource("OverlayBrush"),
                        FontFamily = (FontFamily)FindResource("MonoFont")
                    }
                };
                Grid.SetColumn(hint, 2);
                grid.Children.Add(hint);
            }

            btn.Content = grid;
            btn.Click += (_, __) => k.act?.Invoke();
            return btn;
        }

        bool allOpen;

        void ExpandAll_Click(object s, RoutedEventArgs e)
        {
            allOpen = !allOpen;
            ExpandAll(allOpen);
            ExpandAllBtn.Content = allOpen ? "全部收起" : "全部展开";
        }

        void ToggleGroup(string name)
        {
            if (!groups.TryGetValue(name, out var g)) return;
            SetGroup(name, g.body.Visibility != Visibility.Visible);
        }

        void SetGroup(string name, bool open)
        {
            if (!groups.TryGetValue(name, out var g)) return;
            g.body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            g.chevron.Text = open ? "▾" : "▸";
        }

        void ExpandAll(bool open)
        {
            foreach (var name in groups.Keys.ToList()) SetGroup(name, open);
        }

        readonly Dictionary<string, (Ellipse dot, TextBlock label)> statusRows =
            new Dictionary<string, (Ellipse, TextBlock)>();

        void BuildStatusRows()
        {
            var names = new (string key, string name)[]
            {
                ("napcat", "NapCat"), ("conn", "3001"), ("bot", "机器人"),
                ("watch", "看门狗"), ("pet", "桌宠")
            };
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            StatusList.Children.Add(strip);

            foreach (var (key, name) in names)
            {
                var cell = new StackPanel
                {
                    Width = 44, Margin = new Thickness(0, 2, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                var dot = new Ellipse
                {
                    Width = 7, Height = 7,
                    Fill = (Brush)FindResource("OverlayBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3)
                };
                var label = new TextBlock
                {
                    Text = name, FontSize = 10,
                    Foreground = (Brush)FindResource("OverlayBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                cell.Children.Add(dot);
                cell.Children.Add(label);
                cell.ToolTip = name;
                strip.Children.Add(cell);
                statusRows[key] = (dot, label);
            }
        }

        // ── 刷新 ──
        bool refreshing;

        async Task RefreshAsync()
        {
            if (refreshing) return;
            refreshing = true;
            try { await RefreshInnerAsync(); }
            finally { refreshing = false; }
        }

        async Task RefreshInnerAsync()
        {
            var st = await Get("/api/state");
            if (st == null)
            {
                OnlineText.Text = "后端未启动";
                return;
            }
            var e = st.Value;
            bool online = e.GetProperty("online").GetBoolean();
            mood = e.GetProperty("mood").GetDouble();

            // 在线状态要分三档：真活跃 / 久无活动 / 多半掉线
            double idleMin = e.TryGetProperty("idle_min", out var im) && im.ValueKind == JsonValueKind.Number
                ? im.GetDouble() : -1;

            string onlineText;
            string dotBrush;
            if (!online)
            {
                onlineText = "离线";
                dotBrush = "RedBrush";
            }
            else if (idleMin < 0 || idleMin <= 8)
            {
                onlineText = "在线";
                dotBrush = "GreenBrush";
            }
            else if (idleMin <= 30)
            {
                onlineText = string.Format("空闲 {0:0} 分", idleMin);
                dotBrush = "YellowBrush";
            }
            else
            {
                onlineText = "可能掉线";
                dotBrush = "RedBrush";
            }

            SetText(OnlineText, onlineText);
            OnlineDot.Fill = (Brush)FindResource(dotBrush);
            StatusDot.Fill = (Brush)FindResource(online ? "GreenBrush" : "OverlayBrush");
            OnlinePill.ToolTip = idleMin >= 0
                ? string.Format("最近一次收发消息：{0:0} 分钟前", idleMin)
                : "还没有消息记录";
            SetText(ModeChip, e.GetProperty("mode").GetString());

            string words = e.GetProperty("words").GetString();
            SetText(WordsText, string.IsNullOrWhiteSpace(words) ? "（她还没说话）" : "“" + words.Replace("\n", " ") + "”");
            string doing = e.GetProperty("doing").GetString();
            SetText(DoingText, string.IsNullOrWhiteSpace(doing) ? "" : "正在 " + doing);

            SToday.Text = "¥" + e.GetProperty("today").GetDouble().ToString("0.000");
            STotal.Text = "¥" + e.GetProperty("total").GetDouble().ToString("0.00");
            STurns.Text = e.GetProperty("turns").GetInt32().ToString();

            MTurns.Text = e.GetProperty("turns").GetInt32().ToString();
            MMsgs.Text = e.GetProperty("msgs").GetInt32().ToString();
            MSaved.Text = e.GetProperty("saved").GetInt32().ToString();
            int turns = e.GetProperty("turns").GetInt32();
            TurnsChip.Text = "今日 " + turns + " 轮";
            double pausedUntil = 0;
            if (e.TryGetProperty("paused_until", out var pu) && pu.ValueKind == JsonValueKind.Number)
                pausedUntil = pu.GetDouble();
            if (pausedUntil > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                var left = TimeSpan.FromSeconds(pausedUntil - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                TitleActivity.Text = string.Format("💤 暂停中 · 还有 {0:0} 小时 {1:0} 分",
                    Math.Floor(left.TotalHours), left.Minutes);
            }
            else
            {
                TitleActivity.Text = string.Format(
                    "今日 {0} 轮　·　花费 ¥{1:0.000}　·　收藏 {2}",
                    turns, e.GetProperty("today").GetDouble(), e.GetProperty("saved").GetInt32());
            }

            double ratio = Math.Max(0.04, Math.Min(1.0, (mood + 1) / 2));
            MoodFill.Width = Math.Max(6, (248 - 72) * ratio);
            MoodFace.Text = mood > 0.15 ? "😊" : (mood > -0.15 ? "😐" : "🥺");
            UpdateAvatar(mood, online);

            var procs = e.GetProperty("procs");
            bool petInstalled = !procs.TryGetProperty("pet_installed", out var pi) || pi.GetBoolean();
            foreach (var kv in statusRows)
            {
                bool ok = procs.TryGetProperty(kv.Key, out var p) && p.GetBoolean();
                kv.Value.dot.Fill = (Brush)FindResource(ok ? "GreenBrush" : "OverlayBrush");
                kv.Value.label.Foreground = (Brush)FindResource(ok ? "SubtextBrush" : "OverlayBrush");

                // 桌宠没装就别显示成"掉线"，直接写"未装"
                if (kv.Key == "pet" && !petInstalled)
                {
                    kv.Value.label.Text = "未装";
                    kv.Value.label.ToolTip = "设置里可以把桌宠换成你自己的 exe，或者留空不用";
                }
                else if (kv.Key == "pet")
                {
                    kv.Value.label.Text = "桌宠";
                }
            }

            if (e.TryGetProperty("days", out var days)) DrawSpark(days);

            if (wasOnline.HasValue && wasOnline.Value != online)
                Toast(online ? "她回来了 ♡" : "她掉线了，按 F2 扫码");
            wasOnline = online;

            await LoadTabAsync();
        }

        static readonly Dictionary<string, BitmapImage> avatarCache = new Dictionary<string, BitmapImage>();

        static void SetText(System.Windows.Controls.TextBlock tb, string value)
        {
            if (tb.Text != value) tb.Text = value;
        }

        void UpdateAvatar(double m, bool online)
        {
            string file = manualAvatar
                ?? (!online ? "圆-正面.png"
                : m > 0.35 ? "圆-右45.png"
                : m > 0.10 ? "圆-左45.png"
                : m < -0.20 ? "圆-左侧.png"
                : "圆-正面.png");
            // 用户自己放的头像优先：头像/avatar.png 或 头像/头像.png
            foreach (var dir in AvatarDirs())
            {
                foreach (var custom in new[] { "avatar.png", "头像.png", "avatar.jpg", "头像.jpg" })
                {
                    var p2 = Path.Combine(dir, custom);
                    if (File.Exists(p2)) { file = custom; break; }
                }
                if (file == "avatar.png" || file == "头像.png" || file == "avatar.jpg" || file == "头像.jpg")
                    break;
            }

            if (file == lastAvatar) return;

            string path = null;
            foreach (var dir in AvatarDirs())
            {
                var cand = Path.Combine(dir, file);
                if (File.Exists(cand)) { path = cand; break; }
            }
            if (path == null) return;
            try
            {
                if (!avatarCache.TryGetValue(file, out var bmp))
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 384;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    avatarCache[file] = bmp;
                }
                RenderOptions.SetBitmapScalingMode(AvatarBrush, BitmapScalingMode.HighQuality);
                AvatarBrush.ImageSource = bmp;
                AvatarBrush.Opacity = online ? 1.0 : 0.55;
                lastAvatar = file;
            }
            catch { }
        }


        void DrawSpark(JsonElement days)
        {
            SparkLine.Points = new PointCollection();
            var vals = days.EnumerateArray().Select(d => d.GetProperty("yuan").GetDouble()).ToList();
            if (vals.Count < 2) return;
            double max = Math.Max(vals.Max(), 0.001);
            for (int i = 0; i < vals.Count; i++)
                SparkLine.Points.Add(new Point(i * 14, 18 - vals[i] / max * 16));
        }

        // ── 面板 ──
        async Task LoadTabAsync()
        {
            if (tab == "log" || tab == "chat")
            {
                ChartCanvas.Visibility = Visibility.Collapsed;
                LogScroll.Visibility = Visibility.Visible;
                string path = tab == "log" ? "/api/logs?filter=" + filter : "/api/chat";
                var d = await Get(path);
                if (d == null) return;
                var sb = new StringBuilder();
                if (tab == "log")
                {
                    foreach (var line in d.Value.GetProperty("lines").EnumerateArray())
                        sb.AppendLine(line.GetString());
                }
                else
                {
                    foreach (var it in d.Value.GetProperty("items").EnumerateArray())
                    {
                        bool her = it.GetProperty("role").GetString() == "assistant";
                        sb.AppendLine((her ? "她　" : "群　") + it.GetProperty("text").GetString());
                    }
                    if (sb.Length == 0) sb.Append("还没有对话");
                }
                var text = sb.ToString();
                if (text != lastLogText)
                {
                    lastLogText = text;
                    if (tab == "log") RenderLogColored(text);
                    else LogText.Text = text;
                    LogScroll.ScrollToEnd();
                }
            }
            else
            {
                LogScroll.Visibility = Visibility.Collapsed;
                ChartCanvas.Visibility = Visibility.Visible;
                var st = await Get("/api/state");
                if (st != null && st.Value.TryGetProperty("days", out var days))
                    DrawChart(days);
            }
        }

        static readonly Color CGroup = (Color)ColorConverter.ConvertFromString("#8BD5FF");
        static readonly Color CPriv = (Color)ColorConverter.ConvertFromString("#F5C2E7");
        static readonly Color CErr = (Color)ColorConverter.ConvertFromString("#F38BA8");
        static readonly Color COk = (Color)ColorConverter.ConvertFromString("#A6E3A1");
        static readonly Color CWarn = (Color)ColorConverter.ConvertFromString("#F9E2AF");
        static readonly Color CNormal = (Color)ColorConverter.ConvertFromString("#D6C9F2");

        Color ColorOf(string line)
        {
            if (line.Contains("连接断开") || line.Contains("错误") || line.Contains("失败")
                || line.Contains("WinError"))
                return CErr;
            if (line.Contains("[私聊]") || line.Contains("私聊"))
                return CPriv;
            if (line.Contains("[群]") || line.Contains("群消息") || line.Contains("gid="))
                return CGroup;
            if (line.Contains("已连上") || line.Contains("登录") || line.Contains("成功")
                || line.Contains("回复"))
                return COk;
            if (line.Contains("说") || line.Contains("tieba") || line.Contains("收藏"))
                return CWarn;
            if (line.TrimStart().StartsWith("⋯"))
                return (Color)ColorConverter.ConvertFromString("#6C7086");
            return CNormal;
        }

        void RenderLogColored(string text)
        {
            var lines = text.Split('\n');
            if (lines.Length > 140) lines = lines.Skip(lines.Length - 140).ToArray();
            LogText.Inlines.Clear();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                LogText.Inlines.Add(new Run(line)
                {
                    Foreground = new SolidColorBrush(ColorOf(line))
                });
                LogText.Inlines.Add(new Run("\n"));
            }
        }

        void DrawChart(JsonElement days)
        {
            ChartCanvas.Children.Clear();
            var list = days.EnumerateArray().ToList();
            if (list.Count == 0) return;
            double w = ChartCanvas.ActualWidth > 10 ? ChartCanvas.ActualWidth : 640;
            double h = ChartCanvas.ActualHeight > 10 ? ChartCanvas.ActualHeight : 300;
            double max = Math.Max(list.Max(d => d.GetProperty("yuan").GetDouble()), 0.01);
            double slot = w / list.Count;

            var total = list.Sum(d => d.GetProperty("yuan").GetDouble());
            var title = new TextBlock
            {
                Text = "最近 7 天 · 合计 ¥" + total.ToString("0.00"),
                Foreground = (Brush)FindResource("OverlayBrush"), FontSize = 11
            };
            Canvas.SetLeft(title, 4);
            Canvas.SetTop(title, 0);
            ChartCanvas.Children.Add(title);

            for (int i = 0; i < list.Count; i++)
            {
                double v = list[i].GetProperty("yuan").GetDouble();
                string date = list[i].GetProperty("date").GetString();
                double bh = Math.Max(4, v / max * (h - 70));
                double bw = Math.Min(38, slot * 0.5);
                var bar = new Border
                {
                    Width = bw, Height = bh, CornerRadius = new CornerRadius(8, 8, 4, 4),
                    Background = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#F5C2E7"),
                        (Color)ColorConverter.ConvertFromString("#CBA6F7"), 90),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 18, ShadowDepth = 0,
                        Color = (Color)ColorConverter.ConvertFromString("#CBA6F7"),
                        Opacity = 0.5
                    }
                };
                Canvas.SetLeft(bar, i * slot + (slot - bw) / 2);
                Canvas.SetTop(bar, h - 34 - bh);
                ChartCanvas.Children.Add(bar);

                var val = new TextBlock
                {
                    Text = v > 0 ? v.ToString("0.00") : "",
                    FontSize = 10, Foreground = (Brush)FindResource("SubtextBrush")
                };
                val.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(val, i * slot + (slot - val.DesiredSize.Width) / 2);
                Canvas.SetTop(val, h - 34 - bh - 18);
                ChartCanvas.Children.Add(val);

                var lbl = new TextBlock
                {
                    Text = date, FontSize = 10,
                    Foreground = (Brush)FindResource("OverlayBrush")
                };
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, i * slot + (slot - lbl.DesiredSize.Width) / 2);
                Canvas.SetTop(lbl, h - 20);
                ChartCanvas.Children.Add(lbl);
            }
        }

        // ── 交互 ──
        void SetTab(string t)
        {
            tab = t;
            TabLog.Foreground = (Brush)FindResource(t == "log" ? "PinkBrush" : "OverlayBrush");
            TabChat.Foreground = (Brush)FindResource(t == "chat" ? "PinkBrush" : "OverlayBrush");
            TabChart.Foreground = (Brush)FindResource(t == "chart" ? "PinkBrush" : "OverlayBrush");
            FilterBar.Visibility = t == "log" ? Visibility.Visible : Visibility.Collapsed;
            _ = LoadTabAsync();
        }

        void TabLog_Click(object s, RoutedEventArgs e) => SetTab("log");
        void TabChat_Click(object s, RoutedEventArgs e) => SetTab("chat");
        void TabChart_Click(object s, RoutedEventArgs e) => SetTab("chart");

        void Filter_Click(object sender, RoutedEventArgs e)
        {
            filter = (string)((Button)sender).Tag;
            FAll.Foreground = (Brush)FindResource(filter == "all" ? "TextBrush" : "OverlayBrush");
            FGroup.Foreground = (Brush)FindResource(filter == "group" ? "TextBrush" : "OverlayBrush");
            FPriv.Foreground = (Brush)FindResource(filter == "priv" ? "TextBrush" : "OverlayBrush");
            _ = LoadTabAsync();
        }

        async void Send_Click(object s, RoutedEventArgs e) => await SendAsync();

        async Task SendAsync()
        {
            var text = SayBox.Text.Trim();
            if (text.Length == 0) return;
            SayBox.Text = "";
            await Post("/api/say", new { text });
            Toast("已发给她");
            SetTab("log");
        }

        async void SayBox_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await SendAsync();
        }

        // ── 窗口控制 ──
        void TitleBar_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { ToggleMax(); return; }
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
                SnapToEdges();
            }
        }

        void MinBtn_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void ToggleMax() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void MaxBtn_Click(object s, RoutedEventArgs e) => ToggleMax();
        void CloseBtn_Click(object s, RoutedEventArgs e)
        {
            if (tray != null && AppState.AutostartEnabled())
            {
                Hide();
                return;
            }
            Close();
        }
        void Quit_Click(object s, RoutedEventArgs e) => Close();

        /// <summary>重新读一遍背景图（换了文件之后调用）</summary>
        void ReloadBackground()
        {
            foreach (var dir in AssetDirs())
            {
                var path = Path.Combine(dir, "bg.png");
                if (!File.Exists(path)) continue;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    BgImage.Source = bmp;
                    break;
                }
                catch { }
            }
        }

        void PickAvatar()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选一张头像",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                Program.Root = Root;
                var crop = new CropWindow(dlg.FileName) { Owner = this };
                if (crop.ShowDialog() != true || string.IsNullOrEmpty(crop.ResultPath))
                    return;
                avatarCache.Clear();
                lastAvatar = "";
                manualAvatar = "avatar.png";
                UpdateAvatar(mood, wasOnline ?? false);
                Toast("头像换好了");
            }
            catch (Exception e) { Toast("换头像失败：" + e.Message); }
        }

        void PickBackground()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选一张背景图",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                Directory.CreateDirectory(dir);
                var dst = Path.Combine(dir, "bg.png");
                File.Copy(dlg.FileName, dst, true);
                ReloadBackground();
                Toast("背景换好了（会自动铺满窗口）");
            }
            catch (Exception e) { Toast("换背景失败：" + e.Message); }
        }

        void HardStop()
        {
            var dlg = new DangerWindow { Owner = this };
            if (dlg.ShowDialog() == true) Action("stop");
        }


        // ── 弹窗 ──
        void Toast(string msg)
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false, Topmost = true
            };
            var border = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 11, 20, 11),
                Background = new SolidColorBrush(Color.FromArgb(240, 30, 18, 52)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 26, ShadowDepth = 6, Opacity = 0.5 },
                Child = new TextBlock
                {
                    Text = msg, Foreground = (Brush)FindResource("TextBrush"), FontSize = 13
                }
            };
            win.Content = border;
            win.Loaded += (_, __) =>
            {
                var wa = SystemParameters.WorkArea;
                win.Left = wa.Left + (wa.Width - win.ActualWidth) / 2;
                win.Top = wa.Bottom - win.ActualHeight - 48;
            };
            win.Show();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1900) };
            t.Tick += (_, __) => { t.Stop(); win.Close(); };
            t.Start();
        }

        /// <summary>要启动机器人时先确认有 Python；没有就提示用户自己去装（不给链接）</summary>
        bool NeedPython()
        {
            if (PythonFound) return true;
            var box = MessageBox.Show(
                "这台电脑上没有找到 Python。\n\n"
                + "启动机器人需要 Python 3.8 或更高版本，请你自行下载安装。\n"
                + "装好以后，可以在「设置 → Python」里手动指一下路径。\n\n"
                + "要现在打开设置吗？",
                "需要 Python",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (box == MessageBoxResult.Yes) OpenSettings();
            return false;
        }

        /// <summary>第一次用：没配置也没找到机器人的话，直接把设置打开</summary>
        void FirstRunHint()
        {
            bool hasCfg = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console.json"))
                          || !string.IsNullOrEmpty(CfgPath);
            bool hasBot = File.Exists(Path.Combine(Root, "config.json"));
            if (hasCfg && hasBot) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var w = new ConsoleSettingsWindow(Api, CfgPath) { Owner = this };
                w.ShowDialog();
            }));
        }

        void OpenSettings()
        {
            var w = new ConsoleSettingsWindow(Api, CfgPath) { Owner = this };
            w.ShowDialog();
        }

        void OpenMemories()
        {
            new MemoriesWindow(Root) { Owner = this }.ShowDialog();
        }
    }
}
