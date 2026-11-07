import MarkdownIt from "markdown-it";

export function buildRenderer(): MarkdownIt {
  const md = new MarkdownIt({ html: true, linkify: true, breaks: false })
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
  const srcLine = (src: string, i: number): string => {
    let start = 0;
    for (let k = 0; k < i; k++) {
      const nl = src.indexOf("\n", start);
      if (nl === -1) return "";
      start = nl + 1;
    }
    const end = src.indexOf("\n", start);
    return src.slice(start, end === -1 ? src.length : end);
  };
  md.core.ruler.push("maber_task_items", (state) => {
    for (let i = 0; i < state.tokens.length; i++) {
      const tok = state.tokens[i];
      if (tok.type !== "list_item_open" || !tok.map) continue;
      const lineStart = tok.map[0];
      const line = srcLine(state.src, lineStart);
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
