# 从源码构建

构建环境为 Windows x64。v1.4.0 的便携包已经包含 .NET 桌面运行时，用户无需额外安装；修改源码则需要下列开发工具。

| 工具 | 用途 |
| --- | --- |
| [.NET SDK 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | Windows 桌面、代理和 CLI |
| JDK 21，确保 `java`、`javac`、`jar`、`keytool` 在 PATH | Android 编译与状态测试 |
| Python 3.11 或更新版本 | 下载工具、文档检查、真机脚本 |
| Git | 获取源码 |

## 获取依赖

```powershell
git clone https://github.com/ZARADEE/code_LINK.git
cd code_LINK
python scripts/setup-tools.py
```

脚本从 Google 官方仓库下载 Android Build Tools 35.0.0、API 35 平台和 Platform Tools 37.0.1，从 Maven Central 下载 ZXing 3.5.3。每份输入都校验固定 SHA-256，保存在被 Git 忽略的 `.tools`。Android SDK 的使用适用其[许可条款](https://developer.android.com/studio/terms)。QRCoder 1.6.0 由 NuGet 在 .NET 构建时还原。

仓库已经包含生成后的图标和动画。需要替换素材时，准备有权使用的 `naiwa_0066.jpg` 和 `naiwa_0050.gif`，安装 Pillow 后运行 `python scripts/prepare-assets.py`。

## 构建客户端

在仓库根目录的 PowerShell 中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-desktop.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-android.ps1
```

输出分别为 `dist/desktop/CodeLink.exe`、`dist/desktop/clink.exe` 和 `dist/CodeLink-Android-zh-CN.apk`。桌面便携包必须保留整个输出目录。首次运行时，本机状态保存在该目录下的 `state`；不要把它打包或提交到仓库。

Android 当前直接使用 `aapt`、`javac`、`d8`、`zipalign`、`apksigner` 构建，尚未迁移到 Gradle。构建脚本会生成本机调试签名文件 `.local/CodeLink.debug.keystore`。公开的 1.2.0 APK 也使用开发签名，适合侧载测试；本地新生成的签名不能覆盖另一签名的安装包。需要切换签名时先卸载，卸载会清除平板上的配对、分组和已读记录。密钥不随源码分发。

USB 安装需要已授权的 Android 设备：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-android.ps1 -Install
```

## 不依赖设备的检查

```powershell
python scripts/check-docs.py
New-Item -ItemType Directory -Force .local/java-tests | Out-Null
javac -encoding UTF-8 -d .local/java-tests android/app/src/main/java/com/codelink/app/ReminderState.java android/app/src/main/java/com/codelink/app/OutputCache.java android/tests/ReminderStateTest.java android/tests/OutputCacheTest.java
java -cp .local/java-tests com.codelink.app.ReminderStateTest
java -cp .local/java-tests com.codelink.app.OutputCacheTest
dotnet run --project desktop/CodeLink.Smoke/CodeLink.Smoke.csproj --configuration Release -- --activity
```

CI 在 Windows runner 中执行桌面和 APK 构建、Java 行为测试、终端状态识别样例和文档检查。自动构建通过不能代替真机 USB、蓝牙、输入投递或厂商后台策略验收。

v1.4 新增 `python scripts/test-windows-package.py dist/desktop`，验证包内运行时、真实 WPF 窗口、CLI/代理通信和输入桥接；参数也可传入发布 ZIP，以验证用户下载并解压后的内容。打包读取本轮构建生成的 `build-files.json`，排除残留文件与本机状态。

该检查还会通过 `scripts/test-bluetooth-wizard.ps1` 操作隔离的配对窗口，验证蓝牙入口、二维码连接方式、离线提示、配对码刷新以及切回 USB。对 `dist/desktop-en` 运行相同命令可检查英文包。此项不需要蓝牙硬件；Android 权限、实际蓝牙传输与重连仍由真机验收覆盖。

`dotnet run --project desktop/CodeLink.Smoke/CodeLink.Smoke.csproj --configuration Release -- --pairing-checks` 验证配对确认仅在认证后出现，并检查旧版客户端重连兼容性。

Android 布局验收脚本为 `scripts/test-adaptive-layout.py`，需要 Pillow、已授权测试设备、运行中的测试代理，以及已构建的 `CodeLink.Smoke`。例如 `python scripts/test-adaptive-layout.py --serial YOUR_SERIAL --instance YOUR_TEST_INSTANCE`。脚本创建专用终端，临时调整窗口尺寸、旋转、字体与输入法，完成后恢复设置。手机窗口覆盖不能代替实体手机兼容性测试。Windows 向导可用 `scripts/test-connection-wizard.ps1 -DesktopPid YOUR_TEST_PID` 检查两次真实 USB 配对；两项设备测试均不在无设备 CI 上执行。

## 真机联调与实现

`scripts/test-pad-*.py`、`scripts/test-b-group-mark.py` 和 `scripts/resume-pad-background.py` 是维护者的验收脚本，包含测试机的端口、路径或界面坐标。使用前按自己的环境调整，连接测试平板，并新建专用终端，避免向工作中的命令行写入测试输入。

- [当前实现和兼容边界](implementation.zh-CN.md)
- [开发与验收记录](development-status.md)
- [初始技术方案](technical-proposal.zh-CN.md)，保留设计过程；实际行为以当前实现文档为准

## 网站

介绍页源文件在 `site`，GitHub Pages 工作流将 `docs/assets` 复制到站点的 `assets` 后发布。修改页面时同时检查标题、描述、canonical、社交分享图和 `sitemap.xml`。本地预览可将这两部分复制到 `.local/site-preview`，再运行 `python -m http.server --directory .local/site-preview 8080`。

中英文网页与抓取配置见[搜索与抓取说明](DISCOVERABILITY.md)。

## 发布包

构建两端后运行 `python scripts/package-release.py`，在 `dist/releases/v1.4.0` 生成 Windows ZIP、Android APK 与 `SHA256SUMS.txt`。脚本只打包应用文件，重新生成默认 `runtime.json`，排除本机配对、状态和调试文件。

`v*` 标签或手动运行 **Build release packages** 会生成 GitHub Actions 测试产物。CI 每次生成的 Android 调试密钥不同，产物不会自动覆盖公开 Release。维护者发布升级 APK 时应沿用发布密钥，并在 Release 中记录证书指纹和安装限制。

## Apple builds

Windows 与 Android 的构建脚本支持 `-Language zh-CN` 和 `-Language en`。英文 Windows 输出为 `dist/desktop-en`，英文 APK 为 `dist/CodeLink-Android-en.apk`。发布前构建两种语言，再运行打包脚本；发布文件会包含版本号和语言后缀。

翻译字典在 `localization/en.json`。新增文案后运行 `python scripts/localize-sources.py extract` 与 `check`，确保英文覆盖完整。生成目录 `.local/localized/en` 不应手动修改或提交。开发导读见 [if_want_learn.md](../if_want_learn.md)。

On a Mac with Xcode, .NET 10 SDK and XcodeGen, run `bash scripts/build-apple.sh`. This builds macOS arm64 / x64, iOS Simulator and an unsigned iOS device archive, then runs protocol, reminder and host integration checks. See [Apple setup and signing](APPLE.md).
