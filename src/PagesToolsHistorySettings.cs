using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace E300DeviceConsole
{
    public sealed class ToolsPage : UserControl, IDisposable
    {
        private readonly AppState _state = AppState.Current;
        private TextBox _captureFolder;
        private TextBox _recordRemote;
        private TextBlock _captureStatus;
        private Process _recordProcess;
        private TextBox _performancePackage;
        private TextBox _performanceOutput;
        private DispatcherTimer _performanceTimer;
        private bool _performanceBusy;
        private ComboBox _scriptPreset;
        private TextBox _scriptEditor;
        private CheckBox _scriptAllDevices;
        private TextBox _scriptOutput;
        private TextBox _scrcpyPath;

        public ToolsPage()
        {
            var root = new Grid { Margin = Ui.PagePadding };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var heading = new StackPanel();
            Ui.Heading("工具与诊断", "截图录屏、性能监控、诊断导出、脚本预设和 scrcpy 集成。", heading);
            root.Children.Add(heading);
            var tabs = new TabControl();
            tabs.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            tabs.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            tabs.Items.Add(new TabItem { Header = "截图与录屏", Content = BuildCaptureTab() });
            tabs.Items.Add(new TabItem { Header = "性能监控", Content = BuildPerformanceTab() });
            tabs.Items.Add(new TabItem { Header = "诊断导出", Content = BuildDiagnosticsTab() });
            tabs.Items.Add(new TabItem { Header = "批量脚本", Content = BuildScriptsTab() });
            tabs.Items.Add(new TabItem { Header = "设备镜像", Content = BuildScrcpyTab() });
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);
            Content = root;
        }

        private UIElement BuildCaptureTab()
        {
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(Ui.Text("保存位置", 15, "TextPrimaryBrush", FontWeights.SemiBold));
            var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 24) };
            _captureFolder = Ui.Input(_state.Settings.DefaultFolder, 470);
            var choose = Ui.Button("选择目录", false);
            choose.Click += delegate
            {
                var dialog = new Forms.FolderBrowserDialog { SelectedPath = Directory.Exists(_captureFolder.Text) ? _captureFolder.Text : _state.Settings.DefaultFolder };
                if (dialog.ShowDialog() == Forms.DialogResult.OK) _captureFolder.Text = dialog.SelectedPath;
            };
            folderRow.Children.Add(_captureFolder);
            folderRow.Children.Add(choose);
            panel.Children.Add(folderRow);

            var screenshot = Ui.Button("截取当前屏幕", true);
            screenshot.Width = 160;
            screenshot.Click += async delegate { await ScreenshotAsync(); };
            panel.Children.Add(screenshot);

            var divider = new Border { Height = 1, Margin = new Thickness(0, 26, 0, 22) };
            divider.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            panel.Children.Add(divider);
            panel.Children.Add(Ui.Text("屏幕录制", 15, "TextPrimaryBrush", FontWeights.SemiBold));
            var recordRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 14) };
            _recordRemote = Ui.Input("/sdcard/e300-screen.mp4", 360);
            var start = Ui.Button("开始录制", true);
            start.Click += delegate { StartRecording(); };
            var stop = Ui.Button("停止并拉取", false);
            stop.Click += async delegate { await StopRecordingAsync(); };
            recordRow.Children.Add(_recordRemote);
            recordRow.Children.Add(start);
            recordRow.Children.Add(stop);
            panel.Children.Add(recordRow);
            _captureStatus = Ui.Text("录屏最长 180 秒，停止后自动拉取到保存目录。", 12, "TextSecondaryBrush", FontWeights.Normal);
            panel.Children.Add(_captureStatus);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement BuildPerformanceTab()
        {
            var panel = new Grid { Margin = new Thickness(22) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            _performancePackage = Ui.Input("可选包名", 240);
            var start = Ui.Button("开始监控", true);
            start.Click += delegate { StartPerformance(); };
            var stop = Ui.Button("停止", false);
            stop.Click += delegate { StopPerformance(); };
            toolbar.Children.Add(_performancePackage);
            toolbar.Children.Add(start);
            toolbar.Children.Add(stop);
            panel.Children.Add(toolbar);
            _performanceOutput = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 16, 0, 0),
                Padding = new Thickness(12)
            };
            _performanceOutput.SetResourceReference(Control.BackgroundProperty, "LogBackgroundBrush");
            _performanceOutput.SetResourceReference(Control.ForegroundProperty, "LogTextBrush");
            _performanceOutput.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            Grid.SetRow(_performanceOutput, 1);
            panel.Children.Add(_performanceOutput);
            return panel;
        }

        private UIElement BuildDiagnosticsTab()
        {
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(Ui.Text("诊断资料", 15, "TextPrimaryBrush", FontWeights.SemiBold));
            var note = Ui.Text("快速快照会收集设备属性、电量、存储、网络和最近错误日志。完整 Bugreport 可能需要数分钟。", 12, "TextSecondaryBrush", FontWeights.Normal);
            note.Margin = new Thickness(0, 8, 0, 20);
            panel.Children.Add(note);
            var snapshot = Ui.Button("导出快速快照", true);
            snapshot.Width = 160;
            snapshot.Margin = new Thickness(0, 0, 0, 10);
            snapshot.Click += async delegate { await ExportSnapshotAsync(); };
            panel.Children.Add(snapshot);
            var bugreport = Ui.Button("生成完整 Bugreport", false);
            bugreport.Width = 180;
            bugreport.Margin = new Thickness(0, 0, 0, 10);
            bugreport.Click += async delegate { await ExportBugreportAsync(); };
            panel.Children.Add(bugreport);
            var copy = Ui.Button("复制设备属性", false);
            copy.Width = 160;
            copy.Click += async delegate { await CopyPropertiesAsync(); };
            panel.Children.Add(copy);
            return panel;
        }

        private UIElement BuildScriptsTab()
        {
            var panel = new Grid { Margin = new Thickness(22) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(130) });
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            _scriptPreset = Ui.Combo(230);
            foreach (string item in new[] { "设备概览", "网络诊断", "存储检查", "最近错误日志", "自定义" }) _scriptPreset.Items.Add(item);
            _scriptPreset.SelectedIndex = 0;
            _scriptPreset.SelectionChanged += delegate { ApplyScriptPreset(); };
            _scriptAllDevices = Ui.Check("在全部设备上执行");
            var run = Ui.Button("执行脚本", true);
            run.Click += async delegate { await RunScriptAsync(); };
            toolbar.Children.Add(_scriptPreset);
            toolbar.Children.Add(_scriptAllDevices);
            toolbar.Children.Add(run);
            panel.Children.Add(toolbar);
            _scriptEditor = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 14, 0, 12),
                Padding = new Thickness(12)
            };
            _scriptEditor.SetResourceReference(Control.StyleProperty, "InputStyle");
            Grid.SetRow(_scriptEditor, 1);
            panel.Children.Add(_scriptEditor);
            _scriptOutput = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10)
            };
            _scriptOutput.SetResourceReference(Control.BackgroundProperty, "LogBackgroundBrush");
            _scriptOutput.SetResourceReference(Control.ForegroundProperty, "LogTextBrush");
            Grid.SetRow(_scriptOutput, 2);
            panel.Children.Add(_scriptOutput);
            ApplyScriptPreset();
            return panel;
        }

        private UIElement BuildScrcpyTab()
        {
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(Ui.Text("设备镜像", 15, "TextPrimaryBrush", FontWeights.SemiBold));
            var note = Ui.Text("scrcpy 是可选外部组件。指定 scrcpy.exe 后，可对当前设备启动低延迟镜像和控制。", 12, "TextSecondaryBrush", FontWeights.Normal);
            note.Margin = new Thickness(0, 8, 0, 18);
            panel.Children.Add(note);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _scrcpyPath = Ui.Input(_state.Settings.ScrcpyPath, 470);
            var browse = Ui.Button("选择 scrcpy", false);
            browse.Click += delegate
            {
                var dialog = new Forms.OpenFileDialog { Filter = "scrcpy 可执行文件 (scrcpy.exe)|scrcpy.exe|可执行文件 (*.exe)|*.exe" };
                if (dialog.ShowDialog() == Forms.DialogResult.OK) _scrcpyPath.Text = dialog.FileName;
            };
            row.Children.Add(_scrcpyPath);
            row.Children.Add(browse);
            panel.Children.Add(row);
            var launch = Ui.Button("启动设备镜像", true);
            launch.Width = 160;
            launch.Margin = new Thickness(0, 18, 0, 0);
            launch.Click += delegate { LaunchScrcpy(); };
            panel.Children.Add(launch);
            return panel;
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

        private async void StartPerformance()
        {
            if (RequireDevice("性能监控") == null) return;
            if (_performanceTimer == null)
            {
                _performanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _performanceTimer.Tick += async delegate { await RefreshPerformanceAsync(); };
            }
            _performanceTimer.Start();
            await RefreshPerformanceAsync();
            _state.SetStatus("性能监控已启动", false);
        }

        private void StopPerformance()
        {
            if (_performanceTimer != null) _performanceTimer.Stop();
            _state.SetStatus("性能监控已停止", false);
        }

        private async Task RefreshPerformanceAsync()
        {
            if (_performanceBusy) return;
            DeviceInfo device = _state.SelectedDevice;
            if (device == null) return;
            _performanceBusy = true;
            try
            {
                string package = _performancePackage.Text.Trim();
                string command = "shell \"echo '[CPU]'; dumpsys cpuinfo | head -n 16; echo; echo '[MEMORY]'; " +
                    ((!string.IsNullOrWhiteSpace(package) && package != "可选包名") ? "dumpsys meminfo " + package + " | head -n 30" : "cat /proc/meminfo | head -n 8") +
                    "; echo; echo '[LOAD]'; cat /proc/loadavg\"";
                CommandResult result = await _state.Adb.RunAsync(command, device.Serial, 12000);
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
            var root = new Grid { Margin = Ui.PagePadding };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var heading = new StackPanel();
            Ui.Heading("操作记录", "查看连接、安装、传输和诊断任务的执行结果与原始输出。", heading);
            root.Children.Add(heading);
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            var export = Ui.Button("导出记录", true);
            export.Click += delegate { Export(); };
            var clear = Ui.Button("清空记录", false);
            clear.Click += delegate
            {
                if (MessageBox.Show("确定清空全部操作记录？", "清空记录", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) _state.ClearHistory();
            };
            toolbar.Children.Add(export);
            toolbar.Children.Add(clear);
            var toolPanel = Ui.Panel(toolbar, new Thickness(0, 0, 0, 12));
            Grid.SetRow(toolPanel, 1);
            root.Children.Add(toolPanel);
            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            _list = new ListView { ItemsSource = _state.Operations };
            _list.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            _list.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            _list.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = "时间", Width = 130, DisplayMemberBinding = new Binding("TimeText") });
            view.Columns.Add(new GridViewColumn { Header = "操作", Width = 170, DisplayMemberBinding = new Binding("Action") });
            view.Columns.Add(new GridViewColumn { Header = "目标设备", Width = 250, DisplayMemberBinding = new Binding("Device") });
            view.Columns.Add(new GridViewColumn { Header = "结果", Width = 90, DisplayMemberBinding = new Binding("Status") });
            view.Columns.Add(new GridViewColumn { Header = "耗时", Width = 100, DisplayMemberBinding = new Binding("DurationText") });
            _list.View = view;
            _list.SelectionChanged += delegate
            {
                var item = _list.SelectedItem as OperationItem;
                if (item != null) _detail.Text = item.Detail;
            };
            body.Children.Add(_list);
            _detail = new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"), FontSize = 11, Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0)
            };
            _detail.SetResourceReference(Control.BackgroundProperty, "LogBackgroundBrush");
            _detail.SetResourceReference(Control.ForegroundProperty, "LogTextBrush");
            Grid.SetRow(_detail, 1);
            body.Children.Add(_detail);
            Grid.SetRow(body, 2);
            root.Children.Add(body);
            Content = root;
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
            var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var content = new StackPanel { Margin = Ui.PagePadding, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            Ui.Heading("设置", "配置 ADB、scrcpy、默认目录、日志容量和全局主题。", content);

            var form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            for (int i = 0; i < 5; i++) form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

            _adbPath = AddPathRow(form, 0, "ADB 可执行文件", _state.Settings.AdbPath, "选择 ADB", "adb.exe");
            _scrcpyPath = AddPathRow(form, 1, "scrcpy 可执行文件", _state.Settings.ScrcpyPath, "选择 scrcpy", "scrcpy.exe");
            _defaultFolder = AddFolderRow(form, 2, "默认保存目录", _state.Settings.DefaultFolder);
            AddLabel(form, 3, "日志缓冲行数");
            _logBuffer = Ui.Input(_state.Settings.LogBuffer.ToString(), 180);
            Grid.SetRow(_logBuffer, 3); Grid.SetColumn(_logBuffer, 1); form.Children.Add(_logBuffer);
            AddLabel(form, 4, "应用主题");
            _theme = Ui.Combo(180);
            _theme.Items.Add("Light"); _theme.Items.Add("Dark");
            _theme.SelectedItem = _state.Settings.Theme;
            Grid.SetRow(_theme, 4); Grid.SetColumn(_theme, 1); form.Children.Add(_theme);
            content.Children.Add(Ui.Panel(form, new Thickness(0, 0, 0, 16)));

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var save = Ui.Button("保存设置", true);
            save.Click += async delegate { await SaveAsync(); };
            var detect = Ui.Button("重新检测 ADB", false);
            detect.Click += async delegate
            {
                _state.Adb.Detect();
                _updateHeader();
                _status.Text = _state.Adb.IsAvailable ? "检测到：" + _state.Adb.ExecutablePath : "未找到 adb.exe";
                if (_state.Adb.IsAvailable) await _state.RefreshDevicesAsync();
            };
            var open = Ui.Button("打开数据目录", false);
            open.Click += delegate { Process.Start("explorer.exe", AdbClient.Quote(_state.Settings.DataDirectory)); };
            actions.Children.Add(save); actions.Children.Add(detect); actions.Children.Add(open);
            content.Children.Add(actions);
            _status = Ui.Text(_state.Adb.IsAvailable ? "当前 ADB：" + _state.Adb.ExecutablePath : "未找到 ADB，请指定 adb.exe。", 12, "TextSecondaryBrush", FontWeights.Normal);
            _status.Margin = new Thickness(0, 16, 0, 0);
            content.Children.Add(_status);
            root.Content = content;
            Content = root;
        }

        private TextBox AddPathRow(Grid grid, int row, string label, string value, string buttonText, string expectedName)
        {
            AddLabel(grid, row, label);
            var input = Ui.Input(value, 520);
            input.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(input, row); Grid.SetColumn(input, 1); grid.Children.Add(input);
            var button = Ui.Button(buttonText, false);
            Grid.SetRow(button, row); Grid.SetColumn(button, 2);
            button.Click += delegate
            {
                var dialog = new Forms.OpenFileDialog { Filter = expectedName + "|" + expectedName + "|可执行文件 (*.exe)|*.exe" };
                if (dialog.ShowDialog() == Forms.DialogResult.OK) input.Text = dialog.FileName;
            };
            grid.Children.Add(button);
            return input;
        }

        private TextBox AddFolderRow(Grid grid, int row, string label, string value)
        {
            AddLabel(grid, row, label);
            var input = Ui.Input(value, 520);
            input.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(input, row); Grid.SetColumn(input, 1); grid.Children.Add(input);
            var button = Ui.Button("选择目录", false);
            Grid.SetRow(button, row); Grid.SetColumn(button, 2);
            button.Click += delegate
            {
                var dialog = new Forms.FolderBrowserDialog { SelectedPath = Directory.Exists(input.Text) ? input.Text : _state.Settings.DefaultFolder };
                if (dialog.ShowDialog() == Forms.DialogResult.OK) input.Text = dialog.SelectedPath;
            };
            grid.Children.Add(button);
            return input;
        }

        private static void AddLabel(Grid grid, int row, string value)
        {
            var label = Ui.Text(value, 13, "TextPrimaryBrush", FontWeights.Normal);
            Grid.SetRow(label, row); Grid.SetColumn(label, 0); grid.Children.Add(label);
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
