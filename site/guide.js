(() => {
  const zh = document.documentElement.lang === 'zh-CN';
  const release = 'https://github.com/ZARADEE/code_LINK/releases/download/v1.3.0/';
  const platform = document.querySelector('#download-platform');
  const language = document.querySelector('#download-language');
  const links = document.querySelector('#download-links');
  const note = document.querySelector('#download-note');
  const text = (en, cn) => zh ? cn : en;
  if (platform && language && links && note) {
    const update = () => {
      links.replaceChildren();
      const add = (label, file, isGuide = false) => {
        const a = document.createElement('a');
        a.className = 'button';
        a.textContent = label;
        a.href = isGuide ? file : release + file;
        links.append(a);
      };
      const lang = language.value;
      if (platform.value === 'windows-android') {
        note.textContent = text('Windows x64 + Android 9+. Install .NET 10 Desktop Runtime on Windows. Start with USB; both clients are required.', 'Windows x64 + Android 9 以上。Windows 需安装 .NET 10 Desktop Runtime。首次推荐 USB，两端都需要安装。');
        add('Windows · ' + lang, `CodeLink-Windows-v1.3.0-${lang}.zip`);
        add('Android · ' + lang, `CodeLink-Android-v1.3.0-${lang}.apk`);
      } else if (platform.value === 'mac-android') {
        note.textContent = text('macOS 14+ creates managed terminal/Codex sessions; connect Android over LAN. Mac is English, includes the runtime and is not notarized. iPhone signing is not needed for the Mac app. Physical cross-device acceptance is pending.', 'macOS 14 以上支持创建托管终端与 Codex 会话，Android 通过局域网连接。Mac 为英文，包含运行时，尚未公证。使用 Mac 应用不需要 iPhone 签名；跨设备真机验收待完成。');
        add('Mac · Apple silicon · en', 'CodeLink-macOS-AppleSilicon-v1.3.0-en.zip');
        add('Mac · Intel · en', 'CodeLink-macOS-Intel-v1.3.0-en.zip');
        add('Android · ' + lang, `CodeLink-Android-v1.3.0-${lang}.apk`);
      } else {
        note.textContent = text('iOS/iPadOS 17+ developer preview, English. Simulator runs on a Mac; physical devices require your own Xcode signing. Connection pauses in the background. Choose either the simulator download or source for personal signing.', 'iOS / iPadOS 17 以上开发预览，英文界面。模拟器在 Mac 上运行，真机需使用自己的 Xcode 签名。进入后台会暂停连接；请根据用途选择模拟器包或自行签名源码。');
        add(text('iOS Simulator · en', 'iOS 模拟器 · en'), 'CodeLink-iOS-Simulator-v1.3.0-en.zip');
        add(text('Xcode source · en', 'Xcode 源码 · en'), 'CodeLink-Apple-Source-v1.3.0-en.zip');
        add(text('Apple installation guide', 'Apple 安装说明'), 'https://github.com/ZARADEE/code_LINK/blob/main/docs/APPLE.md', true);
      }
      language.disabled = platform.value === 'ios';
    };
    platform.addEventListener('change', update);
    language.addEventListener('change', update);
    update();
  }
  const checks = [...document.querySelectorAll('.setup-check')];
  const progress = document.querySelector('.setup-progress');
  const count = document.querySelector('.setup-count');
  if (checks.length && progress && count) {
    const update = () => {
      const done = checks.filter(c => c.checked).length;
      progress.value = done;
      count.textContent = text(`${done} / ${checks.length} steps checked`, `已勾选 ${done} / ${checks.length} 步`);
    };
    checks.forEach(check => {
      check.addEventListener('click', event => event.stopPropagation());
      check.addEventListener('change', update);
    });
    update();
  }
})();
