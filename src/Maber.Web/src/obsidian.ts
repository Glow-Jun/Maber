import type MarkdownIt from "markdown-it";
import type StateInline from "markdown-it/lib/rules_inline/state_inline.mjs";

/** 依序注册：高亮、注释、标签、wikilink/embed、callout */
export function obsidianPlugins(md: MarkdownIt): MarkdownIt {
  inlineRule(md, "highlight", "==", (s) => `<mark>${s}</mark>`);
  inlineRule(md, "maber_comment", "%%", () => "");
  addTagRule(md);
  addWikilinkRule(md);
  addCalloutRule(md);
  return md;
}

/** 通用成对标记规则：==xx==、%%xx%% */
function inlineRule(md: MarkdownIt, name: string, marker: string, wrap: (s: string) => string) {
  md.inline.ruler.before("emphasis", name, (state: StateInline, silent) => {
    const start = state.pos;
    if (state.src.slice(start, start + marker.length) !== marker) return false;
    const end = state.src.indexOf(marker, start + marker.length);
    if (end === -1) return false;
    const content = state.src.slice(start + marker.length, end);
    if (!content.trim()) return false;
    if (!silent) {
      const tok = state.push("html_inline", "", 0);
      tok.content = wrap(md.utils.escapeHtml(content));
    }
    state.pos = end + marker.length;
    return true;
  });
}

