# Third-party notices

Code Link includes the following components. Their licenses apply independently of the project's MIT license.

| Component | Version | Usage | License |
| --- | --- | --- | --- |
| [xterm.js](https://github.com/xtermjs/xterm.js) | `@xterm/xterm` 5.5.0 | Bundled Android terminal renderer | [MIT](licenses/xterm.txt) |
| [QRCoder](https://github.com/codebude/QRCoder) | 1.6.0 | Windows pairing QR codes, restored through NuGet | [MIT](licenses/QRCoder.txt) |
| [ZXing Core](https://github.com/zxing/zxing) | 3.5.3 | Android QR scanning, included in the APK | [Apache-2.0](licenses/ZXing.txt) |
| [Android Platform Tools](https://developer.android.com/tools/releases/platform-tools) | 37.0.1 | ADB and Windows USB libraries in the desktop ZIP | [Upstream notices](licenses/Android-Platform-Tools.txt) |
| [iroh](https://github.com/n0-computer/iroh) and Rust dependencies | iroh 1.5.0; exact versions in Cargo.lock | Embedded Windows / Android remote transport | MIT / Apache-2.0 with upstream notices, including BSD-derived code; [complete dependency notices](licenses/remote-third-party.txt) |

Android build tools and platform files are downloaded separately by the build setup script. See the [Android SDK terms](https://developer.android.com/studio/terms). .NET and the JDK are separately installed prerequisites.

[DSH Tether](https://github.com/zexadev/dsh-tether) informed the remote transport architecture. Its application code, interface, and protocol are not included.

User-supplied images, animations, and their derivatives are described in [assets/README.md](assets/README.md). The MIT license for project code does not grant rights to those images.
