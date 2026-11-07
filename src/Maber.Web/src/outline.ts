import type { OutlineItem } from "./types";

export function extractOutline(root: HTMLElement): OutlineItem[] {
  return [...root.querySelectorAll("h1,h2,h3,h4,h5,h6[data-line]")].map((h) => ({
    level: Number(h.tagName.slice(1)),
    text: h.textContent?.trim() ?? "",
    line: Number(h.getAttribute("data-line")),
  }));
}
