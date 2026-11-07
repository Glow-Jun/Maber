import * as esbuild from "esbuild";
import { mkdirSync, cpSync, rmSync } from "node:fs";

const outDir = "../Maber.App/wwwroot";
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
rmSync(`${outDir}/vendor`, { recursive: true, force: true });
mkdirSync(`${outDir}/vendor`, { recursive: true });
cpSync("node_modules/katex/dist/katex.min.css", `${outDir}/vendor/katex.min.css`);
cpSync("node_modules/katex/dist/fonts", `${outDir}/vendor/fonts`, { recursive: true });
cpSync("index.html", `${outDir}/index.html`);
