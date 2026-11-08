export interface TypographySettings {
  fontFamily: string;
  customFontFamily?: string;
  fontSize: number;
  lineHeight: number;
  contentWidth: number;
  codeFontFamily?: string;
  codeFontSize?: number;
  fontFaces?: { family: string; url: string }[];
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

const MONO_STACK = `"Cascadia Code", Consolas, "Microsoft YaHei", monospace`;
const FALLBACK_STACK = `"Microsoft YaHei", "Segoe UI", system-ui, sans-serif`;

function clamp(value: number, min: number, max: number, fallback: number): number {
  const n = Number(value);
  return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : fallback;
}

function quote(family: string): string {
  return `"${family.replace(/\\/g, "\\\\").replace(/"/g, '\\"')}"`;
}

function bodyStack(t: TypographySettings): string {
  if (t.fontFamily === "custom") {
    const family = (t.customFontFamily ?? "").trim();
    return family ? `${quote(family)}, ${FALLBACK_STACK}` : FONT_STACKS.default;
  }
  return FONT_STACKS[t.fontFamily] ?? FONT_STACKS.default;
}

function codeStack(t: TypographySettings, body: string): string {
  const key = (t.codeFontFamily ?? "mono").trim();
  if (key === "" || key === "mono") return MONO_STACK;
  if (key === "default") return body;
  return `${quote(key)}, "Cascadia Code", Consolas, monospace`;
}

/** 未知字体键回落 default；数值夹到与宿主一致的范围，坏设置不打乱排版 */
export function typographyVars(t: TypographySettings): Record<string, string> {
  const body = bodyStack(t);
  return {
    "--font-family": body,
    "--font-size": `${clamp(t.fontSize, 12, 24, 16)}px`,
    "--line-height": String(clamp(t.lineHeight, 1.4, 2.2, 1.75)),
    "--content-width": `${clamp(t.contentWidth, 30, 70, 46)}rem`,
    "--code-font-family": codeStack(t, body),
    "--code-font-size": `${clamp(t.codeFontSize ?? 13, 10, 20, 13)}px`,
  };
}

/** 导入字体以 @font-face 注入；同名字体文件优先于系统字体 */
export function applyFontFaces(faces: TypographySettings["fontFaces"]) {
  let style = document.getElementById("maber-font-faces") as HTMLStyleElement | null;
  if (!style) {
    style = document.createElement("style");
    style.id = "maber-font-faces";
    document.head.appendChild(style);
  }
  style.textContent = (faces ?? [])
    .filter((f) => f.family && f.url)
    .map((f) => `@font-face{font-family:"${f.family}";src:url("${f.url}");font-display:swap;}`)
    .join("\n");
}

export function applyTypography(t: TypographySettings) {
  applyFontFaces(t.fontFaces);
  const root = document.documentElement;
  for (const [key, value] of Object.entries(typographyVars(t))) root.style.setProperty(key, value);
}
