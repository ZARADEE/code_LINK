# 仓库搜索与网页抓取

## GitHub 入口

About 使用英文用途说明，主页指向[英文项目站](https://zaradee.github.io/code_LINK/en.html)，站内可切换中文。搜索分类使用 Topics，版本管理使用 `v1.4.0` 这类 Git 标签。当前 Topics 优先描述终端伴侣、本地连接和实际平台。

```text
android android-tablet windows macos ios terminal terminal-companion
terminal-monitor remote-terminal remote-control codex codex-cli powershell
developer-tools productivity usb bluetooth local-first task-management notifications
```

README 将用途、真实截图和下载放在前面；构建、贡献、安全、路线图与安装文档提供进一步入口。Topics 帮助 GitHub 分类，README 正文提供用户真正会搜索的用途与操作说明。

## 项目站

页面由 GitHub Pages 工作流部署。中文首页与 `en.html` 是直接返回正文的静态 HTML，各自提供标题、描述、canonical、双向 hreflang、Open Graph、Twitter Card 和 SoftwareApplication JSON-LD。结构化数据只描述实际支持的系统、当前版本、免费代码许可和下载地址。

`sitemap.xml` 列出中英文首页与场景指南，网页头部也链接站点地图。指南提供可执行的 USB、Codex 和并行任务步骤，使用 TechArticle 结构化数据，与首页互相链接。`llms.txt` 提供自动阅读工具可用的文档索引，属于补充约定。它不代替 robots 规则，也不控制搜索排名。

## robots.txt 的位置

GitHub Pages 项目站位于 `/code_LINK/`。搜索爬虫读取的是 `https://zaradee.github.io/robots.txt`；本仓库中的 `site/robots.txt` 发布为 `/code_LINK/robots.txt`，只作为可合并至根站的规则样例。页面使用 `index,follow` 元数据允许收录。github.com 自身的抓取策略由 GitHub 管理。

根路径当前返回 404，按 Google 的 robots 处理规则不构成禁止抓取。如果以后维护 `ZARADEE/ZARADEE.github.io` 根站，可在根路径加入本站地图地址。此次仅配置 code_LINK 仓库。

## 搜索平台收录

站点上线后，维护者可在 Google Search Console 添加 `https://zaradee.github.io/code_LINK/` URL 前缀资源，按账号提示验证所有权，再提交本站的 `sitemap.xml`。其他搜索平台也需要由维护者账号完成验证。本仓库提供公开、可抓取的入口，未自动向账号提交所有权验证或申请收录。

新版本需要同时更新下载链接、页面版本、结构化数据和 sitemap 修改日期。保持网页和 README 的实际支持范围一致。

## 依据

- [GitHub Topics](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/classifying-your-repository-with-topics)
- [GitHub Pages 自定义工作流](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)
- [Google robots 文件位置与创建规则](https://developers.google.com/crawling/docs/robots-txt/create-robots-txt)
- [Google 多语言页面](https://developers.google.com/search/docs/specialty/international/localized-versions)
