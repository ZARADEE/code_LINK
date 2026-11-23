# 安装与升级 Code Link 2.0

Windows 是任务服务器，Android 手机或平板用于查看和操作。Windows 包面向 x64，Android APK 面向 ARM64、Android 9 及以上系统。macOS / iOS 使用 LAN，见 [Apple 指南](APPLE.md)。

## 下载和运行

从 [v2.0.0 Release](https://github.com/ZARADEE/code_LINK/releases/tag/v2.0.0) 下载 Windows ZIP、Android APK 与 `SHA256SUMS.txt`。

1. 完整解压 `CodeLink-Windows-v2.0.0-zh-CN.zip`，运行 `CodeLink.exe`。包内已有 .NET 10 桌面运行时。
2. 安装 `CodeLink-Android-v2.0.0-zh-CN.apk`，按系统提示允许安装来源和通知。
3. 按下面的远程、USB 或蓝牙步骤连接。手机选择一个终端；也可在电脑选择终端和在线设备，点“映射到移动端”。
4. 在测试终端发送 `echo hello` 检查输入与输出。需要提醒时，打开“更多 → 后台与通知”，允许横幅和后台运行。

`zh-CN` 为简体中文，`en` 为英文。[英文安装说明](INSTALL.en.md)。

## 互联网远程

1. Windows 打开“连接设备”，点击“开启远程连接”，等待入口就绪。
2. Android 选择“连接电脑 → 扫描二维码”，扫描**远程二维码**，或粘贴远程配对信息。
3. 首次邀请 10 分钟内有效；过期后在电脑刷新。配对完成后会保存设备凭据，不用每次扫码。
4. 手机可使用移动数据、热点或其他 Wi-Fi，电脑保持开机、不睡眠，Code Link 持续运行。

远程模式不要求 USB 调试、手机 VPN、端口转发或租服务器。两端尝试直连，失败时使用免费公共中继。配对失败先确认扫码的是远程入口、邀请未过期且电脑在线。[原理、边界与验证](REMOTE.md)

## USB 连接

1. 开启 Android 开发者选项和 USB 调试，连接数据线，解锁并允许这台电脑。
2. Windows 打开“连接设备 → USB 一键连接”，等待本次应用认证完成。
3. 找不到设备时检查数据线和端口，在应用目录运行 `tools\adb.exe -P 5037 devices -l`；`unauthorized` 表示需在 Android 确认。必要时检查 5038 端口。

USB 模式拔线会断开；要跨网使用，请切换到远程入口。

## 蓝牙连接

1. 在系统蓝牙设置中配对电脑与 Android，打开两端 Code Link。
2. 首次应用配对时，Windows 选择“连接设备 → 蓝牙连接”；Android 选择“连接电脑 → 蓝牙”，扫码或粘贴邀请，允许“附近的设备”，选择电脑。
3. 已配对设备可通过“更多 → 蓝牙连接”重连，原有 USB 应用配对可以复用。

无需先接 USB、USB 调试、Wi-Fi 或 PAN。权限被拒绝时按提示进入应用设置；没有已配对电脑时先进入系统设置配对，再返回刷新。

## 阅读、全屏与提醒

手机在列表与详情之间切换，宽屏平板可并排显示。终端区域下滑看前文，上滑看后文；“同步电脑”需要控制权，“本地查看”只翻阅已有缓存。

右上角“全屏”进入横屏，左上角返回或系统返回键恢复布局与草稿。“回到最新”可触发终端里的 Back to bottom。电脑处理授权并恢复运行后，手机自动撤回对应待处理提示；完成通知继续按 A/B 规则确认。

## 从 1.x 升级或切换语言

Android 官方中文和英文 APK 共用应用身份与原有签名，可直接覆盖安装并保留配对、分组，不要先卸载。2.0 的 Android `versionCode` 为 8。源码和 CI 测试包可能使用不同密钥，签名不一致时不能覆盖官方包。

Windows 先保存任务状态并退出 Code Link 与其主机，再替换应用文件，保留原来的 `state` 目录。重新启动后可能需要重新映射已有终端。2.0 远程配对使用专用邀请，已有 USB 或蓝牙配对不会自动开启远程入口。

## 核对下载

```powershell
Get-FileHash .\CodeLink-Windows-v2.0.0-zh-CN.zip -Algorithm SHA256
Get-FileHash .\CodeLink-Android-v2.0.0-zh-CN.apk -Algorithm SHA256
```

与同一 Release 的 `SHA256SUMS.txt` 比较。APK 继续使用原有开发签名，Windows 程序未作商业代码签名；发布页列出 Android 证书指纹。`state` 含本机配对数据，升级时保留，不要公开分享。
