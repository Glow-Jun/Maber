// markdown-it-footnote 没有与当前 @types/markdown-it 版本兼容的声明文件，
// 这里只声明实际用到的插件签名，避免引入第二套 markdown-it 类型。
declare module "markdown-it-footnote" {
  import type MarkdownIt from "markdown-it";

  const footnote: (md: MarkdownIt) => void;
  export default footnote;
}