function addTagRule(md: MarkdownIt) {
  md.inline.ruler.before("link", "maber_tag", (state, silent) => {
    const start = state.pos;
    if (state.src[start] !== "#") return false;
    const prev = start === 0 ? " " : state.src[start - 1];
    if (!/[\s(（>]/.test(prev)) return false;                 // 必须在空白/括号后，避免命中标题和 URL 锚点
    const m = /^#([\p{L}\p{N}][\p{L}\p{N}_/-]*)/u.exec(state.src.slice(start));
    if (!m) return false;
    if (!silent) {
      const tok = state.push("html_inline", "", 0);
      tok.content = `<span class="tag">#${md.utils.escapeHtml(m[1])}</span>`;
    }
    state.pos += m[0].length;
    return true;
  });
}

function addWikilinkRule(md: MarkdownIt) {
  md.inline.ruler.before("link", "maber_wikilink", (state, silent) => {
    const src = state.src;
    const start = state.pos;
    const isEmbed = src.slice(start, start + 3) === "![[";
    if (!isEmbed && src.slice(start, start + 2) !== "[[") return false;

    const open = isEmbed ? start + 3 : start + 2;
    const close = src.indexOf("]]", open);
    if (close === -1) return false;
    const raw = src.slice(open, close);
    // 只还原 \| 与 \\ 转义：整体删反斜杠会把 Windows 路径 [[C:\a.md]] 变成 C:a.md
    const unescape = (s: string) => s.replace(/\\([|\\])/g, "$1");
    let sep = -1;
    for (let k = 0; k < raw.length; k++) {
      if (raw[k] === "\\") { k++; continue; }
      if (raw[k] === "|") { sep = k; break; }
    }
    const target = unescape(sep === -1 ? raw : raw.slice(0, sep)).split("#")[0].trim();
    const alias = sep === -1 ? undefined : unescape(raw.slice(sep + 1));
    if (!target && !alias) return false;

    if (!silent) {
      const tok = state.push("html_inline", "", 0);
      if (isEmbed && /\.(png|jpe?g|gif|webp|svg|bmp|avif)$/i.test(target)) {
        // src 由 main.ts 统一重写到宿主映射的文件域，这里只带原始目标
        tok.content = `<img class="embed" data-embed="${md.utils.escapeHtml(target)}" alt="${md.utils.escapeHtml(target)}">`;
      } else if (isEmbed) {
        tok.content = `<span class="embed-placeholder">![[]] ${md.utils.escapeHtml(target)}</span>`;
      } else {
        const label = (alias ?? target).split("#")[0] || target;
        tok.content = `<a class="wikilink" data-target="${md.utils.escapeHtml(target)}" href="#">${md.utils.escapeHtml(label)}</a>`;
      }
    }
    state.pos = close + 2;
    return true;
  });
}

/**
 * callout：core 规则把匹配 `> [!type]± 标题` 的 blockquote token 流整体改写为 callout 结构。
 * 注：计划原 renderer 改写方案失效——blockquote_open 的下一 token 是 paragraph_open 而非
 * inline，且续行与首行同属一段（inline.content 含 \n，`(.*)$` 无法匹配），按计划预案改走
 * core 规则，对外行为（测试断言）不变。
 */
function addCalloutRule(md: MarkdownIt) {
  md.core.ruler.push("maber_callouts", (state) => {
    const tokens = state.tokens;
    for (let i = 0; i < tokens.length; i++) {
      if (tokens[i].type !== "blockquote_open") continue;
      // 定位匹配的 blockquote_close 与块内（栈深 1）第一个 inline token
      let depth = 0;
      let close = -1;
      let inlineIdx = -1;
      for (let j = i; j < tokens.length; j++) {
        const t = tokens[j];
        if (t.type === "blockquote_open") depth++;
        else if (t.type === "blockquote_close" && --depth === 0) { close = j; break; }
        else if (depth === 1 && inlineIdx === -1 && t.type === "inline") inlineIdx = j;
      }
      if (close === -1 || inlineIdx === -1) continue;
      const inline = tokens[inlineIdx];
      // 识别只看首行：blockquote 续行与首行同属一段，inline.content 含 \n，`$` 锚点跨行不匹配
      const firstNl = inline.content.indexOf("\n");
      const firstLine = (firstNl === -1 ? inline.content : inline.content.slice(0, firstNl)).trimStart();
      const m = /^\[!(\w+)\]([+-]?)\s*(.*)$/.exec(firstLine);
      if (!m) continue;
      const [, kind, fold, titleLine] = m;
      const title = titleLine.trim() || kind.toUpperCase();

      // open/close 原地改写为 callout 专用 token：无自定义规则时默认 renderToken 按 div 渲染
      const open = tokens[i];
      open.type = "callout_open";
      open.tag = "div";
      open.attrJoin("class", `callout callout-${kind.toLowerCase()}`);
      if (fold === "-") open.attrSet("data-collapsed", "true");
      tokens[close].type = "callout_close";
      tokens[close].tag = "div";

      const titleOpen = new state.Token("paragraph_open", "div", 1);
      titleOpen.attrJoin("class", "callout-title");
      const titleInline = new state.Token("inline", "", 0);
      const titleText = new state.Token("text", "", 0);
      titleText.content = title;
      titleInline.children = [titleText];
      titleInline.content = title;
      const titleClose = new state.Token("paragraph_close", "div", -1);

      // 首行已被标题占用，剩余行（软换行续行）留作正文首段
      const rest = firstNl === -1 ? "" : inline.content.slice(firstNl + 1);
      const prev = tokens[inlineIdx - 1];
      const next = tokens[inlineIdx + 1];
      const wrapped = prev?.type === "paragraph_open" && next?.type === "paragraph_close";
      if (rest.trim()) {
        const bodyText = new state.Token("text", "", 0);
        bodyText.content = rest;
        inline.children = [bodyText];
        inline.content = rest;
        // 标题必须落在原段落之外，否则会渲染出 <p><div class="callout-title">…</div>正文</p>：
        // 非法嵌套被浏览器自动修复后，正文变成裸文本节点，折叠样式（只匹配元素）就藏不住它
        tokens.splice(wrapped ? inlineIdx - 1 : inlineIdx, 0, titleOpen, titleInline, titleClose);
      } else if (wrapped) {
        // 首段整体即标题：整段替换为标题三连
        tokens.splice(inlineIdx - 1, 3, titleOpen, titleInline, titleClose);
      } else {
        tokens.splice(inlineIdx, 0, titleOpen, titleInline, titleClose);
      }
    }
  });
}
