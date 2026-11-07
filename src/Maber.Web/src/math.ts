import type MarkdownIt from "markdown-it";
import katex from "katex";

export function mathPlugin(md: MarkdownIt): MarkdownIt {
  const render = (tex: string, display: boolean) => {
    try {
      return katex.renderToString(tex, { displayMode: display, throwOnError: true, output: "html" });
    } catch {
      return `<span class="katex-error">${md.utils.escapeHtml(tex)}</span>`;
    }
  };

  // ---- inline $...$ ----
  md.inline.ruler.before("escape", "maber_math_inline", (state, silent) => {
    const start = state.pos;
    if (state.src[start] !== "$") return false;
    if (state.src[start - 1] === "\\") return false;
    // $ 后不允许直接空白（排除 "价格 $ 5"），$ 前一位若是数字则排除（排除 100$ 买）
    const after = state.src[start + 1];
    if (after === undefined || /\s/.test(after)) return false;
    const before = start === 0 ? "" : state.src[start - 1];
    if (/\d/.test(before) && !silent) { /* fallthrough：允许 100$ 的场景由闭合计价 */ }

    let end = start + 1;
    let found = -1;
    while ((end = state.src.indexOf("$", end)) !== -1) {
      if (state.src[end - 1] === "\\") { end += 1; continue; }
      if (end === start + 1) { break; }               // $$ 由块规则处理
      if (/\s/.test(state.src[end - 1])) { break; }   // 闭合前是空白 → 不是公式
      found = end;
      break;
    }
    if (found === -1) return false;

    if (!silent) {
      const tex = state.src.slice(start + 1, found).replace(/\\\$/g, "$");
      const tok = state.push("html_inline", "", 0);
      tok.content = render(tex, false);
    }
    state.pos = found + 1;
    return true;
  });

  // ---- block $$...$$ ----
  md.block.ruler.before("fence", "maber_math_block", (state, startLine, endLine, silent) => {
    const startPos = state.bMarks[startLine] + state.tShift[startLine];
    if (state.src.slice(startPos, startPos + 2) !== "$$") return false;

    let line = startLine;
    let content = "";
    const firstLine = state.src.slice(startPos + 2, state.eMarks[startLine]).trim();
    if (firstLine.endsWith("$$") && firstLine.length >= 2) {
      content = firstLine.slice(0, -2);
      line = startLine;
    } else {
      content = firstLine ? firstLine + "\n" : "";
      let closed = false;
      for (line = startLine + 1; line < endLine; line++) {
        const ls = state.bMarks[line] + state.tShift[line];
        const l = state.src.slice(ls, state.eMarks[line]);
        if (l.trimEnd() === "$$") { closed = true; break; }
        content += l + "\n";
      }
      if (!closed) return false;
    }
    if (silent) return true;

    const tok = state.push("html_block", "", 0);
    tok.content = render(content, true);
    state.line = line + 1;
    return true;
  });

  return md;
}
