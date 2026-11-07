let hits: HTMLElement[] = [];
let current = -1;

export function find(root: HTMLElement, query: string, direction: 1 | -1): number {
  if (!query) return -1;
  if (current === -1 || hits[0]?.dataset.query !== query) buildHits(root, query);
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

function buildHits(root: HTMLElement, query: string) {
  clearFind(root);
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const targets: Text[] = [];
  let node: Node | null;
  while ((node = walker.nextNode())) {
    if ((node as Text).data.toLowerCase().includes(query.toLowerCase())) targets.push(node as Text);
  }
  for (const t of targets) {
    const frag = document.createDocumentFragment();
    let rest = t.data;
    let idx: number;
    while ((idx = rest.toLowerCase().indexOf(query.toLowerCase())) !== -1) {
      if (idx > 0) frag.appendChild(document.createTextNode(rest.slice(0, idx)));
      const mark = document.createElement("mark");
      mark.className = "find-hit";
      mark.dataset.query = query;
      mark.textContent = rest.slice(idx, idx + query.length);
      frag.appendChild(mark);
      hits.push(mark);
      rest = rest.slice(idx + query.length);
    }
    if (rest) frag.appendChild(document.createTextNode(rest));
    t.parentNode!.replaceChild(frag, t);
  }
}
