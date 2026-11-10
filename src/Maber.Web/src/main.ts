import "./styles/theme.css";
import "./styles/app.css";
import { buildRenderer, preserveIndent } from "./renderer";
import { obsidianPlugins } from "./obsidian";
import { mathPlugin } from "./math";
import { hydrateMermaid, setMermaidTheme } from "./mermaid";
import { extractOutline } from "./outline";
import type { SourceEditor } from "./editor"; // 仅类型；实现按需动态 import，CodeMirror+语言包不进启动包
import { find, clearFind } from "./search";
import { applyTypography } from "./typography";
import { send, onHostMessage } from "./bridge";
import type { ViewMode } from "./types";

const app = document.querySelector<HTMLDivElement>("#app")!;
app.innerHTML = `
  <div id="frontmatter" class="hidden"></div>
  <article id="content"></article>
  <div id="editor-host"></div>`;

const appEl = document.querySelector<HTMLElement>("#app")!;
const content = document.querySelector<HTMLElement>("#content")!;
const fmBox = document.querySelector<HTMLElement>("#frontmatter")!;
const emptyBox = document.querySelector<HTMLElement>("#empty")!;

// 渲染特性由设置控制：变化时重建 markdown 实例并重渲染
let features = { math: true, mermaid: true };
function createRenderer() {
  const base = obsidianPlugins(buildRenderer());
  return features.math ? mathPlugin(base) : base;
}
let md = createRenderer();

let mode: ViewMode = "render";
let currentText = "";
let contentBase = ""; // 宿主映射的当前文件目录域（http://md.files/），图片等相对资源重写到此

let editor: SourceEditor | null = null;
async function ensureEditor(): Promise<SourceEditor> {
  if (!editor) {
    const { SourceEditor: SourceEditorImpl } = await import("./editor");
    editor = new SourceEditorImpl(
      document.querySelector<HTMLElement>("#editor-host")!,
      (text) => {
        currentText = text; // 始终持有最新源码，供切回渲染与宿主取源
        send({ type: "contentChanged", content: text });
      },
    );
  }
  return editor;
}

function renderDocument(text: string) {
  currentText = text;
  clearFind(content); // 先拆掉旧高亮，search 模块持有的 hits 引用随之失效
  const env: Record<string, unknown> = {};
  content.innerHTML = md.render(preserveIndent(text), env);
  injectTaskMarkers(content);
  rewriteLocalImages(content);
  if (features.mermaid) hydrateMermaid(content);
  send({ type: "outlineChanged", items: extractOutline(content) });

  // frontmatter 展示
  const fm = env.frontmatter as Record<string, string> | undefined;
  if (fm && Object.keys(fm).length) {
    fmBox.classList.remove("hidden");
    fmBox.innerHTML = Object.entries(fm)
      .map(([k, v]) => `<span class="fm-item"><b>${escapeHtml(k)}</b> ${escapeHtml(v)}</span>`)
      .join("");
  } else {
    fmBox.classList.add("hidden");
  }
}

// 计划缺口：renderer 只给 li 打 task/data-* 标记，.task-marker 元素在此补齐（点击处理器与 Task 11 样式都依赖它）
function injectTaskMarkers(root: HTMLElement) {
  for (const li of root.querySelectorAll<HTMLElement>("li.task")) {
    const target = li.querySelector("p") ?? li;
    const checked = li.getAttribute("data-checked") === "true";
    target.insertAdjacentHTML("afterbegin", `<span class="task-marker${checked ? " checked" : ""}"></span>`);
  }
}

// 相对路径图片统一指向宿主映射的文件域；逐段编码，兼容已编码的中文/空格且不重复编码
function encodeAssetPath(path: string): string {
  return path
    .split("/")
    .map((segment) => {
      try {
        return encodeURIComponent(decodeURIComponent(segment));
      } catch {
        return encodeURIComponent(segment);
      }
    })
    .join("/");
}

function rewriteLocalImages(root: HTMLElement) {
  if (!contentBase) return;
  for (const img of root.querySelectorAll<HTMLImageElement>("img")) {
    const embed = img.getAttribute("data-embed");
    const src = embed ?? img.getAttribute("src") ?? "";
    if (!src || /^(https?:|data:|blob:|\/\/)/i.test(src)) continue;
    const normalized = src.replace(/^\.\//, "");
    // 以 / 开头按 Obsidian vault 根处理，宿主会把它解析到内容根目录
    const fromRoot = normalized.startsWith("/");
    const rel = fromRoot ? normalized.slice(1) : normalized;
    img.src = contentBase + (fromRoot ? "@root/" : "") + encodeAssetPath(rel);
  }
}

function escapeHtml(s: string) {
  return s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]!));
}

