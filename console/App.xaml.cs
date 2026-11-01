using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DafeiyuConsole
{
    public partial class App : Application
    {
        static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DafeiyuConsole");
        static readonly string CrashLog = Path.Combine(LogDir, "crash.log");

        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Dump(e.ExceptionObject as Exception, "AppDomain");
            DispatcherUnhandledException += (s, e) =>
            {
                Dump(e.Exception, "Dispatcher");
                e.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Dump(e.Exception, "Task");
                e.SetObserved();
            };
        }

        static bool restarting;

        static void Dump(Exception ex, string source)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                var text = string.Format("[{0}] {1}\n{2}\n\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), source,
                    ex?.ToString() ?? "unknown");
                File.AppendAllText(CrashLog, text);
                var fi = new FileInfo(CrashLog);
                if (fi.Exists && fi.Length > 200 * 1024)
                {
                    var keep = File.ReadAllText(CrashLog);
                    File.WriteAllText(CrashLog, keep.Substring(keep.Length - 120 * 1024));
                }
            }
            catch { }
        }

        /// <summary>界面挂了也能自己回来</summary>
        public static void RestartSafely()
        {
            if (restarting) return;
            restarting = true;
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true
                });
                Thread.Sleep(150);
                Current.Shutdown();
            }
            catch (Exception e)
            {
                Dump(e, "Restart");
            }
        }
    }
}
