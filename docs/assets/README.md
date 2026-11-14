# 展示图片来源

| 文件 | 内容与来源 |
| --- | --- |
| `desktop.png` | 2026-11-14 Windows 当前版本窗口截图，显示专用 PowerShell 演示终端 |
| `android.png` | 2026-11-14 联想小新 Pad Pro 12.7 实拍屏幕，USB 已连接；从平板发送 12345，终端回显已收到 |
| `background-settings.png` | 同日平板的后台与通知设置截图；系统勿扰当时处于开启状态 |
| `overview.png` | 将上述真实 Windows 与 Android 截图排版，添加说明文字 |
| `desktop-en.png` | 2026-11-14 英文 Windows 客户端实际窗口，同一 USB 演示终端完整收到平板发送的 12345 |
| `android-en.png` | 同日英文 APK 覆盖安装后的联想平板截图，保留原配对并实际发送输入 |
| `background-settings-en.png` | 同日英文客户端后台与通知设置；保留当时真实的系统勿扰状态 |
| `overview-en.png` | 将上述真实英文 Windows 与 Android 截图排版，添加英文说明 |
| `macos.png` | 2026-11-14 GitHub macOS runner 上运行的原生 Mac 应用，连接实际 PTY 主机 |
| `ios.png` | 同次构建中的 iPhone 16 Pro 模拟器，已配对并接收真实终端输出 |
| `ipados.png` | 同次构建中的 iPad 模拟器，已配对并接收同一终端输出 |
| `apple-overview.png` | 将上述三张 Apple 实际截图排版，标明模拟器来源 |
| `social-preview.png` | 使用四个平台真实界面制作的网页分享图，尺寸 1280 × 640 |
| `notifications.png` | 2026-11-13 真机后台验收中的 A/B 通知横幅，依据 [验收记录](../../research/pad-background-acceptance.json) |
| `reminder-modes.png` | A/B 完成提醒规则示意图 |
| `logo.png` | 由维护者提供的原始素材生成，许可范围见 [素材说明](../../assets/README.md) |

截图仅裁掉系统边栏并模糊本机路径、设备名和私人窗口标题。终端正文与连接状态保留实际画面。原始截图留在本机，公开仓库只保存处理后的版本。

英文界面、输入回显以及中英文覆盖安装保留配对的结果见 [英文版实机验收记录](../../research/english-release-acceptance.json)。通知横幅图片来自此前中文版验收，未将其改绘成英文。

Apple 截图来自 [通过全部运行检查的构建](https://github.com/ZARADEE/code_LINK/actions/runs/43569332131)。终端中的检查说明由演示命令输出，对应此前已完成的 TLS、PTY 输入输出和凭据重连检查。iOS / iPadOS 为模拟器截图，尚无 Apple 真机验收。
