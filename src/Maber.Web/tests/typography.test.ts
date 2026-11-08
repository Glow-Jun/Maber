import { describe, expect, it } from "vitest";
import { typographyVars, type TypographySettings } from "../src/typography";

const base: TypographySettings = {
  fontFamily: "default",
  fontSize: 16,
  lineHeight: 1.75,
  contentWidth: 46,
};

describe("typography", () => {
  it("default keys map to the built-in stack and units", () => {
    const vars = typographyVars(base);
    expect(vars["--font-family"]).toContain("Segoe UI");
    expect(vars["--font-size"]).toBe("16px");
    expect(vars["--line-height"]).toBe("1.75");
    expect(vars["--content-width"]).toBe("46rem");
  });

  it("unknown font key falls back to default", () => {
    expect(typographyVars({ ...base, fontFamily: "comic" })["--font-family"]).toContain("Segoe UI");
  });

  it("open-source Chinese font presets map to their stacks", () => {
    expect(typographyVars({ ...base, fontFamily: "wenkai" })["--font-family"]).toContain("霞鹜文楷");
    expect(typographyVars({ ...base, fontFamily: "smiley" })["--font-family"]).toContain("得意黑");
  });

  it("out-of-range values are clamped", () => {
    const vars = typographyVars({ ...base, fontSize: 100, lineHeight: 0.2, contentWidth: 10 });
    expect(vars["--font-size"]).toBe("24px");
    expect(vars["--line-height"]).toBe("1.4");
    expect(vars["--content-width"]).toBe("30rem");
  });

  it("non-numeric values fall back to defaults", () => {
    const vars = typographyVars({
      ...base,
      fontSize: Number.NaN,
      lineHeight: Number.NaN,
      contentWidth: Number.NaN,
    });
    expect(vars["--font-size"]).toBe("16px");
    expect(vars["--line-height"]).toBe("1.75");
    expect(vars["--content-width"]).toBe("46rem");
  });
});
