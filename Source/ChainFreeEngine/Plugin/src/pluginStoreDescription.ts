/** Convert untrusted HTML/mixed Markdown to the existing MarkdownBlocks subset.
 * Output is Markdown TEXT, never sanitized HTML for dangerouslySetInnerHTML.
 * A detached template keeps scripts, custom elements and media inert. */
export function descriptionToMarkdown(description: string, baseUrl?: string): string {
  if (!description) return "";
  const template = document.createElement("template");
  template.innerHTML = description;
  const blocked = new Set([
    "SCRIPT", "STYLE", "IFRAME", "OBJECT", "EMBED", "SVG", "MATH", "TEMPLATE",
    "FORM", "INPUT", "BUTTON", "SELECT", "TEXTAREA", "NOSCRIPT", "HEAD",
    "IMG", "VIDEO", "AUDIO", "SOURCE", "LINK", "META", "BASE",
  ]);
  const compact = (text: string) => text.replace(/\s+/g, " ").trim();
  const children = (node: Node): string => Array.from(node.childNodes, render).join("");
  const block = (text: string) => `\n\n${text.trim()}\n\n`;
  function render(node: Node): string {
    if (node.nodeType === 3) return node.textContent ?? "";
    if (node.nodeType !== 1) return "";
    const element = node as HTMLElement;
    const tag = element.tagName.toUpperCase();
    if (blocked.has(tag) || element.hidden || element.getAttribute("aria-hidden") === "true") return "";
    if (tag === "BR") return "\n";
    if (tag === "HR") return "\n\n";
    if (tag === "TABLE") {
      // MarkdownBlocks has no table support: retain cells as labeled list rows.
      const rows = Array.from(element.querySelectorAll("tr"))
        .filter((row) => row.closest("table") === element);
      let headers: string[] = [];
      return block(rows.map((row, index) => {
        const cells = Array.from(row.children).filter((cell) => /^(TD|TH)$/.test(cell.tagName));
        const values = cells.map((cell) => compact(children(cell)));
        if (index === 0 && cells.length && cells.every((cell) => cell.tagName === "TH")) {
          headers = values;
          return "";
        }
        return "- " + values.map((value, i) => headers[i] ? `**${headers[i]}:** ${value}` : value).join("; ");
      }).filter(Boolean).join("\n") || headers.join("; "));
    }
    const text = children(element);
    if (/^H[1-6]$/.test(tag)) return block(`${"#".repeat(Math.min(4, Number(tag[1])))} ${compact(text)}`);
    if (tag === "STRONG" || tag === "B") return text.trim() ? `**${compact(text)}**` : "";
    if (tag === "CODE") return `\`${compact(text).replace(/`/g, "'")}\``;
    if (tag === "BLOCKQUOTE") return block(text.trim().split(/\n+/).map((line) => `> ${line.trim()}`).join("\n"));
    if (tag === "UL" || tag === "OL") return block(Array.from(element.children, render).map((item) => item.trim()).join("\n"));
    if (tag === "LI") {
      const siblings = Array.from(element.parentElement?.children ?? []).filter((child) => child.tagName === "LI");
      const prefix = element.parentElement?.tagName === "OL" ? `${siblings.indexOf(element) + 1}.` : "-";
      return `\n${prefix} ${text.trim()}\n`;
    }
    if (tag === "A") {
      const label = compact(text).replace(/[\[\]]/g, "");
      const href = element.getAttribute("href")?.trim();
      if (!href || !label) return label;
      try {
        const url = new URL(href, baseUrl);
        if (url.protocol !== "https:" || url.username || url.password) return label;
        const target = url.href.replace(/[()]/g, (character) => character === "(" ? "%28" : "%29");
        return `[${label}](${target})`;
      } catch { return label; }
    }
    if (/^(P|DIV|SECTION|ARTICLE|HEADER|FOOTER|MAIN|ASIDE|PRE|DL|DT|DD|FIGURE|FIGCAPTION|DETAILS|SUMMARY)$/.test(tag)) return block(text);
    return text;
  }
  return children(template.content)
    .replace(/\[\s*\]\((?:[^()\n]|\([^()\n]*\))*\)/g, "")
    .replace(/\[\s*\]\[[^\]\n]*\]/g, "")
    .replace(/\r/g, "")
    .replace(/\u00a0/g, " ")
    .replace(/^[ \t]*\u2022[ \t]+/gm, "- ")
    .replace(/\n[ \t]+/g, "\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}
