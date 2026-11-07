import { describe, expect, it } from "vitest";
import { buildRenderer } from "../src/renderer";

describe("renderer base", () => {
  const md = buildRenderer();

  it("renders GFM tables", () => {
    const html = md.render("| a | b |\n|---|---|\n| 1 | 2 |\n");
    expect(html).toContain("<table>");
  });

  it("renders task list items with data-line", () => {
    const html = md.render("- [ ] todo\n- [ ] done\n", { });
    expect(html).toContain('class="task"');
    expect(html).toContain('data-line="0"');
    expect(html).toContain('data-line="1"');
  });

  it("adds data-line to headings", () => {
    const html = md.render("# T1\n\ntext\n\n## T2\n");
    expect(html).toContain('<h1 data-line="0">T1</h1>');
    expect(html).toContain('<h2 data-line="4">T2</h2>');
  });

  it("extracts frontmatter into env", () => {
    const env: any = {};
    md.render("---\ntitle: hello\n---\n\nbody\n", env);
    expect(env.frontmatter).toEqual({ title: "hello" });
  });
});
