# 仓库与文档参考

整理于 2026-11-13。参考以下项目的组织方式，文字、截图和功能说明均按 Code Link 的当前实现编写。

| 参考 | 采用的写法 |
| --- | --- |
| [scrcpy](https://github.com/Genymobile/scrcpy) | 开头说明用途、展示实际画面，将下载与首次使用放在前面 |
| [LocalSend](https://github.com/localsend/localsend) | 突出本地连接、平台和安装入口，提供多语言 README |
| [Syncthing](https://github.com/syncthing/syncthing) | 将用户介绍、构建、贡献与问题反馈分开 |

GitHub 的 [README 说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes)、[Topics 文档](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/classifying-your-repository-with-topics)和 [Pages 工作流文档](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)用于核对仓库配置。

## 搜索与抓取入口

- GitHub About 使用准确的中英文项目描述，Topics 对应平台、终端、本地连接和通知用途。
- GitHub Pages 页面提供独立的 HTML 标题、描述、canonical、Open Graph、Twitter Card、SoftwareApplication 结构化数据和 sitemap。
- `site/llms.txt` 提供面向自动阅读工具的简短资料索引，是补充入口，不是搜索引擎排名指令。
- 项目站位于 `/code_LINK/`。该路径下的 `robots.txt` 无法控制域名根路径的抓取规则，因此使用页面 robots 元数据和站点地图；仓库中的文件也不能修改 github.com 的抓取策略。

这些配置提供清晰、可抓取的公开资料。搜索引擎何时收录、如何排序由其决定；项目不使用关键词堆砌、虚构评分或自动生成外链。
