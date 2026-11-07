import * as esbuild from "esbuild";
import { mkdirSync, cpSync, rmSync } from "node:fs";

const outDir = "../Maber.App/wwwroot";
// 每次全量重建：esbuild 的 hash 文件名不会覆盖旧 chunk，不清空会无限累积
rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });

await esbuild.build({
  entryPoints: ["src/main.ts"],
  bundle: true,
  splitting: true,
  format: "esm",
  outdir: outDir,
  sourcemap: process.argv.includes("--watch") ? false : "linked",
  minify: !process.argv.includes("--watch"),
  target: "es2022",
  logLevel: "info",
});

// KaTeX 的 CSS 与字体是公式渲染刚需，拷进 vendor
mkdirSync(`${outDir}/vendor`, { recursive: true });
cpSync("node_modules/katex/dist/katex.min.css", `${outDir}/vendor/katex.min.css`);
cpSync("node_modules/katex/dist/fonts", `${outDir}/vendor/fonts`, { recursive: true });
cpSync("index.html", `${outDir}/index.html`);
