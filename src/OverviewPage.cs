using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace XMG_ADB
{
    public sealed class OverviewPage : UserControl, IDisposable
    {
        private readonly AppState _state = AppState.Current;
        private readonly FrameworkElement _view;
        private CancellationTokenSource _request;
        private bool _disposed;

        public OverviewPage(Action<string> navigate)
        {
            _view = Ui.LoadView("OverviewPage");
            Content = _view;
            Ui.Find<Button>(_view, "Connect").Click += delegate { navigate("设备"); };
            Ui.Find<Button>(_view, "Install").Click += delegate { navigate("APK 安装"); };
            Ui.Find<Button>(_view, "Transfer").Click += delegate { navigate("文件传输"); };
            Ui.Find<Button>(_view, "Capture").Click += delegate { navigate("工具与诊断"); };
            Ui.Find<Button>(_view, "Mirror").Click += delegate
            {
                navigate("工具与诊断");
                ((ToolsPage)((MainWindow)Application.Current.MainWindow).CurrentPage).SelectTab(4);
            };
            Ui.Find<Button>(_view, "Diagnostics").Click += delegate { navigate("工具与诊断"); };
            Ui.Find<Button>(_view, "Refresh").Click += async delegate
            {
                var button = Ui.Find<Button>(_view, "Refresh");
                button.IsEnabled = false;
                try { await _state.RefreshDevicesAsync(); await LoadDeviceAsync(); }
                catch (Exception ex) { SetText("ReadStatus", ex.Message); }
                finally { button.IsEnabled = true; }
            };
            Loaded += async delegate { await LoadDeviceAsync(); };
            Unloaded += delegate { if (_request != null) _request.Cancel(); };
            _state.SelectedDeviceChanged += DeviceChanged;
            _state.Operations.CollectionChanged += HistoryChanged;
            UpdateHistory();
        }

        private async void DeviceChanged(object sender, EventArgs args)
        {
            if (IsLoaded) await LoadDeviceAsync();
        }

        private async Task LoadDeviceAsync()
        {
            if (_disposed) return;
            if (_request != null) _request.Cancel();
            var request = new CancellationTokenSource();
            _request = request;
            var device = _state.SelectedDevice;
            foreach (string name in new[] { "Android", "Battery", "Storage", "Temperature" }) SetText(name, "暂无数据");
            SetText("DeviceName", device == null ? "尚未连接设备" : (string.IsNullOrWhiteSpace(device.Model) ? "Android 设备" : device.Model.Replace('_', ' ')));
            SetText("DeviceState", device == null ? "使用 USB 连接设备，或前往设备连接进行无线配对。" : device.Serial + "  ·  " + (device.Serial.Contains(":") ? "网络" : "USB") + "  ·  " + device.StateText);
            SetText("ReadStatus", device == null ? "连接后将显示设备信息。" : device.State == "unauthorized" ? "请在设备上确认 USB 调试授权，然后刷新。" : "正在读取设备信息…");
            try
            {
                if (device == null || device.State != "device")
                {
                    if (device != null && device.State != "unauthorized") SetText("ReadStatus", "设备不可用，请检查连接后刷新。");
                    return;
                }
                var version = await _state.Adb.RunAsync("shell getprop ro.build.version.release", device.Serial, 5000, request.Token);
                var battery = await _state.Adb.RunAsync("shell dumpsys battery", device.Serial, 5000, request.Token);
                var storage = await _state.Adb.RunAsync("shell df -h /data", device.Serial, 5000, request.Token);
                if (request.IsCancellationRequested || _disposed || _state.SelectedDevice != device) return;
                SetText("Android", version.Success && !string.IsNullOrWhiteSpace(version.Output) ? version.Output.Trim() : "暂不可用");
                var level = Regex.Match(battery.Output ?? "", @"(?m)^\s*level:\s*(\d+)");
                var scale = Regex.Match(battery.Output ?? "", @"(?m)^\s*scale:\s*(\d+)");
                double denominator;
                SetText("Battery", battery.Success && level.Success && scale.Success && double.TryParse(scale.Groups[1].Value, out denominator) && denominator > 0 ? (100 * double.Parse(level.Groups[1].Value) / denominator).ToString("0") + "%" : "暂不可用");
                var temperature = Regex.Match(battery.Output ?? "", @"(?m)^\s*temperature:\s*(-?\d+)");
                SetText("Temperature", battery.Success && temperature.Success ? (double.Parse(temperature.Groups[1].Value, CultureInfo.InvariantCulture) / 10).ToString("0.#") + " °C" : "暂不可用");
                var lines = (storage.Output ?? "").Trim().Split('\n');
                var columns = Regex.Split(lines.Last().Trim(), @"\s+");
                SetText("Storage", storage.Success && lines.Length > 1 && columns.Length >= 6 ? columns[3] : "暂不可用");
                SetText("ReadStatus", version.Success && battery.Success && storage.Success ? "设备信息已更新" : "部分信息读取失败或设备不支持，可刷新重试。");
            }
            catch (Exception ex)
            {
                if (!request.IsCancellationRequested && !_disposed) SetText("ReadStatus", "读取失败：" + ex.Message);
            }
            finally
            {
                if (_request == request) _request = null;
                request.Dispose();
            }
        }

        private void SetText(string name, string text) { Ui.Find<TextBlock>(_view, name).Text = text; }
        private void HistoryChanged(object sender, NotifyCollectionChangedEventArgs args) { UpdateHistory(); }
        private void UpdateHistory()
        {
            Ui.Find<ListView>(_view, "History").ItemsSource = _state.Operations.Take(5).ToArray();
            Ui.Find<TextBlock>(_view, "EmptyHistory").Visibility = _state.Operations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        public void Dispose()
        {
            _disposed = true;
            if (_request != null) _request.Cancel();
            _state.SelectedDeviceChanged -= DeviceChanged;
            _state.Operations.CollectionChanged -= HistoryChanged;
        }
    }
}
