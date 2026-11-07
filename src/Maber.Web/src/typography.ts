export interface TypographySettings {
  fontFamily: string;
  fontSize: number;
  lineHeight: number;
  contentWidth: number;
}

const FONT_STACKS: Record<string, string> = {
  default: `"Segoe UI", "Microsoft YaHei", system-ui, sans-serif`,
  yahei: `"Microsoft YaHei", "Segoe UI", system-ui, sans-serif`,
  simsun: `SimSun, "Songti SC", serif`,
  kaiti: `KaiTi, "Kaiti SC", serif`,
  mono: `"Cascadia Code", Consolas, "Microsoft YaHei", monospace`,
};

function clamp(value: number, min: number, max: number, fallback: number): number {
  const n = Number(value);
  return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : fallback;
}

/** 未知字体键回落 default；数值夹到与宿主一致的范围，坏设置不打乱排版 */
export function typographyVars(t: TypographySettings): Record<string, string> {
  return {
    "--font-family": FONT_STACKS[t.fontFamily] ?? FONT_STACKS.default,
    "--font-size": `${clamp(t.fontSize, 12, 24, 16)}px`,
    "--line-height": String(clamp(t.lineHeight, 1.4, 2.2, 1.75)),
    "--content-width": `${clamp(t.contentWidth, 30, 70, 46)}rem`,
  };
}

export function applyTypography(t: TypographySettings) {
  const root = document.documentElement;
  for (const [key, value] of Object.entries(typographyVars(t))) root.style.setProperty(key, value);
}