function scrollRatio(): number {
  const max = appEl.scrollHeight - appEl.clientHeight;
  return max > 0 ? appEl.scrollTop / max : 0;
}

// 模式切换期间布局会剧烈变化，抑制这段滚动事件，避免把阅读位置写成 0
let suppressPosition = false;

async function setMode(next: ViewMode) {
  if (next === mode) return;
  const ratio = mode === "render" ? scrollRatio() : (editor?.scrollRatio() ?? 0);
  suppressPosition = true;
  mode = next;
  if (next === "source") {
    (await ensureEditor()).show(currentText);
    content.style.display = "none";
    editor?.setScrollRatio(ratio);
  } else {
    const text = editor?.hide() ?? currentText;
    content.style.display = "";
    renderDocument(text); // 空文档也要重渲染，否则清空全文后会继续显示旧内容
    restoreScroll(ratio);
  }
  document.body.dataset.mode = mode;
  requestAnimationFrame(() => { suppressPosition = false; });
}

// 任务复选框点击 → 通知宿主写回源文件
content.addEventListener("click", (e) => {
  const t = e.target as HTMLElement;
  const calloutTitle = t.closest(".callout-title");
  if (calloutTitle) {
    // 折叠态 callout 点标题展开/收起（样式侧 .expanded 已就位，此前没有任何代码去切换它）
    calloutTitle.closest("[data-collapsed]")?.classList.toggle("expanded");
  }
  if (t.classList.contains("task-marker")) {
    const item = t.closest("[data-line][data-checked]")!;
    const checked = item.getAttribute("data-checked") !== "true";
    item.setAttribute("data-checked", String(checked));
    t.classList.toggle("checked", checked);
    send({ type: "checkboxToggled", line: Number(item.getAttribute("data-line")), checked });
  }
  const link = t.closest("a");
  if (link) {
    e.preventDefault();
    if (link.classList.contains("wikilink")) {
      send({
        type: "openWikilink",
        target: link.getAttribute("data-target") ?? "",
        anchor: link.getAttribute("data-anchor") || undefined,
      });
      return;
    }
    const href = link.getAttribute("href") ?? "";
    // 脚注与页内 id 直接在当前文档滚动，不必绕回宿主
    if (href.startsWith("#")) {
      let id = href.slice(1);
      try { id = decodeURIComponent(id); } catch { /* 保留原值 */ }
      const target = document.getElementById(id);
      if (target) {
        target.scrollIntoView({ block: "start", behavior: "smooth" });
        return;
      }
    }
    send({ type: "openLink", href });
  }
});

onHostMessage((msg) => {
  switch (msg.type) {
    case "loadDocument":
      currentText = msg.content;
      emptyBox.classList.add("hidden");
      if (msg.contentBase !== undefined) contentBase = msg.contentBase;
      // 宿主加载文件后总是回到阅读模式，两侧状态对齐
      if (mode !== "render") {
        editor?.hide();
        content.style.display = "";
        mode = "render";
        document.body.dataset.mode = "render";
      }
      appEl.scrollTop = 0;
      lastSentRatio = -1;
      renderDocument(msg.content);
      break;
    case "setMode": setMode(msg.mode); break;
    case "setTheme": {
      const changed = document.body.dataset.theme !== msg.theme;
      document.body.dataset.theme = msg.theme;
      setMermaidTheme(msg.theme);
      // 已渲染的 SVG 不会自动换色，主题真的变了就整篇重绘一次（保留滚动位置）
      if (changed && content.querySelector(".mermaid-diagram")) {
        const top = appEl.scrollTop;
        renderDocument(currentText);
        appEl.scrollTop = top;
      }
      break;
    }
    case "setTypography": {
      const nextMath = msg.renderMath !== false;
      const nextMermaid = msg.renderMermaid !== false;
      const changed = nextMath !== features.math || nextMermaid !== features.mermaid;
      features = { math: nextMath, mermaid: nextMermaid };
      if (changed) {
        md = createRenderer();
        if (mode === "render") {
          const top = appEl.scrollTop;
          renderDocument(currentText);
          appEl.scrollTop = top;
        }
      }
      applyTypography(msg);
      break;
    }
    case "setShortcuts": {
      shortcutGestures = new Set(msg.shortcuts.map((s) => s.gesture));
      const open = msg.shortcuts.find((s) => s.id === "open")?.gesture ?? "Ctrl+O";
      const hint = document.querySelector<HTMLElement>("#empty-open-key");
      if (hint) hint.textContent = open;
      break;
    }
    case "restoreReadingPosition": restoreScroll(msg.ratio); break;
    case "setEmptyState": emptyBox.classList.toggle("hidden", !msg.visible); break;
    case "scrollToHeading": {
      const el = content.querySelector(`[data-line="${msg.line}"]`);
      el?.scrollIntoView({ block: "start", behavior: "smooth" });
      break;
    }
    case "find":
      if (!msg.query) {
        // 关闭查找框：清掉两种模式下的高亮
        clearFind(content);
        editor?.clearFind();
        break;
      }
      if (editor?.visible) editor.find(msg.query, msg.direction);
      else find(content, msg.query, msg.direction, msg.wholeWord ?? false);
      break;
  }
});

