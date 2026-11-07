import { EditorView, keymap, Decoration } from "@codemirror/view";
import { EditorSelection, RangeSetBuilder, StateEffect, StateField } from "@codemirror/state";
import type { DecorationSet } from "@codemirror/view";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { languages } from "@codemirror/language-data";
import { defaultHighlightStyle, syntaxHighlighting } from "@codemirror/language";

// ---- 页内查找（渲染态 mark.find-hit 的源码态对应物）----
const setFindQuery = StateEffect.define<string>();
const findMark = Decoration.mark({ class: "cm-find-hit" });

function matchPositions(doc: string, query: string): number[] {
  const lower = doc.toLowerCase(), q = query.toLowerCase();
  const out: number[] = [];
  let i = 0;
  while ((i = lower.indexOf(q, i)) !== -1) { out.push(i); i += q.length; }
  return out;
}

const findField = StateField.define<{ query: string; deco: DecorationSet }>({
  create: () => ({ query: "", deco: Decoration.none }),
  update(value, tr) {
    let query = value.query;
    for (const e of tr.effects) if (e.is(setFindQuery)) query = e.value;
    if (!query) return { query, deco: Decoration.none };
    if (tr.docChanged || query !== value.query) {
      const b = new RangeSetBuilder<Decoration>();
      for (const pos of matchPositions(tr.state.doc.toString(), query)) {
        b.add(pos, pos + query.length, findMark);
      }
      return { query, deco: b.finish() };
    }
    return { query, deco: value.deco };
  },
  provide: (f) => EditorView.decorations.from(f, (v) => v.deco),
});

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
        findField,
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

  /** Enter/Shift+Enter 循环跳转：全部命中打高亮，当前项加描边并滚动到视野中央 */
  find(query: string, direction: 1 | -1) {
    const view = this.view;
    if (!view || !query) return;
    view.dispatch({ effects: setFindQuery.of(query) });
    const matches = matchPositions(view.state.doc.toString(), query);
    if (matches.length === 0) return;

    const pos = direction === 1 ? view.state.selection.main.head : view.state.selection.main.from;
    let target: number;
    if (direction === 1) target = matches.find((m) => m > pos) ?? matches[0];
    else {
      const before = matches.filter((m) => m < pos);
      target = before.length ? before[before.length - 1] : matches[matches.length - 1];
    }
    const sel = EditorSelection.single(target + query.length, target);
    view.dispatch({
      selection: sel,
      effects: EditorView.scrollIntoView(sel.main, { y: "center" }),
    });
    view.focus();
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
