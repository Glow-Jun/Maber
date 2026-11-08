let hits: HTMLElement[] = [];
let current = -1;

export function find(root: HTMLElement, query: string, direction: 1 | -1, wholeWord = false): number {
  if (!query) { clearFind(root); return 0; }
  // 文档重渲染后 hits 会指向已脱离文档的旧节点，必须重建，否则 Enter 完全没反应
  const stale = hits.length === 0 || hits[0].dataset.query !== query ||
    hits[0].dataset.whole !== String(wholeWord) || !root.contains(hits[0]);
  if (current === -1 || stale) buildHits(root, query, wholeWord);
  if (hits.length === 0) return 0;
  // 首次定位：正向落第一个命中，反向落最后一个（计划原式的 -1 起点会让反向跳到倒数第二个）
  if (current === -1) current = direction === 1 ? hits.length - 1 : 0;
  current = (current + direction + hits.length) % hits.length;
  hits.forEach((h, i) => h.classList.toggle("current", i === current));
  hits[current].scrollIntoView({ block: "center", behavior: "smooth" });
  return hits.length;
}

export function clearFind(root: HTMLElement) {
  root.querySelectorAll("mark.find-hit").forEach((m) => {
    const parent = m.parentNode!;
    parent.replaceChild(document.createTextNode(m.textContent ?? ""), m);
    parent.normalize();
  });
  hits = []; current = -1;
}

function isWordChar(ch: string | undefined): boolean {
  return !!ch && /[\p{L}\p{N}_]/u.test(ch);
}

function buildHits(root: HTMLElement, query: string, wholeWord: boolean) {
  clearFind(root);
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const targets: Text[] = [];
  let node: Node | null;
  while ((node = walker.nextNode())) {
    if ((node as Text).data.toLowerCase().includes(query.toLowerCase())) targets.push(node as Text);
  }
  const q = query.toLowerCase();
  for (const t of targets) {
    const text = t.data;
    const lower = text.toLowerCase();
    const frag = document.createDocumentFragment();
    let pos = 0;
    let idx: number;
    while ((idx = lower.indexOf(q, pos)) !== -1) {
      const end = idx + query.length;
      const boundaryOk = !wholeWord || (!isWordChar(text[idx - 1]) && !isWordChar(text[end]));
      if (!boundaryOk) {
        pos = idx + 1;
        continue;
      }
      if (idx > pos) frag.appendChild(document.createTextNode(text.slice(pos, idx)));
      const mark = document.createElement("mark");
      mark.className = "find-hit";
      mark.dataset.query = query;
      mark.dataset.whole = String(wholeWord);
      mark.textContent = text.slice(idx, end);
      frag.appendChild(mark);
      hits.push(mark);
      pos = end;
    }
    if (pos < text.length) frag.appendChild(document.createTextNode(text.slice(pos)));
    t.parentNode!.replaceChild(frag, t);
  }
}
