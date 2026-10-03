using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace XMG_ADB
{
    public sealed class CommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public bool TimedOut { get; set; }
        public bool Cancelled { get; set; }
        public bool Success { get { return !TimedOut && !Cancelled && ExitCode == 0; } }
        public string Combined
        {
            get
            {
                string value = ((Output ?? "") + Environment.NewLine + (Error ?? "")).Trim();
                return value.Length == 0 ? "命令未返回文本。" : value;
            }
        }
    }

    public sealed class DeviceInfo
    {
        public string Serial { get; set; }
        public string State { get; set; }
        public string Model { get; set; }
        public string Product { get; set; }
        public string Transport { get; set; }
        public string AndroidVersion { get; set; }

        public string Address
        {
            get { return Serial != null && Serial.Contains(":") ? Serial : "USB"; }
        }

        public string StateText
        {
            get
            {
                if (State == "device") return "已连接";
                if (State == "offline") return "离线";
                if (State == "unauthorized") return "未授权";
                if (State == "no permissions") return "无权限";
                return string.IsNullOrWhiteSpace(State) ? "未知" : State;
            }
        }

        public string Display
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(Model) ? "Android 设备" : Model.Replace('_', ' ');
                return string.Format("{0}  ·  {1}  ·  {2}", name, Serial, StateText);
            }
        }
    }

    public sealed class OperationItem
    {
        public DateTime Time { get; set; }
        public string Action { get; set; }
        public string Device { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public long DurationMs { get; set; }

        public string TimeText { get { return Time.ToString("MM-dd HH:mm:ss"); } }
        public string DurationText { get { return DurationMs <= 0 ? "" : DurationMs + " ms"; } }
        public string Summary
        {
            get
            {
                return string.Format("{0}    {1,-10}    {2,-8}    {3}", TimeText, Action, Status, Device);
            }
        }
    }

    public sealed class SettingsStore
    {
        public string AdbPath { get; set; }
        public string ScrcpyPath { get; set; }
        public string DefaultFolder { get; set; }
        public string Theme { get; set; }
        public int LogBuffer { get; set; }
        public List<string> RecentDevices { get; private set; }

        public string DataDirectory { get; private set; }
        public string SettingsPath { get { return Path.Combine(DataDirectory, "settings.ini"); } }
        public string HistoryPath { get { return Path.Combine(DataDirectory, "operations.log"); } }

        public SettingsStore()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DataDirectory = EnsureWritableDirectory(Path.Combine(local, "XMG_ADB"));
            Directory.CreateDirectory(Path.Combine(DataDirectory, "android"));
            Environment.SetEnvironmentVariable("ANDROID_USER_HOME", Path.Combine(DataDirectory, "android"));

            AdbPath = "";
            ScrcpyPath = "";
            DefaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Theme = "Light";
            LogBuffer = 30000;
            RecentDevices = new List<string>();
            Load();
        }

        private static string EnsureWritableDirectory(string preferred)
        {
            string[] candidates =
            {
                preferred,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"),
                Path.Combine(Path.GetTempPath(), "XMG_ADB")
            };
            foreach (string candidate in candidates)
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    string probe = Path.Combine(candidate, ".write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return candidate;
                }
                catch { }
            }
            throw new UnauthorizedAccessException("无法创建应用数据目录。");
        }

        public void Load()
        {
            if (!File.Exists(SettingsPath)) return;
            foreach (string raw in File.ReadAllLines(SettingsPath, Encoding.UTF8))
            {
                int index = raw.IndexOf('=');
                if (index <= 0) continue;
                string key = raw.Substring(0, index).Trim();
                string value = raw.Substring(index + 1).Trim();
                if (key == "AdbPath") AdbPath = Decode(value);
                else if (key == "ScrcpyPath") ScrcpyPath = Decode(value);
                else if (key == "DefaultFolder") DefaultFolder = Decode(value);
                else if (key == "Theme") Theme = value;
                else if (key == "LogBuffer")
                {
                    int count;
                    if (int.TryParse(value, out count)) LogBuffer = Math.Max(1000, Math.Min(200000, count));
                }
                else if (key == "RecentDevices")
                {
                    RecentDevices = value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(Decode).Distinct().Take(20).ToList();
                }
            }
        }

        public void Save()
        {
            var lines = new List<string>();
            lines.Add("AdbPath=" + Encode(AdbPath));
            lines.Add("ScrcpyPath=" + Encode(ScrcpyPath));
            lines.Add("DefaultFolder=" + Encode(DefaultFolder));
            lines.Add("Theme=" + Theme);
            lines.Add("LogBuffer=" + LogBuffer);
            lines.Add("RecentDevices=" + string.Join("|", RecentDevices.Select(Encode)));
            File.WriteAllLines(SettingsPath, lines.ToArray(), new UTF8Encoding(false));
        }

        public void RememberDevice(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            RecentDevices.RemoveAll(delegate(string item) { return string.Equals(item, address, StringComparison.OrdinalIgnoreCase); });
            RecentDevices.Insert(0, address);
            if (RecentDevices.Count > 20) RecentDevices.RemoveRange(20, RecentDevices.Count - 20);
            Save();
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Decode(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return value; }
        }
    }

    public sealed class AdbClient
    {
        private readonly SettingsStore _settings;
        public string ExecutablePath { get; private set; }
        public bool IsAvailable { get { return !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath); } }

        public AdbClient(SettingsStore settings)
        {
            _settings = settings;
            Detect();
        }

        public void Detect()
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(_settings.AdbPath)) candidates.Add(_settings.AdbPath);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "platform-tools", "adb.exe"));
            candidates.Add(Path.Combine(baseDir, "adb.exe"));
            string androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
            if (!string.IsNullOrWhiteSpace(androidHome)) candidates.Add(Path.Combine(androidHome, "platform-tools", "adb.exe"));
            string sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            if (!string.IsNullOrWhiteSpace(sdkRoot)) candidates.Add(Path.Combine(sdkRoot, "platform-tools", "adb.exe"));
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string folder in path.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(folder)) candidates.Add(Path.Combine(folder.Trim(), "adb.exe"));
            }
            ExecutablePath = candidates.FirstOrDefault(File.Exists) ?? "";
        }

        public void SetPath(string path)
        {
            _settings.AdbPath = path ?? "";
            _settings.Save();
            Detect();
        }

        public Task<CommandResult> RunAsync(string arguments, string serial, int timeoutMs)
        {
            return RunAsync(arguments, serial, timeoutMs, CancellationToken.None);
        }

        public Task<CommandResult> RunAsync(string arguments, string serial, int timeoutMs, CancellationToken cancellationToken)
        {
            if (!IsAvailable)
            {
                return Task.FromResult(new CommandResult { ExitCode = -1, Error = "未找到 adb.exe，请在设置中指定路径。" });
            }
            string all = string.IsNullOrWhiteSpace(serial)
                ? arguments
                : "-s " + Quote(serial) + " " + arguments;
            return RunProcessAsync(ExecutablePath, all, timeoutMs, cancellationToken);
        }

        public Process StartStreaming(string arguments, string serial, Action<string, bool> onLine)
        {
            if (!IsAvailable) throw new FileNotFoundException("未找到 adb.exe");
            string all = string.IsNullOrWhiteSpace(serial)
                ? arguments
                : "-s " + Quote(serial) + " " + arguments;
            var info = CreateStartInfo(ExecutablePath, all);
            var process = new Process();
            process.StartInfo = info;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
            {
                if (args.Data != null && onLine != null) onLine(args.Data, false);
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
            {
                if (args.Data != null && onLine != null) onLine(args.Data, true);
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        public async Task<CommandResult> RunToFileAsync(string arguments, string serial, string outputPath, int timeoutMs)
        {
            return await RunToFileAsync(arguments, serial, outputPath, timeoutMs, CancellationToken.None);
        }

        public async Task<CommandResult> RunToFileAsync(string arguments, string serial, string outputPath, int timeoutMs, CancellationToken cancellationToken)
        {
            if (!IsAvailable) return new CommandResult { ExitCode = -1, Error = "未找到 adb.exe" };
            string all = string.IsNullOrWhiteSpace(serial) ? arguments : "-s " + Quote(serial) + " " + arguments;
            var process = new Process();
            process.StartInfo = CreateStartInfo(ExecutablePath, all);
            bool keepOutput = false;
            try
            {
                process.Start();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    Task copy = process.StandardOutput.BaseStream.CopyToAsync(file);
                    ProcessCompletion completion = await WaitForProcessAsync(process, timeoutMs, cancellationToken);
                    if (completion != ProcessCompletion.Completed)
                    {
                        await SettleAsync(Task.WhenAll(copy, errorTask), 2000);
                        return CreateInterruptedResult(completion);
                    }
                    await Task.WhenAll(copy, errorTask);
                }
                keepOutput = process.ExitCode == 0;
                return new CommandResult { ExitCode = process.ExitCode, Error = await errorTask, Output = keepOutput ? outputPath : "" };
            }
            catch (Exception ex)
            {
                return new CommandResult { ExitCode = -1, Error = ex.Message };
            }
            finally
            {
                process.Dispose();
                if (!keepOutput) TryDelete(outputPath);
            }
        }

        public static string Quote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public static string ShellQuote(string value)
        {
            return "'" + (value ?? "").Replace("'", "'\\''") + "'";
        }

        private static ProcessStartInfo CreateStartInfo(string fileName, string arguments)
        {
            var info = new ProcessStartInfo(fileName, arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            return info;
        }

        private enum ProcessCompletion
        {
            Completed,
            TimedOut,
            Cancelled
        }

        private static async Task<CommandResult> RunProcessAsync(string fileName, string arguments, int timeoutMs, CancellationToken cancellationToken)
        {
            var process = new Process();
            process.StartInfo = CreateStartInfo(fileName, arguments);
            try
            {
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                ProcessCompletion completion = await WaitForProcessAsync(process, timeoutMs, cancellationToken);
                if (completion != ProcessCompletion.Completed)
                {
                    await SettleAsync(Task.WhenAll(output, error), 2000);
                    return CreateInterruptedResult(completion, output.IsCompleted && !output.IsFaulted ? output.Result : "");
                }
                await Task.WhenAll(output, error);
                return new CommandResult { ExitCode = process.ExitCode, Output = output.Result, Error = error.Result };
            }
            catch (Exception ex)
            {
                return new CommandResult { ExitCode = -1, Error = ex.Message };
            }
            finally { process.Dispose(); }
        }

        private static async Task<ProcessCompletion> WaitForProcessAsync(Process process, int timeoutMs, CancellationToken cancellationToken)
        {
            Task wait = Task.Run(delegate { process.WaitForExit(); });
            Task timeout = Task.Delay(Math.Max(1, timeoutMs));
            var cancelled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(delegate { cancelled.TrySetResult(true); }))
            {
                Task completed = await Task.WhenAny(wait, timeout, cancelled.Task);
                if (completed == wait)
                {
                    await wait;
                    return ProcessCompletion.Completed;
                }

                KillAndWait(process);
                return completed == cancelled.Task ? ProcessCompletion.Cancelled : ProcessCompletion.TimedOut;
            }
        }

        private static void KillAndWait(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch { }
            try
            {
                if (!process.HasExited) process.WaitForExit(5000);
            }
            catch { }
        }

        private static async Task SettleAsync(Task task, int timeoutMs)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed == task)
            {
                try { await task; } catch { }
            }
        }

        private static CommandResult CreateInterruptedResult(ProcessCompletion completion, string output = "")
        {
            bool cancelled = completion == ProcessCompletion.Cancelled;
            return new CommandResult
            {
                ExitCode = -1,
                Output = output,
                Error = cancelled ? "操作已取消" : "操作超时",
                Cancelled = cancelled,
                TimedOut = !cancelled
            };
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }

    public sealed class AppState
    {
        private static readonly AppState _current = new AppState();
        private readonly object _historyLock = new object();
        private DeviceInfo _selectedDevice;

        public static AppState Current { get { return _current; } }
        public SettingsStore Settings { get; private set; }
        public AdbClient Adb { get; private set; }
        public ObservableCollection<DeviceInfo> Devices { get; private set; }
        public ObservableCollection<OperationItem> Operations { get; private set; }
        public DeviceInfo SelectedDevice
        {
            get { return _selectedDevice; }
            set
            {
                _selectedDevice = value;
                if (SelectedDeviceChanged != null) SelectedDeviceChanged(this, EventArgs.Empty);
            }
        }

        public event EventHandler DevicesChanged;
        public event EventHandler SelectedDeviceChanged;
        public event EventHandler StatusChanged;
        public string StatusText { get; private set; }
        public bool IsBusy { get; private set; }

        private AppState()
        {
            Settings = new SettingsStore();
            Adb = new AdbClient(Settings);
            Devices = new ObservableCollection<DeviceInfo>();
            Operations = new ObservableCollection<OperationItem>();
            StatusText = "就绪";
            LoadHistory();
        }

        public async Task RefreshDevicesAsync()
        {
            CommandResult result = await Adb.RunAsync("devices -l", null, 10000);
            var parsed = ParseDevices(result.Output);
            await Application.Current.Dispatcher.InvokeAsync(delegate
            {
                string selectedSerial = SelectedDevice == null ? null : SelectedDevice.Serial;
                Devices.Clear();
                foreach (DeviceInfo device in parsed) Devices.Add(device);
                SelectedDevice = Devices.FirstOrDefault(delegate(DeviceInfo d) { return d.Serial == selectedSerial; })
                    ?? Devices.FirstOrDefault(delegate(DeviceInfo d) { return d.State == "device"; });
                if (DevicesChanged != null) DevicesChanged(this, EventArgs.Empty);
            });
        }

        public async Task<CommandResult> RunTrackedAsync(string action, string arguments, string serial, int timeoutMs)
        {
            SetStatus(action + "正在执行", true);
            var watch = Stopwatch.StartNew();
            CommandResult result = await Adb.RunAsync(arguments, serial, timeoutMs);
            watch.Stop();
            Record(action, serial, result.Success ? "成功" : "失败", Explain(result), watch.ElapsedMilliseconds);
            SetStatus(action + (result.Success ? "完成" : "失败"), false);
            return result;
        }

        public void Record(string action, string device, string status, string detail, long duration)
        {
            var item = new OperationItem
            {
                Time = DateTime.Now,
                Action = action,
                Device = string.IsNullOrWhiteSpace(device) ? "本机" : device,
                Status = status,
                Detail = detail ?? "",
                DurationMs = duration
            };
            Application.Current.Dispatcher.Invoke(delegate
            {
                Operations.Insert(0, item);
                while (Operations.Count > 1000) Operations.RemoveAt(Operations.Count - 1);
            });
            lock (_historyLock)
            {
                string line = string.Join("\t", new[]
                {
                    item.Time.ToString("o"), Clean(item.Action), Clean(item.Device), Clean(item.Status),
                    duration.ToString(), Convert.ToBase64String(Encoding.UTF8.GetBytes(item.Detail ?? ""))
                });
                File.AppendAllText(Settings.HistoryPath, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }

        public void ClearHistory()
        {
            Operations.Clear();
            if (File.Exists(Settings.HistoryPath)) File.Delete(Settings.HistoryPath);
        }

        public void SetStatus(string text, bool busy)
        {
            StatusText = text;
            IsBusy = busy;
            if (StatusChanged != null) StatusChanged(this, EventArgs.Empty);
        }

        public static string Explain(CommandResult result)
        {
            string text = result.Combined;
            if (result.Cancelled) return "操作已取消。\n\n" + text;
            if (result.TimedOut) return "操作超时。请检查设备连接和网络。\n\n" + text;
            if (text.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
                return "设备未授权。请在设备上确认 USB 或无线调试授权。\n\n" + text;
            if (text.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0)
                return "设备处于离线状态。请重新连接设备或重启 ADB Server。\n\n" + text;
            if (text.IndexOf("INSTALL_FAILED_INSUFFICIENT_STORAGE", StringComparison.OrdinalIgnoreCase) >= 0)
                return "设备存储空间不足。\n\n" + text;
            if (text.IndexOf("INSTALL_FAILED_VERSION_DOWNGRADE", StringComparison.OrdinalIgnoreCase) >= 0)
                return "安装包版本低于设备上的现有版本。可在高级选项中允许降级。\n\n" + text;
            if (text.IndexOf("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase) >= 0)
                return "安装包签名与现有应用不一致。\n\n" + text;
            if (text.IndexOf("cannot connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("No connection", StringComparison.OrdinalIgnoreCase) >= 0)
                return "无法连接目标地址。请检查 IP、端口、防火墙和设备调试设置。\n\n" + text;
            return text;
        }

        public static List<DeviceInfo> ParseDevices(string output)
        {
            var result = new List<DeviceInfo>();
            foreach (string raw in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.StartsWith("List of devices")) continue;
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var device = new DeviceInfo { Serial = parts[0], State = parts[1], Model = "", Product = "", Transport = "" };
                for (int i = 2; i < parts.Length; i++)
                {
                    int separator = parts[i].IndexOf(':');
                    if (separator < 1) continue;
                    string key = parts[i].Substring(0, separator);
                    string value = parts[i].Substring(separator + 1);
                    if (key == "model") device.Model = value;
                    else if (key == "product") device.Product = value;
                    else if (key == "transport_id") device.Transport = value;
                }
                result.Add(device);
            }
            return result;
        }

        private void LoadHistory()
        {
            if (!File.Exists(Settings.HistoryPath)) return;
            try
            {
                string[] lines = File.ReadAllLines(Settings.HistoryPath, Encoding.UTF8);
                foreach (string line in lines.Reverse().Take(500))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 6) continue;
                    DateTime time;
                    long duration;
                    if (!DateTime.TryParse(parts[0], out time)) time = DateTime.Now;
                    long.TryParse(parts[4], out duration);
                    string detail = "";
                    try { detail = Encoding.UTF8.GetString(Convert.FromBase64String(parts[5])); } catch { }
                    Operations.Add(new OperationItem
                    {
                        Time = time, Action = parts[1], Device = parts[2], Status = parts[3],
                        DurationMs = duration, Detail = detail
                    });
                }
            }
            catch { }
        }

        private static string Clean(string value)
        {
            return (value ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
