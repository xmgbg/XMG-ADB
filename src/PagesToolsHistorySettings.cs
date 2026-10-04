using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace XMG_ADB
{
    public sealed class ToolsPage : UserControl, IDisposable
    {
        private TabControl _tabs;
        public void SelectTab(int index) { _tabs.SelectedIndex = index; }
        private readonly AppState _state = AppState.Current;
        private TextBox _captureFolder;
        private TextBox _recordRemote;
        private TextBlock _captureStatus;
        private Process _recordProcess;
        private TextBox _performancePackage;
        private TextBox _performanceOutput;
        private DispatcherTimer _performanceTimer;
        private CancellationTokenSource _performanceCancellation;
        private bool _performanceBusy;
        private ComboBox _scriptPreset;
        private TextBox _scriptEditor;
        private CheckBox _scriptAllDevices;
        private TextBox _scriptOutput;
        private TextBox _scrcpyPath;

        public ToolsPage()
        {
            var root = Ui.LoadView("ToolsPage");
            Content = root;
            _tabs = Ui.Find<TabControl>(root, "Tabs");
            _captureFolder = Ui.Find<TextBox>(root, "CaptureFolder");
            _recordRemote = Ui.Find<TextBox>(root, "RecordRemote");
            _captureStatus = Ui.Find<TextBlock>(root, "CaptureStatus");
            _performancePackage = Ui.Find<TextBox>(root, "PerformancePackage");
            _performanceOutput = Ui.Find<TextBox>(root, "PerformanceOutput");
            _scriptPreset = Ui.Find<ComboBox>(root, "ScriptPreset");
            _scriptEditor = Ui.Find<TextBox>(root, "ScriptEditor");
            _scriptAllDevices = Ui.Find<CheckBox>(root, "ScriptAllDevices");
            _scriptOutput = Ui.Find<TextBox>(root, "ScriptOutput");
            _scrcpyPath = Ui.Find<TextBox>(root, "ScrcpyPath");
            _captureFolder.Text = _state.Settings.DefaultFolder;
            _scrcpyPath.Text = _state.Settings.ScrcpyPath;
            foreach (string item in new[] { "设备概览", "网络诊断", "存储检查", "最近错误日志", "自定义" }) _scriptPreset.Items.Add(item);
            _scriptPreset.SelectionChanged += delegate { ApplyScriptPreset(); };
            _scriptPreset.SelectedIndex = 0;
            Ui.Find<Button>(root, "ChooseFolder").Click += delegate
            {
                var dialog = new Forms.FolderBrowserDialog { SelectedPath = Directory.Exists(_captureFolder.Text) ? _captureFolder.Text : _state.Settings.DefaultFolder };
                if (dialog.ShowDialog() == Forms.DialogResult.OK) _captureFolder.Text = dialog.SelectedPath;
            };
            Ui.Find<Button>(root, "ChooseScrcpy").Click += delegate
            {
                var dialog = new Forms.OpenFileDialog { Filter = "scrcpy 可执行文件 (scrcpy.exe)|scrcpy.exe|可执行文件 (*.exe)|*.exe" };
                if (dialog.ShowDialog() == Forms.DialogResult.OK) _scrcpyPath.Text = dialog.FileName;
            };
            Ui.Find<Button>(root, "Screenshot").Click += async delegate { await ScreenshotAsync(); };
            Ui.Find<Button>(root, "RecordStart").Click += delegate { StartRecording(); };
            Ui.Find<Button>(root, "RecordStop").Click += async delegate { await StopRecordingAsync(); };
            Ui.Find<Button>(root, "MonitorStart").Click += async delegate { await StartPerformanceAsync(); };
            Ui.Find<Button>(root, "MonitorStop").Click += delegate { StopPerformance(); };
            Ui.Find<Button>(root, "Snapshot").Click += async delegate { await ExportSnapshotAsync(); };
            Ui.Find<Button>(root, "Bugreport").Click += async delegate { await ExportBugreportAsync(); };
            Ui.Find<Button>(root, "CopyProperties").Click += async delegate { await CopyPropertiesAsync(); };
            Ui.Find<Button>(root, "RunScript").Click += async delegate { await RunScriptAsync(); };
            Ui.Find<Button>(root, "LaunchMirror").Click += delegate { LaunchScrcpy(); };
        }

        private DeviceInfo RequireDevice(string action)
        {
            DeviceInfo device = _state.SelectedDevice;
            if (device == null || device.State != "device")
            {
                MessageBox.Show("请先选择一台已连接设备。", action, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            return device;
        }

        private async Task ScreenshotAsync()
        {
            DeviceInfo device = RequireDevice("设备截图");
            if (device == null) return;
            string folder = EnsureFolder(_captureFolder.Text);
            string path = Path.Combine(folder, "screenshot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            _state.SetStatus("正在截取设备屏幕", true);
            var watch = Stopwatch.StartNew();
            CommandResult result = await _state.Adb.RunToFileAsync("exec-out screencap -p", device.Serial, path, 60000);
            watch.Stop();
            _state.Record("设备截图", device.Serial, result.Success ? "成功" : "失败", result.Success ? path : result.Combined, watch.ElapsedMilliseconds);
            _state.SetStatus(result.Success ? "截图已保存" : "截图失败", false);
            _captureStatus.Text = result.Success ? "已保存：" + path : AppState.Explain(result);
            if (!result.Success) Ui.ShowResult("设备截图", result);
        }

        private void StartRecording()
        {
            DeviceInfo device = RequireDevice("屏幕录制");
            if (device == null) return;
            if (_recordProcess != null && !_recordProcess.HasExited)
            {
                MessageBox.Show("当前已经有录屏任务。", "屏幕录制", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string remote = _recordRemote.Text.Trim();
            if (!remote.StartsWith("/"))
            {
                MessageBox.Show("请输入设备端绝对路径。", "屏幕录制", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                _recordProcess = _state.Adb.StartStreaming("shell screenrecord --bit-rate 8000000 --time-limit 180 " + AdbClient.ShellQuote(remote), device.Serial, delegate(string line, bool error)
                {
                    Dispatcher.Invoke(delegate { _captureStatus.Text = line; });
                });
                _captureStatus.Text = "正在录制 " + device.Serial + "，最长 180 秒。";
                _state.Record("开始录屏", device.Serial, "运行中", remote, 0);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "屏幕录制", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async Task StopRecordingAsync()
        {
            DeviceInfo device = RequireDevice("屏幕录制");
            if (device == null) return;
            await _state.Adb.RunAsync("shell pkill -INT screenrecord", device.Serial, 10000);
            if (_recordProcess != null)
            {
                try { if (!_recordProcess.HasExited) _recordProcess.Kill(); } catch { }
                try { _recordProcess.Dispose(); } catch { }
                _recordProcess = null;
            }
            await Task.Delay(800);
            string folder = EnsureFolder(_captureFolder.Text);
            string local = Path.Combine(folder, "recording-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mp4");
            CommandResult result = await _state.RunTrackedAsync("拉取录屏", "pull " + AdbClient.Quote(_recordRemote.Text.Trim()) + " " + AdbClient.Quote(local), device.Serial, 10 * 60 * 1000);
            _captureStatus.Text = result.Success ? "已保存：" + local : AppState.Explain(result);
        }

        private async Task StartPerformanceAsync()
        {
            if (RequireDevice("性能监控") == null) return;
            StopPerformance();
            _performanceCancellation = new CancellationTokenSource();
            if (_performanceTimer == null)
            {
                _performanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _performanceTimer.Tick += async delegate { await RefreshPerformanceSafeAsync(); };
            }
            _performanceTimer.Start();
            await RefreshPerformanceSafeAsync();
            if (_performanceCancellation != null && !_performanceCancellation.IsCancellationRequested)
                _state.SetStatus("性能监控已启动", false);
        }

        private void StopPerformance()
        {
            if (_performanceTimer != null) _performanceTimer.Stop();
            if (_performanceCancellation != null)
            {
                _performanceCancellation.Cancel();
                _performanceCancellation.Dispose();
                _performanceCancellation = null;
            }
            _state.SetStatus("性能监控已停止", false);
        }

        private async Task RefreshPerformanceSafeAsync()
        {
            CancellationTokenSource cancellation = _performanceCancellation;
            if (cancellation == null || cancellation.IsCancellationRequested) return;
            try
            {
                await RefreshPerformanceAsync(cancellation.Token);
            }
            catch (Exception ex)
            {
                _performanceOutput.Text = "性能监控失败：" + ex.Message;
                StopPerformance();
                _state.SetStatus("性能监控失败", false);
            }
        }

        private async Task RefreshPerformanceAsync(CancellationToken cancellationToken)
        {
            if (_performanceBusy) return;
            DeviceInfo device = _state.SelectedDevice;
            if (device == null) return;
            _performanceBusy = true;
            try
            {
                string package = _performancePackage.Text.Trim();
                string command = "shell \"echo '[CPU]'; dumpsys cpuinfo | head -n 16; echo; echo '[MEMORY]'; " +
                    ((!string.IsNullOrWhiteSpace(package) && package != "可选包名") ? "dumpsys meminfo " + AdbClient.ShellQuote(package) + " | head -n 30" : "cat /proc/meminfo | head -n 8") +
                    "; echo; echo '[LOAD]'; cat /proc/loadavg\"";
                CommandResult result = await _state.Adb.RunAsync(command, device.Serial, 12000, cancellationToken);
                if (result.Cancelled) return;
                _performanceOutput.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + Environment.NewLine + result.Combined;
            }
            finally { _performanceBusy = false; }
        }

        private async Task ExportSnapshotAsync()
        {
            DeviceInfo device = RequireDevice("诊断快照");
            if (device == null) return;
            var dialog = new Forms.SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt",
                FileName = "diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
                InitialDirectory = _state.Settings.DefaultFolder
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            string command = "shell \"echo '=== BUILD ==='; getprop; echo '=== BATTERY ==='; dumpsys battery; echo '=== STORAGE ==='; df -h; echo '=== NETWORK ==='; ip addr; echo '=== ERRORS ==='; logcat -d -v threadtime '*:E' | tail -n 500\"";
            CommandResult result = await _state.RunTrackedAsync("导出诊断快照", command, device.Serial, 120000);
            if (result.Success) File.WriteAllText(dialog.FileName, result.Output, new UTF8Encoding(false));
            Ui.ShowResult("导出诊断快照", result);
        }

        private async Task ExportBugreportAsync()
        {
            DeviceInfo device = RequireDevice("Bugreport");
            if (device == null) return;
            var dialog = new Forms.SaveFileDialog
            {
                Filter = "ZIP 文件 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                FileName = "bugreport-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip",
                InitialDirectory = _state.Settings.DefaultFolder
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            CommandResult result = await _state.RunTrackedAsync("生成 Bugreport", "bugreport " + AdbClient.Quote(dialog.FileName), device.Serial, 15 * 60 * 1000);
            Ui.ShowResult("生成 Bugreport", result);
        }

        private async Task CopyPropertiesAsync()
        {
            DeviceInfo device = RequireDevice("设备属性");
            if (device == null) return;
            CommandResult result = await _state.RunTrackedAsync("读取设备属性", "shell getprop", device.Serial, 30000);
            if (result.Success) Clipboard.SetText(result.Output);
            Ui.ShowResult("设备属性", result);
        }

        private void ApplyScriptPreset()
        {
            if (_scriptEditor == null || _scriptPreset == null) return;
            string selected = _scriptPreset.SelectedItem as string;
            if (selected == "设备概览") _scriptEditor.Text = "shell getprop ro.product.model\nshell getprop ro.build.version.release\nshell dumpsys battery\nshell df -h /data";
            else if (selected == "网络诊断") _scriptEditor.Text = "shell ip addr\nshell ip route\nshell getprop dhcp.wlan0.ipaddress\nshell ping -c 4 8.8.8.8";
            else if (selected == "存储检查") _scriptEditor.Text = "shell df -h\nshell du -h -d 1 /sdcard 2>/dev/null | sort -h | tail -n 20";
            else if (selected == "最近错误日志") _scriptEditor.Text = "logcat -d -v threadtime *:E";
            else if (selected == "自定义" && _scriptEditor.Text.StartsWith("shell ")) _scriptEditor.Clear();
        }

        private async Task RunScriptAsync()
        {
            var devices = _scriptAllDevices.IsChecked == true
                ? _state.Devices.Where(delegate(DeviceInfo d) { return d.State == "device"; }).ToList()
                : new List<DeviceInfo>(new[] { _state.SelectedDevice }.Where(delegate(DeviceInfo d) { return d != null && d.State == "device"; }));
            if (devices.Count == 0)
            {
                MessageBox.Show("请先选择已连接设备。", "批量脚本", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string[] lines = _scriptEditor.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool dangerous = lines.Any(delegate(string line)
            {
                string value = line.ToLowerInvariant();
                return value.Contains(" reboot") || value.StartsWith("reboot") || value.Contains(" wipe") || value.Contains(" format") || value.Contains(" uninstall") || value.Contains(" pm clear");
            });
            if (dangerous && MessageBox.Show("脚本包含可能中断设备或清除数据的命令。是否继续？", "批量脚本", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _scriptOutput.Clear();
            foreach (DeviceInfo device in devices)
            {
                _scriptOutput.AppendText("[" + device.Serial + "]" + Environment.NewLine);
                foreach (string raw in lines)
                {
                    string command = raw.Trim();
                    if (command.Length == 0 || command.StartsWith("#")) continue;
                    if (command.StartsWith("adb ", StringComparison.OrdinalIgnoreCase)) command = command.Substring(4);
                    CommandResult result = await _state.RunTrackedAsync("执行脚本", command, device.Serial, 5 * 60 * 1000);
                    _scriptOutput.AppendText("> " + command + Environment.NewLine + result.Combined + Environment.NewLine + Environment.NewLine);
                    _scriptOutput.ScrollToEnd();
                }
            }
        }

        private void LaunchScrcpy()
        {
            DeviceInfo device = RequireDevice("设备镜像");
            if (device == null) return;
            string path = _scrcpyPath.Text.Trim();
            if (!File.Exists(path))
            {
                MessageBox.Show("请先选择有效的 scrcpy.exe。", "设备镜像", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                _state.Settings.ScrcpyPath = path;
                _state.Settings.Save();
                Process.Start(new ProcessStartInfo(path, "-s " + AdbClient.Quote(device.Serial)) { UseShellExecute = false });
                _state.Record("启动设备镜像", device.Serial, "成功", path, 0);
            }
            catch (Exception ex)
            {
                _state.Record("启动设备镜像", device.Serial, "失败", ex.Message, 0);
                MessageBox.Show(ex.Message, "设备镜像", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string EnsureFolder(string value)
        {
            string folder = string.IsNullOrWhiteSpace(value) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : value.Trim();
            Directory.CreateDirectory(folder);
            return folder;
        }

        public void Dispose()
        {
            StopPerformance();
            if (_recordProcess != null)
            {
                try { if (!_recordProcess.HasExited) _recordProcess.Kill(); } catch { }
                try { _recordProcess.Dispose(); } catch { }
            }
        }
    }

    public sealed class HistoryPage : UserControl
    {
        private readonly AppState _state = AppState.Current;
        private readonly ListView _list;
        private readonly TextBox _detail;

        public HistoryPage()
        {
            var root = Ui.LoadView("HistoryPage");
            Content = root;
            _list = Ui.Find<ListView>(root, "History");
            _detail = Ui.Find<TextBox>(root, "Detail");
            _list.ItemsSource = _state.Operations;
            _list.SelectionChanged += delegate
            {
                var item = _list.SelectedItem as OperationItem;
                _detail.Text = item == null ? "选择记录查看详情。" : item.Detail;
            };
            Ui.Find<Button>(root, "Export").Click += delegate { Export(); };
            Ui.Find<Button>(root, "Clear").Click += delegate
            {
                if (MessageBox.Show("确定清空全部操作记录？", "清空记录", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) _state.ClearHistory();
            };
        }

        private void Export()
        {
            var dialog = new Forms.SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt",
                FileName = "operations-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv",
                InitialDirectory = _state.Settings.DefaultFolder
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            var lines = new List<string> { "时间,操作,目标设备,结果,耗时毫秒,详情" };
            foreach (OperationItem item in _state.Operations)
            {
                lines.Add(string.Join(",", new[] { Csv(item.Time.ToString("yyyy-MM-dd HH:mm:ss")), Csv(item.Action), Csv(item.Device), Csv(item.Status), item.DurationMs.ToString(), Csv(item.Detail) }));
            }
            File.WriteAllLines(dialog.FileName, lines.ToArray(), new UTF8Encoding(true));
        }

        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
    }

    public sealed class SettingsPage : UserControl
    {
        private readonly AppState _state = AppState.Current;
        private readonly Action<string> _applyTheme;
        private readonly Action _updateHeader;
        private readonly TextBox _adbPath;
        private readonly TextBox _scrcpyPath;
        private readonly TextBox _defaultFolder;
        private readonly TextBox _logBuffer;
        private readonly ComboBox _theme;
        private readonly TextBlock _status;

        public SettingsPage(Action<string> applyTheme, Action updateHeader)
        {
            _applyTheme = applyTheme;
            _updateHeader = updateHeader;
            var root = Ui.LoadView("SettingsPage");
            Content = root;
            _adbPath = Ui.Find<TextBox>(root, "AdbPath");
            _scrcpyPath = Ui.Find<TextBox>(root, "ScrcpyPath");
            _defaultFolder = Ui.Find<TextBox>(root, "DefaultFolder");
            _logBuffer = Ui.Find<TextBox>(root, "LogBuffer");
            _theme = Ui.Find<ComboBox>(root, "Theme");
            _status = Ui.Find<TextBlock>(root, "Status");
            _adbPath.Text = _state.Settings.AdbPath;
            _scrcpyPath.Text = _state.Settings.ScrcpyPath;
            _defaultFolder.Text = _state.Settings.DefaultFolder;
            _logBuffer.Text = _state.Settings.LogBuffer.ToString();
            _theme.Items.Add("Light");
            _theme.Items.Add("Dark");
            _theme.SelectedItem = _state.Settings.Theme;
            _status.Text = _state.Adb.IsAvailable ? "当前 ADB：" + _state.Adb.ExecutablePath : "未找到 ADB，请指定 adb.exe。";
            Ui.Find<Button>(root, "ChooseAdb").Click += delegate { ChooseExecutable(_adbPath, "adb.exe"); };
            Ui.Find<Button>(root, "ChooseScrcpy").Click += delegate { ChooseExecutable(_scrcpyPath, "scrcpy.exe"); };
            Ui.Find<Button>(root, "ChooseFolder").Click += delegate
            {
                var dialog = new Forms.FolderBrowserDialog { SelectedPath = Directory.Exists(_defaultFolder.Text) ? _defaultFolder.Text : _state.Settings.DefaultFolder };
                if (dialog.ShowDialog() == Forms.DialogResult.OK) _defaultFolder.Text = dialog.SelectedPath;
            };
            Ui.Find<Button>(root, "Save").Click += async delegate { await SaveAsync(); };
            Ui.Find<Button>(root, "Detect").Click += async delegate
            {
                _state.Adb.Detect();
                _updateHeader();
                _status.Text = _state.Adb.IsAvailable ? "检测到：" + _state.Adb.ExecutablePath : "未找到 adb.exe";
                if (_state.Adb.IsAvailable) await _state.RefreshDevicesAsync();
            };
            Ui.Find<Button>(root, "OpenFolder").Click += delegate { Process.Start("explorer.exe", AdbClient.Quote(_state.Settings.DataDirectory)); };
        }

        private static void ChooseExecutable(TextBox input, string expectedName)
        {
            var dialog = new Forms.OpenFileDialog { Filter = expectedName + "|" + expectedName + "|可执行文件 (*.exe)|*.exe" };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) input.Text = dialog.FileName;
        }

        private async Task SaveAsync()
        {
            int buffer;
            if (!int.TryParse(_logBuffer.Text, out buffer) || buffer < 1000 || buffer > 200000)
            {
                MessageBox.Show("日志缓冲行数应在 1000 到 200000 之间。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _state.Settings.AdbPath = _adbPath.Text.Trim();
            _state.Settings.ScrcpyPath = _scrcpyPath.Text.Trim();
            _state.Settings.DefaultFolder = _defaultFolder.Text.Trim();
            _state.Settings.LogBuffer = buffer;
            _state.Settings.Theme = _theme.SelectedItem == null ? "Light" : _theme.SelectedItem.ToString();
            _state.Settings.Save();
            _state.Adb.SetPath(_state.Settings.AdbPath);
            _applyTheme(_state.Settings.Theme);
            _updateHeader();
            _status.Text = _state.Adb.IsAvailable ? "设置已保存。ADB：" + _state.Adb.ExecutablePath : "设置已保存，但仍未找到 adb.exe。";
            _state.Record("保存设置", null, "成功", _status.Text, 0);
            if (_state.Adb.IsAvailable) await _state.RefreshDevicesAsync();
        }
    }
}
