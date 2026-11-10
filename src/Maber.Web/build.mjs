import * as esbuild from "esbuild";
import { mkdirSync, cpSync, rmSync } from "node:fs";

const outDir = "../Maber.App/wwwroot";
// 每次全量重建：esbuild 的 hash 文件名不会覆盖旧 chunk，不清空会无限累积
rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });

// 生产构建不产出 source map（16MB 级别，会明显增大安装包与仓库）；
// 需要调试时用 npm run build:debug 或 --watch。
const wantSourceMap = process.argv.includes("--watch") || process.argv.includes("--sourcemap");

await esbuild.build({
  entryPoints: ["src/main.ts"],
  bundle: true,
  splitting: true,
  format: "esm",
  outdir: outDir,
  sourcemap: wantSourceMap ? "linked" : false,
  minify: !process.argv.includes("--watch"),
  target: "es2022",
  logLevel: "info",
});

// KaTeX 的 CSS 与字体是公式渲染刚需，拷进 vendor
mkdirSync(`${outDir}/vendor`, { recursive: true });
cpSync("node_modules/katex/dist/katex.min.css", `${outDir}/vendor/katex.min.css`);
cpSync("node_modules/katex/dist/fonts", `${outDir}/vendor/fonts`, { recursive: true });
cpSync("index.html", `${outDir}/index.html`);
