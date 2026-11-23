using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;

namespace CodeLink.Desktop;

internal sealed class PairDeviceWindow : Window
{
    public PairDeviceWindow(Window owner, Func<string, string, string> invitation,
        Func<bool, Task> enableLan, Func<Action<string>, CancellationToken, Task<string>> connectUsb,
        Func<int> onlineCount, Func<string> bluetoothStatus, Func<string[]> bluetoothDevices, Func<Task> refreshPairing,
        Func<bool, Task> enableRemote, Func<System.Text.Json.Nodes.JsonNode?> remoteState)
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
        Text("远程连接可在不同网络下访问这台电脑。首次配对后，手机只需打开 Code Link；电脑需保持开机、不睡眠。");
        var remotePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        panel.Children.Add(remotePanel);
        var remoteToggle = new Button { Content = "开启远程连接", MinHeight = 40 };
        AutomationProperties.SetAutomationId(remoteToggle, "PairRemoteToggle");
        remotePanel.Children.Add(remoteToggle);
        var remoteStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetAutomationId(remoteStatus, "PairRemoteStatus");
        remotePanel.Children.Add(remoteStatus);
        remotePanel.Children.Add(new TextBlock { Text = "优先直连，必要时使用免费公共中继。无需租服务器或安装 VPN；公共中继有限流且没有可用性保证。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var remoteQr = new Image { Width = 240, Height = 240, Visibility = Visibility.Collapsed };
        remotePanel.Children.Add(remoteQr);
        var remoteInvite = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 65, FontSize = 11, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(remoteInvite, "PairRemoteInvitation");
        remotePanel.Children.Add(remoteInvite);
        var remoteCopy = new Button { Content = "复制远程配对信息", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        remoteCopy.Click += (_, _) => Clipboard.SetText(remoteInvite.Text);
        remotePanel.Children.Add(remoteCopy);
        var remoteRefresh = new Button { Content = "刷新远程配对码", Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(remoteRefresh, "PairRemoteRefresh");
        remoteRefresh.Click += async (_, _) =>
        {
            remoteRefresh.IsEnabled = false;
            try { await refreshPairing(); }
            catch(Exception error) { remoteStatus.Text = error.Message; }
            finally { remoteRefresh.IsEnabled = true; }
        };
        remotePanel.Children.Add(remoteRefresh);
        remoteToggle.Click += async (_, _) =>
        {
            remoteToggle.IsEnabled = false;
            try { await enableRemote(!remoteState().B("enabled")); }
            catch(Exception error) { remoteStatus.Text = error.Message; }
            finally { remoteToggle.IsEnabled = true; }
        };
        void RefreshRemote()
        {
            var remote = remoteState();var enabled = remote.B("enabled");
            remoteToggle.Content = enabled ? "关闭远程连接" : "开启远程连接";
            remoteStatus.Text = remote.S("state") == "error" ? remote.S("error") : !enabled ? "远程连接已关闭。" : remote.B("relayOnline") ? "远程入口已就绪，使用 Android 的“连接电脑 → 扫描二维码”配对。" : "正在连接远程网络…";
            var ready = enabled && remote.B("relayOnline");
            remoteQr.Visibility = remoteInvite.Visibility = remoteCopy.Visibility = remoteRefresh.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
            if(!ready)return;
            var value = invitation("127.0.0.1", "Remote");
            if(value == remoteInvite.Text)return;
            remoteInvite.Text = value;
            using var generator = new QRCodeGenerator();using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.M);using var png = new PngByteQRCode(data);
            var bitmap = new BitmapImage();bitmap.BeginInit();bitmap.StreamSource = new MemoryStream(png.GetGraphic(6));bitmap.CacheOption = BitmapCacheOption.OnLoad;bitmap.EndInit();remoteQr.Source = bitmap;
        }
        Text("近距离备用连接", 18).FontWeight = FontWeights.Bold;
        var bluetooth = new Button { Content = "蓝牙连接", MinHeight = 40, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(bluetooth, "PairBluetooth");
        panel.Children.Add(bluetooth);
        Text("通过 USB 连接", 16).FontWeight = FontWeights.Bold;
        Text("1. 安装 Android APK，开启开发者选项和 USB 调试。\n2. 用数据线连接并解锁设备，允许这台电脑进行 USB 调试。\n3. 点击下方按钮，等待 USB 已连接，再在 Android 选择一个终端。");

        var online = Text("", 13);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void RefreshStatus() => online.Text = "桌面当前看到的在线设备数：" + onlineCount();
        timer.Tick += (_, _) => { RefreshStatus(); RefreshRemote(); };
        Loaded += (_, _) => { RefreshStatus(); RefreshRemote(); timer.Start(); };
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
        var advanced = new Expander { Header = "蓝牙 / 局域网与二维码", Content = advancedPanel };
        panel.Children.Add(advanced);
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254"))
            .Select(a => a.Address.ToString()).Distinct();
        var combo = new ComboBox { ItemsSource = new[] { "USB", "蓝牙" }.Concat(addresses).ToArray(),
            SelectedIndex = 0, Margin = new Thickness(0, 12, 0, 12) };
        AutomationProperties.SetAutomationId(combo, "PairTransport");
        advancedPanel.Children.Add(combo);
        var bluetoothInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(bluetoothInfo, "PairBluetoothStatus");
        advancedPanel.Children.Add(bluetoothInfo);
        void RefreshBluetooth()
        {
            var devices = bluetoothDevices();
            bluetoothInfo.Text = "电脑蓝牙服务：" + bluetoothStatus() + "\n" + (devices.Length == 0 ? "尚无通过蓝牙连接的设备。" : "蓝牙已连接 · " + string.Join("、", devices));
        }
        timer.Tick += (_, _) => { if (combo.SelectedIndex == 1) RefreshBluetooth(); };
        bluetooth.Click += (_, _) => { advanced.IsExpanded = true; combo.SelectedIndex = 1; advanced.BringIntoView(); };
        var qr = new Image { Width = 200, Height = 200 };
        advancedPanel.Children.Add(qr);
        var invite = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            Height = 80, FontSize = 11, Margin = new Thickness(0, 10, 0, 10) };
        AutomationProperties.SetAutomationId(invite, "PairInvitation");
        advancedPanel.Children.Add(invite);
        void RefreshInvitation()
        {
            invite.Text = invitation(combo.SelectedIndex < 2 ? "127.0.0.1" : (string)combo.SelectedItem, combo.SelectedIndex == 1 ? "Bluetooth" : combo.SelectedIndex == 0 ? "USB" : "Wi-Fi");
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
            bluetoothInfo.Visibility = combo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            RefreshBluetooth();
            try { if (combo.SelectedIndex > 1) await enableLan(true); RefreshInvitation(); }
            catch (Exception error) { status.Text = error.Message; }
        };
        var copy = new Button { Content = "复制配对信息" };
        copy.Click += (_, _) => Clipboard.SetText(invite.Text);
        advancedPanel.Children.Add(copy);
        var refresh = new Button { Content = "刷新配对码", Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetAutomationId(refresh, "PairRefresh");
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try { await refreshPairing(); RefreshInvitation(); }
            catch (Exception error) { status.Text = error.Message; }
            finally { refresh.IsEnabled = true; }
        };
        advancedPanel.Children.Add(refresh);
        advancedPanel.Children.Add(new TextBlock { Text = "蓝牙：先在两端系统设置中完成蓝牙配对。Android 点“连接电脑 → 蓝牙”或“更多 → 蓝牙连接”，选择已配对的电脑。首次应用配对可扫码或粘贴上方信息，允许“附近的设备”权限后继续；已通过 USB 配对过的设备可直接切换，无需再次扫码。\n蓝牙无需 USB 调试、Wi-Fi 或个人局域网 PAN。电脑蓝牙服务不可用时，开启蓝牙后重启 Code Link 代理。\n局域网：请选择电脑的可达地址，再在移动端扫码。配对信息包含临时凭据，请勿公开。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        var done = new Button { Content = "返回面板，选择终端与设备", Margin = new Thickness(0, 18, 0, 0) };
        done.Click += (_, _) => Close();
        panel.Children.Add(done);
        RefreshInvitation();
    }
}
