using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XMG_ADB
{
    internal static class UiSelfTest
    {
        public static int Run(string outputFolder)
        {
            var app = new Application();
            MainWindow window = null;
            int count = 0;
            try
            {
                window = new MainWindow();
                app.MainWindow = window;
                string[] pages = { "设备概览", "设备", "实时日志", "APK 安装", "文件传输", "应用管理", "工具与诊断", "操作记录", "设置" };
                foreach (string theme in new[] { "Light", "Dark" })
                {
                    window.SetTheme(theme, false);
                    foreach (Size size in new[] { new Size(1366, 850), new Size(1100, 640) })
                    {
                        for (int i = 0; i < pages.Length; i++)
                        {
                            window.Navigate(pages[i]);
                            var page = window.CurrentPage;
                            // Missing named XAML elements must fail here, before an event is clicked.
                            foreach (FieldInfo field in page.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                                if (typeof(FrameworkElement).IsAssignableFrom(field.FieldType) && field.GetValue(page) == null)
                                    throw new InvalidOperationException(page.GetType().Name + "." + field.Name + " is not connected.");
                            Render(window, size, outputFolder, theme + "-" + (int)size.Width + "-" + i);
                            if (page is ToolsPage)
                                for (int tab = 0; tab < 5; tab++)
                                {
                                    ((ToolsPage)page).SelectTab(tab);
                                    Render(window, size, outputFolder, theme + "-" + (int)size.Width + "-tools-" + tab);
                                    count++;
                                }
                            count++;
                        }
                    }
                }
                window.Navigate("设备概览");
                var overview = (FrameworkElement)window.CurrentPage.Content;
                Ui.Find<Button>(overview, "Install").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!(window.CurrentPage is ApkPage)) throw new Exception("Install shortcut failed.");
                window.Navigate("设备概览");
                Ui.Find<Button>(overview, "Mirror").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var tools = (FrameworkElement)window.CurrentPage.Content;
                if (Ui.Find<TabControl>(tools, "Tabs").SelectedIndex != 4) throw new Exception("Mirror shortcut failed.");
                window.Navigate("设备");
                var devices = (FrameworkElement)window.CurrentPage.Content;
                var address = Ui.Find<ComboBox>(devices, "Address");
                address.Text = "192.168.1.42";
                address.ApplyTemplate();
                if (address.Template.FindName("PART_EditableTextBox", address) == null) throw new Exception("Editable address template failed.");
                Console.WriteLine("PASS: " + count + " page/theme/size renders; named controls; shortcuts; editable address.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                if (window != null) window.Close();
                app.Shutdown();
            }
        }

        private static void Render(MainWindow window, Size size, string folder, string name)
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(folder, name + ".png"))) encoder.Save(stream);
        }
    }
}
