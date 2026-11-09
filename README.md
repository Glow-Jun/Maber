# Maber

类 Typora 的极简 Markdown 阅读器，为随时打开散落各处的 Markdown 文档而设计：像 Notepad 一样轻快，不要求工作区或笔记库。WinUI 3 原生壳 + WebView2 渲染内核，原生窗口的轻量与 Web 排版生态兼得。

v0.4.3「Settings & Reading」采用 Paper & Ink 设计语言：暖纸底色、墨色文字、单一陶土强调色。

![status](https://img.shields.io/badge/version-v0.4.3-blue) ![platform](https://img.shields.io/badge/platform-Windows%2010+-lightgrey)

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
- 本地字体：搜索并选择系统已安装字体，或导入 `.ttf / .otf / .ttc` 字体文件
- 正文与代码字体、代码字号分别设置

**设置（v0.4）**

- 独立设置窗口，外观 / 阅读 / 快捷键 / 文件与最近 / 关于五个分类，与主界面共用 Paper & Ink
- 自定义快捷键：点击录制、冲突检测、单项或全部恢复默认
- 侧栏默认展开与宽度、窗口大小与位置记忆
- 最近文件上限、启动行为（空白页 / 恢复上次文件）、外部修改策略
- 数学公式 / Mermaid 渲染开关，实时预览字体与排版

**随时打开**

- `Ctrl+P` 快速打开：模糊搜索最近文件，扫描当前文件所在目录（深度 2），也可直接粘贴完整路径
- 文件拖入窗口直接打开
- 记住每个文件的阅读位置，再次打开回到上次滚动处

**完整模式（Total Mode）**

- 直接启动进入 Total：左侧栏含大纲 / 最近文件 / 语法速查，`Ctrl+B` 折叠
- 最近文件自动记录、去重（默认上限 20，可调 5–50），失效路径自动剔除
- 带文件参数启动仍进入 Slim Mode：无侧栏，保持单文件极简阅读

**导航与编辑**

- `Ctrl+E` 阅读 / 源码双模式（源码为 CodeMirror，带高亮与查找）
- `Ctrl+K` 大纲：Total 下切到侧栏，Slim 下悬浮弹层，点击跳转对应标题
- `Ctrl+F` 页内查找，支持全词匹配与搜索历史，Enter / Shift+Enter 循环跳转
- 任务复选框 `- [ ]` 点击即写回源文件

**文件**

- 非独占打开：阅读的同时可用 Obsidian / VS Code 编辑同一文件，外部修改自动热重载；本地有未保存改动时提示而不覆盖
- 原子保存（临时文件 + 替换），UTF-8 / UTF-8 BOM / GBK 编码自动识别与回写
- `[[wikilink]]` 与 `[text](path.md)` 链接在新窗口打开，支持绝对路径与相对路径
- 文档内相对路径图片直接渲染（文件所在目录作为资源根）

## 快捷键

以下为默认绑定，可在设置中逐项修改。

| 快捷键 | 功能 |
|---|---|
| `Ctrl+O` | 打开文件 |
| `Ctrl+S` | 保存 |
| `Ctrl+E` | 阅读 / 源码切换 |
| `Ctrl+K` | 大纲 |
| `Ctrl+F` | 页内查找 |
| `Ctrl+B` | 折叠 / 展开侧栏（仅 Total） |
| `Ctrl+P` | 快速打开 |
| `Ctrl+,` | 设置 |

## 安装

运行 `Maber-Setup-0.4.3.exe`：

- 产品名称与主程序均为 **Maber**（`Maber.exe`）
- 默认安装到 `C:\Program Files\Maber`，安装向导中可修改路径（需要管理员权限）
- 创建开始菜单快捷方式，可选桌面图标；可在“设置 → 应用 → 已安装的应用”中卸载
- 注册 `.md` / `.markdown` 文件关联：Maber 会出现在“打开方式”和 Windows 默认应用的推荐列表中；Markdown 文件使用羊皮卷图标
- 配置、阅读位置与字体保留在 `%LOCALAPPDATA%\Maber`，卸载不会删除

也可以直接运行 `publish\Maber.exe` 便携使用。

## 构建

依赖：.NET 10 SDK、Node 24+（仅 Web 内核开发需要）、Windows 10 1809+。

```bash
# Web 内核（产物提交在 src/Maber.App/wwwroot，跳过此步亦可构建）
cd src/Maber.Web && npm install && npm run build

# 应用
dotnet build
dotnet run --project src/Maber.App

# 安装包（需 Inno Setup 6，输出到仓库根目录）
iscc installer/Maber.iss
```

测试：`dotnet test`（C# 内核）+ `npm test`（渲染内核，在 src/Maber.Web 下）。

## 架构

```
┌─ Maber.App（WinUI 3 壳：窗口 / 文件 / 监视 / 快捷键）
│    ↕ JSON 消息协议（types.ts / BridgeMessages.cs 双侧对齐）
├─ Maber.Web（TS 渲染内核：markdown-it + KaTeX + Mermaid + CodeMirror，esbuild 打包）
└─ Maber.Core（无 UI 依赖：编码检测、非独占读写、外部修改监视、设置存储，xunit 覆盖）
```

设置与最近文件存放于 `%LOCALAPPDATA%\Maber\settings.json`，阅读位置与导入字体分别在 `reading-state.json` 与 `fonts\` 目录。

## 当前限制（v0.4.3）

- 无 vault / 工作区概念，wikilink 按当前文件位置逐级向上解析
- 源码模式为纯编辑器，非所见即所得
- wikilink 不校验悬空链接；嵌入仅支持图片
- 暂未支持多标签、wikilink 跨文件解析与 MSIX 打包

## License

MIT
