using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace E300DeviceConsole
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
        private readonly Dictionary<string, UserControl> _pages;
        private readonly Dictionary<string, Button> _navButtons;
        private string _activePage;

        public MainWindow()
        {
            _state = AppState.Current;
            _pages = new Dictionary<string, UserControl>();
            _navButtons = new Dictionary<string, Button>();
            Title = "E300 Device Console";
            Width = 1366;
            Height = 820;
            MinWidth = 1100;
            MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            ConfigureResources();
            ApplyTheme(_state.Settings.Theme);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");

            var header = BuildHeader(out _deviceSelector, out _adbStatus, out _adbDot);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var nav = BuildNavigation();
            Grid.SetColumn(nav, 0);
            body.Children.Add(nav);
            _content = new ContentControl();
            Grid.SetColumn(_content, 1);
            body.Children.Add(_content);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var footer = BuildFooter(out _statusText, out _busyBar);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;

            _state.SelectedDeviceChanged += OnSelectedDeviceChanged;
            _state.DevicesChanged += OnDevicesChanged;
            _state.StatusChanged += OnStatusChanged;
            _deviceSelector.SelectionChanged += delegate
            {
                if (_deviceSelector.SelectedItem is DeviceInfo) _state.SelectedDevice = (DeviceInfo)_deviceSelector.SelectedItem;
            };

            Loaded += async delegate
            {
                Navigate("设备");
                UpdateHeader();
                if (_state.Adb.IsAvailable) await _state.RefreshDevicesAsync();
            };
            Closed += delegate
            {
                foreach (UserControl page in _pages.Values)
                {
                    var disposable = page as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
            };
        }

        public void ApplyTheme(string theme)
        {
            bool dark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
            Resources["BackgroundBrush"] = Brush(dark ? "#11151C" : "#F4F6F9");
            Resources["SurfaceBrush"] = Brush(dark ? "#191F29" : "#FFFFFF");
            Resources["SurfaceAltBrush"] = Brush(dark ? "#232B37" : "#EEF2F7");
            Resources["TextPrimaryBrush"] = Brush(dark ? "#F1F5F9" : "#172033");
            Resources["TextSecondaryBrush"] = Brush(dark ? "#9AA8B8" : "#667085");
            Resources["ComboForegroundBrush"] = Brush("#172033");
            Resources["BorderBrush"] = Brush(dark ? "#303A48" : "#DCE1E8");
            Resources["AccentBrush"] = Brush(dark ? "#6E8BFF" : "#3767D8");
            Resources["AccentHoverBrush"] = Brush(dark ? "#8199FF" : "#2E59BD");
            Resources["OnAccentBrush"] = Brush("#FFFFFF");
            Resources["SuccessBrush"] = Brush(dark ? "#52C77A" : "#238636");
            Resources["WarningBrush"] = Brush(dark ? "#F2B84B" : "#B26A00");
            Resources["DangerBrush"] = Brush(dark ? "#FF7B72" : "#C9362B");
            Resources["LogBackgroundBrush"] = Brush(dark ? "#0C1016" : "#151A22");
            Resources["LogTextBrush"] = Brush("#D8DEE9");
            _state.Settings.Theme = dark ? "Dark" : "Light";
            _state.Settings.Save();
        }

        public void Navigate(string name)
        {
            if (!_pages.ContainsKey(name)) _pages[name] = CreatePage(name);
            _content.Content = _pages[name];
            _activePage = name;
            foreach (var pair in _navButtons)
            {
                pair.Value.FontWeight = pair.Key == name ? FontWeights.SemiBold : FontWeights.Normal;
                pair.Value.SetResourceReference(Control.BackgroundProperty, pair.Key == name ? "SurfaceAltBrush" : "SurfaceBrush");
                pair.Value.SetResourceReference(Control.ForegroundProperty, pair.Key == name ? "AccentBrush" : "TextPrimaryBrush");
            }
        }

        private UserControl CreatePage(string name)
        {
            if (name == "设备") return new DevicesPage();
            if (name == "实时日志") return new LogcatPage();
            if (name == "APK 安装") return new ApkPage();
            if (name == "文件传输") return new FilesPage();
            if (name == "应用管理") return new PackagesPage();
            if (name == "工具与诊断") return new ToolsPage();
            if (name == "操作记录") return new HistoryPage();
            return new SettingsPage(ApplyTheme, UpdateHeader);
        }

        private Border BuildHeader(out ComboBox selector, out TextBlock adbStatus, out Ellipse adbDot)
        {
            var header = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 0, 18, 0) };
            header.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            header.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var mark = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 11, 0) };
            mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var markText = Ui.Text("E3", 12, "OnAccentBrush", FontWeights.Bold);
            markText.HorizontalAlignment = HorizontalAlignment.Center;
            markText.VerticalAlignment = VerticalAlignment.Center;
            mark.Child = markText;
            brand.Children.Add(mark);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Ui.Text("E300 Device Console", 15, "TextPrimaryBrush", FontWeights.SemiBold));
            names.Children.Add(Ui.Text("ADB 运维工作台", 11, "TextSecondaryBrush", FontWeights.Normal));
            brand.Children.Add(names);
            grid.Children.Add(brand);

            var adbPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 20, 0) };
            adbDot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0) };
            adbStatus = Ui.Text("ADB 检测中", 12, "TextSecondaryBrush", FontWeights.Normal);
            adbPanel.Children.Add(adbDot);
            adbPanel.Children.Add(adbStatus);
            Grid.SetColumn(adbPanel, 1);
            grid.Children.Add(adbPanel);

            selector = Ui.Combo(430);
            selector.HorizontalAlignment = HorizontalAlignment.Right;
            selector.VerticalAlignment = VerticalAlignment.Center;
            selector.ItemsSource = _state.Devices;
            selector.DisplayMemberPath = "Display";
            selector.ToolTip = "当前操作目标设备";
            Grid.SetColumn(selector, 2);
            grid.Children.Add(selector);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var theme = Ui.Button("切换主题", false);
            theme.Click += delegate { ApplyTheme(_state.Settings.Theme == "Dark" ? "Light" : "Dark"); };
            var settings = Ui.Button("设置", false);
            settings.Margin = new Thickness(0);
            settings.Click += delegate { Navigate("设置"); };
            actions.Children.Add(theme);
            actions.Children.Add(settings);
            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);
            header.Child = grid;
            return header;
        }

        private Border BuildNavigation()
        {
            var border = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 16, 12, 12) };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var stack = new StackPanel();
            string[] items = { "设备", "实时日志", "APK 安装", "文件传输", "应用管理", "工具与诊断", "操作记录", "设置" };
            foreach (string item in items)
            {
                var button = new Button
                {
                    Content = item,
                    Height = 42,
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(14, 0, 14, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    FontSize = 13
                };
                button.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
                button.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
                string target = item;
                button.Click += delegate { Navigate(target); };
                _navButtons[item] = button;
                stack.Children.Add(button);
            }
            border.Child = stack;
            return border;
        }

        private Border BuildFooter(out TextBlock statusText, out ProgressBar busyBar)
        {
            var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 0, 18, 0) };
            footer.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            footer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusText = Ui.Text("就绪", 12, "TextSecondaryBrush", FontWeights.Normal);
            grid.Children.Add(statusText);
            busyBar = new ProgressBar { Height = 3, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, 12, 0) };
            busyBar.SetResourceReference(ProgressBar.ForegroundProperty, "AccentBrush");
            Grid.SetColumn(busyBar, 1);
            grid.Children.Add(busyBar);
            var hint = Ui.Text("所有操作均在后台执行", 11, "TextSecondaryBrush", FontWeights.Normal);
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
            footer.Child = grid;
            return footer;
        }

        private void ConfigureResources()
        {
            Resources["StatusBrushConverter"] = new StatusBrushConverter();

            var primary = new Style(typeof(Button));
            primary.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentBrush")));
            primary.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("OnAccentBrush")));
            primary.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("AccentBrush")));
            primary.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            primary.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            Resources["PrimaryButtonStyle"] = primary;

            var secondary = new Style(typeof(Button));
            secondary.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush")));
            secondary.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimaryBrush")));
            secondary.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
            secondary.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            secondary.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            Resources["SecondaryButtonStyle"] = secondary;

            var input = new Style(typeof(TextBox));
            input.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush")));
            input.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimaryBrush")));
            input.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
            input.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            input.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, new DynamicResourceExtension("AccentBrush")));
            Resources["InputStyle"] = input;

            var combo = new Style(typeof(ComboBox));
            combo.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush")));
            combo.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ComboForegroundBrush")));
            combo.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
            combo.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            Resources["ComboStyle"] = combo;
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
