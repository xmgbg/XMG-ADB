using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace E300DeviceConsole
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Any(delegate(string arg) { return string.Equals(arg, "/selftest", StringComparison.OrdinalIgnoreCase); }))
                return SelfTest.Run();

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += OnUnhandledException;
            var window = new MainWindow();
            app.MainWindow = window;
            window.Show();
            return app.Run();
        }

        private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            try
            {
                string path = Path.Combine(AppState.Current.Settings.DataDirectory, "crash.log");
                File.AppendAllText(path, DateTime.Now.ToString("o") + Environment.NewLine + args.Exception + Environment.NewLine);
            }
            catch { }
            MessageBox.Show("程序遇到未处理错误：\n\n" + args.Exception.Message, "E300 Device Console", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        }
    }

    public static class SelfTest
    {
        public static int Run()
        {
            int failed = 0;
            failed += Check("解析网络设备", delegate
            {
                var devices = AppState.ParseDevices("List of devices attached\n192.168.1.8:5555 device product:e300 model:E300_Medical transport_id:2\n");
                return devices.Count == 1 && devices[0].Serial == "192.168.1.8:5555" && devices[0].Model == "E300_Medical";
            });
            failed += Check("解析未授权状态", delegate
            {
                var devices = AppState.ParseDevices("ABC123 unauthorized usb:1-1 transport_id:1\n");
                return devices.Count == 1 && devices[0].StateText == "未授权";
            });
            failed += Check("命令参数引用", delegate
            {
                return AdbClient.Quote("C:\\A B\\a.apk") == "\"C:\\A B\\a.apk\"";
            });
            failed += Check("设置目录可写", delegate
            {
                var settings = new SettingsStore();
                string path = Path.Combine(settings.DataDirectory, "selftest.tmp");
                File.WriteAllText(path, "ok");
                File.Delete(path);
                return true;
            });
            Console.WriteLine(failed == 0 ? "SELFTEST PASS" : "SELFTEST FAIL: " + failed);
            return failed == 0 ? 0 : 1;
        }

        private static int Check(string name, Func<bool> test)
        {
            try
            {
                bool ok = test();
                Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + name + ": " + ex.Message);
                return 1;
            }
        }
    }
}