// 快捷键：Web 内核捕获后上报规范化组合键，宿主按同一份绑定表分发
let shortcutGestures = new Set<string>();

function keyToken(e: KeyboardEvent): string | null {
  const key = e.key;
  if (key.length === 1) {
    if (/[a-zA-Z]/.test(key)) return key.toUpperCase();
    if (/[0-9]/.test(key)) return key;
    if (",./;'[]\\-=`".includes(key)) return key;
    return null;
  }
  const named: Record<string, string> = {
    Enter: "Enter",
    " ": "Space",
    Home: "Home",
    End: "End",
    PageUp: "PageUp",
    PageDown: "PageDown",
    ArrowLeft: "Left",
    ArrowRight: "Right",
    ArrowUp: "Up",
    ArrowDown: "Down",
    Insert: "Insert",
    Delete: "Delete",
    Backspace: "Backspace",
  };
  if (named[key]) return named[key];
  if (/^F([1-9]|1[0-2])$/.test(key)) return key;
  return null;
}

function gestureOf(e: KeyboardEvent): string | null {
  if (!e.ctrlKey && !e.altKey && !e.shiftKey) return null;
  const token = keyToken(e);
  if (!token) return null;
  const mods: string[] = [];
  if (e.ctrlKey) mods.push("Ctrl");
  if (e.altKey) mods.push("Alt");
  if (e.shiftKey) mods.push("Shift");
  return [...mods, token].join("+");
}

window.addEventListener(
  "keydown",
  (e) => {
    if (e.isComposing || e.repeat) return;
    const gesture = gestureOf(e);
    if (!gesture || !shortcutGestures.has(gesture)) return;
    e.preventDefault();
    e.stopPropagation();
    send({ type: "accelerator", gesture });
  },
  { capture: true },
);

// 阅读位置：滚动比例去抖上报；打开文档后按宿主指令恢复
let lastSentRatio = -1;
let scrollTimer: number | undefined;
appEl.addEventListener("scroll", () => {
  if (suppressPosition || mode !== "render") return;
  if (scrollTimer !== undefined) return;
  scrollTimer = window.setTimeout(() => {
    scrollTimer = undefined;
    const ratio = scrollRatio();
    if (Math.abs(ratio - lastSentRatio) < 0.002) return;
    lastSentRatio = ratio;
    send({ type: "readingPosition", ratio });
  }, 400);
});

function restoreScroll(ratio: number) {
  requestAnimationFrame(() => {
    const max = appEl.scrollHeight - appEl.clientHeight;
    appEl.scrollTop = max > 0 ? Math.min(1, Math.max(0, ratio)) * max : 0;
    lastSentRatio = ratio;
  });
}

// 诊断：图片加载失败 / JS 异常上报宿主进日志
window.addEventListener(
  "error",
  (e) => {
    const t = e.target as HTMLElement | null;
    if (t && t.tagName === "IMG") send({ type: "diag", text: `img-failed ${t.getAttribute("src")}` });
    else if (e instanceof ErrorEvent && e.message) send({ type: "diag", text: `js-error ${e.message}` });
  },
  true,
);

// 给宿主的取源入口
send({ type: "ready" });
