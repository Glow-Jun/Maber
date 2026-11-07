import "./styles/theme.css";
import "./styles/app.css";
import { buildRenderer, preserveIndent } from "./renderer";
import { obsidianPlugins } from "./obsidian";
import { mathPlugin } from "./math";
import { hydrateMermaid, setMermaidTheme } from "./mermaid";
import { extractOutline } from "./outline";
import type { SourceEditor } from "./editor"; // 仅类型；实现按需动态 import，CodeMirror+语言包不进启动包
import { find } from "./search";
import { send, onHostMessage } from "./bridge";
import type { AcceleratorKey, ViewMode } from "./types";

const app = document.querySelector<HTMLDivElement>("#app")!;
app.innerHTML = `
  <div id="frontmatter" class="hidden"></div>
  <article id="content"></article>
  <div id="editor-host"></div>`;

const md = mathPlugin(obsidianPlugins(buildRenderer()));
const content = document.querySelector<HTMLElement>("#content")!;
const fmBox = document.querySelector<HTMLElement>("#frontmatter")!;

let mode: ViewMode = "render";
let currentText = "";

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
  const env: Record<string, unknown> = {};
  content.innerHTML = md.render(preserveIndent(text), env);
  injectTaskMarkers(content);
  hydrateMermaid(content);
  send({ type: "outlineChanged", items: extractOutline(content) });

  // frontmatter 展示
  const fm = env.frontmatter as Record<string, string> | undefined;
  if (fm && Object.keys(fm).length) {
    fmBox.classList.remove("hidden");
    fmBox.innerHTML = Object.entries(fm)
      .map(([k, v]) => `<span class="fm-item"><b>${k}</b> ${escapeHtml(v)}</span>`)
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

function escapeHtml(s: string) {
  return s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]!));
}

async function setMode(next: ViewMode) {
  if (next === mode) return;
  mode = next;
  if (next === "source") {
    (await ensureEditor()).show(currentText);
    content.style.display = "none";
  } else {
    const text = editor?.hide() ?? "";
    content.style.display = "";
    if (text) renderDocument(text);
  }
  // 清掉残留 scrollTop：源码态 #app overflow hidden，残留会让编辑器整体偏移
  document.querySelector("#app")!.scrollTop = 0;
  document.body.dataset.mode = mode;
}

// 任务复选框点击 → 通知宿主写回源文件
content.addEventListener("click", (e) => {
  const t = e.target as HTMLElement;
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
      send({ type: "openWikilink", target: link.getAttribute("data-target") ?? "" });
      return;
    }
    send({ type: "openLink", href: link.getAttribute("href") ?? "" });
  }
});

onHostMessage((msg) => {
  switch (msg.type) {
    case "loadDocument":
      currentText = msg.content;
      // 宿主加载文件后总是回到阅读模式，两侧状态对齐
      if (editor?.visible) {
        editor.hide();
        content.style.display = "";
        mode = "render";
        document.body.dataset.mode = "render";
      }
      renderDocument(msg.content);
      break;
    case "setMode": setMode(msg.mode); break;
    case "setTheme":
      document.body.dataset.theme = msg.theme;
      setMermaidTheme(msg.theme);
      break;
    case "scrollToHeading": {
      const el = content.querySelector(`[data-line="${msg.line}"]`);
      el?.scrollIntoView({ block: "start", behavior: "smooth" });
      break;
    }
    case "find":
      if (editor?.visible) editor.find(msg.query, msg.direction);
      else find(content, msg.query, msg.direction);
      break;
  }
});

// 快捷键：焦点在 WebView2 时 XAML KeyboardAccelerator 收不到事件，
// 由内核在捕获阶段拦截 Ctrl+O/S/E/K/F 上报宿主分发
const ACCELERATOR_KEYS = new Set(["o", "s", "e", "k", "f"]);
window.addEventListener(
  "keydown",
  (e) => {
    if (!e.ctrlKey || e.altKey || e.metaKey) return;
    const key = e.key.toLowerCase();
    if (!ACCELERATOR_KEYS.has(key)) return;
    e.preventDefault();
    e.stopPropagation();
    send({ type: "accelerator", key: key as AcceleratorKey });
  },
  { capture: true },
);

// 给宿主的取源入口
(window as any).__maber = { getSource: () => currentText };
send({ type: "ready" });
