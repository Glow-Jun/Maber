# Maber

类 Typora 的极简 Markdown 阅读器，为 Obsidian 笔记库优化。WinUI 3 原生壳 + WebView2 渲染内核，原生窗口的轻量与 Web 排版生态兼得。

v0.3.1「Refresh」采用 Paper & Ink 设计语言：暖纸底色、墨色文字、单一陶土强调色。

![status](https://img.shields.io/badge/version-v0.3.1-blue) ![platform](https://img.shields.io/badge/platform-Windows%2010+-lightgrey)

## 特性

**阅读**

- Obsidian 风格渲染：段内单换行即换行、续行缩进保留、`==高亮==`、`%%注释%%`、`#标签`、callout、`[[wikilink]]` 与 `![[嵌入]]`
- KaTeX 数学公式（`$...$` 与 `$$...$$`）、Mermaid 图表（懒加载，遇到才渲染）
- frontmatter 以元信息条形式展示，不混入正文
- 默认浅色（暖纸底、墨色字），可切换跟随系统或深色

**视觉与品牌**

- Paper & Ink 设计系统：暖中性底色、1px 描边、单一陶土强调色，明暗双主题同源
- 「折页 M」品牌标记与应用图标（16–256 多尺寸）
- 阅读字体预设：系统默认、微软雅黑、霞鹜文楷（屏幕阅读）、得意黑、宋体、楷体、等宽

**完整模式（Total Mode）**

- 直接启动进入 Total：左侧栏含大纲 / 最近文件 / 语法速查，`Ctrl+B` 折叠
- 最近文件自动记录、去重（上限 20），失效路径自动剔除
- 设置：主题（跟随系统 / 浅色 / 深色）、字体、字号、行高、阅读宽度，改动实时生效并持久化
- 带文件参数启动仍进入 Slim Mode：无侧栏，保持单文件极简阅读

**导航与编辑**

- `Ctrl+E` 阅读 / 源码双模式（源码为 CodeMirror，带高亮与查找）
- `Ctrl+K` 大纲：Total 下切到侧栏，Slim 下悬浮弹层，点击跳转对应标题
- `Ctrl+F` 页内查找，两种模式均支持，Enter / Shift+Enter 循环跳转
- 任务复选框 `- [ ]` 点击即写回源文件

**文件**

- 非独占打开：阅读的同时可用 Obsidian / VS Code 编辑同一文件，外部修改自动热重载；本地有未保存改动时提示而不覆盖
- 原子保存（临时文件 + 替换），UTF-8 / UTF-8 BOM / GBK 编码自动识别与回写
- `[[wikilink]]` 与 `[text](path.md)` 链接在新窗口打开，支持绝对路径与相对路径
- 文档内相对路径图片直接渲染（文件所在目录作为资源根）

## 快捷键

| 快捷键 | 功能 |
|---|---|
| `Ctrl+O` | 打开文件 |
| `Ctrl+S` | 保存 |
| `Ctrl+E` | 阅读 / 源码切换 |
| `Ctrl+K` | 大纲 |
| `Ctrl+F` | 页内查找 |
| `Ctrl+B` | 折叠 / 展开侧栏（仅 Total） |

## 构建

依赖：.NET 10 SDK、Node 24+（仅 Web 内核开发需要）、Windows 10 1809+。

```bash
# Web 内核（产物提交在 src/Maber.App/wwwroot，跳过此步亦可构建）
cd src/Maber.Web && npm install && npm run build

# 应用
dotnet build
dotnet run --project src/Maber.App
```

测试：`dotnet test`（C# 内核）+ `npm test`（渲染内核，在 src/Maber.Web 下）。

## 架构

```
┌─ Maber.App（WinUI 3 壳：窗口 / 文件 / 监视 / 快捷键）
│    ↕ JSON 消息协议（types.ts / BridgeMessages.cs 双侧对齐）
├─ Maber.Web（TS 渲染内核：markdown-it + KaTeX + Mermaid + CodeMirror，esbuild 打包）
└─ Maber.Core（无 UI 依赖：编码检测、非独占读写、外部修改监视、设置存储，xunit 覆盖）
```

设置与最近文件存放于 `%LOCALAPPDATA%\Maber\settings.json`。

## 当前限制（v0.3.1）

- 无 vault / 工作区概念，wikilink 按当前文件位置逐级向上解析
- 源码模式为纯编辑器，非所见即所得
- wikilink 不校验悬空链接；嵌入仅支持图片
- 暂未支持多标签、wikilink 跨文件解析与 MSIX 打包

## License

MIT
