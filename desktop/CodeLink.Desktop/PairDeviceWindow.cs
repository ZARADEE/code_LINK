using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;

namespace CodeLink.Desktop;

internal sealed class PairDeviceWindow : Window
{
    public PairDeviceWindow(Window owner, Func<string, string> invitation,
        Func<bool, Task> enableLan, Func<Action<string>, CancellationToken, Task<string>> connectUsb,
        Func<int> onlineCount, Func<Task> refreshPairing)
    {
        Owner = owner;
        Title = "首次连接与设备配对";
        Width = 600;
        Height = 740;
        MinWidth = 460;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = owner.Background;
        var lifetime = new CancellationTokenSource();
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        TextBlock Text(string text, double size = 14)
        {
            var block = new TextBlock { Text = text, FontSize = size,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(block);
            return block;
        }

        Text("连接你的 Android 手机或平板", 23).FontWeight = FontWeights.Bold;
        Text("1. 安装 Android APK，开启开发者选项和 USB 调试。\n2. 用数据线连接并解锁设备，允许这台电脑进行 USB 调试。\n3. 点击下方按钮，等待 USB 已连接，再在 Android 选择一个终端。");

        var online = Text("", 13);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void RefreshStatus() => online.Text = "桌面当前看到的在线设备数：" + onlineCount();
        timer.Tick += (_, _) => RefreshStatus();
        Loaded += (_, _) => { RefreshStatus(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); lifetime.Cancel(); };

        var status = Text("等待连接。请保持电脑和 Android 应用都在运行。", 14);
        status.Foreground = new SolidColorBrush(Color.FromRgb(29, 55, 45));
        var usb = new Button { Content = "USB 一键连接", MinHeight = 40,
            Background = new SolidColorBrush(Color.FromRgb(233, 187, 69)) };
        panel.Children.Add(usb);
        Action refreshQr = () => { };
        usb.Click += async (_, _) =>
        {
            usb.IsEnabled = false;
            status.Foreground = new SolidColorBrush(Color.FromRgb(29, 55, 45));
            try { status.Text = await connectUsb(message => status.Text = message, lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (TimeoutException)
            {
                status.Text = "USB 操作超时。解锁平板，确认授权弹窗后重试；仍无响应时更换数据线或 USB 接口。";
                status.Foreground = Brushes.DarkRed;
            }
            catch (Exception error)
            {
                status.Text = error.Message;
                status.Foreground = Brushes.DarkRed;
            }
            finally { usb.IsEnabled = true; RefreshStatus(); if (!lifetime.IsCancellationRequested) refreshQr(); }
        };

        var help = new Expander { Header = "连接失败？按现象检查", Margin = new Thickness(0, 16, 0, 12) };
        help.Content = new TextBlock { Text = "找不到设备：确认数据线支持传输，更换 USB 接口。\n未授权：解锁 Android 并允许 USB 调试；必要时撤销 USB 调试授权后重新连接。\n设备离线：重新插拔数据线，等待系统识别。\n多台设备：首次配对先只保留一台 USB 设备。\n找不到应用：先安装 Android APK，再重试。\n已连接但没输出：在平板点击一个终端，或在桌面选中终端与设备后点击映射。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(help);

        var advancedPanel = new StackPanel();
        var advanced = new Expander { Header = "蓝牙初次配对 / 局域网与二维码", Content = advancedPanel };
        panel.Children.Add(advanced);
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254"))
            .Select(a => a.Address.ToString()).Distinct();
        var combo = new ComboBox { ItemsSource = new[] { "USB / 蓝牙初次配对" }.Concat(addresses).ToArray(),
            SelectedIndex = 0, Margin = new Thickness(0, 12, 0, 12) };
        advancedPanel.Children.Add(combo);
        var qr = new Image { Width = 200, Height = 200 };
        advancedPanel.Children.Add(qr);
        var invite = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            Height = 80, FontSize = 11, Margin = new Thickness(0, 10, 0, 10) };
        advancedPanel.Children.Add(invite);
        void RefreshInvitation()
        {
            invite.Text = invitation(combo.SelectedIndex == 0 ? "127.0.0.1" : (string)combo.SelectedItem);
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(invite.Text, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(png.GetGraphic(5));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            qr.Source = bitmap;
        }
        refreshQr = RefreshInvitation;
        combo.SelectionChanged += async (_, _) =>
        {
            try { await enableLan(combo.SelectedIndex > 0); RefreshInvitation(); }
            catch (Exception error) { status.Text = error.Message; }
        };
        var copy = new Button { Content = "复制配对信息" };
        copy.Click += (_, _) => Clipboard.SetText(invite.Text);
        advancedPanel.Children.Add(copy);
        var refresh = new Button { Content = "刷新配对码", Margin = new Thickness(0, 8, 0, 0) };
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try { await refreshPairing(); RefreshInvitation(); }
            catch (Exception error) { status.Text = error.Message; }
            finally { refresh.IsEnabled = true; }
        };
        advancedPanel.Children.Add(refresh);
        advancedPanel.Children.Add(new TextBlock { Text = "蓝牙先在系统中配对，并用 USB 建立应用信任。局域网请选择电脑的可达地址，再在移动端扫码。配对信息包含临时凭据，请勿公开。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        var done = new Button { Content = "返回面板，选择终端与设备", Margin = new Thickness(0, 18, 0, 0) };
        done.Click += (_, _) => Close();
        panel.Children.Add(done);
        RefreshInvitation();
    }
}
