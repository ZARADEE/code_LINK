# Contributing to Code Link

[中文说明](#参与-code-link)

Code Link focuses on Windows terminals, Android tablets, local connections and reliable task alerts. Questions and use cases belong in [Discussions](https://github.com/ZARADEE/code_LINK/discussions); reproducible bugs and proposed changes belong in [Issues](https://github.com/ZARADEE/code_LINK/issues/new/choose). English and Chinese are both welcome.

1. Fork the repository and create a branch. Discuss larger behavior changes in an issue first.
2. Follow the [build guide](docs/BUILDING.md). Run checks relevant to your change and describe your test environment and results in the PR.
3. Include hardware validation for changes to input delivery, Bluetooth, USB, notifications or window capture. The Java reminder tests do not need a tablet. Maintainer scripts named `scripts/test-pad-*.py` require your own test device and terminal.

Keep build outputs, pairing credentials and local `.local` / `.tools` directories out of commits. Update third-party notices when adding dependencies. Code contributions use MIT; provide the source and license for artwork.

## 参与 Code Link

欢迎提交可复现的问题、使用建议和 Pull Request。当前重点是 Windows + Android 平板、本地连接、终端输入以及可靠的提醒状态。

## 提交问题

使用 [Issue 模板](https://github.com/ZARADEE/code_LINK/issues/new/choose)，提供系统与版本、连接方式、终端类型、复现步骤，以及预期和实际结果。日志应删除配对码、设备密钥、二维码及私人终端内容。

## 修改代码

1. Fork 仓库，新建分支。较大的行为变化先在 Issue 中讨论。
2. 按[构建说明](docs/BUILDING.md)准备工具，完成与修改相关的测试。
3. 在 PR 中写清解决的问题、使用者可见的变化、测试环境与结果。

Java 提醒状态测试不依赖平板。涉及输入投递、蓝牙、USB、通知或窗口采集的修改，还需说明真机验证范围。`scripts/test-pad-*.py` 是维护者的真机联调脚本，运行前需调整设备标识，只在自己的测试终端中执行。

请将构建产物、设备凭据和本机 `.local` / `.tools` 留在仓库之外。新增第三方组件时，同时更新第三方声明。代码贡献按 MIT 授权；图片素材请注明来源与授权。
