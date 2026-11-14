# 安装 Code Link / Install Code Link

macOS / iOS 用户请看 [Apple 安装指南](APPLE.md)。下列 USB 与 APK 步骤适用于 Windows + Android。

电脑继续运行任务，在 Android 查看终端、发送输入并接收完成提醒。首次使用推荐 USB 数据线。

## 下载和运行

仅从 [ZARADEE/code_LINK Releases](https://github.com/ZARADEE/code_LINK/releases/latest) 下载 Windows ZIP、Android APK 和 `SHA256SUMS.txt`。

1. Windows x64 安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，完整解压 ZIP，运行 `CodeLink.exe`。
2. Android 9 或以上安装 `CodeLink-Android.apk`，允许该安装来源，打开 App 并允许通知。
3. 平板开启开发者选项和 USB 调试，接上支持数据传输的线，解锁并允许电脑的调试授权。
4. 电脑点击 **连接设备 → USB 一键连接**。平板显示已连接后，点击列表里的终端；也可在电脑选中终端和设备，再点 **映射到移动端**。
5. 运行一条测试任务，检查输出和输入。需要完成提醒时，在 **后台与通知** 中允许横幅和后台运行。

当前优先适配横屏平板，界面为中文。Windows 包依赖已安装的 .NET Desktop Runtime；Android APK 使用开发签名，Windows 程序尚无商业代码签名。更换 Android 签名需要卸载旧包，卸载会清除本地配对与分组。下载页会保留本次 APK 的证书指纹。

## 核对下载文件

在下载目录运行 PowerShell，将输出与同一 Release 的 `SHA256SUMS.txt` 比较。

```powershell
Get-FileHash .\CodeLink-Windows-v1.3.0.zip -Algorithm SHA256
Get-FileHash .\CodeLink-Android.apk -Algorithm SHA256
```

配对会允许操作所选终端，只配对自己的设备。发布包首次运行会在解压目录生成 `state`，升级前保留自己的状态目录，不要分享它。

完整的[中文使用说明](https://github.com/ZARADEE/code_LINK#readme)包含蓝牙、通知与故障排查；也可阅读 [English guide](https://github.com/ZARADEE/code_LINK/blob/main/README.en.md)。

## English quick start

1. Download the Windows ZIP and Android APK from the official release above. Compare `Get-FileHash -Algorithm SHA256` with `SHA256SUMS.txt`.
2. Install .NET 10 Desktop Runtime x64 on Windows, extract the full ZIP, and run `CodeLink.exe`. Install the APK on Android 9+.
3. Enable Android USB debugging, connect a data cable, unlock the device, and authorize your computer.
4. Open both apps. On Windows choose **连接设备 → USB 一键连接** (Connect device → USB quick connect), then select a terminal on Android.
5. Allow notifications and background operation in **后台与通知**. Keep the Windows tray app running.

The interface is currently Chinese and optimized for landscape tablets. This release uses an Android development signing certificate; the Windows executable is unsigned. Preserve the extracted app's `state` directory when upgrading. Source builds use a different Android signing key, so they cannot overwrite this APK.
