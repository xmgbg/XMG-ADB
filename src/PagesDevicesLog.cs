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

namespace E300DeviceConsole
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
            var root = new Grid { Margin = Ui.PagePadding };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var heading = new StackPanel();
            Ui.Heading("设备连接", "管理 USB 与网络设备，当前选择会同步到所有功能页面。", heading);
            root.Children.Add(heading);

            var connectGrid = new Grid();
            connectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            connectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fields = new StackPanel { Orientation = Orientation.Horizontal };
            _address = Ui.Combo(260);
            _address.IsEditable = true;
            foreach (string recent in _state.Settings.RecentDevices) _address.Items.Add(recent);
            if (_address.Items.Count > 0) _address.SelectedIndex = 0;
            else _address.Text = "192.168.1.100";
            _port = Ui.Input("5555", 86);
            var connect = Ui.Button("连接", true);
            connect.Click += async delegate { await ConnectAsync(); };
            fields.Children.Add(_address);
            fields.Children.Add(_port);
            fields.Children.Add(connect);
            connectGrid.Children.Add(fields);
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            var refresh = Ui.Button("刷新设备", false);
            refresh.Click += async delegate { await RefreshAsync(); };
            var restart = Ui.Button("重启 ADB", false);
            restart.Margin = new Thickness(0);
            restart.Click += async delegate { await RestartAdbAsync(); };
            tools.Children.Add(refresh);
            tools.Children.Add(restart);
            Grid.SetColumn(tools, 1);
            connectGrid.Children.Add(tools);
            var connectPanel = Ui.Panel(connectGrid, new Thickness(0, 0, 0, 12));
            Grid.SetRow(connectPanel, 1);
            root.Children.Add(connectPanel);

            var pairGrid = new Grid();
            pairGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pairGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var pairFields = new StackPanel { Orientation = Orientation.Horizontal };
            pairFields.Children.Add(Ui.Text("无线配对", 13, "TextPrimaryBrush", FontWeights.SemiBold));
            pairFields.Children[pairFields.Children.Count - 1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            _pairAddress = Ui.Input("192.168.1.100:37000", 220);
            _pairCode = Ui.Input("六位配对码", 120);
            var pair = Ui.Button("配对", false);
            pair.Click += async delegate { await PairAsync(); };
            pairFields.Children.Add(_pairAddress);
            pairFields.Children.Add(_pairCode);
            pairFields.Children.Add(pair);
            pairGrid.Children.Add(pairFields);
            var qrHelp = Ui.Button("QR 配对说明", false);
            qrHelp.Margin = new Thickness(0);
            qrHelp.Click += delegate
            {
                MessageBox.Show("ADB 命令行支持配对码流程。设备端选择“使用配对码配对”，把配对地址和六位代码填入本页。\n\nAndroid Studio 的 QR 模式依赖 mDNS 配对服务，不是静态二维码。此工具会优先提供兼容性更高的配对码流程。", "无线调试配对", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            Grid.SetColumn(qrHelp, 1);
            pairGrid.Children.Add(qrHelp);
            var pairPanel = Ui.Panel(pairGrid, new Thickness(0, 0, 0, 12));
            Grid.SetRow(pairPanel, 2);
            root.Children.Add(pairPanel);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            _list = new ListView { ItemsSource = _state.Devices, SelectionMode = SelectionMode.Extended, Margin = new Thickness(0, 0, 12, 0) };
            _list.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            _list.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            _list.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = "设备", Width = 190, DisplayMemberBinding = new Binding("Model") });
            view.Columns.Add(new GridViewColumn { Header = "地址 / 序列号", Width = 230, DisplayMemberBinding = new Binding("Serial") });
            view.Columns.Add(new GridViewColumn { Header = "状态", Width = 90, DisplayMemberBinding = new Binding("StateText") });
            view.Columns.Add(new GridViewColumn { Header = "连接方式", Width = 90, DisplayMemberBinding = new Binding("Address") });
            _list.View = view;
            _list.SelectionChanged += async delegate
            {
                var selected = _list.SelectedItem as DeviceInfo;
                if (selected == null) return;
                _state.SelectedDevice = selected;
                await LoadDetailsAsync(selected);
            };
            body.Children.Add(_list);

            var detailStack = new StackPanel();
            detailStack.Children.Add(Ui.Text("设备详情", 16, "TextPrimaryBrush", FontWeights.SemiBold));
            _details = Ui.Text("选择一台设备查看详细信息。", 12, "TextSecondaryBrush", FontWeights.Normal);
            _details.Margin = new Thickness(0, 14, 0, 18);
            _details.FontFamily = new FontFamily("Consolas");
            detailStack.Children.Add(_details);
            var disconnect = Ui.Button("断开当前设备", false);
            disconnect.Click += async delegate { await DisconnectAsync(); };
            var copy = Ui.Button("复制序列号", false);
            copy.Margin = new Thickness(0, 8, 0, 0);
            copy.Click += delegate
            {
                if (_state.SelectedDevice != null) Clipboard.SetText(_state.SelectedDevice.Serial);
            };
            var batch = Ui.Button("断开所选网络设备", false);
            batch.Margin = new Thickness(0, 8, 0, 0);
            batch.Click += async delegate { await BatchDisconnectAsync(); };
            detailStack.Children.Add(disconnect);
            detailStack.Children.Add(copy);
            detailStack.Children.Add(batch);
            var detailPanel = Ui.Panel(detailStack, new Thickness(0));
            Grid.SetColumn(detailPanel, 1);
            body.Children.Add(detailPanel);
            Grid.SetRow(body, 3);
            root.Children.Add(body);
            Content = root;
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
            var root = new Grid { Margin = Ui.PagePadding };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            var heading = new StackPanel();
            Ui.Heading("实时日志", "使用 threadtime 格式显示日志，支持级别、Tag、进程和文本过滤。", heading);
            root.Children.Add(heading);

            var toolbar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            _level = Ui.Combo(110);
            foreach (string item in new[] { "Verbose", "Debug", "Info", "Warn", "Error", "Fatal", "Silent" }) _level.Items.Add(item);
            _level.SelectedIndex = 1;
            _tag = Ui.Input("Tag", 140);
            _processFilter = Ui.Input("PID / 包名", 140);
            _search = Ui.Input("搜索文本", 180);
            var start = Ui.Button("开始", true);
            start.Click += async delegate { await StartAsync(); };
            var pause = Ui.Button("暂停 / 继续", false);
            pause.Click += delegate { _paused = !_paused; _state.SetStatus(_paused ? "日志显示已暂停" : "日志显示已继续", false); };
            var clear = Ui.Button("清空", false);
            clear.Click += async delegate { await ClearAsync(); };
            var export = Ui.Button("导出", false);
            export.Click += async delegate { await ExportAsync(false); };
            var cache = Ui.Button("导出设备缓存", false);
            cache.Click += async delegate { await ExportAsync(true); };
            toolbar.Children.Add(_level);
            toolbar.Children.Add(_tag);
            toolbar.Children.Add(_processFilter);
            toolbar.Children.Add(_search);
            toolbar.Children.Add(start);
            toolbar.Children.Add(pause);
            toolbar.Children.Add(clear);
            toolbar.Children.Add(export);
            toolbar.Children.Add(cache);
            var toolPanel = Ui.Panel(toolbar, new Thickness(0, 0, 0, 12));
            Grid.SetRow(toolPanel, 1);
            root.Children.Add(toolPanel);

            _log = new RichTextBox
            {
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                IsUndoEnabled = false,
                Document = new FlowDocument { PagePadding = new Thickness(0), LineHeight = 17 }
            };
            _log.SetResourceReference(Control.BackgroundProperty, "LogBackgroundBrush");
            _log.SetResourceReference(Control.ForegroundProperty, "LogTextBrush");
            _log.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            Grid.SetRow(_log, 2);
            root.Children.Add(_log);

            _stats = Ui.Text("未启动", 11, "TextSecondaryBrush", FontWeights.Normal);
            _stats.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(_stats, 3);
            root.Children.Add(_stats);
            Content = root;

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
