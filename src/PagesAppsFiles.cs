using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace XMG_ADB
{
    public sealed class ApkPage : UserControl
    {
        private readonly AppState _state = AppState.Current;
        private readonly ListBox _files;
        private readonly CheckBox _replace;
        private readonly CheckBox _test;
        private readonly CheckBox _downgrade;
        private readonly CheckBox _grant;
        private readonly CheckBox _allDevices;
        private readonly TextBlock _result;
        private readonly ProgressBar _progress;

        public ApkPage()
        {
            var root = Ui.LoadView("ApkPage");
            Content = root;
            _files = Ui.Find<ListBox>(root, "Files");
            _replace = Ui.Find<CheckBox>(root, "Replace");
            _test = Ui.Find<CheckBox>(root, "Test");
            _downgrade = Ui.Find<CheckBox>(root, "Downgrade");
            _grant = Ui.Find<CheckBox>(root, "Grant");
            _allDevices = Ui.Find<CheckBox>(root, "AllDevices");
            _result = Ui.Find<TextBlock>(root, "Result");
            _progress = Ui.Find<ProgressBar>(root, "Progress");
            Ui.Find<Button>(root, "Select").Click += delegate { SelectApks(); };
            Ui.Find<Button>(root, "Clear").Click += delegate { _files.Items.Clear(); };
            Ui.Find<Button>(root, "Install").Click += async delegate { await InstallAsync(); };
            root.Drop += OnDrop;
            root.DragOver += delegate(object sender, DragEventArgs args) { args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; args.Handled = true; };
        }

        private void SelectApks()
        {
            var dialog = new Forms.OpenFileDialog { Filter = "Android 安装包 (*.apk)|*.apk", Multiselect = true };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            AddFiles(dialog.FileNames);
        }

        private void OnDrop(object sender, DragEventArgs args)
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop)) return;
            AddFiles((string[])args.Data.GetData(DataFormats.FileDrop));
        }

        private void AddFiles(IEnumerable<string> files)
        {
            foreach (string file in files.Where(delegate(string p) { return string.Equals(Path.GetExtension(p), ".apk", StringComparison.OrdinalIgnoreCase); }))
                if (!_files.Items.Contains(file)) _files.Items.Add(file);
        }

        private async Task InstallAsync()
        {
            var files = _files.Items.Cast<string>().Where(File.Exists).ToList();
            if (files.Count == 0)
            {
                MessageBox.Show("请选择至少一个有效的 APK 文件。", "APK 安装", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var devices = _allDevices.IsChecked == true
                ? _state.Devices.Where(delegate(DeviceInfo d) { return d.State == "device"; }).ToList()
                : new List<DeviceInfo>(new[] { _state.SelectedDevice }.Where(delegate(DeviceInfo d) { return d != null && d.State == "device"; }));
            if (devices.Count == 0)
            {
                MessageBox.Show("请先选择一台已连接设备。", "APK 安装", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string flags = "";
            if (_replace.IsChecked == true) flags += " -r";
            if (_test.IsChecked == true) flags += " -t";
            if (_downgrade.IsChecked == true) flags += " -d";
            if (_grant.IsChecked == true) flags += " -g";
            string fileArgs = string.Join(" ", files.Select(AdbClient.Quote));
            string command = (files.Count > 1 ? "install-multiple" : "install") + flags + " " + fileArgs;
            _progress.Visibility = Visibility.Visible;
            _result.Text = string.Format("正在向 {0} 台设备安装 {1} 个文件...", devices.Count, files.Count);
            var results = new List<string>();
            foreach (DeviceInfo device in devices)
            {
                CommandResult result = await _state.RunTrackedAsync(files.Count > 1 ? "安装分包 APK" : "安装 APK", command, device.Serial, 10 * 60 * 1000);
                results.Add(device.Serial + Environment.NewLine + AppState.Explain(result));
            }
            _progress.Visibility = Visibility.Collapsed;
            _result.Text = string.Join(Environment.NewLine + Environment.NewLine, results);
        }
    }

    public sealed class FilesPage : UserControl
    {
        private readonly AppState _state = AppState.Current;
        private readonly TextBox _localPath;
        private readonly TextBox _remotePath;
        private readonly ListBox _remoteList;
        private readonly TextBlock _result;
        private readonly ProgressBar _progress;

        public FilesPage()
        {
            var root = Ui.LoadView("FilesPage");
            Content = root;
            _localPath = Ui.Find<TextBox>(root, "LocalPath");
            _localPath.Text = _state.Settings.DefaultFolder;
            _remotePath = Ui.Find<TextBox>(root, "RemotePath");
            _remoteList = Ui.Find<ListBox>(root, "RemoteList");
            _result = Ui.Find<TextBlock>(root, "Result");
            _progress = Ui.Find<ProgressBar>(root, "Progress");
            Ui.Find<Button>(root, "ChooseFile").Click += delegate { ChooseLocalFile(); };
            Ui.Find<Button>(root, "ChooseFolder").Click += delegate { ChooseLocalFolder(); };
            Ui.Find<Button>(root, "Push").Click += async delegate { await PushAsync(); };
            Ui.Find<Button>(root, "Pull").Click += async delegate { await PullAsync(); };
            Ui.Find<Button>(root, "Verify").Click += async delegate { await VerifyAsync(); };
            Ui.Find<Button>(root, "Browse").Click += async delegate { await BrowseRemoteAsync(); };
            _remoteList.MouseDoubleClick += delegate { var item = _remoteList.SelectedItem as string; if (!string.IsNullOrWhiteSpace(item)) Clipboard.SetText(item); };
        }

        private void ChooseLocalFile()
        {
            var dialog = new Forms.OpenFileDialog { Filter = "所有文件 (*.*)|*.*" };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) _localPath.Text = dialog.FileName;
        }

        private void ChooseLocalFolder()
        {
            var dialog = new Forms.FolderBrowserDialog { SelectedPath = Directory.Exists(_localPath.Text) ? _localPath.Text : _state.Settings.DefaultFolder };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) _localPath.Text = dialog.SelectedPath;
        }

        private async Task PushAsync()
        {
            DeviceInfo device = RequireDevice();
            if (device == null) return;
            string local = _localPath.Text.Trim();
            string remote = _remotePath.Text.Trim();
            if ((!File.Exists(local) && !Directory.Exists(local)) || !remote.StartsWith("/"))
            {
                MessageBox.Show("请选择有效的本地路径，并输入设备端绝对路径。", "推送文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await TransferAsync("推送文件", "push " + AdbClient.Quote(local) + " " + AdbClient.Quote(remote), device.Serial);
            await BrowseRemoteAsync();
        }

        private async Task PullAsync()
        {
            DeviceInfo device = RequireDevice();
            if (device == null) return;
            string local = _localPath.Text.Trim();
            string remote = _remotePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(remote) || !remote.StartsWith("/"))
            {
                MessageBox.Show("请输入设备端绝对路径。", "拉取文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!Directory.Exists(local)) Directory.CreateDirectory(local);
            await TransferAsync("拉取文件", "pull " + AdbClient.Quote(remote) + " " + AdbClient.Quote(local), device.Serial);
        }

        private async Task TransferAsync(string action, string command, string serial)
        {
            _progress.Visibility = Visibility.Visible;
            _result.Text = action + "正在执行";
            CommandResult result = await _state.RunTrackedAsync(action, command, serial, 30 * 60 * 1000);
            _progress.Visibility = Visibility.Collapsed;
            _result.Text = AppState.Explain(result);
            if (!result.Success) Ui.ShowResult(action, result);
        }

        private async Task BrowseRemoteAsync()
        {
            DeviceInfo device = RequireDevice();
            if (device == null) return;
            string remote = _remotePath.Text.Trim();
            CommandResult result = await _state.RunTrackedAsync("浏览设备目录", "shell ls -la " + AdbClient.ShellQuote(remote), device.Serial, 15000);
            _remoteList.Items.Clear();
            if (result.Success)
            {
                foreach (string line in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) _remoteList.Items.Add(line);
            }
            else _remoteList.Items.Add(AppState.Explain(result));
        }

        private async Task VerifyAsync()
        {
            DeviceInfo device = RequireDevice();
            if (device == null) return;
            string local = _localPath.Text.Trim();
            string remote = _remotePath.Text.Trim();
            if (!File.Exists(local))
            {
                MessageBox.Show("校验功能需要选择一个本地文件。", "文件校验", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string localHash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(local)) localHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            CommandResult result = await _state.RunTrackedAsync("校验文件", "shell sha256sum " + AdbClient.ShellQuote(remote), device.Serial, 60000);
            string remoteHash = result.Success ? result.Output.Trim().Split(' ')[0].ToLowerInvariant() : "";
            bool same = localHash == remoteHash;
            _result.Text = same ? "SHA-256 校验一致" : "SHA-256 校验不一致或设备不支持 sha256sum\n本地: " + localHash + "\n设备: " + remoteHash;
            _state.Record("文件校验", device.Serial, same ? "成功" : "失败", _result.Text, 0);
        }

        private DeviceInfo RequireDevice()
        {
            DeviceInfo device = _state.SelectedDevice;
            if (device == null || device.State != "device")
            {
                MessageBox.Show("请先选择一台已连接设备。", "文件传输", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            return device;
        }
    }

    public sealed class PackagesPage : UserControl
    {
        private readonly AppState _state = AppState.Current;
        private readonly TextBox _search;
        private readonly ListBox _packages;
        private readonly CheckBox _systemApps;
        private readonly TextBlock _detail;

        public PackagesPage()
        {
            var root = Ui.LoadView("PackagesPage");
            Content = root;
            _search = Ui.Find<TextBox>(root, "Search");
            _packages = Ui.Find<ListBox>(root, "Packages");
            _systemApps = Ui.Find<CheckBox>(root, "SystemApps");
            _detail = Ui.Find<TextBlock>(root, "Detail");
            _search.TextChanged += delegate { ApplyFilter(); };
            _packages.SelectionChanged += async delegate { if (_packages.SelectedItem != null) await LoadPackageDetailAsync(); };
            Ui.Find<Button>(root, "Refresh").Click += async delegate { await RefreshAsync(); };
            Ui.Find<Button>(root, "Launch").Click += async delegate { await LaunchAsync(); };
            Ui.Find<Button>(root, "Stop").Click += async delegate { await ForceStopAsync(); };
            Ui.Find<Button>(root, "ClearData").Click += async delegate { await ClearDataAsync(); };
            Ui.Find<Button>(root, "Uninstall").Click += async delegate { await UninstallAsync(); };
        }

        private List<string> _allPackages = new List<string>();

        private async Task RefreshAsync()
        {
            DeviceInfo device = RequireDevice();
            if (device == null) return;
            CommandResult result = await _state.RunTrackedAsync("读取应用列表", "shell pm list packages " + (_systemApps.IsChecked == true ? "" : "-3"), device.Serial, 30000);
            if (!result.Success) { Ui.ShowResult("读取应用列表", result); return; }
            _allPackages = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(delegate(string line) { return line.Replace("package:", "").Trim(); })
                .Where(delegate(string line) { return line.Length > 0; }).OrderBy(delegate(string line) { return line; }).ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string search = _search.Text.Trim();
            _packages.Items.Clear();
            foreach (string package in _allPackages.Where(delegate(string item)
            {
                return search == "搜索包名" || string.IsNullOrWhiteSpace(search) || item.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            })) _packages.Items.Add(package);
        }

        private async Task LoadPackageDetailAsync()
        {
            DeviceInfo device = RequireDevice();
            string package = SelectedPackage();
            if (device == null || package == null) return;
            CommandResult result = await _state.Adb.RunAsync("shell dumpsys package " + AdbClient.ShellQuote(package) + " | grep -E 'versionName|versionCode|firstInstallTime|lastUpdateTime' | head -n 8", device.Serial, 15000);
            _detail.Text = package + Environment.NewLine + Environment.NewLine + (result.Success ? result.Output.Trim() : result.Combined);
        }

        private Task LaunchAsync() { return RunPackageCommand("启动应用", "shell monkey -p {0} -c android.intent.category.LAUNCHER 1", false); }
        private Task ForceStopAsync() { return RunPackageCommand("强制停止", "shell am force-stop {0}", false); }
        private Task ClearDataAsync() { return RunPackageCommand("清除数据", "shell pm clear {0}", true); }
        private Task UninstallAsync() { return RunPackageCommand("卸载应用", "uninstall {0}", true); }

        private async Task RunPackageCommand(string action, string format, bool confirm)
        {
            DeviceInfo device = RequireDevice();
            string package = SelectedPackage();
            if (device == null || package == null) return;
            if (confirm && MessageBox.Show("将在 " + device.Serial + " 上执行“" + action + "”：\n\n" + package + "\n\n此操作可能导致数据丢失，是否继续？", action, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            CommandResult result = await _state.RunTrackedAsync(action, string.Format(format, AdbClient.ShellQuote(package)), device.Serial, 60000);
            Ui.ShowResult(action, result);
            if (action == "卸载应用" && result.Success) await RefreshAsync();
        }

        private string SelectedPackage()
        {
            string package = _packages.SelectedItem as string;
            if (package == null) MessageBox.Show("请先选择一个应用包名。", "应用管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return package;
        }

        private DeviceInfo RequireDevice()
        {
            DeviceInfo device = _state.SelectedDevice;
            if (device == null || device.State != "device")
            {
                MessageBox.Show("请先选择一台已连接设备。", "应用管理", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            return device;
        }
    }
}
