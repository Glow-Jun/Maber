let mermaidReady = false;
let theme: "default" | "dark" = "default";

async function ensureMermaid() {
  if (mermaidReady) return;
  const mermaid = (await import("mermaid")).default;
  mermaid.initialize({ startOnLoad: false, theme, securityLevel: "strict" });
  mermaidReady = true;
}

/** 渲染完成后调用：把 language-mermaid 代码块替换为图 */
export async function hydrateMermaid(root: HTMLElement) {
  const blocks = [...root.querySelectorAll("pre code.language-mermaid")];
  if (blocks.length === 0) return;
  await ensureMermaid();
  const mermaid = (await import("mermaid")).default;
  for (const code of blocks) {
    const pre = code.parentElement!;
    const holder = document.createElement("div");
    holder.className = "mermaid-diagram";
    pre.replaceWith(holder);
    try {
      const { svg } = await mermaid.render(`mmd-${Math.random().toString(36).slice(2)}`, code.textContent ?? "");
      holder.innerHTML = svg;
    } catch (e) {
      holder.innerHTML = `<pre class="mermaid-error">${String(e)}</pre><pre>${code.textContent}</pre>`;
    }
  }
}

export function setMermaidTheme(t: "light" | "dark") {
  theme = t === "dark" ? "dark" : "default";
  mermaidReady = false; // 下次渲染按新主题重建
}
