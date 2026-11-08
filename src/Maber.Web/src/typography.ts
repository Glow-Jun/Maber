export interface TypographySettings {
  fontFamily: string;
  fontSize: number;
  lineHeight: number;
  contentWidth: number;
}

const FONT_STACKS: Record<string, string> = {
  default: `"Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei", system-ui, sans-serif`,
  yahei: `"Microsoft YaHei", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif`,
  wenkai: `"霞鹜文楷 GB 屏幕阅读版 R", "LXGW WenKai GB Screen R", "LXGW WenKai", "KaiTi", serif`,
  smiley: `"得意黑", "Smiley Sans Oblique", "Microsoft YaHei", "Segoe UI", sans-serif`,
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
