import { EditorView, keymap } from "@codemirror/view";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { languages } from "@codemirror/language-data";
import { defaultHighlightStyle, syntaxHighlighting } from "@codemirror/language";

export class SourceEditor {
  private view: EditorView | null = null;
  private host: HTMLElement;
  private onChange: (text: string) => void;

  constructor(host: HTMLElement, onChange: (text: string) => void) {
    this.host = host;
    this.onChange = onChange;
  }

  show(text: string) {
    if (this.view) { this.view.focus(); return; }
    this.view = new EditorView({
      parent: this.host,
      doc: text,
      extensions: [
        history(),
        keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
        markdown({ base: markdownLanguage, codeLanguages: languages }),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        EditorView.lineWrapping,
        EditorView.updateListener.of((u) => {
          if (u.docChanged) this.onChange(u.state.doc.toString());
        }),
      ],
    });
    this.host.classList.add("active");
  }

  hide(): string {
    if (!this.view) return "";
    const text = this.view.state.doc.toString();
    this.view.destroy();
    this.view = null;
    this.host.classList.remove("active");
    return text;
  }

  get visible() { return this.view !== null; }
}
