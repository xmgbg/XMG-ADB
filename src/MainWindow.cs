using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace XMG_ADB
{
    public sealed class MainWindow : Window
    {
        private readonly AppState _state;
        private readonly ContentControl _content;
        private readonly ComboBox _deviceSelector;
        private readonly TextBlock _adbStatus;
        private readonly Ellipse _adbDot;
        private readonly TextBlock _statusText;
        private readonly ProgressBar _busyBar;
        private readonly Border _startupOverlay;
        private readonly Dictionary<string, UserControl> _pages;
        private readonly Dictionary<string, RadioButton> _navButtons;
        private string _activePage;
        public UserControl CurrentPage { get { return _content.Content as UserControl; } }

        public MainWindow()
        {
            _state = AppState.Current;
            _pages = new Dictionary<string, UserControl>();
            _navButtons = new Dictionary<string, RadioButton>();
            Title = "XMG_ADB";
            Width = 1366;
            Height = 900;
            MinWidth = 1100;
            MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/" + typeof(Ui).Assembly.GetName().Name + ";component/src/Styles/Controls.xaml", UriKind.Relative)));
            Resources["StatusBrushConverter"] = new StatusBrushConverter();
            SetTheme(_state.Settings.Theme, false);
            var root = Ui.LoadView("MainWindow");
            Content = root;
            _content = Ui.Find<ContentControl>(root, "PageContent");
            _deviceSelector = Ui.Find<ComboBox>(root, "DeviceSelector");
            _deviceSelector.ItemsSource = _state.Devices;
            _adbStatus = Ui.Find<TextBlock>(root, "AdbStatus");
            _adbDot = Ui.Find<Ellipse>(root, "AdbDot");
            _statusText = Ui.Find<TextBlock>(root, "StatusText");
            _busyBar = Ui.Find<ProgressBar>(root, "BusyBar");
            _startupOverlay = Ui.Find<Border>(root, "StartupOverlay");
            Ui.Find<Button>(root, "ThemeButton").Click += delegate { ApplyTheme(_state.Settings.Theme == "Dark" ? "Light" : "Dark"); };
            var navigation = Ui.Find<StackPanel>(root, "Navigation");
            string[] names = { "设备概览", "设备", "实时日志", "APK 安装", "文件传输", "应用管理", "工具与诊断", "操作记录" };
            string[] icons = { "\uE80F", "\uE8EA", "\uE9D9", "\uE7B8", "\uE8B7", "\uE80A", "\uE90F", "\uE81C" };
            for (int i = 0; i < names.Length; i++)
            {
                string target = names[i];
                var button = new RadioButton { GroupName = "Navigation", Style = (Style)Resources["NavButton"] };
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(new TextBlock { Text = icons[i], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Width = 34, Foreground = Brushes.White });
                label.Children.Add(new TextBlock { Text = target == "设备" ? "设备连接" : target, Foreground = Brushes.White, FontSize = 14 });
                button.Content = label;
                button.Click += delegate { Navigate(target); };
                navigation.Children.Add(button);
                _navButtons[target] = button;
            }
            var settings = Ui.Find<RadioButton>(root, "SettingsNav");
            settings.Click += delegate { Navigate("设置"); };
            _navButtons["设置"] = settings;
            UpdateHeader();
            _statusText.Text = _state.StatusText;

            _state.SelectedDeviceChanged += OnSelectedDeviceChanged;
            _state.DevicesChanged += OnDevicesChanged;
            _state.StatusChanged += OnStatusChanged;
            _deviceSelector.SelectionChanged += delegate
            {
                if (_deviceSelector.SelectedItem is DeviceInfo) _state.SelectedDevice = (DeviceInfo)_deviceSelector.SelectedItem;
            };

            Loaded += async delegate
            {
                Navigate("设备概览");
                UpdateHeader();
                PlayStartupAnimation(root);
                if (_state.Adb.IsAvailable) await _state.RefreshDevicesAsync();
            };
            Closed += delegate
            {
                _state.SelectedDeviceChanged -= OnSelectedDeviceChanged;
                _state.DevicesChanged -= OnDevicesChanged;
                _state.StatusChanged -= OnStatusChanged;
                foreach (UserControl page in _pages.Values)
                {
                    var disposable = page as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
            };
        }

        private void PlayStartupAnimation(FrameworkElement root)
        {
            if (!SystemParameters.ClientAreaAnimation)
            {
                _startupOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            var logo = Ui.Find<Image>(root, "StartupLogo");
            var title = Ui.Find<TextBlock>(root, "StartupTitle");
            var subtitle = Ui.Find<TextBlock>(root, "StartupSubtitle");
            var progress = Ui.Find<Border>(root, "StartupProgress");
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            logo.BeginAnimation(OpacityProperty, Animation(0, 1, 260, 40, easing));
            ((TranslateTransform)logo.RenderTransform).BeginAnimation(TranslateTransform.YProperty, Animation(14, 0, 360, 40, easing));
            title.BeginAnimation(OpacityProperty, Animation(0, 1, 240, 180, easing));
            subtitle.BeginAnimation(OpacityProperty, Animation(0, 1, 220, 260, easing));
            ((ScaleTransform)progress.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, Animation(0, 1, 560, 220, easing));

            var fade = Animation(1, 0, 220, 760, easing);
            fade.Completed += delegate
            {
                _startupOverlay.Visibility = Visibility.Collapsed;
                _startupOverlay.BeginAnimation(OpacityProperty, null);
            };
            _startupOverlay.BeginAnimation(OpacityProperty, fade);
        }

        private static DoubleAnimation Animation(double from, double to, int durationMs, int delayMs, IEasingFunction easing)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
        }

        public void ApplyTheme(string theme)
        {
            SetTheme(theme, true);
        }

        internal void SetTheme(string theme, bool save)
        {
            bool dark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
            Resources["BackgroundBrush"] = Brush(dark ? "#11151C" : "#F4F6F9");
            Resources["SurfaceBrush"] = Brush(dark ? "#191F29" : "#FFFFFF");
            Resources["SurfaceAltBrush"] = Brush(dark ? "#232B37" : "#EEF2F7");
            Resources["TextPrimaryBrush"] = Brush(dark ? "#F1F5F9" : "#172033");
            Resources["TextSecondaryBrush"] = Brush(dark ? "#9AA8B8" : "#667085");
            Resources["ComboForegroundBrush"] = Brush("#172033");
            Resources["BorderBrush"] = Brush(dark ? "#303A48" : "#DCE1E8");
            Resources["AccentBrush"] = Brush("#2563EB");
            Resources["AccentHoverBrush"] = Brush(dark ? "#8199FF" : "#2E59BD");
            Resources["OnAccentBrush"] = Brush("#FFFFFF");
            Resources["SuccessBrush"] = Brush(dark ? "#52C77A" : "#238636");
            Resources["WarningBrush"] = Brush(dark ? "#F2B84B" : "#B26A00");
            Resources["DangerBrush"] = Brush(dark ? "#FF7B72" : "#C9362B");
            Resources["LogBackgroundBrush"] = Brush(dark ? "#0C1016" : "#151A22");
            Resources["LogTextBrush"] = Brush("#D8DEE9");
            if (save)
            {
                _state.Settings.Theme = dark ? "Dark" : "Light";
                _state.Settings.Save();
            }
        }

        public void Navigate(string name)
        {
            if (!_pages.ContainsKey(name)) _pages[name] = CreatePage(name);
            _content.Content = _pages[name];
            _activePage = name;
            foreach (var pair in _navButtons) pair.Value.IsChecked = pair.Key == name;
        }

        private UserControl CreatePage(string name)
        {
            if (name == "设备概览") return new OverviewPage(Navigate);
            if (name == "设备") return new DevicesPage();
            if (name == "实时日志") return new LogcatPage();
            if (name == "APK 安装") return new ApkPage();
            if (name == "文件传输") return new FilesPage();
            if (name == "应用管理") return new PackagesPage();
            if (name == "工具与诊断") return new ToolsPage();
            if (name == "操作记录") return new HistoryPage();
            return new SettingsPage(ApplyTheme, UpdateHeader);
        }

        private void UpdateHeader()
        {
            bool available = _state.Adb.IsAvailable;
            _adbStatus.Text = available ? "ADB 可用" : "未找到 ADB";
            _adbStatus.SetResourceReference(TextBlock.ForegroundProperty, available ? "TextSecondaryBrush" : "DangerBrush");
            _adbDot.SetResourceReference(Shape.FillProperty, available ? "SuccessBrush" : "DangerBrush");
        }

        private void OnDevicesChanged(object sender, EventArgs args)
        {
            _deviceSelector.Items.Refresh();
            _deviceSelector.SelectedItem = _state.SelectedDevice;
        }

        private void OnSelectedDeviceChanged(object sender, EventArgs args)
        {
            _deviceSelector.SelectedItem = _state.SelectedDevice;
        }

        private void OnStatusChanged(object sender, EventArgs args)
        {
            Dispatcher.Invoke(delegate
            {
                _statusText.Text = _state.StatusText;
                _busyBar.Visibility = _state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private static SolidColorBrush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
