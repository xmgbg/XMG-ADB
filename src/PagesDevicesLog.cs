using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace XMG_ADB
{
    public sealed class DevicesPage : UserControl
    {
        private readonly AppState _state = AppState.Current;
        private readonly ComboBox _address;
        private readonly TextBox _port;
        private readonly ListView _list;
        private readonly TextBlock _details;
        private readonly TextBox _pairAddress;
        private readonly TextBox _pairCode;

        public DevicesPage()
        {
            var root = Ui.LoadView("DevicesPage");
            Content = root;
            _address = Ui.Find<ComboBox>(root, "Address");
            _port = Ui.Find<TextBox>(root, "Port");
            _pairAddress = Ui.Find<TextBox>(root, "PairAddress");
            _pairCode = Ui.Find<TextBox>(root, "PairCode");
            _list = Ui.Find<ListView>(root, "Devices");
            _details = Ui.Find<TextBlock>(root, "Details");
            foreach (string recent in _state.Settings.RecentDevices) _address.Items.Add(recent);
            if (_address.Items.Count > 0) _address.SelectedIndex = 0;
            else _address.Text = "192.168.1.100";
            _list.ItemsSource = _state.Devices;
            _list.SelectionChanged += async delegate
            {
                var selected = _list.SelectedItem as DeviceInfo;
                if (selected == null) return;
                _state.SelectedDevice = selected;
                await LoadDetailsAsync(selected);
            };
            Ui.Find<Button>(root, "Connect").Click += async delegate { await ConnectAsync(); };
            Ui.Find<Button>(root, "Refresh").Click += async delegate { await RefreshAsync(); };
            Ui.Find<Button>(root, "Restart").Click += async delegate { await RestartAdbAsync(); };
            Ui.Find<Button>(root, "Pair").Click += async delegate { await PairAsync(); };
            Ui.Find<Button>(root, "Help").Click += delegate { MessageBox.Show("在 Android 无线调试设置中选择使用配对码配对，填入设备显示的配对地址和六位配对码。配对后使用连接端口连接设备。", "无线配对"); };
            Ui.Find<Button>(root, "Disconnect").Click += async delegate { await DisconnectAsync(); };
            Ui.Find<Button>(root, "Batch").Click += async delegate { await BatchDisconnectAsync(); };
            Ui.Find<Button>(root, "Copy").Click += delegate { if (_state.SelectedDevice != null) Clipboard.SetText(_state.SelectedDevice.Serial); };
        }

        private async Task ConnectAsync()
        {
            string host = _address.Text.Trim();
            int port;
            if (string.IsNullOrWhiteSpace(host) || (!host.Contains(":") && (!int.TryParse(_port.Text, out port) || port < 1 || port > 65535)))
            {
                MessageBox.Show("请输入有效的设备地址和端口。", "连接设备", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string target = host.Contains(":") ? host : host + ":" + _port.Text.Trim();
            CommandResult result = await _state.RunTrackedAsync("连接设备", "connect " + AdbClient.Quote(target), null, 15000);
            if (result.Success)
            {
                _state.Settings.RememberDevice(target);
                if (!_address.Items.Contains(target)) _address.Items.Insert(0, target);
                await _state.RefreshDevicesAsync();
            }
            Ui.ShowResult("连接设备", result);
        }

        private async Task PairAsync()
        {
            string address = _pairAddress.Text.Trim();
            string code = _pairCode.Text.Trim();
            if (!address.Contains(":") || code.Length < 6)
            {
                MessageBox.Show("请输入设备显示的配对地址和六位配对码。", "无线配对", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CommandResult result = await _state.RunTrackedAsync("无线配对", "pair " + AdbClient.Quote(address) + " " + AdbClient.Quote(code), null, 30000);
            Ui.ShowResult("无线配对", result);
        }

        private async Task RefreshAsync()
        {
            await _state.RefreshDevicesAsync();
            if (_state.Devices.Count == 0)
                MessageBox.Show("当前没有检测到设备。请检查数据线、网络、调试授权或 ADB 环境。", "刷新设备", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task RestartAdbAsync()
        {
            await _state.RunTrackedAsync("停止 ADB Server", "kill-server", null, 10000);
            CommandResult result = await _state.RunTrackedAsync("启动 ADB Server", "start-server", null, 15000);
            await _state.RefreshDevicesAsync();
            Ui.ShowResult("重启 ADB Server", result);
        }

        private async Task DisconnectAsync()
        {
            DeviceInfo device = _state.SelectedDevice;
            if (device == null) return;
            if (!device.Serial.Contains(":"))
            {
                MessageBox.Show("USB 设备不能通过 adb disconnect 断开，请拔出数据线或撤销调试授权。", "断开设备", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            CommandResult result = await _state.RunTrackedAsync("断开设备", "disconnect " + AdbClient.Quote(device.Serial), null, 10000);
            await _state.RefreshDevicesAsync();
            Ui.ShowResult("断开设备", result);
        }

        private async Task BatchDisconnectAsync()
        {
            var devices = _list.SelectedItems.Cast<DeviceInfo>().Where(delegate(DeviceInfo d) { return d.Serial.Contains(":"); }).ToList();
            if (devices.Count == 0)
            {
                MessageBox.Show("请在设备列表中选择至少一个网络设备。", "批量断开", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            foreach (DeviceInfo device in devices)
                await _state.RunTrackedAsync("批量断开", "disconnect " + AdbClient.Quote(device.Serial), null, 10000);
            await _state.RefreshDevicesAsync();
        }

        private async Task LoadDetailsAsync(DeviceInfo device)
        {
            if (device.State != "device")
            {
                _details.Text = device.Display + "\n\n" + (device.State == "unauthorized" ? "请在设备屏幕上确认调试授权。" : "设备当前不可执行 ADB 命令。");
                return;
            }
            CommandResult result = await _state.Adb.RunAsync("shell \"echo Android: $(getprop ro.build.version.release); echo SDK: $(getprop ro.build.version.sdk); echo Brand: $(getprop ro.product.brand); echo Model: $(getprop ro.product.model); dumpsys battery | grep -E 'level|status|temperature'; df -h /data | tail -n 1\"", device.Serial, 12000);
            _details.Text = result.Success ? result.Output.Trim() : AppState.Explain(result);
        }
    }

    public sealed class LogcatPage : UserControl, IDisposable
    {
        private readonly AppState _state = AppState.Current;
        private readonly ComboBox _level;
        private readonly TextBox _tag;
        private readonly TextBox _search;
        private readonly TextBox _processFilter;
        private readonly RichTextBox _log;
        private readonly TextBlock _stats;
        private readonly ConcurrentQueue<Tuple<string, bool>> _queue = new ConcurrentQueue<Tuple<string, bool>>();
        private readonly DispatcherTimer _flushTimer;
        private Process _process;
        private bool _paused;
        private int _lineCount;
        private DateTime _started;

        public LogcatPage()
        {
            var root = Ui.LoadView("LogcatPage");
            Content = root;
            _level = Ui.Find<ComboBox>(root, "Level");
            _tag = Ui.Find<TextBox>(root, "Tag");
            _processFilter = Ui.Find<TextBox>(root, "Process");
            _search = Ui.Find<TextBox>(root, "Search");
            _log = Ui.Find<RichTextBox>(root, "Log");
            _stats = Ui.Find<TextBlock>(root, "Stats");
            foreach (string item in new[] { "Verbose", "Debug", "Info", "Warn", "Error", "Fatal", "Silent" }) _level.Items.Add(item);
            _level.SelectedIndex = 1;
            Ui.Find<Button>(root, "Start").Click += async delegate { await StartAsync(); };
            Ui.Find<Button>(root, "Pause").Click += delegate { _paused = !_paused; _state.SetStatus(_paused ? "日志显示已暂停" : "日志显示已继续", false); };
            Ui.Find<Button>(root, "Clear").Click += async delegate { await ClearAsync(); };
            Ui.Find<Button>(root, "Export").Click += async delegate { await ExportAsync(false); };
            Ui.Find<Button>(root, "Cache").Click += async delegate { await ExportAsync(true); };
            _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _flushTimer.Tick += FlushLines;
            _flushTimer.Start();
        }

        private async Task StartAsync()
        {
            StopProcess();
            DeviceInfo device = _state.SelectedDevice;
            if (device == null || device.State != "device")
            {
                MessageBox.Show("请先选择一台已连接设备。", "实时日志", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string[] codes = { "V", "D", "I", "W", "E", "F", "S" };
            string code = codes[Math.Max(0, _level.SelectedIndex)];
            string tag = _tag.Text.Trim();
            string filter = string.IsNullOrWhiteSpace(tag) || tag == "Tag" ? "*:" + code : tag + ":" + code + " *:S";
            string process = _processFilter.Text.Trim();
            if (!string.IsNullOrWhiteSpace(process) && process != "PID / 包名" && !process.All(char.IsDigit))
            {
                CommandResult pid = await _state.Adb.RunAsync("shell pidof " + AdbClient.ShellQuote(process), device.Serial, 6000);
                if (pid.Success && !string.IsNullOrWhiteSpace(pid.Output)) _processFilter.Text = pid.Output.Trim().Split(' ')[0];
            }
            try
            {
                _process = _state.Adb.StartStreaming("logcat -v threadtime " + filter, device.Serial, delegate(string line, bool error)
                {
                    _queue.Enqueue(Tuple.Create(line, error));
                });
                _started = DateTime.Now;
                _paused = false;
                _state.Record("启动日志", device.Serial, "运行中", filter, 0);
                _state.SetStatus("正在读取 " + device.Serial + " 的实时日志", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "实时日志", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ClearAsync()
        {
            _log.Document.Blocks.Clear();
            _lineCount = 0;
            DeviceInfo device = _state.SelectedDevice;
            if (device != null)
                await _state.RunTrackedAsync("清空日志缓存", "logcat -c", device.Serial, 8000);
        }

        private async Task ExportAsync(bool deviceCache)
        {
            var dialog = new Forms.SaveFileDialog
            {
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt",
                FileName = "logcat-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log",
                InitialDirectory = _state.Settings.DefaultFolder
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            if (deviceCache)
            {
                DeviceInfo device = _state.SelectedDevice;
                if (device == null) return;
                CommandResult result = await _state.RunTrackedAsync("导出日志缓存", "logcat -d -v threadtime", device.Serial, 60000);
                if (result.Success) File.WriteAllText(dialog.FileName, result.Output, new UTF8Encoding(false));
                Ui.ShowResult("导出日志缓存", result);
            }
            else
            {
                string text = new TextRange(_log.Document.ContentStart, _log.Document.ContentEnd).Text;
                File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false));
                _state.Record("导出当前日志", _state.SelectedDevice == null ? null : _state.SelectedDevice.Serial, "成功", dialog.FileName, 0);
            }
        }

        private void FlushLines(object sender, EventArgs args)
        {
            if (_paused) return;
            int batch = 0;
            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 17 };
            Tuple<string, bool> item;
            string search = _search.Text.Trim();
            string process = _processFilter.Text.Trim();
            while (batch < 400 && _queue.TryDequeue(out item))
            {
                string line = item.Item1;
                if (!string.IsNullOrWhiteSpace(search) && search != "搜索文本" && line.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrWhiteSpace(process) && process != "PID / 包名" && process.All(char.IsDigit))
                {
                    string padded = " " + process.PadLeft(5) + " ";
                    if (!line.Contains(padded)) continue;
                }
                var run = new Run(line + Environment.NewLine);
                if (item.Item2 || line.Contains(" E/")) run.Foreground = (Brush)FindResource("DangerBrush");
                else if (line.Contains(" W/")) run.Foreground = (Brush)FindResource("WarningBrush");
                else if (line.Contains(" I/")) run.Foreground = (Brush)FindResource("SuccessBrush");
                else run.SetResourceReference(TextElement.ForegroundProperty, "LogTextBrush");
                paragraph.Inlines.Add(run);
                batch++;
            }
            if (batch == 0) return;
            paragraph.Tag = batch;
            _log.Document.Blocks.Add(paragraph);
            _lineCount += batch;
            int limit = _state.Settings.LogBuffer;
            while (_lineCount > limit && _log.Document.Blocks.FirstBlock != null)
            {
                Block first = _log.Document.Blocks.FirstBlock;
                int count = first.Tag is int ? (int)first.Tag : 1;
                _log.Document.Blocks.Remove(first);
                _lineCount -= count;
            }
            _log.ScrollToEnd();
            _stats.Text = string.Format("{0:N0} 行  ·  已运行 {1}", _lineCount, DateTime.Now - _started);
        }

        private void StopProcess()
        {
            if (_process == null) return;
            try { if (!_process.HasExited) _process.Kill(); } catch { }
            try { _process.Dispose(); } catch { }
            _process = null;
        }

        public void Dispose()
        {
            _flushTimer.Stop();
            StopProcess();
        }
    }
}
