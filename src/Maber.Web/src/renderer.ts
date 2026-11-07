import MarkdownIt from "markdown-it";

export function buildRenderer(): MarkdownIt {
  // breaks: Obsidian 默认把段内单换行渲染为换行，而非 CommonMark 的软换行
  // linkify 关闭：linkify-it 会把 MOC.md/Map.md 这类 "xx.md" 误判为域名链接；Obsidian 也不自动链裸域名
  const md = new MarkdownIt({ html: true, linkify: false, breaks: true })
    .enable(["table", "strikethrough"]);

  // 标题带源行号（heading_open token.map[0] 即行首）
  const defaultHeadingOpen = md.renderer.rules.heading_open ?? ((t, i, e, _o, s) => s.renderToken(t, i, e));
  md.renderer.rules.heading_open = (tokens, idx, opts, env, self) => {
    const map = tokens[idx].map;
    if (map) tokens[idx].attrSet("data-line", String(map[0]));
    return defaultHeadingOpen(tokens, idx, opts, env, self);
  };

  // 任务列表：list_item_open 检测首行 `- [ ]`/`- [x]`，打 class/data-* 标记
  // 注：StateCore 没有 bMarks/eMarks（那是 block 级字段），从 src 按行取。
  // 行起点表只建一次，避免每个列表项都从文档头线性扫描（长文档 O(n²)）。
  md.core.ruler.push("maber_task_items", (state) => {
    const src = state.src;
    const starts = [0];
    for (let i = 0; i < src.length; i++) if (src[i] === "\n") starts.push(i + 1);
    const lineAt = (n: number): string => {
      if (n < 0 || n >= starts.length) return "";
      const end = n + 1 < starts.length ? starts[n + 1] - 1 : src.length;
      return src.slice(starts[n], end);
    };
    for (let i = 0; i < state.tokens.length; i++) {
      const tok = state.tokens[i];
      if (tok.type !== "list_item_open" || !tok.map) continue;
      const lineStart = tok.map[0];
      const line = lineAt(lineStart);
      const m = /^(\s*[-*+]\s+)\[( |x|X)\](\s+)/.exec(line);
      if (!m) continue;
      tok.attrJoin("class", "task");
      tok.attrSet("data-line", String(lineStart));
      tok.attrSet("data-checked", m[2].toLowerCase() === "x" ? "true" : "false");
    }
  });

  // frontmatter：文首 --- 块抽出进 env，不参与渲染。
  // 注：markdown-it 核心不识别 frontmatter，文首 --- 会被切成 hr/setext 标题，
  // 故从 src 直接检测定界符，再按行号区间从 token 流剔除，而非按 token 类型判断。
  md.core.ruler.push("maber_frontmatter", (state) => {
    const src = state.src;
    if (!src.startsWith("---")) return;
    const firstLineEnd = src.indexOf("\n");
    if (firstLineEnd === -1 || src.slice(0, firstLineEnd).trim() !== "---") return;
    const end = src.indexOf("\n---", firstLineEnd + 1);
    if (end === -1) return;
    const afterEnd = end + 4;
    if (afterEnd < src.length && src[afterEnd] !== "\n" && src[afterEnd] !== "\r") return;
    const block = src.slice(firstLineEnd + 1, end);
    (state.env as Record<string, unknown>).frontmatter = parseYamlFlat(block);
    const fmEndLine = (src.slice(0, end).match(/\n/g) ?? []).length + 1;
    state.tokens = state.tokens.filter((t) => !t.map || t.map[0] > fmEndLine);
  });

  return md;
}

/** v0.1 只做一层 `key: value` 解析，够用为先 */
export function parseYamlFlat(block: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const raw of block.split("\n")) {
    const line = raw.trim();
    if (!line) continue;
    const idx = line.indexOf(":");
    if (idx <= 0) continue;
    out[line.slice(0, idx).trim()] = line.slice(idx + 1).trim().replace(/^["']|["']$/g, "");
  }
  return out;
}

// 段内续行的行首缩进：Obsidian 会保留，CommonMark 则剥掉（还可能误判成代码块）。
// 渲染前把续行行首空白替换为 em/en space（非 ASCII 空白，不会被解析器吃掉）：
// Tab（4 空格宽）→ em space，普通空格 → en space。
const BLOCK_CONSTRUCT = /^\s*(>|#{1,6}\s|[-*+]\s|\d+[.)]\s|```|~~~)/;

export function preserveIndent(text: string): string {
  const lines = text.split("\n");
  let inFence = false;
  let fenceMarker = "";
  for (let i = 1; i < lines.length; i++) {
    const trimmed = lines[i].trimStart();
    if (trimmed.startsWith("```") || trimmed.startsWith("~~~")) {
      const marker = trimmed.slice(0, 3);
      if (!inFence) { inFence = true; fenceMarker = marker; }
      else if (marker === fenceMarker) inFence = false;
      continue;
    }
    if (inFence) continue;
    const prev = lines[i - 1];
    if (!prev.trim()) continue;                     // 仅处理续行（上一行非空）
    if (!/^[ \t]+/.test(lines[i])) continue;
    if (BLOCK_CONSTRUCT.test(lines[i])) continue;   // 列表/引用/标题等结构行不动
    lines[i] = lines[i].replace(/^([ \t]+)/, (ws) =>
      [...ws].map((c) => (c === "\t" ? "\u2003" : "\u2002")).join(""),
    );
  }
  return lines.join("\n");
}
