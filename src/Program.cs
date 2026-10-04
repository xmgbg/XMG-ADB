using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace XMG_ADB
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            int? fixtureResult = TestFixture.TryRun(args);
            if (fixtureResult.HasValue) return fixtureResult.Value;
            if (args.Length > 0 && args[0] == "/uitest")
                return UiSelfTest.Run(args.Length > 1 ? args[1] : null);
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
            MessageBox.Show("程序遇到未处理错误：\n\n" + args.Exception.Message, "XMG_ADB", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        }
    }

    internal static class TestFixture
    {
        public static int? TryRun(string[] args)
        {
            if (args.Length == 0) return null;
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args[0] == "/fixture-echo")
            {
                Console.Write(args.Length > 1 ? args[1] : "");
                return 0;
            }
            if (args[0] == "/fixture-error")
            {
                Console.Error.Write("fixture error");
                return 7;
            }
            if (args[0] == "/fixture-sleep")
            {
                Thread.Sleep(2000);
                return 0;
            }
            if (args[0] == "/fixture-stream")
            {
                Console.Write(new string('X', 4096));
                Console.Out.Flush();
                Thread.Sleep(2000);
                return 0;
            }
            return null;
        }
    }

    public static class SelfTest
    {
        public static int Run()
        {
            int failed = 0;
            string testRoot = Path.Combine(Path.GetTempPath(), "XMG_ADB-SelfTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            try
            {
                failed += Check("解析网络设备", delegate
                {
                    var devices = AppState.ParseDevices("List of devices attached\n192.168.1.8:5555 device product:xmg model:XMG_Device transport_id:2\n");
                    return devices.Count == 1 && devices[0].Serial == "192.168.1.8:5555" && devices[0].Model == "XMG_Device";
                });
                failed += Check("解析未授权状态", delegate
                {
                    var devices = AppState.ParseDevices("ABC123 unauthorized usb:1-1 transport_id:1\n");
                    return devices.Count == 1 && devices[0].StateText == "未授权";
                });
                failed += Check("保持设备选择", delegate
                {
                    var devices = AppState.ParseDevices("A offline\nB device model:Second\nC device model:Third\n");
                    return AppState.SelectDevice(devices, "C").Serial == "C" && AppState.SelectDevice(devices, "missing").Serial == "B";
                });
                failed += Check("历史记录往返", delegate
                {
                    var original = new OperationItem { Time = DateTime.Now, Action = "安装\t应用", Device = "设备A", Status = "成功", Detail = "第一行\n第二行", DurationMs = 123 };
                    OperationItem parsed = AppState.ParseHistory(AppState.SerializeHistory(original));
                    return parsed != null && parsed.Action == "安装 应用" && parsed.Detail == original.Detail && parsed.DurationMs == 123;
                });
                failed += Check("损坏配置兼容", delegate
                {
                    string folder = Path.Combine(testRoot, "broken-settings");
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "settings.ini"), "AdbPath=%%%\nLogBuffer=-10\nRecentDevices=%%%", Encoding.UTF8);
                    var settings = new SettingsStore(folder);
                    return settings.AdbPath == "%%%" && settings.LogBuffer == 1000;
                });
                failed += Check("特殊路径配置", delegate
                {
                    string folder = Path.Combine(testRoot, "settings 空格");
                    var settings = new SettingsStore(folder);
                    settings.DefaultFolder = "C:\\测试 目录\\尾部\\";
                    settings.Save();
                    return new SettingsStore(folder).DefaultFolder == settings.DefaultFolder;
                });
                failed += Check("设置目录可写", delegate
                {
                    var settings = new SettingsStore(Path.Combine(testRoot, "writable"));
                    string path = Path.Combine(settings.DataDirectory, "selftest.tmp");
                    File.WriteAllText(path, "ok");
                    File.Delete(path);
                    return true;
                });

                var processSettings = new SettingsStore(Path.Combine(testRoot, "process"));
                processSettings.AdbPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                processSettings.Save();
                var client = new AdbClient(processSettings);

                failed += CheckAsync("特殊命令参数", async delegate
                {
                    string value = "C:\\测试 目录\\尾部\\";
                    CommandResult result = await client.RunAsync("/fixture-echo " + AdbClient.Quote(value), null, 5000);
                    if (!result.Success || result.Output != value)
                        throw new InvalidOperationException("expected=" + value + "; actual=" + result.Output + "; error=" + result.Error);
                    return true;
                });
                failed += CheckAsync("错误输出与退出码", async delegate
                {
                    CommandResult result = await client.RunAsync("/fixture-error", null, 5000);
                    return result.ExitCode == 7 && result.Error.Contains("fixture error");
                });
                failed += CheckAsync("命令超时", async delegate
                {
                    CommandResult result = await client.RunAsync("/fixture-sleep", null, 100);
                    return result.TimedOut && !result.Cancelled;
                });
                failed += CheckAsync("命令取消", async delegate
                {
                    using (var cancellation = new CancellationTokenSource())
                    {
                        cancellation.CancelAfter(100);
                        CommandResult result = await client.RunAsync("/fixture-sleep", null, 5000, cancellation.Token);
                        return result.Cancelled && !result.TimedOut;
                    }
                });
                failed += CheckAsync("清理不完整文件", async delegate
                {
                    string path = Path.Combine(testRoot, "partial.bin");
                    CommandResult result = await client.RunToFileAsync("/fixture-stream", null, path, 100);
                    return result.TimedOut && !File.Exists(path);
                });
            }
            finally
            {
                try { Directory.Delete(testRoot, true); } catch { }
            }
            Console.WriteLine(failed == 0 ? "SELFTEST PASS" : "SELFTEST FAIL: " + failed);
            return failed == 0 ? 0 : 1;
        }

        private static int CheckAsync(string name, Func<Task<bool>> test)
        {
            return Check(name, delegate { return test().GetAwaiter().GetResult(); });
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
