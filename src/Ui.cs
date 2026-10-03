using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace XMG_ADB
{
    public static class Ui
    {
        public static readonly Thickness PagePadding = new Thickness(28, 24, 28, 24);

        public static TextBlock Text(string value, double size, string brushKey, FontWeight weight)
        {
            var text = new TextBlock
            {
                Text = value,
                FontSize = size,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return text;
        }

        public static TextBlock Heading(string title, string subtitle, Panel parent)
        {
            var titleText = Text(title, 22, "TextPrimaryBrush", FontWeights.SemiBold);
            parent.Children.Add(titleText);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var sub = Text(subtitle, 13, "TextSecondaryBrush", FontWeights.Normal);
                sub.Margin = new Thickness(0, 5, 0, 20);
                parent.Children.Add(sub);
            }
            else titleText.Margin = new Thickness(0, 0, 0, 20);
            return titleText;
        }

        public static Border Panel(UIElement child, Thickness margin)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18),
                Margin = margin,
                Child = child
            };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            return border;
        }

        public static Button Button(string text, bool primary)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 82,
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 13,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
            };
            button.SetResourceReference(Control.StyleProperty, primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            return button;
        }

        public static TextBox Input(string text, double width)
        {
            var input = new TextBox
            {
                Text = text ?? "",
                Width = width,
                Height = 34,
                Padding = new Thickness(10, 6, 10, 5),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            input.SetResourceReference(Control.StyleProperty, "InputStyle");
            return input;
        }

        public static ComboBox Combo(double width)
        {
            var combo = new ComboBox
            {
                Width = width,
                Height = 34,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            combo.SetResourceReference(Control.StyleProperty, "ComboStyle");
            return combo;
        }

        public static CheckBox Check(string text)
        {
            var check = new CheckBox
            {
                Content = text,
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };
            check.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            return check;
        }

        public static TextBlock Label(string text)
        {
            var label = Text(text, 12, "TextSecondaryBrush", FontWeights.Normal);
            label.Margin = new Thickness(0, 0, 0, 6);
            return label;
        }

        public static void ShowResult(string action, CommandResult result)
        {
            MessageBox.Show(AppState.Explain(result), action, MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
    }

    public sealed class StatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value == null ? "" : value.ToString();
            string key = status == "成功" || status == "已连接" ? "SuccessBrush" :
                status == "失败" || status == "未授权" ? "DangerBrush" :
                status == "运行中" || status == "离线" ? "WarningBrush" : "TextSecondaryBrush";
            return Application.Current.MainWindow.FindResource(key);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
