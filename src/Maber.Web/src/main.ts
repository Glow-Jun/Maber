import { buildRenderer } from "./renderer";
import { obsidianPlugins } from "./obsidian";
import { mathPlugin } from "./math";
import { hydrateMermaid, setMermaidTheme } from "./mermaid";
import { extractOutline } from "./outline";
import { SourceEditor } from "./editor";
import { find } from "./search";
import { send, onHostMessage } from "./bridge";
import type { ViewMode } from "./types";

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

const editor = new SourceEditor(document.querySelector<HTMLElement>("#editor-host")!, (text) => {
  currentText = text; // 始终持有最新源码，供切回渲染与宿主取源
  send({ type: "contentChanged", content: text });
});

function renderDocument(text: string) {
  currentText = text;
  const env: Record<string, unknown> = {};
  content.innerHTML = md.render(text, env);
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

function setMode(next: ViewMode) {
  if (next === mode) return;
  mode = next;
  if (next === "source") {
    editor.show(currentText);
    content.style.display = "none";
  } else {
    const text = editor.hide();
    content.style.display = "";
    if (text) renderDocument(text);
  }
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
    if (link.classList.contains("wikilink")) return; // v0.1 不跳转
    send({ type: "openLink", href: link.getAttribute("href") ?? "" });
  }
});

onHostMessage((msg) => {
  switch (msg.type) {
    case "loadDocument":
      currentText = msg.content;
      if (editor.visible) { editor.hide(); editor.show(msg.content); } // SourceEditor.show 幂等不换文档，需先 hide
      else renderDocument(msg.content);
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
      // v0.1 源码模式查找交给 CodeMirror 面板处理，这里只管渲染态
      if (!editor.visible) find(content, msg.query, msg.direction);
      break;
  }
});

// 给宿主的取源入口
(window as any).__maber = { getSource: () => currentText };
send({ type: "ready" });
