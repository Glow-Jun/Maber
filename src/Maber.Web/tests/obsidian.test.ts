import { describe, expect, it } from "vitest";
import { buildRenderer } from "../src/renderer";
import { obsidianPlugins } from "../src/obsidian";

const md = () => obsidianPlugins(buildRenderer());

describe("obsidian syntax", () => {
  it("callouts render type and fold marker", () => {
    const html = md().render("> [!note]- 折叠标题\n> 内容\n");
    expect(html).toContain('class="callout callout-note"');
    expect(html).toContain("data-collapsed");
    expect(html).toContain("折叠标题");
  });

  it("callout with body keeps title outside the paragraph", () => {
    const html = md().render("> [!note] 标题\n> 正文内容\n");
    // 标题若被塞进 <p> 内会渲染成 <p><div>…</div>正文</p>，浏览器修复后折叠样式失效
    expect(html).not.toMatch(/<p>\s*<div class="callout-title"/);
    expect(html).toContain('<div class="callout-title">标题</div>');
    expect(html).toContain("<p>正文内容</p>");
  });

  it("wikilink keeps Windows path backslashes", () => {
    const html = md().render("[[C:\\Users\\a.md]]");
    expect(html).toContain('data-target="C:\\Users\\a.md"');
  });

  it("wikilink escaped pipe stays inside target", () => {
    const html = md().render("[[a\\|b.png]]");
    expect(html).toContain('data-target="a|b.png"');
  });

  it("==highlight== becomes mark", () => {
    expect(md().render("==亮了==")).toContain("<mark>亮了</mark>");
  });

  it("wikilinks render target and alias", () => {
    const html = md().render("[[材料科学基础|教材]]");
    expect(html).toContain('class="wikilink"');
    expect(html).toContain('data-target="材料科学基础"');
    expect(html).toContain(">教材</a>");
  });

  it("tags render span", () => {
    const html = md().render("这是 #标签A 测试");
    expect(html).toContain('class="tag"');
    expect(html).toContain("标签A");
  });

  it("%%comments%% hidden in render mode", () => {
    expect(md().render("a %%secret%% b")).not.toContain("secret");
  });

  it("![[image.png]] embeds image", () => {
    expect(md().render("![[pic.png]]")).toContain("<img");
  });

  it("wikilink keeps heading anchor separately", () => {
    const html = md().render("[[笔记#第二节|别名]]");
    expect(html).toContain('data-target="笔记"');
    expect(html).toContain('data-anchor="第二节"');
    expect(html).toContain(">别名</a>");
  });

  it("wikilink without alias shows note and heading", () => {
    const html = md().render("[[笔记#第二节]]");
    expect(html).toContain(">笔记 &gt; 第二节</a>");
  });

  it("self wikilink anchor renders as in-page jump", () => {
    const html = md().render("[[#第二节]]");
    expect(html).toContain('data-target=""');
    expect(html).toContain('data-anchor="第二节"');
    expect(html).toContain(">第二节</a>");
  });
});
