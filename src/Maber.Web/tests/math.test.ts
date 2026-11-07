import { describe, expect, it } from "vitest";
import { buildRenderer } from "../src/renderer";
import { mathPlugin } from "../src/math";

const md = () => mathPlugin(buildRenderer());

describe("math", () => {
  it("inline $E=mc^2$ renders katex", () => {
    const html = md().render("能量 $E=mc^2$ 守恒");
    expect(html).toContain("katex");
    // KaTeX 把每个符号拆进独立 span，连续子串 "E=mc" 不会出现；去标签后应为 "E=mc2"
    expect(html.replace(/<[^>]+>/g, "")).toContain("E=mc2");
  });

  it("block $$..$$ renders display mode", () => {
    const html = md().render("$$\n\\int_0^1 x\\,dx\n$$\n");
    expect(html).toContain("katex-display");
  });

  it("broken latex degrades to red original text", () => {
    const html = md().render("$\\fra{1}{2}$");
    expect(html).toContain('class="katex-error"');
  });

  it("price like 100$ 不会误触发", () => {
    expect(md().render("花了 100$ 买")).not.toContain("katex");
  });
});
