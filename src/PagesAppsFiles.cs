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
            var root = new Grid { Margin = Ui.PagePadding, AllowDrop = true };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel();
            Ui.Heading("APK 安装", "支持拖放、覆盖安装、测试包、降级、权限授予、分包和多设备安装。", heading);
            root.Children.Add(heading);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            var left = new StackPanel();
            var selectRow = new StackPanel { Orientation = Orientation.Horizontal };
            var select = Ui.Button("选择 APK", true);
            select.Click += delegate { SelectApks(); };
            var clear = Ui.Button("清空列表", false);
            clear.Click += delegate { _files.Items.Clear(); };
            selectRow.Children.Add(select);
            selectRow.Children.Add(clear);
            left.Children.Add(selectRow);
            var dropHint = Ui.Text("可将一个或多个 .apk 文件拖入下方列表", 12, "TextSecondaryBrush", FontWeights.Normal);
            dropHint.Margin = new Thickness(0, 12, 0, 8);
            left.Children.Add(dropHint);
            _files = new ListBox { MinHeight = 280, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            _files.SetResourceReference(Control.BackgroundProperty, "SurfaceAltBrush");
            _files.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            _files.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            left.Children.Add(_files);
            var leftPanel = Ui.Panel(left, new Thickness(0, 0, 12, 0));
            body.Children.Add(leftPanel);

            var right = new StackPanel();
            right.Children.Add(Ui.Text("安装选项", 16, "TextPrimaryBrush", FontWeights.SemiBold));
            _replace = Ui.Check("覆盖安装 (-r)"); _replace.IsChecked = true; _replace.Margin = new Thickness(0, 18, 0, 12);
            _test = Ui.Check("允许测试包 (-t)"); _test.Margin = new Thickness(0, 0, 0, 12);
            _downgrade = Ui.Check("允许降级 (-d)"); _downgrade.Margin = new Thickness(0, 0, 0, 12);
            _grant = Ui.Check("授予运行时权限 (-g)"); _grant.Margin = new Thickness(0, 0, 0, 12);
            _allDevices = Ui.Check("安装到全部已连接设备"); _allDevices.Margin = new Thickness(0, 6, 0, 18);
            right.Children.Add(_replace);
            right.Children.Add(_test);
            right.Children.Add(_downgrade);
            right.Children.Add(_grant);
            right.Children.Add(_allDevices);
            var install = Ui.Button("开始安装", true);
            install.Width = 150;
            install.Click += async delegate { await InstallAsync(); };
            right.Children.Add(install);
            _progress = new ProgressBar { Height = 4, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 18, 0, 12) };
            _progress.SetResourceReference(ProgressBar.ForegroundProperty, "AccentBrush");
            right.Children.Add(_progress);
            _result = Ui.Text("等待选择安装包。", 12, "TextSecondaryBrush", FontWeights.Normal);
            _result.FontFamily = new FontFamily("Consolas");
            right.Children.Add(_result);
            var rightPanel = Ui.Panel(right, new Thickness(0));
            Grid.SetColumn(rightPanel, 1);
            body.Children.Add(rightPanel);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var note = Ui.Text("提示：多选 APK 时使用 install-multiple，适用于 split APK；批量设备安装将按设备顺序执行。", 11, "TextSecondaryBrush", FontWeights.Normal);
            note.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(note, 2);
            root.Children.Add(note);
            root.Drop += OnDrop;
            root.DragOver += delegate(object sender, DragEventArgs args) { args.Effects = DragDropEffects.Copy; args.Handled = true; };
            Content = root;
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
            var root = new Grid { Margin = Ui.PagePadding };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var heading = new StackPanel();
            Ui.Heading("文件传输", "在本地与设备之间推送或拉取文件，并支持设备目录浏览和校验。", heading);
            root.Children.Add(heading);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var local = new StackPanel();
            local.Children.Add(Ui.Text("本地", 16, "TextPrimaryBrush", FontWeights.SemiBold));
            var localRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 10) };
            _localPath = Ui.Input(_state.Settings.DefaultFolder, 360);
            var chooseFile = Ui.Button("选择文件", false);
            chooseFile.Click += delegate { ChooseLocalFile(); };
            var chooseFolder = Ui.Button("选择目录", false);
            chooseFolder.Click += delegate { ChooseLocalFolder(); };
            localRow.Children.Add(_localPath);
            localRow.Children.Add(chooseFile);
            localRow.Children.Add(chooseFolder);
            local.Children.Add(localRow);
            var localHint = Ui.Text("推送时可选择文件或目录；拉取时此处为本地保存位置。", 12, "TextSecondaryBrush", FontWeights.Normal);
            local.Children.Add(localHint);
            var push = Ui.Button("推送到设备", true);
            push.Margin = new Thickness(0, 22, 0, 0);
            push.Width = 140;
            push.Click += async delegate { await PushAsync(); };
            local.Children.Add(push);
            var verify = Ui.Button("校验文件", false);
            verify.Margin = new Thickness(0, 10, 0, 0);
            verify.Width = 140;
            verify.Click += async delegate { await VerifyAsync(); };
            local.Children.Add(verify);
            var localPanel = Ui.Panel(local, new Thickness(0, 0, 12, 0));
            body.Children.Add(localPanel);

            var remote = new Grid();
            remote.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            remote.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            remote.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            remote.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            remote.Children.Add(Ui.Text("Android 设备", 16, "TextPrimaryBrush", FontWeights.SemiBold));
            var remoteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 10) };
            _remotePath = Ui.Input("/sdcard/", 360);
            var browse = Ui.Button("浏览", false);
            browse.Click += async delegate { await BrowseRemoteAsync(); };
            remoteRow.Children.Add(_remotePath);
            remoteRow.Children.Add(browse);
            Grid.SetRow(remoteRow, 1);
            remote.Children.Add(remoteRow);
            _remoteList = new ListBox { FontFamily = new FontFamily("Consolas"), FontSize = 12, MinHeight = 210 };
            _remoteList.SetResourceReference(Control.BackgroundProperty, "SurfaceAltBrush");
            _remoteList.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            _remoteList.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            _remoteList.MouseDoubleClick += delegate
            {
                string item = _remoteList.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(item)) Clipboard.SetText(item);
            };
            Grid.SetRow(_remoteList, 2);
            remote.Children.Add(_remoteList);
            var pull = Ui.Button("拉取到本地", true);
            pull.Margin = new Thickness(0, 12, 0, 0);
            pull.Width = 140;
            pull.Click += async delegate { await PullAsync(); };
            Grid.SetRow(pull, 3);
            remote.Children.Add(pull);
            var remotePanel = Ui.Panel(remote, new Thickness(0));
            Grid.SetColumn(remotePanel, 1);
            body.Children.Add(remotePanel);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var overlay = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20), IsHitTestVisible = false };
            _progress = new ProgressBar { Height = 4, IsIndeterminate = true, Visibility = Visibility.Collapsed };
            _progress.SetResourceReference(ProgressBar.ForegroundProperty, "AccentBrush");
            _result = Ui.Text("", 11, "TextSecondaryBrush", FontWeights.Normal);
            _result.Margin = new Thickness(0, 5, 0, 0);
            overlay.Children.Add(_progress);
            overlay.Children.Add(_result);
            Grid.SetRow(overlay, 1);
            root.Children.Add(overlay);
            Content = root;
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
            var root = new Grid { Margin = Ui.PagePadding };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var heading = new StackPanel();
            Ui.Heading("应用管理", "查询、启动、停止、卸载和清除应用数据。危险操作会要求确认。", heading);
            root.Children.Add(heading);
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            _search = Ui.Input("搜索包名", 260);
            _search.TextChanged += delegate { ApplyFilter(); };
            _systemApps = Ui.Check("包含系统应用");
            var refresh = Ui.Button("刷新列表", true);
            refresh.Click += async delegate { await RefreshAsync(); };
            toolbar.Children.Add(_search);
            toolbar.Children.Add(_systemApps);
            toolbar.Children.Add(refresh);
            var toolPanel = Ui.Panel(toolbar, new Thickness(0, 0, 0, 12));
            Grid.SetRow(toolPanel, 1);
            root.Children.Add(toolPanel);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            _packages = new ListBox { FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 0, 12, 0) };
            _packages.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            _packages.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            _packages.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            _packages.SelectionChanged += async delegate { await LoadPackageDetailAsync(); };
            body.Children.Add(_packages);
            var actions = new StackPanel();
            actions.Children.Add(Ui.Text("应用操作", 16, "TextPrimaryBrush", FontWeights.SemiBold));
            _detail = Ui.Text("选择一个包名。", 12, "TextSecondaryBrush", FontWeights.Normal);
            _detail.Margin = new Thickness(0, 14, 0, 18);
            _detail.FontFamily = new FontFamily("Consolas");
            actions.Children.Add(_detail);
            foreach (Tuple<string, Func<Task>> action in new[]
            {
                Tuple.Create<string, Func<Task>>("启动应用", LaunchAsync),
                Tuple.Create<string, Func<Task>>("强制停止", ForceStopAsync),
                Tuple.Create<string, Func<Task>>("清除数据", ClearDataAsync),
                Tuple.Create<string, Func<Task>>("卸载应用", UninstallAsync)
            })
            {
                var button = Ui.Button(action.Item1, action.Item1 == "启动应用");
                button.Margin = new Thickness(0, 0, 0, 8);
                button.Width = 130;
                Func<Task> handler = action.Item2;
                button.Click += async delegate { await handler(); };
                actions.Children.Add(button);
            }
            var actionPanel = Ui.Panel(actions, new Thickness(0));
            Grid.SetColumn(actionPanel, 1);
            body.Children.Add(actionPanel);
            Grid.SetRow(body, 2);
            root.Children.Add(body);
            Content = root;
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
