import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
import { createRequire } from "node:module";

const source = fs.readFileSync(new URL("../src/quickSettings/index.tsx", import.meta.url), "utf8");
const ast = ts.createSourceFile("index.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const fn = ast.statements.find(n => ts.isFunctionDeclaration(n) && n.name?.text === "QuickDropdown");
const exports = {};
vm.runInNewContext(ts.transpileModule(fn.getText(ast) + "\nexport { QuickDropdown };", {
  compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.React },
}).outputText, { exports, DropdownItem: "DropdownItem", React: { createElement: (type, props, ...children) => ({ type, props, children }) } });

test("dropdown uses the Controller native DropdownItem pattern and preserves callback", () => {
  let changed;
  const field = exports.QuickDropdown({ label: "Audio", fullWidth: true, options: [{ data: "a", label: "A" }], value: "a", onChange: value => { changed = value; } });
  assert.equal(field.type, "div");
  assert.equal(field.children[0].props.layout, "below");
  assert.equal(field.children[0].props.childrenContainerWidth, "max");
  assert.equal(field.children[0].props.bottomSeparator, "none");
  assert.equal(field.children[0].props.padding, undefined);
  assert.equal(field.children[0].type, "DropdownItem");
  field.children[0].props.onChange({ data: "b" });
  assert.equal(changed, "b");
  assert.equal(exports.QuickDropdown({ label: "X", options: [], disabled: true }).children[0].props.disabled, true);
});

test("Audio and Video section names stay separate for twelve locales and Steam aliases", () => {
  const output = {};
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/coreLocale.ts", import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports: output, require: () => ({ controlLocale: () => ({ performance: "Performance" }) }) });
  for (const language of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "zh", "ja", "ko", "hi"]) {
    const copy = output.quickCoreLocale(language);
    assert.ok(copy.audio && copy.display);
    assert.notEqual(copy.audio, copy.display);
    assert.doesNotMatch(copy.audio, /video|vidéo|vídeo/i);
  }
  assert.equal(output.quickCoreLocale("italian").audio, "Audio");
  assert.equal(output.quickCoreLocale("italian").display, "Video");
  assert.equal(output.quickCoreLocale("koreana").audio, output.quickCoreLocale("ko").audio);
  assert.match(source, /\[local.audio\]: \["performance", "audio"\], \[local.display\]: \["audio", "display"\]/);
});

test("hash-only native Controller structure retains full width, native padding and label alignment", { skip: process.env.PLAYHUB_BROWSER_QA !== "1" }, async () => {
  const { chromium } = createRequire(import.meta.url)("playwright");
  const browser = await chromium.launch({ executablePath: process.env.PLAYHUB_CHROMIUM_PATH, headless: true });
  const css = source.match(/<style>\{`([\s\S]*?)`\}<\/style>/)[1];
  try {
    const page = await browser.newPage();
    for (const width of [264, 300, 360]) {
      const fields = ["Uscita audio", "Ingresso microfono", "Risoluzione", "Frequenza di aggiornamento", "Modalita energetica Windows"].map(label => {
        const children = '<div class="_c3"><div class="_c4"><button role="combobox">Dispositivo selezionato</button></div></div>';
        return `<div class="qsDropdownField qsDropdownFullWidth"><div class="_a1"><div class="_b2"><div class="_l5"><div id="label-${label}">${label}</div></div>${children}</div></div></div>`;
      }).join("");
      await page.setViewportSize({ width, height: 950 });
      await page.setContent(`<style>body{margin:0}._a1{display:flex;padding:10px 0;column-gap:12px}._b2{flex:1 1 auto}._c3{display:flex}._c4{flex:1 1 auto}._l5{height:26px}button{display:block;width:100%;height:40px;padding:10px 16px;border:0}${css}</style><div class="qsRedesign"><div class="qsCardBody">${fields}<div role="separator"></div></div></div>`);
      const results = await page.locator(".qsDropdownFullWidth>div").evaluateAll(fields => fields.map(field => {
        const label = field.querySelector('[id^="label-"]').getBoundingClientRect();
        const button = field.querySelector("button").getBoundingClientRect();
        const rect = field.getBoundingClientRect();
        return { below: button.top >= label.bottom, width: Math.abs(button.width - rect.width), left: Math.abs(button.left-label.left), padding: getComputedStyle(field).padding, border: getComputedStyle(field).borderBottomWidth };
      }));
      for (const result of results) {
        assert.equal(result.below, true, `${width}px`);
        assert.ok(result.width < 1);
        assert.equal(result.border, "0px");
        assert.ok(result.left < 1);
        assert.equal(result.padding, "10px 0px");
      }
      assert.equal(await page.locator('[role="separator"]').isVisible(), false);
    }
  } finally { await browser.close(); }
});
