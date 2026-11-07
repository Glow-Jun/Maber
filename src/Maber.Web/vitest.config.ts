import { defineConfig } from "vitest/config";

export default defineConfig({
  // 阻止 vite 在 resolveId 时把 subst/junction 路径 realpath 回含 "#" 的物理路径
  resolve: {
    preserveSymlinks: true,
  },
  test: {
    // 本仓库物理路径含 "#"（D:\# GuangJ Base Mirr\...）：vite 的 load 阶段无法
    // 服务任何含 "#" 的路径（"#" 被当作 URL fragment）。本机开发约定：
    //   1. node_modules 实体位于 C:\maber-root\node_modules（无 "#"），仓库内为同名
    //      junction；vitest/vite 等包文件 realpath 后落在干净路径，node 包解析亦正常。
    //   2. 测试经 subst 盘符运行（subst M: <仓库根>），源文件 id 保持无 "#" 拼写。
    // junction 目标在 vite server 根之外，故放宽 fs.strict（server 仅本地 middleware 模式）。
    server: {
      fs: {
        strict: false,
      },
    },
  },
});
