import assert from 'node:assert/strict';
import { readFileSync, mkdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const source = readFileSync(new URL('../src/ControlCenter.tsx', import.meta.url), 'utf8');
const ids = ['home', 'audio', 'performance', 'graphics', 'controller', 'store', 'decky'];
function fixture(source, visible, active, width) {
  const ast = ts.createSourceFile('ControlCenter.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  let nav;
  const visit = node => {
    if (ts.isJsxElement(node) && node.openingElement.attributes.properties.some(prop =>
      prop.name?.text === 'className' && prop.initializer?.text === 'ph-control-nav')) nav = node;
    ts.forEachChild(node, visit);
  };
  visit(ast);
  assert.ok(nav);
  const exports = {};
  const React = { createElement: (type, props, ...children) => ({ type, props: props ?? {}, children: children.flat() }) };
  vm.runInNewContext(ts.transpileModule(`export const nav = (${nav.getText(ast)});`, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
  }).outputText, { exports, React, Focusable: 'div', DialogButton: 'button', TbSettings: 'svg',
    icons: Object.fromEntries(ids.map(id => [id, 'svg'])), copy: Object.fromEntries([...ids, 'settings'].map(id => [id, id])),
    visible, active, ready: true, origin: 'qam' });
  const escape = text => String(text).replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
  const html = node => {
    if (!node || typeof node === 'boolean') return '';
    if (typeof node !== 'object') return escape(node);
    const attrs = Object.entries(node.props).filter(([key, value]) => key !== 'key' && !key.startsWith('on') && value !== undefined
      && (value !== false || key.startsWith('aria-') || key.startsWith('data-')))
      .map(([key, value]) => `${key === 'className' ? 'class' : key}="${escape(value)}"`).join(' ');
    return `<${node.type} ${attrs}>${node.children.map(html).join('')}</${node.type}>`;
  };
  const css = source.split('const css = `')[1].split('`;')[0];
  return `<style>body{margin:0;background:#171a21;color:white}button:hover{transform:scale(1.1);padding:8px}button:focus{border-width:4px}${css}</style><div class="ph-controls" style="width:${width}px">${html(exports.nav)}</div>`;
}

test('Settings shares the actual native tab grid; editor remains independently scoped', () => {
  const markup = fixture(source, ids, 'home', 300);
  assert.match(markup, /grid-template-columns:minmax\(0,1fr\);align-items:center/);
  assert.doesNotMatch(markup, /grid-template-columns:minmax\(0,1fr\) 32px/);
  assert.ok(markup.indexOf('aria-label="settings"') < markup.lastIndexOf('</button></div></div>'));
  assert.match(source, /\.ph-controls \.ph-tab-editor\{display:grid;grid-auto-flow:column/);
});

test('native-width tab tracks stay equal through hiding, selection and hover', {
  skip: !process.env.PLAYHUB_BROWSER_QA && 'Set PLAYHUB_BROWSER_QA=1 for isolated Chromium geometry',
}, async () => {
  const { chromium } = createRequire(import.meta.url)('playwright');
  const browser = await chromium.launch({ headless: true,
    ...(process.env.PLAYHUB_CHROMIUM_PATH ? { executablePath: process.env.PLAYHUB_CHROMIUM_PATH } : {}) });
  const output = process.env.PLAYHUB_QA_OUTPUT ?? join(tmpdir(), 'playhub-tabrow-qa');
  mkdirSync(output, { recursive: true });
  try {
    const page = await browser.newPage({ viewport: { width: 855, height: 682 } });
    const measure = () => page.locator('.ph-control-nav .ph-control-icon').evaluateAll(nodes => nodes.map((node, index) => {
      const rect = node.getBoundingClientRect(), icon = node.querySelector('svg').getBoundingClientRect();
      return { x: rect.x, width: rect.width, height: rect.height, gap: index ? rect.left - nodes[index - 1].getBoundingClientRect().right : null,
        iconX: icon.x, iconY: icon.y, transform: getComputedStyle(node).transform };
    }));
    if (process.env.PLAYHUB_NAV_BASELINE) {
      await page.setContent(fixture(readFileSync(process.env.PLAYHUB_NAV_BASELINE, 'utf8'), ids, 'decky', 300));
      console.log('BEFORE', JSON.stringify(await measure()));
      await page.screenshot({ path: join(output, 'before-300.png') });
    }
    for (const width of [264, 300, 360]) for (const visible of [ids, ids.filter(id => !['audio', 'graphics'].includes(id)), ['home']]) {
      let baseline;
      for (const active of visible) {
        await page.setContent(fixture(source, visible, active, width));
        const initial = await measure();
        assert.equal(initial.length, visible.length + 1);
        for (const rect of initial) {
          assert.ok(Math.abs(rect.width - initial[0].width) < 0.02, JSON.stringify(initial));
          assert.equal(rect.height, 34);
          if (rect.gap !== null) assert.ok(Math.abs(rect.gap - 3) < 0.02, JSON.stringify(initial));
          assert.equal(rect.transform, 'none');
        }
        if (baseline) assert.deepEqual(initial, baseline, 'Selection moved the tracks or icons');
        baseline = initial;
        await page.locator('.ph-control-nav .ph-control-icon').last().hover();
        await page.locator('.ph-control-nav .ph-control-icon').last().focus();
        assert.equal(await page.locator('.ph-control-nav .ph-control-icon').last().evaluate(node => document.activeElement === node), true);
        assert.deepEqual(await measure(), initial, 'Hover/focus moved the tracks or icons');
      }
      console.log('AFTER', JSON.stringify({ width, count: baseline.length, buttons: baseline }));
      if (width === 300 && visible.length === 7) await page.screenshot({ path: join(output, 'after-300.png') });
    }
  } finally { await browser.close(); }
});
