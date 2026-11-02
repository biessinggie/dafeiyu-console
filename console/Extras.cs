using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace DafeiyuConsole
{
    /// <summary>托盘图标（纯 Win32）</summary>
    public class TrayIcon : IDisposable
    {
        const int WM_TRAY = 0x8000 + 1;
        const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
        const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
        const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_LBUTTONDBLCLK = 0x0203;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr LoadImage(IntPtr hinst, string name, int type, int cx, int cy, int load);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002;
        const int HOTKEY_ID = 0x0DAF;

        readonly Window owner;
        readonly Action onShow, onQr, onSettings, onExit, onHotkey;
        HwndSource source;
        IntPtr hIcon;
        bool added;
        bool hotkeyOn;

        public TrayIcon(Window win, string iconPath, string tip,
                        Action show, Action qr, Action settings, Action exit, Action hotkey = null)
        {
            owner = win;
            onShow = show; onQr = qr; onSettings = settings; onExit = exit; onHotkey = hotkey;
            hIcon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x10);
            var helper = new WindowInteropHelper(win);
            source = HwndSource.FromHwnd(helper.Handle);
            source.AddHook(WndProc);

            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = helper.Handle,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAY,
                hIcon = hIcon,
                szTip = tip
            };
            added = Shell_NotifyIcon(NIM_ADD, ref data);

            try { hotkeyOn = RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, 0x44); }
            catch { hotkeyOn = false; }
        }

        public void SetTip(string tip)
        {
            try
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = new WindowInteropHelper(owner).Handle,
                    uID = 1,
                    uFlags = NIF_TIP,
                    szTip = tip
                };
                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
            catch { }
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAY)
            {
                int e = lParam.ToInt32();
                if (e == WM_LBUTTONUP || e == WM_LBUTTONDBLCLK) { onShow(); handled = true; }
                else if (e == WM_RBUTTONUP) { ShowMenu(); handled = true; }
            }
            else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                onHotkey?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        void ShowMenu()
        {
            var menu = new ContextMenu();
            foreach (var (text, act) in new (string, Action)[]
            {
                ("显示控制台", onShow),
                ("刷新二维码", onQr),
                ("设置", onSettings)
            })
            {
                var mi = new MenuItem { Header = text };
                mi.Click += (_, __) => act();
                menu.Items.Add(mi);
            }
            menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "退出（机器人继续跑）" };
            exit.Click += (_, __) => onExit();
            menu.Items.Add(exit);

            menu.PlacementTarget = owner;
            menu.IsOpen = true;
            SetForegroundWindow(new WindowInteropHelper(owner).Handle);
        }

        public void Dispose()
        {
            if (added)
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = new WindowInteropHelper(owner).Handle,
                    uID = 1
                };
                Shell_NotifyIcon(NIM_DELETE, ref data);
                added = false;
            }
            if (hotkeyOn)
            {
                try { UnregisterHotKey(new WindowInteropHelper(owner).Handle, HOTKEY_ID); } catch { }
                hotkeyOn = false;
            }
            if (hIcon != IntPtr.Zero) { DestroyIcon(hIcon); hIcon = IntPtr.Zero; }
        }
    }

    /// <summary>窗口位置记忆 + 开机自启 + 主题偏好</summary>
    public static class AppState
    {
        static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DafeiyuConsole");
        static readonly string FilePath = Path.Combine(Dir, "window.json");
        static readonly string PrefPath = Path.Combine(Dir, "prefs.json");
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "DafeiyuConsole";

        public class Bounds
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool Light { get; set; }
        }

        public static Bounds Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Bounds>(File.ReadAllText(FilePath));
            }
            catch { }
            return null;
        }

        public static void Save(Window w)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var b = new Bounds
                {
                    Left = w.RestoreBounds.Left,
                    Top = w.RestoreBounds.Top,
                    Width = w.RestoreBounds.Width,
                    Height = w.RestoreBounds.Height
                };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(b));
            }
            catch { }
        }

        public static bool AutostartEnabled()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return k?.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        public static void SetAutostart(bool on)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (k == null) return;
                if (on)
                {
                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    k.SetValue(RunName, "\"" + exe + "\"");
                }
                else k.DeleteValue(RunName, false);
            }
            catch { }
        }
    }
}
