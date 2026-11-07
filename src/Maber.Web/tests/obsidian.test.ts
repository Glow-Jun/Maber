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
});
