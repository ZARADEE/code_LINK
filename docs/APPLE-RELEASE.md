Verified macOS and iOS developer packages / macOS 与 iOS 开发者下载包

- macOS 14+: download AppleSilicon for Apple silicon, or Intel for Intel Macs. Extract `CodeLink.app` and move it to Applications. The .NET host runtime is included. The app uses an ad-hoc signature and is not notarized.
- iOS / iPadOS 17+: the Simulator ZIP runs in Xcode's Simulator. The unsigned archive requires your own Apple signing and provisioning before device installation. The source ZIP includes the generated Xcode project and the source needed to rebuild the Mac host.
- For personal device testing, open the source project's `apple/CodeLink.xcodeproj`, choose `CodeLinkiOS`, enable signing, choose your development team and a unique bundle identifier, and run on your device.
- Verify downloads against `APPLE-SHA256SUMS.txt`. The build checks both Mac architectures, simulator architectures, device metadata and signatures, then tests the real host with iPhone and iPad simulators before publication.

Mac 包已包含运行时，使用临时签名，尚未公证。iOS 提供模拟器包、未签名归档和完整源码工程；在真机上使用时需通过自己的 Apple 账号签名。本次发放不含直接安装的 IPA 或 TestFlight。

See [installation and signing instructions](https://github.com/ZARADEE/code_LINK/blob/main/docs/APPLE.md).
